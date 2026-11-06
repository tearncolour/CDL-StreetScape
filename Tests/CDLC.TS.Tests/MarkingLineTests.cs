using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The fills (hatching, outlines, offsets) and the text-dimensioned lines of GB 5768.3-2025 (MarkingFills, MarkingLines).</summary>
    internal static class MarkingLineTests
    {
        public static void RunAll()
        {
            Program.Run("fills: hatching a rectangle, a triangle and a concave area", Hatching);
            Program.Run("fills: the outline band of a polygon, the offset of a line", OutlinesAndOffsets);
            Program.Run("lines: pairs of lines (double yellow, solid-and-dashed, tidal, bicycle crossing)", Pairs);
            Program.Run("lines: edge lines, junction guide, lane change, reserved lanes, guide lane", Singles);
            Program.Run("lines: chevrons, half discs, zigzag, give-way dashed line", Figures);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static float Area(MeshBuilder mesh)
        {
            double sum = 0.0;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                sum += MeshBuilder.Cross(b - a, c - a).Length * 0.5;
            }

            return (float)sum;
        }

        private static void Sound(MeshBuilder mesh, string what)
        {
            Program.Check(mesh.Indices.Count % 3 == 0, what + ": whole triangles");
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

        private static bool InsideRect(Vec3 p, float x0, float z0, float x1, float z1, float tol) => p.X >= x0 - tol && p.X <= x1 + tol && p.Z >= z0 - tol && p.Z <= z1 + tol;

        private static void Hatching()
        {
            // A 4 x 10 m rectangle hatched at 45 degrees with 40 cm bands at 1 m pitch: the bands stay inside, each is 40 cm wide (measured across), the covered share is about 0.4 / 1.
            List<Vec3> rect = MarkingFills.Rectangle(0f, 0f, 4f, 10f);
            var mesh = new MeshBuilder();
            int bands = MarkingFills.Hatch(mesh, rect, 0.4f, 1f, 45f);
            Sound(mesh, "hatch rect");
            Program.Check(bands > 8, "a 4 x 10 area at 45 degrees and 1 m pitch has many bands: " + bands);
            foreach (Vec3 p in mesh.Positions)
                Program.Check(InsideRect(p, 0f, 0f, 4f, 10f, 1e-3f), "every corner of a band is inside the rectangle (" + p + ")");
            float covered = Area(mesh) / 40f;
            Program.Check(covered > 0.36f && covered < 0.44f, "about 40 % of the area is painted: " + covered);
            // The bands run at 45 degrees at 1 m pitch from the corner of least (x cos 45 - z sin 45): every triangle centroid lies within 20 cm (across) of a band's centre line.
            float nx = (float)Math.Cos(Math.PI / 4), nz = -(float)Math.Sin(Math.PI / 4);
            float tMin = float.MaxValue;
            foreach (Vec3 p in rect)
                tMin = Math.Min(tMin, p.X * nx + p.Z * nz);
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 c = (mesh.Positions[mesh.Indices[i]] + mesh.Positions[mesh.Indices[i + 1]] + mesh.Positions[mesh.Indices[i + 2]]) * (1f / 3f);
                float acrossBand = (c.X * nx + c.Z * nz - tMin) % 1f;
                float toCentre = Math.Min(acrossBand, 1f - acrossBand);
                Program.Check(toCentre <= 0.2f + 1e-4f, "a piece of paint lies on a 45-degree band at 1 m pitch (" + toCentre + " m off)");
            }

            // 135 degrees leans the other way; 0 degrees is along the road; 90 is across.
            var mesh2 = new MeshBuilder();
            MarkingFills.Hatch(mesh2, rect, 0.4f, 1f, 135f);
            Program.Near(Area(mesh2), Area(mesh), Area(mesh) * 0.1f, "the other slant covers about the same");
            var along = new MeshBuilder();
            int n0 = MarkingFills.Hatch(along, rect, 0.4f, 1f, 0f);
            Program.Check(n0 == 5, "along the road at 1 m pitch across 4 m: five bands (x 0, 1, 2, 3, 4; the two at the edges half outside, cut): " + n0);
            Bounds(along, out _, out _, out float az0, out float az1);
            Program.Near(az1 - az0, 10f, 1e-3f, "and each runs the whole 10 m");
            var across = new MeshBuilder();
            int n90 = MarkingFills.Hatch(across, rect, 0.4f, 1f, 90f);
            Program.Check(n90 == 11, "across the road at 1 m pitch along 10 m: eleven bands (z 0 to 10): " + n90);
            Program.Near(Area(across), 0.4f * 4f * 10f, 1e-3f, "ten whole bands' worth of paint (the two half bands make one): " + Area(across));

            // The phase moves the family.
            var shifted = new MeshBuilder();
            MarkingFills.Hatch(shifted, rect, 0.4f, 1f, 0f, 0.5f);
            Bounds(along, out float ax0, out _, out _, out _);
            Bounds(shifted, out float sx0, out _, out _, out _);
            Program.Check(Math.Abs(Math.Abs(sx0 - ax0) - 0.5f) < 1e-3f || Math.Abs(Math.Abs(sx0 - ax0) - 0.5f) < 1e-3f + 1f, "a phase of 0.5 m moves the bands by 0.5 m: " + ax0 + " -> " + sx0);

            // A triangle (the end of an island): the bands get shorter toward the point and all stay inside.
            var tri = new List<Vec3> { P(0f, 0f), P(3f, 0f), P(1.5f, 12f) };
            var triMesh = new MeshBuilder();
            int triBands = MarkingFills.Hatch(triMesh, tri, 0.45f, 1f, 45f);
            Sound(triMesh, "hatch triangle");
            Program.Check(triBands > 5, "a tall triangle has several bands: " + triBands);
            foreach (Vec3 p in triMesh.Positions)
                Program.Check(Polygon.Cross(tri[0], tri[1], p) >= -1e-2f && Polygon.Cross(tri[1], tri[2], p) >= -1e-2f && Polygon.Cross(tri[2], tri[0], p) >= -1e-2f, "a band corner is inside the triangle: " + p);

            // A concave area (a U): a band that crosses the gap is two bands, none inside the gap.
            var u = new List<Vec3> { P(0f, 0f), P(6f, 0f), P(6f, 6f), P(4f, 6f), P(4f, 2f), P(2f, 2f), P(2f, 6f), P(0f, 6f) };
            var uMesh = new MeshBuilder();
            MarkingFills.Hatch(uMesh, u, 0.3f, 0.8f, 90f);
            Sound(uMesh, "hatch U");
            for (int i = 0; i + 2 < uMesh.Indices.Count; i += 3)
            {
                Vec3 c = (uMesh.Positions[uMesh.Indices[i]] + uMesh.Positions[uMesh.Indices[i + 1]] + uMesh.Positions[uMesh.Indices[i + 2]]) * (1f / 3f);
                Program.Check(!(c.X > 2.01f && c.X < 3.99f && c.Z > 2.01f), "no band in the gap of the U: " + c);
            }

            // Bad input.
            Program.Check(MarkingFills.Hatch(new MeshBuilder(), null, 0.4f, 1f, 45f) == 0 && MarkingFills.Hatch(new MeshBuilder(), rect, 0f, 1f, 45f) == 0 && MarkingFills.Hatch(new MeshBuilder(), rect, 0.4f, 0f, 45f) == 0 && MarkingFills.Hatch(new MeshBuilder(), rect, 0.4f, 1f, float.NaN) == 0, "no area, width, pitch or angle: no bands");
        }

        private static void OutlinesAndOffsets()
        {
            // The outline of a 3 x 10 rectangle, 20 cm wide: four quads, area = outer - inner = 30 - 2.6 x 9.6 = 5.04.
            List<Vec3> rect = MarkingFills.Rectangle(0f, 0f, 3f, 10f);
            var mesh = new MeshBuilder();
            int quads = MarkingFills.Outline(mesh, rect, 0.2f);
            Sound(mesh, "outline");
            Program.Check(quads == 4, "four edges, four quads");
            Program.Near(Area(mesh), 30f - 2.6f * 9.6f, 1e-3f, "the band is inside the figure's size: " + Area(mesh));
            foreach (Vec3 p in mesh.Positions)
                Program.Check(InsideRect(p, 0f, 0f, 3f, 10f, 1e-6f), "the band does not reach outside");
            // Clockwise input: the same band.
            var cw = new List<Vec3>(rect);
            cw.Reverse();
            var mesh2 = new MeshBuilder();
            MarkingFills.Outline(mesh2, cw, 0.2f);
            Program.Near(Area(mesh2), Area(mesh), 1e-4f, "either order");
            // The diamond of 线11 (150 x 300, 20 wide) is a rhombus outline.
            var diamond = new List<Vec3> { P(0f, 0f), P(0.75f, 1.5f), P(0f, 3f), P(-0.75f, 1.5f) };
            var dm = new MeshBuilder();
            MarkingFills.Outline(dm, diamond, 0.2f);
            Sound(dm, "diamond");
            Program.Check(Area(dm) > 0.5f * 1.5f * 3f * 0.3f && Area(dm) < 0.5f * 1.5f * 3f, "the diamond band has a sensible area: " + Area(dm));
            Program.Check(MarkingFills.Outline(new MeshBuilder(), new List<Vec3> { P(0, 0), P(1, 0) }, 0.2f) == 0 && MarkingFills.Outline(new MeshBuilder(), rect, 0f) == 0, "two points or no width: nothing");

            // Offset: a line up +Z moved right by 1 is at x + 1; left by 1 at x - 1; a bend keeps the distance.
            var line = new List<Vec3> { P(0f, 0f), P(0f, 5f), P(0f, 10f) };
            List<Vec3> right = MarkingFills.Offset(line, 1f), left = MarkingFills.Offset(line, -1f);
            Program.Check(right.Count == 3 && Math.Abs(right[1].X - 1f) < 1e-6f && Math.Abs(left[1].X + 1f) < 1e-6f && Math.Abs(right[1].Z - 5f) < 1e-6f, "right is +X for a line running +Z");
            var arc = new List<Vec3>();
            Polygon.Arc(arc, 0f, 0f, 10f, 0f, (float)Math.PI / 2, 20);
            // A counter-clockwise arc runs with its centre on its left, so "right" is outward: radius 11; moved left it is inside.
            List<Vec3> outer = MarkingFills.Offset(arc, 1f), inner = MarkingFills.Offset(arc, -1f);
            for (int i = 1; i < arc.Count - 1; i++)
            {
                Program.Near(outer[i].LengthXZ, 11f, 0.02f, "a counter-clockwise arc moved right is 1 m outside (radius 11)");
                Program.Near(inner[i].LengthXZ, 9f, 0.02f, "and moved left 1 m inside (radius 9)");
            }
        }

        private static void Pairs()
        {
            var centre = new List<Vec3> { P(0f, 0f), P(0f, 30f) };

            // 线43 double yellow: two solid 15 cm lines 10 cm apart: the inner edges at -0.05 and +0.05, the outer at -0.2 and +0.2.
            List<MarkingShape> dy = MarkingLines.DoubleYellow(centre, 0.10f);
            Program.Check(dy.Count == 2 && dy[0].Colour == MarkingColour.Yellow && dy[1].Colour == MarkingColour.Yellow, "two yellow lines");
            Bounds(dy[0].Mesh, out float lx0, out float lx1, out _, out _);
            Bounds(dy[1].Mesh, out float rx0, out float rx1, out _, out _);
            Program.Near(lx0, -0.2f, 1e-4f, "left outer edge"); Program.Near(lx1, -0.05f, 1e-4f, "left inner edge");
            Program.Near(rx0, 0.05f, 1e-4f, "right inner edge"); Program.Near(rx1, 0.2f, 1e-4f, "right outer edge");
            Program.Near(dy[0].Area, 0.15f * 30f, 1e-3f, "solid: the whole length");
            // 50 cm apart: a third shape, the yellow hatching between the lines.
            List<MarkingShape> wide = MarkingLines.DoubleYellow(centre, 0.6f);
            Program.Check(wide.Count == 3 && wide[2].Colour == MarkingColour.Yellow && wide[2].Area > 0.3f * 0.6f * 30f * 0.8f, "60 cm apart: hatched between (" + (wide.Count == 3 ? wide[2].Area : 0f) + " m2)");
            Bounds(wide[2].Mesh, out float hx0, out float hx1, out _, out _);
            Program.Check(hx0 >= -0.3f - 1e-3f && hx1 <= 0.3f + 1e-3f, "the hatching stays between the lines");
            Program.Check(MarkingLines.DoubleYellow(centre, 0.3f).Count == 2 && MarkingLines.DoubleYellow(centre, 0.45f).Count == 2, "30 or 45 cm apart: no hatching (6.1.2: from 50)");
            Program.Check(MarkingLines.DoubleYellow(centre, 0.5f).Count == 3, "exactly 50 cm apart: hatched");
            Program.Check(MarkingLines.DoubleYellow(centre, 0.05f).Count == 2 && Math.Abs(MarkingLines.DoubleYellow(centre, 0.05f)[1].Mesh.Positions[0].X - 0.05f) < 1e-4f, "a gap under 10 cm is 10 cm");
            Program.Check(MarkingLines.DoubleYellow(centre, 0.1f, 0.1f)[0].Area < MarkingLines.DoubleYellow(centre, 0.1f, 0.15f)[0].Area, "the 10 cm width is narrower");

            // 线44 yellow solid-and-dashed: the dashed side as asked, 4-6 dashes.
            List<MarkingShape> ysd = MarkingLines.YellowSolidDashed(centre, dashedOnRight: true);
            Program.Near(ysd[0].Area, 0.15f * 30f, 1e-3f, "left solid");
            Program.Near(ysd[1].Area, 0.15f * 12f, 1e-3f, "right dashed 4-6 over 30 m: three dashes (12 m)");
            List<MarkingShape> ysdL = MarkingLines.YellowSolidDashed(centre, dashedOnRight: false);
            Program.Near(ysdL[0].Area, 0.15f * 12f, 1e-3f, "or the left is the dashed one");

            // 线47 white solid-and-dashed with the road's dashes (2-4): 30 m has five dashes.
            List<MarkingShape> wsd = MarkingLines.WhiteSolidDashed(centre, true, RoadMarkings.LaneLineSlow());
            Program.Check(wsd[1].Colour == MarkingColour.White, "white");
            Program.Near(wsd[1].Area, 0.15f * 10f, 1e-3f, "2-4 over 30 m: five dashes");

            // 线4 tidal: two yellow dashed lines with the road's dashes, 15 wide, 10 apart.
            List<MarkingShape> tidal = MarkingLines.TidalLaneLine(centre, RoadMarkings.LaneLineFast());
            Program.Check(tidal.Count == 2 && tidal[0].Colour == MarkingColour.Yellow && tidal[1].Colour == MarkingColour.Yellow, "two yellow");
            Program.Near(tidal[0].Area, 0.15f * 12f, 1e-3f, "6-9 over 30: two dashes");
            Bounds(tidal[1].Mesh, out float tx0, out _, out _, out _);
            Program.Near(tx0, 0.05f, 1e-4f, "10 cm apart");
            Program.Check(Math.Abs(MarkingLines.TidalLaneLine(centre, RoadMarkings.LaneLineFast(), 0.5f)[1].Mesh.Positions[0].X - 0.075f) < 1e-4f, "the gap is kept to 15 cm");

            // The tidal stop line: solid and 50/50 dashed, dashed on the side asked.
            List<MarkingShape> tsl = MarkingLines.TidalStopLine(centre, dashedOnRight: true);
            Program.Near(tsl[0].Area, 0.15f * 30f, 1e-3f, "solid left");
            Program.Near(tsl[1].Area, 0.15f * 15f, 1e-3f, "50/50 dashed right: half");

            // 线42 bicycle crossing: two white 1-1 dashed lines 10 wide, 2 m apart centre to centre (or wider).
            List<MarkingShape> bc = MarkingLines.BicycleCrossing(centre, 2f, 0.1f);
            Bounds(bc[0].Mesh, out float bx0, out float bx1, out _, out _);
            Bounds(bc[1].Mesh, out float cx0, out float cx1, out _, out _);
            Program.Near((cx0 + cx1) * 0.5f - (bx0 + bx1) * 0.5f, 2f, 1e-4f, "2 m centre to centre");
            Program.Near(bx1 - bx0, 0.1f, 1e-4f, "10 cm wide");
            Program.Near(bc[0].Area, 0.1f * 15f, 1e-3f, "1-1: half the length");
            Program.Check(Math.Abs(MarkingLines.BicycleCrossing(centre, 1f)[1].Mesh.Positions[0].X - MarkingLines.BicycleCrossing(centre, 2f)[1].Mesh.Positions[0].X) < 1e-5f, "narrower than 2 m is 2 m");
            Bounds(MarkingLines.BicycleCrossing(centre, 3.5f, 0.08f)[0].Mesh, out float ex0, out float ex1, out _, out _);
            Program.Near(ex1 - ex0, 0.08f, 1e-4f, "8 cm when asked");

            // Pair: bad input is empty.
            Program.Check(MarkingLines.Pair(null, 0.1f, LineSide.Solid(0.1f, MarkingColour.White), LineSide.Solid(0.1f, MarkingColour.White)).Count == 0 && MarkingLines.Pair(new List<Vec3> { P(0, 0) }, 0.1f, LineSide.Solid(0.1f, MarkingColour.White), LineSide.Solid(0.1f, MarkingColour.White)).Count == 0, "no line, no pair");
            Program.Check(MarkingLines.Pair(centre, float.NaN, LineSide.Solid(0.1f, MarkingColour.White), LineSide.Solid(0.1f, MarkingColour.White)).Count == 0, "a gap that is not a number: no pair");
        }

        private static void Singles()
        {
            var centre = new List<Vec3> { P(0f, 0f), P(0f, 60f) };
            // 线5/线6 edge lines: right white, left yellow; solid or 60/30; 15 or 20 (10); both white on a two-way single lane.
            MarkingShape rightSolid = MarkingLines.EdgeLine(centre, leftSide: false, dashed: false);
            Program.Check(rightSolid.Colour == MarkingColour.White, "right edge: white");
            Program.Near(rightSolid.Area, 0.15f * 60f, 1e-3f, "solid 15 cm");
            MarkingShape leftDashed = MarkingLines.EdgeLine(centre, leftSide: true, dashed: true, width: 0.2f);
            Program.Check(leftDashed.Colour == MarkingColour.Yellow, "left edge: yellow");
            // 60 on 30 off over 60 m: dashes start every 0.9 m from 0 to 59.4, the last one whole (59.4 to 60.0): 67 dashes of 0.6 m = 40.2 m, 20 wide.
            Program.Near(leftDashed.Area, 0.2f * 67f * 0.6f, 1e-3f, "60/30 over 60 m: 40.2 m painted, 20 wide");
            Program.Check(MarkingLines.EdgeLine(centre, true, false, 0.15f, twoWaySingleLane: true).Colour == MarkingColour.White, "two-way single lane: the left one is white too");
            Bounds(MarkingLines.EdgeLine(centre, false, false, 0.11f).Mesh, out float wx0, out float wx1, out _, out _);
            Program.Near(wx1 - wx0, 0.10f, 1e-4f, "10 cm when nearest");

            // 线8 junction guide: 2-2 dashed, 15 (8 for bicycles), the colour given.
            MarkingShape jg = MarkingLines.JunctionGuideLine(centre, MarkingColour.Yellow);
            Program.Check(jg.Colour == MarkingColour.Yellow, "the colour given");
            Program.Near(jg.Area, 0.15f * 30f, 1e-3f, "2-2: half");
            Bounds(MarkingLines.JunctionGuideLine(centre, MarkingColour.White, forBicycles: true).Mesh, out float jx0, out float jx1, out _, out _);
            Program.Near(jx1 - jx0, 0.08f, 1e-4f, "8 cm for bicycles");

            // 线9 lane change: 1-1 white, the width given.
            MarkingShape lc = MarkingLines.LaneChangeLine(centre, 0.1f);
            Program.Check(lc.Colour == MarkingColour.White, "white");
            Program.Near(lc.Area, 0.1f * 30f, 1e-3f, "1-1: half");

            // 线59/60 reserved lane: 5-1 white, 20 or 25.
            MarkingShape bus = MarkingLines.ReservedLaneLine(centre);
            Program.Near(bus.Area, 0.2f * 50f, 1e-3f, "5-1 over 60 m: ten dashes of 5 m, 20 wide");
            Bounds(MarkingLines.ReservedLaneLine(centre, 0.25f).Mesh, out float bx0, out float bx1, out _, out _);
            Program.Near(bx1 - bx0, 0.25f, 1e-4f, "25 when asked");

            // 线45, 线46, 线61.
            Program.Check(MarkingLines.SingleYellow(centre).Colour == MarkingColour.Yellow, "single yellow");
            Bounds(MarkingLines.SingleYellow(centre, 0.5f).Mesh, out float sx0, out float sx1, out _, out _);
            Program.Near(sx1 - sx0, 0.3f, 1e-4f, "single yellow is 30 at most");
            Bounds(MarkingLines.WhiteSolid(centre, 0.12f).Mesh, out float ws0, out float ws1, out _, out _);
            Program.Near(ws1 - ws0, 0.10f, 1e-4f, "white solid: 10 or 15");
            Program.Near(MarkingLines.GuideLaneLine(centre).Area, 0.15f * 60f, 1e-3f, "guide lane line: solid white");
            Program.Check(MarkingLines.GuideLaneMinLength == 30f, "30 m at least (6.10.3)");
        }

        private static void Figures()
        {
            // 线12 chevrons: 3 m across, apex 60 degrees, 40 wide, two 5 m apart, groups 50 m apart.
            MarkingShape ch = MarkingLines.DistanceChevrons(100f, 2);
            Sound(ch.Mesh, "chevrons");
            Bounds(ch.Mesh, out float cx0, out float cx1, out float cz0, out float cz1);
            Program.Near(cx1 - cx0, 3f + 0.4f / (float)Math.Cos(Math.PI / 3) * 0f + 0.4f, 0.2f, "about 3 m across (plus the arm ends): " + (cx1 - cx0));
            float back = 1.5f / (float)Math.Tan(Math.PI / 6);
            // From the arms' ends of the first chevron (z 0) to the apex of the last (back + 50 + 5, plus the mitre of the ribbon at the apex).
            Program.Near(cz1 - cz0, 50f + 5f + back, 0.5f, "from the first group's arms to the second group's last apex: " + (cz1 - cz0));
            Program.Check(ch.Mesh.TriangleCount == 4 * 4, "four chevrons of two ribbon segments (two quads each): " + ch.Mesh.TriangleCount);
            Program.Check(MarkingLines.DistanceChevrons(40f, 2).Mesh.TriangleCount == 2 * 4, "40 m holds one group only");
            Program.Check(MarkingLines.DistanceChevrons(0f).Mesh.TriangleCount == 0, "no length, no chevrons");
            // One chevron: the apex at (0, z); the arms reach x = +-1.5 at z - 2.598.
            var one = new MeshBuilder();
            MarkingLines.Chevron(one, 0f, 10f);
            Bounds(one, out float ox0, out float ox1, out float oz0, out float oz1);
            Program.Near(oz1, 10f + 0.2f / (float)Math.Sin(Math.PI / 6) * 0f + 0.4f, 0.4f, "the apex is near z 10 (the ribbon's mitre reaches a little beyond): " + oz1);
            Program.Near(oz0, 10f - back - 0.2f * (float)Math.Cos(Math.PI / 3), 0.25f, "the arms reach 2.6 m back: " + oz0);

            // 线13 half discs: radius 30 cm, outside the edge line, every 50 m: 100 m of line gives three (0, 50, 100).
            var edge = new List<Vec3> { P(0f, 0f), P(0f, 100f) };
            MarkingShape hd = MarkingLines.HalfDiscs(edge, 0.15f, rightSide: true);
            Sound(hd.Mesh, "half discs");
            Program.Near(hd.Area, 3f * (float)(Math.PI * 0.09 * 0.5), 0.02f, "three half discs of radius 30: " + hd.Area);
            // Every 50 m: 149 m of line has three (0, 50, 100), 150 m four; the second disc is centred on z 50.
            Program.Check(MarkingLines.HalfDiscs(new List<Vec3> { P(0f, 0f), P(0f, 149f) }, 0.15f).Mesh.TriangleCount == 3 * 11 && MarkingLines.HalfDiscs(new List<Vec3> { P(0f, 0f), P(0f, 150f) }, 0.15f).Mesh.TriangleCount == 4 * 11, "one disc every 50 m (a disc of 13 points is 11 triangles)");
            bool secondAt50 = false;
            foreach (Vec3 p in hd.Mesh.Positions)
                secondAt50 |= Math.Abs(p.Z - 50f) < 1e-3f && Math.Abs(p.X - (0.115f + 0.3f)) < 1e-3f;
            Program.Check(secondAt50, "the second disc's outermost point is at z 50");
            Bounds(hd.Mesh, out float dx0, out float dx1, out _, out _);
            Program.Near(dx0, 0.075f + 0.04f, 1e-3f, "the flat side 4 cm outside the 15 cm line");
            Program.Near(dx1, 0.075f + 0.04f + 0.3f, 1e-3f, "bulging 30 cm out");
            Bounds(MarkingLines.HalfDiscs(edge, 0.15f, rightSide: false).Mesh, out float ex0, out float ex1, out _, out _);
            Program.Near(ex1, -0.115f, 1e-3f, "on the left when asked");
            Program.Check(MarkingLines.HalfDiscs(null, 0.15f).Mesh.TriangleCount == 0, "no line, no discs");

            // 线41 zigzag: 20 wide, interior apex 60 degrees, the height given.
            MarkingShape zz = MarkingLines.Zigzag(20f, 1f);
            Sound(zz.Mesh, "zigzag");
            Bounds(zz.Mesh, out float zx0, out float zx1, out float zz0, out float zz1);
            Program.Near(zx1 - zx0, 1f + 0.2f / (float)Math.Cos(Math.PI / 3), 0.25f, "swings 1 m across (plus the mitre): " + (zx1 - zx0));
            float leg = (float)Math.Tan(Math.PI / 6);
            Program.Check(zz1 >= 20f - leg - 0.3f && zz1 <= 20f + 0.3f, "runs about the length: " + zz1);
            Program.Check(zz.Colour == MarkingColour.White && MarkingLines.Zigzag(20f, 1f, 0.2f, MarkingColour.Yellow).Colour == MarkingColour.Yellow, "white by default, or the colour given");
            Program.Check(MarkingLines.Zigzag(0f, 1f).Mesh.TriangleCount == 0 && MarkingLines.Zigzag(10f, 0f).Mesh.TriangleCount == 0, "no length or height: nothing");

            // 线54 give-way dashed line: two 60/20 dashed 20 cm lines 20 cm apart across the span, a triangle (1.2 x 3) on each lane pointing at the driver (-Z), its base 1 m behind the near line.
            List<MarkingShape> gw = MarkingLines.GiveWayDashed(7f, new[] { -1.75f, 1.75f });
            Program.Check(gw.Count == 3, "two lines and the triangles");
            Bounds(gw[0].Mesh, out float g0x0, out float g0x1, out float g0z0, out float g0z1);
            Bounds(gw[1].Mesh, out _, out _, out float g1z0, out float g1z1);
            Program.Near(g0x1 - g0x0, 7f, 0.01f, "across the span (the dashes end at the ends)");
            Program.Near(Math.Abs((g0z0 + g0z1) * 0.5f - (g1z0 + g1z1) * 0.5f), 0.4f, 1e-4f, "the two lines are 20 cm apart (40 centre to centre)");
            Program.Near(gw[0].Area, 0.2f * 7f * 0.75f, 0.2f * 0.6f + 1e-3f, "60/20: three quarters painted: " + gw[0].Area);
            Program.Check(gw[2].Mesh.TriangleCount == 2 * 6, "two hollow triangles of three band quads each: " + gw[2].Mesh.TriangleCount);
            // A hollow triangle 1.2 x 3 with 15-wide strokes: the outer area 1.8, the inner a similar triangle whose inradius is 0.15 less (inradius = area / half perimeter).
            float half = (1.2f + 2f * (float)Math.Sqrt(0.36 + 9.0)) * 0.5f, inradius = 1.8f / half, inner = 1.8f * (inradius - 0.15f) * (inradius - 0.15f) / (inradius * inradius);
            // The mitre of the outline at the sharp apex is capped (five widths), so the band falls a little short of the exact figure.
            Program.Near(gw[2].Area, 2f * (1.8f - inner), 0.03f, "each triangle is a 15 cm band round 1.2 x 3: " + gw[2].Area + " / " + 2f * (1.8f - inner));
            Bounds(gw[2].Mesh, out _, out _, out float tz0, out float tz1);
            Program.Near(tz1, -0.3f - 2f, 1e-4f, "the base 2 m behind the near line's near edge (Fig. 77: 200 to 250)");
            Program.Near(tz0, -0.3f - 2f - 3f, 1e-4f, "the point 3 m further back, toward the driver");
            Program.Check(MarkingLines.GiveWayDashed(7f).Count == 2, "no lanes given: the lines only");
            Program.Check(MarkingLines.GiveWayDashed(0f).Count == 0, "no span: nothing");
        }
    }
}
