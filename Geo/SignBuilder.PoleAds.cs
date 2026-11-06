using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        private const int AdSides = 12;

        /// <summary>
        /// A lamp-post ad or decoration (0.12.49, PoleAd): its parts beside the post (its axis at the placement's X), each Gap from the post's surface, or one round it (PoleAd.Wrap), in the plane through
        /// the axis, with the print on both faces (the back mapped so that it reads the right way from behind) and an edge in the hardware's colour or the design's; and what holds them: a hoop round
        /// the post at each arm, the arms (a banner's at its top and bottom edge, a figure's or a lantern's above it with a cord down), a box's or a panel's design (AddDesign), and the lanterns' bodies.
        /// </summary>
        private static void AddPoleAd(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            GuideBoard board = instance.Board;
            PoleAd ad = board.PoleAd;
            int member = placement.Board.Member;
            MeshBuilder face = parts.AddFace(instance);
            float axisX = placement.Cx, r = Math.Max(0.03f, layout.PoleRadius);
            float boxW = instance.BoundsX1 - instance.BoundsX0, boxH = instance.BoundsY1 - instance.BoundsY0;
            float z0 = -ad.Depth * 0.5f, z1 = ad.Depth * 0.5f;
            MeshBuilder hardware = parts.Solid(ad.Hardware);
            var hoops = new HashSet<int>();
            void Hoop(float y)
            {
                // One hoop at a height for both parts of a pair (in centimetres, so that the two arms' heights are one key).
                if (!hoops.Add((int)Math.Round(y * 100f)))
                    return;
                float half = Signs.LampAds.HoopHeight * 0.5f;
                Solids.Band(hardware, new Vec3(axisX, y - half, 0f), new Vec3(axisX, y + half, 0f), r, r + 0.008f, AdSides * 2);
            }

            for (int i = 0; i < board.Parts.Count; i++)
            {
                GuidePart part = board.Parts[i];
                float side = ad.Pair && i == 1 ? -1f : 1f;
                float cx = ad.Wrap ? axisX : axisX + side * (r + ad.Gap + part.W * 0.5f);
                float cy = placement.Cy + (board.Main.H * 0.5f - (part.Y + part.H * 0.5f));
                float top = cy + part.H * 0.5f, bottom = cy - part.H * 0.5f, inner = cx - side * part.W * 0.5f, outer = cx + side * part.W * 0.5f;
                List<Point2> outline = GuideKit.PartOutline(part);
                List<Point2> inFrame = MirrorX(outline);
                parts.Picks.Add(BoardPick(inFrame, cx, cy, part.W, part.H, Math.Min(z0, -0.01f), member, Math.Max(z1, 0.01f), false));
                if (ad.Kind == PoleAdKind.Neon)
                {
                    // Hung by its hook from an arm over its middle, and held at its body's middle by an arm to its inner side.
                    AddNeon(parts, ad, ad.Neon[i], cx, cy);
                    float hang = top + 0.03f, middle = cy + ad.Neon[i].BodyY;
                    AdArm(hardware, axisX + side * r * 0.9f, cx + side * 0.012f, hang, ArmRadiusOf(ad));
                    Solids.Tube(hardware, new Vec3(cx, top - 0.01f, 0f), new Vec3(cx, hang, 0f), 0.006f, 0.006f, 6, false, false);
                    AdArm(hardware, axisX + side * r * 0.9f, inner + side * 0.004f, middle, 0.01f);
                    Hoop(hang);
                    Hoop(middle);
                    continue;
                }

                if (ad.Kind == PoleAdKind.Lantern)
                {
                    AddLanterns(parts, ad, cx, top, part.W);
                    float arm = top + 0.18f;
                    AdArm(hardware, axisX + side * r * 0.9f, cx + side * 0.04f, arm, ArmRadiusOf(ad));
                    Hoop(arm);
                    continue;
                }

                float centreX = part.X + part.W * 0.5f, centreY = part.Y + part.H * 0.5f, x0 = instance.BoundsX0, y0 = instance.BoundsY0;
                Func<float, float, (float u, float v)> uv = (px, py) => ((centreX + px - x0) / boxW, 1f - (centreY - py - y0) / boxH);
                // The back reads the right way from behind (the picture mirrored on it); a part cut to a picture's clear parts has the picture as the front has it, which is what fits its cut.
                bool pictureCut = part.Cut != null && part.Elements.Exists(e => e.Kind == GuideElementKind.Image && e.Opaque);
                Func<float, float, (float u, float v)> back = pictureCut ? uv : (px, py) => uv(-px, py);
                MeshBuilder edge = EdgeOf(parts, ad, part);
                if (part.Cut != null)
                {
                    Prims.LoopsFace(face, part.Cut, z1, true, cx, cy, uv);
                    Prims.LoopsFace(face, part.Cut, z0, false, cx, cy, back);
                    // A box's sides are seen from afar too (0.12.49: a round, oval or arched box was open at its edge in reduced detail); a figure's edge is thin.
                    if (edge != null && (!t_Reduced || ad.Kind == PoleAdKind.Box))
                        Prims.LoopsRim(edge, part.Cut, z0, z1, cx, cy, true);
                }
                else
                {
                    Prims.PolygonFaceMapped(face, outline, z1, true, cx, cy, uv);
                    Prims.PolygonFaceMapped(face, outline, z0, false, cx, cy, back);
                    if (edge != null && (!t_Reduced || ad.Kind == PoleAdKind.Box))
                        Prims.Rim(edge, inFrame, z0, z1, cy, cx);
                }

                float armRadius = ArmRadiusOf(ad);
                if (ad.Kind == PoleAdKind.Box || ad.Kind == PoleAdKind.Panel)
                {
                    AddDesign(parts, ad, side, cx, cy, axisX, r, hardware, Hoop);
                    continue;
                }

                switch (ad.Kind)
                {
                    case PoleAdKind.Banner:
                    {
                        // The banner's top and bottom hems are in the slots of the arms (8 x 4 cm bars, the hem in their middle), which reach past its outer edge and end in red caps.
                        float ah = Signs.LampAds.ArmHeight, ad2 = Signs.LampAds.ArmDepth * 0.5f, from = axisX + side * r * 0.9f, to = outer + side * LampAdsArmPast;
                        foreach (float y in new[] { top + ah * 0.25f, bottom - ah * 0.25f })
                        {
                            Solids.Box(hardware, new Vec3((from + to) * 0.5f, y, 0f), new Vec3(Math.Abs(to - from) * 0.5f, ah * 0.5f, ad2));
                            if (!t_Reduced)
                                Solids.Box(parts.Solid(SolidColour.Red), new Vec3(to + side * 0.006f, y, 0f), new Vec3(0.006f, ah * 0.5f + 0.004f, ad2 + 0.004f));
                            Hoop(y);
                        }

                        break;
                    }
                    default:
                        // A figure hangs by a cord from an arm above its middle.
                        float armY = top + 0.15f;
                        AdArm(hardware, axisX + side * r * 0.9f, cx + side * 0.04f, armY, armRadius);
                        Solids.Tube(hardware, new Vec3(cx, top - 0.01f, 0f), new Vec3(cx, armY, 0f), 0.004f, 0.004f, 6, false, false);
                        Hoop(armY);
                        break;
                }
            }
        }

        /// <summary>
        /// What is round a box or a panel and holds it (PoleAd.Plates, Rods, Balls, Arms, Hoops; 0.12.49, Signs.LampBoxes), in the first part's frame (as the viewer sees it, the side towards the post
        /// +x): put at (cx - side x, cy + y), so that the second of a pair is its mirror. A plate is a solid of its loops (in reduced detail its faces only), lit in its
        /// LitColour when the ad is; an arm runs from inside the post to the bracket plate on the edge it holds, with a hoop round the post; a box round the post has a collar where the post goes in
        /// and one where it comes out.
        /// </summary>
        private static void AddDesign(SignParts parts, PoleAd ad, float side, float cx, float cy, float axisX, float r, MeshBuilder hardware, Action<float> hoop)
        {
            Vec3 At(float x, float y, float z) => new Vec3(cx - side * x, cy + y, z);
            foreach (PoleAdPlate plate in ad.Plates)
            {
                List<List<Point2>> loops = side > 0f ? plate.Loops : Signs.LampBoxes.Mirrored(plate.Loops);
                MeshBuilder mesh = parts.Solid(ad.Lit ? plate.LitColour : plate.Colour);
                Prims.LoopsFace(mesh, loops, plate.Z + plate.Half, true, cx, cy, null);
                Prims.LoopsFace(mesh, loops, plate.Z - plate.Half, false, cx, cy, null);
                if (!t_Reduced)
                    Prims.LoopsRim(mesh, loops, plate.Z - plate.Half, plate.Z + plate.Half, cx, cy, true);
            }

            foreach (PoleAdRod rod in ad.Rods)
                BentTube(parts.Solid(rod.Colour), rod.Path.ConvertAll(p => At(p.X, p.Y, 0f)), rod.Radius, t_Reduced ? 6 : AdSides, rod.CapStart, rod.CapEnd);
            foreach ((Point2 centre, float radius, SolidColour colour) in ad.Balls)
                Ball(parts.Solid(colour), At(centre.X, centre.Y, 0f), radius);
            float armRadius = ArmRadiusOf(ad);
            foreach (PoleAdArm arm in ad.Arms)
            {
                // The bracket plate from 2 mm inside the edge to 10 mm out of it, across the edge (its normal towards the post); the arm ends 7 mm out, inside it.
                float length = (float)Math.Sqrt(1f + arm.Slope * arm.Slope), nx = 1f / length, ny = -arm.Slope / length, tx = arm.Slope / length, ty = 1f / length;
                var corners = new Vec3[8];
                for (int k = 0; k < 8; k++)
                {
                    float n = (k & 1) != 0 ? 0.010f : -0.002f, t = (k & 2) != 0 ? arm.Along : -arm.Along, z = (k & 4) != 0 ? arm.Half : -arm.Half;
                    corners[k] = At(arm.Reach + nx * n + tx * t, arm.Y + ny * n + ty * t, z);
                }

                if (!t_Reduced)
                    Solids.Convex(hardware, corners, s_PlateFaces);
                AdArm(hardware, axisX + side * r * 0.9f, cx - side * (arm.Reach + 0.007f * length), cy + arm.Y, armRadius);
                hoop(cy + arm.Y);
            }

            foreach (float y in ad.Hoops)
                hoop(cy + y);
            if (ad.Wrap)
            {
                foreach ((float y, float dir) in new[] { (ad.WrapTop, 1f), (ad.WrapBottom, -1f) })
                    Solids.Band(hardware, new Vec3(axisX, cy + y - dir * 0.005f, 0f), new Vec3(axisX, cy + y + dir * 0.04f, 0f), r, r + 0.012f, AdSides * 2);
            }
        }

        /// <summary>The faces of a plate whose eight corners are numbered by bits (1: out of the edge, 2: up the edge, 4: across the plane), as Solids.Box numbers a box's.</summary>
        private static readonly int[][] s_PlateFaces =
        {
            new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 }
        };

        /// <summary>
        /// A tube of the radius through points in a plane z = const (a crook arm, an upright): a ring at each point square to the bisector of its two segments and stretched across the bend so that the
        /// tube keeps its radius along both (no gap at a bend), smooth; flat ends when asked.
        /// </summary>
        private static void BentTube(MeshBuilder mesh, List<Vec3> points, float radius, int segments, bool capStart, bool capEnd)
        {
            int m = points.Count;
            if (m < 2)
                return;
            var up = new Vec3(0f, 0f, 1f);
            var rings = new int[m, segments + 1];
            var ahead = new Vec3[m];
            for (int i = 0; i < m; i++)
            {
                Vec3 before = MeshBuilder.Normalize(points[Math.Max(1, i)] - points[Math.Max(1, i) - 1]), after = MeshBuilder.Normalize(points[Math.Min(m - 1, i + 1)] - points[Math.Min(m - 1, i + 1) - 1]);
                Vec3 t = MeshBuilder.Normalize(before + after), across = MeshBuilder.Cross(up, t);
                float stretch = 1f / Math.Max(0.5f, Vec3.Dot(t, after));
                ahead[i] = t;
                for (int k = 0; k <= segments; k++)
                {
                    double a = 2.0 * Math.PI * k / segments;
                    float c = (float)Math.Cos(a), sn = (float)Math.Sin(a);
                    rings[i, k] = mesh.Vertex(points[i] + up * (radius * c) + across * (radius * sn * stretch), MeshBuilder.Normalize(up * c + across * sn));
                }
            }

            for (int i = 0; i + 1 < m; i++)
            {
                for (int k = 0; k < segments; k++)
                    mesh.QuadFacing(rings[i, k], rings[i, k + 1], rings[i + 1, k + 1], rings[i + 1, k], mesh.Normals[rings[i, k]] + mesh.Normals[rings[i, k + 1]]);
            }

            foreach ((bool cap, int i, float dir) in new[] { (capStart, 0, -1f), (capEnd, m - 1, 1f) })
            {
                if (!cap)
                    continue;
                Vec3 n = ahead[i] * dir;
                int centre = mesh.Vertex(points[i], n), first = mesh.VertexCount;
                for (int k = 0; k < segments; k++)
                    mesh.Vertex(mesh.Positions[rings[i, k]], n);
                for (int k = 0; k < segments; k++)
                    mesh.TriFacing(centre, first + k, first + (k + 1) % segments, n);
            }
        }

        /// <summary>A ball (a finial): bands between latitudes from 75 degrees south to 75 north, flat caps there.</summary>
        private static void Ball(MeshBuilder mesh, Vec3 centre, float radius)
        {
            int slices = t_Reduced ? 3 : 7;
            for (int j = 0; j < slices; j++)
            {
                double a0 = (-75.0 + 150.0 * j / slices) * Math.PI / 180.0, a1 = (-75.0 + 150.0 * (j + 1) / slices) * Math.PI / 180.0;
                Solids.Tube(mesh, centre + new Vec3(0f, radius * (float)Math.Sin(a0), 0f), centre + new Vec3(0f, radius * (float)Math.Sin(a1), 0f), radius * (float)Math.Cos(a0), radius * (float)Math.Cos(a1),
                    AdSides, j == 0, j == slices - 1);
            }
        }

        /// <summary>
        /// A part's neon (PoleAd.Neon, Signs.Neon): each stroke a strip NeonWidth across and the ad's Depth through, its runs (Neon.Runs) mitred, closed at their ends, lit in its colour or in its
        /// silicone's when the ad is not lit. A colour's strips stand a little out of those of the colours before it on both faces (Neon.Layer), so that where two cross one is in front.
        /// </summary>
        private static void AddNeon(SignParts parts, PoleAd ad, NeonArt art, float cx, float cy)
        {
            // The skeleton's rods through the strips' middle (too thin to see from afar).
            if (!t_Reduced)
            {
                MeshBuilder steel = parts.Solid(ad.Hardware);
                foreach ((Point2 a, Point2 b) in art.Rods)
                    Solids.Tube(steel, new Vec3(cx - a.X, cy + a.Y, 0f), new Vec3(cx - b.X, cy + b.Y, 0f), Signs.Neon.RodRadius, Signs.Neon.RodRadius, 6, false, false);
            }

            float half = ad.NeonWidth * 0.5f;
            foreach (NeonStroke stroke in art.Strokes)
            {
                float dz = 0.0015f * Signs.Neon.Layer(stroke.Colour);
                MeshBuilder mesh = parts.Solid(ad.Lit ? stroke.Colour : Signs.Neon.Unlit(stroke.Colour));
                foreach (List<Point2> run in Signs.Neon.Runs(stroke, out bool loop))
                    NeonStrip(mesh, run, loop, half, -ad.Depth * 0.5f - dz, ad.Depth * 0.5f + dz, cx, cy);
            }
        }

        /// <summary>
        /// A strip half wide each side of a run (as the viewer sees it, round (cx, cy): put at (cx - x, cy + y)) between the planes z0 and z1: its front and back, and (not reduced) its sides and its
        /// two ends unless it is a loop. The mitre at a turn is at most twice the half width (Neon.Runs leaves no turn sharper than 100 degrees).
        /// </summary>
        private static void NeonStrip(MeshBuilder mesh, List<Point2> run, bool loop, float half, float z0, float z1, float cx, float cy)
        {
            int m = run.Count;
            var left = new Point2[m];
            var right = new Point2[m];
            var across = new Point2[m];
            for (int i = 0; i < m; i++)
            {
                Point2 a = i > 0 ? run[i - 1] : loop ? run[m - 1] : run[i], b = run[i], c = i < m - 1 ? run[i + 1] : loop ? run[0] : run[i];
                Point2 n0 = LeftOf(a, b), n1 = LeftOf(b, c);
                if (i == 0 && !loop)
                    n0 = n1;
                if (i == m - 1 && !loop)
                    n1 = n0;
                float nx = n0.X + n1.X, ny = n0.Y + n1.Y, length = (float)Math.Sqrt(nx * nx + ny * ny);
                nx = length > 1e-6f ? nx / length : n1.X;
                ny = length > 1e-6f ? ny / length : n1.Y;
                float reach = half / Math.Max(0.5f, nx * n1.X + ny * n1.Y);
                across[i] = new Point2(nx, ny);
                left[i] = new Point2(b.X + nx * reach, b.Y + ny * reach);
                right[i] = new Point2(b.X - nx * reach, b.Y - ny * reach);
            }

            Vec3 At(Point2 p, float z) => new Vec3(cx - p.X, cy + p.Y, z);
            // A direction as the viewer sees it, in the sign's frame (its x runs the other way).
            Vec3 Out(float x, float y) => new Vec3(-x, y, 0f);
            int segments = loop ? m : m - 1;
            foreach ((float z, Vec3 facing) in new[] { (z1, new Vec3(0f, 0f, 1f)), (z0, new Vec3(0f, 0f, -1f)) })
            {
                int first = mesh.VertexCount;
                for (int i = 0; i < m; i++)
                {
                    mesh.Vertex(At(left[i], z), facing);
                    mesh.Vertex(At(right[i], z), facing);
                }

                for (int s = 0; s < segments; s++)
                {
                    int i = first + 2 * s, j = first + 2 * ((s + 1) % m);
                    mesh.QuadFacing(i, i + 1, j + 1, j, facing);
                }
            }

            if (t_Reduced)
                return;
            foreach (bool onLeft in new[] { true, false })
            {
                Point2[] edge = onLeft ? left : right;
                float sign = onLeft ? 1f : -1f;
                int first = mesh.VertexCount;
                for (int i = 0; i < m; i++)
                {
                    Vec3 n = Out(sign * across[i].X, sign * across[i].Y);
                    mesh.Vertex(At(edge[i], z0), n);
                    mesh.Vertex(At(edge[i], z1), n);
                }

                for (int s = 0; s < segments; s++)
                {
                    int i = first + 2 * s, j = first + 2 * ((s + 1) % m), k = (s + 1) % m;
                    mesh.QuadFacing(i, j, j + 1, i + 1, Out(sign * (across[s].X + across[k].X), sign * (across[s].Y + across[k].Y)));
                }
            }

            if (loop)
                return;
            foreach (bool start in new[] { true, false })
            {
                int e = start ? 0 : m - 1, o = start ? 1 : m - 2;
                float dx = run[e].X - run[o].X, dy = run[e].Y - run[o].Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
                Vec3 n = Out(dx / d, dy / d);
                int a = mesh.Vertex(At(left[e], z0), n), b = mesh.Vertex(At(left[e], z1), n), c = mesh.Vertex(At(right[e], z1), n), f = mesh.Vertex(At(right[e], z0), n);
                mesh.QuadFacing(a, b, c, f, n);
            }
        }

        /// <summary>The unit vector to the left of the way from a to b (as the viewer sees it).</summary>
        private static Point2 LeftOf(Point2 a, Point2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, d = (float)Math.Sqrt(dx * dx + dy * dy);
            return d < 1e-9f ? new Point2(0f, 1f) : new Point2(-dy / d, dx / d);
        }

        /// <summary>LampAds.ArmPast, which the builder uses (the class is in another namespace).</summary>
        private const float LampAdsArmPast = Signs.LampAds.ArmPast;

        private static float ArmRadiusOf(PoleAd ad) => ad.Kind == PoleAdKind.Box ? 0.02f : 0.012f;

        /// <summary>An arm along X from x0 to x1 at the height y, in the plane of the ads.</summary>
        private static void AdArm(MeshBuilder hardware, float x0, float x1, float y, float radius) =>
            Solids.Tube(hardware, new Vec3(x0, y, 0f), new Vec3(x1, y, 0f), radius, radius, AdSides, false, true);

        /// <summary>What the edge of a part is made of: a box's and a panel's frame are the hardware's colour or the design's (PoleAd.Edge), a figure's its own colour; a banner has none (it is cloth).</summary>
        private static MeshBuilder EdgeOf(SignParts parts, PoleAd ad, GuidePart part)
        {
            switch (ad.Kind)
            {
                case PoleAdKind.Banner:
                    return null;
                case PoleAdKind.Knot:
                    return parts.Solid(part.Ground == GuideColour.Gold ? SolidColour.Gold : part.Ground == GuideColour.White ? SolidColour.White : part.Ground == GuideColour.Blue ? SolidColour.Blue : SolidColour.Red);
                default:
                    return parts.Solid(ad.Edge);
            }
        }

        /// <summary>
        /// The lanterns of one string from its top (top) down, each of diameter d with its gold caps and its tassel, joined by cords: a round lantern a cut ellipsoid, a palace lantern a hexagonal body
        /// between two gold roofs, a long lantern a tall ellipsoid. Their bodies glow at night when the ad is lit (SolidColour.GlowRed, GlowGold).
        /// </summary>
        private static void AddLanterns(SignParts parts, PoleAd ad, float cx, float top, float d)
        {
            MeshBuilder body = parts.Solid(ad.Lit ? (ad.LanternGold ? SolidColour.GlowGold : SolidColour.GlowRed) : (ad.LanternGold ? SolidColour.Yellow : SolidColour.Red));
            MeshBuilder gold = parts.Solid(SolidColour.Gold);
            MeshBuilder tassel = parts.Solid(ad.LanternGold ? SolidColour.Red : SolidColour.Gold);
            string kind = ad.LanternKind == 1 ? "palace" : ad.LanternKind == 2 ? "long" : "round";
            float h1 = Signs.LampAds.LanternHeight(kind, d), link = Signs.LampAds.LanternLink * d, R = d * 0.5f;
            int segments = t_Reduced ? 6 : 16;
            Solids.Tube(gold, new Vec3(cx, top, 0f), new Vec3(cx, top + 0.18f, 0f), 0.004f, 0.004f, 6, false, false);
            for (int k = 0; k < ad.Lanterns; k++)
            {
                float y = top - k * (h1 + link);
                float cap = 0.05f * d, tasselH = 0.2f * d;
                float bodyH = h1 - 2f * cap - tasselH;
                float bodyTop = y - cap, bodyBottom = bodyTop - bodyH;
                if (kind == "palace")
                {
                    // A hexagonal body with gold roofs that reach out past it.
                    Solids.Tube(body, new Vec3(cx, bodyBottom, 0f), new Vec3(cx, bodyTop, 0f), R * 0.88f, R * 0.88f, 6, false, false);
                    Solids.Tube(gold, new Vec3(cx, bodyTop, 0f), new Vec3(cx, y, 0f), R * 1.08f, R * 0.55f, 6, true, true);
                    Solids.Tube(gold, new Vec3(cx, bodyBottom - cap, 0f), new Vec3(cx, bodyBottom, 0f), R * 0.55f, R * 1.08f, 6, true, true);
                }
                else
                {
                    // An ellipsoid cut where its radius is 0.62 of the largest: the caps fit there.
                    const float endRadius = 0.62f;
                    double phi0 = Math.Asin(endRadius), a = bodyH / (2.0 * Math.Cos(phi0));
                    float middle = (bodyTop + bodyBottom) * 0.5f;
                    int slices = t_Reduced ? 4 : 10;
                    for (int j = 0; j < slices; j++)
                    {
                        double p0 = phi0 + (Math.PI - 2.0 * phi0) * j / slices, p1 = phi0 + (Math.PI - 2.0 * phi0) * (j + 1) / slices;
                        Solids.Tube(body, new Vec3(cx, middle + (float)(a * Math.Cos(p0)), 0f), new Vec3(cx, middle + (float)(a * Math.Cos(p1)), 0f), R * (float)Math.Sin(p0), R * (float)Math.Sin(p1), segments, j == 0, j == slices - 1);
                    }

                    Solids.Tube(gold, new Vec3(cx, bodyTop, 0f), new Vec3(cx, y, 0f), R * endRadius * 1.02f, R * endRadius * 0.9f, segments, false, true);
                    Solids.Tube(gold, new Vec3(cx, bodyBottom - cap, 0f), new Vec3(cx, bodyBottom, 0f), R * endRadius * 0.9f, R * endRadius * 1.02f, segments, true, false);
                }

                // The tassel under the lower cap: a cord and a skirt that widens.
                float t0 = bodyBottom - cap;
                Solids.Tube(gold, new Vec3(cx, t0 - 0.06f * d, 0f), new Vec3(cx, t0, 0f), 0.012f * d, 0.012f * d, 6, false, false);
                Solids.Tube(tassel, new Vec3(cx, t0 - tasselH, 0f), new Vec3(cx, t0 - 0.06f * d, 0f), 0.11f * d, 0.03f * d, t_Reduced ? 6 : 10, true, true);
                if (k + 1 < ad.Lanterns)
                    Solids.Tube(gold, new Vec3(cx, t0 - tasselH - link, 0f), new Vec3(cx, t0 - tasselH, 0f), 0.004f, 0.004f, 6, false, false);
            }
        }
    }
}
