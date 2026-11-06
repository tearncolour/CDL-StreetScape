using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>Wall, roof or slope under the cursor, and which support a board takes there (Geo/BuildingSurface.cs).</summary>
    internal static class BuildingSurfaceTests
    {
        public static void RunAll()
        {
            Program.Run("building surface: wall, roof or slope from the normal", Classify);
            Program.Run("building surface: what a board does with it", Plans);
            Program.Run("building surface: the log line", Line);
        }

        private static void Classify()
        {
            Program.Check(BuildingSurface.Classify(0f, 1f, 0f) == SurfaceKind.Roof, "a flat roof");
            Program.Check(BuildingSurface.Classify(0f, 0f, 1f) == SurfaceKind.Wall && BuildingSurface.Classify(-0.24f, 0f, 0.97f) == SurfaceKind.Wall, "a wall, level normal");
            Program.Check(BuildingSurface.Classify(0f, -1f, 0f) == SurfaceKind.Roof, "a surface facing straight down counts by its steepness, like the wall test");
            // The limits (unit normals): up to 0.35 of the normal is a wall, from 0.7 a roof, between them neither; a thousandth to each side of the limits.
            Program.Check(Lean(0.349f) == SurfaceKind.Wall && Lean(0.351f) == SurfaceKind.Slope, "the wall limit is 0.35");
            Program.Check(Lean(0.699f) == SurfaceKind.Slope && Lean(0.701f) == SurfaceKind.Roof, "the roof limit is 0.7");
            Program.Check(BuildingSurface.Classify(0.8f, 0.5f, 0.3f) == SurfaceKind.Slope, "a steep roof (about 60 degrees) is neither");
            Program.Check(BuildingSurface.Classify(0f, 5f, 0f) == SurfaceKind.Roof && BuildingSurface.Classify(0f, 0f, 9f) == SurfaceKind.Wall, "the length of the normal does not matter");
            Program.Check(BuildingSurface.Classify(0f, 0f, 0f) == SurfaceKind.None && BuildingSurface.Classify(float.NaN, 1f, 0f) == SurfaceKind.None && BuildingSurface.Classify(0f, float.PositiveInfinity, 0f) == SurfaceKind.None, "no normal, or one that is not a number: none");
            Program.Check(BuildingSurface.RoofMinNormalY > WallMount.MaxNormalY, "the roof limit is above the wall limit: the gap is the slope");
        }

        /// <summary>A unit normal whose y is up.</summary>
        private static SurfaceKind Lean(float up) => BuildingSurface.Classify((float)Math.Sqrt(1.0 - (double)up * up), up, 0f);

        private static void Plans()
        {
            // No building, no surface: nothing, whatever else.
            foreach (bool text in new[] { false, true })
            {
                foreach (bool roofSupport in new[] { false, true })
                {
                    Program.Check(BuildingSurface.Plan(false, SurfaceKind.Roof, text, true, roofSupport) == SurfacePlan.Nothing, "no building: nothing");
                    Program.Check(BuildingSurface.Plan(true, SurfaceKind.None, text, true, roofSupport) == SurfacePlan.Nothing, "no surface: nothing");
                    Program.Check(BuildingSurface.Plan(true, SurfaceKind.Slope, text, true, roofSupport) == SurfacePlan.HintSlope, "a slope is told apart from a wall");
                }
            }

            // A text board goes where the cursor is, whichever of the two supports is chosen.
            foreach (bool roofSupport in new[] { false, true })
            {
                Program.Check(BuildingSurface.Plan(true, SurfaceKind.Roof, true, true, roofSupport) == SurfacePlan.Roof, "text on a roof: the roof support, chosen " + (roofSupport ? "roof" : "wall"));
                Program.Check(BuildingSurface.Plan(true, SurfaceKind.Wall, true, true, roofSupport) == SurfacePlan.Wall, "text on a wall: the wall support, chosen " + (roofSupport ? "roof" : "wall"));
            }

            // Another board keeps what the chosen support does.
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Wall, false, true, false) == SurfacePlan.Wall, "another board, wall support, on a wall: placed");
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Roof, false, true, false) == SurfacePlan.HintRoof, "another board, wall support, on a roof: the hint");
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Roof, false, true, true) == SurfacePlan.Roof, "another board, roof support, on a roof: the roof path (it refuses a board that is not text, with its own line)");
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Wall, false, true, true) == SurfacePlan.HintWall, "another board, roof support, on a wall: the hint");
            // The supports of the text box switched off: nothing is automatic.
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Roof, true, false, false) == SurfacePlan.HintRoof && BuildingSurface.Plan(true, SurfaceKind.Wall, true, false, true) == SurfacePlan.HintWall, "with the text supports off a text board is like another");
            Program.Check(BuildingSurface.Plan(true, SurfaceKind.Wall, true, false, false) == SurfacePlan.Wall, "and the wall is as it was");
        }

        private static void Line()
        {
            string line = BuildingSurface.Describe(0f, 1f, 0f, 736504, true, SurfaceKind.Roof, SurfacePlan.Roof, true, false);
            Program.Check(line.StartsWith("Surface: hit direction (0.00, 1.00, 0.00) on entity 736504, a building, roof") && line.Contains("tilted 0 degrees") && line.Contains("text board, wall support chosen: Roof"), "a roof: " + line);
            string wall = BuildingSurface.Describe(-0.24f, 0f, 0.97f, 5, true, SurfaceKind.Wall, SurfacePlan.Wall, false, true);
            Program.Check(wall.Contains("(-0.24, 0.00, 0.97)") && wall.Contains("tilted 90 degrees") && wall.Contains("other board, roof support chosen: Wall"), "a wall: " + wall);
            string none = BuildingSurface.Describe(float.NaN, 0f, 0f, 0, false, SurfaceKind.None, SurfacePlan.Nothing, true, false);
            Program.Check(none.Contains("no building") && !none.Contains("tilted"), "no normal: no tilt: " + none);
        }
    }
}
