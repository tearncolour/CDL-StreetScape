using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    internal static partial class GreeningTests
    {
        private static List<Vec3> Points(MarkingPart part) => part == null ? new List<Vec3>() : part.Mesh.Positions;

        private static float MaxY(MarkingPart part) => Points(part).Count == 0 ? float.NaN : Points(part).ConvertAll(p => p.Y).Max();

        /// <summary>The intervals (along) that the values fill, those more than gap apart parted.</summary>
        private static List<(float From, float To)> Intervals(IEnumerable<float> values, float gap)
        {
            var sorted = new List<float>(values);
            sorted.Sort();
            var spans = new List<(float, float)>();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (spans.Count == 0 || sorted[i] - spans[spans.Count - 1].Item2 > gap)
                    spans.Add((sorted[i], sorted[i]));
                else
                    spans[spans.Count - 1] = (spans[spans.Count - 1].Item1, sorted[i]);
            }

            return spans;
        }

        private static void Pits()
        {
            MarkingPlan plan = PlanOf(MarkingTemplates.TreePitId, "where=m", Straight(12f));
            MarkingPart kerb = Part(plan, SolidColour.Granite), soil = Part(plan, SolidColour.Soil), grate = Part(plan, SolidColour.CastIron, DetailKind.TreeGrate);
            Program.Check(kerb != null && soil != null && grate != null && Part(plan, SolidColour.Wood) == null, "a pit: granite kerbs, soil, a cast grate, no guard");
            Program.Check(OpenEdges(kerb.Mesh) == 0 && OpenEdges(soil.Mesh) == 0, "the kerbs and the soil are closed bodies");
            Program.Check(plan.BakedProps.Count == 3, "trees every 6 m over 12 m, none crowded in at the end (CJJ/T 75-2023 4.3.2): " + plan.BakedProps.Count);
            float soilTop = Barriers.FlushTop - Barriers.GrateThickness;
            for (int i = 0; i < plan.BakedProps.Count; i++)
            {
                PropAnchor tree = plan.BakedProps[i];
                Program.Check(tree.Kind == "tree.plane" && Math.Abs(tree.Local.X) < 1e-4f && Math.Abs(tree.Local.Z - 6f * i) < 1e-3f && Math.Abs(tree.Local.Y - soilTop) < 1e-4f,
                    "a London plane in the middle of its pit on the soil under the grate: " + tree.Local);
            }

            Program.Near(MaxY(kerb), Barriers.FlushTop, 1e-4f, "the kerb level with the footway (3.5 cm over the game's)");
            Program.Near(MaxY(grate), Barriers.FlushTop, 1e-4f, "the grate level with its kerb (CJJ/T 75-2023 4.3.5)");
            Program.Near(MaxY(soil), soilTop, 1e-4f, "the soil under the grate");
            Program.Check(!kerb.Mesh.Positions.Exists(p => Math.Abs(p.X) < 0.599f && Math.Abs(p.Z - Math.Round(p.Z / 6f) * 6f) < 0.599f), "the pit 1.2 m clear inside its kerbs (DB11/T 839-2025 5.1.3)");
            Bounds(kerb.Mesh.Positions, out Vec3 klo, out Vec3 khi);
            Program.Check(Math.Abs(khi.X - 0.7f) < 1e-4f && Math.Abs(klo.X + 0.7f) < 1e-4f && klo.Y < -0.05f, "kerbs 0.10 m wide, their feet in the ground");
            Bounds(grate.Mesh.Positions, out Vec3 glo, out Vec3 ghi);
            Program.Check(Math.Abs(ghi.X - 0.6f) < 1e-4f && Math.Abs(glo.X + 0.6f) < 1e-4f, "the grate fills its ring");
            Program.Check(grate.Mesh.U.Max() <= 1.0001f && grate.Mesh.U.Max() > 0.99f, "the grate's top spans its tile once");

            // With a guard: young trees, the poles through the grate's corner holes into the soil, 1.65 m high.
            MarkingPlan guarded = PlanOf(MarkingTemplates.TreePitId, "where=m;cover=resin;guard=1", Straight(1f));
            MarkingPart guard = Part(guarded, SolidColour.Wood, DetailKind.Timber);
            Program.Check(guard != null && Part(guarded, SolidColour.ResinGreen, DetailKind.TreeGrate) != null, "a resin grate and a timber guard");
            Program.Check(guarded.BakedProps.Count == 1 && guarded.BakedProps[0].Kind == "tree.plane.young", "a guarded tree is young");
            Program.Check(Math.Abs(MaxY(guard) - (soilTop + 1.65f)) < 0.01f, "the guard 1.65 m high (DB11/T 839-2025 5.9.3: 1.5 to 2 m): " + MaxY(guard));
            int through = 0, outside = 0;
            foreach (Vec3 p in guard.Mesh.Positions)
            {
                if (p.Y < soilTop || p.Y > Barriers.FlushTop)
                    continue;
                through++;
                float t = DetailSurfaces.GrateTile;
                if (DetailSurfaces.GrateSolid((p.X / 1.2f + 0.5f) * t, (p.Z / 1.2f + 0.5f) * t) >= 0f)
                    outside++;
            }

            Program.Check(through == 0 || outside == 0, $"no pole goes through the iron of the grate ({outside} of {through} points)");
            Bounds(guard.Mesh.Positions, out Vec3 plo, out _);
            Program.Check(plo.Y < soilTop - 0.25f && Math.Abs(plo.X) < 0.6f && Math.Abs(plo.Z) < 0.6f, "the poles' feet in the pit's soil");
            int battens = guard.Mesh.Positions.FindAll(p => p.Y > 1.4f && p.Y < 1.56f && Math.Abs(p.X) < 0.3f && Math.Abs(p.Z) < 0.3f).Count;
            Program.Check(battens > 0 && !guard.Mesh.Positions.Exists(p => p.Y > 1.3f && p.Y < 1.6f && Math.Abs(p.X) < 0.1f && Math.Abs(p.Z) < 0.1f), "a square of battens round the trunk, 0.2 m clear for it");

            // A raised pit is open, its soil 8 cm under the rim (DB11/T 839-2025 6.1.1.3); a grating has its square hole.
            MarkingPlan raised = PlanOf(MarkingTemplates.TreePitId, "where=m;kerb=15", Straight(1f));
            Program.Near(MaxY(Part(raised, SolidColour.Granite)), 0.15f, 1e-4f, "a raised kerb 15 cm");
            Program.Near(raised.BakedProps[0].Local.Y, 0.07f, 1e-4f, "its tree on the soil 8 cm under the rim");
            Program.Check(Part(raised, SolidColour.CastIron) == null, "no grate in a raised pit");
            MarkingPlan frp = PlanOf(MarkingTemplates.TreePitId, "where=m;cover=frp;size=150", Straight(1f));
            MarkingPart grating = Part(frp, SolidColour.ResinGreen, DetailKind.FrpGrating);
            Program.Check(grating != null && !grating.Mesh.Positions.Exists(p => Math.Abs(p.X) < 0.199f && Math.Abs(p.Z) < 0.199f), "a grating with a 0.4 m square round the trunk");
            Bounds(grating.Mesh.Positions, out Vec3 flo, out Vec3 fhi);
            Program.Check(Math.Abs(fhi.X - 0.75f) < 1e-4f && Math.Abs(flo.Z + 0.75f) < 1e-4f, "a 1.5 m pit's grating");

            // The trees' line by the roadside: 0.9 m from the line, away from the traffic (DB11/T 839-2025 5.1.3: the trunk at least 90 cm from the kerb's face).
            MarkingPlan side = PlanOf(MarkingTemplates.TreePitId, "where=r", Straight(12f));
            Program.Check(side.BakedProps.TrueForAll(t => Math.Abs(t.Local.X + 0.9f) < 1e-3f), "the trees 0.9 m off the line, the traffic on its right: " + side.BakedProps[0].Local);
            Program.Check(PlanOf(MarkingTemplates.TreePitId, "where=m;gap=4", Straight(12f)).BakedProps.Count == 4, "every 4 m for small crowns");

            // On a slope of 5 %: the pits follow it, the trees on their soil.
            MarkingPlan slope = PlanOf(MarkingTemplates.TreePitId, "where=m", Straight(12f, 0.05f));
            MarkingPart slopeKerb = Part(slope, SolidColour.Granite);
            Program.Check(slopeKerb.Mesh.Positions.Exists(p => Math.Abs(p.Z - 6.7f) < 1e-3f && Math.Abs(p.Y - (0.335f + Barriers.FlushTop)) < 1e-3f), "a kerb's top on the slope's height at its far side");
            Program.Check(slope.BakedProps.TrueForAll(t => Math.Abs(t.Local.Y - (0.05f * t.Local.Z + soilTop)) < 1e-3f), "the trees on the sloping soil");
        }

        private static void Belts()
        {
            MarkingPlan plan = PlanOf(MarkingTemplates.GreenBeltId, "", Straight(20f));
            MarkingPart kerb = Part(plan, SolidColour.Granite), soil = Part(plan, SolidColour.Soil);
            Program.Check(kerb != null && soil != null && OpenEdges(kerb.Mesh) == 0 && OpenEdges(soil.Mesh) == 0, "a belt: closed kerbs and soil");
            Program.Near(MaxY(kerb), 0.15f, 1e-4f, "kerbs 15 cm (CJJ 37-2012 5.5.2: 15 to 20 cm)");
            Program.Near(MaxY(soil), 0.10f, 1e-4f, "the soil 5 cm under them");
            Bounds(kerb.Mesh.Positions, out Vec3 lo, out Vec3 hi);
            Program.Check(Math.Abs(hi.X - 1.15f) < 1e-4f && Math.Abs(lo.Z) < 1e-4f && Math.Abs(hi.Z - 20f) < 1e-4f, "2.0 m clear between kerbs 0.15 m wide, returned at both ends");
            List<PropAnchor> hedges = plan.BakedProps.FindAll(p => p.Kind == GreenPlants.Hedge);
            Program.Check(hedges.Count == 4, "four hedge pieces end to end in 20 m: " + hedges.Count);
            hedges.Sort((a, b) => a.Local.Z.CompareTo(b.Local.Z));
            for (int i = 0; i < hedges.Count; i++)
            {
                PropAnchor h = hedges[i];
                Program.Check(Math.Abs(h.Local.X) + GreenPlants.HedgeWidth * 0.5f <= 1.0f && h.Local.Z - GreenPlants.HedgeLength * 0.5f >= 0.15f - 1e-3f && h.Local.Z + GreenPlants.HedgeLength * 0.5f <= 19.85f + 1e-3f,
                    "a hedge piece inside the bed: " + h.Local);
                Program.Check(Math.Abs(h.Yaw) < 1e-4f, "laid along the belt");
                float top = h.Local.Y + GreenPlants.HedgeHeight;
                Program.Check(top >= 0.6f && top <= 1.5f, "the hedge's top in the anti-glare band 0.6 to 1.5 m (CJJ/T 75-2023 4.2.4): " + top);
                if (i > 0)
                {
                    float gap = h.Local.Z - hedges[i - 1].Local.Z - GreenPlants.HedgeLength;
                    Program.Check(gap <= 0f && gap > -0.15f, "the pieces meet, overlapping a little: " + gap);
                }
            }

            foreach (PropAnchor ball in plan.BakedProps.FindAll(p => p.Kind == GreenPlants.Shrub))
                Program.Check(ball.Local.Z - ball.Room * 0.5f >= 0.15f - 1e-3f && ball.Local.Z + ball.Room * 0.5f <= 19.85f + 1e-3f, "a shrub ball where the chain leaves room at an end: " + ball.Local);

            // Trees: 0.75 m from the kerbs and the ends (CJJ/T 75-2023 4.2.2), at their spacing, centred; the hedge between them; nothing within 1 m of a trunk.
            foreach ((string settings, float w) in new[] { ("width=150;tree=plane", 1.5f), ("width=400;tree=plane", 4f), ("width=300;under=shrubs;tree=linden;treegap=6", 3f), ("width=250;tree=palm;under=flowers;gap=2", 2.5f) })
            {
                MarkingPlan treed = PlanOf(MarkingTemplates.GreenBeltId, settings, Straight(30f));
                List<PropAnchor> trees = treed.BakedProps.FindAll(p => GreenPlants.IsTree(p.Kind));
                Program.Check(trees.Count >= 3, settings + ": trees: " + trees.Count);
                foreach (PropAnchor t in trees)
                {
                    Program.Check(w * 0.5f - Math.Abs(t.Local.X) >= 0.75f - 1e-4f && t.Local.Z >= 0.9f - 1e-3f && t.Local.Z <= 29.1f + 1e-3f, settings + ": a trunk 0.75 m clear of the kerbs: " + t.Local);
                    Program.Check(!treed.BakedProps.Exists(p => !GreenPlants.IsTree(p.Kind) && p.Kind != GreenPlants.Hedge && Math.Abs(p.Local.Z - t.Local.Z) < 0.999f && Math.Abs(p.Local.X - t.Local.X) < 0.5f),
                        settings + ": no shrub within 1 m of a trunk");
                    Program.Check(!treed.BakedProps.Exists(p => p.Kind == GreenPlants.Hedge && Math.Abs(p.Local.Z - t.Local.Z) < GreenPlants.HedgeLength * 0.5f + 0.999f - 0.051f && Math.Abs(p.Local.X) < 0.7f),
                        settings + ": the hedge stops 1 m from a trunk");
                }

                float half = w * 0.5f;
                foreach (PropAnchor p in treed.BakedProps)
                {
                    if (p.Kind == GreenPlants.Hedge)
                        Program.Check(Math.Abs(p.Local.X) + GreenPlants.HedgeWidth * 0.5f <= half + 1e-3f, settings + ": a hedge row inside the bed: " + p.Local.X);
                    else if (!GreenPlants.IsTree(p.Kind))
                        Program.Check(Math.Abs(p.Local.X) < half && p.Room <= w + 1e-3f, settings + ": a shrub inside the bed: " + p.Local.X);
                }
            }

            // A tree strip in a footway (连续树池): flush kerbs, trees every 6 m, shrubs between.
            MarkingPlan strip = PlanOf(MarkingTemplates.TreePitId, "where=m;size=s150", Straight(20f));
            Program.Near(MaxY(Part(strip, SolidColour.Granite)), Barriers.FlushTop, 1e-4f, "a strip's kerbs level with the footway");
            List<PropAnchor> stripTrees = strip.BakedProps.FindAll(p => GreenPlants.IsTree(p.Kind));
            Program.Check(stripTrees.Count == 4 && Math.Abs(stripTrees[1].Local.Z - stripTrees[0].Local.Z - 6f) < 1e-3f && Math.Abs(stripTrees[0].Local.Z - 1f) < 1e-3f, "trees every 6 m in a strip, centred: " + stripTrees.Count);
            Program.Check(strip.BakedProps.Exists(p => p.Kind == GreenPlants.Shrub) && strip.BakedProps.TrueForAll(p => Math.Abs(p.Local.X) < 0.75f), "shrubs between the trees, all in the strip");
            // On the game's own median: no kerbs, only the soil.
            MarkingPlan bare = PlanOf(MarkingTemplates.GreenBeltId, "edge=none", Straight(20f));
            Program.Check(Part(bare, SolidColour.Granite) == null && Math.Abs(MaxY(Part(bare, SolidColour.Soil)) - 0.02f) < 1e-4f, "without kerbs a soil 2 cm proud on the game's median");
            // By the roadside: the kerb's outer face on the line moved by the setback, the bed away from the traffic.
            MarkingPlan roadside = PlanOf(MarkingTemplates.GreenBeltId, "where=r;off=50", Straight(20f));
            Bounds(Part(roadside, SolidColour.Granite).Mesh.Positions, out Vec3 rlo, out Vec3 rhi);
            Program.Check(Math.Abs(rhi.X + 0.5f) < 1e-3f && Math.Abs(rlo.X + 2.8f) < 1e-3f, $"a roadside belt from 0.5 m off the line away from the traffic: {rlo.X} .. {rhi.X}");
        }

        private static void BeltRuns()
        {
            // A 40 m belt in two pieces has the plants of the whole.
            MarkingPlan whole = PlanOf(MarkingTemplates.GreenBeltId, "tree=plane;width=250", Straight(40f), 0f, 40f);
            MarkingPlan a = PlanOf(MarkingTemplates.GreenBeltId, "tree=plane;width=250", Straight(20f), 0f, 40f), b = PlanOf(MarkingTemplates.GreenBeltId, "tree=plane;width=250", Straight(20f), 20f, 40f);
            var pieces = new List<Vec3>();
            pieces.AddRange(a.BakedProps.ConvertAll(p => p.Local));
            pieces.AddRange(b.BakedProps.ConvertAll(p => p.Local + new Vec3(0f, 0f, 20f)));
            Program.Check(pieces.Count == whole.BakedProps.Count && whole.BakedProps.TrueForAll(p => pieces.Exists(q => (q - p.Local).Length < 1e-3f)), $"two pieces put the plants of the whole: {pieces.Count} of {whole.BakedProps.Count}");
            Program.Check(!Part(a, SolidColour.Granite).Mesh.Positions.Exists(p => p.Z > 19.99f && Math.Abs(p.X) < 1.2f), "no return where a piece meets the next");
            MarkingPlan middle = PlanOf(MarkingTemplates.GreenBeltId, "", Straight(30f), 100f, 300f);
            Program.Check(!Part(middle, SolidColour.Granite).Mesh.Positions.Exists(p => Math.Abs(p.X) < 0.99f), "a piece in the middle of a run has no returns");

            // On a bend of 20 m, a belt 1.5 m wide: the straight hedge pieces stay in the bed.
            const float r = 20f;
            List<Vec3> arc = Arc(r, 24f, true);
            MarkingPlan bend = PlanOf(MarkingTemplates.GreenBeltId, "width=150", arc, 0f, 0f);
            Vec3 centre = ArcCentre(arc, r, true);
            List<PropAnchor> pieceList = bend.BakedProps.FindAll(p => p.Kind == GreenPlants.Hedge);
            Program.Check(pieceList.Count >= 4, "hedges along the bend: " + pieceList.Count);
            float worst = 0f;
            foreach (PropAnchor h in pieceList)
            {
                var ahead = new Vec3((float)Math.Sin(h.Yaw), 0f, (float)Math.Cos(h.Yaw));
                Vec3 right = ahead.RightXZ();
                foreach ((float x, float z) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f), (-1f, 0f), (1f, 0f) })
                {
                    Vec3 c = h.Local + right * (x * GreenPlants.HedgeWidth * 0.5f) + ahead * (z * GreenPlants.HedgeLength * 0.5f);
                    float lateral = r - (c - centre).LengthXZ;
                    worst = Math.Max(worst, Math.Abs(lateral));
                }
            }

            Program.Check(worst <= 0.75f + 1e-3f, $"no hedge corner past the kerbs on a 20 m bend: {worst:0.000} m of 0.75");
            Program.Check(OpenEdges(Part(bend, SolidColour.Granite).Mesh) == 0 && OpenEdges(Part(bend, SolidColour.Soil).Mesh) == 0, "closed kerbs and soil on a bend");
            // On a slope of 5 %: kerbs and plants on it.
            MarkingPlan slope = PlanOf(MarkingTemplates.GreenBeltId, "", Straight(20f, 0.05f));
            Program.Check(Part(slope, SolidColour.Granite).Mesh.Positions.Exists(p => Math.Abs(p.Z - 20f) < 1e-3f && Math.Abs(p.Y - 1.15f) < 1e-3f), "a kerb's top 0.15 m over the slope at its far end");
            Program.Check(slope.BakedProps.TrueForAll(p => Math.Abs(p.Local.Y - (0.05f * p.Local.Z + (p.Kind == GreenPlants.Hedge ? 0.02f : 0.10f))) < 1e-3f), "the plants on the sloping soil (a hedge 8 cm into it)");
        }
    }
}
