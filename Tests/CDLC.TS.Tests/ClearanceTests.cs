using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The clearance of a hung support set by hand (0.12.78; the owner: a height slider for the gantry, for a height-limit frame at a tunnel): the layout follows it (the truss, the arms, the tops of the posts), the boards
    /// hang at it, a board put on later takes it, the records and the caches follow it, and the save keeps it (TsItem 11, record format 7) without changing what a save with none looks like.
    /// </summary>
    internal static class ClearanceTests
    {
        public static void RunAll()
        {
            Program.Run("clearance: the layout of a hung support follows it, the others and the standard's own do not", Layout);
            Program.Run("clearance: the boards hang at it (with a rise, over lanes, on the back, on both arms) and the support is made for it", Boards);
            Program.Run("clearance: a board put on or moved on a support has the clearance the others have", Attaching);
            Program.Run("clearance: the records, the tile diff, the live key and the parts caches follow it", Records);
            Program.Run("clearance: the record of a save keeps it (format 7), a save with none is the format it was", Saved);
            Program.Run("clearance: the boards on the post keep their room, a lower clearance is held where they still fit", PostRoom);
        }

        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f)
        };

        private static readonly SignDef Tall = Make("t.tall", 2.0f, 2.0f), Short = Make("t.short", 1.5f, 1.0f), Small = Make("t.small", 0.8f, 0.8f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int slot, float clearance = 0f, float rise = 0f, int lane = 0, float x = 0f) =>
            new AssemblyBoard { Instance = Inst(def), Member = member, Slot = slot, Clearance = clearance, Rise = rise, Lane = lane, OffsetX = x };

        private static SupportDef Of(string id) => SupportCatalog.ById(id);

        private static void Layout()
        {
            SupportDef gantry = Of(SupportCatalog.Gantry);
            foreach (float span in new[] { 10f, 14f, 26f })
            {
                SupportLayout standard = SupportLayouts.Of(Short, gantry, span);
                SupportLayout same = SupportLayouts.Of(Short, gantry, span, 0f);
                Program.Check(standard.BottomClearance == 5.5f && same.BottomClearance == 5.5f && same.PoleTop == standard.PoleTop && same.ArmY == standard.ArmY && same.BoardTop == standard.BoardTop, $"span {span}: no clearance asked is the standard's 5.5 m, as before");
                foreach (float asked in new[] { 3f, 4.5f, 7.5f, 9f })
                {
                    SupportLayout low = SupportLayouts.Of(Short, gantry, span, asked);
                    float shift = asked - 5.5f;
                    Program.Near(low.BottomClearance, asked, 1e-5f, $"span {span}: the boards' lower edge is the {asked} m asked for");
                    Program.Near(low.BoardTop, standard.BoardTop + shift, 1e-4f, $"span {span}, {asked} m: the top of the boards moves with it");
                    Program.Near(low.ArmY, standard.ArmY + shift, 1e-4f, $"span {span}, {asked} m: the truss moves with it");
                    Program.Near(low.PoleTop, standard.PoleTop + shift, 1e-4f, $"span {span}, {asked} m: the posts' tops move with it (the feet do not: the posts get shorter or taller)");
                    Program.Check(low.ChordYs.Length == standard.ChordYs.Length && Math.Abs(low.ChordYs[0] - standard.ChordYs[0] - shift) < 1e-4f && Math.Abs(low.ChordYs[low.ChordYs.Length - 1] - standard.ChordYs[standard.ChordYs.Length - 1] - shift) < 1e-4f, $"span {span}, {asked} m: every chord moves with it");
                    Program.Check(low.ArmLength == standard.ArmLength && low.PoleX[1] == standard.PoleX[1] && low.BeamDepth == standard.BeamDepth && low.PoleRadius == standard.PoleRadius, $"span {span}, {asked} m: the span and the sections are the standard's");
                }
            }

            // Held to the range; not a number and 0 or less are the standard's.
            Program.Near(SupportLayouts.Of(Short, gantry, 14f, 1f).BottomClearance, SupportLayouts.MinClearance, 1e-5f, "below the lowest: the lowest (3 m)");
            Program.Near(SupportLayouts.Of(Short, gantry, 14f, 50f).BottomClearance, SupportLayouts.MaxClearance, 1e-5f, "above the highest: the highest (9 m)");
            Program.Check(SupportLayouts.Of(Short, gantry, 14f, float.NaN).BottomClearance == 5.5f && SupportLayouts.Of(Short, gantry, 14f, -2f).BottomClearance == 5.5f, "not a number or negative: the standard's");
            Program.Check(SupportLayouts.MinClearance == 3f && SupportLayouts.MaxClearance == 9f, "the range is 3 to 9 m");
            Program.Check(SupportLayouts.ClampClearance(float.NaN) == 0f && SupportLayouts.ClampClearance(0f) == 0f && SupportLayouts.ClampClearance(-1f) == 0f && SupportLayouts.ClampClearance(1f) == 3f && SupportLayouts.ClampClearance(99f) == 9f && SupportLayouts.ClampClearance(4.567f) == 4.57f, "a stored clearance: none is 0, else in the range, in centimetres");

            // Every support: the hung ones take it, the others have no use for it.
            foreach (SupportDef support in SupportCatalog.All)
            {
                SupportLayout standard = SupportLayouts.Of(Short, support, 0f), asked = SupportLayouts.Of(Short, support, 0f, 4.2f);
                if (SupportKinds.Hung(support.Kind))
                {
                    Program.Near(asked.BottomClearance, 4.2f, 1e-5f, support.Id + ": hung, takes it");
                    Program.Near(asked.PoleTop - standard.PoleTop, 4.2f - standard.BottomClearance, 1e-4f, support.Id + ": its post follows");
                    Program.Near(asked.ArmY - standard.ArmY, 4.2f - standard.BottomClearance, 1e-4f, support.Id + ": its arm follows");
                }
                else
                {
                    Program.Check(asked.BottomClearance == standard.BottomClearance && asked.PoleTop == standard.PoleTop, support.Id + ": not hung, the clearance means nothing");
                }
            }

            // The arms: every arm of the support moves, a double cantilever's two sides alike.
            SupportDef twin = Of(SupportCatalog.DoubleCantilever);
            SupportLayout twinStandard = SupportLayouts.Of(Tall, twin, 0f), twinLow = SupportLayouts.Of(Tall, twin, 0f, 4f);
            bool arms = twinLow.ArmYs.Length == twinStandard.ArmYs.Length;
            for (int i = 0; i < Math.Min(twinLow.ArmYs.Length, twinStandard.ArmYs.Length); i++)
                arms &= Math.Abs(twinLow.ArmYs[i] - twinStandard.ArmYs[i] - (4f - twinStandard.BottomClearance)) < 1e-4f;
            Program.Check(arms && twinLow.ArmYs.Length >= 1, "a double cantilever: every arm moves by the same");
        }

        private static void Boards()
        {
            SupportDef gantry = Of(SupportCatalog.Gantry);
            Program.Check(Assembly.ClearanceOf(new List<AssemblyBoard>()) == 0f && Assembly.ClearanceOf(new[] { B(Short, 0, 0), B(Short, 1, 1) }) == 0f, "no board has one: none");
            Program.Check(Assembly.ClearanceOf(new[] { B(Short, 0, 0), B(Short, 1, 1, 4.5f) }) == 4.5f, "the first board that has one gives it");
            Program.Check(Assembly.ClearanceOf(new[] { B(Short, 0, 0, 4.5f), B(Short, 1, 1, 6f) }) == 4.5f, "of two that differ, the first in the order");
            Program.Check(Assembly.ClearanceKey(null) == 0 && Assembly.ClearanceKey(B(Short, 0, 0)) == 0 && Assembly.ClearanceKey(B(Short, 0, 0, 4.5f)) == 450 && Assembly.ClearanceKey(B(Short, 0, 0, 4.5f)) != Assembly.ClearanceKey(B(Short, 0, 0, 4.6f)), "the cache key is the clearance in centimetres");

            // One board on a gantry: its lower edge is the clearance.
            AssemblyPlan standard = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0) }, gantry, 16f);
            AssemblyPlan low = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 4.5f) }, gantry, 16f);
            Program.Near(standard.Placed[0].Bottom, 5.5f, 1e-5f, "the standard: 5.5 m");
            Program.Near(low.Placed[0].Bottom, 4.5f, 1e-5f, "set to 4.5 m: the board hangs at 4.5 m");
            Program.Near(low.Layout.BoardTop, 4.5f + Short.Height, 1e-5f, "the support is made for it: the top of the board");
            Program.Check(low.Layout.ArmY < standard.Layout.ArmY && low.Layout.PoleTop < standard.Layout.PoleTop && low.Layout.PoleTop > low.Placed[0].Top, "the truss and the posts come down with it, the posts still above the board");

            // A rise stays a rise from the clearance; over lanes, on the back, on a cantilever, on both arms of a double one.
            AssemblyPlan raised = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 4.5f, 0.6f) }, gantry, 16f);
            Program.Near(raised.Placed[0].Bottom, 5.1f, 1e-5f, "a board raised by 0.6 m is 0.6 m above the clearance, whatever that is");
            var lanes = new List<AssemblyBoard> { B(Short, 0, 0, 7f, 0.4f, Attach.LaneHand, 5f), B(Short, 1, 1, 7f, 0f, Attach.LaneHand, 9f) };
            AssemblyPlan over = Assembly.Plan(Assembly.Order(lanes), gantry, 20f);
            Program.Near(over.Placed.Find(p => p.Board.Member == 1).Bottom, 7f, 1e-5f, "over lanes: at the clearance");
            Program.Near(over.Placed.Find(p => p.Board.Member == 0).Bottom, 7.4f, 1e-5f, "over lanes, raised: above it");
            var back = new List<AssemblyBoard> { B(Short, 0, 0, 4f) };
            back[0].Back = true;
            Program.Near(Assembly.Plan(Assembly.Order(back), gantry, 20f).Placed[0].Bottom, 4f, 1e-5f, "on the back of the beam: the same");
            AssemblyPlan arm = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 4.2f) }, Of(SupportCatalog.Cantilever), 0f);
            Program.Near(arm.Placed[0].Bottom, 4.2f, 1e-5f, "on a cantilever");
            AssemblyPlan both = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Short, 0, 0, 4.2f), B(Short, 1, 1, 4.2f) }), Of(SupportCatalog.DoubleCantilever), 0f);
            Program.Check(Math.Abs(both.Placed[0].Bottom - 4.2f) < 1e-5f && Math.Abs(both.Placed[1].Bottom - 4.2f) < 1e-5f, "on both arms of a double cantilever");

            // The posts' boards are not on the beam: a board on the post of a hung support is stacked there whatever the clearance is.
            AssemblyPlan onPost = Assembly.Plan(new List<AssemblyBoard> { B(Small, 0, 0, 4.5f, 0f, Assembly.OnPost) }, gantry, 16f);
            AssemblyPlan onPostPlain = Assembly.Plan(new List<AssemblyBoard> { B(Small, 0, 0, 0f, 0f, Assembly.OnPost) }, gantry, 16f);
            Program.Near(onPost.Placed[0].Bottom, onPostPlain.Placed[0].Bottom, 1e-5f, "a board on the post of a gantry stays where the post's stack puts it");

            // A support that is not hung has no use for it.
            SupportDef pole = Of(SupportCatalog.SinglePole);
            AssemblyPlan postAsked = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 4.5f) }, pole, 0f), postPlain = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0) }, pole, 0f);
            Program.Check(postAsked.Placed[0].Bottom == postPlain.Placed[0].Bottom && postAsked.Layout.PoleTop == postPlain.Layout.PoleTop, "a post: the clearance means nothing");

            // What is built is what the plan says: the lower edge of the face and the top of the posts.
            SignParts parts = SignBuilder.BuildAssembly(new List<AssemblyBoard> { B(Short, 0, 0, 4.5f) }, gantry, 16f);
            SignParts usual = SignBuilder.BuildAssembly(new List<AssemblyBoard> { B(Short, 0, 0) }, gantry, 16f);
            parts.Faces[0].Mesh.Bounds(out Vec3 faceLow, out _);
            usual.Faces[0].Mesh.Bounds(out Vec3 faceUsual, out _);
            Program.Near(faceUsual.Y - faceLow.Y, 1f, 0.02f, "the face of the board is 1 m lower in the mesh");
            Program.Near(usual.Max.Y - parts.Max.Y, 1f, 0.05f, "and the whole support is 1 m lower: the posts' tops with it");
        }

        private static void PostRoom()
        {
            SupportDef gantry = Of(SupportCatalog.Gantry), arm = Of(SupportCatalog.Cantilever);
            foreach (SupportDef support in new[] { gantry, arm })
            {
                List<AssemblyBoard> Stack(float clearance) => new List<AssemblyBoard> { B(Small, 0, 0, clearance, 0f, Assembly.OnPost), B(Small, 1, 1, clearance, 0f, Assembly.OnPost) };
                AssemblyPlan standard = Assembly.Plan(Assembly.Order(Stack(0f)), support, 16f);
                Program.Check(standard.Placed.Count == 2 && standard.Placed.TrueForAll(p => p.OnPost) && Assembly.PostOverrun(standard) == 0f, support.Id + ": two small boards on the post fit at the standard clearance");
                AssemblyPlan low = Assembly.Plan(Assembly.Order(Stack(3f)), support, 16f);
                float over = Assembly.PostOverrun(low);
                Program.Check(over > 0.1f, support.Id + $": at 3 m the stack runs into the truss or the arm: {over:0.00} m over");
                float held = SpanSlider.WithPostRoom(support.Kind, 3f, over);
                Program.Check(held > 3f && held <= SupportLayouts.MaxClearance && Math.Abs(held * 10f - Math.Round(held * 10f)) < 1e-3f, support.Id + $": held at {held}: raised, in tenths, within the range");
                Program.Check(Assembly.PostOverrun(Assembly.Plan(Assembly.Order(Stack(held)), support, 16f)) == 0f, support.Id + ": at the held clearance the boards fit");
                // and by the layout itself, not by the function that holds it: the stack's top is under the post's top, and by less than a tenth (it is the lowest)
                AssemblyPlan atHeld = Assembly.Plan(Assembly.Order(Stack(held)), support, 16f);
                float stackTop = float.MinValue;
                foreach (Placement p in atHeld.Placed)
                    stackTop = Math.Max(stackTop, p.Bottom + p.HeightWithPlate);
                float slack = atHeld.Layout.PostTop - stackTop;
                Program.Check(slack >= -1e-4f && slack < 0.1f + 1e-3f, support.Id + $": the stack's top is {slack:0.000} m under the post's top at the held clearance");
                Program.Check(Assembly.PostOverrun(Assembly.Plan(Assembly.Order(Stack(held - 0.1f)), support, 16f)) > 0f, support.Id + ": and a tenth lower they do not: it is the lowest");
                float roomy = Assembly.PostOverrun(Assembly.Plan(Assembly.Order(Stack(8f)), support, 16f));
                Program.Check(roomy == 0f && SpanSlider.WithPostRoom(support.Kind, 8f, roomy) == 8f, support.Id + ": a high clearance leaves them room and is as asked");
            }

            // A board hung on the beam is not on the post: it never counts; nor does any board of a support that stacks no boards on a post.
            AssemblyPlan beam = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 3f) }, gantry, 16f);
            Program.Check(Assembly.PostOverrun(beam) == 0f, "a board on the beam: no overrun at the lowest clearance");
            Program.Check(SpanSlider.WithPostRoom(SupportKind.Gantry, 4f, 0f) == 4f && SpanSlider.WithPostRoom(SupportKind.Gantry, 4f, -1f) == 4f && SpanSlider.WithPostRoom(SupportKind.Gantry, 4.55f, 0f) == 4.55f, "no overrun (or a negative one): exactly as asked, not rounded up");
            Program.Check(SpanSlider.WithPostRoom(SupportKind.Gantry, 3f, 0.51f) == 3.6f && SpanSlider.WithPostRoom(SupportKind.Gantry, 3f, 0.5f) == 3.5f && SpanSlider.WithPostRoom(SupportKind.Gantry, 3f, 0.50005f) == 3.5f && SpanSlider.WithPostRoom(SupportKind.Gantry, 3f, 0.502f) == 3.6f, "the next tenth above what they need; an exact tenth is not pushed a tenth higher by float noise");
            Program.Check(SpanSlider.WithPostRoom(SupportKind.Gantry, 8.5f, 4f) == 9f, "never above the highest");
        }

        private static AttachResult Eval(List<AssemblyBoard> existing, SignDef add, SupportDef support, bool moving = false, int member = -1, float reach = 0f, float offset = float.NaN) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Reach = reach, Board = Inst(add), Moving = moving, Member = member, Offset = offset });

        private static void Attaching()
        {
            SupportDef gantry = Of(SupportCatalog.Gantry);
            AttachResult put = Eval(new List<AssemblyBoard> { B(Tall, 0, 0, 4.5f, 0f, Attach.LaneHand, 3f) }, Short, gantry, reach: 20f);
            AssemblyBoard added = put.Boards.Find(b => b.Member == put.NewMember);
            Program.Check(put.Ok && added != null && added.Clearance == 4.5f && put.Boards.Find(b => b.Member == 0).Clearance == 4.5f, "a board put on a support that was set to 4.5 m hangs at 4.5 m, and so do the others");
            Program.Near(Assembly.Plan(Assembly.Order(put.Boards), put.Support, put.Reach).Placed.Find(p => p.Board.Member == put.NewMember).Bottom, 4.5f, 1e-5f, "and that is where the plan puts it");
            AttachResult plain = Eval(new List<AssemblyBoard> { B(Tall, 0, 0, 0f, 0f, Attach.LaneHand, 3f) }, Short, gantry, reach: 20f);
            Program.Check(plain.Ok && plain.Boards.TrueForAll(b => b.Clearance == 0f), "a support with none: the new board has none");
            AttachResult moved = Eval(new List<AssemblyBoard> { B(Tall, 0, 0, 6f, 0f, Attach.LaneHand, 3f) }, Short, gantry, true, 1, 20f, 12f);
            Program.Check(moved.Ok && moved.Boards.Find(b => b.Member == 1).Clearance == 6f && moved.Boards.Find(b => b.Member == 0).Clearance == 6f, "a board that is moved keeps the support's clearance");
            AttachResult first = Eval(new List<AssemblyBoard>(), Short, gantry, reach: 20f);
            Program.Check(first.Ok && first.Boards[0].Clearance == 0f, "the first board of a support has none to take");
        }

        private static ItemRecord Record(float clearance) =>
            new ItemRecord(SignCatalog.ChoiceById("gb.prohib.05").Def.Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(SupportCatalog.Gantry).Hash, null, 16f, 99u, 0) { Clearance = clearance };

        private static void Records()
        {
            ItemRecord a = Record(0f), b = Record(4.5f);
            Program.Check(!TileDiff.Same(a, b) && TileDiff.Same(a, Record(0f)) && TileDiff.Same(b, Record(4.5f)), "a record with another clearance is another picture for the tile diff");
            Program.Check(LiveEdit.Key(new[] { a }, null) != LiveEdit.Key(new[] { b }, null) && LiveEdit.Key(new[] { a }, null) == LiveEdit.Key(new[] { Record(0f) }, null), "and for the live edit's key");
            List<AssemblyBoard> boards = ItemGroups.Boards(new[] { a, b });
            Program.Check(boards.Count == 2 && boards[0].Clearance == 0f && boards[1].Clearance == 4.5f, "the groups read the clearance into the boards");

            SignParts one = ChunkBuilder.PartsOfGroup(new[] { a }), other = ChunkBuilder.PartsOfGroup(new[] { b });
            Program.Check(one != null && other != null && one.Max.Y - other.Max.Y > 0.9f && one.Max.Y - other.Max.Y < 1.1f, $"the group's parts are 1 m lower at 4.5 m: {one?.Max.Y:0.00} and {other?.Max.Y:0.00}");
            SignParts alone = ChunkBuilder.PartsOfRecord(a), low = ChunkBuilder.PartsOfRecord(b);
            Program.Check(alone != null && low != null && alone.Max.Y - low.Max.Y > 0.9f, $"a board alone on its support is drawn at its clearance: {alone?.Max.Y:0.00} and {low?.Max.Y:0.00}");
            Program.Check(ChunkBuilder.PartsOfGroup(new[] { b }) == other, "the same record again is the cached one");
        }

        private static SaveRecord Sample()
        {
            var r = new SaveRecord { ModVersion = "0.12.78", City = "c", SaveId = "s", SaveName = "n", CityName = "c" };
            r.Items.Add(new StoredItem { TypeHash = 5, Position = new Vec3(1f, 2f, 3f), Lane = 255, OffsetX = 4f, Spec = "a=b", Anchor = -1 });
            r.Items.Add(new StoredItem { TypeHash = 6, Position = new Vec3(4f, 5f, 6f), Reach = 12f, Anchor = -1, BatchId = 77 });
            r.Items.Add(new StoredItem { TypeHash = 7, Position = new Vec3(7f, 8f, 9f), Anchor = -1 });
            return r;
        }

        private static byte[] Bytes(SaveRecord record, int? format = null)
        {
            var stream = new MemoryStream();
            if (format.HasValue)
                record.Write(stream, format.Value);
            else
                record.Write(stream);
            return stream.ToArray();
        }

        private static SaveRecord Back(byte[] bytes) => SaveRecord.Read(new MemoryStream(bytes));

        private static void Saved()
        {
            Program.Check(SaveRecord.Format == 7 && SaveRecord.RiseFormat == 6 && SaveRecord.BatchFormat == 5 && SaveRecord.PlainFormat == 4, "the formats: 4 plain, 5 a batch, 6 a rise, 7 a clearance");

            // A save with no clearance is written as it was before: the mods before 0.12.78 read it.
            SaveRecord none = Sample();
            Program.Check(BitConverter.ToInt32(Bytes(none), 8) == SaveRecord.BatchFormat, "no clearance, a batch item: format 5, as before");
            none.Items[1].BatchId = 0;
            Program.Check(BitConverter.ToInt32(Bytes(none), 8) == SaveRecord.PlainFormat, "no clearance, nothing else: format 4, as before");
            none.Items[0].Rise = 0.35f;
            Program.Check(BitConverter.ToInt32(Bytes(none), 8) == SaveRecord.RiseFormat, "no clearance, a raised board: format 6, as before");

            SaveRecord set = Sample();
            set.Items[2].Clearance = 4.5f;
            byte[] bytes = Bytes(set);
            Program.Check(BitConverter.ToInt32(bytes, 8) == 7, "a clearance: format 7");
            SaveRecord read = Back(bytes);
            Program.Check(read.Items[2].Clearance == 4.5f && read.Items[0].Clearance == 0f && read.Items[1].Clearance == 0f, "each item's clearance read back");
            Program.Check(read.Items[1].BatchId == 77 && read.Items[0].Spec == "a=b" && read.Items[0].Lane == 255 && read.Items[2].Position.Z == 9f && read.Items[1].Reach == 12f, "what comes before and after it is not shifted");
            set.Items[0].Rise = 0.35f;
            SaveRecord both = Back(Bytes(set));
            Program.Check(Math.Abs(both.Items[0].Rise - 0.35f) < 1e-5f && both.Items[2].Clearance == 4.5f && both.Items[1].BatchId == 77, "a clearance with a rise and a batch: all three kept");

            // Read as the formats before: no clearance, nothing else lost.
            SaveRecord six = Back(Bytes(set, 6));
            Program.Check(six.Items.Count == 3 && six.Items.TrueForAll(i => i.Clearance == 0f) && Math.Abs(six.Items[0].Rise - 0.35f) < 1e-5f && six.Items[1].BatchId == 77, "written as format 6: no clearance, the rise and the batch kept");
            Program.Check(Back(Bytes(set, 5)).Items.TrueForAll(i => i.Clearance == 0f), "written as format 5: no clearance");

            // Out of range in a file: held to the range; not a number is none.
            set.Items[2].Clearance = 40f;
            Program.Check(Back(Bytes(set)).Items[2].Clearance == 9f, "past the highest in a file: the highest");
            set.Items[2].Clearance = 1.5f;
            Program.Check(Back(Bytes(set)).Items[2].Clearance == 3f, "below the lowest in a file: the lowest");
            set.Items[2].Clearance = float.NaN;
            SaveRecord nan = Back(Bytes(set, 7));
            Program.Check(nan.Items[2].Clearance == 0f, "not a number in a file: none");
            Program.Check(SaveRecord.Read(new MemoryStream(bytes), true).ItemCount == 3, "the header of a format 7 record reads");
        }
    }
}
