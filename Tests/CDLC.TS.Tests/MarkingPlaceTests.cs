using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Where a road marking goes across a road (MarkingSnap) and how a line along a road is cut into items (MarkingRuns): the pure part of placing markings.</summary>
    internal static class MarkingPlaceTests
    {
        public static void RunAll()
        {
            Program.Run("marking place: the kinds of marking and where each stands across a road", Anchors);
            Program.Run("marking place: lanes that are the same lane are one", Merging);
            Program.Run("marking place: a stamp goes into the lane, over its direction, or over the road", Stamps);
            Program.Run("marking place: a line snaps to the lines between lanes and the edges", Lines);
            Program.Run("marking place: the line of a run along a curve, its ends and its direction", RunLine);
            Program.Run("marking place: a line is cut into items that join, each fits its settings", RunItems);
            Program.Run("marking place: a dashed line goes on in its pattern across the items", Phase);
        }

        private static Vec3 P(float x, float y, float z) => new Vec3(x, y, z);

        private static void Anchors()
        {
            foreach (string id in new[] { "mk.arrow.straight", "mk.arrow.left", "mk.diamond", "mk.noturn" })
                Program.Check(MarkingSnap.AnchorOf(id) == MarkingAnchor.Lane, id + " stands in a lane");
            foreach (string id in new[] { "mk.stop", "mk.giveway.stop", "mk.giveway.yield", "mk.rumble.transverse", "mk.hump.warning" })
                Program.Check(MarkingSnap.AnchorOf(id) == MarkingAnchor.Block, id + " spans the lanes of its direction");
            foreach (string id in new[] { "mk.crosswalk", "mk.hump.large", "mk.hump.small", "mk.railway" })
                Program.Check(MarkingSnap.AnchorOf(id) == MarkingAnchor.Road, id + " spans the whole road");
            Program.Check(MarkingSnap.AnchorOf("mk.bay.parallel") == MarkingAnchor.Free && MarkingSnap.AnchorOf(null) == MarkingAnchor.Free && MarkingSnap.AnchorOf("gb.prohib.01") == MarkingAnchor.Free, "the rest stand where the cursor is");
            // Every arrow of the catalogue is one the tool puts in a lane (an arrow added without being listed would stand wherever the cursor is).
            int arrows = 0;
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                if (t.Id.StartsWith("mk.arrow.", StringComparison.Ordinal))
                {
                    arrows++;
                    Program.Check(MarkingSnap.AnchorOf(t.Id) == MarkingAnchor.Lane, t.Id + " is anchored in a lane");
                }
            }

            Program.Check(arrows == 11, "eleven arrows: " + arrows);
        }

        private static void Merging()
        {
            List<float> merged = MarkingSnap.Merged(new[] { 3.5f, 3.2f, -3.4f, float.NaN, float.PositiveInfinity, -3.6f, 9f });
            Program.Check(merged.Count == 3, "two pairs of lanes and a single one: " + merged.Count);
            Program.Near(merged[0], -3.5f, 1e-4f, "the mean of the pair on the left");
            Program.Near(merged[1], 3.35f, 1e-4f, "the mean of the pair in the middle");
            Program.Near(merged[2], 9f, 1e-4f, "the single one");
            Program.Check(MarkingSnap.Merged(null).Count == 0 && MarkingSnap.Merged(new float[0]).Count == 0, "nothing in, nothing out");
            // A chain of lanes each within the merge distance of the one before is not one lane all the way: the distance is from the run's mean.
            List<float> chain = MarkingSnap.Merged(new[] { 0f, 0.4f, 0.8f, 1.2f });
            Program.Check(chain.Count >= 2, "a chain of near lanes is cut: " + chain.Count);
            Program.Check(MarkingSnap.Merged(new[] { 0f, 0.49f }).Count == 1 && MarkingSnap.Merged(new[] { 0f, 0.51f }).Count == 2, "half a metre is the limit");
        }

        private static void Stamps()
        {
            // A road with two lanes each way: with the edge at 1.75 and 5.25 right of the centre line, against it at -1.75 and -5.25 (right-hand traffic seen from the edge's direction: the lanes on the right run with it).
            var with = new List<float> { 1.75f, 5.25f };
            var against = new List<float> { -1.75f, -5.25f };

            MarkingSpot inLane = MarkingSnap.Stamp(MarkingAnchor.Lane, with, against, 4.8f);
            Program.Near(inLane.Lateral, 5.25f, 1e-4f, "an arrow goes to the middle of the lane that is nearest");
            Program.Check(inLane.Forward && inLane.Snapped && inLane.HadLanes, "with the edge, snapped, lanes known");
            MarkingSpot back = MarkingSnap.Stamp(MarkingAnchor.Lane, with, against, -2.2f);
            Program.Near(back.Lateral, -1.75f, 1e-4f, "on the other side, the lane there");
            Program.Check(!back.Forward, "against the edge: it faces the other way");

            MarkingSpot stop = MarkingSnap.Stamp(MarkingAnchor.Block, with, against, 5f);
            Program.Near(stop.Lateral, 3.5f, 1e-4f, "a stop line is over the middle of the two lanes of its direction");
            Program.Check(stop.Forward, "and faces with the edge");
            MarkingSpot stopBack = MarkingSnap.Stamp(MarkingAnchor.Block, with, against, -1f);
            Program.Near(stopBack.Lateral, -3.5f, 1e-4f, "the other way, over its two lanes");
            Program.Check(!stopBack.Forward, "against the edge");

            MarkingSpot cross = MarkingSnap.Stamp(MarkingAnchor.Road, with, against, 4f);
            Program.Near(cross.Lateral, 0f, 1e-4f, "a crosswalk is over the middle of the whole road");
            MarkingSpot oneWay = MarkingSnap.Stamp(MarkingAnchor.Road, with, null, 0f);
            Program.Near(oneWay.Lateral, 3.5f, 1e-4f, "of a one-way road, over its lanes");

            MarkingSpot free = MarkingSnap.Stamp(MarkingAnchor.Free, with, against, 2.6f);
            Program.Near(free.Lateral, 2.6f, 1e-4f, "a free marking stays where the cursor is");
            Program.Check(!free.Snapped && free.Forward, "not snapped, with the edge (the lane nearest is on the right)");

            // No lane read: the side of the cursor decides the way, the place is the cursor's.
            MarkingSpot none = MarkingSnap.Stamp(MarkingAnchor.Lane, null, null, -2f);
            Program.Check(!none.HadLanes && !none.Forward && Math.Abs(none.Lateral + 2f) < 1e-4f, "no lanes: the cursor's place and side");
            MarkingSpot nan = MarkingSnap.Stamp(MarkingAnchor.Lane, with, against, float.NaN);
            Program.Check(!nan.HadLanes && nan.Lateral == 0f, "a cursor that is not a number: the middle of the road");
            // A road with one lane each way: a stop line goes over that lane.
            MarkingSpot single = MarkingSnap.Stamp(MarkingAnchor.Block, new List<float> { 1.8f }, new List<float> { -1.8f }, 0.3f);
            Program.Near(single.Lateral, 1.8f, 1e-4f, "one lane: over it");
        }

        private static void Lines()
        {
            var with = new List<float> { 1.75f, 5.25f };
            var against = new List<float> { -1.75f, -5.25f };
            List<float> places = MarkingSnap.LinePlaces(with, against);
            Program.Check(places.Count == 5, "five places for four lanes: " + places.Count);
            Program.Near(places[0], -7f, 1e-3f, "the edge on the left, half a lane beyond the lane's middle");
            Program.Near(places[1], -3.5f, 1e-3f, "between two lanes the same way");
            Program.Near(places[2], 0f, 1e-3f, "the centre line");
            Program.Near(places[3], 3.5f, 1e-3f, "between two lanes the same way");
            Program.Near(places[4], 7f, 1e-3f, "the edge on the right");

            MarkingSpot snapped = MarkingSnap.Line(with, against, 0.4f);
            Program.Check(snapped.Snapped && Math.Abs(snapped.Lateral) < 1e-3f, "near the centre line it snaps to it");
            MarkingSpot loose = MarkingSnap.Line(with, against, 1.2f);
            Program.Check(!loose.Snapped && Math.Abs(loose.Lateral - 1.2f) < 1e-4f, "far from every place it stays where the cursor is: " + loose.Lateral);
            Program.Check(MarkingSnap.Line(with, against, 3.0f).Snapped && Math.Abs(MarkingSnap.Line(with, against, 3.0f).Lateral - 3.5f) < 1e-3f, "near the line between two lanes, on it");
            Program.Check(MarkingSnap.Line(with, against, MarkingSnap.LineRange - 0.01f).Snapped && !MarkingSnap.Line(with, against, MarkingSnap.LineRange + 0.01f).Snapped, "the range is the limit: just inside snaps, just outside does not");
            MarkingSpot edge = MarkingSnap.Line(with, against, 7.5f);
            Program.Check(edge.Snapped && Math.Abs(edge.Lateral - 7f) < 1e-3f, "near the edge, on it");

            // A median: the road has two carriageways with nothing between them (the lanes are more than 6.5 m apart): each carriageway has its own edges.
            List<float> median = MarkingSnap.LinePlaces(new List<float> { 4.5f, 8f }, new List<float> { -4.5f, -8f });
            Program.Check(median.Count == 6, "six places with a median: " + median.Count);
            bool inner = false;
            foreach (float place in median)
                inner |= Math.Abs(Math.Abs(place) - 2.875f) < 0.01f;
            Program.Check(inner, "each side of the median has its edge line, not one line through it");

            // One direction only: a lane has two lines, one each side.
            List<float> one = MarkingSnap.LinePlaces(new List<float> { 1.75f }, null);
            Program.Check(one.Count == 2 && Math.Abs(one[1] - one[0] - MarkingSnap.DefaultLane) < 1e-3f, "a lane alone is a lane's width wide: " + (one.Count == 2 ? (one[1] - one[0]).ToString() : "?"));
            Program.Check(MarkingSnap.LinePlaces(null, null).Count == 0, "no lanes, no places");
            MarkingSpot noLanes = MarkingSnap.Line(null, null, -3f);
            Program.Check(!noLanes.HadLanes && !noLanes.Snapped && Math.Abs(noLanes.Lateral + 3f) < 1e-4f, "no lanes: where the cursor is");
        }

        private static ArcTable Straight(float length) => new ArcTable(Bezier3.Line(P(0f, 0f, 0f), P(0f, 0f, length)));

        private static void RunLine()
        {
            ArcTable arc = Straight(100f);
            List<Vec3> line = MarkingRuns.Line(arc, 10f, 50f, 1.75f, 0.1f);
            Program.Check(line.Count == 21, "40 m in steps of 2 m: " + line.Count);
            Program.Near(line[0].X, 1.75f, 1e-3f, "right of the centre line of a road that runs north is east");
            Program.Near(line[0].Z, 10f, 1e-3f, "it starts at the start");
            Program.Near(line[line.Count - 1].Z, 50f, 1e-3f, "and ends at the end");
            Program.Near(line[0].Y, 0.1f, 1e-4f, "raised by the surface");

            List<Vec3> backwards = MarkingRuns.Line(arc, 50f, 10f, 1.75f, 0f);
            Program.Check(backwards.Count == line.Count && Math.Abs(backwards[0].Z - 50f) < 1e-3f && Math.Abs(backwards[backwards.Count - 1].Z - 10f) < 1e-3f, "dragged backwards: from the first click toward the second");
            bool descending = true;
            for (int i = 0; i + 1 < backwards.Count; i++)
                descending &= Math.Abs(backwards[i].Z - (50f - 2f * i)) < 1e-3f || i == backwards.Count - 2;
            Program.Check(descending, "every point of a backwards line is 2 m nearer the second click than the one before");

            // The points before the end do not move when the end does (the items before the last stay the same).
            List<Vec3> a = MarkingRuns.Line(arc, 10f, 50.3f, 1.75f, 0f), b = MarkingRuns.Line(arc, 10f, 52.1f, 1.75f, 0f);
            bool same = true;
            for (int i = 0; i < 20; i++)
                same &= (a[i] - b[i]).Length < 1e-4f;
            Program.Check(same, "moving the end leaves the points before it where they are");
            Program.Near(a[a.Count - 1].Z, 50.3f, 1e-3f, "the end is the end");
            // A last step that is nearly the end is not a point of its own.
            Program.Check(MarkingRuns.Line(arc, 10f, 50.2f, 0f, 0f).Count == 21, "a last point 20 cm before the end is the end: " + MarkingRuns.Line(arc, 10f, 50.2f, 0f, 0f).Count);

            Program.Check(MarkingRuns.Line(arc, 10f, 10.5f, 0f, 0f).Count == 0, "under a metre is a click that did not move");
            Program.Check(MarkingRuns.Line(arc, 10f, 11f, 0f, 0f).Count == 2, "a metre is a line");
            Program.Check(MarkingRuns.Line(Straight(5000f), 0f, 3500f, 0f, 0f).Count == 0 && MarkingRuns.Line(Straight(5000f), 0f, 2900f, 0f, 0f).Count > 1000, "3 km is the longest");
            Program.Check(MarkingRuns.Line(arc, float.NaN, 10f, 0f, 0f).Count == 0 && MarkingRuns.Line(arc, 0f, 10f, float.NaN, 0f).Count == 0 && MarkingRuns.Line(arc, 0f, float.PositiveInfinity, 0f, 0f).Count == 0 && MarkingRuns.Line(null, 0f, 10f, 0f, 0f).Count == 0, "numbers that are not numbers: no line");
            List<Vec3> clamped = MarkingRuns.Line(arc, -20f, 150f, 0f, 0f);
            Program.Check(clamped.Count > 2 && Math.Abs(clamped[0].Z) < 1e-3f && Math.Abs(clamped[clamped.Count - 1].Z - 100f) < 1e-3f, "past the ends of the road it is the road's length");

            // On a bend: the line keeps its distance from the centre line (lateral 3 m on a curve of radius 40 m).
            var curve = new Bezier3(P(0f, 0f, 0f), P(0f, 0f, 22f), P(11f, 0f, 40f), P(40f, 0f, 40f));
            ArcTable bent = new ArcTable(curve);
            List<Vec3> offset = MarkingRuns.Line(bent, 5f, bent.Length - 5f, 3f, 0f);
            Program.Check(offset.Count > 10, "a line on a bend: " + offset.Count);
            float worst = 0f;
            foreach (Vec3 p in offset)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i <= 200; i++)
                    nearest = Math.Min(nearest, Vec3.DistanceXZ(curve.Point(i / 200f), p));
                worst = Math.Max(worst, Math.Abs(nearest - 3f));
            }

            Program.Check(worst < 0.05f, "it stays 3 m from the centre line within 5 cm: " + worst);
        }

        /// <summary>Whether a point is under a triangle of a mesh.</summary>
        private static bool Under(MeshBuilder mesh, float x, float z)
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

        private static void Phase()
        {
            // A straight road 400 m long running north; a lane line along it from 10 m to 310 m, 6 m of paint and 9 m of gap (the pattern of the road's lane line), cut into items.
            ArcTable arc = Straight(400f);
            List<RunItem> items = MarkingRuns.Items(MarkingRuns.Line(arc, 10f, 310f, 0f, 0f));
            Program.Check(items.Count >= 5, "300 m is several items: " + items.Count);
            Program.Near(items[0].Start, 0f, 1e-4f, "the first item starts at the start");
            for (int i = 1; i < items.Count; i++)
            {
                List<Vec3> before = WorldPoints(items[i - 1]);
                Program.Near(items[i].Start, items[i - 1].Start + RunPath.Length(items[i - 1].Path), 0.05f, "item " + i + " starts where the one before it ended, along the line: " + items[i].Start);
            }

            GuideTemplate lane = MarkingTemplates.ById("mk.line.lane");
            string spec = lane.Parse(lane.Default().ToString()).With("pattern", "69").With("width", "15").ToString();
            int wrongWith = 0, wrongWithout = 0, samples = 0;
            for (float s = 0.1f; s < 299.9f; s += 0.37f)
            {
                // The pattern starts at the start of the line: 6 m on, 9 m off, over the whole of it. Samples close to an end of a dash are left out (the paint has a width in length of nothing, but the edges are exact).
                float within = s % 15f;
                if (Math.Abs(within - 6f) < 0.06f || within < 0.06f || within > 14.94f)
                    continue;
                bool expected = within < 6f;
                samples++;
                wrongWith += CoveredByItems(lane, items, spec, 10f + s, true) == expected ? 0 : 1;
                wrongWithout += CoveredByItems(lane, items, spec, 10f + s, false) == expected ? 0 : 1;
            }

            Program.Check(samples > 400 && wrongWith == 0, "with the phase every sample of the line is on or off as the one pattern says: " + wrongWith + " wrong of " + samples);
            Program.Check(wrongWithout > 20, "(without it the pattern would begin again in every item: " + wrongWithout + " wrong)");

            // Half discs every 50 m of the whole line, not every 50 m of every item: 200 m has five.
            GuideTemplate disc = MarkingTemplates.ById("mk.halfdisc");
            List<RunItem> edge = MarkingRuns.Items(MarkingRuns.Line(arc, 0f, 200f, 0f, 0f));
            int triangles = 0;
            string discSpec = disc.Default().ToString();
            foreach (RunItem item in edge)
                triangles += MarkingTemplates.PlanOf(disc, MarkingTemplates.WithRun(disc, discSpec, item.Path, item.Start)).TriangleCount;
            Program.Check(edge.Count >= 3 && triangles % 11 == 0 && triangles / 11 == 5, "five half discs along 200 m, one each 50 m of the whole line (11 triangles each): " + triangles / 11.0 + " in " + edge.Count + " items");

            // A template that is not a run is not given a phase.
            GuideTemplate stop = MarkingTemplates.ById("mk.stop");
            Program.Check(MarkingTemplates.WithRun(stop, stop.Default().ToString(), "100:0:0", 12f) == stop.Default().ToString(), "a marking that is not a line takes neither a path nor a phase");
            Program.Check(MarkingTemplates.WithRun(lane, spec, "100:0:0", float.NaN) == MarkingTemplates.WithPath(lane, spec, "100:0:0"), "a phase that is not a number is none");
        }

        /// <summary>Whether the paint of the items covers the point of the road at distance s along the line (the line runs north from z = 10 at x = 0, so the world z of the point is its distance).</summary>
        private static bool CoveredByItems(GuideTemplate template, List<RunItem> items, string spec, float z, bool withPhase)
        {
            foreach (RunItem item in items)
            {
                string itemSpec = withPhase ? MarkingTemplates.WithRun(template, spec, item.Path, item.Start) : MarkingTemplates.WithPath(template, spec, item.Path);
                MarkingPlan plan = MarkingTemplates.PlanOf(template, itemSpec);
                foreach (MarkingPart part in plan.Parts)
                {
                    if (Under(part.Mesh, 0f - item.Position.X, z - item.Position.Z))
                        return true;
                }
            }

            return false;
        }

        /// <summary>The world points of an item's path: its start, turned by its yaw.</summary>
        private static List<Vec3> WorldPoints(RunItem item)
        {
            float s = (float)Math.Sin(item.Yaw), c = (float)Math.Cos(item.Yaw);
            var world = new List<Vec3>();
            foreach (Vec3 p in RunPath.Decode(item.Path))
                world.Add(new Vec3(p.X * c + p.Z * s + item.Position.X, p.Y + item.Position.Y, -p.X * s + p.Z * c + item.Position.Z));
            return world;
        }

        private static void RunItems()
        {
            // 150 m straight: items of at most 32 pieces, joined end to end.
            ArcTable arc = Straight(200f);
            List<Vec3> line = MarkingRuns.Line(arc, 20f, 170f, 0f, 0f);
            List<RunItem> items = MarkingRuns.Items(line);
            Program.Check(items.Count >= 3, "150 m is several items: " + items.Count);
            float total = 0f;
            for (int i = 0; i < items.Count; i++)
            {
                Program.Check(items[i].Path.Length > 0 && items[i].Path.Length <= MarkingRuns.MaxPathChars, "item " + i + " fits the settings: " + items[i].Path.Length);
                Program.Check(items[i].Path.Split('|').Length <= RunPath.MaxPieces, "item " + i + " has at most 32 pieces");
                total += RunPath.Length(items[i].Path);
                if (i > 0)
                {
                    List<Vec3> before = WorldPoints(items[i - 1]);
                    Program.Check((before[before.Count - 1] - items[i].Position).Length < 0.05f, "item " + i + " starts where the one before ends: " + (before[before.Count - 1] - items[i].Position).Length);
                }
            }

            Program.Near(total, 150f, 0.5f, "the items add up to the line");

            // A tight bend with a rise (a ramp's curve): paths are longer text; they still fit, and the pieces still join.
            var curve = new Bezier3(P(0f, 0f, 0f), P(0f, 6f, 60f), P(60f, 12f, 60f), P(60f, 18f, 0f));
            ArcTable bent = new ArcTable(curve);
            List<RunItem> ramp = MarkingRuns.Items(MarkingRuns.Line(bent, 0f, bent.Length, 2f, 0f));
            Program.Check(ramp.Count >= 2, "a long bend is several items: " + ramp.Count);
            foreach (RunItem item in ramp)
                Program.Check(item.Path.Length <= MarkingRuns.MaxPathChars, "a bend's path fits: " + item.Path.Length);
            for (int i = 1; i < ramp.Count; i++)
            {
                List<Vec3> before = WorldPoints(ramp[i - 1]);
                Program.Check((before[before.Count - 1] - ramp[i].Position).Length < 0.1f, "the bend's items join: " + (before[before.Count - 1] - ramp[i].Position).Length);
            }

            // The last point of the last item is the end of the line, in height too.
            List<Vec3> last = WorldPoints(ramp[ramp.Count - 1]);
            List<Vec3> drawn = MarkingRuns.Line(bent, 0f, bent.Length, 2f, 0f);
            Program.Check((last[last.Count - 1] - drawn[drawn.Count - 1]).Length < 0.15f, "the items end where the line ends: " + (last[last.Count - 1] - drawn[drawn.Count - 1]).Length);

            // A spiral ramp of 8 m radius rising 12 %: a piece is "200:2500:120" and 32 of them are more text than a setting holds, so the item is cut in two (and joins).
            var spiral = new List<Vec3>();
            for (int i = 0; i < 40; i++)
            {
                double a = i * 0.25;
                spiral.Add(P((float)(8.0 * Math.Cos(a)), i * 0.24f, (float)(8.0 * Math.Sin(a))));
            }

            List<RunItem> coil = MarkingRuns.Items(spiral);
            bool cut = false;
            foreach (RunItem item in coil)
            {
                Program.Check(item.Path.Length <= MarkingRuns.MaxPathChars, "a tight ramp's path fits: " + item.Path.Length);
                cut |= item.Path.Split('|').Length < RunPath.MaxPieces && coil.Count > 1;
            }

            Program.Check(coil.Count >= 3 && cut, "32 pieces of a tight ramp are too much text, so it is cut in more items than 32 pieces make: " + coil.Count);
            for (int i = 1; i < coil.Count; i++)
            {
                List<Vec3> before = WorldPoints(coil[i - 1]);
                Program.Check((before[before.Count - 1] - coil[i].Position).Length < 0.1f, "the ramp's items join: " + (before[before.Count - 1] - coil[i].Position).Length);
                Program.Near(coil[i].Start, coil[i - 1].Start + RunPath.Length(coil[i - 1].Path), 0.1f, "and each starts along the line where the one before it ended, also when an item was cut in two for its text: " + coil[i].Start);
            }

            Program.Check(MarkingRuns.Items(null).Count == 0 && MarkingRuns.Items(new List<Vec3>()).Count == 0 && MarkingRuns.Items(new List<Vec3> { P(0f, 0f, 0f) }).Count == 0, "no line, no items");
            Program.Check(MarkingRuns.Items(new List<Vec3> { P(0f, 0f, 0f), P(float.NaN, 0f, 5f) }).Count == 0, "a point that is not a number: no items");

            // An item is a marking of the catalogue: its paint is a plan with the length of the item.
            GuideTemplate lane = MarkingTemplates.ById("mk.line.whitesolid");
            RunItem first = items[0];
            MarkingPlan plan = MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), first.Path));
            Program.Check(plan.TriangleCount > 0 && plan.MaxZ - plan.MinZ > RunPath.Length(first.Path) - 0.5f, "the paint of an item is as long as its path: " + (plan.MaxZ - plan.MinZ) + " / " + RunPath.Length(first.Path));
        }
    }
}
