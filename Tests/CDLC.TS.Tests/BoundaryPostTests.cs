using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The two posts of GB 5768.2-2022 8.6 that the roadside posts of 0.12.78 left out (the owner, 3 Nov: "你还是有遗漏的。没有把国标里边东西都做出来"): the 公路界碑 (8.6.7, Fig. 246) and the 道口标柱 (8.6.8,
    /// Fig. 247). The numbers of the checks are the figures' (centimetres, here in metres), written out and not read from the constants. The game's own part (the materials, the film at night, the road
    /// under them) is not here.
    /// </summary>
    internal static class BoundaryPostTests
    {
        public static void RunAll()
        {
            Program.Run("boundary posts: both are barriers of the panel, with short settings", Catalogue);
            Program.Run("boundary posts: the stone of Fig. 246, its size, its cut top and its places", StoneGeometry);
            Program.Run("boundary posts: the characters 公路界 stand as the figure sets them, on both faces, not mirrored", StoneText);
            Program.Run("boundary posts: the stones' spacing, the side and the setback, and a run cut in pieces", StonePlaces);
            Program.Run("boundary posts: the post of Fig. 247 b, its size and its bands of film", PostGeometry);
            Program.Run("boundary posts: the two posts of a side stand 2 m and 4 m from the junction, from either end of the line", PostPlaces);
            Program.Run("boundary posts: a post on the joint of two pieces is made once", PostPieces);
        }

        private static void Catalogue()
        {
            foreach (string id in new[] { MarkingTemplates.BoundaryStoneId, MarkingTemplates.JunctionPostId })
            {
                GuideTemplate t = MarkingTemplates.ById(id);
                Program.Check(t != null && t.Group == MarkingTemplates.BarrierGroup && t.IsMarking && t.Standalone && t.Id.StartsWith("bar.", StringComparison.Ordinal), id + " is a barrier of the panel");
                Program.Check(SignCatalog.Choices.Count(c => c.Id == id) == 1, id + " is listed once in the catalogue");
                GuideSpec spec = t.Default();
                Program.Check(System.Text.Encoding.UTF8.GetByteCount(spec.ToString()) < 128, id + ": the settings are short: " + spec);
                Program.Check(spec.Is("where", "l"), id + ": on the side of the other posts by default");
                Program.Check(t.FieldOf("off") != null && t.FieldOf(RunPath.Key) != null && t.FieldOf(MarkingTemplates.PhaseKey) != null && t.FieldOf(MarkingTemplates.TotalKey) != null, id + ": a run with a setback, cut in pieces as the others");
                MarkingPlan plan = MarkingTemplates.PlanOf(t, spec.ToString());
                Program.Check(plan != null && plan.TriangleCount > 0, id + ": the picture of one has something to show");
            }

            Program.Check(MarkingTemplates.ById(MarkingTemplates.BoundaryStoneId).Code == "图246" && MarkingTemplates.ById(MarkingTemplates.JunctionPostId).Code == "图247", "the figures are the codes");
            Program.Check(MarkingTemplates.ById(MarkingTemplates.BoundaryStoneId).Default().Is("gap", "200") && MarkingTemplates.ById(MarkingTemplates.JunctionPostId).Default().Is("at", "s"), "200 m, and the junction at the line's start, by default");
            // Without a road under it a run shows a few posts (the picture, the cursor first look): the stones 3 m apart on the 8 m of its picture, the two posts at 2 and 4.
            MarkingPlan stonePicture = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.BoundaryStoneId), MarkingTemplates.ById(MarkingTemplates.BoundaryStoneId).Default().ToString());
            Program.Check(RoadPostTests.Clusters(RoadPostTests.Of(stonePicture, SolidColour.White), 1f).Count == 3, "the picture shows three stones: " + RoadPostTests.Clusters(RoadPostTests.Of(stonePicture, SolidColour.White), 1f).Count);
            MarkingPlan postPicture = MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.JunctionPostId), MarkingTemplates.ById(MarkingTemplates.JunctionPostId).Default().ToString());
            Program.Check(RoadPostTests.Clusters(RoadPostTests.Of(postPicture, SolidColour.Red), 0.5f).Count == 2, "the picture shows the two posts");
            Program.Check(RoadPosts.JunctionDistances.Length == 2 && RoadPosts.JunctionDistances[0] == 2f && RoadPosts.JunctionDistances[1] == 4f, "Fig. 247 a: 200 and 400 from where the curve begins");
        }

        // ------------------------------------------------------------------ 公路界碑

        private static void StoneGeometry()
        {
            MarkingPlan plan = RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(450f));
            List<Vec3> white = RoadPostTests.Of(plan, SolidColour.White);
            List<float> places = RoadPostTests.Clusters(white, 20f);
            Program.Check(places.Count == 3 && Math.Abs(places[0]) < 0.2f && Math.Abs(places[1] - 200f) < 0.2f && Math.Abs(places[2] - 400f) < 0.2f, "a stone every 200 m from the line's start: " + string.Join(", ", places));
            List<Vec3> one = white.Where(v => v.Z < 100f).ToList();
            Program.Near(RoadPostTests.Max(one.Select(v => v.Y)), 0.50f, 1e-4f, "50 above the ground");
            Program.Near(RoadPostTests.Min(one.Select(v => v.Y)), -0.70f, 1e-4f, "and 70 in it");
            Program.Near(RoadPostTests.Max(one.Select(v => v.X)) - RoadPostTests.Min(one.Select(v => v.X)), 0.15f, 1e-4f, "15 wide");
            Program.Near(RoadPostTests.Max(one.Select(v => v.Z)) - RoadPostTests.Min(one.Select(v => v.Z)), 0.15f, 1e-4f, "15 deep: a square prism");
            // The top's two corners are cut 2 by 2: the flat top is 11 across and the sides end 2 under it.
            Program.Near(RoadPostTests.Max(one.Where(v => v.Y > 0.499f).Select(v => v.X)), 0.055f, 1e-4f, "the flat top is 11 across");
            Program.Near(RoadPostTests.Max(one.Where(v => v.X > 0.0749f && v.Y > 0f).Select(v => v.Y)), 0.48f, 1e-4f, "the side goes up to 48, the cut's 2 under the top");
            Program.Check(RoadPostTests.Of(plan, SolidColour.White, DetailKind.Painted).Count == white.Count, "painted white, no film");
            Program.Check(RoadPostTests.Of(plan, SolidColour.Black).Count > 100, "the characters are black");
            Program.Check(RoadPostTests.Of(plan, SolidColour.Red).Count == 0 && RoadPostTests.Of(plan, SolidColour.Blue).Count == 0, "and no other colour is on it");
        }

        private static void StoneText()
        {
            if (FontLetterTests.Font == null)
            {
                Program.Check(false, "the bundled font is needed for the characters");
                return;
            }

            MarkingLetters.Font = FontLetterTests.Font;
            MarkingPlan plan = RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(10f));
            List<Vec3> ink = RoadPostTests.Of(plan, SolidColour.Black);
            // Fig. 246: three characters 8 high and wide, 2.5 apart; the first 5 under the top (50), so their middles are at 41, 30.5 and 20 above the ground; centred across.
            var reference = new MeshBuilder();
            for (int i = 0; i < 3; i++)
                Program.Check(MarkingLetters.AddCharacter(reference, "公路界"[i], 0f, 0.41f - i * 0.105f, 0.08f, 0.08f, false), "the font has " + "公路界"[i]);
            var want = reference.Positions.Select(p => (x: p.X, y: p.Z)).OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            Program.Check(want.Count > 100, "the characters have outlines: " + want.Count);
            foreach (bool minus in new[] { true, false })
            {
                // The face toward -Z is looked at by the one whose right is +X; the face toward +Z by the one whose right is -X.
                var face = ink.Where(v => minus ? v.Z < -0.0749f : v.Z > 0.0749f).Select(v => (x: minus ? v.X : -v.X, y: v.Y)).OrderBy(p => p.x).ThenBy(p => p.y).ToList();
                string name = minus ? "the -Z face" : "the +Z face";
                Program.Check(face.Count == want.Count, name + " has the characters: " + face.Count + " against " + want.Count);
                if (face.Count != want.Count)
                    continue;
                float worst = 0f;
                for (int i = 0; i < face.Count; i++)
                    worst = Math.Max(worst, Math.Max(Math.Abs(face[i].x - want[i].x), Math.Abs(face[i].y - want[i].y)));
                Program.Check(worst < 1e-3f, name + ": 公路界 as Fig. 246 sets it, upright and not mirrored, the largest difference " + worst);
            }

            float top = ink.Max(v => v.Y), bottom = ink.Min(v => v.Y);
            Program.Check(top <= 0.45f + 1e-3f && bottom >= 0.16f - 1e-3f, $"the column is between 16 and 45 above the ground: {bottom} to {top}");
        }

        private static void StonePlaces()
        {
            Program.Check(RoadPostTests.Clusters(RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "gap=500", RoadPostTests.Straight(1100f)), SolidColour.White), 20f).Count == 3, "500 m: three on 1100 m");
            Program.Check(RoadPostTests.Clusters(RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "gap=100", RoadPostTests.Straight(450f)), SolidColour.White), 20f).Count == 5, "100 m, the denser spacing for curves: five on 450 m");
            int low = RoadPostTests.Clusters(RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "gap=7", RoadPostTests.Straight(450f)), SolidColour.White), 20f).Count;
            Program.Check(low == 3, "a spacing that is not one of the choices is the default 200 m: " + low);
            int high = RoadPostTests.Clusters(RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "gap=9000", RoadPostTests.Straight(1100f)), SolidColour.White), 20f).Count;
            Program.Check(high == 6, "also a very large one: 200 m, six on 1100 m: " + high);

            // The side: with the traffic on the line's left (where l) the stone is moved away from it, to the right of the line, by the setback; with the traffic on the right to the left.
            List<Vec3> left = RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "off=300", RoadPostTests.Straight(10f)), SolidColour.White);
            List<Vec3> right = RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "off=300;where=r", RoadPostTests.Straight(10f)), SolidColour.White);
            float lx = (RoadPostTests.Max(left.Select(v => v.X)) + RoadPostTests.Min(left.Select(v => v.X))) * 0.5f, rx = (RoadPostTests.Max(right.Select(v => v.X)) + RoadPostTests.Min(right.Select(v => v.X))) * 0.5f;
            Program.Check(Math.Abs(Math.Abs(lx) - 3f) < 0.05f && Math.Abs(Math.Abs(rx) - 3f) < 0.05f && lx * rx < 0f, $"the setback is 3 m, to opposite sides for the two: {lx}, {rx}");
            List<Vec3> centred = RoadPostTests.Of(RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(10f)), SolidColour.White);
            Program.Check(Math.Abs(RoadPostTests.Max(centred.Select(v => v.X)) + RoadPostTests.Min(centred.Select(v => v.X))) < 1e-3f, "no setback: on the line");

            // A run cut in pieces: 1100 m in two (450 and 650): the stones at 0, 200 ... 1000 are six, three in each, the second piece's at 150, 350 and 550 of its own.
            MarkingPlan whole = RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(1100f), 0f, 1100f);
            MarkingPlan a = RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(450f), 0f, 1100f), b = RoadPostTests.PlanOf(MarkingTemplates.BoundaryStoneId, "", RoadPostTests.Straight(650f), 450f, 1100f);
            Program.Check(RoadPostTests.Clusters(RoadPostTests.Of(whole, SolidColour.White), 20f).Count == 6, "six on the whole run");
            List<float> pa = RoadPostTests.Clusters(RoadPostTests.Of(a, SolidColour.White), 20f), pb = RoadPostTests.Clusters(RoadPostTests.Of(b, SolidColour.White), 20f);
            Program.Check(pa.Count == 3 && pb.Count == 3 && Math.Abs(pb[0] - 150f) < 0.2f && Math.Abs(pb[1] - 350f) < 0.2f && Math.Abs(pb[2] - 550f) < 0.2f, "the pieces go on where the last ended: " + string.Join(", ", pb));
        }

        // ------------------------------------------------------------------ 道口标柱

        private static void PostGeometry()
        {
            MarkingPlan plan = RoadPostTests.PlanOf(MarkingTemplates.JunctionPostId, "", RoadPostTests.Straight(10f));
            List<Vec3> red = RoadPostTests.Of(plan, SolidColour.Red, DetailKind.Reflective), film = RoadPostTests.Of(plan, SolidColour.White, DetailKind.Reflective), plain = RoadPostTests.Of(plan, SolidColour.White, DetailKind.Painted);
            Program.Check(RoadPostTests.Of(plan, SolidColour.Red).Count == red.Count && RoadPostTests.Of(plan, SolidColour.White).Count == film.Count + plain.Count, "the red is film, the white is film or the plain part in the ground");
            List<Vec3> redOne = red.Where(v => v.Z < 3f).ToList(), filmOne = film.Where(v => v.Z < 3f).ToList(), plainOne = plain.Where(v => v.Z < 3f).ToList();
            Program.Check(redOne.Count > 0 && filmOne.Count > 0 && plainOne.Count > 0, "a post has red film, white film and a plain part");
            // Bands of 20 from the top: red 60 to 80, white 40 to 60, red 20 to 40, white 0 to 20; 80 above the ground.
            Program.Check(redOne.All(v => (v.Y > 0.2f - 1e-4f && v.Y < 0.4f + 1e-4f) || (v.Y > 0.6f - 1e-4f && v.Y < 0.8f + 1e-4f)), "the red is at 20 to 40 and 60 to 80");
            Program.Check(filmOne.All(v => (v.Y > -1e-4f && v.Y < 0.2f + 1e-4f) || (v.Y > 0.4f - 1e-4f && v.Y < 0.6f + 1e-4f)), "the white film at 0 to 20 and 40 to 60");
            Program.Near(RoadPostTests.Max(redOne.Select(v => v.Y)), 0.80f, 1e-4f, "80 above the ground, the top red");
            Program.Check(redOne.Any(v => Math.Abs(v.Y - 0.80f) < 1e-4f && Math.Abs(v.X) < 1e-4f && Math.Abs(v.Z - 2f) < 1e-4f), "the top is closed: a cap, its middle on the axis");
            Program.Near(RoadPostTests.Min(redOne.Select(v => v.Y)), 0.20f, 1e-4f, "red from 20");
            Program.Near(RoadPostTests.Min(filmOne.Select(v => v.Y)), 0f, 1e-4f, "the white film from the ground");
            Program.Near(RoadPostTests.Max(plainOne.Select(v => v.Y)), 0f, 1e-4f, "the plain part is the part in the ground");
            Program.Near(RoadPostTests.Min(plainOne.Select(v => v.Y)), -0.25f, 1e-4f, "25 deep (an estimate: the figure draws it without a number)");
            // phi 12: every vertex is 6 from the post's axis (the post is at z = 2 on the line, x = 0).
            float widest = 0f, narrowest = 1f;
            foreach (Vec3 v in redOne.Concat(filmOne).Concat(plainOne).Where(v => Math.Abs(v.Z - 2f) < 0.1f))
            {
                float r = (float)Math.Sqrt(v.X * v.X + (v.Z - 2f) * (v.Z - 2f));
                // The centre of an end cap is on the axis.
                if (r < 1e-3f)
                    continue;
                widest = Math.Max(widest, r);
                narrowest = Math.Min(narrowest, r);
            }

            Program.Check(widest < 0.06f + 1e-4f && narrowest > 0.06f - 1e-4f, $"phi 12: every point 6 from the axis ({narrowest} to {widest})");
            int ring = redOne.Where(v => Math.Abs(v.Y - 0.4f) < 1e-4f && Math.Abs(v.Z - 2f) < 0.1f).Select(v => ((int)Math.Round(v.X * 1000f), (int)Math.Round((v.Z - 2f) * 1000f))).Distinct().Count();
            Program.Check(ring >= 12, "round, not a prism of a few sides: " + ring + " points to a ring");
        }

        private static List<float> Posts(string settings, float length, float phase = 0f, float total = 0f)
        {
            MarkingPlan plan = RoadPostTests.PlanOf(MarkingTemplates.JunctionPostId, settings, RoadPostTests.Straight(length), phase, total);
            return RoadPostTests.Clusters(RoadPostTests.Of(plan, SolidColour.Red), 0.5f);
        }

        private static void PostPlaces()
        {
            List<float> start = Posts("", 10f);
            Program.Check(start.Count == 2 && Math.Abs(start[0] - 2f) < 0.01f && Math.Abs(start[1] - 4f) < 0.01f, "the junction at the start: posts at 2 and 4: " + string.Join(", ", start));
            List<float> end = Posts("at=e", 10f);
            Program.Check(end.Count == 2 && Math.Abs(end[0] - 6f) < 0.01f && Math.Abs(end[1] - 8f) < 0.01f, "the junction at the end: posts 2 and 4 from it, at 6 and 8: " + string.Join(", ", end));
            List<float> shortStart = Posts("", 3f), shortEnd = Posts("at=e", 3f);
            Program.Check(shortStart.Count == 1 && Math.Abs(shortStart[0] - 2f) < 0.01f, "a line of 3 m has the post at 2 and not the one at 4: " + string.Join(", ", shortStart));
            Program.Check(shortEnd.Count == 1 && Math.Abs(shortEnd[0] - 1f) < 0.01f, "from the end, the post 2 from it, at 1: " + string.Join(", ", shortEnd));
            Program.Check(Posts("", 1.5f).Count == 0 && Posts("at=e", 1.5f).Count == 0, "a line of 1.5 m reaches neither");
            // The posts stand on the line; the setback takes them off it, away from the traffic, as the other posts.
            MarkingPlan set = RoadPostTests.PlanOf(MarkingTemplates.JunctionPostId, "off=50", RoadPostTests.Straight(10f)), setRight = RoadPostTests.PlanOf(MarkingTemplates.JunctionPostId, "off=50;where=r", RoadPostTests.Straight(10f));
            List<Vec3> l = RoadPostTests.Of(set, SolidColour.Red), r = RoadPostTests.Of(setRight, SolidColour.Red);
            float lx = (RoadPostTests.Max(l.Select(v => v.X)) + RoadPostTests.Min(l.Select(v => v.X))) * 0.5f, rx = (RoadPostTests.Max(r.Select(v => v.X)) + RoadPostTests.Min(r.Select(v => v.X))) * 0.5f;
            Program.Check(Math.Abs(Math.Abs(lx) - 0.5f) < 0.01f && Math.Abs(Math.Abs(rx) - 0.5f) < 0.01f && lx * rx < 0f, $"a setback of 50 cm to opposite sides for the two: {lx}, {rx}");
        }

        private static void PostPieces()
        {
            // A run of 12 m cut in two at 5 m: the posts at 2 and 4 are in the first piece, none in the second; from the end (8 and 10) both are in the second.
            Program.Check(Posts("", 5f, 0f, 12f).Count == 2 && Posts("", 7f, 5f, 12f).Count == 0, "from the start: both posts in the first piece, none in the second");
            List<float> second = Posts("at=e", 7f, 5f, 12f);
            Program.Check(Posts("at=e", 5f, 0f, 12f).Count == 0 && second.Count == 2 && Math.Abs(second[0] - 3f) < 0.01f && Math.Abs(second[1] - 5f) < 0.01f, "from the end: both in the second piece, at 3 and 5 of its own: " + string.Join(", ", second));
            // A post on the joint is made once: the run of 12 m cut at 4 m, the post at 4.
            Program.Check(Posts("", 4f, 0f, 12f).Count == 1 && Posts("", 8f, 4f, 12f).Count == 1, "the post at 4 on the joint of two pieces belongs to the second only");
            // ... and the end of the whole run belongs to the last piece: a line of 4 m has both posts.
            Program.Check(Posts("", 4f).Count == 2, "a whole line of 4 m has the post at its very end");
            // The whole length not known (a piece given only where it starts): the end of the piece is taken for the end of the line, so the posts are 2 and 4 from it.
            List<float> unknown = Posts("at=e", 7f, 5f, 0f);
            Program.Check(unknown.Count == 2 && Math.Abs(unknown[0] - 3f) < 0.01f && Math.Abs(unknown[1] - 5f) < 0.01f, "the whole length unknown: 2 and 4 from the end of the piece, at 3 and 5 of its own: " + string.Join(", ", unknown));
            // A run cut in three, every post made once.
            int n = Posts("", 3f, 0f, 9f).Count + Posts("", 3f, 3f, 9f).Count + Posts("", 3f, 6f, 9f).Count;
            Program.Check(n == 2, "in three pieces, two posts in all, not more: " + n);
            int m = Posts("at=e", 3f, 0f, 9f).Count + Posts("at=e", 3f, 3f, 9f).Count + Posts("at=e", 3f, 6f, 9f).Count;
            Program.Check(m == 2, "from the end, too: " + m);
        }
    }
}
