using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    internal static partial class GreeningTests
    {
        /// <summary>How far (m) the points of a mesh go across the plane x = 0 into the side where the traffic is not (dir: +1 the line's right), at heights between y0 and y1: the wall's back is there.</summary>
        private static float Beyond(MeshBuilder mesh, float dir, float y0, float y1)
        {
            float most = 0f;
            foreach (Vec3 p in mesh.Positions)
            {
                if (p.Y >= y0 && p.Y <= y1)
                    most = Math.Max(most, p.X * dir);
            }

            return most;
        }

        /// <summary>The boxes along a straight run (from, to: their outsides with the rims' lips), from their soil: the soil of a box has its two ends along it, BoxWall and the lip inside the box's ends.</summary>
        private static List<(float From, float To)> Boxes(MarkingPart soil)
        {
            var ends = new SortedSet<float>();
            foreach (Vec3 p in soil.Mesh.Positions)
                ends.Add((float)Math.Round(p.Z, 4));
            var list = new List<float>(ends);
            var boxes = new List<(float, float)>();
            for (int i = 0; i + 1 < list.Count; i += 2)
                boxes.Add((list[i] - Barriers.BoxWall - 0.012f, list[i + 1] + Barriers.BoxWall + 0.012f));
            return boxes;
        }

        /// <summary>
        /// The least gap (m, seen from above) between the rims of two boxes one after the other: each box's soil is one quad, its corners in order round it; its rim is that grown by the wall and the lip.
        /// The gap of two convex quads is the most they are apart along any of their edges' normals.
        /// </summary>
        private static float RimGap(MarkingPart soil)
        {
            var rims = new List<Vec3[]>();
            List<Vec3> q = soil.Mesh.Positions;
            const float grow = Barriers.BoxWall + 0.012f;
            for (int k = 0; k + 3 < q.Count; k += 4)
            {
                Vec3 u = (q[k + 1] - q[k]).HeadingXZ(), v = (q[k + 3] - q[k]).HeadingXZ();
                rims.Add(new[] { q[k] - u * grow - v * grow, q[k + 1] + u * grow - v * grow, q[k + 2] + u * grow + v * grow, q[k + 3] - u * grow + v * grow });
            }

            float least = float.MaxValue;
            for (int i = 1; i < rims.Count; i++)
            {
                float apart = float.MinValue;
                foreach (Vec3[] a in new[] { rims[i - 1], rims[i] })
                {
                    for (int e = 0; e < 4; e++)
                    {
                        Vec3 n = (a[(e + 1) % 4] - a[e]).HeadingXZ().RightXZ();
                        float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                        foreach (Vec3 c in rims[i - 1])
                        {
                            minA = Math.Min(minA, c.X * n.X + c.Z * n.Z);
                            maxA = Math.Max(maxA, c.X * n.X + c.Z * n.Z);
                        }

                        foreach (Vec3 c in rims[i])
                        {
                            minB = Math.Min(minB, c.X * n.X + c.Z * n.Z);
                            maxB = Math.Max(maxB, c.X * n.X + c.Z * n.Z);
                        }

                        apart = Math.Max(apart, Math.Max(minB - maxA, minA - maxB));
                    }
                }

                least = Math.Min(least, apart);
            }

            return least;
        }

        /// <summary>How far a point is to the right of a polyline (m, - its left): from the segment it is nearest to.</summary>
        private static float Lateral(List<Vec3> path, Vec3 q)
        {
            float best = float.MaxValue, lateral = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vec3 a = path[i - 1], d = path[i] - a;
                float length = d.LengthXZ, along = Math.Max(0f, Math.Min(length, ((q.X - a.X) * d.X + (q.Z - a.Z) * d.Z) / length));
                Vec3 foot = a + d.HeadingXZ() * along;
                float distance = Vec3.DistanceXZ(q, foot);
                if (distance < best)
                {
                    best = distance;
                    lateral = Vec3.Dot(q - foot, d.HeadingXZ().RightXZ());
                }
            }

            return lateral;
        }

        private static void Viaduct()
        {
            // Hung on the outer face (the traffic on the line's right: the outside on its left), the wall 1.0 m high and 0.3 m on top.
            MarkingPlan hung = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(6f));
            MarkingPart wall = Part(hung, SolidColour.Concrete), box = Part(hung, SolidColour.White), soil = Part(hung, SolidColour.Soil), steel = Part(hung, SolidColour.Metal);
            Program.Check(wall != null && box != null && soil != null && steel != null, "a wall, boxes, soil and steel brackets");
            var boxAndSoil = new MeshBuilder();
            boxAndSoil.Append(box.Mesh, Vec3.Zero, 0f);
            boxAndSoil.Append(soil.Mesh, Vec3.Zero, 0f);
            Program.Check(OpenEdges(wall.Mesh) == 0 && OpenEdges(boxAndSoil) == 0, "the wall is closed, and the boxes with their soil (seen from under the viaduct)");
            Program.Check(OpenEdges(box.Mesh) > 0, "(the box alone is open where its soil is)");
            Bounds(wall.Mesh.Positions, out Vec3 wlo, out Vec3 whi);
            Program.Check(Math.Abs(whi.Y - 1f) < 1e-4f && Math.Abs(wlo.X) < 1e-4f && whi.X > 0.3f && whi.X < 0.6f, $"the wall 1.0 m, its back on the line, its face toward the traffic: {wlo.X} .. {whi.X}");
            Bounds(box.Mesh.Positions, out Vec3 blo, out Vec3 bhi);
            Program.Check(bhi.X <= -Barriers.HungGap + 0.012f + 1e-4f && bhi.X < -0.008f - 0.005f && blo.X < -Barriers.HungGap - Barriers.BoxWidth + 0.01f, $"the boxes outside the wall's back, their rims clear of the brackets' bars on it: {blo.X} .. {bhi.X}");
            Program.Check(bhi.Y <= 1f - 0.05f + 1e-4f && blo.Y > 0.5f, "their rims 5 cm under the wall's top: " + bhi.Y);
            Program.Near(MaxY(soil), 0.95f - 0.03f, 1e-4f, "the soil 3 cm under the rim (沪绿容〔2021〕41号)");
            // Five boxes of 1 m with 4 cm between them in 6 m, centred, none past the ends.
            List<(float From, float To)> boxes = Boxes(soil);
            Program.Check(boxes.Count == 5 && boxes[0].From >= 0f && boxes[boxes.Count - 1].To <= 6f, $"five boxes in 6 m: {boxes.Count}, {boxes[0].From:0.000} .. {boxes[boxes.Count - 1].To:0.000}");
            for (int i = 1; i < boxes.Count; i++)
                Program.Near(boxes[i].From - boxes[i - 1].To, Barriers.BoxGap - 0.024f, 1e-3f, "the boxes' lips 16 mm apart on a straight");
            // Each box on two brackets: the steel under it touching its bottom, over the wall's top and against its back.
            int hooked = 0, under = 0;
            foreach ((float from, float to) in boxes)
            {
                List<Vec3> bracket = steel.Mesh.Positions.FindAll(p => p.Z > from && p.Z < to);
                hooked += bracket.Exists(p => p.Y > 1f && p.X > 0.1f) ? 1 : 0;
                under += bracket.Exists(p => Math.Abs(p.Y - (0.95f - Barriers.BoxDepth)) < 1e-3f && p.X < -0.3f) ? 1 : 0;
            }

            Program.Check(hooked == 5 && under == 5, $"every box hangs on brackets hooked over the wall: {hooked}, carried under its bottom: {under}");
            Program.Check(Beyond(steel.Mesh, 1f, 0f, 0.999f) < 1e-4f, "no bracket inside the wall's back");
            Program.Check(!steel.Mesh.Positions.Exists(p => p.Y > 1f && p.X > 0.16f), "the hook stops on the top, short of the traffic face");
            foreach (PropAnchor rose in hung.BakedProps)
            {
                Program.Check(rose.Kind == GreenPlants.Flower && Math.Abs(rose.Local.Y - 0.92f) < 1e-3f && rose.Local.X < -Barriers.HungGap && rose.Local.X > -Barriers.HungGap - Barriers.BoxWidth,
                    "a rose on the soil in its box: " + rose.Local);
                Program.Check(boxes.Exists(b => rose.Local.Z - rose.Room * 0.5f >= b.From - 1e-3f && rose.Local.Z + rose.Room * 0.5f <= b.To + 1e-3f), "inside one box along it");
            }

            Program.Check(hung.BakedProps.Count == 10, "two plants to a metre of box: " + hung.BakedProps.Count);

            // Astride: the saddle's skirts on both faces, the box over the top; on the top: sleepers bolted on.
            MarkingPlan saddle = PlanOf(MarkingTemplates.ViaductPlanterId, "mount=saddle", Straight(5f));
            MarkingPart skirts = Part(saddle, SolidColour.Metal);
            Bounds(skirts.Mesh.Positions, out Vec3 slo, out Vec3 shi);
            Program.Check(slo.Y < 0.75f && slo.X < -0.01f && shi.X > 0.3f, $"the saddle comes down both faces: x {slo.X} .. {shi.X}, down to {slo.Y}");
            Program.Check(!skirts.Mesh.Positions.Exists(p => p.Y < 0.99f && p.X > 0.005f && p.X < 0.29f), "no steel inside the wall");
            Bounds(Part(saddle, SolidColour.White).Mesh.Positions, out Vec3 sblo, out _);
            Program.Check(sblo.Y >= 1f - 1e-4f, "the box sits on the saddle over the wall");
            foreach ((string settings, float minWall) in new[] { ("mount=top", 1.04f), ("mount=top;box=green;length=150", 1.04f) })
            {
                MarkingPlan top = PlanOf(MarkingTemplates.ViaductPlanterId, settings, Straight(5f));
                MarkingPart topBox = top.Parts.Find(p => p.Solid != SolidColour.Concrete && p.Solid != SolidColour.Soil && p.Solid != SolidColour.Metal);
                Bounds(topBox.Mesh.Positions, out Vec3 tlo, out _);
                Program.Check(Math.Abs(tlo.Y - minWall) < 1e-3f, settings + ": the box on its sleepers on the top: " + tlo.Y);
                Program.Check(top.BakedProps.TrueForAll(p => p.Local.X > -0.05f && p.Local.X < 0.35f), settings + ": the plants over the wall");
            }

            // A trough cast with the wall: closed ends, its soil under the rim, no steel.
            MarkingPlan trough = PlanOf(MarkingTemplates.ViaductPlanterId, "mount=trough;plants=mixed", Straight(6f));
            Program.Check(Part(trough, SolidColour.Metal) == null && OpenEdges(Part(trough, SolidColour.Concrete).Mesh) == 0, "a trough: closed concrete and soil, no steel");
            Program.Check(trough.BakedProps.Exists(p => p.Kind == GreenPlants.Shrub) && trough.BakedProps.Exists(p => p.Kind == GreenPlants.Flower), "roses and shrubs two to one");
            Program.Check(trough.BakedProps.TrueForAll(p => p.Local.X < 0f && p.Local.X > -0.45f && p.Local.Z > 0.12f && p.Local.Z < 5.88f), "the plants inside the trough");

            // In a median: astride (a hung box would hang over the traffic), F-shaped both ways.
            MarkingPlan median = PlanOf(MarkingTemplates.ViaductPlanterId, "where=m;mount=hung", Straight(5f));
            Bounds(Part(median, SolidColour.Concrete).Mesh.Positions, out Vec3 mlo, out Vec3 mhi);
            Program.Check(Math.Abs(mlo.X + mhi.X) < 1e-3f, "a median wall is symmetric");
            Program.Check(Part(median, SolidColour.White).Mesh.Positions.TrueForAll(p => p.Y >= 1f - 1e-4f), "in a median the boxes sit astride, not hung");
            // Without the mod's wall: the boxes fit the player's wall as set.
            MarkingPlan own = PlanOf(MarkingTemplates.ViaductPlanterId, "where=l;wall=0;height=110;top=50", Straight(5f));
            Program.Check(Part(own, SolidColour.Concrete) == null, "no wall when the player has one");
            Bounds(Part(own, SolidColour.White).Mesh.Positions, out Vec3 olo, out Vec3 ohi);
            Program.Check(olo.X >= Barriers.HungGap - 0.012f - 1e-4f && Math.Abs(ohi.Y - 1.05f) < 1e-3f, "the traffic on the left: the boxes hung on the right, 5 cm under a 1.1 m top");

            // On bends of 30 m and 20 m both ways (the boxes on the outside of a right-hand bend, on the inside of a left-hand one), with boxes of 1 and 2 m, one rising 4 %: every mount clear of the
            // wall's faces, the boxes and the saddles' skirts out of it, the brackets on its back. The wall is swept along the line's chords: measured from them.
            foreach ((List<Vec3> line, string name) in new[] { (Arc(30f, 16f, false, 0.04f), "r 30 left rising"), (Arc(30f, 16f, true), "r 30 right"), (Arc(20f, 12f, false), "r 20 left"), (Arc(20f, 12f, true), "r 20 right") })
            {
                List<Vec3> path = RunPath.Decode(RunPath.Encode(line, out _));
                foreach (string settings in new[] { "", "length=200", "mount=saddle", "mount=saddle;length=200", "mount=top;length=200", "where=m;mount=saddle;length=200" })
                {
                    MarkingPlan bent = PlanOf(MarkingTemplates.ViaductPlanterId, settings, line);
                    bool inMedian = settings.Contains("where=m");
                    float top = 0.3f, faceLow = inMedian ? -0.15f : 0f;
                    MarkingPart bentBox = Part(bent, SolidColour.White), bentSteel = Part(bent, SolidColour.Metal);
                    // Inside the wall: across from its back (or a median wall's left face) to its other face, under its top.
                    float worst = 0f;
                    foreach (MeshBuilder mesh in new[] { bentBox.Mesh, bentSteel.Mesh })
                    {
                        foreach (Vec3 q in mesh.Positions)
                        {
                            if (q.Y >= 0.999f)
                                continue;
                            float x = Lateral(path, q), depth = Math.Min(x - faceLow, faceLow + top - x);
                            worst = Math.Max(worst, depth);
                        }
                    }

                    Program.Check(worst < 0.0015f, $"bend {name} [{settings}]: nothing in the wall under its top: {worst * 1000f:0.0} mm");
                    float closest = RimGap(Part(bent, SolidColour.Soil));
                    Program.Check(closest > 0.004f, $"bend {name} [{settings}]: two boxes' rims apart: {closest * 1000f:0.0} mm");
                    if (settings.Length == 0)
                    {
                        float nearest = float.MaxValue;
                        foreach (Vec3 q in bentBox.Mesh.Positions)
                            nearest = Math.Min(nearest, -Lateral(path, q));
                        Program.Check(nearest > 0.005f, $"bend {name}: a hung box's rim clear of the wall's back: {nearest * 1000f:0} mm");
                    }
                }
            }

            MarkingPlan sloped = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(6f, 0.04f));
            Program.Check(sloped.BakedProps.TrueForAll(p => Math.Abs(p.Local.Y - (0.92f + 0.04f * p.Local.Z)) < 1e-3f), "on a 4 % slope the plants on their boxes' soil");
            Program.Check(Part(sloped, SolidColour.White).Mesh.Positions.TrueForAll(p => p.Y <= 0.95f + 0.04f * p.Z + 1e-3f), "the rims under the sloping wall's top");
        }

        private static void ViaductRuns()
        {
            // 20 m in two pieces: the boxes of the whole, none cut at the join.
            MarkingPlan whole = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(20f), 0f, 20f);
            MarkingPlan a = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(10f), 0f, 20f), b = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(10f), 10f, 20f);
            var both = new List<Vec3>(a.BakedProps.ConvertAll(p => p.Local));
            both.AddRange(b.BakedProps.ConvertAll(p => p.Local + new Vec3(0f, 0f, 10f)));
            Program.Check(both.Count == whole.BakedProps.Count && whole.BakedProps.TrueForAll(p => both.Exists(q => (q - p.Local).Length < 1e-3f)), $"two pieces: the plants of the whole ({both.Count} of {whole.BakedProps.Count})");
            int Count(MarkingPlan plan) => Boxes(Part(plan, SolidColour.Soil)).Count;
            Program.Check(Count(a) + Count(b) == Count(whole), $"each box whole in one piece: {Count(a)} + {Count(b)} of {Count(whole)}");
            // The wall's ends capped at the run's ends only.
            MarkingPlan middle = PlanOf(MarkingTemplates.ViaductPlanterId, "", Straight(10f), 50f, 200f);
            MarkingPart wall = Part(middle, SolidColour.Concrete);
            Program.Check(!wall.Mesh.Normals.Exists(n => Math.Abs(n.Z) > 0.99f), "no cap in the middle of a run");
            Program.Check(Part(whole, SolidColour.Concrete).Mesh.Normals.Count(n => n.Z < -0.99f) > 0 && Part(whole, SolidColour.Concrete).Mesh.Normals.Count(n => n.Z > 0.99f) > 0, "capped at both ends of the whole");
            // A trough's end walls at the ends of the whole run only.
            MarkingPlan troughMiddle = PlanOf(MarkingTemplates.ViaductPlanterId, "mount=trough", Straight(10f), 50f, 200f);
            Program.Check(!Part(troughMiddle, SolidColour.Concrete).Mesh.Positions.Exists(p => p.Z > 0.01f && p.Z < 0.11f && p.X < -0.1f && p.Y > 0.6f), "no end wall in the middle of a trough");
        }

        private static void Bowls()
        {
            MarkingPlan stone = PlanOf(MarkingTemplates.FlowerBowlId, "where=m", Straight(12f));
            MarkingPart bowl = Part(stone, SolidColour.Granite, DetailKind.Stone), soil = Part(stone, SolidColour.Soil);
            Program.Check(bowl != null && soil != null && stone.BakedProps.Count == 3, "three stone bowls in 12 m at 5 m: " + stone.BakedProps.Count);
            Bounds(bowl.Mesh.Positions.Where(p => p.Z < 1f), out Vec3 lo, out Vec3 hi);
            Program.Check(Math.Abs(hi.X - 0.505f) < 1e-3f && Math.Abs(hi.Y - 0.68f) < 1e-3f && lo.Y < 0f, $"a bowl 1.0 m across, 0.68 m high, its foot in the ground: {hi.X} x {hi.Y}");
            foreach (PropAnchor p in stone.BakedProps)
                Program.Check(Math.Abs(p.Local.Y - 0.63f) < 1e-3f && p.Room <= 0.9f && p.Kind == GreenPlants.Flower, "a flowering bush on the soil 5 cm under the rim: " + p.Local);
            Program.Check(Math.Abs(MaxY(soil) - 0.63f) < 1e-3f, "the soil under the rim");
            MarkingPlan round = PlanOf(MarkingTemplates.FlowerBowlId, "where=m;style=round;colour=white;size=120;plants=shrub", Straight(1f));
            Bounds(Part(round, SolidColour.White).Mesh.Positions, out _, out Vec3 rhi);
            Program.Check(Math.Abs(rhi.X - 0.6f) < 1e-3f && Math.Abs(rhi.Y - 0.84f) < 1e-3f && round.BakedProps[0].Kind == GreenPlants.Shrub, "a round planter 1.2 m across, 0.84 m high, shrubs");
        }
    }
}
