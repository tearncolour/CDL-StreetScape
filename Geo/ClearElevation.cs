using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The heights of a clearing's corners, saved beside its existing XZ polygon. Old saves have no profile and use the item's saved height.</summary>
    public static class ClearElevation
    {
        public const string Key = "elev";

        /// <summary>Write the outline and its matching centimetre heights together; never let GuideSpec silently truncate either half.</summary>
        public static string WithOutline(GuideSpec seed, IList<Vec3> world, Vec3 origin, float yaw, bool strip = false)
        {
            if (seed == null || world == null || world.Count < 3 || !origin.IsFinite)
                return null;
            List<Vec3> points = MarkingPolygon.ToLocal(world, origin, yaw);
            seed = seed.With(MarkingPolygon.Key, "").With(Key, "");
            while (points.Count >= 3)
            {
                string poly = MarkingPolygon.Encode(points), heights = Encode(points, strip);
                if (poly.Length > 0 && heights != null && MarkingPolygon.Decode(poly).Count == points.Count)
                {
                    GuideSpec candidate = seed.With(MarkingPolygon.Key, poly).With(Key, heights);
                    // keep=all -> keep=lines is the longest editable setting change (+2 bytes).
                    if (candidate.Get(MarkingPolygon.Key) == poly && candidate.Get(Key) == heights && Encoding.UTF8.GetByteCount(candidate.ToString()) <= GuideSpec.MaxBytes - 2)
                        return candidate.ToString();
                }

                // Road strips are ordered in corresponding left/right rows. Do not simplify one side independently of the other.
                if (strip)
                    return null;

                // Only simplify a corner whose height is still represented by its neighbours. A steep crest must not become a flat clearing.
                int remove = -1;
                float least = float.MaxValue;
                for (int i = 0; i < points.Count; i++)
                {
                    Vec3 a = points[(i + points.Count - 1) % points.Count], p = points[i], b = points[(i + 1) % points.Count];
                    Vec3 ab = b - a;
                    float length2 = ab.X * ab.X + ab.Z * ab.Z;
                    float t = length2 > 1e-8f ? Math.Max(0f, Math.Min(1f, ((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / length2)) : 0f;
                    if (Math.Abs(p.Y - (a.Y + ab.Y * t)) > 0.1f)
                        continue;
                    float cost = Math.Abs(Polygon.Cross(a, p, b));
                    if (cost >= least)
                        continue;
                    var trial = new List<Vec3>(points);
                    trial.RemoveAt(i);
                    if (!MarkingFills.IsSimple(trial))
                        continue;
                    least = cost;
                    remove = i;
                }

                if (remove < 0 || points.Count == 3)
                    return null;
                points.RemoveAt(remove);
            }

            return null;
        }

        private static string Encode(IList<Vec3> points, bool strip)
        {
            if (points.Count > MarkingPolygon.MaxPoints || (strip && (points.Count < 4 || points.Count % 2 != 0)))
                return null;
            byte[] bytes = new byte[points.Count * 2];
            for (int i = 0; i < points.Count; i++)
            {
                if (!points[i].IsFinite)
                    return null;
                double cm = Math.Round(points[i].Y * 100.0);
                if (cm < short.MinValue || cm > short.MaxValue)
                    return null;
                ushort value = unchecked((ushort)(short)cm);
                bytes[2 * i] = (byte)value;
                bytes[2 * i + 1] = (byte)(value >> 8);
            }

            // URL alphabet avoids GuideSpec's escaped '=' and keeps 40 heights below 110 characters.
            return (strip ? "s" : "h") + Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool IsStrip(GuideSpec spec) => spec.Get(Key).StartsWith("s", StringComparison.Ordinal);

        /// <summary>Read local corners with their heights. A malformed new profile fails closed; only an absent legacy profile means a flat polygon.</summary>
        public static List<Vec3> Decode(GuideSpec spec)
        {
            List<Vec3> points = MarkingPolygon.Decode(spec.Get(MarkingPolygon.Key));
            string text = spec.Get(Key);
            if (text.Length == 0 || points.Count == 0)
                return points;
            if ((text[0] != 'h' && text[0] != 's') || text.Length > 110 || (IsStrip(spec) && (points.Count < 4 || points.Count % 2 != 0)))
                return new List<Vec3>();
            try
            {
                string encoded = text.Substring(1).Replace('-', '+').Replace('_', '/');
                encoded += new string('=', (4 - encoded.Length % 4) % 4);
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length != points.Count * 2)
                    return new List<Vec3>();
                for (int i = 0; i < points.Count; i++)
                {
                    short cm = unchecked((short)(bytes[i * 2] | bytes[i * 2 + 1] << 8));
                    Vec3 p = points[i];
                    points[i] = new Vec3(p.X, cm / 100f, p.Z);
                }
                return points;
            }
            catch (FormatException)
            {
                return new List<Vec3>();
            }
        }
    }
}
