using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.96: the road the camera's ray points at when the game's ray stopped at the ground above it (RoadUnderCursor), with the numbers of the owner's log of a cutting: a road 15 m wide whose
    /// surface lies 8.3 m under the terrain hit at 408 m, seen from above at a slant, so that the ray's place at the terrain's height is metres off the road. The place on the road's surface, inside the
    /// width and the margin and not beyond, the depth under the ground hit (a road on the ground is the game's own ray's), the road the ray meets first, a bend, and what is no road.
    /// </summary>
    internal static class RoadUnderCursorTests
    {
        private const float Ground = 408f, Centre = 399.7f, Rise = 0.2f, Surface = Centre + Rise;

        public static void RunAll()
        {
            Program.Run("road under the cursor: the place on the road's surface, not the terrain's", Place);
            Program.Run("road under the cursor: inside the width and the margin, not beyond it", Width);
            Program.Run("road under the cursor: only a road under the ground the ray hit", Depth);
            Program.Run("road under the cursor: of two roads the one the ray meets first", First);
            Program.Run("road under the cursor: a bend, and what is no road", Odd);
        }

        // A road 15 m wide along +Z from z = 0 to 200 at x = 0 (the centre line at 399.7, the surface 0.2 above).
        private static RoadUnderCursor.Candidate Straight(float x, float y, float width = 15f) =>
            new RoadUnderCursor.Candidate { Curve = Bezier3.Line(new Vec3(x, y, 0f), new Vec3(x, y, 200f)), Width = width, Rise = Rise };

        // The camera's ray through a place: from a camera up and to the side, on to three times as far.
        private static void RayThrough(Vec3 target, out Vec3 from, out Vec3 to)
        {
            from = new Vec3(target.X + 60f, 650f, target.Z - 90f);
            to = from + (target - from) * 3f;
        }

        private static void Place()
        {
            var roads = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre) };
            var target = new Vec3(2f, Surface, 100f);
            RayThrough(target, out Vec3 from, out Vec3 to);
            // What the game's ray reported: the terrain at 408, where the ray crosses that height: not on the road's surface.
            float atGround = (from.Y - Ground) / (from.Y - to.Y);
            Vec3 terrain = from + (to - from) * atGround;
            Program.Check(Vec3.DistanceXZ(terrain, target) > 3f, "the ray's place at the terrain's height is more than 3 m off the place on the road (the log's nearest hits were 3.0 and 3.4 m): " + Vec3.DistanceXZ(terrain, target).ToString("0.0"));
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out RoadUnderCursor.Found found) && found.Index == 0, "the road is found");
            Program.Check(Vec3.DistanceXZ(found.Hit, target) < 1e-2f && System.Math.Abs(found.Hit.Y - Surface) < 1e-2f, "its place is the cursor's place on the surface: " + found.Hit.X.ToString("0.00") + ", " + found.Hit.Y.ToString("0.00") + ", " + found.Hit.Z.ToString("0.00"));
            Program.Near(found.T, 0.5f, 2e-3f, "halfway along the road");
            Program.Near(found.Centre.Y, Centre, 1e-3f, "the centre line's point is at the centre line's height");
            Program.Near(found.Outside, 2f - 7.5f, 1e-2f, "5.5 m inside the edge (2 m from the centre line, half width 7.5)");
            Program.Check(!RoadUnderCursor.Pick(roads, from, from, Ground, out _), "a ray that does not go down is no ray");
            Program.Check(!RoadUnderCursor.Pick(roads, to, from, Ground, out _), "nor one that goes up");
        }

        private static void Width()
        {
            var roads = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre) };
            RoadUnderCursor.Found found;
            Vec3 from, to;
            RayThrough(new Vec3(7.0f, Surface, 100f), out from, out to);
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out found) && found.Outside < 0f, "0.5 m inside the edge: the road");
            RayThrough(new Vec3(8.2f, Surface, 100f), out from, out to);
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out found) && found.Outside > 0.6f && found.Outside < 0.8f, "0.7 m beyond the edge: still the road (the margin is 1 m): " + found.Outside.ToString("0.00"));
            RayThrough(new Vec3(9.0f, Surface, 100f), out from, out to);
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Ground, out _), "1.5 m beyond it: none");
            RayThrough(new Vec3(-9.0f, Surface, 100f), out from, out to);
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Ground, out _), "the other side the same");
            RayThrough(new Vec3(8.2f, Surface, 100f), out from, out to);
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Ground, out _, 0.5f), "a smaller margin refuses what the usual one takes");
            RayThrough(new Vec3(1f, Surface, 230f), out from, out to);
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Ground, out _), "30 m beyond the end of the road: none");
        }

        private static void Depth()
        {
            var roads = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre) };
            RayThrough(new Vec3(1f, Surface, 100f), out Vec3 from, out Vec3 to);
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Surface + 1.4f, out _), "the ground hit 1.4 m over the surface: a road on the ground, the game's ray finds it");
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Surface + 1.6f, out _), "1.6 m over it: under the ground");
            Program.Check(!RoadUnderCursor.Pick(roads, from, to, Surface, out _), "the ground at the surface itself: none");
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Surface + 0.5f, out _, 1f, 0.4f), "a smaller depth takes what the usual one leaves");
            // A camera under the road's surface sees nothing of it.
            Program.Check(!RoadUnderCursor.Pick(roads, new Vec3(0f, 390f, -50f), new Vec3(0f, 300f, 50f), Ground, out _), "a camera under the road: none");
            // A camera under the road whose ray, taken back past the camera, would cross the road, and a segment that stops short of the road's height: neither is a hit (the ray is the segment, not its line).
            Program.Check(!RoadUnderCursor.Pick(roads, new Vec3(0f, 390f, 50f), new Vec3(0f, 300f, 150f), Ground, out _), "a camera under the road, its line back through the road: none");
            Program.Check(!RoadUnderCursor.Pick(roads, new Vec3(0f, 600f, 0f), new Vec3(0f, 500f, 100f), Ground, out _), "a segment that ends 100 m over the road: none");
        }

        private static void First()
        {
            // A road in a cutting (surface 399.9) and one in a tunnel right under it (surface 389.9): the ray meets the upper one first.
            var roads = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre - 10f), Straight(0f, Centre) };
            RayThrough(new Vec3(1f, Surface, 100f), out Vec3 from, out Vec3 to);
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out RoadUnderCursor.Found found) && found.Index == 1, "of the two the upper road, though the lower is listed first");
            // The roads side by side, 30 m apart: the ray through one is that one's.
            roads = new List<RoadUnderCursor.Candidate> { Straight(-30f, Centre), Straight(0f, Centre) };
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out found) && found.Index == 1, "side by side: the one the ray goes through");
            RayThrough(new Vec3(-29f, Surface, 100f), out from, out to);
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out found) && found.Index == 0, "and the other the other");
            // Two roads that overlap in the plane at the same height (a junction's arms): the place most inside one.
            roads = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre), Straight(5f, Centre) };
            RayThrough(new Vec3(4.5f, Surface, 100f), out from, out to);
            Program.Check(RoadUnderCursor.Pick(roads, from, to, Ground, out found) && found.Index == 1, "inside both: the one whose centre line is nearer");
        }

        private static void Odd()
        {
            RoadUnderCursor.Found found;
            Program.Check(!RoadUnderCursor.Pick(null, new Vec3(0f, 600f, 0f), new Vec3(0f, 0f, 0f), Ground, out found), "no list: none");
            Program.Check(!RoadUnderCursor.Pick(new List<RoadUnderCursor.Candidate>(), new Vec3(0f, 600f, 0f), new Vec3(0f, 0f, 0f), Ground, out _), "an empty list: none");
            var broken = new List<RoadUnderCursor.Candidate> { Straight(0f, Centre, float.NaN), Straight(0f, Centre, -1f) };
            RayThrough(new Vec3(0f, Surface, 100f), out Vec3 from, out Vec3 to);
            Program.Check(!RoadUnderCursor.Pick(broken, from, to, Ground, out _), "a road without a width that can be believed is skipped");
            var noRise = new List<RoadUnderCursor.Candidate> { new RoadUnderCursor.Candidate { Curve = Bezier3.Line(new Vec3(0f, Centre, 0f), new Vec3(0f, Centre, 200f)), Width = 15f, Rise = float.NaN } };
            Program.Check(!RoadUnderCursor.Pick(noRise, from, to, Ground, out _), "nor one without a rise");
            // A bend 60 m long: the place beside its middle.
            var bend = new List<RoadUnderCursor.Candidate> { new RoadUnderCursor.Candidate { Width = 12f, Rise = Rise, Curve = new Bezier3(new Vec3(0f, Centre, 0f), new Vec3(30f, Centre, 0f), new Vec3(60f, Centre, 30f), new Vec3(60f, Centre, 60f)) } };
            Vec3 middle = bend[0].Curve.Point(0.5f);
            RayThrough(new Vec3(middle.X + 2f, Surface, middle.Z - 2f), out from, out to);
            Program.Check(RoadUnderCursor.Pick(bend, from, to, Ground, out found) && found.T > 0.4f && found.T < 0.6f, "beside the middle of a bend: the road, near t 0.5: " + found.T.ToString("0.00"));
            // A ramp: the centre line falls 6 m along the road; the ray is put through the surface where it is, not at the start's height.
            var ramp = new List<RoadUnderCursor.Candidate> { new RoadUnderCursor.Candidate { Width = 15f, Rise = Rise, Curve = Bezier3.Line(new Vec3(0f, Centre, 0f), new Vec3(0f, Centre - 6f, 100f)) } };
            var onRamp = ramp[0].Curve.Point(0.5f);
            RayThrough(new Vec3(1f, onRamp.Y + Rise, onRamp.Z), out from, out to);
            Program.Check(RoadUnderCursor.Pick(ramp, from, to, Ground, out found) && System.Math.Abs(found.Hit.Y - (onRamp.Y + Rise)) < 2e-2f && System.Math.Abs(found.Hit.Z - onRamp.Z) < 2e-2f, "on a ramp the place is on the surface where it is: " + found.Hit.Y.ToString("0.00") + " at " + found.Hit.Z.ToString("0.0"));
        }
    }
}
