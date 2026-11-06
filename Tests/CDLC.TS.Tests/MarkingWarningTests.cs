using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The channelising fills and the warning markings of GB 5768.3-2025 chapter 6.6, 6.12 and 7 (MarkingWarnings).</summary>
    internal static class MarkingWarningTests
    {
        public static void RunAll()
        {
            Program.Run("warnings: channelising islands (chevron, diagonal, cycle side) and the obstacle approach", Islands);
            Program.Run("warnings: the exit gore", Gore);
            Program.Run("warnings: transverse and longitudinal rumble markings", Rumble);
            Program.Run("warnings: the toll plaza bars and the toll island", Toll);
            Program.Run("warnings: the level crossing", Railway);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static void Sound(MeshBuilder mesh, string what)
        {
            Program.Check(mesh.Indices.Count % 3 == 0 && mesh.Indices.Count > 0, what + ": triangles");
            foreach (Vec3 p in mesh.Positions)
                Program.Check(p.IsFinite, what + ": finite");
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                Program.Check(MeshBuilder.Cross(b - a, c - a).Y > 0f, what + ": triangle " + i / 3 + " faces up");
            }
        }

        private static void Bounds(MeshBuilder mesh, out float x0, out float x1, out float z0, out float z1)
        {
            x0 = z0 = float.MaxValue;
            x1 = z1 = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }
        }

        private static bool InsideTriangle(Vec3 p, Vec3 a, Vec3 b, Vec3 c, float tol) =>
            Polygon.Cross(a, b, p) >= -tol && Polygon.Cross(b, c, p) >= -tol && Polygon.Cross(c, a, p) >= -tol;

        private static void Islands()
        {
            // A gore-shaped island 3 m wide at its base, 20 m long, the point toward the traffic.
            var island = new List<Vec3> { P(0f, 0f), P(1.5f, 20f), P(-1.5f, 20f) };
            foreach (MarkingWarnings.ChannelFill fill in Enum.GetValues(typeof(MarkingWarnings.ChannelFill)))
            {
                MarkingColour colour = fill == MarkingWarnings.ChannelFill.Diagonal ? MarkingColour.Yellow : MarkingColour.White;
                MarkingShape s = MarkingWarnings.Channelising(island, 0.15f, fill, colour);
                Sound(s.Mesh, "island " + fill);
                Program.Check(s.Colour == colour, fill + ": the colour given (white the same way, yellow opposing)");
                foreach (Vec3 p in s.Mesh.Positions)
                    Program.Check(InsideTriangle(p, island[0], island[1], island[2], 1e-2f), fill + ": the paint stays inside the island");
                float outline = 30f - 0.5f * (3f - 2f * 0.15f * (float)Math.Sqrt(1 + 400 / 2.25)) * 0f;
                Program.Check(s.Area > 3f && s.Area < 30f * 0.6f, fill + ": outline and lines, not solid: " + s.Area);
            }

            // The chevron fill: the lines of the left half lean one way (135) and of the right half the other (45): the V points at the traffic. Checked by the mean direction of long edges on each side.
            MarkingShape ch = MarkingWarnings.Channelising(island, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White);
            double leftSum = 0, rightSum = 0;
            int leftN = 0, rightN = 0;
            for (int i = 0; i + 2 < ch.Mesh.Indices.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vec3 a = ch.Mesh.Positions[ch.Mesh.Indices[i + k]], b = ch.Mesh.Positions[ch.Mesh.Indices[i + (k + 1) % 3]];
                    Vec3 e = b - a;
                    if (e.LengthXZ < 0.6f || Math.Abs(Math.Abs(e.X) - Math.Abs(e.Z)) > 0.05f * e.LengthXZ)
                        continue;
                    float mx = (a.X + b.X) * 0.5f;
                    double slope = Math.Sign(e.X * e.Z);
                    if (mx < -0.3f) { leftSum += slope; leftN++; }
                    if (mx > 0.3f) { rightSum += slope; rightN++; }
                }
            }

            Program.Check(leftN > 5 && rightN > 5, "both halves have 45-degree lines: " + leftN + " / " + rightN);
            Program.Check(leftSum < 0 && rightSum > 0, "left half leans one way (x falls as z rises), right half the other: " + leftSum + " / " + rightSum);
            // The diagonal fill is all one way; the cycle-side fill is 30 wide at 2 m (less paint).
            MarkingShape diag = MarkingWarnings.Channelising(island, 0.15f, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.Yellow);
            MarkingShape cycle = MarkingWarnings.Channelising(island, 0.15f, MarkingWarnings.ChannelFill.CycleSide, MarkingColour.White);
            Program.Check(cycle.Area < diag.Area, "30 wide at 2 m is less than 45 at 1 m: " + cycle.Area + " < " + diag.Area);
            Program.Check(MarkingWarnings.Channelising(null, 0.15f, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.White).Mesh.TriangleCount == 0 && MarkingWarnings.Channelising(island, 0f, MarkingWarnings.ChannelFill.Diagonal, MarkingColour.White).Mesh.TriangleCount == 0, "no island or no outline: nothing");

            // The obstacle approach: yellow diagonals on the centre line, white chevrons between lanes the same way; the area reaches the obstacle's width plus the clearance each side.
            MarkingShape yellow = MarkingWarnings.ObstacleApproach(30f, 1f, opposing: true);
            MarkingShape white = MarkingWarnings.ObstacleApproach(30f, 1f, opposing: false);
            Program.Check(yellow.Colour == MarkingColour.Yellow && white.Colour == MarkingColour.White, "yellow when opposing, white the same way");
            Bounds(yellow.Mesh, out float ox0, out float ox1, out float oz0, out float oz1);
            Program.Near(ox1 - ox0, 1f + 2f * 0.3f, 1e-3f, "the obstacle's width plus 30 each side");
            Program.Near(oz1 - oz0, 30f, 1e-3f, "the taper's length");
            Bounds(MarkingWarnings.ObstacleApproach(30f, 1f, true, 0.15f, 0.6f).Mesh, out float wx0, out float wx1, out _, out _);
            Program.Near(wx1 - wx0, 2.2f, 1e-3f, "60 clearance when asked; 30 to 60 (7.2.2)");
            Bounds(MarkingWarnings.ObstacleApproach(30f, 1f, true, 0.15f, 5f).Mesh, out float cx0, out float cx1, out _, out _);
            Program.Near(cx1 - cx0, 2.2f, 1e-3f, "never over 60");
            Program.Check(MarkingWarnings.ObstacleApproach(0f, 1f, true).Mesh.TriangleCount == 0, "no length, nothing");
        }

        private static void Gore()
        {
            MarkingShape gore = MarkingWarnings.ExitGore(30f, 4f, 60f);
            Sound(gore.Mesh, "gore");
            Program.Check(gore.Colour == MarkingColour.White, "white");
            Bounds(gore.Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(z0, -60f, 1e-3f, "the thick dashed line starts 60 m before the nose");
            Program.Near(z1, 30f, 1e-3f, "to the gore's end");
            Program.Near(x1, 4f, 1e-3f, "the ramp side at the width");
            Program.Near(x0, 0f, 1e-3f, "the through edge at x 0");
            // The 3-3 dashed line: 60 m is ten dashes of 3 m, 45 wide: 13.5 m2; the thick solid 45 x 200: 0.9 m2.
            MarkingShape bare = MarkingWarnings.ExitGore(30f, 4f, 0f);
            Program.Near(gore.Area - bare.Area, 10f * 3f * 0.45f, 0.5f, "the dashed line's paint: " + (gore.Area - bare.Area));
            Program.Check(bare.Area > 0.9f + 2f && bare.Area < 0.5f * 4f * 30f * 0.6f, "the solid, the edge lines and the V lines (one family a half), not a solid fill: " + bare.Area);
            Program.Check(MarkingWarnings.ExitGore(0f, 4f).Mesh.TriangleCount == 0 && MarkingWarnings.ExitGore(30f, 0f).Mesh.TriangleCount == 0, "no length or width: nothing");
            Bounds(MarkingWarnings.ExitGore(30f, 4f, 0f, 1f).Mesh, out _, out _, out float sz0, out _);
            Program.Near(sz0, 0f, 1e-3f, "the thick solid is 2 m at least (6.12.2: 200 or more)");
        }

        private static void Rumble()
        {
            // Transverse: a group of two bars across a 3.5 m lane: bars 45 thick, 45 apart, pieces of 1 m with 4 cm slots.
            MarkingShape group = MarkingWarnings.TransverseRumbleGroup(3.5f, 2);
            Sound(group.Mesh, "rumble group");
            Bounds(group.Mesh, out float gx0, out float gx1, out float gz0, out float gz1);
            Program.Near(gx1 - gx0, 3.08f, 1e-4f, "three full 100 cm pieces plus two 4 cm slots, centred in the lane");
            Program.Near(gz1 - gz0, 0.45f + 0.45f + 0.45f, 1e-4f, "two bars 45 thick, 45 apart");
            Program.Check(group.Mesh.TriangleCount == 2 * 3 * 2, "three full pieces per bar: " + group.Mesh.TriangleCount);
            float pieces = 3f;
            Program.Near(group.Area, 2f * 0.45f * pieces, 1e-3f, "the slots are not painted: " + group.Area);
            Program.Check(MarkingWarnings.TransverseRumbleBars(0) == 2 && MarkingWarnings.TransverseRumbleBars(5) == 2 && MarkingWarnings.TransverseRumbleBars(6) == 3 && MarkingWarnings.TransverseRumbleBars(9) == 3, "Table 5: two bars to the sixth group, three from the seventh");
            Program.Check(MarkingWarnings.TransverseRumbleSpacing.Length == 9 && MarkingWarnings.TransverseRumbleSpacing[0] == 17f && MarkingWarnings.TransverseRumbleSpacing[8] == 32f, "Table 5: 17, 20, 23, 26, 28, 30, 32, 32, 32");
            MarkingShape ten = MarkingWarnings.TransverseRumble(3.5f, 10);
            Sound(ten.Mesh, "ten groups");
            Bounds(ten.Mesh, out _, out _, out float tz0, out float tz1);
            float sum = 0f;
            foreach (float s in MarkingWarnings.TransverseRumbleSpacing)
                sum += s;
            Program.Near(tz1 - tz0, sum + 3f * 0.45f + 2f * 0.45f, 1e-3f, "ten groups over 240 m, the last of three bars: " + (tz1 - tz0));
            MarkingShape ordinary = MarkingWarnings.TransverseRumble(3.5f, 5, table: false);
            Bounds(ordinary.Mesh, out _, out _, out float oz0, out float oz1);
            Program.Near(oz1 - oz0, 64f + 1.35f, 1e-3f, "an ordinary road: three groups at most, 32 m apart");
            Program.Check(MarkingWarnings.TransverseRumble(0f, 3).Mesh.TriangleCount == 0 && MarkingWarnings.TransverseRumbleGroup(3.5f, 0).Mesh.TriangleCount == 0, "no lane or no bars: nothing");

            // Longitudinal: blocks 30 wide, 1 m long, 1 m apart, 5 cm from the line, 45-degree ends.
            MarkingShape lon = MarkingWarnings.LongitudinalRumble(20f);
            Sound(lon.Mesh, "longitudinal");
            Program.Check(lon.Mesh.TriangleCount == 10 * 2, "ten blocks over 20 m: " + lon.Mesh.TriangleCount);
            Bounds(lon.Mesh, out float lx0, out float lx1, out _, out float lz1);
            Program.Near(lx0, 0.05f, 1e-5f, "5 cm from the line");
            Program.Near(lx1, 0.35f, 1e-5f, "30 wide");
            Program.Near(lz1, 19.3f, 1e-5f, "the last long edge ends at 19, the slanted tip at 19.3");
            Program.Near(lon.Area, 10f * 0.3f, 1e-4f, "Fig. 104: ten parallelograms of 30 x 100 cm");
            MarkingShape taper = MarkingWarnings.LongitudinalRumble(40f, taper: true);
            Program.Check(taper.Area < MarkingWarnings.LongitudinalRumble(40f).Area, "the taper's first blocks are narrower");
            Bounds(MarkingWarnings.LongitudinalRumble(40f, true).Mesh, out _, out float tx1, out _, out _);
            Program.Near(tx1, 0.35f, 1e-5f, "and reach 30 after 30 m");
            Bounds(MarkingWarnings.LongitudinalRumble(20f, false, rightSide: false).Mesh, out float rx0, out float rx1, out _, out _);
            Program.Near(rx1, -0.05f, 1e-5f, "or on the left of the line");
            Program.Check(MarkingWarnings.LongitudinalRumble(0.5f).Mesh.TriangleCount == 0, "too short for a block: nothing");
        }

        private static void Toll()
        {
            MarkingShape one = MarkingWarnings.TollPlazaGroup(12f, 3);
            Sound(one.Mesh, "toll group");
            Bounds(one.Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(x1 - x0, 11.3f, 1e-3f, "thirteen full 50 cm dashes and twelve 40 cm gaps, centred");
            Program.Near(z1 - z0, 3f * 0.45f + 2f * 0.45f, 1e-4f, "three rows 45 thick, 45 apart");
            Program.Near(one.Area, 3f * 0.45f * 13f * 0.5f, 1e-3f, "thirteen complete dashes per row: " + one.Area);
            Program.Check(MarkingWarnings.TollPlazaRows(0) == 1 && MarkingWarnings.TollPlazaRows(1) == 1 && MarkingWarnings.TollPlazaRows(2) == 2 && MarkingWarnings.TollPlazaRows(5) == 2 && MarkingWarnings.TollPlazaRows(6) == 3 && MarkingWarnings.TollPlazaRows(11) == 3, "Table 6: 1, 1, 2, 2, 2, 2, 3 ...");
            Program.Check(MarkingWarnings.TollPlazaSpacing.Length == 9 && MarkingWarnings.TollPlazaSpacing[0] == 5f && MarkingWarnings.TollPlazaSpacing[8] == 30f && MarkingWarnings.TollPlazaFirst == 50f, "Table 6: 5, 9, 13, 17, 20, 23, 26, 28, 30; the first 50 m from the centre line");
            MarkingShape five = MarkingWarnings.TollPlaza(12f, 5);
            Sound(five.Mesh, "five groups");
            Program.Near(MarkingWarnings.TollPlazaLength(5), 5f + 9f + 13f + 17f, 1e-4f, "five groups: 44 m from the farthest to the nearest");
            Program.Near(MarkingWarnings.TollPlazaLength(12), 5f + 9f + 13f + 17f + 20f + 23f + 26f + 28f + 30f + 32f + 32f, 1e-4f, "twelve groups: the eleventh and twelfth 32 apart");
            Program.Near(MarkingWarnings.TollPlazaLength(2), MarkingWarnings.TollPlazaLength(5), 1e-4f, "five at least");
            Program.Near(MarkingWarnings.TollPlazaLength(20), MarkingWarnings.TollPlazaLength(12), 1e-4f, "twelve at most");
            Bounds(five.Mesh, out _, out _, out float fz0, out float fz1);
            Program.Near(fz1 - fz0, 44f + 0.45f, 1e-3f, "the span of five groups: 44 m between the farthest and the nearest, plus the nearest's one row: " + (fz1 - fz0));

            MarkingShape island = MarkingWarnings.TollIsland(2.2f);
            Sound(island.Mesh, "toll island");
            Program.Check(island.Colour == MarkingColour.White && MarkingWarnings.TollIsland(2.2f, true).Colour == MarkingColour.Yellow, "white, yellow where it divides the directions");
            Bounds(island.Mesh, out float ix0, out float ix1, out float iz0, out float iz1);
            Program.Near(ix1 - ix0, 2.2f, 1e-4f, "the island's width");
            Program.Near(iz0, 0f, 1e-4f, "from the tip of the thick line");
            Program.Near(iz1, 15f, 1e-4f, "500 of thick line and 1000 of nose");
            Program.Check(island.Area > 0.2f * 5f + 1f && island.Area < 1f + 0.5f * 2.2f * 10f * 0.7f, "the thick line, the outline and the V lines: " + island.Area);
            Program.Check(MarkingWarnings.TollIsland(0f).Mesh.TriangleCount == 0, "no width, nothing");
        }

        private static void Railway()
        {
            List<MarkingShape> lc = MarkingWarnings.LevelCrossing(3.5f);
            Program.Check(lc.Count == 1, "one shape");
            Sound(lc[0].Mesh, "level crossing");
            Bounds(lc[0].Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(x1 - x0, 3.5f, 1e-3f, "the stop line across the lane");
            Program.Near(z0, -0.4f, 1e-4f, "the stop line 40 wide");
            Program.Check(z1 > 10f && z1 < 10.5f, "the X ends 10 m on: " + z1);
            Program.Check(lc[0].Texts.Count == 2 && lc[0].Texts[0].Text == "铁" && lc[0].Texts[1].Text == "路", "the words 铁 and 路");
            Program.Check(lc[0].Texts[0].Length == 2f && lc[0].Texts[0].Width == 0.7f, "2 m high, 70 wide");
            Program.Check(lc[0].Texts[0].X < -1.5f && lc[0].Texts[1].X > 1.5f, "beside the X");
            List<MarkingShape> narrow = MarkingWarnings.LevelCrossing(3f);
            Program.Check(narrow[0].Texts[0].Length == 1.7f && narrow[0].Texts[0].Width == 0.6f, "a lane of 3 m or less: 1.7 m and 60");
            Bounds(narrow[0].Mesh, out _, out _, out _, out _);
            Program.Check(MarkingWarnings.LevelCrossing(0f).Count == 0, "no lane, nothing");
            // The X: two ribbons 40 wide from (-1.5, 4) to (1.5, 10) and back: 2 x 0.4 x sqrt(9 + 36) = 5.37 less the overlap; the dashed line 60/60 across 3.5 m, 40 wide; the stop line 3.5 x 0.4.
            float diag = (float)Math.Sqrt(9f + 36f);
            Program.Check(lc[0].Area > 2f * 0.4f * diag * 0.8f + 1.4f && lc[0].Area < 2f * 0.4f * diag + 1.4f + 0.4f * 3.5f + 0.1f, "the X, the stop line and the dashed line: " + lc[0].Area);
        }
    }
}
