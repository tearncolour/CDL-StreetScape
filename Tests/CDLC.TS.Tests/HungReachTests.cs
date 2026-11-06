using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The span of a gantry and the arm of a cantilever give back what a board took when it is taken off or moved in (0.12.9, Assembly.ReachFor; the owner: "悬架原本的路牌删除或者移动之后，长度应该能自动缩小").
    /// The support's own length is what it was placed with or the player set; boards make it longer as they need, in half metres, as Attach does when one is put on.
    /// </summary>
    internal static class HungReachTests
    {
        public static void RunAll()
        {
            Program.Run("hung reach: a cantilever's arm after a board is taken off or moved", ArmShrinks);
            Program.Run("hung reach: a gantry's span, a double cantilever, and supports that are not hung", OtherSupports);
        }

        private static SignInstance Inst(string id) => SignInstances.Of(SignCatalog.ById(id), null);

        private static AssemblyBoard B(string id, int member, int lane = 0, float x = 0f) => new AssemblyBoard { Instance = Inst(id), Member = member, Slot = member, Lane = lane, OffsetX = x };

        private static List<AssemblyBoard> Without(List<AssemblyBoard> boards, int member) => boards.FindAll(b => b.Member != member);

        private static float Length(List<AssemblyBoard> boards, SupportDef support, float reach) => Assembly.Plan(Assembly.Order(boards), support, reach).Layout.ArmLength;

        private static void ArmShrinks()
        {
            // A cantilever placed with its arm over the lanes, 6 m (its own); a no-horns board on it. A guide board put on far out makes the arm longer.
            SupportDef arm = SupportCatalog.ById(SupportCatalog.Cantilever);
            const float own = 6f;
            var first = new List<AssemblyBoard> { B("gb.prohib.34", 0) };
            float before = Length(first, arm, own);
            AttachResult joined = Attach.Evaluate(new AttachRequest { Existing = first, Support = arm, Reach = own, Board = Inst("gb.prohib.12"), Offset = 9f });
            Program.Check(joined.Ok && joined.Grew && joined.Reach > own, "a board put on beyond the end makes the arm longer: " + joined.Reach);
            List<AssemblyBoard> both = joined.Boards;
            Program.Near(Assembly.ReachFor(both, arm, own), joined.Reach, 1e-4f, "with both boards on it the arm keeps the length it was given when the second was put on");

            // The far board taken off: the arm is its own again.
            int far = joined.NewMember;
            List<AssemblyBoard> nearOnly = Without(both, far);
            float back = Assembly.ReachFor(nearOnly, arm, own);
            Program.Near(back, own, 1e-4f, "the far board taken off: the arm's own length is kept, " + back);
            Program.Near(Length(nearOnly, arm, back), before, 1e-3f, "as long as it was before the board was put on: " + before);
            Program.Check(Length(nearOnly, arm, back) < Length(both, arm, joined.Reach) - 1f, "shorter than with the far board");

            // The near board taken off instead: the far one stays where it is over the road, and the arm is what it needs.
            List<AssemblyBoard> farOnly = Without(both, 0);
            AssemblyBoard farBoard = farOnly[0];
            float farNeed = Math.Abs(farBoard.OffsetX) + farBoard.Instance.Def.Width * 0.5f - Assembly.ArmTipInside;
            Program.Near(Assembly.ReachFor(farOnly, arm, own), (float)Math.Ceiling(farNeed * 2f) / 2f, 1e-4f, "the near board taken off: the far one still needs its length");

            // The far board moved in (the arrow keys, or a lane nearer the post): the arm follows it in, not shorter than its own.
            var movedIn = new List<AssemblyBoard>();
            foreach (AssemblyBoard b in both)
                movedIn.Add(new AssemblyBoard { Instance = b.Instance, Member = b.Member, Slot = b.Slot, Lane = b.Lane, OffsetX = b.Member == far ? b.OffsetX - 2f : b.OffsetX });
            float inReach = Assembly.ReachFor(movedIn, arm, own);
            Program.Check(inReach < joined.Reach - 1.5f && inReach >= own, "moved 2 m in, the arm is shorter: " + joined.Reach + " -> " + inReach);
            Program.Near(inReach * 2f, (float)Math.Round(inReach * 2f), 1e-4f, "in half metres");
            var movedFurther = new List<AssemblyBoard>();
            foreach (AssemblyBoard b in both)
                movedFurther.Add(new AssemblyBoard { Instance = b.Instance, Member = b.Member, Slot = b.Slot, Lane = b.Lane, OffsetX = b.Member == far ? 2.6f : b.OffsetX });
            Program.Near(Assembly.ReachFor(movedFurther, arm, own), own, 1e-4f, "moved in within the arm's own length: back to it");

            // Onto the post: it takes nothing of the arm.
            var onPost = new List<AssemblyBoard>();
            foreach (AssemblyBoard b in both)
                onPost.Add(new AssemblyBoard { Instance = b.Instance, Member = b.Member, Slot = b.Slot, Lane = b.Member == far ? Assembly.OnPost : b.Lane, OffsetX = b.Member == far ? 0f : b.OffsetX });
            Program.Near(Assembly.ReachFor(onPost, arm, own), own, 1e-4f, "a board moved onto the post gives the arm back");

            // A length the player set by hand longer than the boards need stays.
            Program.Near(Assembly.ReachFor(nearOnly, arm, 11f), 11f, 1e-4f, "a length of the player's own is kept");
            // The support's default (0) stays the default while the boards fit in it.
            Program.Near(Assembly.ReachFor(first, arm, 0f), 0f, 1e-4f, "the default stays the default");
        }

        private static void OtherSupports()
        {
            // A gantry fitted to the road, 14 m: a board over a lane at 16 m made it 18.5 m; taken off, 14 m again.
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            var boards = new List<AssemblyBoard> { B("gb.prohib.34", 0, 1, 5f), B("gb.prohib.12", 1, 2, 16f) };
            float grown = Assembly.ReachFor(boards, gantry, 14f);
            Program.Check(grown > 14f && grown >= 16f + 0.4f + Assembly.GantryClear - 1e-3f, "the gantry is as long as the far board needs: " + grown);
            Program.Near(Assembly.ReachFor(Without(boards, 1), gantry, 14f), 14f, 1e-4f, "the far board taken off: the span the road gave");
            Program.Check(Length(Without(boards, 1), gantry, 14f) < Length(boards, gantry, grown) - 3f, "and its far post comes back");

            // A double cantilever: either arm's farthest board counts.
            SupportDef twin = SupportCatalog.ById(SupportCatalog.DoubleCantilever);
            var twinBoards = new List<AssemblyBoard> { B("gb.prohib.34", 0, Attach.LaneHand, 3f), B("gb.prohib.12", 1, Attach.LaneHand, -9f) };
            Program.Check(Assembly.ReachFor(twinBoards, twin, 0f) >= 9f + 0.4f, "the longer arm's board sets both arms");
            Program.Near(Assembly.ReachFor(Without(twinBoards, 1), twin, 0f), 0f, 1e-4f, "taken off: the default arms");

            // Not hung: the reach is what it was (on a wall it is the height of the lower edge).
            Program.Near(Assembly.ReachFor(boards, SupportCatalog.ById(SupportCatalog.SinglePole), 3f), 3f, 0f, "a post has no reach to change");
            Program.Near(Assembly.ReachFor(boards, SupportCatalog.ById(SupportCatalog.WallFlat), 3.3f), 3.3f, 0f, "nor a wall");
        }
    }
}
