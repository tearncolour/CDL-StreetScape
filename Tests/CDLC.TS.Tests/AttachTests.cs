using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of putting a sign on a support that has boards on it, and of moving a board on it (the arrow keys): the rules and the arithmetic of the room.</summary>
    internal static class AttachTests
    {
        public static void RunAll()
        {
            Program.Run("attach: large boards and the limit of four", Rules);
            Program.Run("attach: the height of a stack on a post", PostHeight);
            Program.Run("attach: the standard's order and the player's order on a post", PostOrder);
            Program.Run("attach: free intervals on a beam", Intervals);
            Program.Run("attach: the arrow keys move along the free places", Stepping);
            Program.Run("attach: a board on a beam stands clear of the others and the support", BeamPlace);
            Program.Run("attach: the boards that are side by side are pinned where they stand", BeamPinned);
            Program.Run("attach: an arm grows to take a board and a gantry does not", ArmGrows);
            Program.Run("attach: moving a board", Moving);
            Program.Run("attach: the order flag changes how a stack is read", ManualOrder);
            Program.Run("attach: what comes back is what the build draws", BuildAgrees);
            Program.Run("attach: the next-exit board goes under the 500 m exit board on a post (Fig. 277)", NextExit);
            Program.Run("gantry: up to twelve boards and the length is worked out", GantryMany);
            Program.Run("gantry: the beam grows only when it is asked to or has no room", GantryGrowth);
            Program.Run("hint: every kind has text in both languages, with the numbers", HintTexts);
            Program.Run("co-location: nothing is said about what stands on one support (the notes of GB 5768.2 4.8.3 to 4.8.5 are gone)", NoCoLocationNotes);
            Program.Run("pick: what is near the line of the cursor", Nearness);
        }

        // 4 Nov (the owner: "把这个和类似的删了"): the standard's notes about the signs on one support (4.8.3 to 4.8.5) are not made any more, in the editor or in the line of help.
        private static void NoCoLocationNotes()
        {
            var warnings = new List<AssemblyBoard> { R("gb.warn.02", 0, 0), R("gb.warn.03", 1, 1), R("gb.warn.04", 2, 2) };
            AttachResult four = Attach.Evaluate(new AttachRequest { Existing = warnings, Support = Pole, Board = Real("gb.warn.11") });
            Program.Check(four.Ok, "the fourth warning sign still joins the post: " + four.Reason);
            var guide = new List<AssemblyBoard> { R("gb.guide9.29", 0, 0, false) };
            AttachResult mixed = Attach.Evaluate(new AttachRequest { Existing = guide, Support = Gantry, Reach = 14f, Board = Real("gb.prohib.05") });
            Program.Check(mixed.Ok, "a prohibition sign beside a guide board on a gantry still joins: " + mixed.Reason);
            foreach (bool zh in new[] { true, false })
            {
                string line = HintText.Of(new HintInfo { Kind = "attach", Count = 3, Left = 1f, Name = "x" }, zh) + " " + HintText.Of(new HintInfo { Kind = "attach-post", Count = 3, Left = 1f, Name = "x" }, zh)
                    + " " + HintText.Of(new HintInfo { Kind = "attach", Hung = true, Span = 14f, Name = "x" }, zh);
                Program.Check(!line.Contains("4.8") && !line.Contains("⚠"), (zh ? "zh" : "en") + ": the line of help has no note of the standard: " + line);
            }
        }

        private static SignInstance Real(string choiceId)
        {
            SignChoice choice = SignCatalog.ChoiceById(choiceId);
            Program.Check(choice != null, "the catalogue has " + choiceId);
            return SignInstances.Of(choice.Def, choice.Spec);
        }

        private static AssemblyBoard R(string choiceId, int member, int slot, bool manual = false) => new AssemblyBoard { Instance = Real(choiceId), Member = member, Slot = slot, Manual = manual };

        private static void NextExit()
        {
            SignInstance advance = Real("gb.exit.adv.500"), advance2km = Real("gb.exit.adv.2km"), direction = Real("gb.exit.dir"), next = Real("gb.exit.next.h"), nextV = Real("gb.exit.next.v");
            Program.Check(Attach.IsLarge(advance.Def) && advance.Def.Height > Attach.MaxPostStack, "the exit board is large and higher than a stack: " + advance.Def.Width + " x " + advance.Def.Height);
            Program.Check(Attach.IsExitAdvance500(advance) && !Attach.IsExitAdvance500(advance2km) && !Attach.IsExitAdvance500(direction) && !Attach.IsExitAdvance500(next), "only the 500 m advance board is the one");
            Program.Check(Assembly.IsNextExit(next.Def) && Assembly.IsNextExit(nextV.Def) && !Assembly.IsNextExit(advance.Def), "路54 is told by its id");
            SupportDef doublePole = SupportCatalog.ById(SupportCatalog.DoublePole), single = SupportCatalog.ById(SupportCatalog.SinglePole);

            // The next-exit board under the 500 m board, on two posts and on one (Fig. 277 a and b).
            foreach (SupportDef support in new[] { doublePole, single })
            {
                var existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0) };
                AttachResult result = Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Board = next });
                Program.Check(result.Ok && result.Count == 2, support.Id + ": the next-exit board joins the 500 m board: " + result.Reason);
                Program.Check(result.Boards[1].Member == result.NewMember, support.Id + ": and is below it");
                SignParts parts = SignBuilder.BuildAssembly(result.Boards, support, 0f);
                parts.Faces[0].Mesh.Bounds(out CDLC.TS.Snap.Vec3 topLo, out _);
                parts.Faces[1].Mesh.Bounds(out CDLC.TS.Snap.Vec3 lowLo, out CDLC.TS.Snap.Vec3 lowHi);
                Program.Check(lowLo.Y < topLo.Y && lowLo.Y >= parts.Layout.BottomClearance - 1e-3f, support.Id + ": the small board is under the big one, at the clearance");
                Program.Check(parts.Layout.PoleTop > topLo.Y + advance.Def.Height * 0.5f && parts.Layout.PoleTop < topLo.Y + advance.Def.Height, support.Id + ": the posts end behind the exit board, in its upper half");
            }

            // The other way round: the exit board put on a post that has the next-exit board on it.
            AttachResult reverse = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.next.h", 0, 0) }, Support = doublePole, Board = advance });
            Program.Check(reverse.Ok && reverse.Boards[0].Instance.Def.Id == Assembly.ExitAdvanceId && reverse.Boards[1].Instance.Def.Id == Assembly.NextExitId, "whichever is put on first, the next-exit board is below: " + reverse.Reason);

            // Without a support named: the planner takes a post of the exit board's size; it still works.
            AttachResult planned = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0) }, Board = nextV });
            Program.Check(planned.Ok, "the two-line next-exit board too, with the planner's support: " + planned.Reason);

            // Only that pair: not the 2 km board, not the exit direction board, not a third board, not two exit boards.
            Program.Check(!Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.2km", 0, 0) }, Support = doublePole, Board = next }).Ok, "not under the 2 km board");
            Program.Check(!Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.dir", 0, 0) }, Support = doublePole, Board = next }).Ok, "not under an exit direction board");
            Program.Check(!Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0), R("gb.exit.next.h", 1, 1) }, Support = doublePole, Board = Inst(Small) }).Ok, "a third board on the pair is turned away");
            AttachResult twoExits = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0) }, Support = doublePole, Board = advance });
            Program.Check(!twoExits.Ok && twoExits.Reason == AttachReason.Large, "two 500 m boards on a post are still two large boards: " + twoExits.Reason);
            Program.Check(!Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0) }, Support = doublePole, Board = Inst(Small) }).Ok, "a small board does not join the exit board on a post");

            // On a gantry nothing changes: any size, side by side.
            Program.Check(Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { R("gb.exit.adv.500", 0, 0) }, Support = Gantry, Reach = 20f, Board = next }).Ok, "on a gantry they hang side by side");

            // The order: 路54 is the last of the guide boards whichever slot it has, and a board of another kind (a mandatory sign) still comes before the guide boards.
            var mixed = new List<AssemblyBoard> { R("gb.exit.next.h", 0, 0), R("gb.exit.adv.500", 1, 1) };
            List<AssemblyBoard> ordered = Assembly.Order(mixed);
            Program.Check(ordered[0].Instance.Def.Id == Assembly.ExitAdvanceId && ordered[1].Instance.Def.Id == Assembly.NextExitId, "the order puts 路54 under the exit board");
            Program.Check(Assembly.Rank(next.Def) > Assembly.Rank(advance.Def) && Assembly.Rank(advance.Def) == 3, "its rank is after the other guide boards");
            Program.Check(HintText.Of(new HintInfo { Kind = "refuse-large" }, true).Contains("1.4 m") && HintText.Of(new HintInfo { Kind = "refuse-large" }, false).Contains("1.4 m"), "the refusal names the size");
        }

        private static void GantryMany()
        {
            // Twelve boards of 1.2 m one after the other on a gantry that starts 14 m long: each goes on, nothing overlaps, every board is inside the beam's bounds, the beam is made as long as
            // the boards need and no longer than the longest.
            var boards = new List<AssemblyBoard>();
            float reach = 14f;
            bool grewOnce = false;
            AttachResult last = null;
            for (int i = 0; i < 12; i++)
            {
                AttachResult r = Attach.Evaluate(new AttachRequest { Existing = boards, Support = Gantry, Reach = reach, Board = Inst(Round) });
                Program.Check(r.Ok, "board " + (i + 1) + " of 12 goes on the gantry: " + r.Reason + " (span " + reach + ")");
                if (!r.Ok)
                    return;
                Program.Check(r.MaxBoards == 12 && r.Count == i + 1, "the limit is twelve and the count " + (i + 1));
                boards = r.Boards;
                reach = r.Reach > 0f ? r.Reach : reach;
                grewOnce |= r.Grew;
                last = r;
                AssemblyPlan plan = Assembly.Plan(Assembly.Order(boards), Gantry, reach);
                Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "after board " + (i + 1) + " nothing overlaps");
                Program.Near(plan.Layout.ArmLength, r.Span, 1e-3f, "the span the result says is the span the layout makes (board " + (i + 1) + ")");
                foreach (Placement p in plan.Placed)
                {
                    Assembly.LaneBounds(plan.Layout, p.Def.Width * 0.5f, out float lo, out float hi);
                    Program.Check(p.Cx >= lo - 1e-3f && p.Cx <= hi + 1e-3f, "board " + p.Board.Member + " is inside the beam: " + p.Cx + " in [" + lo + ", " + hi + "]");
                }
            }

            Program.Check(grewOnce, "the beam was made longer on the way");
            AssemblyPlan final = Assembly.Plan(Assembly.Order(boards), Gantry, reach);
            float needed = 12 * 1.2f + 11 * Assembly.SideGap + 2f * Assembly.GantryClear;
            Program.Check(final.Layout.ArmLength + 0.01f >= needed && final.Layout.ArmLength <= SupportLayouts.MaxSpan, $"twelve boards of 1.2 m need {needed:0.0} m and the beam is {final.Layout.ArmLength:0.0} m");
            Program.Check(last.Span <= SupportLayouts.MaxSpan, "never past the longest beam");

            // The thirteenth is turned away, with the limit.
            AttachResult thirteenth = Attach.Evaluate(new AttachRequest { Existing = boards, Support = Gantry, Reach = reach, Board = Inst(Round) });
            Program.Check(!thirteenth.Ok && thirteenth.Reason == AttachReason.Full && thirteenth.MaxBoards == 12 && thirteenth.Count == 0, "a thirteenth: " + thirteenth.Reason);

            // And built: twelve faces, each can be picked, and the beam is the length the plan says.
            SignParts parts = SignBuilder.BuildAssembly(boards, Gantry, reach);
            Program.Check(parts.Faces.Count == 12, "twelve faces: " + parts.Faces.Count);
            var picked = new HashSet<int>();
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name != "board")
                    continue;
                var at = new CDLC.TS.Snap.Vec3((shape.Min.X + shape.Max.X) * 0.5f, (shape.Min.Y + shape.Max.Y) * 0.5f, 6f);
                if (Picking.Pick(parts.Picks, CDLC.TS.Snap.Vec3.Zero, 0f, at, new CDLC.TS.Snap.Vec3(at.X, at.Y, -6f), out _, out PickShape hit) && hit.Name == "board" && hit.Member == shape.Member)
                    picked.Add(shape.Member);
            }

            Program.Check(picked.Count == 12, "every one of the twelve is hit before the beam: " + picked.Count);
            parts.Faces[0].Mesh.Bounds(out CDLC.TS.Snap.Vec3 lo0, out _);
            Program.Check(lo0.X > 0f, "and none is behind a post");

            // The same gantry with a mix of sizes (the exit boards are 4 m wide): a few of them fill it, the beam is made as long as they need.
            var mixed = new List<AssemblyBoard>();
            float mixedReach = 14f;
            SignInstance[] set = { Real("gb.exit.adv.500"), Real("gb.exit.dir"), Real("gb.exit.adv.500"), Real("gb.exit.dir") };
            foreach (SignInstance board in set)
            {
                AttachResult r = Attach.Evaluate(new AttachRequest { Existing = mixed, Support = Gantry, Reach = mixedReach, Board = board });
                Program.Check(r.Ok, "an exit board on a gantry of " + mixedReach + " m: " + r.Reason);
                if (!r.Ok)
                    break;
                mixed = r.Boards;
                mixedReach = r.Reach > 0f ? r.Reach : mixedReach;
            }

            Program.Check(Assembly.Overlaps(Assembly.Plan(Assembly.Order(mixed), Gantry, mixedReach).Placed, true).Count == 0, "large boards do not overlap either");
        }

        private static void GantryGrowth()
        {
            // One board of 1.2 m at 7 m on a 20 m gantry.
            var one = new List<AssemblyBoard> { B(Round, 0, 0, Attach.LaneHand, 7f) };

            // Asked inside the beam, by the mouse: it stays as long as it is.
            AttachResult within = Attach.Evaluate(new AttachRequest { Existing = one, Support = Gantry, Reach = 20f, Board = Inst(Small), Offset = 12f });
            Program.Check(within.Ok && !within.Grew && Math.Abs(within.Reach - 20f) < 1e-3f && Math.Abs(within.Span - 20f) < 1e-3f, $"inside the beam: no growth ({within.Reach}, {within.Span})");

            // Asked beyond the end by the mouse: kept inside (the posts stand where the road's edges are).
            AttachResult beyond = Attach.Evaluate(new AttachRequest { Existing = one, Support = Gantry, Reach = 20f, Board = Inst(Small), Offset = 35f });
            Assembly.LaneBounds(Assembly.Plan(beyond.Boards, Gantry, 20f).Layout, 0.4f, out _, out float hiNow);
            Program.Check(beyond.Ok && !beyond.Grew && beyond.Offset <= hiNow + 1e-3f && Math.Abs(beyond.Span - 20f) < 1e-3f, "beyond the end, without the keys: kept inside and not longer");

            // With the keys (Grow): the board goes where it is asked and the beam is as long as it then needs, in half metres.
            AttachResult keys = Attach.Evaluate(new AttachRequest { Existing = one, Support = Gantry, Reach = 20f, Board = Inst(Small), Offset = 30f, Grow = true });
            Program.Check(keys.Ok && keys.Grew, "with the keys it grows: " + keys.Reason);
            Program.Near(keys.Offset, 30f, 1e-3f, "the board is where it was taken");
            float need = 30f + 0.4f + Assembly.GantryClear;
            Program.Near(keys.Reach, (float)Math.Ceiling(need * 2f) / 2f, 1e-3f, "the beam is as long as it needs, in half metres: " + keys.Reach);
            Program.Near(keys.Span, keys.Reach, 1e-3f, "and the layout makes it that long");
            Assembly.LaneBounds(Assembly.Plan(keys.Boards, Gantry, keys.Reach).Layout, 0.4f, out _, out float hiKeys);
            Program.Check(keys.Offset <= hiKeys + 1e-3f, "the board is inside the longer beam");

            // Taken as far as it goes: the longest beam, no further.
            AttachResult far = Attach.Evaluate(new AttachRequest { Existing = one, Support = Gantry, Reach = 20f, Board = Inst(Small), Offset = 99f, Grow = true });
            Program.Check(far.Ok && far.Reach <= SupportLayouts.MaxSpan && far.Span <= SupportLayouts.MaxSpan + 1e-3f, "never past the longest beam: " + far.Reach);
            Program.Check(far.Offset < 99f, "and the board is kept inside it: " + far.Offset);

            // The beam is not shortened by a board that is nearer: the span that is kept is the one asked for.
            AttachResult stays = Attach.Evaluate(new AttachRequest { Existing = one, Support = Gantry, Reach = 25f, Board = Inst(Small), Offset = 9f, Grow = true });
            Program.Check(stays.Ok && !stays.Grew && Math.Abs(stays.Span - 25f) < 1e-3f, "a board inside the beam does not change it, even with the keys: " + stays.Span);

            // A moved board (editing) may go past the end, and then the beam grows.
            var others = new List<AssemblyBoard> { B(Round, 0, 0, Attach.LaneHand, 7f) };
            AttachResult moved = Attach.Evaluate(new AttachRequest { Existing = others, Support = Gantry, Reach = 20f, Board = Inst(Small), Offset = 24f, Moving = true, Member = 1, Slot = 1, Grow = true });
            Program.Check(moved.Ok && moved.Grew && moved.NewMember == 1 && Math.Abs(moved.Offset - 24f) < 1e-3f, "a board that is moved past the end lengthens the beam: " + moved.Reach);

            // An arm grows as it did, with no keys needed.
            AttachResult arm = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { B(Small, 0, 0, Attach.LaneHand, 3.2f) }, Support = Arm, Board = Inst(Small), Offset = 8f });
            Program.Check(arm.Ok && arm.Grew && Math.Abs(arm.Offset - 8f) < 1e-3f, "an arm still grows when a board is asked for beyond its end");

            // The room for the first board on a fresh gantry: counted against the longest beam, with the limit in the result.
            AttachResult first = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard>(), Support = Gantry, Reach = 14f, Board = Inst(Round) });
            Program.Check(first.Ok && first.MaxBoards == 12 && first.SpaceLeft > 30f, "the first board: room " + first.SpaceLeft + " of the longest beam");
        }

        private static void HintTexts()
        {
            foreach (string kind in HintText.Kinds)
            {
                foreach (bool zh in new[] { true, false })
                {
                    var info = new HintInfo { Kind = kind, Count = 3, Left = 1.25f, Need = 0.8f, X = 12.5f, Name = "示1" };
                    string text = HintText.Of(info, zh);
                    Program.Check(text.Length > 5, kind + (zh ? " in Chinese" : " in English") + " has text");
                    Program.Check(!text.Contains("{") && !text.Contains("}"), kind + ": nothing is left to fill in: " + text);
                    // 0.12.47: the keys of the state, the way out last; none of them named in the line of help any more.
                    List<string[]> keys = HintText.Keys(info, zh);
                    Program.Check(keys.Count > 0 && keys[keys.Count - 1][0].Contains("Esc") && keys.TrueForAll(k => k.Length == 2 && k[0].Length > 0 && k[1].Length > 0 && !k[1].Contains("{")), kind + ": its keys, Esc last");
                    Program.Check(!text.Contains("Esc") && !text.Contains("Backspace") && !text.Contains("[ ]") && !text.Contains(", ."), kind + ": no keys in the line: " + text);
                }
            }

            Program.Check(HintText.Keys(new HintInfo { Kind = "nothing" }, true).Count == 0, "a kind that is not known has no keys");

            Program.Check(HintText.Of(default, true) == "" && HintText.Of(new HintInfo { Kind = "nothing" }, false) == "", "no kind, or one that is not known, is no text");
            Program.Check(HintText.Of(new HintInfo { Kind = "attach", Hung = true, Left = 1.25f, X = 12.5f }, false).Contains("12.5 m") && HintText.Of(new HintInfo { Kind = "attach", Hung = true, Left = 1.25f, X = 12.5f }, false).Contains("1.3 m") || HintText.Of(new HintInfo { Kind = "attach", Hung = true, Left = 1.25f, X = 12.5f }, false).Contains("1.2 m"), "a beam: the place and the room are in the text");
            string post = HintText.Of(new HintInfo { Kind = "attach", Count = 3, Left = 0.9f, Manual = true }, true), standard = HintText.Of(new HintInfo { Kind = "attach", Count = 3, Left = 0.9f }, true);
            Program.Check(post.Contains("3/4") && post.Contains("手动顺序") && standard.Contains("标准顺序") && post.Contains("0.9 m"), "a post: how many boards, the room, and whose order");
            Program.Check(HintText.Of(new HintInfo { Kind = "refuse-height", Left = 0.5f }, true).Contains("3.3") && HintText.Of(new HintInfo { Kind = "refuse-large" }, true).Contains("1.4"), "the limits are in the texts");
            Program.Check(HintText.Of(new HintInfo { Kind = "refuse-space", Need = 3f, Left = 1f }, false).Contains("3.0 m"), "the width that is needed");
            string full = HintText.Of(new HintInfo { Kind = "refuse-full", Count = 12, Max = 12 }, true), fullEn = HintText.Of(new HintInfo { Kind = "refuse-full", Count = 4, Max = 4 }, false);
            Program.Check(full.Contains("12") && full.Contains("4") && fullEn.Contains("4 boards") && fullEn.Contains("12 on a gantry"), "the refusal names both limits: " + full);
            string beam = HintText.Of(new HintInfo { Kind = "attach", Hung = true, Span = 23.5f, Grew = true, Left = 20f, X = 21f }, true), steady = HintText.Of(new HintInfo { Kind = "attach", Hung = true, Span = 14f, Grew = false, Left = 20f, X = 6f }, true);
            Program.Check(beam.Contains("23.5") && beam.Contains("已加长") && !steady.Contains("已加长"), "the new length of the beam is in the hint when it was made longer: " + beam);
            Program.Check(HintText.Of(new HintInfo { Kind = "attach", Hung = true, Span = 23.5f, Grew = true }, false).Contains("lengthened"), "in English too");
            Program.Check(HintText.Of(new HintInfo { Kind = "place", Bad = true }, false).StartsWith("Not allowed"), "a place that is not allowed says so first");
            bool KeyOf(HintInfo h, string key) => HintText.Keys(h, true).Exists(k => k[0] == key);
            Program.Check(KeyOf(new HintInfo { Kind = "move", Hung = true }, "← →") && KeyOf(new HintInfo { Kind = "move", Count = 3 }, "↑ ↓"), "the keys are named for the support that is edited");
            Program.Check(KeyOf(new HintInfo { Kind = "attach", Hung = true }, "← →") && KeyOf(new HintInfo { Kind = "attach", Count = 2 }, "↑ ↓") && !KeyOf(new HintInfo { Kind = "move", Count = 1 }, "↑ ↓"),
                "a beam moves along with the left and right arrows, a post's order with up and down; a board alone has no order");
            // The hints of the road markings: the length and the pieces are in the line of a run that was started, and a place that is not allowed says so first.
            foreach (bool zh in new[] { true, false })
            {
                string run = HintText.Of(new HintInfo { Kind = "mark-run-end", Count = 4, Left = 187.5f, Need = 3000f }, zh);
                Program.Check(run.Contains("187.5") && run.Contains("4"), "a started line says its length, its pieces and the longest: " + run);
                Program.Check(HintText.Of(new HintInfo { Kind = "mark-run-end", Bad = true }, zh).Length > HintText.Of(new HintInfo { Kind = "mark-run-end" }, zh).Length, "a line that cannot end here says so first");
                Program.Check(HintText.Of(new HintInfo { Kind = "mark-stamp", Bad = true }, zh).Length > HintText.Of(new HintInfo { Kind = "mark-stamp" }, zh).Length, "a marking that cannot be placed says so first");
                Program.Check(HintText.Of(new HintInfo { Kind = "mark-run-start" }, zh).Length > 4 && HintText.Of(new HintInfo { Kind = "mark-run-road" }, zh).Length > 4, "the start and the road hints have text");
            }

            // The hints of the area markings say the numbers and the state.
            foreach (bool zh in new[] { true, false })
            {
                string started = HintText.Of(new HintInfo { Kind = "mark-poly", Count = 5 }, zh), closing = HintText.Of(new HintInfo { Kind = "mark-poly", Count = 5, Manual = true }, zh);
                Program.Check(started.Contains("5") && started.Contains(MarkingPolygon.MaxPoints.ToString()) && closing != started, "the island's hint says how many corners, and when a click closes it: " + closing);
                Program.Check(HintText.Of(new HintInfo { Kind = "mark-poly" }, zh) != started && HintText.Of(new HintInfo { Kind = "mark-poly", Count = 2, Bad = true }, zh).Length > HintText.Of(new HintInfo { Kind = "mark-poly", Count = 2 }, zh).Length, "the first corner has its own words; a refused corner says so");
                string exit = HintText.Of(new HintInfo { Kind = "mark-gore-fork", Manual = true, Hung = true, Left = 35.5f, Need = 4f, X = 62f }, zh), entry = HintText.Of(new HintInfo { Kind = "mark-gore-fork", Manual = false, Hung = false, Left = 35.5f, Need = 4f, X = 62f }, zh);
                Program.Check(exit.Contains("35.5") && exit.Contains("4.0") && exit != entry, "the fork's hint says the gore's length and width and the lead's, exit or entry: " + exit);
                Program.Check(HintText.Of(new HintInfo { Kind = "mark-gore-free" }, zh).Length > 6, "and away from a fork");
            }

            // The limits in the texts are the ones the rules use.
            Program.Check(Attach.MaxPostStack == 3.3f && Attach.LargeBoard == 1.4f && Assembly.MaxBoards == 4, "3.3 m, 1.4 m and 4 are the limits the texts name");
        }

        private static void Nearness()
        {
            var a = new CDLC.TS.Snap.Vec3(0f, 0f, 0f);
            var b = new CDLC.TS.Snap.Vec3(0f, 0f, 100f);
            Program.Near(Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(3f, 0f, 50f), a, b), 3f, 1e-4f, "beside the middle");
            Program.Near(Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(0f, 4f, -3f), a, b), 5f, 1e-4f, "before the start: the distance to the start");
            Program.Near(Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(0f, 0f, 110f), a, b), 10f, 1e-4f, "past the end");
            Program.Near(Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(1f, 2f, 3f), a, a), (float)Math.Sqrt(14.0), 1e-4f, "a segment of no length is a point");
            Program.Check(Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(60f, 0f, 20f), a, b) < 70f && Picking.DistanceToSegment(new CDLC.TS.Snap.Vec3(80f, 0f, 20f), a, b) > 70f, "70 m is the radius within which a support is looked at");
        }

        private static SignDef Make(string id, string group, float w, float h, float extraTop = 0f) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = group, Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, ExtraTop = extraTop, TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f)
        };

        private static readonly SignDef Small = Make("t.small", "prohibition", 0.8f, 0.8f), Round = Make("t.round", "prohibition", 1.2f, 1.2f), Aux = Make("t.aux", "auxiliary", 0.8f, 0.4f),
            Warn = Make("t.warn", "warning", 0.9f, 0.8f), Big = Make("t.big", "guide", 4.0f, 2.0f), Tall = Make("t.tall", "mandatory", 0.8f, 1.5f), Wide = Make("t.wide", "mandatory", 1.4f, 0.8f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int slot, int lane = 0, float x = 0f, bool manual = false) =>
            new AssemblyBoard { Instance = Inst(def), Member = member, Slot = slot, Lane = lane, OffsetX = x, Manual = manual };

        private static AttachResult Eval(List<AssemblyBoard> existing, SignDef add, SupportDef support = null, float reach = 0f, int index = -1, float offset = float.NaN, bool moving = false, int member = -1) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Reach = reach, Board = Inst(add), Index = index, Offset = offset, Moving = moving, Member = member });

        private static SupportDef Pole => SupportCatalog.ById(SupportCatalog.SinglePole);
        private static SupportDef Gantry => SupportCatalog.ById(SupportCatalog.Gantry);
        private static SupportDef Arm => SupportCatalog.ById(SupportCatalog.Cantilever);

        private static void Rules()
        {
            Program.Check(!Attach.IsLarge(Small) && !Attach.IsLarge(Round) && !Attach.IsLarge(Wide) && !Attach.IsLarge(Aux), "the boards of the fastest band (1.2 m), and one 1.4 m wide, are not large");
            Program.Check(Attach.IsLarge(Big) && Attach.IsLarge(Tall), "an exit board, and a board 1.5 m high, are large");
            Program.Check(!Attach.IsLarge(Make("edge", "x", 1.4f, 1.4f)) && Attach.IsLarge(Make("over", "x", 1.41f, 1f)), "1.4 m is the limit");

            // A large board does not go on a post (nor on a stack that has one); on a beam it does.
            AttachResult onPost = Eval(new List<AssemblyBoard> { B(Small, 0, 0) }, Big, Pole);
            Program.Check(!onPost.Ok && onPost.Reason == AttachReason.Large, "a large board on a post: " + onPost.Reason);
            AttachResult overBig = Eval(new List<AssemblyBoard> { B(Big, 0, 0) }, Small, Pole);
            Program.Check(!overBig.Ok && overBig.Reason == AttachReason.Large, "a small one on a post that carries a large one: " + overBig.Reason);
            AttachResult onBeam = Eval(new List<AssemblyBoard> { B(Small, 0, 0) }, Big, Gantry, 20f);
            Program.Check(onBeam.Ok, "a large board on a gantry is fine: " + onBeam.Reason);

            // Four at the most, on any support.
            var four = new List<AssemblyBoard> { B(Aux, 0, 0), B(Aux, 1, 1), B(Aux, 2, 2), B(Aux, 3, 3) };
            AttachResult fifth = Eval(four, Aux, Pole);
            Program.Check(!fifth.Ok && fifth.Reason == AttachReason.Full && fifth.Count == 0, "a fifth board on a post: " + fifth.Reason);
            Program.Check(Eval(four, Aux, Gantry, 20f).Ok, "a fifth board on a gantry is fine: it carries twelve");
            Program.Check(Eval(four, Aux, Arm, 0f).Reason == AttachReason.Full, "a fifth board on an arm is not: four");
            Program.Check(fifth.MaxBoards == 4 && Eval(four, Aux, Gantry, 20f).MaxBoards == 12, "the result says the limit of the support it was asked about");
            Program.Check(Eval(new List<AssemblyBoard> { B(Aux, 0, 0), B(Aux, 1, 1), B(Aux, 2, 2) }, Aux, Pole).Ok, "a fourth board on a post");

            // A moving board is not held to the rules for a new one.
            AttachResult moved = Eval(new List<AssemblyBoard> { B(Small, 0, 0) }, Big, Pole, moving: true);
            Program.Check(moved.Ok || moved.Reason != AttachReason.Large, "a board that is moved is not turned away for being large");

            // The planner chooses the support when none is named, from the whole.
            AttachResult planned = Eval(new List<AssemblyBoard> { B(Small, 0, 0) }, Aux);
            Program.Check(planned.Ok && planned.Support != null && !planned.Hung, "no support named: the planner's, which for small boards is a post");
        }

        private static void PostHeight()
        {
            // Two boards of 1.2 m (2.48 m with the gap) and a warning board of 0.8 (0.88 more) = 3.36 m: over the stack's limit of 3.3; an auxiliary board of 0.4 instead (2.96 m) passes.
            var two = new List<AssemblyBoard> { B(Round, 0, 0), B(Round, 1, 1) };
            AttachResult third = Eval(two, Warn, Pole);
            Program.Check(Eval(two, Aux, Pole).Ok, "two of 1.2 m and an auxiliary board of 0.4 m (2.96 m) fit");
            float stack = Assembly.TotalDef(Assembly.Order(new List<AssemblyBoard> { B(Round, 0, 0), B(Round, 1, 1), B(Warn, 2, 2) }), false).Height;
            Program.Check(stack > Attach.MaxPostStack == !third.Ok, $"the stack of {stack:0.00} m against the limit of {Attach.MaxPostStack} m");
            Program.Check(!third.Ok && third.Reason == AttachReason.NoHeight, "three that are too high: " + third.Reason);
            Program.Near(third.SpaceBefore, Attach.MaxPostStack - Assembly.TotalDef(Assembly.Order(two), false).Height, 1e-4f, "the room there was is the limit less the stack");
            Program.Check(third.SpaceLeft == 0f, "none is left when it does not fit");

            var oneSmall = new List<AssemblyBoard> { B(Small, 0, 0) };
            AttachResult second = Eval(oneSmall, Warn, Pole);
            Program.Check(second.Ok && second.Count == 2, "two small boards fit: " + second.Reason);
            float expectLeft = Attach.MaxPostStack - Assembly.TotalDef(second.Boards, false).Height;
            Program.Near(second.SpaceLeft, expectLeft, 1e-4f, "the room that is left is the limit less the stack: " + second.SpaceLeft);
            Program.Check(second.SpaceLeft > 0f && second.SpaceLeft < Attach.MaxPostStack, "and it is part of the limit");

            // Four boards of 0.4 m: 1.6 + 3 gaps = 1.84 m.
            var fourSmall = new List<AssemblyBoard> { B(Aux, 0, 0), B(Aux, 1, 1), B(Aux, 2, 2) };
            AttachResult fourth = Eval(fourSmall, Aux, Pole);
            Program.Check(fourth.Ok && fourth.Count == 4, "four small boards: " + fourth.Reason);

            // Four of 0.8 m: 3.2 + 0.24 = 3.44 m: over the limit even though four are allowed.
            var threeSmall = new List<AssemblyBoard> { B(Small, 0, 0), B(Small, 1, 1), B(Small, 2, 2) };
            AttachResult tooHigh = Eval(threeSmall, Small, Pole);
            Program.Check(!tooHigh.Ok && tooHigh.Reason == AttachReason.NoHeight, "four boards of 0.8 m are too high for a post: " + tooHigh.Reason);
        }

        private static List<int> Members(AttachResult r)
        {
            var list = new List<int>();
            foreach (AssemblyBoard b in r.Boards)
                list.Add(b.Member);
            return list;
        }

        private static void PostOrder()
        {
            SignDef mand = Make("t.mand", "mandatory", 0.8f, 0.8f), proh = Make("t.proh", "prohibition", 0.8f, 0.8f);

            // The standard's order: a prohibition sign goes above the mandatory one that was there first (4.8.6), the player has not asked for anything.
            var existing = new List<AssemblyBoard> { B(mand, 0, 0) };
            AttachResult auto = Eval(existing, proh, Pole);
            Program.Check(auto.Ok && !auto.Manual && auto.Index == 0 && auto.Boards[0].Member == auto.NewMember, "automatic: the prohibition sign is on top: index " + auto.Index);

            // The player puts it below (index 1): the order is theirs from then on.
            AttachResult below = Eval(existing, proh, Pole, index: 1);
            Program.Check(below.Ok && below.Manual && below.Index == 1 && below.Boards[1].Member == below.NewMember && below.Boards[0].Member == 0, "index 1: below the mandatory sign");
            Program.Check(below.Boards[0].Slot == 0 && below.Boards[1].Slot == 1 && below.Boards[0].Manual && below.Boards[1].Manual, "the slots are the order, and every board carries the flag");

            // Out of range: the end of the stack, or the top.
            Program.Check(Eval(existing, proh, Pole, index: 9).Index == 1, "an index too large is the bottom");
            Program.Check(Eval(existing, proh, Pole, index: 0).Index == 0, "0 is the top");

            // A stack the player has put in order already: a new board with no index goes to the end, whatever its rank.
            var ordered = new List<AssemblyBoard> { B(mand, 0, 0, manual: true), B(proh, 1, 1, manual: true) };
            AttachResult appended = Eval(ordered, Make("t.warn2", "warning", 0.8f, 0.8f), Pole);
            Program.Check(appended.Ok && appended.Manual && appended.Index == 2 && appended.Boards[2].Member == appended.NewMember, "a stack in the player's order takes a new board at the bottom: " + appended.Index);
            Program.Check(Members(appended)[0] == 0 && Members(appended)[1] == 1, "and keeps the order it had (a mandatory sign above a prohibition sign)");

            // The numbers: the new board is the next one, or the one asked for.
            Program.Check(auto.NewMember == 1 && Eval(existing, proh, Pole, member: 7).NewMember == 7, "the new board's member");
        }

        private static void Intervals()
        {
            var one = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(10f, 1f) };
            // A board of width 1 next to a board of width 1 at 10 m: its middle may not be within (1 + 1) / 2 + 0.4 = 1.4 m of 10.
            List<BeamSpan> free = Attach.FreeIntervals(3f, 20f, one, 1f, 0.4f);
            Program.Check(free.Count == 2, "two intervals: " + free.Count);
            Program.Near(free[0].Lo, 3f, 1e-5f, "the first starts at the lower bound");
            Program.Near(free[0].Hi, 8.6f, 1e-4f, "and ends 1.4 m short of the board");
            Program.Near(free[1].Lo, 11.4f, 1e-4f, "the second starts 1.4 m past it");
            Program.Near(free[1].Hi, 20f, 1e-5f, "and goes to the upper bound");

            // Nothing in the way: one interval. Boards that cover everything: none. A board near the end: one.
            Program.Check(Attach.FreeIntervals(3f, 20f, new List<KeyValuePair<float, float>>(), 1f, 0.4f).Count == 1, "an empty beam is one interval");
            var wall = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(5f, 6f), new KeyValuePair<float, float>(10f, 6f) };
            Program.Check(Attach.FreeIntervals(4f, 11f, wall, 2f, 0.4f).Count == 0, "boards that cover the whole beam leave no place");
            var near = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(3.2f, 1f) };
            List<BeamSpan> afterNear = Attach.FreeIntervals(3f, 20f, near, 1f, 0.4f);
            Program.Check(afterNear.Count == 1 && Math.Abs(afterNear[0].Lo - 4.6f) < 1e-3f, "a board at the start leaves the rest: " + (afterNear.Count > 0 ? afterNear[0].Lo : -1f));

            // Two boards that are close: the gap between them is only an interval if it holds the new board.
            var pair = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(8f, 1f), new KeyValuePair<float, float>(10.5f, 1f) };
            Program.Check(Attach.FreeIntervals(3f, 20f, pair, 1f, 0.4f).Count == 2, "two close boards with no room between them are two intervals, one at each side");
            var apart = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(8f, 1f), new KeyValuePair<float, float>(14f, 1f) };
            List<BeamSpan> between = Attach.FreeIntervals(3f, 20f, apart, 1f, 0.4f);
            Program.Check(between.Count == 3 && Math.Abs(between[1].Lo - 9.4f) < 1e-3f && Math.Abs(between[1].Hi - 12.6f) < 1e-3f, "room between two boards 6 m apart: " + between.Count);

            // Unsorted in, same out.
            var shuffled = new List<KeyValuePair<float, float>> { new KeyValuePair<float, float>(14f, 1f), new KeyValuePair<float, float>(8f, 1f) };
            Program.Check(Attach.FreeIntervals(3f, 20f, shuffled, 1f, 0.4f).Count == 3, "the boards need not be sorted");
        }

        private static void Stepping()
        {
            var free = new List<BeamSpan> { new BeamSpan(3f, 8.6f), new BeamSpan(11.4f, 20f) };
            Program.Near(Attach.Nearest(free, 10f), 8.6f, 1e-5f, "10 is in the way: the nearest free place, 8.6 (1.4 away) rather than 11.4 (1.4 away) ... ties go to the first");
            Program.Near(Attach.Nearest(free, 9.9f), 8.6f, 1e-5f, "9.9: 8.6");
            Program.Near(Attach.Nearest(free, 10.4f), 11.4f, 1e-5f, "10.4: 11.4");
            Program.Near(Attach.Nearest(free, 5f), 5f, 1e-6f, "a free place is itself");
            Program.Near(Attach.Nearest(free, -4f), 3f, 1e-6f, "before the beam: its start");
            Program.Near(Attach.Nearest(free, 99f), 20f, 1e-6f, "past the beam: its end");
            Program.Check(float.IsNaN(Attach.Nearest(new List<BeamSpan>(), 1f)), "no place: NaN");

            Program.Near(Attach.Move(free, 5f, 1, 0.25f), 5.25f, 1e-5f, "right by a step");
            Program.Near(Attach.Move(free, 5f, -1, 0.25f), 4.75f, 1e-5f, "left by a step");
            Program.Near(Attach.Move(free, 8.6f, 1, 0.25f), 11.4f, 1e-5f, "at the end of an interval it jumps over the board to the next");
            Program.Near(Attach.Move(free, 11.4f, -1, 0.25f), 8.6f, 1e-5f, "and back");
            Program.Near(Attach.Move(free, 20f, 1, 0.25f), 20f, 1e-5f, "at the end of the beam it stays");
            Program.Near(Attach.Move(free, 3f, -1, 0.25f), 3f, 1e-5f, "at the start too");
            Program.Near(Attach.Move(free, 19.9f, 1, 0.25f), 20f, 1e-5f, "a step that passes the end stops at it");
            Program.Near(Attach.Move(free, 10f, 1, 0.25f), 11.4f, 1e-5f, "from a place that is not free it goes to the free one on its side: " + Attach.Move(free, 10f, 1, 0.25f));
            Program.Near(Attach.Move(free, 5f, 1, 1f), 6f, 1e-5f, "a longer step");

            // A walk from the left end to the right end in steps visits only free places and ends at the end.
            float x = 3f;
            bool allFree = true;
            for (int i = 0; i < 200; i++)
            {
                x = Attach.Move(free, x, 1, 0.25f);
                allFree &= Math.Abs(Attach.Nearest(free, x) - x) < 1e-4f;
            }

            Program.Check(allFree && Math.Abs(x - 20f) < 1e-4f, "stepping right 200 times: only free places, and the end: " + x);
            Program.Near(Attach.Move(new List<BeamSpan>(), 4f, 1, 0.25f), 4f, 1e-6f, "no place: the key does nothing");
            Program.Near(Attach.Move(free, 5f, 0, 0.25f), 5f, 1e-6f, "no direction: stay");
        }

        private static void BeamPlace()
        {
            // A gantry with a 20 m span and a board of 1.2 m on it at 6 m: a new one of 0.8 m, asked for at 6.5, is put clear of it: 1.2 / 2 + 0.8 / 2 + 0.4 = 1.4 m away.
            var existing = new List<AssemblyBoard> { B(Round, 0, 0, Attach.LaneHand, 6f) };
            AttachResult result = Eval(existing, Small, Gantry, 20f, offset: 6.5f);
            Program.Check(result.Ok && result.Hung, "a gantry takes it: " + result.Reason);
            Program.Check(Math.Abs(result.Offset - 6f) >= 1.4f - 1e-3f, "clear of the board at 6 m: " + result.Offset);
            Program.Check(Math.Abs(result.Offset - 6.5f) <= Math.Abs(4.6f - 6.5f) + 1e-3f || Math.Abs(result.Offset - 7.4f) < 0.01f, "and as near the asked place as that allows: " + result.Offset);

            AssemblyPlan plan = Assembly.Plan(result.Boards, result.Support, result.Reach);
            Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "the plan has no overlap");
            foreach (Placement p in plan.Placed)
            {
                Assembly.LaneBounds(plan.Layout, p.Def.Width * 0.5f, out float lo, out float hi);
                Program.Check(p.Cx >= lo - 1e-3f && p.Cx <= hi + 1e-3f, "a board stands inside the bounds [" + lo + ", " + hi + "]: " + p.Cx);
            }

            // Asked for a place beside the post: moved clear of it and its knee brace.
            AttachResult nearPost = Eval(new List<AssemblyBoard>(), Small, Gantry, 20f, offset: 0.2f);
            Assembly.LaneBounds(Assembly.Plan(nearPost.Boards, nearPost.Support, 20f).Layout, 0.4f, out float low, out _);
            Program.Near(nearPost.Offset, low, 1e-3f, "asked for 0.2 m from the post: the first place that is clear of its brace");

            // No place asked for: the middle of the beam.
            AttachResult middle = Eval(new List<AssemblyBoard>(), Small, Gantry, 20f);
            Program.Check(Math.Abs(middle.Offset - 10f) < 1.5f, "no place asked for: about the middle of a 20 m span: " + middle.Offset);

            // A gantry that has no room left in its span is made longer for the next board (it used to refuse): the board goes after the last one, and the beam is as long as it needs.
            var crowd = new List<AssemblyBoard>();
            SignDef wide = Make("t.w", "mandatory", 3.0f, 1.0f);
            for (int i = 0; i < 3; i++)
                crowd.Add(B(wide, i, i, Attach.LaneHand, 3.2f + i * 3.4f));
            AttachResult grown = Eval(crowd, wide, Gantry, 14f);
            Program.Check(grown.Ok && grown.Grew, "boards that fill the span: the beam is made longer: " + grown.Reason);
            Program.Near(grown.Offset, 3.2f + 3 * 3.4f, 1e-3f, "the new board goes right after the last: " + grown.Offset);
            Program.Check(grown.Reach >= grown.Offset + 1.5f + Assembly.GantryClear - 1e-3f && grown.Reach <= SupportLayouts.MaxSpan, "to the length the board needs: " + grown.Reach);
            Program.Near(grown.Reach * 2f, (float)Math.Round(grown.Reach * 2f), 1e-4f, "in steps of half a metre");
            Program.Check(grown.Span >= grown.Reach - 1e-3f, "and the span the layout makes is that long: " + grown.Span);

            // At the longest beam there is no more: eleven boards of 3 m on 40 m leave no place for a twelfth.
            var packed = new List<AssemblyBoard>();
            for (int i = 0; i < 11; i++)
                packed.Add(B(wide, i, i, Attach.LaneHand, 2.5f + i * 3.4f));
            AttachResult none = Eval(packed, wide, Gantry, SupportLayouts.MaxSpan);
            Program.Check(!none.Ok && none.Reason == AttachReason.NoSpace, "eleven boards on the longest beam leave no place: " + none.Reason);
            Program.Check(none.SpaceLeft == 0f, "no room is left");

            // The room is counted against the longest beam (40 m): the beam can be made longer, so what is left does not depend on the span it has now.
            AttachResult shortSpan = Eval(existing, Small, Gantry, 12f), longSpan = Eval(existing, Small, Gantry, 30f);
            Program.Near(shortSpan.SpaceLeft, longSpan.SpaceLeft, 0.2f, $"the room left does not depend on the span the beam has now: {shortSpan.SpaceLeft:0.0} m and {longSpan.SpaceLeft:0.0} m");
            Program.Check(shortSpan.SpaceLeft > 25f && shortSpan.SpaceLeft < SupportLayouts.MaxSpan, "and is about the longest beam less the boards and the clearances: " + shortSpan.SpaceLeft);
            Program.Check(longSpan.SpaceBefore > longSpan.SpaceLeft, "there was more room before than after");
            Program.Near(shortSpan.Span, 12f, 1e-3f, "the beam keeps the length it has: " + shortSpan.Span);
            Program.Check(!shortSpan.Grew && !longSpan.Grew, "and did not grow");
        }

        private static void BeamPinned()
        {
            // Two boards that are side by side (lane 0): centred on the span. A third is put at the right: the first two must stay where they are, not be centred again.
            var side = new List<AssemblyBoard> { B(Small, 0, 0), B(Warn, 1, 1) };
            AssemblyPlan before = Assembly.Plan(Assembly.Order(side), Gantry, 20f);
            float[] centres = { before.Placed[0].Cx, before.Placed[1].Cx };
            AttachResult result = Eval(side, Aux, Gantry, 20f, offset: 17f);
            Program.Check(result.Ok, "a third board: " + result.Reason);
            foreach (AssemblyBoard b in result.Boards)
            {
                if (b.Member == result.NewMember)
                    continue;
                Program.Check(b.Lane == Attach.LaneHand, "the board " + b.Member + " is pinned (by hand)");
                Program.Near(b.OffsetX, centres[b.Member], 1e-3f, "at the place where it stood: " + b.OffsetX + " vs " + centres[b.Member]);
            }

            AssemblyPlan after = Assembly.Plan(result.Boards, result.Support, result.Reach);
            foreach (Placement p in after.Placed)
            {
                if (p.Board.Member == result.NewMember)
                    Program.Near(p.Cx, result.Offset, 1e-3f, "the new board is where it was put");
                else
                    Program.Near(p.Cx, centres[p.Board.Member], 1e-3f, "the others have not moved");
            }

            // Boards over lanes keep their lane.
            var laned = new List<AssemblyBoard> { B(Small, 0, 0, 2, 9f) };
            AttachResult keep = Eval(laned, Warn, Gantry, 20f, offset: 15f);
            Program.Check(keep.Boards.Find(b => b.Member == 0).Lane == 2 && Math.Abs(keep.Boards.Find(b => b.Member == 0).OffsetX - 9f) < 1e-4f, "a board over lane 2 stays over lane 2");

            // The inputs are not changed (the caller's boards are what it has read from the entities).
            Program.Check(side[0].Lane == 0 && side[1].Lane == 0 && laned[0].Lane == 2, "the boards that were given are left as they were");
        }

        private static void ArmGrows()
        {
            // A 5 m arm with a board at 3.2 m: another asked for at 8 m needs the arm to be longer: 8 + 0.4 + 0.45 = 8.85 m, so 9 m.
            var existing = new List<AssemblyBoard> { B(Small, 0, 0, Attach.LaneHand, 3.2f) };
            AttachResult result = Eval(existing, Small, Arm, 0f, offset: 8f);
            Program.Check(result.Ok && result.Hung, "an arm takes it: " + result.Reason);
            Program.Near(result.Offset, 8f, 1e-3f, "at the asked place, beyond the arm's old end");
            Program.Check(result.Reach >= 8f + 0.4f - Assembly.ArmTipInside - 1e-3f && result.Reach <= 40f, "the arm is made long enough: " + result.Reach);
            Program.Near(result.Reach * 2f, (float)Math.Round(result.Reach * 2f), 1e-4f, "in steps of half a metre");

            // Within the arm: no change to its length.
            AttachResult within = Eval(existing, Small, Arm, 0f, offset: 3.8f + 0.4f + 0.4f);
            Program.Check(within.Ok, "a place on the arm that is free");

            // A gantry's span is the road's: it is not changed, and a place beyond it is not offered.
            AttachResult gantry = Eval(existing, Small, Gantry, 14f, offset: 30f);
            Program.Check(gantry.Ok && gantry.Reach == 14f, "a gantry's span stays");
            Assembly.LaneBounds(Assembly.Plan(gantry.Boards, gantry.Support, 14f).Layout, 0.4f, out _, out float hi);
            Program.Check(gantry.Offset <= hi + 1e-3f, "and the board is kept inside it: " + gantry.Offset + " <= " + hi);

            // The longest arm: nothing beyond.
            var far = new List<AssemblyBoard> { B(Small, 0, 0, Attach.LaneHand, 3.2f) };
            AttachResult longest = Eval(far, Small, Arm, 0f, offset: 99f);
            Program.Check(longest.Ok && longest.Reach <= SupportLayouts.MaxSpan, "never past the longest span: " + longest.Reach);
        }

        private static void Moving()
        {
            // Two boards at 6 and 12 on a 20 m gantry; the one at 12 is moved. The request carries the other one only, and the moved board with its own number.
            var others = new List<AssemblyBoard> { B(Small, 0, 0, Attach.LaneHand, 6f) };
            AttachResult right = Eval(others, Small, Gantry, 20f, offset: 12.25f, moving: true, member: 1);
            Program.Check(right.Ok && right.NewMember == 1 && Math.Abs(right.Offset - 12.25f) < 1e-3f, "moved to 12.25: " + right.Offset);

            // Moving it left toward the other: it stops short of it, and a step further jumps over it.
            AttachResult stop = Eval(others, Small, Gantry, 20f, offset: 6.5f, moving: true, member: 1);
            Program.Check(Math.Abs(stop.Offset - 6f) >= 1.2f - 1e-3f, "it cannot be put on the other: " + stop.Offset);
            float x = Attach.Move(stop.Free, 7.4f, -1, 0.25f);
            Program.Check(x < 6f, "one more step to the left jumps over the other board: " + x);

            // A post: the board is moved up (index 0) or down.
            var stack = new List<AssemblyBoard> { B(Small, 0, 0), B(Warn, 2, 2) };
            AttachResult up = Eval(stack, Aux, Pole, index: 0, moving: true, member: 1);
            Program.Check(up.Ok && up.Boards[0].Member == 1 && up.Boards[1].Member == 0 && up.Boards[2].Member == 2, "moved to the top");
            AttachResult down = Eval(stack, Aux, Pole, index: 2, moving: true, member: 1);
            Program.Check(down.Ok && down.Boards[2].Member == 1 && down.Index == 2, "moved to the bottom");

            // A moved board is not turned away for the height of the stack even when it is at its limit.
            var tall = new List<AssemblyBoard> { B(Round, 0, 0), B(Round, 1, 1) };
            AttachResult third = Eval(tall, Aux, Pole, index: 1, moving: true, member: 2);
            Program.Check(third.Ok, "a move is not checked against the height: " + third.Reason);
        }

        private static void ManualOrder()
        {
            SignDef mand = Make("t.mand", "mandatory", 0.8f, 0.8f), proh = Make("t.proh", "prohibition", 0.8f, 0.8f), warn = Make("t.warn", "warning", 0.8f, 0.8f);
            // Standard order: prohibition, mandatory, warning whatever the slots.
            var standard = new List<AssemblyBoard> { B(warn, 0, 0), B(mand, 1, 1), B(proh, 2, 2) };
            List<AssemblyBoard> a = Assembly.Order(standard);
            Program.Check(a[0].Member == 2 && a[1].Member == 1 && a[2].Member == 0, "no flag: the standard's order");

            // One board flagged: the slots are the order.
            var manual = new List<AssemblyBoard> { B(warn, 0, 0), B(mand, 1, 1, manual: true), B(proh, 2, 2) };
            List<AssemblyBoard> b = Assembly.Order(manual);
            Program.Check(b[0].Member == 0 && b[1].Member == 1 && b[2].Member == 2, "flagged: warning, mandatory, prohibition as the slots say");

            // An auxiliary board is not carried along to its owner then: it stays where its slot is.
            var withAux = new List<AssemblyBoard> { B(proh, 0, 0, manual: true), B(mand, 1, 1, manual: true), B(Aux, 2, 2, manual: true) };
            List<AssemblyBoard> c = Assembly.Order(withAux);
            Program.Check(c[2].Member == 2, "the slot says last: last");

            // The record carries the flag to the boards.
            var records = new List<ItemRecord> { new ItemRecord(SignCatalog.All[0].Hash, Vec3Zero, 0f, 0u, null, 0f, 5u, 0) { Manual = true } };
            List<AssemblyBoard> fromRecords = ItemGroups.Boards(records);
            Program.Check(fromRecords.Count == 1 && fromRecords[0].Manual, "ItemGroups.Boards passes the flag on");
            Program.Check(!TileDiff.Same(records[0], new ItemRecord(SignCatalog.All[0].Hash, Vec3Zero, 0f, 0u, null, 0f, 5u, 0)), "the flag is a change to the tile diff");
            ItemRecord hidden = records[0];
            hidden.Hidden = true;
            Program.Check(!TileDiff.Same(records[0], hidden), "so is being hidden");
            Program.Check(ChunkBuilder.Build(new[] { hidden }, Vec3Zero, out int skipped).Count == 0 && skipped == 0, "a hidden item is not drawn and is not counted as skipped");
            ItemRecord shown = records[0];
            Program.Check(ChunkBuilder.Build(new[] { shown }, Vec3Zero, out _).Count > 0, "and a shown one is");
        }

        private static readonly CDLC.TS.Snap.Vec3 Vec3Zero = CDLC.TS.Snap.Vec3.Zero;

        private static void BuildAgrees()
        {
            // The result is what the build draws: put a board on a gantry, build the support with what comes back, and the face of the new board is at the place that was worked out.
            var existing = new List<AssemblyBoard> { B(Round, 0, 0, Attach.LaneHand, 7f) };
            AttachResult result = Eval(existing, Small, Gantry, 20f, offset: 12f);
            SignParts parts = SignBuilder.BuildAssembly(result.Boards, result.Support, result.Reach);
            int index = result.Boards.FindIndex(b => b.Member == result.NewMember);
            parts.Faces[index].Mesh.Bounds(out CDLC.TS.Snap.Vec3 lo, out CDLC.TS.Snap.Vec3 hi);
            Program.Near((lo.X + hi.X) * 0.5f, result.Offset, 0.01f, "the face of the new board is at the offset that was worked out");
            Program.Check(parts.Faces.Count == 2, "two faces");

            // A stack: the order that comes back is the order the build draws from the top: the new board's face is the top one when index is 0.
            var stack = new List<AssemblyBoard> { B(Small, 0, 0) };
            AttachResult onTop = Eval(stack, Warn, Pole, index: 0);
            SignParts post = SignBuilder.BuildAssembly(onTop.Boards, onTop.Support, 0f);
            int newFace = onTop.Boards.FindIndex(b => b.Member == onTop.NewMember);
            post.Faces[newFace].Mesh.Bounds(out CDLC.TS.Snap.Vec3 nlo, out _);
            post.Faces[1 - newFace].Mesh.Bounds(out CDLC.TS.Snap.Vec3 olo, out _);
            Program.Check(nlo.Y > olo.Y, "index 0 is the top of the stack: its face is above the other");

            // And the other way up.
            AttachResult underneath = Eval(stack, Warn, Pole, index: 1);
            SignParts post2 = SignBuilder.BuildAssembly(underneath.Boards, underneath.Support, 0f);
            int f2 = underneath.Boards.FindIndex(b => b.Member == underneath.NewMember);
            post2.Faces[f2].Mesh.Bounds(out CDLC.TS.Snap.Vec3 ulo, out _);
            post2.Faces[1 - f2].Mesh.Bounds(out CDLC.TS.Snap.Vec3 vlo, out _);
            Program.Check(ulo.Y < vlo.Y, "index 1 is below it");
        }
    }
}
