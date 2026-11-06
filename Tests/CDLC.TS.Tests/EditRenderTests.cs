using System;
using System.Collections.Generic;
using System.Reflection;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.58 (the owner: "优化立杆编辑时性能和实时渲染，修复部分情况下的材质关系错误问题"): what the renderer takes for a change of an item, and the live picture of a support while it is edited.
    /// 0.12.55 and 0.12.57 added the lean and the style to the record and not to TileDiff.Same, so that a change of the style alone was no change: the store kept the old record and the tile stayed as
    /// it was until something else moved the item, which is when the finish, the black backs and the root paint of the owner's edits showed late or not as set.
    /// </summary>
    internal static class EditRenderTests
    {
        public static void RunAll()
        {
            Program.Run("edit render: every drawn field of a record is a change to the tile diff", EveryField);
            Program.Run("edit render: a change of the style or the lean alone reaches the store", StoreKeepsStyle);
            Program.Run("edit render: the live picture's key", LiveKey);
            Program.Run("edit render: a board's layout is found without being made (0.12.64)", CachedInstance);
            Program.Run("supports: the families of the panel (0.12.58)", Families);
            Program.Run("supports: which have posts to paint at the root", RootPaintable);
        }

        private static ItemRecord Base() => new ItemRecord(SignCatalog.ById("gb.mand.01").Hash, new Vec3(10f, 2f, 30f), 0.5f, SupportCatalog.ById(SupportCatalog.SinglePole).Hash, "speed=60", 0f, 7u, 1);

        /// <summary>The fields that are not drawn: the automatic marking's flag only says what marking the stretch again replaces.</summary>
        private static readonly HashSet<string> NotDrawn = new HashSet<string> { "Auto" };

        /// <summary>The fields whose change is a reference to something worked out (the fit, the laid marking): checked where they are made (AreaFitTests, MarkingGroundTests).</summary>
        private static readonly HashSet<string> Referenced = new HashSet<string> { "Fit", "Laid" };

        private static object Other(FieldInfo field, object now)
        {
            Type t = field.FieldType;
            if (t == typeof(uint)) return (uint)now + 1u;
            if (t == typeof(int)) return (int)now + 1;
            if (t == typeof(float)) return (float)now + 0.25f;
            if (t == typeof(bool)) return !(bool)now;
            if (t == typeof(byte)) return (byte)((byte)now + 1);
            if (t == typeof(string)) return ((string)now ?? "") + "x";
            if (t == typeof(Vec3)) return new Vec3(1f, 2f, 3f);
            if (t == typeof(GroundProfile)) return GroundProfile.Sample((x, z) => 0f, -2f, 2f);
            return null;
        }

        private static void EveryField()
        {
            foreach (FieldInfo field in typeof(ItemRecord).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (NotDrawn.Contains(field.Name) || Referenced.Contains(field.Name))
                    continue;
                object boxed = Base();
                object other = Other(field, field.GetValue(boxed));
                Program.Check(other != null, $"ItemRecord.{field.Name} ({field.FieldType.Name}) is a kind of field this test knows: add it here, and to TileDiff.Same if it is drawn");
                if (other == null)
                    continue;
                ItemRecord a = Base();
                field.SetValue(boxed, other);
                var b = (ItemRecord)boxed;
                Program.Check(!TileDiff.Same(a, b), $"a change of ItemRecord.{field.Name} alone is a change to the tile diff (the renderer rebuilds the tile)");
            }

            ItemRecord same = Base();
            Program.Check(TileDiff.Same(Base(), same), "an item is the same as itself");
            ItemRecord auto = Base();
            auto.Auto = true;
            Program.Check(TileDiff.Same(Base(), auto), "the automatic flag draws nothing: not a change");
        }

        private static void StoreKeepsStyle()
        {
            var store = new ItemStore<int>();
            ItemRecord first = Base();
            store.Upsert(1, first);
            store.TakeDirty(new HashSet<TileKey>());
            int changes = store.Changes;
            ItemRecord painted = first;
            painted.Style = new SupportStyle { Finish = SupportFinish.Graphite, BlackBack = true, Root = RootPaint.BlueRings }.Packed;
            bool changed = store.Upsert(1, painted);
            var dirty = new HashSet<TileKey>();
            store.TakeDirty(dirty);
            Program.Check(changed && store.Changes == changes + 1 && dirty.Contains(Tiles.Of(first.Position.X, first.Position.Z)), "a new style: the store takes it and its tile is dirty");
            Program.Check(store.TryGetValue(1, out ItemRecord kept) && kept.Style == painted.Style, "the record the store keeps is the painted one, not the one before");
            ItemRecord leaning = painted;
            leaning.Tilt = BoardTilt.Radians(2);
            bool leaned = store.Upsert(1, leaning);
            Program.Check(leaned && store.TryGetValue(1, out kept) && kept.Tilt == leaning.Tilt, "a new lean: the same");
            Program.Check(!store.Upsert(1, leaning), "the same again: nothing");
        }

        private static void Families()
        {
            var seen = new Dictionary<string, int>();
            foreach (SupportDef def in SupportCatalog.Offerable)
            {
                string family = def.Family;
                Program.Check(Array.Exists(SupportFamilies.All, f => f.Id == family), $"{def.Id} is in a family of the panel ({family})");
                seen[family] = (seen.TryGetValue(family, out int n) ? n : 0) + 1;
            }

            Program.Check(seen.Count == SupportFamilies.All.Length, $"every family has a support listed: {string.Join(", ", seen)}");
            Program.Check(seen[SupportFamilies.Post] == 3 && seen[SupportFamilies.Cantilever] == 8 && seen[SupportFamilies.Gantry] == 3 && seen[SupportFamilies.Attached] == 7 && seen[SupportFamilies.Plate] == 4,
                "3 posts, 8 cantilevers (round, cranked, F, double, octagonal, octagonal double, framed, Shanghai multi-function pole), 3 gantries (the portal, the space truss and the height-limit frame, 0.12.78), 7 attached (lamp post, wall flat, roof, wall bracket, signal arm, over the road 0.12.80, over the road hung 0.12.81), 4 street-name posts");
            foreach (SupportDef def in SupportCatalog.All)
            {
                if (def.Hidden)
                    Program.Check(def.Family == SupportCatalog.ListedOf(def).Family, $"{def.Id}, a mirror, is in the family of the one it mirrors");
            }

            Program.Check(SupportCatalog.ById(SupportCatalog.PlateHangzhou).Family == SupportFamilies.Plate && SupportCatalog.ById(SupportCatalog.WallBracket).Family == SupportFamilies.Attached
                && SupportCatalog.ById(SupportCatalog.AttachedArm).Family == SupportFamilies.Attached && SupportCatalog.ById(SupportCatalog.OverRoad).Family == SupportFamilies.Attached && SupportCatalog.ById(SupportCatalog.Integrated).Family == SupportFamilies.Cantilever
                && SupportCatalog.ById(SupportCatalog.Gantry).Family == SupportFamilies.Gantry && SupportFamilies.Of(null) == SupportFamilies.Post, "the plate posts are no posts, the Shanghai pole a cantilever, walls and signal arms attached");
        }

        private static void RootPaintable()
        {
            SignDef plate = SignCatalog.ById("gb17733.street");
            foreach (SupportDef def in SupportCatalog.Offerable)
            {
                SupportLayout layout = SupportLayouts.Of(plate, def, 0f);
                bool can = SupportKinds.RootPaintable(def.Kind, layout);
                bool hasPost = !SupportKinds.Hosted(def.Kind) && !SupportKinds.OnWall(def.Kind) && !SupportKinds.OverRoad(def.Kind) && !def.HeightLimit;
                Program.Check(can == hasPost, $"{def.Id}: root paint {(can ? "offered" : "not offered")} ({(hasPost ? "has posts" : "no post of its own")})");
                if (!can)
                    continue;
                // What is offered is drawn: the paint is on the support's meshes.
                var record = new ItemRecord(plate.Hash, new Vec3(0f, 0f, 0f), 0f, def.Hash, null, 6f);
                record.Style = new SupportStyle { Root = RootPaint.BlueRings }.Packed;
                SignParts parts = ChunkBuilder.PartsOfGroup(new[] { record });
                Program.Check(parts.Solids[(int)SolidColour.Blue] != null && parts.Solids[(int)SolidColour.Blue].VertexCount > 0, $"{def.Id}: and the rings are there");
            }

            SignDef wide = SignCatalog.ById("gb.exit.adv.2km") ?? SignCatalog.ById("guide.exit.advance");
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            Program.Check(SupportKinds.RootPaintable(SupportKind.Gantry, SupportLayouts.Of(wide, gantry, 12f)) && !SupportKinds.RootPaintable(SupportKind.Gantry, SupportLayouts.Of(wide, gantry, 20f)),
                "a gantry on round posts (a span to 16 m) is painted, one on a space truss's H legs (beyond it) is not");
        }

        /// <summary>The editor asks for a board it must not wait for: found if a worker made it, else nothing (and the main thread does not lay it out).</summary>
        private static void CachedInstance()
        {
            SignChoice choice = null;
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (!c.Def.IsMarking && c.Def.Template != null && c.Def.Template.Fields.Length > 0 && c.Def.Template.Fields[0].Kind == GuideFieldKind.Text)
                {
                    choice = c;
                    break;
                }
            }

            Program.Check(choice != null, "a board with a text setting is in the catalogue");
            if (choice == null)
                return;
            string spec = choice.Def.Template.Parse(choice.Spec ?? "").With(choice.Def.Template.Fields[0].Key, "只此一份" + Guid.NewGuid().ToString("N")).ToString();
            Program.Check(SignInstances.Cached(choice.Def, spec) == null, "a board never laid out is not found, and is not made by asking");
            SignInstance made = SignInstances.Of(choice.Def, spec);
            Program.Check(ReferenceEquals(SignInstances.Cached(choice.Def, spec), made), "once laid out (on a worker) it is found, the same object");
            Program.Check(SignInstances.Cached(choice.Def, spec + ";") == null, "another text is another board");
            SignDef classic = null;
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.Def.Template == null)
                {
                    classic = c.Def;
                    break;
                }
            }

            Program.Check(classic == null || SignInstances.Cached(classic, "") != null, "a classic sign, where the catalogue has one, has its instance at once");
        }

        private static List<ItemRecord> Group()
        {
            ItemRecord lead = Base(), next = new ItemRecord(SignCatalog.ById("gb.prohib.01").Hash, new Vec3(10f, 2f, 30f), 0.5f, lead.SupportHash, null, 0f, 7u, 2);
            return new List<ItemRecord> { lead, next };
        }

        private static void LiveKey()
        {
            List<ItemRecord> records = Group();
            GroundProfile ground = GroundProfile.Sample((x, z) => 0f, -2f, 2f);
            string key = LiveEdit.Key(records, ground);
            Program.Check(key == LiveEdit.Key(Group(), ground), "the same support, the same key");
            // Every field that changes what is drawn, but where it stands (the picture is moved, not made again), changes the key.
            foreach (FieldInfo field in typeof(ItemRecord).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                string name = field.Name;
                if (name == "Position" || name == "Yaw" || NotDrawn.Contains(name) || Referenced.Contains(name) || name == "Hidden" || name == "Ground")
                    continue;
                List<ItemRecord> changed = Group();
                object boxed = changed[1];
                object other = Other(field, field.GetValue(boxed));
                if (other == null)
                    continue;
                field.SetValue(boxed, other);
                changed[1] = (ItemRecord)boxed;
                Program.Check(LiveEdit.Key(changed, ground) != key, $"ItemRecord.{name} of a board is in the live picture's key");
                boxed = records[0];
                changed = Group();
                field.SetValue(boxed, Other(field, field.GetValue(boxed)));
                changed[0] = (ItemRecord)boxed;
                Program.Check(LiveEdit.Key(changed, ground) != key, $"ItemRecord.{name} of the lead is in it");
            }

            List<ItemRecord> moved = Group();
            ItemRecord m = moved[0];
            m.Position = new Vec3(500f, 9f, -70f);
            m.Yaw = 2f;
            moved[0] = m;
            Program.Check(LiveEdit.Key(moved, ground) == key, "where it stands is not in it: the picture is moved");
            Program.Check(LiveEdit.Key(records, GroundProfile.Sample((x, z) => 0f, -2f, 2f)) != key && LiveEdit.Key(records, null) != key, "another ground is another picture");
        }
    }
}
