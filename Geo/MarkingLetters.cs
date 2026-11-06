using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The words and figures that are painted on the road (停, 公交, 铁路, 出租车, the digits of a limit or of a time) as flat meshes made from the outlines of the bundled font (FontOutlines): each
    /// character is triangulated once (its outer contours and holes told apart by their winding, Polygon.Fill) and scaled into the box a marking gives it. In a box the character is upright for a
    /// driver coming toward +Z: its top toward +Z, its right toward +X. The characters of the standard's road lettering are a narrow heavy face with a height to width of 2.5:1 to 3:1 (5.14.1, Table 3);
    /// the font is not that face, so a word is the font's characters stretched into those boxes: that is what stands in for the standard's letterforms. Pure once the font is set.
    /// </summary>
    public static class MarkingLetters
    {
        private static FontOutlines s_Font, s_Latin;
        private static bool s_Tried, s_LatinTried;

        /// <summary>Changes whenever the font does: what was drawn with another font is drawn again.</summary>
        public static int Version { get; private set; }

        private static readonly Dictionary<int, Cell> s_Cells = new Dictionary<int, Cell>();

        /// <summary>
        /// The font the words are made from, all of them (a test, the preview tools): null until one is set or read from SignFonts.Cjk, and then no word is drawn. A font set here is the only one: the
        /// letters and figures are its own too.
        /// </summary>
        public static FontOutlines Font
        {
            get => s_Font;
            set
            {
                lock (s_Cells)
                {
                    s_Font = value;
                    s_Tried = value != null;
                    s_Latin = null;
                    s_LatinTried = true;
                    s_Cells.Clear();
                    Version++;
                }
            }
        }

        /// <summary>Lets the fonts and the triangulated characters go (a city is left); the next word reads them again from SignFonts (8 to 9 MB each: read when the first word is drawn, not at the start).</summary>
        public static void Release()
        {
            lock (s_Cells)
            {
                s_Font = s_Latin = null;
                s_Tried = s_LatinTried = false;
                s_Cells.Clear();
                Version++;
            }
        }

        private static FontOutlines Read(string path)
        {
            if (path == null)
                return null;
            try
            {
                var read = new FontOutlines(System.IO.File.ReadAllBytes(path));
                return read.Ok ? read : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The Chinese font, read from SignFonts.Cjk the first time it is wanted; null when there is none or it cannot be read (and not tried again until Release).</summary>
        private static FontOutlines Ensure()
        {
            FontOutlines font = s_Font;
            if (font != null || s_Tried)
                return font;
            lock (s_Cells)
            {
                if (s_Font != null || s_Tried)
                    return s_Font;
                s_Tried = true;
                s_Font = Read(SignFonts.Cjk);
                if (s_Font != null)
                {
                    s_Cells.Clear();
                    Version++;
                }

                return s_Font;
            }
        }

        /// <summary>The letters' and figures' font (0.12.28, SignFonts.Latin), read with the Chinese one; null: the Chinese font's own letters.</summary>
        private static FontOutlines EnsureLatin()
        {
            if (s_LatinTried)
                return s_Latin;
            lock (s_Cells)
            {
                if (!s_LatinTried)
                {
                    s_LatinTried = true;
                    s_Latin = Read(SignFonts.Latin);
                }

                return s_Latin;
            }
        }

        /// <summary>One character triangulated in font units: x to the right, y up (as Vec3 x, 0, y), the box the glyph's ink fills, and the em of the font it is from (the two fonts' may differ).</summary>
        private sealed class Cell
        {
            public Vec3[] Vertices;
            public int[] Triangles;
            public float MinX, MaxX, MinY, MaxY, Advance, Em;

            /// <summary>The box a Chinese character of its font is designed on (FontOutlines.EmBottom, EmTop).</summary>
            public float EmBottom, EmTop;
        }

        /// <summary>Whether a character has an outline in the font.</summary>
        public static bool Has(char c) => Cell0(c) != null;

        /// <summary>
        /// A character's outline in the Chinese font (0.12.49, the neon letters: Signs.Neon.Letter), with the em and the box it is designed on (font units, y up: bottom to top, 0 to em across); null
        /// without the font or the character.
        /// </summary>
        public static GlyphOutline Outline(char c, out float em, out float bottom, out float top)
        {
            em = 1000f;
            bottom = 0f;
            top = em;
            FontOutlines font = Ensure();
            if (font == null || !font.Ok || !font.TryGlyph(c, out GlyphOutline glyph) || glyph.IsEmpty)
                return null;
            em = font.UnitsPerEm;
            bottom = font.EmBottom;
            top = font.EmTop;
            return glyph;
        }

        private static Cell Cell0(int codepoint)
        {
            FontOutlines font = Ensure();
            if (font == null || !font.Ok)
                return null;
            lock (s_Cells)
            {
                if (s_Cells.TryGetValue(codepoint, out Cell hit))
                    return hit;
            }

            // A letter or a figure from the letters' font when there is one and it has it, else the Chinese font's.
            GlyphOutline glyph = null;
            FontOutlines from = null;
            FontOutlines latin = codepoint < 0x2E80 ? EnsureLatin() : null;
            if (latin != null && latin.TryGlyph(codepoint, out GlyphOutline letter) && !letter.IsEmpty)
            {
                glyph = letter;
                from = latin;
            }
            else if (font.TryGlyph(codepoint, out GlyphOutline own) && !own.IsEmpty)
            {
                glyph = own;
                from = font;
            }

            Cell made = glyph != null ? Triangulate(glyph) : null;
            if (made != null)
            {
                made.Em = from.UnitsPerEm;
                made.EmBottom = from.EmBottom;
                made.EmTop = from.EmTop;
            }
            lock (s_Cells)
                s_Cells[codepoint] = made;
            return made;
        }

        /// <summary>The share of a figure's cell that the ink of a 0 fills, and the gap between two figures as a share of the cell (Fig. 48 reads about 0.82 and 0.12).</summary>
        public const float FigureInk = 0.82f, FigureGap = 0.12f;

        /// <summary>
        /// How wide a row of figures is as AddFigures lays it out for a cell width (m): the font's own widths of the figures (a 4 may be wider than the 0 the cell is made for: in the traffic sign font
        /// it is, 0.12.28). NaN when the font has no 0 (there is no row then).
        /// </summary>
        public static float FigureRowWidth(string text, float cellWidth)
        {
            Cell zero = Cell0('0');
            if (zero == null || string.IsNullOrEmpty(text))
                return float.NaN;
            float sx = cellWidth * FigureInk / Math.Max(1f, zero.MaxX - zero.MinX), total = 0f;
            int n = 0;
            foreach (char c in text)
            {
                Cell cell = Cell0(c);
                if (cell == null)
                    continue;
                total += (cell.MaxX - cell.MinX) * sx;
                n++;
            }

            return n == 0 ? 0f : total + cellWidth * FigureGap * (n - 1);
        }

        /// <summary>
        /// A row of figures (the speed of 线34): each keeps the width the font gives it, all squeezed by the same amount so that the ink of a 0 is cellWidth x FigureInk wide and the figures are height tall
        /// (the figures of Fig. 48 are three times as tall as the cell is wide, Table 3), side by side with FigureGap of the cell between, the row centred on (centreX, centreZ). Returns how many were drawn.
        /// </summary>
        public static int AddFigures(MeshBuilder mesh, string text, float centreX, float centreZ, float cellWidth, float height)
        {
            Cell zero = Cell0('0');
            if (zero == null || string.IsNullOrEmpty(text) || !(cellWidth > 0f) || !(height > 0f))
                return 0;
            float sx = cellWidth * FigureInk / Math.Max(1f, zero.MaxX - zero.MinX), sz = height / Math.Max(1f, zero.MaxY - zero.MinY);
            float gap = cellWidth * FigureGap;
            var cells = new List<Cell>();
            float total = 0f;
            foreach (char c in text)
            {
                Cell cell = Cell0(c);
                if (cell == null)
                    continue;
                cells.Add(cell);
                total += (cell.MaxX - cell.MinX) * sx;
            }

            if (cells.Count == 0)
                return 0;
            total += gap * (cells.Count - 1);
            var up = new Vec3(0f, 1f, 0f);
            float x = centreX - total * 0.5f;
            foreach (Cell cell in cells)
            {
                int first = mesh.VertexCount;
                foreach (Vec3 v in cell.Vertices)
                    mesh.Vertex(new Vec3(x + (v.X - cell.MinX) * sx, 0f, centreZ - height * 0.5f + (v.Z - zero.MinY) * sz), up, v.X * 0.001f, v.Z * 0.001f);
                for (int i = 0; i + 2 < cell.Triangles.Length; i += 3)
                    mesh.TriFacing(first + cell.Triangles[i], first + cell.Triangles[i + 1], first + cell.Triangles[i + 2], up);
                x += (cell.MaxX - cell.MinX) * sx + gap;
            }

            return cells.Count;
        }

        /// <summary>The contours as outers and holes (the dominant winding is the outer's), each hole given to the smallest outer that holds it, and every outer with its holes filled.</summary>
        private static Cell Triangulate(GlyphOutline glyph)
        {
            var loops = new List<List<Vec3>>();
            var areas = new List<float>();
            foreach (float[] c in glyph.Contours)
            {
                var loop = new List<Vec3>(c.Length / 2);
                for (int i = 0; i + 1 < c.Length; i += 2)
                    loop.Add(new Vec3(c[i], 0f, c[i + 1]));
                loops.Add(loop);
                areas.Add(Polygon.SignedArea2(loop) * 0.5f);
            }

            int largest = 0;
            for (int i = 1; i < loops.Count; i++)
            {
                if (Math.Abs(areas[i]) > Math.Abs(areas[largest]))
                    largest = i;
            }

            bool outerPositive = areas[largest] > 0f;
            var outers = new List<int>();
            var holes = new List<int>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (Math.Abs(areas[i]) < 1e-3f)
                    continue;
                if ((areas[i] > 0f) == outerPositive)
                    outers.Add(i);
                else
                    holes.Add(i);
            }

            var holesOf = new Dictionary<int, List<IList<Vec3>>>();
            foreach (int h in holes)
            {
                int owner = -1;
                float ownerArea = float.MaxValue;
                foreach (int o in outers)
                {
                    if (Math.Abs(areas[o]) < ownerArea && Contains(loops[o], loops[h][0]))
                    {
                        owner = o;
                        ownerArea = Math.Abs(areas[o]);
                    }
                }

                // A hole that no outer holds is a contour of its own (the font drew two shapes the other way round).
                if (owner < 0)
                {
                    outers.Add(h);
                    continue;
                }

                if (!holesOf.TryGetValue(owner, out List<IList<Vec3>> list))
                    holesOf[owner] = list = new List<IList<Vec3>>();
                list.Add(loops[h]);
            }

            var mesh = new MeshBuilder();
            foreach (int o in outers)
            {
                holesOf.TryGetValue(o, out List<IList<Vec3>> own);
                Polygon.Fill(mesh, loops[o], own);
            }

            if (mesh.TriangleCount == 0)
                return null;
            return new Cell { Vertices = mesh.Positions.ToArray(), Triangles = mesh.Indices.ToArray(), MinX = glyph.MinX, MaxX = glyph.MaxX, MinY = glyph.MinY, MaxY = glyph.MaxY, Advance = glyph.Advance };
        }

        private static bool Contains(IList<Vec3> loop, Vec3 p)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                if ((loop[i].Z > p.Z) != (loop[j].Z > p.Z) && p.X < (loop[j].X - loop[i].X) * (p.Z - loop[i].Z) / (loop[j].Z - loop[i].Z) + loop[i].X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>
        /// Adds one character into a box (centre x, z; width across, length along): a character of the ideographic em box (a word of Chinese) fills the box with the em square's width and the ideographic
        /// height of it (the box the font is designed on: FontOutlines.EmBottom to EmTop, -0.12 to 0.88 em in Noto, -0.2 to 0.8 in the traffic sign font); a figure or a letter (fitInk) is fitted to the box by its own ink, so that a narrow 1
        /// and a wide 0 each fill their cell. Returns false when the font has no such character.
        /// </summary>
        public static bool AddCharacter(MeshBuilder mesh, char c, float centreX, float centreZ, float width, float length, bool fitInk) => AddCharacter(mesh, c, centreX, centreZ, width, length, fitInk, false);

        /// <summary>How wide a character is set in the font's own proportions, in ems: its advance (an ideograph's em); half an em for one the font does not have (0.12.22, the custom text).</summary>
        public static float AdvanceOf(char c)
        {
            Cell cell = Cell0(c);
            if (cell == null)
                return 0.5f;
            return IsIdeograph(c) ? 1f : Math.Max(0.15f, cell.Advance / cell.Em);
        }

        /// <summary>As AddCharacter; proportional: a character that is not an ideograph takes its advance across and the em's height (the box was made AdvanceOf wide), not its ink.</summary>
        private static bool AddCharacter(MeshBuilder mesh, char c, float centreX, float centreZ, float width, float length, bool fitInk, bool proportional)
        {
            Cell cell = Cell0(c);
            if (cell == null || !(width > 0f) || !(length > 0f))
                return false;
            float em = cell.Em;
            float x0, x1, y0, y1;
            if (proportional)
            {
                x0 = 0f;
                x1 = IsIdeograph(c) ? em : Math.Max(0.15f * em, cell.Advance);
                y0 = cell.EmBottom;
                y1 = cell.EmTop;
            }
            else if (fitInk)
            {
                x0 = cell.MinX;
                x1 = cell.MaxX;
                y0 = cell.MinY;
                y1 = cell.MaxY;
            }
            else
            {
                x0 = 0f;
                x1 = em;
                y0 = cell.EmBottom;
                y1 = cell.EmTop;
            }

            float sx = width / Math.Max(1f, x1 - x0), sz = length / Math.Max(1f, y1 - y0);
            var up = new Vec3(0f, 1f, 0f);
            int first = mesh.VertexCount;
            foreach (Vec3 v in cell.Vertices)
                mesh.Vertex(new Vec3(centreX - width * 0.5f + (v.X - x0) * sx, 0f, centreZ - length * 0.5f + (v.Z - y0) * sz), up, v.X / em, v.Z / em);
            for (int i = 0; i + 2 < cell.Triangles.Length; i += 3)
                mesh.TriFacing(first + cell.Triangles[i], first + cell.Triangles[i + 1], first + cell.Triangles[i + 2], up);
            return true;
        }

        /// <summary>The characters of a text in a box that are Chinese or other full-width ones (not the ASCII figures, letters and the hyphen of a limit or a time).</summary>
        public static bool IsIdeograph(char c) => c >= 0x2E80;

        /// <summary>
        /// A marking's text as meshes (one box is one character: the markings' own lettering is a character to a box, Fig. 76's 停, Fig. 33's 公交车 each with a box of 100 x 250). The box's
        /// Text is the character (or, for the figures of a time or a limit, the whole short string laid left to right across the box). Returns how many characters were drawn.
        /// </summary>
        public static int AddTexts(MeshBuilder mesh, IList<MarkingText> boxes)
        {
            int drawn = 0;
            if (boxes == null)
                return 0;
            foreach (MarkingText box in boxes)
            {
                if (string.IsNullOrEmpty(box.Text))
                    continue;
                if (box.Yaw != 0f)
                {
                    // Drawn upright about its own centre, then turned and moved: the word's frame is the box's.
                    var upright = new MeshBuilder();
                    drawn += AddTexts(upright, new List<MarkingText> { new MarkingText { Text = box.Text, X = 0f, Z = 0f, Width = box.Width, Length = box.Length, Digits = box.Digits, Proportional = box.Proportional } });
                    mesh.Append(upright, new Vec3(box.X, 0f, box.Z), box.Yaw);
                    continue;
                }

                if (box.Digits)
                {
                    drawn += AddFigures(mesh, box.Text, box.X, box.Z, box.Width, box.Length);
                    continue;
                }

                if (box.Text.Length == 1)
                {
                    if (AddCharacter(mesh, box.Text[0], box.X, box.Z, box.Width, box.Length, !IsIdeograph(box.Text[0]), box.Proportional))
                        drawn++;
                    continue;
                }

                // A short string of figures laid across the box, each in its own cell of equal width (the figures of a time: 17-18, a limit: 100).
                float cell = box.Width / box.Text.Length;
                for (int i = 0; i < box.Text.Length; i++)
                {
                    char c = box.Text[i];
                    float cx = box.X - box.Width * 0.5f + cell * (i + 0.5f);
                    // The hyphen is a short bar, not a full-height figure.
                    bool bar = c == '-' || c == '—';
                    if (bar)
                    {
                        RoadMarkings.Rect(mesh, cx - cell * 0.4f, box.Z - box.Length * 0.06f, cx + cell * 0.4f, box.Z + box.Length * 0.06f);
                        drawn++;
                    }
                    else if (AddCharacter(mesh, c, cx, box.Z, cell * 0.92f, box.Length, !IsIdeograph(c)))
                    {
                        drawn++;
                    }
                }
            }

            return drawn;
        }
    }
}
