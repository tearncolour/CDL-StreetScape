using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The hollow and neon styles of the text light box (0.12.76): the band along a letter's outline (areas worked out by hand, the nesting of its loops, strokes too thin for a hole), the tube swept along
    /// the outline moved inward (its distance from the edge, its section, that it is closed and faces out), the glyphs of the traffic sign font in both, the plan's frame on them, and the board built on a
    /// post. The look in the game is not testable here.
    /// </summary>
    internal static class TextEffectTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("effects: the numbers of the frame for a band or a tube", Numbers);
            Program.Run("effects: the band along an outline, against areas worked out by hand", Bands);
            Program.Run("effects: the tube along an outline", Tubes);
            Program.Run("effects: glyphs of the traffic sign font as bands and tubes", RealGlyphs);
            Program.Run("effects: planned, and held by a frame that fits the band", Planned);
            Program.Run("effects: dressings that come one after the other", Combined);
            Program.Run("effects: built on a post", Built);
        }

        private const float Flat = 0.0017f;

        private static List<Point2> Rect(float x0, float y0, float x1, float y1, bool hole = false)
        {
            var loop = new List<Point2> { new Point2(x0, y0), new Point2(x1, y0), new Point2(x1, y1), new Point2(x0, y1) };
            if (hole)
                loop.Reverse();
            return loop;
        }

        private static GlyphMesh Mesh(params List<Point2>[] loops) => TextGlyphs.FromLoops(loops.ToList(), 1f);

        /// <summary>The least distance from a point to the edges of the loops.</summary>
        private static float DistanceTo(List<List<Point2>> loops, Point2 p)
        {
            double best = double.MaxValue;
            foreach (List<Point2> loop in loops)
            {
                for (int i = 0; i < loop.Count; i++)
                {
                    Point2 a = loop[i], b = loop[(i + 1) % loop.Count];
                    double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                    double t = l2 < 1e-18 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
                    double ex = p.X - (a.X + dx * t), ey = p.Y - (a.Y + dy * t);
                    best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
                }
            }

            return (float)best;
        }

        private static void Numbers()
        {
            TextArt plain = TextArt.Create(true), hollow = TextStyles.Art(TextStyles.Outline, false), neon = TextStyles.Art(TextStyles.Neon, false);
            Program.Check(hollow.Effect == TextEffect.Outline && hollow.EffectWidth == TextStyles.OutlineWidth && neon.Effect == TextEffect.Neon && neon.EffectWidth == TextStyles.NeonRadius, "the styles' effects");
            Program.Check(hollow.ChangesGlyphs && neon.ChangesGlyphs && !hollow.IsPlain && hollow.Key != neon.Key && hollow.GlyphVariant != neon.GlyphVariant && hollow.GlyphVariant != 0, "each has its own key and glyph variant");
            Program.Check(TextArt.Create(false, 0f, 0f, TextEffect.None, 0.5f).IsPlain && TextArt.Create(false, 0f, 0f, TextEffect.None, 0.5f).EffectWidth == 0f, "a width with no effect is no dressing");
            Program.Check(TextArt.Create(false, 0f, 0f, TextEffect.Outline, -1f).EffectWidth == 0f && !hollow.Equals(TextArt.Create(false, 0f, 0f, TextEffect.Outline, 0.05f)) && hollow.Equals(TextArt.Create(false, 0f, 0f, TextEffect.Outline, 0.03f)), "the width is part of the dressing, and never negative");

            Program.Check(TextArt.Create(true, 0f, 0f, TextEffect.None, 0.5f).Equals(TextArt.Create(true)) && TextArt.Create(true, 0f, 0f, TextEffect.None, 0.5f).Key == TextArt.Create(true).Key, "with only a fallback, a width with no effect is no dressing either");
            Program.Check(hollow.Key != TextArt.Create(false, 0f, 0f, TextEffect.Outline, 0.05f).Key && neon.Key != TextArt.Create(false, 0f, 0f, TextEffect.Neon, 0.03f).Key, "the width is in the key, so that a stale plan is told from the current one");

            // The same width as a band and as a tube is not the same dressing.
            TextArt sameWidthOutline = TextArt.Create(false, 0f, 0f, TextEffect.Outline, 0.03f), sameWidthNeon = TextArt.Create(false, 0f, 0f, TextEffect.Neon, 0.03f);
            Program.Check(sameWidthOutline.Key != sameWidthNeon.Key && sameWidthOutline.GlyphVariant != sameWidthNeon.GlyphVariant && !sameWidthOutline.Equals(sameWidthNeon), "a band and a tube of one width are two dressings");
            Program.Check(TextArt.Create(false, 0.02f, 0f, TextEffect.Outline, 0.03f).GlyphVariant != sameWidthOutline.GlyphVariant && TextArt.Create(false, 0f, 0.2f, TextEffect.Outline, 0.03f).GlyphVariant != sameWidthOutline.GlyphVariant, "and bold or leaning ones from the plain hollow");

            // The studs of the frame are half the size for a band or a tube.
            FrameStyle solid = FrameStyle.For(0.3f), light = TextStyles.FrameFor(0.3f, hollow);
            Program.Near(light.StudRadius, solid.StudRadius * 0.5f, 1e-7f, "a hollow letter's studs are half as thick");
            Program.Check(TextStyles.FrameFor(0.3f, plain).StudRadius == solid.StudRadius && TextStyles.FrameFor(0.3f, null).StudRadius == solid.StudRadius && TextStyles.FrameFor(0.3f, TextArt.Plain).RailSize == solid.RailSize, "any other dressing has the frame as it was");
            Program.Near(TextStyles.MinBand(0.3f, hollow), 4.4f * light.StudRadius / 0.3f, 1e-6f, "the least band is four studs' radii and a tenth, in em: " + TextStyles.MinBand(0.3f, hollow));
            Program.Check(Math.Abs(TextStyles.MinBand(0.1f, hollow) - TextStyles.MinBand(0.3f, hollow)) < 1e-5f && TextStyles.MinBand(1f, hollow) < TextStyles.MinBand(0.3f, hollow), "the same share of the letter for small letters, less for big ones");
            Program.Near(TextStyles.TubeRadius(0.3f, neon), 0.02f, 1e-7f, "a tube is its own radius when a stud fits");
            Program.Check(TextStyles.TubeRadius(0.3f, hollow) == 0f && TextStyles.TubeRadius(0.3f, null) == 0f && TextStyles.TubeRadius(0.3f, TextStyles.Art(TextStyles.Neon, false)) > 0f, "and none for a letter that is not neon");
            TextArt fat = TextArt.Create(false, 0f, 0f, TextEffect.Neon, 0.004f);
            Program.Near(TextStyles.TubeRadius(0.3f, fat), TextStyles.MinBand(0.3f, fat) * 0.5f, 1e-6f, "a tube too thin for a stud is made as thick as one needs");
        }

        private static void Bands()
        {
            // A square's band of width 0.1: the square less the square shrunk by 0.1 (its corners stay sharp).
            GlyphMesh square = Mesh(Rect(0, 0, 1, 1));
            GlyphMesh band = GlyphStyles.Outline(square, 0.1f, Flat);
            Program.Near(band.Area, 1f - 0.64f, 0.004f, "a square's band: " + band.Area);
            Program.Check(band.Loops.Count == 2 && band.Loops.Count(l => CutOutline.Area(l) > 0f) == 1 && band.Loops.Count(l => CutOutline.Area(l) < 0f) == 1, "an outer loop and a hole");
            Program.Check(band.IsSound(out string why), "a sound mesh: " + why);
            Program.Check(band.Loops.Any(l => l.Count == 4 && l.SequenceEqual(square.Loops[0])), "the letter's own edge is the band's outer edge, point for point");
            List<Point2> hole = band.Loops.First(l => CutOutline.Area(l) < 0f);
            Program.Check(hole.All(p => Math.Abs(DistanceTo(square.Loops, p) - 0.1f) < 0.006f), "the hole is 0.1 in from the edge all the way round");
            Program.Check(square.Loops.Count == 1 && square.Area == 1f, "the glyph given is the glyph it was");

            // A ring: a band along the outside of the letter and another round its hole: two rings, four loops, nested three deep.
            GlyphMesh ring = Mesh(Rect(0, 0, 1, 1), Rect(0.3f, 0.3f, 0.7f, 0.7f, true));
            GlyphMesh two = GlyphStyles.Outline(ring, 0.05f, Flat);
            float expected = (1f - 0.81f) + (0.25f - (4f - (float)Math.PI) * 0.0025f - 0.16f);
            Program.Near(two.Area, expected, 0.004f, "the band of a ring: " + two.Area + " of " + expected);
            Program.Check(two.Loops.Count == 4 && two.Loops.Count(l => CutOutline.Area(l) > 0f) == 2, "four loops, two of them running the way a hole does");
            Program.Check(two.IsSound(out why), "and the mesh covers what the loops fill: " + why);
            Program.Check(!GlyphStyles.Crosses(two.Loops), "no loop crosses another");

            // A stroke narrower than twice the width has no hole: the band is the stroke.
            GlyphMesh strip = Mesh(Rect(0, 0, 0.06f, 1f));
            GlyphMesh whole = GlyphStyles.Outline(strip, 0.05f, Flat);
            Program.Check(whole.Loops.Count == 1 && Math.Abs(whole.Area - strip.Area) < 1e-5f && whole.IsSound(out _), "a stroke 0.06 wide with a band of 0.05 stays a stroke");
            Program.Check(GlyphStyles.Outline(Mesh(Rect(0, 0, 0.1f, 1f)), 0.05f, Flat).Loops.Count == 1, "(and one just wide enough for no hole)");

            // The band is at least what a stud needs.
            GlyphMesh wide = GlyphStyles.Outline(square, 0.01f, Flat, 0.05f);
            Program.Near(wide.Area, 1f - 0.81f, 0.004f, "a band asked for 0.01 and held to 0.05: " + wide.Area);

            // Nothing to do, and numbers that are not numbers.
            Program.Check(GlyphStyles.Outline(null, 0.1f, Flat) == null && ReferenceEquals(GlyphStyles.Outline(square, 0f, Flat), square) && ReferenceEquals(GlyphStyles.Outline(square, -1f, Flat), square) && ReferenceEquals(GlyphStyles.Outline(square, float.NaN, Flat), square), "no mesh, no width: nothing made");

            // The way up: a T's band holds its stem at the top.
            GlyphMesh tee = Mesh(new List<Point2> { new Point2(0, 0), new Point2(3, 0), new Point2(3, 1), new Point2(2, 1), new Point2(2, 3), new Point2(1, 3), new Point2(1, 1), new Point2(0, 1) });
            GlyphMesh teeBand = GlyphStyles.Outline(tee, 0.1f, Flat);
            List<Point2> teeHole = teeBand.Loops.First(l => CutOutline.Area(l) < 0f);
            Program.Check(teeHole.Max(p => p.Y) > 2.8f && teeHole.Min(p => p.Y) < 0.2f && TextGlyphs.Inside(teeHole, 1.5f, 2.5f) && !TextGlyphs.Inside(teeHole, 0.3f, 2.5f), "the hole of a T is a T: its stem up");
        }

        private static List<List<Point2>> NeonLoops(GlyphMesh mesh) => mesh.Tube.Centre;

        private static void Tubes()
        {
            GlyphMesh square = Mesh(Rect(0, 0, 1, 1));
            GlyphMesh neon = GlyphStyles.Neon(square, 0.05f, Flat);
            Program.Check(neon.Tube != null && neon.Tube.Centre.Count == 1 && neon.Tube.Radius == 0.05f, "a tube of radius 0.05 along one line");
            Program.Check(NeonLoops(neon)[0].All(p => Math.Abs(DistanceTo(square.Loops, p) - 0.05f) < 0.005f), "its line is 0.05 in from the edge all the way round");
            bool sound = neon.IsSound(out string why);
            Program.Check(neon.Loops.Count == 2 && sound && Math.Abs(neon.Area - (1f - 0.64f)) < 0.004f, "the glyph's loops are the band under the tube, 2r wide: " + neon.Area + " " + why);
            Program.Check(neon.Vertices == neon.Tube.Vertices && neon.TriangleCount == neon.Tube.Triangles && neon.ReturnVertices == neon.Tube.Vertices && neon.Tube.Vertices == neon.Tube.Points * NeonTube.Sides, "the mesh counts its tube");
            Program.Check(neon.Bytes > GlyphStyles.Outline(square, 0.1f, Flat).Bytes, "and its memory");

            // The tube on a board: its front at the face plane, a diameter deep, closed, facing out.
            var b = new MeshBuilder();
            const float Mpe = 0.4f, X = 0.3f, Y = 0.2f, Front = 1.5f;
            neon.AddTo(b, null, Mpe, X, Y, 0f, Front, true);
            float r = 0.05f * Mpe;
            Program.Check(b.VertexCount == neon.Tube.Vertices && b.TriangleCount == neon.Tube.Triangles, "the tube's vertices and triangles: " + b.VertexCount + ", " + b.TriangleCount);
            Program.Near(b.Positions.Max(p => p.Z), Front, 1e-5f, "its front is the face plane");
            Program.Near(b.Positions.Min(p => p.Z), Front - 2f * r, 1e-5f, "its back is a diameter behind");
            // The vertices are mirrored in x as every vertex of a board is: the tube's outside is the letter's edge.
            Program.Near(-b.Positions.Min(p => p.X), X + 1f * Mpe, 2e-3f, "the outside reaches the letter's edge on one side: " + b.Positions.Min(p => p.X));
            Program.Near(-b.Positions.Max(p => p.X), X + 0f * Mpe, 2e-3f, "and on the other");
            Program.Near(b.Positions.Max(p => p.Y), Y + 1f * Mpe, 2e-3f, "and above");
            Program.Near(b.Positions.Min(p => p.Y), Y + 0f * Mpe, 2e-3f, "and below");

            // Closed: every edge of the triangles has two triangles.
            var edges = new Dictionary<(int, int), int>();
            for (int i = 0; i < b.Indices.Count; i += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    int p = b.Indices[i + k], q = b.Indices[i + (k + 1) % 3];
                    var key = p < q ? (p, q) : (q, p);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }

            Program.Check(edges.Values.All(n => n == 2), "every edge of the tube is shared by two triangles: " + edges.Values.Count(n => n != 2) + " that are not");
            Program.Check(b.Normals.All(n => Math.Abs(n.Length - 1f) < 1e-3f), "unit normals");
            bool facing = true;
            for (int i = 0; i < b.Indices.Count; i += 3)
            {
                Vec3 geometric = MeshBuilder.Cross(b.Positions[b.Indices[i + 1]] - b.Positions[b.Indices[i]], b.Positions[b.Indices[i + 2]] - b.Positions[b.Indices[i]]);
                Vec3 mean = b.Normals[b.Indices[i]] + b.Normals[b.Indices[i + 1]] + b.Normals[b.Indices[i + 2]];
                facing &= Vec3.Dot(geometric, mean) > 0f;
            }

            Program.Check(facing, "every triangle faces the way its vertices' normals do");

            // Strokes thinner than the tube: no line to lay it on, the flat band.
            GlyphMesh strip = Mesh(Rect(0, 0, 0.03f, 1f));
            GlyphMesh flat = GlyphStyles.Neon(strip, 0.02f, Flat);
            Program.Check(flat.Tube == null && flat.Loops.Count == 1, "a stroke thinner than the tube is a flat band, not lost");
            GlyphMesh held = GlyphStyles.Neon(square, 0.004f, Flat, 0.1f);
            Program.Near(held.Tube.Radius, 0.05f, 1e-6f, "a tube too thin for a stud has the radius a stud needs");
            Program.Check(GlyphStyles.Neon(null, 0.05f, Flat) == null && ReferenceEquals(GlyphStyles.Neon(square, 0f, Flat), square), "no mesh, no tube");
            // A ring: a line along the outside and one round the hole.
            GlyphMesh ring = Mesh(Rect(0, 0, 1, 1), Rect(0.3f, 0.3f, 0.7f, 0.7f, true));
            GlyphMesh two = GlyphStyles.Neon(ring, 0.03f, Flat);
            Program.Check(two.Tube.Centre.Count == 2, "a ring has two lines");
        }

        private static void RealGlyphs()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            float minBand = TextStyles.MinBand(0.3f, TextStyles.Art(TextStyles.Outline, false));
            var clock = Stopwatch.StartNew();
            int total = 0, bands = 0, solid = 0, tubes = 0, flatNeon = 0, points = 0, tubePoints = 0;
            string why = "";
            for (int c = 0x4E00; c < 0x4E00 + 400; c++)
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                if (mesh == null || !mesh.IsSound(out _) || GlyphStyles.Crosses(mesh.Loops))
                    continue;
                total++;
                GlyphMesh band = GlyphStyles.Outline(mesh, TextStyles.OutlineWidth, TextGlyphs.BucketFlatness(bucket), minBand);
                if (ReferenceEquals(band, mesh))
                    solid++;
                else
                    bands++;
                bool sound = band.IsSound(out why) && !GlyphStyles.Crosses(band.Loops) && band.Area <= mesh.Area + 1e-5f;
                if (!sound)
                    Program.Check(false, "U+" + c.ToString("X") + " outline: " + why);
                points += band.LoopPoints;
                GlyphMesh neon = GlyphStyles.Neon(mesh, TextStyles.NeonRadius, TextGlyphs.BucketFlatness(bucket), minBand);
                if (neon.Tube != null)
                {
                    tubes++;
                    tubePoints += neon.Tube.Points;
                    if (neon.Tube.Centre.Any(l => l.Count < 3) || !neon.IsSound(out why))
                        Program.Check(false, "U+" + c.ToString("X") + " neon: " + why);
                }
                else
                {
                    flatNeon++;
                }
            }

            Console.WriteLine($"  hollow and neon, {total} characters in {clock.ElapsedMilliseconds} ms: {bands} bands and {solid} left whole; {tubes} tubes ({tubePoints / Math.Max(1, tubes)} points of line each against {points / Math.Max(1, total)} of band), {flatNeon} flat");
            Program.Check(total > 350 && bands > total * 9 / 10, "nearly every character has a hollow: " + bands + " of " + total);
            Program.Check(tubes > total * 9 / 10, "and a tube: " + tubes + " of " + total);

            foreach (char c in "口小永国一人大0ABO的目")
            {
                GlyphMesh mesh = TextGlyphs.Build(font, c, bucket);
                if (mesh == null)
                    continue;
                GlyphMesh band = GlyphStyles.Outline(mesh, TextStyles.OutlineWidth, TextGlyphs.BucketFlatness(bucket), minBand);
                Program.Check(band.Area < mesh.Area && band.Loops.Count >= mesh.Loops.Count, c + ": a band has less ink than the letter, and at least its loops: " + mesh.Area + " to " + band.Area);
            }

            GlyphMesh kou = TextGlyphs.Build(font, '口', bucket);
            GlyphMesh kouBand = GlyphStyles.Outline(kou, TextStyles.OutlineWidth, TextGlyphs.BucketFlatness(bucket), minBand);
            Program.Check(kou.Loops.Count == 2 && kouBand.Loops.Count == 4, "口 is two rings, a band along the outside and a band along the hole: " + kouBand.Loops.Count);
        }

        private static TextPlanner Planner(GlyphCache cache) => new TextPlanner(id => id == TextFonts.DefaultId ? TextLayoutTests.BundledFont() : null, cache);

        private static TextPlanJob Job(string text, TextArt art, float height) =>
            new TextPlanJob(text, TextFonts.DefaultId, height, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(height), art);

        private static void Planned()
        {
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            TextPlanner planner = Planner(cache);
            const string Text = "永国大小\n口人一目";
            foreach (float height in new[] { 0.15f, 0.3f, 0.6f, 1.5f })
            {
                TextPlanResult plain = planner.Plan(Job(Text, TextArt.Plain, height));
                foreach (string style in new[] { TextStyles.Outline, TextStyles.Neon })
                {
                    TextArt art = TextStyles.Art(style, false);
                    TextPlanResult plan = planner.Plan(Job(Text, art, height));
                    Program.Check(plan.Meshes.All(m => m != null) && plan.Frame.Islands.Count >= plain.Frame.Islands.Count, $"{style} at {height} m: every letter has its mesh");
                    Program.Check(plan.Frame.EffectiveArea < plain.Frame.EffectiveArea, $"{style} at {height} m: the ink is less than a solid letter's: {plan.Frame.EffectiveArea} against {plain.Frame.EffectiveArea}");
                    Program.Check(plan.Frame.FloatingIslands == 0, $"{style} at {height} m: nothing is left floating ({plan.Frame.FloatingIslands})");
                    Program.Check(plan.Frame.Studs.Count >= 8 && plan.Frame.Studs.All(s => plan.Frame.Islands[s.Island].Contains(s.At.X, s.At.Y)), $"{style} at {height} m: every stud is in the band or the tube's footprint ({plan.Frame.Studs.Count})");
                    if (style == TextStyles.Neon)
                        Program.Check(plan.Meshes.All(m => m.Tube != null && Math.Abs(m.Tube.Radius - TextStyles.TubeRadius(height, art)) < 1e-6f), $"{style} at {height} m: every letter has its tube, a radius that a stud can hold");
                    else
                        Program.Check(plan.Meshes.All(m => m.Tube == null), $"{style} at {height} m: no tube");
                }
            }

            // The plan's vertices are the tubes' for a neon text, and a band's face and rim for a hollow one.
            TextPlanResult neonPlan = planner.Plan(Job(Text, TextStyles.Art(TextStyles.Neon, false), 0.3f)), hollowPlan = planner.Plan(Job(Text, TextStyles.Art(TextStyles.Outline, false), 0.3f));
            Program.Check(TextPlans.ReturnVertices(neonPlan) == neonPlan.Meshes.Sum(m => m.Tube.Vertices) && TextPlans.ReturnVertices(hollowPlan) == hollowPlan.Meshes.Sum(m => m.FaceVertices + m.RimVertices), "the vertices a text must keep under one mesh");

            // A band thinner than a stud can hold is made as wide as one needs, by the planner too: a glyph is the band of at least that width, and nothing is left floating.
            const float Thin = 0.005f;
            TextArt thin = TextArt.Create(false, 0f, 0f, TextEffect.Outline, Thin);
            TextPlanResult thinPlan = planner.Plan(Job(Text, thin, 0.3f));
            FontOutlines face = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            GlyphMesh baseGlyph = TextGlyphs.Build(face, '永', bucket);
            float flatness = TextGlyphs.BucketFlatness(bucket), least = TextStyles.MinBand(0.3f, thin);
            Program.Check(Math.Abs(thinPlan.Meshes[0].Area - GlyphStyles.Outline(baseGlyph, Thin, flatness, least).Area) < 1e-6f && Math.Abs(thinPlan.Meshes[0].Area - GlyphStyles.Outline(baseGlyph, Thin, flatness).Area) > 1e-4f && thinPlan.Frame.FloatingIslands == 0, "a band thinner than a stud holds is as wide as one needs, in the plan: " + thinPlan.Meshes[0].Area);

            // Dressed and kept: planned again the same glyphs, and the plain ones untouched.
            TextArt neonArt = TextStyles.Art(TextStyles.Neon, false);
            TextPlanResult a = planner.Plan(Job(Text, neonArt, 0.3f)), b = planner.Plan(Job(Text, neonArt, 0.3f));
            Program.Check(a.Meshes.Zip(b.Meshes, ReferenceEquals).All(x => x), "planned again, the same glyphs");
            Program.Check(!ReferenceEquals(planner.Plan(Job(Text, TextStyles.Art(TextStyles.Outline, false), 0.3f)).Meshes[0], a.Meshes[0]), "a hollow glyph and a neon one are two");

            // The heaviest text a neon tube can be: 36 characters; the plan's vertices stay under one mesh or it is cut coarser.
            string heavy = string.Concat(Enumerable.Repeat("口永国目大", 7)).Substring(0, 12) + "\n" + string.Concat(Enumerable.Repeat("田里重量", 3)) + "\n" + string.Concat(Enumerable.Repeat("鬱驫龍", 4));
            TextPlans.Clear();
            TextPlanResult many = TextPlans.Get(Job(heavy, neonArt, 0.3f));
            Program.Check(many.Vertices > 0 && (many.Vertices <= TextPlans.Limit || many.TooHeavy || many.Coarsened > 0), "a heavy neon text is within a mesh, cut coarser, or drawn as bars: " + many.Vertices + " vertices, coarsened " + many.Coarsened);
            Console.WriteLine($"  36 heavy characters as neon: {many.Vertices} vertices, coarsened {many.Coarsened}, too heavy {many.TooHeavy}");
        }

        private static void Combined()
        {
            // A leaning hollow letter is the band of the leaning letter: the lean comes first, then the hollow.
            FontOutlines font = TextLayoutTests.BundledFont();
            int bucket = TextGlyphs.Bucket(TextPlans.RelativeFlatness(0.3f));
            GlyphMesh mesh = TextGlyphs.Build(font, '永', bucket);
            const float Pivot = 0.3f, Slant = 0.2126f;
            float flat = TextGlyphs.BucketFlatness(bucket);
            GlyphMesh both = GlyphStyles.Apply(mesh, TextArt.Create(false, 0f, Slant, TextEffect.Outline, 0.03f), Pivot, flat, 0.026f);
            GlyphMesh expected = GlyphStyles.Outline(GlyphStyles.Shear(mesh, Slant, Pivot), 0.03f, flat, 0.026f);
            Program.Check(both.Loops.Count == expected.Loops.Count && Math.Abs(both.Area - expected.Area) < 1e-5f && Math.Abs(both.MaxX - expected.MaxX) < 1e-5f && both.Tube == null, "a leaning hollow letter is the band of the leaning letter");
            GlyphMesh bold = GlyphStyles.Apply(mesh, TextArt.Create(false, 0.02f, 0f, TextEffect.Neon, 0.02f), Pivot, flat, 0.026f);
            GlyphMesh boldNeon = GlyphStyles.Neon(GlyphStyles.Offset(mesh, 0.02f, flat, out _), 0.02f, flat, 0.026f);
            Program.Check(bold.Tube != null && Math.Abs(bold.Area - boldNeon.Area) < 1e-5f && bold.Tube.Points == boldNeon.Tube.Points, "a bold neon letter is the tube along the bold letter");
        }

        private static void Built()
        {
            GuideTemplate t = TextBoxes.Template;
            TextPlans.Clear();
            SignInstance hollow = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=outline").With("text", "口永").ToString());
            SignParts parts = BuildOne(hollow);
            TextBoard text = hollow.Board.Text;
            MeshBuilder glow = parts.Solids[(int)text.Glow], back = parts.Solids[(int)text.Return];
            Program.Check(!parts.Provisional && glow != null && glow.VertexCount > 50 && back != null && back.VertexCount > glow.VertexCount, "a hollow letter has a face of bands and a return: " + glow?.VertexCount + ", " + back?.VertexCount);
            SignInstance solid = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=plain").With("text", "口永").ToString());

            SignInstance neon = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), t.Parse("style=neon").With("text", "口永").ToString());
            SignParts tubes = BuildOne(neon);
            TextBoard nt = neon.Board.Text;
            MeshBuilder glass = tubes.Solids[(int)nt.Glow];
            Program.Check(glass != null && glass.VertexCount > 100 && (tubes.Solids[(int)nt.Return] == null || tubes.Solids[(int)nt.Return].VertexCount == 0), "a neon letter is its glass alone: no return");
            float face = SignBuilder.FacePlane(tubes.Layout, neon);
            float diameter = 2f * TextStyles.TubeRadius(nt.Job.LetterHeight, nt.Job.Art) * nt.Layout.MetresPerEm;
            Program.Near(nt.Depth, diameter, 1e-6f, "the board's depth is the tube's diameter, whatever the depth field says: " + nt.Depth);
            Program.Near(glass.Positions.Max(p => p.Z), face, 1e-4f, "the tube's front is the face plane");
            Program.Near(glass.Positions.Min(p => p.Z), face - diameter, 1e-4f, "its back is a diameter behind it");
            // The studs run from the support's plane to the tube's back.
            FramePlan frame = TextPlans.Peek(nt.Job).Frame;
            float reachTop = tubes.Metal.Positions.Where(p => p.Z > tubes.Layout.BackZ + 0.0005f).Max(p => p.Z);
            Program.Check(frame.Studs.Count >= 4 && reachTop >= face - diameter - 1e-4f && reachTop <= face - diameter + 0.006f, "the studs end in the tube's back: " + reachTop + " against " + (face - diameter));
            Program.Near(face - tubes.Layout.BackZ, nt.Reach, 1e-4f, "and the board's face is Reach in front of the support");

            // On a wall and in the far tile (bars), and the fields' rows.
            Program.Check(t.FieldOf("ret").Visible(t.Parse("style=outline")) && !t.FieldOf("ret").Visible(t.Parse("style=neon")) && t.FieldOf("depth").Visible(t.Parse("style=outline")) && !t.FieldOf("depth").Visible(t.Parse("style=neon")), "a neon letter has no return colour and no depth to set");
            Program.Check(t.FieldOf("style").Choices.Select(c => c.Value).SequenceEqual(new[] { "plain", "bold", "light", "italic", "bolditalic", "outline", "neon", "twotone" }), "the styles listed");
            Program.Check(TextBoxes.JobOf(t.Parse("style=outline"), out _).Art.Effect == TextEffect.Outline && TextBoxes.JobOf(t.Parse("style=neon"), out _).Art.Effect == TextEffect.Neon, "the job carries the effect");
        }

        private static SignParts BuildOne(SignInstance instance)
        {
            SupportDef support = SupportPlanner.Auto(instance.Def);
            return SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, support, 0f, SupportDetail.Full, default);
        }
    }
}
