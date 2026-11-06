using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.49 (the owner: "加入中式路灯广告牌…多做一些样式。同样支持导入PNG"): the lamp-post ads and decorations (LampAds, SignBuilder.PoleAds): every preset on a lamp post and on a post of its own, its parts
    /// beside the post with the print on both faces, the hardware, the lanterns, the picture, the night glow's flags.
    /// </summary>
    internal static class LampAdTests
    {
        public static void RunAll()
        {
            Program.Run("lamp ads: every preset is sound on a lamp post and on a post", EveryPreset);
            Program.Run("lamp ads: a pair, one each side of the post, printed both sides", Pair);
            Program.Run("lamp ads: the hardware", Hardware);
            Program.Run("lamp ads: lanterns", Lanterns);
            Program.Run("lamp ads: an imported picture", Picture);
            Program.Run("lamp ads: a picture cut where it is clear, or kept", PictureCut);
            Program.Run("lamp ads: lit at night, placed on lamp posts, at their height", LitAndPlaced);
            Program.Run("lamp ads: neon figures, their strips and colours", NeonFigures);
            Program.Run("lamp ads: neon characters thinned from the font", NeonLetters);
            Program.Run("lamp ads: neon strokes cut at their sharp turns", NeonRuns);
            Program.Run("lamp ads: a neon figure is one piece with its skeleton", NeonHeld);
            Program.Run("lamp ads: the light boxes' designs, sound and held, in every shape", BoxDesigns);
            Program.Run("lamp ads: a light box round the post", BoxRoundThePost);
            Program.Run("lamp ads: the light boxes' ornaments glow, their frames do not", BoxGlow);
            Program.Run("lamp ads: the outlines of the designs", BoxOutlines);
        }

        private static IEnumerable<SignChoice> Choices()
        {
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Group == LampAds.Group)
                    yield return choice;
            }
        }

        private static SignParts Build(string id, string overrides = null, string support = SupportCatalog.AttachedPole) => SignBuilder.Build(Of(id, overrides), SupportCatalog.ById(support));

        private static SignInstance Of(string id, string overrides = null)
        {
            SignChoice choice = SignCatalog.ChoiceById(id);
            string spec = choice.Spec;
            if (overrides != null)
            {
                GuideSpec s = choice.Def.Template.Parse(spec);
                foreach (string pair in overrides.Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    if (eq > 0)
                        s = s.With(pair.Substring(0, eq), pair.Substring(eq + 1));
                }

                spec = s.ToString();
            }

            return SignInstances.Of(choice.Def, spec);
        }

        /// <summary>Whole triangles, indices in range, no degenerate one, each facing as its vertices' normals say.</summary>
        private static void Sound(MeshBuilder mesh, string what)
        {
            if (mesh == null || mesh.VertexCount == 0)
                return;
            Program.Check(mesh.Indices.Count % 3 == 0 && mesh.Normals.Count == mesh.VertexCount && mesh.U.Count == mesh.VertexCount, what + ": whole triangles, parallel arrays");
            int bad = 0, degenerate = 0, outside = 0;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount)
                {
                    outside++;
                    continue;
                }

                Vec3 n = MeshBuilder.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                if (n.Length < 1e-10f)
                {
                    degenerate++;
                    continue;
                }

                if (Vec3.Dot(n, mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c]) <= 0f)
                    bad++;
            }

            Program.Check(outside == 0 && degenerate == 0 && bad == 0, $"{what}: {outside} indices out of range, {degenerate} degenerate triangles, {bad} facing against their normals");
            foreach (Vec3 p in mesh.Positions)
            {
                if (!p.IsFinite)
                {
                    Program.Check(false, what + ": a vertex is not finite");
                    break;
                }
            }
        }

        private static void EveryPreset()
        {
            int count = 0;
            foreach (SignChoice choice in Choices())
            {
                count++;
                foreach (string support in new[] { SupportCatalog.AttachedPole, SupportCatalog.SinglePole })
                {
                    string what = choice.Id + " on " + support;
                    SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                    SignParts parts = SignBuilder.Build(instance, SupportCatalog.ById(support));
                    Program.Check(instance.Board?.PoleAd != null, what + ": a lamp-post ad");
                    bool lantern = instance.Board.PoleAd.Kind == PoleAdKind.Lantern || instance.Board.PoleAd.Kind == PoleAdKind.Neon;
                    Program.Check(parts.Faces.Count == 1 && (lantern ? parts.Face.VertexCount == 0 : parts.Face.TriangleCount > 0), what + ": one face, with triangles (a lantern's and a neon figure's are empty: they are built)");
                    Sound(parts.Metal, what + " metal");
                    Sound(parts.Face, what + " face");
                    for (int c = 0; c < parts.Solids.Length; c++)
                        Sound(parts.Solids[c], what + " solid " + (SolidColour)c);
                    Program.Check(parts.Picks.FindAll(p => p.Name == "board").Count == instance.Board.Parts.Count, what + ": a pick for each part");
                    // On a lamp post nothing of a post of ours is built; on a post of its own the post is.
                    Program.Check(support == SupportCatalog.AttachedPole ? !parts.Picks.Exists(p => p.Name == "post") : parts.Picks.Exists(p => p.Name == "post"), what + ": the post only when it is ours");
                    parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                    if (!lantern)
                        Program.Check(fMin.Y >= instance.Def.BottomClearance - 0.02f, what + $": the faces start at the lower edge's height: {fMin.Y:0.00} of {instance.Def.BottomClearance:0.00}");
                }
            }

            Program.Check(count == 46, "46 lamp-post ads and decorations: " + count);
            foreach (GuideTemplate t in LampAds.All())
                Program.Check(!t.Standalone && t.Group == LampAds.Group && Encoding(t.Default().ToString()) <= GuideSpec.MaxBytes, t.Id + ": a template of the presets, its settings fit their field");
        }

        private static int Encoding(string text) => System.Text.Encoding.UTF8.GetByteCount(text);

        private static void Pair()
        {
            SignChoice choice = SignCatalog.ChoiceById("lamp.banner.civic");
            SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
            PoleAd ad = instance.Board.PoleAd;
            SignParts parts = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.AttachedPole));
            float r = parts.Layout.PoleRadius;
            Program.Check(ad.Pair && instance.Board.Parts.Count == 2 && ad.TwoSided, "a pair of banners, printed on both sides");
            List<PickShape> boards = parts.Picks.FindAll(p => p.Name == "board");
            Program.Check(boards.Count == 2 && boards[0].CentreX > 0f && boards[1].CentreX < 0f, "one each side of the post");
            float w = instance.Board.Parts[0].W;
            Program.Near(boards[0].CentreX, r + ad.Gap + w * 0.5f, 1e-4f, "gap from the post's surface");
            Program.Near(-boards[1].CentreX, r + ad.Gap + w * 0.5f, 1e-4f, "on both sides");
            int front = 0, back = 0;
            float zFront = float.NaN, zBack = float.NaN;
            for (int i = 0; i < parts.Face.VertexCount; i++)
            {
                if (parts.Face.Normals[i].Z > 0.99f)
                {
                    front++;
                    zFront = parts.Face.Positions[i].Z;
                }
                else if (parts.Face.Normals[i].Z < -0.99f)
                {
                    back++;
                    zBack = parts.Face.Positions[i].Z;
                }
            }

            Program.Check(front > 0 && front == back, "as many vertices on the back as on the front");
            Program.Near(zFront - zBack, ad.Depth, 1e-5f, "the faces a banner's thickness apart, the post's axis between them");
            Program.Near(zFront + zBack, 0f, 1e-5f, "about the post's plane");

            // The back reads the right way from behind: the vertex at the far +X (the viewer's left from the front) of the +X banner shows the picture's right side on the back.
            int frontLeft = -1, backLeft = -1;
            for (int i = 0; i < parts.Face.VertexCount; i++)
            {
                Vec3 p = parts.Face.Positions[i];
                bool f = parts.Face.Normals[i].Z > 0f;
                ref int best = ref (f ? ref frontLeft : ref backLeft);
                if (best < 0 || p.X > parts.Face.Positions[best].X + 1e-5f || (Math.Abs(p.X - parts.Face.Positions[best].X) < 1e-5f && p.Y > parts.Face.Positions[best].Y))
                    best = i;
            }

            float left = parts.Face.Positions[frontLeft].X;
            Program.Check(parts.Face.U[frontLeft] < 0.1f, "from the front the outer edge of the left banner is the picture's left: " + parts.Face.U[frontLeft]);
            Program.Check(Math.Abs(parts.Face.Positions[backLeft].X - left) < 1e-4f && parts.Face.U[backLeft] > parts.Face.U[frontLeft] + 0.2f, "from behind the same edge is the banner's right side: " + parts.Face.U[backLeft]);

            // One side only: on the sign's +X (the road's side of a post at the right of the traffic).
            SignParts one = Build("lamp.banner.civic", "form=one");
            List<PickShape> single = one.Picks.FindAll(p => p.Name == "board");
            Program.Check(single.Count == 1 && single[0].CentreX > 0f, "one side: on +X");
        }

        private static void Hardware()
        {
            // A banner: an arm at its top and one at its bottom, each from the post past its outer edge; a hoop round the post at each (one for both of a pair).
            SignChoice choice = SignCatalog.ChoiceById("lamp.banner.civic");
            SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
            SignParts parts = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.AttachedPole));
            MeshBuilder steel = parts.Solids[(int)SolidColour.Steel];
            Program.Check(steel != null && steel.VertexCount > 0, "silver hardware");
            steel.Bounds(out Vec3 lo, out Vec3 hi);
            parts.Face.Bounds(out Vec3 fLo, out Vec3 fHi);
            Program.Check(hi.Y > fHi.Y && lo.Y < fLo.Y, "arms above the top edge and below the bottom one");
            Program.Check(hi.X > fHi.X && lo.X < fLo.X, "the arms reach past the outer edges");
            int hoopBand = 0;
            for (int i = 0; i < steel.VertexCount; i++)
            {
                Vec3 p = steel.Positions[i];
                float d = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                if (Math.Abs(d - parts.Layout.PoleRadius - 0.008f) < 1e-3f)
                    hoopBand++;
            }

            Program.Check(hoopBand > 0, "a hoop round the post");
            SignParts black = Build("lamp.banner.civic", "hw=black");
            Program.Check(black.Solids[(int)SolidColour.Black] != null && black.Solids[(int)SolidColour.Black].VertexCount > 0 && (black.Solids[(int)SolidColour.Steel] == null || black.Solids[(int)SolidColour.Steel].VertexCount == 0), "black hardware");

            // A light box: its edge is a frame of the hardware's colour, its depth as set; two arms into its inner side.
            SignParts box = Build("lamp.box.civic", "d=15");
            box.Face.Bounds(out Vec3 bLo, out Vec3 bHi);
            Program.Near(bHi.Z - bLo.Z, 0.15f, 1e-4f, "a box 15 cm deep");
            // A knot hangs by a cord from an arm above it.
            SignParts knot = Build("lamp.knot.one");
            knot.Face.Bounds(out Vec3 kLo, out Vec3 kHi);
            knot.Solids[(int)SolidColour.Steel].Bounds(out Vec3 sLo, out Vec3 sHi);
            Program.Check(sHi.Y > kHi.Y + 0.1f, "the knot's arm is above it");
            // The knot and the LED figures are cut to their outlines: fewer triangles' area than their box.
            Program.Check(knot.Face.TriangleCount > 8, "the knot's face follows its outline");
        }

        private static void Lanterns()
        {
            SignParts pair = Build("lamp.lantern.pair");
            Program.Check(pair.Solids[(int)SolidColour.GlowRed] != null && pair.Solids[(int)SolidColour.GlowRed].VertexCount > 0, "lit red lanterns glow");
            Program.Check(pair.Solids[(int)SolidColour.Gold] != null && pair.Solids[(int)SolidColour.Gold].VertexCount > 0, "with gold caps and tassels");
            SignParts unlit = Build("lamp.lantern.pair", "lit=0");
            Program.Check((unlit.Solids[(int)SolidColour.GlowRed] == null || unlit.Solids[(int)SolidColour.GlowRed].VertexCount == 0) && unlit.Solids[(int)SolidColour.Red].VertexCount > 0, "unlit: plain red");
            SignParts gold = Build("lamp.lantern.long");
            Program.Check(gold.Solids[(int)SolidColour.GlowGold] != null && gold.Solids[(int)SolidColour.GlowGold].VertexCount > 0, "gold lanterns glow gold");
            // A string of three is three times as tall as one, and each string hangs on its side.
            SignParts one = Build("lamp.lantern.pair", "count=1");
            SignParts three = Build("lamp.lantern.pair", "count=3");
            one.Solids[(int)SolidColour.GlowRed].Bounds(out Vec3 oLo, out Vec3 oHi);
            three.Solids[(int)SolidColour.GlowRed].Bounds(out Vec3 tLo, out Vec3 tHi);
            Program.Check(oHi.Y - oLo.Y > 0.2f && tHi.Y - tLo.Y > 2.5f * (oHi.Y - oLo.Y), $"three on a string: {tHi.Y - tLo.Y:0.00} m of body against {oHi.Y - oLo.Y:0.00}");
            Program.Check(oLo.X < 0f && oHi.X > 0f, "a pair: one each side of the post");
            Program.Check(SolidGlow.Is(SolidColour.GlowRed) && SolidGlow.Is(SolidColour.GlowGold) && !SolidGlow.Is(SolidColour.Red) && !SolidGlow.Is(SolidColour.Gold), "the glowing colours");
            SignParts palace = Build("lamp.lantern.palace");
            Program.Check(palace.Solids[(int)SolidColour.GlowRed].VertexCount > 0 && palace.Solids[(int)SolidColour.Gold].VertexCount > 0, "a palace lantern: a body and gold roofs");
        }

        private static bool Uses(SignParts parts, SolidColour colour) => parts.Solids[(int)colour] != null && parts.Solids[(int)colour].VertexCount > 0;

        private static bool Has(NeonArt art, SolidColour colour) => art.Strokes.Exists(s => s.Colour == colour);

        /// <summary>
        /// The owner's photo as a preset (a pair: hills, clouds and waves with characters on one, a red sun on the other), its strips in their colours or unlit, one colour, every shape and pattern
        /// inside its part, the lowest strip at the lower edge, and what a pair costs.
        /// </summary>
        private static void NeonFigures()
        {
            // The boards made before the font was set have no characters.
            MarkingLetters.Font = FontLetterTests.Font;
            SignInstances.ClearCaches();
            SignInstance photo = Of("lamp.neon.shanshui");
            PoleAd ad = photo.Board.PoleAd;
            Program.Check(ad.Kind == PoleAdKind.Neon && ad.Pair && ad.Neon.Count == 2 && photo.Board.Parts.Count == 2, "a pair of neon figures, each with its strokes");
            Program.Check(Has(ad.Neon[0], SolidColour.GlowGold) && Has(ad.Neon[0], SolidColour.GlowBlue) && Has(ad.Neon[0], SolidColour.GlowPink) && Has(ad.Neon[0], SolidColour.GlowGreen)
                && !Has(ad.Neon[0], SolidColour.GlowRed), "the first: hills, clouds and waves");
            Program.Check(Has(ad.Neon[1], SolidColour.GlowRed) && !Has(ad.Neon[1], SolidColour.GlowBlue), "the other: the red sun");
            int frame = 2 + 6;
            Program.Check(ad.Neon[0].Strokes.FindAll(s => s.Colour == SolidColour.GlowWhite).Count > frame + 8, "the first has its characters: " + ad.Neon[0].Strokes.FindAll(s => s.Colour == SolidColour.GlowWhite).Count + " white strokes");
            SignParts parts = SignBuilder.Build(photo, SupportCatalog.ById(SupportCatalog.AttachedPole));
            Program.Check(parts.Face.VertexCount == 0, "no face: the strips are built");
            foreach (SolidColour colour in new[] { SolidColour.GlowWhite, SolidColour.GlowGold, SolidColour.GlowBlue, SolidColour.GlowPink, SolidColour.GlowGreen, SolidColour.GlowRed })
                Program.Check(Uses(parts, colour) && SolidGlow.Is(colour), colour + ": strips that glow");
            int vertices = 0;
            float lowest = float.MaxValue;
            foreach (SolidColour colour in SolidGlow.All)
            {
                if (!Uses(parts, colour))
                    continue;
                vertices += parts.Solids[(int)colour].VertexCount;
                parts.Solids[(int)colour].Bounds(out Vec3 lo, out _);
                lowest = Math.Min(lowest, lo.Y);
            }

            Console.WriteLine($"  neon: the pair of the owner's photo on a lamp post: {vertices} vertices of strips, the lowest {lowest:0.000} m (the lower edge {photo.Def.BottomClearance:0.00} m)");
            Program.Check(vertices < 26000, "a pair costs fewer than 26000 vertices of strips: " + vertices);
            Program.Check(lowest >= photo.Def.BottomClearance - 0.02f && lowest <= photo.Def.BottomClearance + 0.03f, $"the tassel's foot at the lower edge: {lowest:0.000}");
            SignParts unlit = Build("lamp.neon.shanshui", "lit=0");
            Program.Check(Array.TrueForAll(SolidGlow.All, c => !Uses(unlit, c)) && Uses(unlit, SolidColour.White) && Uses(unlit, SolidColour.Blue), "unlit: no strip glows, each in its silicone's colour");
            Program.Check(Of("lamp.neon.warm").Board.PoleAd.Neon[0].Strokes.TrueForAll(s => s.Colour == SolidColour.GlowWarm), "one colour: the pattern is the neon's colour");
            // Every shape with every pattern, one and a pair: the strokes inside their parts, the strips sound.
            foreach (string shape in new[] { "lantern", "round", "haitang", "arch" })
            {
                foreach (string motif in new[] { "shanshui", "sun", "moon", "clouds", "waves", "plum", "none" })
                {
                    string what = shape + " " + motif;
                    SignInstance one = Of("lamp.neon.shanshui", $"shape={shape};motif={motif};motif2=same;form=one;w=45;h=60;line=20");
                    GuidePart part = one.Board.Parts[0];
                    int outside = 0;
                    foreach (NeonStroke stroke in one.Board.PoleAd.Neon[0].Strokes)
                    {
                        foreach (Point2 p in stroke.Points)
                            outside += Math.Abs(p.X) > part.W * 0.5f + 1e-3f || Math.Abs(p.Y) > part.H * 0.5f + 1e-3f ? 1 : 0;
                    }

                    Program.Check(outside == 0 && one.Board.PoleAd.Neon[0].Strokes.Count > 8, $"{what}: {one.Board.PoleAd.Neon[0].Strokes.Count} strokes, {outside} points outside the part");
                    SignParts built = SignBuilder.Build(one, SupportCatalog.ById(SupportCatalog.AttachedPole));
                    for (int c = 0; c < built.Solids.Length; c++)
                        Sound(built.Solids[c], what + " " + (SolidColour)c);
                }
            }

            // The tassel and the strips' width.
            float tall = Of("lamp.neon.shanshui").Board.Parts[0].H, bare = Of("lamp.neon.shanshui", "tassel=0").Board.Parts[0].H;
            Program.Near(tall - bare, Neon.TasselHeight * 0.7f, 1e-4f, "the tassel adds its height");
            Program.Near(Of("lamp.neon.shanshui", "line=30").Board.PoleAd.NeonWidth, 0.03f, 1e-6f, "the strips' width is the setting's");
            SignParts reduced = SignBuilder.Build(photo, SupportCatalog.ById(SupportCatalog.AttachedPole), 0f, SupportDetail.Reduced);
            int few = 0;
            foreach (SolidColour colour in SolidGlow.All)
                few += Uses(reduced, colour) ? reduced.Solids[(int)colour].VertexCount : 0;
            Program.Check(few > 0 && few * 2 <= vertices, $"reduced detail: the strips' fronts and backs only, {few} vertices of {vertices}");
        }

        private static bool Inside(GlyphOutline glyph, float x, float y)
        {
            int winding = 0;
            foreach (float[] contour in glyph.Contours)
            {
                int m = contour.Length / 2;
                for (int k = 0; k < m; k++)
                {
                    int k1 = (k + 1) % m;
                    float ax = contour[2 * k], ay = contour[2 * k + 1], bx = contour[2 * k1], by = contour[2 * k1 + 1];
                    if ((ay <= y) != (by <= y) && x < ax + (bx - ax) * (y - ay) / (by - ay))
                        winding += by > ay ? 1 : -1;
                }
            }

            return winding != 0;
        }

        private static float ToEdge(GlyphOutline glyph, float x, float y)
        {
            float best = float.MaxValue;
            foreach (float[] contour in glyph.Contours)
            {
                int m = contour.Length / 2;
                for (int k = 0; k < m; k++)
                {
                    int k1 = (k + 1) % m;
                    float ax = contour[2 * k], ay = contour[2 * k + 1], dx = contour[2 * k1] - ax, dy = contour[2 * k1 + 1] - ay, l2 = dx * dx + dy * dy;
                    float t = l2 < 1e-9f ? 0f : Math.Max(0f, Math.Min(1f, ((x - ax) * dx + (y - ay) * dy) / l2));
                    float ex = ax + dx * t - x, ey = ay + dy * t - y;
                    best = Math.Min(best, (float)Math.Sqrt(ex * ex + ey * ey));
                }
            }

            return best;
        }

        /// <summary>The characters as single lines on the glyph's ink (the ends lengthened a little past where thinning leaves them), reaching across it; none without the font.</summary>
        private static void NeonLetters()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            foreach (char c in "山水之城宜居新福国口")
            {
                List<List<Point2>> lines = Neon.Letter(c);
                GlyphOutline glyph = MarkingLetters.Outline(c, out float em, out float bottom, out float top);
                Program.Check(glyph != null && lines.Count > 0, c + ": lines");
                if (glyph == null)
                    continue;
                int points = 0, off = 0;
                float length = 0f, x0 = 1f, x1 = -1f, y0 = 1f, y1 = -1f;
                foreach (List<Point2> line in lines)
                {
                    for (int i = 0; i < line.Count; i++)
                    {
                        Point2 p = line[i];
                        points++;
                        float fx = (p.X + 0.5f) * em, fy = bottom + (p.Y + 0.5f) * (top - bottom);
                        off += Inside(glyph, fx, fy) || ToEdge(glyph, fx, fy) < 0.03f * em ? 0 : 1;
                        x0 = Math.Min(x0, p.X);
                        x1 = Math.Max(x1, p.X);
                        y0 = Math.Min(y0, p.Y);
                        y1 = Math.Max(y1, p.Y);
                        if (i > 0)
                            length += (float)Math.Sqrt((p.X - line[i - 1].X) * (p.X - line[i - 1].X) + (p.Y - line[i - 1].Y) * (p.Y - line[i - 1].Y));
                    }
                }

                Program.Check(off == 0, $"{c}: every point on the ink ({off} of {points} off it)");
                float ink = (glyph.MaxX - glyph.MinX) / em, inkH = (glyph.MaxY - glyph.MinY) / (top - bottom);
                Program.Check(x1 - x0 > 0.7f * ink && y1 - y0 > 0.7f * inkH, $"{c}: the lines reach across the character ({x1 - x0:0.00} of {ink:0.00}, {y1 - y0:0.00} of {inkH:0.00})");
                Program.Check(length > 1f && length < 12f && points < 400, $"{c}: single lines, {length:0.0} em long in {points} points");
            }

            List<List<Point2>> box = Neon.Letter('口');
            float around = 0f;
            foreach (List<Point2> line in box)
            {
                for (int i = 1; i < line.Count; i++)
                    around += (float)Math.Sqrt((line[i].X - line[i - 1].X) * (line[i].X - line[i - 1].X) + (line[i].Y - line[i - 1].Y) * (line[i].Y - line[i - 1].Y));
            }

            Program.Check(around > 2f && around < 4f, $"口: one line round the box, {around:0.00} em");
            MarkingLetters.Font = null;
            Program.Check(Neon.Letter('山').Count == 0, "no font: no lines");
            MarkingLetters.Font = FontLetterTests.Font;
            Program.Check(Neon.Letter('山').Count > 0, "the font again: the lines again");
        }

        /// <summary>The segments of a stroke's middle line.</summary>
        private static List<(Point2, Point2)> Segments(NeonStroke s)
        {
            var list = new List<(Point2, Point2)>();
            int n = s.Points.Count, count = s.Closed && n > 2 ? n : n - 1;
            for (int i = 0; i < count; i++)
                list.Add((s.Points[i], s.Points[(i + 1) % n]));
            return list;
        }

        private static float Gap(List<(Point2, Point2)> p, List<(Point2, Point2)> q)
        {
            float best = float.MaxValue;
            foreach ((Point2 a, Point2 b) in p)
            {
                foreach ((Point2 c, Point2 d) in q)
                    best = Math.Min(best, Neon.SegmentDistance(a, b, c, d));
            }

            return best;
        }

        /// <summary>
        /// How many pieces a figure is in: its strips joined where their middle lines are within nine tenths of the strip's width (they overlap), a rod joined to a strip its end or its length is in
        /// (within half the width) and to a rod it meets.
        /// </summary>
        private static int Pieces(NeonArt art, float line)
        {
            var members = new List<List<(Point2, Point2)>>();
            foreach (NeonStroke s in art.Strokes)
                members.Add(Segments(s));
            int strokes = members.Count;
            foreach ((Point2 a, Point2 b) in art.Rods)
                members.Add(new List<(Point2, Point2)> { (a, b) });
            var group = new int[members.Count];
            for (int i = 0; i < group.Length; i++)
                group[i] = i;
            int Find(int i) => group[i] == i ? i : group[i] = Find(group[i]);
            for (int i = 0; i < members.Count; i++)
            {
                for (int j = i + 1; j < members.Count; j++)
                {
                    float within = i < strokes && j < strokes ? 0.9f * line : j >= strokes && i >= strokes ? 2f * Neon.RodRadius : 0.5f * line;
                    if (Gap(members[i], members[j]) <= within + 1e-5f)
                        group[Find(j)] = Find(i);
                }
            }

            var roots = new HashSet<int>();
            for (int i = 0; i < members.Count; i++)
                roots.Add(Find(i));
            return roots.Count;
        }

        private static void NeonHeld()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            SignInstances.ClearCaches();
            int figures = 0, rods = 0, straight = 0;
            foreach (string shape in new[] { "lantern", "round", "haitang", "arch" })
            {
                foreach (string motif in new[] { "shanshui", "sun", "moon", "clouds", "waves", "plum", "none" })
                {
                    foreach (string extra in new[] { "", ";line=8;w=45;h=60", ";line=30;w=120;h=200;tassel=0" })
                    {
                        string what = shape + " " + motif + extra;
                        SignInstance figure = Of("lamp.neon.shanshui", $"shape={shape};motif={motif};motif2=same{extra}");
                        PoleAd ad = figure.Board.PoleAd;
                        for (int i = 0; i < ad.Neon.Count; i++)
                        {
                            NeonArt art = ad.Neon[i];
                            GuidePart part = figure.Board.Parts[i];
                            figures++;
                            int pieces = Pieces(art, ad.NeonWidth);
                            // The pattern and the characters (the strokes all in the body's height but the frame and the hook) are inside the frame.
                            List<Point2> frame = art.Strokes[0].Points;
                            float bottom = float.MaxValue, top = float.MinValue;
                            foreach (Point2 p in frame)
                            {
                                bottom = Math.Min(bottom, p.Y);
                                top = Math.Max(top, p.Y);
                            }

                            int outside = 0;
                            for (int s = 2; s < art.Strokes.Count; s++)
                            {
                                if (art.Strokes[s].Points.TrueForAll(p => p.Y > bottom && p.Y < top))
                                    outside += art.Strokes[s].Points.FindAll(p => !InPolygon(frame, p)).Count;
                            }

                            Program.Check(outside == 0, $"{what} ({i}): the pattern and the characters inside the frame ({outside} points outside)");
                            Program.Check(pieces == 1, $"{what} ({i}): one piece with its {art.Rods.Count} rods, not {pieces}");
                            foreach ((Point2 a, Point2 b) in art.Rods)
                            {
                                rods++;
                                straight += Math.Abs(a.X - b.X) < 1e-5f || Math.Abs(a.Y - b.Y) < 1e-5f ? 1 : 0;
                                Program.Check(Math.Abs(a.X) <= part.W * 0.5f && Math.Abs(b.X) <= part.W * 0.5f && Math.Abs(a.Y) <= part.H * 0.5f && Math.Abs(b.Y) <= part.H * 0.5f,
                                    $"{what}: a rod inside the part");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"  neon skeleton: {figures} figures, {rods} rods, {straight} of them across or up and down");
            Program.Check(straight * 10 >= rods * 9, $"nine of ten rods or more are straight across or up and down: {straight} of {rods}");
            // The rods are built in steel, not in the reduced detail.
            SignInstance photo = Of("lamp.neon.shanshui");
            Program.Check(photo.Board.PoleAd.Neon[0].Rods.Count > 5, "the photo's figure is held by rods: " + photo.Board.PoleAd.Neon[0].Rods.Count);
            SignParts full = SignBuilder.Build(photo, SupportCatalog.ById(SupportCatalog.AttachedPole));
            SignParts far = SignBuilder.Build(photo, SupportCatalog.ById(SupportCatalog.AttachedPole), 0f, SupportDetail.Reduced);
            int steel = full.Solids[(int)SolidColour.Steel].VertexCount, steelFar = far.Solids[(int)SolidColour.Steel].VertexCount;
            Program.Check(steel > steelFar + 12 * photo.Board.PoleAd.Neon[0].Rods.Count, $"the rods in steel: {steel} vertices of steel against {steelFar} in the reduced detail");
            // Without the font the figure is still one piece (no characters).
            MarkingLetters.Font = null;
            SignInstances.ClearCaches();
            SignInstance bare = Of("lamp.neon.shanshui");
            Program.Check(Pieces(bare.Board.PoleAd.Neon[0], bare.Board.PoleAd.NeonWidth) == 1, "no font: one piece");
            MarkingLetters.Font = FontLetterTests.Font;
            SignInstances.ClearCaches();
        }

        private static bool InPolygon(List<Point2> polygon, Point2 p)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y))
                    inside = !inside;
            }

            return inside;
        }

        private static NeonStroke Stroke(bool closed, IEnumerable<Point2> points)
        {
            var stroke = new NeonStroke { Closed = closed, Colour = SolidColour.GlowWhite };
            stroke.Points.AddRange(points);
            return stroke;
        }

        private static IEnumerable<Point2> Ring(int n, float r)
        {
            for (int i = 0; i < n; i++)
                yield return new Point2(r * (float)Math.Cos(2.0 * Math.PI * i / n), r * (float)Math.Sin(2.0 * Math.PI * i / n));
        }

        private static void NeonRuns()
        {
            Program.Check(Neon.Runs(Stroke(true, Ring(32, 0.3f)), out bool loop).Count == 1 && loop, "a circle: one loop");
            Program.Check(Neon.Runs(Stroke(true, Ring(4, 0.3f)), out loop).Count == 1 && loop, "a square: one loop, its corners mitred");
            List<List<Point2>> hairpin = Neon.Runs(Stroke(false, new[] { new Point2(0f, 0f), new Point2(1f, 0f), new Point2(0f, 0.05f) }), out loop);
            Program.Check(hairpin.Count == 2 && !loop && hairpin[0].Count == 2 && hairpin[1].Count == 2, "a hairpin: two runs, cut at its turn");
            List<List<Point2>> triangle = Neon.Runs(Stroke(true, Ring(3, 0.3f)), out loop);
            Program.Check(triangle.Count == 3 && !loop, "a triangle (turns of 120 degrees): three runs, from corner to corner");
            List<List<Point2>> repeated = Neon.Runs(Stroke(false, new[] { new Point2(0f, 0f), new Point2(0f, 0f), new Point2(0.5f, 0f), new Point2(0.5f, 0.0005f), new Point2(1f, 0f) }), out loop);
            Program.Check(repeated.Count == 1 && repeated[0].Count == 3, "repeated points are dropped");
            Program.Check(Neon.Runs(Stroke(false, new[] { new Point2(0f, 0f) }), out loop).Count == 0, "one point: nothing");
        }

        private static void Picture()
        {
            GuideTemplate banner = GuideTemplates.ById(LampAds.BannerId);
            GuideSpec waiting = banner.Default().With("style", "pic");
            GuidePart part = banner.Layout(waiting).Main;
            Program.Check(part.Ground == GuideColour.LightGray && !part.Elements.Exists(e => e.Kind == GuideElementKind.Image), "no picture yet: a grey placeholder");
            GuideSpec with = waiting.With(LampAds.PictureKey, "0123456789abcdef").With("pw", "600").With("ph", "1800");
            GuideBoard board = banner.Layout(with);
            GuideElement image = board.Main.Elements.Find(e => e.Kind == GuideElementKind.Image);
            Program.Check(image != null && image.Text == "0123456789abcdef" && image.Crop && Math.Abs(image.W - board.Main.W) < 1e-5f, "a picture fills the banner (cropped)");
            Program.Check(board.Parts.Count == 2 && board.Parts[1].Elements.Exists(e => e.Kind == GuideElementKind.Image), "both banners of a pair show it");
            GuideElement whole = banner.Layout(with.With("fit", "contain")).Main.Elements.Find(e => e.Kind == GuideElementKind.Image);
            Program.Near(whole.H / whole.W, 3f, 1e-3f, "whole: the picture's proportions");
            Program.Check(banner.FieldOf(LampAds.PictureKey) != null && GuideTemplates.ById(LampAds.BoxId).FieldOf(LampAds.PictureKey) != null && GuideTemplates.ById(LampAds.PanelId).FieldOf(LampAds.PictureKey) != null,
                "banners, boxes and panels take pictures");
            Program.Check(GuideTemplates.ById(LampAds.KnotId).FieldOf(LampAds.PictureKey) == null, "a knot does not");
            Program.Check(LampAds.Wrap("文明出行 礼让行人", 4).Count == 2 && LampAds.Wrap("", 4).Count == 0, "titles are cut into lines at their spaces");
            List<string> five = LampAds.Wrap("城市马拉松", 4);
            Program.Check(five.Count == 2 && five[0] == "城市马" && five[1] == "拉松", "five characters as three and two: " + string.Join("|", five));
            Program.Check(LampAds.Wrap("热线 400-000-0000", 4).Count == 2 && LampAds.Wrap("热线 400-000-0000", 4)[1] == "400-000-0000", "a telephone number is not broken");
        }

        /// <summary>0.12.49 (the owner: "png导入同样可以使用PNG透明裁切/保留"): as a custom sign's picture.</summary>
        private static void PictureCut()
        {
            Func<string, CustomMask> before = CustomSigns.Masks;
            try
            {
                // A picture whose right half is clear.
                var rgba = new byte[64 * 64 * 4];
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 64; x++)
                        rgba[(y * 64 + x) * 4 + 3] = (byte)(x < 32 ? 255 : 0);
                }

                CustomMask half = CustomMask.FromAlpha(rgba, 64, 64);
                CustomSigns.Masks = key => key == "00000000000000aa" ? half : null;
                GuideTemplate banner = GuideTemplates.ById(LampAds.BannerId);
                GuideSpec spec = banner.Default().With("form", "one").With("style", "pic").With(LampAds.PictureKey, "00000000000000aa").With("pw", "64").With("ph", "64").With("fit", "fill");
                Program.Check(banner.FieldOf("cut").Visible(spec) && !banner.FieldOf("cut").Visible(spec.With("style", "vert")), "the cut is a setting of a picture");
                GuidePart kept = banner.Layout(spec).Main;
                Program.Check(kept.Cut == null && !kept.Elements.Find(e => e.Kind == GuideElementKind.Image).Opaque, "kept: the banner is whole, its ground shows through the clear parts");
                Program.Check(banner.Layout(spec.With("bg", "blue")).Main.Ground == GuideColour.Blue, "the ground is the one set");
                GuidePart cut = banner.Layout(spec.With("cut", "1")).Main;
                float area = 0f;
                foreach (List<Point2> loop in cut.Cut ?? new List<List<Point2>>())
                {
                    for (int i = 0; i < loop.Count; i++)
                        area += loop[i].X * loop[(i + 1) % loop.Count].Y - loop[(i + 1) % loop.Count].X * loop[i].Y;
                }

                area = Math.Abs(area) * 0.5f;
                Program.Check(cut.Cut != null && Math.Abs(area - cut.W * cut.H * 0.5f) < 0.02f * cut.W * cut.H && cut.Elements.Find(e => e.Kind == GuideElementKind.Image).Opaque, "cut: the banner is the picture's opaque half: " + area);

                // Built: the face is the cut, both sides; the back maps as the front, so the picture fits the cut from behind.
                SignInstance instance = SignInstances.Of(SignCatalog.ById(LampAds.BannerId), spec.With("cut", "1").ToString());
                SignParts parts = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.AttachedPole));
                float faceArea = 0f;
                MeshBuilder face = parts.Face;
                for (int t = 0; t + 2 < face.Indices.Count; t += 3)
                    faceArea += MeshBuilder.Cross(face.Positions[face.Indices[t + 1]] - face.Positions[face.Indices[t]], face.Positions[face.Indices[t + 2]] - face.Positions[face.Indices[t]]).Length * 0.5f;
                Program.Check(Math.Abs(faceArea - cut.W * cut.H) < 0.04f * cut.W * cut.H, "front and back are the cut: " + faceArea);
                bool same = true;
                for (int i = 0; i < face.VertexCount; i++)
                {
                    if (face.Normals[i].Z >= 0f)
                        continue;
                    for (int j = 0; j < face.VertexCount; j++)
                    {
                        if (face.Normals[j].Z > 0f && Math.Abs(face.Positions[j].X - face.Positions[i].X) < 1e-5f && Math.Abs(face.Positions[j].Y - face.Positions[i].Y) < 1e-5f)
                        {
                            same &= Math.Abs(face.U[j] - face.U[i]) < 1e-5f;
                            break;
                        }
                    }
                }

                Program.Check(same, "the back has the picture where the front has it");

                // An arched light box cuts the picture within its arch.
                GuideTemplate box = GuideTemplates.ById(LampAds.BoxId);
                GuideSpec arch = box.Default().With("form", "one").With("shape", "arch").With("style", "pic").With(LampAds.PictureKey, "00000000000000aa").With("pw", "64").With("ph", "64").With("fit", "fill").With("cut", "1");
                GuidePart arched = box.Layout(arch).Main;
                List<Point2> shape = LampAds.Arch(arched.W, arched.H);
                float cell = Math.Max(arched.W, arched.H) / 160f * 1.5f;
                bool inside = true, leftHalf = true;
                foreach (List<Point2> loop in arched.Cut)
                {
                    foreach (Point2 p in loop)
                    {
                        leftHalf &= p.X <= 0.01f;
                        for (int i = 0; i < shape.Count; i++)
                        {
                            Point2 s0 = shape[i], s1 = shape[(i + 1) % shape.Count];
                            float len = (float)Math.Sqrt((s1.X - s0.X) * (s1.X - s0.X) + (s1.Y - s0.Y) * (s1.Y - s0.Y));
                            if (len > 1e-6f && ((s1.X - s0.X) * (p.Y - s0.Y) - (s1.Y - s0.Y) * (p.X - s0.X)) / len < -cell)
                                inside = false;
                        }
                    }
                }

                Program.Check(leftHalf && inside, "the arched box keeps the opaque half, within its arch");
                GuidePart plainArch = box.Layout(arch.With("cut", "0")).Main;
                Program.Check(plainArch.Cut != null && plainArch.Cut.Count == 1 && plainArch.Cut[0].Count > 10, "kept: the box is its arch");
            }
            finally
            {
                CustomSigns.Masks = before;
            }
        }

        private static void LitAndPlaced()
        {
            foreach ((string id, bool lit) in new[] { ("lamp.box.civic", true), ("lamp.figure.fu", true), ("lamp.knot.pair", true), ("lamp.banner.civic", false), ("lamp.panel.traffic", false) })
            {
                SignChoice choice = SignCatalog.ChoiceById(id);
                Program.Check(SignInstances.Of(choice.Def, choice.Spec).Board.PoleAd.Lit == lit, id + (lit ? ": lit at night" : ": not lit"));
            }

            SignChoice box = SignCatalog.ChoiceById("lamp.box.civic");
            Program.Check(!GuideTemplates.ById(LampAds.BoxId).Layout(GuideTemplates.ById(LampAds.BoxId).Parse(box.Spec).With("lit", "0")).PoleAd.Lit, "a box can be unlit");
            foreach (SignChoice choice in Choices())
            {
                Program.Check(SupportPlanner.Auto(choice.Def).Kind == SupportKind.Attached && SupportPlanner.FitsHost(choice.Def), choice.Id + ": goes on the game's lamp posts by default");
                GuideSpec spec = choice.Def.Template.Parse(choice.Spec);
                SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                // The height set is the lowest point's (0.12.49): what hangs under a light box (a knot, a pendant) is below its parts' lower edge.
                Program.Near(instance.Def.BottomClearance - instance.Board.PoleAd.Below, (float)spec.Number("y", 0.0) / 100f, 1e-4f, choice.Id + ": its lowest point at the height set");
            }

            SignChoice banner = SignCatalog.ChoiceById("lamp.banner.civic");
            SignInstance high = SignInstances.Of(banner.Def, banner.Def.Template.Parse(banner.Spec).With("y", "450").ToString());
            Program.Near(high.Def.BottomClearance, 4.5f, 1e-4f, "set higher: 4.5 m");
        }

        /// <summary>Inside the loops by the even-odd rule (a hole's inside is outside).</summary>
        private static bool InLoops(List<List<Point2>> loops, Point2 p)
        {
            bool inside = false;
            foreach (List<Point2> loop in loops)
                inside ^= InPolygon(loop, p);
            return inside;
        }

        private static bool InPlates(PoleAd ad, Point2 p) => ad.Plates.Exists(plate => InLoops(plate.Loops, p));

        /// <summary>The lowest and the highest point of what is built (the faces and the solids, not the post).</summary>
        private static void Height(SignParts parts, out float lo, out float hi)
        {
            lo = float.MaxValue;
            hi = float.MinValue;
            foreach (MeshBuilder mesh in parts.Solids)
            {
                if (mesh == null || mesh.VertexCount == 0)
                    continue;
                mesh.Bounds(out Vec3 a, out Vec3 b);
                lo = Math.Min(lo, a.Y);
                hi = Math.Max(hi, b.Y);
            }

            parts.Face.Bounds(out Vec3 f0, out Vec3 f1);
            lo = Math.Min(lo, f0.Y);
            hi = Math.Max(hi, f1.Y);
        }

        private static int SolidVertices(SignParts parts)
        {
            int n = parts.Face.VertexCount;
            foreach (MeshBuilder mesh in parts.Solids)
                n += mesh == null ? 0 : mesh.VertexCount;
            return n;
        }

        /// <summary>
        /// 0.12.49 (the owner: "制作更多样式，包括灯箱样式"): every design of a light box in every shape and in three sizes, one and a pair, sound on a lamp post and on a post, in both details; what is round
        /// a box is held (each piece of an ornament or a frame reaches into the box, an arm runs from the post to the box's edge or its frame, a rod's ends are in a frame, on a rod, under a ball or in a
        /// hoop), nothing of it is in the post or through the box's faces, a pair is two mirrored parts, the lowest point is at the height set, the highest within what the support is told, and a pair
        /// is cheap.
        /// </summary>
        private static void BoxDesigns()
        {
            int cases = 0, worst = 0;
            string worstWhat = "";
            foreach (string design in new[] { "arms", "ruyi", "knot", "lattice", "fret", "fan" })
            {
                bool shaped = design != "fan" && design != "lattice";
                foreach (string shape in shaped ? new[] { "rect", "round", "arch", "oval", "circle" } : new[] { "rect" })
                {
                    foreach (string size in new[] { "w=60;h=120;d=12", "w=40;h=50;d=5", "w=120;h=200;d=30" })
                    {
                        foreach (string form in new[] { "pair", "one" })
                        {
                            string what = $"{design} {shape} {size} {form}";
                            SignInstance box = Of("lamp.box.civic", $"design={design};shape={shape};{size};form={form};gap=3;y=320");
                            PoleAd ad = box.Board.PoleAd;
                            GuidePart part = box.Board.Parts[0];
                            List<Point2> outline = part.Cut != null ? part.Cut[0] : GuideKit.PartOutline(part);
                            float post = part.W * 0.5f + ad.Gap;
                            cases++;
                            Program.Check(ad.Kind == PoleAdKind.Box && box.Board.Parts.Count == (form == "pair" ? 2 : 1) && !ad.Wrap, what + ": the parts");
                            foreach (PoleAdPlate plate in ad.Plates)
                            {
                                int outers = 0;
                                foreach (List<Point2> loop in plate.Loops)
                                {
                                    Program.Check(loop.Count >= 3 && loop.TrueForAll(p => !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.X) && !float.IsInfinity(p.Y)), what + ": a loop of finite points");
                                    if (CutOutline.Area(loop) <= 0f)
                                        continue;
                                    outers++;
                                    // A piece reaches into the box by its outside or by the edge of a hole in it (a frame round the box).
                                    bool into = loop.Exists(p => LampBoxes.Polygon(p.X, p.Y, outline) < -0.003f) || plate.Loops.Exists(hole =>
                                        CutOutline.Area(hole) < 0f && InPolygon(loop, hole[0]) && hole.Exists(p => LampBoxes.Polygon(p.X, p.Y, outline) < -0.003f));
                                    Program.Check(into, what + ": each piece of an ornament or a frame reaches into the box");
                                }

                                // The frets above and below are two pieces; any other plate is one.
                                Program.Check(design == "fret" ? outers == 2 : outers == 1, $"{what}: a plate of one piece ({outers})");
                                Program.Check(plate.Loops.TrueForAll(l => l.TrueForAll(p => p.X <= post + 1e-4f)), what + ": nothing of a plate in the post");
                                Program.Check(plate.Half > 0f && Math.Abs(plate.Z) + plate.Half <= ad.Depth * 0.5f - 0.004f, what + ": a plate inside the box's faces");
                            }

                            foreach (PoleAdArm arm in ad.Arms)
                            {
                                var end = new Point2(arm.Reach - 0.003f, arm.Y);
                                Program.Check(arm.Reach < post && (InPolygon(outline, end) || InPlates(ad, end)), $"{what}: an arm from the post to the box or its frame (it ends at {arm.Reach:0.000}, the post at {post:0.000})");
                                Program.Check(arm.Along >= 0.025f && arm.Half >= 0.012f, what + ": its bracket");
                            }

                            foreach (PoleAdRod rod in ad.Rods)
                            {
                                foreach (Point2 p in new[] { rod.Path[0], rod.Path[rod.Path.Count - 1] })
                                {
                                    bool inHoop = ad.Hoops.Exists(y => Math.Abs(p.Y + LampAds.HoopHeight * 0.5f - y) < 1e-4f) && p.X + rod.Radius <= post + 1e-4f && p.X + rod.Radius >= post - 0.005f;
                                    bool onRod = ad.Rods.Exists(o => o != rod && Neon.SegmentDistance(o.Path[0], o.Path[o.Path.Count - 1], p, p) <= o.Radius);
                                    bool underBall = ad.Balls.Exists(b => Math.Abs(b.Centre.X - p.X) < 1e-4f && Math.Abs(b.Centre.Y - p.Y) < 1e-4f);
                                    Program.Check(InPlates(ad, p) || inHoop || onRod || underBall, $"{what}: a rod's end ({p.X:0.000}, {p.Y:0.000}) is held");
                                }

                                Program.Check(rod.Path.TrueForAll(p => p.X + rod.Radius <= post + 1e-4f), what + ": a rod beside the post");
                            }

                            foreach ((Point2 centre, float radius, SolidColour _) in ad.Balls)
                                Program.Check(ad.Rods.Exists(o => Math.Abs(o.Path[0].X - centre.X) < 1e-4f && Math.Abs(o.Path[0].Y - centre.Y) < 1e-4f && o.Radius < radius), what + ": a ball on a rod's end");
                            Program.Check(ad.Arms.Count > 0 || ad.Hoops.Count > 0, what + ": held to the post");

                            foreach (string support in new[] { SupportCatalog.AttachedPole, SupportCatalog.SinglePole })
                            {
                                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                                {
                                    SignParts parts = SignBuilder.Build(box, SupportCatalog.ById(support), 0f, detail);
                                    Sound(parts.Face, what + " face");
                                    Sound(parts.Metal, what + " metal");
                                    for (int c = 0; c < parts.Solids.Length; c++)
                                        Sound(parts.Solids[c], $"{what} on {support} {detail}: {(SolidColour)c}");
                                }
                            }

                            SignParts full = SignBuilder.Build(box, SupportCatalog.ById(SupportCatalog.AttachedPole));
                            SignParts far = SignBuilder.Build(box, SupportCatalog.ById(SupportCatalog.AttachedPole), 0f, SupportDetail.Reduced);
                            Height(full, out float lo, out float hi);
                            float boardTop = full.Layout.BoardTop;
                            Program.Check(lo >= ad.Clearance - 0.006f && lo <= ad.Clearance + 0.03f, $"{what}: the lowest point {lo:0.000} at the height set {ad.Clearance:0.00}");
                            Program.Check(hi <= boardTop + box.Def.ExtraTop + 0.006f, $"{what}: the highest point {hi:0.000} within the top the support is told, {boardTop + box.Def.ExtraTop:0.000}");
                            int vertices = SolidVertices(full), few = SolidVertices(far);
                            if (form == "pair" && size.StartsWith("w=60", StringComparison.Ordinal) && vertices > worst)
                            {
                                worst = vertices;
                                worstWhat = what;
                            }

                            Program.Check(few < vertices, $"{what}: fewer vertices far away ({few} of {vertices})");
                            if (form == "pair")
                            {
                                float x0 = float.MaxValue, x1 = float.MinValue;
                                foreach (MeshBuilder mesh in full.Solids)
                                {
                                    if (mesh == null || mesh.VertexCount == 0)
                                        continue;
                                    mesh.Bounds(out Vec3 a, out Vec3 b);
                                    x0 = Math.Min(x0, a.X);
                                    x1 = Math.Max(x1, b.X);
                                }

                                Program.Check(Math.Abs(x0 + x1) < 0.002f, $"{what}: a pair mirrored about the post ({x0:0.000} to {x1:0.000})");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"  light boxes: {cases} designs, shapes and sizes; the dearest pair of the stock size {worstWhat}, {worst} vertices");
            Program.Check(cases == 4 * 5 * 3 * 2 + 2 * 3 * 2, "every design, shape and size: " + cases);
            Program.Check(worst < 9000, $"a pair of light boxes of the stock size costs fewer than 9000 vertices: {worstWhat} {worst}");
            foreach (string id in new[] { "lamp.box.ruyi", "lamp.box.knot", "lamp.box.lattice", "lamp.box.fret", "lamp.box.fan", "lamp.box.wrap" })
            {
                SignParts parts = Build(id);
                Console.WriteLine($"  {id}: {SolidVertices(parts)} vertices, {SolidVertices(SignBuilder.Build(Of(id), SupportCatalog.ById(SupportCatalog.AttachedPole), 0f, SupportDetail.Reduced))} far away");
            }
        }

        /// <summary>
        /// 抱柱灯箱 (dl-63): one box, its middle on the post's axis, the post through it with a collar where it goes in and where it comes out, deep enough that the post and its collars are inside it, the
        /// crests in front of the post and behind it (clear of it), no arms; one even when a pair is asked for, the pair and the room to the post not shown.
        /// </summary>
        private static void BoxRoundThePost()
        {
            float r = SupportLayouts.HostRadius;
            foreach (string id in new[] { "lamp.box.wrap", "lamp.box.wrapplain" })
            {
                SignInstance box = Of(id, "form=pair;d=8");
                PoleAd ad = box.Board.PoleAd;
                Program.Check(ad.Wrap && !ad.Pair && box.Board.Parts.Count == 1 && ad.Arms.Count == 0, id + ": one box round the post, no arms");
                Program.Check(ad.Depth >= LampAds.WrapDepth && ad.Depth * 0.5f >= r + 0.012f + 0.03f, $"{id}: deep enough for the post and its collars ({ad.Depth:0.00} m)");
                foreach (PoleAdPlate plate in ad.Plates)
                    Program.Check(Math.Abs(plate.Z) - plate.Half >= r + 0.012f && Math.Abs(plate.Z) + plate.Half <= ad.Depth * 0.5f - 0.004f, id + ": the crests clear of the post and inside the faces");
                SignParts parts = Build(id, "form=pair;d=8");
                List<PickShape> boards = parts.Picks.FindAll(p => p.Name == "board");
                Program.Check(boards.Count == 1 && Math.Abs(boards[0].CentreX) < 1e-4f, id + ": the box's middle on the post");
                MeshBuilder steel = parts.Solids[(int)SolidColour.Steel];
                int collar = 0;
                for (int i = 0; i < steel.VertexCount; i++)
                {
                    Vec3 p = steel.Positions[i];
                    collar += Math.Abs((float)Math.Sqrt(p.X * p.X + p.Z * p.Z) - r - 0.012f) < 1e-3f ? 1 : 0;
                }

                Program.Check(collar > 0, id + ": collars round the post");
                steel.Bounds(out Vec3 sLo, out Vec3 sHi);
                parts.Face.Bounds(out Vec3 fLo, out Vec3 fHi);
                Program.Check(sLo.Y < fLo.Y + 0.01f && sHi.Y > fHi.Y - 0.01f, id + ": a collar at the top and one at the bottom");
                GuideTemplate template = GuideTemplates.ById(LampAds.BoxId);
                GuideSpec spec = template.Parse(box.Spec);
                Program.Check(!template.FieldOf("form").Visible(spec) && !template.FieldOf("gap").Visible(spec) && template.FieldOf("y").Visible == null && template.FieldOf("y").SectionZh != null,
                    id + ": the pair and the room to the post not shown, the lower edge heads the mounting");
                foreach (string support in new[] { SupportCatalog.AttachedPole, SupportCatalog.SinglePole })
                {
                    SignParts on = Build(id, null, support);
                    for (int c = 0; c < on.Solids.Length; c++)
                        Sound(on.Solids[c], id + " on " + support + " " + (SolidColour)c);
                }
            }

            Program.Check(!Of("lamp.box.fan", "wrap=1").Board.PoleAd.Wrap && !Of("lamp.box.lattice", "wrap=1").Board.PoleAd.Wrap, "a fan and a round box in a lattice are never round the post");
        }

        private static void BoxGlow()
        {
            foreach ((string id, bool glows) in new[] { ("lamp.box.ruyi", true), ("lamp.box.wrap", true), ("lamp.box.knot", true), ("lamp.box.lattice", false), ("lamp.box.fret", false), ("lamp.box.fan", false) })
            {
                SignParts parts = Build(id);
                bool any = Array.Exists(SolidGlow.All, c => Uses(parts, c));
                Program.Check(Of(id).Board.PoleAd.Lit && any == glows, id + (glows ? ": its face and its ornaments glow at night" : ": its face glows at night, its painted frame does not"));
                SignParts unlit = Build(id, "lit=0");
                Program.Check(!Of(id, "lit=0").Board.PoleAd.Lit && !Array.Exists(SolidGlow.All, c => Uses(unlit, c)), id + ": unlit, nothing glows");
            }

            Program.Check(Uses(Build("lamp.box.ruyi"), SolidColour.GlowRed) && Uses(Build("lamp.box.ruyi", "trim=gold"), SolidColour.GlowGold) && Uses(Build("lamp.box.ruyi", "lit=0"), SolidColour.Red),
                "the crests glow red, or gold; unlit they are red");
            Program.Check(Uses(Build("lamp.box.fangold"), SolidColour.Gold) && Uses(Build("lamp.box.fret"), SolidColour.Black), "a gold fan's frame is gold, the frets the hardware's black");
            // The edge of a box with a knot is the knot's red (lit, the knot glows and the edge does not); a plain box's is the hardware's.
            Program.Check(Uses(Build("lamp.box.knot"), SolidColour.Red) && !Uses(Build("lamp.box.knot", "design=arms"), SolidColour.Red), "the knot's box has a red edge, a plain one not");
        }

        /// <summary>The outlines of the designs (LampBoxes): traced fields and unions of rectangles, the fan's measures, every ornament one piece.</summary>
        private static void BoxOutlines()
        {
            List<List<Point2>> disc = LampBoxes.Contour((x, y) => LampBoxes.Circle(x, y, 0f, 0f, 0.3f), -0.32f, -0.32f, 0.32f, 0.32f, 0.005f);
            Program.Check(disc.Count == 1 && Math.Abs(CutOutline.Area(disc[0]) - (float)Math.PI * 0.09f) < 0.01f * (float)Math.PI * 0.09f, "a disc: one loop, anticlockwise, of its area: " + (disc.Count > 0 ? CutOutline.Area(disc[0]) : 0f));
            List<List<Point2>> ring = LampBoxes.Contour(LampBoxes.Minus((x, y) => LampBoxes.Circle(x, y, 0f, 0f, 0.3f), (x, y) => LampBoxes.Circle(x, y, 0f, 0f, 0.2f)), -0.32f, -0.32f, 0.32f, 0.32f, 0.005f);
            Program.Check(ring.Count == 2 && ring.Exists(l => CutOutline.Area(l) > 0f) && ring.Exists(l => CutOutline.Area(l) < 0f), "a ring: an outer loop and a hole");
            List<List<Point2>> frame = LampBoxes.Rects(new[] { (0f, 0f, 1f, 0.1f), (0f, 0.9f, 1f, 1f), (0f, 0f, 0.1f, 1f), (0.9f, 0f, 1f, 1f) });
            float area = 0f;
            foreach (List<Point2> l in frame)
                area += CutOutline.Area(l);
            Program.Check(frame.Count == 2 && frame.TrueForAll(l => l.Count == 4) && Math.Abs(area - 0.36f) < 1e-5f, $"four bars: a square frame, its corners exact ({frame.Count} loops, {area:0.0000} m2)");
            List<List<Point2>> el = LampBoxes.Rects(new[] { (0f, 0f, 0.5f, 0.1f), (0f, 0f, 0.1f, 0.4f) });
            Program.Check(el.Count == 1 && el[0].Count == 6 && Math.Abs(CutOutline.Area(el[0]) - 0.08f) < 1e-5f, "an L of two bars: six corners");
            List<List<Point2>> mirrored = LampBoxes.Mirrored(el);
            Program.Check(Math.Abs(CutOutline.Area(mirrored[0]) - CutOutline.Area(el[0])) < 1e-6f && mirrored[0].TrueForAll(p => p.X <= 0f), "mirrored: the same area, on the other side");

            List<Point2> fan = LampBoxes.Fan(0.9f, 0f);
            Program.Check(fan.Exists(p => Math.Abs(p.X - 0.45f) < 1e-3f && Math.Abs(p.Y) < 1e-3f) && fan.Exists(p => Math.Abs(p.X + 0.216f) < 2e-3f && Math.Abs(p.Y + 0.3906f) < 2e-3f), "the fan's corners");
            Program.Near(LampBoxes.EdgeY(fan, 0f, true), 0.257f * 0.9f, 2e-3f, "the fan's top rises a quarter of its width");
            Program.Near(LampBoxes.EdgeY(fan, 0f, false), (-0.434f + 0.143f) * 0.9f, 2e-3f, "its bottom bulges up");
            List<Point2> face = LampBoxes.Fan(0.9f, 0.072f);
            Program.Check(face.TrueForAll(p => LampBoxes.Polygon(p.X, p.Y, fan) < -0.07f && LampBoxes.Polygon(p.X, p.Y, fan) > -0.074f), "the face inside the frame by the band's width");
            Program.Check(CutOutline.Area(fan) > 0f && CutOutline.Area(face) > 0f, "anticlockwise");

            var rect = new List<Point2> { new Point2(-0.3f, -0.6f), new Point2(0.3f, -0.6f), new Point2(0.3f, 0.6f), new Point2(-0.3f, 0.6f) };
            Program.Near(LampBoxes.EdgeX(rect, 0.1f), 0.3f, 1e-6f, "the side of a box at a height");
            Program.Near(LampBoxes.EdgeY(rect, 0.1f, false), -0.6f, 1e-6f, "its bottom");
            Program.Check(float.IsNaN(LampBoxes.EdgeX(rect, 0.7f)), "no side above the box");

            foreach ((string name, Func<float, float, float> field, float x0, float y0, float x1, float y1) in new (string, Func<float, float, float>, float, float, float, float)[]
            {
                ("a crest", LampBoxes.Crest(0.5f, 0.2f, 0.02f), -0.26f, -0.03f, 0.26f, 0.22f),
                ("a wide crest", LampBoxes.Crest(1.2f, 0.2f, 0.02f), -0.61f, -0.03f, 0.61f, 0.22f),
                ("a knot", LampBoxes.Knot(0.15f, 2, 0.075f, true), -0.24f, -0.45f, 0.24f, 0.24f),
                ("a pendant", LampBoxes.Pendant(0.5f, 0.07f, 0.5f, 0.02f, out _), -0.26f, -0.52f, 0.26f, 0.03f),
                ("a narrow pendant", LampBoxes.Pendant(0.15f, 0.045f, 0.6f, 0.02f, out _), -0.09f, -0.62f, 0.09f, 0.03f),
                ("a fan's frame", LampBoxes.FanFrame(0.9f, 0.072f, 0.012f, 0.153f, -0.558f, 0.135f), -0.46f, -0.95f, 0.46f, 0.42f)
            })
            {
                List<List<Point2>> loops = LampBoxes.Contour(field, x0, y0, x1, y1, 0.004f);
                int outers = loops.FindAll(l => CutOutline.Area(l) > 0f).Count;
                LampBoxes.Bounds(loops, out float bx0, out float by0, out float bx1, out float by1);
                Program.Check(outers == 1 && bx0 > x0 && bx1 < x1 && by0 > y0 && by1 < y1, $"{name}: one piece inside its box ({outers} pieces)");
            }
        }
    }
}
