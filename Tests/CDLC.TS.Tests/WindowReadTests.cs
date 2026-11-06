using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.59 (the owner, with the game's profile: "性能占用太高了", AreaFitSystem 3.84 ms): the log of 31 Oct showed one marking's fit take 82 ms and one area's 92 ms, which the bench put in AreaFit.FromStrips
    /// (a window of 66 bands: 22 ms, growing faster than the bands do). What a window of the road is read for is the height at its 30 points, which needs no cutting of the cells. The numbers are those
    /// of FromStrips as it was (the fixtures are SurfaceFixture's): the fit that cuts did not change, the fit for heights gives the heights it does.
    /// </summary>
    internal static class WindowReadTests
    {
        public static void RunAll()
        {
            Program.Run("window read: the fit that cuts is as it was", FitsAreAsTheyWere);
            Program.Run("window read: no two pieces of a fit overlap", NoOverlap);
            Program.Run("window read: a fit for heights gives the heights of the fit that cuts", SameHeights);
            Program.Run("window read: a fit for heights is not for paint", NotForPaint);
        }

        private static AreaFit Cut(int bands, int seed) => AreaFit.FromStrips(SurfaceFixture.Junction(bands, seed), new Vec3(0f, 0f, 0f), 0f, -30f, 30f, -3f, 3f);

        private static AreaFit Loose(int bands, int seed) => AreaFit.ForHeights(SurfaceFixture.Junction(bands, seed), new Vec3(0f, 0f, 0f), 0f, -30f, 30f, -3f, 3f);

        private static void FitsAreAsTheyWere()
        {
            // As FromStrips made them in 0.12.58.
            Program.Check(SurfaceFixture.Summary(Cut(12, 7)) == "188 pieces, 2300 places, 202 elevated, height sum 963.357", "12 bands: " + SurfaceFixture.Summary(Cut(12, 7)));
            Program.Check(SurfaceFixture.Summary(Cut(30, 7)) == "382 pieces, 2948 places, 181 elevated, height sum 836.393", "30 bands: " + SurfaceFixture.Summary(Cut(30, 7)));
            Program.Check(SurfaceFixture.Summary(Cut(66, 7)) == "684 pieces, 3334 places, 31 elevated, height sum 722.352", "66 bands: " + SurfaceFixture.Summary(Cut(66, 7)));
            Program.Check(SurfaceFixture.Summary(Cut(120, 7)) == "1143 pieces, 3562 places, 28 elevated, height sum 686.913", "120 bands: " + SurfaceFixture.Summary(Cut(120, 7)));
        }

        private static void NoOverlap()
        {
            // What paint is cut to, once each: the heights do not tell (the first cell wins), a piece over another would draw it twice.
            foreach (int bands in new[] { 12, 30 })
            {
                AreaFit fit = Cut(bands, 7);
                var scratch = new List<Vec3>();
                int overlapping = 0;
                for (int i = 0; i < fit.PieceCount; i++)
                {
                    for (int j = i + 1; j < fit.PieceCount; j++)
                    {
                        var common = new List<Vec3>(fit.Pieces[i]);
                        Vec3[] b = fit.Pieces[j];
                        for (int e = 0; e < b.Length && common.Count >= 3; e++)
                            AreaFit.ClipBy(common, scratch, b[e], b[(e + 1) % b.Length]);
                        if (common.Count >= 3 && Math.Abs(Polygon.SignedArea2(common)) / 2f > 1e-4f)
                            overlapping++;
                    }
                }

                Program.Check(overlapping == 0, $"{bands} bands: {fit.PieceCount} pieces, {overlapping} pairs of them overlap");
            }
        }

        private static void SameHeights()
        {
            int places = 0, differ = 0;
            double worst = 0.0;
            foreach (int seed in new[] { 7, 11, 23 })
            {
                foreach (int bands in new[] { 12, 30, 66, 120 })
                {
                    AreaFit cut = Cut(bands, seed), loose = Loose(bands, seed);
                    var seen = new HashSet<int>();
                    var found = new List<int>();
                    for (float x = -31.3f; x <= 31.3f; x += 0.37f)
                    {
                        for (float z = -9.1f; z <= 9.1f; z += 0.41f)
                        {
                            bool a = cut.HeightAt(x, z, out float ya, out bool ea, seen, found), b = loose.HeightAt(x, z, out float yb, out bool eb, seen, found);
                            places++;
                            if (a != b || (a && ea != eb))
                            {
                                differ++;
                                continue;
                            }

                            if (a)
                                worst = Math.Max(worst, Math.Abs(ya - yb));
                        }
                    }
                }
            }

            Program.Check(differ == 0, $"{places} places over 3 junctions of 4 sizes: {differ} where one fit has a surface and the other has not, or they differ in being elevated");
            Program.Check(worst < 1e-3, $"and the heights differ by {worst * 1000:0.000} mm at most");
        }

        private static void NotForPaint()
        {
            AreaFit loose = Loose(12, 7);
            bool threw = false;
            try
            {
                loose.Drape(new MeshBuilder());
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }

            Program.Check(threw, "paint cut to cells over one another would be drawn twice: Drape refuses");
            Program.Check(loose.PieceCount < Cut(12, 7).PieceCount + 1 && loose.PieceCount > 0, "and its cells are the bands' triangles, none cut");
        }
    }
}
