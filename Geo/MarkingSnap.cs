using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Where across a road a kind of marking stands when it is put on the road: in a lane, over the lanes of its traffic, over the whole road, or just where the cursor is.</summary>
    public enum MarkingAnchor
    {
        /// <summary>Where the cursor is.</summary>
        Free,

        /// <summary>In the middle of the lane the cursor is nearest (an arrow, a word, a mark that belongs to one lane).</summary>
        Lane,

        /// <summary>Over all the lanes of the traffic that way (a stop line, a give-way line, rumble bars).</summary>
        Block,

        /// <summary>Over all the lanes of the road, both ways (a crosswalk, a hump).</summary>
        Road
    }

    /// <summary>Where a marking was put across the road: how far to the right of the road's centre line, and whether its traffic runs with the edge or against it.</summary>
    public struct MarkingSpot
    {
        /// <summary>Metres to the right of the centre line, looking the way the edge runs.</summary>
        public float Lateral;

        /// <summary>The marking's traffic runs the way the edge runs (+Z of the item is along the edge).</summary>
        public bool Forward;

        /// <summary>The lateral was taken from the lanes (a lane's middle, a line between lanes), not from the cursor.</summary>
        public bool Snapped;

        /// <summary>The lanes of the road were known; false when the cursor's side alone decided.</summary>
        public bool HadLanes;

        /// <summary>A barrier's line was put on a raised strip between the carriageways (BarrierLine): it keeps its place across the road and does not follow the road's edge.</summary>
        public bool OnRaised;
    }

    /// <summary>
    /// Where a marking goes across a road, worked out from the lanes the game keeps (their lateral places right of the edge's centre line, for the traffic that runs with the edge and for that which runs
    /// against it) and from the cursor. Pure: the tool reads the lanes, this decides. A marking is decoration only, so none of this changes a lane.
    /// </summary>
    public static class MarkingSnap
    {
        /// <summary>Lanes closer than this to each other (m) are one lane (the game keeps a lane curve for each connection, and a lane can be seen twice).</summary>
        public const float MergeWithin = 0.5f;

        /// <summary>A line of a run is pulled to a line between lanes (or the edge of the carriageway) that is this near the cursor (m); farther, it stays where the cursor is.</summary>
        public const float LineRange = 0.9f;

        /// <summary>The width of a lane when a road has one lane in a direction and nothing else tells (m; the game's roads have 3.0 to 3.75).</summary>
        public const float DefaultLane = 3.25f;

        /// <summary>The widest gap between two lanes' middles that still counts as neighbouring lanes (m): more is a median or a verge, and the lines are not drawn through it.</summary>
        public const float MaxNeighbourGap = 6.5f;

        /// <summary>Where the kind of marking stands across the road.</summary>
        public static MarkingAnchor AnchorOf(string id)
        {
            if (id == null)
                return MarkingAnchor.Free;
            if (id.StartsWith("mk.arrow.", StringComparison.Ordinal) || id == "mk.diamond" || id == "mk.noturn" || id == "mk.speed" || id == "mk.speedface")
                return MarkingAnchor.Lane;
            if (id == "mk.stop" || id.StartsWith("mk.giveway.", StringComparison.Ordinal) || id == "mk.rumble.transverse" || id == "mk.hump.warning")
                return MarkingAnchor.Block;
            if (id == "mk.crosswalk" || id == "mk.bicyclecrossing" || id == "mk.hump.large" || id == "mk.hump.small" || id == "mk.railway")
                return MarkingAnchor.Road;
            return MarkingAnchor.Free;
        }

        /// <summary>The numbers sorted, with those within MergeWithin of the ones before taken as the same (the mean of them is kept). Not a number and infinite values are dropped.</summary>
        public static List<float> Merged(IEnumerable<float> values)
        {
            var sorted = new List<float>();
            if (values != null)
            {
                foreach (float v in values)
                {
                    if (!float.IsNaN(v) && !float.IsInfinity(v))
                        sorted.Add(v);
                }
            }

            sorted.Sort();
            var result = new List<float>();
            int run = 0;
            float sum = 0f;
            foreach (float v in sorted)
            {
                if (run > 0 && v - sum / run > MergeWithin)
                {
                    result.Add(sum / run);
                    run = 0;
                    sum = 0f;
                }

                sum += v;
                run++;
            }

            if (run > 0)
                result.Add(sum / run);
            return result;
        }

        /// <summary>
        /// Where a marking that is one piece (an arrow, a stop line, a crosswalk) stands across the road, and the way its traffic runs: the lane nearest the cursor decides the way (its traffic runs with the
        /// edge when it is in forward, against it when it is in opposite), the anchor decides the place. With no lane read the side of the cursor decides the way (the right of the edge runs with it, right-hand
        /// traffic) and the marking stands where the cursor is.
        /// </summary>
        public static MarkingSpot Stamp(MarkingAnchor anchor, IList<float> forward, IList<float> opposite, float cursor)
        {
            List<float> with = Merged(forward), against = Merged(opposite);
            if (with.Count + against.Count == 0 || float.IsNaN(cursor))
                return new MarkingSpot { Lateral = float.IsNaN(cursor) ? 0f : cursor, Forward = !(cursor < 0f), Snapped = false, HadLanes = false };

            float best = float.MaxValue;
            bool forwardNearest = true;
            float nearest = 0f;
            foreach (float lane in with)
            {
                float d = Math.Abs(lane - cursor);
                if (d < best)
                {
                    best = d;
                    nearest = lane;
                    forwardNearest = true;
                }
            }

            foreach (float lane in against)
            {
                float d = Math.Abs(lane - cursor);
                if (d < best)
                {
                    best = d;
                    nearest = lane;
                    forwardNearest = false;
                }
            }

            var spot = new MarkingSpot { Lateral = cursor, Forward = forwardNearest, Snapped = false, HadLanes = true };
            switch (anchor)
            {
                case MarkingAnchor.Lane:
                    spot.Lateral = nearest;
                    spot.Snapped = true;
                    break;
                case MarkingAnchor.Block:
                    spot.Lateral = MiddleOf(forwardNearest ? with : against);
                    spot.Snapped = true;
                    break;
                case MarkingAnchor.Road:
                    spot.Lateral = MiddleOf(with, against);
                    spot.Snapped = true;
                    break;
            }

            return spot;
        }

        private static float MiddleOf(List<float> lanes) => lanes.Count == 0 ? 0f : (lanes[0] + lanes[lanes.Count - 1]) * 0.5f;

        private static float MiddleOf(List<float> a, List<float> b)
        {
            float low = float.MaxValue, high = float.MinValue;
            foreach (List<float> list in new[] { a, b })
            {
                foreach (float v in list)
                {
                    low = Math.Min(low, v);
                    high = Math.Max(high, v);
                }
            }

            return low > high ? 0f : (low + high) * 0.5f;
        }

        /// <summary>
        /// The places a line that runs along the road can lie at: between each two neighbouring lanes (the middle of them; where the traffic changes direction that is the centre line), and the outer edge of the
        /// outermost lanes, half a lane beyond their middles (a lane's width is taken from the gap to its neighbour, DefaultLane when it has none).
        /// </summary>
        public static List<float> LinePlaces(IList<float> forward, IList<float> opposite)
        {
            var all = new List<float>();
            if (forward != null)
                all.AddRange(forward);
            if (opposite != null)
                all.AddRange(opposite);
            List<float> lanes = Merged(all);
            var places = new List<float>();
            if (lanes.Count == 0)
                return places;
            float firstGap = lanes.Count > 1 ? Math.Min(lanes[1] - lanes[0], MaxNeighbourGap) : DefaultLane;
            float lastGap = lanes.Count > 1 ? Math.Min(lanes[lanes.Count - 1] - lanes[lanes.Count - 2], MaxNeighbourGap) : DefaultLane;
            places.Add(lanes[0] - firstGap * 0.5f);
            for (int i = 1; i < lanes.Count; i++)
            {
                if (lanes[i] - lanes[i - 1] <= MaxNeighbourGap)
                {
                    places.Add((lanes[i] + lanes[i - 1]) * 0.5f);
                }
                else
                {
                    // A median or a verge between: each side has its own edge.
                    places.Add(lanes[i - 1] + DefaultLane * 0.5f);
                    places.Add(lanes[i] - DefaultLane * 0.5f);
                }
            }

            places.Add(lanes[lanes.Count - 1] + lastGap * 0.5f);
            return places;
        }

        /// <summary>
        /// Where a marking that spans lanes lies and how long it is across the road (0.12.1): over the lanes of the traffic the cursor's lane is in (Block: a stop line, a give-way line) or over all the
        /// lanes (Road: a crosswalk), from the outer side of the first of them to that of the last; each lane reaches to the middle between it and its neighbour (the line between two lanes, the centre
        /// line between two directions), and an outermost lane half the gap to its neighbour beyond its middle (DefaultLane alone). Lanes is how many lanes it spans. With no lanes read, the spot of Stamp
        /// and a span of 0 (the caller keeps the settings' own).
        /// </summary>
        public static MarkingSpot Fit(MarkingAnchor anchor, IList<float> forward, IList<float> opposite, float cursor, out float span, out int lanes)
        {
            span = 0f;
            lanes = 0;
            MarkingSpot spot = Stamp(anchor, forward, opposite, cursor);
            if (!spot.HadLanes || (anchor != MarkingAnchor.Block && anchor != MarkingAnchor.Road))
                return spot;
            List<float> with = Merged(forward), against = Merged(opposite);
            var all = new List<float>(with);
            all.AddRange(against);
            List<float> sorted = Merged(all);
            if (sorted.Count == 0)
                return spot;
            // The reach of each lane across the road.
            var low = new float[sorted.Count];
            var high = new float[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                float below = i > 0 ? sorted[i] - sorted[i - 1] : float.MaxValue, above = i + 1 < sorted.Count ? sorted[i + 1] - sorted[i] : float.MaxValue;
                float own = Math.Min(below, above);
                float half = (own > MaxNeighbourGap ? DefaultLane : own) * 0.5f;
                low[i] = below <= MaxNeighbourGap ? sorted[i] - below * 0.5f : sorted[i] - half;
                high[i] = above <= MaxNeighbourGap ? sorted[i] + above * 0.5f : sorted[i] + half;
            }

            List<float> chosen = anchor == MarkingAnchor.Road ? sorted : (spot.Forward ? with : against);
            float from = float.MaxValue, to = float.MinValue;
            foreach (float lane in chosen)
            {
                int k = Nearest(sorted, lane);
                from = Math.Min(from, low[k]);
                to = Math.Max(to, high[k]);
            }

            if (from > to)
                return spot;
            span = to - from;
            lanes = chosen.Count;
            spot.Lateral = (from + to) * 0.5f;
            spot.Snapped = true;
            return spot;
        }

        private static int Nearest(List<float> sorted, float value)
        {
            int best = 0;
            for (int i = 1; i < sorted.Count; i++)
            {
                if (Math.Abs(sorted[i] - value) < Math.Abs(sorted[best] - value))
                    best = i;
            }

            return best;
        }

        /// <summary>A line that is not pulled to any line of the lanes: where the cursor is, or where the tool found a kerb's face for it (snapped, 0.12.33: the lanes do not tell where a kerb is).</summary>
        public static MarkingSpot FreeLine(float cursor, bool snapped = false) => new MarkingSpot { Lateral = float.IsNaN(cursor) ? 0f : cursor, Forward = !(cursor < 0f), Snapped = snapped, HadLanes = false };

        /// <summary>A raised strip this narrow (m) or less holds one barrier, in its middle; on a wider one the barrier goes where the cursor is, KerbInset from its sides at least.</summary>
        public const float NarrowStrip = 2f;

        public const float KerbInset = 0.3f;

        /// <summary>
        /// Where a barrier's line lies across the road (0.12.22): on a raised strip between the carriageways (SurfaceSection.Raised, laterals from the left to the right) when the cursor is over it or within
        /// KerbInset of it: a narrow one's middle, a wide one's place under the cursor (its middle within LineRange); else as Line, on a line between lanes or where the cursor is.
        /// </summary>
        public static MarkingSpot BarrierLine(IList<float> forward, IList<float> opposite, IList<(float Lo, float Hi)> raised, float cursor)
        {
            if (raised != null && !float.IsNaN(cursor))
            {
                foreach ((float lo, float hi) in raised)
                {
                    if (cursor < lo - KerbInset || cursor > hi + KerbInset)
                        continue;
                    float middle = (lo + hi) * 0.5f, at = middle;
                    if (hi - lo > NarrowStrip && Math.Abs(cursor - middle) > LineRange)
                        at = Math.Max(lo + KerbInset, Math.Min(hi - KerbInset, cursor));
                    return new MarkingSpot { Lateral = at, Forward = !(at < 0f), Snapped = true, HadLanes = true, OnRaised = true };
                }
            }

            return Line(forward, opposite, cursor);
        }

        /// <summary>Where a run's line lies across the road: the nearest of the line places when it is within LineRange of the cursor, else where the cursor is.</summary>
        public static MarkingSpot Line(IList<float> forward, IList<float> opposite, float cursor)
        {
            List<float> places = LinePlaces(forward, opposite);
            if (places.Count == 0 || float.IsNaN(cursor))
                return new MarkingSpot { Lateral = float.IsNaN(cursor) ? 0f : cursor, Forward = !(cursor < 0f), Snapped = false, HadLanes = false };
            float best = float.MaxValue, at = cursor;
            foreach (float place in places)
            {
                float d = Math.Abs(place - cursor);
                if (d < best)
                {
                    best = d;
                    at = place;
                }
            }

            bool snapped = best <= LineRange;
            return new MarkingSpot { Lateral = snapped ? at : cursor, Forward = !(cursor < 0f), Snapped = snapped, HadLanes = true };
        }
    }
}
