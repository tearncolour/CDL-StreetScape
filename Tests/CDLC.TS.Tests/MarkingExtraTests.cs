using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The kerb lines (线48, 线50), the elevation marking (线72), the width transitions (7.1), the raised markers and delineators (8.1, 8.2) and the entry side of 线65: the parts of GB 5768.3 that stand up,
    /// lie in a layout rule, or are small things on a line.
    /// </summary>
    internal static class MarkingExtraTests
    {
        public static void RunAll()
        {
            Program.Run("kerb lines: the face, the top, the side, the dashes", Kerb);
            Program.Run("elevation marking: an upright sheet of yellow with black stripes at 45 degrees", Elevation);
            Program.Run("width transition: formula (1), Table 4, the extension and the S", Transition);
            Program.Run("delineators and markers: Table 7, the spacing, the places, the bodies", Studs);
            Program.Run("entry gore: the exit figure the other way over the road", Entry);
            Program.Run("extras: the templates of the catalogue", Templates);
        }

        private static Vec3 P(float x, float y, float z) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length, float y = 0f)
        {
            var line = new List<Vec3>();
            for (float z = 0f; z < length; z += 2f)
                line.Add(P(0f, y, z));
            line.Add(P(0f, y, length));
            return line;
        }

        private static void Extents(MeshBuilder mesh, out float x0, out float x1, out float y0, out float y1, out float z0, out float z1)
        {
            x0 = y0 = z0 = float.MaxValue;
            x1 = y1 = z1 = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                y0 = Math.Min(y0, p.Y);
                y1 = Math.Max(y1, p.Y);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }
        }

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

        // ---------------------------------------------------------------- kerb lines

        private static void Kerb()
        {
            // 10 m straight, the kerb on the right of the way (+X), 15 cm high, 15 wide: the face stands just in front of x = 0 looking left (-X, the road), the top is a ribbon 0.15 wide from it just over the kerb.
            MarkingShape right = MarkingKerb.Line(Straight(10f), true, 0.15f, 0.15f, false);
            Program.Check(right.Colour == MarkingColour.Yellow, "yellow");
            int walls = 0, tops = 0, wrong = 0;
            for (int i = 0; i + 2 < right.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = right.Mesh.Positions[right.Mesh.Indices[i]], b = right.Mesh.Positions[right.Mesh.Indices[i + 1]], c = right.Mesh.Positions[right.Mesh.Indices[i + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (Math.Abs(n.Y) < 1e-9f)
                {
                    walls++;
                    wrong += n.X < 0f ? 0 : 1;
                }
                else
                {
                    tops++;
                    wrong += n.Y > 0f && Math.Abs(a.Y - 0.15f - MarkingKerb.Above) < 1e-5f ? 0 : 1;
                }
            }

            Program.Check(walls > 0 && tops > 0 && wrong == 0, "walls face the road (-X) and the top faces up at the kerb's height: " + walls + " walls, " + tops + " tops, " + wrong + " wrong");
            Extents(right.Mesh, out float x0, out float x1, out float y0, out float y1, out _, out float z1);
            Program.Near(x0, -MarkingKerb.Ahead, 1e-5f, "the face is just in front of the path");
            Program.Near(x1, 0.15f - MarkingKerb.Ahead, 1e-5f, "the top reaches 15 cm out from it, away from the road");
            Program.Near(y0, 0f, 1e-5f, "the foot is at the road");
            Program.Near(y1, 0.15f + MarkingKerb.Above, 1e-5f, "and the top just over the kerb's height");
            Program.Near(z1, 10f, 1e-5f, "the whole length");

            // On the left: the other way over.
            MarkingShape left = MarkingKerb.Line(Straight(10f), false, 0.15f, 0.15f, false);
            Extents(left.Mesh, out float lx0, out float lx1, out _, out _, out _, out _);
            Program.Near(lx0, -0.15f + MarkingKerb.Ahead, 1e-5f, "a kerb on the left: its top is out on the left");
            Program.Near(lx1, MarkingKerb.Ahead, 1e-5f, "and its face just in front of the path");
            bool facesRoad = true;
            for (int i = 0; i + 2 < left.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = left.Mesh.Positions[left.Mesh.Indices[i]], b = left.Mesh.Positions[left.Mesh.Indices[i + 1]], c = left.Mesh.Positions[left.Mesh.Indices[i + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (Math.Abs(n.Y) < 1e-9f)
                    facesRoad &= n.X > 0f;
            }

            Program.Check(facesRoad, "and its face looks right, at the road");

            // Heights and widths are kept to what a kerb can be.
            Extents(MarkingKerb.Line(Straight(10f), true, 2f, 0.15f, false).Mesh, out _, out _, out _, out float tall, out _, out _);
            Program.Near(tall, MarkingKerb.MaxHeight + MarkingKerb.Above, 1e-5f, "a kerb is at most " + MarkingKerb.MaxHeight + " m");
            Extents(MarkingKerb.Line(Straight(10f), true, 0f, 0.15f, false).Mesh, out _, out _, out _, out float low, out _, out _);
            Program.Near(low, MarkingKerb.MinHeight + MarkingKerb.Above, 1e-5f, "and at least " + MarkingKerb.MinHeight + " m");
            Extents(MarkingKerb.Line(Straight(10f), true, 0.15f, 9f, false).Mesh, out _, out float wide, out _, out _, out _, out _);
            Program.Near(wide, MarkingKerb.MaxWidth - MarkingKerb.Ahead, 1e-5f, "the line is at most " + MarkingKerb.MaxWidth + " m wide");

            // Dashed, 1 m on and 1 m off from the start: five dashes in 10 m (the last ends at the end).
            MarkingShape dashed = MarkingKerb.Line(Straight(10f), true, 0.15f, 0.15f, true);
            Program.Check(dashed.Mesh.TriangleCount == 5 * 4, "five dashes of a face and a top (4 triangles each): " + dashed.Mesh.TriangleCount);
            // Started a metre in the pattern: the first dash is cut to 0 m of pattern left... (phase 1 is the start of an off), so the dashes come at 1 m later.
            MarkingShape shifted = MarkingKerb.Line(Straight(10f), true, 0.15f, 0.15f, true, 1f);
            Extents(shifted.Mesh, out _, out _, out _, out _, out float sz0, out _);
            Program.Near(sz0, 1f, 1e-4f, "with the pattern a metre on, the first dash is a metre in: " + sz0);
            Extents(dashed.Mesh, out _, out _, out _, out _, out float dz0, out _);
            Program.Near(dz0, 0f, 1e-5f, "and from the start it begins at 0");

            // On a bend the face follows the line and every wall looks at the road.
            var bend = new List<Vec3>();
            for (int i = 0; i <= 12; i++)
                bend.Add(P((float)(30.0 * (1.0 - Math.Cos(i * 0.1))), 0f, (float)(30.0 * Math.Sin(i * 0.1))));
            MarkingShape curved = MarkingKerb.Line(bend, true, 0.15f, 0.15f, false);
            int towardCentre = 0, wallsOnBend = 0;
            for (int i = 0; i + 2 < curved.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = curved.Mesh.Positions[curved.Mesh.Indices[i]], b = curved.Mesh.Positions[curved.Mesh.Indices[i + 1]], c = curved.Mesh.Positions[curved.Mesh.Indices[i + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (Math.Abs(n.Y) < 1e-6f && n.LengthXZ > 1e-6f)
                {
                    wallsOnBend++;
                    // The path curves to the right (its centre is at (30, 0)); the kerb is on its right, so its face looks to the left: away from that centre.
                    Vec3 away = a - P(30f, 0f, 0f);
                    towardCentre += Vec3.Dot(n.HeadingXZ(), away.HeadingXZ()) > 0f ? 1 : 0;
                }
            }

            Program.Check(wallsOnBend >= 12 && towardCentre == wallsOnBend, "on a bend every wall faces the road, which is the outside of this one: " + towardCentre + " of " + wallsOnBend);
            Program.Check(MarkingKerb.Line(null, true, 0.15f, 0.15f, false).Mesh.VertexCount == 0 && MarkingKerb.Line(Straight(10f), true, float.NaN, 0.15f, false).Mesh.VertexCount == 0, "no line, or a height that is not a number: no paint");
        }

        // ---------------------------------------------------------------- elevation marking

        /// <summary>Whether a point of the front (x across, y up) is under a triangle of a mesh made upright.</summary>
        private static bool UnderFront(MeshBuilder mesh, float x, float y)
        {
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                float d1 = (x - b.X) * (a.Y - b.Y) - (a.X - b.X) * (y - b.Y), d2 = (x - c.X) * (b.Y - c.Y) - (b.X - c.X) * (y - c.Y), d3 = (x - a.X) * (c.Y - a.Y) - (c.X - a.X) * (y - a.Y);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos))
                    return true;
            }

            return false;
        }

        private static void Elevation()
        {
            List<MarkingShape> shapes = MarkingKerb.Elevation(1f, 2.5f, true);
            MarkingShape yellow = shapes.Find(s => s.Colour == MarkingColour.Yellow), black = shapes.Find(s => s.Colour == MarkingColour.Black);
            Program.Check(shapes.Count == 2 && yellow != null && black != null && yellow.Upright && black.Upright, "yellow and black, both upright");
            Extents(yellow.Mesh, out float x0, out float x1, out float y0, out float y1, out float z0, out float z1);
            Program.Near(x1 - x0, 1f, 1e-5f, "as wide as asked");
            Program.Near(y1 - y0, 2.5f, 1e-5f, "as high as asked: at least 2.5 m (7.7)");
            Program.Near(y0, 0f, 1e-5f, "standing on the ground");
            Program.Check(z0 == 0f && z1 == 0f, "in the plane across the way");
            Program.Near(Area(yellow.Mesh), 2.5f, 1e-4f, "the whole panel is yellow");
            // Stripes 15 cm wide with 15 between, so about half of the panel is black.
            Program.Check(Math.Abs(Area(black.Mesh) / 2.5f - 0.5f) < 0.06f, "about half of it is black: " + Area(black.Mesh) / 2.5f);
            Program.Check(black.Lift > 0f && yellow.Lift == 0f, "the black is in a layer in front of the yellow");
            bool facing = true;
            foreach (MarkingShape s in shapes)
            {
                for (int i = 0; i + 2 < s.Mesh.Indices.Count; i += 3)
                {
                    Vec3 a = s.Mesh.Positions[s.Mesh.Indices[i]], b = s.Mesh.Positions[s.Mesh.Indices[i + 1]], c = s.Mesh.Positions[s.Mesh.Indices[i + 2]];
                    facing &= MeshBuilder.Cross(b - a, c - a).Z < 0f;
                }
            }

            Program.Check(facing, "every triangle faces the oncoming traffic (-Z)");

            // The stripes are at 45 degrees and 15 cm wide (measured across them): along a horizontal line they are 15 x sqrt(2) = 21.2 cm wide, with as much between.
            foreach (bool laneOnRight in new[] { true, false })
            {
                MarkingShape b = MarkingKerb.Elevation(1f, 2.5f, laneOnRight).Find(s => s.Colour == MarkingColour.Black);
                var runs = new List<float>();
                float start = float.NaN, firstBlack = float.NaN;
                for (float x = -0.5f; x <= 0.5f; x += 0.002f)
                {
                    bool inside = UnderFront(b.Mesh, x, 1.25f);
                    if (inside && float.IsNaN(start))
                    {
                        start = x;
                        if (float.IsNaN(firstBlack) && x > -0.49f)
                            firstBlack = x;
                    }
                    else if (!inside && !float.IsNaN(start))
                    {
                        // A stripe that begins at the panel's edge is cut by it: only the whole ones are measured.
                        if (start > -0.498f)
                            runs.Add(x - start);
                        start = float.NaN;
                    }
                }

                Program.Check(runs.Count >= 1 && !float.IsNaN(firstBlack), "whole stripes cross a horizontal line: " + runs.Count);
                foreach (float run in runs)
                    Program.Near(run, 0.15f * (float)Math.Sqrt(2.0), 0.012f, "a stripe is 15 cm across: " + run);

                // Along a line at 45 degrees ("\" for a lane on the right: down as it goes right) the paint does not change: from inside a stripe, it stays in it.
                float dir = laneOnRight ? -1f : 1f, xs = firstBlack + 0.05f;
                bool same = UnderFront(b.Mesh, xs, 1.25f);
                for (float t = 0f; t <= 0.4f; t += 0.01f)
                    same &= UnderFront(b.Mesh, xs + t, 1.25f + dir * t);
                Program.Check(same, (laneOnRight ? "a lane on the right: the stripes lean \\" : "a lane on the left: the stripes lean /") + " (the edge that slopes down faces the lane)");
            }

            // Not under 2.5 m high, and not a number is the least.
            Extents(MarkingKerb.Elevation(1f, 1f, true)[0].Mesh, out _, out _, out float ly0, out float ly1, out _, out _);
            Program.Near(ly1 - ly0, 2.5f, 1e-5f, "under 2.5 m is 2.5 m");
            Extents(MarkingKerb.Elevation(float.NaN, float.NaN, true)[0].Mesh, out float nx0, out float nx1, out _, out _, out _, out _);
            Program.Check(nx1 - nx0 > 0.5f, "a size that is not a number is the default");
        }

        // ---------------------------------------------------------------- width transitions

        private static void Transition()
        {
            Program.Near(MarkingTransition.TableMinimum(20f), 20f, 1e-4f, "Table 4: 20 km/h");
            Program.Near(MarkingTransition.TableMinimum(25f), 25f, 1e-4f, "a speed between two is the higher row's (30)");
            Program.Near(MarkingTransition.TableMinimum(40f), 30f, 1e-4f, "40 km/h");
            Program.Near(MarkingTransition.TableMinimum(50f), 35f, 1e-4f, "50 km/h");
            Program.Near(MarkingTransition.TableMinimum(60f), 40f, 1e-4f, "60 km/h");
            Program.Near(MarkingTransition.TableMinimum(70f), 70f, 1e-4f, "70 km/h");
            Program.Near(MarkingTransition.TableMinimum(80f), 85f, 1e-4f, "80 km/h");
            Program.Near(MarkingTransition.TableMinimum(81f), 100f, 1e-4f, "over 80 km/h");
            Program.Near(MarkingTransition.TableMinimum(120f), 100f, 1e-4f, "120 km/h");
            Program.Near(MarkingTransition.TableMinimum(5f), 20f, 1e-4f, "under 20 km/h is the first row");
            Program.Near(MarkingTransition.Formula(60f, 3.5f), 60f * 60f * 3.5f / 155f, 1e-3f, "formula (1) up to 60 km/h: V squared W over 155");
            Program.Near(MarkingTransition.Formula(61f, 3.5f), 0.625f * 61f * 3.5f, 1e-3f, "formula (1) above 60 km/h: 0.625 V W");
            Program.Near(MarkingTransition.Formula(float.NaN, 3.5f), 0f, 1e-6f, "a speed that is not a number: nothing");
            Program.Near(MarkingTransition.Length(30f, 3.5f, true), 25f, 1e-3f, "30 km/h, 3.5 m: the formula gives 20.3 m, Table 4's 25 m is more");
            Program.Near(MarkingTransition.Length(60f, 3.5f, true), 60f * 60f * 3.5f / 155f, 1e-3f, "60 km/h, 3.5 m: the formula's 81.3 m is more than the table's 40");
            Program.Near(MarkingTransition.Length(100f, 3.5f, true), 218.75f, 1e-2f, "100 km/h, 3.5 m: 218.75 m");
            Program.Near(MarkingTransition.Length(100f, 0.1f, true), 100f, 1e-3f, "a tiny change at 100 km/h: Table 4's 100 m");
            Program.Near(MarkingTransition.Length(60f, 3.5f, false), 25f, 1e-4f, "a widening: 25 m");
            Program.Near(MarkingTransition.Length(60f, 3.5f, false, 10f), 20f, 1e-4f, "a widening is at least 20 m");
            Program.Near(MarkingTransition.Length(60f, 3.5f, false, 99f), 30f, 1e-4f, "and at most 30 m");
            Program.Near(MarkingTransition.Extension(60f, true), 40f, 1e-4f, "past the end of a narrowing: 40 m at 60 km/h");
            Program.Near(MarkingTransition.Extension(59.9f, true), 20f, 1e-4f, "20 m under 60 km/h");
            Program.Near(MarkingTransition.Extension(100f, false), 0f, 1e-4f, "and nothing past a widening");

            Program.Near(MarkingTransition.Smooth(0f), 0f, 1e-6f, "the S starts at 0");
            Program.Near(MarkingTransition.Smooth(1f), 1f, 1e-6f, "and ends at 1");
            Program.Near(MarkingTransition.Smooth(0.5f), 0.5f, 1e-6f, "through the middle");
            Program.Check(MarkingTransition.Smooth(0.1f) < 0.1f && MarkingTransition.Smooth(0.9f) > 0.9f, "slowly at the ends");

            List<Vec3> line = MarkingTransition.Centre(60f, 3.5f, true, true);
            float length = MarkingTransition.Length(60f, 3.5f, true), extension = MarkingTransition.Extension(60f, true);
            Program.Near(line[0].X, 0f, 1e-5f, "the line starts on the road's line");
            Program.Near(line[line.Count - 1].X, 3.5f, 1e-5f, "and ends W to the right");
            Program.Near(line[line.Count - 1].Z, length + extension, 1e-3f, "its length is the transition and the extension: " + line[line.Count - 1].Z);
            bool monotone = true, straightEnd = true;
            for (int i = 1; i < line.Count; i++)
            {
                monotone &= line[i].X >= line[i - 1].X - 1e-6f && line[i].Z > line[i - 1].Z;
                if (line[i].Z > length + 1e-3f)
                    straightEnd &= Math.Abs(line[i].X - 3.5f) < 1e-5f;
            }

            Program.Check(monotone && straightEnd, "it only goes ahead and across, and is straight after the S");
            Vec3 middle = line[0];
            foreach (Vec3 p in line)
            {
                if (Math.Abs(p.Z - length * 0.5f) < Math.Abs(middle.Z - length * 0.5f))
                    middle = p;
            }

            Program.Check(Math.Abs(middle.X - 1.75f) < 0.06f, "halfway along it has moved half of W: " + middle.X);
            Program.Check(line[1].X < 0.03f, "it leaves the road's line gently: " + line[1].X);
            List<Vec3> toLeft = MarkingTransition.Centre(60f, 3.5f, true, false);
            Program.Near(toLeft[toLeft.Count - 1].X, -3.5f, 1e-5f, "to the left it ends W to the left");
            List<Vec3> widen = MarkingTransition.Centre(60f, 3.5f, false, true);
            Program.Near(widen[widen.Count - 1].Z, 25f, 1e-3f, "a widening ends at 25 m with no extension");
            Program.Check(MarkingTransition.Centre(60f, 0f, true, true).Count == 0 && MarkingTransition.Centre(60f, float.NaN, true, true).Count == 0, "no change of width, no line");

            Program.Check(MarkingTransition.Line(60f, 3.5f, true, true, "edge").Colour == MarkingColour.White && MarkingTransition.Line(60f, 3.5f, true, true, "yellow").Colour == MarkingColour.Yellow, "an edge line is white, a centre line yellow");
            MarkingShape solid = MarkingTransition.Line(60f, 3.5f, true, true, "edge"), change = MarkingTransition.Line(60f, 3.5f, true, true, "change");
            Program.Check(change.Mesh.TriangleCount > 0 && change.Mesh.TriangleCount < solid.Mesh.TriangleCount * 2 && Area(change.Mesh) < Area(solid.Mesh) * 0.6f, "the lane-change line is dashed: half the paint of the solid one");
        }

        // ---------------------------------------------------------------- delineators and markers

        private static void Studs()
        {
            // Table 7, row by row, at the edges of each.
            (float radius, float spacing)[] table =
            {
                (1f, 4f), (29.9f, 4f), (30f, 8f), (89.9f, 8f), (90f, 12f), (179.9f, 12f), (180f, 16f), (274.9f, 16f), (275f, 24f), (374.9f, 24f), (375f, 32f), (999.9f, 32f),
                (1000f, 40f), (1999.9f, 40f), (2000f, 48f), (50000f, 48f)
            };
            foreach ((float radius, float spacing) in table)
                Program.Near(MarkingStuds.CurveSpacing(radius), spacing, 1e-4f, "Table 7: radius " + radius + " m has " + spacing + " m");
            Program.Near(MarkingStuds.CurveSpacing(float.NaN), 4f, 1e-4f, "a radius that is not a number is the tightest row");
            Program.Near(MarkingStuds.CurveSpacing(-5f), 4f, 1e-4f, "and so is one under 0");

            Program.Near(MarkingStuds.DelineatorSpacing(0f, 30f), 30f, 1e-4f, "a straight: the class of road's");
            Program.Near(MarkingStuds.DelineatorSpacing(100f, 30f), 12f, 1e-4f, "R = 100 on another road: 12 m (Table 7)");
            Program.Near(MarkingStuds.DelineatorSpacing(1500f, 30f), 30f, 1e-4f, "a gentle curve is not looser than the straight: 30 m");
            Program.Near(MarkingStuds.DelineatorSpacing(1500f, 50f), 40f, 1e-4f, "but is closer than a motorway's 50 m: 40 m");
            Program.Near(MarkingStuds.DelineatorSpacing(50f, 50f), 8f, 1e-4f, "R = 50 on a motorway: 8 m");
            Program.Near(MarkingStuds.StraightHighway, 50f, 1e-4f, "50, 40 and 30 m on a straight");
            Program.Near(MarkingStuds.StraightFirstClass, 40f, 1e-4f, "");
            Program.Near(MarkingStuds.StraightOther, 30f, 1e-4f, "");

            // The radius of a path.
            Program.Check(MarkingStuds.MinRadius(Straight(60f)) == float.MaxValue, "a straight has no radius");
            Program.Check(MarkingStuds.MinRadius(Straight(8f)) == float.MaxValue, "a path under 12 m is too short to tell");
            var arc = new List<Vec3>();
            for (int i = 0; i <= 30; i++)
                arc.Add(P((float)(50.0 * (1.0 - Math.Cos(i * 2.0 / 50.0))), 0f, (float)(50.0 * Math.Sin(i * 2.0 / 50.0))));
            float radiusOfArc = MarkingStuds.MinRadius(arc);
            Program.Check(Math.Abs(radiusOfArc - 50f) < 2f, "an arc of 50 m: " + radiusOfArc);
            Program.Check(MarkingStuds.CircleRadius(P(0f, 0f, 0f), P(1f, 0f, 0f), P(2f, 0f, 0f)) == float.MaxValue, "three points in a line have no circle");
            Program.Near(MarkingStuds.CircleRadius(P(10f, 0f, 0f), P(0f, 0f, 10f), P(-10f, 0f, 0f)), 10f, 1e-3f, "three points on a circle of 10");

            // The places along a line.
            List<float> at0 = MarkingStuds.Stations(25f, 10f, 0f), at5 = MarkingStuds.Stations(25f, 10f, 5f), at10 = MarkingStuds.Stations(25f, 10f, 10f);
            Program.Check(at0.Count == 3 && at0[0] == 0f && Math.Abs(at0[2] - 20f) < 1e-4f, "from the start: 0, 10, 20");
            Program.Check(at5.Count == 3 && Math.Abs(at5[0] - 5f) < 1e-4f && Math.Abs(at5[2] - 25f) < 1e-4f, "a piece that starts 5 m along: 5, 15, 25");
            Program.Check(at10.Count == 2 && Math.Abs(at10[0] - 10f) < 1e-4f, "a piece that starts at a place leaves it to the piece before: 10, 20 (global 20, 30)");
            Program.Check(MarkingStuds.Stations(0f, 10f, 0f).Count == 0 && MarkingStuds.Stations(10f, 0f, 0f).Count == 0 && MarkingStuds.Stations(10f, float.NaN, 0f).Count == 0, "no length or no spacing: no places");

            // Raised markers: one every gap, in the colour of the line, low and small, on the line's height.
            MarkingShape studs = MarkingStuds.Studs(Straight(30f, 5f), 10f, MarkingColour.Yellow);
            Program.Check(studs.Colour == MarkingColour.Yellow, "in the colour of the line");
            Program.Check(studs.Mesh.TriangleCount == 4 * 10, "four markers (0, 10, 20, 30 m) of ten triangles: " + studs.Mesh.TriangleCount);
            Extents(studs.Mesh, out float sx0, out float sx1, out float sy0, out float sy1, out float sz0, out float sz1);
            Program.Near(sx1 - sx0, MarkingStuds.StudBase, 1e-4f, "10 cm across");
            Program.Near(sy0, 5f, 1e-4f, "on the road at the height of the line");
            Program.Near(sy1 - sy0, MarkingStuds.StudHeight, 1e-4f, "2.2 cm high");
            Program.Near(sz0, -MarkingStuds.StudBase * 0.5f, 1e-4f, "the first at the start");
            Program.Near(sz1, 30f + MarkingStuds.StudBase * 0.5f, 1e-4f, "the last at 30 m");
            bool noneDown = true;
            for (int i = 0; i + 2 < studs.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = studs.Mesh.Positions[studs.Mesh.Indices[i]], b = studs.Mesh.Positions[studs.Mesh.Indices[i + 1]], c = studs.Mesh.Positions[studs.Mesh.Indices[i + 2]];
                noneDown &= MeshBuilder.Cross(b - a, c - a).Y > -1e-9f;
            }

            Program.Check(noneDown, "none faces down");
            Program.Check(MarkingStuds.Studs(Straight(30f), 1f, MarkingColour.White).Mesh.TriangleCount == 16 * 10, "closer than 2 m is 2 m: sixteen in 30 m");
            Program.Check(MarkingStuds.Studs(Straight(30f), 99f, MarkingColour.White).Mesh.TriangleCount == 3 * 10, "and wider than 15 m is 15 m: three (0, 15, 30)");

            // Delineators: a post and a reflector each, every spacing metres, the reflector on the face toward the approaching driver.
            List<MarkingShape> posts = MarkingStuds.Delineators(Straight(60f, 2f), 30f, MarkingColour.Red, 0.7f);
            Program.Check(posts.Count == 2 && posts[0].Colour == MarkingColour.White && posts[1].Colour == MarkingColour.Red, "white posts, red reflectors");
            Program.Check(posts[0].Mesh.TriangleCount == 3 * 10 && posts[1].Mesh.TriangleCount == 3 * 10, "three of each (0, 30, 60 m): " + posts[0].Mesh.TriangleCount + ", " + posts[1].Mesh.TriangleCount);
            List<MarkingShape> one = MarkingStuds.Delineators(Straight(1f, 2f), 30f, MarkingColour.Red, 0.7f);
            Program.Check(one[0].Mesh.TriangleCount == 10 && one[1].Mesh.TriangleCount == 10, "a line of 1 m has the one post, at its start");
            Extents(one[0].Mesh, out float px0, out float px1, out float py0, out float py1, out float pz0, out float pz1);
            Program.Near(py0, 2f, 1e-4f, "on the road");
            Program.Near(py1 - py0, 0.7f, 1e-4f, "70 cm high");
            Program.Near(px1 - px0, MarkingStuds.PostSide, 1e-4f, "7 cm across");
            Extents(one[1].Mesh, out float rx0, out float rx1, out float ry0, out float ry1, out float rz0, out float rz1);
            Program.Near(rx1 - rx0, MarkingStuds.PlateWidth, 1e-4f, "the reflector is 15 cm wide");
            Program.Check(ry0 > py0 + 0.7f * 0.7f && ry1 < py1, "high on the post, under its top: " + (ry0 - py0) + " to " + (ry1 - py0));
            Program.Check(rz0 < pz0 + 1e-5f && rz1 < pz0 + 0.02f, "on the face toward the approaching driver (-Z), against the post");
            Extents(MarkingStuds.Delineators(Straight(10f), 30f, MarkingColour.White, 0.3f)[0].Mesh, out _, out _, out float lo0, out float lo1, out _, out _);
            Program.Near(lo1 - lo0, 0.6f, 1e-4f, "never lower than 60 cm");
            Extents(MarkingStuds.Delineators(Straight(10f), 30f, MarkingColour.White, float.NaN)[0].Mesh, out _, out _, out float nn0, out float nn1, out _, out _);
            Program.Near(nn1 - nn0, 0.7f, 1e-4f, "a height that is not a number is 70 cm");
            Program.Check(MarkingStuds.Delineators(null, 30f, MarkingColour.White, 0.7f)[0].Mesh.VertexCount == 0, "no line, no posts");
        }

        // ---------------------------------------------------------------- the entry

        private static void Entry()
        {
            MarkingShape exit = MarkingWarnings.ExitGore(30f, 4f, 60f), entry = MarkingWarnings.EntryGore(30f, 4f, 60f);
            Extents(exit.Mesh, out float ex0, out float ex1, out _, out _, out float ez0, out float ez1);
            Extents(entry.Mesh, out float nx0, out float nx1, out _, out _, out float nz0, out float nz1);
            Program.Near(nz0, -ez1, 1e-4f, "the entry runs the other way over the road: its gore's far end is where the exit's is, behind the nose");
            Program.Check(nz1 <= -ez0 + 1e-4f && nz1 >= -ez0 - 3f - 1e-4f, "and its thick dashed line follows the nose for the length asked (it ends with a dash or a gap of 3 m): " + nz1);
            Program.Near(nx0, ex0, 1e-3f, "across the road it is the same");
            Program.Near(nx1, ex1, 1e-3f, "");
            Program.Check(Math.Abs(Area(entry.Mesh) - Area(exit.Mesh)) < 0.06f * Area(exit.Mesh), "about the same paint: " + Area(entry.Mesh) + " / " + Area(exit.Mesh));
            // The Vs point to the traffic in both (Fig. 94 a and b): the traffic runs +Z, so each V's point is its lowest z. The gores alone, without the leads.
            foreach (bool isExit in new[] { true, false })
            {
                float sign = isExit ? 1f : -1f;
                var main = new List<Vec3> { P(0f, 0f, 2f * sign), P(0f, 0f, 30f * sign) };
                var ramp = new List<Vec3> { P(0f, 0f, 2f * sign), P(4f, 0f, 30f * sign) };
                var apexes = new List<Vec3>();
                MarkingShape g = MarkingAreas.Gore(main, ramp, isExit, apexes);
                Program.Check(apexes.Count >= 8, (isExit ? "exit" : "entry") + ": Vs along the gore: " + apexes.Count);
                int pointing = 0, wrong = 0;
                foreach (Vec3 a in apexes)
                {
                    // An arm goes forward and out from the point: 0.4 m forward and out of a V's point is paint; 0.4 m back and out is not (it would be, were the V the other way round).
                    bool ahead = CoveredXZ(g.Mesh, a.X + 0.28f, a.Z + 0.28f) || CoveredXZ(g.Mesh, a.X - 0.28f, a.Z + 0.28f);
                    bool behind = CoveredXZ(g.Mesh, a.X + 0.28f, a.Z - 0.6f) && CoveredXZ(g.Mesh, a.X - 0.28f, a.Z - 0.6f);
                    if (ahead && !behind)
                        pointing++;
                    else
                        wrong++;
                }

                Program.Check(pointing > 0 && wrong <= apexes.Count / 4, (isExit ? "exit" : "entry") + ": the Vs point to the traffic that comes (-Z): " + pointing + " right, " + wrong + " not");
            }
            bool up = true;
            for (int i = 0; i + 2 < entry.Mesh.Indices.Count; i += 3)
            {
                Vec3 a = entry.Mesh.Positions[entry.Mesh.Indices[i]], b = entry.Mesh.Positions[entry.Mesh.Indices[i + 1]], c = entry.Mesh.Positions[entry.Mesh.Indices[i + 2]];
                up &= MeshBuilder.Cross(b - a, c - a).Y > 0f;
            }

            Program.Check(up, "every triangle faces up");
            // The exit is wide at its far end (z = 30 + 2); at the entry the wide end is the near one: more paint across the road at the start.
            float WidthIn(MarkingShape s, float z0, float z1)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (Vec3 p in s.Mesh.Positions)
                {
                    if (p.Z >= z0 && p.Z <= z1)
                    {
                        lo = Math.Min(lo, p.X);
                        hi = Math.Max(hi, p.X);
                    }
                }

                return hi < lo ? 0f : hi - lo;
            }

            Program.Check(WidthIn(exit, 29f, 30.1f) > 3f && WidthIn(entry, -30.1f, -29f) > 3f, "the gore is wide where the figure ends for an exit and where it starts for an entry");
            Program.Check(WidthIn(exit, -60f, -10f) < 0.6f && WidthIn(entry, 10f, 60f) < 0.6f, "and the thick dashed line is before the nose of an exit and after the nose of an entry");
        }

        /// <summary>Whether a point of the ground is under a triangle of a mesh.</summary>
        private static bool CoveredXZ(MeshBuilder mesh, float x, float z)
        {
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                float d1 = (x - b.X) * (a.Z - b.Z) - (a.X - b.X) * (z - b.Z), d2 = (x - c.X) * (b.Z - c.Z) - (b.X - c.X) * (z - c.Z), d3 = (x - a.X) * (c.Z - a.Z) - (c.X - a.X) * (z - a.Z);
                bool neg = d1 < 0f || d2 < 0f || d3 < 0f, pos = d1 > 0f || d2 > 0f || d3 > 0f;
                if (!(neg && pos))
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- the catalogue

        private static void Templates()
        {
            string[] ids = { "mk.kerb.nopark", "mk.kerb.nolong", "mk.taper", "mk.elevation", "mk.stud", "mk.delineator" };
            string[] codes = { "线48", "线50", "7.1", "线72", "8.1", "8.2" };
            for (int i = 0; i < ids.Length; i++)
            {
                SignDef def = SignCatalog.ById(ids[i]);
                Program.Check(def != null && def.IsMarking && def.Code == codes[i], ids[i] + " is in the catalogue as " + codes[i]);
                MarkingPlan plan = MarkingTemplates.PlanOf(def, def.Template.Default().ToString());
                Program.Check(plan.TriangleCount >= 4, ids[i] + ": paint of the default: " + plan.TriangleCount);
            }

            // The runs have a path and a phase; the elevation marking and the taper are not runs.
            Program.Check(MarkingTemplates.IsRun(MarkingTemplates.ById("mk.kerb.nolong")) && MarkingTemplates.IsRun(MarkingTemplates.ById("mk.stud")) && MarkingTemplates.IsRun(MarkingTemplates.ById("mk.delineator")), "kerb lines, markers and delineators are runs along a road");
            Program.Check(!MarkingTemplates.IsRun(MarkingTemplates.ById("mk.elevation")) && !MarkingTemplates.IsRun(MarkingTemplates.ById("mk.taper")), "the elevation marking and the transition are not");

            // The exit or the entry.
            GuideTemplate gore = MarkingTemplates.ById("mk.gore");
            MarkingPlan exit = MarkingTemplates.PlanOf(gore, gore.Default().ToString()), entry = MarkingTemplates.PlanOf(gore, gore.Parse(gore.Default().ToString()).With("form", "entry").ToString());
            Program.Check(exit.MaxZ > 20f && entry.MaxZ < 70f && entry.MinZ < -20f && exit.MinZ > -70f, "the exit's gore is ahead of the nose, the entry's behind it: " + exit.MinZ + ".." + exit.MaxZ + " / " + entry.MinZ + ".." + entry.MaxZ);

            // The design speed of the transition is the road's when it is 0 and kept when set.
            GuideTemplate taper = MarkingTemplates.ById("mk.taper");
            string spec = taper.Default().ToString();
            Program.Check(taper.Parse(MarkingTemplates.ResolveSize(taper, spec, 80f, true)).Number("kmh", 0.0) == 80.0, "0 takes the road's speed");
            Program.Check(taper.Parse(MarkingTemplates.ResolveSize(taper, spec, 0f, false)).Number("kmh", 0.0) == 60.0, "and 60 when the road is not known");
            Program.Check(taper.Parse(MarkingTemplates.ResolveSize(taper, taper.Parse(spec).With("kmh", "45").ToString(), 80f, true)).Number("kmh", 0.0) == 45.0, "a speed that was set is kept");
            MarkingPlan at40 = MarkingTemplates.PlanOf(taper, taper.Parse(spec).With("kmh", "40").ToString()), at80 = MarkingTemplates.PlanOf(taper, taper.Parse(spec).With("kmh", "80").ToString());
            Program.Check(at80.MaxZ > at40.MaxZ * 1.5f, "a faster road has a longer transition: " + at40.MaxZ + " / " + at80.MaxZ);
            string note = taper.MarkingNote(taper.Parse(spec).With("kmh", "60"), false);
            Program.Check(note.Contains("81.3") && note.Contains("40"), "the editor says the length the formula gives and the table's: " + note);

            // The delineators are closer on a curve.
            GuideTemplate post = MarkingTemplates.ById("mk.delineator");
            var arc = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
                arc.Add(P((float)(60.0 * (1.0 - Math.Cos(i * 2.0 / 60.0))), 0f, (float)(60.0 * Math.Sin(i * 2.0 / 60.0))));
            string path = RunPath.Encode(arc, out _);
            string curved = MarkingTemplates.WithPath(post, post.Default().ToString(), path), straight = MarkingTemplates.WithPath(post, post.Parse(post.Default().ToString()).With("curve", "0").ToString(), path);
            Program.Check(MarkingTemplates.PlanOf(post, curved).TriangleCount > MarkingTemplates.PlanOf(post, straight).TriangleCount, "on a curve of 60 m radius there are more posts than at the straight spacing: " + MarkingTemplates.PlanOf(post, curved).TriangleCount + " / " + MarkingTemplates.PlanOf(post, straight).TriangleCount);
            Program.Check(post.MarkingNote(post.Parse(curved), true).Contains("8 m") && post.MarkingNote(post.Parse(straight), true).Contains("30 m"), "the editor says the spacing: " + post.MarkingNote(post.Parse(curved), true));
        }
    }
}
