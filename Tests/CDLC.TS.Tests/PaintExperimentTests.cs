using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The experiment of the road paint (0.12.83): its file, the maps it puts on the paint, and the store's way of building every tile again.</summary>
    internal static class PaintExperimentTests
    {
        public static void RunAll()
        {
            Program.Run("paint experiment: the file's keys, the last line, the comments, what is wrong", Parse);
            Program.Run("paint experiment: what the file means for the paint", Meaning);
            Program.Run("paint experiment: the grain scales the baked slope and 1 is the baked map", Grain);
            Program.Run("paint experiment: the smoothness is moved to the mean asked for, the rest of the mask is as baked", Smoothness);
            Program.Run("paint experiment: the baked road paint is faint and not smooth", Baked);
            Program.Run("paint experiment: every tile is built again, and a build under way is out of date", BumpAll);
        }

        private static MarkingExperiment Read(string text, out List<string> problems)
        {
            problems = new List<string>();
            return MarkingExperiment.Parse(text, problems);
        }

        private static void Parse()
        {
            MarkingExperiment e = Read("# a comment\r\nmarkingNormals = off   # and one at the end\r\nmarkingGrain=2.5\nMARKINGSMOOTHNESS = 0.1\n", out List<string> problems);
            Program.Check(problems.Count == 0 && e.Normals == false && e.Grain == 2.5f && e.Smoothness == 0.1f, "the three keys, in any case, with comments: " + e.Describe());
            Program.Check(Read("markingGrain = 1\nmarkingGrain = 3", out _).Grain == 3f, "the last line of a key wins");
            Program.Check(Read("markingGrain = off", out _).Grain == 0f && Read("markingGrain = none", out _).Grain == 0f && Read("markingGrain = 0", out _).Grain == 0f, "off, none and 0 are no grain");
            Program.Check(Read("markingGrain = 4", out List<string> p4).Grain == 4f && p4.Count == 0, "4 is the most");
            Program.Check(Read("markingNormals = on", out _).Normals == true, "normals on");

            foreach (string bad in new[] { "markingGrain = 4.1", "markingGrain = -0.1", "markingGrain = loud", "markingGrain = NaN", "markingSmoothness = 1.01", "markingSmoothness = -1", "markingSmoothness = x", "markingNormals = maybe", "bogus = 1", "no equals sign", "= 3" })
            {
                MarkingExperiment r = Read(bad, out List<string> why);
                Program.Check(r.IsEmpty && why.Count == 1, "'" + bad + "' is named and left out: " + string.Join("; ", why));
            }

            MarkingExperiment mixed = Read("markingGrain = 9\nmarkingSmoothness = 0.5\nbogus = 1", out List<string> two);
            Program.Check(mixed.Grain == null && mixed.Smoothness == 0.5f && two.Count == 2 && two[0].StartsWith("line 1", StringComparison.Ordinal) && two[1].StartsWith("line 3", StringComparison.Ordinal), "a bad line does not take the good ones with it, and is named by its number");
            Program.Check(Read(null, out _).IsEmpty && Read("", out _).IsEmpty && Read("\n# nothing\n", out _).IsEmpty, "nothing is the defaults");
            Program.Check(Read("markingSmoothness = 0,5", out List<string> comma).IsEmpty && comma.Count == 1, "a comma is not a decimal point in this file");
            Program.Check(Read("markingSmoothness = 0.25", out _).Equals(Read("markingSmoothness=0.25 # same", out _)) && !Read("markingSmoothness = 0.25", out _).Equals(Read("markingSmoothness = 0.26", out _))
                && !MarkingExperiment.None.Equals(Read("markingNormals = on", out _)), "equal when they say the same");
            Program.Check(MarkingExperiment.Parse(MarkingExperiment.Example(), null).IsEmpty, "the example is all comments");
            foreach (string key in new[] { "markingNormals", "markingGrain", "markingSmoothness" })
                Program.Check(MarkingExperiment.Example().Contains("# " + key + " = "), "the example names " + key);
        }

        private static void Meaning()
        {
            MarkingExperiment none = MarkingExperiment.None;
            Program.Check(none.NormalsOn && none.GrainScale == 1f && !none.ChangesMaps && none.PlainSmooth == MarkingExperiment.PlainSmoothness && none.Describe().Contains("defaults"), "no file: normals on, the baked grain, nothing replaced, the plain paint's own smoothness");
            Program.Check(!Read("markingGrain = 1", out _).ChangesMaps, "a grain of 1 is the baked one: no maps are replaced");
            Program.Check(Read("markingGrain = 2", out _).ChangesMaps && Read("markingGrain = 0", out _).ChangesMaps && Read("markingSmoothness = 0.2", out _).ChangesMaps, "another grain, or a smoothness, replaces them");
            Program.Check(!Read("markingNormals = off", out _).ChangesMaps && !Read("markingNormals = off", out _).NormalsOn, "the normals are not the maps");
            Program.Check(Read("markingSmoothness = 0.9", out _).PlainSmooth == 0.9f && MarkingExperiment.PlainSmoothness == 0.28f, "the plain paint takes the smoothness as a number; its own is 0.28");
            Program.Check(Read("markingGrain = 2, markingSmoothness = 0", out _).IsEmpty, "two keys on a line are one wrong line");
            Program.Check(Read("markingNormals = off\nmarkingGrain = 0.5\nmarkingSmoothness = 0.1", out _).Describe() == "normals off, grain 0.5, smoothness 0.1", "the log line");
        }

        private static DetailSurface s_Paint;

        private static DetailSurface Paint() => s_Paint ?? (s_Paint = DetailSurfaces.Make(DetailKind.RoadPaint));

        private static void Grain()
        {
            DetailSurface s = Paint();
            DetailPixels.Maps(s, out byte[] baked, out _);
            byte[] one = PaintMaps.Grain(s, 1f);
            bool same = one.Length == baked.Length;
            for (int i = 0; same && i < one.Length; i++)
                same = one[i] == baked[i];
            Program.Check(same, "a grain of 1 is the baked normal map, byte for byte");

            byte[] flat = PaintMaps.Grain(s, 0f);
            bool allFlat = flat.Length == baked.Length;
            for (int k = 0; allFlat && k < flat.Length; k += 4)
                allFlat = flat[k] == 128 && flat[k + 1] == 128 && flat[k + 2] == 255 && flat[k + 3] == 255;
            Program.Check(allFlat, "a grain of 0 is a flat map: (128, 128, 255)");

            byte[] two = PaintMaps.Grain(s, 2f), half = PaintMaps.Grain(s, 0.5f);
            int compared = 0;
            double ratioTwo = 0.0, ratioHalf = 0.0;
            for (int k = 0; k < s.Width * s.Height; k++)
            {
                float nx = s.Normals[k * 3], nz = s.Normals[k * 3 + 2];
                if (Math.Abs(nx) < 0.03f)
                    continue;
                // The slope the texel stands for, read back from the bytes: x / z of the unpacked normal.
                float slopeBaked = nx / nz;
                float slopeTwo = Slope(two, k), slopeHalf = Slope(half, k);
                ratioTwo += slopeTwo / slopeBaked;
                ratioHalf += slopeHalf / slopeBaked;
                compared++;
            }

            Program.Check(compared > 1000, "texels with a slope to compare: " + compared);
            Program.Near((float)(ratioTwo / compared), 2f, 0.04f, "a grain of 2 is twice the slope");
            Program.Near((float)(ratioHalf / compared), 0.5f, 0.02f, "a grain of 0.5 is half of it");
            // The texel that tips the most, at the strongest grain: still a unit normal (the z is made with the slope, not kept).
            int steepest = 0;
            for (int k = 0; k < s.Width * s.Height; k++)
            {
                if (s.Normals[k * 3 + 2] < s.Normals[steepest * 3 + 2])
                    steepest = k;
            }

            byte[] four = PaintMaps.Grain(s, 4f);
            float fx = four[steepest * 4] / 255f * 2f - 1f, fy = four[steepest * 4 + 1] / 255f * 2f - 1f, fz = four[steepest * 4 + 2] / 255f * 2f - 1f;
            Program.Near((float)Math.Sqrt(fx * fx + fy * fy + fz * fz), 1f, 0.012f, "the steepest texel at a grain of 4 is a unit normal: " + fz);
            Program.Check(fz < 0.97f, "and leans: its z is not 1 (" + fz + ")");
            for (int k = 0; k < two.Length; k += 4 * 997)
            {
                float x = two[k] / 255f * 2f - 1f, y = two[k + 1] / 255f * 2f - 1f, z = two[k + 2] / 255f * 2f - 1f;
                Program.Near((float)Math.Sqrt(x * x + y * y + z * z), 1f, 0.02f, "a unit normal");
                Program.Check(z > 0f && two[k + 3] == 255, "faces out, opaque");
            }
        }

        private static float Slope(byte[] pixels, int texel)
        {
            float x = pixels[texel * 4] / 255f * 2f - 1f, z = pixels[texel * 4 + 2] / 255f * 2f - 1f;
            return x / z;
        }

        private static void Smoothness()
        {
            DetailSurface s = Paint();
            DetailPixels.Maps(s, out _, out byte[] baked);
            float mean = PaintMaps.MeanSmooth(s);
            foreach (float want in new[] { 0.4f, 0.1f, mean })
            {
                byte[] mask = PaintMaps.Smoothness(s, want);
                double sum = 0.0;
                for (int k = 0; k < s.Smooth.Length; k++)
                    sum += mask[k * 4 + 3] / 255.0;
                Program.Near((float)(sum / s.Smooth.Length), want, 0.004f, "the mean smoothness is " + want);
                bool others = true;
                for (int k = 0; others && k < s.Smooth.Length; k++)
                    others = mask[k * 4] == baked[k * 4] && mask[k * 4 + 1] == baked[k * 4 + 1] && mask[k * 4 + 2] == baked[k * 4 + 2];
                Program.Check(others, "metal, coat and the empty channel are as baked");
            }

            byte[] same = PaintMaps.Smoothness(s, mean);
            int off = 0;
            for (int k = 0; k < s.Smooth.Length; k++)
                off = Math.Max(off, Math.Abs(same[k * 4 + 3] - baked[k * 4 + 3]));
            Program.Check(off <= 1, "the baked mean asks for the baked map to a byte: " + off);

            // The variation across the paint stays: two texels keep their difference.
            byte[] high = PaintMaps.Smoothness(s, 0.6f);
            int lowTexel = Array.IndexOf(s.Smooth, MinOf(s.Smooth)), highTexel = Array.IndexOf(s.Smooth, MaxOf(s.Smooth));
            Program.Near((high[highTexel * 4 + 3] - high[lowTexel * 4 + 3]) / 255f, s.Smooth[highTexel] - s.Smooth[lowTexel], 0.005f, "the difference between the smoothest and the roughest texel is kept");

            byte[] top = PaintMaps.Smoothness(s, 1f), bottom = PaintMaps.Smoothness(s, 0f);
            int highest = 0, lowest = 255;
            for (int k = 0; k < s.Smooth.Length; k++)
            {
                highest = Math.Max(highest, bottom[k * 4 + 3]);
                lowest = Math.Min(lowest, top[k * 4 + 3]);
            }

            Program.Check(highest < 128 && lowest > 128, "0 is rough and 1 is smooth, kept between 0 and 255 (" + lowest + ", " + highest + ")");
        }

        private static float MinOf(float[] a)
        {
            float m = float.MaxValue;
            foreach (float v in a)
                m = Math.Min(m, v);
            return m;
        }

        private static float MaxOf(float[] a)
        {
            float m = float.MinValue;
            foreach (float v in a)
                m = Math.Max(m, v);
            return m;
        }

        private static void Baked()
        {
            DetailSurface s = Paint();
            // What the log says of the paint: its grain tips the normal by under 3 degrees on average, and its smoothness is that of rough asphalt rather than of a glossy coat.
            float tilt = PaintMaps.MeanTilt(s), mean = PaintMaps.MeanSmooth(s);
            Program.Check(tilt > 1f && tilt < 3f, "the baked grain is faint: " + tilt + " degrees on average");
            Program.Check(mean > 0.15f && mean < 0.25f, "the baked smoothness is low: " + mean);
            Program.Check(MarkingExperiment.PlainSmoothness > mean, "the plain paint, the fallback, is smoother than the baked one: " + MarkingExperiment.PlainSmoothness);
        }

        private static ItemRecord At(float x, float z) => new ItemRecord(1u, new Vec3(x, 0f, z), 0f, 0u, null, 0f, 0u, 0);

        private static void BumpAll()
        {
            var store = new ItemStore<int>();
            Program.Check(store.BumpAll() == 0 && !store.HasDirty, "an empty store has nothing to build again");
            float far = Tiles.Size * 5f + 3f;
            store.Upsert(1, At(10f, 10f));
            store.Upsert(2, At(far, 10f));
            store.Upsert(3, At(far + 1f, 12f));
            TileKey a = Tiles.Of(10f, 10f), b = Tiles.Of(far, 10f);
            var taken = new HashSet<TileKey>();
            store.TakeDirty(taken);
            int va = store.VersionOf(a), vb = store.VersionOf(b);
            Program.Check(!store.HasDirty && taken.Count == 2, "two tiles, clean");
            Program.Check(store.BumpAll() == 2, "two tiles are built again");
            taken.Clear();
            store.TakeDirty(taken);
            Program.Check(taken.Count == 2 && taken.Contains(a) && taken.Contains(b), "both are marked");
            Program.Check(store.VersionOf(a) == va + 1 && store.VersionOf(b) == vb + 1, "both versions are up by one: a build read at the old ones is out of date");
            Program.Check(store.Count == 3, "the items are as they were");
        }
    }
}
