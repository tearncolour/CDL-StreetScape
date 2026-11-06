using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 (the owner via the RIE session: "路牌在红绿灯支架上也应该支持左右位置和上下位置调整"): a board on another mod's signal arm moves along it by the free stretches (HostPick.StepOnArm) and up
    /// and down within the room the arm's heads leave (HostArm.Room, RIE 0.2.12's contract: below and above the arm's axis for each stretch of Free; HostPick.RoomAt, DropRange); how far it is moved is
    /// the item's OffsetX, drawn by Assembly.Arrange.
    /// </summary>
    internal static class ArmAdjustTests
    {
        public static void RunAll()
        {
            Program.Run("arm adjust: the room of a stretch (HostArm.Room)", Room);
            Program.Run("arm adjust: the range up and down", Range);
            Program.Run("arm adjust: a step along the arm by the free places", Step);
            Program.Run("arm adjust: the board drawn where it was moved", Drawn);
        }

        private static HostArm Arm(float[] room) => new HostArm { X = 0f, Y = 6.2f, Z = 0f, DirX = 1f, DirZ = 0f, Length = 8f, Radius = 0.12f, TipRadius = 0.06f, FaceX = 0f, FaceZ = -1f, Free = new[] { 0.3f, 3.0f, 3.8f, 7.9f }, Room = room, Id = 7 };

        private static void Room()
        {
            HostArm with = Arm(new[] { 0.4f, 0.5f, 1.5f, 0.2f });
            HostPick.RoomAt(with, 1.0f, out float below, out float above);
            Program.Check(below == 0.4f && above == 0.5f, $"the first stretch's room ({below}, {above})");
            HostPick.RoomAt(with, 5.0f, out below, out above);
            Program.Check(below == 1.5f && above == 0.2f, $"the second stretch's ({below}, {above})");
            HostPick.RoomAt(with, 3.2f, out below, out above);
            Program.Check(below == 0.4f && above == 0.5f, "in the gap between them: the nearer stretch's (3.2 is 0.2 from the first, 0.6 from the second)");
            HostPick.RoomAt(with, 3.6f, out below, out above);
            Program.Check(below == 1.5f && above == 0.2f, "and 3.6: the second's");
            HostPick.RoomAt(Arm(null), 1f, out below, out above);
            Program.Check(below == HostPick.DefaultBelow && above == HostPick.DefaultAbove && below == 0.5f && above == 0.3f, "an older RIE that gives none: 0.5 below, 0.3 above");
            HostPick.RoomAt(Arm(new[] { 1f }), 1f, out below, out above);
            Program.Check(below == HostPick.DefaultBelow && above == HostPick.DefaultAbove, "a Room that does not pair with Free is not taken");
            HostPick.RoomAt(new HostArm { Length = 5f, Room = new[] { 1f, 1f } }, 1f, out below, out above);
            Program.Check(below == HostPick.DefaultBelow, "an arm that lists no stretches: the defaults");
        }

        private static void Range()
        {
            // A board 0.6 m high hangs with the arm through its middle (ArmInset 0.3): from -0.3 to +0.3 about the axis.
            HostPick.DropRange(0.6f, 0f, 1.5f, 0.5f, out float lowest, out float highest);
            Program.Check(Math.Abs(lowest - (0.6f - 0.3f - 1.5f)) < 1e-5f && Math.Abs(highest - (0.5f - 0.3f)) < 1e-5f, $"room 1.5 under, 0.5 over: from {lowest:0.00} to {highest:0.00}");
            // Lower edge at the room's bottom: -0.3 + lowest = -1.5.
            Program.Check(Math.Abs((-0.3f + lowest) - -1.5f) < 1e-5f && Math.Abs((0.3f + highest) - 0.5f) < 1e-5f, "the lower edge at -1.5, the top at +0.5 at the ends");
            // A board that does not fit the room where it hangs (0.6 high, 0.2 over): it cannot go higher, and can still go down.
            HostPick.DropRange(0.6f, 0f, 1.5f, 0.1f, out lowest, out highest);
            Program.Check(highest == 0f && lowest < 0f, "no room for it over the axis: it stays, and goes down");
            HostPick.DropRange(0.6f, 0f, 0.2f, 0.5f, out lowest, out highest);
            Program.Check(lowest == 0f && highest > 0f, "no room under: it goes up only");
            HostPick.DropRange(1.2f, 0.1f, 0.5f, 0.3f, out lowest, out highest);
            float inset = SupportLayouts.ArmInset(1.2f);
            Program.Check(lowest == 0f && Math.Abs(highest - Math.Max(0f, 0.3f - inset - 0.1f)) < 1e-5f, "a plate on top takes room over the axis");
        }

        private static void Step()
        {
            HostArm arm = Arm(null);
            Program.Near(HostPick.StepOnArm(arm, 1f, 0.6f, 1, 0.1f), 1.1f, 1e-5f, "to the right of the arm (toward the tip) by a step");
            Program.Near(HostPick.StepOnArm(arm, 1f, 0.6f, -1, 0.1f), 0.9f, 1e-5f, "toward the root");
            float next = HostPick.StepOnArm(arm, 2.7f, 0.6f, 1, 0.1f);
            Program.Check(Math.Abs(next - 4.1f) < 1e-5f, $"at the end of its stretch (3.0 - 0.3 = 2.7) it goes on to the next stretch's near edge (3.8 + 0.3 = 4.1): {next:0.00}");
            Program.Near(HostPick.StepOnArm(arm, 4.1f, 0.6f, -1, 0.1f), 2.7f, 1e-5f, "and back to the first stretch's far edge");
            Program.Check(float.IsNaN(HostPick.StepOnArm(arm, 7.6f, 0.6f, 1, 0.1f)), "nowhere further out than the tip (7.9 - 0.3 = 7.6)");
            Program.Check(float.IsNaN(HostPick.StepOnArm(arm, 0.6f, 0.6f, -1, 0.1f)), "nor nearer the post than 0.3 + 0.3");
            Program.Near(HostPick.StepOnArm(arm, 1f, 0.6f, 1, 0.5f), 1.5f, 1e-5f, "a coarse step (Shift) of 0.5");
            Program.Near(HostPick.StepOnArm(arm, 2.5f, 0.6f, 1, 0.5f), 2.7f, 1e-5f, "a coarse step that would pass its stretch's end: to the end");
            // The stretches the other boards take are left out first (WithoutTaken).
            arm.Free = HostPick.WithoutTaken(arm.Free, new List<(float From, float To)> { (4.5f, 5.5f) });
            // What is left is 0.3 to 3.0, 3.8 to 4.5 and 5.5 to 7.9: a board 0.6 wide goes from 4.1 to 4.2 in the middle one, and then on to 5.8 (5.5 + 0.3).
            Program.Near(HostPick.StepOnArm(arm, 4.1f, 0.6f, 1, 0.1f), 4.2f, 1e-5f, "a step inside what a board on the arm leaves");
            Program.Near(HostPick.StepOnArm(arm, 4.2f, 0.6f, 1, 0.1f), 5.8f, 1e-5f, "past the taken part's edge to the next free place");
        }

        private static void Drawn()
        {
            SupportDef support = SupportCatalog.ById(SupportCatalog.AttachedArm);
            SignDef def = SignCatalog.ById("gb17733.street");
            ItemRecord Record(float up) => new ItemRecord(def.Hash, new Vec3(0f, 0f, 0f), 0f, support.Hash, null, 0.1f, 0u, 0, 0, up);
            SignParts level = ChunkBuilder.PartsOfRecord(Record(0f)), down = ChunkBuilder.PartsOfRecord(Record(-0.3f)), upward = ChunkBuilder.PartsOfRecord(Record(0.2f));
            level.Face.Bounds(out Vec3 lo, out Vec3 hi);
            down.Face.Bounds(out Vec3 dlo, out Vec3 dhi);
            upward.Face.Bounds(out Vec3 ulo, out Vec3 uhi);
            Program.Check(Math.Abs(dlo.Y - (lo.Y - 0.3f)) < 1e-4f && Math.Abs(dhi.Y - (hi.Y - 0.3f)) < 1e-4f, $"moved down 0.3 m: the board 0.3 lower ({lo.Y:0.000} -> {dlo.Y:0.000})");
            Program.Check(Math.Abs(ulo.Y - (lo.Y + 0.2f)) < 1e-4f, "moved up 0.2 m");
            Program.Check(!ReferenceEquals(level, down) && !ReferenceEquals(down, upward), "a negative move is drawn by its own assembly, not the plain board's cache");
            // The arm stays where it is: the clamps are round it at the axis (y = 0), the channels run up to them from the board that hangs under it.
            float topOfMetal = float.MinValue;
            foreach (Vec3 p in down.Metal.Positions)
                topOfMetal = Math.Max(topOfMetal, p.Y);
            Program.Check(topOfMetal > 0f - 1e-4f && dhi.Y < 0.05f, $"a board moved down under the arm is held by clamps on it ({topOfMetal:0.000} over the axis)");
            // A board moved right up (its bottom over the axis) is held from below.
            ItemRecord high = Record(0.9f);
            SignParts over = ChunkBuilder.PartsOfRecord(high);
            over.Face.Bounds(out Vec3 olo, out _);
            float bottomOfMetal = float.MaxValue;
            foreach (Vec3 p in over.Metal.Positions)
                bottomOfMetal = Math.Min(bottomOfMetal, p.Y);
            Program.Check(olo.Y > 0.1f && bottomOfMetal < 0.02f, $"a board above the arm is clamped to it from below ({bottomOfMetal:0.000})");
            // Its channels run down from the board's back to the clamps on the arm (nothing else is at the back plane lower than the board).
            float channelLow = float.MaxValue;
            foreach (Vec3 p in over.Metal.Positions)
            {
                if (p.Z > over.Layout.BackZ - 1e-4f && p.Y < olo.Y - 0.2f)
                    channelLow = Math.Min(channelLow, p.Y);
            }

            Program.Check(channelLow < 0.02f, $"the channels reach down to the arm, so the board does not float over it ({channelLow:0.000})");
            // The item keeps its place where Assembly arranges it (the board's Bottom is the default one plus the move).
            var board = new AssemblyBoard { Instance = SignInstances.Of(def, null), OffsetX = -0.25f };
            AssemblyPlan plan = Assembly.Plan(new List<AssemblyBoard> { board }, support, 0.1f);
            Program.Check(Math.Abs(plan.Placed[0].Bottom - (plan.Layout.BottomClearance - 0.25f)) < 1e-5f, "Assembly.Arrange: Bottom is the layout's plus OffsetX");
            Program.Check(!Assembly.ByHand(SupportKind.AttachedArm, new[] { new AssemblyBoard { Instance = board.Instance, OffsetX = 0.4f } }), "OffsetX on an arm is not a height by hand");
        }
    }
}
