using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The eleven guide arrows of GB 5768.3-2025 Table 1 (5.13.3), in the order of the table.</summary>
    public enum ArrowKind
    {
        Straight,
        StraightLeft,
        Left,
        StraightRight,
        Right,
        UTurn,
        StraightUTurn,
        LeftUTurn,
        LeftRight,
        MergeLeft,
        MergeRight
    }

    /// <summary>The four sizes of 5.13.4: 线30 (Fig. 42, design speed 40 km/h and under), 线31 (Fig. 43, over 40 and under 100), 线32 (Fig. 44, 100 and over), 线33 (Fig. 45, a city road over 40 up to 60 may use it).</summary>
    public enum ArrowSize
    {
        Line30,
        Line31,
        Line32,
        Line33
    }

    /// <summary>
    /// The guide arrows (导向箭头, GB 5768.3-2025 5.13, Figs. 42 to 45) as flat white figures. The four figures are one design at four scales: every dimension of Fig. 42 is half of Fig. 43, Fig. 44
    /// is one and a half times, Fig. 45 three quarters (printed rounded: 23 for 22.5, 68 for 67.5, 203 for 202.5); so the figures are drawn once in the centimetres of Fig. 43 (the 6 m arrows) and
    /// scaled. Frame: +Z the way the traffic runs (up in the figures), +X the right of the driver; the figure is moved so that its box is centred on X = 0 and starts at Z = 0.
    /// What Fig. 43 prints and what is construed:
    /// - straight: 600 long, head 240, shaft 30, the head 30 wider than the shaft each side (all printed);
    /// - left (and right, its mirror): the vertical edge of the head 80 left of the left edge of the shaft and its tip 40 further; the corners of the head at 100, 160 (the tip), 220 and 310 below
    ///   its top; the two 45-degree edges of the stem leave the inner corners of the head (100 and 220 below the top) and meet the right edge of the shaft 390 and 270 up, as printed ("120" and
    ///   "270"); the same head in the straight-or-left arrow, whose stem meets the left edge of the shaft 160 and 40 up (printed), so that head stands 340 up;
    /// - U-turn: outer width 180, outer radius 90, inner 50 (the band is the 40 shaft), the left leg down to 200 below the top, the head 40 wider than the leg each side, 240 long (all printed);
    /// - the hook of the straight-or-U-turn and left-or-U-turn arrows: Figs. 42 to 45 print only its top (295 up), the widening of its head (30 each side of a 30 leg) and its reach (120 left of
    ///   the left edge of the shaft); **construed**: the band is the 30 of the shaft, the inner arc ends tangent to the left edge of the shaft (so the centre is 30 left of that edge, radii 60
    ///   and 30), the head is the U-turn head at three quarters (90 wide, 180 long, the tip at the foot of the shaft). On the print the base of that head measures about 80 and the top of the
    ///   hook about 286;
    /// - left-right (the Y): the heads of the left and right arrows, their vertical edges 80 outside the edges of the shaft, the 45-degree stems meeting in a notch on the axis;
    /// - merge left (and right): the tip 120 left of the left edge of the shaft at the top, the head 240 long and 30 wider than the shaft each side (printed); **the tilt of the head is not
    ///   printed: measured 22.3 degrees on Fig. 43 and taken as 22.5**; the shaft is straight at the foot and bends in one circular arc into the axis of the head (the radius follows: about 5.7 m).
    /// Pure, so that it is checked offline.
    /// </summary>
    public static class RoadArrows
    {
        /// <summary>The scale of each size against Fig. 43 (5.13.4).</summary>
        public static float ScaleOf(ArrowSize size)
        {
            switch (size)
            {
                case ArrowSize.Line30: return 0.5f;
                case ArrowSize.Line32: return 1.5f;
                case ArrowSize.Line33: return 0.75f;
                default: return 1f;
            }
        }

        /// <summary>The size for a design speed (5.13.4): 40 and under, over 40 and under 100, 100 and over. The city option (线33) is a choice, not a speed.</summary>
        public static ArrowSize SizeFor(float designKmh) => designKmh <= 40f ? ArrowSize.Line30 : designKmh < 100f ? ArrowSize.Line31 : ArrowSize.Line32;

        /// <summary>The length along the road of every arrow of a size (m): 3, 6, 9, 4.5.</summary>
        public static float Length(ArrowSize size) => 6f * ScaleOf(size);

        /// <summary>The tilt of the heads of the merge arrows (degrees): see the class note.</summary>
        public const float MergeTiltDegrees = 22.5f;

        /// <summary>How many segments a quarter turn of an arc of the figure is drawn with.</summary>
        public const int ArcSegments = 12;

        /// <summary>The arrow as a shape in metres (white), its box centred on X = 0 and starting at Z = 0.</summary>
        public static MarkingShape Shape(ArrowKind kind, ArrowSize size)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            List<Vec3> outline = Outline(kind);
            float s = ScaleOf(size) * 0.01f;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue;
            foreach (Vec3 p in outline)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minZ = Math.Min(minZ, p.Z);
            }

            float cx = (minX + maxX) * 0.5f;
            var scaled = new List<Vec3>(outline.Count);
            foreach (Vec3 p in outline)
                scaled.Add(new Vec3((p.X - cx) * s, 0f, (p.Z - minZ) * s));
            Polygon.Fill(shape.Mesh, scaled);
            return shape;
        }

        /// <summary>The outline of an arrow in the centimetres of Fig. 43, in whichever order it comes (the filler takes either).</summary>
        public static List<Vec3> Outline(ArrowKind kind)
        {
            switch (kind)
            {
                case ArrowKind.Straight: return Straight();
                case ArrowKind.StraightLeft: return StraightLeft();
                case ArrowKind.StraightRight: return Mirror(StraightLeft());
                case ArrowKind.Left: return Left();
                case ArrowKind.Right: return Mirror(Left());
                case ArrowKind.UTurn: return UTurn();
                case ArrowKind.StraightUTurn: return StraightUTurn();
                case ArrowKind.LeftUTurn: return LeftUTurn();
                case ArrowKind.LeftRight: return LeftRight();
                case ArrowKind.MergeLeft: return Merge();
                case ArrowKind.MergeRight: return Mirror(Merge());
                default: return Straight();
            }
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static List<Vec3> Mirror(List<Vec3> points)
        {
            var mirrored = new List<Vec3>(points.Count);
            for (int i = points.Count - 1; i >= 0; i--)
                mirrored.Add(P(-points[i].X, points[i].Z));
            return mirrored;
        }

        // Fig. 43, the straight arrow: shaft 30 wide (x -15..15) 360 up, head 240 long over a base 90 wide.
        private static List<Vec3> Straight() => new List<Vec3> { P(-15, 0), P(-15, 360), P(-45, 360), P(0, 600), P(45, 360), P(15, 360), P(15, 0) };

        /// <summary>The left head whose top is at z = top and whose vertical edge is at x = edge: the corners A (top), B (100 down), C (the tip, 40 further left, 160 down), E (220 down), D (310 down).</summary>
        private static void LeftHead(float edge, float top, out Vec3 a, out Vec3 b, out Vec3 c, out Vec3 e, out Vec3 d)
        {
            a = P(edge, top);
            b = P(edge, top - 100);
            c = P(edge - 40, top - 160);
            e = P(edge, top - 220);
            d = P(edge, top - 310);
        }

        // Fig. 43, the left-turn arrow: shaft x -30..0; the edge of the head 80 left of the left edge of the shaft; the edges of the stem at 45 degrees from B and E to the right edge of the shaft (390 and 270 up).
        private static List<Vec3> Left()
        {
            LeftHead(-110, 600, out Vec3 a, out Vec3 b, out Vec3 c, out Vec3 e, out Vec3 d);
            return new List<Vec3> { a, b, P(0, 390), P(0, 0), P(-30, 0), P(-30, 300), e, d, c };
        }

        // Fig. 43, straight or left: the straight arrow on a shaft x -30..0 and the left head 340 up, its stem meeting the left edge of the shaft 160 and 40 up.
        private static List<Vec3> StraightLeft()
        {
            LeftHead(-110, 340, out Vec3 a, out Vec3 b, out Vec3 c, out Vec3 e, out Vec3 d);
            return new List<Vec3> { P(-15, 600), P(30, 360), P(0, 360), P(0, 0), P(-30, 0), P(-30, 40), e, d, c, a, b, P(-30, 160), P(-30, 360), P(-60, 360) };
        }

        // Fig. 43, the U-turn: the outer left edge at x 0, the shaft x 140..180, the arcs about (90, 510), the left leg down to 400, the head 120 wide and 240 long.
        private static List<Vec3> UTurn()
        {
            var o = new List<Vec3> { P(180, 0), P(180, 510) };
            Polygon.Arc(o, 90, 510, 90, 0f, (float)Math.PI, ArcSegments * 2);
            o.Add(P(0, 400));
            o.Add(P(-40, 400));
            o.Add(P(20, 160));
            o.Add(P(80, 400));
            o.Add(P(40, 400));
            o.Add(P(40, 510));
            Polygon.Arc(o, 90, 510, 50, (float)Math.PI, 0f, ArcSegments * 2);
            o.Add(P(140, 0));
            return o;
        }

        /// <summary>The hook on the left of a shaft whose left edge is at x = -30 (see the class note): the points from the left edge of the shaft 235 up, round the hook, and back to it about 287 up.</summary>
        private static void Hook(List<Vec3> o)
        {
            const float cx = -60f, cz = 235f, ro = 60f, ri = 30f;
            o.Add(P(-30, cz));
            Polygon.Arc(o, cx, cz, ri, 0f, (float)Math.PI, ArcSegments * 2);
            o.Add(P(-90, 180));
            o.Add(P(-60, 180));
            o.Add(P(-105, 0));
            o.Add(P(-150, 180));
            o.Add(P(-120, 180));
            o.Add(P(-120, cz));
            // The outer arc from the leg (180 degrees) to where it meets the left edge of the shaft: cos a = 30 / 60.
            Polygon.Arc(o, cx, cz, ro, (float)Math.PI, (float)Math.Acos(0.5), ArcSegments * 2);
        }

        // Fig. 43, straight or U-turn: the straight arrow on a shaft x -30..0 with the hook on its left.
        private static List<Vec3> StraightUTurn()
        {
            var o = new List<Vec3> { P(-15, 600), P(30, 360), P(0, 360), P(0, 0), P(-30, 0) };
            Hook(o);
            o.Add(P(-30, 360));
            o.Add(P(-60, 360));
            return o;
        }

        // Fig. 43, left or U-turn: the left-turn arrow with the hook on the left edge of the shaft below the stem.
        private static List<Vec3> LeftUTurn()
        {
            LeftHead(-110, 600, out Vec3 a, out Vec3 b, out Vec3 c, out Vec3 e, out Vec3 d);
            var o = new List<Vec3> { a, b, P(0, 390), P(0, 0), P(-30, 0) };
            Hook(o);
            o.Add(P(-30, 300));
            o.Add(e);
            o.Add(d);
            o.Add(c);
            return o;
        }

        // Fig. 43, left or right (the Y): shaft x -15..15; the edges of the heads 80 outside the shaft; the upper edges of the stems meet on the axis 405 up, the lower ones reach the edges of the shaft 300 up.
        private static List<Vec3> LeftRight()
        {
            LeftHead(-95, 600, out Vec3 al, out Vec3 bl, out Vec3 cl, out Vec3 el, out Vec3 dl);
            Vec3 ar = P(95, 600), br = P(95, 500), cr = P(135, 440), er = P(95, 380), dr = P(95, 290);
            return new List<Vec3> { al, bl, P(0, 405), br, ar, cr, dr, er, P(15, 300), P(15, 0), P(-15, 0), P(-15, 300), el, dl, cl };
        }

        // Fig. 43, merge left: shaft x 0..30, the tip at (-120, 600), the head 240 long tilted MergeTiltDegrees, one arc from the vertical shaft into the axis of the head.
        private static List<Vec3> Merge()
        {
            double t = MergeTiltDegrees * Math.PI / 180.0;
            double sinT = Math.Sin(t), cosT = Math.Cos(t);
            double bx = -120 + 240 * sinT, bz = 600 - 240 * cosT;
            double nx = cosT, nz = sinT;
            double r = (15 - bx) / (1 - cosT);
            double ccx = bx - r * nx, ccz = bz - r * nz;
            var o = new List<Vec3> { P(0, 0), P(0, (float)ccz) };
            for (int i = 0; i <= ArcSegments; i++)
            {
                double a = t * i / ArcSegments;
                o.Add(P((float)(ccx + (r - 15) * Math.Cos(a)), (float)(ccz + (r - 15) * Math.Sin(a))));
            }

            o.Add(P((float)(bx - 45 * nx), (float)(bz - 45 * nz)));
            o.Add(P(-120, 600));
            o.Add(P((float)(bx + 45 * nx), (float)(bz + 45 * nz)));
            for (int i = ArcSegments; i >= 0; i--)
            {
                double a = t * i / ArcSegments;
                o.Add(P((float)(ccx + (r + 15) * Math.Cos(a)), (float)(ccz + (r + 15) * Math.Sin(a))));
            }

            o.Add(P(30, 0));
            return o;
        }
    }
}
