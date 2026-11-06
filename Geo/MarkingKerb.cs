using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The markings of GB 5768.3-2025 that are painted on something standing up: the no-parking lines on a kerb (6.3.1 线48, 6.3.2 线50: yellow, on the kerb's face and its top) and the elevation marking of 7.7
    /// (线72: yellow and black stripes, 45 degrees, 15 wide each, on a pier or a portal wall, at least 2.5 m up). The standard gives no kerb height and no width of a pier, so those are settings with a typical
    /// default (a kerb 15 cm high; a panel 1 m wide). A kerb line is a run along a road: its path is the foot of the kerb's face. The elevation marking is one upright sheet of paint that faces the traffic
    /// (-Z of the item, the traffic runs to +Z) and stands on the ground at the item. Pure.
    /// </summary>
    public static class MarkingKerb
    {
        /// <summary>The height of the kerb face (m) when the player does not say; the limits of what they may say.</summary>
        public const float DefaultHeight = 0.15f, MinHeight = 0.05f, MaxHeight = 0.4f;

        /// <summary>6.3.2 (Fig. 73): the dashes of the long-stay line are 1 m long with 1 m between. 6.3.1: the line is 15 cm wide or as wide as the kerb.</summary>
        public const float DashOn = 1f, DashOff = 1f, DefaultWidth = 0.15f, MaxWidth = 1f;

        /// <summary>How far the paint stands in front of the kerb's face and over its top (m, 0.12.33): over the stones of a kerb style (Kerbs.Proud, Kerbs.Over), and clear of the game's own kerb where there are none.</summary>
        public const float Ahead = Kerbs.Proud + 0.002f, Above = Kerbs.Over + 0.002f;

        /// <summary>Yellow paint on the face of a kerb and on its top: the path is the foot of the face; the kerb is on the right of the way the path runs or on its left; solid (线48) or dashed 1 m on 1 m off (线50).</summary>
        public static MarkingShape Line(IList<Vec3> path, bool kerbOnRight, float height, float width, bool dashed, float phase = 0f)
        {
            var shape = new MarkingShape { Colour = MarkingColour.Yellow };
            if (path == null || path.Count < 2 || float.IsNaN(height) || float.IsNaN(width))
                return shape;
            float h = Math.Max(MinHeight, Math.Min(MaxHeight, height)), w = Math.Max(0.05f, Math.Min(MaxWidth, width));
            List<List<Vec3>> pieces = dashed ? RoadMarkings.DashPieces(path, DashOn, DashOff, phase) : new List<List<Vec3>> { new List<Vec3>(path) };
            foreach (List<Vec3> piece in pieces)
            {
                if (piece.Count < 2)
                    continue;
                List<Vec3> face = MarkingFills.Offset(piece, (kerbOnRight ? -1f : 1f) * Ahead);
                Wall(shape.Mesh, face, h + Above, kerbOnRight);
                // The top: a ribbon w wide on the outer side of the face, over the kerb.
                List<Vec3> middle = MarkingFills.Offset(face, (kerbOnRight ? 1f : -1f) * w * 0.5f);
                var top = new List<Vec3>(middle.Count);
                foreach (Vec3 p in middle)
                    top.Add(new Vec3(p.X, p.Y + h + Above, p.Z));
                RoadMarkings.Ribbon(shape.Mesh, top, w);
            }

            return shape;
        }

        /// <summary>The face of the kerb: a vertical strip from the line up by the height, facing the road (the side the kerb is not on).</summary>
        private static void Wall(MeshBuilder mesh, IList<Vec3> line, float height, bool kerbOnRight)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1];
                Vec3 heading = (b - a).HeadingXZ();
                if (Vec3.DistanceXZ(a, b) < 1e-5f)
                    continue;
                // The road is on the side of the way that the kerb is not: the left when the kerb is on the right.
                Vec3 towardRoad = heading.RightXZ() * (kerbOnRight ? -1f : 1f);
                int v0 = mesh.Vertex(a, towardRoad, 0f, 0f);
                int v1 = mesh.Vertex(b, towardRoad, 1f, 0f);
                int v2 = mesh.Vertex(new Vec3(b.X, b.Y + height, b.Z), towardRoad, 1f, 1f);
                int v3 = mesh.Vertex(new Vec3(a.X, a.Y + height, a.Z), towardRoad, 0f, 1f);
                mesh.QuadFacing(v0, v1, v2, v3, towardRoad);
            }
        }

        // ---------------------------------------------------------------- 7.7 线72 立面标记

        /// <summary>The panel's least height (m): the paint goes up to at least 2.5 m above the road (7.7).</summary>
        public const float MinElevationHeight = 2.5f, StripeWidth = 0.15f, StripeAngle = 45f;

        /// <summary>
        /// The elevation marking (线72): a panel width wide and height high (at least 2.5 m) of yellow with black stripes at 45 degrees, 15 cm wide each; the stripes slope down toward the lane (the standard
        /// says the edge that slopes down faces the lane), so laneOnRight (the lane is to the driver's right of the panel) leans them "\" and the other "/", as the driver sees the panel from the lane.
        /// The panel is flat against the plane z = 0, facing -Z (the oncoming driver); its foot is on the ground. Upright shapes: the picture of it is its front.
        /// </summary>
        public static List<MarkingShape> Elevation(float width, float height, bool laneOnRight)
        {
            var shapes = new List<MarkingShape>();
            float w = Math.Max(0.3f, Math.Min(6f, float.IsNaN(width) ? 1f : width)), h = Math.Max(MinElevationHeight, Math.Min(8f, float.IsNaN(height) ? MinElevationHeight : height));
            var yellow = new MarkingShape { Colour = MarkingColour.Yellow, Upright = true };
            var front = new Vec3(0f, 0f, -1f);
            int a = yellow.Mesh.Vertex(new Vec3(-w * 0.5f, 0f, 0f), front, 0f, 0f), b = yellow.Mesh.Vertex(new Vec3(w * 0.5f, 0f, 0f), front, 1f, 0f);
            int c = yellow.Mesh.Vertex(new Vec3(w * 0.5f, h, 0f), front, 1f, 1f), d = yellow.Mesh.Vertex(new Vec3(-w * 0.5f, h, 0f), front, 0f, 1f);
            yellow.Mesh.QuadFacing(a, b, c, d, front);
            shapes.Add(yellow);

            // The stripes are made flat (x across, z for the height) and stood up.
            var flat = new MeshBuilder();
            MarkingFills.Hatch(flat, MarkingFills.Rectangle(-w * 0.5f, 0f, w * 0.5f, h), StripeWidth, 2f * StripeWidth, laneOnRight ? 180f - StripeAngle : StripeAngle);
            var black = new MarkingShape { Colour = MarkingColour.Black, Upright = true };
            for (int i = 0; i + 2 < flat.Indices.Count; i += 3)
            {
                Vec3 p = flat.Positions[flat.Indices[i]], q = flat.Positions[flat.Indices[i + 1]], r = flat.Positions[flat.Indices[i + 2]];
                int v0 = black.Mesh.Vertex(new Vec3(p.X, p.Z, 0f), front, 0f, 0f), v1 = black.Mesh.Vertex(new Vec3(q.X, q.Z, 0f), front, 1f, 0f), v2 = black.Mesh.Vertex(new Vec3(r.X, r.Z, 0f), front, 0f, 1f);
                black.Mesh.TriFacing(v0, v1, v2, front);
            }

            black.Layer(1);
            shapes.Add(black);
            return shapes;
        }
    }
}
