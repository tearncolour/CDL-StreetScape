using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The quality of the line follow on a wall (0.12.83 follow work; the owner: "隔音墙的MoveIt跟随不是很好"): a noise wall is a line item with a baked path that runs along a curved road at a lateral, cut into
    /// items of at most MarkingRuns.MaxItemLength metres; Move It moves the road (bodily, or by one node: the neighbours' rule) and the follow maps each item's points to the new curves. This puts a wall on a chain of
    /// roads, moves the chain, follows it as the production LineFollow does, and compares what the follow made with what a fresh placement on the moved chain makes: the largest distance of the followed points and of
    /// their chords' middles from the true offset line (cm), the gaps between the items, the largest turn at a joint against the true line's, and how the panels (posts every 4 m from where the whole line starts)
    /// meet where two items meet, with the phases the items have and with the phases a fresh placement gives them. Deterministic. The game is not here: the roads are curves.
    /// </summary>
    internal static class FollowWallSim
    {
        internal sealed class Chain
        {
            public Bezier3[] Roads;
            public ArcTable[] Arcs;
            public float[] Start;
            public float Length;

            public Chain(Bezier3[] roads)
            {
                Roads = roads;
                Arcs = roads.Select(r => new ArcTable(r)).ToArray();
                Start = new float[roads.Length];
                float sum = 0f;
                for (int i = 0; i < roads.Length; i++)
                {
                    Start[i] = sum;
                    sum += Arcs[i].Length;
                }

                Length = sum;
            }

            /// <summary>The point of the offset line at the distance s along the chain (lateral positive to the right of the way the road runs).</summary>
            public Vec3 At(float s, float lateral)
            {
                int i = Roads.Length - 1;
                while (i > 0 && s < Start[i])
                    i--;
                float local = Math.Max(0f, Math.Min(Arcs[i].Length, s - Start[i]));
                float t = Arcs[i].TAt(local);
                Vec3 heading = Roads[i].Tangent(t).HeadingXZ();
                return Roads[i].Point(t) + heading.RightXZ() * lateral;
            }

            /// <summary>The offset line as finely as asked and with the exact start and end of every road in it: what a perfect wall is on this chain (a corner at a node is a gap in it that no chord can be right across).</summary>
            public List<Vec3> Dense(float lateral, float step)
            {
                var line = new List<Vec3>();
                for (int i = 0; i < Roads.Length; i++)
                {
                    int n = Math.Max(1, (int)Math.Ceiling(Arcs[i].Length / step));
                    for (int k = 0; k <= n; k++)
                        line.Add(At(Start[i] + Arcs[i].Length * k / n - (k == n && i + 1 < Roads.Length ? 1e-4f : 0f), lateral));
                }

                return line;
            }

            /// <summary>A line along the whole chain at a lateral, a point every step metres from the start and the end (as RoadChain.Line makes it, 2 m).</summary>
            public List<Vec3> Line(float lateral, float step)
            {
                var line = new List<Vec3>();
                int whole = (int)Math.Floor(Length / step);
                for (int k = 0; k <= whole; k++)
                {
                    if (Length - k * step < 0.5f && k > 0)
                        break;
                    line.Add(At(k * step, lateral));
                }

                line.Add(At(Length, lateral));
                return line;
            }
        }

        /// <summary>A road that bends: arcs of the radius, each segment of a Bezier curve with the handles of a circle's arc, joined with the same tangent at the nodes (a node of a road is where the game cuts it).</summary>
        internal static Chain Bend(int roads, float segment, float radius, float turnPerSegment = 1f, float climb = 0f)
        {
            var curves = new Bezier3[roads];
            double heading = 0.0;
            Vec3 at = Vec3.Zero;
            for (int i = 0; i < roads; i++)
            {
                double angle = segment / radius * turnPerSegment * (i % 2 == 0 ? 1.0 : 1.0);
                double k = 4.0 / 3.0 * Math.Tan(angle / 4.0) * radius;
                Vec3 h0 = new Vec3((float)Math.Sin(heading), 0f, (float)Math.Cos(heading)), h1 = new Vec3((float)Math.Sin(heading + angle), 0f, (float)Math.Cos(heading + angle));
                double chord = 2.0 * radius * Math.Sin(angle / 2.0);
                Vec3 end = at + new Vec3((float)(Math.Sin(heading + angle / 2.0) * chord), climb, (float)(Math.Cos(heading + angle / 2.0) * chord));
                curves[i] = new Bezier3(at, at + h0 * (float)k + new Vec3(0f, climb / 3f, 0f), end - h1 * (float)k - new Vec3(0f, climb / 3f, 0f), end);
                at = end;
                heading += angle;
            }

            return new Chain(curves);
        }

        /// <summary>The curve after its end node was dragged by delta (Move It's neighbours rule, FollowFixture.Neighbour), or its start node (the same on the reversed curve).</summary>
        internal static Bezier3 DragEnd(Bezier3 c, Vec3 delta) => FollowFixture.Neighbour(c, delta);

        internal static Bezier3 DragStart(Bezier3 c, Vec3 delta)
        {
            Bezier3 reversed = FollowFixture.Neighbour(new Bezier3(c.D, c.C, c.B, c.A), delta);
            return new Bezier3(reversed.D, reversed.C, reversed.B, reversed.A);
        }

        /// <summary>A road moved bodily: shifted and turned about one pivot, the same for every road of a chain (FollowFixture.Rigid turns each curve about its own start, which would pull a chain apart).</summary>
        internal static Bezier3 RigidAbout(Bezier3 c, Vec3 pivot, Vec3 shift, float turn)
        {
            double s = Math.Sin(turn), co = Math.Cos(turn);
            Vec3 Map(Vec3 p)
            {
                double x = (double)p.X - pivot.X, z = (double)p.Z - pivot.Z;
                return new Vec3((float)(pivot.X + shift.X + x * co + z * s), p.Y + shift.Y, (float)(pivot.Z + shift.Z + z * co - x * s));
            }

            return new Bezier3(Map(c.A), Map(c.B), Map(c.C), Map(c.D));
        }

        /// <summary>The chain with the node before road index node dragged by along m the way the first road runs and 6 m across (Move It's neighbours rule on both sides); for one road its end.</summary>
        internal static Bezier3[] Dragged(Chain chain, float along)
        {
            int node = chain.Roads.Length == 1 ? 1 : 2;
            Vec3 dir = chain.Roads[node - 1].Tangent(1f).HeadingXZ();
            Vec3 delta = dir * along + dir.RightXZ() * 6f;
            Bezier3[] now = (Bezier3[])chain.Roads.Clone();
            now[node - 1] = DragEnd(chain.Roads[node - 1], delta);
            if (node < chain.Roads.Length)
                now[node] = DragStart(chain.Roads[node], delta);
            return now;
        }

        /// <summary>How far the node is to be dragged along the road for the whole chain's length to change by target (a share): found by halving.</summary>
        internal static float AlongFor(Chain chain, float target)
        {
            float lo = -40f, hi = 80f;
            for (int i = 0; i < 40; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float change = new Chain(Dragged(chain, mid)).Length / chain.Length - 1f;
                if (change < target)
                    lo = mid;
                else
                    hi = mid;
            }

            return (lo + hi) * 0.5f;
        }

        internal sealed class Item
        {
            public string Path;
            public Vec3 Position;
            public float Yaw;
            public float PhaseM, PathLength;
            public LineRelation Relation;
        }

        internal sealed class Outcome
        {
            public string Label;
            public float LengthChange;
            public int Items, Rewritten, PoseOnly, Unchanged, Failed;
            public float MaxVertexCm, MaxChordMiddleCm, MaxHeightCm, MaxGapCm, MaxTurnDeg, TrueMaxTurnDeg, FreshVertexCm, FreshChordMiddleCm;
            public float PanelShortest, PanelLongest, FreshShortest, FreshLongest, MaxPhaseErrorM, RebasedShortest, RebasedLongest;
        }

        internal static List<Item> Place(Chain chain, float lateral, float spacing = 2f)
        {
            List<Vec3> line = chain.Line(lateral, spacing);
            var items = new List<Item>();
            float start = 0f;
            foreach (RunItem run in MarkingRuns.Items(line))
            {
                items.Add(new Item { Path = run.Path, Position = run.Position, Yaw = run.Yaw, PhaseM = run.Start, PathLength = RunPath.Length(run.Path) });
                start = run.Start;
            }

            return items;
        }

        /// <summary>The items followed to the roads' new curves, as the watch does it: each item's relation measured on the old tables of the roads, applied to the new.</summary>
        internal static List<Item> Follow(List<Item> items, Chain before, Bezier3[] now, float limit, Outcome outcome)
        {
            var work = new LineWorkspace();
            FollowCurve[] old = before.Roads.Select(r => new FollowCurve(r)).ToArray(), tables = now.Select(r => new FollowCurve(r)).ToArray();
            var result = new List<Item>();
            foreach (Item item in items)
            {
                if (!LineFollow.TryMeasure(item.Path, item.Position, item.Yaw, old, old.Length, work, out LineRelation relation))
                {
                    outcome.Failed++;
                    result.Add(item);
                    continue;
                }

                LineOutcome how = LineFollow.Apply(relation, tables, FollowMode.Fraction, limit, work, out string path, out Vec3 position, out float yaw, out _);
                switch (how)
                {
                    case LineOutcome.Rewritten:
                        outcome.Rewritten++;
                        break;
                    case LineOutcome.PoseOnly:
                        outcome.PoseOnly++;
                        break;
                    case LineOutcome.Unchanged:
                        outcome.Unchanged++;
                        break;
                    default:
                        outcome.Failed++;
                        break;
                }

                result.Add(new Item { Path = path, Position = position, Yaw = yaw, PhaseM = item.PhaseM, PathLength = RunPath.Length(path) });
            }

            return result;
        }

        private static List<Vec3> Points(Item item) => MarkingPolygon.ToWorld(RunPath.Decode(item.Path), item.Position, item.Yaw);

        private static float DistanceToLine(Vec3 p, IList<Vec3> line, out float height)
        {
            float best = float.MaxValue;
            height = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                Vec3 a = line[i - 1], b = line[i];
                float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                float u = l2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2));
                float x = a.X + dx * u, z = a.Z + dz * u, d = (float)Math.Sqrt((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z));
                if (d < best)
                {
                    best = d;
                    height = Math.Abs(p.Y - (a.Y + (b.Y - a.Y) * u));
                }
            }

            return best;
        }

        private static float MaxTurn(IList<Vec3> line)
        {
            float worst = 0f;
            for (int i = 2; i < line.Count; i++)
            {
                float h0 = (float)Math.Atan2(line[i - 1].X - line[i - 2].X, line[i - 1].Z - line[i - 2].Z), h1 = (float)Math.Atan2(line[i].X - line[i - 1].X, line[i].Z - line[i - 1].Z);
                worst = Math.Max(worst, Math.Abs(RoadRelation.Wrap(h1 - h0)));
            }

            return worst * 57.29578f;
        }

        /// <summary>The widths of the panels (posts every spacing m along the whole line, from where the line starts; the panel across two items has the posts either side of the joint) given each item's phase and length.</summary>
        internal static List<float> Panels(IList<(float Phase, float Length)> pieces, float spacing)
        {
            // The posts of each piece in its own frame (distance from the whole line's start as that piece believes it: its phase plus the distance into it), put on one axis by the piece's real start.
            var posts = new List<float>();
            float real = 0f;
            foreach ((float phase, float length) in pieces)
            {
                double k = Math.Floor(phase / spacing + 1e-6);
                for (int n = 0; n < 4000; n++, k++)
                {
                    float s = (float)(k * spacing - phase);
                    if (s < -1e-4f)
                        continue;
                    if (s > length + 1e-4f)
                        break;
                    posts.Add(real + s);
                }

                real += length;
            }

            var widths = new List<float>();
            posts.Sort();
            for (int i = 1; i < posts.Count; i++)
            {
                float w = posts[i] - posts[i - 1];
                if (w > 1e-3f)
                    widths.Add(w);
            }

            return widths;
        }

        /// <summary>Follows the wall to the new roads and measures it against a fresh placement on them.</summary>
        internal static Outcome Measure(string label, Chain before, float lateral, Bezier3[] now, float limit = FollowGate.LineChordLimit)
        {
            var o = new Outcome { Label = label };
            List<Item> items = Place(before, lateral);
            var after = new Chain(now);
            o.LengthChange = after.Length / before.Length - 1f;
            o.Items = items.Count;
            List<Item> followed = Follow(items, before, now, limit, o);
            List<Vec3> truth = after.Dense(lateral, 0.25f);
            Vec3 previousEnd = default;
            for (int k = 0; k < followed.Count; k++)
            {
                List<Vec3> points = Points(followed[k]);
                for (int i = 0; i < points.Count; i++)
                {
                    o.MaxVertexCm = Math.Max(o.MaxVertexCm, DistanceToLine(points[i], truth, out float h) * 100f);
                    o.MaxHeightCm = Math.Max(o.MaxHeightCm, h * 100f);
                    if (i > 0)
                        o.MaxChordMiddleCm = Math.Max(o.MaxChordMiddleCm, DistanceToLine(Vec3.Lerp(points[i - 1], points[i], 0.5f), truth, out _) * 100f);
                }

                if (k > 0)
                    o.MaxGapCm = Math.Max(o.MaxGapCm, Vec3.DistanceXZ(previousEnd, points[0]) * 100f);
                previousEnd = points[points.Count - 1];
            }

            List<Vec3> all = new List<Vec3>();
            foreach (Item item in followed)
            {
                List<Vec3> points = Points(item);
                all.AddRange(all.Count == 0 ? points : points.Skip(1));
            }

            o.MaxTurnDeg = MaxTurn(all);
            o.TrueMaxTurnDeg = MaxTurn(Place(after, lateral).SelectMany((it, n) => n == 0 ? Points(it) : Points(it).Skip(1)).ToList());

            // The panels: the followed items with the phases they had (what 0.12.83 writes), and the fresh placement's.
            var stale = followed.Select(i => (i.PhaseM, i.PathLength)).ToList();
            List<float> a = Panels(stale, 4f);
            o.PanelShortest = a.Count == 0 ? 0f : a.Min();
            o.PanelLongest = a.Count == 0 ? 0f : a.Max();
            // The phases made again from the lengths (LinePhases), as the watch writes them.
            var phases = items.Select(i => (long)Math.Round(i.PhaseM * 100.0)).ToList();
            long total = (long)Math.Round((items[items.Count - 1].PhaseM + items[items.Count - 1].PathLength) * 100.0);
            var made = new long[items.Count];
            if (LinePhases.TryRebase(phases, items.Select(i => i.PathLength).ToList(), followed.Select(i => i.PathLength).ToList(), total, made, out _))
            {
                List<float> c = Panels(followed.Select((i, k) => (made[k] / 100f, i.PathLength)).ToList(), 4f);
                o.RebasedShortest = c.Min();
                o.RebasedLongest = c.Max();
            }

            List<Item> fresh = Place(after, lateral);
            foreach (Item item in fresh)
            {
                List<Vec3> points = Points(item);
                for (int i = 0; i < points.Count; i++)
                {
                    o.FreshVertexCm = Math.Max(o.FreshVertexCm, DistanceToLine(points[i], truth, out _) * 100f);
                    if (i > 0)
                        o.FreshChordMiddleCm = Math.Max(o.FreshChordMiddleCm, DistanceToLine(Vec3.Lerp(points[i - 1], points[i], 0.5f), truth, out _) * 100f);
                }
            }

            List<float> b = Panels(fresh.Select(i => (i.PhaseM, i.PathLength)).ToList(), 4f);
            o.FreshShortest = b.Count == 0 ? 0f : b.Min();
            o.FreshLongest = b.Count == 0 ? 0f : b.Max();
            float run = 0f, worst = 0f;
            foreach (Item item in followed)
            {
                worst = Math.Max(worst, Math.Abs(item.PhaseM - run));
                run += item.PathLength;
            }

            o.MaxPhaseErrorM = worst;
            return o;
        }

        internal static string Format(Outcome o) =>
            $"{o.Label,-44} length {o.LengthChange * 100f,5:0.0}% items {o.Items} (rewritten {o.Rewritten}, pose {o.PoseOnly}, same {o.Unchanged}, failed {o.Failed}) | vertex {o.MaxVertexCm,5:0.0} cm (fresh {o.FreshVertexCm:0.0}), chord middle {o.MaxChordMiddleCm,5:0.0} cm (fresh {o.FreshChordMiddleCm:0.0}), height {o.MaxHeightCm,4:0.0} cm, gap {o.MaxGapCm,4:0.0} cm, turn {o.MaxTurnDeg,4:0.0} deg (fresh {o.TrueMaxTurnDeg:0.0}) | panels with the old phases {o.PanelShortest:0.00}-{o.PanelLongest:0.00} m, with the phases made again {o.RebasedShortest:0.00}-{o.RebasedLongest:0.00} (fresh {o.FreshShortest:0.00}-{o.FreshLongest:0.00}), the old phases off by {o.MaxPhaseErrorM:0.00} m";

        /// <summary>The cases: one road and a chain of four, at 3.7 and 10 m, bodily moved and with one node dragged, the length changing by about 0, 2, 5 and 8 %.</summary>
        internal static List<Outcome> Cases()
        {
            var cases = new List<Outcome>();
            foreach ((string name, Chain chain) in new[] { ("one road 160 m, R 120", Bend(1, 160f, 120f)), ("four roads of 60 m, R 90", Bend(4, 60f, 90f)), ("four roads of 80 m, R 60 (tight)", Bend(4, 80f, 60f)) })
            {
                foreach (float lateral in new[] { 3.7f, 10f })
                {
                    cases.Add(Measure($"{name}, {lateral:0.0} m: moved bodily", chain, lateral, chain.Roads.Select(r => RigidAbout(r, chain.Roads[0].A, new Vec3(30f, 1f, -20f), 0.3f)).ToArray()));
                    foreach (float stretch in new[] { 0f, 0.02f, 0.05f, 0.08f })
                    {
                        // The middle node (or the end, for one road) is dragged by what changes the whole chain's length by the share, and 6 m across.
                        Bezier3[] now = Dragged(chain, AlongFor(chain, stretch));
                        cases.Add(Measure($"{name}, {lateral:0.0} m: a node dragged (+{stretch * 100f:0}%)", chain, lateral, now));
                    }
                }
            }

            return cases;
        }

        /// <summary>Where the worst vertex of a case is: which item and vertex, how far from the true line, the road its relation says, and its place on that road (for finding a cause).</summary>
        internal static string Worst(Chain chain, float lateral, Bezier3[] now)
        {
            List<Item> items = Place(chain, lateral);
            var o = new Outcome();
            List<Item> followed = Follow(items, chain, now, FollowGate.LineChordLimit, o);
            var after = new Chain(now);
            List<Vec3> truth = after.Dense(lateral, 0.25f);
            var work = new LineWorkspace();
            FollowCurve[] old = chain.Roads.Select(r => new FollowCurve(r)).ToArray();
            float worst = -1f;
            string text = "";
            for (int k = 0; k < followed.Count; k++)
            {
                LineFollow.TryMeasure(items[k].Path, items[k].Position, items[k].Yaw, old, old.Length, work, out LineRelation rel);
                List<Vec3> points = Points(followed[k]);
                for (int i = 0; i < points.Count; i++)
                {
                    float d = DistanceToLine(points[i], truth, out _);
                    if (d > worst && i < rel.Points)
                    {
                        worst = d;
                        text = $"item {k} vertex {i} of {points.Count}: {d * 100f:0.0} cm off; its relation: road {rel.Road[i]}, fraction {rel.Fraction[i]:0.000}, along {rel.Along[i]:0.00} m, lateral {rel.Lateral[i]:0.00} m";
                    }
                }
            }

            return text;
        }

        public static void Print()
        {
            Chain probe = Bend(4, 60f, 90f);
            Console.WriteLine("worst vertex, four roads of 60 m, 3.7 m, a node dragged +5%: " + Worst(probe, 3.7f, Dragged(probe, AlongFor(probe, 0.05f))));
            Console.WriteLine("worst vertex, four roads of 60 m, 10 m, a node dragged +8%: " + Worst(probe, 10f, Dragged(probe, AlongFor(probe, 0.08f))));
            foreach (Outcome o in Cases())
                Console.WriteLine(Format(o));
        }
    }
}
