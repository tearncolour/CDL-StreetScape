using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The neon style's glass (0.12.76): a round tube swept along closed lines (the glyph's outline moved inward by the tube's radius, so that the tube's outside is the glyph's edge), in em units like the
    /// glyph it belongs to. The sweep keeps the tube's section round at corners (a mitre scale, at most Mitre) and gives it smooth normals along the line. The front of the tube is the board's face plane,
    /// its back (a diameter behind it) is where the studs end. This is this mod's design (a neon tube drawn along a letter's outline), not an object of a standard. Pure.
    /// </summary>
    public sealed class NeonTube
    {
        /// <summary>Corners of the line are sharper than this and the section is not scaled further (the tube is thinner there than round).</summary>
        public const float Mitre = 1.6f;

        /// <summary>The facets of the section: eight, so that the section has a vertex at the top (the face plane), at the bottom and on each side, and reads as round with smooth normals.</summary>
        public const int Sides = 8;

        public readonly List<List<Point2>> Centre;

        /// <summary>The tube's radius in em.</summary>
        public readonly float Radius;

        public NeonTube(List<List<Point2>> centre, float radius)
        {
            Centre = centre;
            Radius = radius;
        }

        public int Points
        {
            get
            {
                int n = 0;
                foreach (List<Point2> loop in Centre)
                    n += loop.Count;
                return n;
            }
        }

        public int Vertices => Points * Sides;

        public int Triangles => Points * Sides * 2;

        /// <summary>
        /// The tube on a board (the glyph at (x, y) metres from the middle of the text, em at metresPerEm, x as the viewer sees it, the vertex at -x as GlyphMesh.AddTo puts it): its front at frontZ and
        /// its back a diameter behind that. The triangles face outward from the tube's axis.
        /// </summary>
        public void AddTo(MeshBuilder b, float metresPerEm, float x, float y, float frontZ) => AddTo(b, metresPerEm, x, y, frontZ, 0f);

        /// <summary>The same, turned about the glyph's origin by angle (radians, counter-clockwise as the viewer sees it) before it is moved to (x, y): a letter of a text on an arc.</summary>
        public void AddTo(MeshBuilder b, float metresPerEm, float x, float y, float frontZ, float angle)
        {
            float r = Radius * metresPerEm, zc = frontZ - r;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            foreach (List<Point2> loop in Centre)
            {
                int n = loop.Count;
                if (n < 3)
                    continue;
                var ring = new int[n * Sides];
                for (int i = 0; i < n; i++)
                {
                    Point2 prev = loop[(i + n - 1) % n], at = loop[i], next = loop[(i + 1) % n];
                    float t1x = at.X - prev.X, t1y = at.Y - prev.Y, t2x = next.X - at.X, t2y = next.Y - at.Y;
                    float l1 = (float)Math.Sqrt(t1x * t1x + t1y * t1y), l2 = (float)Math.Sqrt(t2x * t2x + t2y * t2y);
                    if (l1 < 1e-9f || l2 < 1e-9f)
                    {
                        l1 = Math.Max(l1, 1e-9f);
                        l2 = Math.Max(l2, 1e-9f);
                    }

                    t1x /= l1;
                    t1y /= l1;
                    t2x /= l2;
                    t2y /= l2;
                    // The normal of the line at the point is the mean of its two edges' normals (to the right of the way the line runs); the section is scaled so that it stays round across a corner.
                    float nx = t1y + t2y, ny = -(t1x + t2x), length = (float)Math.Sqrt(nx * nx + ny * ny);
                    if (length < 1e-6f)
                    {
                        nx = t1y;
                        ny = -t1x;
                        length = 1f;
                    }

                    nx /= length;
                    ny /= length;
                    float scale = Math.Min(Mitre, 1f / Math.Max(0.25f, nx * t1y + ny * -t1x));
                    // The glyph's own frame is turned for the board: the offset from the glyph's origin and the normal both.
                    float onx = cos * nx - sin * ny, ony = sin * nx + cos * ny;
                    for (int k = 0; k < Sides; k++)
                    {
                        double a = 2.0 * Math.PI * k / Sides;
                        float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                        // In the plane of the board the section reaches out by r c along the normal (scaled), and by r s along z.
                        float lx = at.X * metresPerEm + nx * r * c * scale, ly = at.Y * metresPerEm + ny * r * c * scale;
                        float px = x + cos * lx - sin * ly, py = y + sin * lx + cos * ly;
                        // Mirrored in x like every vertex of a board: the normal is mirrored too.
                        ring[i * Sides + k] = b.Vertex(new Vec3(-px, py, zc + r * s), new Vec3(-onx * c, ony * c, s));
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    for (int k = 0; k < Sides; k++)
                    {
                        int l = (k + 1) % Sides;
                        double a = 2.0 * Math.PI * (k + 0.5) / Sides;
                        Point2 p0 = loop[i], p1 = loop[j];
                        float ex = p1.X - p0.X, ey = p1.Y - p0.Y, el = Math.Max(1e-9f, (float)Math.Sqrt(ex * ex + ey * ey));
                        float nx = ey / el, ny = -ex / el;
                        float onx = cos * nx - sin * ny, ony = sin * nx + cos * ny;
                        var facing = new Vec3(-onx * (float)Math.Cos(a), ony * (float)Math.Cos(a), (float)Math.Sin(a));
                        b.QuadFacing(ring[i * Sides + k], ring[j * Sides + k], ring[j * Sides + l], ring[i * Sides + l], facing);
                    }
                }
            }
        }
    }
}
