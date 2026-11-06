using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of the auxiliary (chapter 12), notice (chapter 11) and tourist area (chapter 10) signs of GB 5768.2-2022, prefix N_. Redrawn from the printed figures (Fig. 302 to 345):
    /// the coordinates were read off enlarged renders of the figures and put in the 100 x 100 box of the area the sign gives to the figure (for the tourist symbols, the inside of the white
    /// border). Lines that the print draws thinner than 3 units (the cable of 旅5, the fishing line of 旅10, the stick of 旅4) are drawn 3 to 5 units thick, so that they do not vanish.
    /// Figures that face a way face LEFT here; the signs that face right mirror them.
    /// </summary>
    public static partial class Glyphs
    {
        // ------------------------------------------------------------------ helpers (private: not glyphs)

        /// <summary>The part of a circle above the line y = cut (y down: the points with y &lt; cut), as a polygon: an arch cut out of a body whose lower edge is that line.</summary>
        private static float[] CircleAbove(float cx, float cy, float r, float cut)
        {
            var p = new List<float>();
            float dy = cut - cy;
            if (dy >= r)
                dy = r * 0.999f;
            if (dy <= -r)
                return new float[0];
            double a0 = Math.Asin(Math.Max(-1.0, Math.Min(1.0, dy / r)));
            // From the right end of the chord over the top to the left end (angles in the y-down frame: up is -90 degrees).
            double start = a0, end = Math.PI - a0;
            const int steps = 24;
            for (int i = 0; i <= steps; i++)
            {
                double a = start - (start - (end - 2.0 * Math.PI)) * i / steps;
                p.Add(cx + r * (float)Math.Cos(a));
                p.Add(cy + r * (float)Math.Sin(a));
            }

            return p.ToArray();
        }

        /// <summary>A piece of a ring between two angles (degrees, y down: 0 is right, 90 is down, 180 left, 270 up).</summary>
        private static float[] RingPiece(float cx, float cy, float inner, float outer, float a0, float a1, int steps = 12)
        {
            var p = new List<float>();
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + outer * (float)Math.Cos(a));
                p.Add(cy + outer * (float)Math.Sin(a));
            }

            for (int i = steps; i >= 0; i--)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p.Add(cx + inner * (float)Math.Cos(a));
                p.Add(cy + inner * (float)Math.Sin(a));
            }

            return p.ToArray();
        }

        /// <summary>A quadratic curve from p0 through the control point to p2 as a thick line (a cable, a fishing rod).</summary>
        private static PicBuilder Curve(this PicBuilder b, float x0, float y0, float cx, float cy, float x2, float y2, float width, int steps = 10)
        {
            var pts = new List<float>();
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps, u = 1f - t;
                pts.Add(u * u * x0 + 2f * u * t * cx + t * t * x2);
                pts.Add(u * u * y0 + 2f * u * t * cy + t * t * y2);
            }

            return b.Stroke(width, pts.ToArray());
        }

        /// <summary>A wavy band from x0 to x1 between y and y + thickness, its edges going up and down by amp with the given number of waves (the water of 旅10, 旅13, 旅14).</summary>
        private static PicBuilder Waves(this PicBuilder b, float x0, float x1, float y, float thickness, float amp, int waves)
        {
            var top = new List<float>();
            const int perWave = 8;
            int n = waves * perWave;
            for (int i = 0; i <= n; i++)
            {
                float x = x0 + (x1 - x0) * i / n;
                top.Add(x);
                top.Add(y + amp * (float)Math.Sin(2.0 * Math.PI * i / perWave));
            }

            for (int i = n; i >= 0; i--)
            {
                float x = x0 + (x1 - x0) * i / n;
                top.Add(x);
                top.Add(y + thickness + amp * (float)Math.Sin(2.0 * Math.PI * i / perWave));
            }

            return b.Poly(top.ToArray());
        }

        // ------------------------------------------------------------------ vehicles of the auxiliary signs (辅3 to 辅5, 辅20)

        /// <summary>辅3 机动车 (Fig. 327): a car seen from the front: the cabin with its windscreen, the body with two round lamps and two grille slots, the two wheels below.</summary>
        public static readonly string N_CarFront = new PicBuilder()
            .Poly(17.5f, 15.6f, 83.6f, 15.6f, 91f, 40f, 9f, 40f)                       // the cabin, narrower at the top
            .RoundRect(17.5f, 13.5f, 66.1f, 8f, 4f)                                   // the round roof line
            .RoundRect(5.6f, 38f, 88.8f, 36.4f, 6f)                                   // the body
            .Rect(11.7f, 70f, 9.1f, 15.6f).Rect(79.7f, 70f, 9.7f, 15.6f)              // the wheels
            .Poly(true, 21.4f, 23.9f, 78.6f, 23.9f, 81.4f, 37f, 18.6f, 37f)           // the windscreen
            .Circle(13.9f, 50.8f, 5.6f, true).Circle(86.1f, 50.8f, 5.6f, true)        // the lamps
            .Rect(22.2f, 46.5f, 56.4f, 6.5f, true)                                    // the grille
            .Rect(23.6f, 57.2f, 52.8f, 6.1f, true)
            .ToString();

        /// <summary>辅4 货车 (Fig. 328): a lorry facing left: the cab with its window and sloping front, the bed with three loads, two wheels in their arches.</summary>
        public static readonly string N_Truck = new PicBuilder()
            .Poly(18.6f, 23.2f, 33.6f, 23.2f, 35.6f, 25.6f, 35.6f, 44.6f, 94.6f, 44.6f, 94.6f, 64.6f, 8f, 64.6f, 5.6f, 61.6f, 6.6f, 48f, 11.4f, 44.2f)
            .Rect(36.6f, 29.2f, 17.4f, 12.4f).Rect(57f, 29.2f, 17.6f, 12.4f).Rect(77.6f, 29.2f, 17f, 12.4f)   // the loads
            .Poly(true, 21.5f, 27.5f, 31f, 27.5f, 31f, 40.5f, 16.5f, 40.5f)          // the cab window
            .Poly(true, CircleAbove(22.2f, 68.6f, 11f, 64.6f))                        // the wheel arches
            .Poly(true, CircleAbove(70.6f, 68.6f, 11f, 64.6f))
            .Ring(22.2f, 68.6f, 8f, 4.6f).Ring(70.6f, 68.6f, 8f, 4.6f)                // the wheels
            .ToString();

        /// <summary>辅5 (Fig. 329, lower figure): a tractor facing left: the exhaust, the cab with its window, the bonnet, the big rear wheel under its mudguard and the small front wheel.</summary>
        public static readonly string N_Tractor = BuildTractor();

        private static float[] ArcPoints(float cx, float cy, float r, float a0, float a1, int steps = 10)
        {
            var p = new float[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                double a = (a0 + (a1 - a0) * i / steps) * Math.PI / 180.0;
                p[i * 2] = cx + r * (float)Math.Cos(a);
                p[i * 2 + 1] = cy + r * (float)Math.Sin(a);
            }

            return p;
        }

        private static string BuildTractor()
        {
            // The body's outline: the bonnet's top, the mudguard over the rear wheel, the lower edge between the wheels and the arch over the front wheel (in the outline, so that the arch
            // does not reach outside the body).
            var body = new List<float> { 7.4f, 36.5f, 84.7f, 36.5f, 91f, 39f, 96f, 47f, 92f, 50f, 87f, 45f, 76f, 42.5f, 62f, 46f, 55f, 52f, 53f, 62f, 31f, 62f };
            body.AddRange(ArcPoints(15.3f, 74.1f, 16.5f, -46f, -116f));
            body.AddRange(new[] { 7.4f, 58f });
            return new PicBuilder()
                .Rect(18.5f, 18.2f, 4.4f, 20f)                                        // the exhaust
                .Poly(49.4f, 13.8f, 84.7f, 13.8f, 84.7f, 40f, 44.1f, 40f, 44.1f, 37.4f)   // the cab
                .Poly(body.ToArray())                                                 // the bonnet, the body and the mudguard
                .Rect(3.8f, 43f, 4f, 8f)                                              // the front weight
                .Poly(true, 52.9f, 19.7f, 79.6f, 19.7f, 79.6f, 35.9f, 49.4f, 35.9f)  // the cab window
                .Ring(75.9f, 67f, 20.6f, 11.8f)                                       // the rear wheel
                .Ring(15.3f, 74.1f, 13.2f, 7.2f)                                      // the front wheel
                .ToString();
        }

        // ------------------------------------------------------------------ the turn arrows of 辅6 (Fig. 330 f, g)

        /// <summary>The arrow of 辅6 g (Fig. 330): up from the bottom, a round bend, then to the LEFT with a solid head (辅6 f is its mirror).</summary>
        public static string N_TurnLeft => Mirror(s_TurnRight);

        private static readonly string s_TurnRight = new PicBuilder()
            .Rect(13f, 50f, 14f, 42f)                                                 // the stem from below
            .Poly(RingPiece(38f, 50f, 11f, 25f, 180f, 270f))                          // the bend
            .Rect(38f, 25f, 24f, 14f)                                                 // to the right
            .Poly(95f, 32f, 60f, 8f, 60f, 56f)                                        // the head
            .ToString();

        // ------------------------------------------------------------------ the notice signs (chapter 11)

        /// <summary>告示 Fig. 321, 322 (E.247): a surveillance camera facing LEFT: the body tilted down to the front, the hood at its front end, a short mount and the rod up to the ceiling.</summary>
        public static string N_Camera => Mirror(s_CameraRight);

        private static readonly string s_CameraRight = new PicBuilder()
            .Rect(49.4f, 8f, 5f, 37f)                                                 // the rod
            .Rect(41f, 42.5f, 22f, 5.5f)                                              // the mount
            .Poly(17f, 44f, 79f, 58.5f, 73f, 79f, 11f, 64.5f)                         // the body
            .Poly(76f, 59f, 87f, 60f, 82f, 70f, 76.5f, 77f)                           // the hood
            .Poly(84f, 67f, 92f, 66f, 89f, 81f, 81f, 79f)                             // the lens mark
            .ToString();

        /// <summary>告示 Fig. 324 b (E.248) 禁扔弃物: a black ring and bar over a car seen from behind at the left, things thrown from it at the right.</summary>
        public static readonly string N_NoLitter = new PicBuilder()
            .Ring(50f, 50f, 46f, 37f)
            .Poly(20.3f, 26.7f, 26.7f, 20.3f, 79.7f, 73.3f, 73.3f, 79.7f)             // the bar, inside the ring
            .RoundRect(18f, 44f, 38f, 30f, 5f)                                        // the car's body
            .Poly(23f, 46f, 27f, 31f, 47f, 31f, 52f, 46f)                             // its roof and rear window
            .Rect(21f, 52f, 32f, 4f, true).Rect(21f, 60f, 32f, 4f, true)              // the rear's stripes
            .Rect(20f, 73f, 8f, 6f).Rect(46f, 73f, 8f, 6f)                            // the wheels
            .Circle(63f, 44f, 4f).Circle(71f, 53f, 3.5f).Poly(74f, 40f, 81f, 43f, 77f, 48f)   // the litter
            .ToString();

        /// <summary>告示 Fig. 324 a: a mobile phone (the body; the sign puts a green screen into its window): the antenna, the earpiece, the window, twelve keys.</summary>
        public static readonly string N_Phone = BuildPhone();

        private static string BuildPhone()
        {
            var b = new PicBuilder()
                .Rect(58f, 5f, 5f, 18f)                                               // the antenna
                .RoundRect(30f, 20f, 40f, 75f, 6f)                                    // the body
                .Ellipse(50f, 27f, 8f, 3f, true)                                      // the earpiece
                .Rect(36f, 35f, 28f, 18f, true);                                      // the window
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 3; col++)
                    b.Rect(36f + col * 10f, 59f + row * 8.5f, 7f, 4.5f, true);
            }

            return b.ToString();
        }

        /// <summary>告示 Fig. 324 c 系安全带: a seat belt: the strap from the top right down to the buckle, the buckle with its button, the latch plate with its slot, the strap going off to the left.</summary>
        public static readonly string N_SeatBelt = new PicBuilder()
            .Poly(70f, 7.5f, 83.75f, 7.5f, 75f, 53.75f, 61.25f, 50f)                  // the strap from the shoulder
            .Poly(true, 75.5f, 10f, 79f, 10f, 71.5f, 47f, 68.5f, 46f)                 // the stripe along it
            .Poly(48.75f, 51.25f, 67.5f, 45.75f, 75f, 66.25f, 56.25f, 72.5f)          // the buckle
            .Poly(true, 52.5f, 60f, 58.75f, 58.75f, 60f, 66.25f, 54.25f, 67.5f)       // its button
            .Poly(73.75f, 58.75f, 95f, 68.75f, 95f, 80f, 76.25f, 73.75f)              // the strap on the right
            .Poly(13.25f, 76.25f, 45.75f, 58.75f, 54.5f, 80f, 23.25f, 95f)            // the latch plate
            .Poly(true, 32f, 71.25f, 42f, 66.25f, 47f, 80f, 35.75f, 85f)              // its slot
            .Poly(5f, 82f, 14f, 77.5f, 20f, 90f, 8f, 95f)                             // the strap to the left
            .ToString();

        // ------------------------------------------------------------------ the tourist symbols (旅3 to 旅17, Fig. 304 to 318); the box is the inside of the white border

        /// <summary>旅3 信息服务 (Fig. 304): a serif i.</summary>
        public static readonly string N_TourInfo = new PicBuilder()
            .Circle(48.5f, 22f, 8.8f)
            .RoundRect(41.6f, 35.5f, 15.1f, 43f, 1.5f)
            .RoundRect(34.2f, 35.5f, 9f, 4.5f, 1.5f)
            .RoundRect(34.2f, 76f, 29.5f, 5.5f, 1.5f)
            .ToString();

        /// <summary>旅4 徒步 (Fig. 305): a hiker with a rucksack and a stick, facing LEFT (the sign mirrors it).</summary>
        public static string N_Hiker => Mirror(s_HikerRight);

        private static readonly string s_HikerRight = new PicBuilder()
            .Circle(55.3f, 13.9f, 6.5f)
            .Poly(32.5f, 22.7f, 40.5f, 24.4f, 36.3f, 46.8f, 26.4f, 44.7f)            // the rucksack
            .Poly(46.2f, 22.7f, 54.2f, 22.7f, 58f, 34.3f, 49f, 58.4f, 37.8f, 60.5f, 42.6f, 32.1f)   // the body
            .Stroke(6f, 54.2f, 26f, 60.5f, 40.5f, 76.3f, 41.2f)                      // the arm to the stick
            .Rect(74.5f, 38.5f, 4f, 54.6f)                                           // the stick
            .Stroke(9.5f, 46.2f, 57.4f, 58f, 72.1f, 65.1f, 90.5f)                    // the leg in front
            .Stroke(9.5f, 40.5f, 58.4f, 36.3f, 74.2f, 26.9f, 89.5f)                  // the leg behind
            .ToString();

        /// <summary>旅5 索道 (Fig. 306): a cable rising to the right, the two pulleys on it, the hanger and the cabin with its window. The cable is 3 units (thinner in the print).</summary>
        public static readonly string N_CableCar = new PicBuilder()
            .Curve(13.9f, 56.9f, 53.4f, 52.4f, 87.8f, 13.2f, 3f, 16)
            .Ring(52.1f, 43.7f, 4.6f, 1.8f).Ring(52.1f, 52.6f, 4.6f, 1.8f)
            .Rect(50.6f, 56f, 3f, 8f)
            .Rect(33.6f, 63f, 37.4f, 22.7f)
            .Rect(39.1f, 66.8f, 26f, 6.9f, true)
            .ToString();

        /// <summary>旅6 野营地 (Fig. 307): a tent with a bulging back and a triangular door, standing on the ground line; the poles cross at the top.</summary>
        public static readonly string N_Tent = new PicBuilder()
            .Poly(49f, 20.6f, 56f, 33f, 62f, 47f, 68f, 58f, 74.2f, 71f, 78.4f, 82.6f, 24.8f, 82.6f)
            .Poly(true, 48.3f, 53.2f, 58.8f, 79f, 38.4f, 79f)
            .Rect(16f, 82f, 66.6f, 4f)
            .Capsule(46.5f, 13.5f, 49f, 22f, 3f).Capsule(51.5f, 13.5f, 49f, 22f, 3f)
            .ToString();

        /// <summary>旅7 营火 (Fig. 308): a flame between two smaller ones, over two crossed logs.</summary>
        public static readonly string N_Campfire = new PicBuilder()
            .Poly(47f, 6f, 52f, 12f, 58f, 20f, 63f, 29f, 65.5f, 38f, 66f, 47f, 64.5f, 56f, 60f, 64f, 54f, 70f, 49f, 72f, 43f, 70.5f, 38f, 66f, 35.5f, 58f, 35.5f, 49f, 37f, 41f, 39.5f, 33f, 42.5f, 24f, 45f, 16f)
            .Poly(33f, 36f, 28f, 42f, 25f, 49f, 24.5f, 56f, 26.5f, 63f, 31f, 69f, 37f, 73.5f, 42f, 74.5f, 38f, 70f, 34f, 64f, 32f, 57f, 31.5f, 50f, 32f, 43f)
            .Poly(69f, 30f, 74f, 37f, 77.5f, 45f, 78.5f, 53f, 77f, 61f, 73f, 67.5f, 66f, 72.5f, 58f, 74.5f, 62f, 70f, 67f, 64.5f, 69.5f, 57f, 70f, 49f, 69.5f, 41f)
            .Capsule(27f, 78f, 73f, 90f, 8f).Capsule(27f, 90f, 73f, 78f, 8f)
            .ToString();

        /// <summary>旅8 旅居车营地 (Fig. 309): a fir tree at the top left and a caravan facing left (its drawbar at the left) with a door, a window and its wheel.</summary>
        public static readonly string N_Caravan = new PicBuilder()
            .Poly(22.3f, 4.8f, 31.1f, 13.9f, 13.2f, 13.9f).Poly(22.3f, 11.1f, 34.9f, 23.7f, 9f, 23.7f).Poly(22.3f, 20.6f, 39.1f, 35.3f, 5f, 35.3f)
            .Rect(20.1f, 35f, 4.3f, 5.5f)
            .Poly(24.8f, 80.7f, 24.8f, 50f, 26f, 45.5f, 29f, 42.5f, 33f, 41.4f, 86f, 41.4f, 91.5f, 43.5f, 94.5f, 49f, 95.2f, 56f, 95.2f, 71f, 93.5f, 78f, 89f, 82.5f, 83f, 83.5f, 83f, 80.7f)
            .Rect(16.4f, 81.7f, 9f, 4.3f)                                            // the drawbar
            .Rect(34.9f, 51.9f, 14.1f, 25.6f, true)                                  // the door
            .Rect(57.4f, 51.9f, 27.7f, 9.9f, true)                                   // the window
            .Poly(true, CircleAbove(72.1f, 88f, 10.9f, 80.7f))                       // the wheel's arch
            .Circle(72.1f, 88f, 8.2f)
            .ToString();

        /// <summary>旅9 骑马 (Fig. 310): a rider on a walking horse, facing LEFT (the sign mirrors it).</summary>
        public static string N_HorseRider => Mirror(s_RiderRight);

        private static readonly string s_RiderRight = new PicBuilder()
            .Circle(52.1f, 19.5f, 5f)
            .Capsule(47f, 27.5f, 43.5f, 39.5f, 10f)
            .Stroke(5.5f, 49f, 29.5f, 51.5f, 36f, 59.5f, 36f)
            .Capsule(43f, 40f, 52f, 46f, 8f).Capsule(52f, 46f, 44f, 59f, 6.5f)
            .Poly(20.6f, 46.8f, 32f, 45f, 45f, 44f, 57f, 42f, 62f, 40.5f, 70f, 33f, 75.2f, 28f, 77f, 22f, 80f, 26.8f, 89.1f, 28.2f, 87.5f, 33f, 88.5f, 49.5f, 82f, 50.2f, 77f, 42.5f, 74.5f, 52f, 74f, 60.5f, 62f, 64f, 40f, 66f, 26f, 65f, 20.6f, 58.4f, 19.2f, 51f)
            .Poly(19.5f, 48f, 13f, 58f, 7f, 69f, 11.1f, 71.4f, 17f, 63f, 21f, 55f)   // the tail
            .Stroke(7.5f, 72f, 58f, 77.5f, 89.5f).Stroke(7.5f, 66f, 61f, 54.5f, 88.5f)
            .Stroke(7.5f, 32f, 62f, 22.7f, 74.2f, 15.3f, 84.7f).Stroke(7.5f, 39.5f, 63f, 31.5f, 74.5f, 41.6f, 88.5f)
            .ToString();

        /// <summary>旅10 钓鱼 (Fig. 311): an angler sitting on a stool, the rod bent up to the right, the line down to the water, facing LEFT (the sign mirrors it).</summary>
        public static string N_Fishing => Mirror(s_FisherRight);

        private static readonly string s_FisherRight = new PicBuilder()
            .Circle(25.8f, 21.6f, 6.7f)
            .Poly(17f, 30.5f, 29f, 30.5f, 35.5f, 40f, 35.5f, 60f, 11f, 63.6f, 12.5f, 50f, 14.5f, 38f)
            .Stroke(7f, 29f, 36.5f, 51f, 46.5f)
            .RoundRect(10.1f, 56.5f, 30.4f, 9.3f, 2f)
            .Capsule(36f, 61f, 37f, 85f, 8f)
            .Rect(8f, 70f, 21f, 17.4f)                                               // the stool
            .Curve(51f, 46.8f, 66f, 20f, 84f, 6.9f, 4f)                              // the rod
            .Rect(80.2f, 8f, 3f, 66f)                                                // the line
            .Waves(48.3f, 95f, 74f, 4.5f, 1.2f, 3).Waves(48.3f, 95f, 82f, 4.5f, 1.2f, 3)
            .ToString();

        /// <summary>旅11 高尔夫球 (Fig. 312): a golfer at the end of the swing, the club over his shoulders.</summary>
        public static readonly string N_Golf = new PicBuilder()
            .Circle(41f, 23.5f, 5f)
            .Capsule(22.7f, 16.6f, 66f, 16f, 3.2f)
            .Poly(17.4f, 18.1f, 23.1f, 15.5f, 24.2f, 22.3f, 20.6f, 23.5f)
            .Stroke(7f, 47.5f, 33f, 65.8f, 16.5f).Stroke(6f, 55f, 34.5f, 66f, 17f)
            .Capsule(47f, 36f, 51f, 61f, 10.5f)
            .Capsule(49f, 61f, 36.3f, 92.5f, 9f).Capsule(53f, 60f, 68.5f, 93f, 9f)
            .ToString();

        /// <summary>旅12 潜水 (Fig. 313): the water's surface, and a diver with fins and a tank going down to the left.</summary>
        public static readonly string N_Diving = new PicBuilder()
            .Waves(7.9f, 88.8f, 11f, 5.5f, 1.3f, 5)
            .Poly(73.7f, 21.8f, 80.9f, 22.4f, 74.7f, 32.2f, 71.2f, 31.1f)            // the fins
            .Capsule(73f, 35.5f, 60.2f, 53f, 9f)                                     // the legs
            .Capsule(60.2f, 53f, 34f, 68.5f, 13f)                                    // the body
            .Poly(31.1f, 50f, 46.7f, 43.3f, 49.4f, 48.9f, 33.8f, 55.6f)              // the tank
            .Circle(25.9f, 69.5f, 5.5f)
            .Capsule(36f, 69f, 22f, 88f, 7f)                                         // the arm
            .ToString();

        /// <summary>旅13 游泳 (Fig. 314): a swimmer's head and arm over two lines of waves.</summary>
        public static readonly string N_Swim = new PicBuilder()
            .Circle(45.2f, 37.7f, 6.2f)
            .Poly(16.6f, 46f, 73.7f, 47f, 74f, 52f, 16.6f, 52f)
            .Stroke(7f, 50f, 46f, 72.6f, 24.9f, 84f, 36.3f)
            .Waves(5.2f, 93.4f, 55f, 4.5f, 1.3f, 4).Waves(5.2f, 93.4f, 62.5f, 4.5f, 1.3f, 4)
            .ToString();

        /// <summary>旅14 划船 (Fig. 315): a boat cut by an oar, the blade in the waves.</summary>
        public static readonly string N_Boat = new PicBuilder()
            .Poly(9.3f, 36.3f, 41.7f, 36.3f, 48.4f, 52.3f, 17.6f, 52.3f)
            .Poly(51.7f, 36.3f, 91.3f, 36.3f, 90f, 44f, 85.1f, 52.3f, 58.4f, 52.3f)
            .Capsule(36.3f, 11.4f, 57f, 61f, 5f)
            .Poly(55f, 60.2f, 63.3f, 57.1f, 69.5f, 73.7f, 63.3f, 77.8f, 59.1f, 74.7f)
            .Waves(10f, 51f, 57f, 4.5f, 1.2f, 3).Waves(67f, 89.2f, 57f, 4.5f, 1.2f, 2)
            .Waves(10f, 55f, 65.5f, 4.5f, 1.2f, 3).Waves(72f, 89.2f, 65.5f, 4.5f, 1.2f, 1)
            .ToString();

        /// <summary>旅15 冬季游览区 (Fig. 316): a snowflake of six arms, each with an arrow-like tip and two pairs of side branches.</summary>
        public static readonly string N_Snowflake = BuildSnowflake();

        private static string BuildSnowflake()
        {
            var b = new PicBuilder();
            for (int k = 0; k < 6; k++)
            {
                double a = k * Math.PI / 3.0;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a), nx = -dy, ny = dx;
                float P(float r, float s, bool x) => x ? 50f + dx * r + nx * s : 50f + dy * r + ny * s;
                b.Poly(P(0f, -3f, true), P(0f, -3f, false), P(36f, -3f, true), P(36f, -3f, false), P(36f, 3f, true), P(36f, 3f, false), P(0f, 3f, true), P(0f, 3f, false));
                // The tip: a short arrow head.
                b.Poly(P(45f, 0f, true), P(45f, 0f, false), P(37f, 7.5f, true), P(37f, 7.5f, false), P(33f, 4f, true), P(33f, 4f, false), P(33f, -4f, true), P(33f, -4f, false), P(37f, -7.5f, true), P(37f, -7.5f, false));
                // The side branches.
                foreach (float r in new[] { 17f, 26f })
                    b.Capsule(P(r, -7f, true), P(r, -7f, false), P(r, 7f, true), P(r, 7f, false), 4f);
            }

            return b.ToString();
        }

        /// <summary>旅16 滑雪 (Fig. 317): a skier going down to the left, crouched, a pole behind him, on a ski with its tip turned up.</summary>
        public static readonly string N_Ski = new PicBuilder()
            .Circle(27.5f, 19f, 6f)
            .Capsule(42f, 27f, 62f, 36.5f, 13f)
            .Capsule(40f, 38f, 23f, 52f, 7f)
            .Capsule(63f, 37f, 45f, 52f, 11f).Capsule(45f, 52f, 54f, 73.5f, 10f)
            .Capsule(60f, 29.5f, 83f, 16.6f, 3.5f).Capsule(80f, 13f, 85f, 21f, 3.5f)
            .Capsule(26f, 90.5f, 91f, 60.5f, 4.5f).Capsule(23.5f, 86f, 26.5f, 90.5f, 4.5f)
            .ToString();

        /// <summary>旅17 滑冰 (Fig. 318): a skater bent forward to the left, an arm back, one leg standing on its blade and the other stretched out behind.</summary>
        public static readonly string N_Skate = new PicBuilder()
            .Circle(27f, 15.5f, 6.2f)
            .Capsule(38.5f, 26f, 55f, 44.5f, 13f)
            .Capsule(41.5f, 23f, 70.5f, 23.2f, 7f)
            .Stroke(10f, 51f, 47.7f, 39.4f, 58.1f, 41.5f, 82f)
            .Capsule(54f, 48.7f, 81f, 76.8f, 10f)
            .Rect(29.5f, 84.5f, 21f, 3.5f).Rect(29.5f, 81f, 3.5f, 4f)
            .Capsule(75.5f, 86f, 93f, 74.5f, 3.5f).Capsule(73.5f, 82.5f, 75.5f, 86f, 3.5f)
            .ToString();

        // ------------------------------------------------------------------ the representative pictures of the tourist guide boards (旅1, 旅2): examples, the standard leaves them to the designer

        /// <summary>旅1 (Fig. 302): the sun rising behind a range of hills (a simplified redrawing: the print's hills are a free brush drawing).</summary>
        public static readonly string N_Scenery = new PicBuilder()
            .Poly(RingPiece(68f, 44f, 6f, 10f, 180f, 360f))
            .Capsule(55.8f, 39.6f, 50.1f, 37.5f, 3f).Capsule(59.4f, 33.6f, 55.6f, 29f, 3f).Capsule(68f, 31f, 68f, 25f, 3f).Capsule(76.6f, 33.6f, 80.4f, 29f, 3f).Capsule(80.2f, 39.6f, 85.9f, 37.5f, 3f)
            .Poly(5f, 66f, 24f, 60f, 40f, 55f, 54f, 50f, 64f, 46.5f, 72f, 47f, 78f, 50f, 84f, 56f, 88f, 63f, 95f, 72f, 86f, 70f, 76f, 67f, 66f, 69f, 56f, 67f, 44f, 70f, 32f, 73f, 46f, 73f, 36f, 77f, 22f, 74f, 12f, 70f)
            .Poly(true, 24f, 63f, 44f, 58f, 58f, 55f, 46f, 61f)
            .Poly(true, 64f, 58f, 74f, 53f, 78f, 60f, 70f, 62f)
            .ToString();

        /// <summary>旅2 (Fig. 303): a pagoda on a hill at the left and a mountain with a wall coming down it (a simplified redrawing of the print's free drawing).</summary>
        public static readonly string N_Pagoda = BuildPagoda();

        private static string BuildPagoda()
        {
            // The pagoda: the spire, a core and seven eaves widening downwards, on a plinth.
            var b = new PicBuilder().Capsule(22f, 5f, 22f, 12f, 3f).Rect(19f, 10f, 6f, 46f);
            for (int i = 0; i < 7; i++)
            {
                float w = 8f + i * 1.4f, y = 11f + i * 6.2f;
                b.Poly(22f - w * 0.5f - 1f, y + 3.2f, 22f + w * 0.5f + 1f, y + 3.2f, 22f + w * 0.5f - 1f, y, 22f - w * 0.5f + 1f, y);
            }

            b.Rect(15f, 54f, 14f, 7f)
             .Poly(5f, 72f, 12f, 62f, 32f, 60f, 40f, 66f, 30f, 72f, 20f, 76f, 8f, 77f)              // the hill under the pagoda
             .Poly(40f, 64f, 47f, 34f, 53f, 32f, 62f, 48f, 74f, 66f, 90f, 78f, 70f, 79f, 54f, 73f, 44f, 70f);   // the mountain
            // The wall coming down the mountain: a band with its merlons on the upper side.
            float x0 = 36f, y0 = 68f, x1 = 72f, y1 = 90f;
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            dx /= len; dy /= len;
            float nx = dy, ny = -dx;
            b.Poly(x0 - nx * 3f, y0 - ny * 3f, x1 - nx * 3f, y1 - ny * 3f, x1 + nx * 3f, y1 + ny * 3f, x0 + nx * 3f, y0 + ny * 3f);
            for (int k = 0; k < 5; k++)
            {
                float t = 4f + k * 8f, bx = x0 + dx * t + nx * 2.5f, by = y0 + dy * t + ny * 2.5f;
                b.Poly(bx - dx * 2f, by - dy * 2f, bx + dx * 2f, by + dy * 2f, bx + dx * 2f + nx * 4.5f, by + dy * 2f + ny * 4.5f, bx - dx * 2f + nx * 4.5f, by - dy * 2f + ny * 4.5f);
            }

            return b.Poly(6f, 84f, 20f, 79f, 32f, 82f, 26f, 88f, 44f, 93f, 30f, 96f, 14f, 93f).ToString();
        }
    }
}
