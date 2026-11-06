using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public enum ShapeKind
    {
        Circle,
        Octagon,
        Rect,

        /// <summary>A rectangle with rounded corners (guide boards: GB 5768.2 Fig. 3); the radius is SignDef.CornerRadius.</summary>
        RoundRect,

        /// <summary>An equilateral triangle, apex up (warning signs) or down (yield); Width is the side, the corners are rounded by SignDef.CornerRadius.</summary>
        Triangle,
        TriangleDown
    }

    /// <summary>
    /// One kind of sign: its outline and size and how it is found in the panel. The support it stands on is a separate choice (SupportDef); the sign only
    /// names its default. The numbers are provisional test values; the standard's own tables are to be checked against its text before they are called exact.
    /// </summary>
    public sealed class SignDef
    {
        public string Id;

        /// <summary>The number in GB 5768.2, or a word when the sign is only a test.</summary>
        public string Code;

        public string Zh;
        public string En;
        public string[] Keywords;

        /// <summary>Pinyin initials of the Chinese name, for the search.</summary>
        public string Pinyin;

        public ShapeKind Shape;

        /// <summary>The width and height of the board (m); for a circle both are the diameter, for an octagon the distance between parallel sides.</summary>
        public float Width, Height;

        /// <summary>The height of the lower edge of the board above the ground (m) on a roadside post.</summary>
        public float BottomClearance;

        /// <summary>The kind of sign, for the filter of the panel: prohibition (禁令), warning (警告), mandatory (指示), guide (指路).</summary>
        public string Group;

        /// <summary>The picture of the face: its size in pixels.</summary>
        public int TextureWidth, TextureHeight;

        /// <summary>A guide board: the template that lays it out from its settings (the size above is then only the default's; each instance has its own). Null for a classic sign.</summary>
        public GuideTemplate Template;

        /// <summary>A road marking (GB 5768.3), not a board: it is painted on the road and has no support.</summary>
        public bool IsMarking => Template != null && Template.IsMarking;

        /// <summary>RoundRect: the radius of the corners (m). Triangle: the radius of the rounding of its corners.</summary>
        public float CornerRadius;

        /// <summary>Triangle: the nominal side before the corners are rounded (the standard's A); Width and Height are those of the rounded outline.</summary>
        public float Side;

        /// <summary>How far plates fixed to the board reach above its top edge (m); the posts and the arm clear them.</summary>
        public float ExtraTop;

        /// <summary>The area (m2) the support planner counts for the board when it is not its box's: the ink of a row of channel letters (0.12.75, Signs.TextBoxes). Null: the box. May be asked from any thread.</summary>
        public Func<float> InkArea;

        /// <summary>A copy that an instance may change (the size of a guide board) without touching the catalogue's.</summary>
        public SignDef Derive() => (SignDef)MemberwiseClone();

        public uint Hash => SignCatalog.HashOf(Id);
    }

    /// <summary>One entry of the panel: a kind of sign with the settings it starts from. A classic sign has none; a guide board's preset has the spec of the figure it follows.</summary>
    public sealed class SignChoice
    {
        public string Id;
        public SignDef Def;

        /// <summary>The written spec; empty for a classic sign.</summary>
        public string Spec;

        public string Code, Zh, En, Pinyin, Group;

        /// <summary>The code for an English game (the standard's designations stay; the words of a code are put into English).</summary>
        public string CodeEn => Signs.CodeNames.En(Code);
        public string[] Keywords;

        /// <summary>The series the panel also lists it in, besides its kind ("" for none; 0.12.22: Signs.BrtSigns, 0.12.26: Signs.TollSigns).</summary>
        public string Series = "";

        /// <summary>Listed in the series' tab only, not in the tab of its kind (0.12.107: what belongs to a toll plaza, or to the greening, and to nothing else).</summary>
        public bool SeriesOnly => Signs.SignSeries.Exclusive(Id);

        /// <summary>Not listed in the panel, only chosen by the mod: the bare custom sign that a picture just imported starts from (0.12.29).</summary>
        public bool Hidden;
    }

    public static class SignCatalog
    {
        /// <summary>The kinds of sign that are not templates: none (every sign of the standard is a template that is laid out and painted from its parts; the two test signs of 0.1.x are templates now, under their old ids as legacy ids).</summary>
        private static readonly SignDef[] s_All = new SignDef[0];

        /// <summary>The id the first builds (0.1.x) gave their one test board; items saved with it are drawn as an exit advance board so that they are not lost.</summary>
        public const string LegacyTestExit = "test.exit";

        private static volatile SignDef[] s_Templates;
        private static volatile SignDef[] s_Every;

        /// <summary>The signs by the hash that is saved with an item, and the templates by the hashes of the ids they replaced, made from the table they stand beside (a table that is made again gets its own).</summary>
        private sealed class HashIndex
        {
            public SignDef[] Source;
            public Dictionary<uint, SignDef> ByHash, ByLegacyHash;
        }

        private static volatile HashIndex s_HashIndex;

        /// <summary>
        /// Guards the two tables below (0.12.73): the first thread to ask makes a table under it and the others take that one. A table built twice would be two sets of SignDefs, which are compared by
        /// reference, and the planners run on workers. A built table is read without the lock; the lock is reentrant (Every asks for Templates).
        /// </summary>
        private static readonly object s_BuildLock = new object();
        private static volatile SignChoice[] s_Choices;
        private static int s_ChoicesCustom = -1;

        private static SignDef[] Every
        {
            get
            {
                if (s_Every != null)
                    return s_Every;
                lock (s_BuildLock)
                    return s_Every ?? (s_Every = Concat(s_All, Templates()));
            }
        }

        /// <summary>The templates' defs. Every path to them goes through Every first today (LegacyTemplate is reached after it), so its own lock is for the day one does not: no test can tell the two apart.</summary>
        private static SignDef[] Templates()
        {
            if (s_Templates != null)
                return s_Templates;
            lock (s_BuildLock)
                return s_Templates ?? (s_Templates = BuildTemplateDefs());
        }

        private static SignDef[] BuildTemplateDefs()
        {
            var list = new List<SignDef>();
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                GuideBoard board = t.Layout(t.Default());
                list.Add(new SignDef
                {
                    Id = t.Id,
                    Code = t.Code,
                    Zh = t.Zh,
                    En = t.En,
                    Group = t.Group,
                    Keywords = t.Keywords,
                    Pinyin = t.Pinyin,
                    Shape = ShapeOf(board.Main.Shape),
                    Width = board.Main.W,
                    Height = board.Main.H,
                    CornerRadius = board.Main.Radius,
                    Side = board.Main.Side,
                    ExtraTop = board.ExtraTop,
                    BottomClearance = Signs.PlaceNameSigns.IsStone(t.Id) ? Signs.PlaceNameSigns.StoneClearance : t.Clearance > 0f ? t.Clearance : SupportCatalog.RoadsideClearance,
                    TextureWidth = 512,
                    TextureHeight = 512,
                    Template = t
                });
            }

            return list.ToArray();
        }

        /// <summary>The template that replaced a sign of an earlier build (its old id is in LegacyIds), found by the old id's hash; null when there is none.</summary>
        private static SignDef LegacyTemplate(uint oldHash) =>
            IndexOf(Every).ByLegacyHash.TryGetValue(oldHash, out SignDef def) ? def : null;

        /// <summary>The kind of outline a board part has, as the geometry (supports, picking, the mesh) knows it.</summary>
        public static ShapeKind ShapeOf(GuidePartShape shape)
        {
            switch (shape)
            {
                case GuidePartShape.Circle: return ShapeKind.Circle;
                case GuidePartShape.Octagon: return ShapeKind.Octagon;
                case GuidePartShape.Triangle: return ShapeKind.Triangle;
                case GuidePartShape.TriangleDown: return ShapeKind.TriangleDown;
                default: return ShapeKind.RoundRect;
            }
        }

        private static SignDef[] Concat(SignDef[] a, SignDef[] b)
        {
            var all = new SignDef[a.Length + b.Length];
            Array.Copy(a, all, a.Length);
            Array.Copy(b, 0, all, a.Length, b.Length);
            return all;
        }

        /// <summary>Every kind of sign an item can be: the classic signs and the templates of the guide boards.</summary>
        public static IReadOnlyList<SignDef> All => Every;

        /// <summary>
        /// What the panel lists: the classic signs and every preset of the guide boards, each with the settings it starts from, and the signs the player saved from pictures (made again when those
        /// change: CustomSigns.Version, and the switch that lists the automatic marking).
        /// </summary>
        public static IReadOnlyList<SignChoice> Choices
        {
            get
            {
                SignChoice[] choices = s_Choices;
                int custom = Signs.CustomSigns.Version * 2 + (AutoMarking.Offered ? 1 : 0);
                if (choices == null || s_ChoicesCustom != custom)
                {
                    choices = BuildChoices();
                    s_Choices = choices;
                    s_ChoicesCustom = custom;
                }

                return choices;
            }
        }

        private static SignChoice[] BuildChoices()
        {
            var list = new List<SignChoice>();
            foreach (SignDef def in s_All)
                list.Add(new SignChoice { Id = def.Id, Def = def, Spec = "", Code = def.Code, Zh = def.Zh, En = def.En, Keywords = def.Keywords, Pinyin = def.Pinyin, Group = def.Group, Series = Signs.SignSeries.Of(def.Id, def.Group) });
            foreach (GuideTemplate template in GuideTemplates.All)
            {
                if (!template.Standalone)
                    continue;
                SignDef def = ById(template.Id);
                list.Add(new SignChoice
                {
                    Id = template.Id, Def = def, Spec = template.Default().ToString(), Code = template.Code, Zh = template.Zh, En = template.En,
                    Keywords = template.Keywords, Pinyin = template.Pinyin, Group = template.Group, Series = Signs.SignSeries.Of(template.Id, template.Group),
                    Hidden = template.Id == AutoMarking.Id && !AutoMarking.Offered
                });
            }

            foreach (GuidePreset preset in GuideTemplates.Presets)
            {
                SignDef def = ById(preset.Template);
                list.Add(new SignChoice
                {
                    Id = preset.Id,
                    Def = def,
                    Spec = GuideTemplates.SpecOf(preset).ToString(),
                    Code = preset.Code,
                    Zh = preset.Zh,
                    En = preset.En,
                    Keywords = preset.Keywords,
                    Pinyin = preset.Pinyin,
                    // A preset is of its template's kind: a marking's preset is placed as a marking.
                    Group = def.Group,
                    Series = Signs.SignSeries.Of(preset.Id, def.Group)
                });
            }

            SignDef custom = ById(Signs.CustomSigns.TemplateId);
            GuideTemplate own = Signs.CustomSigns.Template;
            list.Add(new SignChoice
            {
                Id = own.Id, Def = custom, Spec = own.Default().ToString(), Code = own.Code, Zh = own.Zh, En = own.En, Keywords = own.Keywords, Pinyin = own.Pinyin, Group = own.Group,
                Hidden = true
            });
            foreach (Signs.CustomSign sign in Signs.CustomSigns.Saved)
            {
                string name = Signs.CustomSigns.NameOf(sign.Spec);
                list.Add(new SignChoice
                {
                    Id = Signs.CustomSigns.ChoicePrefix + sign.Key, Def = custom, Spec = sign.Spec, Code = own.Code, Zh = name, En = name,
                    Keywords = new[] { "自定义", "custom", name }, Pinyin = "", Group = own.Group
                });
            }

            return list.ToArray();
        }

        public static SignChoice ChoiceById(string id)
        {
            foreach (SignChoice choice in Choices)
            {
                if (choice.Id == id)
                    return choice;
            }

            return null;
        }


        public static SignDef ById(string id)
        {
            foreach (SignDef def in Every)
            {
                if (def.Id == id)
                    return def;
            }

            if (id == LegacyTestExit)
                return ById("guide.exit.advance");
            return LegacyTemplate(HashOf(id));
        }

        /// <summary>
        /// The sign with this hash. A table lookup: until 0.12.75 it was a walk over every sign that hashed each id again, 4 to 13 microseconds a call, and the items' road check asked it for each item
        /// of every round (SignDef.Hash is computed from the id, not kept).
        /// </summary>
        public static SignDef ByHash(uint hash)
        {
            if (IndexOf(Every).ByHash.TryGetValue(hash, out SignDef def))
                return def;
            if (hash == LegacyTestExitHash)
                return ById("guide.exit.advance");
            return LegacyTemplate(hash);
        }

        private static readonly uint LegacyTestExitHash = HashOf(LegacyTestExit);

        private static HashIndex IndexOf(SignDef[] every)
        {
            HashIndex index = s_HashIndex;
            if (index != null && ReferenceEquals(index.Source, every))
                return index;
            lock (s_BuildLock)
            {
                index = s_HashIndex;
                if (index != null && ReferenceEquals(index.Source, every))
                    return index;
                index = new HashIndex { Source = every, ByHash = HashTable(every), ByLegacyHash = LegacyTable(Templates()) };
                s_HashIndex = index;
                return index;
            }
        }

        /// <summary>The signs by hash; of two with one hash the first in the table is the one a walk over it would find.</summary>
        internal static Dictionary<uint, SignDef> HashTable(IList<SignDef> defs)
        {
            var table = new Dictionary<uint, SignDef>(defs.Count);
            foreach (SignDef def in defs)
            {
                uint hash = HashOf(def.Id);
                if (!table.ContainsKey(hash))
                    table[hash] = def;
            }

            return table;
        }

        /// <summary>The templates by the hash of each id they replaced; the first template (and its first old id) to claim a hash keeps it.</summary>
        internal static Dictionary<uint, SignDef> LegacyTable(IList<SignDef> templates)
        {
            var table = new Dictionary<uint, SignDef>();
            foreach (SignDef def in templates)
            {
                foreach (string old in def.Template.LegacyIds)
                {
                    uint hash = HashOf(old);
                    if (!table.ContainsKey(hash))
                        table[hash] = def;
                }
            }

            return table;
        }

        /// <summary>FNV-1a of the id: the number that is saved with an item, so an id can be changed in the catalogue without a save losing it.</summary>
        public static uint HashOf(string id)
        {
            uint h = 2166136261u;
            foreach (char c in id)
            {
                h ^= c & 0xFFu;
                h *= 16777619u;
                h ^= (uint)c >> 8;
                h *= 16777619u;
            }

            return h;
        }

        /// <summary>The outline of the board, counter-clockwise, around the middle of the board.</summary>
        public static List<Point2> Outline(SignDef def)
        {
            var points = new List<Point2>();
            switch (def.Shape)
            {
                case ShapeKind.Circle:
                {
                    const int n = 48;
                    float r = def.Width * 0.5f;
                    for (int i = 0; i < n; i++)
                    {
                        float a = (float)(2.0 * Math.PI * i / n);
                        points.Add(new Point2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r));
                    }

                    break;
                }
                case ShapeKind.Octagon:
                {
                    // The distance between parallel sides is the width, so the corners lie on a circle a little larger.
                    float r = def.Width * 0.5f / (float)Math.Cos(Math.PI / 8);
                    for (int i = 0; i < 8; i++)
                    {
                        float a = (float)(Math.PI / 8 + Math.PI / 4 * i);
                        points.Add(new Point2((float)Math.Cos(a) * r, (float)Math.Sin(a) * r));
                    }

                    break;
                }
                case ShapeKind.RoundRect:
                    return GuideKit.RoundRect(def.Width, def.Height, def.CornerRadius);
                case ShapeKind.Triangle:
                    return GuideKit.TriangleOutlineIn(def.Side > 0f ? def.Side : def.Width, def.CornerRadius, true);
                case ShapeKind.TriangleDown:
                    return GuideKit.TriangleOutlineIn(def.Side > 0f ? def.Side : def.Width, def.CornerRadius, false);
                default:
                {
                    float w = def.Width * 0.5f;
                    float h = def.Height * 0.5f;
                    points.Add(new Point2(-w, -h));
                    points.Add(new Point2(w, -h));
                    points.Add(new Point2(w, h));
                    points.Add(new Point2(-w, h));
                    break;
                }
            }

            return points;
        }

        /// <summary>The width of the board at a height above its middle (m), for the length of a rail behind it; a rectangle is as wide everywhere.</summary>
        public static float ChordAt(SignDef def, float dy)
        {
            if (def.Shape == ShapeKind.Rect || def.Shape == ShapeKind.RoundRect)
                return def.Width;
            if (def.Shape == ShapeKind.Triangle || def.Shape == ShapeKind.TriangleDown)
            {
                // The width at a height above the middle of the box: nothing at the apex, the whole side at the base.
                float fromApex = def.Shape == ShapeKind.Triangle ? def.Height * 0.5f - dy : dy + def.Height * 0.5f;
                return Math.Max(0f, Math.Min(def.Width, def.Width * fromApex / def.Height));
            }
            float r = def.Width * 0.5f;
            return 2f * (float)Math.Sqrt(Math.Max(0f, r * r - dy * dy));
        }
    }
}
