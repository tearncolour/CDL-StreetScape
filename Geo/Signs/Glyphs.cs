using System;
using System.Collections.Generic;
using System.Reflection;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of the signs, written once and used by every sign that needs them. A glyph is a public static string field (or property) of this class, in any of its partial files
    /// (Glyphs.Vehicles.cs, Glyphs.People.cs, ...): the SVG path data of a figure in a 100 x 100 box, y down, built with PicBuilder (solid shapes counter-clockwise, holes clockwise: the
    /// builder does it). Every public static string of the class is a glyph and is found by the registry below (by reflection), so the glyph sheet of the preview program and the tests
    /// need no list to keep in step. Glyphs are drawn in ONE colour; a sign puts the colour on (black on yellow, white on blue). A figure with two colours (a red car on a black road) is
    /// two glyphs on top of each other.
    ///
    /// Drawing rules, so that they look like one family: the figure fills the box without touching its edge (5 units of room all round, or what the printed figure has); a figure that
    /// faces a way faces LEFT (a sign that needs the other way mirrors it with Mirror); a stroke is 8 to 12 units thick; no detail thinner than 3 units (it is gone at 80 cm on a screen).
    /// </summary>
    public static partial class Glyphs
    {
        /// <summary>Every glyph: its name (the field's) and its path data, in name order.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> All()
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (FieldInfo f in typeof(Glyphs).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (f.FieldType == typeof(string) && f.GetValue(null) is string value && !string.IsNullOrEmpty(value))
                    list.Add(new KeyValuePair<string, string>(f.Name, value));
            }

            foreach (PropertyInfo p in typeof(Glyphs).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (p.PropertyType == typeof(string) && p.GetGetMethod() != null && p.GetValue(null, null) is string value && !string.IsNullOrEmpty(value))
                    list.Add(new KeyValuePair<string, string>(p.Name, value));
            }

            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        /// <summary>A glyph looking the other way: x' = 100 - x. The winding turns with the mirror, so the contours are written again the other way round.</summary>
        public static string Mirror(string path)
        {
            List<float[]> contours = GuideKit.Contours(path);
            var sb = new System.Text.StringBuilder();
            foreach (float[] c in contours)
            {
                int n = c.Length / 2;
                for (int k = 0; k < n; k++)
                {
                    int i = n - 1 - k;
                    sb.Append(k == 0 ? 'M' : 'L').Append((100f - c[i * 2]).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(' ').Append(c[i * 2 + 1].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                }

                sb.Append("Z ");
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ a model of the family: a walking person

        /// <summary>A walking person seen from the side, facing left: a head, a body bent a little forward, an arm swinging, two legs in step.</summary>
        public static readonly string Pedestrian = new PicBuilder()
            .Circle(46f, 13f, 8f)                                    // head
            .Capsule(48f, 27f, 54f, 55f, 15f)                        // trunk
            .Capsule(47f, 31f, 36f, 47f, 8f)                         // the arm in front
            .Capsule(52f, 31f, 62f, 47f, 8f)                         // the arm behind
            .Capsule(54f, 55f, 41f, 76f, 11f).Capsule(41f, 76f, 38f, 93f, 9f)   // the leg in front
            .Capsule(54f, 55f, 66f, 75f, 11f).Capsule(66f, 75f, 74f, 91f, 9f)   // the leg behind
            .ToString();
    }
}
