using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The F-type cantilever (0.12.31; the owner: "F悬臂不是上下挂很多牌子用的，而是国内常见的一个牌子后有两个悬臂杆支持这样"): one post, two arms one above the other reaching the same way, both behind the
    /// boards, which stand side by side in front of them as on a single cantilever.
    /// </summary>
    internal static class FCantileverTests
    {
        public static void RunAll()
        {
            Program.Run("F type: the supports and the names", Catalogue);
            Program.Run("F type: two arms behind the boards", Layout);
            Program.Run("F type: the boards stand where a single cantilever's do", Placement);
            Program.Run("F type: putting boards on", Attaching);
            Program.Run("F type: the mirror", Mirror);
            Program.Run("F type: built", Built);
            Program.Run("F type: the items of 0.12.26 to 0.12.30 (rows of boards) stand on one level", OldItems);
        }

        private static SignDef Make(string id, string group, float w, float h, ShapeKind shape = ShapeKind.RoundRect) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = group, Shape = shape, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Big = Make("f.big", "guide", 4.0f, 2.4f), Mid = Make("f.mid", "guide", 1.6f, 1.0f), Low = Make("f.low", "guide", 1.2f, 0.4f), Round = Make("f.round", "prohibition", 0.9f, 0.9f, ShapeKind.Circle);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int lane = 0, float x = 0f) => new AssemblyBoard { Instance = Inst(def), Member = member, Slot = member, Lane = lane, OffsetX = x };

        private static readonly SupportDef F = SupportCatalog.ById(SupportCatalog.FCantilever), FRight = SupportCatalog.ById(SupportCatalog.FCantileverRight), Single = SupportCatalog.ById(SupportCatalog.Cantilever);

        private static AssemblyPlan PlanOf(SupportDef support, params AssemblyBoard[] boards) => Assembly.Plan(Assembly.Order(boards), support, 0f);

        private static Placement Of(AssemblyPlan plan, int member) => plan.Placed.Find(p => p.Board.Member == member);

        private static void Catalogue()
        {
            Program.Check(F != null && F.Kind == SupportKind.FCantilever && FRight != null && FRight.Kind == SupportKind.FCantilever, "the support and its mirror exist");
            Program.Check(F.Id == "cantilever.multi" && FRight.Id == "cantilever.multi.r", "under the ids of 0.12.26, which saved items name");
            Program.Check(F.Zh == "F 型悬臂式" && F.En == "F-type cantilever" && FRight.Zh == F.Zh, "named the F type");
            Program.Check(F.Side == 1 && FRight.Side == -1 && F.MirrorId == FRight.Id && FRight.MirrorId == F.Id && !F.Hidden && FRight.Hidden && ReferenceEquals(SupportCatalog.ListedOf(FRight), F), "named for each other, the mirror hidden");
            Program.Check(SupportKinds.Hung(SupportKind.FCantilever) && SupportKinds.Arm(SupportKind.FCantilever) && SupportKinds.OneWayArm(SupportKind.FCantilever) && SupportKinds.PostBoards(SupportKind.FCantilever), "hung, an arm, one way, with boards on its post");
            Program.Check(Assembly.MaxFor(SupportKind.FCantilever) == Assembly.MaxBoards, "four boards, as on a single cantilever");
            Program.Check(ReferenceEquals(SupportCatalog.Facing(F, true), FRight) && ReferenceEquals(SupportCatalog.Facing(FRight, false), F) && ReferenceEquals(SupportCatalog.Facing(F, false), F), "the one that reaches the way asked");
            Program.Check(TsMarkers.KindOf(SupportKind.FCantilever) == TsMarkers.KindCantilever, "MiniMap sees a cantilever");
        }

        private static void Layout()
        {
            // 22MR601 pp.78-85: 1F up to 2.3 m², 2F 0.9 m apart up to 5 m², 3F 1.2 m apart above (as far apart as the board's height less 0.3 m allows), centred on the board; the F type two at least.
            SupportLayout l = PlanOf(F, B(Big, 0)).Layout;
            Program.Check(l.ArmYs != null && l.ArmYs.Length == 3 && l.ArmYs[0] > l.ArmYs[1] && l.ArmYs[1] > l.ArmYs[2], "a board of 9.6 m²: three arms, the upper first");
            float bottom = l.BottomClearance, top = l.BottomClearance + Big.Height;
            Program.Near(l.BottomClearance, 5.5f, 1e-4f, "the board's lower edge at the clearance");
            Program.Near(l.ArmYs[0] - l.ArmYs[1], 1.05f, 1e-4f, "1.2 m apart, but no further than the 2.4 m board allows (2.1 / 2)");
            Program.Near((l.ArmYs[0] + l.ArmYs[2]) * 0.5f, (top + bottom) * 0.5f, 1e-4f, "centred on the board");
            Program.Near(l.ArmY, l.ArmYs[0], 1e-5f, "ArmY is the upper arm's");
            Program.Near(l.PoleTop, l.ArmY + SupportLayouts.ArmPostAbove, 1e-5f, "the post 0.3 m over the upper arm");
            Program.Check(l.PostTop < l.ArmYs[2] - SupportLayouts.JointFlangeOf(l), "the small boards of the post are under the lower arm's flange: " + l.PostTop);
            Program.Near(l.PoleRadius, 0.1885f, 1e-5f, "a φ377 post for 9.6 m²");

            SupportLayout mid = PlanOf(F, B(Mid, 0)).Layout;
            Program.Check(mid.ArmYs.Length == 2 && Math.Abs(mid.ArmYs[0] - mid.ArmYs[1] - 0.7f) < 1e-4f, "a board of 1.6 m on the F type: two arms 0.7 m apart (1.0 m less 0.3)");
            Program.Check(PlanOf(Single, B(Mid, 0)).Layout.ArmYs.Length == 1, "the single cantilever one");

            // A low board: the arms keep their room, as far apart as the least spacing.
            SupportLayout low = PlanOf(F, B(Low, 0)).Layout;
            Program.Near(low.ArmYs[0] - low.ArmYs[1], SupportLayouts.MinArmSpacing, 1e-4f, "on a low board the arms are the least apart");

            Program.Check(PlanOf(SupportCatalog.ById(SupportCatalog.Gantry), B(Big, 0)).Layout.ArmYs == null, "a gantry none");
        }

        private static void Placement()
        {
            AssemblyPlan f = PlanOf(F, B(Mid, 0), B(Round, 1), B(Round, 2, Assembly.OnPost));
            AssemblyPlan single = PlanOf(Single, B(Mid, 0), B(Round, 1), B(Round, 2, Assembly.OnPost));
            Placement a = Of(f, 0), b = Of(f, 1);
            Program.Near(a.Bottom, f.Layout.BottomClearance, 1e-4f, "the boards' lower edges level at the clearance");
            Program.Near(b.Bottom, f.Layout.BottomClearance, 1e-4f, "both");
            Program.Check(Math.Abs(a.Cx - b.Cx) >= (a.Def.Width + b.Def.Width) * 0.5f + Assembly.SideGap - 1e-3f, "side by side, clear of each other");
            Program.Check(Math.Min(a.Cx - a.Def.Width * 0.5f, b.Cx - b.Def.Width * 0.5f) >= f.Layout.BoardFrom - 1e-3f && Of(f, 0).Cx == Of(single, 0).Cx && Of(f, 1).Cx == Of(single, 1).Cx, "where a single cantilever's boards are");
            Program.Check(Assembly.Overlaps(f.Placed, true).Count == 0, "none overlaps");
            Placement post = Of(f, 2);
            Program.Check(post.OnPost && post.Top <= f.Layout.PostTop + 1e-3f, "the board on the post is under the lower arm");
            Program.Near(f.Layout.ArmLength, single.Layout.ArmLength, 1e-4f, "the arms as long as a single cantilever's");

            // Over lanes: the offsets along the arms.
            AssemblyPlan lanes = PlanOf(F, B(Mid, 0, 1, 6f), B(Mid, 1, 2, 9.5f));
            Program.Near(Of(lanes, 0).Cx, 6f, 1e-3f, "over its lane");
            Program.Near(Of(lanes, 1).Cx, 9.5f, 1e-3f, "and the other over its own");
            Program.Check(lanes.Layout.ArmLength >= 9.5f + Mid.Width * 0.5f - Assembly.ArmTipInside - 1e-3f, "the arms reach the far one: " + lanes.Layout.ArmLength);
            List<KeyValuePair<int, float>> spread = Assembly.SpreadOverLanes(new[] { B(Mid, 0), B(Mid, 1) }, new List<float> { 10f, 6f });
            Program.Check(spread[0].Key == 1 && spread[1].Key == 2, "the boards go over the lanes from the driver's left, one after the other");
        }

        private static AttachResult Put(List<AssemblyBoard> existing, SignDef add, SupportDef support, float offset = float.NaN) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Board = Inst(add), Offset = offset, Grow = true });

        private static void Attaching()
        {
            AttachResult first = Put(new List<AssemblyBoard>(), Big, F);
            Program.Check(first.Ok && first.Hung && first.MaxBoards == Assembly.MaxBoards, "the first board");
            AttachResult beside = Put(new List<AssemblyBoard> { B(Mid, 0) }, Mid, F);
            AssemblyPlan made = Assembly.Plan(beside.Boards, F, beside.Reach);
            Program.Check(beside.Ok && Of(made, beside.NewMember).Bottom == Of(made, 0).Bottom && Assembly.Overlaps(made.Placed, true).Count == 0, "a second beside the first, level with it");
            var four = new List<AssemblyBoard> { B(Round, 0), B(Round, 1), B(Round, 2), B(Round, 3) };
            AttachResult fifth = Put(four, Round, F);
            Program.Check(!fifth.Ok && fifth.Reason == AttachReason.Full, "a fifth is refused: " + fifth.Reason);
        }

        private static void Mirror()
        {
            AssemblyPlan left = PlanOf(F, B(Mid, 0));
            AssemblyPlan right = PlanOf(FRight, B(Mid, 0));
            Program.Check(left.Layout.ArmSide == 1f && right.Layout.ArmSide == -1f, "the arms' side");
            Program.Near(Of(right, 0).Cx, -Of(left, 0).Cx, 1e-4f, "the board on the other side of the post");
            Program.Check(right.Layout.ArmYs[0] == left.Layout.ArmYs[0] && right.Layout.ArmYs[1] == left.Layout.ArmYs[1], "the arms at the same heights");
        }

        private static void Built()
        {
            var boards = new[] { B(Big, 0), B(Round, 1, Assembly.OnPost) };
            SignParts parts = SignBuilder.BuildAssembly(boards, F, 0f);
            SupportLayout l = parts.Layout;
            Program.Check(parts.Faces.Count == 2, "two faces");
            List<PickShape> arms = parts.Picks.FindAll(p => p.Name == "arm");
            Program.Check(arms.Count == 3 && arms[0].Min.Y > arms[1].Min.Y && Math.Abs(arms[0].Max.X - arms[1].Max.X) < 1e-4f, "three arms to point at, one over the other, as long");

            // The big board is crossed by both arms' axes, and has a channel at each (its rails are at the heights it is clamped at).
            FacePart face = parts.Faces.Find(p => p.Instance.Def.Id == Big.Id);
            face.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Check(Array.TrueForAll(l.ArmYs, y => y > lo.Y && y < hi.Y), "the arms pass behind the board");
            parts.Metal.Bounds(out Vec3 mLo, out Vec3 mHi);
            Program.Check(mHi.Y >= l.PoleTop && mHi.X >= l.ArmLength - 1e-3f && mHi.X <= l.ArmLength + 0.05f, "the metal reaches the top of the post and the end of the arms");

            // No brace (22MR601: none on any cantilever): no metal between the arms' joints and the board's inner edge, under the lower arm.
            float inner = Of(Assembly.Plan(Assembly.Order(boards), F, 0f), 0).Cx - Big.Width * 0.5f;
            float joint = l.PoleRadius + SupportLayouts.JointStub + SupportLayouts.JointPlate + 0.25f;
            bool brace = false;
            foreach (Vec3 v in parts.Metal.Positions)
                brace |= v.X > joint && v.X < inner - 0.05f && v.Y < l.ArmYs[2] - l.ArmRootRadius - 0.02f && v.Y > l.PostTop + 0.2f;
            Program.Check(!brace, "the arms are bolted to the post, with no brace under them");

            // Against the single cantilever with a board of 1.6 m²: one more arm.
            SignParts single = SignBuilder.BuildAssembly(new[] { B(Mid, 0) }, Single, 0f), f = SignBuilder.BuildAssembly(new[] { B(Mid, 0) }, F, 0f);
            Program.Check(f.Picks.FindAll(p => p.Name == "arm").Count == single.Picks.FindAll(p => p.Name == "arm").Count + 1, "one arm more than the single cantilever");
            Program.Check(SignBuilder.BuildAssembly(boards, F, 0f, SupportDetail.Reduced).Faces.Count == 2 && SignBuilder.BuildAssembly(boards, FRight, 0f).Faces.Count == 2, "in reduced detail and mirrored");
        }

        private static void OldItems()
        {
            // A group of 0.12.26 to 0.12.30 had a board on each of two arms (the arm was in the top bits of the items' flags, which nothing reads now): the boards stand side by side on one level.
            SignDef sign = SignCatalog.ById("gb.prohib.09");
            var first = new ItemRecord(sign.Hash, Vec3.Zero, 0f, F.Hash, null, 6f, 77u, 0, 0, 0f);
            var second = new ItemRecord(sign.Hash, Vec3.Zero, 0f, F.Hash, null, 6f, 77u, 1, 0, 0f);
            SignParts drawn = ChunkBuilder.PartsOfGroup(new[] { first, second });
            Program.Check(drawn.Faces.Count == 2 && drawn.Layout.ArmYs.Length == 2, "two boards, two arms");
            drawn.Faces[0].Mesh.Bounds(out Vec3 lo0, out Vec3 hi0);
            drawn.Faces[1].Mesh.Bounds(out Vec3 lo1, out Vec3 hi1);
            Program.Check(Math.Abs(lo0.Y - lo1.Y) < 1e-3f && (hi0.X < lo1.X || hi1.X < lo0.X), "level and side by side: " + lo0.Y + " / " + lo1.Y);
        }
    }
}
