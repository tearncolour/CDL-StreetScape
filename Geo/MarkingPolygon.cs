using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The outline of an area marking (an island the player drew, a gore found at a fork, an area where the game's markings are cleared) kept as text in the item's settings, so that it is saved with the
    /// item and drawn the same whatever the road later becomes: the corners in the item's own frame (the item's position is the origin, +Z the way its yaw points) in whole centimetres. Since 0.12.0 the text
    /// is "d" and the first corner "x:z", then each next corner as its difference from the one before, joined by "|" (the sides of an outline are short beside where it is, so this is about half as long);
    /// the form of 0.10.0 and 0.11.0, every corner "x:z" as it is with no "d", is read as well. At most MaxPoints corners; a polygon that is not simple or has no area reads as none. Pure.
    /// </summary>
    public static class MarkingPolygon
    {
        public const string Key = "poly";

        /// <summary>The most corners of an outline (24 before 0.12.0): an outline that does not fit MaxChars is simplified to fewer (EncodeFitting).</summary>
        public const int MaxPoints = 40;

        /// <summary>The mark of the form with differences.</summary>
        public const char DeltaMark = 'd';

        /// <summary>The most characters the outline's text may have.</summary>
        public const int MaxChars = 380;

        /// <summary>The points of a line or an outline in the frame of an item at origin turned by yaw (its +Z is (sin yaw, cos yaw) in the world); the heights are taken relative to the origin's.</summary>
        public static List<Vec3> ToLocal(IList<Vec3> world, Vec3 origin, float yaw)
        {
            var local = new List<Vec3>(world.Count);
            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
            foreach (Vec3 p in world)
            {
                float dx = p.X - origin.X, dz = p.Z - origin.Z;
                local.Add(new Vec3(dx * c - dz * s, p.Y - origin.Y, dx * s + dz * c));
            }

            return local;
        }

        /// <summary>The opposite of ToLocal.</summary>
        public static List<Vec3> ToWorld(IList<Vec3> local, Vec3 origin, float yaw)
        {
            var world = new List<Vec3>(local.Count);
            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
            foreach (Vec3 p in local)
                world.Add(new Vec3(p.X * c + p.Z * s + origin.X, p.Y + origin.Y, -p.X * s + p.Z * c + origin.Z));
            return world;
        }

        /// <summary>
        /// The text of an outline (local points; their heights are not kept), in the form with differences. Empty when there are fewer than three or more than MaxPoints, or a point is not a number or is more
        /// than 2 km from the origin.
        /// </summary>
        public static string Encode(IList<Vec3> local)
        {
            if (local == null || local.Count < 3 || local.Count > MaxPoints)
                return "";
            var sb = new StringBuilder();
            sb.Append(DeltaMark);
            int lastX = 0, lastZ = 0;
            for (int i = 0; i < local.Count; i++)
            {
                Vec3 p = local[i];
                if (!p.IsFinite || Math.Abs(p.X) > 2000f || Math.Abs(p.Z) > 2000f)
                    return "";
                // The differences are of the corners rounded to the centimetre, so the corners read back are those rounded corners exactly (no error adds up along the outline).
                int x = (int)Math.Round(p.X * 100f), z = (int)Math.Round(p.Z * 100f);
                if (i > 0)
                    sb.Append('|');
                sb.Append((x - lastX).ToString(CultureInfo.InvariantCulture)).Append(':').Append((z - lastZ).ToString(CultureInfo.InvariantCulture));
                lastX = x;
                lastZ = z;
            }

            return sb.ToString();
        }

        /// <summary>The text of an outline in the form of 0.10.0 and 0.11.0 (every corner as it is), which Decode still reads; for the tests of old settings.</summary>
        public static string EncodeAbsolute(IList<Vec3> local)
        {
            if (local == null || local.Count < 3 || local.Count > MaxPoints)
                return "";
            var sb = new StringBuilder();
            foreach (Vec3 p in local)
            {
                if (!p.IsFinite || Math.Abs(p.X) > 2000f || Math.Abs(p.Z) > 2000f)
                    return "";
                if (sb.Length > 0)
                    sb.Append('|');
                sb.Append(((int)Math.Round(p.X * 100f)).ToString(CultureInfo.InvariantCulture)).Append(':').Append(((int)Math.Round(p.Z * 100f)).ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        /// <summary>The corners of an outline's text (heights 0). Empty for no text, a damaged one, fewer than three corners, more than MaxPoints, or an outline that crosses itself or has no area.</summary>
        public static List<Vec3> Decode(string text)
        {
            var points = new List<Vec3>();
            if (string.IsNullOrEmpty(text))
                return points;
            bool delta = text[0] == DeltaMark;
            string[] pieces = (delta ? text.Substring(1) : text).Split('|');
            if (pieces.Length < 3 || pieces.Length > MaxPoints)
                return points;
            long atX = 0, atZ = 0;
            foreach (string piece in pieces)
            {
                string[] xz = piece.Split(':');
                if (xz.Length != 2 || !int.TryParse(xz[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) || !int.TryParse(xz[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int z))
                    return new List<Vec3>();
                atX = delta ? atX + x : x;
                atZ = delta ? atZ + z : z;
                if (Math.Abs(atX) > 200000 || Math.Abs(atZ) > 200000)
                    return new List<Vec3>();
                points.Add(new Vec3(atX / 100f, 0f, atZ / 100f));
            }

            if (!MarkingFills.IsSimple(points) || Math.Abs(Polygon.SignedArea2(points)) < 0.02f)
                return new List<Vec3>();
            return points;
        }

        /// <summary>
        /// An outline with fewer corners: the corner that adds the least area (with its two neighbours) is taken away, again and again, until there are at most count (a corner of a split side, given in keep, is
        /// never taken: the nose and the far ends of a gore). The outline stays simple; when taking a corner would make it cross itself, the next least is taken.
        /// </summary>
        public static List<Vec3> Simplify(IList<Vec3> polygon, int count, ICollection<int> keep = null)
        {
            var points = new List<Vec3>(polygon);
            var ids = new List<int>();
            for (int i = 0; i < points.Count; i++)
                ids.Add(i);
            while (points.Count > Math.Max(3, count))
            {
                int n = points.Count, best = -1;
                float least = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (keep != null && keep.Contains(ids[i]))
                        continue;
                    float area = Math.Abs(Polygon.Cross(points[(i + n - 1) % n], points[i], points[(i + 1) % n]));
                    if (area >= least)
                        continue;
                    var trial = new List<Vec3>(points);
                    trial.RemoveAt(i);
                    if (!MarkingFills.IsSimple(trial))
                        continue;
                    least = area;
                    best = i;
                }

                if (best < 0)
                    break;
                points.RemoveAt(best);
                ids.RemoveAt(best);
            }

            return points;
        }

        /// <summary>The text of an outline that fits in maxChars: corners are taken away (Simplify, each time from the whole outline) until it does; empty when it cannot be made to.</summary>
        public static string EncodeFitting(IList<Vec3> local, int maxChars = MaxChars, ICollection<int> keep = null)
        {
            if (local == null || local.Count < 3)
                return "";
            for (int target = Math.Min(local.Count, MaxPoints); target >= 3; target--)
            {
                string text = Encode(target == local.Count ? new List<Vec3>(local) : Simplify(local, target, keep));
                if (text.Length > 0 && text.Length <= maxChars)
                    return text;
            }

            return "";
        }
    }
}
