using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of the prohibition signs (GB 5768.2-2022 chapter 5): vehicles, people, the arrows of the turning bans, the horn, the axle. Each is redrawn from the dimensioned drawing
    /// of Appendix E (the "制作图例", a 20 x 20 grid over the sign's diameter D): the coordinates below are read off that grid in units of D/100 (so 5 units to a grid cell), with the sign's
    /// centre at (50, 50), and mapped into the glyph's own 100 x 100 box by P_Pen. Unless said otherwise the box is the square 11..89 of the sign (round the inside of the ring): a figure placed back into that square of a
    /// round sign (Canvas.Pic with 11, 11, 78, 78 on a canvas over the whole sign) stands where the printed figure stands. Vehicles face left, as printed.
    /// </summary>
    public static partial class Glyphs
    {
        // ------------------------------------------------------------------ the pen: coordinates of the sign mapped into a glyph's box

        /// <summary>The square of the round sign (units of D/100) that the box of a P_ glyph covers, unless the glyph says otherwise.</summary>
        internal const float P_FrameX = 11f, P_FrameY = 11f, P_FrameSide = 78f;

        /// <summary>A PicBuilder that takes coordinates of the sign (units of D/100) and writes them into the glyph's box: the square (x0, y0, side) of the sign becomes 0..100.</summary>
        private sealed class P_Pen
        {
            private readonly PicBuilder m_B = new PicBuilder();
            private readonly float m_X0, m_Y0, m_K;

            public P_Pen(float x0 = P_FrameX, float y0 = P_FrameY, float side = P_FrameSide)
            {
                m_X0 = x0;
                m_Y0 = y0;
                m_K = 100f / side;
            }

            private float X(float x) => (x - m_X0) * m_K;

            private float Y(float y) => (y - m_Y0) * m_K;

            public P_Pen Circle(float cx, float cy, float r, bool hole = false)
            {
                m_B.Circle(X(cx), Y(cy), r * m_K, hole);
                return this;
            }

            public P_Pen Ellipse(float cx, float cy, float rx, float ry, bool hole = false)
            {
                m_B.Ellipse(X(cx), Y(cy), rx * m_K, ry * m_K, hole);
                return this;
            }

            /// <summary>A wheel: a tyre ring (or a disc when inner is 0).</summary>
            public P_Pen Ring(float cx, float cy, float outer, float inner)
            {
                m_B.Circle(X(cx), Y(cy), outer * m_K);
                if (inner > 0f)
                    m_B.Circle(X(cx), Y(cy), inner * m_K, true);
                return this;
            }

            public P_Pen Rect(float x, float y, float w, float h, bool hole = false)
            {
                m_B.Rect(X(x), Y(y), w * m_K, h * m_K, hole);
                return this;
            }

            public P_Pen RoundRect(float x, float y, float w, float h, float r, bool hole = false)
            {
                m_B.RoundRect(X(x), Y(y), w * m_K, h * m_K, r * m_K, hole);
                return this;
            }

            public P_Pen Capsule(float x0, float y0, float x1, float y1, float width, bool hole = false)
            {
                m_B.Capsule(X(x0), Y(y0), X(x1), Y(y1), width * m_K, hole);
                return this;
            }

            public P_Pen Stroke(float width, params float[] points)
            {
                for (int i = 0; i + 3 < points.Length; i += 2)
                    Capsule(points[i], points[i + 1], points[i + 2], points[i + 3], width);
                return this;
            }

            public P_Pen Poly(bool hole, params float[] points)
            {
                var mapped = new float[points.Length];
                for (int i = 0; i + 1 < points.Length; i += 2)
                {
                    mapped[i] = X(points[i]);
                    mapped[i + 1] = Y(points[i + 1]);
                }

                m_B.Poly(hole, mapped);
                return this;
            }

            public P_Pen Poly(params float[] points) => Poly(false, points);

            public P_Pen Shape(P_Outline outline, bool hole = false) => Poly(hole, outline.Points);

            /// <summary>A band along a circle between two radii, from angle a0 to a1 (degrees, y down: 90 is straight down, -90 straight up).</summary>
            public P_Pen ArcBand(float cx, float cy, float rIn, float rOut, float a0, float a1, bool hole = false) =>
                Shape(new P_Outline().Arc(cx, cy, rOut, a0, a1).Arc(cx, cy, rIn, a1, a0), hole);

            public override string ToString() => m_B.ToString();
        }

        /// <summary>The outline of one polygon in the sign's units, written point by point, with circular arcs and Bezier curves cut into short pieces.</summary>
        private sealed class P_Outline
        {
            private readonly List<float> m_Points = new List<float>();

            public float[] Points => m_Points.ToArray();

            public P_Outline L(params float[] xy)
            {
                m_Points.AddRange(xy);
                return this;
            }

            /// <summary>Points on a circle from angle a0 to a1 (degrees, y down), both ends included.</summary>
            public P_Outline Arc(float cx, float cy, float r, float a0, float a1, int steps = 0)
            {
                if (steps <= 0)
                    steps = Math.Max(3, (int)Math.Ceiling(Math.Abs(a1 - a0) / 8f));
                for (int i = 0; i <= steps; i++)
                {
                    double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                    m_Points.Add(cx + r * (float)Math.Cos(a));
                    m_Points.Add(cy + r * (float)Math.Sin(a));
                }

                return this;
            }

            /// <summary>A quadratic curve from the last point through the control point (cx, cy) to (x, y).</summary>
            public P_Outline Q(float cx, float cy, float x, float y)
            {
                int n = m_Points.Count;
                float x0 = m_Points[n - 2], y0 = m_Points[n - 1];
                const int steps = 8;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps, u = 1f - t;
                    m_Points.Add(u * u * x0 + 2f * u * t * cx + t * t * x);
                    m_Points.Add(u * u * y0 + 2f * u * t * cy + t * t * y);
                }

                return this;
            }

            /// <summary>A cubic curve from the last point with the control points (c1, c2) to (x, y).</summary>
            public P_Outline C(float c1x, float c1y, float c2x, float c2y, float x, float y)
            {
                int n = m_Points.Count;
                float x0 = m_Points[n - 2], y0 = m_Points[n - 1];
                const int steps = 10;
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps, u = 1f - t;
                    float a = u * u * u, b = 3f * u * u * t, c = 3f * u * t * t, d = t * t * t;
                    m_Points.Add(a * x0 + b * c1x + c * c2x + d * x);
                    m_Points.Add(a * y0 + b * c1y + c * c2y + d * y);
                }

                return this;
            }
        }

        // ------------------------------------------------------------------ motor vehicles

        /// <summary>A car seen from the front (禁6 Fig. E.6, 禁42 Fig. E.43): the cabin with its windscreen, the wide body with two lamps and two grilles, the two tyres under it.</summary>
        public static readonly string P_CarFront = new P_Pen()
            .Shape(new P_Outline().L(25f, 64.2f, 23.4f, 49.6f, 24.1f, 48.2f, 27.2f, 46.3f, 29.3f, 33.4f, 30.7f, 32f).Q(50f, 25.6f, 69.3f, 32f).L(70.7f, 33.4f, 72.8f, 46.3f, 75.9f, 48.2f, 76.6f, 49.6f, 75f, 64.2f))
            .RoundRect(27.4f, 62f, 5.2f, 9.7f, 1.6f)
            .RoundRect(67.4f, 62f, 5.2f, 9.7f, 1.6f)
            .Shape(new P_Outline().L(32.1f, 35.6f).Q(50f, 30.5f, 67.9f, 35.6f).L(69.5f, 44.1f).Q(50f, 41.3f, 30.5f, 44.1f), true)
            .Circle(28.6f, 50.3f, 3.1f, true)
            .Circle(71.4f, 50.3f, 3.1f, true)
            .Shape(new P_Outline().L(33f, 49.2f, 33.8f, 48.4f).Q(50f, 46.6f, 66.2f, 48.4f).L(67f, 49.2f, 67f, 51.4f, 66.2f, 52.2f, 33.8f, 52.2f, 33f, 51.4f), true)
            .Poly(true, 34.3f, 53.9f, 65.7f, 53.9f, 66.3f, 55.1f, 65.5f, 58.1f, 34.5f, 58.1f, 33.7f, 55.1f)
            .ToString();

        /// <summary>A bus seen from the side (禁7 Fig. E.7): a long box with the door window at the front, four windows, the bumper at the front, two wheels in their arches.</summary>
        public static readonly string P_Bus = new P_Pen()
            .Shape(new P_Outline()
                .L(20.3f, 42f, 21.3f, 40.6f, 79.5f, 40.6f).Arc(79.5f, 43.1f, 2.5f, -90f, 0f).L(82f, 54f, 81f, 55.4f, 68.9f, 57.6f)
                .Arc(64.7f, 57.8f, 4.3f, 0f, -180f).L(36.8f, 57.8f).Arc(32.5f, 57.8f, 4.3f, 0f, -180f).L(20f, 58.3f, 18.6f, 58.3f, 18.6f, 52.2f, 19.6f, 49.6f, 20.3f, 49.2f))
            .RoundRect(23.2f, 44f, 3.3f, 7.4f, 1.6f, true)
            .RoundRect(29.6f, 44.2f, 10f, 4.5f, 0.7f, true)
            .RoundRect(42.3f, 44.2f, 10f, 4.5f, 0.7f, true)
            .RoundRect(54.8f, 44.2f, 10f, 4.5f, 0.7f, true)
            .RoundRect(67.4f, 44.2f, 10f, 4.5f, 0.7f, true)
            .Ring(32.5f, 57.8f, 3.3f, 1.4f)
            .Ring(64.7f, 57.8f, 3.3f, 1.4f)
            .ToString();

        /// <summary>A saloon car seen from the side (禁8 Fig. E.8): bonnet, windscreen, roof, rear window and boot, two windows, two wheels in deep arches.</summary>
        public static readonly string P_Car = new P_Pen()
            .Shape(new P_Outline()
                .L(19f, 58.5f, 19f, 51.5f).Arc(21.5f, 51.5f, 2.5f, 180f, 270f).L(33f, 46.6f, 45.5f, 38.3f, 63.5f, 38.3f, 74f, 46.6f, 79.5f, 46.6f).Arc(79.5f, 49.1f, 2.5f, -90f, 0f)
                .L(82f, 58.5f, 81f, 60f, 76.4f, 60f).Arc(68.8f, 60f, 7.6f, 0f, -180f).L(40.6f, 60f).Arc(33f, 60f, 7.6f, 0f, -180f).L(20.4f, 60f))
            .Poly(true, 37.8f, 46.7f, 46.2f, 40.6f, 53.3f, 40.6f, 53.3f, 46.7f)
            .Poly(true, 56.2f, 40.6f, 62.5f, 40.6f, 69.9f, 46.7f, 56.2f, 46.7f)
            .Circle(33f, 59.2f, 5.2f)
            .Circle(68.8f, 59.2f, 5.2f)
            .ToString();

        /// <summary>A lorry seen from the side (禁9 Fig. E.9): the cab with its window, the flat bed with three loads on it, two wheels with a hub.</summary>
        public static readonly string P_Truck = new P_Pen()
            .Shape(new P_Outline()
                .L(21.5f, 59f, 21.5f, 57.8f, 23.7f, 57.6f, 21.8f, 49.4f, 22.3f, 47.9f).Q(23f, 46.6f, 25f, 46.3f).L(29.6f, 34.2f, 30.6f, 33.1f, 38.8f, 33.1f, 39.9f, 34f, 40f, 46f)
                .Q(40.2f, 47.9f, 42.5f, 48f).L(78.8f, 48f, 78.8f, 59f, 69.5f, 59f).Arc(63.4f, 62.1f, 6.8f, -27f, -153f).L(38.3f, 59f).Arc(32.4f, 62.1f, 6.7f, -27.5f, -152.5f))
            .Poly(true, 28.3f, 44.3f, 31.2f, 35.3f, 37.1f, 35.3f, 37.4f, 44.3f)
            .Rect(41.2f, 36.8f, 11.5f, 9.8f)
            .Rect(54f, 36.8f, 11.4f, 9.8f)
            .Rect(66.7f, 36.8f, 11.3f, 9.8f)
            .Ring(32.4f, 62.1f, 5.2f, 3.5f)
            .Ring(63.4f, 62.1f, 5.2f, 3.5f)
            .ToString();

        /// <summary>
        /// A tractor with a semi-trailer (禁10 Fig. E.10): the cab with its window, the long box of the trailer resting on the tractor, two axles under the tractor and a tandem axle under the
        /// trailer, each wheel in its arch.
        /// </summary>
        public static readonly string P_Trailer = new P_Pen()
            .Shape(new P_Outline()
                .L(18.2f, 57.8f, 19.4f, 55.8f, 19.4f, 44.4f, 21.6f, 38.3f, 32.3f, 38.3f, 32.3f, 51.7f, 48.4f, 51.7f, 48.4f, 57.8f, 46.7f, 57.8f)
                .Arc(41.9f, 58.4f, 4.85f, -7f, -173f).L(30.7f, 57.8f).Arc(25.9f, 58.4f, 4.85f, -7f, -173f))
            .Poly(true, 21.4f, 45.4f, 23.4f, 41f, 28.6f, 41f, 28.6f, 45.4f)
            .Rect(37.3f, 37.2f, 43.4f, 14.6f)
            .Shape(new P_Outline().L(52.9f, 57.8f, 54.7f, 57.8f).Arc(59.5f, 58.4f, 4.85f, -173f, -7f).L(64.3f, 57.8f, 66.2f, 57.8f).Arc(71f, 58.4f, 4.85f, -173f, -7f).L(75.8f, 57.8f, 77.2f, 57.8f, 77.2f, 51.6f, 52.9f, 51.6f))
            .Circle(25.9f, 58.4f, 3.75f)
            .Circle(41.9f, 58.4f, 3.75f)
            .Circle(59.5f, 58.4f, 3.75f)
            .Circle(71f, 58.4f, 3.75f)
            .ToString();

        /// <summary>A wheeled tractor (禁11 Fig. E.11): the exhaust pipe, the bonnet with its lamp, the cab with a slanted window, a small front wheel and a big rear wheel under a mudguard.</summary>
        public static readonly string P_Tractor = new P_Pen()
            .Shape(new P_Outline()
                .L(28f, 41f, 48f, 41f, 51.5f, 28.2f, 70.5f, 28.2f, 71.3f, 29f, 71.3f, 47.4f)
                .Arc(65.8f, 57.9f, 11.6f, -62.4f, -180f).L(39.6f, 57.9f)
                .Arc(31.7f, 61.2f, 8.6f, -22.6f, -115.5f).L(28f, 48.6f, 25.6f, 49.1f, 25.6f, 44.9f, 28f, 44.5f))
            .Poly(true, 52.3f, 40.9f, 55.2f, 32.3f, 66.6f, 32.3f, 66.6f, 40.9f)
            .Rect(33.9f, 30.4f, 2.4f, 11f)
            .ArcBand(65.8f, 57.9f, 11.6f, 13.9f, -65f, -33f)
            .Circle(65.8f + 12.75f * 0.8387f, 57.9f - 12.75f * 0.5446f, 1.15f)
            .Ring(65.8f, 57.9f, 10.6f, 6f)
            .Ring(31.7f, 61.2f, 7.1f, 4.1f)
            .ToString();

        /// <summary>
        /// A three-wheeled lorry (三轮汽车、低速货车, 禁12 Fig. E.12): a motorcycle front with its mudguard and fork, a closed cab with two windows, a flat bed with a side rail of three panels,
        /// one rear wheel.
        /// </summary>
        public static readonly string P_ThreeWheelTruck = new P_Pen()
            .Shape(new P_Outline()
                .L(34.4f, 60.7f, 51.6f, 60.7f, 51.6f, 57.6f, 61.2f, 57.6f).Arc(66.9f, 60.6f, 6.45f, -152f, -28f).L(80f, 57.6f, 80f, 46.6f, 50.7f, 46.6f, 50.7f, 31.2f)
                .L(50.2f, 30.5f, 38.3f, 30.5f, 37.4f, 31.3f, 33.2f, 44.9f, 31.5f, 46f, 30.8f, 47.8f, 31.2f, 52f, 32.2f, 54.6f, 33.6f, 57.6f))
            .Poly(true, 33.6f, 44.7f, 37.2f, 32.4f, 38.9f, 32.4f, 35.4f, 44.7f)
            .Poly(true, 37.7f, 44.7f, 41.7f, 32.4f, 49.3f, 32.4f, 49.3f, 44.7f)
            .Rect(51.3f, 47.7f, 8.4f, 4f, true)
            .Rect(60.6f, 47.7f, 9f, 4f, true)
            .Rect(70.8f, 47.7f, 8.4f, 4f, true)
            .ArcBand(28.3f, 60.4f, 6.3f, 7.7f, 221f, 303f)
            .Circle(28.3f + 7f * -0.7547f, 60.4f + 7f * -0.6561f, 0.7f)
            .Capsule(28.3f, 60.4f, 31.4f, 54.4f, 1.9f)
            .Ring(28.3f, 60.4f, 5f, 3.5f)
            .Ring(66.9f, 60.6f, 5.3f, 3.9f)
            .ToString();

        // ------------------------------------------------------------------ two-wheelers and people

        /// <summary>
        /// A motorcycle with its rider (禁13 Fig. E.13): fat tyres with a hub, the fork, the headlamp box, tank, seat and engine, the rider leaning on the handlebar. The white gaps that part the
        /// arm from the tank and the shin from the engine are cut out of the one black shape, as printed.
        /// </summary>
        public static readonly string P_Motorcycle = new P_Pen()
            .Circle(47.4f, 25.9f, 4.4f)
            .Shape(new P_Outline()
                .L(40.6f, 43.4f, 45.3f, 40.3f, 45.8f, 35.6f, 48.4f, 31.6f, 51.4f, 30.3f, 54.2f, 30.9f, 56.6f, 33.6f, 61.3f, 41.6f, 62.4f, 45.6f, 76f, 45.6f, 76.9f, 46.6f, 76.9f, 49.1f, 72.4f, 49.1f)
                .L(62.3f, 49.6f, 59.4f, 52.2f, 58.8f, 58.2f, 58.2f, 63.3f, 52.2f, 63.5f).Arc(49.6f, 63.4f, 2.6f, 0f, 180f).L(44.3f, 60.6f, 43.6f, 55f, 41.4f, 51.8f, 38.6f, 47.5f))
            .RoundRect(31.6f, 40.6f, 8.2f, 7f, 0.6f)
            .Poly(true, 41.6f, 44.7f, 48.6f, 42.4f, 50.1f, 45.8f, 46.4f, 47.7f)
            .Poly(true, 51.4f, 49.4f, 54.4f, 52.4f, 50.8f, 52.6f)
            .Ring(30.5f, 61.2f, 11.2f, 7.1f)
            .Circle(30.5f, 61.2f, 2.4f)
            .Capsule(30.5f, 61.2f, 34.6f, 50.6f, 2.3f)
            .Ring(69.5f, 61.2f, 10.6f, 6.9f)
            .Circle(69.5f, 61.2f, 2.4f)
            .ToString();

        /// <summary>A bicycle (禁14 Fig. E.14): two tyres, the diamond frame, the fork and handlebar, the saddle, the chain wheel with its two cranks and pedals.</summary>
        public static readonly string P_Bicycle = new P_Pen()
            .Ring(28.8f, 54.2f, 10.8f, 8f)
            .Ring(70.2f, 54.2f, 10.8f, 8f)
            .Capsule(37.8f, 37f, 58.2f, 37f, 2.4f)
            .Capsule(37.6f, 43.3f, 49.8f, 52.1f, 2.4f)
            .Capsule(58.3f, 36.2f, 55.6f, 50.4f, 2.4f)
            .Capsule(59.4f, 37.6f, 70.2f, 54.2f, 2.4f)
            .Capsule(57.6f, 54.5f, 70.6f, 54.5f, 2.3f)
            .Stroke(2.6f, 36.3f, 33.6f, 37.4f, 39f, 33.6f, 47.4f, 28.8f, 54.2f)
            .Stroke(2.4f, 36.3f, 33.6f, 34.3f, 28.9f, 43.2f, 27.5f)
            .Poly(55.6f, 29.1f, 64.6f, 28.9f, 65.3f, 30.1f, 64.3f, 31.4f, 62.4f, 32.3f, 60.6f, 32.3f, 58f, 30.8f, 55.8f, 30.5f)
            .Capsule(60.8f, 32f, 59.4f, 37f, 2.2f)
            .Ring(53f, 54.4f, 4.6f, 2.6f)
            .Capsule(52.9f, 50.2f, 52.6f, 46.6f, 2f)
            .Rect(49.8f, 45.4f, 4.2f, 1.7f)
            .Capsule(54.7f, 58.3f, 56.3f, 61.2f, 2f)
            .Rect(54.2f, 60.8f, 4.2f, 1.7f)
            .ToString();

        /// <summary>
        /// An electric bicycle (禁15 Fig. E.15): two tyres, the bent steering tube with its grip, the lamp on the front tyre, the battery frame, the chain wheel with cranks and pedals, the rear
        /// fork and the long seat.
        /// </summary>
        public static readonly string P_EBike = new P_Pen()
            .Ring(27.4f, 53f, 12f, 8.6f)
            .Ring(71.5f, 52.8f, 12f, 8.6f)
            .Capsule(27f, 52.6f, 37.2f, 28.6f, 3.2f)
            .Capsule(37.2f, 28.6f, 40.6f, 25.6f, 3.2f)
            .Rect(40.2f, 24.1f, 5.4f, 3.3f)
            .Poly(20.5f, 38.6f, 26.8f, 37.4f, 26.8f, 40.6f, 21.3f, 41.2f)
            .Poly(36.2f, 31.6f, 45.2f, 31.6f, 49.3f, 38.3f, 46.3f, 46.8f, 48.2f, 51f, 45.5f, 52.6f, 44f, 51.6f, 35.2f, 35.6f)
            .Ring(50f, 53.2f, 4.7f, 2.6f)
            .Capsule(52.6f, 50.3f, 55f, 47.6f, 2.1f)
            .Poly(53.8f, 46.2f, 55.7f, 44.3f, 57.4f, 46f, 55.5f, 47.9f)
            .Capsule(47.6f, 56.1f, 45.4f, 58.8f, 2.1f)
            .Poly(42.9f, 58.6f, 44.8f, 56.7f, 46.6f, 58.5f, 44.7f, 60.4f)
            .Capsule(54.6f, 52.9f, 72.5f, 52.9f, 3.2f)
            .Poly(57.3f, 33.4f, 58.8f, 31.6f, 81f, 31.6f, 81f, 37f, 66.5f, 37f, 63.5f, 38f, 61.5f, 39.7f, 60.3f, 39.5f, 57.3f, 34.6f)
            .ToString();

        /// <summary>A horse pulling a two-wheeled cart (畜力车, 禁16 Fig. E.16): the horse walking left with its four legs and tail, the shaft, the box of the cart over one wheel.</summary>
        public static readonly string P_AnimalCart = new P_Pen()
            .Poly(17.7f, 45.6f, 20f, 43.2f, 21.4f, 40.6f, 22.1f, 40.2f, 22.1f, 39.3f, 22.8f, 38.7f, 24.3f, 40f, 27.8f, 43.1f, 30f, 46.6f, 30.9f, 48f, 32.6f, 49.1f, 39f, 49f, 42.2f, 48f,
                44.8f, 47.8f, 46.6f, 48.6f, 48.4f, 50f, 49.3f, 51.6f, 49.2f, 56.8f, 48.5f, 57.8f, 49.2f, 61.8f, 49.2f, 70.7f, 46.5f, 70.7f, 47.3f, 62.7f, 47.6f, 58.8f, 43.9f, 58.6f,
                44.2f, 63.6f, 41f, 70.9f, 38.6f, 70.9f, 41.7f, 63.6f, 39.5f, 57.8f, 30.6f, 58.3f, 31.7f, 61.9f, 31.4f, 67.2f, 31.2f, 70.7f, 28.8f, 70.7f, 29.9f, 62.8f, 26.9f, 57.9f,
                24.8f, 58.1f, 22.6f, 60f, 23.9f, 67f, 21.8f, 67.8f, 20.1f, 59.9f, 24.3f, 55.6f, 24.3f, 47.2f, 23.2f, 45.5f, 21.4f, 45.6f, 19f, 46.9f)
            .Poly(50.2f, 50.2f, 52.8f, 52f, 53.7f, 54.8f, 54.4f, 59.8f, 51f, 59.9f, 50.9f, 55.6f, 49.6f, 51.4f)
            .Rect(49f, 55.6f, 7f, 2f)
            .Shape(new P_Outline().L(55.2f, 46.6f, 81.5f, 46.6f, 81.5f, 60.4f, 76.5f, 60.4f).Arc(69.8f, 63.6f, 7.4f, -25.6f, -154.4f).L(55.2f, 60.4f))
            .Ring(69.8f, 63.6f, 6.2f, 4.4f)
            .ToString();

        /// <summary>
        /// A motor tricycle for passengers (三轮车, 禁17 Fig. E.17): a scooter front with a tall steering column and grip, the leg shield and floor, the saddle, and behind it a seat with an arm
        /// rest and a back rest over the rear wheel.
        /// </summary>
        public static readonly string P_Tricycle = new P_Pen()
            .Ring(24.9f, 58.7f, 7.9f, 6f)
            .Ring(69.2f, 58.7f, 7.8f, 6f)
            .Capsule(28f, 51.8f, 31.7f, 45.2f, 2.3f)
            .RoundRect(34.9f, 22.6f, 2.3f, 4.2f, 1.1f)
            .Poly(35f, 30.3f, 39.8f, 31.7f, 39.8f, 32.7f, 36.6f, 32.7f)
            .Shape(new P_Outline()
                .L(35.1f, 26f, 36.8f, 26f, 36.6f, 28.8f, 36.9f, 32.5f, 36f, 40f, 37.8f, 42.3f, 37.6f, 44.6f, 36f, 47f, 36.5f, 50f, 38f, 52f, 45.3f, 52f, 48.7f, 44.6f, 51f, 44.8f, 55f, 52.2f,
                    58.8f, 52.2f, 58.8f, 50.4f, 59.4f, 48.5f, 60.6f, 46.9f, 62.6f, 45.4f, 62.7f, 43.3f, 65.4f, 43.3f, 62.7f, 40.4f, 62.6f, 38.6f, 63.6f, 37.6f, 75.4f, 37.2f, 75.8f, 33.2f,
                    76.6f, 32.2f, 79.3f, 31.7f, 80f, 32.5f, 79.1f, 55f, 78.1f, 55f)
                .Arc(69.2f, 58.7f, 9.6f, -22.6f, -161f)
                .L(59.6f, 56.6f, 59f, 56.8f, 46.8f, 57.3f, 45.6f, 58.7f, 43.6f, 58.6f, 42.6f, 57.6f, 40f, 57f, 37.5f, 55.5f, 35f, 52.7f, 32.6f, 48.5f, 32.3f, 46f, 30.3f, 44.8f, 30f, 44f,
                    31.5f, 40.2f, 33.9f, 36f, 32.9f, 34.7f, 30.9f, 33.6f, 31.1f, 32.4f, 34.3f, 29.3f, 35.2f, 28.4f))
            .Poly(true, 65.1f, 38.9f, 75.2f, 38.8f, 75.2f, 43.1f, 67.5f, 43.2f, 64.4f, 40.1f, 64.5f, 39.4f)
            .Rect(49.2f, 41.6f, 1.9f, 3.6f)
            .Poly(45.8f, 40.5f, 46.1f, 39.2f, 54f, 39.4f, 54.8f, 40.3f, 54f, 41.5f, 51.5f, 42f, 49.4f, 42.4f)
            .ToString();

        /// <summary>
        /// A pedal rickshaw for passengers (人力客运三轮车, 禁18 Fig. E.18): the rider (head, body bent forward, the arm to the handlebar, the near leg down to the pedal, the far leg on the other
        /// pedal) on a bicycle front, the chain wheel, and behind him the hooded seat over the rear wheel, the chassis bar running to the rear axle.
        /// </summary>
        public static readonly string P_Rickshaw = new P_Pen()
            .Circle(45.9f, 28.8f, 3.9f)
            .Shape(new P_Outline()
                .L(42.6f, 36.3f).Q(46.8f, 31.4f, 51f, 35.2f).L(54.6f, 43.4f, 55.2f, 46f, 54.8f, 51.8f, 48.3f, 51.9f, 51.9f, 58.5f, 51.6f, 60.4f, 50f, 61.4f, 47.8f, 61.1f, 43.6f, 52.6f,
                    42f, 52.4f, 38f, 52.4f, 38f, 49.4f, 40.3f, 47.4f, 45.6f, 46.6f, 44.8f, 42.7f, 40.8f, 41.4f))
            .Capsule(43.2f, 40.7f, 35.3f, 43.1f, 3.4f)
            .RoundRect(37.9f, 48.5f, 4.1f, 15.2f, 2f)
            .Ring(25.2f, 61.6f, 9.1f, 6.6f)
            .Capsule(25.6f, 61.4f, 33.6f, 47.3f, 1.6f)
            .Poly(31.4f, 43.8f, 33.6f, 43.1f, 34.4f, 46f, 32.8f, 47.3f, 31.6f, 46.6f)
            .Capsule(32.6f, 49.3f, 38.4f, 49.3f, 1.5f)
            .Capsule(31.4f, 52.4f, 38.2f, 56.4f, 1.5f)
            .Ring(44.5f, 61.6f, 2.65f, 1.75f)
            .Capsule(47.2f, 62f, 65.5f, 62.6f, 1.3f)
            .Rect(49.4f, 63.1f, 4.6f, 1.4f)
            .Shape(new P_Outline()
                .L(79.8f, 26.4f, 81.4f, 36f, 81.4f, 40f, 80.6f, 49.6f, 78f, 51.6f, 76.4f, 54.2f, 75.6f, 59.8f)
                .Arc(65.55f, 62.9f, 9.8f, -14f, -176f)
                .L(55.4f, 63.2f, 53.3f, 63.2f, 53.4f, 59f, 54.4f, 56f, 57.2f, 51.2f, 60f, 49f, 63.2f, 47.6f, 65.8f, 47.1f, 68f, 45.8f, 69.6f, 43.2f, 70f, 40.8f, 69.8f, 36.8f, 70.4f, 33.2f,
                    72.8f, 29.2f, 76f, 27.2f))
            .Ring(65.55f, 62.9f, 8.25f, 6.4f)
            .ToString();

        /// <summary>
        /// A pedal tricycle for goods (人力货运三轮车, 禁19 Fig. E.19): the rider sitting upright, his arm and lap over the handlebar, on a bicycle front with the chain wheel; a flat load box behind
        /// him over the rear wheel, the chassis bar to the rear axle.
        /// </summary>
        public static readonly string P_CargoTrike = new P_Pen()
            .Circle(49.6f, 25f, 4.2f)
            .Shape(new P_Outline()
                .L(44.2f, 38.4f, 46.6f, 32.6f).Q(49.8f, 29.4f, 53.4f, 31.2f).L(55.6f, 35f, 57.7f, 40.4f, 59.3f, 45f, 59.6f, 50.8f, 53.6f, 50.8f, 56.3f, 56.8f, 56.2f, 58.6f, 54.9f, 59.8f,
                    53.2f, 59.9f, 48.3f, 53.3f, 47.3f, 53.3f, 47.3f, 47f, 48.5f, 43.7f, 47.4f, 39.5f))
            .Capsule(44.3f, 39.4f, 37.8f, 42.2f, 3.4f)
            .Poly(37.6f, 43.8f, 46.4f, 41.1f, 47.4f, 39.5f, 48.6f, 43.7f, 43.3f, 47f, 39.5f, 47f, 38.2f, 45.8f)
            .RoundRect(42.6f, 46.4f, 4.7f, 16.1f, 2.2f)
            .Ring(29f, 62.1f, 10.1f, 8.05f)
            .Capsule(29.2f, 61.9f, 38.4f, 45.6f, 1.6f)
            .Poly(36.3f, 42.6f, 35.4f, 43.5f, 36.3f, 45.6f, 38.5f, 46.3f, 38.6f, 47.2f, 40f, 47.2f, 39.6f, 44.6f)
            .Capsule(38.8f, 48.8f, 43f, 48.8f, 1.5f)
            .Capsule(37.6f, 50.4f, 43f, 55.6f, 1.5f)
            .Ring(49.6f, 62.1f, 3.3f, 1.9f)
            .Capsule(52.8f, 62.1f, 72.4f, 62.1f, 1.6f)
            .Shape(new P_Outline().L(59.6f, 48.9f, 87.2f, 48.9f, 85.5f, 55.9f, 81.4f, 55.9f).Arc(71.85f, 61.9f, 10.95f, -32f, -148f).L(57.2f, 55.9f, 57.2f, 50.8f, 59.6f, 50.8f))
            .Ring(71.85f, 61.9f, 9.95f, 7.95f)
            .ToString();

        /// <summary>A hand cart (人力车, 禁20 Fig. E.20) seen from the side and tilted as printed: the long rail with three struts, the stand, the big wheel with its hub, the shaft.</summary>
        public static readonly string P_HandCart = new P_Pen()
            .Ring(42.6f, 56.6f, 10.5f, 8f)
            .Circle(42.6f, 56.6f, 3.1f)
            .Poly(18.7f, 50.3f, 55.4f, 36f, 56.3f, 38.3f, 19.6f, 52.6f)
            .Poly(23f, 51.5f, 25.4f, 50.6f, 29.8f, 59.5f, 27.3f, 60.4f)
            .Poly(19.6f, 64.8f, 31.9f, 59.6f, 32.9f, 61.8f, 20.6f, 67f)
            .Poly(32.4f, 48f, 34.7f, 47.2f, 36.6f, 51.4f, 34.2f, 52.2f)
            .Poly(51.4f, 39.8f, 53.9f, 38.9f, 57.6f, 48.6f, 55.2f, 49.6f)
            .Poly(52.6f, 51.4f, 81.6f, 39.4f, 82.3f, 42.1f, 53.4f, 54.8f)
            .ToString();

        /// <summary>A pedestrian (禁21 Fig. E.21): the head, the body, the front arm reaching forward, the back arm hanging, the legs in a stride, ends rounded.</summary>
        public static readonly string P_Walker = new P_Pen()
            .Circle(50f, 26.8f, 5.5f)
            .Shape(new P_Outline()
                .L(44.6f, 37.6f).Q(50f, 32.2f, 55.6f, 37.6f).L(63.9f, 46.7f, 63.9f, 53.8f).Arc(61.95f, 53.8f, 1.95f, 0f, 180f).L(60f, 49.6f, 56.6f, 45.7f, 56.6f, 64.6f, 62.7f, 74.6f)
                .Arc(60.85f, 75.7f, 2.15f, -30f, 150f).L(50.2f, 65f, 50.2f, 57.9f, 44.3f, 65f, 42.4f, 75.4f).Arc(40.15f, 75.1f, 2.25f, 10f, 175f).L(38.6f, 62.4f, 44.3f, 54.4f, 44.3f, 46.9f, 41.5f, 49.6f, 33.4f, 52.5f)
                .Arc(32.8f, 50.4f, 2.2f, 75f, 250f).L(39.1f, 45.9f))
            .ToString();

        // ------------------------------------------------------------------ arrows

        /// <summary>
        /// A straight arrow with the swept-back head of the turning bans (Fig. E.25): the stem centred on cx, halfStem wide each side, from its tail to the notch where the back edges of the head
        /// meet it; the head halfHead wide each side, its sides running on for flat beyond the notch, the tip tipLen further. dir 1 points up (to smaller y), -1 down.
        /// </summary>
        private static P_Outline P_ArrowOutline(float cx, float halfStem, float tail, float notch, float halfHead, float flat, float tipLen, float dir = 1f) => new P_Outline().L(
            cx + halfStem, tail, cx + halfStem, notch, cx + halfHead, notch + dir * (halfHead - halfStem), cx + halfHead, notch - dir * flat, cx, notch - dir * (flat + tipLen),
            cx - halfHead, notch - dir * flat, cx - halfHead, notch + dir * (halfHead - halfStem), cx - halfStem, notch, cx - halfStem, tail);

        /// <summary>
        /// A left turn arrow (Fig. E.23): a stem from the bottom up to a quarter bend round (cx, cy) of inner radius ri and outer radius ro, an arm to the left and the swept-back head (half height
        /// 13.55, as all the turning bans) whose back edges meet the arm at x notch.
        /// </summary>
        private static P_Outline P_TurnLeftOutline(float cx, float cy, float ri, float ro, float bottom, float notch)
        {
            const float half = 13.55f, flat = 3.3f, tip = 14.35f;
            float yc = cy - (ri + ro) * 0.5f, t = ro - ri;
            return new P_Outline()
                .L(cx + ri, bottom, cx + ri, cy).Arc(cx, cy, ri, 0f, -90f)
                .L(notch, cy - ri, notch + half - t * 0.5f, yc + half, notch - flat, yc + half, notch - flat - tip, yc, notch - flat, yc - half, notch + half - t * 0.5f, yc - half, notch, cy - ro)
                .Arc(cx, cy, ro, -90f, 0f).L(cx + ro, bottom);
        }

        private static P_Outline P_MirrorX(P_Outline outline)
        {
            float[] p = outline.Points;
            var m = new P_Outline();
            for (int i = 0; i + 1 < p.Length; i += 2)
                m.L(100f - p[i], p[i + 1]);
            return m;
        }

        /// <summary>The two arrows of 会车让行 (禁3 Fig. E.3), in the box 27.5..73.5 of the sign: the broad one pointing down (drawn black), the thin one pointing up (drawn red).</summary>
        internal static readonly float[] P_OncomingFrame = { 27.5f, 28f, 46f };

        public static readonly string P_OncomingDown = new P_Pen(27.5f, 28f, 46f).Shape(P_ArrowOutline(39.25f, 3.25f, 32f, 58f, 9.5f, 2.5f, 10f, -1f)).ToString();

        public static readonly string P_OncomingUp = new P_Pen(27.5f, 28f, 46f).Shape(P_ArrowOutline(65.75f, 1.75f, 70.5f, 39.4f, 5.25f, 1.9f, 5.5f)).ToString();

        /// <summary>The turn left arrow of 禁23 (Fig. E.23); 禁24 is its mirror.</summary>
        public static readonly string P_TurnLeft = new P_Pen().Shape(P_TurnLeftOutline(49.5f, 50f, 9.7f, 18.6f, 81.2f, 35f)).ToString();

        /// <summary>The straight arrow of 禁25 (Fig. E.25).</summary>
        public static readonly string P_Straight = new P_Pen().Shape(P_ArrowOutline(50f, 4.4f, 85.5f, 31.7f, 13.6f, 3.1f, 14.3f)).ToString();

        /// <summary>The fork to the left and to the right of 禁26 (Fig. E.26): two turn arrows that share their stem.</summary>
        public static readonly string P_TurnBoth = new P_Pen()
            .Shape(P_TurnLeftOutline(36.8f, 58.6f, 8.8f, 17.6f, 85.5f, 32.6f))
            .Shape(P_MirrorX(P_TurnLeftOutline(36.8f, 58.6f, 8.8f, 17.6f, 85.5f, 32.6f)))
            .ToString();

        /// <summary>Straight on and to the left (禁27 Fig. E.27): the straight arrow and a turn arrow branching off its stem low down; 禁28 is the mirror.</summary>
        public static readonly string P_StraightLeft = new P_Pen()
            .Shape(P_ArrowOutline(56.6f, 4.4f, 84.5f, 32.6f, 13.5f, 3.3f, 14.35f))
            .Shape(P_TurnLeftOutline(41.5f, 74.3f, 10.7f, 19.5f, 84.5f, 33.4f))
            .ToString();

        /// <summary>The U turn of 禁29 (Fig. E.29): up the right, round the top, down the left to a swept-back head pointing down.</summary>
        public static readonly string P_UTurn = new P_Pen()
            .Shape(new P_Outline()
                .L(72.4f, 78f, 72.4f, 39f).Arc(54.7f, 39f, 17.7f, 0f, -180f)
                .L(37f, 60.8f, 27.8f, 51.6f, 27.8f, 64.1f, 41.4f, 78.45f, 55f, 64.1f, 55f, 51.6f, 45.8f, 60.8f, 45.8f, 39f)
                .Arc(54.7f, 39f, 8.9f, 180f, 360f).L(63.6f, 78f))
            .ToString();

        /// <summary>
        /// The overtaking manoeuvre of 禁30 and 禁31 (Fig. E.30, E.31): two arrows up in the right lane, the rear one swinging out to the left lane and back in ahead of the front one.
        /// </summary>
        public static readonly string P_Overtaking = new P_Pen()
            .Poly(58.8f, 29.4f, 58.8f, 35.1f, 30f, 51.9f, 30f, 72.8f, 53.3f, 83.3f, 53.3f, 76.7f, 36.2f, 68.8f, 36.2f, 55.4f, 64.3f, 39f, 64.9f, 37.9f, 64.9f, 29.4f, 73.2f, 37.6f,
                73.2f, 28.4f, 61.85f, 16.9f, 50.5f, 28.4f, 50.5f, 37.6f)
            .Shape(P_ArrowOutline(61.85f, 3.05f, 75.3f, 62.3f, 11.35f, 1.1f, 11.6f))
            .ToString();

        /// <summary>The arrow of the parking bans' range (禁32 Fig. 55 c, top): pointing left, in the box 34..67 x 8.3..41.3 of the sign. Its mirror, drawn lower, is the arrow of Fig. 55 a.</summary>
        internal static readonly float[] P_ParkArrowFrame = { 34f, 8.3f, 33f };

        public static readonly string P_ParkArrow = new P_Pen(34f, 8.3f, 33f)
            .Poly(66f, 22.6f, 43f, 22.6f, 49.45f, 16.4f, 43.5f, 16.4f, 35.05f, 24.6f, 43.5f, 32.8f, 49.45f, 32.8f, 43f, 26.6f, 66f, 26.6f)
            .ToString();

        // ------------------------------------------------------------------ other figures

        /// <summary>A horn (禁34 Fig. E.34): the bell at the left, the tube going right, bending down and back, up again and out through the middle to the mouthpiece.</summary>
        public static readonly string P_Horn = new P_Pen()
            .Shape(new P_Outline().L(22.5f, 31.5f).Q(31f, 42.6f, 43f, 43.2f).L(48f, 43.2f, 48f, 47.5f).Q(29f, 48.2f, 22.5f, 60.5f))
            .Rect(44f, 43.2f, 22.6f, 4.3f)
            .ArcBand(66.5f, 53.25f, 5.75f, 10.05f, -90f, 90f)
            .Rect(41.4f, 59f, 25.2f, 4.3f)
            .ArcBand(41.4f, 57.25f, 2f, 6.05f, 90f, 270f)
            .Rect(41.4f, 51.4f, 36.2f, 3.7f)
            .Poly(77.4f, 51.4f, 80.3f, 50.1f, 81.6f, 50.1f, 81.6f, 56.4f, 80.3f, 56.4f, 77.4f, 55.1f)
            .ToString();

        /// <summary>An axle seen from behind (禁38 Fig. E.38): two tall wheels, the hub caps outside them, the axle with the differential in the middle.</summary>
        public static readonly string P_Axle = new P_Pen()
            .RoundRect(26.6f, 56.6f, 7f, 21f, 1.2f)
            .RoundRect(66.4f, 56.6f, 7f, 21f, 1.2f)
            .Circle(29.7f, 66.9f, 4.6f)
            .Circle(70.3f, 66.9f, 4.6f)
            .Rect(33f, 64f, 34f, 5.8f)
            .Circle(50f, 66.9f, 4.7f)
            .ToString();

        /// <summary>The cloud of an explosion (禁42 Fig. E.43), drawn in yellow behind the rays and the car.</summary>
        public static readonly string P_Blast = new P_Pen()
            .Circle(29.6f, 33.8f, 6.5f)
            .Circle(31f, 42.6f, 6.2f)
            .Circle(35.8f, 27.8f, 6f)
            .Circle(46.6f, 24.6f, 6.6f)
            .Circle(57.3f, 24.6f, 5.8f)
            .Circle(66f, 28.8f, 5.5f)
            .Circle(70.6f, 34.3f, 5.4f)
            .Circle(71.4f, 40.8f, 4.6f)
            .Circle(64f, 44.5f, 4.3f)
            .Rect(30f, 27f, 41f, 20f)
            .ToString();

        /// <summary>The black rays, the sparks and the little face of the explosion (禁42 Fig. E.43), drawn over the cloud.</summary>
        public static readonly string P_BlastRays = new P_Pen()
            .Poly(25.9f, 32.8f, 27.6f, 31.4f, 30.3f, 35.6f, 29.2f, 36.3f)
            .Poly(29.8f, 31f, 31.2f, 29.4f, 41.3f, 38.1f, 40.5f, 38.7f)
            .Poly(33.8f, 24.7f, 36.8f, 25f, 41.4f, 34.5f)
            .Poly(41.9f, 21.3f, 44.4f, 21.5f, 45.8f, 29f)
            .Poly(43.3f, 27.1f, 44.8f, 28.6f, 43.8f, 29.5f, 42.3f, 28.4f)
            .Poly(47.8f, 21.5f, 51.1f, 21.6f, 49.2f, 34.8f)
            .Poly(53.6f, 21.9f, 56.4f, 21.9f, 51.7f, 31.4f)
            .Poly(50.1f, 32f, 51.3f, 31.4f, 52.1f, 32.7f, 51.4f, 33.2f)
            .Poly(66f, 24.4f, 67.3f, 25.4f, 56.2f, 32f)
            .Poly(70.9f, 33.2f, 71.6f, 37f, 57.3f, 39.2f)
            .Poly(67.1f, 42.3f, 67.5f, 43.7f, 57.3f, 44.2f)
            .Poly(27.6f, 43.7f, 30.1f, 42f, 38.2f, 44.4f)
            .Poly(31.2f, 38.1f, 32.9f, 36.8f, 40.5f, 42.9f)
            .Poly(46.4f, 37.6f, 47.5f, 36.2f, 48.3f, 37.3f, 47.9f, 38.7f)
            .Poly(50f, 36.2f, 52.5f, 36.7f, 51.4f, 38.7f, 49.4f, 38.1f)
            .ArcBand(49f, 40.6f, 1.3f, 3.6f, 5f, 178f)
            .ToString();
    }
}
