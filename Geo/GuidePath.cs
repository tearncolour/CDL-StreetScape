using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A small SVG path reader for the pictograms of the signs: the commands M L H V C S Q T A Z in absolute and relative form, read into closed contours of x, y pairs (curves and arcs
    /// cut into short straight pieces). Contours are filled together by the even-odd rule, so a hole (a window, a hub) is a contour inside another. Pure; the painter fills the contours and the
    /// tests measure them.
    /// </summary>
    public static class GuidePath
    {
        /// <summary>How many straight pieces a curve is cut into: 14 is smooth at the size of a sign on a 512 px picture.</summary>
        public const int CurveSteps = 14;

        /// <summary>Reads a path; unknown commands and malformed numbers end the reading at that point (the contours read so far are kept), so that a typo gives a visibly wrong shape rather than an exception.</summary>
        public static List<float[]> Parse(string d)
        {
            var contours = new List<float[]>();
            if (string.IsNullOrEmpty(d))
                return contours;

            var current = new List<float>();
            int i = 0;
            float x = 0f, y = 0f, startX = 0f, startY = 0f;
            float lastCx = 0f, lastCy = 0f;   // the last control point, for the smooth commands
            char command = ' ', previous = ' ';

            void Close()
            {
                if (current.Count >= 6)
                    contours.Add(current.ToArray());
                current = new List<float>();
            }

            void Line(float nx, float ny)
            {
                if (current.Count == 0)
                {
                    current.Add(x);
                    current.Add(y);
                }

                current.Add(nx);
                current.Add(ny);
                x = nx;
                y = ny;
            }

            while (true)
            {
                SkipSeparators(d, ref i);
                if (i >= d.Length)
                    break;

                char c = d[i];
                if (char.IsLetter(c))
                {
                    command = c;
                    i++;
                    if (command == 'Z' || command == 'z')
                    {
                        Close();
                        x = startX;
                        y = startY;
                        previous = 'z';
                        continue;
                    }
                }
                else if (command == ' ' || command == 'Z' || command == 'z')
                {
                    break;
                }

                bool relative = char.IsLower(command);
                char upper = char.ToUpperInvariant(command);
                float ox = relative ? x : 0f, oy = relative ? y : 0f;
                float[] n;
                switch (upper)
                {
                    case 'M':
                        if (!Numbers(d, ref i, 2, out n))
                            return Done();
                        // A moveto starts a new contour; the numbers that follow it are lineto's.
                        Close();
                        x = ox + n[0];
                        y = oy + n[1];
                        startX = x;
                        startY = y;
                        command = relative ? 'l' : 'L';
                        previous = 'M';
                        break;
                    case 'L':
                        if (!Numbers(d, ref i, 2, out n))
                            return Done();
                        Line(ox + n[0], oy + n[1]);
                        previous = 'L';
                        break;
                    case 'H':
                        if (!Numbers(d, ref i, 1, out n))
                            return Done();
                        Line(ox + n[0], y);
                        previous = 'L';
                        break;
                    case 'V':
                        if (!Numbers(d, ref i, 1, out n))
                            return Done();
                        Line(x, oy + n[0]);
                        previous = 'L';
                        break;
                    case 'C':
                    {
                        if (!Numbers(d, ref i, 6, out n))
                            return Done();
                        Cubic(Line, x, y, ox + n[0], oy + n[1], ox + n[2], oy + n[3], ox + n[4], oy + n[5]);
                        lastCx = ox + n[2];
                        lastCy = oy + n[3];
                        previous = 'C';
                        break;
                    }

                    case 'S':
                    {
                        if (!Numbers(d, ref i, 4, out n))
                            return Done();
                        float c1x = previous == 'C' ? 2f * x - lastCx : x, c1y = previous == 'C' ? 2f * y - lastCy : y;
                        Cubic(Line, x, y, c1x, c1y, ox + n[0], oy + n[1], ox + n[2], oy + n[3]);
                        lastCx = ox + n[0];
                        lastCy = oy + n[1];
                        previous = 'C';
                        break;
                    }

                    case 'Q':
                    {
                        if (!Numbers(d, ref i, 4, out n))
                            return Done();
                        Quadratic(Line, x, y, ox + n[0], oy + n[1], ox + n[2], oy + n[3]);
                        lastCx = ox + n[0];
                        lastCy = oy + n[1];
                        previous = 'Q';
                        break;
                    }

                    case 'T':
                    {
                        if (!Numbers(d, ref i, 2, out n))
                            return Done();
                        float qx = previous == 'Q' ? 2f * x - lastCx : x, qy = previous == 'Q' ? 2f * y - lastCy : y;
                        Quadratic(Line, x, y, qx, qy, ox + n[0], oy + n[1]);
                        lastCx = qx;
                        lastCy = qy;
                        previous = 'Q';
                        break;
                    }

                    case 'A':
                    {
                        // rx ry rotation large-arc sweep x y; the two flags may be written without separators.
                        if (!Numbers(d, ref i, 3, out n))
                            return Done();
                        if (!Flag(d, ref i, out float large) || !Flag(d, ref i, out float sweep) || !Numbers(d, ref i, 2, out float[] end))
                            return Done();
                        Arc(Line, x, y, n[0], n[1], n[2], large != 0f, sweep != 0f, ox + end[0], oy + end[1]);
                        previous = 'A';
                        break;
                    }

                    default:
                        return Done();
                }
            }

            return Done();

            List<float[]> Done()
            {
                Close();
                return contours;
            }
        }

        // ------------------------------------------------------------------ reading numbers

        private static void SkipSeparators(string d, ref int i)
        {
            while (i < d.Length && (char.IsWhiteSpace(d[i]) || d[i] == ','))
                i++;
        }

        private static bool Numbers(string d, ref int i, int count, out float[] values)
        {
            values = new float[count];
            for (int k = 0; k < count; k++)
            {
                SkipSeparators(d, ref i);
                if (!Number(d, ref i, out values[k]))
                    return false;
            }

            return true;
        }

        private static bool Flag(string d, ref int i, out float value)
        {
            SkipSeparators(d, ref i);
            value = 0f;
            if (i < d.Length && (d[i] == '0' || d[i] == '1'))
            {
                value = d[i] - '0';
                i++;
                return true;
            }

            return false;
        }

        /// <summary>One number as SVG writes them: a sign, digits with a point, an exponent; "1.5.5" is two numbers and "10-5" too.</summary>
        private static bool Number(string d, ref int i, out float value)
        {
            int start = i;
            value = 0f;
            if (i < d.Length && (d[i] == '+' || d[i] == '-'))
                i++;
            bool digits = false;
            while (i < d.Length && char.IsDigit(d[i]))
            {
                i++;
                digits = true;
            }

            if (i < d.Length && d[i] == '.')
            {
                i++;
                while (i < d.Length && char.IsDigit(d[i]))
                {
                    i++;
                    digits = true;
                }
            }

            if (!digits)
            {
                i = start;
                return false;
            }

            if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
            {
                int save = i;
                i++;
                if (i < d.Length && (d[i] == '+' || d[i] == '-'))
                    i++;
                if (i < d.Length && char.IsDigit(d[i]))
                {
                    while (i < d.Length && char.IsDigit(d[i]))
                        i++;
                }
                else
                {
                    i = save;
                }
            }

            return float.TryParse(d.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        // ------------------------------------------------------------------ curves

        private static void Cubic(Action<float, float> line, float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3)
        {
            for (int s = 1; s <= CurveSteps; s++)
            {
                float t = (float)s / CurveSteps, u = 1f - t;
                float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, e = t * t * t;
                line(a * x0 + b * x1 + c * x2 + e * x3, a * y0 + b * y1 + c * y2 + e * y3);
            }
        }

        private static void Quadratic(Action<float, float> line, float x0, float y0, float x1, float y1, float x2, float y2)
        {
            for (int s = 1; s <= CurveSteps; s++)
            {
                float t = (float)s / CurveSteps, u = 1f - t;
                line(u * u * x0 + 2f * u * t * x1 + t * t * x2, u * u * y0 + 2f * u * t * y1 + t * t * y2);
            }
        }

        /// <summary>An elliptical arc from the current point to (x, y): the endpoint form of SVG converted to the centre form (SVG 1.1, F.6.5), then cut.</summary>
        private static void Arc(Action<float, float> line, float x0, float y0, float rx, float ry, float rotation, bool large, bool sweep, float x, float y)
        {
            if ((x0 == x && y0 == y) || rx == 0f || ry == 0f)
            {
                line(x, y);
                return;
            }

            rx = Math.Abs(rx);
            ry = Math.Abs(ry);
            double phi = rotation * Math.PI / 180.0;
            double cos = Math.Cos(phi), sin = Math.Sin(phi);
            double dx = (x0 - x) * 0.5, dy = (y0 - y) * 0.5;
            double x1 = cos * dx + sin * dy, y1 = -sin * dx + cos * dy;
            double lambda = x1 * x1 / ((double)rx * rx) + y1 * y1 / ((double)ry * ry);
            double rxd = rx, ryd = ry;
            if (lambda > 1.0)
            {
                double scale = Math.Sqrt(lambda);
                rxd *= scale;
                ryd *= scale;
            }

            double numerator = rxd * rxd * ryd * ryd - rxd * rxd * y1 * y1 - ryd * ryd * x1 * x1;
            double denominator = rxd * rxd * y1 * y1 + ryd * ryd * x1 * x1;
            double coefficient = denominator == 0.0 ? 0.0 : Math.Sqrt(Math.Max(0.0, numerator / denominator));
            if (large == sweep)
                coefficient = -coefficient;
            double cxp = coefficient * rxd * y1 / ryd, cyp = -coefficient * ryd * x1 / rxd;
            double cx = cos * cxp - sin * cyp + (x0 + x) * 0.5, cy = sin * cxp + cos * cyp + (y0 + y) * 0.5;
            double theta = Angle(1.0, 0.0, (x1 - cxp) / rxd, (y1 - cyp) / ryd);
            double delta = Angle((x1 - cxp) / rxd, (y1 - cyp) / ryd, (-x1 - cxp) / rxd, (-y1 - cyp) / ryd);
            if (!sweep && delta > 0.0)
                delta -= 2.0 * Math.PI;
            else if (sweep && delta < 0.0)
                delta += 2.0 * Math.PI;

            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(delta) / (Math.PI * 0.5) * CurveSteps * 0.6));
            for (int s = 1; s <= steps; s++)
            {
                double a = theta + delta * s / steps;
                double px = rxd * Math.Cos(a), py = ryd * Math.Sin(a);
                line((float)(cos * px - sin * py + cx), (float)(sin * px + cos * py + cy));
            }
        }

        private static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double length = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double a = Math.Acos(Math.Max(-1.0, Math.Min(1.0, length == 0.0 ? 1.0 : dot / length)));
            return ux * vy - uy * vx < 0.0 ? -a : a;
        }

        // ------------------------------------------------------------------ helpers to write paths and to measure them

        public static string Circle(float cx, float cy, float r) => Ellipse(cx, cy, r, r);

        public static string Ellipse(float cx, float cy, float rx, float ry)
        {
            string f(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            return $"M{f(cx - rx)} {f(cy)}a{f(rx)} {f(ry)} 0 1 0 {f(2 * rx)} 0a{f(rx)} {f(ry)} 0 1 0 {f(-2 * rx)} 0z";
        }

        public static string Rect(float x, float y, float w, float h)
        {
            string f(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            return $"M{f(x)} {f(y)}h{f(w)}v{f(h)}h{f(-w)}z";
        }

        /// <summary>A rounded rectangle path (x, y, width, height, corner radius).</summary>
        public static string RoundRect(float x, float y, float w, float h, float r)
        {
            string f(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            r = Math.Min(r, Math.Min(w, h) * 0.5f);
            return $"M{f(x + r)} {f(y)}h{f(w - 2 * r)}a{f(r)} {f(r)} 0 0 1 {f(r)} {f(r)}v{f(h - 2 * r)}a{f(r)} {f(r)} 0 0 1 {f(-r)} {f(r)}h{f(-(w - 2 * r))}a{f(r)} {f(r)} 0 0 1 {f(-r)} {f(-r)}v{f(-(h - 2 * r))}a{f(r)} {f(r)} 0 0 1 {f(r)} {f(-r)}z";
        }

        public static void Bounds(IList<float[]> contours, out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue;
            minY = float.MaxValue;
            maxX = float.MinValue;
            maxY = float.MinValue;
            foreach (float[] c in contours)
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    minX = Math.Min(minX, c[i]);
                    maxX = Math.Max(maxX, c[i]);
                    minY = Math.Min(minY, c[i + 1]);
                    maxY = Math.Max(maxY, c[i + 1]);
                }
            }
        }

        /// <summary>The contours scaled and moved: x' = x * sx + dx, y' = y * sy + dy (a negative sx mirrors).</summary>
        public static List<float[]> Map(IList<float[]> contours, float sx, float sy, float dx, float dy)
        {
            var result = new List<float[]>(contours.Count);
            foreach (float[] c in contours)
            {
                var m = new float[c.Length];
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    m[i] = c[i] * sx + dx;
                    m[i + 1] = c[i + 1] * sy + dy;
                }

                result.Add(m);
            }

            return result;
        }

        /// <summary>The area of a contour (absolute), for tests that a shape is not empty or degenerate.</summary>
        public static float Area(float[] contour)
        {
            float a = 0f;
            for (int i = 0; i + 1 < contour.Length; i += 2)
            {
                int j = (i + 2) % contour.Length;
                a += contour[i] * contour[j + 1] - contour[j] * contour[i + 1];
            }

            return Math.Abs(a) * 0.5f;
        }
    }
}
