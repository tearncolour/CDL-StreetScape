using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The width-transition markings of GB 5768.3-2025 7.1 (路面（车行道）宽度渐变段标线): where the road narrows or loses a lane, or gets wider, the lines move sideways over a length of road. 7.1.2: a widening
    /// takes 20 m to 30 m; a narrowing or a lost lane takes L by formula (1), L = V2 W / 155 for V up to 60 km/h and 0.625 V W above 60 (V the design speed in km/h, W the change of width in m), but not
    /// less than Table 4's minimum for the speed, and the line goes on straight past the end of it, 40 m at 60 km/h or more and 20 m otherwise. Fig. 95 to Fig. 97 draw the line moving over the length in a smooth
    /// S, which is read here as the smoothstep curve (zero slope at both ends). Pure.
    /// </summary>
    public static class MarkingTransition
    {
        /// <summary>Table 4 (渐变段长度最小值): the design speeds and the least lengths (m); over 80 km/h it is 100 m.</summary>
        public static readonly float[] TableSpeeds = { 20f, 30f, 40f, 50f, 60f, 70f, 80f };

        public static readonly float[] TableLengths = { 20f, 25f, 30f, 35f, 40f, 70f, 85f };

        public const float TableLengthAbove80 = 100f;

        /// <summary>A widening is 20 to 30 m long (7.1.2); the middle when the player does not say.</summary>
        public const float WideningMin = 20f, WideningMax = 30f, WideningDefault = 25f;

        /// <summary>Table 4's least length for a design speed: that of the first speed in the table that is at least the design speed (a speed between two is the higher), 100 m above 80.</summary>
        public static float TableMinimum(float kmh)
        {
            if (float.IsNaN(kmh))
                return TableLengths[0];
            for (int i = 0; i < TableSpeeds.Length; i++)
            {
                if (kmh <= TableSpeeds[i])
                    return TableLengths[i];
            }

            return TableLengthAbove80;
        }

        /// <summary>Formula (1): the length (m) from the design speed (km/h) and the change of width (m).</summary>
        public static float Formula(float kmh, float width)
        {
            if (float.IsNaN(kmh) || float.IsNaN(width) || kmh < 0f || width < 0f)
                return 0f;
            return kmh <= 60f ? kmh * kmh * width / 155f : 0.625f * kmh * width;
        }

        /// <summary>The length of the transition (m): for a narrowing the formula's, and not less than Table 4's; for a widening 20 to 30 m (the length asked for, kept inside that).</summary>
        public static float Length(float kmh, float width, bool narrowing, float widening = WideningDefault)
        {
            if (!narrowing)
                return Math.Max(WideningMin, Math.Min(WideningMax, float.IsNaN(widening) ? WideningDefault : widening));
            return Math.Max(Formula(kmh, width), TableMinimum(kmh));
        }

        /// <summary>How far the line goes on straight past the end of a narrowing (m): 40 at 60 km/h and over, else 20; nothing for a widening.</summary>
        public static float Extension(float kmh, bool narrowing) => !narrowing ? 0f : kmh >= 60f ? 40f : 20f;

        /// <summary>The smooth S of the transition: 0 at its start, 1 at its end, with no slope at either.</summary>
        public static float Smooth(float t) => t <= 0f ? 0f : t >= 1f ? 1f : t * t * (3f - 2f * t);

        /// <summary>
        /// The line of the transition: from the start (x = 0, z = 0) over length L it moves sideways by shift (to the right of the traffic when toRight, else to the left) in the S, then goes on straight for the
        /// extension. The points are every step metres (and one at each end of the S).
        /// </summary>
        public static List<Vec3> Centre(float kmh, float shift, bool narrowing, bool toRight, float widening = WideningDefault, float step = 1f)
        {
            var points = new List<Vec3>();
            if (float.IsNaN(shift) || float.IsInfinity(shift) || shift <= 0f || !(step > 0.05f))
                return points;
            float l = Length(kmh, shift, narrowing, widening), extension = Extension(kmh, narrowing), sign = toRight ? 1f : -1f;
            int n = Math.Max(2, (int)Math.Ceiling(l / step));
            for (int i = 0; i <= n; i++)
            {
                float z = l * i / n;
                points.Add(new Vec3(sign * shift * Smooth(z / l), 0f, z));
            }

            if (extension > 0f)
            {
                int m = Math.Max(1, (int)Math.Ceiling(extension / Math.Max(step, 2f)));
                for (int i = 1; i <= m; i++)
                    points.Add(new Vec3(sign * shift, 0f, l + extension * i / m));
            }

            return points;
        }

        /// <summary>The line as paint: kind "edge" (white solid, 15 wide), "change" (white dashed 1 m on 1 m off, the lane-change line 线9) or "yellow" (yellow solid, 15 wide: a centre line that moves).</summary>
        public static MarkingShape Line(float kmh, float shift, bool narrowing, bool toRight, string kind, float widening = WideningDefault)
        {
            List<Vec3> centre = Centre(kmh, shift, narrowing, toRight, widening);
            if (centre.Count < 2)
                return new MarkingShape { Colour = MarkingColour.White };
            if (kind == "change")
                return MarkingLines.LaneChangeLine(centre);
            return MarkingLines.Line(centre, LineSide.Solid(0.15f, kind == "yellow" ? MarkingColour.Yellow : MarkingColour.White));
        }
    }
}
