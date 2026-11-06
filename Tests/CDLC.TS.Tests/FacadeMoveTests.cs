using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The arrow keys on a building (FacadeMove) and the roof's edge (RoofFit), and the hints that say so.</summary>
    internal static class FacadeMoveTests
    {
        public static void RunAll()
        {
            Program.Run("facade: a step and the way the screen's right points along a wall", Steps);
            Program.Run("facade: along a flat wall", Flat);
            Program.Run("facade: along a curved wall the board stays on it and turns with it", Arc);
            Program.Run("facade: the height of the lower edge, and the sum of the presses", Heights);
            Program.Run("facade: across a roof as the camera sees it", Roof);
            Program.Run("facade: the roof's edge", Edge);
            Program.Run("facade: the hints of a wall and a roof", Hints);
        }

        private static void Steps()
        {
            Program.Near(FacadeMove.StepOf(false), 0.1f, 0f, "a press is 10 cm");
            Program.Near(FacadeMove.StepOf(true), 0.5f, 0f, "with Shift 50 cm");
            Program.Check(FacadeMove.SenseOf(1f, 0f, 1f, 0f) == 1 && FacadeMove.SenseOf(1f, 0f, -1f, 0f) == -1, "the right of the camera along the tangent or against it");
            Program.Check(FacadeMove.SenseOf(0f, 1f, 1f, 0f) == 1, "a wall square to the camera's right: the first sense (no zero)");
            Program.Check(FacadeMove.SenseOf(0.6f, 0.8f, 1f, 0f) == 1 && FacadeMove.SenseOf(-0.6f, 0.8f, 1f, 0f) == -1, "by the sign of the dot product");
        }

        private static void Flat()
        {
            // A wall facing +Z (normal (0, 1)): its tangent is (-1, 0).
            FacadePlace a = FacadeMove.Along(5f, 7f, 0f, 1f, 0f, 2f);
            Program.Near(a.X, 3f, 1e-5f, "two metres along the tangent: x");
            Program.Near(a.Z, 7f, 1e-5f, "and z stays");
            Program.Near(a.Yaw, 0f, 1e-6f, "and the board faces as it did");
            FacadePlace east = FacadeMove.Along(0f, 0f, 1f, 0f, 0f, 1.5f);
            Program.Near(east.X, 0f, 1e-5f, "a wall facing +X: along it is -Z... x");
            Program.Near(east.Z, 1.5f, 1e-5f, "and z");
            Program.Near(east.Yaw, (float)Math.PI * 0.5f, 1e-5f, "facing +X is a quarter turn");
            FacadePlace unit = FacadeMove.Along(0f, 0f, 0f, 3f, 0f, 1f);
            Program.Near(unit.X, -1f, 1e-5f, "the normal is taken as a unit one");
            FacadePlace none = FacadeMove.Along(2f, 3f, 0f, 0f, 0f, 1f);
            Program.Check(none.X == 2f && none.Z == 3f && none.Yaw == 0f, "no normal: it stays");
            FacadePlace nan = FacadeMove.Along(0f, 0f, 0f, 1f, float.NaN, 1f);
            Program.Near(nan.X, -1f, 1e-5f, "a radius that is not a number is a flat wall");
            // Back and forth: where it was.
            FacadePlace there = FacadeMove.Along(1f, 2f, 0.6f, 0.8f, 0f, 3f);
            FacadePlace back = FacadeMove.Along(there.X, there.Z, 0.6f, 0.8f, 0f, -3f);
            Program.Near(back.X, 1f, 1e-5f, "back and forth: x");
            Program.Near(back.Z, 2f, 1e-5f, "back and forth: z");
        }

        private static void Arc()
        {
            // A tower of radius 10 seen from outside, the board at its nearest point facing +Z: the centre is behind it at (0, -10).
            FacadePlace quarter = FacadeMove.Along(0f, 0f, 0f, 1f, 10f, (float)Math.PI * 5f);
            Program.Near(quarter.X, -10f, 1e-3f, "a quarter of the way round the tower: x");
            Program.Near(quarter.Z, -10f, 1e-3f, "z");
            Program.Near(quarter.Yaw, -(float)Math.PI * 0.5f, 1e-4f, "it has turned a quarter");

            // A hollow wall of radius 10: the centre is in front, the board moves toward -x and comes toward the viewer.
            FacadePlace hollow = FacadeMove.Along(0f, 0f, 0f, 1f, -10f, 1f);
            Program.Near(hollow.X, -0.998f, 2e-3f, "a hollow wall: x");
            Program.Near(hollow.Z, 0.0500f, 2e-3f, "it comes toward the viewer: z");
            Program.Near(hollow.Yaw, 0.1f, 1e-3f, "and turns toward the centre");

            // On the circle after any number of steps, an arc's length each, and back to the start when undone.
            foreach (float radius in new[] { 4f, -4f, 25f, -25f })
            {
                float x = 3f, z = -2f, nx = 0.6f, nz = 0.8f;
                double cx = x - nx * radius, cz = z - nz * radius;
                FacadePlace p = new FacadePlace(x, z, (float)Math.Atan2(nx, nz));
                for (int i = 0; i < 40; i++)
                {
                    float yaw = p.Yaw;
                    p = FacadeMove.Along(p.X, p.Z, (float)Math.Sin(yaw), (float)Math.Cos(yaw), radius, 0.5f);
                    double ring = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz));
                    Program.Near((float)ring, Math.Abs(radius), 2e-3f, radius + ": step " + i + " is on the wall");
                }

                // Along the tangent a bulge turns the normal toward it: the yaw goes down by length over radius (up for a hollow), whatever whole turns that makes.
                float turned = p.Yaw - (float)Math.Atan2(nx, nz) + 20f / radius;
                Program.Near((float)Math.Atan2(Math.Sin(turned), Math.Cos(turned)), 0f, 3e-3f, radius + ": 20 m along it turns it by length over radius");
                for (int i = 0; i < 40; i++)
                {
                    float yaw = p.Yaw;
                    p = FacadeMove.Along(p.X, p.Z, (float)Math.Sin(yaw), (float)Math.Cos(yaw), radius, -0.5f);
                }

                Program.Near(p.X, x, 5e-3f, radius + ": undone: x");
                Program.Near(p.Z, z, 5e-3f, radius + ": undone: z");
            }

            // A short step on a big radius is the flat step.
            FacadePlace small = FacadeMove.Along(0f, 0f, 0f, 1f, 150f, 0.1f);
            Program.Near(small.X, -0.1f, 1e-4f, "0.1 m on a radius of 150 m");
        }

        private static void Heights()
        {
            Program.Near(FacadeMove.Height(2.3f, 0.1f, 0.2f, 60f), 2.4f, 1e-5f, "a step up");
            Program.Near(FacadeMove.Height(2.3f, -0.5f, 0.2f, 60f), 1.8f, 1e-5f, "a coarse step down");
            Program.Near(FacadeMove.Height(2.34f, 0f, 0.2f, 60f), 2.3f, 1e-5f, "to a tenth of a metre");
            Program.Near(FacadeMove.Height(0.3f, -0.5f, 0.2f, 60f), 0.2f, 1e-5f, "never below the lowest");
            Program.Near(FacadeMove.Height(59.8f, 0.5f, 0.2f, 60f), 60f, 1e-5f, "never above the highest");
            Program.Near(FacadeMove.Height(3f, 1f, 0.2f, 12f), 4f, 1e-5f, "a roof's limit");

            float offset = 0f;
            for (int i = 0; i < 100; i++)
                offset = FacadeMove.Accumulate(offset, 1, false);
            Program.Near(offset, 10f, 1e-4f, "a hundred presses of 10 cm are 10 m, without drift");
            for (int i = 0; i < 100; i++)
                offset = FacadeMove.Accumulate(offset, -1, true);
            Program.Near(offset, -FacadeMove.MaxShift, 1e-4f, "and 100 presses of 0.5 m the other way stop at the limit: " + offset);
            for (int i = 0; i < 1000; i++)
                offset = FacadeMove.Accumulate(offset, 1, true);
            Program.Near(offset, FacadeMove.MaxShift, 1e-4f, "never beyond the limit");
            Program.Near(FacadeMove.Accumulate(0f, 0, false), 0f, 0f, "no direction, no move");
        }

        private static void Roof()
        {
            // The camera looks along +Z: its right is +X and forward +Z.
            (float x, float z) = FacadeMove.AcrossRoof(10f, 20f, 1f, 0f, 0f, 1f, 1, 0, false);
            Program.Check(Math.Abs(x - 10.1f) < 1e-5f && z == 20f, "right is +x for that camera");
            (x, z) = FacadeMove.AcrossRoof(10f, 20f, 1f, 0f, 0f, 1f, 0, 1, true);
            Program.Check(x == 10f && Math.Abs(z - 20.5f) < 1e-5f, "forward with Shift is 0.5 m");
            // Turned a quarter: its right is +Z... the same keys go where it looks.
            (x, z) = FacadeMove.AcrossRoof(0f, 0f, 0f, -1f, 1f, 0f, 1, 1, false);
            Program.Check(Math.Abs(x - 0.1f) < 1e-5f && Math.Abs(z + 0.1f) < 1e-5f, "right and forward together");
            (x, z) = FacadeMove.AcrossRoof(0f, 0f, 1f, 0f, 0f, 1f, -1, -1, false);
            Program.Check(Math.Abs(x + 0.1f) < 1e-5f && Math.Abs(z + 0.1f) < 1e-5f, "left and back");
            (x, z) = FacadeMove.AcrossRoof(3f, 4f, 1f, 0f, 0f, 1f, 0, 0, false);
            Program.Check(x == 3f && z == 4f, "no key, no move");
        }

        private static void Edge()
        {
            // A building at (100, 200) facing +Z with a box of 20 by 30 m: x -10 to 10, z -15 to 15.
            float Over(float px, float pz, float yaw, float half, float front, float back, float buildingYaw = 0f) =>
                RoofFit.Overhang(100f, 200f, buildingYaw, -10f, 10f, -15f, 15f, 100f + px, 200f + pz, yaw, half, front, back);
            Program.Near(Over(0f, 0f, 0f, 2f, 0.1f, 1f), 0f, 0f, "in the middle: nothing over");
            Program.Near(Over(9f, 0f, 0f, 2f, 0.1f, 1f), 1f, 1e-4f, "reaching 1 m past the east edge: 1 m");
            Program.Near(Over(0f, 14.5f, 0f, 2f, 0.1f, 1f), 0f, 1e-4f, "its back (behind, -z) is inside and its front 0.1 m: 14.6, inside");
            Program.Near(Over(0f, 14.95f, 0f, 2f, 0.5f, 1f), 0.45f, 1e-4f, "the front 0.5 m past 14.95 is 0.45 over the north edge");
            Program.Near(Over(0f, -14.5f, 0f, 2f, 0.1f, 1f), 0.5f, 1e-4f, "the back 1 m behind -14.5 is 0.5 over the south edge");
            // The board turned round: front and back swap.
            Program.Near(Over(0f, -14.5f, (float)Math.PI, 2f, 0.1f, 1f), 0f, 1e-4f, "turned half round its back is toward +z and its front reaches only -14.6: inside");
            // A corner out in the direction of the building's own x (local x = 9 + 2 = 11) is 1 over, whatever the building's turn.
            float quarter = (float)Math.PI * 0.5f;
            foreach (float turn in new[] { 0f, 0.7f, quarter, 2.5f, -1.2f })
            {
                // The board's middle at local (9, 0) of the building, turned with it.
                float wx = 100f + 9f * (float)Math.Cos(turn), wz = 200f - 9f * (float)Math.Sin(turn);
                float local = RoofFit.Overhang(100f, 200f, turn, -10f, 10f, -15f, 15f, wx, wz, turn, 2f, 0.1f, 1f);
                Program.Near(local, 1f, 1e-3f, "building turned by " + turn + ": 1 m over");
            }

            Program.Check(RoofFit.Tolerance > 0.1f && RoofFit.Tolerance < 1f, "a small tolerance");
            Program.Check(TextRoofFrame.DepthFor(0.9f, 2f) == 0f, "a low, narrow text needs no rear brace");
            Program.Near(TextRoofFrame.DepthFor(2f, 2f), 1.2f, 1e-5f, "a tall one: 0.6 of its height behind");
            Program.Near(TextRoofFrame.DepthFor(0.5f, 6f), 0.4f, 1e-5f, "a wide one: at least 0.4");
            Program.Near(TextRoofFrame.DepthFor(9f, 2f), 2f, 1e-5f, "and at most 2 m");
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                foreach (string kind in new[] { "wall", "roof", "roof-edge" })
                {
                    var info = new HintInfo { Kind = kind, Left = 1f, Need = 0.4f, Name = "x" };
                    List<string[]> keys = HintText.Keys(info, zh);
                    Program.Check(keys.Any(k => k[0].Contains("←") && k[0].Contains("↑")), kind + (zh ? " (zh)" : " (en)") + ": the arrow keys are listed");
                    Program.Check(HintText.Of(info, zh).Length > 12 && !HintText.Of(info, zh).Contains("Esc"), kind + ": a short line");
                }

                foreach (string kind in new[] { "roof-none", "roof-text" })
                    Program.Check(HintText.Of(new HintInfo { Kind = kind }, zh).Length > 5, kind + ": a line");
            }

            Program.Check(!HintText.Keys(new HintInfo { Kind = "roof-none" }, false).Any(k => k[0].Contains("←")), "no arrows to show where there is no roof");
            foreach (string kind in new[] { "roof", "roof-none", "roof-text", "roof-edge", "roof-off", "wall-roof" })
                Program.Check(Array.IndexOf(HintText.Kinds, kind) >= 0, kind + " is a known kind");
            Program.Check(HintText.Of(new HintInfo { Kind = "wall-roof" }, true).Contains("屋顶") && HintText.Of(new HintInfo { Kind = "roof-off" }, false).Contains("textSupports"), "the two refusals say what is the matter");
        }
    }
}
