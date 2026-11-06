using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The road barriers of 0.12.13 (Barriers, the templates of the group "barrier"): their bodies along a run, their materials, the tiles and the pictures.</summary>
    internal static class BarrierTests
    {
        public static void RunAll()
        {
            Program.Run("barriers: the catalogue and the materials", Catalogue);
            Program.Run("barriers: a W-beam guardrail along a run", WBeam);
            Program.Run("barriers: a concrete barrier, a city fence and cones", Others);
            Program.Run("barriers: the tile, the picture and a run of pieces", TilesAndRuns);
            Program.Run("barriers: the second stage (0.12.14)", Stage2);
            Program.Run("barriers: anti-glare panels (0.12.16)", Glare);
            Program.Run("barriers: the heights of a path follow the surface (0.12.20)", PathHeights);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        /// <summary>All the parts of a colour together (0.12.32: a colour may be in parts of several kinds of detail, the W-beam's beam and its posts), null when there are none.</summary>
        private static MarkingPart Part(MarkingPlan plan, SolidColour colour)
        {
            MarkingPart all = null;
            foreach (MarkingPart p in plan.Parts)
            {
                if (p.Solid != colour || p.Mesh.VertexCount == 0)
                    continue;
                all = all ?? new MarkingPart { Solid = colour, Mesh = new MeshBuilder(), Detail = p.Detail };
                all.Mesh.Append(p.Mesh, Vec3.Zero, 0f);
            }

            return all;
        }

        private static List<Vec3> Straight(float length) => new List<Vec3> { P(0f, 0f), P(0f, length) };

        private static bool Finite(MarkingPlan plan)
        {
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    if (!p.IsFinite)
                        return false;
                }
            }

            return true;
        }

        /// <summary>Every triangle faces the way its corners' normal says (the renderer culls the others).</summary>
        private static bool Wound(MeshBuilder mesh)
        {
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (n.Length > 1e-8f && Vec3.Dot(n, mesh.Normals[mesh.Indices[t]]) < 0f)
                    return false;
            }

            return true;
        }

        private static void Catalogue()
        {
            foreach (string id in new[] { MarkingTemplates.WBeamId, MarkingTemplates.ConcreteBarrierId, MarkingTemplates.CityFenceId, MarkingTemplates.ConeId, MarkingTemplates.CableId, MarkingTemplates.WarningPostId,
                MarkingTemplates.WarningBlockId, MarkingTemplates.BollardId, MarkingTemplates.WaterBarrierId, MarkingTemplates.CrashDrumId, MarkingTemplates.HoardingId })
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(def.Template), id + ": in the catalogue, in the barriers' group, drawn along a road");
                Program.Check(!AreaFitRules.IsAreaHash(def.Hash) && !MarkingTemplates.WantsFit(def, ""), id + ": not an area fitted to the road");
                MarkingPlan plan = MarkingTemplates.PlanOf(def, "");
                Program.Check(plan != null && plan.HasSolids && plan.TriangleCount > 20 && Finite(plan), id + ": a body of its own with no path (for its picture): " + plan?.TriangleCount);
                foreach (MarkingPart part in plan.Parts)
                    Program.Check(part.Solid.HasValue && Wound(part.Mesh), id + ": solid " + part.Solid + ", every triangle facing its normal");
            }

            Program.Check(ChunkBuilder.SolidMaterial(SolidColour.Metal) == ChunkBuilder.MetalMaterial && ChunkBuilder.SolidMaterial(SolidColour.Concrete) == ChunkBuilder.ConcreteMaterial, "steel and concrete are the signs' own materials");
            foreach (SolidColour c in new[] { SolidColour.White, SolidColour.Blue, SolidColour.Yellow, SolidColour.Black, SolidColour.Orange })
            {
                string name = ChunkBuilder.SolidMaterial(c);
                Program.Check(name.StartsWith(ChunkBuilder.SolidPrefix, StringComparison.Ordinal) && ChunkBuilder.SolidColourOf(name) == c && !ChunkBuilder.IsMarkingMaterial(name), c + ": a solid's material, not paint");
            }

            Program.Check(!MarkingTemplates.PlanOf(SignCatalog.ById("mk.line.edge"), "").HasSolids, "a line is paint");
        }

        private static void WBeam()
        {
            // 20 m straight, posts every 4 m: at 0, 4 ... 20 (the end of the whole run too): six; the beam's middle 600 mm up, on the traffic's side (the right).
            MarkingPlan plan = PlanOf(MarkingTemplates.WBeamId, "where=r;ends=none", Straight(20f));
            MarkingPart metal = Part(plan, SolidColour.Metal);
            Program.Check(metal != null && plan.Parts.TrueForAll(p => p.Solid == SolidColour.Metal), "all of it galvanised steel");
            Program.Check(plan.Parts.Exists(p => p.Detail == DetailKind.WBeam4) && plan.Parts.Exists(p => p.Detail == DetailKind.Galvanised), "the beam with its splices baked in, the posts galvanised");
            float maxY = float.MinValue, minY = float.MaxValue, maxX = float.MinValue, minX = float.MaxValue;
            foreach (Vec3 p in metal.Mesh.Positions)
            {
                maxY = Math.Max(maxY, p.Y);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                minX = Math.Min(minX, p.X);
            }

            Program.Near(maxY, 0.755f, 0.01f, "the beam's top at 755 mm (its middle 600, half of 310 above)");
            Program.Near(minY, -Barriers.Embed, 1e-4f, "the posts go into the ground");
            Program.Check(maxX > 0.3f && minX > -0.08f, $"the beam on the traffic's side, the posts on the line: {minX:0.000} .. {maxX:0.000}");
            int posts = CountTops(metal.Mesh, 0.735f);
            Program.Check(posts == 6, "a post every 4 m and one at the end: " + posts);
            MarkingPart dense = Part(PlanOf(MarkingTemplates.WBeamId, "where=r;post=2;ends=none", Straight(20f)), SolidColour.Metal);
            Program.Check(CountTops(dense.Mesh, 0.735f) == 11, "every 2 m: eleven");

            // A median: the beam on both sides; the left of the traffic: mirrored; three waves: 506 mm, its middle 697 mm up.
            MarkingPart median = Part(PlanOf(MarkingTemplates.WBeamId, "where=m;ends=none", Straight(8f)), SolidColour.Metal);
            Bounds(median.Mesh, out Vec3 lo, out Vec3 hi);
            Program.Check(lo.X < -0.3f && hi.X > 0.3f && Math.Abs(lo.X + hi.X) < 1e-3f, "a median's beams on both sides");
            Bounds(Part(PlanOf(MarkingTemplates.WBeamId, "where=l;ends=none", Straight(8f)), SolidColour.Metal).Mesh, out lo, out hi);
            Program.Check(lo.X < -0.3f && hi.X < 0.08f, "the traffic on the left: the beam on the left");
            Bounds(Part(PlanOf(MarkingTemplates.WBeamId, "wave=3;ends=none", Straight(8f)), SolidColour.Metal).Mesh, out lo, out hi);
            Program.Near(hi.Y, 0.697f + 0.253f, 0.01f, "a three-wave beam's top");

            // Away from the drawn line: 50 cm behind it, away from the traffic.
            Bounds(Part(PlanOf(MarkingTemplates.WBeamId, "where=r;off=50;ends=none", Straight(8f)), SolidColour.Metal).Mesh, out lo, out hi);
            Program.Check(lo.X < -0.55f && hi.X < 0.0f, $"50 cm behind the line: {lo.X:0.00} .. {hi.X:0.00}");

            // On a slope and a bend: the posts stand plumb on the line's heights.
            var bend = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
            {
                double a = i * 0.05;
                bend.Add(P((float)(30.0 * (1.0 - Math.Cos(a))), (float)(30.0 * Math.Sin(a)), i * 0.15f));
            }

            MarkingPlan sloped = PlanOf(MarkingTemplates.WBeamId, "where=r", bend);
            Program.Check(sloped.HasSolids && Finite(sloped) && Wound(Part(sloped, SolidColour.Metal).Mesh), "along a rising bend");
        }

        /// <summary>How many separate tops (flat at a height) a mesh has within maxX of the line: vertices at that height, those within radius of one another one.</summary>
        private static int CountTops(MeshBuilder mesh, float y, float radius = 0.3f, float maxX = 0.1f)
        {
            var places = new List<Vec3>();
            foreach (Vec3 p in mesh.Positions)
            {
                if (Math.Abs(p.Y - y) > 1e-3f || Math.Abs(p.X) > maxX)
                    continue;
                if (!places.Exists(q => Vec3.DistanceXZ(q, p) < radius))
                    places.Add(p);
            }

            return places.Count;
        }

        private static void Bounds(MeshBuilder mesh, out Vec3 lo, out Vec3 hi) => mesh.Bounds(out lo, out hi);

        private static void Others()
        {
            // A median of concrete: 810 mm high, 610 mm at the foot, both faces; capped at the ends of the whole run only.
            MeshBuilder median = Part(PlanOf(MarkingTemplates.ConcreteBarrierId, "where=m", Straight(12f)), SolidColour.Concrete).Mesh;
            median.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Near(hi.Y, 0.81f, 1e-4f, "810 mm high");
            Program.Near(hi.X - lo.X, 0.61f, 1e-3f, "610 mm at the foot");
            Program.Check(Wound(median), "facing out");
            // Every face's normal points away from the barrier's middle (x = 0, half its height).
            bool outward = true;
            for (int i = 0; i < median.VertexCount; i++)
            {
                Vec3 p = median.Positions[i], n = median.Normals[i];
                if (Math.Abs(n.Z) > 0.9f)
                    continue;
                outward &= n.X * p.X + n.Y * (p.Y - 0.4f) > -1e-4f;
            }

            Program.Check(outward, "every side faces away from its middle");
            int caps = CountCapFaces(median);
            Program.Check(caps == 2, "two ends capped: " + caps);
            int middleCaps = CountCapFaces(Part(PlanOf(MarkingTemplates.ConcreteBarrierId, "where=m", Straight(12f), 12f, 36f), SolidColour.Concrete).Mesh);
            Program.Check(middleCaps == 0, "a piece in the middle of a run has no caps: " + middleCaps);
            MeshBuilder roadside = Part(PlanOf(MarkingTemplates.ConcreteBarrierId, "where=r;height=100", Straight(6f)), SolidColour.Concrete).Mesh;
            roadside.Bounds(out lo, out hi);
            Program.Check(Math.Abs(hi.Y - 1.0f) < 1e-4f && lo.X > -1e-4f && hi.X > 0.3f, "by the roadside: upright back on the line, the face toward the traffic, 1 m high");

            // A city fence, 3 m panels, blue and white, on concrete feet: posts at 0, 3 ... 9 (four), bars every 15 cm.
            MarkingPlan fence = PlanOf(MarkingTemplates.CityFenceId, "", Straight(9f));
            Program.Check(Part(fence, SolidColour.Blue) != null && Part(fence, SolidColour.White) != null && Part(fence, SolidColour.Concrete) != null, "blue frame, white bars, concrete feet");
            Part(fence, SolidColour.Blue).Mesh.Bounds(out lo, out hi);
            Program.Near(hi.Y, 1.0f, 1e-4f, "1 m high");
            int feet = CountTops(Part(fence, SolidColour.Concrete).Mesh, 0.12f, 0.6f, 0.2f);
            Program.Check(feet == 4, "a foot under each post: " + feet);
            int bars = CountTops(Part(fence, SolidColour.White).Mesh, 0.95f, 0.05f);
            Program.Check(bars >= 9 / 0.3f, "upright bars all along: " + bars);
            MarkingPlan yellow = PlanOf(MarkingTemplates.CityFenceId, "scheme=yb;feet=0", Straight(6f));
            Program.Check(Part(yellow, SolidColour.Yellow) != null && Part(yellow, SolidColour.Black) != null && Part(yellow, SolidColour.Concrete) == null, "yellow and black, no feet");

            // Cones every 3 m on 9 m: at 0, 3, 6, 9; 700 mm high, orange with white bands on a black foot.
            MarkingPlan cones = PlanOf(MarkingTemplates.ConeId, "", Straight(9f));
            Program.Check(Part(cones, SolidColour.Orange) != null && Part(cones, SolidColour.White) != null && Part(cones, SolidColour.Black) != null, "orange, white bands, black foot");
            Part(cones, SolidColour.Orange).Mesh.Bounds(out lo, out hi);
            Program.Near(hi.Y, 0.7f, 1e-3f, "700 mm high");
            Program.Check(CountTops(Part(cones, SolidColour.Black).Mesh, 0.04f, 0.6f, 0.2f) == 4, "four cones");
            Part(PlanOf(MarkingTemplates.ConeId, "height=90", Straight(1f)), SolidColour.Orange).Mesh.Bounds(out lo, out hi);
            Program.Near(hi.Y, 0.9f, 1e-3f, "a 900 mm cone");
        }

        /// <summary>The faces of a mesh that face along the line (±Z): a closed body's end caps.</summary>
        private static int CountCapFaces(MeshBuilder mesh)
        {
            var seen = new List<float>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 n = mesh.Normals[mesh.Indices[t]];
                if (Math.Abs(n.Z) < 0.99f)
                    continue;
                float z = mesh.Positions[mesh.Indices[t]].Z;
                if (!seen.Exists(s => Math.Abs(s - z) < 1e-3f))
                    seen.Add(z);
            }

            return seen.Count;
        }

        private static float Lowest(MarkingPlan plan)
        {
            float low = float.MaxValue;
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                    low = Math.Min(low, p.Y);
            }

            return low;
        }

        private static void Stage2()
        {
            // Every body reaches under the line, so nothing stands on air on a crowned road (the owner: "路锥比地面微微高一点").
            foreach (string id in new[] { MarkingTemplates.ConeId, MarkingTemplates.WarningBlockId, MarkingTemplates.WaterBarrierId, MarkingTemplates.CrashDrumId, MarkingTemplates.HoardingId, MarkingTemplates.WarningPostId, MarkingTemplates.BollardId })
                Program.Check(Lowest(PlanOf(id, "", Straight(10f))) <= -Barriers.FootSink + 1e-4f, id + ": its foot reaches under the line");

            // Round ends: the beam turns round the end post, beyond it; flared ends bend away from the traffic (the traffic on the right: to the left) by a metre at the ends, none in the middle.
            MeshBuilder plain = Part(PlanOf(MarkingTemplates.WBeamId, "ends=none", Straight(20f)), SolidColour.Metal).Mesh, round = Part(PlanOf(MarkingTemplates.WBeamId, "ends=round", Straight(20f)), SolidColour.Metal).Mesh;
            plain.Bounds(out Vec3 plo, out Vec3 phi);
            round.Bounds(out Vec3 rlo, out Vec3 rhi);
            Program.Check(rlo.Z < plo.Z - 0.2f && rhi.Z > phi.Z + 0.2f, $"a round end goes round each end post: {rlo.Z:0.00} .. {rhi.Z:0.00}");
            Program.Check(Wound(round), "facing its normals");
            MeshBuilder flared = Part(PlanOf(MarkingTemplates.WBeamId, "ends=flare", Straight(40f)), SolidColour.Metal).Mesh;
            flared.Bounds(out Vec3 flo, out _);
            Program.Near(flo.X, -Barriers.FlareOffset - 0.06f, 0.08f, "a flared end bends a metre away from the traffic");
            bool middleStraight = true;
            foreach (Vec3 p in flared.Positions)
            {
                if (p.Z > 12f && p.Z < 28f)
                    middleStraight &= p.X > -0.08f;
            }

            Program.Check(middleStraight, "and the middle of the run is where it was");
            Program.Check(Barriers.Flared(Straight(40f), 0f, 40f, -1f).Count > 10, "sampled every metre near the ends");

            // 0.12.19: the ends by default. By the roadside the end the traffic comes to is flared, the other round: the traffic on the line's right (where=r) runs against the line, so the flare is at
            // the run's end; on its left at its start. In a median both round.
            Barriers.BeamEnds(BeamEnd.Auto, 1, false, out BeamEnd s1, out BeamEnd e1);
            Barriers.BeamEnds(BeamEnd.Auto, -1, false, out BeamEnd s2, out BeamEnd e2);
            Barriers.BeamEnds(BeamEnd.Auto, 1, true, out BeamEnd s3, out BeamEnd e3);
            Barriers.BeamEnds(BeamEnd.Flare, 1, true, out BeamEnd s4, out _);
            Program.Check(s1 == BeamEnd.Round && e1 == BeamEnd.Flare, "traffic on the line's right: round at the start, flared at the end");
            Program.Check(s2 == BeamEnd.Flare && e2 == BeamEnd.Round, "on its left: flared at the start");
            Program.Check(s3 == BeamEnd.Round && e3 == BeamEnd.Round && s4 == BeamEnd.Round, "a median: round at both ends, never flared");
            MeshBuilder auto = Part(PlanOf(MarkingTemplates.WBeamId, "where=r", Straight(40f)), SolidColour.Metal).Mesh;
            auto.Bounds(out Vec3 alo, out Vec3 ahi);
            Program.Check(alo.Z < -0.2f && alo.X < -0.9f, $"by default: a round end at the start, the flare toward the end ({alo.X:0.00}, {alo.Z:0.00})");
            bool startStraight = true;
            foreach (Vec3 p in auto.Positions)
            {
                if (p.Z > 0f && p.Z < 25f)
                    startStraight &= p.X > -0.08f;
            }

            Program.Check(startStraight, "and no flare at the start");
            MeshBuilder middlePiece = Part(PlanOf(MarkingTemplates.WBeamId, "where=r", Straight(10f), 10f, 40f), SolidColour.Metal).Mesh;
            middlePiece.Bounds(out Vec3 mlo, out Vec3 mhi);
            Program.Check(mlo.Z > -0.1f && mhi.Z < 10.1f && mlo.X > -0.08f, "a piece in the middle of a run has no ends");

            // A cable guardrail: three cables at 530, 630, 730 mm in the middle, down to anchors at the ends; no posts by the anchors.
            MarkingPlan cable = PlanOf(MarkingTemplates.CableId, "", Straight(40f));
            MarkingPart steel = Part(cable, SolidColour.Metal), anchors = Part(cable, SolidColour.Concrete);
            Program.Check(steel != null && anchors != null && CountTops(anchors.Mesh, 0.25f, 1.2f, 0.4f) == 2, "two concrete anchors");
            int atMiddle = 0;
            foreach (Vec3 p in steel.Mesh.Positions)
                atMiddle += p.Z > 5.9f && p.Z < 34.1f && p.Y > 0.5f && p.Y < 0.76f && Math.Abs(p.X) < 0.02f ? 1 : 0;
            Program.Check(atMiddle > 0, "cables at their heights in the middle");
            MarkingPart four = Part(PlanOf(MarkingTemplates.CableId, "cables=4", Straight(40f)), SolidColour.Metal);
            four.Mesh.Bounds(out _, out Vec3 fhi);
            Program.Near(fhi.Y, 0.53f + 0.3f + 0.12f, 1e-3f, "four cables: the posts reach over the top one");

            // Warning posts: white with red bands at the top; black bands; 1 m high.
            MarkingPlan posts = PlanOf(MarkingTemplates.WarningPostId, "", Straight(9f));
            Program.Check(Part(posts, SolidColour.White) != null && Part(posts, SolidColour.Red) != null, "white posts with red bands");
            Part(posts, SolidColour.Red).Mesh.Bounds(out _, out Vec3 bhi);
            Program.Near(bhi.Y, 1.0f, 1e-4f, "the top band at the top, 1 m");
            Program.Check(CountTops(Part(posts, SolidColour.Red).Mesh, 1.0f) == 4, "every 3 m: four posts on 9 m");
            Program.Check(Part(PlanOf(MarkingTemplates.WarningPostId, "band=k", Straight(3f)), SolidColour.Black) != null, "black bands");

            // Warning blocks, bollards, water barriers, drums, hoarding.
            MarkingPlan blocks = PlanOf(MarkingTemplates.WarningBlockId, "scheme=yb", Straight(4f));
            Program.Check(Part(blocks, SolidColour.Yellow) != null && Part(blocks, SolidColour.Black) != null, "yellow and black blocks");
            MarkingPlan bollards = PlanOf(MarkingTemplates.BollardId, "", Straight(3f));
            Program.Check(Part(bollards, SolidColour.Metal) != null && Part(bollards, SolidColour.Red) != null, "steel bollards with red bands");
            MarkingPlan water = PlanOf(MarkingTemplates.WaterBarrierId, "", Straight(6f));
            Program.Check(Part(water, SolidColour.Red) != null && Part(water, SolidColour.White) != null, "red and white units");
            int units = 0;
            foreach (MarkingPart part in water.Parts)
                units += CountCapFaces(part.Mesh);
            Program.Check(units == 8, "four units of 1.5 m on 6 m, each capped at both ends: " + units);
            MarkingPlan nextPiece = PlanOf(MarkingTemplates.WaterBarrierId, "", Straight(3f), 1.5f, 6f);
            Program.Check(Part(nextPiece, SolidColour.White) != null, "a piece that starts with the second unit starts white");
            MarkingPlan drums = PlanOf(MarkingTemplates.CrashDrumId, "", Straight(3f));
            Part(drums, SolidColour.Orange).Mesh.Bounds(out Vec3 dlo, out Vec3 dhi);
            Program.Near(dhi.Y, 0.95f, 1e-3f, "drums 950 mm high");
            Program.Near(dhi.X - dlo.X, 0.9f, 0.02f, "900 mm across (an 18-sided tube)");
            MarkingPlan hoarding = PlanOf(MarkingTemplates.HoardingId, "colour=g;height=250", Straight(6f));
            Part(hoarding, SolidColour.Green).Mesh.Bounds(out _, out Vec3 hhi);
            Program.Near(hhi.Y, 2.5f, 1e-4f, "a green hoarding 2.5 m high");
            Program.Check(Part(hoarding, SolidColour.White) != null, "on white posts");
            Program.Check(Part(PlanOf(MarkingTemplates.HoardingId, "colour=w", Straight(6f)), SolidColour.Metal) != null, "a white one on steel posts");
            Program.Check(SolidColour.Red == (SolidColour)7 && SolidColour.Green == (SolidColour)8 && ChunkBuilder.SolidColourOf(ChunkBuilder.SolidMaterial(SolidColour.Green)) == SolidColour.Green, "the new colours keep their numbers");
        }

        private static void PathHeights()
        {
            // 32 chords of 2 m whose heights step up and down by a few centimetres (a barrier over a crowned road, a kerb, a median): the written path gives every point back within 3 mm.
            var line = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
                line.Add(P(0.3f * (float)Math.Sin(i * 0.2), i * 2f, 0.013f * i + (i % 5 == 0 ? 0.15f : 0f) + 0.0049f * (i % 3)));
            string path = RunPath.Encode(line, out _);
            List<Vec3> back = RunPath.Decode(path);
            float worst = 0f;
            for (int i = 0; i < back.Count && i < line.Count; i++)
                worst = Math.Max(worst, Math.Abs(back[i].Y - (line[i].Y - line[0].Y)));
            Program.Check(back.Count == line.Count && worst < 0.003f, "the heights do not drift along the path: " + worst);
        }

        private static void Glare()
        {
            // A median W-beam with panels every 80 cm on 8 m: green panels 750 mm high over a rail at 850 mm, turned a little from square across.
            MarkingPlan plan = PlanOf(MarkingTemplates.WBeamId, "where=m;glare=1", Straight(8f));
            MarkingPart green = Part(plan, SolidColour.Green);
            Program.Check(green != null, "green panels");
            green.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Near(lo.Y, 0.9f, 1e-3f, "on the rail");
            Program.Near(hi.Y, 1.65f, 1e-3f, "750 mm high");
            int panels = CountTops(green.Mesh, 1.65f, 0.3f, 0.2f);
            Program.Check(panels == 10, "every 80 cm, from half a gap in: " + panels);
            Program.Check(Wound(green.Mesh), "facing their normals");
            bool turned = false;
            foreach (Vec3 n in green.Mesh.Normals)
                turned |= Math.Abs(n.Y) < 0.1f && Math.Abs(n.Z) > 0.9f && Math.Abs(n.X) > 0.1f;
            Program.Check(turned, "their faces look along the road, turned a little");
            Program.Check(Part(PlanOf(MarkingTemplates.WBeamId, "where=r;glare=1", Straight(8f)), SolidColour.Green) == null, "only in a median");
            Program.Check(Part(PlanOf(MarkingTemplates.WBeamId, "where=m", Straight(8f)), SolidColour.Green) == null, "off by default");
            MarkingPart onConcrete = Part(PlanOf(MarkingTemplates.ConcreteBarrierId, "where=m;glare=1;glaregap=50", Straight(5f)), SolidColour.Green);
            onConcrete.Mesh.Bounds(out lo, out _);
            Program.Near(lo.Y, 0.81f + 0.05f, 1e-3f, "on a concrete barrier, on its top");
            int onTop = CountTops(onConcrete.Mesh, 0.81f + 0.05f + 0.75f, 0.26f, 0.2f);
            Program.Check(onTop == 10, "every 50 cm on 5 m: " + onTop);
        }

        private static void TilesAndRuns()
        {
            // In a tile: the solids in their materials at the item's height (not lifted like paint).
            SignDef def = SignCatalog.ById(MarkingTemplates.CityFenceId);
            GuideTemplate t = def.Template;
            string spec = MarkingTemplates.WithRun(t, t.Default().ToString(), RunPath.Encode(Straight(6f), out _), 0f, 6f);
            var item = new ItemRecord(def.Hash, new Vec3(10f, 5f, 20f), 0f, 0u, spec);
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { item }, Vec3.Zero, out int skipped);
            string blue = ChunkBuilder.DetailMaterial(DetailKind.Painted, SolidColour.Blue), concrete = ChunkBuilder.DetailMaterial(DetailKind.Concrete, SolidColour.Concrete);
            Program.Check(skipped == 0 && parts.Exists(p => p.Material == blue) && parts.Exists(p => p.Material == concrete) && !parts.Exists(p => ChunkBuilder.IsMarkingMaterial(p.Material)),
                "a tile draws the fence's solids in their detail materials, no paint: " + string.Join(", ", parts.ConvertAll(p => p.Material)));
            ChunkPart feet = parts.Find(p => p.Material == concrete);
            feet.Mesh.Bounds(out Vec3 lo, out _);
            Program.Near(lo.Y, 5f - Barriers.Embed, 1e-3f, "standing on the item's height, not lifted as paint is");

            // The picture: a body seen at an angle, not empty.
            byte[] picture = MarkingRaster.Render(MarkingTemplates.PlanOf(def, spec), 128, out int w, out int h);
            int opaque = 0;
            for (int i = 3; i < picture.Length; i += 4)
                opaque += picture[i] > 0 ? 1 : 0;
            Program.Check(w > 0 && h > 0 && opaque > 200, "its picture shows it: " + opaque + " pixels");

            // A run of two pieces: posts every 4 m along the whole line, none twice where the pieces meet.
            MarkingPart first = Part(PlanOf(MarkingTemplates.WBeamId, "ends=none", Straight(10f), 0f, 20f), SolidColour.Metal);
            MarkingPart second = Part(PlanOf(MarkingTemplates.WBeamId, "ends=none", Straight(10f), 10f, 20f), SolidColour.Metal);
            int a = CountTops(first.Mesh, 0.735f), b = CountTops(second.Mesh, 0.735f);
            Program.Check(a == 3 && b == 3, $"posts at 0, 4, 8 and 12, 16, 20 of the whole line: {a} + {b}");
        }
    }
}
