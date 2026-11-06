using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The flat shapes of the road markings (RoadMarkings, GB 5768.3-2025): sizes from the standard, up-facing triangles, layout of the crosswalk, dashes that follow a bend.</summary>
    internal static class RoadMarkingTests
    {
        public static void RunAll()
        {
            Program.Run("markings: a rectangle is two up-facing triangles of the right area", Rectangles);
            Program.Run("markings: the stop line (20, 30, 40 cm)", StopLine);
            Program.Run("markings: the crosswalk stripes and gaps (40/45 cm, gap 60 to 80 cm)", Crosswalk);
            Program.Run("markings: the stop-and-give-way line and its word", GiveWay);
            Program.Run("markings: dashed lines (4-6, 6-9, 3-9, 2-4) on a straight line and on a bend", Dashes);
            Program.Run("markings: bad input gives an empty shape", BadInput);
        }

        /// <summary>Every triangle of the mesh faces up (the geometric normal has a positive Y), the indices are in range, the numbers finite, and no triangle is a sliver of no area.</summary>
        private static void Sound(MeshBuilder mesh, string what)
        {
            Program.Check(mesh.Indices.Count % 3 == 0, what + ": whole triangles");
            for (int i = 0; i < mesh.Indices.Count; i++)
                Program.Check(mesh.Indices[i] >= 0 && mesh.Indices[i] < mesh.VertexCount, what + ": index " + i + " in range");
            foreach (Vec3 p in mesh.Positions)
                Program.Check(p.IsFinite, what + ": finite position");
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[i]], b = mesh.Positions[mesh.Indices[i + 1]], c = mesh.Positions[mesh.Indices[i + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                Program.Check(n.Y > 0f, what + ": triangle " + i / 3 + " faces up (" + n.Y + ")");
                Program.Check(n.Length > 1e-7f, what + ": triangle " + i / 3 + " has an area");
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

        private static void Rectangles()
        {
            var mesh = new MeshBuilder();
            RoadMarkings.Rect(mesh, 1f, 2f, 4f, 3f);
            Sound(mesh, "rect");
            Program.Check(mesh.TriangleCount == 2 && mesh.VertexCount == 4, "two triangles on four vertices");
            var shape = new MarkingShape();
            RoadMarkings.Rect(shape.Mesh, 1f, 2f, 4f, 3f);
            Program.Near(shape.Area, 3f, 1e-4f, "3 m x 1 m is 3 m2");
            // The corners given in the other order make the same rectangle.
            var flipped = new MarkingShape();
            RoadMarkings.Rect(flipped.Mesh, 4f, 3f, 1f, 2f);
            Sound(flipped.Mesh, "flipped rect");
            Program.Near(flipped.Area, 3f, 1e-4f, "corners in the other order, the same area");
            var raised = new MeshBuilder();
            RoadMarkings.Rect(raised, 0f, 0f, 1f, 1f, 7.5f);
            Program.Check(raised.Positions.TrueForAll(p => p.Y == 7.5f), "the height is the one given");
        }

        private static void StopLine()
        {
            foreach ((float asked, float width) in new[] { (0.2f, 0.2f), (0.3f, 0.3f), (0.4f, 0.4f), (0.31f, 0.3f), (0.36f, 0.4f), (0.9f, 0.4f), (0.05f, 0.2f), (0.25f, 0.2f), (0.35f, 0.3f) })
            {
                MarkingShape line = RoadMarkings.StopLine(7f, asked);
                Sound(line.Mesh, "stop line " + asked);
                Bounds(line.Mesh, out float x0, out float x1, out float z0, out float z1);
                Program.Near(x1 - x0, 7f, 1e-4f, asked + ": the span is the span");
                Program.Near(z1 - z0, width, 1e-4f, asked + ": the width is snapped to " + width);
                Program.Near((x0 + x1) * 0.5f, 0f, 1e-4f, asked + ": centred across");
                Program.Near((z0 + z1) * 0.5f, 0f, 1e-4f, asked + ": centred along");
                Program.Near(line.Area, 7f * width, 1e-3f, asked + ": area is span x width");
                Program.Check(line.Colour == MarkingColour.White, "white");
            }

            Program.Check(RoadMarkings.StopLine(3.5f).Texts.Count == 0, "a stop line has no word");
        }

        private static void Crosswalk()
        {
            // Spans from a narrow lane to a wide road, both stripe widths: the stripes cover the span from end to end, the gaps are equal and stay in the standard's range wherever the span allows.
            for (float span = 3f; span <= 40f; span += 0.37f)
            {
                foreach (float stripe in new[] { 0.4f, 0.45f })
                {
                    int count = RoadMarkings.CrosswalkStripes(span, stripe, out float gap);
                    Program.Check(count >= 2, span + "/" + stripe + ": two stripes at least (" + count + ")");
                    Program.Near(count * stripe + (count - 1) * gap, span, 1e-3f, span + "/" + stripe + ": stripes and gaps fill the span");
                    Program.Check(gap <= RoadMarkings.CrosswalkMaxGap + 1e-4f, span + "/" + stripe + ": the gap is not over 80 cm (" + gap + ")");
                    Program.Check(gap >= 0.3f, span + "/" + stripe + ": the gap is not under 30 cm (my sanity limit, the standard has none): " + gap);

                    MarkingShape walk = RoadMarkings.Crosswalk(span, stripe, 3f);
                    Sound(walk.Mesh, "crosswalk " + span);
                    Bounds(walk.Mesh, out float x0, out float x1, out float z0, out float z1);
                    Program.Near(x1 - x0, span, 1e-3f, span + "/" + stripe + ": from end to end");
                    Program.Near(z1 - z0, 3f, 1e-4f, span + "/" + stripe + ": the length is the length");
                    Program.Near(walk.Area, count * stripe * 3f, 1e-2f, span + "/" + stripe + ": area is the stripes");
                    Program.Check(walk.Mesh.TriangleCount == count * 2, span + "/" + stripe + ": two triangles to a stripe");
                }
            }

            // The common case: 3.0 m of 40 cm stripes at the usual 60 cm: (3.0 + 0.6) / 1.0 = 3 stripes would leave a gap of 0.9 (over 80), so four with 0.467 are nearer 60.
            int four = RoadMarkings.CrosswalkStripes(3f, 0.4f, out float g4);
            Program.Check(four == 4 && Math.Abs(g4 - 1.4f / 3f) < 1e-4f, "3 m of 40 cm stripes: four, the gap 0.467 (" + four + ", " + g4 + ")");
            int exact = RoadMarkings.CrosswalkStripes(5f, 0.4f, out float ge);
            Program.Check(exact == 5 && Math.Abs(ge - 0.75f) < 1e-3f, "5 m of 40 cm stripes: five, the gap 0.75 (" + exact + ", " + ge + ")");
            int tight = RoadMarkings.CrosswalkStripes(3f, 0.45f, out float gt);
            Program.Check(tight == 4 && Math.Abs(gt - 0.4f) < 1e-3f, "3 m of 45 cm stripes: three would leave 82.5 cm, over the 80 the standard allows, so four with 40 cm (" + tight + ", " + gt + ")");
            int fit = RoadMarkings.CrosswalkStripes(0.4f * 7f + 0.6f * 6f, 0.4f, out float gf);
            Program.Check(fit == 7 && Math.Abs(gf - 0.6f) < 1e-4f, "a span that is seven stripes and six 60 cm gaps is exactly that (" + fit + ", " + gf + ")");

            // A span that is exactly n stripes and n - 1 gaps of 60 cm has n stripes, however the float sum rounds (n = 36 of 45 cm stripes comes out a hair under without a margin).
            foreach (float stripe in new[] { 0.4f, 0.45f })
            {
                for (int n = 2; n <= 40; n++)
                {
                    float exactSpan = n * stripe + (n - 1) * 0.6f;
                    int found = RoadMarkings.CrosswalkStripes(exactSpan, stripe, out float exactGap);
                    Program.Check(found == n && Math.Abs(exactGap - 0.6f) < 1e-3f, "exactly n stripes: " + n + " of " + stripe + " in " + exactSpan + " m, found " + found + " with gap " + exactGap);
                }
            }

            // The length: not under 2 m (the figure's label), the given one above it.
            MarkingShape shortWalk = RoadMarkings.Crosswalk(6f, 0.4f, 1f);
            Bounds(shortWalk.Mesh, out _, out _, out float zs0, out float zs1);
            Program.Near(zs1 - zs0, 2f, 1e-4f, "a stripe is 2 m long at the least");
            MarkingShape longWalk = RoadMarkings.Crosswalk(6f, 0.45f, 5f);
            Bounds(longWalk.Mesh, out _, out _, out float zl0, out float zl1);
            Program.Near(zl1 - zl0, 5f, 1e-4f, "a longer stripe stays");
            Program.Check(RoadMarkings.Crosswalk(6f, 0.43f).Area == RoadMarkings.Crosswalk(6f, 0.45f).Area && RoadMarkings.Crosswalk(6f, 0.42f).Area == RoadMarkings.Crosswalk(6f, 0.4f).Area, "a width in between is snapped to 40 or 45");

            // A span too narrow for two stripes: one stripe, as wide as the span allows.
            MarkingShape narrow = RoadMarkings.Crosswalk(0.6f, 0.4f, 3f);
            Sound(narrow.Mesh, "narrow");
            Bounds(narrow.Mesh, out float nx0, out float nx1, out _, out _);
            Program.Check(narrow.Mesh.TriangleCount == 2 && nx1 - nx0 <= 0.6f + 1e-4f, "a span too narrow for two stripes has one that fits");
        }

        private static void GiveWay()
        {
            MarkingShape line = RoadMarkings.GiveWay(8f, new[] { -2f, 2f });
            Sound(line.Mesh, "give way");
            Program.Check(line.Mesh.TriangleCount == 4, "two lines");
            Program.Near(line.Area, 2f * 8f * RoadMarkings.GiveWayLineWidth, 1e-3f, "two lines of 20 cm across 8 m");
            Bounds(line.Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(x1 - x0, 8f, 1e-4f, "the span");
            Program.Near(z1 - z0, 0.6f, 1e-4f, "20 cm, 20 cm, 20 cm: 60 cm in all");
            Program.Near((z0 + z1) * 0.5f, 0f, 1e-4f, "the pair is centred on z = 0");

            // The two lines are apart by 20 cm: the middle row of z has no triangle in it.
            var zs = new SortedSet<float>();
            foreach (Vec3 p in line.Mesh.Positions)
                zs.Add((float)Math.Round(p.Z, 3));
            var rows = new List<float>(zs);
            Program.Check(rows.Count == 4 && Math.Abs(rows[2] - rows[1] - 0.2f) < 1e-3f, "the gap between the two lines is 20 cm (" + string.Join(", ", rows) + ")");

            Program.Check(line.Texts.Count == 2 && line.Texts[0].Text == "停" && line.Texts[1].Text == "停", "one word for each lane centre given");
            foreach (MarkingText text in line.Texts)
            {
                Program.Near(text.Width, 1f, 1e-6f, "the word is 1 m wide");
                Program.Near(text.Length, 2.5f, 1e-6f, "and 2.5 m high");
                Program.Check(text.Z + text.Length * 0.5f <= -0.3f - RoadMarkings.GiveWayTextGap + 1e-4f, "behind the lines (the traffic comes from -Z): the edge nearer the lines is at " + (text.Z + text.Length * 0.5f));
            }

            Program.Near(line.Texts[0].X, -2f, 1e-6f, "the word is on its lane");
            Program.Check(RoadMarkings.GiveWay(8f).Texts.Count == 0 && RoadMarkings.GiveWay(8f, new float[0]).Texts.Count == 0, "no lane centres, no word (non-motor lanes have none)");
            MarkingShape wide = RoadMarkings.GiveWay(8f, new[] { 0f }, 2f);
            Program.Near(wide.Texts[0].Z + 1.25f, -0.3f - 2f, 1e-4f, "another gap moves the word");
            Program.Check(RoadMarkings.GiveWay(8f, new[] { float.NaN, 1f, float.PositiveInfinity }).Texts.Count == 1, "a lane centre that is not a number has no word");
            Program.Check(RoadMarkings.GiveWayTextGap == 2f && RoadMarkings.GiveWayTextGapMax == 2.5f, "Fig. 76: the word 200 to 250 behind the line; 2 m used");
        }

        private static List<Vec3> Line(float length, int steps)
        {
            var points = new List<Vec3>();
            for (int i = 0; i <= steps; i++)
                points.Add(new Vec3(0f, 0f, length * i / steps));
            return points;
        }

        private static void Dashes()
        {
            // The four patterns of the standard.
            DashSpec centre = RoadMarkings.CentreLine();
            Program.Check(centre.On == 4f && centre.Off == 6f && centre.Width == 0.15f && centre.Colour == MarkingColour.Yellow, "centre line: yellow 4-6, 15 cm");
            Program.Check(RoadMarkings.CentreLine(true).Width == 0.10f, "10 cm on a special road");
            DashSpec fast = RoadMarkings.LaneLineFast();
            Program.Check(fast.On == 6f && fast.Off == 9f && fast.Colour == MarkingColour.White, "lane line at speed: white 6-9");
            Program.Check(RoadMarkings.LaneLineFast(true).On == 3f && RoadMarkings.LaneLineFast(true).Off == 9f, "or 3-9");
            Program.Check(RoadMarkings.LaneLineSlow().On == 2f && RoadMarkings.LaneLineSlow().Off == 4f && RoadMarkings.LaneLineSlow(true).Width == 0.10f, "lane line below 60 km/h: 2-4");

            // A straight line of 100 m in 4-6: ten dashes of 4 m.
            MarkingShape ten = RoadMarkings.Dashed(Line(100f, 4), centre);
            Sound(ten.Mesh, "straight dashes");
            Program.Check(ten.Colour == MarkingColour.Yellow, "the colour of the spec");
            Program.Near(ten.Area, 10f * 4f * 0.15f, 1e-2f, "ten dashes of 4 m x 15 cm");
            Bounds(ten.Mesh, out float x0, out float x1, out float z0, out float z1);
            Program.Near(x1 - x0, 0.15f, 1e-4f, "15 cm wide");
            Program.Near(z0, 0f, 1e-4f, "the first dash starts at the start");
            Program.Near(z1, 94f, 1e-3f, "the last dash ends at 94 m (90 + 4)");

            // The phase moves the pattern; a dash cut by the end is shortened.
            MarkingShape phased = RoadMarkings.Dashed(Line(20f, 2), centre, 3f);
            Bounds(phased.Mesh, out _, out _, out float p0, out float p1);
            Program.Check(p0 < 1e-4f && p1 <= 20f + 1e-4f, "the phase starts the line inside a dash and the end is the end of the line");
            Program.Near(phased.Area, (1f + 4f + 3f) * 0.15f, 1e-2f, "a 3 m phase on 20 m: the dashes are at -3..1, 7..11 and 17..21, cut to 0..1, 7..11 and 17..20: 1 + 4 + 3 m");

            // A bend: a quarter circle of radius 20 m. Every vertex of every dash is half a width from the circle, and the dashes follow it.
            const float r = 20f;
            var arc = new List<Vec3>();
            const int pieces = 90;
            for (int i = 0; i <= pieces; i++)
            {
                double a = Math.PI * 0.5 * i / pieces;
                arc.Add(new Vec3((float)(r * (1.0 - Math.Cos(a))), 0f, (float)(r * Math.Sin(a))));
            }

            MarkingShape bend = RoadMarkings.Dashed(arc, RoadMarkings.LaneLineSlow());
            Sound(bend.Mesh, "bend");
            Vec3 centrePoint = new Vec3(r, 0f, 0f);
            float worst = 0f;
            foreach (Vec3 p in bend.Mesh.Positions)
                worst = Math.Max(worst, Math.Abs(Math.Abs(Vec3.DistanceXZ(p, centrePoint) - r) - 0.075f));
            Program.Check(worst < 0.01f, "every vertex of a dash on the bend is half a width from the circle (worst " + worst + ")");
            float arcLength = (float)(Math.PI * 0.5 * r);
            int dashes = (int)Math.Ceiling(arcLength / 6f);
            Program.Check(bend.Mesh.TriangleCount > dashes * 2, "a dash on a bend has more than one quad: " + bend.Mesh.TriangleCount + " triangles for " + dashes + " dashes");
            float expected = 0f;
            foreach (Dash d in Marking.Dashes(arcLength, 2f, 4f))
                expected += d.End - d.Start;
            Program.Near(bend.Area, expected * 0.15f, expected * 0.15f * 0.03f, "the painted area is the dashes' length x the width (within 3 %)");

            // A solid line (no gap) is one long ribbon.
            MarkingShape solid = RoadMarkings.Dashed(Line(30f, 3), new DashSpec(4f, 0f, 0.2f, MarkingColour.White));
            Program.Near(solid.Area, 30f * 0.2f, 1e-2f, "a line with no gap is solid");
        }

        private static void BadInput()
        {
            Program.Check(RoadMarkings.StopLine(0f).Mesh.TriangleCount == 0 && RoadMarkings.StopLine(-3f).Mesh.TriangleCount == 0 && RoadMarkings.StopLine(float.NaN).Mesh.TriangleCount == 0 && RoadMarkings.StopLine(float.PositiveInfinity).Mesh.TriangleCount == 0, "no span, no stop line");
            Program.Check(RoadMarkings.StopLine(5f, float.NaN).Mesh.TriangleCount == 2, "a width that is not a number is snapped to the first (20 cm)");
            Program.Check(RoadMarkings.Crosswalk(0f).Mesh.TriangleCount == 0 && RoadMarkings.Crosswalk(float.NaN).Mesh.TriangleCount == 0 && RoadMarkings.Crosswalk(-1f).Mesh.TriangleCount == 0, "no span, no crosswalk");
            Program.Check(RoadMarkings.Crosswalk(5f, 0.4f, float.NaN).Mesh.TriangleCount == 0, "a length that is not a number, no crosswalk");
            Program.Check(RoadMarkings.GiveWay(0f).Mesh.TriangleCount == 0 && RoadMarkings.GiveWay(float.NaN).Mesh.TriangleCount == 0, "no span, no give-way line");
            var spec = RoadMarkings.CentreLine();
            Program.Check(RoadMarkings.Dashed(null, spec).Mesh.TriangleCount == 0 && RoadMarkings.Dashed(new List<Vec3>(), spec).Mesh.TriangleCount == 0 && RoadMarkings.Dashed(new List<Vec3> { Vec3.Zero }, spec).Mesh.TriangleCount == 0, "no line, no dashes");
            Program.Check(RoadMarkings.Dashed(Line(10f, 1), new DashSpec(0f, 6f, 0.15f, MarkingColour.White)).Mesh.TriangleCount == 0 && RoadMarkings.Dashed(Line(10f, 1), new DashSpec(4f, 6f, 0f, MarkingColour.White)).Mesh.TriangleCount == 0, "no dash length or no width, no dashes");
            Program.Check(RoadMarkings.Dashed(Line(10f, 1), spec, float.NaN).Mesh.TriangleCount == 0 && RoadMarkings.Dashed(Line(10f, 1), spec, float.PositiveInfinity).Mesh.TriangleCount == 0, "a phase that is not a number, no dashes");
            var stationary = new List<Vec3> { new Vec3(1f, 0f, 1f), new Vec3(1f, 0f, 1f), new Vec3(1f, 0f, 1f) };
            Program.Check(RoadMarkings.Dashed(stationary, spec).Mesh.TriangleCount == 0, "a line that does not move has no length, no dashes");
            // A line with a repeated point inside a dash still gives sound dashes (no quad of no area).
            var repeated = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 2f), new Vec3(0f, 0f, 2f), new Vec3(0f, 0f, 12f) };
            MarkingShape ok = RoadMarkings.Dashed(repeated, spec);
            Sound(ok.Mesh, "repeated point");
            Program.Near(ok.Area, (4f + 2f) * 0.15f, 1e-3f, "the dashes 0..4 and 10..12 are whole although a point of the line is twice in the first");
        }
    }
}
