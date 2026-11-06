using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The green W-beam (0.12.78): the galvanised guardrail is built as it always was (the hashes below were taken from the build before the colour field existed), the painted beam surfaces, and a
    /// green guardrail's parts, ends, panels and materials.
    /// </summary>
    internal static class GreenBeamTests
    {
        public static void RunAll()
        {
            Program.Run("green beam: the galvanised guardrail is built as before", Regression);
            Program.Run("green beam: the painted beam surfaces", PaintedSurfaces);
            Program.Run("green beam: the template's colour field and the kinds' ids", ColourField);
            Program.Run("green beam: a green guardrail has the galvanised one's shape in green", GreenShape);
            Program.Run("green beam: posts, ends and panels", PostsEndsPanels);
            Program.Run("green beam: materials by tier and the picture", MaterialsAndPicture);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

        private static List<Vec3> Bend()
        {
            var bend = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
            {
                double a = i * 0.05;
                bend.Add(P((float)(30.0 * (1.0 - Math.Cos(a))), (float)(30.0 * Math.Sin(a)), i * 0.15f));
            }

            return bend;
        }

        private static MarkingPlan PlanOf(string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.WBeamId);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), RunPath.Encode(line, out _), phase, total));
        }

        /// <summary>FNV-1a over the bits of every vertex, normal, coordinate and index, and each part's solid and detail: a change of one bit anywhere changes it.</summary>
        private sealed class Fnv
        {
            private ulong m_Hash = 14695981039346656037UL;

            public void Add(int value)
            {
                for (int i = 0; i < 4; i++)
                {
                    m_Hash ^= (uint)(value >> (i * 8)) & 0xFFu;
                    m_Hash *= 1099511628211UL;
                }
            }

            public void Add(float value) => Add(BitConverter.SingleToInt32Bits(value));

            public void AddVertex(MeshBuilder mesh, int i)
            {
                Add(mesh.Positions[i].X);
                Add(mesh.Positions[i].Y);
                Add(mesh.Positions[i].Z);
                Add(mesh.Normals[i].X);
                Add(mesh.Normals[i].Y);
                Add(mesh.Normals[i].Z);
                Add(mesh.U[i]);
                Add(mesh.V[i]);
            }

            public void Add(MeshBuilder mesh)
            {
                Add(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                    AddVertex(mesh, i);
                Add(mesh.Indices.Count);
                foreach (int index in mesh.Indices)
                    Add(index);
            }

            public string Text => m_Hash.ToString("x16");

            public ulong Value => m_Hash;
        }

        private static string HashOf(MarkingPlan plan)
        {
            var h = new Fnv();
            h.Add(plan.Parts.Count);
            foreach (MarkingPart part in plan.Parts)
            {
                h.Add(part.Solid.HasValue ? (int)part.Solid.Value : -1);
                h.Add((int)part.Detail);
                h.Add(part.Mesh);
            }

            return h.Text;
        }

        private static string HashOf(List<MarkingShape> shapes)
        {
            var h = new Fnv();
            h.Add(shapes.Count);
            foreach (MarkingShape shape in shapes)
            {
                h.Add(shape.Solid.HasValue ? (int)shape.Solid.Value : -1);
                h.Add((int)shape.Detail);
                h.Add(shape.Mesh);
            }

            return h.Text;
        }

        /// <summary>
        /// The triangles of a mesh as a set: the sum of each triangle's own hash over the position and normal of its three corners in order (no coordinates: the posts of a green guardrail lie in the
        /// painted tile, the galvanised ones in the galvanised). Only the triangles that are drawn count: DetailUv.Ensure leaves vertices no triangle uses and appends new ones, so how the vertex list is
        /// laid out says nothing.
        /// </summary>
        private static ulong Cloud(MeshBuilder mesh, out int triangles)
        {
            ulong sum = 0UL;
            triangles = mesh.Indices.Count / 3;
            for (int t = 0; t < triangles; t++)
            {
                var h = new Fnv();
                for (int k = 0; k < 3; k++)
                {
                    int i = mesh.Indices[t * 3 + k];
                    h.Add(mesh.Positions[i].X);
                    h.Add(mesh.Positions[i].Y);
                    h.Add(mesh.Positions[i].Z);
                    h.Add(mesh.Normals[i].X);
                    h.Add(mesh.Normals[i].Y);
                    h.Add(mesh.Normals[i].Z);
                }

                sum += h.Value;
            }

            return sum;
        }

        private static ulong Cloud(MarkingPlan plan, out int triangles)
        {
            ulong sum = 0UL;
            triangles = 0;
            foreach (MarkingPart part in plan.Parts)
            {
                sum += Cloud(part.Mesh, out int n);
                triangles += n;
            }

            return sum;
        }

        private static string HashOf(MeshBuilder mesh)
        {
            var h = new Fnv();
            h.Add(mesh);
            return h.Text;
        }

        private static MarkingPart Find(MarkingPlan plan, SolidColour solid, DetailKind detail) => plan.Parts.Find(p => p.Solid == solid && p.Detail == detail);

        // ------------------------------------------------------------------ the galvanised guardrail as it was

        /// <summary>The settings a plan case is built with, as the template had them before the colour: each case's line and the run's phase and whole length.</summary>
        private static readonly (string Name, string Settings, int Line, float Phase, float Total)[] kPlanCases =
        {
            ("default", "", 20, 0f, 0f),
            ("right, no ends", "where=r;ends=none", 20, 0f, 0f),
            ("dense posts", "where=r;post=2;ends=none", 20, 0f, 0f),
            ("median", "where=m;ends=none", 8, 0f, 0f),
            ("left", "where=l;ends=none", 8, 0f, 0f),
            ("three waves", "wave=3;ends=none", 8, 0f, 0f),
            ("set back", "where=r;off=50;ends=none", 8, 0f, 0f),
            ("round ends", "ends=round", 20, 0f, 0f),
            ("flared ends", "ends=flare", 40, 0f, 0f),
            ("auto ends", "where=r", 40, 0f, 0f),
            ("auto ends, left", "where=l", 40, 0f, 0f),
            ("median, round ends", "where=m;ends=round", 12, 0f, 0f),
            ("median with panels", "where=m;glare=1", 8, 0f, 0f),
            ("median with panels, three waves, 50 cm", "where=m;glare=1;glaregap=50;wave=3", 8, 0f, 0f),
            ("middle piece", "where=r", 10, 10f, 40f),
            ("first piece", "where=r;ends=none", 10, 0f, 20f),
            ("second piece", "where=r;ends=none", 10, 10f, 20f),
            ("slope and bend", "where=r", -1, 0f, 0f),
            ("slope and bend, dense, three waves, median", "where=m;post=2;wave=3;ends=round", -1, 0f, 0f)
        };

        private static List<Vec3> LineOf(int length) => length < 0 ? Bend() : Straight(length);

        private static readonly Dictionary<string, string> kGolden = new Dictionary<string, string>
        {
            { "default", "1579c8511feef03d" },
            { "right, no ends", "d6fc5facd8e4d653" },
            { "dense posts", "af4388e83b0605a3" },
            { "median", "56a38c4198cff53d" },
            { "left", "0515e4f600b93c25" },
            { "three waves", "5e189bb9c4250f2b" },
            { "set back", "3fce0c594140b93a" },
            { "round ends", "73b1da82a22f01d8" },
            { "flared ends", "2f80876eee5a324f" },
            { "auto ends", "54a018e3357caf91" },
            { "auto ends, left", "d2753accb32ac5a1" },
            { "median, round ends", "01557eaff606caff" },
            { "median with panels", "b7f146edf321d433" },
            { "median with panels, three waves, 50 cm", "62d40d7c181cb60d" },
            { "middle piece", "8031ff53931183b2" },
            { "first piece", "3e5606e1c1455621" },
            { "second piece", "8031ff53931183b2" },
            { "slope and bend", "0d4b8fbb2e6e7365" },
            { "slope and bend, dense, three waves, median", "c17a7dc1263ff419" },
            { "direct, short form", "41f4eada49de8ee7" },
            { "direct, ends form", "61d285c6e9497a77" },
            { "direct, own ends", "426e9ed1e8b939b9" },
            { "movable barrier", "9e6d2c35f69cadeb" }
        };

        private static void Regression()
        {
            // Said in the settings or not: the colour's default is the galvanised guardrail, bit for bit.
            foreach (bool said in new[] { false, true })
            {
                string how = said ? " (colour=metal said)" : "";
                foreach ((string name, string settings, int line, float phase, float total) in kPlanCases)
                {
                    string hash = HashOf(PlanOf(said ? (settings == "" ? "colour=metal" : settings + ";colour=metal") : settings, LineOf(line), phase, total));
                    Program.Check(kGolden.TryGetValue(name, out string expected) && expected == hash, $"green beam: {name}{how} is built as before: {hash} against {expected}");
                }

                var direct = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("direct, short form", HashOf(said ? Barriers.WBeam(Straight(16f), false, 4f, 1, false, 0f, true, true, BeamEnd.None, 0f, SolidColour.Metal)
                        : Barriers.WBeam(Straight(16f), false, 4f, 1, false, 0f, true, true))),
                    new KeyValuePair<string, string>("direct, ends form", HashOf(said ? Barriers.WBeam(Straight(30f), true, 2f, -1, false, 3f, true, true, BeamEnd.Auto, 60f, SolidColour.Metal)
                        : Barriers.WBeam(Straight(30f), true, 2f, -1, false, 3f, true, true, BeamEnd.Auto, 60f))),
                    new KeyValuePair<string, string>("direct, own ends", HashOf(said ? Barriers.WBeam(Straight(30f), false, 4f, 1, false, 0f, true, true, BeamEnd.Round, BeamEnd.Flare, 30f, SolidColour.Metal)
                        : Barriers.WBeam(Straight(30f), false, 4f, 1, false, 0f, true, true, BeamEnd.Round, BeamEnd.Flare, 30f)))
                };
                foreach (KeyValuePair<string, string> c in direct)
                    Program.Check(kGolden[c.Key] == c.Value, $"green beam: {c.Key}{how} is built as before: {c.Value} against {kGolden[c.Key]}");
            }

            string movable = HashOf(Barriers.MovableBarrier(Straight(12f), 0f, true, true));
            Program.Check(kGolden["movable barrier"] == movable, "green beam: the movable barrier is built as before: " + movable);

            // A spec saved before the field existed has no colour in it: the field's default fills it in.
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.WBeamId);
            string old = "wave=2;where=r;post=4;ends=auto;glare=0;glaregap=80;off=0";
            GuideSpec parsed = t.Parse(old);
            Program.Check(parsed.Get("colour") == "metal", "green beam: a spec from before the field reads as galvanised: " + parsed.Get("colour"));
            MarkingPlan fromOld = MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, parsed.ToString(), RunPath.Encode(Straight(20f), out _), 0f, 0f));
            Program.Check(HashOf(fromOld) == kGolden["default"], "green beam: and builds as the default does");
        }

        // ------------------------------------------------------------------ the painted surfaces

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(a[i]) != BitConverter.SingleToInt32Bits(b[i]))
                    return false;
            }

            return true;
        }

        private static float ReliefAt(DetailSurface s, float u, float v)
        {
            int i = Math.Max(0, Math.Min(s.Width - 1, (int)(u / s.SizeU * s.Width))), j = Math.Max(0, Math.Min(s.Height - 1, (int)(v / s.SizeV * s.Height)));
            return s.Relief[j * s.Width + i];
        }

        /// <summary>The largest relief (m) between two lengths along the tile, across the whole width: a stretch with no bolt on it.</summary>
        private static float QuietMax(DetailSurface s, float v0, float v1)
        {
            float worst = 0f;
            for (int j = (int)(v0 / s.SizeV * s.Height); j < (int)(v1 / s.SizeV * s.Height); j++)
            {
                for (int i = 0; i < s.Width; i++)
                    worst = Math.Max(worst, Math.Abs(s.Relief[j * s.Width + i]));
            }

            return worst;
        }

        private static void PaintedSurfaces()
        {
            float width = DetailSurfaces.ProfileLength(Barriers.BeamProfile(false));
            foreach ((DetailKind painted, DetailKind plain, bool dense) in new[] { (DetailKind.WBeam4Painted, DetailKind.WBeam4, false), (DetailKind.WBeam2Painted, DetailKind.WBeam2, true) })
            {
                string who = "green beam: " + painted;
                DetailSurface p = DetailSurfaces.Make(painted), g = DetailSurfaces.Make(plain);
                Program.Check(p != null && g != null, who + " bakes");
                if (p == null || g == null)
                    continue;

                // The same tile and texture size as the galvanised beam, a sheet without holes.
                Program.Check(p.Width == g.Width && p.Height == g.Height && p.SizeU == g.SizeU && p.SizeV == g.SizeV, $"{who}: the galvanised beam's texture size and tile ({p.Width} x {p.Height})");
                Program.Check(DetailSurfaces.TileOf(painted) == DetailSurfaces.TileOf(plain) && Math.Abs(p.SizeU - width) < 1e-5f && p.SizeV == DetailSurfaces.BeamPeriod, who + ": the beam's tile");
                Program.Check(p.Alpha == null && !DetailSurfaces.HasHoles(painted) && p.Kind == painted, who + ": solid, no holes");

                // Every texel finite; metal 0.1, no coat, smoothness 0.55 to 0.65 with a small grain.
                bool finite = true;
                float metalLo = float.MaxValue, metalHi = float.MinValue, smoothLo = float.MaxValue, smoothHi = float.MinValue, coatHi = 0f, shadeLo = float.MaxValue;
                for (int k = 0; k < p.Relief.Length; k++)
                {
                    finite &= Finite(p.Relief[k]) && Finite(p.Shade[k]) && Finite(p.Metal[k]) && Finite(p.Coat[k]) && Finite(p.Smooth[k])
                        && Finite(p.Normals[k * 3]) && Finite(p.Normals[k * 3 + 1]) && Finite(p.Normals[k * 3 + 2]);
                    metalLo = Math.Min(metalLo, p.Metal[k]);
                    metalHi = Math.Max(metalHi, p.Metal[k]);
                    smoothLo = Math.Min(smoothLo, p.Smooth[k]);
                    smoothHi = Math.Max(smoothHi, p.Smooth[k]);
                    coatHi = Math.Max(coatHi, p.Coat[k]);
                    shadeLo = Math.Min(shadeLo, p.Shade[k]);
                }

                Program.Check(finite, who + ": no NaN or infinity in any map");
                Program.Check(metalLo >= 0.09f && metalHi <= 0.11f, $"{who}: metal about 0.1: {metalLo:0.000} .. {metalHi:0.000}");
                Program.Check(g.Metal.Min() > 0.8f, who + ": the galvanised beam is the metal one (0.85), so the painted one is not it");
                Program.Check(coatHi == 0f, who + ": no clear coat");
                Program.Check(smoothLo >= 0.55f - 1e-6f && smoothHi <= 0.65f + 1e-6f, $"{who}: smoothness 0.55 to 0.65: {smoothLo:0.000} .. {smoothHi:0.000}");
                Program.Check(smoothHi - smoothLo > 0.02f, $"{who}: with a small grain: {smoothHi - smoothLo:0.000}");
                Program.Check(shadeLo > 0.5f && shadeLo <= 1f, who + ": the shade is the galvanised beam's cavity darkening");

                // The same relief and bolts as the galvanised beam: the two differ only by the spangle and the orange peel (a few hundredths of a millimetre).
                float apart = 0f;
                for (int k = 0; k < p.Relief.Length; k++)
                    apart = Math.Max(apart, Math.Abs(p.Relief[k] - g.Relief[k]));
                Program.Check(apart < 3e-4f, $"{who}: the galvanised beam's relief and bolts, to {apart * 1000f:0.000} mm");
                Program.Check(ReliefAt(p, width * 0.5f, 0.001f) > 0.010f, who + ": the post bolt at the splice");
                Program.Check(ReliefAt(p, width * 0.22f, 0.051f) > 0.010f && ReliefAt(p, width * 0.78f, 3.949f) > 0.010f, who + ": the splice bolts either side of the joint");
                Program.Check(Math.Abs(ReliefAt(p, width * 0.4f, 0.1f) - 0.003f) < 0.001f, who + ": the lap is the beam's thickness proud");
                Program.Check(dense ? ReliefAt(p, width * 0.5f, 2f) > 0.008f : ReliefAt(p, width * 0.5f, 2f) < 0.001f, who + ": a post bolt half way with posts every 2 m, none with posts every 4 m");

                // No spangle: away from the bolts the painted relief is the orange peel (at most 0.03 mm), the galvanised one the spangle (about 0.1 mm).
                float quietPainted = QuietMax(p, 0.5f, 1.5f), quietPlain = QuietMax(g, 0.5f, 1.5f);
                Program.Check(quietPainted < 4e-5f && quietPlain > 5e-5f, $"{who}: no spangle: {quietPainted * 1000f:0.000} mm against the galvanised beam's {quietPlain * 1000f:0.000} mm");

                // The same every time.
                DetailSurface again = DetailSurfaces.Make(painted);
                Program.Check(Same(again.Relief, p.Relief) && Same(again.Normals, p.Normals) && Same(again.Shade, p.Shade) && Same(again.Metal, p.Metal) && Same(again.Smooth, p.Smooth), who + ": the same every time");

                // The bytes of the maps the game gets.
                DetailPixels.Maps(p, out byte[] normal, out byte[] mask);
                DetailPixels.Maps(g, out byte[] plainNormal, out byte[] plainMask);
                Program.Check(normal.Length == plainNormal.Length && mask.Length == plainMask.Length && mask.Length == p.Width * p.Height * 4, who + ": as many texels as the galvanised beam's maps");
                bool bytes = true;
                for (int k = 0; k < p.Width * p.Height; k++)
                {
                    bytes &= mask[k * 4] >= 23 && mask[k * 4] <= 28 && mask[k * 4 + 1] == 0 && mask[k * 4 + 2] == 0 && mask[k * 4 + 3] >= 140 && mask[k * 4 + 3] <= 166 && normal[k * 4 + 3] == 255;
                }

                Program.Check(bytes, who + ": the mask map's bytes: metal 23 to 28, no coat, smoothness 140 to 166");
                byte[] colour = DetailPixels.Colour(p, 0.30f, 0.65f, 0.45f);
                bool green = colour.Length == DetailPixels.Colour(g, 0.30f, 0.65f, 0.45f).Length;
                for (int k = 0; k < p.Width * p.Height && green; k++)
                    green = colour[k * 4 + 3] == 255 && colour[k * 4 + 1] > colour[k * 4 + 2] && colour[k * 4 + 2] > colour[k * 4];
                Program.Check(green, who + ": the colour map is the colour times the shade, opaque");
            }
        }

        // ------------------------------------------------------------------ the field and the ids

        private static void ColourField()
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.WBeamId);
            GuideField field = t.FieldOf("colour");
            Program.Check(field != null && field.Kind == GuideFieldKind.Choice && field.Default == "metal", "green beam: a colour choice, galvanised by default");
            Program.Check(field != null && string.Join(",", field.Choices.Select(c => c.Value)) == "metal,green", "green beam: the colours offered: galvanised and green (no photo of another)");
            Program.Check(t.Default().Get("colour") == "metal", "green beam: a new guardrail is galvanised");
            Program.Check(t.Default().With("colour", "green").Get("colour") == "green", "green beam: green is accepted");
            Program.Check(t.Default().With("colour", "purple").Get("colour") == "metal", "green beam: a colour the template does not offer is the default");
            Program.Check(t.Default().With("colour", "white").Get("colour") == "metal", "green beam: not a colour the shared parser knows but the template does not offer");

            // Appended after every kind there was; the ids that were saved have not moved.
            Program.Check((int)DetailKind.WBeam4 == 8 && (int)DetailKind.WBeam2 == 9 && (int)DetailKind.TreeGrate == 17 && (int)DetailKind.Brick == 22, "green beam: the ids of the kinds that were there have not moved");
            Program.Check((int)DetailKind.WBeam4Painted > (int)DetailKind.Brick && (int)DetailKind.WBeam2Painted > (int)DetailKind.Brick && DetailKind.WBeam4Painted != DetailKind.WBeam2Painted,
                "green beam: the painted kinds come after the last one there was");
            // The colours after the 33 are the living wall's five blossoms (0.12.78); the guardrail added none: its green is the existing one.
            Program.Check((int)SolidColour.Green == 8 && (int)SolidColour.PoleLightGrey == 33 && (int)SolidColour.BlossomRed == 34 && Enum.GetValues(typeof(SolidColour)).Length == 34 + 5, "green beam: no new solid colour: the green is the existing one");
            foreach (DetailKind kind in new[] { DetailKind.WBeam4Painted, DetailKind.WBeam2Painted })
            {
                string name = ChunkBuilder.DetailMaterial(kind, SolidColour.Green);
                Program.Check(ChunkBuilder.DetailOf(name, out DetailKind k, out SolidColour c) && k == kind && c == SolidColour.Green && !ChunkBuilder.IsMarkingMaterial(name), "green beam: the material name says its kind and colour: " + name);
            }
        }

        // ------------------------------------------------------------------ a green guardrail

        private static void GreenShape()
        {
            foreach ((string name, string settings, int line, float phase, float total) in kPlanCases)
            {
                MarkingPlan galvanised = PlanOf(settings, LineOf(line), phase, total);
                MarkingPlan green = PlanOf(settings == "" ? "colour=green" : settings + ";colour=green", LineOf(line), phase, total);
                ulong a = Cloud(galvanised, out int na), b = Cloud(green, out int nb);
                Program.Check(na > 0 && na == nb && a == b, $"green beam: {name}: the same triangles, with the same normals, in green ({na} and {nb})");
                bool glare = settings.Contains("glare=1");
                bool dense = settings.Contains("post=2");
                DetailKind beam = dense ? DetailKind.WBeam2Painted : DetailKind.WBeam4Painted;
                Program.Check(Find(green, SolidColour.Green, beam) != null && Find(green, SolidColour.Green, DetailKind.Painted) != null, $"green beam: {name}: the beam in its painted kind and the posts painted green");
                Program.Check(!green.Parts.Exists(p => p.Detail == DetailKind.WBeam4 || p.Detail == DetailKind.WBeam2), $"green beam: {name}: no galvanised beam in it");
                // The only galvanised thing in a green guardrail is the anti-glare panels' own rail, which keeps its colour.
                Program.Check(glare ? green.Parts.Exists(p => p.Solid == SolidColour.Metal) : !green.Parts.Exists(p => p.Solid == SolidColour.Metal || p.Detail == DetailKind.Galvanised),
                    $"green beam: {name}: every part is green" + (glare ? " but the panels' rail" : ""));
                if (!glare)
                {
                    // Part by part: the posts and the beam are the galvanised ones' meshes, only their material is another.
                    MarkingPart plainBeam = Find(galvanised, SolidColour.Metal, dense ? DetailKind.WBeam2 : DetailKind.WBeam4), plainPosts = Find(galvanised, SolidColour.Metal, DetailKind.Galvanised);
                    // The beam's tile is the galvanised beam's, so its whole mesh, coordinates and all, is the galvanised one; the posts' coordinates are in the painted tile (0.25 m, not 0.5 m).
                    Program.Check(plainBeam != null && HashOf(Find(green, SolidColour.Green, beam).Mesh) == HashOf(plainBeam.Mesh), $"green beam: {name}: the beam's mesh is the galvanised one's, coordinates too");
                    Program.Check(plainPosts != null && Cloud(Find(green, SolidColour.Green, DetailKind.Painted).Mesh, out _) == Cloud(plainPosts.Mesh, out _), $"green beam: {name}: the posts' triangles are the galvanised ones'");
                    Program.Check(green.Parts.Count == galvanised.Parts.Count && green.Parts.Count == 2, $"green beam: {name}: two parts, the posts and the beam");
                }
            }
        }

        private static void PostsEndsPanels()
        {
            // Posts and their blocks: the shape the posts are in is green, not galvanised.
            List<MarkingShape> shapes = Barriers.WBeam(Straight(20f), false, 4f, 1, false, 0f, true, true, BeamEnd.None, BeamEnd.None, 0f, SolidColour.Green);
            Program.Check(shapes.Count == 2 && shapes.TrueForAll(s => s.Solid == SolidColour.Green), "green beam: both shapes, the posts with their blocks and the beam, are green");
            Program.Check(shapes[1].Detail == DetailKind.WBeam4Painted && shapes[0].Detail == DetailKind.None, "green beam: the beam painted, the posts take the solid's own default (painted)");
            shapes = Barriers.WBeam(Straight(20f), false, 2f, 1, false, 0f, true, true, BeamEnd.None, BeamEnd.None, 0f, SolidColour.Green);
            Program.Check(shapes[1].Detail == DetailKind.WBeam2Painted, "green beam: posts every 2 m: the painted two-metre kind");
            shapes = Barriers.WBeam(Straight(20f), false, 2f, 1, false, 0f, true, true, BeamEnd.None, BeamEnd.None, 0f, SolidColour.Metal);
            Program.Check(shapes[0].Solid == SolidColour.Metal && shapes[1].Solid == SolidColour.Metal && shapes[1].Detail == DetailKind.WBeam2, "green beam: Metal is still the galvanised one");

            // The ends turn and bend as the beam does, so they are its colour and its kind.
            foreach ((string ends, float length, string where) in new[] { ("round", 20f, "r"), ("flare", 40f, "r"), ("auto", 40f, "r"), ("auto", 40f, "l"), ("round", 12f, "m") })
            {
                string settings = "ends=" + ends + ";where=" + where;
                MarkingPlan none = PlanOf("ends=none;where=" + where + ";colour=green", Straight(length)), withEnds = PlanOf(settings + ";colour=green", Straight(length)), galvanised = PlanOf(settings, Straight(length));
                MarkingPart beamNone = Find(none, SolidColour.Green, DetailKind.WBeam4Painted), beam = Find(withEnds, SolidColour.Green, DetailKind.WBeam4Painted), plain = Find(galvanised, SolidColour.Metal, DetailKind.WBeam4);
                Program.Check(beamNone != null && beam != null && plain != null && beam.Mesh.VertexCount > beamNone.Mesh.VertexCount, $"green beam: {settings}: the ends are in the painted beam ({beam?.Mesh.VertexCount} against {beamNone?.Mesh.VertexCount} without)");
                Program.Check(plain != null && beam != null && HashOf(beam.Mesh) == HashOf(plain.Mesh) && withEnds.Parts.TrueForAll(p => p.Solid == SolidColour.Green), $"green beam: {settings}: the ends are the galvanised ones' shape, all green");
            }

            // Anti-glare panels keep their own colour: the panels are the green they were, their rail and uprights stay galvanised, and the panel meshes are the ones they were.
            foreach (string settings in new[] { "where=m;glare=1", "where=m;glare=1;glaregap=50;wave=3;post=2" })
            {
                MarkingPlan galvanised = PlanOf(settings, Straight(8f)), noGlare = PlanOf(settings.Replace("glare=1", "glare=0"), Straight(8f));
                MarkingPlan green = PlanOf(settings + ";colour=green", Straight(8f)), greenNoGlare = PlanOf(settings.Replace("glare=1", "glare=0") + ";colour=green", Straight(8f));
                MarkingPart panels = Find(galvanised, SolidColour.Green, DetailKind.Painted), rail = Find(galvanised, SolidColour.Metal, DetailKind.Galvanised), railNoGlare = Find(noGlare, SolidColour.Metal, DetailKind.Galvanised);
                MarkingPart greenRail = Find(green, SolidColour.Metal, DetailKind.Galvanised), greenPosts = Find(green, SolidColour.Green, DetailKind.Painted), greenPostsNoGlare = Find(greenNoGlare, SolidColour.Green, DetailKind.Painted);
                Program.Check(panels != null && rail != null && railNoGlare != null && greenRail != null && greenPosts != null && greenPostsNoGlare != null, $"green beam: {settings}: panels, rail and posts are there");
                if (panels == null || rail == null || railNoGlare == null || greenRail == null || greenPosts == null || greenPostsNoGlare == null)
                    continue;
                Program.Check(greenRail.Mesh.VertexCount == rail.Mesh.VertexCount - railNoGlare.Mesh.VertexCount, $"green beam: {settings}: the panels' rail and uprights are galvanised and as they were");
                Program.Check(greenPosts.Mesh.VertexCount == greenPostsNoGlare.Mesh.VertexCount + panels.Mesh.VertexCount, $"green beam: {settings}: the panels are drawn in the same green as the posts");
                ulong together = Cloud(greenPosts.Mesh, out int togetherTriangles), postsOnly = Cloud(greenPostsNoGlare.Mesh, out int postTriangles), panelsOnly = Cloud(panels.Mesh, out int panelTriangles);
                Program.Check(together == postsOnly + panelsOnly && togetherTriangles == postTriangles + panelTriangles && panelTriangles > 0, $"green beam: {settings}: the panels are where they were, as they were ({panelTriangles} triangles)");
            }
        }

        /// <summary>The pixels of a picture that are not the road square's colour (the corner's), and how many of them are green (the green channel above the red and the blue).</summary>
        private static int Greenish(byte[] rgba, out int body)
        {
            int green = 0;
            body = 0;
            for (int i = 4; i + 3 < rgba.Length; i += 4)
            {
                if (rgba[i] == rgba[0] && rgba[i + 1] == rgba[1] && rgba[i + 2] == rgba[2])
                    continue;
                body++;
                if (rgba[i + 1] > rgba[i] + 12 && rgba[i + 1] > rgba[i + 2] + 6)
                    green++;
            }

            return green;
        }

        private static void MaterialsAndPicture()
        {
            SignDef def = SignCatalog.ById(MarkingTemplates.WBeamId);
            GuideTemplate t = def.Template;
            string galvanisedBeam = ChunkBuilder.DetailMaterial(DetailKind.WBeam4, SolidColour.Metal), galvanisedPosts = ChunkBuilder.DetailMaterial(DetailKind.Galvanised, SolidColour.Metal);
            string greenBeam = ChunkBuilder.DetailMaterial(DetailKind.WBeam4Painted, SolidColour.Green), greenPosts = ChunkBuilder.DetailMaterial(DetailKind.Painted, SolidColour.Green);
            foreach (string colour in new[] { "metal", "green" })
            {
                string spec = MarkingTemplates.WithRun(t, t.Default().With("colour", colour).With("ends", "none").ToString(), RunPath.Encode(Straight(12f), out _), 0f, 12f);
                var item = new ItemRecord(def.Hash, new Vec3(10f, 5f, 20f), 0f, 0u, spec);
                string[] expected = colour == "green" ? new[] { greenBeam, greenPosts } : new[] { galvanisedBeam, galvanisedPosts };
                // The tiers: the barrier is built whole at every one (only the noise walls have coarser levels), so it is the same material far off; at the horizon it is not built, as before.
                foreach (TileTier tier in new[] { TileTier.Far, TileTier.Mid, TileTier.Near })
                {
                    List<ChunkPart> parts = ChunkBuilder.BuildDetailed(new[] { item }, Vec3.Zero, ChunkDetail.Of(tier), out int skipped);
                    string[] names = parts.Select(p => p.Material).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    Program.Check(skipped == 0 && names.SequenceEqual(expected.OrderBy(n => n, StringComparer.Ordinal)), $"green beam: {colour} at {tier}: the materials are {string.Join(", ", expected)}, got {string.Join(", ", names)}");
                }

                List<ChunkPart> horizon = ChunkBuilder.BuildDetailed(new[] { item }, Vec3.Zero, ChunkDetail.Of(TileTier.Horizon), out _);
                Program.Check(horizon.Count == 0, "green beam: " + colour + " at the horizon: not built, as before");
            }

            // The picture the editor shows follows the colour.
            byte[] plain = MarkingRaster.Render(PlanOf("", Straight(12f)), 128, out int w, out int h), paint = MarkingRaster.Render(PlanOf("colour=green", Straight(12f)), 128, out int pw, out int ph);
            int plainGreen = Greenish(plain, out int plainOpaque), paintGreen = Greenish(paint, out int paintOpaque);
            Program.Check(w == pw && h == ph && plainOpaque > 200 && paintOpaque == plainOpaque, $"green beam: the same picture's shape: {plainOpaque} and {paintOpaque} pixels that are not the road");
            Program.Check(paintGreen * 2 > paintOpaque && plainGreen * 20 < plainOpaque, $"green beam: the picture of a green guardrail is green ({paintGreen} of {paintOpaque} pixels), the galvanised one is not ({plainGreen} of {plainOpaque})");
        }
    }
}
