using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// LED neon figures on lamp posts (0.12.49; the owner, with a photo of a pair of neon lantern outlines lit at night: "路灯灯箱加入这种支持夜间发光的"): strips of neon flex along strokes, each of
    /// one colour that glows at night (this game's HDRP/Lit has no emissive map, so each colour is a material of its own: SolidColour.Glow*). The trade's pole figures are LED strips lit both ways on
    /// bent stainless steel without a backing, about 1.1 x 1.5 m a figure, 2.8 x 2.0 m and 40 mm deep a pair (scratch/research-lampads/REPORT.md section 8). The drawing is this mod's, after the
    /// owner's photo: a frame (a palace lantern, round, a begonia window or arched) with the hook it hangs by, a 如意 knot and a tassel under it, a pattern (hills, clouds, waves, a red sun, the moon,
    /// plum blossom) and characters of the font thinned to single strokes (Letter). Coordinates: metres as the viewer sees the part (x right, y up) round its middle. Pure once the font is set.
    /// </summary>
    public static class Neon
    {
        /// <summary>The colours of neon flex, in the order they lie over one another where strokes cross (Layer).</summary>
        public static readonly SolidColour[] Colours =
        {
            SolidColour.GlowWhite, SolidColour.GlowWarm, SolidColour.GlowRed, SolidColour.GlowGold, SolidColour.GlowBlue, SolidColour.GlowPink, SolidColour.GlowGreen
        };

        /// <summary>How deep a strip is (m): the trade's pairs are 40 mm deep with their steel.</summary>
        public const float Depth = 0.02f;

        /// <summary>The hook above the body and the tassel under it (its cord, the 如意 knot and the tassel), as parts of the width: the owner's photo has about 0.1 and 1.0.</summary>
        public const float Hook = 0.1f, TasselHeight = 0.98f;

        /// <summary>The radius of the skeleton's rods (m): stainless bars of 8 mm, this mod's estimate (the trade's figures are on stainless steel: REPORT.md section 8).</summary>
        public const float RodRadius = 0.004f;

        /// <summary>A turn of a stroke sharper than this (the cosine of 100 degrees between its two segments) starts a new run of strip (Runs).</summary>
        private const float SharpTurn = -0.17f;

        public static int Layer(SolidColour colour) => Math.Max(0, Array.IndexOf(Colours, colour));

        /// <summary>The colour of a strip that is not lit: its silicone's.</summary>
        public static SolidColour Unlit(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.GlowRed:
                case SolidColour.GlowPink:
                    return SolidColour.Red;
                case SolidColour.GlowGold:
                    return SolidColour.Yellow;
                case SolidColour.GlowBlue:
                    return SolidColour.Blue;
                case SolidColour.GlowGreen:
                    return SolidColour.Green;
                default:
                    return SolidColour.White;
            }
        }

        /// <summary>A strip's colour in the catalogue's picture of the figure (its strokes on black).</summary>
        public static GuideColour Print(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.GlowRed:
                    return GuideColour.Red;
                case SolidColour.GlowPink:
                    return GuideColour.Pink;
                case SolidColour.GlowGold:
                    return GuideColour.Gold;
                case SolidColour.GlowBlue:
                    return GuideColour.Blue;
                case SolidColour.GlowGreen:
                    return GuideColour.Green;
                default:
                    return GuideColour.White;
            }
        }

        /// <summary>A setting's colour (cool white for one it does not know).</summary>
        public static SolidColour ColourOf(string name)
        {
            switch (name)
            {
                case "warm": return SolidColour.GlowWarm;
                case "red": return SolidColour.GlowRed;
                case "gold": return SolidColour.GlowGold;
                case "blue": return SolidColour.GlowBlue;
                case "pink": return SolidColour.GlowPink;
                case "green": return SolidColour.GlowGreen;
                default: return SolidColour.GlowWhite;
            }
        }

        /// <summary>One figure's settings (LampAds reads them from the spec).</summary>
        public sealed class Design
        {
            public string Shape = "lantern", Motif = "shanshui", Title = "";
            public float W = 0.7f, BodyH = 1.0f, Line = 0.015f;
            public bool Tassel = true, Mono;
            public SolidColour Frame = SolidColour.GlowWhite, Ink = SolidColour.GlowWhite;
        }

        /// <summary>The body's height: a round one is as tall as it is wide.</summary>
        public static float BodyHeight(Design d) => d.Shape == "round" ? d.W : d.BodyH;

        /// <summary>The figure's height: the hook, the body and the tassel when it has one.</summary>
        public static float Height(Design d) => Hook * d.W + BodyHeight(d) + (d.Tassel ? TasselHeight * d.W : 0f);

        /// <summary>The strokes of a figure: its frame, the hook, the tassel, the pattern and the characters down a column where the pattern leaves room.</summary>
        public static NeonArt Draw(Design d)
        {
            var art = new NeonArt();
            float w = d.W, hb = BodyHeight(d), top = Height(d) * 0.5f, bodyTop = top - Hook * w, bodyBottom = bodyTop - hb;
            art.BodyY = (bodyTop + bodyBottom) * 0.5f;
            // The frame's strip is inside the part: its middle line half the strip in from the edge.
            float inset = d.Line * 0.5f;
            Add(art, d.Frame, true, Outline(d.Shape, w - 2f * inset, hb - 2f * inset, art.BodyY));
            Add(art, d.Frame, false, new List<Point2> { new Point2(0f, bodyTop - inset), new Point2(0f, top - inset) });
            if (d.Tassel)
                Tassel(art, d.Frame, w, bodyBottom + inset, d.Line);
            // The pattern and the characters keep inside the frame: below a lantern's shoulders and an arch's top, in the box a round frame holds.
            float a = w * 0.5f, x0, x1, y0, y1;
            if (d.Shape == "round" || d.Shape == "haitang")
            {
                // The box inside the circle (its corners at 0.93 of the radius), or inside the begonia window's notches (0.8 of its half sizes at 45 degrees: 0.57 across and up).
                float k = d.Shape == "round" ? 0.66f : 0.52f;
                x1 = k * a;
                y1 = art.BodyY + k * hb * 0.5f;
                y0 = art.BodyY - k * hb * 0.5f;
            }
            else
            {
                // A palace lantern narrows at its foot as at its top: the pattern stands above the lower shoulders (the waves on them, as in the owner's photo).
                x1 = 0.88f * a;
                y1 = bodyTop - (d.Shape == "arch" ? ArchRise(w, hb) : ShoulderDrop(w, hb)) - 0.03f * hb;
                y0 = bodyBottom + (d.Shape == "arch" ? 0.06f * a : ShoulderDrop(w, hb) + 0.02f * hb);
            }

            x0 = -x1;
            Func<SolidColour, SolidColour> c = colour => d.Mono ? d.Frame : colour;
            // A stroke of the pattern that a low or a narrow body has no room for is left out.
            var pattern = new NeonArt();
            (float x, float maxSize, float floor) column = Pattern(pattern, d.Motif, c, x0, x1, y0, y1, d.Line);
            foreach (NeonStroke stroke in pattern.Strokes)
            {
                if (stroke.Points.TrueForAll(p => p.X >= x0 - d.Line && p.X <= x1 + d.Line && p.Y >= y0 - d.Line && p.Y <= y1 + d.Line))
                    art.Strokes.Add(stroke);
            }

            Letters(art, d.Title, d.Ink, column.x, y1 - 0.02f * hb, Math.Max(column.floor, y0) + 0.02f * hb, column.maxSize);
            Brace(art, d.Line);
            return art;
        }

        private static void Add(NeonArt art, SolidColour colour, bool closed, List<Point2> points)
        {
            var stroke = new NeonStroke { Colour = colour, Closed = closed };
            stroke.Points.AddRange(points);
            art.Strokes.Add(stroke);
        }

        private static float Distance(Point2 a, Point2 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static List<Point2> Arc(float cx, float cy, float rx, float ry, double from, double to, int steps)
        {
            var points = new List<Point2>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double t = from + (to - from) * i / steps;
                points.Add(new Point2(cx + rx * (float)Math.Cos(t), cy + ry * (float)Math.Sin(t)));
            }

            return points;
        }

        private static List<Point2> Circle(float cx, float cy, float r, int steps)
        {
            List<Point2> points = Arc(cx, cy, r, r, 0.0, 2.0 * Math.PI, steps);
            points.RemoveAt(points.Count - 1);
            return points;
        }

        // ------------------------------------------------------------------ the frames

        /// <summary>A palace lantern's cap: its half-width, and the radii of its rounded corner and of the concave shoulder down to the side (scaled down for a low body).</summary>
        private static void Shoulder(float w, float hb, out float cap, out float corner, out float shoulder)
        {
            float k = Math.Min(1f, 0.4f * hb / (0.2f * w));
            corner = 0.04f * w * k;
            shoulder = 0.16f * w * k;
            cap = w * 0.5f - shoulder;
        }

        private static float ShoulderDrop(float w, float hb)
        {
            Shoulder(w, hb, out _, out float corner, out float shoulder);
            return corner + shoulder;
        }

        private static float ArchRise(float w, float hb) => Math.Min(w * 0.5f, hb * 0.45f);

        /// <summary>The frame of the body round (0, cy), closed.</summary>
        public static List<Point2> Outline(string shape, float w, float hb, float cy)
        {
            float a = w * 0.5f, t = hb * 0.5f;
            var points = new List<Point2>();
            switch (shape)
            {
                case "round":
                    return Circle(0f, cy, a, 40);
                case "haitang":
                {
                    // A begonia window (海棠窗): four broad lobes, notched between them.
                    const int steps = 64;
                    for (int i = 0; i < steps; i++)
                    {
                        double th = 2.0 * Math.PI * i / steps;
                        float r = 0.8f + 0.2f * (float)Math.Abs(Math.Cos(2.0 * th));
                        points.Add(new Point2(a * r * (float)Math.Cos(th), cy + t * r * (float)Math.Sin(th)));
                    }

                    return points;
                }
                case "arch":
                {
                    float rise = ArchRise(w, hb), corner = 0.04f * w, shoulderY = cy + t - rise;
                    points.AddRange(Arc(a - corner, cy - t + corner, corner, corner, -0.5 * Math.PI, 0.0, 4));
                    points.AddRange(Arc(0f, shoulderY, a, rise, 0.0, Math.PI, 24));
                    points.AddRange(Arc(-a + corner, cy - t + corner, corner, corner, Math.PI, 1.5 * Math.PI, 4));
                    return points;
                }
                default:
                {
                    // A palace lantern (宫灯形, the owner's photo): a narrower cap at the top and the foot, each with rounded corners and a concave shoulder out to the straight sides.
                    Shoulder(w, hb, out float cap, out float corner, out float shoulder);
                    float yc = t - corner;
                    var right = new List<Point2>();
                    right.AddRange(Arc(cap - corner, cy + yc, corner, corner, 0.5 * Math.PI, 0.0, 4));
                    right.AddRange(Arc(a, cy + yc, shoulder, shoulder, Math.PI, 1.5 * Math.PI, 8));
                    right.AddRange(Arc(a, cy - yc, shoulder, shoulder, 0.5 * Math.PI, Math.PI, 8));
                    right.AddRange(Arc(cap - corner, cy - yc, corner, corner, 0.0, -0.5 * Math.PI, 4));
                    points.AddRange(right);
                    for (int i = right.Count - 1; i >= 0; i--)
                        points.Add(new Point2(-right[i].X, right[i].Y));
                    return points;
                }
            }
        }

        /// <summary>The cord under the body, the 如意 knot (two curls over a chin) and the tassel with its threads, down from the body's foot; each part on the one above it (line: the strips' width).</summary>
        private static void Tassel(NeonArt art, SolidColour colour, float w, float bodyBottom, float line)
        {
            float r = 0.075f * w, yc = bodyBottom - 0.2f * w - r * 0.1f;
            Add(art, colour, false, new List<Point2> { new Point2(0f, bodyBottom), new Point2(0f, yc) });
            foreach (float side in new[] { 1f, -1f })
            {
                // Each curl leaves the cord's foot over its top and turns inward, 1.1 turns.
                var curl = new List<Point2>();
                const int steps = 26;
                for (int i = 0; i <= steps; i++)
                {
                    double f = i / (double)steps, th = Math.PI - f * 2.2 * Math.PI;
                    float rr = r * (1f - 0.65f * (float)f);
                    curl.Add(new Point2(side * (r + rr * (float)Math.Cos(th)), yc + rr * (float)Math.Sin(th)));
                }

                Add(art, colour, false, curl);
            }

            // The chin starts where each curl is farthest out (a half turn in: 1 / 2.2 of it), its foot on the tassel's top, the threads from that top down to its rounded foot.
            float chin = yc - 0.16f * w, outer = r + r * (1f - 0.65f / 2.2f);
            Add(art, colour, false, new List<Point2>
            {
                new Point2(outer, yc), new Point2(1.35f * r, yc - 0.1f * w), new Point2(0.4f * r, chin), new Point2(-0.4f * r, chin), new Point2(-1.35f * r, yc - 0.1f * w), new Point2(-outer, yc)
            });
            float yt = chin - 0.5f * line, hw = 0.1f * w, foot = yt - 0.5f * w, bw = 0.14f * w, round = 0.08f * w;
            var body = new List<Point2> { new Point2(-hw, yt), new Point2(-bw, foot) };
            body.AddRange(Arc(0f, foot, bw, round, Math.PI, 2.0 * Math.PI, 10));
            body.Add(new Point2(hw, yt));
            Add(art, colour, true, body);
            foreach (float x in new[] { -0.05f * w, 0f, 0.05f * w })
                Add(art, colour, false, new List<Point2> { new Point2(x, yt), new Point2(x, foot - round * (float)Math.Sqrt(1.0 - x * x / (bw * bw))) });
        }

        // ------------------------------------------------------------------ the patterns

        /// <summary>
        /// A pattern in the box x0..x1, y0..y1 in its colours (through c: one colour when the figure has one); the column it leaves for the characters: its middle, the largest a character may be, and
        /// the lowest a character may go (over the waves).
        /// </summary>
        private static (float x, float maxSize, float floor) Pattern(NeonArt art, string motif, Func<SolidColour, SolidColour> c, float x0, float x1, float y0, float y1, float line)
        {
            float u = x1 - x0, v = y1 - y0, mid = (x0 + x1) * 0.5f;
            switch (motif)
            {
                case "shanshui":
                {
                    // As the owner's photo: hills at the top left, a cloud on the right, another over the waves at the foot, the characters right of the middle.
                    float waves = Waves(art, c, x0, y0, u);
                    Hills(art, c(SolidColour.GlowGold), x0 + 0.22f * u, y1 - 0.02f * v, 0.18f * u);
                    Scroll(art, c(SolidColour.GlowGold), x0 + 0.8f * u, y0 + 0.55f * v, 0.18f * u, 0.045f * u, 1, line);
                    Scroll(art, c(SolidColour.GlowGreen), x0 + 0.36f * u, y0 + waves + 0.05f * v + 0.045f * u, 0.3f * u, 0.045f * u, -1, line);
                    return (x0 + 0.58f * u, 0.32f * u, y0 + waves + 0.04f * v);
                }
                case "sun":
                {
                    float r = Math.Min(0.22f * u, 0.16f * v);
                    foreach (float k in new[] { 1f, 0.72f, 0.44f, 0.16f })
                        Add(art, c(SolidColour.GlowRed), true, Circle(x1 - r - 0.04f * u, y0 + 0.42f * v, r * k, k > 0.5f ? 32 : 16));
                    return (x0 + 0.3f * u, 0.38f * u, y0);
                }
                case "moon":
                {
                    float r = Math.Min(0.2f * u, 0.14f * v);
                    Moon(art, c(SolidColour.GlowGold), x0 + 0.28f * u, y1 - r - 0.04f * v, r);
                    Scroll(art, c(SolidColour.GlowWarm), x0 + 0.05f * u, y1 - 2.3f * r - 0.04f * v, 0.42f * u, 0.04f * u, 1, line);
                    return (x0 + 0.74f * u, 0.36f * u, y0);
                }
                case "clouds":
                    Scroll(art, c(SolidColour.GlowGold), x0 + 0.06f * u, y1 - 0.08f * v, 0.36f * u, 0.05f * u, 1, line);
                    Scroll(art, c(SolidColour.GlowGold), x1 - 0.06f * u, y0 + 0.1f * v + 0.05f * u, 0.36f * u, 0.05f * u, -1, line);
                    return (x0 + 0.62f * u, 0.36f * u, y0 + 0.18f * v + 0.15f * u);
                case "waves":
                    return (mid, 0.46f * u, y0 + Waves(art, c, x0, y0, u) + 0.06f * v);
                case "plum":
                    Plum(art, c, x0, y0, u, v);
                    return (x0 + 0.74f * u, 0.34f * u, y0);
                default:
                    return (mid, 0.8f * u, y0);
            }
        }

        /// <summary>Waves at the foot (青海波): four groups of concentric half circles standing on y0, gold, blue, gold and a small pink one, as in the owner's photo; their height.</summary>
        private static float Waves(NeonArt art, Func<SolidColour, SolidColour> c, float x0, float y0, float u)
        {
            float r = 0.17f * u;
            (float at, float radius, SolidColour colour)[] groups =
            {
                (0.18f, r, SolidColour.GlowGold), (0.46f, r, SolidColour.GlowBlue), (0.74f, r, SolidColour.GlowGold), (0.9f, 0.55f * r, SolidColour.GlowPink)
            };
            foreach ((float at, float radius, SolidColour colour) in groups)
            {
                foreach (float k in new[] { 1f, 0.68f, 0.36f })
                    Add(art, c(colour), false, Arc(x0 + at * u, y0, radius * k, radius * k, 0.0, Math.PI, k > 0.5f ? 14 : 8));
            }

            return r;
        }

        /// <summary>Hills (山): three nested peaks from the apex (ax, ay) down, half as wide as half.</summary>
        private static void Hills(NeonArt art, SolidColour colour, float ax, float ay, float half)
        {
            float height = 0.75f * half, step = 0.2f * half;
            for (int j = 0; j < 3; j++)
            {
                float hw = half - j * step, apex = ay - j * step * 1.3f;
                Add(art, colour, false, new List<Point2> { new Point2(ax - hw, ay - height), new Point2(ax, apex), new Point2(ax + hw, ay - height) });
            }
        }

        /// <summary>
        /// A lucky cloud (祥云) scroll: a curl of radius r whose foot is at (x, y), and a line from it length long, to the right (dir 1) or the left (-1), with a shorter one under it; the curl's middle
        /// no tighter than the strip (line) is wide.
        /// </summary>
        private static void Scroll(NeonArt art, SolidColour colour, float x, float y, float length, float r, int dir, float line)
        {
            var points = new List<Point2>();
            const int steps = 28;
            float inner = Math.Min(r, Math.Max(0.3f * r, line));
            for (int i = 0; i <= steps; i++)
            {
                // From the curl's middle out to its foot in 1.25 turns, where it runs on along the line (the way it goes there is dir).
                double f = i / (double)steps, th = -0.5 * Math.PI - (1.0 - f) * 2.5 * Math.PI;
                float rr = inner + (r - inner) * (float)f;
                points.Add(new Point2(x + dir * rr * (float)Math.Cos(th), y + r + rr * (float)Math.Sin(th)));
            }

            points.Add(new Point2(x + dir * length, y));
            Add(art, colour, false, points);
            Add(art, colour, false, new List<Point2> { new Point2(x + dir * 0.3f * length, y - r), new Point2(x + dir * 0.85f * length, y - r) });
        }

        /// <summary>A crescent moon round (x, y) of radius r, open to the right: the outer circle's arc and the inner circle's back.</summary>
        private static void Moon(NeonArt art, SolidColour colour, float x, float y, float r)
        {
            float d = 0.45f * r, ri = 0.8f * r;
            float ix = (r * r - ri * ri + d * d) / (2f * d), iy = (float)Math.Sqrt(Math.Max(0f, r * r - ix * ix));
            double outer = Math.Atan2(iy, ix), inner = Math.Atan2(iy, ix - d);
            List<Point2> points = Arc(x, y, r, r, outer, 2.0 * Math.PI - outer, 28);
            List<Point2> back = Arc(x + d, y, ri, ri, 2.0 * Math.PI - inner, inner, 20);
            back.RemoveAt(0);
            back.RemoveAt(back.Count - 1);
            points.AddRange(back);
            Add(art, colour, true, points);
        }

        /// <summary>A branch of plum blossom (梅花) from the foot on the left up: the branch in warm white, five-petalled pink flowers with gold hearts on it.</summary>
        private static void Plum(NeonArt art, Func<SolidColour, SolidColour> c, float x0, float y0, float u, float v)
        {
            var branch = new List<Point2>();
            for (int i = 0; i <= 12; i++)
            {
                float f = i / 12f;
                branch.Add(new Point2(x0 + u * (0.08f + 0.36f * f + 0.08f * (float)Math.Sin(f * Math.PI * 2.0)), y0 + v * (0.05f + 0.85f * f)));
            }

            Add(art, c(SolidColour.GlowWarm), false, branch);
            float r = 0.09f * u;
            foreach (int at in new[] { 3, 7, 11 })
            {
                Point2 p = branch[at];
                var flower = new List<Point2>();
                const int steps = 40;
                for (int i = 0; i < steps; i++)
                {
                    double th = Math.PI * 0.5 + 2.0 * Math.PI * i / steps;
                    float rr = r * (0.55f + 0.45f * (float)Math.Abs(Math.Cos(2.5 * (th - Math.PI * 0.5))));
                    flower.Add(new Point2(p.X + rr * (float)Math.Cos(th), p.Y + rr * (float)Math.Sin(th)));
                }

                Add(art, c(SolidColour.GlowPink), true, flower);
                Add(art, c(SolidColour.GlowGold), true, Circle(p.X, p.Y, 0.22f * r, 10));
            }
        }

        // ------------------------------------------------------------------ the characters

        /// <summary>The characters one under another round x, between top and bottom, each at most maxSize and as large as the column lets it be.</summary>
        private static void Letters(NeonArt art, string text, SolidColour colour, float x, float top, float bottom, float maxSize)
        {
            var chars = new List<char>();
            foreach (char ch in text ?? "")
            {
                if (!char.IsWhiteSpace(ch))
                    chars.Add(ch);
            }

            if (chars.Count == 0 || top - bottom < 0.05f)
                return;
            float size = Math.Min(maxSize, (top - bottom) / (chars.Count * 1.08f - 0.08f));
            float y = (top + bottom) * 0.5f + (chars.Count * 1.08f - 0.08f) * size * 0.5f - size * 0.5f;
            foreach (char ch in chars)
            {
                foreach (List<Point2> line in Letter(ch))
                {
                    var points = new List<Point2>(line.Count);
                    foreach (Point2 p in line)
                        points.Add(new Point2(x + p.X * size, y + p.Y * size));
                    bool closed = points.Count > 3 && Distance(points[0], points[points.Count - 1]) < 1e-5f;
                    if (closed)
                        points.RemoveAt(points.Count - 1);
                    Add(art, colour, closed, points);
                }

                y -= size * 1.08f;
            }
        }

        private const int Grid = 72, Pad = 2;

        private static readonly Dictionary<char, List<List<Point2>>> s_Letters = new Dictionary<char, List<List<Point2>>>();
        private static int s_LettersFont = int.MinValue;

        /// <summary>
        /// A character as neon strokes: the font's glyph filled on a grid of Grid cells an em, thinned to its middle lines (Zhang-Suen, then the steps of a diagonal taken out), traced into lines from
        /// their ends and forks (a loop round where it has neither), the short spurs at a stroke's corners left out, simplified, the free ends lengthened by the half stroke the thinning took.
        /// In the em box: x and y from -0.5 to 0.5, y up; a closed line ends where it starts. None without the font or the character. Made once a character for a font (MarkingLetters.Version).
        /// </summary>
        public static List<List<Point2>> Letter(char c)
        {
            lock (s_Letters)
            {
                if (s_LettersFont != MarkingLetters.Version)
                {
                    s_Letters.Clear();
                    s_LettersFont = MarkingLetters.Version;
                }

                if (s_Letters.TryGetValue(c, out List<List<Point2>> hit))
                    return hit;
            }

            GlyphOutline glyph = MarkingLetters.Outline(c, out float em, out float bottom, out float top);
            List<List<Point2>> made = glyph != null ? Thin(glyph, em, bottom, top) : new List<List<Point2>>();
            lock (s_Letters)
            {
                if (s_LettersFont == MarkingLetters.Version)
                    s_Letters[c] = made;
            }

            return made;
        }

        private static List<List<Point2>> Thin(GlyphOutline glyph, float em, float bottom, float top)
        {
            const int n = Grid + 2 * Pad;
            var on = new bool[n * n];
            float sx = Grid / em, sy = Grid / Math.Max(1f, top - bottom);
            var crossings = new List<(float X, int Dir)>();
            for (int j = Pad; j < Pad + Grid; j++)
            {
                // The glyph filled by its winding (non-zero): its outer contours and holes wind the other way round, and strokes that overlap stay filled.
                float y = top - (j - Pad + 0.5f) / sy;
                crossings.Clear();
                foreach (float[] contour in glyph.Contours)
                {
                    int m = contour.Length / 2;
                    for (int k = 0; k < m; k++)
                    {
                        int k1 = (k + 1) % m;
                        float ax = contour[2 * k], ay = contour[2 * k + 1], bx = contour[2 * k1], by = contour[2 * k1 + 1];
                        if ((ay <= y) == (by <= y))
                            continue;
                        crossings.Add((ax + (bx - ax) * (y - ay) / (by - ay), by > ay ? 1 : -1));
                    }
                }

                crossings.Sort((p, q) => p.X.CompareTo(q.X));
                int winding = 0;
                for (int k = 0; k + 1 < crossings.Count; k++)
                {
                    winding += crossings[k].Dir;
                    if (winding == 0)
                        continue;
                    int i0 = Math.Max(0, (int)Math.Ceiling(crossings[k].X * sx - 0.5f)), i1 = Math.Min(Grid - 1, (int)Math.Ceiling(crossings[k + 1].X * sx - 0.5f) - 1);
                    for (int i = i0; i <= i1; i++)
                        on[j * n + i + Pad] = true;
                }
            }

            int[] around = { -n, -n + 1, 1, n + 1, n, n - 1, -1, -n - 1 };
            var remove = new List<int>();
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int step = 0; step < 2; step++)
                {
                    remove.Clear();
                    for (int p = n + 1; p < n * n - n - 1; p++)
                    {
                        if (!on[p])
                            continue;
                        int count = 0, rises = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            count += on[p + around[k]] ? 1 : 0;
                            rises += !on[p + around[k]] && on[p + around[(k + 1) % 8]] ? 1 : 0;
                        }

                        bool north = on[p - n], east = on[p + 1], south = on[p + n], west = on[p - 1];
                        if (count < 2 || count > 6 || rises != 1)
                            continue;
                        if (step == 0 ? (north && east && south) || (east && south && west) : (north && east && west) || (north && south && west))
                            continue;
                        remove.Add(p);
                    }

                    foreach (int p in remove)
                        on[p] = false;
                    changed |= remove.Count > 0;
                }
            }

            // A corner of a diagonal step whose two neighbours touch each other is not needed: without it the line is one cell wide, so that only its ends and forks have other than two neighbours.
            for (int p = n + 1; p < n * n - n - 1; p++)
            {
                if (!on[p])
                    continue;
                bool north = on[p - n], east = on[p + 1], south = on[p + n], west = on[p - 1];
                if ((north && east && !south && !west && !on[p + n - 1]) || (east && south && !north && !west && !on[p - n - 1])
                    || (south && west && !north && !east && !on[p - n + 1]) || (west && north && !south && !east && !on[p + n + 1]))
                    on[p] = false;
            }

            int Degree(int p)
            {
                int d = 0;
                foreach (int o in around)
                    d += on[p + o] ? 1 : 0;
                return d;
            }

            var lines = new List<List<int>>();
            var used = new HashSet<long>();
            long Key(int a, int b) => a < b ? (long)a * n * n + b : (long)b * n * n + a;
            void Walk(int from, int to)
            {
                var line = new List<int> { from, to };
                int prev = from, cur = to;
                while (cur != from && Degree(cur) == 2)
                {
                    int next = -1;
                    foreach (int o in around)
                    {
                        int q = cur + o;
                        if (on[q] && q != prev && !used.Contains(Key(cur, q)))
                        {
                            next = q;
                            break;
                        }
                    }

                    if (next < 0)
                        break;
                    used.Add(Key(cur, next));
                    line.Add(next);
                    prev = cur;
                    cur = next;
                }

                lines.Add(line);
            }

            // From the ends and the forks first; what is left are loops.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int p = n + 1; p < n * n - n - 1; p++)
                {
                    if (!on[p] || (pass == 0) == (Degree(p) == 2))
                        continue;
                    foreach (int o in around)
                    {
                        if (on[p + o] && used.Add(Key(p, p + o)))
                            Walk(p, p + o);
                    }
                }
            }

            var strokes = new List<List<Point2>>();
            foreach (List<int> line in lines)
            {
                int first = Degree(line[0]), last = Degree(line[line.Count - 1]);
                // A spur: a short line from a fork to a free end, left where a thick stroke turns.
                if (line.Count < 6 && ((first == 1 && last >= 3) || (last == 1 && first >= 3)))
                    continue;
                var points = new List<Point2>(line.Count);
                foreach (int p in line)
                    points.Add(new Point2(p % n, p / n));
                points = Simplify(points, 0.6f);
                if (points.Count < 2)
                    continue;
                if (first == 1)
                    points[0] = Extend(points[1], points[0], 1.5f);
                if (last == 1)
                    points[points.Count - 1] = Extend(points[points.Count - 2], points[points.Count - 1], 1.5f);
                var unit = new List<Point2>(points.Count);
                foreach (Point2 p in points)
                    unit.Add(new Point2((p.X - Pad + 0.5f) / Grid - 0.5f, 0.5f - (p.Y - Pad + 0.5f) / Grid));
                strokes.Add(unit);
            }

            return strokes;
        }

        /// <summary>b moved away from a by more (in cells).</summary>
        private static Point2 Extend(Point2 a, Point2 b, float more)
        {
            float d = Distance(a, b);
            return d < 1e-6f ? b : new Point2(b.X + (b.X - a.X) / d * more, b.Y + (b.Y - a.Y) / d * more);
        }

        /// <summary>A line with the points that are within tolerance of the line through their neighbours taken out (Douglas-Peucker); a closed line keeps its ends.</summary>
        private static List<Point2> Simplify(List<Point2> points, float tolerance)
        {
            if (points.Count < 3)
                return points;
            var keep = new bool[points.Count];
            keep[0] = keep[points.Count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, points.Count - 1));
            while (stack.Count > 0)
            {
                (int a, int b) = stack.Pop();
                float worst = tolerance;
                int at = -1;
                for (int i = a + 1; i < b; i++)
                {
                    float d = ToSegment(points[i], points[a], points[b]);
                    if (d > worst)
                    {
                        worst = d;
                        at = i;
                    }
                }

                if (at < 0)
                    continue;
                keep[at] = true;
                stack.Push((a, at));
                stack.Push((at, b));
            }

            var kept = new List<Point2>();
            for (int i = 0; i < points.Count; i++)
            {
                if (keep[i])
                    kept.Add(points[i]);
            }

            return kept;
        }

        private static float ToSegment(Point2 p, Point2 a, Point2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            return Distance(p, new Point2(a.X + dx * t, a.Y + dy * t));
        }

        // ------------------------------------------------------------------ the skeleton

        private sealed class Member
        {
            public readonly List<(Point2 A, Point2 B)> Segments = new List<(Point2 A, Point2 B)>();
            public float MinX = float.MaxValue, MinY = float.MaxValue, MaxX = float.MinValue, MaxY = float.MinValue;

            public void Add(Point2 a, Point2 b)
            {
                Segments.Add((a, b));
                MinX = Math.Min(MinX, Math.Min(a.X, b.X));
                MaxX = Math.Max(MaxX, Math.Max(a.X, b.X));
                MinY = Math.Min(MinY, Math.Min(a.Y, b.Y));
                MaxY = Math.Max(MaxY, Math.Max(a.Y, b.Y));
            }

            public bool Near(Member other, float within) =>
                MinX - within <= other.MaxX && other.MinX - within <= MaxX && MinY - within <= other.MaxY && other.MinY - within <= MaxY;
        }

        /// <summary>
        /// The skeleton (0.12.49; the owner: "应该有一些支撑，现在霓虹灯悬浮着"): strips that touch are one piece; each piece that does not touch the frame is welded to what is already held by a straight
        /// rod across or up and down, the shortest from any of its points to a held strip or rod, the shortest of all first so that a later piece may be held by an earlier one; a piece that no such
        /// rod reaches gets the shortest rod in any direction. A rod also holds every piece it crosses. The rods run through the strips' middle, where their ends are hidden.
        /// </summary>
        private static void Brace(NeonArt art, float line)
        {
            var members = new List<Member>();
            foreach (NeonStroke stroke in art.Strokes)
            {
                var m = new Member();
                int count = stroke.Points.Count, segments = stroke.Closed && count > 2 ? count : count - 1;
                for (int i = 0; i < segments; i++)
                    m.Add(stroke.Points[i], stroke.Points[(i + 1) % count]);
                members.Add(m);
            }

            if (members.Count == 0)
                return;
            // Strips touch where their middle lines are nearer than nine tenths of their width.
            float touch = 0.9f * line, weld = 0.5f * line;
            var parent = new int[members.Count];
            for (int i = 0; i < parent.Length; i++)
                parent[i] = i;
            int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);
            for (int i = 0; i < members.Count; i++)
            {
                for (int j = i + 1; j < members.Count; j++)
                {
                    if (Root(i) != Root(j) && members[i].Near(members[j], touch) && Within(members[i], members[j], touch))
                        parent[Root(j)] = Root(i);
                }
            }

            var pieces = new Dictionary<int, Member>();
            for (int i = 0; i < members.Count; i++)
            {
                int root = Root(i);
                if (!pieces.TryGetValue(root, out Member piece))
                    pieces[root] = piece = new Member();
                foreach ((Point2 a, Point2 b) in members[i].Segments)
                    piece.Add(a, b);
            }

            Member held = pieces[Root(0)];
            var loose = new List<Member>();
            foreach (KeyValuePair<int, Member> pair in pieces)
            {
                if (pair.Key != Root(0))
                    loose.Add(pair.Value);
            }

            var best = new List<(float Length, Point2 From, Point2 To)>();
            foreach (Member piece in loose)
                best.Add(Straight(piece, held.Segments, (float.MaxValue, default, default)));
            while (loose.Count > 0)
            {
                int pick = 0;
                for (int k = 1; k < loose.Count; k++)
                {
                    if (best[k].Length < best[pick].Length)
                        pick = k;
                }

                (float Length, Point2 From, Point2 To) rod = best[pick];
                if (rod.Length == float.MaxValue)
                {
                    for (int k = 0; k < loose.Count; k++)
                        best[k] = Nearest(loose[k], held.Segments);
                    pick = 0;
                    for (int k = 1; k < loose.Count; k++)
                    {
                        if (best[k].Length < best[pick].Length)
                            pick = k;
                    }

                    rod = best[pick];
                }

                art.Rods.Add((rod.From, rod.To));
                var added = new Member();
                added.Add(rod.From, rod.To);
                var rodOnly = new Member();
                rodOnly.Add(rod.From, rod.To);
                foreach ((Point2 a, Point2 b) in loose[pick].Segments)
                    added.Add(a, b);
                loose.RemoveAt(pick);
                best.RemoveAt(pick);
                for (int k = loose.Count - 1; k >= 0; k--)
                {
                    if (loose[k].Near(rodOnly, weld) && Within(loose[k], rodOnly, weld))
                    {
                        foreach ((Point2 a, Point2 b) in loose[k].Segments)
                            added.Add(a, b);
                        loose.RemoveAt(k);
                        best.RemoveAt(k);
                    }
                }

                foreach ((Point2 a, Point2 b) in added.Segments)
                    held.Add(a, b);
                for (int k = 0; k < loose.Count; k++)
                    best[k] = Straight(loose[k], added.Segments, best[k]);
            }
        }

        /// <summary>Whether two members' middle lines come within a distance of each other.</summary>
        private static bool Within(Member p, Member q, float within)
        {
            foreach ((Point2 a, Point2 b) in p.Segments)
            {
                foreach ((Point2 c, Point2 d) in q.Segments)
                {
                    if (SegmentDistance(a, b, c, d) <= within)
                        return true;
                }
            }

            return false;
        }

        /// <summary>The shortest rod across or up and down from a point of the piece (its segments' ends and middles) to one of the targets; the better of it and so far.</summary>
        private static (float Length, Point2 From, Point2 To) Straight(Member piece, List<(Point2 A, Point2 B)> targets, (float Length, Point2 From, Point2 To) sofar)
        {
            foreach ((Point2 a, Point2 b) in piece.Segments)
            {
                foreach (Point2 p in new[] { a, new Point2((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f) })
                {
                    foreach ((Point2 c, Point2 d) in targets)
                    {
                        for (int dir = 0; dir < 4; dir++)
                        {
                            float t = Ray(p, dir, c, d);
                            if (t < sofar.Length)
                                sofar = (t, p, dir == 0 ? new Point2(p.X + t, p.Y) : dir == 1 ? new Point2(p.X - t, p.Y) : dir == 2 ? new Point2(p.X, p.Y + t) : new Point2(p.X, p.Y - t));
                        }
                    }
                }
            }

            return sofar;
        }

        /// <summary>How far from p along +x, -x, +y or -y (dir 0 to 3) the segment c-d is; MaxValue when it is not that way.</summary>
        private static float Ray(Point2 p, int dir, Point2 c, Point2 d)
        {
            bool across = dir < 2;
            float pa = across ? p.Y : p.X, ca = across ? c.Y : c.X, da = across ? d.Y : d.X;
            if ((ca - pa) * (da - pa) > 0f || ca == da)
                return float.MaxValue;
            float cb = across ? c.X : c.Y, db = across ? d.X : d.Y, at = cb + (db - cb) * (pa - ca) / (da - ca);
            float t = dir % 2 == 0 ? at - (across ? p.X : p.Y) : (across ? p.X : p.Y) - at;
            return t > 1e-4f ? t : float.MaxValue;
        }

        /// <summary>The shortest rod in any direction from the piece to one of the targets.</summary>
        private static (float Length, Point2 From, Point2 To) Nearest(Member piece, List<(Point2 A, Point2 B)> targets)
        {
            (float Length, Point2 From, Point2 To) found = (float.MaxValue, default, default);
            foreach ((Point2 a, Point2 b) in piece.Segments)
            {
                foreach ((Point2 c, Point2 d) in targets)
                {
                    foreach ((Point2 p, Point2 s0, Point2 s1, bool fromPiece) in new[] { (a, c, d, true), (b, c, d, true), (c, a, b, false), (d, a, b, false) })
                    {
                        Point2 q = Closest(p, s0, s1);
                        float length = Distance(p, q);
                        if (length < found.Length)
                            found = fromPiece ? (length, p, q) : (length, q, p);
                    }
                }
            }

            return found;
        }

        private static Point2 Closest(Point2 p, Point2 a, Point2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
            float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            return new Point2(a.X + dx * t, a.Y + dy * t);
        }

        /// <summary>The distance between two segments (0 where they cross).</summary>
        public static float SegmentDistance(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            float Side(Point2 p, Point2 q, Point2 r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
            float s1 = Side(a, b, c), s2 = Side(a, b, d), s3 = Side(c, d, a), s4 = Side(c, d, b);
            if (((s1 > 0f && s2 < 0f) || (s1 < 0f && s2 > 0f)) && ((s3 > 0f && s4 < 0f) || (s3 < 0f && s4 > 0f)))
                return 0f;
            return Math.Min(Math.Min(Distance(a, Closest(a, c, d)), Distance(b, Closest(b, c, d))), Math.Min(Distance(c, Closest(c, a, b)), Distance(d, Closest(d, a, b))));
        }

        // ------------------------------------------------------------------ the strips and the picture

        /// <summary>
        /// A stroke as the runs a strip is built along (SignBuilder.PoleAds): its points without repeats, cut at each turn sharper than 100 degrees, so that no mitre gets long (the strips of two runs
        /// overlap there); loop: one run all round a closed stroke that has no such turn.
        /// </summary>
        public static List<List<Point2>> Runs(NeonStroke stroke, out bool loop)
        {
            loop = false;
            var points = new List<Point2>();
            foreach (Point2 p in stroke.Points)
            {
                if (points.Count == 0 || Distance(points[points.Count - 1], p) > 0.001f)
                    points.Add(p);
            }

            bool closed = stroke.Closed;
            if (closed && points.Count > 1 && Distance(points[0], points[points.Count - 1]) <= 0.001f)
                points.RemoveAt(points.Count - 1);
            var runs = new List<List<Point2>>();
            if (points.Count < 2)
                return runs;
            int m = points.Count;
            if (closed && m > 2)
            {
                int first = -1;
                for (int i = 0; i < m && first < 0; i++)
                {
                    if (Sharp(points[(i + m - 1) % m], points[i], points[(i + 1) % m]))
                        first = i;
                }

                if (first < 0)
                {
                    loop = true;
                    runs.Add(points);
                    return runs;
                }

                // Opened at a sharp turn: it starts and ends there.
                var open = new List<Point2>(m + 1);
                for (int k = 0; k <= m; k++)
                    open.Add(points[(first + k) % m]);
                points = open;
            }

            var run = new List<Point2> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                run.Add(points[i]);
                if (i < points.Count - 1 && Sharp(points[i - 1], points[i], points[i + 1]))
                {
                    runs.Add(run);
                    run = new List<Point2> { points[i] };
                }
            }

            runs.Add(run);
            return runs;
        }

        private static bool Sharp(Point2 a, Point2 b, Point2 c)
        {
            float l0 = Distance(a, b), l1 = Distance(b, c);
            return l0 > 1e-6f && l1 > 1e-6f && ((b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y)) / (l0 * l1) < SharpTurn;
        }

        /// <summary>The part of a figure w x h as the catalogue pictures it (the builder draws no face for it): its rods in grey and its strokes line wide on black.</summary>
        public static GuidePart Picture(NeonArt art, float w, float h, float line)
        {
            var part = new GuidePart { W = w, H = h, Shape = GuidePartShape.RoundRect, Ground = GuideColour.Black, Liner = 0f, Border = 0f };
            foreach ((Point2 a, Point2 b) in art.Rods)
            {
                if (Distance(a, b) > 1e-4f)
                    part.Elements.Add(GuideKit.Bar(w * 0.5f + a.X, h * 0.5f - a.Y, w * 0.5f + b.X, h * 0.5f - b.Y, 2f * RodRadius, GuideColour.Gray));
            }

            foreach (NeonStroke stroke in art.Strokes)
            {
                GuideColour colour = Print(stroke.Colour);
                int m = stroke.Points.Count, segments = stroke.Closed ? m : m - 1;
                for (int i = 0; i < segments; i++)
                {
                    Point2 a = stroke.Points[i], b = stroke.Points[(i + 1) % m];
                    if (Distance(a, b) > 1e-4f)
                        part.Elements.Add(GuideKit.Bar(w * 0.5f + a.X, h * 0.5f - a.Y, w * 0.5f + b.X, h * 0.5f - b.Y, line, colour));
                }
            }

            return part;
        }
    }
}
