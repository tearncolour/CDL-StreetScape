using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The snapping of a drawn barrier from end to end (0.12.64; the owner: "还有吸附验证"), as the placement tool does it but with the game's roads made up: the line along a chain, cut at the junctions
    /// it is cut at (RoadChain.Spans), stood on what is under it (SurfaceHeights.Put over an AreaFit of the deck, the terrain 2.3 m under it), cut into items (MarkingRuns.Items, RunPath), and read back
    /// (RunPath.Decode). The checks are the faults of the owner's pictures: a pit or a spike at a joint, a foot that fell to the ground beside a deck, a gap at a fork, a path cut short, two lines of one
    /// wall that do not meet.
    /// </summary>
    internal static class BarrierPipelineTests
    {
        public static void RunAll()
        {
            Program.Run("barrier pipeline: along an elevated road the wall stands on the deck from end to end", Elevated);
            Program.Run("barrier pipeline: a fork cuts the line on the side the road leaves by and on no other", Fork);
            Program.Run("barrier pipeline: the items of one line read back whole and meet", ItemsMeet);
            Program.Run("barrier pipeline: a second line starts where the first ended", SecondLine);
        }

        private const float Deck = 12f, Ground = Deck - 2.3f;

        /// <summary>
        /// A straight road north (+Z) 400 m long in three pieces, each 7 m to a side, the deck 12 m up and the ground 2.3 m under it: the pieces meet at 190 m (a node the road only goes on through)
        /// and at 210 m, a junction where the lanes stop 6 m before it and start 9 m after it (a segment's mouth: the lanes' ends are not the pieces'), and a road leaves it on the left.
        /// </summary>
        private static RoadChain Road(bool joinRight = false)
        {
            ChainPiece Piece(float z0, float z1, ChainJoint after, float laneFrom = float.NaN, float laneTo = float.NaN)
            {
                var piece = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, Deck, z0), new Vec3(0f, Deck, z1)), Kmh = 80f, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo, HalfWidth = 7f };
                piece.Forward.Add(1.75f);
                piece.Backward.Add(-1.75f);
                return piece;
            }

            ChainPiece a = Piece(0f, 190f, ChainJoint.Pass), b = Piece(190f, 210f, ChainJoint.Junction, laneTo: 14f), c = Piece(210f, 400f, ChainJoint.End, laneFrom: 9f);
            b.JoinLeft = true;
            b.JoinRight = joinRight;
            return new RoadChain(new[] { a, b, c });
        }

        /// <summary>The surface a post may stand on: the deck from -6 to 6 m (the parapet of 1 m each side is not), as the game's reader gives it, at the deck's height.</summary>
        private static AreaFit Surface(float minX, float minZ, float maxX, float maxZ, float originY)
        {
            var strip = new SurfaceStrip { Elevated = true };
            for (float z = -20f; z <= 420f; z += 10f)
            {
                strip.Add(new Vec3(-6f, Deck, z), new Vec3(6f, Deck, z));
            }

            return AreaFit.FromStrips(new List<SurfaceStrip> { strip }, new Vec3(0f, originY, 0f), 0f, minX, maxX, minZ, maxZ);
        }

        private static float TerrainUnder(float x, float y, float z) => Ground;

        /// <summary>What the tool makes of a line at a lateral: the parts, each with its heights read, and the items of each part.</summary>
        private static List<(List<Vec3> Line, List<RunItem> Items)> Draw(RoadChain chain, float lateral, JunctionMode mode, float s0, float s1, SurfaceHeights heights = null)
        {
            heights = heights ?? new SurfaceHeights();
            var result = new List<(List<Vec3>, List<RunItem>)>();
            int k = 0;
            foreach (ChainSpan part in RoadChain.Clip(chain.Spans(lateral, mode), s0, s1))
            {
                List<Vec3> line = chain.Line(part.From, part.To, lateral, 0f);
                // The chain's line is at the road's centre height: the surface read puts each point where it stands.
                List<Vec3> stood = heights.Put(line, "part" + k++, line.Count, Surface, TerrainUnder);
                result.Add((stood, new List<RunItem>(MarkingRuns.Items(stood))));
            }

            return result;
        }

        private static void Elevated()
        {
            RoadChain chain = Road();
            // The foot of an edge barrier: 0.35 m inside the road's edge, 6.65 m out: 0.65 m beyond the deck the posts stand on, over the parapet.
            var parts = Draw(chain, 6.65f, JunctionMode.Through, 0f, chain.Length);
            Program.Check(parts.Count == 1, "a line that goes through every junction is one part: " + parts.Count);
            float worst = 0f;
            int points = 0;
            foreach (var part in parts)
            {
                foreach (Vec3 p in part.Line)
                {
                    worst = Math.Max(worst, Math.Abs(p.Y - Deck));
                    points++;
                }
            }

            Program.Check(points > 150 && worst < 0.003f, $"{points} points, every one at the deck's height ({worst * 1000f:0.0} mm at the worst), none on the ground under it ({Ground} m)");

            // 4 m beyond the deck is the slope: the ground.
            var beside = Draw(chain, 10f, JunctionMode.Through, 0f, chain.Length);
            float low = 0f;
            foreach (Vec3 p in beside[0].Line)
                low = Math.Max(low, Math.Abs(p.Y - Ground));
            Program.Check(low < 0.003f, $"a line 4 m beyond the deck stands on the ground ({low * 1000f:0.0} mm off)");
        }

        private static void Fork()
        {
            RoadChain chain = Road(joinRight: false);
            // A road leaves on the left at the junction at 210 m: the line on the left (-6.65) is cut there from where the lanes end to where they start, the line on the right goes on.
            List<ChainSpan> left = chain.Spans(-6.65f, JunctionMode.Side), right = chain.Spans(6.65f, JunctionMode.Side);
            Program.Check(left.Count == 2 && Math.Abs(left[1].From - left[0].To - 15f) < 0.5f, $"the line on the side the road leaves by is cut over the node's {(left.Count == 2 ? left[1].From - left[0].To : 0f):0.0} m (6 before, 9 after)");
            Program.Check(right.Count == 1 && right[0].To - right[0].From > 390f, "the line on the other side goes on through it, whole");

            // A road leaving on both sides cuts both.
            RoadChain both = Road(joinRight: true);
            Program.Check(both.Spans(6.65f, JunctionMode.Side).Count == 2 && both.Spans(-6.65f, JunctionMode.Side).Count == 2, "a node a road leaves at each side of cuts both lines");
            // In Through mode the line is whole whatever joins.
            Program.Check(both.Spans(6.65f, JunctionMode.Through).Count == 1, "'through' goes on past every junction");

            // The ends of the two parts are where the lanes end and begin: the wall comes up to the node and starts again after it, no further from it than the node is long.
            var parts = Draw(chain, -6.65f, JunctionMode.Side, 0f, chain.Length);
            Program.Check(parts.Count == 2, "two parts: " + parts.Count);
            float gap = Vec3.DistanceXZ(parts[0].Line[parts[0].Line.Count - 1], parts[1].Line[0]);
            Program.Check(gap < 15.6f && gap > 14.4f, $"the gap between the parts is the node's: {gap:0.0} m");
            float step = Math.Abs(parts[1].Line[0].Y - parts[0].Line[parts[0].Line.Count - 1].Y);
            Program.Check(step < 0.003f, $"and both ends are at the deck's height ({step * 1000f:0.0} mm apart)");
        }

        private static void ItemsMeet()
        {
            RoadChain chain = Road();
            var parts = Draw(chain, 6.65f, JunctionMode.Through, 0f, chain.Length);
            List<RunItem> items = parts[0].Items;
            Program.Check(items.Count >= 4, "a line of 400 m is several items: " + items.Count);
            float longestGap = 0f, tallestStep = 0f, widestTurn = 0f;
            int maxGrade = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var item = new RunJoins.Item { Path = items[i].Path, Position = items[i].Position, Yaw = items[i].Yaw };
                List<Vec3> decoded = RunPath.Decode(items[i].Path);
                Program.Check(decoded.Count >= 2, $"item {i + 1} reads back whole ({decoded.Count} points, the path of {items[i].Path.Split('|').Length} chords)");
                foreach (string piece in items[i].Path.Split('|'))
                    maxGrade = Math.Max(maxGrade, Math.Abs(int.Parse(piece.Split(':')[2])));
                Program.Check(RunJoins.Ends(item, out Vec3 first, out Vec3 last), $"item {i + 1} has two ends");
                if (i + 1 < items.Count)
                {
                    var next = new RunJoins.Item { Path = items[i + 1].Path, Position = items[i + 1].Position, Yaw = items[i + 1].Yaw };
                    RunJoins.Ends(next, out Vec3 nextFirst, out _);
                    longestGap = Math.Max(longestGap, Vec3.DistanceXZ(last, nextFirst));
                    tallestStep = Math.Max(tallestStep, Math.Abs(nextFirst.Y - last.Y));
                }
            }

            foreach (RunItem item in items)
            {
                List<Vec3> d = RunPath.Decode(item.Path);
                for (int i = 2; i < d.Count; i++)
                {
                    float a = (float)Math.Atan2(d[i - 1].X - d[i - 2].X, d[i - 1].Z - d[i - 2].Z), b = (float)Math.Atan2(d[i].X - d[i - 1].X, d[i].Z - d[i - 1].Z);
                    widestTurn = Math.Max(widestTurn, Math.Abs(b - a));
                }
            }

            Program.Check(longestGap < 0.12f, $"the items meet end to end: the widest gap {longestGap * 100f:0.0} cm");
            Program.Check(tallestStep < 0.01f, $"and at one height: the tallest step {tallestStep * 100f:0.0} cm");
            Program.Check(widestTurn < 0.01f, $"the line along a straight road is straight: the widest turn between chords {widestTurn * 57.3f:0.00} degrees");
            Program.Check(maxGrade <= RunPath.MaxGrade, $"no grade over what the reader takes: {maxGrade}");
        }

        private static void SecondLine()
        {
            RoadChain chain = Road();
            var first = Draw(chain, 6.65f, JunctionMode.Through, 0f, 150f);
            List<RunItem> placed = first[0].Items;
            var joins = new List<RunJoins.Item>();
            foreach (RunItem item in placed)
                joins.Add(new RunJoins.Item { Path = item.Path, Position = item.Position, Yaw = item.Yaw });
            List<float> ends = RunJoins.Of(chain, joins, 6.65f);
            Program.Check(ends.Count == placed.Count * 2 && ends.Exists(e => Math.Abs(e - 150f) < 0.3f) && ends.Exists(e => e < 0.5f), $"the ends of the first line on its line of the road: {string.Join(", ", ends.ConvertAll(e => e.ToString("0.0")))}");
            // The player starts the second 2.6 m before where the first ended, a click's worth of error: it is drawn to the end.
            float start = RunJoins.Snap(ends, 147.4f);
            Program.Check(Math.Abs(start - 150f) < 0.3f, $"a start 2.6 m short is drawn to the end at 150 m: {start:0.0}");
            var second = Draw(chain, 6.65f, JunctionMode.Through, start, 300f);
            Vec3 a = first[0].Line[first[0].Line.Count - 1], b = second[0].Line[0];
            Program.Check(Vec3.DistanceXZ(a, b) < 0.05f && Math.Abs(a.Y - b.Y) < 0.003f, $"the two lines meet at one point ({Vec3.DistanceXZ(a, b) * 100f:0.0} cm, {Math.Abs(a.Y - b.Y) * 1000f:0.0} mm)");
            // A line on the other side of the road is not drawn to it.
            List<float> opposite = RunJoins.Of(chain, joins, -6.65f);
            Program.Check(opposite.Count == 0, "the other side of the road has no such end");
        }
    }
}
