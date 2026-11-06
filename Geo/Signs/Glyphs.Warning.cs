using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The pictograms of the warning signs (chapter 7), prefix W_. Each is drawn in the field units of the warning sign it was measured on (see WarningSigns: x 0 to 100 across the base of the
    /// yellow field, y 100 on the base), so that the sign draws it where the printed figure is without a second set of numbers; the glyph here is the same figure moved and scaled to fill
    /// the 100 x 100 box (for the glyph sheet and for other signs). The figures face left as printed.
    /// </summary>
    public static partial class Glyphs
    {
        /// <summary>A path written in any units, moved and scaled evenly so that its bounds are centred in the box with 5 units of room on its longer side.</summary>
        internal static string Boxed(string path)
        {
            List<float[]> contours = GuidePath.Parse(path);
            GuidePath.Bounds(contours, out float x0, out float y0, out float x1, out float y1);
            float s = 90f / Math.Max(1e-3f, Math.Max(x1 - x0, y1 - y0));
            float dx = 50f - (x0 + x1) * 0.5f * s, dy = 50f - (y0 + y1) * 0.5f * s;
            var sb = new StringBuilder();
            foreach (float[] c in contours)
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    sb.Append(i == 0 ? 'M' : 'L').Append((c[i] * s + dx).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                        .Append((c[i + 1] * s + dy).ToString("0.##", CultureInfo.InvariantCulture));
                }

                sb.Append("Z ");
            }

            return sb.ToString();
        }

        /// <summary>警10: a person walking to the left over a zebra crossing (the person only; the stripes are the sign's).</summary>
        public static string W_Walker => Boxed(WarningArt.Walker(0f));

        /// <summary>警11: two children running to the left, the second with a school bag.</summary>
        public static string W_Children => Boxed(WarningArt.Children);

        /// <summary>警12: a person in a wheelchair, facing left.</summary>
        public static string W_Wheelchair => Boxed(WarningArt.Wheelchair);

        /// <summary>警13: a bicycle, facing left.</summary>
        public static string W_Bicycle => Boxed(WarningArt.Bicycle);

        /// <summary>警14: an electric bicycle (a step-through frame, a battery under the saddle), facing left.</summary>
        public static string W_EBike => Boxed(WarningArt.EBike);

        /// <summary>警15: a cow, facing left.</summary>
        public static string W_Cow => Boxed(WarningArt.Cow);

        /// <summary>警16: a deer leaping to the left.</summary>
        public static string W_Deer => Boxed(WarningArt.Deer);

        /// <summary>警19: a windsock on its pole (the light bands of the sock are left out: one colour).</summary>
        public static string W_Windsock => Boxed(WarningArt.Windsock);

        /// <summary>警28 (turned in 警20 and 警22): a car seen from the front.</summary>
        public static string W_CarFront => Boxed(WarningArt.CarFront);

        /// <summary>警23: a fir tree.</summary>
        public static string W_FirTree => Boxed(WarningArt.FirTree);

        /// <summary>警30: a steam locomotive with its smoke, facing left.</summary>
        public static string W_Locomotive => Boxed(WarningArt.Locomotive);

        /// <summary>警36: a man digging with a shovel.</summary>
        public static string W_Worker => Boxed(WarningArt.Worker);

        /// <summary>警44-1: a snowflake.</summary>
        public static string W_Snowflake => Boxed(WarningArt.Snowflake);

        /// <summary>警44-4: a cloud with a lightning bolt.</summary>
        public static string W_Storm => Boxed(WarningArt.Cloud + WarningArt.Lightning);

        /// <summary>警45: a car seen from behind.</summary>
        public static string W_CarRear => Boxed(WarningArt.QueueCar(0f, 0f, 0f));

        /// <summary>警17: the housing of a traffic light with three round holes (the lights are drawn in their colours by the sign).</summary>
        public static string W_SignalHousing => Boxed(WarningArt.SignalHousing);
    }

    /// <summary>The warning pictograms in field units, where they stand in their signs (measured on Fig. 157 to 201 at 3000 px a page; see WarningSigns).</summary>
    internal static class WarningArt
    {
        private static PicBuilder P() => new PicBuilder();

        // ------------------------------------------------------------------ people

        /// <summary>警10 (Fig. 157, E.125): the walker, every part grown by grow (the yellow line round it where it stands on the stripes).</summary>
        internal static string Walker(float grow) => P()
            .Circle(50.4f, 43.8f, 5.2f + grow)
            .Poly(44.2f - grow, 55.5f, 45.5f - grow, 52.5f - grow, 48f, 50.8f - grow, 50.7f, 50.3f - grow, 53.5f, 50.8f - grow, 56f + grow, 52.5f - grow, 57.3f + grow, 55.5f,
                57f + grow, 73f + grow, 44.7f - grow, 72f + grow)
            .Capsule(45.5f, 58f, 33.5f, 65.2f, 4.6f + 2f * grow)
            .Stroke(4.2f + 2f * grow, 56f, 55f, 62.8f, 62.4f, 61.8f, 69.4f)
            .Stroke(5f + 2f * grow, 48f, 71f, 40f, 77.2f, 41.8f, 89.3f)
            .Stroke(5f + 2f * grow, 53.5f, 71f, 55.8f, 79.6f, 60f, 90.2f)
            .ToString();

        /// <summary>警11 (Fig. 158, E.126): a boy in front, a girl with a bag behind him, both running to the left.</summary>
        internal static readonly string Children = P()
            // The boy.
            .Circle(39.1f, 61.1f, 4.1f)
            .Poly(34.1f, 68.6f, 36.5f, 67.3f, 39.5f, 66.8f, 43f, 67.6f, 46.2f, 70.9f, 47.3f, 74.9f, 46.2f, 81.5f, 37.2f, 81.9f, 34.1f, 73.3f)
            .Capsule(35.5f, 71f, 26.6f, 76f, 3.8f)
            .Poly(44f, 81.5f, 48.5f, 81.8f, 48.5f, 86.6f, 44f, 86.2f)
            .Stroke(4.4f, 37.5f, 81f, 29.5f, 85.5f, 29.9f, 94.1f)
            .Stroke(4.4f, 40.5f, 81.5f, 39f, 88f, 46.6f, 94.6f)
            // The girl.
            .Circle(57.1f, 57.7f, 4.1f)
            .Poly(53.6f, 64.2f, 57f, 63.4f, 60.6f, 64.2f, 61f, 70.9f, 64.7f, 78.8f, 51.1f, 78.8f, 53.6f, 70.9f)
            .Capsule(54.5f, 67.5f, 50f, 66.3f, 3.6f)
            .Capsule(60.5f, 66f, 63.5f, 69f, 3f)
            .Poly(62.2f, 66.2f, 71.6f, 68.2f, 69.3f, 74.5f, 61.4f, 72.1f)
            .Stroke(4.4f, 54.5f, 78f, 54.5f, 88.6f)
            .Stroke(4.4f, 59f, 78.5f, 61f, 82.5f, 68.4f, 85.1f)
            .ToString();

        /// <summary>警12 (Fig. 160, E.127): a person sitting in a wheelchair, facing left; the wheel is a ring open where the legs are.</summary>
        internal static readonly string Wheelchair = P()
            .Circle(55.4f, 47.2f, 4.1f)
            .RoundRect(50.2f, 53.2f, 7.8f, 25f, 3.9f)
            .Capsule(53f, 64.5f, 39f, 64.5f, 4f)
            .Poly(56.5f, 72f, 56.5f, 78.3f, 36f, 78.3f, 33f, 72f)
            .Stroke(4.6f, 34.5f, 75.2f, 28f, 92.2f)
            .Arc(54.35f, 78.6f, 14.3f, 4.5f, -175f, 70f, 40)
            .ToString();

        // ------------------------------------------------------------------ bicycles

        /// <summary>警13 (Fig. 161, E.128): a bicycle facing left: two wheels, the frame, the fork and the bar, the saddle, the chain wheel and the pedals.</summary>
        internal static readonly string Bicycle = P()
            .Ring(31.5f, 84.2f, 9.6f, 7.4f)
            .Ring(68.1f, 84.2f, 9.6f, 7.4f)
            .Capsule(39f, 68.6f, 58.6f, 68.6f, 2.4f)
            .Capsule(38.6f, 73.8f, 53.4f, 84.4f, 2.4f)
            .Capsule(58.8f, 66f, 53.4f, 84.4f, 2.4f)
            .Capsule(59f, 68.6f, 68.1f, 84.2f, 2.4f)
            .Capsule(53.4f, 84.4f, 68.1f, 84.2f, 2.4f)
            .Capsule(31.5f, 84.2f, 38.6f, 74f, 2.4f)
            .Stroke(2.4f, 38.6f, 74f, 38.6f, 66f, 37.5f, 62.4f)
            .Capsule(36.5f, 62f, 44f, 61.2f, 2.2f)
            .RoundRect(54.6f, 63f, 8.2f, 1.9f, 0.9f)
            .Ring(53.4f, 84.4f, 4.6f, 2.3f)
            .Capsule(53.4f, 84.4f, 51.7f, 77.8f, 1.8f)
            .Rect(50.4f, 77f, 2.8f, 1.4f)
            .Capsule(53.4f, 84.4f, 55.1f, 90.6f, 1.8f)
            .Rect(53.8f, 90.2f, 2.8f, 1.4f)
            .ToString();

        /// <summary>警14 (Fig. 162, E.129): an electric bicycle facing left: thick wheels, a frame curving down from the bar, the battery box, a long saddle, the motor hub and the swing arm.</summary>
        internal static readonly string EBike = P()
            .Ring(26.95f, 81.65f, 10.8f, 7f)
            .Ring(67.95f, 81.65f, 10.8f, 7f)
            .Capsule(25.5f, 80f, 37.5f, 57.5f, 3.6f)
            .Stroke(3.2f, 37.5f, 57.5f, 39.5f, 55.6f, 43.8f, 55.6f)
            .Poly(34.6f, 66.5f, 37.3f, 64f, 44.6f, 64f, 47.3f, 70.5f, 45f, 78f, 49.2f, 82.7f, 46f, 84f, 37f, 66.5f)
            .Capsule(49.2f, 82.4f, 69.5f, 82.4f, 3.4f)
            .Ring(49.2f, 82.7f, 3.9f, 2f)
            .Capsule(49.2f, 82.7f, 53.6f, 78f, 1.8f)
            .Capsule(49.2f, 82.7f, 45.2f, 88.4f, 1.8f)
            .Poly(55.5f, 63.4f, 76.2f, 63.4f, 76.2f, 67.8f, 57.8f, 70.8f, 54.7f, 65.3f)
            .Poly(21.7f, 72f, 22.6f, 69.4f, 25.3f, 68.6f, 27.6f, 70.2f, 27.6f, 72f)
            .ToString();

        // ------------------------------------------------------------------ animals

        /// <summary>警15 (Fig. 163, E.130): a cow facing left: the head with a horn, the body, two pairs of legs, the tail.</summary>
        internal static readonly string Cow = P()
            .Poly(20.4f, 73.3f, 22f, 71f, 24.6f, 69.2f, 25.6f, 67.4f, 24.6f, 64.8f, 32.6f, 63.9f, 34.6f, 65f, 36.7f, 66.3f, 36.5f, 67.9f,
                48.3f, 69f, 55f, 70f, 63.3f, 69.3f, 77.5f, 69f, 78.4f, 71.5f, 78.3f, 78f, 77.2f, 83f, 77f, 88f,
                73.3f, 89.2f, 70f, 90f, 63f, 89f, 55.8f, 88.3f, 45f, 87.5f, 35f, 86.7f, 33.5f, 82f, 30.8f, 77.1f, 24.2f, 75.4f, 21f, 74.6f)
            .Poly(37.9f, 86f, 43.3f, 86f, 42.6f, 95.5f, 43.8f, 97f, 40.6f, 97.3f, 40.4f, 92f, 39.6f, 97.3f, 37f, 97.3f, 38.2f, 95.5f)
            .Poly(70.4f, 88.5f, 77f, 86f, 77.6f, 95.5f, 78.3f, 97.2f, 75.2f, 97.3f, 74.6f, 92f, 73.6f, 95.5f, 74.2f, 97.3f, 71.2f, 97.3f, 72.2f, 95f, 71.6f, 90f)
            .Stroke(1.2f, 78.3f, 71f, 79.4f, 80f, 79.6f, 86f)
            .Capsule(79.6f, 85f, 79.8f, 89f, 2f)
            .ToString();

        /// <summary>警16 (Fig. 164, E.131): a deer leaping to the left: antlers, the head and neck up, the forelegs tucked, the hind legs stretched back.</summary>
        internal static readonly string Deer = P()
            .Poly(39.3f, 48.9f, 40.2f, 47.9f, 41.7f, 47.5f, 45.5f, 47.6f, 47.3f, 45.6f, 50.4f, 45.8f, 49.3f, 47.4f, 48.7f, 59.9f,
                56f, 64.5f, 63f, 67.5f, 69.8f, 70.2f, 71.4f, 70.75f, 74.6f, 70.6f,
                72.8f, 73.5f, 73.4f, 79f, 76.6f, 82.5f, 78.4f, 88f, 79.5f, 94.5f, 80.3f, 97.3f, 78.9f, 97.6f, 77.2f, 93.5f, 75.4f, 88f, 71.6f, 84.6f, 68.8f, 82.2f, 65.5f, 77.8f,
                57f, 76f, 50.4f, 74f, 45.5f, 72f, 43.5f, 72.5f, 39.8f, 71.6f, 38.5f, 68.4f, 37.2f, 67.4f, 36.1f, 68.6f, 36.9f, 75.7f, 40.4f, 77.4f, 40.4f, 78.9f, 38.4f, 79.3f, 35.3f, 77.2f,
                34.4f, 70f, 33.9f, 66.4f, 36.3f, 65.6f, 42.6f, 66.4f, 43.4f, 62f, 42f, 50.9f, 40f, 50f)
            .Stroke(1.6f, 47.3f, 45.6f, 46.3f, 41f, 45.4f, 36f)
            .Stroke(1.6f, 46.3f, 41.5f, 44.2f, 39f, 43.8f, 36.3f)
            .Stroke(1.6f, 47.1f, 43.5f, 50.6f, 40.5f, 51.2f, 37f)
            .Stroke(1.6f, 50.4f, 45.8f, 52.4f, 45.2f)
            .Stroke(1.5f, 74.4f, 70.4f, 76.6f, 69f, 76.4f, 66.2f)
            .ToString();

        // ------------------------------------------------------------------ things

        /// <summary>警17 (Fig. 165, E.132): a traffic light's housing, a rounded column with three holes for the lights.</summary>
        internal static readonly string SignalHousing = P()
            .RoundRect(41.4f, 38.8f, 16.7f, 58.8f, 8.35f)
            .Circle(49.75f, 47.1f, 7.3f, true)
            .Circle(49.75f, 68.8f, 7.3f, true)
            .Circle(49.75f, 89.7f, 7.3f, true)
            .ToString();

        /// <summary>A path moved: the point (fx, fy) goes to (tx, ty), the rest scaled by s and turned by degrees (clockwise as seen) round it.</summary>
        internal static string Place(string path, float fx, float fy, float tx, float ty, float s, float degrees)
        {
            double a = degrees * Math.PI / 180.0;
            float c = (float)Math.Cos(a) * s, sn = (float)Math.Sin(a) * s;
            var sb = new StringBuilder();
            foreach (float[] contour in GuidePath.Parse(path))
            {
                for (int i = 0; i + 1 < contour.Length; i += 2)
                {
                    float x = contour[i] - fx, y = contour[i + 1] - fy;
                    sb.Append(i == 0 ? 'M' : 'L').Append((tx + x * c - y * sn).ToString("0.##", CultureInfo.InvariantCulture)).Append(' ')
                        .Append((ty + x * sn + y * c).ToString("0.##", CultureInfo.InvariantCulture));
                }

                sb.Append("Z ");
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ vehicles

        /// <summary>The middle of the car's headlights in CarFront (field units of 警28): the point a placed car is turned round.</summary>
        internal const float CarX = 51f, CarY = 73.6f;

        /// <summary>
        /// 警28 (Fig. 177, E.146), and turned and scaled in 警20 and 警22: a car seen from the front: the cabin with its windscreen, the body with two headlights, the grille and the bumper
        /// as slots, the two wheels below.
        /// </summary>
        internal static readonly string CarFront = P()
            .Poly(40.4f, 70f, 40.6f, 66.5f, 42f, 64.4f, 46f, 63.1f, 50.3f, 62.7f, 54.6f, 63.1f, 58.6f, 64.4f, 60f, 66.5f, 61.5f, 70f)
            .Poly(true, 42.3f, 69.6f, 42.5f, 66.9f, 43.6f, 65.6f, 47f, 64.8f, 50.3f, 64.6f, 53.6f, 64.8f, 57f, 65.6f, 58.1f, 66.9f, 58.4f, 69.6f)
            .Poly(40.4f, 69.3f, 61.5f, 69.3f, 63.6f, 71f, 64.5f, 72.5f, 64.5f, 79.5f, 63f, 82f, 39f, 82f, 37.6f, 79.5f, 37.6f, 72.5f, 38.5f, 71f)
            .Circle(40.4f, 73.6f, 1.5f, true)
            .Circle(61.6f, 73.6f, 1.5f, true)
            .Ellipse(51f, 73.7f, 7.9f, 0.95f, true)
            .Rect(43.6f, 76f, 15.2f, 1.3f, true)
            .RoundRect(39.8f, 81f, 2.6f, 3.7f, 1.1f)
            .RoundRect(59.6f, 81f, 2.6f, 3.7f, 1.1f)
            .ToString();

        /// <summary>警20 (Fig. 168, E.136): the car of 警28 smaller and turned, sliding.</summary>
        internal static readonly string CarSkidding = Place(CarFront, CarX, CarY, 51f, 56f, 0.72f, 16.7f);

        /// <summary>警22 (Fig. 170, E.139): the car of 警28 turned, going off the bank.</summary>
        internal static readonly string CarFalling = Place(CarFront, CarX, CarY, 49.75f, 56.1f, 1.09f, 16.6f);

        /// <summary>
        /// 警33 (Fig. 183, E.149): a tractor (seen from the side, facing right: the cab of two blocks, the body) that a car has run into, without their wheels. The sign draws over it the yellow
        /// lines round the wheels (AccidentGaps), the wheels (AccidentWheels) and the white parts (AccidentWhite: the burst, the car's windows, the hubs).
        /// </summary>
        internal static readonly string Accident = P()
            .Poly(24.1f, 60.1f, 29.6f, 60.1f, 29.6f, 70.4f, 18.5f, 70.4f)
            .Rect(31.1f, 60.1f, 15.7f, 10.3f)
            .Poly(17.4f, 72.3f, 46.8f, 72.3f, 46.8f, 86.1f, 9.5f, 86.1f)
            .Poly(43.3f, 92.1f, 47f, 87.5f, 56.5f, 84.5f, 60.1f, 80.9f, 74.8f, 78.6f, 78.1f, 83.1f, 88.3f, 82.8f, 90.5f, 85.75f, 83f, 89.5f, 60f, 92.5f, 50f, 93.7f)
            .Poly(49.6f, 76.6f, 52.1f, 67.4f, 51f, 76.8f)
            .Poly(55f, 79.8f, 61.8f, 72.4f, 55.8f, 80.6f)
            .Poly(45f, 86.4f, 37.6f, 90.3f, 45.4f, 87.4f)
            .Poly(48.4f, 31.4f, 50.4f, 31.4f, 49.8f, 45.7f, 49f, 45.7f)
            .Circle(49.4f, 49.3f, 1.5f)
            .ToString();

        /// <summary>警33: the yellow lines round the wheels (where a wheel stands on a body).</summary>
        internal static readonly string AccidentGaps = P().Circle(26f, 89.5f, 9.9f).Circle(53.4f, 94.2f, 4.5f).Circle(78.5f, 90.9f, 4.7f).ToString();

        /// <summary>警33: the three wheels.</summary>
        internal static readonly string AccidentWheels = P().Circle(26f, 89.5f, 9f).Circle(53.4f, 94.2f, 3.5f).Circle(78.5f, 90.9f, 3.7f).ToString();

        /// <summary>警33: the white of the accident: the burst between the two vehicles, the car's windows and the hubs of the wheels.</summary>
        internal static readonly string AccidentWhite = P()
            .Circle(51f, 83f, 5.4f).Circle(47.5f, 85.5f, 3.6f).Circle(54.5f, 86.5f, 3.8f).Circle(50.5f, 88f, 3.2f).Circle(48f, 80.5f, 3f).Circle(55.5f, 81f, 3f)
            .Poly(64.5f, 82.6f, 70.6f, 81.6f, 70.8f, 85.3f, 65.6f, 86.3f)
            .Poly(71.6f, 81.4f, 77f, 80.6f, 78.6f, 84.4f, 72.2f, 85.2f)
            .Circle(26f, 89.5f, 3.4f)
            .Circle(53.4f, 94.2f, 1.4f)
            .Circle(78.5f, 90.9f, 1.4f)
            .ToString();

        /// <summary>警30 (Fig. 179, E.148): a steam locomotive facing left with its smoke: the boiler and the cab (its window a notch), the chimney and the dome, the buffer beam, the wheels.</summary>
        internal static readonly string Locomotive = P()
            .Rect(23.6f, 72.6f, 51.6f, 15.7f)
            .Rect(65.1f, 69.8f, 10.1f, 3.1f)
            .Rect(68.2f, 72.9f, 7f, 3.35f, true)
            .Poly(28.7f, 69.9f, 35.5f, 69.9f, 33.6f, 72.8f, 30.3f, 72.8f)
            .Poly(52.6f, 69.9f, 56.8f, 69.9f, 57.8f, 72.8f, 51.4f, 72.8f)
            .Rect(20.4f, 84f, 3.4f, 4.3f)
            .Circle(23.6f, 91.9f, 3.2f)
            .Circle(31.2f, 91.6f, 3.8f)
            .Circle(41.7f, 88.75f, 6.2f)
            .Circle(54.6f, 88.75f, 6.2f)
            .Circle(67.5f, 88.75f, 6.2f)
            .Stroke(2.4f, 32.2f, 70f, 32f, 65.5f, 34f, 62.5f, 38f, 61f)
            .Poly(33.5f, 63.5f, 36f, 60.2f, 41.5f, 58.3f, 44f, 57.5f, 47.2f, 54.8f, 51.5f, 52.3f, 55f, 53.6f, 59f, 54.2f, 62.5f, 51.6f, 65.5f, 51.5f, 67.6f, 53.5f, 67.4f, 56.6f,
                65f, 58.5f, 61f, 58f, 57.8f, 60.3f, 54.5f, 62.6f, 50.5f, 61.7f, 46.5f, 62.4f, 43f, 64.4f, 39f, 63.4f, 36.5f, 64.4f)
            .ToString();

        // ------------------------------------------------------------------ people at work, the windsock

        /// <summary>警36 (Fig. 186, E.154): a man digging with a shovel at a heap, facing right; the heap and the ground are the sign's.</summary>
        internal static readonly string Worker = P()
            .Circle(49.1f, 50.15f, 5.1f)
            .Poly(40.6f, 53.35f, 35.34f, 54.5f, 29.3f, 57.4f, 29f, 59f, 33.24f, 66.07f, 23.25f, 87.6f, 23.5f, 91f, 28f, 91f, 36.13f, 72.9f, 37.97f, 74.2f, 40.07f, 77.1f,
                40.07f, 91f, 45.3f, 91f, 46.38f, 77.6f, 45.33f, 74.2f, 42.43f, 71.3f, 43.75f, 68.7f, 45.85f, 65f, 48.48f, 71.06f, 52.16f, 71.85f, 53.2f, 70.3f, 50.58f, 65.5f,
                49.53f, 60.8f, 47.95f, 56.6f, 46.38f, 55.3f, 43.75f, 53.7f)
            .Poly(true, 34.8f, 59.8f, 37.7f, 58.2f, 35.6f, 61.9f)
            .Capsule(42.7f, 70.5f, 59.5f, 76.3f, 1.7f)
            .Poly(58.7f, 77.6f, 61.1f, 75.5f, 63.7f, 76.05f, 62.9f, 83.15f, 59f, 80.8f)
            .ToString();

        /// <summary>警19 (Fig. 167, E.135): a windsock on its pole, blowing to the right: the sock with two light bands is SockLight (yellow, over it).</summary>
        internal static readonly string Windsock = P()
            .Rect(31.1f, 51.9f, 4.3f, 45f)
            .Poly(31.1f, 51.9f, 47.7f, 50.4f, 77.4f, 66.4f, 72.9f, 74.3f, 40.4f, 63.9f, 35.4f, 56.5f)
            .Circle(72.9f, 69.5f, 4.8f)
            .ToString();

        /// <summary>警19: the light parts of the sock (the field's yellow): the two bands, the hole of the ring at the end and the triangle at the pole.</summary>
        internal static readonly string SockLight = P()
            .Poly(46f, 61.5f, 45.4f, 58f, 47.2f, 55.6f, 50.3f, 53.9f, 59.4f, 58.7f, 57.6f, 60.8f, 56.6f, 64f, 55.5f, 66.9f, 45.1f, 63.9f)
            .Poly(61f, 66f, 61.6f, 62.8f, 63.8f, 60.8f, 71.1f, 64.7f, 69.6f, 66.5f, 68.5f, 71.2f, 60.3f, 68.6f)
            .Circle(72.9f, 69.5f, 1.7f)
            .Poly(37.3f, 53.9f, 40.8f, 53.9f, 38.8f, 57.4f)
            .ToString();

        // ------------------------------------------------------------------ weather (警44, yellow on black)

        /// <summary>警44-1 (Fig. 197 a): a snowflake of six arms with forked ends.</summary>
        internal static readonly string Snowflake = SnowflakePath();

        private static string SnowflakePath()
        {
            var b = P();
            const float cx = 50f, cy = 73.3f;
            // The printed flake is wider than it is high: the four side arms are 22.5 degrees from the level (not 30) and a little longer than the upright ones.
            foreach (double degrees in new[] { 90.0, 22.5, -22.5, -90.0, 157.5, 202.5 })
            {
                double a = degrees * Math.PI / 180.0;
                bool upright = Math.Abs(Math.Abs(degrees) - 90.0) < 1.0;
                float dx = (float)Math.Cos(a), dy = -(float)Math.Sin(a), length = upright ? 18f : 19.5f, at = upright ? 12.5f : 13.5f;
                b.Capsule(cx, cy, cx + dx * length, cy + dy * length, 3.4f);
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    double ba = a + side * 55.0 * Math.PI / 180.0;
                    float sx = cx + dx * at, sy = cy + dy * at;
                    b.Capsule(sx, sy, sx + 7f * (float)Math.Cos(ba), sy - 7f * (float)Math.Sin(ba), 3.2f);
                }
            }

            return b.ToString();
        }

        /// <summary>The cloud of 警44-2 and 警44-4 (Fig. 197 b, d): lobes of circles on a flat bottom scalloped with small round bumps.</summary>
        internal static readonly string Cloud = CloudPath();

        private static string CloudPath()
        {
            var b = P().Circle(34f, 58.4f, 4.5f).Circle(41f, 55f, 5f).Circle(49.5f, 50.5f, 6.8f).Circle(58.5f, 52.5f, 5.5f).Circle(65f, 58.5f, 4f).Circle(30.5f, 60.5f, 2.2f)
                .Circle(67.3f, 60.6f, 2f).Rect(30.5f, 56f, 36.8f, 5.6f);
            for (float u = 31.6f; u < 67f; u += 2.6f)
                b.Circle(u, 61.4f, 1.2f);
            return b.ToString();
        }

        /// <summary>警44-2: the rain (or snow) under the cloud: short slanting rows of drops.</summary>
        internal static readonly string Rain = RainPath();

        private static string RainPath()
        {
            var b = P();
            float[] starts = { 29.7f, 64.3f, 3f, 43.1f, 64.8f, 2f, 58.3f, 65.6f, 2f, 51.3f, 68.6f, 3f, 68f, 68.6f, 3f, 25.7f, 78.9f, 3f, 35.4f, 80.4f, 3f, 45.3f, 76.7f, 3f, 60.4f, 77.4f, 3f,
                74.3f, 82.9f, 3f, 54.7f, 86.4f, 3f, 44.3f, 90f, 2f, 67.7f, 89.7f, 2f };
            for (int i = 0; i + 2 < starts.Length; i += 3)
            {
                for (int k = 0; k < (int)starts[i + 2]; k++)
                    b.Circle(starts[i] + 0.85f * k, starts[i + 1] + 2.55f * k, 1.05f);
            }

            return b.ToString();
        }

        /// <summary>警44-4 (Fig. 197 d): the lightning under the cloud, a bolt of three zigzags.</summary>
        internal static readonly string Lightning = P()
            .Poly(59.07f, 64.27f, 52.15f, 73.33f, 59.6f, 73.4f, 50.73f, 80.1f, 55.4f, 80.2f, 36.5f, 94.5f, 46.7f, 82.9f, 38.9f, 83f, 50.2f, 75.5f, 43.8f, 75.45f)
            .ToString();

        /// <summary>
        /// 警45 (Fig. 198): a car seen from behind, its middle shifted by (du, dv) from the front one of the queue, every part grown by grow (the black line round a car where it stands
        /// in front of another); the window is a hole when not grown.
        /// </summary>
        internal static string QueueCar(float du, float dv, float grow)
        {
            var b = P()
                .Poly(55f + du - grow, 71.9f + dv - grow, 68.6f + du + grow, 71.9f + dv - grow, 72.7f + du + grow, 79.3f + dv, 51.5f + du - grow, 79.3f + dv)
                .RoundRect(50.3f + du - grow, 78.6f + dv - grow, 23.6f + 2f * grow, 7.1f + 2f * grow, 1.2f + grow)
                .RoundRect(52.1f + du - grow, 84f + dv, 2.2f + 2f * grow, 6.3f + grow, 1f)
                .RoundRect(69.7f + du - grow, 84f + dv, 2.2f + 2f * grow, 6.3f + grow, 1f);
            if (grow <= 0f)
                b.Poly(true, 55.8f + du, 73f + dv, 67.8f + du, 73f + dv, 70.9f + du, 78.4f + dv, 52.7f + du, 78.4f + dv);
            return b.ToString();
        }

        // ------------------------------------------------------------------ settlements, structures

        /// <summary>警23 (Fig. 171, E.141): the fir tree beside the house (the house and the ground are masses of the sign).</summary>
        internal static readonly string FirTree = P()
            .Poly(57.6f, 44.6f, 67.1f, 67.6f, 64.9f, 67.6f, 67.6f, 72.1f, 65.2f, 72.1f, 67.9f, 78.2f, 58.4f, 78.2f, 58.4f, 92.5f, 55f, 92.5f, 55f, 78.2f, 46.4f, 78.2f, 49.1f, 72.1f,
                46.9f, 72.1f, 49.6f, 67.6f, 47.3f, 67.6f)
            .ToString();
    }
}
