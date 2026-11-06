using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.60 (the owner: "隔音墙吸附还是不够好", the starts, ends and seams of a barrier): a line started or ended near the end of a barrier of its kind on the same line of the road is drawn to that end, so that
    /// lines put one after another meet; ends of walls on another level or across the road are not seams.
    /// </summary>
    internal static class RunJoinTests
    {
        public static void RunAll()
        {
            Program.Run("run joins: the ends of the barriers that are on the line", EndsOnTheLine);
            Program.Run("run joins: a start or an end is drawn to the nearest end within reach", Snapping);
        }

        private static RoadChain Straight()
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 200f)), Kmh = 50f, HasSpeed = true, After = ChainJoint.End };
            piece.Forward.Add(1.75f);
            piece.Backward.Add(-1.75f);
            return new RoadChain(new[] { piece });
        }

        /// <summary>An item of a barrier along the chain from s0 to s1 at a lateral, lifted by up (m), as the tool writes it.</summary>
        private static RunJoins.Item Wall(RoadChain chain, float s0, float s1, float lateral, float up = 0f)
        {
            List<Vec3> line = chain.Line(s0, s1, lateral, up);
            string path = RunPath.Encode(line, out float yaw);
            return new RunJoins.Item { Path = path, Position = line[0], Yaw = yaw };
        }

        private static void EndsOnTheLine()
        {
            RoadChain chain = Straight();
            var items = new List<RunJoins.Item>
            {
                Wall(chain, 20f, 60f, 3.8f),
                Wall(chain, 60f, 120f, 3.8f),
                // Another wall of the same kind on the road 9 m up (a deck over it), and one 7 m across it: neither is on the line.
                Wall(chain, 80f, 100f, 3.8f, 9f),
                Wall(chain, 80f, 100f, -7f),
                new RunJoins.Item { Path = "", Position = new Vec3(0f, 0f, 0f), Yaw = 0f }
            };
            List<float> ends = RunJoins.Of(chain, items, 3.8f);
            ends.Sort();
            Program.Check(ends.Count == 4, $"four ends of the two walls on the line: {string.Join(", ", ends.ConvertAll(e => e.ToString("0.0")))}");
            Program.Check(ends.Count == 4 && Math.Abs(ends[0] - 20f) < 0.1f && Math.Abs(ends[1] - 60f) < 0.1f && Math.Abs(ends[2] - 60f) < 0.1f && Math.Abs(ends[3] - 120f) < 0.1f, "at 20, 60, 60 and 120 m");
            Program.Check(RunJoins.Of(null, items, 3.8f).Count == 0 && RunJoins.Of(chain, new List<RunJoins.Item>(), 3.8f).Count == 0, "no chain or no walls: none");
            // Seen from the other side of the road the same walls are 7 m across the line, not on it; a wall at -7 m is.
            List<float> across = RunJoins.Of(chain, items, -7f);
            Program.Check(across.Count == 2 && Math.Abs(across[0] - 80f) < 0.1f, $"the line at -7 m has the ends of the wall there: {across.Count}");
        }

        private static void Snapping()
        {
            var ends = new List<float> { 20f, 60f, 120f };
            Program.Near(RunJoins.Snap(ends, 117f), 120f, 1e-4f, "117 m is drawn to the end at 120 m");
            Program.Near(RunJoins.Snap(ends, 62.5f), 60f, 1e-4f, "62.5 m to the end at 60 m");
            Program.Check(float.IsNaN(RunJoins.Snap(ends, 90f)), "90 m, 30 m from any end, is not drawn to one");
            Program.Check(float.IsNaN(RunJoins.Snap(ends, 64.01f)), "a start 4.01 m from an end is out of reach: " + RunJoins.Snap(ends, 64.01f));
            Program.Near(RunJoins.Snap(new List<float> { 58f, 63f }, 60.5f), 58f, 1e-4f, "of two ends the nearer");
            Program.Check(float.IsNaN(RunJoins.Snap(null, 60f)) && float.IsNaN(RunJoins.Snap(new List<float>(), 60f)), "none to be drawn to: NaN");
        }
    }
}
