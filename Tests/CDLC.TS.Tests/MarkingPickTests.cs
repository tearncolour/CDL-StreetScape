using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A click on a line that misses it by a hand's breadth (the owner's log of 4 Nov: four of five clicks in the edit mode and two in the delete mode took the area that clears the game's markings,
    /// which lies under the line over the whole road, and deleting it brought the game's lines back over his): the area's boxes are low (MarkingPlan.OutlineTop) so that a line painted in it wins when
    /// the ray is on it, and a line is thin, so the ray has to be nearly on it; with Picking.Pick's extraGrow a line takes a ray that passes a little beside it. Pure; the tool's use of it (the choice
    /// between the two in PlacementTool.FindUnderCursor) needs the game's entities and is not run here.
    /// </summary>
    internal static class MarkingPickTests
    {
        public static void RunAll()
        {
            Program.Run("marking pick: a line is picked before the area under it, and a ray that passes beside it takes it only with the extra tolerance", LineAndArea);
        }

        private static List<Vec3> Rectangle(float x0, float x1, float z0, float z1) => new List<Vec3> { new Vec3(x0, 0f, z0), new Vec3(x0, 0f, z1), new Vec3(x1, 0f, z1), new Vec3(x1, 0f, z0) };

        private static List<PickShape> Shapes(MarkingPlan plan) => plan.PickShapes(2f, 120);

        private static bool Hit(List<PickShape> shapes, float x, float grow, out float distance)
        {
            var from = new Vec3(x, 5f, 5f);
            var to = new Vec3(x, -1f, 5f);
            return Picking.Pick(shapes, Vec3.Zero, 0f, from, to, out distance, out _, grow);
        }

        private static void LineAndArea()
        {
            // A line of paint 15 cm wide and 10 m long, and the area of a clearing over the road, 8 m wide, as the tool makes them.
            var line = new MarkingPlan();
            var paint = new MeshBuilder();
            Polygon.Fill(paint, Rectangle(-0.075f, 0.075f, 0f, 10f));
            line.Parts.Add(new MarkingPart { Mesh = paint });
            var area = new MarkingPlan();
            area.Outlines.Add(Rectangle(-4f, 4f, 0f, 10f));
            List<PickShape> lineShapes = Shapes(line), areaShapes = Shapes(area);
            Program.Check(lineShapes.Count > 0 && areaShapes.Count > 0, "both have boxes to point at");

            // On the line both are hit, and the line is nearer: the ray reaches the top of the line's box (0.3 m) before the area's (0.02 m).
            bool lineOn = Hit(lineShapes, 0f, 0f, out float onLine), areaOn = Hit(areaShapes, 0f, 0f, out float onArea);
            Program.Check(lineOn && areaOn && onLine < onArea, "on the line it is hit before the area: " + onLine + " against " + onArea);

            // 30 cm beside it: the line is missed, the area is hit. This is the click that took the area.
            Program.Check(!Hit(lineShapes, 0.3f, 0f, out _) && Hit(areaShapes, 0.3f, 0f, out _), "30 cm beside the line: the line is missed, the area is hit");
            // With the extra tolerance the line takes it.
            Program.Check(Hit(lineShapes, 0.3f, 0.25f, out _), "30 cm beside the line, with the extra tolerance: the line is hit");
            // 60 cm beside it is still the area's (the tolerance is a hand's breadth, not a lane).
            Program.Check(!Hit(lineShapes, 0.6f, 0.25f, out _) && Hit(areaShapes, 0.6f, 0.25f, out _), "60 cm beside the line, even with the tolerance: the area");
            // Along the line: past its end it is not hit either way.
            var past = new Vec3(0f, 5f, 12f);
            Program.Check(!Picking.Pick(lineShapes, Vec3.Zero, 0f, past, new Vec3(0f, -1f, 12f), out _, out _, 0.25f), "past the end of the line, with the tolerance: not hit");
            // The tolerance is nothing unless asked for: the default call is the old one.
            Program.Check(!Picking.Pick(lineShapes, Vec3.Zero, 0f, new Vec3(0.3f, 5f, 5f), new Vec3(0.3f, -1f, 5f), out _), "the call without the argument is as before");
        }
    }
}
