using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The figures read off the drawings of GB 5768.3-2025 (MarkingFigures): diamond, arrows of crossings and bays, bay rows, bus bays, humps, no-parking lines, centre circles, grids, variable lane line, forbidden turns.</summary>
    internal static class MarkingFigureTests
    {
        public static void RunAll()
        {
            Program.Run("figures: the diamond, the crossing arrow, the bay arrow", Small);
            Program.Run("figures: rows of bays, their words, the hatched zone, the bicycle bay", Bays);
            Program.Run("figures: the bus bay in its five forms and the kerb stop", BusStops);
            Program.Run("figures: the hump chevron, the small triangles, the warning bars", Humps);
            Program.Run("figures: no-parking lines, centre circle and diamond, grids", Areas);
            Program.Run("figures: the variable guide lane line and the forbidden-turn marks", Lanes);
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

        private static void Small()
        {
            MarkingShape diamond = MarkingFigures.Diamond();
            Sound(diamond.Mesh, "diamond");
            Bounds(diamond.Mesh, out float dx0, out float dx1, out float dz0, out float dz1);
            Program.Near(dx1 - dx0, 1.5f, 1e-4f, "Fig. 15: 150 wide");
            Program.Near(dz1 - dz0, 3f, 1e-4f, "300 long");
            Program.Near(dz0, 0f, 1e-4f, "its point at z 0");
            Program.Check(diamond.Area > 0.5f && diamond.Area < 2.25f, "a band, not a filled rhombus (2.25 m2): " + diamond.Area);
            Program.Check(diamond.Colour == MarkingColour.White, "white");

            MarkingShape ped = MarkingFigures.PedestrianArrow();
            Sound(ped.Mesh, "pedestrian arrow");
            Bounds(ped.Mesh, out float px0, out float px1, out float pz0, out float pz1);
            Program.Near(pz1 - pz0, 1f, 1e-5f, "Fig. 17: 1 m long");
            Program.Near(px1 - px0, 0.45f, 1e-5f, "head 45 wide (15 + 15 + 15)");
            Program.Near(ped.Area, 0.15f * 0.6f + 0.45f * 0.4f * 0.5f, 1e-5f, "shaft 15 x 60 and head 45 x 40 / 2");

            MarkingShape bay = MarkingFigures.BayArrow();
            Sound(bay.Mesh, "bay arrow");
            Bounds(bay.Mesh, out float bx0, out float bx1, out float bz0, out float bz1);
            Program.Near(bz1 - bz0, 1.2f, 1e-5f, "Fig. 23: 120 long");
            Program.Near(bx1 - bx0, 0.18f, 1e-5f, "head 18 wide (6 + 6 + 6)");
            Program.Near(bay.Area, 0.06f * 0.72f + 0.18f * 0.48f * 0.5f, 1e-5f, "shaft 6 x 72 and head 18 x 48 / 2");
            Program.Check(MarkingFigures.BayArrowTipFromEdge == 0.5f, "the tip 50 below the bay's edge");
        }

        private static void Bays()
        {
            // Perpendicular bays: 4 bays 2.5 m wide and 5 m deep on the left of a kerb along +Z: five dividers to x = -5 and the outer line; the kerb line only when asked.
            MarkingShape row = MarkingFigures.BayRow(4, 2.5f, 5f, 90f, LineSide.Solid(0.1f, MarkingColour.White));
            Sound(row.Mesh, "bay row");
            Bounds(row.Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(x0, -5f - 0.05f, 1e-4f, "to the depth (the outer line's half width beyond)");
            Program.Check(x1 <= 0f + 1e-4f && x1 >= -0.05f - 1e-4f, "up to the kerb (the divider's end at x 0)");
            Program.Near(z0, -0.05f, 1e-4f, "the first divider's half width before z 0");
            Program.Near(z1, 10.05f, 1e-4f, "four bays of 2.5");
            Program.Near(row.Area, 5f * 5f * 0.1f + 10f * 0.1f, 0.05f, "five dividers of 5 m and the outer line of 10 m, 10 cm wide: " + row.Area);
            MarkingShape withKerb = MarkingFigures.BayRow(4, 2.5f, 5f, 90f, LineSide.Solid(0.1f, MarkingColour.White), kerbLine: true);
            Program.Near(withKerb.Area - row.Area, 10f * 0.1f, 0.05f, "the kerb line adds 10 m");

            // Angled bays at 45 degrees: the dividers lean forward-left; their ends are depth along the lean.
            MarkingShape angled = MarkingFigures.BayRow(3, 3.5f, 5f, 45f, LineSide.Solid(0.08f, MarkingColour.White));
            Sound(angled.Mesh, "angled bays");
            Bounds(angled.Mesh, out float ax0, out _, out _, out float az1);
            Program.Near(ax0, -5f * (float)Math.Sin(Math.PI / 4), 0.1f, "45 degrees: the ends 3.54 m into the road");
            Program.Near(az1, 3f * 3.5f + 5f * (float)Math.Cos(Math.PI / 4), 0.1f, "and 3.54 m further along");

            // Dashed bays (线19: 50/25, 线22: 60/60): less paint than solid.
            MarkingShape dashed = MarkingFigures.BayRow(4, 2.5f, 5f, 90f, LineSide.Dashed(0.1f, MarkingColour.White, 0.6f, 0.6f));
            Program.Check(dashed.Area < row.Area * 0.6f && dashed.Area > row.Area * 0.4f, "60/60 dashed: about half the paint: " + dashed.Area + " / " + row.Area);

            // Bad input.
            Program.Check(MarkingFigures.BayRow(0, 2.5f, 5f, 90f, LineSide.Solid(0.1f, MarkingColour.White)).Mesh.TriangleCount == 0 && MarkingFigures.BayRow(3, 0f, 5f, 90f, LineSide.Solid(0.1f, MarkingColour.White)).Mesh.TriangleCount == 0 && MarkingFigures.BayRow(3, 2.5f, 5f, 0f, LineSide.Solid(0.1f, MarkingColour.White)).Mesh.TriangleCount == 0 && MarkingFigures.BayRow(3, 2.5f, 5f, 91f, LineSide.Solid(0.1f, MarkingColour.White)).Mesh.TriangleCount == 0, "no bays, no pitch, no angle: nothing");

            // The centre of a bay and the words: 出租车 is three boxes 80 x 120, 50 apart, the first farthest along the bay.
            Vec3 centre = MarkingFigures.BayCentre(1, 2.5f, 5f, 90f, out Vec3 along);
            Program.Near(centre.X, -2.5f, 1e-5f, "the centre is half the depth in");
            Program.Near(centre.Z, 3.75f, 1e-5f, "and half way along the second bay");
            Program.Near(along.X, -1f, 1e-5f, "the bay runs into the road");
            List<MarkingText> words = MarkingFigures.BayWords("出租车", P(0f, 10f), MarkingFigures.BayWordWidth, MarkingFigures.BayWordHeight, MarkingFigures.BayWordGap);
            Program.Check(words.Count == 3 && words[0].Text == "出" && words[2].Text == "车", "three characters");
            Program.Near(words[1].Z - words[0].Z, 1.7f, 1e-5f, "120 high and 50 apart: 170 between centres");
            Program.Near(words[0].Z - 0.6f, 10f - (3f * 1.2f + 2f * 0.5f) * 0.5f, 1e-5f, "the first at the near end of the block centred on the place (read from near to far)");
            Program.Check(words[1].Width == 0.8f && words[1].Length == 1.2f, "80 x 120");
            Program.Check(MarkingFigures.BayWords("", P(0, 0), 1f, 1f, 1f).Count == 0, "no text, no words");
            Program.Check(MarkingFigures.BayTimeHeight == 0.6f, "digits of a time 60 high (5.10.8)");

            // The hatched zone beside a disabled bay: 1.2 x 5, outline and 45-degree lines 1 m apart.
            MarkingShape zone = MarkingFigures.HatchedZone(-5f, 0f, -5f + 1.2f, 5f);
            Sound(zone.Mesh, "hatched zone");
            Bounds(zone.Mesh, out float hx0, out float hx1, out float hz0, out float hz1);
            Program.Near(hx1 - hx0, 1.2f, 1e-4f, "120 wide");
            Program.Near(hz1 - hz0, 5f, 1e-4f, "as long as the bay");
            Program.Check(zone.Area > 0.08f * (2f * 6.2f) && zone.Area < 1.2f * 5f * 0.5f, "an outline and some lines, not a solid: " + zone.Area);
            Program.Check(MarkingFigures.DisabledZoneWidth == 1.2f, "Fig. 26: 120");

            // The bicycle bay: an outline.
            MarkingShape bike = MarkingFigures.BicycleBay(6f, 2f);
            Sound(bike.Mesh, "bicycle bay");
            Program.Near(bike.Area, 6f * 2f - (6f - 0.16f) * (2f - 0.16f), 1e-3f, "8 cm band round 6 x 2");
            Program.Check(MarkingFigures.BicycleBay(0f, 2f).Mesh.TriangleCount == 0, "no width, nothing");
        }

        private static void BusStops()
        {
            foreach (BusBayForm form in Enum.GetValues(typeof(BusBayForm)))
            {
                List<MarkingShape> bay = MarkingFigures.BusBay(form, 25f, 30f, 25f, 3f);
                string what = "bus bay " + form;
                Program.Check(bay.Count >= 1 && bay.Count <= 2, what + ": one or two shapes");
                foreach (MarkingShape s in bay)
                {
                    Sound(s.Mesh, what);
                    Program.Check(s.Colour == MarkingColour.White, what + ": white (5.11.3)");
                }

                Bounds(bay[0].Mesh, out float x0, out float x1, out float z0, out float z1);
                Program.Check(x0 >= -0.3f && x1 <= 3.3f, what + ": between the lane edge and the kerb: " + x0 + ".." + x1);
                Program.Check(z0 <= 0.5f && z1 >= 79.5f, what + ": from the start of the first taper to the end of the second: " + z0 + ".." + z1);
                bool hasWords = form == BusBayForm.C || form == BusBayForm.D || form == BusBayForm.E;
                Program.Check((bay.Count == 2) == hasWords, what + ": the words and zigzags only in c), d), e)");
                if (hasWords)
                {
                    Program.Check(bay[1].Texts.Count == 3 && bay[1].Texts[0].Text == "公" && bay[1].Texts[2].Text == "车", what + ": 公交车, the first nearest");
                    Program.Check(bay[1].Texts[0].Width == 1f && bay[1].Texts[0].Length == 2.5f, what + ": 100 x 250");
                    Program.Near(bay[1].Texts[1].Z - bay[1].Texts[0].Z, 4.5f, 1e-4f, what + ": 250 high, 200 apart");
                    Program.Near(bay[1].Texts[1].Z, 25f + 15f, 1e-4f, what + ": the middle character in the middle of the straight part");
                    Program.Check(bay[1].Mesh.TriangleCount > 8, what + ": zigzags with their bars");
                }
            }

            // The tapers are dashed 100/100 and 45 wide: the paint of form a) is the two tapers (half of their length) and the solid middle.
            float taper = (float)Math.Sqrt(25f * 25f + 9f);
            MarkingShape a = MarkingFigures.BusBay(BusBayForm.A, 25f, 30f, 25f, 3f)[0];
            Program.Near(a.Area, 0.45f * (taper + 30f), 0.45f * 2.5f, "form a): two dashed tapers and a 30 m solid line, 45 wide: " + a.Area);
            MarkingShape e = MarkingFigures.BusBay(BusBayForm.E, 25f, 30f, 25f, 3f)[0];
            Program.Near(e.Area, 0.45f * (taper + 15f), 0.45f * 2.5f, "form e): dashed all along: " + e.Area);
            MarkingShape b = MarkingFigures.BusBay(BusBayForm.B, 25f, 30f, 25f, 3f)[0];
            Program.Check(b.Area > a.Area, "form b): the hatched band is more paint than the solid line");
            Program.Check(MarkingFigures.BusBay(BusBayForm.A, 0f, 30f, 25f, 3f).Count == 0 && MarkingFigures.BusBay(BusBayForm.A, 25f, 30f, 25f, 0f).Count == 0, "no taper or no depth: nothing");
            Program.Check(MarkingFigures.BayTaperMin == 25f, "5.11.4: a taper of 25 m or more");

            // The kerb stop: an edge line 20 wide and the words with zigzags.
            List<MarkingShape> stop = MarkingFigures.KerbStop(20f, 3f, "校车");
            Program.Check(stop.Count == 2, "edge and inside");
            Sound(stop[0].Mesh, "kerb stop edge");
            Sound(stop[1].Mesh, "kerb stop inside");
            float actualLength = Math.Max(20f, MarkingFigures.BusStopMinimumLength(3f, 0.2f, "校车"));
            Program.Near(stop[0].Area, 0.2f * actualLength, 1e-3f, "edge line 20 wide over the length needed for text and zigzags");
            Program.Check(stop[1].Texts.Count == 2 && stop[1].Texts[0].Text == "校", "校车");
            Bounds(stop[1].Mesh, out float sx0, out float sx1, out float sz0, out float sz1);
            Program.Check(sx0 >= 0.2f - 1e-3f && sx1 <= 3f - 0.1f + 1e-3f, "the zigzags between the edge line and the kerb: " + sx0 + ".." + sx1);
            Program.Check(sz0 >= 0.5f - 0.11f && sz1 <= actualLength - 0.5f + 0.11f, "50 from the ends: " + sz0 + ".." + sz1);
            Program.Check(MarkingFigures.KerbStop(0f, 3f).Count == 0, "no length, nothing");
        }

        private static void Humps()
        {
            MarkingShape chevron = MarkingFigures.HumpChevron();
            Sound(chevron.Mesh, "hump chevron");
            Bounds(chevron.Mesh, out float cx0, out float cx1, out float cz0, out float cz1);
            Program.Near(cx1 - cx0, 1.8f, 1e-5f, "Fig. 35: 180 wide");
            Program.Near(cz1 - cz0, 1.8f, 1e-5f, "180 long");
            Program.Check(chevron.Mesh.Positions.Exists(p => Math.Abs(p.X) < 1e-6f && Math.Abs(p.Z - 1.8f) < 1e-6f), "the apex forward (+Z), the way the traffic runs (Figs. 36, 39)");
            // The strokes are 30 wide: the area of the outer triangle less the inner one.
            double ang = Math.Atan2(1.8, 0.9);
            float dz = (float)(0.3 / Math.Sin(ang)), dx = (float)(0.3 / Math.Cos(ang));
            Program.Near(chevron.Area, 0.5f * 1.8f * 1.8f - 0.5f * (1.8f - 2f * dx) * (1.8f - dz), 1e-4f, "strokes 30 wide");

            MarkingShape small = MarkingFigures.SmallHumpTriangles(5);
            Sound(small.Mesh, "small hump");
            Program.Check(small.Mesh.TriangleCount == 5, "five triangles");
            Program.Near(small.Area, 5f * 0.5f * 0.3f * 0.45f, 1e-5f, "each 30 x 45 / 2");
            Bounds(small.Mesh, out float sx0, out float sx1, out float sz0, out float sz1);
            Program.Near(sx1 - sx0, 4f * 0.6f + 0.3f, 1e-5f, "60 apart: 2.7 m over five");
            Program.Near(sz1, 0.45f, 1e-5f, "45 long");
            Program.Check(small.Mesh.Positions.Exists(p => Math.Abs(p.X) < 1e-6f && p.Z == 0f), "the apex at z 0, toward the approaching traffic (Fig. 38)");
            Program.Check(MarkingFigures.SmallHumpTriangles(0).Mesh.TriangleCount == 0, "none");

            MarkingShape bars = MarkingFigures.HumpWarningBars();
            Sound(bars.Mesh, "warning bars");
            Program.Check(bars.Mesh.TriangleCount == 16, "eight bars");
            float expected = 0f;
            for (int i = 1; i <= 8; i++)
                expected += 0.3f * i * 0.3f;
            Program.Near(bars.Area, expected, 1e-4f, "30 thick, 30 to 240 wide");
            Bounds(bars.Mesh, out float bx0, out float bx1, out float bz0, out float bz1);
            Program.Near(bx1 - bx0, 2.4f, 1e-5f, "the widest 240");
            Program.Near(bz1 - bz0, MarkingFigures.HumpWarningLength() + 0.3f, 1e-4f, "from the first bar to the last: " + (bz1 - bz0));
            Program.Near(MarkingFigures.HumpWarningLength(), 29.7f, 1e-4f, "610 + 540 + 490 + 420 + 370 + 300 + 240 = 29.7 m");
        }

        private static void Areas()
        {
            MarkingShape two = MarkingFigures.NoParkingLine(10f, twoLines: true);
            Sound(two.Mesh, "no parking two lines");
            Program.Near(two.Area, 2f * 0.05f * 10f + 2f * 0.15f * 0.05f, 1e-5f, "two 5 cm lines and two 5 cm bars across 15");
            Bounds(two.Mesh, out float nx0, out float nx1, out _, out _);
            Program.Near(nx1 - nx0, 0.15f, 1e-5f, "15 across in all (5 + 5 + 5)");
            MarkingShape one = MarkingFigures.NoParkingLine(10f, twoLines: false);
            Program.Near(one.Area, 0.05f * 10f + 2f * 0.15f * 0.05f, 1e-5f, "one 5 cm line and two 15 cm bars");
            Program.Check(MarkingFigures.NoParkingLine(0f, true).Mesh.TriangleCount == 0, "no length, nothing");

            MarkingShape circle = MarkingFigures.CentreCircle();
            Sound(circle.Mesh, "centre circle");
            Bounds(circle.Mesh, out float cx0, out float cx1, out float cz0, out float cz1);
            Program.Near(cx1 - cx0, 1.2f, 0.01f, "diameter 1.2 m at least");
            Program.Near(cz1 - cz0, 1.2f, 0.01f, "round");
            Program.Check(circle.Area > 0.4f && circle.Area < (float)(Math.PI * 0.36 * 0.8), "a ring with strips inside, not a disc: " + circle.Area);
            Bounds(MarkingFigures.CentreCircle(2f).Mesh, out float wx0, out float wx1, out _, out _);
            Program.Near(wx1 - wx0, 2f, 0.01f, "or wider");
            Bounds(MarkingFigures.CentreCircle(0.5f).Mesh, out float mx0, out float mx1, out _, out _);
            Program.Near(mx1 - mx0, 1.2f, 0.01f, "never under 1.2");

            MarkingShape rhombus = MarkingFigures.CentreDiamond();
            Sound(rhombus.Mesh, "centre diamond");
            Bounds(rhombus.Mesh, out float rx0, out float rx1, out float rz0, out float rz1);
            Program.Near(rx1 - rx0, 1.5f, 1e-4f, "diagonal 1.5 m at least");
            Program.Near(rz1 - rz0, 1.5f, 1e-4f, "both ways");
            Program.Check(rhombus.Area > 0.3f && rhombus.Area < 0.5f * 1.5f * 1.5f * 0.8f, "a band with strips: " + rhombus.Area);

            MarkingShape grid = MarkingFigures.Grid(6f, 8f, 1f);
            Sound(grid.Mesh, "grid");
            Bounds(grid.Mesh, out float gx0, out float gx1, out float gz0, out float gz1);
            Program.Near(gx1 - gx0, 6f, 1e-4f, "6 wide");
            Program.Near(gz1 - gz0, 8f, 1e-4f, "8 long");
            float outline = 6f * 8f - 5.6f * 7.6f;
            Program.Check(grid.Area > outline + 2f && grid.Area < 48f * 0.5f, "outline 20 and two families of 10 cm lines: " + grid.Area);
            Program.Check(MarkingFigures.Grid(6f, 8f, 5f).Area < grid.Area && MarkingFigures.Grid(6f, 8f, 0.2f).Area == grid.Area, "a wider pitch is less paint; under 1 m is 1 m");

            MarkingShape simple = MarkingFigures.SimpleGrid(6f, 8f);
            Sound(simple.Mesh, "simple grid");
            float diag = (float)Math.Sqrt(36f + 64f);
            Program.Near(simple.Area, (6f * 8f - 5.2f * 7.2f) + 2f * 0.4f * diag, 0.6f, "outline and two diagonals, 40 wide: " + simple.Area);
            Program.Check(MarkingFigures.SimpleGrid(13f, 8f).Mesh.TriangleCount == 0, "a side over 12 m: not the simple form (6.8.3)");
        }

        private static void Lanes()
        {
            var centre = new List<Vec3> { P(0f, 0f), P(0f, 10f) };
            MarkingShape vg = MarkingFigures.VariableGuideLaneLine(centre, ticksOnRight: true);
            Sound(vg.Mesh, "variable guide lane line");
            Bounds(vg.Mesh, out float x0, out float x1, out _, out _);
            Program.Near(x0, -0.075f, 1e-4f, "the line 15 wide on the centre");
            Program.Check(x1 > 0.075f + 0.3f - 0.05f && x1 < 0.075f + 0.3f + 0.15f, "ticks reaching 30 across on the right: " + x1);
            Program.Check(vg.Mesh.TriangleCount == 2 + 10 * 2, "ten ticks over 10 m (one a metre): " + vg.Mesh.TriangleCount);
            Program.Near(vg.Area - 0.15f * 10f, 10f * 0.15f * (float)Math.Sqrt(0.3 * 0.3 + 0.35 * 0.35), 0.05f, "each tick a 15 cm bar about 46 cm long: " + (vg.Area - 1.5f));
            Bounds(MarkingFigures.VariableGuideLaneLine(centre, ticksOnRight: false).Mesh, out float lx0, out _, out _, out _);
            Program.Check(lx0 < -0.3f, "or on the left");
            Bounds(MarkingFigures.VariableGuideLaneLine(centre, true, 0.2f).Mesh, out float wx0, out _, out _, out _);
            Program.Near(wx0, -0.1f, 1e-4f, "20 wide when asked");

            // Forbidden U-turn: the yellow U-turn arrow and a cross as wide and long, 50 to its left, bars as wide as the shaft.
            MarkingShape no = MarkingFigures.ForbiddenTurn(ArrowKind.UTurn, ArrowSize.Line31);
            Sound(no.Mesh, "no U-turn");
            Program.Check(no.Colour == MarkingColour.Yellow, "yellow (6.11.2)");
            MarkingShape arrow = RoadArrows.Shape(ArrowKind.UTurn, ArrowSize.Line31);
            Bounds(arrow.Mesh, out float ax0, out float ax1, out _, out float az1);
            Bounds(no.Mesh, out float nx0, out float nx1, out float nz0, out float nz1);
            Program.Near(nx1, ax1, 1e-4f, "the arrow where it was");
            Program.Near(nx0, ax0 - 0.5f - (ax1 - ax0), 0.2f, "the cross as wide as the arrow, 50 to the left: " + nx0);
            Program.Near(nz1, az1, 0.3f * 0.5f + 1e-4f, "as long as the arrow (the bars' square ends reach a little past the corners): " + nz1);
            // The two bars of the cross are ribbons as wide as the arrow's shaft (30 at 6 m) along the diagonal of the arrow's box.
            float diagonal = (float)Math.Sqrt((ax1 - ax0) * (ax1 - ax0) + az1 * az1);
            Program.Near(no.Area - arrow.Area, 2f * 0.3f * diagonal, 0.02f, "the cross: two bars 30 wide along the diagonal: " + (no.Area - arrow.Area) + " / " + 2f * 0.3f * diagonal);
            float diagonal3 = (float)Math.Sqrt(0.5f * 0.5f * (ax1 - ax0) * (ax1 - ax0) + 0.25f * az1 * az1);
            MarkingShape half = MarkingFigures.ForbiddenTurn(ArrowKind.UTurn, ArrowSize.Line30);
            MarkingShape halfArrow = RoadArrows.Shape(ArrowKind.UTurn, ArrowSize.Line30);
            Program.Near(half.Area - halfArrow.Area, 2f * 0.15f * diagonal3, 0.02f, "the bars scale with the arrow's size (15 at 3 m): " + (half.Area - halfArrow.Area));
            Program.Check(no.Area > arrow.Area + 2f * 0.3f * (ax1 - ax0) * 0.8f, "the cross adds two bars about as long as the diagonal: " + (no.Area - arrow.Area));
            Bounds(MarkingFigures.ForbiddenTurn(ArrowKind.UTurn, ArrowSize.Line31, 1f).Mesh, out float gx0, out _, out _, out _);
            Program.Near(nx0 - gx0, 0.5f, 1e-4f, "a 1 m gap moves the cross 50 further");
            Bounds(MarkingFigures.ForbiddenTurn(ArrowKind.UTurn, ArrowSize.Line31, 3f).Mesh, out float cx0, out _, out _, out _);
            Program.Near(cx0, gx0, 1e-4f, "the gap is 100 at most");
            MarkingShape noLeft = MarkingFigures.ForbiddenTurn(ArrowKind.Left, ArrowSize.Line30);
            Sound(noLeft.Mesh, "no left turn");
            Bounds(noLeft.Mesh, out _, out _, out _, out float lz1);
            Program.Near(lz1, 3f, 0.15f * 0.5f + 1e-4f, "the 3 m size when asked: " + lz1);
        }
    }
}
