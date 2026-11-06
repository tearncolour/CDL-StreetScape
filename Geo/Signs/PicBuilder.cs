using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Builds the path of a pictogram from simple shapes (circles, ellipses, rectangles, polygons, thick lines with round ends, raw path pieces), in a 100 x 100 box (y down). The shapes are
    /// filled together by the NON-ZERO winding rule, as SVG does by default: solid shapes are written counter-clockwise on the screen so that overlapping ones make one shape; a hole is a
    /// shape written the other way round, which cuts out what lies under it and nothing else. The builder takes care of that: give it a polygon in any order, and it is made counter-
    /// clockwise (solid) or clockwise (hole). A raw piece (Path) is taken as written: when it makes a solid, write its contour counter-clockwise on the screen (y down).
    /// Nothing but text comes out, so a pictogram is a plain string that can be read, diffed and kept in the source.
    /// </summary>
    public sealed class PicBuilder
    {
        private readonly StringBuilder m_Path = new StringBuilder();

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        public override string ToString() => m_Path.ToString();

        /// <summary>A raw piece of SVG path data, taken as written.</summary>
        public PicBuilder Path(string d)
        {
            m_Path.Append(d).Append(' ');
            return this;
        }

        public PicBuilder Circle(float cx, float cy, float r, bool hole = false) => Ellipse(cx, cy, r, r, hole);

        public PicBuilder Ellipse(float cx, float cy, float rx, float ry, bool hole = false)
        {
            // Two half arcs. Sweep 0 goes counter-clockwise on the screen (y down); a hole goes the other way.
            int sweep = hole ? 1 : 0;
            m_Path.Append($"M{F(cx - rx)} {F(cy)}A{F(rx)} {F(ry)} 0 1 {sweep} {F(cx + rx)} {F(cy)}A{F(rx)} {F(ry)} 0 1 {sweep} {F(cx - rx)} {F(cy)}Z ");
            return this;
        }

        public PicBuilder Rect(float x, float y, float w, float h, bool hole = false) => Poly(hole, x, y, x + w, y, x + w, y + h, x, y + h);

        public PicBuilder RoundRect(float x, float y, float w, float h, float r, bool hole = false)
        {
            r = Math.Min(r, Math.Min(w, h) * 0.5f);
            var p = new List<float>();
            const int steps = 6;
            void Corner(float cx, float cy, float a0)
            {
                for (int i = 0; i <= steps; i++)
                {
                    double a = (a0 + 90.0 * i / steps) * Math.PI / 180.0;
                    p.Add(cx + r * (float)Math.Cos(a));
                    p.Add(cy + r * (float)Math.Sin(a));
                }
            }

            Corner(x + w - r, y + r, -90f);
            Corner(x + w - r, y + h - r, 0f);
            Corner(x + r, y + h - r, 90f);
            Corner(x + r, y + r, 180f);
            return Poly(hole, p.ToArray());
        }

        /// <summary>A closed polygon of x, y pairs; any order, made counter-clockwise (a hole: clockwise) on the screen.</summary>
        public PicBuilder Poly(bool hole, params float[] points)
        {
            int n = points.Length / 2;
            if (n < 3)
                return this;
            // Signed area with y down: positive means clockwise on the screen.
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += points[i * 2] * points[j * 2 + 1] - points[j * 2] * points[i * 2 + 1];
            }

            bool clockwise = area > 0f;
            bool reverse = hole ? !clockwise : clockwise;
            for (int k = 0; k < n; k++)
            {
                int i = reverse ? n - 1 - k : k;
                m_Path.Append(k == 0 ? 'M' : 'L').Append(F(points[i * 2])).Append(' ').Append(F(points[i * 2 + 1]));
            }

            m_Path.Append("Z ");
            return this;
        }

        public PicBuilder Poly(params float[] points) => Poly(false, points);

        /// <summary>A thick line from one point to another with round ends: a limb, a rod, a stripe.</summary>
        public PicBuilder Capsule(float x0, float y0, float x1, float y1, float width, bool hole = false)
        {
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-4f)
                return Circle(x0, y0, width * 0.5f, hole);
            float r = width * 0.5f;
            float nx = -dy / len, ny = dx / len;
            var p = new List<float>();
            const int steps = 6;
            double baseAngle = Math.Atan2(ny, nx);
            // n is the line's direction turned a quarter round, so (counter-clockwise in the maths sense) the direction BACK from the start is n turned a quarter round again. The cap at the
            // start goes from the +n side round the back (away from the line) to the -n side; the cap at the end from the -n side round the front to the +n side.
            for (int i = 0; i <= steps; i++)
            {
                double a = baseAngle + Math.PI * i / steps;
                p.Add(x0 + r * (float)Math.Cos(a));
                p.Add(y0 + r * (float)Math.Sin(a));
            }

            for (int i = 0; i <= steps; i++)
            {
                double a = baseAngle + Math.PI + Math.PI * i / steps;
                p.Add(x1 + r * (float)Math.Cos(a));
                p.Add(y1 + r * (float)Math.Sin(a));
            }

            return Poly(hole, p.ToArray());
        }

        /// <summary>A polyline of thick segments with round joints (a bent limb, an arrow's stem that turns).</summary>
        public PicBuilder Stroke(float width, params float[] points)
        {
            for (int i = 0; i + 3 < points.Length; i += 2)
                Capsule(points[i], points[i + 1], points[i + 2], points[i + 3], width);
            return this;
        }

        /// <summary>A ring: a solid circle with a hole in it.</summary>
        public PicBuilder Ring(float cx, float cy, float outer, float inner) => Circle(cx, cy, outer).Circle(cx, cy, inner, true);
    }
}
