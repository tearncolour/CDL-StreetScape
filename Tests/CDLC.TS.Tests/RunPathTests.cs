using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The text path of a marking that runs along a road (RunPath): written, read back, cut into items.</summary>
    internal static class RunPathTests
    {
        public static void RunAll()
        {
            Program.Run("run path: a straight line, an arc and an S, written and read back", RoundTrip);
            Program.Run("run path: the text is short and safe, and damage ends the path", Text);
            Program.Run("run path: a step the reader would refuse is written within what it takes (0.12.60)", SteepStep);
            Program.Run("run path: a long run is cut into items that join", Splitting);
            Program.Run("run path: the rounding of the lengths does not add up along the item", NoDrift);
        }

        private static Vec3 P(float x, float y, float z) => new Vec3(x, y, z);

        /// <summary>The world points of a decoded path: turned by the yaw about Y (local +Z to (sin yaw, cos yaw)) and moved to the start.</summary>
        private static List<Vec3> ToWorld(List<Vec3> local, float yaw, Vec3 start)
        {
            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
            var world = new List<Vec3>();
            foreach (Vec3 p in local)
                world.Add(new Vec3(p.X * c + p.Z * s + start.X, p.Y + start.Y, -p.X * s + p.Z * c + start.Z));
            return world;
        }

        private static float Worst(IList<Vec3> a, IList<Vec3> b)
        {
            float worst = 0f;
            for (int i = 0; i < a.Count; i++)
                worst = Math.Max(worst, (a[i] - b[i]).Length);
            return worst;
        }

        private static void RoundTrip()
        {
            // A straight line of 40 m north-east, 2 % uphill: ten chords of 4 m.
            var line = new List<Vec3>();
            for (int i = 0; i <= 10; i++)
                line.Add(P(100f + i * 2.8284f, 5f + i * 4f * 0.02f, 200f + i * 2.8284f));
            string text = RunPath.Encode(line, out float yaw);
            Program.Check(text.Length > 0 && Math.Abs(yaw - Math.PI / 4) < 1e-4, "a line: the yaw is the direction of the first chord (45 degrees): " + yaw);
            List<Vec3> back = ToWorld(RunPath.Decode(text), yaw, line[0]);
            Program.Check(back.Count == line.Count, "as many points back: " + back.Count);
            Program.Check(Worst(line, back) < 0.03f, "a straight line comes back within 3 cm: " + Worst(line, back));
            Program.Near(RunPath.Length(text), 40f, 0.05f, "its length");

            // An arc of radius 30 m through 90 degrees, sampled every 3 m, to the right (clockwise): 16 chords.
            var arc = new List<Vec3>();
            double step = 3.0 / 30.0;
            for (int i = 0; i <= 15; i++)
            {
                double a = i * step;
                arc.Add(P((float)(30.0 * (1.0 - Math.Cos(a))) + 10f, 0f, (float)(30.0 * Math.Sin(a)) + 20f));
            }

            string arcText = RunPath.Encode(arc, out float arcYaw);
            Program.Check(arcText.Split('|').Length == 15, "fifteen pieces: " + arcText.Split('|').Length);
            List<Vec3> arcBack = ToWorld(RunPath.Decode(arcText), arcYaw, arc[0]);
            Program.Check(Worst(arc, arcBack) < 0.03f, "an arc comes back within 3 cm: " + Worst(arc, arcBack));

            // The turn is positive toward +X: a path that bends to the right (x grows) has positive turns, to the left negative.
            Program.Check(arcText.Split('|')[2].Split(':')[1][0] != '-', "bending toward +X has positive turns: " + arcText.Split('|')[2]);
            var left = new List<Vec3>();
            foreach (Vec3 p in arc)
                left.Add(P(-p.X, p.Y, p.Z));
            string leftText = RunPath.Encode(left, out _);
            Program.Check(leftText.Split('|')[2].Split(':')[1][0] == '-', "bending toward -X has negative turns: " + leftText.Split('|')[2]);

            // An S: right then left; no drift over 32 pieces of 3 m (about 96 m).
            var s = new List<Vec3>();
            for (int i = 0; i <= 32; i++)
                s.Add(P((float)(6.0 * Math.Sin(i * 3.0 / 20.0)), (float)(i * 3.0 * 0.01), i * 3f));
            string sText = RunPath.Encode(s, out float sYaw);
            List<Vec3> sBack = ToWorld(RunPath.Decode(sText), sYaw, s[0]);
            Program.Check(sBack.Count == 33 && Worst(s, sBack) < 0.06f, "an S of 96 m comes back within 6 cm: " + Worst(s, sBack));

            // Many random roads: the error stays small whatever the bends.
            var rng = new Random(11);
            for (int trial = 0; trial < 200; trial++)
            {
                var pts = new List<Vec3> { P(0f, 0f, 0f) };
                double dir = rng.NextDouble() * 6.28, y = 0;
                int n = rng.Next(2, RunPath.MaxPieces + 1);
                for (int i = 0; i < n; i++)
                {
                    dir += (rng.NextDouble() - 0.5) * 0.3;
                    double len = 1.0 + rng.NextDouble() * 3.0;
                    y += (rng.NextDouble() - 0.5) * 0.2;
                    Vec3 last = pts[pts.Count - 1];
                    pts.Add(P(last.X + (float)(len * Math.Sin(dir)), (float)y, last.Z + (float)(len * Math.Cos(dir))));
                }

                string t = RunPath.Encode(pts, out float yw);
                List<Vec3> b = ToWorld(RunPath.Decode(t), yw, pts[0]);
                Program.Check(b.Count == pts.Count && Worst(pts, b) < 0.15f, "a random road of " + n + " pieces comes back within 15 cm: " + Worst(pts, b));
            }
        }

        private static void NoDrift()
        {
            // 24 chords of 2.4949 m: each length is rounded to 2.49 m, 4.9 mm short, and the 24 add up to 118 mm if each is rounded on its own.
            var straight = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
                straight.Add(P(10f + i * 2.4949f, 0f, 50f));
            string text = RunPath.Encode(straight, out float yaw);
            List<Vec3> back = ToWorld(RunPath.Decode(text), yaw, straight[0]);
            Program.Check(back.Count == straight.Count && Worst(straight, back) < 0.01f, "24 chords of 2.4949 m come back within 1 cm of the line: " + Worst(straight, back));

            // Roads that bend, with chords of any length: no point is more than 1 cm off however far along the item it is.
            var rng = new Random(11);
            float worst = 0f;
            for (int n = 0; n < 40; n++)
            {
                var pts = new List<Vec3> { P(100f, 0f, 200f) };
                double dir = rng.NextDouble() * 6.28;
                for (int i = 0; i < 32; i++)
                {
                    dir += (rng.NextDouble() - 0.5) * 0.3;
                    double len = 0.3 + rng.NextDouble() * 4.0;
                    Vec3 last = pts[pts.Count - 1];
                    pts.Add(P(last.X + (float)(len * Math.Sin(dir)), 0f, last.Z + (float)(len * Math.Cos(dir))));
                }

                string t = RunPath.Encode(pts, out float yw);
                List<Vec3> b = ToWorld(RunPath.Decode(t), yw, pts[0]);
                worst = Math.Max(worst, Worst(pts, b));
            }

            Program.Check(worst < 0.01f, "40 bending roads of 32 chords of any length come back within 1 cm at every point: " + worst);
        }

        private static void SteepStep()
        {
            // The owner's line over a ramp's joint had a chord that rose 3.2 m in 2 m (a grade of 1586): the reader takes a grade of 1 at most and ended the path there, so that the rest of the barrier was
            // not drawn. The writer gives no grade over what the reader takes, and the next chords bring the line back to where it was.
            var pts = new List<Vec3>();
            for (int i = 0; i <= 12; i++)
                pts.Add(P(i * 2f, i < 6 ? 0f : 3.2f, 0f));
            string text = RunPath.Encode(pts, out _);
            Program.Check(text.Split('|').Length == 12, "twelve chords are written: " + text.Split('|').Length);
            List<Vec3> back = RunPath.Decode(text);
            Program.Check(back.Count == pts.Count, $"and every one is read back ({back.Count} of {pts.Count} points)");
            foreach (string piece in text.Split('|'))
                Program.Check(Math.Abs(int.Parse(piece.Split(':')[2])) <= 1000, "no grade over 1000: " + piece);
            // The sheer step is not drawn in one chord, but the line is back at its height at the end of the next few.
            Program.Check(Math.Abs(back[back.Count - 1].Y - 3.2f) < 0.05f, $"the end is at its height again: {back[back.Count - 1].Y:0.00} m");
        }

        private static void Text()
        {
            // The longest path fits the settings with room to spare.
            var pts = new List<Vec3>();
            for (int i = 0; i <= 40; i++)
                pts.Add(P(i * 3.4f, 0f, 0.5f * (float)Math.Sin(i * 0.5) * 100f));
            string t = RunPath.Encode(pts, out _);
            Program.Check(t.Split('|').Length == RunPath.MaxPieces, "at most " + RunPath.MaxPieces + " pieces are written: " + t.Split('|').Length);
            Program.Check(System.Text.Encoding.UTF8.GetByteCount(t) < 400, "and the text is under 400 bytes: " + t.Length);
            Program.Check(t.IndexOf(';') < 0 && t.IndexOf('=') < 0 && t.IndexOf('\\') < 0, "no character the settings escape");

            // Nothing to write.
            Program.Check(RunPath.Encode(null, out _) == "" && RunPath.Encode(new List<Vec3>(), out _) == "" && RunPath.Encode(new List<Vec3> { P(0, 0, 0) }, out _) == "", "no line, no path");
            Program.Check(RunPath.Encode(new List<Vec3> { P(0, 0, 0), P(0, 0, 0.01f) }, out _) == "", "a line that does not move, no path");
            Program.Check(RunPath.Encode(new List<Vec3> { P(0, 0, 0), P(float.NaN, 0, 1) }, out _) == "", "a point that is not a number: no path");
            // A repeated point is dropped.
            string repeated = RunPath.Encode(new List<Vec3> { P(0, 0, 0), P(0, 0, 0), P(0, 0, 4), P(0, 0, 4), P(0, 0, 8) }, out _);
            Program.Check(repeated.Split('|').Length == 2, "repeated points are dropped: " + repeated);

            // Reading: damage ends the path where it is; an empty or odd text is no path.
            Program.Check(RunPath.Decode("").Count == 0 && RunPath.Decode(null).Count == 0 && RunPath.Decode("junk").Count == 0 && RunPath.Decode("100:0").Count == 0, "no path, junk, a piece with two parts: nothing");
            List<Vec3> partly = RunPath.Decode("400:0:0|400:100:0|xx|400:0:0");
            Program.Check(partly.Count == 3, "a damaged piece ends the path: " + partly.Count);
            Program.Check(RunPath.Decode("0:0:0").Count == 0 && RunPath.Decode("-5:0:0").Count == 0 && RunPath.Decode("400:99999:0").Count == 0 && RunPath.Decode("400:0:5000").Count == 0, "a zero or negative length, a turn over half a turn, a grade over 1: nothing");
            Program.Near(RunPath.Length("400:0:0|300:0:0"), 7f, 1e-4f, "lengths add");
            // The path of a straight line heads +Z in the item's frame.
            List<Vec3> straight = RunPath.Decode("400:0:50");
            Program.Check(straight.Count == 2 && Math.Abs(straight[1].X) < 1e-5f && Math.Abs(straight[1].Z - 4f) < 1e-5f && Math.Abs(straight[1].Y - 0.2f) < 1e-5f, "400:0:50 is 4 m ahead and 20 cm up: " + straight[1]);
            // 90 degrees to the right (15708 units) after a first piece.
            List<Vec3> right = RunPath.Decode("400:0:0|400:15708:0");
            Program.Check(right.Count == 3 && Math.Abs(right[2].X - 4f) < 1e-3f && Math.Abs(right[2].Z - 4f) < 1e-3f, "a turn of 15708 units is a right angle toward +X: " + right[2]);
        }

        private static void Splitting()
        {
            // 300 m of straight line: runs of 100 m at most, each of at most 32 pieces, joined end to start.
            var line = new List<Vec3> { P(0, 0, 0), P(0, 0, 300) };
            List<List<Vec3>> runs = RunPath.Split(line, 100f);
            // 86 chords of 3.49 m: 28 chords (97.7 m) fit a run of 100 m, so 4 runs.
            Program.Check(runs.Count == 4, "300 m in runs of 100 m or less: " + runs.Count);
            for (int i = 0; i < runs.Count; i++)
            {
                float length = 0f;
                for (int k = 1; k < runs[i].Count; k++)
                    length += Vec3.DistanceXZ(runs[i][k - 1], runs[i][k]);
                Program.Check(length <= 100.5f && runs[i].Count - 1 <= RunPath.MaxPieces, "run " + i + " is at most 100 m and 32 pieces: " + length + " / " + (runs[i].Count - 1));
                if (i > 0)
                    Program.Check((runs[i][0] - runs[i - 1][runs[i - 1].Count - 1]).Length < 1e-4f, "run " + i + " starts where the run before ended");
            }

            Program.Near(runs[runs.Count - 1][runs[runs.Count - 1].Count - 1].Z, 300f, 1e-3f, "the last run ends at the end of the line");
            // Every run's chords are at most 3.5 m.
            foreach (List<Vec3> run in runs)
            {
                for (int k = 1; k < run.Count; k++)
                    Program.Check(Vec3.DistanceXZ(run[k - 1], run[k]) <= 3.5f + 1e-3f, "chords are at most 3.5 m");
            }

            // A long line of 1000 m with many pieces needs more runs than its length says (32 pieces of 3.5 m is 112 m).
            Program.Check(RunPath.Split(new List<Vec3> { P(0, 0, 0), P(0, 0, 1000) }, 500f).Count >= 9, "pieces, not only length, cut a run");
            // Short lines: one run.
            Program.Check(RunPath.Split(new List<Vec3> { P(0, 0, 0), P(0, 0, 10) }, 100f).Count == 1, "a short line is one run");
            Program.Check(RunPath.Split(null, 100f).Count == 0 && RunPath.Split(line, 0f).Count == 0 && RunPath.Split(new List<Vec3> { P(0, 0, 0), P(float.NaN, 0, 1) }, 100f).Count == 0, "no line, no length, a point that is not a number: no runs");
            // Each run encodes and decodes (the whole chain stays within 10 cm of the line).
            var bend = new List<Vec3>();
            for (int i = 0; i <= 60; i++)
                bend.Add(P((float)(40.0 * (1.0 - Math.Cos(i * 4.0 / 40.0))), 0f, (float)(40.0 * Math.Sin(i * 4.0 / 40.0))));
            float worst = 0f;
            foreach (List<Vec3> run in RunPath.Split(bend, 100f))
            {
                string text = RunPath.Encode(run, out float yaw);
                float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
                List<Vec3> local = RunPath.Decode(text);
                Program.Check(local.Count > 1, "a run decodes");
                foreach (Vec3 p in local)
                {
                    var w = new Vec3(p.X * c + p.Z * s + run[0].X, 0f, -p.X * s + p.Z * c + run[0].Z);
                    // The distance to the original arc (radius 40 about (40, 0)).
                    float r = (float)Math.Sqrt((w.X - 40f) * (w.X - 40f) + w.Z * w.Z);
                    worst = Math.Max(worst, Math.Abs(r - 40f));
                }
            }

            Program.Check(worst < 0.15f, "the runs of a bend stay within 15 cm of the arc (the chords' sag is 4 cm): " + worst);
        }
    }
}
