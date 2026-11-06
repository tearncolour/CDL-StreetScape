using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>What a run of kilometre stones and hundred-metre posts puts out (RoadPosts.Hundreds): both, the stones only, or the posts only.</summary>
    public enum MilePostKind
    {
        All,
        Stones,
        Hundreds
    }

    /// <summary>
    /// The roadside posts (0.12.78): the 柱式轮廓标 with its 百米牌, and the 里程碑 and 百米桩. Drawn in the world's frame along a line like the other barriers, solids in their own colours; the figures are the
    /// bundled font's, laid flat on the face that looks at the oncoming traffic (the way the line runs: the face against it), so a post is seen as the driver sees it.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>The section of the 柱式轮廓标 in (u across to the right of the face, w toward the viewer from the section's middle): the isosceles triangle, its base toward the viewer, the corners rounded.</summary>
        private static readonly List<(float u, float w)> s_PostSection = PostSection();

        private static List<(float u, float w)> PostSection()
        {
            float b = RoadPosts.TriangleBase * 0.5f, h = RoadPosts.TriangleHeight, wBase = h / 3f, wApex = -2f * h / 3f;
            var corners = new[] { (u: -b, w: wBase), (u: b, w: wBase), (u: 0f, w: wApex) };
            var points = new List<(float u, float w)>();
            for (int i = 0; i < 3; i++)
            {
                var corner = corners[i];
                var before = corners[(i + 2) % 3];
                var after = corners[(i + 1) % 3];
                var a = Toward(corner, before, RoadPosts.Rounding);
                var c = Toward(corner, after, RoadPosts.Rounding);
                // The point between them is pulled toward the corner a little, so that the rounding is not a chamfer.
                float mu = (a.u + c.u) * 0.5f, mw = (a.w + c.w) * 0.5f;
                points.Add(a);
                points.Add((mu + (corner.u - mu) * 0.3f, mw + (corner.w - mw) * 0.3f));
                points.Add(c);
            }

            // The rounding takes a corner's tip off; the post is 100 mm across and 120 mm from the base to the apex as built, the base on the plane h / 3 in front of the middle.
            float minU = points.Min(p => p.u), maxU = points.Max(p => p.u), minW = points.Min(p => p.w), maxW = points.Max(p => p.w);
            float scaleU = RoadPosts.TriangleBase / (maxU - minU), scaleW = h / (maxW - minW);
            for (int i = 0; i < points.Count; i++)
                points[i] = (points[i].u * scaleU, (points[i].w - minW) * scaleW + wApex);
            return points;
        }

        private static (float u, float w) Toward((float u, float w) from, (float u, float w) to, float distance)
        {
            float du = to.u - from.u, dw = to.w - from.w, length = (float)Math.Sqrt(du * du + dw * dw);
            return (from.u + du / length * distance, from.w + dw / length * distance);
        }

        /// <summary>The way a viewer who looks at a face (facing it along -face) has his right: along the ground, square to the face.</summary>
        private static Vec3 ViewRight(Vec3 face) => new Vec3(-face.Z, 0f, face.X);

        /// <summary>The cell a row of figures is set in: the one asked for, narrowed so that the row (the font's own widths of its figures) is no wider than maxWidth. A font whose figures are wide (a player's own) is held to the face.</summary>
        public static float FigureCell(string text, float cell, float maxWidth)
        {
            float row = MarkingLetters.FigureRowWidth(text, cell);
            return !float.IsNaN(row) && row > maxWidth ? cell * (maxWidth / row) : cell;
        }

        /// <summary>
        /// A text as flat figures standing on a face: centred at (cu across to the viewer's right, cy up from the post's foot), on the plane w in front of the post's middle, as high as height and in cells of
        /// cell (narrowed to keep the row within maxWidth). The figures are the bundled font's (MarkingLetters), which read as they do on the ground to a driver coming along +Z: here upright, to the viewer.
        /// </summary>
        private static void Figures(MeshBuilder mesh, string text, Vec3 foot, Vec3 face, float cu, float cy, float w, float cell, float height, float maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return;
            cell = FigureCell(text, cell, maxWidth);
            var flat = new MeshBuilder();
            if (MarkingLetters.AddFigures(flat, text, 0f, 0f, cell, height) == 0)
                return;
            Lay(mesh, flat, foot, face, cu, cy, w);
        }

        /// <summary>
        /// A column of Chinese characters on a face (Fig. 246): each size high and wide (the font's em box), gap between them, the first with its top at top above the foot, centred across at cu, on the plane w
        /// in front of the post's middle.
        /// </summary>
        private static void Characters(MeshBuilder mesh, string text, Vec3 foot, Vec3 face, float cu, float top, float w, float size, float gap)
        {
            var flat = new MeshBuilder();
            for (int i = 0; i < text.Length; i++)
                MarkingLetters.AddCharacter(flat, text[i], 0f, top - size * 0.5f - i * (size + gap), size, size, false);
            if (flat.VertexCount > 0)
                Lay(mesh, flat, foot, face, cu, 0f, w);
        }

        /// <summary>A flat mesh (x across, z up, as the road markings are made) stood on a face, upright to the one who looks at it.</summary>
        private static void Lay(MeshBuilder mesh, MeshBuilder flat, Vec3 foot, Vec3 face, float cu, float cy, float w)
        {
            Vec3 right = ViewRight(face);
            int first = mesh.VertexCount;
            for (int i = 0; i < flat.VertexCount; i++)
            {
                Vec3 v = flat.Positions[i];
                mesh.Vertex(foot + right * (cu + v.X) + face * w + Up * (cy + v.Z), face, flat.U[i], flat.V[i]);
            }

            for (int i = 0; i + 2 < flat.Indices.Count; i += 3)
                mesh.TriFacing(first + flat.Indices[i], first + flat.Indices[i + 1], first + flat.Indices[i + 2], face);
        }

        /// <summary>A box on a face: u0 to u1 across to the viewer's right, y0 to y1 up, from the plane w0 to w1 toward the viewer, all from the foot.</summary>
        private static void FaceBox(MeshBuilder mesh, Vec3 foot, Vec3 face, float u0, float u1, float y0, float y1, float w0, float w1)
        {
            Vec3 right = ViewRight(face), centre = foot + right * ((u0 + u1) * 0.5f) + face * ((w0 + w1) * 0.5f);
            Box(mesh, new Vec3(centre.X, foot.Y, centre.Z), face * -1f, u1 - u0, w1 - w0, y0, y1);
        }

        // ------------------------------------------------------------------ the 柱式轮廓标

        /// <summary>Where the top of the post is at this place across it (m above the foot): it slopes down to the carriageway (lean +1: the carriageway is on the right of the line, -1 on the left, 0 flat).</summary>
        private static float PostRise(float u, int lean) => RoadPosts.PostLength - RoadPosts.Buried - lean * RoadPosts.Slope * u;

        private static void PostBody(MeshBuilder mesh, Vec3 foot, Vec3 right, Vec3 face, int lean)
        {
            int n = s_PostSection.Count;
            var verts = new Vec3[2 * n];
            for (int i = 0; i < n; i++)
            {
                (float u, float w) = s_PostSection[i];
                Vec3 at = foot + right * u + face * w;
                verts[i] = at + Up * -RoadPosts.Buried;
                verts[n + i] = at + Up * PostRise(u, lean);
            }

            var faces = new int[n + 2][];
            faces[0] = new int[n];
            faces[1] = new int[n];
            for (int i = 0; i < n; i++)
            {
                faces[0][i] = i;
                faces[1][i] = n + i;
                faces[2 + i] = new[] { i, (i + 1) % n, n + (i + 1) % n, n + i };
            }

            Solids.Convex(mesh, verts, faces);
        }

        /// <summary>The black mark: a sleeve a few millimetres outside the post, between two planes with the slope of the top (Fig. 287 draws its edges as the top's), 250 mm long, round the whole post.</summary>
        private static void PostBand(MeshBuilder mesh, Vec3 foot, Vec3 right, Vec3 face, int lean)
        {
            const float scale = 1.1f;
            float lo = RoadPosts.BandCentre - RoadPosts.BandLength * 0.5f, hi = RoadPosts.BandCentre + RoadPosts.BandLength * 0.5f;
            int n = s_PostSection.Count;
            for (int i = 0; i < n; i++)
            {
                (float au, float aw) = s_PostSection[i];
                (float bu, float bw) = s_PostSection[(i + 1) % n];
                au *= scale; aw *= scale; bu *= scale; bw *= scale;
                float nu = bw - aw, nw = -(bu - au);
                if (nu * (au + bu) + nw * (aw + bw) < 0f)
                {
                    nu = -nu;
                    nw = -nw;
                }

                Vec3 normal = MeshBuilder.Normalize(right * nu + face * nw);
                Vec3 a = foot + right * au + face * aw, b = foot + right * bu + face * bw;
                int v0 = mesh.Vertex(a + Up * (lo - lean * RoadPosts.Slope * au), normal), v1 = mesh.Vertex(b + Up * (lo - lean * RoadPosts.Slope * bu), normal);
                int v2 = mesh.Vertex(b + Up * (hi - lean * RoadPosts.Slope * bu), normal), v3 = mesh.Vertex(a + Up * (hi - lean * RoadPosts.Slope * au), normal);
                mesh.QuadFacing(v0, v1, v2, v3, normal);
            }
        }

        /// <summary>The 百米牌 on the post's face (Fig. 287, 路64 of GB 5768.2): a green disc with a white edge, the hundred's digit large over a white line and the kilometre under it, in the proportions of 路64.</summary>
        private static void PostPlate(MeshBuilder white, MeshBuilder green, Vec3 foot, Vec3 face, string hundred, string km)
        {
            float d = RoadPosts.PlateDiameter, r = d * 0.5f, flat = RoadPosts.TriangleHeight / 3f;
            // The disc's top is a little under the lowest of the post's top across its width; it stands on the post's face.
            float top = RoadPosts.PostLength - RoadPosts.Buried - RoadPosts.Slope * r - 0.006f, yc = top - r;
            Vec3 centre = foot + Up * yc;
            Solids.Tube(white, centre + face * (flat - 0.0015f), centre + face * (flat + 0.0035f), r, r, 24, false, true);
            Solids.Tube(green, centre + face * (flat - 0.0015f), centre + face * (flat + 0.0060f), r - 0.004f * d / 0.10f, r - 0.004f * d / 0.10f, 24, false, true);
            float front = flat + 0.0090f;
            Figures(white, hundred, foot, face, 0f, yc + 0.14f * d, front, 0.4f * d / 0.82f, 0.5f * d, 0.40f * d);
            FaceBox(white, foot, face, -0.36f * d, 0.36f * d, yc - 0.165f * d - 0.015f * d, yc - 0.165f * d + 0.015f * d, flat + 0.004f, front);
            Figures(white, km, foot, face, 0f, yc - 0.31f * d, front, 0.18f * d * 0.55f, 0.18f * d, 0.44f * d);
        }

        /// <summary>
        /// The 柱式轮廓标 along a line (GB 5768.2-2022 Fig. 287; the post of GB/T 24970-2020): a post every spacing metres, with a 百米牌 on every hundredth metre when plates are on, numbered from kilometre km and
        /// hundred hm. Each stands with its face toward the traffic that comes along the line; its top slopes to the carriageway (lean). The plate is left off where the hundred is 0: a stone stands there.
        /// </summary>
        public static List<MarkingShape> PostDelineators(IList<Vec3> line, float spacing, bool plates, int km, int hm, bool yellow, int lean, float phase)
        {
            var body = new MarkingShape { Solid = SolidColour.White };
            var band = new MarkingShape { Solid = SolidColour.Black };
            var bar = new MarkingShape { Solid = yellow ? SolidColour.Yellow : SolidColour.White, Detail = DetailKind.Reflective };
            var disc = new MarkingShape { Solid = SolidColour.Green };
            var shapes = new List<MarkingShape> { body, band, bar, disc };
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in MarkingStuds.Stations(total, spacing, phase))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s), right = ahead.RightXZ(), face = ahead * -1f;
                PostBody(body.Mesh, p, right, face, lean);
                PostBand(band.Mesh, p, right, face, lean);
                float front = RoadPosts.TriangleHeight / 3f * 1.1f;
                FaceBox(bar.Mesh, p, face, -RoadPosts.BarWidth * 0.5f, RoadPosts.BarWidth * 0.5f, RoadPosts.BandCentre - RoadPosts.BarHeight * 0.5f, RoadPosts.BandCentre + RoadPosts.BarHeight * 0.5f, front - 0.0040f, front + 0.0030f);
                if (!plates)
                    continue;
                int index = RoadPosts.IndexAt(phase + s, spacing);
                if (!RoadPosts.HasPlate(index, spacing))
                    continue;
                PostMark mark = RoadPosts.Hundreds(km, hm, RoadPosts.PlateIndex(index, spacing));
                if (!mark.Stone)
                    PostPlate(body.Mesh, disc.Mesh, p, face, RoadPosts.Digits(mark.Hundred), RoadPosts.Digits(mark.Km));
            }

            return shapes;
        }

        // ------------------------------------------------------------------ 里程碑, 百米桩

        /// <summary>A post of the stones' kind: a prism whose front is a rectangle with its two top corners cut, from the foot (buried below it) to the height, depth deep; both faces are the front.</summary>
        private static void StoneBody(MeshBuilder mesh, Vec3 foot, Vec3 right, Vec3 face, float width, float depth, float height, float chamfer, float buried)
        {
            float hw = width * 0.5f, hd = depth * 0.5f;
            var front = new[] { (u: -hw, y: -buried), (u: hw, y: -buried), (u: hw, y: height - chamfer), (u: hw - chamfer, y: height), (u: -hw + chamfer, y: height), (u: -hw, y: height - chamfer) };
            int n = front.Length;
            var verts = new Vec3[2 * n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = foot + right * front[i].u + face * hd + Up * front[i].y;
                verts[n + i] = foot + right * front[i].u - face * hd + Up * front[i].y;
            }

            var faces = new int[n + 2][];
            faces[0] = new int[n];
            faces[1] = new int[n];
            for (int i = 0; i < n; i++)
            {
                faces[0][i] = i;
                faces[1][i] = n + i;
                faces[2 + i] = new[] { i, (i + 1) % n, n + (i + 1) % n, n + i };
            }

            Solids.Convex(mesh, verts, faces);
        }

        /// <summary>The 里程碑 of Fig. 243: the road's number in a line under the top, and the kilometre in a frame below it, on both faces.</summary>
        private static void Milestone(MeshBuilder white, MeshBuilder ink, Vec3 foot, Vec3 ahead, string code, int km)
        {
            string number = RoadPosts.Digits(km);
            RoadPosts.StoneSize(number.Length, out float width, out float height, out float frameBottom, out float frameHeight, out float buried);
            Vec3 right = ahead.RightXZ();
            StoneBody(white, foot, right, ahead, width, RoadPosts.StoneDepth, height, RoadPosts.StoneChamfer, buried);
            float frameWidth = width - 2f * RoadPosts.StoneChamfer, line = 0.008f, depth = RoadPosts.StoneDepth * 0.5f;
            foreach (Vec3 face in new[] { ahead, ahead * -1f })
            {
                float y0 = frameBottom, y1 = frameBottom + frameHeight, hw = frameWidth * 0.5f;
                FaceBox(ink, foot, face, -hw, hw, y0, y0 + line, depth, depth + 0.003f);
                FaceBox(ink, foot, face, -hw, hw, y1 - line, y1, depth, depth + 0.003f);
                FaceBox(ink, foot, face, -hw, -hw + line, y0, y1, depth, depth + 0.003f);
                FaceBox(ink, foot, face, hw - line, hw, y0, y1, depth, depth + 0.003f);
                float digit = frameHeight * 0.5f;
                Figures(ink, number, foot, face, 0f, (y0 + y1) * 0.5f, depth + 0.0045f, digit * 0.55f, digit, frameWidth - 0.06f);
                Figures(ink, code, foot, face, 0f, height - RoadPosts.StoneChamfer - RoadPosts.CodeHeight * 0.5f, depth + 0.0045f, RoadPosts.CodeHeight * 0.55f, RoadPosts.CodeHeight, width - 0.12f);
            }
        }

        /// <summary>The 百米桩 of Fig. 245: a square post with the hundred's digit on both faces.</summary>
        private static void HundredPost(MeshBuilder white, MeshBuilder ink, Vec3 foot, Vec3 ahead, int hundred)
        {
            StoneBody(white, foot, ahead.RightXZ(), ahead, RoadPosts.HundredSide, RoadPosts.HundredSide, RoadPosts.HundredHeight, RoadPosts.HundredChamfer, RoadPosts.HundredBuried);
            float depth = RoadPosts.HundredSide * 0.5f, y = RoadPosts.HundredHeight - RoadPosts.HundredDigitTop - RoadPosts.HundredDigit * 0.5f;
            foreach (Vec3 face in new[] { ahead, ahead * -1f })
                Figures(ink, RoadPosts.Digits(hundred), foot, face, 0f, y, depth + 0.003f, RoadPosts.HundredDigit * 0.55f, RoadPosts.HundredDigit, RoadPosts.HundredSide - 0.03f);
        }

        /// <summary>
        /// The 里程碑 and 百米桩 along a line (GB 5768.2-2022 8.6.5, 8.6.6): a place every spacing metres, each one hundred metres on in the numbering (a kilometre on, for stones only), from kilometre km and
        /// hundred hm; the stone where the hundred is 0, the post with its digit elsewhere. The figures are the class of the road's colour. A run cut in pieces numbers on by the place's index in the whole run.
        /// </summary>
        public static List<MarkingShape> MilePosts(IList<Vec3> line, MilePostKind kind, PostRoadClass road, string code, int km, int hm, float spacing, float phase)
        {
            var white = new MarkingShape { Solid = SolidColour.White };
            var ink = new MarkingShape { Solid = RoadPosts.ColourOf(road) };
            var shapes = new List<MarkingShape> { white, ink };
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in MarkingStuds.Stations(total, spacing, phase))
            {
                int index = RoadPosts.IndexAt(phase + s, spacing);
                PostMark mark = kind == MilePostKind.Stones ? RoadPosts.Kilometres(km, index) : RoadPosts.Hundreds(km, hm, index);
                if (kind == MilePostKind.Hundreds && mark.Stone)
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                if (mark.Stone)
                    Milestone(white.Mesh, ink.Mesh, p, ahead, code, mark.Km);
                else
                    HundredPost(white.Mesh, ink.Mesh, p, ahead, mark.Hundred);
            }

            return shapes;
        }

        // ------------------------------------------------------------------ 公路界碑, 道口标柱

        /// <summary>The 公路界碑 of Fig. 246: a white square post with its top corners cut, 公路界 in black on both faces, the faces the traffic of both ways looks at.</summary>
        private static void BoundaryStone(MeshBuilder white, MeshBuilder ink, Vec3 foot, Vec3 ahead)
        {
            StoneBody(white, foot, ahead.RightXZ(), ahead, RoadPosts.BoundaryWidth, RoadPosts.BoundaryDepth, RoadPosts.BoundaryHeight, RoadPosts.BoundaryChamfer, RoadPosts.BoundaryBuried);
            float depth = RoadPosts.BoundaryDepth * 0.5f, top = RoadPosts.BoundaryHeight - RoadPosts.BoundaryTopMargin;
            foreach (Vec3 face in new[] { ahead, ahead * -1f })
                Characters(ink, RoadPosts.BoundaryText, foot, face, 0f, top, depth + 0.003f, RoadPosts.BoundaryCharacter, RoadPosts.BoundaryGap);
        }

        /// <summary>
        /// The 公路界碑 along a line (GB 5768.2-2022 8.6.7, Fig. 246): one every spacing metres on the grid of the whole run (a run cut in pieces goes on where the last piece ended), white with 公路界 in black on
        /// the two faces along the road.
        /// </summary>
        public static List<MarkingShape> BoundaryStones(IList<Vec3> line, float spacing, float phase)
        {
            var white = new MarkingShape { Solid = SolidColour.White };
            var ink = new MarkingShape { Solid = SolidColour.Black };
            var shapes = new List<MarkingShape> { white, ink };
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in MarkingStuds.Stations(total, spacing, phase))
                BoundaryStone(white.Mesh, ink.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s));
            return shapes;
        }

        /// <summary>The 道口标柱 of Fig. 247 b: a round post, bands of 20 of red and white reflective film from the top (red, white, red, white), the part in the ground plain white.</summary>
        private static void JunctionPost(MeshBuilder plain, MeshBuilder redFilm, MeshBuilder whiteFilm, Vec3 foot)
        {
            const int sides = 16;
            float r = RoadPosts.JunctionRadius, band = RoadPosts.JunctionBand;
            int bands = (int)Math.Round(RoadPosts.JunctionHeight / band);
            Solids.Tube(plain, foot + Up * -RoadPosts.JunctionBuried, foot, r, r, sides, true, false);
            for (int i = 0; i < bands; i++)
            {
                MeshBuilder film = i % 2 == 0 ? whiteFilm : redFilm;
                Solids.Tube(film, foot + Up * (i * band), foot + Up * ((i + 1) * band), r, r, sides, false, i == bands - 1);
            }
        }

        /// <summary>
        /// The 道口标柱 of one side of a branch's mouth (GB 5768.2-2022 8.6.8, Fig. 247): two posts along the main line's edge, 2 m and 4 m from the line's start (the junction is at its end when junctionAtEnd: the
        /// distances are then counted from there). The line is drawn from where the corner's curve begins and away from the junction. A run cut in pieces makes each post once: the piece that holds its place
        /// (half open, the last piece owns its end); phase is where the piece starts along the whole run and total the run's whole length (0 when it is not known).
        /// </summary>
        public static List<MarkingShape> JunctionPosts(IList<Vec3> line, float phase, float total, bool last, bool junctionAtEnd)
        {
            var plain = new MarkingShape { Solid = SolidColour.White };
            var red = new MarkingShape { Solid = SolidColour.Red, Detail = DetailKind.Reflective };
            var white = new MarkingShape { Solid = SolidColour.White, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { plain, red, white };
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float length = RoadMarkings.Lengths(line, at), whole = total > 0f ? total : phase + length;
            foreach (float d in RoadPosts.JunctionDistances)
            {
                float s = (junctionAtEnd ? whole - d : d) - phase;
                if (s < -1e-4f || s > length + 1e-4f || (s > length - 1e-4f && !last))
                    continue;
                JunctionPost(plain.Mesh, red.Mesh, white.Mesh, RoadMarkings.PointAt(line, at, Math.Max(0f, Math.Min(length, s))));
            }

            return shapes;
        }
    }
}
