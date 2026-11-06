using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The kind of road a stretch is, for the barriers it gets (BarrierAuto).</summary>
    public enum RoadClass
    {
        /// <summary>高速公路、城市快速路: the game's highway rules.</summary>
        Highway,

        /// <summary>国道、省道: no footway, 60 km/h or more.</summary>
        National,

        /// <summary>城市道路: footways on both sides.</summary>
        City,

        /// <summary>村道: the rest.</summary>
        Village
    }

    /// <summary>What a barrier does at a junction the road crosses (0.12.16; the owner: "既能自动让开路口也应该得能不忽略路口，做那种汇入道路").</summary>
    public enum JunctionMode
    {
        /// <summary>It goes on on a side where no other road joins (the far side of a merge, of a fork, of a T), and is cut where one does.</summary>
        Side,

        /// <summary>It is cut at every junction.</summary>
        Cut,

        /// <summary>It goes on through every junction.</summary>
        Through
    }

    /// <summary>One barrier the automatic placing puts along a stretch: its item, its settings, where across the chain it runs (m right of the chain's direction) and from where to where (m along it).</summary>
    public sealed class BarrierRun
    {
        public string Id;
        public readonly List<KeyValuePair<string, string>> Settings = new List<KeyValuePair<string, string>>();
        public float Lateral, S0, S1;

        /// <summary>A roadside barrier follows the road's edge on this side (+1 the chain's right, -1 its left; 0: it keeps Lateral): EdgeOffset beyond the edge there (EdgeProfile).</summary>
        public int EdgeSide;
        public float EdgeOffset;

        public float Length => S1 - S0;

        public string Setting(string key) => Settings.Find(kv => kv.Key == key).Value;

        private EdgeProfile m_Edge;

        /// <summary>Where across the chain it runs at a distance along it.</summary>
        public float LateralAt(RoadChain chain, float s)
        {
            if (EdgeSide == 0)
                return Lateral;
            if (m_Edge == null || !m_Edge.Of(chain, EdgeSide))
                m_Edge = new EdgeProfile(chain, EdgeSide);
            return m_Edge.LateralAt(s, EdgeOffset);
        }
    }

    /// <summary>
    /// How far from the chain's centre line the road's edge is on a side (m; side +1 its right, -1 its left): the half width of the road less BarrierAuto.EdgeInset, and never less than the outermost lane
    /// (car or cycle) on that side, its half and 0.3 m. Between two pieces it changes smoothly: across a junction from where one piece's lanes end to where the next one's start, and over 3 m each side of a
    /// node the road goes on through. A barrier that goes on through a merge follows the edge of the road it goes on along.
    /// Worked out once for a side of a chain (0.12.51; the owner: "优化超长链摆放时候的掉帧问题"): a roadside line asks at every point, about 1500 on 3 km, and a drawn line is made again at every half
    /// metre its end moves; until then each question made the chain's tables again. The chain's pieces do not change once it is made.
    /// </summary>
    public sealed class EdgeProfile
    {
        private readonly RoadChain m_Chain;
        private readonly int m_Side;
        private readonly float[] m_From, m_To, m_Edge;

        /// <summary>
        /// 0.12.99 (the owner, 5 Nov 20:2x, a kink in a wall at a node where the road widened from 12 to 16 m and its pieces were 2.0 m to the side of each other: the wall moved 2.3 m across in 2 m along, turns
        /// of +41 and -55 degrees): the edges in the frame of the chain's first piece, each with the steps of the centre line before it (the jogs of the joins, toward this profile's side) added, so that the
        /// profile changes by what the road's edge does in the world, smoothly, and the step the chain's centre line makes across a jog is taken off again where it is made (At).
        /// </summary>
        private readonly float[] m_EdgeAbs;

        /// <summary>The jogs the centre line makes: the distance where each is made, how long it takes along the chain, and how far toward this profile's side it is.</summary>
        private readonly List<(float S, float Chord, float Jog)> m_Steps = new List<(float, float, float)>();

        /// <summary>The least jog (m) that is taken into account: below it the step is a few centimetres and the legacy profile is as it was.</summary>
        public const float MinJog = 0.25f;

        /// <summary>How long (m per m of change) the edge is spread over when it changes across a jogged node, and at least and at most how long.</summary>
        private const float kSpreadPerMetre = 6f, kSpreadLeast = 6f, kSpreadMost = 24f;

        /// <summary>The widest gap (m) between where one piece's lanes end and the next's start that is a node the road goes on through: the edge changes over 6 m there. A wider gap is a junction's.</summary>
        private const float kThroughGap = 6f;

        public EdgeProfile(RoadChain chain, int side)
        {
            m_Chain = chain;
            m_Side = side;
            int n = chain == null ? 0 : chain.Pieces.Count;
            m_From = new float[n];
            m_To = new float[n];
            m_Edge = new float[n];
            m_EdgeAbs = new float[n];
            for (int i = 0; i < n; i++)
            {
                ChainPiece p = chain.Pieces[i];
                m_From[i] = i == 0 ? float.MinValue : p.LanesS0;
                m_To[i] = i == n - 1 ? float.MaxValue : p.LanesS1;
                m_Edge[i] = BarrierAuto.EdgeOf(p, side);
            }

            // The jogs: the centre line of the chain steps across from one piece to the next by a metre or two at a node whose pieces are side by side. A step toward this profile's side moves the road's edge
            // out by as much, away from it in by as much (a profile on the left, side -1, is on the other side of a jog to the right).
            var jogs = new ChainJoin[n];
            var jogged = new bool[n];
            float cumulative = 0f;
            if (n > 0)
                m_EdgeAbs[0] = m_Edge[0];
            if (chain != null)
            {
                foreach (ChainJoin join in chain.Joins)
                {
                    if (join.Piece >= 0 && join.Piece + 1 < n && Math.Abs(join.Jog) >= MinJog)
                    {
                        jogs[join.Piece] = join;
                        jogged[join.Piece] = true;
                    }
                }
            }

            for (int i = 0; i + 1 < n; i++)
            {
                float jog = jogged[i] ? side * jogs[i].Jog : 0f;
                cumulative += jog;
                m_EdgeAbs[i + 1] = m_Edge[i + 1] + cumulative;
                if (jogged[i])
                    m_Steps.Add((jogs[i].S, Math.Max(0.05f, jogs[i].Chord), jog));
            }

            for (int i = 0; i + 1 < n; i++)
            {
                // A break (0.12.100): each piece holds its own edge to its end, and the next piece's begins at its start; where a line across it goes from one to the other is BreakBridge's, in the plane. The
                // blend over the lanes' gap pulled a wall 1.4 m in from the edge of a road 14 m out in the 13 m before the break at the owner's junction.
                if (chain.BrokenAfter(i))
                {
                    m_To[i] = chain.Pieces[i].S1;
                    m_From[i + 1] = chain.Pieces[i + 1].S0;
                    continue;
                }

                // Where one piece's lanes end where the next's start, or leave a small gap (a node the road goes on through; 0.12.60: 0.5 m until then, and a gap of 1 m made the half width's 0.5 m change in one
                // chord of the owner's barrier), the change is spread over 3 m each side of it, or over the gap when that is wider.
                if (m_From[i + 1] - m_To[i] < kThroughGap)
                {
                    float spread = Math.Min(3f, Math.Min((chain.Pieces[i].S1 - chain.Pieces[i].S0) * 0.25f, (chain.Pieces[i + 1].S1 - chain.Pieces[i + 1].S0) * 0.25f));
                    float middle = (m_From[i + 1] + m_To[i]) * 0.5f;
                    m_To[i] = Math.Min(m_To[i], middle - spread);
                    m_From[i + 1] = Math.Max(m_From[i + 1], middle + spread);
                    if (jogged[i])
                        SpreadAcrossJog(chain, i, jogs[i], i == 0 ? chain.Pieces[0].S0 : m_From[i], i + 1 == n - 1 ? chain.Pieces[i + 1].S1 : m_To[i + 1]);
                }
            }
        }

        /// <summary>
        /// A through node whose pieces are side by side: the edge, in the frame of the first piece, changes by D (the next piece's edge plus the jog less this piece's) and is spread over a length that
        /// grows with D (6 m for each metre, from 6 to 24 m) on the side of the node where the wall is not in the road: **before** the node when the road gets wider there (D above 0: the wall is out
        /// already where the road widens), **after** it when it gets narrower. The legacy window, 6 m centred on the node, put 4 m of change into 6 m at the owner's node.
        /// </summary>
        private void SpreadAcrossJog(RoadChain chain, int i, ChainJoin join, float plateauStart, float plateauEnd)
        {
            float change = m_EdgeAbs[i + 1] - m_EdgeAbs[i];
            float length = Math.Max(kSpreadLeast, Math.Min(kSpreadMost, kSpreadPerMetre * Math.Abs(change)));
            if (change >= 0f)
            {
                // Out before the node: the plateau of this piece ends length before it (not before the plateau began), the next one's begins where the chord across the node ends.
                float from = Math.Max(plateauStart + 0.5f, join.S - length);
                if (join.S - from < 2f)
                    return;
                m_To[i] = from;
                m_From[i + 1] = join.S + join.Chord;
            }
            else
            {
                // In after the node: this piece's plateau holds to the node, the next one's begins length after it (not after the plateau ends).
                float to = Math.Min(plateauEnd - 0.5f, join.S + join.Chord + length);
                if (to - join.S < 2f)
                    return;
                m_To[i] = join.S;
                m_From[i + 1] = to;
            }
        }

        /// <summary>
        /// Where a line offset from the edge runs at a distance along the chain (m right of it): on the profile's side, never nearer the centre line than MinFromCentre (0.12.52: a barrier put well
        /// inside the edge kept that offset where the road narrowed and went over the centre line to the other side).
        /// </summary>
        public float LateralAt(float s, float offset) => m_Side * Math.Max(MinFromCentre, At(s) + offset);

        public const float MinFromCentre = 0.5f;

        /// <summary>Whether this is the profile of that side of that chain.</summary>
        public bool Of(RoadChain chain, int side) => ReferenceEquals(chain, m_Chain) && side == m_Side;

        /// <summary>The edge's distance from the chain's centre line at a distance along it (m).</summary>
        public float At(float s)
        {
            int n = m_Edge.Length;
            if (n == 0)
                return 3f;
            return Absolute(s, n) - StepsBefore(s);
        }

        /// <summary>The edge in the frame of the first piece's line (m): the pieces' edges with the jogs before them added, blended where two pieces meet.</summary>
        private float Absolute(float s, int n)
        {
            for (int i = 0; i < n; i++)
            {
                if (s >= m_From[i] && s <= m_To[i])
                    return m_EdgeAbs[i];
                if (i + 1 < n && s > m_To[i] && s < m_From[i + 1])
                {
                    float t = (s - m_To[i]) / Math.Max(1e-4f, m_From[i + 1] - m_To[i]);
                    t = t * t * (3f - 2f * t);
                    return m_EdgeAbs[i] + (m_EdgeAbs[i + 1] - m_EdgeAbs[i]) * t;
                }
            }

            return m_EdgeAbs[n - 1];
        }

        /// <summary>How far the chain's centre line has stepped toward this profile's side at a distance along it (m): each jog is made over its chord, straight, from where the piece before it ends.</summary>
        private float StepsBefore(float s)
        {
            float sum = 0f;
            for (int k = 0; k < m_Steps.Count; k++)
            {
                (float at, float chord, float jog) = m_Steps[k];
                sum += jog * Math.Max(0f, Math.Min(1f, (s - at) / chord));
            }

            return sum;
        }
    }

    /// <summary>
    /// Barriers along a stretch of road by its kind (0.12.15, bar.auto; the owner's "城市道路/国道/村道"): a W-beam by both sides of a highway and a national road and in a highway's median (concrete where the
    /// opposing lanes are close), a city road's median fence where two lanes or more go each way, warning posts by a village road. Each line runs where the road's lanes run, through the nodes where the road
    /// only goes on, and is cut at each junction and each crosswalk across it, with a clearance. The kinds and the rules are this mod's simplification of the usual practice (GB 5768.2 and JTG D81 leave the
    /// place of a barrier to the road's design), decoration only. Pure.
    /// </summary>
    public static class BarrierAuto
    {
        public const string Id = "bar.auto";

        public const string ClassKey = "road", SidesKey = "sides", MedianKey = "median", RangeKey = "range", JunctionKey = "jn", GlareKey = "glare";

        /// <summary>
        /// A barrier drawn along a road (0.12.73; the owner: "弯道墙停在路口前"): "j" runs the line's two ends on to the node (RoadChain.Spans toNodes), "l" stops them where the lanes end. Used when it is
        /// placed, as JunctionKey is; the path of an item that was placed is baked, so an old item without the key is as it was drawn.
        /// </summary>
        public const string EndsKey = "nd";

        public static bool EndsAtNodes(GuideSpec spec) => !spec.Is(EndsKey, "l");

        public static JunctionMode JunctionOf(GuideSpec spec) => spec.Is(JunctionKey, "cut") ? JunctionMode.Cut : spec.Is(JunctionKey, "through") ? JunctionMode.Through : JunctionMode.Side;

        /// <summary>The edge of one piece on a side (EdgeProfile).</summary>
        public static float EdgeOf(ChainPiece piece, int side)
        {
            float outer = 0f;
            bool any = false;
            foreach (List<float> lanes in new[] { piece.With, piece.Against })
            {
                foreach (float x in lanes)
                {
                    outer = Math.Max(outer, side > 0 ? x : -x);
                    any = true;
                }
            }

            foreach (List<float> lanes in new[] { piece.BikeWith, piece.BikeAgainst })
            {
                foreach (float x in lanes)
                {
                    outer = Math.Max(outer, (side > 0 ? x : -x) + piece.BikeWidth * 0.5f - MarkingSnap.DefaultLane * 0.5f);
                    any = true;
                }
            }

            // Beyond the outermost lane by its half and 30 cm, where the road is no wider (0.12.16: 2.2 m put it outside a motorway whose 4 m lanes fill it); but never outside the road itself (0.12.65: a
            // lane read at 3.56 m in a road 4.5 m to a side put the edge at 5.61 m, and the owner's wall went 1.4 m off the road at its end).
            float edge = Math.Max(Math.Max(1.5f, piece.HalfWidth - EdgeInset), any ? outer + MarkingSnap.DefaultLane * 0.5f + 0.3f : 0f);
            return Math.Min(edge, Math.Max(1.5f, piece.HalfWidth));
        }

        /// <summary>
        /// The edge of a road that has not been read as a chain yet (the line's start under the cursor): EdgeOf for a road of half the width and these lanes (with the segment, against it).
        /// </summary>
        public static float EdgeOfRoad(float halfWidth, IList<float> with, IList<float> against, int side)
        {
            var piece = new ChainPiece { HalfWidth = Math.Max(2f, halfWidth) };
            if (with != null)
                piece.With.AddRange(with);
            if (against != null)
                piece.Against.AddRange(against);
            return EdgeOf(piece, side);
        }

        /// <summary>
        /// 0.12.101 (the owner, 5 Nov 23:5x, after the guard rail of his wide road bend: "这个吸附有一点点问题"; his log of the placement: "+3.66 m at the start ... along the edge, -0.49 m off it"): the lines a
        /// line along the road snaps to (MarkingSnap.LinePlaces) include the outer edge of the outermost lane, which is the painted edge line, 0.49 m inside the edge a roadside barrier is given (0.35 m
        /// inside the road's edge, EdgeOf): the rail stood 0.84 m inside the road's edge, on the carriageway. A roadside barrier whose line snapped to that outermost line is put at the road's edge
        /// (edge: how far from the centre line it is on that side) instead, so that the offset from it is 0 and the player's own offset (外移) comes from there. A line that snapped to any other place (between
        /// lanes, on a kerb strip), or that took the cursor's place, is as it was. Pure.
        /// </summary>
        public static MarkingSpot RoadsideSpot(MarkingSpot spot, IList<float> with, IList<float> against, float edge)
        {
            if (!spot.Snapped || spot.OnRaised || !(edge > 0f))
                return spot;
            List<float> places = MarkingSnap.LinePlaces(with, against);
            if (places.Count == 0)
                return spot;
            int side = spot.Lateral >= 0f ? 1 : -1;
            float outermost = places[0];
            foreach (float place in places)
                outermost = side > 0 ? Math.Max(outermost, place) : Math.Min(outermost, place);
            if (Math.Abs(spot.Lateral - outermost) > 0.01f)
                return spot;
            spot.Lateral = side * edge;
            return spot;
        }

        /// <summary>How far a barrier stays from a crosswalk across the road (m, each side of it).</summary>
        public const float CrosswalkClearance = 1f;

        /// <summary>How far a roadside barrier stands inside the road's outer edge (m): on the shoulder's edge.</summary>
        public const float EdgeInset = 0.35f;

        /// <summary>The gap between the opposing lanes (m, from the middles of the nearest lanes, less a lane) under which a median is a concrete barrier on a highway (no room for a W-beam's posts) and a city road has a fence.</summary>
        public const float NarrowMedian = 2f;

        /// <summary>The kind of a stretch from what its roads are: the game's highway rules, footways on both sides, the speed limit.</summary>
        public static RoadClass Classify(bool highway, RoadChain chain)
        {
            if (highway)
                return RoadClass.Highway;
            if (chain == null || chain.Pieces.Count == 0)
                return RoadClass.Village;
            float footed = 0f, fast = 0f, total = 0f;
            foreach (ChainPiece p in chain.Pieces)
            {
                float length = Math.Max(0.1f, p.S1 - p.S0);
                total += length;
                if (p.KerbLeft && p.KerbRight)
                    footed += length;
                if (p.HasSpeed && p.Kmh >= 60f)
                    fast += length;
            }

            if (footed >= total * 0.5f)
                return RoadClass.City;
            return fast >= total * 0.5f ? RoadClass.National : RoadClass.Village;
        }

        public static RoadClass ClassOf(GuideSpec spec, bool highway, RoadChain chain)
        {
            switch (spec.Get(ClassKey))
            {
                case "hw": return RoadClass.Highway;
                case "nat": return RoadClass.National;
                case "city": return RoadClass.City;
                case "vil": return RoadClass.Village;
                default: return Classify(highway, chain);
            }
        }

        /// <summary>A barrier line of one piece: its kind (a key that is the same for the same barrier in the next piece), item, settings and lateral.</summary>
        private struct PieceLine
        {
            public string Key, Id;
            public KeyValuePair<string, string>[] Settings;
            public float Lateral;
        }

        private static KeyValuePair<string, string> S(string key, string value) => new KeyValuePair<string, string>(key, value);

        /// <summary>The barriers of one piece's median by the road's kind (the roadsides are made along the whole chain: SideRuns).</summary>
        private static List<PieceLine> PieceLines(ChainPiece piece, RoadClass kind, bool median)
        {
            var lines = new List<PieceLine>();
            List<float> with = MarkingSnap.Merged(piece.With), against = MarkingSnap.Merged(piece.Against);
            if (with.Count + against.Count == 0)
                return lines;
            float outer = Math.Max(1.5f, piece.HalfWidth - EdgeInset);
            // The outermost lanes of the carriageway, for a road whose half width is not known well: the barrier is beyond them at least.
            float maxLane = float.MinValue, minLane = float.MaxValue;
            foreach (float x in with)
            {
                maxLane = Math.Max(maxLane, x);
                minLane = Math.Min(minLane, x);
            }

            foreach (float x in against)
            {
                maxLane = Math.Max(maxLane, x);
                minLane = Math.Min(minLane, x);
            }

            // The opposing lanes: with the chain on its right (larger laterals), against it on its left.
            bool twoWay = with.Count > 0 && against.Count > 0;
            float gap = 0f, middle = 0f;
            if (twoWay)
            {
                float nearWith = float.MaxValue, nearAgainst = float.MinValue;
                foreach (float x in with)
                    nearWith = Math.Min(nearWith, x);
                foreach (float x in against)
                    nearAgainst = Math.Max(nearAgainst, x);
                middle = (nearWith + nearAgainst) * 0.5f;
                gap = nearWith - nearAgainst - MarkingSnap.DefaultLane;
            }

            switch (kind)
            {
                case RoadClass.Highway:
                case RoadClass.National:
                    if (median && twoWay && (kind == RoadClass.Highway || (with.Count >= 2 && against.Count >= 2)))
                    {
                        if (gap >= NarrowMedian)
                            lines.Add(new PieceLine { Key = "median-w", Id = MarkingTemplates.WBeamId, Lateral = middle, Settings = new[] { S("where", "m") } });
                        else
                            lines.Add(new PieceLine { Key = "median-c", Id = MarkingTemplates.ConcreteBarrierId, Lateral = middle, Settings = new[] { S("where", "m") } });
                    }

                    break;
                case RoadClass.City:
                    // A fence on the centre line where two lanes or more go each way and nothing parts them; a planted or raised median needs none.
                    if (median && twoWay && with.Count >= 2 && against.Count >= 2 && gap < NarrowMedian)
                        lines.Add(new PieceLine { Key = "median-f", Id = MarkingTemplates.CityFenceId, Lateral = middle, Settings = new[] { S("scheme", "bw") } });
                    break;
            }

            return lines;
        }

        /// <summary>
        /// The roadside barriers of the chain by the road's kind: a W-beam on a motorway and a national road, warning posts on a village road, none on a city road; each side along the stretches its
        /// junctions leave by the mode (RoadChain.Spans), following the road's edge.
        /// </summary>
        private static List<BarrierRun> SideRuns(RoadChain chain, RoadClass kind, JunctionMode mode)
        {
            var runs = new List<BarrierRun>();
            if (kind == RoadClass.City)
                return runs;
            foreach (int side in new[] { 1, -1 })
            {
                // The wall follows the road's edge on its side: where it stands is the edge's there (0.12.67: the opening at a junction's mouth depends on it).
                var edge = new EdgeProfile(chain, side);
                foreach (ChainSpan span in chain.Spans(side, mode, false, s => edge.LateralAt(s, 0f)))
                {
                    var run = new BarrierRun { S0 = span.From, S1 = span.To, EdgeSide = side };
                    if (kind == RoadClass.Village)
                    {
                        run.Id = MarkingTemplates.WarningPostId;
                        run.Settings.Add(S("gap", "10"));
                    }
                    else
                    {
                        // The run is drawn along the chain: on its right the traffic is on the barrier's left, and on its left on its right.
                        run.Id = MarkingTemplates.WBeamId;
                        run.Settings.Add(S("where", side > 0 ? "l" : "r"));
                    }

                    run.Lateral = run.LateralAt(chain, (span.From + span.To) * 0.5f);
                    runs.Add(run);
                }
            }

            return runs;
        }

        private sealed class Track
        {
            public PieceLine Line;
            public float S0, S1;
            public int LastPiece;
        }

        /// <summary>
        /// The barriers of a stretch: each piece's lines by the road's kind, a line going on from the piece before through a node where the road only goes on when it is the same barrier within 0.3 m
        /// across, each ending where the lanes do at a junction, and every line cut round each crosswalk (CrosswalkClearance either side). Lines shorter than 2 m are left out.
        /// </summary>
        public static List<BarrierRun> Plan(RoadChain chain, RoadClass kind, bool sides, bool median) => Plan(chain, kind, sides, median, JunctionMode.Side);

        /// <summary>
        /// As above, with what the barriers do at the junctions (mode): the roadsides go on where the mode lets them (SideRuns); a median goes on through a node and, in Through, through every junction, in
        /// Side through one that a road joins from one side only (a merge, a fork, a T), and is cut at a crossroads.
        /// </summary>
        public static List<BarrierRun> Plan(RoadChain chain, RoadClass kind, bool sides, bool median, JunctionMode mode)
        {
            var runs = new List<BarrierRun>();
            if (chain == null || chain.Pieces.Count == 0)
                return runs;
            var open = new List<Track>();
            var done = new List<Track>();
            bool Joined(ChainPiece p) => p.After == ChainJoint.Pass || (p.After == ChainJoint.Junction && (mode == JunctionMode.Through || (mode == JunctionMode.Side && !(p.JoinLeft && p.JoinRight))));
            for (int i = 0; i < chain.Pieces.Count; i++)
            {
                ChainPiece piece = chain.Pieces[i];
                bool joinedBefore = i > 0 && Joined(chain.Pieces[i - 1]);
                var next = new List<Track>();
                foreach (PieceLine line in PieceLines(piece, kind, median))
                {
                    Track track = joinedBefore ? open.Find(t => t.LastPiece == i - 1 && t.Line.Key == line.Key && Math.Abs(t.Line.Lateral - line.Lateral) <= 0.3f) : null;
                    float from = joinedBefore ? piece.S0 : piece.LanesS0;
                    float to = Joined(piece) && i < chain.Pieces.Count - 1 ? piece.S1 : piece.LanesS1;
                    if (to - from < 0.5f)
                        continue;
                    if (track == null)
                        track = new Track { Line = line, S0 = from };
                    else
                        open.Remove(track);
                    track.S1 = to;
                    track.LastPiece = i;
                    next.Add(track);
                }

                done.AddRange(open);
                open = next;
            }

            done.AddRange(open);
            var all = new List<BarrierRun>();
            foreach (Track t in done)
            {
                var run = new BarrierRun { Id = t.Line.Id, Lateral = t.Line.Lateral, S0 = t.S0, S1 = t.S1 };
                run.Settings.AddRange(t.Line.Settings);
                all.Add(run);
            }

            if (sides)
                all.AddRange(SideRuns(chain, kind, mode));
            foreach (BarrierRun whole in all)
            {
                foreach ((float s0, float s1) in CutAt(whole.S0, whole.S1, chain.Crossings))
                {
                    if (s1 - s0 < 2f)
                        continue;
                    var run = new BarrierRun { Id = whole.Id, Lateral = whole.Lateral, S0 = s0, S1 = s1, EdgeSide = whole.EdgeSide, EdgeOffset = whole.EdgeOffset };
                    run.Settings.AddRange(whole.Settings);
                    runs.Add(run);
                }
            }

            runs.Sort((a, b) => a.S0 != b.S0 ? a.S0.CompareTo(b.S0) : a.Lateral.CompareTo(b.Lateral));
            return runs;
        }

        /// <summary>The parts of s0..s1 outside every crossing grown by CrosswalkClearance, in order.</summary>
        public static List<(float, float)> CutAt(float s0, float s1, IList<ChainCrossing> crossings)
        {
            var parts = new List<(float, float)> { (s0, s1) };
            if (crossings == null)
                return parts;
            foreach (ChainCrossing c in crossings)
            {
                float a = c.S0 - CrosswalkClearance, b = c.S1 + CrosswalkClearance;
                var next = new List<(float, float)>();
                foreach ((float p0, float p1) in parts)
                {
                    if (b <= p0 || a >= p1)
                    {
                        next.Add((p0, p1));
                        continue;
                    }

                    if (a > p0)
                        next.Add((p0, a));
                    if (b < p1)
                        next.Add((b, p1));
                }

                parts = next;
            }

            parts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return parts;
        }
    }
}
