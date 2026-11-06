using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The ear-clipping filler (Polygon) and the eleven guide arrows of GB 5768.3 Figs. 42 to 45 (RoadArrows).</summary>
    internal static class PolygonArrowTests
    {
        public static void RunAll()
        {
            Program.Run("polygon: squares, an L, a star, either order, repeated points", Simple);
            Program.Run("polygon: holes, degenerate input, the arc helper", HolesAndEdges);
            Program.Run("arrows: every kind at every size is sound, the right length, centred, and keeps the outline's area", Arrows);
            Program.Run("arrows: the printed dimensions of Fig. 43", Dimensions);
            Program.Run("arrows: mirrors and scales", MirrorsAndScales);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static float MeshArea(MeshBuilder mesh)
        {
            double sum = 0.0;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                sum += MeshBuilder.Cross(b - a, c - a).Length * 0.5;
            }

            return (float)sum;
        }

        /// <summary>Every triangle faces up and has an area; the indices are in range; the positions are finite.</summary>
        private static void Sound(MeshBuilder mesh, string what)
        {
            Program.Check(mesh.Indices.Count % 3 == 0 && mesh.Indices.Count > 0, what + ": triangles");
            for (int i = 0; i < mesh.Indices.Count; i++)
                Program.Check(mesh.Indices[i] >= 0 && mesh.Indices[i] < mesh.VertexCount, what + ": index in range");
            foreach (Vec3 p in mesh.Positions)
                Program.Check(p.IsFinite, what + ": finite");
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                Program.Check(n.Y > 0f, what + ": triangle " + i / 3 + " faces up");
            }
        }

        private static void Simple()
        {
            var square = new List<Vec3> { P(0, 0), P(1, 0), P(1, 1), P(0, 1) };
            var mesh = new MeshBuilder();
            int made = Polygon.Fill(mesh, square);
            Sound(mesh, "square");
            Program.Check(made == 2 && mesh.TriangleCount == 2, "a square is two triangles: " + made);
            Program.Near(MeshArea(mesh), 1f, 1e-5f, "of area 1");
            Program.Near(Polygon.SignedArea2(square), 2f, 1e-6f, "counter-clockwise: positive twice the area");

            // The same square clockwise gives the same triangles facing up.
            var clockwise = new List<Vec3>(square);
            clockwise.Reverse();
            var mesh2 = new MeshBuilder();
            Polygon.Fill(mesh2, clockwise);
            Sound(mesh2, "clockwise square");
            Program.Near(MeshArea(mesh2), 1f, 1e-5f, "clockwise: the same area, still facing up");
            Program.Check(Polygon.SignedArea2(clockwise) < 0f, "clockwise: negative signed area");

            // An L (concave, 6 corners): 4 triangles, area 3.
            var ell = new List<Vec3> { P(0, 0), P(2, 0), P(2, 1), P(1, 1), P(1, 2), P(0, 2) };
            var mesh3 = new MeshBuilder();
            int madeL = Polygon.Fill(mesh3, ell);
            Sound(mesh3, "L");
            Program.Check(madeL == 4, "an L of six corners is four triangles: " + madeL);
            Program.Near(MeshArea(mesh3), 3f, 1e-5f, "the L has area 3");

            // A five-pointed star (10 corners, deeply concave): n - 2 triangles and the shoelace area, whatever the order.
            var star = new List<Vec3>();
            for (int i = 0; i < 10; i++)
            {
                double a = Math.PI / 2 + i * Math.PI / 5;
                float r = i % 2 == 0 ? 1f : 0.4f;
                star.Add(P((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
            }

            foreach (bool reverse in new[] { false, true })
            {
                var pts = new List<Vec3>(star);
                if (reverse)
                    pts.Reverse();
                var m = new MeshBuilder();
                int n = Polygon.Fill(m, pts);
                Sound(m, "star" + (reverse ? " reversed" : ""));
                Program.Check(n == 8, "a star of ten corners is eight triangles: " + n);
                Program.Near(MeshArea(m), Math.Abs(Polygon.SignedArea2(pts)) * 0.5f, 1e-5f, "the star keeps the shoelace area");
                // No triangle reaches outside the star: every centroid is inside (tested by the winding of the outline about it).
                for (int i = 0; i + 2 < m.Indices.Count; i += 3)
                {
                    Vec3 c = (m.Positions[m.Indices[i]] + m.Positions[m.Indices[i + 1]] + m.Positions[m.Indices[i + 2]]) * (1f / 3f);
                    Program.Check(InsidePolygon(c, star), "a triangle centroid of the star is inside the star");
                }
            }

            // Repeated points, a closing copy of the first point, and a point that is not a number: cleaned out.
            var messy = new List<Vec3> { P(0, 0), P(0, 0), P(1, 0), P(1, 1), P(1, 1), P(float.NaN, 3), P(0, 1), P(0, 0) };
            var mesh4 = new MeshBuilder();
            Program.Check(Polygon.Fill(mesh4, messy) == 2, "repeated and closing points are cleaned out: two triangles");
            Program.Near(MeshArea(mesh4), 1f, 1e-5f, "and the area is the square's");

            // Y is kept.
            var raised = new List<Vec3> { new Vec3(0f, 2.5f, 0f), new Vec3(1f, 2.5f, 0f), new Vec3(1f, 2.5f, 1f) };
            var mesh5 = new MeshBuilder();
            Polygon.Fill(mesh5, raised);
            Program.Check(mesh5.Positions.TrueForAll(p => p.Y == 2.5f), "the height of the outline is kept");
        }

        private static bool InsidePolygon(Vec3 p, List<Vec3> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X)
                    inside = !inside;
            }

            return inside;
        }

        private static void HolesAndEdges()
        {
            // A 4 x 4 square with a 2 x 2 hole: area 12, every triangle outside the hole.
            var outer = new List<Vec3> { P(0, 0), P(4, 0), P(4, 4), P(0, 4) };
            var hole = new List<Vec3> { P(1, 1), P(3, 1), P(3, 3), P(1, 3) };
            var mesh = new MeshBuilder();
            int made = Polygon.Fill(mesh, outer, new List<IList<Vec3>> { hole });
            Sound(mesh, "square with a hole");
            Program.Near(MeshArea(mesh), 12f, 1e-4f, "area is the square less the hole: " + MeshArea(mesh));
            Program.Check(made == 8, "a square with a square hole is eight triangles: " + made);
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 c = (mesh.Positions[mesh.Indices[i]] + mesh.Positions[mesh.Indices[i + 1]] + mesh.Positions[mesh.Indices[i + 2]]) * (1f / 3f);
                Program.Check(!(c.X > 1f && c.X < 3f && c.Z > 1f && c.Z < 3f), "no triangle centroid is in the hole");
            }

            // The hole given counter-clockwise works the same; two holes work.
            var mesh2 = new MeshBuilder();
            var holeCcw = new List<Vec3>(hole);
            Polygon.Fill(mesh2, outer, new List<IList<Vec3>> { holeCcw });
            Program.Near(MeshArea(mesh2), 12f, 1e-4f, "a hole in either order");
            var two = new List<IList<Vec3>> { new List<Vec3> { P(0.5f, 0.5f), P(1.5f, 0.5f), P(1.5f, 1.5f), P(0.5f, 1.5f) }, new List<Vec3> { P(2.5f, 2.5f), P(3.5f, 2.5f), P(3.5f, 3.5f), P(2.5f, 3.5f) } };
            var mesh3 = new MeshBuilder();
            Polygon.Fill(mesh3, outer, two);
            Sound(mesh3, "two holes");
            Program.Near(MeshArea(mesh3), 14f, 1e-4f, "two holes of 1 each: 14");

            // A glyph-like ring: a circle with a circular hole (an O), 32 and 16 points.
            var ringOuter = new List<Vec3>();
            Polygon.Arc(ringOuter, 0f, 0f, 1f, 0f, (float)(2 * Math.PI) * 31f / 32f, 31);
            var ringHole = new List<Vec3>();
            Polygon.Arc(ringHole, 0f, 0f, 0.5f, 0f, (float)(2 * Math.PI) * 15f / 16f, 15);
            var mesh4 = new MeshBuilder();
            Polygon.Fill(mesh4, ringOuter, new List<IList<Vec3>> { ringHole });
            Sound(mesh4, "O");
            float expected = (float)(0.5 * 32 * Math.Sin(2 * Math.PI / 32) * 1 * 1 - 0.5 * 16 * Math.Sin(2 * Math.PI / 16) * 0.25);
            Program.Near(MeshArea(mesh4), expected, 1e-3f, "an O keeps its area: " + MeshArea(mesh4) + " / " + expected);

            // Degenerate input.
            Program.Check(Polygon.Fill(new MeshBuilder(), null) == 0 && Polygon.Fill(new MeshBuilder(), new List<Vec3>()) == 0 && Polygon.Fill(new MeshBuilder(), new List<Vec3> { P(0, 0), P(1, 1) }) == 0, "nothing, or fewer than three points: no triangle");
            var line = new List<Vec3> { P(0, 0), P(1, 0), P(2, 0), P(3, 0) };
            var meshLine = new MeshBuilder();
            Program.Check(Polygon.Fill(meshLine, line) == 0 || MeshArea(meshLine) < 1e-6f, "collinear points make no area and do not hang");
            var tiny = new List<Vec3> { P(0, 0), P(1, 0), P(1, 1), P(0, 1), P(0, 0.5f), P(0, 0.25f) };
            var meshTiny = new MeshBuilder();
            Polygon.Fill(meshTiny, tiny);
            Program.Near(MeshArea(meshTiny), 1f, 1e-5f, "collinear points on an edge are harmless");

            // The arc helper: the ends are at the angles given, the count is segments + 1.
            var arc = new List<Vec3>();
            Polygon.Arc(arc, 1f, 2f, 3f, 0f, (float)Math.PI / 2, 4);
            Program.Check(arc.Count == 5, "an arc of four segments has five points");
            Program.Near(arc[0].X, 4f, 1e-5f, "starts at the start angle");
            Program.Near(arc[4].Z, 5f, 1e-5f, "ends at the end angle");
            Program.Near(arc[2].X, 1f + 3f * (float)Math.Cos(Math.PI / 4), 1e-5f, "the middle point is on the circle");
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

        private static void Arrows()
        {
            foreach (ArrowKind kind in Enum.GetValues(typeof(ArrowKind)))
            {
                List<Vec3> outline = RoadArrows.Outline(kind);
                float outlineArea = Math.Abs(Polygon.SignedArea2(outline)) * 0.5f;
                Program.Check(outline.Count >= 7 && outlineArea > 1000f, kind + ": an outline with an area (" + outline.Count + " points, " + outlineArea + " cm2)");
                // The outline does not cross itself.
                bool crosses = false;
                for (int i = 0; i < outline.Count && !crosses; i++)
                {
                    for (int k = i + 2; k < outline.Count && !crosses; k++)
                    {
                        if (i == 0 && k == outline.Count - 1)
                            continue;
                        crosses = Polygon.Segments(outline[i], outline[(i + 1) % outline.Count], outline[k], outline[(k + 1) % outline.Count]);
                    }
                }

                Program.Check(!crosses, kind + ": the outline does not cross itself");

                foreach (ArrowSize size in Enum.GetValues(typeof(ArrowSize)))
                {
                    MarkingShape shape = RoadArrows.Shape(kind, size);
                    string what = kind + "/" + size;
                    Sound(shape.Mesh, what);
                    Program.Check(shape.Colour == MarkingColour.White, what + ": white (5.13.5)");
                    Bounds(shape.Mesh, out float x0, out float x1, out float z0, out float z1);
                    Program.Near(z0, 0f, 1e-4f, what + ": starts at Z 0");
                    Program.Near(z1, RoadArrows.Length(size), 1e-3f, what + ": as long as the size says (" + z1 + ")");
                    Program.Near(x0 + x1, 0f, 1e-4f, what + ": the box is centred on X 0");
                    float s = RoadArrows.ScaleOf(size) * 0.01f;
                    Program.Near(shape.Area, outlineArea * s * s, outlineArea * s * s * 1e-4f, what + ": the mesh has the area of the outline (" + shape.Area + " m2)");
                }
            }
        }

        private static void Dimensions()
        {
            // Fig. 43: the straight arrow is 6 m long, its shaft 30 cm wide and 3.6 m long, its head 90 cm wide and 2.4 m long: 0.3 x 3.6 + 0.9 x 2.4 / 2 = 2.16 m2.
            MarkingShape straight = RoadArrows.Shape(ArrowKind.Straight, ArrowSize.Line31);
            Bounds(straight.Mesh, out float sx0, out float sx1, out _, out float sz1);
            Program.Near(sx1 - sx0, 0.9f, 1e-4f, "straight: 90 cm wide");
            Program.Near(sz1, 6f, 1e-4f, "straight: 6 m long");
            Program.Near(straight.Area, 2.16f, 1e-4f, "straight: 2.16 m2");

            // The other sizes of the straight arrow: Fig. 42 is 3 m with a 15 cm shaft, Fig. 44 9 m with 45, Fig. 45 4.5 m with 22.5 (printed 23).
            Program.Near(RoadArrows.Shape(ArrowKind.Straight, ArrowSize.Line30).Area, 2.16f * 0.25f, 1e-4f, "Fig. 42 is the half size");
            Program.Near(RoadArrows.Shape(ArrowKind.Straight, ArrowSize.Line32).Area, 2.16f * 2.25f, 1e-4f, "Fig. 44 is one and a half");
            Program.Near(RoadArrows.Shape(ArrowKind.Straight, ArrowSize.Line33).Area, 2.16f * 0.5625f, 1e-4f, "Fig. 45 is three quarters");
            Program.Check(RoadArrows.SizeFor(40f) == ArrowSize.Line30 && RoadArrows.SizeFor(40.1f) == ArrowSize.Line31 && RoadArrows.SizeFor(99.9f) == ArrowSize.Line31 && RoadArrows.SizeFor(100f) == ArrowSize.Line32, "5.13.4: the size by the design speed");
            Program.Near(RoadArrows.Length(ArrowSize.Line30), 3f, 1e-6f, "3 m");
            Program.Near(RoadArrows.Length(ArrowSize.Line33), 4.5f, 1e-6f, "4.5 m");

            // The left-turn arrow (cm of Fig. 43): the box is 150 wide (the head reaches 40 + 80 left of the shaft's left edge, 30 shaft) and 600 long; the area by parts.
            List<Vec3> left = RoadArrows.Outline(ArrowKind.Left);
            float lx0 = float.MaxValue, lx1 = float.MinValue, lz1 = float.MinValue;
            foreach (Vec3 p in left)
            {
                lx0 = Math.Min(lx0, p.X);
                lx1 = Math.Max(lx1, p.X);
                lz1 = Math.Max(lz1, p.Z);
            }

            Program.Check(lx0 == -150f && lx1 == 0f && lz1 == 600f, "left: from 150 left of the shaft's right edge to it, 600 long");
            Program.Check(left.Contains(new Vec3(0f, 0f, 390f)) && left.Contains(new Vec3(-30f, 0f, 300f)) && left.Contains(new Vec3(-110f, 0f, 500f)) && left.Contains(new Vec3(-110f, 0f, 380f)), "left: the stem's edges leave the head 100 and 220 below its top and meet the shaft 390 up (right edge) and 300 up (left edge, 270 on the right edge extended)");
            // The stem's edges are at 45 degrees (the figure's angle).
            Program.Near(Math.Abs(500f - 390f), Math.Abs(-110f - 0f), 1e-6f, "the upper edge of the stem is at 45 degrees");
            Program.Near(Math.Abs(380f - 300f), Math.Abs(-110f + 30f), 1e-6f, "the lower edge of the stem is at 45 degrees");
            Program.Check(left.Contains(new Vec3(-150f, 0f, 440f)) && left.Contains(new Vec3(-110f, 0f, 290f)), "left: the tip 160 below the top, the bottom corner 310 below");

            // Straight-or-left: the left head stands 340 up, its stem meets the shaft's left edge 160 and 40 up; the straight head is the straight arrow's on a shaft at x -30..0.
            List<Vec3> sl = RoadArrows.Outline(ArrowKind.StraightLeft);
            Program.Check(sl.Contains(new Vec3(-110f, 0f, 340f)) && sl.Contains(new Vec3(-30f, 0f, 160f)) && sl.Contains(new Vec3(-30f, 0f, 40f)) && sl.Contains(new Vec3(-15f, 0f, 600f)) && sl.Contains(new Vec3(30f, 0f, 360f)) && sl.Contains(new Vec3(-60f, 0f, 360f)), "straight-or-left: the printed points");

            // U-turn: box 220 wide (the head reaches 40 left of the outer edge, the shaft's outer edge at 180), the arcs about (90, 510) with radii 90 and 50, the head 120 wide and 240 long.
            List<Vec3> u = RoadArrows.Outline(ArrowKind.UTurn);
            float ux0 = float.MaxValue, ux1 = float.MinValue, uzTop = float.MinValue;
            foreach (Vec3 p in u)
            {
                ux0 = Math.Min(ux0, p.X);
                ux1 = Math.Max(ux1, p.X);
                uzTop = Math.Max(uzTop, p.Z);
            }

            Program.Check(ux0 == -40f && ux1 == 180f && Math.Abs(uzTop - 600f) < 1e-3f, "U-turn: 220 wide, 600 high: " + ux0 + ".." + ux1 + ", " + uzTop);
            Program.Check(u.Contains(new Vec3(-40f, 0f, 400f)) && u.Contains(new Vec3(80f, 0f, 400f)) && u.Contains(new Vec3(20f, 0f, 160f)), "U-turn: the head's base 200 below the top, 120 wide, its tip 240 lower");
            float uArea = Math.Abs(Polygon.SignedArea2(u)) * 0.5f;
            // By parts: shaft 40 x 510, half annulus (90^2 - 50^2) pi / 2, leg 40 x 110, head 120 x 240 / 2; the arcs are 24-segment polygons, so within 0.5 %.
            float uExpected = 40f * 510f + (float)(Math.PI * 0.5 * (90.0 * 90.0 - 50.0 * 50.0)) + 40f * 110f + 120f * 240f * 0.5f;
            Program.Near(uArea, uExpected, uExpected * 0.005f, "U-turn: area by parts " + uArea + " / " + uExpected);

            // The hook of straight-or-U-turn: the box reaches 150 left of the shaft's right edge (120 left of its left edge, printed), the head's tip is at the foot, the hook's top is 295 up (printed).
            List<Vec3> su = RoadArrows.Outline(ArrowKind.StraightUTurn);
            float hx0 = float.MaxValue, hookTop = float.MinValue;
            foreach (Vec3 p in su)
            {
                hx0 = Math.Min(hx0, p.X);
                // The hook's arc (not the straight head's corner at (-60, 360)).
                if (p.X < -40f && p.X > -80f && p.Z < 340f)
                    hookTop = Math.Max(hookTop, p.Z);
            }

            Program.Check(hx0 == -150f, "the hook reaches 120 left of the shaft's left edge");
            Program.Near(hookTop, 295f, 0.5f, "the hook's top is 295 up: " + hookTop);
            Program.Check(su.Contains(new Vec3(-105f, 0f, 0f)) && su.Contains(new Vec3(-150f, 0f, 180f)) && su.Contains(new Vec3(-60f, 0f, 180f)), "the hook's head: 90 wide at 180 up, the tip at the foot");

            // Left-right: symmetric about the axis, the heads' tips 135 out, the notch 405 up.
            List<Vec3> y = RoadArrows.Outline(ArrowKind.LeftRight);
            float yx0 = float.MaxValue, yx1 = float.MinValue;
            foreach (Vec3 p in y)
            {
                yx0 = Math.Min(yx0, p.X);
                yx1 = Math.Max(yx1, p.X);
            }

            Program.Check(yx0 == -135f && yx1 == 135f && y.Contains(new Vec3(0f, 0f, 405f)) && y.Contains(new Vec3(-15f, 0f, 300f)) && y.Contains(new Vec3(15f, 0f, 300f)), "Y: 270 wide, the notch 405 up, the stems leave the shaft 300 up");

            // Merge left: the tip 120 left of the shaft's left edge at the top; the head's base corners 240 along the axis from the tip, 90 apart; the shaft straight at the foot.
            List<Vec3> m = RoadArrows.Outline(ArrowKind.MergeLeft);
            Program.Check(m.Contains(new Vec3(-120f, 0f, 600f)) && m[0].Equals(new Vec3(0f, 0f, 0f)) && m[m.Count - 1].Equals(new Vec3(30f, 0f, 0f)), "merge: the tip and the foot of the shaft");
            Vec3 tip = new Vec3(-120f, 0f, 600f);
            int tipAt = m.IndexOf(tip);
            Vec3 baseLeft = m[tipAt - 1], baseRight = m[tipAt + 1];
            Program.Near(Vec3.DistanceXZ(tip, baseLeft) , (float)Math.Sqrt(240.0 * 240.0 + 45.0 * 45.0), 1e-2f, "merge: the head's corner is 240 along and 45 across from the tip");
            Program.Near(Vec3.DistanceXZ(baseLeft, baseRight), 90f, 1e-2f, "merge: the head's base is 90 wide");
            // The axis from the middle of the base to the tip leans to the left (-X) by the tilt.
            double tilt = Math.Atan2((baseLeft.X + baseRight.X) * 0.5 - tip.X, tip.Z - (baseLeft.Z + baseRight.Z) * 0.5) * 180.0 / Math.PI;
            Program.Near((float)tilt, RoadArrows.MergeTiltDegrees, 1e-3f, "merge: the head is tilted 22.5 degrees to the left: " + tilt);
            // The shaft is 30 wide everywhere along the bend: the right-edge points are 30 from the left-edge points.
            Program.Check(m[1].Equals(new Vec3(0f, 0f, m[1].Z)) && m[1].Z > 100f && m[1].Z < 200f, "merge: the shaft is straight up to about 160: " + m[1].Z);
        }

        private static void MirrorsAndScales()
        {
            foreach ((ArrowKind a, ArrowKind b) in new[] { (ArrowKind.Left, ArrowKind.Right), (ArrowKind.StraightLeft, ArrowKind.StraightRight), (ArrowKind.MergeLeft, ArrowKind.MergeRight) })
            {
                MarkingShape left = RoadArrows.Shape(a, ArrowSize.Line31), right = RoadArrows.Shape(b, ArrowSize.Line31);
                Program.Near(left.Area, right.Area, 1e-5f, a + " and " + b + " have the same area");
                Bounds(left.Mesh, out float lx0, out float lx1, out _, out _);
                Bounds(right.Mesh, out float rx0, out float rx1, out _, out _);
                Program.Near(lx0, -rx1, 1e-5f, a + " / " + b + ": mirrored boxes");
                Program.Near(lx1, -rx0, 1e-5f, a + " / " + b + ": mirrored boxes (other side)");
                // The outline of the right one is the left one's reflected: every point has its mirror.
                List<Vec3> lo = RoadArrows.Outline(a), ro = RoadArrows.Outline(b);
                Program.Check(lo.Count == ro.Count, "same number of points");
                foreach (Vec3 p in lo)
                    Program.Check(ro.Contains(new Vec3(-p.X, 0f, p.Z)), a + ": every point has its mirror in " + b);
            }

            MarkingShape y = RoadArrows.Shape(ArrowKind.LeftRight, ArrowSize.Line31);
            Bounds(y.Mesh, out float yx0, out float yx1, out _, out _);
            Program.Near(yx0, -yx1, 1e-5f, "the Y is symmetric");

            // The sizes: the area scales with the square of the scale for every kind.
            foreach (ArrowKind kind in Enum.GetValues(typeof(ArrowKind)))
            {
                float a1 = RoadArrows.Shape(kind, ArrowSize.Line31).Area;
                Program.Near(RoadArrows.Shape(kind, ArrowSize.Line30).Area, a1 * 0.25f, a1 * 1e-4f, kind + ": Fig. 42 a quarter of the area");
                Program.Near(RoadArrows.Shape(kind, ArrowSize.Line32).Area, a1 * 2.25f, a1 * 1e-4f, kind + ": Fig. 44 2.25 times");
                Program.Near(RoadArrows.Shape(kind, ArrowSize.Line33).Area, a1 * 0.5625f, a1 * 1e-4f, kind + ": Fig. 45 nine sixteenths");
            }
        }
    }
}
