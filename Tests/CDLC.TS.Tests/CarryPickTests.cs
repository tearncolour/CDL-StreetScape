using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The eyedropper with Shift held takes all the boards of a pole (0.12.88; the owner: only one board by default, and with a key held all the boards on the pole): which clicks mean the whole pole,
    /// which supports carry one, what is taken of each board, how the carried boards are written and drawn again, the rings, and when the pole is let go. The tool's own part (the ring, the click,
    /// the items it writes) is not compiled here; what it writes comes from these functions.
    /// </summary>
    internal static class CarryPickTests
    {
        private static readonly Vec3 kAt = new Vec3(40f, 2f, -90f);

        public static void RunAll()
        {
            Program.Run("carry pick: which clicks take the whole pole", Wants);
            Program.Run("carry pick: which supports put boards down together", Takes);
            Program.Run("carry pick: what is taken of each board", Taken);
            Program.Run("carry pick: boards that are left out, and a pole of one", LeftOut);
            Program.Run("carry pick: the fields an item is written with", Fields);
            Program.Run("carry pick: the pole is drawn again as it was", Redrawn);
            Program.Run("carry pick: the rings, the count and the key", Rings);
            Program.Run("carry pick: when the pole is let go", Letting);
            Program.Run("carry pick: the line of help, the keys and what the items are written back as", Hints);
        }

        private static SignDef Real(string group, int skip = 0)
        {
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Group == group && !def.IsMarking && skip-- == 0)
                    return def;
            }

            Program.Check(false, "the catalogue has a sign of the group " + group);
            return SignCatalog.All[0];
        }

        private static bool Entry(SignDef def) => true;

        private static ItemRecord Member(SignDef def, int slot, uint support = 0u, float reach = 0f, int lane = 0, float offset = 0f, float turn = 0f, float rise = 0f, float clearance = 0f, float tilt = 0f, bool back = false, bool manual = false, byte style = 0)
        {
            var record = new ItemRecord(def.Hash, kAt, 0.7f, support, null, reach, 9u, slot, lane, offset) { Turn = turn, Rise = rise, Clearance = clearance, Tilt = tilt, Back = back, Manual = manual, Style = style };
            return record;
        }

        private static List<ItemRecord> ThreeOnAnArm(out SupportDef arm)
        {
            arm = SupportCatalog.ById(SupportCatalog.Cantilever);
            SignDef p = Real("prohibition"), m = Real("mandatory"), w = Real("warning");
            var list = new List<ItemRecord>
            {
                Member(p, 0, arm.Hash, 6f, lane: 1, offset: 5.5f, tilt: BoardTilt.Radians(1), style: 3),
                Member(m, 1, arm.Hash, 6f, lane: 2, offset: 2.5f, rise: 0.3f, clearance: 5.2f, tilt: BoardTilt.Radians(2), style: 3, manual: true),
                Member(w, 2, arm.Hash, 6f, lane: 0, offset: 0f, turn: 0.4f, tilt: BoardTilt.Radians(1), back: true, style: 3)
            };
            list.Sort(ItemGroups.Compare);
            return list;
        }

        private static void Wants()
        {
            foreach (int mode in new[] { 0, 1, 2, StylePick.Mode })
                foreach (bool picking in new[] { false, true })
                    foreach (bool shift in new[] { false, true })
                        foreach (bool typing in new[] { false, true })
                        {
                            bool expected = shift && !typing && (mode == StylePick.Mode || (mode == 0 && picking));
                            Program.Check(CarryPick.Wants(mode, picking, shift, typing) == expected, $"mode {mode}, Ctrl {picking}, Shift {shift}, typing {typing}: {expected}");
                        }

            Program.Check(CarryPick.Wants(StylePick.Mode, false, true, false), "the eyedropper with Shift takes the pole");
            Program.Check(!CarryPick.Wants(StylePick.Mode, false, false, false), "the eyedropper alone takes one board");
            Program.Check(CarryPick.Wants(0, true, true, false) && !CarryPick.Wants(0, false, true, false), "Ctrl and Shift while placing take the pole; Shift alone places as it always did");
            Program.Check(!CarryPick.Wants(1, true, true, false) && !CarryPick.Wants(2, true, true, false), "deleting and editing pick nothing");
            Program.Check(!CarryPick.Wants(StylePick.Mode, false, true, true), "Shift pressed while a text field has the keyboard is for the typing");
        }

        private static void Takes()
        {
            Program.Check(CarryPick.Takes(null), "the planner's support is the road's");
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole, SupportKind.Cantilever, SupportKind.FCantilever, SupportKind.DoubleCantilever, SupportKind.Gantry })
                Program.Check(CarryPick.Takes(kind), kind + " stands or hangs by a road: its boards go down together");
            foreach (SupportKind kind in new[] { SupportKind.WallFlat, SupportKind.WallBracket, SupportKind.AttachedArm, SupportKind.Attached, SupportKind.OverRoad })
                Program.Check(!CarryPick.Takes(kind), kind + " is put onto something that is there, a board at a time: the pick takes one");
        }

        private static void Taken()
        {
            List<ItemRecord> members = ThreeOnAnArm(out SupportDef arm);
            CarryPick pole = CarryPick.Of(members, 1, Entry, out int skipped);
            Program.Check(pole != null && skipped == 0 && pole.Count == 3, "a pole of three boards is taken whole");
            Program.Check(pole.Lead == 1, "the board that was clicked is the lead: " + pole.Lead);
            Program.Check(pole.Boards.Select(b => b.Def.Hash).SequenceEqual(members.Select(r => r.TypeHash)), "the boards are in the order of the slots");
            for (int i = 0; i < 3; i++)
            {
                CarriedBoard b = pole.Boards[i];
                ItemRecord r = members[i];
                Program.Check(b.Slot == r.Slot && b.Lane == r.Lane && b.OffsetX == r.OffsetX && b.Turn == r.Turn && b.Rise == r.Rise && b.Clearance == r.Clearance && b.Tilt == r.Tilt && b.Back == r.Back && b.Manual == r.Manual,
                    "board " + i + " keeps its place on the support: slot, lane, offset, turn, rise, clearance, tilt, back, order");
            }

            Program.Check(CarryPick.Of(members, 0, Entry, out _).Lead == 0 && CarryPick.Of(members, 2, Entry, out _).Lead == 2, "the lead is the board that was clicked, first or last");
            for (int i = 0; i < 3; i++)
                Program.Check(pole.Boards[i].Spec == (pole.Boards[i].Def.Template != null ? MarkingTemplates.Unplaced(pole.Boards[i].Def.Template, members[i].Spec) : ""), "board " + i + " takes its written settings without where they were put, and none when its sign has no template");
            SignDef guide = SignCatalog.All.First(d => d.Template != null && !d.IsMarking && d.Hash != Real("prohibition").Hash);
            string written = guide.Template.Default().ToString();
            var withGuide = new List<ItemRecord> { Member(Real("prohibition"), 0, arm.Hash, 6f), new ItemRecord(guide.Hash, kAt, 0.7f, arm.Hash, written, 6f, 9u, 1) };
            CarryPick mixed = CarryPick.Of(withGuide, 1, Entry, out _);
            Program.Check(mixed != null && mixed.Boards[1].Spec == MarkingTemplates.Unplaced(guide.Template, written), "a board's written settings are taken without where they were put");
        }

        private static void LeftOut()
        {
            List<ItemRecord> members = ThreeOnAnArm(out SupportDef arm);
            Program.Check(CarryPick.Of(new List<ItemRecord> { members[0] }, 0, Entry, out int none) == null && none == 0, "a pole of one board is no pole: the pick is a single one");
            Program.Check(CarryPick.Of(new List<ItemRecord>(), 0, Entry, out _) == null, "no board, no pole");

            var gone = new List<ItemRecord>(members);
            gone.Insert(1, new ItemRecord(4242u, kAt, 0.7f, arm.Hash, null, 6f, 9u, 5));
            CarryPick after = CarryPick.Of(gone, 2, Entry, out int skipped);
            Program.Check(after != null && skipped == 1 && after.Count == 3, "a board of a type that is gone is left out and counted");
            Program.Check(after.Lead == 1 && after.Boards[after.Lead].Def.Hash == members[1].TypeHash, "the lead stays the board that was clicked when one before it is left out");
            Program.Check(CarryPick.Of(gone, 1, Entry, out _) == null, "when the board clicked is the one that is gone there is nothing to pick");

            SignDef refused = SignCatalog.ByHash(members[2].TypeHash);
            CarryPick noEntry = CarryPick.Of(members, 0, def => def != refused, out int noEntrySkipped);
            Program.Check(noEntry != null && noEntrySkipped == 1 && noEntry.Count == 2 && noEntry.Boards.All(b => b.Def != refused), "a board whose sign has no entry in the panel is left out and counted");
            Program.Check(CarryPick.Of(members, 2, def => def != refused, out _) == null, "and when it is the board clicked there is nothing to pick");
            Program.Check(CarryPick.Of(members, 0, def => false, out int all) == null && all == 3, "no entry at all: nothing");

            SignDef marking = SignCatalog.All.First(d => d.IsMarking);
            var withPaint = new List<ItemRecord>(members) { new ItemRecord(marking.Hash, kAt, 0.7f, 0u, null, 0f, 9u, 7) };
            CarryPick painted = CarryPick.Of(withPaint, 0, Entry, out int paintSkipped);
            Program.Check(painted != null && paintSkipped == 1 && painted.Boards.All(b => !b.Def.IsMarking), "paint has no pole: a marking in the list is left out");
            Program.Check(CarryPick.Of(withPaint, withPaint.Count - 1, Entry, out _) == null, "a marking clicked is no pole");
        }

        private static void Fields()
        {
            var board = new CarriedBoard { Def = Real("prohibition"), Spec = "", Slot = 3, Lane = 2, OffsetX = 4.25f, Turn = 0.5f, Rise = 0.4f, Clearance = 5.5f, Tilt = BoardTilt.Radians(2), Back = true, Manual = true };
            BoardFields f = CarryPick.FieldsOf(board);
            Program.Check(f.Slot == 3 && f.Lane == 2 && f.OffsetX == 4.25f && f.Turn == 0.5f && f.Back && f.Manual, "the place on the support is written as it was");
            Program.Check(f.TiltSteps == 2, "the lean is written in steps: " + f.TiltSteps);
            Program.Check(f.Rise == Assembly.ClampRise(0.4f) && f.Clearance == SupportLayouts.ClampClearance(5.5f), "the rise and the clearance are in the range an item keeps them in");

            var wild = new CarriedBoard { Def = board.Def, Slot = 900, Lane = -4, OffsetX = 0f, Rise = 50f, Clearance = 90f, Tilt = 9f };
            BoardFields w = CarryPick.FieldsOf(wild);
            Program.Check(w.Slot == 255 && w.Lane == 0, "a slot over 255 and a lane under 0 are kept in a byte: " + w.Slot + ", " + w.Lane);
            Program.Check(w.Rise == Assembly.ClampRise(50f) && w.Clearance == SupportLayouts.ClampClearance(90f) && w.TiltSteps == BoardTilt.Max, "a rise, a clearance and a lean out of range are kept in it");
            BoardFields plain = CarryPick.FieldsOf(new CarriedBoard { Def = board.Def });
            Program.Check(plain.Slot == 0 && plain.Lane == 0 && plain.OffsetX == 0f && plain.Turn == 0f && plain.Rise == 0f && plain.Clearance == 0f && plain.TiltSteps == 0 && !plain.Back && !plain.Manual, "a board with nothing set writes nothing");
        }

        private static void Redrawn()
        {
            List<ItemRecord> members = ThreeOnAnArm(out SupportDef arm);
            CarryPick pole = CarryPick.Of(members, 1, Entry, out _);
            SignParts picked = ChunkBuilder.PartsOfGroup(members);
            string leadSpec = members[1].Spec;
            List<ItemRecord> again = CarryPick.Records(pole, leadSpec, members[1].Tilt, arm.Hash, 6f, 3, kAt, 0.7f);
            SignParts drawn = ChunkBuilder.PartsOfGroup(again);
            Program.Check(picked != null && drawn != null && picked.VertexCount == drawn.VertexCount && picked.VertexCount > 0, "the carried pole is drawn as the one it was taken from: " + picked?.VertexCount + " and " + drawn?.VertexCount);

            List<AssemblyBoard> was = ItemGroups.Boards(members), is2 = ItemGroups.Boards(again);
            Program.Check(was.Count == is2.Count && was.Count == 3, "three boards again");
            for (int i = 0; i < was.Count; i++)
            {
                AssemblyBoard a = was[i], b = is2[i];
                Program.Check(a.Slot == b.Slot && a.Lane == b.Lane && a.OffsetX == b.OffsetX && a.Turn == b.Turn && a.Rise == b.Rise && a.Clearance == b.Clearance && a.Tilt == b.Tilt && a.Back == b.Back && a.Manual == b.Manual,
                    "board " + i + " is where it was: slot, lane, offset, turn, rise, clearance, tilt, order");
            }

            Program.Check(again.All(r => r.SupportHash == arm.Hash && r.Reach == 6f && r.Style == 3 && r.Position.X == kAt.X && r.Yaw == 0.7f), "the support, the reach, the paint and the place are the ones the placing uses");
            Program.Check(again.Select((r, i) => i == 0 || ItemGroups.Compare(again[i - 1], r) <= 0).All(ok => ok), "the records are in the order of ItemGroups.Compare");

            // The lead takes the settings and the lean now in the draft, the others stay as they were picked.
            SignDef guide = SignCatalog.All.First(d => d.Template != null && !d.IsMarking && d.Hash != Real("prohibition").Hash);
            var withGuide = new List<ItemRecord> { Member(Real("prohibition"), 0, arm.Hash, 6f), new ItemRecord(guide.Hash, kAt, 0.7f, arm.Hash, guide.Template.Default().ToString(), 6f, 9u, 1) };
            CarryPick two = CarryPick.Of(withGuide, 1, Entry, out _);
            const string edited = "k=9";
            List<ItemRecord> shown = CarryPick.Records(two, edited, BoardTilt.Radians(3), arm.Hash, 6f, 0, kAt, 0f);
            ItemRecord lead = shown.Find(r => r.TypeHash == guide.Hash);
            Program.Check(lead.Spec == edited && lead.Tilt == BoardTilt.Radians(3), "the lead is drawn with what the draft says now");
            Program.Check(shown.Find(r => r.TypeHash != guide.Hash).Tilt == withGuide[0].Tilt, "the other board keeps its own lean");
        }

        private static void Rings()
        {
            Program.Check(CarryPick.RingRadii(1).Length == 1 && CarryPick.RingRadii(1)[0] == 1.6f, "a single board has the one ring of 1.6 m it always had");
            Program.Check(CarryPick.RingRadii(0).Length == 1 && CarryPick.RingRadii(-3).Length == 1, "no board is still one ring");
            float[] three = CarryPick.RingRadii(3);
            Program.Check(three.Length == 3 && three[0] == 1.6f && three[0] < three[1] && three[1] < three[2], "three boards, three rings, one inside the other");
            Program.Check(CarryPick.RingRadii(4).Length == 4, "four boards, four rings");

            Program.Check(CarryPick.Fits(10, 1, 11) && !CarryPick.Fits(11, 1, 11), "one board fits while there is room for one: as before");
            Program.Check(CarryPick.Fits(8, 3, 11) && !CarryPick.Fits(9, 3, 11), "three boards need room for the two beside the first");
            Program.Check(CarryPick.Fits(0, 0, 1) && !CarryPick.Fits(1, 0, 1), "a count of 0 is a single placing");

            List<ItemRecord> members = ThreeOnAnArm(out _);
            string key = CarryPick.Of(members, 1, Entry, out _).Key;
            Program.Check(key == CarryPick.Of(members, 1, Entry, out _).Key, "the same pole has the same key");
            Program.Check(key != CarryPick.Of(members, 0, Entry, out _).Key, "another lead is another key");
            var moved = new List<ItemRecord>(members);
            ItemRecord m0 = moved[0];
            m0.OffsetX += 1f;
            moved[0] = m0;
            Program.Check(key != CarryPick.Of(moved, 1, Entry, out _).Key, "a board in another place is another key");
            var changed = new List<ItemRecord>(members);
            changed[2] = new ItemRecord(Real("prohibition", 1).Hash, kAt, 0.7f, members[2].SupportHash, null, 6f, 9u, members[2].Slot, members[2].Lane, members[2].OffsetX);
            Program.Check(key != CarryPick.Of(changed, 1, Entry, out _).Key, "another sign is another key");
            SignDef d = Real("prohibition");
            Program.Check(new CarryPick { Boards = { new CarriedBoard { Def = d, Spec = "a" }, new CarriedBoard { Def = d, Slot = 1 } }, Lead = 0 }.Key != new CarryPick { Boards = { new CarriedBoard { Def = d, Spec = "b" }, new CarriedBoard { Def = d, Slot = 1 } }, Lead = 0 }.Key, "the same sign with other settings is another key");
        }

        private static void Hints()
        {
            string zh3 = HintText.Of(new HintInfo { Kind = "pick", Count = 3, Name = "禁止驶入", NameEn = "No entry" }, true), en3 = HintText.Of(new HintInfo { Kind = "pick", Count = 3, Name = "禁止驶入", NameEn = "No entry" }, false);
            Program.Check(zh3.Contains("整杆") && zh3.Contains("3") && en3.Contains("all 3 boards"), "over a pole of three boards with Shift held the line says all three are taken: " + zh3 + " | " + en3);
            string zh1 = HintText.Of(new HintInfo { Kind = "pick", Count = 1, Name = "禁止驶入", NameEn = "No entry" }, true), en1 = HintText.Of(new HintInfo { Kind = "pick", Count = 0, Name = "禁止驶入", NameEn = "No entry" }, false);
            Program.Check(zh1.Contains("禁止驶入") && !zh1.Contains("整杆") && en1.Contains("No entry") && !en1.Contains("pole"), "one board: the line names it, as before: " + zh1 + " | " + en1);
            string bad = HintText.Of(new HintInfo { Kind = "pick", Count = 3, Bad = true }, true);
            Program.Check(bad.Contains("Ctrl") && !bad.Contains("整杆"), "nothing to pick under the cursor says how to pick: " + bad);
            string zh = HintText.Of(new HintInfo { Kind = "pick-mode" }, true), en = HintText.Of(new HintInfo { Kind = "pick-mode" }, false);
            Program.Check(zh.Contains("Shift") && zh.Contains("整杆") && en.Contains("Shift") && en.Contains("whole pole"), "the line of the eyedropper says that Shift takes the whole pole: " + zh + " | " + en);
            foreach (string kind in new[] { "pick", "pick-mode" })
                foreach (bool chinese in new[] { true, false })
                {
                    List<string[]> rows = HintText.Keys(new HintInfo { Kind = kind }, chinese);
                    Program.Check(rows.Any(row => row[0].Contains("Shift") && row[1] == (chinese ? "吸取整杆" : "the whole pole")), kind + ": a key row says Shift and click take the whole pole (" + (chinese ? "zh" : "en") + ")");
                }

            Program.Check(!HintText.Keys(new HintInfo { Kind = "place" }, false).Any(row => row[0].Contains("Shift") && row[1].Contains("pole")), "placing has no such row: it is the eyedropper's");

            // What an item is written with comes back as the board it was: the fields of FieldsOf, put on records as the tool puts them on items, plan the support as the picked pole did.
            List<ItemRecord> members = ThreeOnAnArm(out SupportDef arm);
            CarryPick pole = CarryPick.Of(members, 1, Entry, out _);
            var written = new List<ItemRecord>();
            for (int i = 0; i < pole.Count; i++)
            {
                BoardFields fields = CarryPick.FieldsOf(pole.Boards[i]);
                written.Add(new ItemRecord(pole.Boards[i].Def.Hash, kAt, 0.7f, arm.Hash, pole.Boards[i].Spec, 6f, 77u, fields.Slot, fields.Lane, fields.OffsetX)
                {
                    Turn = fields.Turn, Rise = fields.Rise, Clearance = fields.Clearance, Tilt = BoardTilt.Radians(fields.TiltSteps), Back = fields.Back, Manual = fields.Manual, Style = 3
                });
            }

            written.Sort(ItemGroups.Compare);
            AssemblyPlan was = ItemGroups.PlanOf(members, out List<AssemblyBoard> wasBoards), now = ItemGroups.PlanOf(written, out List<AssemblyBoard> nowBoards);
            Program.Check(was != null && now != null && was.Layout.ArmLength == now.Layout.ArmLength && wasBoards.Count == nowBoards.Count, "the support the written items make has the arm the picked one had");
            for (int i = 0; i < wasBoards.Count; i++)
                Program.Check(wasBoards[i].Slot == nowBoards[i].Slot && wasBoards[i].Lane == nowBoards[i].Lane && wasBoards[i].OffsetX == nowBoards[i].OffsetX && wasBoards[i].Tilt == nowBoards[i].Tilt && wasBoards[i].Turn == nowBoards[i].Turn
                    && wasBoards[i].Rise == nowBoards[i].Rise && wasBoards[i].Clearance == nowBoards[i].Clearance && wasBoards[i].Manual == nowBoards[i].Manual, "written back, board " + i + " is the board it was");
            Program.Check(ChunkBuilder.PartsOfGroup(written).VertexCount == ChunkBuilder.PartsOfGroup(members).VertexCount, "and the whole is drawn the same");
        }

        private static void Letting()
        {
            List<ItemRecord> members = ThreeOnAnArm(out _);
            CarryPick pole = CarryPick.Of(members, 1, Entry, out _);
            var carrying = new Carrying();
            Program.Check(!carrying.Active && carrying.Pole == null, "nothing is carried at the start");
            carrying.Take(pole);
            Program.Check(carrying.Active && carrying.Pole.Count == 3, "a pole taken is carried");
            carrying.ModeChanged(0);
            Program.Check(carrying.Active, "placing again goes on with it");
            carrying.ChoseEntry();
            Program.Check(!carrying.Active, "choosing another entry lets it go");
            carrying.Take(pole);
            carrying.ChoseSupport();
            Program.Check(!carrying.Active, "choosing another support lets it go");
            foreach (int mode in new[] { 1, 2, StylePick.Mode })
            {
                carrying.Take(pole);
                carrying.ModeChanged(mode);
                Program.Check(!carrying.Active, "mode " + mode + " lets it go");
            }

            carrying.Take(pole);
            carrying.Left();
            Program.Check(!carrying.Active, "leaving the tool (Esc, right click) lets it go");
            carrying.Take(pole);
            carrying.Take(null);
            Program.Check(!carrying.Active, "a pick of one board lets the pole before it go");
            carrying.Take(new CarryPick { Boards = { new CarriedBoard { Def = Real("prohibition") } }, Lead = 0 });
            Program.Check(!carrying.Active, "a pole of one board is not carried");
        }
    }
}
