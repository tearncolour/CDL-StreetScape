using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The paint of a hatched strip cut to the road it lies on (0.12.83, Geo/RunCut.cs, AreaFit.DrapeNear; the owner's picture of 3 Nov: bars out over the water beyond a bridge's edge and on the grass under it).
    /// The roads are made up as BarrierPipelineTests makes them: a deck 12 m up and the ground 2.3 m under it, here 8 m up. The checks are the faults of the picture: paint beyond the edge, paint dropped on
    /// the road under the deck, a paint that is lost where it lies on the road, and the same strip on the road, which must not be touched.
    /// </summary>
    internal static class RunCutTests
    {
        public static void RunAll()
        {
            Program.Run("run heights: every run marking's paint lies at the height of its path, on a ramp as on the flat", FollowsPath);
            Program.Run("run heights: the paint of every run marking on the flat is as 0.12.82 built it, bit for bit", FlatUnchanged);
            Program.Run("run cut: paint across a deck's edge is cut at the edge, at the deck's height", AcrossEdge);
            Program.Run("run cut: paint beyond the deck is left out, whole", Beyond);
            Program.Run("run cut: a road under the deck does not take what the deck's edge leaves, and paint at its own level lies on it", UnderDeck);
            Program.Run("run cut: the piece nearest the paint's own height takes it, not the one nearest the item's", NearestWins);
            Program.Run("run cut: pieces of one surface within 5 cm come in the fit's order, the fillers last", FillerTie);
            Program.Run("run cut: pieces that overlap give the paint once and the seams between pieces lose none", Seams);
            Program.Run("run cut: a kerb's step to a footway is kept, not merged", KerbStep);
            Program.Run("run cut: the cut paint keeps its winding and faces up", Winding);
            Program.Run("run cut: the cut is kept for each mesh", Kept);
            Program.Run("run cut: only the wide markings along a road are cut", Wanted);
            Program.Run("run cut: before the cut a strip's paint beyond the deck lay at the terrain's height or its own", OldHeights);
            Program.Run("run cut: a strip wholly on the deck is not touched, whatever its width, side, fill and length", OnDeck);
            Program.Run("run cut: a strip along the deck's edge is cut at it", StripAtEdge);
            Program.Run("run cut: a strip wholly beyond the deck is left out", StripBeyond);
            Program.Run("run cut: a strip along a ramp stays on it, up and down", Ramp);
            Program.Run("run cut: the tile builder draws the cut paint, and the paint that was not cut as before", Builder);
            Program.Run("run cut: no road read in the box: nothing to cut", NoRoad);
            Program.Run("run cut: every paint part of a plan counts, the solids none", Parts);
            Program.Run("run heights: a mesh follows a line's height between its points and stops at its ends", FollowLineUnit);
        }

        private const float Deck = 8f, Ground = Deck - 2.3f, Under = 2.5f;

        /// <summary>
        /// The faults of the owner's picture, found: the bars of a hatched strip (and of a double yellow line's hatching) were made flat, at the height of the item's first point (AreaIndex's pieces are
        /// emitted at height 0), while the strip's edge lines followed the path: on a road that falls, the bars hung over the water and the grass beside it, on one that climbs they were under the road. Every
        /// run template is built over a path that climbs 10 % and every vertex of its paint must be at the path's height there (the paint is cut by the road to the plane, never by the template).
        /// </summary>
        private static void FollowsPath()
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= 40.001f; z += 2f)
                points.Add(new Vec3(0f, 0.1f * z, z));
            string path = RunPath.Encode(points, out _);
            int templates = 0;
            foreach (GuideTemplate template in MarkingTemplates.All)
            {
                if (!MarkingTemplates.IsRun(template) || !template.Id.StartsWith("mk.", StringComparison.Ordinal) || MarkingTemplates.IsKerbLine(template.Id) || template.Id == "mk.delineator")
                    continue;
                // The templates that spread sideways are asked at their widest as well as by default.
                foreach (string settings in Variants(template))
                {
                    string spec = MarkingTemplates.WithRun(template, template.Parse(settings).ToString(), path, 0f, 0f);
                    MarkingPlan plan = MarkingTemplates.PlanOf(template, spec);
                    float worst = 0f;
                    int vertices = 0;
                    foreach (MarkingPart part in plan.Parts)
                    {
                        if (part.Solid.HasValue)
                            continue;
                        foreach (Vec3 p in part.Mesh.Positions)
                        {
                            worst = Math.Max(worst, Math.Abs(p.Y - 0.1f * Math.Max(0f, Math.Min(40f, p.Z))));
                            vertices++;
                        }
                    }

                    Program.Check(vertices > 0, template.Id + " has paint over the path");
                    Program.Check(worst < 0.05f, $"{template.Id} ({settings}): its paint follows the path's height, at the worst {worst:0.000} m off");
                }

                templates++;
            }

            Program.Check(templates >= 18, "the run templates were all asked: " + templates);
        }

        private static void FlatUnchanged()
        {
            string golden = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "runcut-flat-0.12.82.txt");
            Program.Check(System.IO.File.Exists(golden), "the golden file is there: " + golden);
            if (!System.IO.File.Exists(golden))
                return;
            string[] want = System.IO.File.ReadAllLines(golden, new System.Text.UTF8Encoding(false));
            List<string> have = RunCutPrint.Lines();
            Program.Check(want.Length == have.Count && want.Length >= 60, $"{have.Count} builds against {want.Length} in the golden");
            int differ = 0;
            for (int i = 0; i < Math.Min(want.Length, have.Count); i++)
            {
                if (want[i] != have[i] && differ++ < 5)
                    Program.Check(false, "differs from 0.12.82: " + want[i] + " / " + have[i]);
            }

            Program.Check(differ == 0, "no marking on the flat has moved a vertex: " + differ + " differ");
        }

        private static IEnumerable<string> Variants(GuideTemplate template)
        {
            yield return template.Default().ToString();
            if (template.Id == "mk.strip")
            {
                yield return Strip(1.5f, "r", "chevron");
                yield return Strip(3f, "l", "diagonal");
                yield return Strip(1.5f, "r", "cycle");
            }

            if (template.Id == "mk.line.doubleyellow")
                yield return template.Parse(template.Default().ToString()).With("gap", "100").ToString();
        }

        /// <summary>A band of surface along +Z from z0 to z1 between two x (world), at one height.</summary>
        private static SurfaceStrip Band(float x0, float x1, float z0, float z1, float y, bool elevated = false, bool filler = false)
        {
            var strip = new SurfaceStrip { Elevated = elevated, Filler = filler };
            for (float z = z0; z <= z1 + 0.001f; z += 10f)
                strip.Add(new Vec3(x0, y, Math.Min(z, z1)), new Vec3(x1, y, Math.Min(z, z1)));
            return strip;
        }

        /// <summary>The fit of the strips as the cut reads them, in the frame of an item at origin (the paint in its own frame is cut by it).</summary>
        private static AreaFit FitOf(IList<SurfaceStrip> strips, Vec3 origin, float yaw = 0f, float half = 60f) =>
            AreaFit.ForHeights(strips, origin, yaw, -half, half, -half, half, RunCut.ReadMargin);

        /// <summary>Paint in the frame of the item: a quad x0..x1, z0..z1 at one height, wound as the marking meshes are (facing up).</summary>
        private static MeshBuilder Quad(float x0, float x1, float z0, float z1, float y)
        {
            var mesh = new MeshBuilder();
            Vec3 up = new Vec3(0f, 1f, 0f);
            int a = mesh.Vertex(new Vec3(x0, y, z0), up), b = mesh.Vertex(new Vec3(x1, y, z0), up), c = mesh.Vertex(new Vec3(x1, y, z1), up), d = mesh.Vertex(new Vec3(x0, y, z1), up);
            mesh.QuadFacing(a, b, c, d, up);
            return mesh;
        }

        private static float Area(MeshBuilder mesh) => AreaFitRules.Area(mesh);

        private static (float Min, float Max) Xs(MeshBuilder mesh)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                lo = Math.Min(lo, p.X);
                hi = Math.Max(hi, p.X);
            }

            return (lo, hi);
        }

        private static float MaxHeightError(MeshBuilder mesh, float y)
        {
            float worst = 0f;
            foreach (Vec3 p in mesh.Positions)
                worst = Math.Max(worst, Math.Abs(p.Y - y));
            return worst;
        }

        private static void AcrossEdge()
        {
            // A deck from -6 to 6 at 8 m; the item's frame is at the deck's height at the origin. Paint 3 m wide across the edge at 6: 3 m of it on the deck, 3 m over nothing.
            AreaFit fit = FitOf(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true) }, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(3f, 9f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out float lost);
            Program.Near(Area(cut), 6f, 1e-3f, "3 m by 2 m of the paint is on the deck");
            Program.Near(lost, 6f, 1e-3f, "and 3 m by 2 m is lost");
            (float lo, float hi) = Xs(cut);
            Program.Check(lo > 2.999f && hi < 6.0005f, $"the cut is at the deck's edge: x {lo:0.000} to {hi:0.000}");
            Program.Check(MaxHeightError(cut, 0f) < 1e-4f, "the paint is at the deck's height");

            // Whole on the deck: nothing lost, the same area.
            MeshBuilder on = Quad(-2f, 4f, 10f, 12f, 0f);
            MeshBuilder kept = fit.DrapeNear(on, AreaFit.NearWithin, out float none);
            Program.Check(none < 1e-4f && Math.Abs(Area(kept) - Area(on)) < 1e-3f, "paint wholly on the deck loses nothing: " + none);
        }

        private static void Beyond()
        {
            AreaFit fit = FitOf(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true) }, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(8f, 9.5f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out float lost);
            Program.Check(cut.VertexCount == 0, "paint 2 m beyond the deck's edge is left out: " + cut.VertexCount + " vertices");
            Program.Near(lost, Area(paint), 1e-3f, "all of it is lost");

            var nothing = new AreaFit();
            Program.Near(nothing.DrapeNear(paint, AreaFit.NearWithin, out float all).VertexCount, 0f, 0f, "a fit with no piece draws none");
            Program.Near(all, Area(paint), 1e-3f, "and loses all");
            MeshBuilder none = fit.DrapeNear(null, AreaFit.NearWithin, out float zero);
            Program.Check(none.VertexCount == 0 && zero == 0f, "no paint: nothing");
        }

        private static void UnderDeck()
        {
            // A deck from -6 to 6 at 8 m and, 5.5 m under it, a road from -20 to 20.
            var strips = new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true), Band(-20f, 20f, -30f, 90f, Under) };
            AreaFit fit = FitOf(strips, new Vec3(0f, Deck, 0f));

            // Paint on the deck that reaches over its edge: the part beyond is over the road, 5.5 m down, and is not put on it.
            MeshBuilder onDeck = Quad(3f, 9f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(onDeck, AreaFit.NearWithin, out float lost);
            (float lo, float hi) = Xs(cut);
            Program.Check(hi < 6.0005f && Math.Abs(Area(cut) - 6f) < 1e-3f && Math.Abs(lost - 6f) < 1e-3f, $"paint at the deck's height stops at the edge: x up to {hi:0.000}, {Area(cut):0.00} m2 kept, {lost:0.00} lost");
            Program.Check(MaxHeightError(cut, 0f) < 1e-4f, "and none of it was put 5.5 m down on the road");

            // Paint at the road's own level (laid 5.5 m below the deck's): under the deck, it lies on the road and not on the deck above it.
            MeshBuilder onRoad = Quad(-2f, 10f, 20f, 22f, Under - Deck);
            MeshBuilder low = fit.DrapeNear(onRoad, AreaFit.NearWithin, out float lowLost);
            Program.Check(lowLost < 1e-3f && Math.Abs(Area(low) - Area(onRoad)) < 1e-3f, "paint at the road's level under the deck is wholly on the road: lost " + lowLost);
            Program.Check(MaxHeightError(low, Under - Deck) < 1e-4f, "at the road's height, not the deck's");

            // The window's own origin height is no part of it: the same paint in a fit made at the road's level is cut the same way.
            AreaFit atRoad = FitOf(strips, new Vec3(0f, Under, 0f));
            MeshBuilder again = atRoad.DrapeNear(Quad(3f, 9f, 10f, 12f, Deck - Under), AreaFit.NearWithin, out float lostAgain);
            Program.Check(Math.Abs(Area(again) - 6f) < 1e-3f && Math.Abs(lostAgain - 6f) < 1e-3f, "the deck's paint is cut at the edge in a fit made at the road's level too");
        }

        private static void NearestWins()
        {
            // A ramp that climbs 0.1 per metre over x from -4 to 4 and, at the item's own height at its foot, a road: the paint lies on the ramp (its own height is the ramp's), 0.8 to 1 m over the road,
            // which is within the tolerance too and comes first in the fit's order (it is at the origin's height).
            var ramp = new SurfaceStrip { Elevated = true };
            for (float z = -10f; z <= 130f; z += 10f)
                ramp.Add(new Vec3(-4f, Under + 0.1f * z, z), new Vec3(4f, Under + 0.1f * z, z));
            AreaFit fit = FitOf(new List<SurfaceStrip> { ramp, Band(-30f, 30f, -30f, 150f, Under) }, new Vec3(0f, Under, 0f));
            var paint = new MeshBuilder();
            Vec3 up = new Vec3(0f, 1f, 0f);
            int a = paint.Vertex(new Vec3(-2f, 0.8f, 8f), up), b = paint.Vertex(new Vec3(2f, 0.8f, 8f), up), c = paint.Vertex(new Vec3(2f, 1f, 10f), up), d = paint.Vertex(new Vec3(-2f, 1f, 10f), up);
            paint.QuadFacing(a, b, c, d, up);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out float lost);
            float worst = 0f;
            foreach (Vec3 p in cut.Positions)
                worst = Math.Max(worst, Math.Abs(p.Y - 0.1f * p.Z));
            Program.Check(lost < 1e-3f && cut.VertexCount > 0 && worst < 0.02f, $"the paint is on the ramp, at its height, not on the road under it ({worst * 1000f:0} mm off the ramp, {lost:0.000} lost)");
        }

        private static void FillerTie()
        {
            // The carriageway to x = 6 at 0 and, over the same place and beyond it to 8, a filler (a junction's guessed surface, or a footway) 1 cm higher; paint at the filler's height is within 1 cm
            // of both: the carriageway has it as far as it goes (the fit's order: the fillers last), and the filler only what is left: there is a cut at the carriageway's edge.
            var strips = new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true), Band(-6f, 8f, -30f, 90f, Deck + 0.01f, true, true) };
            AreaFit fit = FitOf(strips, new Vec3(0f, Deck, 0f));
            MeshBuilder cut = fit.DrapeNear(Quad(4f, 7f, 10f, 12f, 0.01f), AreaFit.NearWithin, out float lost);
            int atEdge = 0;
            foreach (Vec3 p in cut.Positions)
            {
                if (Math.Abs(p.X - 6f) < 1e-3f)
                    atEdge++;
            }

            Program.Check(lost < 1e-3f && Math.Abs(Area(cut) - 3f * 2f) < 1e-3f, "the paint is drawn whole, once: " + Area(cut));
            Program.Check(atEdge >= 2, "and the carriageway has what lies over it: it is cut at its edge (" + atEdge + " corners there)");
        }

        private static void Seams()
        {
            // Two bands side by side that overlap by 0.3 m (a segment and a junction's part), and the paint across both.
            var strips = new List<SurfaceStrip> { Band(-6f, 0.15f, -30f, 90f, Deck, true), Band(-0.15f, 6f, -30f, 90f, Deck, true) };
            AreaFit fit = FitOf(strips, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(-3f, 3f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out float lost);
            Program.Check(lost < 1e-3f, "paint across two overlapping bands loses none: " + lost);
            Program.Near(Area(cut), Area(paint), 2e-3f, "and is drawn once: the area is the paint's, not more");

            // Two bands that meet along z (end to end) with the paint across the joint.
            var end = new List<SurfaceStrip> { Band(-6f, 6f, -30f, 20f, Deck, true), Band(-6f, 6f, 20f, 90f, Deck, true) };
            AreaFit joined = FitOf(end, new Vec3(0f, Deck, 0f));
            MeshBuilder across = Quad(-2f, 2f, 18f, 22f, 0f);
            MeshBuilder whole = joined.DrapeNear(across, AreaFit.NearWithin, out float none);
            Program.Check(none < 1e-3f && Math.Abs(Area(whole) - Area(across)) < 2e-3f, "paint across the joint of two bands loses none: " + none);
        }

        private static void KerbStep()
        {
            // The carriageway from -6 to 6 at 8.0 m and a footway from 6 to 8 at 8.15 m (a filler: what a post may stand on), the paint at the carriageway's height across their edge.
            var strips = new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true), Band(6f, 8f, -30f, 90f, Deck + 0.15f, true, true) };
            AreaFit fit = FitOf(strips, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(4f, 7f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out float lost);
            Program.Check(lost < 1e-3f && Math.Abs(Area(cut) - Area(paint)) < 2e-3f, "paint across a kerb onto the footway is kept: lost " + lost);
            float deckSide = float.MinValue, footSide = float.MaxValue;
            foreach (Vec3 p in cut.Positions)
            {
                if (p.X <= 5.999f)
                    deckSide = Math.Max(deckSide, p.Y);
                if (p.X >= 6.001f)
                    footSide = Math.Min(footSide, p.Y);
            }

            float atEdgeLow = float.MaxValue;
            foreach (Vec3 p in cut.Positions)
            {
                if (Math.Abs(p.X - 6f) < 1e-3f)
                    atEdgeLow = Math.Min(atEdgeLow, p.Y);
            }

            Program.Check(Math.Abs(atEdgeLow) < 1e-3f, $"the carriageway's corners on the edge stay at its height, not lifted to the footway's ({atEdgeLow:0.000})");
            Program.Check(Math.Abs(deckSide) < 1e-3f && Math.Abs(footSide - 0.15f) < 1e-3f, $"the carriageway's part at its height ({deckSide:0.000}), the footway's at the footway's ({footSide:0.000}); the kerb's step is not merged");
        }

        private static void Winding()
        {
            AreaFit fit = FitOf(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true) }, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(3f, 9f, 10f, 12f, 0f);
            MeshBuilder cut = fit.DrapeNear(paint, AreaFit.NearWithin, out _);
            bool faces = cut.TriangleCount > 0;
            for (int t = 0; t + 2 < cut.Indices.Count; t += 3)
            {
                Vec3 a = cut.Positions[cut.Indices[t]], b = cut.Positions[cut.Indices[t + 1]], c = cut.Positions[cut.Indices[t + 2]];
                // The normal of the winding (its Y), as the mesh builder's QuadFacing makes it for paint that faces up.
                float ny = (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z);
                float original = WindingY(paint, 0);
                faces &= ny * original > 0f;
            }

            Program.Check(faces, "every triangle of the cut is wound as the paint was");
            foreach (Vec3 n in cut.Normals)
                Program.Check(n.Y > 0.99f, "and its normals point up: " + n.Y);
        }

        private static float WindingY(MeshBuilder mesh, int t)
        {
            Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
            return (b.Z - a.Z) * (c.X - a.X) - (b.X - a.X) * (c.Z - a.Z);
        }

        private static void Kept()
        {
            AreaFit fit = FitOf(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true) }, new Vec3(0f, Deck, 0f));
            MeshBuilder paint = Quad(3f, 9f, 10f, 12f, 0f);
            MeshBuilder first = fit.DrapedNearOf(paint, out float lost);
            MeshBuilder second = fit.DrapedNearOf(paint, out float again);
            Program.Check(ReferenceEquals(first, second) && lost == again && Math.Abs(lost - 6f) < 1e-3f, "the cut of a mesh is made once and kept, with what it lost");
            MeshBuilder other = fit.DrapedNearOf(Quad(3f, 9f, 14f, 16f, 0f), out float otherLost);
            Program.Check(!ReferenceEquals(first, other) && Math.Abs(otherLost - 6f) < 1e-3f, "another mesh has its own");
            Program.Check(fit.DrapedNearOf(null, out float none).VertexCount == 0 && none == 0f, "no mesh: none");
        }

        private static void Wanted()
        {
            Program.Check(RunCut.Wants(SignCatalog.ById("mk.strip")) && RunCut.Wants(SignCatalog.ById("mk.rumble.longitudinal")), "a hatched strip and a longitudinal rumble are cut");
            foreach (string id in new[] { "mk.line.edge", "mk.line.centre", "mk.line.lane", "mk.stop", "mk.line.kerb" })
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def == null || !RunCut.Wants(def), id + " is not");
            }

            Program.Check(!RunCut.Wants(null), "no marking: no");
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Group == MarkingTemplates.BarrierGroup)
                    Program.Check(!RunCut.Wants(def), def.Id + ": a barrier stands on what it stands on");
            }
        }

        // ------------------------------------------------------------------ strips

        /// <summary>The item of a strip along +Z at x, from z0 to z1 every 2 m, at the height y: its position, yaw and the plan the tile builder draws, in the item's frame.</summary>
        private sealed class StripItem
        {
            public Vec3 Position;
            public float Yaw;
            public MarkingPlan Plan;
            public string Spec;
        }

        private static StripItem Item(string settings, float x, float z0, float z1, Func<float, float> height, string id = "mk.strip")
        {
            var points = new List<Vec3>();
            for (float z = z0; z <= z1 + 0.001f; z += 2f)
                points.Add(new Vec3(x, height(z), z));
            return ItemOf(settings, points, id);
        }

        private static StripItem ItemOf(string settings, List<Vec3> points, string id = "mk.strip")
        {
            GuideTemplate template = MarkingTemplates.ById(id);
            string path = RunPath.Encode(points, out float yaw);
            string spec = MarkingTemplates.WithRun(template, template.Parse(settings).ToString(), path, 0f, 0f);
            return new StripItem { Position = points[0], Yaw = yaw, Plan = MarkingTemplates.PlanOf(template, spec), Spec = spec };
        }

        private static string Strip(float width, string side = "r", string fill = "diagonal", string colour = "y") =>
            $"width={width.ToString(System.Globalization.CultureInfo.InvariantCulture)};side={side};fill={fill};colour={colour};line=45;outline=15;tip=s";

        /// <summary>The fit the cut is made from for an item (AreaFitSystem.CutFor): the roads' strips in the item's frame, at the item's first point.</summary>
        private static AreaFit FitFor(StripItem item, IList<SurfaceStrip> strips) =>
            AreaFit.ForHeights(strips, item.Position, item.Yaw, item.Plan.MinX, item.Plan.MaxX, item.Plan.MinZ, item.Plan.MaxZ, RunCut.ReadMargin);

        private static void OldHeights()
        {
            // The heights a line of paint beyond a deck was laid at before the cut (SurfaceHeights.Put as the lay pass makes it, paint: no bridging): the terrain's where it is within 3 m of the line's own
            // height (the grass under the bridge: the owner's second arrow), else the line's own (over the water, the ground far below: the first).
            SurfaceStrip deck = Band(-6f, 6f, -30f, 90f, Deck, true);
            AreaFit Read(float x0, float z0, float x1, float z1, float y) => AreaFit.ForHeights(new List<SurfaceStrip> { deck }, new Vec3(0f, y, 0f), 0f, x0, x1, z0, z1, 1f);
            var line = new List<Vec3>();
            for (float z = 0f; z <= 40f; z += 2f)
                line.Add(new Vec3(9f, Deck, z));
            List<Vec3> grass = new SurfaceHeights().Put(line, "grass", line.Count, Read, (x, y, z) => Ground, false);
            List<Vec3> water = new SurfaceHeights().Put(line, "water", line.Count, Read, (x, y, z) => Deck - 6f, false);
            Program.Check(grass.TrueForAll(p => Math.Abs(p.Y - Ground) < 1e-4f), "paint 3 m beyond the deck, the terrain 2.3 m under it: laid on the terrain, hanging off the bridge");
            Program.Check(water.TrueForAll(p => Math.Abs(p.Y - Deck) < 1e-4f), "paint 3 m beyond the deck, the ground 6 m under it (the water): laid at the deck's own height, in the air");

            // Both are over no surface at their height; the cut leaves them out.
            foreach (List<Vec3> laid in new[] { grass, water })
            {
                StripItem item = ItemOf(Strip(1.5f), laid);
                AreaFit own = FitFor(item, new List<SurfaceStrip> { deck });
                AreaFit needed = RunCut.Needed(item.Plan, own, out float share);
                Program.Check(needed != null && share > 0.99f, "the strip along it is all over no surface: " + share);
            }

        }

        private static void OnDeck()
        {
            SurfaceStrip deck = Band(-6f, 6f, -30f, 120f, Deck, true);
            var strips = new List<SurfaceStrip> { deck };
            int checkedStrips = 0;
            foreach (float width in new[] { 0.5f, 1.5f, 3f, 5f })
            {
                foreach (string side in new[] { "r", "l" })
                {
                    foreach (string fill in new[] { "diagonal", "chevron" })
                    {
                        foreach (float length in new[] { 6f, 40f, 62f })
                        {
                            // On the deck whole: the line 0.3 m in from the edge for the width of the strip, either side.
                            float x = side == "r" ? 5.7f - width : -5.7f + width;
                            StripItem item = Item(Strip(width, side, fill), x, 0f, length, z => Deck);
                            AreaFit fit = FitFor(item, strips);
                            AreaFit needed = RunCut.Needed(item.Plan, fit, out float share);
                            Program.Check(needed == null && share < RunCut.MinLostShare, $"a strip {width} m wide on the {side} with {fill}, {length} m long, wholly on the deck, is not cut: {share * 100f:0.000} %");
                            checkedStrips++;
                        }
                    }
                }
            }

            Program.Check(checkedStrips == 48, "all were looked at: " + checkedStrips);
        }

        private static void StripAtEdge()
        {
            SurfaceStrip deck = Band(-6f, 6f, -30f, 120f, Deck, true);
            var strips = new List<SurfaceStrip> { deck };
            // The line 4.5 m from the centre, the strip 3 m wide to its right: it spans 4.5 to 7.5, 1.5 m of it beyond the edge at 6.
            StripItem item = Item(Strip(3f), 4.5f, 0f, 40f, z => Deck);
            AreaFit fit = FitFor(item, strips);
            AreaFit needed = RunCut.Needed(item.Plan, fit, out float share);
            Program.Check(needed != null && share > 0.3f && share < 0.6f, $"a strip half over the edge is cut: {share * 100f:0.0} % lost");
            float reach = float.MinValue, top = float.MinValue;
            foreach (MarkingPart part in item.Plan.Parts)
            {
                MeshBuilder cut = needed.DrapedNearOf(part.Mesh, out _);
                // The item's frame is turned by the yaw (+Z along the line): x is across the line from its start, the edge is at 6 - 4.5 = 1.5.
                foreach (Vec3 p in cut.Positions)
                {
                    reach = Math.Max(reach, p.X);
                    top = Math.Max(top, Math.Abs(p.Y));
                }
            }

            Program.Check(reach < 1.5005f && reach > 1.49f, $"the paint stops at the edge: {reach:0.0000} m from the line (the edge is 1.5 m)");
            Program.Check(top < 1e-3f, "and lies at the deck's height: " + top);
        }

        private static void StripBeyond()
        {
            SurfaceStrip deck = Band(-6f, 6f, -30f, 120f, Deck, true);
            StripItem item = Item(Strip(1.5f), 9f, 0f, 40f, z => Deck);
            AreaFit fit = FitFor(item, new List<SurfaceStrip> { deck });
            AreaFit needed = RunCut.Needed(item.Plan, fit, out float share);
            Program.Check(needed != null && share > 0.99f, $"a strip 3 m beyond the deck is all lost: {share * 100f:0.0} %");
            foreach (MarkingPart part in item.Plan.Parts)
                Program.Check(needed.DrapedNearOf(part.Mesh, out _).VertexCount == 0, "and none of its paint is drawn");
        }

        private static void Ramp()
        {
            // A ramp that climbs 6 m in 60 m over a road, as the owner's: a deck of three bands (each at its own height, joined) and, under its start, a road at 2.5 m.
            var strips = new List<SurfaceStrip>();
            var ramp = new SurfaceStrip { Elevated = true };
            for (float z = -10f; z <= 130f; z += 10f)
            {
                float y = Under + Math.Max(0f, Math.Min(6f, z * 0.1f));
                ramp.Add(new Vec3(-4f, y, z), new Vec3(4f, y, z));
            }

            strips.Add(ramp);
            strips.Add(Band(-30f, 30f, -30f, 150f, Under));
            Func<float, float> height = z => Under + Math.Max(0f, Math.Min(6f, z * 0.1f));
            // The strip in from the ramp's edges on the right, over its whole length: 1.5 m wide, 0.5 m from the edge.
            StripItem item = Item(Strip(1.5f), 2f, 0f, 62f, height);
            AreaFit fit = FitFor(item, strips);
            AreaFit needed = RunCut.Needed(item.Plan, fit, out float share);
            Program.Check(needed == null, $"a strip on a ramp that climbs over a road is on the ramp, not cut, and not dropped on the road under it: {share * 100f:0.00} %");
            // The same strip just over the edge: the part beyond is left out even where the road under the ramp lies beneath it.
            StripItem beyond = Item(Strip(1.5f), 6f, 0f, 62f, height);
            AreaFit atEdge = FitFor(beyond, strips);
            AreaFit cut = RunCut.Needed(beyond.Plan, atEdge, out float lostShare);
            // Its first 10 m, where the ramp is within 1 m of the road it starts on, are at the road's level and lie on it; the rest is 1 to 6 m over the road and over no ramp.
            Program.Check(cut != null && lostShare > 0.78f && lostShare < 0.9f, "a strip beside the ramp, at the ramp's height, is left out where the road under it is more than 1 m down: " + lostShare);
        }

        private static void Parts()
        {
            AreaFit fit = FitOf(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 90f, Deck, true) }, new Vec3(0f, Deck, 0f));
            var plan = new MarkingPlan();
            plan.Parts.Add(new MarkingPart { Colour = MarkingColour.White, Mesh = Quad(-2f, 2f, 10f, 12f, 0f) });
            plan.Parts.Add(new MarkingPart { Colour = MarkingColour.Yellow, Mesh = Quad(8f, 12f, 10f, 12f, 0f) });
            AreaFit needed = RunCut.Needed(plan, fit, out float share);
            Program.Check(needed != null && Math.Abs(share - 0.5f) < 1e-3f, "one part on the deck and one beyond it: half of the paint is lost: " + share);

            // A solid (a barrier's part) is no paint: the one beyond the deck does not count.
            var withSolid = new MarkingPlan();
            withSolid.Parts.Add(new MarkingPart { Colour = MarkingColour.White, Mesh = Quad(-2f, 2f, 10f, 12f, 0f) });
            withSolid.Parts.Add(new MarkingPart { Colour = MarkingColour.White, Mesh = Quad(8f, 12f, 10f, 12f, 0f), Solid = SolidColour.Metal });
            Program.Check(RunCut.Needed(withSolid, fit, out float none) == null && none == 0f, "a solid beyond the deck is not paint: " + none);
            Program.Check(RunCut.Needed(new MarkingPlan(), fit, out float empty) == null && empty == 0f, "a plan with no paint is not cut");
        }

        private static void FollowLineUnit()
        {
            // A line that climbs from 0 to 4 m over 40 m, and a mesh of flat vertices (height 0) beside it, before it, along it and past it.
            var line = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 1f, 10f), new Vec3(0f, 4f, 40f) };
            var mesh = new MeshBuilder();
            Vec3 up = new Vec3(0f, 1f, 0f);
            foreach (float z in new[] { -5f, 0f, 5f, 10f, 25f, 40f, 45f })
                mesh.Vertex(new Vec3(1.5f, 0f, z), up);
            mesh.Vertex(new Vec3(-3f, 0f, 20f), up);
            float[] want = { 0f, 0f, 0.5f, 1f, 2.5f, 4f, 4f, 2f };
            MarkingFills.FollowLine(mesh, 0, line);
            bool all = true;
            for (int i = 0; i < want.Length; i++)
                all &= Math.Abs(mesh.Positions[i].Y - want[i]) < 1e-4f;
            Program.Check(all, "each vertex is at the line's height at its nearest place, the ends' beyond them");

            var again = new MeshBuilder();
            again.Vertex(new Vec3(0f, 7f, 5f), up);
            again.Vertex(new Vec3(0f, 7f, 30f), up);
            MarkingFills.FollowLine(again, 1, line);
            Program.Check(again.Positions[0].Y == 7f && Math.Abs(again.Positions[1].Y - 3.0f) < 1e-4f, "the vertices before first are left as they are");

            // On the flat nothing moves, to the bit: zero is zero.
            var flat = new MeshBuilder();
            flat.Vertex(new Vec3(1f, 0f, 3.3f), up);
            MarkingFills.FollowLine(flat, 0, new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 10f) });
            Program.Check(BitConverter.SingleToInt32Bits(flat.Positions[0].Y) == 0, "a line of one height leaves the vertices where they were");
            MarkingFills.FollowLine(null, 0, line);
            MarkingFills.FollowLine(flat, 0, null);
            MarkingFills.FollowLine(flat, 0, new List<Vec3> { new Vec3(0f, 5f, 0f) });
            Program.Check(flat.Positions[0].Y == 0f, "no mesh, no line, or a point: nothing");
        }

        private static void Builder()
        {
            SignDef def = SignCatalog.ById("mk.strip");
            SurfaceStrip deck = Band(-6f, 6f, -30f, 120f, Deck, true);
            StripItem item = Item(Strip(3f), 4.5f, 0f, 40f, z => Deck);
            string laidSpec = item.Spec;
            MarkingPlan plan = MarkingTemplates.PlanOf(def, laidSpec);
            AreaFit forPlan = AreaFit.ForHeights(new List<SurfaceStrip> { deck }, item.Position, item.Yaw, plan.MinX, plan.MaxX, plan.MinZ, plan.MaxZ, RunCut.ReadMargin);
            AreaFit cut = RunCut.Needed(plan, forPlan, out _);
            Program.Check(cut != null, "the plan is cut");
            var record = new ItemRecord(def.Hash, item.Position, item.Yaw, 0u, laidSpec) { Laid = new MarkingGround.Laid { Spec = laidSpec, Dy = 0f, Cut = cut } };
            var plain = new ItemRecord(def.Hash, item.Position, item.Yaw, 0u, laidSpec) { Laid = new MarkingGround.Laid { Spec = laidSpec, Dy = 0f } };
            float cutReach = float.MinValue, plainReach = float.MinValue;
            int cutVertices = 0, plainVertices = 0;
            foreach (ChunkPart part in ChunkBuilder.Build(new[] { record }, Vec3.Zero, out _))
            {
                cutVertices += part.Mesh.VertexCount;
                foreach (Vec3 p in part.Mesh.Positions)
                    cutReach = Math.Max(cutReach, p.X);
            }

            foreach (ChunkPart part in ChunkBuilder.Build(new[] { plain }, Vec3.Zero, out _))
            {
                plainVertices += part.Mesh.VertexCount;
                foreach (Vec3 p in part.Mesh.Positions)
                    plainReach = Math.Max(plainReach, p.X);
            }

            Program.Check(cutReach < 6.0005f && plainReach > 7.4f, $"the builder draws the cut paint to the edge ({cutReach:0.000}) and the paint that was not cut as before, to {plainReach:0.000}");
            Program.Check(cutVertices > 0 && plainVertices > 0, "both have paint: " + cutVertices + " and " + plainVertices);

            // An item that has the fit of an area as well as a cut is drawn by the fit (the area's way), whatever the cut says.
            AreaFit higher = AreaFit.ForHeights(new List<SurfaceStrip> { Band(-6f, 6f, -30f, 120f, Deck + 0.5f, true) }, item.Position, item.Yaw, plan.MinX, plan.MaxX, plan.MinZ, plan.MaxZ, RunCut.ReadMargin);
            AreaFit lifted = RunCut.Needed(plan, higher, out _);
            AreaFit area = AreaFit.FromStrips(new List<SurfaceStrip> { deck }, item.Position, item.Yaw, plan.MinX, plan.MaxX, plan.MinZ, plan.MaxZ, RunCut.ReadMargin);
            var both = new ItemRecord(def.Hash, item.Position, item.Yaw, 0u, laidSpec) { Fit = area, Laid = new MarkingGround.Laid { Spec = laidSpec, Dy = 0f, Cut = lifted } };
            float lowest = float.MaxValue;
            foreach (ChunkPart part in ChunkBuilder.Build(new[] { both }, Vec3.Zero, out _))
            {
                foreach (Vec3 p in part.Mesh.Positions)
                    lowest = Math.Min(lowest, p.Y);
            }

            Program.Check(lifted != null && Math.Abs(lowest - Deck - ChunkBuilder.MarkingRaise) < 0.01f, $"the fit of the area is used, not the cut: the paint's lowest is {lowest:0.000} (the deck {Deck} and the raise {ChunkBuilder.MarkingRaise})");
        }

        private static void NoRoad()
        {
            StripItem item = Item(Strip(1.5f), 9f, 0f, 40f, z => Deck);
            Program.Check(RunCut.Needed(item.Plan, null, out float share) == null && share == 0f, "no fit: no cut");
            Program.Check(RunCut.Needed(null, new AreaFit(), out _) == null, "no plan: no cut");
            // A fit of no pieces (the roads read were all too far above or below): everything is over no surface, but only a fit that exists says so; AreaFitSystem asks for none when no band was read.
            AreaFit empty = AreaFit.ForHeights(new List<SurfaceStrip>(), item.Position, item.Yaw, item.Plan.MinX, item.Plan.MaxX, item.Plan.MinZ, item.Plan.MaxZ, 1f);
            Program.Check(RunCut.Needed(item.Plan, empty, out float all) != null && all > 0.99f, "a fit with no piece loses all the paint: " + all);
            RunCut.WorldBox(item.Plan, item.Position, item.Yaw, out float x0, out float z0, out float x1, out float z1);
            Program.Check(x0 < 9f - RunCut.ReadMargin + 0.5f && x1 > 10.4f && z0 < 0f && z1 > 40f, $"the read box covers the paint and its margin: x {x0:0.0} to {x1:0.0}, z {z0:0.0} to {z1:0.0}");
        }
    }
}
