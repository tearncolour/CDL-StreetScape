using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A sign on posts on a raised strip between the carriageways (0.12.24, StripPost): a BRT road's kerbs and its median, with a barrier on them. And a cantilever there (0.12.25; the owner: "中间还是放不了悬臂式"):
    /// its arm over the traffic's lanes, to the driver's right from a median (SupportCatalog.CantileverRight).
    /// </summary>
    internal static class StripPostTests
    {
        public static void RunAll()
        {
            Program.Run("strip posts: the strip under the cursor", Under);
            Program.Run("strip posts: where across it", Across);
            Program.Run("strip posts: clear of a barrier", Barrier);
            Program.Run("strip posts: the traffic it is for", Traffic);
            Program.Run("strip cantilevers: which way the arm reaches, and how far", ArmWay);
            Program.Run("strip cantilevers: the mirrored cantilever", Mirror);
            Program.Run("strip cantilevers: the mirror built", MirrorBuilt);
            Program.Run("strip cantilevers: boards on the mirrored arm", MirrorBoards);
        }

        // A BRT road of 36 m: general lanes outside, kerbed islands of 1.2 m, the BRT lanes inside either side of a median of 3 m.
        private static readonly List<(float Lo, float Hi)> Raised = new List<(float Lo, float Hi)> { (-9.2f, -8f), (-1.5f, 1.5f), (8f, 9.2f) };
        private static readonly List<float> Forward = new List<float> { 3.25f, 6.5f, 11f, 14.5f }, Backward = new List<float> { -3.25f, -6.5f, -11f, -14.5f };
        private static readonly float[] One = { 0f }, Two = { -0.6f, 0.6f };

        private static void Under()
        {
            Program.Check(StripPost.Under(Raised, 8.5f, out float lo, out float hi) && lo == 8f && hi == 9.2f, "the island under the cursor");
            Program.Check(StripPost.Under(Raised, 7.8f, out lo, out _) && lo == 8f, "or just beside it (the kerb's inset)");
            Program.Check(!StripPost.Under(Raised, 5f, out _, out _), "not on a lane");
            Program.Check(!StripPost.Under(new List<(float Lo, float Hi)> { (2f, 2.25f) }, 2.1f, out _, out _), "a kerb with no room for a post is none");
            Program.Check(!StripPost.Under(null, 0f, out _, out _) && !StripPost.Under(Raised, float.NaN, out _, out _), "nothing read, nothing under");
        }

        private static void Across()
        {
            float at = StripPost.Across(8f, 9.2f, 9.1f, 0.4f, One, 0.06f, null, out bool clear);
            Program.Check(clear, "a post on the island");
            Program.Near(at, 8.6f, 1e-4f, "in the middle of a narrow strip, wherever the cursor is on it");
            at = StripPost.Across(-1.5f, 1.5f, -0.4f, 0.4f, One, 0.06f, new List<(float Lo, float Hi)>(), out clear);
            Program.Check(clear && Math.Abs(at - (-0.4f)) < 1e-4f, "on a wide one where the cursor is: " + at);
            at = StripPost.Across(-1.5f, 1.5f, -1.4f, 0.4f, One, 0.06f, null, out _);
            Program.Near(at, -1.5f + 0.4f + StripPost.BoardInset, 1e-4f, "the board kept in from the kerb");
            at = StripPost.Across(-1.5f, 1.5f, 1.4f, 1.3f, One, 0.06f, null, out clear);
            Program.Check(clear && Math.Abs(at) < 1e-4f, "a board as wide as the strip in its middle: " + at);

            // Two posts 1.2 m apart on an island of 1.2 m: no room for them.
            StripPost.Across(8f, 9.2f, 8.6f, 0.9f, Two, 0.08f, null, out clear);
            Program.Check(!clear, "two posts do not fit the island");
            at = StripPost.Across(-1.5f, 1.5f, 0f, 0.9f, Two, 0.08f, null, out clear);
            Program.Check(clear && Math.Abs(at) < 1e-4f, "they do the median");
        }

        private static void Barrier()
        {
            // A concrete barrier of 0.5 m in the middle of the island.
            var wall = new List<(float Lo, float Hi)> { (8.35f, 8.85f) };
            float at = StripPost.Across(8f, 9.2f, 8.9f, 0.4f, One, 0.06f, wall, out bool clear);
            Program.Check(clear, "a post beside the barrier: " + at);
            Program.Check(at - 0.06f >= 8.85f + StripPost.PostGap - 1e-4f || at + 0.06f <= 8.35f - StripPost.PostGap + 1e-4f, "clear of it: " + at);
            Program.Check(at + 0.06f <= 9.2f - StripPost.PostGap + 1e-4f, "and on the island: " + at);
            float other = StripPost.Across(8f, 9.2f, 8.3f, 0.4f, One, 0.06f, wall, out _);
            Program.Check(Math.Abs(other - at) < 1e-4f || (other < 8.35f) != (at < 8.35f), "the side nearest the place wanted: " + other + " / " + at);

            // With the barrier as wide as the island there is no room: the middle, and said.
            at = StripPost.Across(8f, 9.2f, 8.6f, 0.4f, One, 0.06f, new List<(float Lo, float Hi)> { (8f, 9.2f) }, out clear);
            Program.Check(!clear && Math.Abs(at - 8.6f) < 1e-4f, "no room: the middle, not clear");

            // Two posts on the median straddle a barrier in its middle.
            at = StripPost.Across(-1.5f, 1.5f, 0f, 0.9f, Two, 0.08f, new List<(float Lo, float Hi)> { (-0.25f, 0.25f) }, out clear);
            Program.Check(clear && Math.Abs(at) < 1e-4f, "two posts either side of a barrier: " + at);
        }

        private static void Traffic()
        {
            Program.Check(StripPost.Traffic(8f, 9.2f, 8.2f, Forward, Backward, out _) == 1 && StripPost.Traffic(8f, 9.2f, 9.0f, Forward, Backward, out _) == 1, "an island between two lanes of one way: that way, either side");
            Program.Check(StripPost.Traffic(-1.5f, 1.5f, 0.5f, Forward, Backward, out _) == 1, "the median, its right half: the traffic on the right");
            Program.Check(StripPost.Traffic(-1.5f, 1.5f, -0.5f, Forward, Backward, out _) == -1, "its left half: the traffic on the left");
            Program.Check(StripPost.Traffic(8f, 9.2f, 9.0f, new List<float> { 3.25f }, new List<float>(), out int side) == 1 && side == -1, "the nearest lane on the other side when the cursor's side has none");
            Program.Check(StripPost.Traffic(-9.2f, -8f, -8.5f, null, null, out side) == -1 && side == 1 && StripPost.Traffic(8f, 9.2f, 8.5f, null, null, out side) == 1 && side == -1,
                "with no lanes read, by the side of the road, its lanes on the driver's left");
        }

        private static void ArmWay()
        {
            // The island between the BRT lane (6.5) and the general lanes (11, 14.5) of the traffic along the road.
            Program.Check(StripPost.Traffic(8f, 9.2f, 9.0f, Forward, Backward, out int side) == 1 && side == 1 && StripPost.ArmToRight(1, side), "over the general lanes the island is on the traffic's left: the arm to the right");
            Program.Check(StripPost.Traffic(8f, 9.2f, 8.2f, Forward, Backward, out side) == 1 && side == -1 && !StripPost.ArmToRight(1, side), "over the BRT lane it is on its right: the arm to the left, as from the roadside");
            Program.Check(StripPost.Traffic(-1.5f, 1.5f, 0.5f, Forward, Backward, out side) == 1 && StripPost.ArmToRight(1, side), "the median, for the traffic on its right: to the right");
            Program.Check(StripPost.Traffic(-1.5f, 1.5f, -0.5f, Forward, Backward, out side) == -1 && StripPost.ArmToRight(-1, side), "and for the traffic on its left (its right is the smaller laterals): to its right too");
            Program.Check(StripPost.Traffic(-9.2f, -8f, -9.0f, Forward, Backward, out side) == -1 && StripPost.ArmToRight(-1, side), "the other island over the other general lanes");

            List<float> over = StripPost.Beyond(Forward, 8.6f, 1);
            Program.Check(over.Count == 2 && over.Contains(11f) && over.Contains(14.5f), "the lanes beyond the island");
            Program.Check(StripPost.Beyond(Forward, 8.6f, -1).Count == 2 && StripPost.Beyond(null, 0f, 1).Count == 0, "the other way, and none");
            Program.Near(Carriageway.CantileverArm(8.6f, over, 2f), 5.6f, 1e-4f, "the arm reaches over their middle with the board: " + Carriageway.CantileverArm(8.6f, over, 2f));

            // The post on the flange's width, clear of the kerbs; on a wide median where the cursor is.
            float at = StripPost.Across(8f, 9.2f, 9.1f, 0f, One, 0.3f, null, out bool clear);
            Program.Check(clear && Math.Abs(at - 8.6f) < 1e-4f, "in the island's middle: " + at);
            at = StripPost.Across(-1.5f, 1.5f, 1.4f, 0f, One, 0.3f, null, out clear);
            Program.Check(clear && at + 0.3f <= 1.5f - StripPost.PostGap + 1e-4f, "on the median, the flange inside the kerb: " + at);
        }

        private static readonly SignDef Board = new SignDef
        {
            Id = "s.board", Code = "s.board", Zh = "s", En = "s", Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = 2.0f, Height = 1.0f, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = 400, TextureHeight = 200, Keywords = new string[0]
        };

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static readonly SupportDef Left = SupportCatalog.ById(SupportCatalog.Cantilever), Right = SupportCatalog.ById(SupportCatalog.CantileverRight);

        private static void Mirror()
        {
            Program.Check(Right != null && Right.Kind == SupportKind.Cantilever && Right.Side == -1 && Right.Hidden && Left.Side == 1 && !Left.Hidden, "the mirror: a hidden cantilever reaching to -X");
            Program.Check(Left.MirrorId == Right.Id && Right.MirrorId == Left.Id && ReferenceEquals(SupportCatalog.ListedOf(Right), Left), "named as the cantilever in the panel");
            Program.Check(!new List<SupportDef>(SupportCatalog.Listed).Contains(Right) && ReferenceEquals(SupportCatalog.ByHash(Right.Hash), Right), "not listed; found by its hash in a save");
            Program.Check(Right.Clearance == Left.Clearance && Right.ArmLength == Left.ArmLength && Right.Zh == Left.Zh, "the same clearance, arm and name");

            SupportLayout l = SupportLayouts.Of(Board, Left, 6f), r = SupportLayouts.Of(Board, Right, 6f);
            Program.Check(l.ArmSide == 1f && r.ArmSide == -1f && SupportLayouts.Of(Board, SupportCatalog.ById(SupportCatalog.Gantry)).ArmSide == 1f && SupportLayouts.Of(Board, SupportCatalog.ById(SupportCatalog.SinglePole)).ArmSide == 1f, "the arm's side");
            Program.Near(r.BoardCentreX, -l.BoardCentreX, 1e-5f, "the board on the other side of the post");
            Program.Check(r.ArmLength == l.ArmLength && r.ArmY == l.ArmY && r.PoleTop == l.PoleTop && r.BoardFrom == l.BoardFrom, "everything else as it is");

            List<float> right = LaneMath.Arrange(new[] { 3.5f, -1.8f, -5.3f, -0.1f, -5.4f }, float.NegativeInfinity, -0.3f);
            Program.Check(right.Count == 2 && right[0] == -1.8f && Math.Abs(right[1] + 5.3f) < 0.2f, "the lanes to the driver's right, from the left: " + string.Join(", ", right));
        }

        private static void MirrorBuilt()
        {
            var boards = new[] { new AssemblyBoard { Instance = Inst(Board) } };
            SignParts l = SignBuilder.BuildAssembly(boards, Left, 6f), r = SignBuilder.BuildAssembly(boards, Right, 6f);
            l.Metal.Bounds(out Vec3 lMin, out Vec3 lMax);
            r.Metal.Bounds(out Vec3 rMin, out Vec3 rMax);
            Program.Near(rMin.X, -lMax.X, 1e-3f, "the arm reaches as far the other way: " + rMin.X + " / " + lMax.X);
            Program.Near(rMax.X, -lMin.X, 1e-3f, "and the post's foot is the same");
            Program.Check(l.Metal.VertexCount == r.Metal.VertexCount, "the same structure");

            // The board is moved, not mirrored: its picture reads the same.
            MeshBuilder lf = l.Faces[0].Mesh, rf = r.Faces[0].Mesh;
            Program.Check(lf.VertexCount == rf.VertexCount && lf.VertexCount > 0, "the same face");
            float dx = rf.Positions[0].X - lf.Positions[0].X;
            bool moved = true;
            for (int i = 0; i < lf.VertexCount; i++)
                moved &= Math.Abs(rf.Positions[i].X - lf.Positions[i].X - dx) < 1e-4f && Math.Abs(rf.Positions[i].Y - lf.Positions[i].Y) < 1e-5f && Math.Abs(rf.Positions[i].Z - lf.Positions[i].Z) < 1e-5f;
            Program.Check(moved && dx < -1f, "every point of the face moved by the same: " + dx);

            PickShape arm = r.Picks.Find(p => p.Name == "arm");
            Program.Check(arm != null && arm.Max.X <= 1e-4f && arm.Min.X < -5f && r.Picks.FindAll(p => p.Name == "arm").Count == 1, "the arm is pointed at where it is");
            PickShape board = r.Picks.Find(p => p.Name == "board");
            Program.Check(board != null && board.Max.X < 0f, "and the board");
            Program.Check(SignBuilder.BuildAssembly(boards, Right, 6f, SupportDetail.Reduced).Metal.VertexCount > 0, "in reduced detail");
        }

        private static void MirrorBoards()
        {
            // Over a lane at its X (the lanes are at -X now), clamped to the arm.
            AssemblyPlan plan = Assembly.Plan(new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(Board), Lane = 1, OffsetX = -4f } }, Right, 7f);
            Program.Near(plan.Placed[0].Cx, -4f, 1e-4f, "over its lane");
            plan = Assembly.Plan(new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(Board), Lane = 1, OffsetX = 0.5f } }, Right, 7f);
            Assembly.LaneBounds(plan.Layout, 1f, out float lo, out _);
            Program.Near(plan.Placed[0].Cx, -lo, 1e-4f, "a place on the wrong side is the nearest on the arm");
            Program.Check(Assembly.RequiredReach(new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(Board), Lane = 1, OffsetX = -8f } }, SupportKind.Cantilever) > 8f, "the arm grows for a board far out");

            // A second board goes beside the first, on the arm.
            var first = new AssemblyBoard { Instance = Inst(Board), Member = 0 };
            AttachResult two = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { first }, Support = Right, Reach = 7f, Board = Inst(Board) });
            Program.Check(two.Ok, "a second board: " + two.Reason);
            Program.Check(two.Offset < 0f && two.Free.TrueForAll(s => s.Hi <= 1e-4f), "on the arm's side: " + two.Offset);
            AssemblyPlan both = Assembly.Plan(two.Boards, Right, two.Reach);
            Program.Check(both.Placed.TrueForAll(p => p.Cx < 0f) && Assembly.Overlaps(both.Placed, true).Count == 0, "both on the arm, apart");

            // Asked far out, the arm grows to it.
            AttachResult far = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard> { first }, Support = Right, Reach = 7f, Board = Inst(Board), Offset = -11f, Grow = true });
            Program.Check(far.Ok && far.Offset < -9f && far.Reach > 7f, "out past the end it grows: " + far.Offset + ", " + far.Reach);
        }
    }
}
