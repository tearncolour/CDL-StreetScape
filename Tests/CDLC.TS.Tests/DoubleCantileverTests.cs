using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The double cantilever (GB 5768.2 4.8.12 b, Fig. 6 b): one post, an arm each side of it, the boards of both arms facing the same traffic.</summary>
    internal static class DoubleCantileverTests
    {
        public static void RunAll()
        {
            Program.Run("double cantilever: in the catalogue, saved by the hash of its id, the old forms unchanged", Catalogue);
            Program.Run("double cantilever: which arm a board is on, and the width the layout is made for", ArmsOfBoards);
            Program.Run("double cantilever: the boards stand mirrored about the post, in the order of 4.8.6 from the left to the right", Arrangement);
            Program.Run("double cantilever: a board over a lane on the right arm has a negative offset and makes the arms longer", LanesBothSides);
            Program.Run("double cantilever: the lanes on both sides of the post are offered, from the driver's left to the right", LaneCentres);
            Program.Run("double cantilever: free places on both arms, four boards an arm, eight in all", FreePlaces);
            Program.Run("double cantilever: the arrow keys step over the post", Stepping);
            Program.Run("double cantilever: what is built and what can be pointed at", BuiltShape);
            Program.Run("double cantilever: the faces of both arms look outward (a mirrored tube is not inside out)", Winding);
        }

        private static SupportDef Double => SupportCatalog.ById(SupportCatalog.DoubleCantilever);
        private static SupportDef Single => SupportCatalog.ById(SupportCatalog.Cantilever);

        private static SignDef Make(string id, string group, float w, float h) => new SignDef
        {
            Id = id, Group = group, Code = id, Zh = id, En = id, Width = w, Height = h, Shape = ShapeKind.RoundRect, TextureWidth = 64, TextureHeight = 64, BottomClearance = 2.3f, Keywords = new string[0]
        };

        private static readonly SignDef Narrow = Make("t.narrow", "prohibition", 1.2f, 1.2f), Wide = Make("t.wide", "guide", 3.0f, 1.2f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int slot, int lane = 0, float x = 0f) =>
            new AssemblyBoard { Instance = Inst(def), Member = member, Slot = slot, Lane = lane, OffsetX = x };

        private static AttachResult Eval(List<AssemblyBoard> existing, SignDef add, float reach = 0f, float offset = float.NaN, bool moving = false, int member = -1) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = Double, Reach = reach, Board = Inst(add), Offset = offset, Moving = moving, Member = member });

        private static void Catalogue()
        {
            Program.Check(Double != null && Double.Kind == SupportKind.DoubleCantilever && Double.Id == "cantilever.double", "the double cantilever is in the catalogue");
            Program.Check(Double.Clearance == Single.Clearance && Double.ArmLength == Single.ArmLength, "the arm and the clearance are the cantilever's");
            Program.Check(SupportCatalog.ByHash(Double.Hash) == Double && Double.Hash != Single.Hash && Double.Hash != 0u, "found again by its hash, which is not the cantilever's");

            // The enum values of the old forms are what they were (the saved item holds the hash of the id, but code reads the numbers).
            Program.Check((int)SupportKind.SinglePole == 0 && (int)SupportKind.DoublePole == 1 && (int)SupportKind.Cantilever == 2 && (int)SupportKind.TriplePole == 3 && (int)SupportKind.Gantry == 4
                && (int)SupportKind.DoubleCantilever == 5, "the new kind is appended last");
            Program.Check(SupportKinds.Hung(SupportKind.DoubleCantilever) && SupportKinds.Arm(SupportKind.DoubleCantilever) && SupportKinds.Hung(SupportKind.Gantry) && !SupportKinds.Arm(SupportKind.Gantry)
                && !SupportKinds.Hung(SupportKind.SinglePole), "hung and arm kinds");
            Program.Check(Assembly.MaxFor(SupportKind.DoubleCantilever) == 8 && Assembly.MaxBoardsPerArm == 4 && Assembly.MaxFor(SupportKind.Cantilever) == 4, "four an arm, eight in all");

            // The planner never picks it for a sign: it is chosen by the player.
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                SupportDef planned = SupportPlanner.Auto(instance.Def);
                Program.Check(planned.Kind != SupportKind.DoubleCantilever, choice.Id + ": the planner's support is not the double cantilever");
            }
        }

        private static void ArmsOfBoards()
        {
            // Four boards side by side: the first two on the left arm, the last two on the right (reading across is the order).
            var four = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2), B(Narrow, 3, 3) };
            int[] arms = Assembly.Arms(four);
            Program.Check(arms[0] == 1 && arms[1] == 1 && arms[2] == -1 && arms[3] == -1, "four boards: two and two: " + string.Join(",", arms));
            // Three: the extra one on the left. One alone: on the left.
            Program.Check(Assembly.Arms(new[] { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2) }).Length == 3 && Assembly.Arms(new[] { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2) })[1] == 1 && Assembly.Arms(new[] { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2) })[2] == -1, "three boards: left, left, right");
            Program.Check(Assembly.Arms(new[] { B(Narrow, 0, 0) })[0] == 1, "one board: left");

            // A board over a lane is on the arm its offset says; the others are shared.
            int[] mixed = Assembly.Arms(new[] { B(Narrow, 0, 0, 255, -4f), B(Narrow, 1, 1, 255, 4f), B(Narrow, 2, 2), B(Narrow, 3, 3) });
            Program.Check(mixed[0] == -1 && mixed[1] == 1 && mixed[2] == 1 && mixed[3] == -1, "lanes by the sign of the offset, the rest shared: " + string.Join(",", mixed));

            // The layout is made for the wider group: two wide boards one each side are as wide as one, not as two.
            var pair = new List<AssemblyBoard> { B(Wide, 0, 0), B(Wide, 1, 1) };
            Program.Near(Assembly.WidestArmGroup(pair), 3.0f, 1e-4f, "two 3 m boards, one on each arm: the widest group is 3 m");
            SignDef total = Assembly.TotalDef(pair, true, true);
            Program.Near(total.Width, 3.0f, 1e-4f, "the total the layout is made for is 3 m wide");
            SignDef sameArm = Assembly.TotalDef(pair, true, false);
            Program.Near(sameArm.Width, 6.4f, 1e-4f, "on one arm they would be 3 + 0.4 + 3");
            // Three on two arms: two on the left make the group.
            var three = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2) };
            Program.Near(Assembly.WidestArmGroup(three), 1.2f * 2f + Assembly.SideGap, 1e-4f, "two boards on the left arm: 2.8 m");
        }

        private static void Arrangement()
        {
            var boards = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2), B(Narrow, 3, 3) };
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(boards), Double, 0f);
            Program.Check(plan.Hung && plan.Layout.Kind == SupportKind.DoubleCantilever && plan.Layout.PoleX.Length == 1, "hung, one post");
            List<Placement> placed = plan.Placed;
            Program.Check(placed.Count == 4, "four boards");

            // Reading from the viewer's left to the right (the larger X is the viewer's left) is the order of the boards.
            var byX = new List<Placement>(placed);
            byX.Sort((p, q) => q.Cx.CompareTo(p.Cx));
            for (int i = 0; i < 4; i++)
                Program.Check(byX[i].Board.Member == i, "left to right: board " + i + " is number " + byX[i].Board.Member);

            // Two on each side, symmetrical about the post: the two groups are the same distance out.
            Program.Check(placed[0].Cx > 0f && placed[1].Cx > 0f && placed[2].Cx < 0f && placed[3].Cx < 0f, "two on each side of the post");
            Program.Near((placed[0].Cx + placed[1].Cx) * 0.5f, -(placed[2].Cx + placed[3].Cx) * 0.5f, 1e-3f, "the two groups are centred at the same distance from the post");

            // Every board clear of the post and its brace, none beyond the end; none overlaps another.
            foreach (Placement p in placed)
            {
                float inner = Math.Abs(p.Cx) - p.Def.Width * 0.5f, outer = Math.Abs(p.Cx) + p.Def.Width * 0.5f;
                Program.Check(inner >= plan.Layout.BoardFrom - 1e-3f, "board " + p.Board.Member + " keeps its distance from the post: " + inner);
                Program.Check(outer <= plan.Layout.ArmLength + Assembly.ArmTipInside + 1e-3f, "board " + p.Board.Member + " keeps clear of the end: " + outer + " of " + plan.Layout.ArmLength);
            }

            Program.Check(Assembly.Overlaps(placed, true).Count == 0, "nothing overlaps");

            // The arm is the single cantilever's for the same group: a double cantilever needs the length of its widest arm's group, not the sum.
            var oneSide = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1) };
            float singleArm = Assembly.Plan(Assembly.Order(oneSide), Single, 0f).Layout.ArmLength;
            float doubleArm = Assembly.Plan(Assembly.Order(boards), Double, 0f).Layout.ArmLength;
            Program.Near(doubleArm, singleArm, 1e-3f, "four boards on a double cantilever need the arm two boards need on a single one: " + doubleArm);
        }

        private static void LanesBothSides()
        {
            // RequiredReach uses the distance from the post, on either side.
            var lanes = new List<AssemblyBoard> { B(Narrow, 0, 0, 1, 6f), B(Narrow, 1, 1, 2, -9f) };
            Program.Near(Assembly.RequiredReach(lanes, SupportKind.DoubleCantilever), 9f + 0.6f - Assembly.ArmTipInside, 1e-4f, "the right arm's board sets the length: 9 m out");
            Program.Near(Assembly.RequiredReach(lanes, SupportKind.Cantilever), 9f + 0.6f - Assembly.ArmTipInside, 1e-4f, "(and so for a single arm, by the distance)");
            Program.Near(Assembly.RequiredReach(lanes, SupportKind.SinglePole), 0f, 1e-6f, "none for a post");

            AssemblyPlan plan = Assembly.Plan(Assembly.Order(lanes), Double, 0f);
            Program.Check(plan.Layout.ArmLength >= 9f + 0.6f - Assembly.ArmTipInside - 1e-3f, "the arms are long enough for it: " + plan.Layout.ArmLength);
            Placement left = null, right = null;
            foreach (Placement p in plan.Placed)
            {
                if (p.Board.Member == 0) left = p;
                else right = p;
            }

            Program.Near(left.Cx, 6f, 1e-3f, "the left board is 6 m to the left of the post");
            Program.Near(right.Cx, -9f, 1e-3f, "the right board is 9 m to the right");

            // An offset too near the post is brought out to the brace; too far to the end of the arm.
            var near = new List<AssemblyBoard> { B(Narrow, 0, 0, 1, -0.2f) };
            AssemblyPlan nearPlan = Assembly.Plan(near, Double, 0f);
            Assembly.LaneBounds(nearPlan.Layout, 0.6f, out float lo, out float hi);
            Program.Near(nearPlan.Placed[0].Cx, -lo, 1e-3f, "brought out to the brace on the right arm: " + nearPlan.Placed[0].Cx + " vs " + (-lo));

            // The same plan twice is the same (the order of the boards does not move them).
            AssemblyPlan again = Assembly.Plan(Assembly.Order(lanes), Double, 0f);
            Program.Near(again.Placed[0].Cx, plan.Placed[0].Cx, 1e-6f, "deterministic");
        }

        private static void LaneCentres()
        {
            // A post in the middle of four lanes: two lanes to its left (X above 0), two to its right. A roadside post drops the ones beside or behind it; a double cantilever keeps them all.
            float[] xs = { 5.5f, 2.5f, -2.5f, -5.5f, -2.4f };
            List<float> roadside = LaneMath.Arrange(xs, 0.3f);
            Program.Check(roadside.Count == 2 && roadside[0] == 5.5f && roadside[1] == 2.5f, "at the road's side only the lanes toward the road: " + string.Join(",", roadside));
            List<float> both = LaneMath.Arrange(xs, float.NegativeInfinity);
            Program.Check(both.Count == 4, "in the middle all four lanes (the one 10 cm from another counts once): " + string.Join(",", both));
            Program.Check(both[0] > both[1] && both[1] > both[2] && both[2] > both[3] && both[0] == 5.5f && both[3] == -5.5f, "from the driver's left to the right, the largest X first: " + string.Join(",", both));

            // They are spread over the boards in that order: the first board to the leftmost lane, and the lane's sign puts it on its arm.
            var boards = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1), B(Narrow, 2, 2), B(Narrow, 3, 3) };
            List<KeyValuePair<int, float>> spread = Assembly.SpreadOverLanes(Assembly.Order(boards), both);
            Program.Check(spread[0].Value == 5.5f && spread[1].Value == 2.5f && spread[2].Value == -2.5f && spread[3].Value == -5.5f && spread[0].Key == 1 && spread[3].Key == 4, "one board over each lane");
            var laned = new List<AssemblyBoard>();
            for (int i = 0; i < 4; i++)
                laned.Add(B(Narrow, i, i, spread[i].Key, spread[i].Value));
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(laned), Double, 0f);
            int[] arms = Assembly.Arms(Assembly.Order(laned));
            Program.Check(arms[0] == 1 && arms[1] == 1 && arms[2] == -1 && arms[3] == -1, "two lanes on each arm: " + string.Join(",", arms));
            foreach (Placement pl in plan.Placed)
                Program.Check(Math.Abs(pl.Cx - (laned[pl.Board.Member].OffsetX)) < 0.01f, "board " + pl.Board.Member + " is over its lane: " + pl.Cx + " vs " + laned[pl.Board.Member].OffsetX);
            Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "four lanes: nothing overlaps");
        }

        private static void FreePlaces()
        {
            // The first board goes to the middle of the left arm; the second, with nothing asked, to the arm that has fewer boards.
            AttachResult first = Eval(new List<AssemblyBoard>(), Narrow);
            Program.Check(first.Ok && first.Offset > 0f, "the first board goes on the left arm: " + first.Offset + " " + first.Reason);
            AttachResult second = Eval(first.Boards, Narrow);
            Program.Check(second.Ok && second.Offset < 0f, "the second, nothing asked, on the right arm: " + second.Offset);
            Program.Check(second.Free.Count >= 2, "free places on both arms: " + second.Free.Count);
            bool negative = false, positive = false;
            foreach (BeamSpan span in second.Free)
            {
                negative |= span.Hi < 0f;
                positive |= span.Lo > 0f;
            }

            Program.Check(negative && positive, "the free places are signed: some on each side of the post");

            // Asked for a place on the right arm (a negative offset): it goes there, at the nearest free place.
            AttachResult onRight = Eval(new List<AssemblyBoard>(), Narrow, 0f, -4f);
            Program.Check(onRight.Ok && Math.Abs(onRight.Offset + 4f) < 0.01f, "asked for -4 m: " + onRight.Offset);
            AttachResult onLeft = Eval(new List<AssemblyBoard>(), Narrow, 0f, 4f);
            Program.Check(onLeft.Ok && Math.Abs(onLeft.Offset - 4f) < 0.01f, "asked for +4 m: " + onLeft.Offset);

            // Four boards on the left arm, one after the other: the arm takes four, the fifth goes to the right arm even when the left was asked for.
            var boards = new List<AssemblyBoard>();
            float reach = 0f;
            for (int i = 0; i < 4; i++)
            {
                AttachResult r = Eval(boards, Narrow, reach, 3f + 2f * i, false);
                Program.Check(r.Ok, "board " + (i + 1) + " on the left arm: " + r.Reason);
                if (!r.Ok)
                    return;
                boards = r.Boards;
                reach = r.Reach > 0f ? r.Reach : reach;
                Program.Check(r.Offset > 0f, "board " + (i + 1) + " is on the left: " + r.Offset);
            }

            AttachResult fifth = Eval(boards, Narrow, reach, 5f);
            Program.Check(fifth.Ok && fifth.Offset < 0f, "a fifth with the left asked for goes to the right arm: " + fifth.Reason + " " + fifth.Offset);
            Program.Check(fifth.Free.Count > 0 && fifth.Free.TrueForAll(s => s.Hi < 0f), "the left arm has no free place when it has four: " + fifth.Free.Count);

            // Eight in all, the ninth is refused (Full).
            var eight = new List<AssemblyBoard>(boards);
            for (int i = 0; i < 4; i++)
            {
                AttachResult r = Eval(eight, Narrow, reach, -(3f + 2f * i));
                Program.Check(r.Ok && r.Offset < 0f, "board " + (5 + i) + " on the right arm: " + r.Reason + " " + r.Offset);
                if (!r.Ok)
                    return;
                eight = r.Boards;
                reach = r.Reach > 0f ? r.Reach : reach;
            }

            Program.Check(eight.Count == 8, "eight boards");
            AttachResult ninth = Eval(eight, Narrow, reach);
            Program.Check(!ninth.Ok && ninth.Reason == AttachReason.Full && ninth.MaxBoards == 8, "the ninth is refused with the limit of 8: " + ninth.Reason + " " + ninth.MaxBoards);

            // And the eight are laid out without anything overlapping.
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(eight), Double, reach);
            Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "eight boards: nothing overlaps");

            // An arm grows to take a board that is asked for past its end, as a single arm does.
            AttachResult grown = Eval(new List<AssemblyBoard>(), Narrow, 5f, -12f);
            Program.Check(grown.Ok && grown.Grew && grown.Reach >= 12f + 0.6f - Assembly.ArmTipInside - 1e-3f && Math.Abs(grown.Offset + 12f) < 0.01f, "the arms grow for a board 12 m out on the right: " + grown.Reach + " " + grown.Offset);
        }

        private static void Stepping()
        {
            // The free places across the post: a step to the left from the right arm's first place jumps over the post to the left arm.
            var boards = new List<AssemblyBoard>();
            AttachResult r = Eval(boards, Narrow, 0f, 3f);
            AttachResult second = Eval(r.Boards, Narrow, 0f, -3f);
            Program.Check(second.Ok, "two boards: " + second.Reason);

            // Where a third would go on the right arm, the free places have the dead zone round the post between the arms.
            AttachResult probe = Eval(second.Boards, Narrow);
            float lastRight = float.NegativeInfinity, firstLeft = float.PositiveInfinity;
            foreach (BeamSpan span in probe.Free)
            {
                if (span.Hi < 0f)
                    lastRight = Math.Max(lastRight, span.Hi);
                if (span.Lo > 0f)
                    firstLeft = Math.Min(firstLeft, span.Lo);
            }

            Program.Check(lastRight < 0f && firstLeft > 0f && firstLeft - lastRight > 2f, "a dead zone round the post: " + lastRight + " to " + firstLeft);

            // A press of the arrow key at the end of the right arm's free places goes to the left arm's first (toward larger X).
            float moved = Attach.Move(probe.Free, lastRight, 1, Attach.Step);
            Program.Check(moved > 0f, "one step toward the left past the right arm's last place jumps over the post: " + moved);
            float back = Attach.Move(probe.Free, firstLeft, -1, Attach.Step);
            Program.Check(back < 0f, "and back again: " + back);
        }

        private static void Winding()
        {
            // One board on the left arm, so that the right arm is bare. In the middle stretch of each arm, clear of the brace and the clamps, every triangle of the metal must face away from
            // the arm's axis: with back faces culled (the game does), an inside-out tube would not be seen.
            var boards = new List<AssemblyBoard> { B(Narrow, 0, 0, 1, 3f) };
            SignParts parts = SignBuilder.BuildAssembly(boards, Double, 7f);
            SupportLayout layout = parts.Layout;
            float x0 = layout.PoleRadius + SupportLayouts.JointStub + 0.4f, x1 = layout.ArmLength - 0.6f;
            foreach (float side in new[] { 1f, -1f })
            {
                int outward = 0, inward = 0;
                for (int i = 0; i + 2 < parts.Metal.Indices.Count; i += 3)
                {
                    Vec3 p0 = parts.Metal.Positions[parts.Metal.Indices[i]], p1 = parts.Metal.Positions[parts.Metal.Indices[i + 1]], p2 = parts.Metal.Positions[parts.Metal.Indices[i + 2]];
                    Vec3 centre = (p0 + p1 + p2) * (1f / 3f);
                    float along = side * centre.X;
                    if (along < x0 || along > x1 || Math.Abs(centre.Y - layout.ArmY) > layout.ArmRootRadius * 1.5f || Math.Abs(centre.Z) > layout.ArmRootRadius * 1.5f)
                        continue;
                    // Only the arm's own surface: the clamps of the board on the left arm are near the board.
                    if (side > 0f && Math.Abs(centre.X - 3f) < 1.4f)
                        continue;
                    Vec3 normal = MeshBuilder.Cross(p1 - p0, p2 - p0);
                    Vec3 away = new Vec3(0f, centre.Y - layout.ArmY, centre.Z);
                    if (Vec3.Dot(normal, away) > 0f)
                        outward++;
                    else
                        inward++;
                }

                Program.Check(outward > 8 && inward == 0, (side > 0f ? "left" : "right") + " arm: " + outward + " triangles look outward, " + inward + " inward");
            }
        }

        private static void BuiltShape()
        {
            // Two boards on a double cantilever: one on each arm.
            var boards = new List<AssemblyBoard> { B(Narrow, 0, 0), B(Narrow, 1, 1) };
            SignParts parts = SignBuilder.BuildAssembly(boards, Double, 0f);
            SupportLayout layout = parts.Layout;
            parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
            Program.Near(mMax.X, layout.ArmLength, 0.06f, "the left arm reaches the length: " + mMax.X);
            Program.Near(-mMin.X, layout.ArmLength, 0.06f, "the right arm reaches the same: " + mMin.X);
            Program.Check(parts.Faces.Count == 2, "two faces");

            int arms = 0, posts = 0, feet = 0, grown = 0;
            var armBoxes = new List<PickShape>();
            foreach (PickShape shape in parts.Picks)
            {
                if (shape.Name == "arm") { arms++; armBoxes.Add(shape); }
                else if (shape.Name == "post" && shape.Grow > 0f) grown++;
                else if (shape.Name == "post") posts++;
                else if (shape.Name == "foot") feet++;
            }

            Program.Check(arms == 2 && posts == 1 && feet == 1, $"two arms, one post, one foot to point at: {arms} {posts} {feet}");
            Program.Check(grown == 1, "and the post's room for small boards, grown for the ray (0.12.8): " + grown);
            bool leftArm = false, rightArm = false;
            foreach (PickShape box in armBoxes)
            {
                leftArm |= box.Min.X >= -1e-4f && box.Max.X > layout.ArmLength - 1e-3f;
                rightArm |= box.Max.X <= 1e-4f && box.Min.X < -layout.ArmLength + 1e-3f;
            }

            Program.Check(leftArm && rightArm, "the arms' boxes: one on each side");

            // The ray along the sign's depth at the middle of each arm, between the boards, hits the arm; at the board's middle it hits the board.
            float y = layout.ArmY;
            Vec3 origin = Vec3.Zero;
            foreach (float side in new[] { 1f, -1f })
            {
                float x = side * (layout.BoardFrom - 0.2f);
                bool hit = Picking.Pick(parts.Picks, origin, 0f, new Vec3(x, y, 6f), new Vec3(x, y, -6f), out float distance, out PickShape shape);
                Program.Check(hit && shape != null && shape.Name == "arm", (side > 0f ? "left" : "right") + " arm is hit near the post: " + (shape == null ? "nothing" : shape.Name));
            }

            // Each board is picked at its own middle, and they are different boards.
            var members = new HashSet<int>();
            foreach (Placement p in Assembly.Plan(Assembly.Order(boards), Double, 0f).Placed)
            {
                bool hit = Picking.Pick(parts.Picks, origin, 0f, new Vec3(p.Cx, p.Cy, 6f), new Vec3(p.Cx, p.Cy, -6f), out _, out PickShape shape);
                Program.Check(hit && shape.Name == "board", "board " + p.Board.Member + " is picked at its middle");
                if (hit && shape.Name == "board")
                    members.Add(shape.Member);
            }

            Program.Check(members.Count == 2, "two different boards are picked");

            // The mirror of a layout is a layout: one board on the left arm builds the same structure as its mirror on the right (the tubes, the foot and the post do not change).
            var left = new List<AssemblyBoard> { B(Narrow, 0, 0, 1, 4f) };
            var right = new List<AssemblyBoard> { B(Narrow, 0, 0, 1, -4f) };
            SignParts a = SignBuilder.BuildAssembly(left, Double, 6f), b = SignBuilder.BuildAssembly(right, Double, 6f);
            Program.Check(a.Metal.VertexCount == b.Metal.VertexCount && a.Concrete.VertexCount == b.Concrete.VertexCount, "a board on the right builds as many parts as one on the left: " + a.Metal.VertexCount + " and " + b.Metal.VertexCount);
            a.Faces[0].Mesh.Bounds(out Vec3 aLo, out Vec3 aHi);
            b.Faces[0].Mesh.Bounds(out Vec3 bLo, out Vec3 bHi);
            Program.Near(aLo.X, -bHi.X, 1e-3f, "the face is the mirror: " + aLo.X + " and " + bHi.X);
            Program.Near(aHi.X, -bLo.X, 1e-3f, "both ends");
        }
    }
}
