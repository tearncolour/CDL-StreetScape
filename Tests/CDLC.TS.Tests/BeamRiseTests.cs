using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>A board hung on an arm or a beam goes up and down from the support's clearance (0.12.73, TsItem.Rise): the steps and the limits, the layout made for the highest board, and the rise kept through the records.</summary>
    internal static class BeamRiseTests
    {
        public static void RunAll()
        {
            Program.Run("rise: the steps of the up and down keys, the floor at the clearance and the ceiling", Steps);
            Program.Run("rise: a hung board's lower edge is the clearance and its rise; the support is made for the highest", Layout);
            Program.Run("rise: only a board on an arm or a beam has one", Hung);
            Program.Run("rise: a board put on or moved on a beam has its rise, the others keep theirs", Evaluating);
            Program.Run("rise: the records, the tile diff, the live key and the parts caches follow it", Records);
            Program.Run("rise: the record of a save keeps it (format 6), a save with none is the older format", Saved);
            Program.Run("rise: the keys and the line of help for a board on a beam say up and down", Hints);
        }

        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f)
        };

        private static readonly SignDef Tall = Make("t.tall", 2.0f, 2.0f), Short = Make("t.short", 1.5f, 1.0f), Small = Make("t.small", 0.8f, 0.8f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int slot, float rise = 0f, int lane = 0, float x = 0f) =>
            new AssemblyBoard { Instance = Inst(def), Member = member, Slot = slot, Rise = rise, Lane = lane, OffsetX = x };

        private static SupportDef Arm => SupportCatalog.ById(SupportCatalog.Cantilever);
        private static SupportDef Gantry => SupportCatalog.ById(SupportCatalog.Gantry);
        private static SupportDef Pole => SupportCatalog.ById(SupportCatalog.SinglePole);

        private static void Steps()
        {
            Program.Near(Attach.StepRise(0f, 1, false), 0.1f, 1e-5f, "up from the clearance by 0.1 m");
            Program.Near(Attach.StepRise(0.1f, 1, true), 0.6f, 1e-5f, "with Shift by 0.5 m");
            Program.Near(Attach.StepRise(0.6f, -1, true), 0.1f, 1e-5f, "down by 0.5 m");
            Program.Near(Attach.StepRise(0.3f, -1, false), 0.2f, 1e-5f, "down by 0.1 m");
            Program.Check(Attach.StepRise(0f, -1, false) == 0f && Attach.StepRise(0.05f, -1, true) == 0f, "never below the clearance");
            Program.Near(Attach.StepRise(Assembly.MaxRise - 0.05f, 1, false), Assembly.MaxRise, 1e-5f, "never above the most");
            Program.Check(Attach.StepRise(Assembly.MaxRise, 1, true) == Assembly.MaxRise, "at the most it stays");
            float walked = 0f;
            for (int i = 0; i < 7; i++)
                walked = Attach.StepRise(walked, 1, false);
            Program.Near(walked, 0.7f, 1e-5f, "seven presses are 0.7 m, with no drift from the rounding");
            Program.Check(Assembly.ClampRise(float.NaN) == 0f && Assembly.ClampRise(-3f) == 0f && Assembly.ClampRise(99f) == Assembly.MaxRise && Assembly.ClampRise(0.123456f) == 0.12f, "a rise is kept within 0 to the most, in centimetres");
            Program.Check(Assembly.MaxRise == 2f, "the most is 2 m");
        }

        private static void Layout()
        {
            // One board on a cantilever: raised, its lower edge is the support's clearance and its rise, and the arm goes up with it.
            AssemblyPlan flat = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0) }, Arm, 0f);
            AssemblyPlan raised = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 0.6f) }, Arm, 0f);
            float clearance = flat.Layout.BottomClearance;
            Program.Near(flat.Placed[0].Bottom, clearance, 1e-5f, "not raised: at the clearance");
            Program.Near(raised.Placed[0].Bottom, clearance + 0.6f, 1e-5f, "raised by 0.6 m: the clearance and the rise");
            Program.Near(raised.Layout.BoardTop - flat.Layout.BoardTop, 0.6f, 1e-5f, "the envelope the support is made for is 0.6 m higher");
            Program.Check(raised.Layout.ArmY > flat.Layout.ArmY + 0.5f && raised.Layout.ArmY > raised.Placed[0].Bottom && raised.Layout.ArmY < raised.Placed[0].Top, "the arm goes up with the board and still passes behind it");

            // Two boards: the one that is not raised stays at the clearance; a board raised to the top of the taller one changes nothing of the layout.
            AssemblyPlan two = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Tall, 0, 0), B(Short, 1, 1, 1.0f) }), Arm, 0f);
            AssemblyPlan same = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Tall, 0, 0), B(Short, 1, 1) }), Arm, 0f);
            Placement tall = two.Placed.Find(p => p.Board.Member == 0), shorter = two.Placed.Find(p => p.Board.Member == 1);
            Program.Near(tall.Bottom, clearance, 1e-5f, "the board that is not raised stays at the clearance");
            Program.Near(shorter.Bottom, clearance + 1.0f, 1e-5f, "the raised one is above it");
            Program.Near(two.Layout.ArmY, same.Layout.ArmY, 1e-5f, "raised to the top of the taller board: the arm is where it was");
            AssemblyPlan above = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Tall, 0, 0), B(Short, 1, 1, 1.5f) }), Arm, 0f);
            Program.Near(above.Layout.BoardTop - same.Layout.BoardTop, 0.5f, 1e-5f, "raised above the taller board by 0.5 m: the envelope is 0.5 m higher");
            Program.Check(above.Layout.ArmY > same.Layout.ArmY, "and the arm goes up with it");

            // A gantry's boards, over lanes or side by side, and a double cantilever's two arms.
            var lanes = new List<AssemblyBoard> { B(Short, 0, 0, 0.4f, Attach.LaneHand, 5f), B(Short, 1, 1, 0f, Attach.LaneHand, 9f) };
            AssemblyPlan gantry = Assembly.Plan(Assembly.Order(lanes), Gantry, 20f);
            Program.Near(gantry.Placed.Find(p => p.Board.Member == 0).Bottom - gantry.Placed.Find(p => p.Board.Member == 1).Bottom, 0.4f, 1e-5f, "on a gantry, over lanes: 0.4 m between the lower edges");
            var back = new List<AssemblyBoard> { B(Short, 0, 0, 0.3f) };
            back[0].Back = true;
            AssemblyPlan backed = Assembly.Plan(Assembly.Order(back), Gantry, 20f);
            Program.Near(backed.Placed[0].Bottom, backed.Layout.BottomClearance + 0.3f, 1e-5f, "on the back of a gantry's beam too");
            SupportDef twin = SupportCatalog.ById(SupportCatalog.DoubleCantilever);
            AssemblyPlan both = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Short, 0, 0, 0.5f), B(Short, 1, 1) }), twin, 0f);
            Program.Near(Math.Abs(both.Placed[0].Bottom - both.Placed[1].Bottom), 0.5f, 1e-5f, "on a double cantilever, side by side");
            Program.Near(both.Placed.Find(p => p.Board.Member == 1).Bottom, both.Layout.BottomClearance, 1e-5f, "the one not raised at the clearance");

            // A rise out of range counts as the most; the post's boards and the boards that are not hung do not.
            AssemblyPlan wild = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 50f) }, Arm, 0f);
            Program.Near(wild.Placed[0].Bottom, wild.Layout.BottomClearance + Assembly.MaxRise, 1e-5f, "a rise of 50 m is the most");
        }

        private static void Hung()
        {
            AssemblyPlan pole = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 1.0f) }, Pole, 0f);
            AssemblyPlan plain = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0) }, Pole, 0f);
            Program.Near(pole.Placed[0].Bottom, plain.Placed[0].Bottom, 1e-5f, "on a post a rise means nothing");
            Program.Near(pole.Layout.PoleTop, plain.Layout.PoleTop, 1e-5f, "nor does the post grow");

            // A board on the post of a hung support is stacked there, whatever its rise says.
            var onPost = B(Short, 0, 0, 1.0f, Assembly.OnPost);
            AssemblyPlan armPost = Assembly.Plan(new List<AssemblyBoard> { onPost }, Arm, 0f);
            AssemblyPlan armPostPlain = Assembly.Plan(new List<AssemblyBoard> { B(Short, 0, 0, 0f, Assembly.OnPost) }, Arm, 0f);
            Program.Near(armPost.Placed[0].Bottom, armPostPlain.Placed[0].Bottom, 1e-5f, "a board on the post of a cantilever is where the stack puts it");
            Program.Near(armPost.Layout.ArmY, armPostPlain.Layout.ArmY, 1e-5f, "and takes no room from the arm");
        }

        private static AttachResult Eval(List<AssemblyBoard> existing, SignDef add, SupportDef support, float rise = 0f, bool moving = false, int member = -1, float reach = 0f, float offset = float.NaN) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Reach = reach, Board = Inst(add), Rise = rise, Moving = moving, Member = member, Offset = offset });

        private static void Evaluating()
        {
            AttachResult put = Eval(new List<AssemblyBoard> { B(Tall, 0, 0, 0f, Attach.LaneHand, 3f) }, Short, Arm, 0.7f);
            AssemblyBoard added = put.Boards.Find(b => b.Member == put.NewMember);
            Program.Check(put.Ok && put.Hung && Math.Abs(added.Rise - 0.7f) < 1e-5f, "a board put on a beam has the rise it was asked for: " + (added == null ? "none" : added.Rise.ToString()));
            Program.Check(put.Boards.Find(b => b.Member == 0).Rise == 0f, "the other board is not raised by it");

            var withRise = new List<AssemblyBoard> { B(Tall, 0, 0, 0.9f, Attach.LaneHand, 3f) };
            AttachResult next = Eval(withRise, Short, Arm, 0f, offset: 7f);
            Program.Near(next.Boards.Find(b => b.Member == 0).Rise, 0.9f, 1e-5f, "a board that is already there keeps its rise when another is put on");
            Program.Check(next.Boards.Find(b => b.Member == next.NewMember).Rise == 0f, "and the new board has none unless it asks for one");

            // Moved: the board is evaluated with its own rise, the others are the request's existing ones.
            AttachResult moved = Eval(new List<AssemblyBoard> { B(Tall, 0, 0, 0.9f, Attach.LaneHand, 3f) }, Short, Arm, 0.4f, true, 1, offset: 8f);
            Program.Check(moved.Ok && Math.Abs(moved.Boards.Find(b => b.Member == 1).Rise - 0.4f) < 1e-5f && Math.Abs(moved.Boards.Find(b => b.Member == 0).Rise - 0.9f) < 1e-5f, "a board that is moved has its new rise and the others keep theirs");
            AttachResult clamped = Eval(new List<AssemblyBoard>(), Short, Arm, 99f);
            Program.Check(clamped.Ok && clamped.Boards[0].Rise == Assembly.MaxRise, "a rise past the most is the most");

            // On a post nothing is raised; on the post of a hung support the board is stacked.
            // (a board of 1.5 m is large and does not go on a post at all)
            AttachResult onPost = Eval(new List<AssemblyBoard> { B(Small, 0, 0) }, Small, Pole, 1f);
            Program.Check(onPost.Ok && !onPost.Hung && onPost.Boards.TrueForAll(b => b.Rise == 0f), "a post ignores it");

            // The room: a raised board does not change where the others may stand along the beam, nor the free places.
            AttachResult flat = Eval(new List<AssemblyBoard> { B(Short, 0, 0, 0f, Attach.LaneHand, 3f) }, Short, Arm);
            AttachResult high = Eval(new List<AssemblyBoard> { B(Short, 0, 0, 0f, Attach.LaneHand, 3f) }, Short, Arm, 1.2f);
            Program.Check(flat.Free.Count == high.Free.Count && Math.Abs(flat.Offset - high.Offset) < 1e-5f, "the places along the beam are the same raised or not");

            // What comes back is what the build draws: a raised board is higher in the mesh.
            SignParts low = SignBuilder.BuildAssembly(flat.Boards, flat.Support, flat.Reach);
            SignParts up = SignBuilder.BuildAssembly(high.Boards, high.Support, high.Reach);
            Program.Check(up.Max.Y > low.Max.Y + 0.9f, $"the built support is higher with a raised board: {low.Max.Y:0.00} m and {up.Max.Y:0.00} m");
            int index = high.Boards.FindIndex(b => b.Member == high.NewMember);
            up.Faces[index].Mesh.Bounds(out Vec3 faceLo, out _);
            low.Faces[flat.Boards.FindIndex(b => b.Member == flat.NewMember)].Mesh.Bounds(out Vec3 flatLo, out _);
            Program.Near(faceLo.Y - flatLo.Y, 1.2f, 0.02f, "the face of the raised board is 1.2 m higher");
        }

        private static ItemRecord Record(float rise) =>
            new ItemRecord(SignCatalog.ChoiceById("gb.prohib.05").Def.Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(SupportCatalog.Cantilever).Hash, null, 0f, 99u, 0) { Rise = rise };

        private static void Records()
        {
            ItemRecord a = Record(0f), b = Record(0.5f);
            Program.Check(!TileDiff.Same(a, b) && TileDiff.Same(a, Record(0f)), "a record with another rise is another picture for the tile diff");
            Program.Check(LiveEdit.Key(new[] { a }, null) != LiveEdit.Key(new[] { b }, null) && LiveEdit.Key(new[] { a }, null) == LiveEdit.Key(new[] { Record(0f) }, null), "and for the live edit's key");
            List<AssemblyBoard> boards = ItemGroups.Boards(new[] { a, b });
            Program.Check(boards.Count == 2 && boards[0].Rise == 0f && Math.Abs(boards[1].Rise - 0.5f) < 1e-5f, "the groups read the rise into the boards");

            // The caches: the same group with another rise is another mesh (a hit on the old key would draw the board where it was).
            SignParts one = ChunkBuilder.PartsOfGroup(new[] { a });
            SignParts other = ChunkBuilder.PartsOfGroup(new[] { b });
            Program.Check(one != null && other != null && other.Max.Y > one.Max.Y + 0.4f, $"the group's parts are higher when raised: {one?.Max.Y:0.00} and {other?.Max.Y:0.00}");
            SignParts alone = ChunkBuilder.PartsOfRecord(a), alonUp = ChunkBuilder.PartsOfRecord(b);
            Program.Check(alone != null && alonUp != null && alonUp.Max.Y > alone.Max.Y + 0.4f, $"a board alone on its support is drawn raised: {alone?.Max.Y:0.00} and {alonUp?.Max.Y:0.00}");
        }

        private static bool Has(List<string[]> keys, string key) => keys.Exists(k => k[0] == key);

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                string language = zh ? " in Chinese" : " in English";
                // Putting a sign on a beam, and editing a board of one: left and right along it, and up and down.
                foreach (string kind in new[] { "attach", "move" })
                {
                    List<string[]> beam = HintText.Keys(new HintInfo { Kind = kind, Hung = true, Count = 2 }, zh);
                    Program.Check(Has(beam, "← →") && Has(beam, "↑ ↓") && beam.FindIndex(k => k[0] == "← →") < beam.FindIndex(k => k[0] == "↑ ↓"), kind + " on a beam: the keys along it and up and down" + language);
                    string up = beam.Find(k => k[0] == "↑ ↓")[1];
                    Program.Check(up.Length > 8 && up.Contains("Shift") && !up.Contains("{"), kind + ": the up and down line says the step with Shift" + language + ": " + up);
                    Program.Check(beam[beam.Count - 1][0].Contains("Esc"), kind + ": the way out is still last" + language);
                }

                // What it was before: a post's stack, a post at a height (the same keys as ever), a board that is turned on the post of a hung support.
                Program.Check(HintText.Keys(new HintInfo { Kind = "move", Hung = false, X = 2.4f }, zh).Exists(k => k[0] == "↑ ↓") && !Has(HintText.Keys(new HintInfo { Kind = "move", Hung = false, X = 2.4f }, zh), "← →"), "a board at a height on a post: up and down only" + language);
                Program.Check(!Has(HintText.Keys(new HintInfo { Kind = "move", Hung = true, Turns = true }, zh), "↑ ↓") && !Has(HintText.Keys(new HintInfo { Kind = "move", Hung = true, Turns = true }, zh), "← →"), "a board on the post of a hung support: neither" + language);
                Program.Check(Has(HintText.Keys(new HintInfo { Kind = "move", Hung = false, Count = 3 }, zh), "↑ ↓") && !Has(HintText.Keys(new HintInfo { Kind = "move", Hung = false, Count = 3 }, zh), "← →"), "a stack on a post: the order" + language);
                string line = HintText.Of(new HintInfo { Kind = "move", Hung = true }, zh);
                Program.Check(HintText.Keys(new HintInfo { Kind = "move", Hung = true }, zh).Exists(k => k[0] == "↑ ↓" && k[1].Contains(zh ? "升降" : "up / down")), "the card for a board on a beam says it goes up and down too" + language + ": " + line);
            }
        }

        private static SaveRecord Sample()
        {
            var r = new SaveRecord { ModVersion = "0.12.73", City = "c", SaveId = "s", SaveName = "n", CityName = "c" };
            r.Items.Add(new StoredItem { TypeHash = 5, Position = new Vec3(1f, 2f, 3f), Lane = 255, OffsetX = 4f, Spec = "a=b", Anchor = -1 });
            r.Items.Add(new StoredItem { TypeHash = 6, Position = new Vec3(4f, 5f, 6f), Reach = 12f, Anchor = -1, BatchId = 77 });
            r.Items.Add(new StoredItem { TypeHash = 7, Position = new Vec3(7f, 8f, 9f), Anchor = -1 });
            return r;
        }

        private static byte[] Bytes(SaveRecord record, int? format = null)
        {
            var stream = new System.IO.MemoryStream();
            if (format.HasValue)
                record.Write(stream, format.Value);
            else
                record.Write(stream);
            return stream.ToArray();
        }

        private static void Saved()
        {
            SaveRecord plain = Sample();
            plain.Items[1].BatchId = 0;
            Program.Check(BitConverter.ToInt32(Bytes(plain), 8) == SaveRecord.PlainFormat, "no batch and no raised board: format 4, as before");
            SaveRecord batch = Sample();
            Program.Check(BitConverter.ToInt32(Bytes(batch), 8) == SaveRecord.BatchFormat && SaveRecord.BatchFormat == 5, "a batch item and no raised board: format 5, as before");

            SaveRecord raised = Sample();
            raised.Items[0].Rise = 0.35f;
            byte[] bytes = Bytes(raised);
            Program.Check(BitConverter.ToInt32(bytes, 8) == 6 && SaveRecord.RiseFormat == 6, "a raised board: format 6");
            SaveRecord back = SaveRecord.Read(new System.IO.MemoryStream(bytes));
            Program.Check(Math.Abs(back.Items[0].Rise - 0.35f) < 1e-5f && back.Items[1].Rise == 0f && back.Items[2].Rise == 0f, "each item's rise read back");
            Program.Check(back.Items[1].BatchId == 77 && back.Items[0].Spec == "a=b" && back.Items[0].Lane == 255 && back.Items[2].Position.Z == 9f && back.Items[1].Reach == 12f, "what comes before and after it is not shifted");

            SaveRecord raisedPlain = Sample();
            raisedPlain.Items[1].BatchId = 0;
            raisedPlain.Items[2].Rise = Assembly.MaxRise;
            Program.Check(BitConverter.ToInt32(Bytes(raisedPlain), 8) == 6 && Math.Abs(SaveRecord.Read(new System.IO.MemoryStream(Bytes(raisedPlain))).Items[2].Rise - Assembly.MaxRise) < 1e-5f, "a raised board and no batch: still format 6");

            // Read as the older formats: no rise, nothing else lost. A rise out of range in the file is the most.
            SaveRecord five = SaveRecord.Read(new System.IO.MemoryStream(Bytes(raised, 5)));
            Program.Check(five.Items.Count == 3 && five.Items.TrueForAll(i => i.Rise == 0f) && five.Items[1].BatchId == 77, "written as format 5: no rise, the batch kept");
            raised.Items[0].Rise = 40f;
            Program.Check(SaveRecord.Read(new System.IO.MemoryStream(Bytes(raised))).Items[0].Rise == Assembly.MaxRise, "a rise past the most in a file is the most");
            Program.Check(SaveRecord.Read(new System.IO.MemoryStream(bytes), true).ItemCount == 3, "the header of a format 6 record reads");
        }
    }
}
