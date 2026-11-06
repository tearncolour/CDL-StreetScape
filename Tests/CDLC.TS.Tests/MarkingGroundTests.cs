using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The markings laid on the ground as it is (0.12.32, MarkingGround): lines, barriers and things put down with one click, the heights in the tile, and what is not laid.</summary>
    internal static class MarkingGroundTests
    {
        public static void RunAll()
        {
            Program.Run("marking ground: which markings lie on the ground", WhichLie);
            Program.Run("marking ground: a line started on a footway lies on the carriageway", LineOffFootway);
            Program.Run("marking ground: a line over a sag and a crest follows it", LineOverSag);
            Program.Run("marking ground: a line on the ground it was placed on is unchanged", Unchanged);
            Program.Run("marking ground: a hump placed flat before 0.12.30 on a sloped road", HumpOnSlope);
            Program.Run("marking ground: a barrier on a road raised after it was placed", BarrierRaised);
            Program.Run("marking ground: the tile draws the laid marking", InTile);
            Program.Run("marking ground: a kerb line on the kerb's face stands on the carriageway", KerbLineFoot);
            Program.Run("junctions: only the junction's own lines follow its lanes", JunctionLines);
        }

        internal static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            Program.Check(def != null, id + " is in the catalogue");
            return def;
        }

        /// <summary>The settings of a run along +Z from the origin over length metres, on the heights y(z) (world, the item at y(0)).</summary>
        internal static string Run(SignDef def, float length, Func<float, float> y, string settings = "")
        {
            var points = new List<Vec3>();
            for (float z = 0f; z <= length + 1e-3f; z += 2f)
                points.Add(new Vec3(0f, y(z), z));
            string path = RunPath.Encode(points, out _);
            GuideSpec spec = def.Template.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.WithRun(def.Template, spec.ToString(), path, 0f);
        }

        /// <summary>A surface whose height at a world point is ground(x, z): what StandHeights gives, every point on it.</summary>
        internal static Func<List<Vec3>, List<Vec3>> On(Func<float, float, float> ground) => world =>
        {
            var on = new List<Vec3>(world.Count);
            foreach (Vec3 p in world)
                on.Add(new Vec3(p.X, ground(p.X, p.Z), p.Z));
            return on;
        };

        /// <summary>The lowest and highest height of the paint (or the solids) of a laid marking in the world.</summary>
        internal static void Heights(SignDef def, MarkingGround.Laid laid, Vec3 position, bool solid, out float lo, out float hi, out List<Vec3> world)
        {
            MarkingPlan plan = MarkingTemplates.PlanOf(def, laid.Spec);
            lo = float.MaxValue;
            hi = float.MinValue;
            world = new List<Vec3>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid.HasValue != solid)
                    continue;
                var local = new List<Vec3>(part.Mesh.Positions);
                foreach (Vec3 p in MarkingPolygon.ToWorld(local, position + new Vec3(0f, laid.Dy, 0f), 0f))
                {
                    lo = Math.Min(lo, p.Y);
                    hi = Math.Max(hi, p.Y);
                    world.Add(p);
                }
            }
        }

        /// <summary>
        /// A no-parking line on the kerb (线48) with its path on the face of a kerb 20 cm high (the owner's screenshot, 0.12.34: the paint stood on the footway): its foot on the carriageway, its top over
        /// the kerb, whichever side of the line the kerb is.
        /// </summary>
        private static void KerbLineFoot()
        {
            SignDef def = Def("mk.kerb.nopark");
            Program.Check(MarkingTemplates.IsKerbLine(def.Id) && !MarkingTemplates.IsKerbLine("mk.kerbstop") && !MarkingTemplates.IsKerbLine("mk.line.edge"), "the kerb lines are known");
            var position = new Vec3(100f, 10f, 200f);
            foreach (string side in new[] { "r", "l" })
            {
                // The footway on the kerb's side of the face (x = 100), 20 cm over the carriageway.
                float sign = side == "r" ? 1f : -1f;
                string spec = Run(def, 20f, z => 10f, "side=" + side + ";height=20");
                MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On((x, z) => (x - 100f) * sign >= 0f ? 10.2f : 10f));
                Program.Check(laid != null, side + ": laid");
                Program.Near(laid.Dy, 0f, 0.002f, side + ": its foot stays on the carriageway");
                Heights(def, laid, position, false, out float lo, out float hi, out _);
                Program.Near(lo, 10f, 0.01f, side + ": the paint starts at the carriageway");
                Program.Check(hi > 10.2f && hi < 10.22f, side + ": and reaches just over the kerb's top: " + hi);
            }
        }

        /// <summary>0.12.43 (the owner: "现在需要离开路口一段距离才行"): every line and barrier started over a junction's part followed one of its lanes; only the junction's own lines do now.</summary>
        private static void JunctionLines()
        {
            Program.Check(MarkingTemplates.FollowsJunctionLanes("mk.line.junction") && MarkingTemplates.FollowsJunctionLanes("mk.line.waiting"), "the junction guide line and the waiting area's lines follow the junction's lanes");
            int others = 0;
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                if (MarkingTemplates.IsRun(t) && t.Id != "mk.line.junction" && t.Id != "mk.line.waiting" && MarkingTemplates.FollowsJunctionLanes(t.Id))
                    others++;
            }

            Program.Check(others == 0, "no other line and no barrier does: " + others);
            Program.Check(SignCatalog.ById("mk.line.junction") != null && SignCatalog.ById("mk.line.waiting") != null, "both are in the catalogue");
        }

        private static void WhichLie()
        {
            Program.Check(MarkingGround.Lies(Def("mk.line.lane")), "a lane line lies on the ground");
            Program.Check(MarkingGround.Lies(Def("mk.hump.small")), "a hump marking lies on the ground");
            Program.Check(MarkingGround.Lies(Def(MarkingTemplates.WBeamId)), "a barrier lies on the ground");
            Program.Check(MarkingGround.Lies(Def(MarkingTemplates.HumpId)), "a rubber hump lies on the ground");
            Program.Check(!MarkingGround.Lies(Def(MarkingTemplates.ChannelId)), "an area fitted to the road on its own does not (AreaFit)");
            Program.Check(!MarkingGround.Lies(Def(MarkingTemplates.GateId)) && !MarkingGround.Lies(Def(MarkingTemplates.BoothId)), "the gate and the booth stand upright on their plinth");
            Program.Check(!MarkingGround.Lies(SignCatalog.ById("gb.prohib.39")), "a sign does not");
            Program.Check(MarkingGround.Lay(SignCatalog.ById("gb.prohib.39"), "", new Vec3(0f, 0f, 0f), 0f, On((x, z) => 0f)) == null, "nothing is laid for a sign");
        }

        private static void LineOffFootway()
        {
            // Started on a footway 20 cm above the carriageway: the whole line was 20 cm above the road it is painted on.
            SignDef def = Def("mk.line.lane");
            var position = new Vec3(100f, 10.2f, 200f);
            string spec = Run(def, 40f, z => 10.2f);
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On((x, z) => 10f));
            Program.Check(laid != null, "laid");
            Program.Near(laid.Dy, -0.2f, 0.002f, "its origin comes down by 20 cm");
            Program.Near(laid.Worst, 0.2f, 0.002f, "it was 20 cm above the ground");
            Heights(def, laid, position, false, out float lo, out float hi, out _);
            Program.Near(lo, 10f, 0.01f, "the paint is on the carriageway (lowest)");
            Program.Near(hi, 10f, 0.01f, "the paint is on the carriageway (highest)");
            Program.Check(MarkingTemplates.IsRun(def.Template) && RunPath.Decode(def.Template.Parse(laid.Spec).Get(RunPath.Key)).Count == RunPath.Decode(def.Template.Parse(spec).Get(RunPath.Key)).Count,
                "the laid path has as many points as the placed one");
        }

        private static void LineOverSag()
        {
            // Placed straight over a sag (a line from the first point to the last at the cursor's height): the middle was 1.8 m above the road.
            SignDef def = Def("mk.line.edge");
            var position = new Vec3(0f, 5f, 0f);
            Func<float, float, float> sag = (x, z) => 5f + 0.0018f * (z - 30f) * (z - 30f) - 1.62f;
            string spec = Run(def, 60f, z => 5f);
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On(sag));
            Heights(def, laid, position, false, out _, out _, out List<Vec3> paint);
            float worst = 0f;
            foreach (Vec3 p in paint)
                worst = Math.Max(worst, Math.Abs(p.Y - sag(p.X, p.Z)));
            Program.Check(worst < 0.03f, "the paint follows the sag within 3 cm between its points: " + worst);
            Program.Near(laid.Worst, 1.62f, 0.01f, "it was 1.62 m above the bottom of the sag");
            // A crest the other way: the paint is never more than 3 cm under it (it would be hidden in the road).
            Func<float, float, float> crest = (x, z) => 5f - 0.0018f * (z - 30f) * (z - 30f) + 1.62f;
            laid = MarkingGround.Lay(def, spec, position, 0f, On(crest));
            Heights(def, laid, position, false, out _, out _, out paint);
            float under = 0f;
            foreach (Vec3 p in paint)
                under = Math.Max(under, crest(p.X, p.Z) - p.Y);
            Program.Check(under < 0.03f, "the paint is not under the crest: " + under);
        }

        private static void Unchanged()
        {
            SignDef def = Def("mk.line.lane");
            var position = new Vec3(10f, 3f, 20f);
            Func<float, float> slope = z => 3f + 0.04f * z;
            string spec = Run(def, 30f, slope);
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On((x, z) => slope(z - 20f)));
            Program.Near(laid.Dy, 0f, 0.002f, "the origin stays");
            Program.Check(Math.Abs(laid.Worst) < 0.005f, "it was on the ground: " + laid.Worst);
            string before = def.Template.Parse(spec).Get(RunPath.Key), after = def.Template.Parse(laid.Spec).Get(RunPath.Key);
            Program.Check(before == after, $"the path is written as it was: {before} / {after}");
        }

        private static void HumpOnSlope()
        {
            // A hump marking placed flat at the cursor before 0.12.30, on a road that falls 6 % to the item's right (+X) and rises 3 % along it.
            SignDef def = Def("mk.hump.small");
            var position = new Vec3(500f, 20f, 500f);
            string spec = def.Template.Default().ToString();
            Func<float, float, float> road = (x, z) => 20f - 0.06f * (x - 500f) + 0.03f * (z - 500f);
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On(road));
            Program.Check(laid != null && laid.Dy == 0f, "laid, its origin where it is");
            MarkingPlan plan = MarkingTemplates.PlanOf(def, laid.Spec);
            float worst = 0f;
            foreach (MarkingPart part in plan.Parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                    worst = Math.Max(worst, Math.Abs(position.Y + p.Y - part.Lift - road(position.X + p.X, position.Z + p.Z)));
            }

            Program.Check(worst < 0.02f, "every point of its paint is on the road: " + worst);
            Program.Check(Math.Abs(laid.Worst) > 0.05f, "it was off the road: " + laid.Worst);
            Program.Check(def.Template.Parse(laid.Spec).Get("g") == "0", "the grid takes the place of the grade");
        }

        private static void BarrierRaised()
        {
            // A W-beam placed on a road at 10 m that was raised to 10.5 m afterwards: the posts stood in the road.
            SignDef def = Def(MarkingTemplates.WBeamId);
            var position = new Vec3(0f, 10f, 0f);
            string spec = Run(def, 24f, z => 10f);
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, On((x, z) => 10.5f));
            Program.Near(laid.Dy, 0.5f, 0.002f, "it goes up with the road");
            Heights(def, laid, position, true, out float lo, out float hi, out _);
            Program.Near(lo, 10.5f - Barriers.Embed, 0.01f, "the posts go into the raised road as deep as they did");
            Program.Check(hi > 11.0f && hi < 11.4f, "the beam's top is over the raised road: " + hi);
        }

        private static void InTile()
        {
            SignDef def = Def("mk.line.lane");
            var position = new Vec3(1000f, 10.2f, 2000f);
            string spec = Run(def, 20f, z => 10.2f);
            var record = new ItemRecord(def.Hash, position, 0f, 0u, spec) { Laid = MarkingGround.Lay(def, spec, position, 0f, On((x, z) => 10f)) };
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { record }, new Vec3(960f, 0f, 1920f), out int skipped);
            Program.Check(skipped == 0 && parts.Count > 0, "built");
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (ChunkPart part in parts)
            {
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    lo = Math.Min(lo, p.Y);
                    hi = Math.Max(hi, p.Y);
                }
            }

            Program.Near(lo, 10f + ChunkBuilder.MarkingRaise, 0.01f, "the paint is the raise above the carriageway, not above the footway (lowest)");
            Program.Near(hi, 10f + ChunkBuilder.MarkingRaise, 0.01f, "(highest)");
            var placed = new ItemRecord(def.Hash, position, 0f, 0u, spec);
            Program.Check(!TileDiff.Same(placed, record), "a laid marking rebuilds its tile");
        }
    }
}
