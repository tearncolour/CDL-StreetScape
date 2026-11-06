using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.73 (the owner: "升级删除工具，支持只删除一段"): a stretch taken out of a line made of items. The line made up: a polyline of 150 m cut into three items of 50 m (RunPath.Split, as the tool makes them),
    /// each with its phase along the whole line and the whole length (15000 cm).
    /// </summary>
    internal static class LineCutTests
    {
        public static void RunAll()
        {
            Program.Run("line cut: the points of a piece are the path's, a line is items that go on from one another", Pieces);
            Program.Run("line cut: the line's points and the place a ray is nearest it", Pointing);
            Program.Run("line cut: a stretch inside one item, at an item's start, across several items, the whole line", Stretches);
            Program.Run("line cut: a stretch in one chord, parts shorter than a metre, a stretch that is none", Edges);
            Program.Run("line cut: what is left lies on the line it was, and nothing is lost but the stretch", Conserved);
            Program.Run("line cut: the delete tool says how: Shift and click the start and the end", Hints);
        }

        /// <summary>A line of 150 m along +Z (chords of 2 m), with a bend or without, cut into the items the tool makes: 50 m each on the straight one.</summary>
        private static List<Vec3> Polyline(bool bend)
        {
            var points = new List<Vec3>();
            for (int i = 0; i <= 75; i++)
            {
                float s = 2f * i;
                float x = bend ? 12f * (float)Math.Sin(s / 40f) : 0f;
                points.Add(new Vec3(100f + x, 0.01f * s, 200f + s));
            }

            return points;
        }

        private static List<CutPiece> Chain(bool bend = false, string family = "wall", float maxLength = 50f)
        {
            List<Vec3> line = Polyline(bend);
            List<List<Vec3>> runs = RunPath.Split(line, maxLength, 2f);
            var chain = new List<CutPiece>();
            double total = 0.0;
            foreach (List<Vec3> run in runs)
            {
                for (int i = 1; i < run.Count; i++)
                    total += Vec3.DistanceXZ(run[i - 1], run[i]);
            }

            int whole = (int)Math.Round(total * 100.0);
            double done = 0.0;
            foreach (List<Vec3> run in runs)
            {
                string path = RunPath.Encode(run, out float yaw);
                chain.Add(new CutPiece(family, run[0], yaw, path, (int)Math.Round(done * 100.0), whole));
                done += RunPath.Length(path);
            }

            return chain;
        }

        private static CutPiece Make(IList<CutEdit> edits, int i, IList<CutPiece> chain) => new CutPiece(chain[edits[i].Piece].Family, edits[i].Start, edits[i].Yaw, edits[i].Path, edits[i].PhaseCm, edits[i].TotalCm);

        private static string Show(List<CutEdit> edits) => string.Join("; ", edits.ConvertAll(e => $"{e.Kind} #{e.Piece} ph {e.PhaseCm} tl {e.TotalCm} len {(e.Kind == CutKind.Remove ? 0 : new CutPiece("", e.Start, e.Yaw, e.Path, 0, 0).LengthCm)}"));

        private static void Pieces()
        {
            List<CutPiece> chain = Chain(true);
            Program.Check(chain.Count >= 3 && chain.TrueForAll(p => p.Valid), "items of a line: " + chain.Count);
            // The points of a piece are what the path says: the same as RunPath.Decode turned by the yaw and moved to the start.
            CutPiece first = chain[0];
            List<Vec3> own = first.Points(), frame = RunPath.Decode(first.Path);
            bool same = own.Count == frame.Count;
            float s = (float)Math.Sin(first.Yaw), c = (float)Math.Cos(first.Yaw);
            for (int i = 0; same && i < own.Count; i++)
            {
                Vec3 p = frame[i];
                Vec3 world = new Vec3(p.X * c + p.Z * s + first.Start.X, p.Y + first.Start.Y, -p.X * s + p.Z * c + first.Start.Z);
                same = Vec3.DistanceXZ(own[i], world) < 0.002f && Math.Abs(own[i].Y - world.Y) < 0.002f;
            }

            Program.Check(same, "its points are the decoded path in the world");
            Program.Check(Vec3.DistanceXZ(own[own.Count - 1], chain[1].Start) < 0.1f, "an item ends where the next starts: " + Vec3.DistanceXZ(own[own.Count - 1], chain[1].Start));

            Program.Check(LineCut.Follows(chain[0], chain[1]) && LineCut.Follows(chain[1], chain[2]) && !LineCut.Follows(chain[1], chain[0]) && !LineCut.Follows(chain[0], chain[2]), "the next item goes on, the one before does not");
            Program.Check(LineCut.Place(new List<CutPiece> { chain[1] }, chain[2]) == 1 && LineCut.Place(new List<CutPiece> { chain[1] }, chain[0]) == -1 && LineCut.Place(new List<CutPiece> { chain[0] }, chain[2]) == 0, "placed before, after, or not");
            chain = Chain();
            List<CutPiece> other = Chain(false, "rail");
            Program.Check(LineCut.Follows(other[0], other[1]), "the same line of another family is as good a line");
            Program.Check(!LineCut.Follows(chain[0], other[1]), "another sign or other settings: not one line");
            var late = new CutPiece("wall", chain[1].Start, chain[1].Yaw, chain[1].Path, chain[1].PhaseCm + 40, chain[1].TotalCm);
            var near = new CutPiece("wall", chain[1].Start, chain[1].Yaw, chain[1].Path, chain[1].PhaseCm + 5, chain[1].TotalCm);
            Program.Check(!LineCut.Follows(chain[0], late) && LineCut.Follows(chain[0], near), "a phase 40 cm off is another line; 5 cm is rounding");
            var apart = new CutPiece("wall", chain[1].Start + new Vec3(2f, 0f, 0f), chain[1].Yaw, chain[1].Path, chain[1].PhaseCm, chain[1].TotalCm);
            var shorter = new CutPiece("wall", chain[1].Start, chain[1].Yaw, chain[1].Path, chain[1].PhaseCm, chain[1].TotalCm + 100);
            Program.Check(!LineCut.Follows(chain[0], apart) && !LineCut.Follows(chain[0], shorter), "an item 2 m off, or of a line of another length: not one line");
            Program.Check(!new CutPiece("wall", Vec3.Zero, 0f, "", 0, 0).Valid && !new CutPiece("wall", Vec3.Zero, 0f, "xx", 0, 0).Valid && !LineCut.Follows(new CutPiece("wall", Vec3.Zero, 0f, "", 0, 0), chain[1]), "no path, no piece");
        }

        private static void Pointing()
        {
            List<CutPiece> chain = Chain(false);
            List<Vec3> points = LineCut.Line(chain, out List<float> at);
            Program.Check(points.Count == at.Count && points.Count > 70 && Math.Abs(at[at.Count - 1] - 150f) < 0.1f && at[0] == 0f, $"a line of {points.Count} points, 150 m: {at[at.Count - 1]:0.00}");
            bool increasing = true;
            for (int i = 1; i < at.Count; i++)
                increasing &= at[i] > at[i - 1];
            Program.Check(increasing, "the distances go up, and the point two items share is one");

            // A ray from above, down at 70 m along the line, and one that misses it by 40 m.
            Vec3 hit = new Vec3(100f, 0f, 270f);
            float s = LineCut.Nearest(points, at, hit + new Vec3(0f, 30f, 0f), hit, 1f, out float gap);
            Program.Check(Math.Abs(s - 70f) < 0.6f && gap < 1.2f, $"the ray down at 70 m: {s:0.00} m, {gap:0.00} m off");
            Vec3 across = new Vec3(100f, 0f, 270f);
            LineCut.Nearest(points, at, across + new Vec3(40f, 30f, 0f), across + new Vec3(40f, 0f, 0f), 1f, out float far);
            Program.Check(far > 38f, "a ray 40 m away: " + far);
            float before = LineCut.Nearest(points, at, new Vec3(100f, 30f, 150f), new Vec3(100f, 0f, 150f), 1f, out _);
            float after = LineCut.Nearest(points, at, new Vec3(100f, 30f, 400f), new Vec3(100f, 0f, 400f), 1f, out _);
            Program.Check(before == 0f && Math.Abs(after - 150f) < 0.1f, $"beyond the ends: the ends: {before} {after}");
            LineCut.Nearest(points, at, new Vec3(100f, 1f, 100f), new Vec3(100f, 1f, 100f), 0f, out float none);
            Program.Check(none > 90f, "a ray that is a point");
            // A ray along X at 1 m over the line at 70 m (its height there is 0.7): it meets the line lifted by 1 m, and misses the line itself by 1 m.
            Vec3 level = new Vec3(100f, 1.7f, 270f);
            LineCut.Nearest(points, at, level - new Vec3(10f, 0f, 0f), level + new Vec3(10f, 0f, 0f), 1f, out float lifted);
            LineCut.Nearest(points, at, level - new Vec3(10f, 0f, 0f), level + new Vec3(10f, 0f, 0f), 0f, out float plain);
            Program.Check(lifted < 0.05f && plain > 0.9f, $"the ray is taken against the line lifted by the height of a wall: {lifted:0.00} and {plain:0.00}");

            // A line that is the items from the second on starts at that item's phase.
            List<CutPiece> later = chain.GetRange(1, 2);
            LineCut.Line(later, out List<float> lateAt);
            Program.Check(lateAt[0] == later[0].PhaseCm / 100f && Math.Abs(lateAt[lateAt.Count - 1] - later[1].EndCm / 100f) < 0.1f, $"a line from the second item starts at its phase, {lateAt[0]}, and ends at its end: {lateAt[lateAt.Count - 1]}");

            List<Vec3> part = LineCut.Stretch(points, at, 30f, 41.5f);
            Program.Check(part.Count >= 5 && Math.Abs(part[0].Z - 230f) < 0.01f && Math.Abs(part[part.Count - 1].Z - 241.5f) < 0.01f, $"the stretch from 30 to 41.5 m: {part.Count} points from {part[0].Z} to {part[part.Count - 1].Z}");
            Program.Check(LineCut.Stretch(points, at, 5f, 5f).Count == 0 && LineCut.Stretch(points, at, -3f, 2f).Count > 1 && LineCut.Stretch(points, at, 149f, 400f)[LineCut.Stretch(points, at, 149f, 400f).Count - 1].Z == 350f, "a stretch of no length is none; one past the ends ends at the ends");
        }

        private static void Stretches()
        {
            List<CutPiece> chain = Chain();
            int total = chain[0].TotalCm;
            // Inside the middle item: 60 to 70 m.
            Program.Check(LineCut.Cut(chain, 60f, 70f, out List<CutEdit> edits), "a stretch inside one item");
            CutEdit write0 = edits.Find(e => e.Piece == 0), head = edits.Find(e => e.Piece == 1 && e.Kind == CutKind.Write), tail = edits.Find(e => e.Piece == 1 && e.Kind == CutKind.Add), write2 = edits.Find(e => e.Piece == 2);
            Program.Check(edits.Count == 4 && write0.Kind == CutKind.Write && head.Kind == CutKind.Write && tail.Kind == CutKind.Add && write2.Kind == CutKind.Write, "the first is told where the line ends, the middle is shortened and made again for the rest, the last is moved up: " + Show(edits));
            int a = 6000, b = 7000;
            Program.Check(write0.PhaseCm == chain[0].PhaseCm && write0.TotalCm == a && write0.Path == chain[0].Path, "the item before: the same, the line ends at the stretch: " + write0.TotalCm);
            Program.Check(head.PhaseCm == chain[1].PhaseCm && head.TotalCm == a && new CutPiece("", head.Start, head.Yaw, head.Path, 0, 0).LengthCm == a - chain[1].PhaseCm, "the head of the middle item ends at the stretch with the line");
            Program.Check(tail.PhaseCm == 0 && tail.TotalCm == total - b && new CutPiece("", tail.Start, tail.Yaw, tail.Path, 0, 0).LengthCm == chain[1].EndCm - b, "the tail of it starts at phase 0 and is the rest of the line's length: " + tail.TotalCm);
            Program.Check(write2.PhaseCm == chain[2].PhaseCm - b && write2.TotalCm == total - b && write2.Path == chain[2].Path, "the item after is moved up by the stretch: " + write2.PhaseCm);
            // The tail continues the item after it: its end is where that starts, its length is where that phase begins.
            CutPiece tailPiece = Make(edits, edits.IndexOf(tail), chain), lastPiece = Make(edits, edits.IndexOf(write2), chain);
            Program.Check(LineCut.Follows(tailPiece, lastPiece), "the tail and the item after it are one line again");
            Program.Check(!LineCut.Follows(Make(edits, edits.IndexOf(head), chain), tailPiece) && LineCut.Follows(Make(edits, edits.IndexOf(write0), chain), Make(edits, edits.IndexOf(head), chain)), "the head goes on from the first item and is no line with the tail");

            // At the start of an item: 50 to 75 m.
            Program.Check(LineCut.Cut(chain, 50f, 75f, out edits), "a stretch from the start of an item");
            Program.Check(edits.Count == 3 && edits[0].Kind == CutKind.Write && edits[0].Piece == 0 && edits[0].TotalCm == 5000 && edits[1].Kind == CutKind.Write && edits[1].Piece == 1 && edits[1].PhaseCm == 0 && edits[1].TotalCm == total - 7500 && edits[2].Piece == 2 && edits[2].PhaseCm == chain[2].PhaseCm - 7500,
                "the item the stretch begins at is only a tail: " + Show(edits));
            // At the end of an item: 25 to 50 m.
            Program.Check(LineCut.Cut(chain, 25f, 50f, out edits) && edits.Count == 3 && edits[0].Kind == CutKind.Write && edits[0].Piece == 0 && edits[0].TotalCm == 2500 && new CutPiece("", edits[0].Start, edits[0].Yaw, edits[0].Path, 0, 0).LengthCm == 2500 && edits[1].Piece == 1 && edits[1].PhaseCm == 0 && edits[2].Piece == 2,
                "to the end of an item: only a head of the first, the next moved up: " + Show(edits));

            // Across the items: 40 to 110 m: a head, the middle gone, a tail.
            Program.Check(LineCut.Cut(chain, 40f, 110f, out edits), "a stretch across three items");
            Program.Check(edits.Count == 3 && edits[0].Kind == CutKind.Write && edits[0].Piece == 0 && edits[0].TotalCm == 4000 && edits[1].Kind == CutKind.Remove && edits[1].Piece == 1 && edits[2].Kind == CutKind.Write && edits[2].Piece == 2 && edits[2].PhaseCm == 0 && edits[2].TotalCm == total - 11000,
                "the first is cut, the middle removed, the last is a tail: " + Show(edits));
            // The part before and the part after, taken together with the stretch, are the line.
            int kept = new CutPiece("", edits[0].Start, edits[0].Yaw, edits[0].Path, 0, 0).LengthCm + new CutPiece("", edits[2].Start, edits[2].Yaw, edits[2].Path, 0, 0).LengthCm;
            Program.Check(Math.Abs(kept - (total - 7000)) <= 3, $"what is left is the line less 70 m: {kept} of {total} cm");

            // The whole line, a stretch beyond the ends.
            Program.Check(LineCut.Cut(chain, -5f, 400f, out edits) && edits.Count == 3 && edits.TrueForAll(e => e.Kind == CutKind.Remove), "past both ends: all removed: " + Show(edits));
            Program.Check(LineCut.Cut(chain, 0f, 150f, out edits) && edits.TrueForAll(e => e.Kind == CutKind.Remove) && edits.Count == 3, "exactly the line: all removed");
            // From the start to the middle of an item, and from there to the end.
            Program.Check(LineCut.Cut(chain, 0f, 60f, out edits) && edits[0].Kind == CutKind.Remove && edits[1].Kind == CutKind.Write && edits[1].PhaseCm == 0 && edits[1].TotalCm == total - 6000 && edits[2].PhaseCm == chain[2].PhaseCm - 6000, "from the start: the line starts at the end of the stretch: " + Show(edits));
            Program.Check(LineCut.Cut(chain, 90f, 150f, out edits) && edits[0].Kind == CutKind.Write && edits[0].TotalCm == 9000 && edits[1].Kind == CutKind.Write && edits[1].TotalCm == 9000 && edits[2].Kind == CutKind.Remove, "to the end: the line ends at the start of the stretch: " + Show(edits));
        }

        private static void Edges()
        {
            List<CutPiece> chain = Chain();
            int total = chain[0].TotalCm;
            // Shorter than a chord (chords are 2 m): 60.0 to 60.4.
            Program.Check(LineCut.Cut(chain, 60.6f, 61f, out List<CutEdit> edits) && edits.Count == 4, "a stretch of 40 cm in one chord: " + Show(edits));
            CutEdit head = edits.Find(e => e.Piece == 1 && e.Kind == CutKind.Write), tail = edits.Find(e => e.Piece == 1 && e.Kind == CutKind.Add);
            int lengths = new CutPiece("", head.Start, head.Yaw, head.Path, 0, 0).LengthCm + new CutPiece("", tail.Start, tail.Yaw, tail.Path, 0, 0).LengthCm;
            Program.Check(Math.Abs(lengths - (5000 - 40)) <= 2, "the two parts are the item less 40 cm: " + lengths);

            // A part of an item shorter than a metre is taken with the stretch: the stretch begins 0.3 m into an item, and ends 0.5 m before the end of one.
            Program.Check(LineCut.Cut(chain, 50.3f, 70f, out edits), "begins 0.3 m into an item");
            Program.Check(edits.Find(e => e.Piece == 0).TotalCm == 5000 && !edits.Exists(e => e.Piece == 1 && e.Kind == CutKind.Add) && edits.Find(e => e.Piece == 1).PhaseCm == 0, "the 0.3 m is taken too: the line before ends at the item's start: " + Show(edits));
            Program.Check(LineCut.Cut(chain, 60f, 99.5f, out edits), "ends 0.5 m before the end of an item");
            CutEdit rest = edits.Find(e => e.Piece == 2);
            Program.Check(edits.Find(e => e.Piece == 1).Kind == CutKind.Write && edits.Find(e => e.Piece == 1).TotalCm == 6000 && rest.PhaseCm == 0 && rest.TotalCm == total - 10000 && rest.Path == chain[2].Path, "the 0.5 m is taken too: the item after is the line's start: " + Show(edits));
            // 1 m of an item is kept.
            Program.Check(LineCut.Cut(chain, 51.2f, 70f, out edits) && edits.Exists(e => e.Piece == 1 && e.Kind == CutKind.Write && e.PhaseCm == chain[1].PhaseCm && e.TotalCm == 5120), "1.2 m of an item is kept: " + Show(edits));

            // Nothing to cut.
            Program.Check(!LineCut.Cut(chain, 70f, 60f, out edits) && edits.Count == 0, "a stretch backwards is none");
            Program.Check(!LineCut.Cut(chain, 60f, 60f, out _) && !LineCut.Cut(chain, 160f, 190f, out _) && !LineCut.Cut(chain, -20f, -2f, out _), "no length, or past the ends: none");
            Program.Check(!LineCut.Cut(new List<CutPiece>(), 0f, 10f, out _), "no items, nothing");

            // A line whose whole length is not known (0): the line is taken to end at its last item.
            var unknown = new List<CutPiece>();
            foreach (CutPiece p in chain)
                unknown.Add(new CutPiece(p.Family, p.Start, p.Yaw, p.Path, p.PhaseCm, 0));
            Program.Check(LineCut.Cut(unknown, 60f, 70f, out edits) && edits.Find(e => e.Piece == 2).TotalCm == chain[2].EndCm - 7000 && edits.Find(e => e.Piece == 0).TotalCm == 6000, "a line of no known length ends at its last item: " + Show(edits));

            // Splitting a climbing path inside a chord: the tail starts as high as the chord is there.
            var climb = new CutPiece("wall", new Vec3(10f, 5f, 20f), 0.3f, "300:0:500|300:200:0", 0, 600);
            LineCut.Split(climb, 100, out _, out string restPath, out Vec3 at100, out float yawAt);
            Program.Check(Math.Abs(at100.Y - 5.5f) < 0.001f && restPath == "200:0:500|300:200:0" && Math.Abs(yawAt - 0.3f) < 0.001f, $"a cut 1 m into a chord that climbs 500 per mille: {at100.Y:0.000} high, the rest {restPath}");
            LineCut.Split(climb, 450, out _, out string rest2, out Vec3 at450, out float yaw450);
            Program.Check(Math.Abs(at450.Y - (5f + 1.5f)) < 0.001f && rest2 == "150:0:0" && Math.Abs(yaw450 - 0.32f) < 0.001f, $"a cut in the second chord, after the turn of 0.02 rad: yaw {yaw450:0.000}, {at450.Y:0.000} high, the rest {rest2}");

            // Splitting a path itself: at a chord's start, inside a chord, past the end.
            CutPiece piece = chain[0];
            LineCut.Split(piece, 0, out string h0, out string t0, out Vec3 from0, out float yaw0);
            Program.Check(h0 == "" && t0.Split('|').Length == piece.Path.Split('|').Length && Vec3.DistanceXZ(from0, piece.Start) < 0.001f && Math.Abs(yaw0 - piece.Yaw) < 0.001f, "split at 0: nothing before, all after");
            LineCut.Split(piece, piece.LengthCm + 10, out string h9, out string t9, out Vec3 from9, out _);
            Program.Check(h9 == piece.Path && t9 == "" && Vec3.DistanceXZ(from9, piece.Points()[piece.Points().Count - 1]) < 0.01f, "split past the end: all before, nothing after");
        }

        private static void Conserved()
        {
            var random = new Random(11);
            for (int round = 0; round < 40; round++)
            {
                List<CutPiece> chain = Chain(round % 2 == 0, "wall", 20f + random.Next(0, 45));
                List<Vec3> original = LineCut.Line(chain, out List<float> at);
                int total = chain[0].TotalCm;
                float a = (float)(random.NextDouble() * 150.0), b = a + (float)(0.2 + random.NextDouble() * 70.0);
                if (!LineCut.Cut(chain, a, b, out List<CutEdit> edits))
                {
                    Program.Check(b <= 0f || a >= chain[chain.Count - 1].EndCm / 100f, $"round {round}: no cut for {a:0.0} to {b:0.0}");
                    continue;
                }

                // Whatever is made of the edits lies on the original line (a few cm), and the lengths add up to the line less the stretch (as widened).
                double lengthOut = 0.0;
                bool on = true;
                var kept = new List<CutPiece>();
                for (int i = 0; i < edits.Count; i++)
                {
                    if (edits[i].Kind == CutKind.Remove)
                        continue;
                    CutPiece made = Make(edits, i, chain);
                    kept.Add(made);
                    lengthOut += made.LengthCm;
                    foreach (Vec3 p in made.Points())
                        on &= DistanceToLine(original, p) < 0.04f;
                }

                int removedCm = 0;
                foreach (CutEdit e in edits)
                {
                    if (e.Kind == CutKind.Remove)
                        removedCm += chain[e.Piece].LengthCm;
                }

                double wanted = total - Math.Round((Math.Min(b, total / 100f) - a) * 100.0);
                Program.Check(on, $"round {round}: what is left lies on the line");
                Program.Check(lengthOut <= wanted + 2 && lengthOut >= wanted - 2 * LineCut.MinKeepCm - 4, $"round {round}: {lengthOut / 100.0:0.00} m left of {total / 100.0:0.00} m, the stretch {b - a:0.00} m (a part under a metre is taken too)");
                // The two ends of the stretch: no part kept lies inside it.
                bool outside = true;
                foreach (CutPiece p in kept)
                {
                    List<Vec3> points = p.Points();
                    for (int i = 1; i < points.Count - 1; i++)
                    {
                        float along = AlongLine(original, at, points[i]);
                        outside &= !(along > a + 1.1f && along < b - 1.1f);
                    }
                }

                Program.Check(outside, $"round {round}: nothing of what is left is inside the stretch");
            }
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                string start = HintText.Of(new HintInfo { Kind = "cut-start", Count = 3 }, zh), end = HintText.Of(new HintInfo { Kind = "cut-end", Count = 2, Left = 12.5f }, zh);
                Program.Check(start.Contains("Shift") && end.Contains("12.5") && end.Contains("2"), (zh ? "zh" : "en") + ": the line of help names Shift, the length and the pieces: " + start + " / " + end);
                Program.Check(HintText.Of(new HintInfo { Kind = "cut-end", Bad = true }, zh).Length > end.Length, (zh ? "zh" : "en") + ": a cursor away from the line says so first");
                List<string[]> delete = HintText.Keys(new HintInfo { Kind = "delete" }, zh), keys = HintText.Keys(new HintInfo { Kind = "cut-end" }, zh), first = HintText.Keys(new HintInfo { Kind = "cut-start" }, zh);
                Program.Check(delete.Exists(k => k[0].Contains("Shift")), (zh ? "zh" : "en") + ": the keys of the delete mode name Shift and a click");
                Program.Check(keys.Count == 2 && keys[0][0] == (zh ? "左键" : "Left click") && keys[1][0].Contains("Esc"), (zh ? "zh" : "en") + ": the end: a click, and the way out of the stretch");
                Program.Check(first.Count >= 1 && first[0][0] == (zh ? "左键" : "Left click") && first[first.Count - 1][0].Contains("Esc"), (zh ? "zh" : "en") + ": the start: a click, and the way out");
            }
        }

        private static float DistanceToLine(List<Vec3> line, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Count; i++)
            {
                Vec3 d = line[i] - line[i - 1];
                float l2 = Vec3.Dot(d, d), t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, Vec3.Dot(p - line[i - 1], d) / l2)) : 0f;
                best = Math.Min(best, (p - (line[i - 1] + d * t)).Length);
            }

            return best;
        }

        private static float AlongLine(List<Vec3> line, List<float> at, Vec3 p)
        {
            float best = float.MaxValue, along = 0f;
            for (int i = 1; i < line.Count; i++)
            {
                Vec3 d = line[i] - line[i - 1];
                float l2 = Vec3.Dot(d, d), t = l2 > 1e-9f ? Math.Max(0f, Math.Min(1f, Vec3.Dot(p - line[i - 1], d) / l2)) : 0f;
                float distance = (p - (line[i - 1] + d * t)).Length;
                if (distance < best)
                {
                    best = distance;
                    along = at[i - 1] + (at[i] - at[i - 1]) * t;
                }
            }

            return along;
        }
    }
}
