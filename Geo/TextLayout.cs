using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public enum TextAlign
    {
        Left = 0,
        Centre = 1,
        Right = 2
    }

    /// <summary>How a text is laid out: the letter height (m: the em box of the font is this tall), the extra gap between letters as a share of it, the line pitch as a multiple of it, the alignment.</summary>
    public struct TextStyle
    {
        public float LetterHeight, LetterSpacing, LineSpacing;
        public TextAlign Align;

        public static TextStyle Of(float letterHeight) => new TextStyle { LetterHeight = letterHeight, LetterSpacing = 0f, LineSpacing = 1.15f, Align = TextAlign.Centre };
    }

    /// <summary>What the layout needs to know of a font: its em, the box a character is designed on (font units from the baseline), and the advance of a character (negative where the font has none).</summary>
    public sealed class TextFace
    {
        public readonly float UnitsPerEm, EmBottom, EmTop;
        public readonly Func<int, float> Advance;

        public TextFace(float unitsPerEm, float emBottom, float emTop, Func<int, float> advance)
        {
            UnitsPerEm = unitsPerEm;
            EmBottom = emBottom;
            EmTop = emTop;
            Advance = advance;
        }

        /// <summary>The face of a font: a key below TextClusters.ClusterKeyBase is a code point (the font's advance, as always); a key of a sequence is an emoji, the advance of the glyph the font's ligatures make of it.</summary>
        public static TextFace Of(FontOutlines font) =>
            new TextFace(font.UnitsPerEm, font.EmBottom, font.EmTop, key =>
            {
                if (key < TextClusters.ClusterKeyBase)
                    return font.TryAdvance(key, out float advance) ? advance : -1f;
                List<int> glyphs = font.Shape(TextClusters.Scalars(TextClusters.TextOf(key)));
                if (glyphs == null)
                    return -1f;
                foreach (int g in glyphs)
                {
                    float a = font.AdvanceOfGlyph(g);
                    if (a > 0f)
                        return a;
                }

                return -1f;
            });
    }

    /// <summary>A character placed: where its origin (the start of its baseline) is, in metres with y up and the text block's middle at 0, 0.</summary>
    public struct PlacedGlyph
    {
        public int Codepoint, Line;
        public float X, Y;

        /// <summary>The advance the glyph was given in the layout (m), and how far the glyph is turned about its origin (radians, counter-clockwise as the viewer sees it): 0 for a text on a straight line (0.12.76: a text on an arc turns each letter).</summary>
        public float Advance, Angle;

        /// <summary>The font has no glyph for it: it keeps its place as a blank.</summary>
        public bool Missing;
    }

    public sealed class TextLayoutResult
    {
        public readonly List<PlacedGlyph> Glyphs = new List<PlacedGlyph>();
        public readonly List<float> LineWidths = new List<float>();

        /// <summary>The codepoints the font has no glyph for, each once.</summary>
        public readonly List<int> Missing = new List<int>();

        /// <summary>Metres a font unit is, and metres an em is; the letter height asked for (m).</summary>
        public float Scale, MetresPerEm, LetterHeight;

        /// <summary>The top and the bottom of the em box a character is designed on, in metres above the baseline (0.12.76: the box of a letter that is turned).</summary>
        public float EmTopMetres, EmBottomMetres;

        /// <summary>The layout of a text on an arc (ArcTextLayout): how it was bent, and the straight layout it came from. Null for a straight text.</summary>
        public ArcInfo Arc;

        /// <summary>The block's box (m): the widest line by the lines' em boxes with their gaps; the ink may be smaller.</summary>
        public float Width, Height;

        public int Lines => LineWidths.Count;

        /// <summary>Characters or lines beyond the limits were left out.</summary>
        public bool Truncated;
    }

    /// <summary>
    /// The layout of a typed text for a lit board (0.12.75, DESIGN-text-lightbox.md 4): up to MaxLines lines of at most MaxPerLine characters (MaxChars in all; what is over is left out and says so), advances
    /// from the font's own table, no kerning and no shaping. A Chinese character is full width (an em) whatever the font's advance; a character the font lacks keeps its place as a blank (an em for a
    /// Chinese character, half an em for any other) and is listed, not drawn as the font's notdef box, which on a lit sign would look like a fault. Pure.
    /// </summary>
    public static class TextLayout
    {
        public const int MaxLines = 3, MaxPerLine = 12, MaxChars = 36;

        /// <summary>Whether a character (a key of TextClusters: a code point, or a sequence) is set full width: the CJK blocks and their extension planes, kana, hangul, the full-width forms, emoji.</summary>
        public static bool IsWide(int c) => TextClusters.IsWideKey(c);

        public static TextLayoutResult Layout(string text, TextStyle style, TextFace face)
        {
            var result = new TextLayoutResult();
            float height = Math.Max(1e-4f, style.LetterHeight);
            float box = Math.Max(1f, face.EmTop - face.EmBottom);
            result.LetterHeight = height;
            result.Scale = height / box;
            result.MetresPerEm = result.Scale * face.UnitsPerEm;
            result.EmTopMetres = face.EmTop * result.Scale;
            result.EmBottomMetres = face.EmBottom * result.Scale;
            List<List<int>> lines = Split(text ?? "", result);
            int total = 0;
            foreach (List<int> line in lines)
                total += line.Count;
            result.Glyphs.Capacity = total;
            float gap = Math.Max(0f, style.LetterSpacing) * height;
            var advances = new List<float[]>(lines.Count);
            var absent = new List<bool[]>(lines.Count);
            float widest = 0f;
            foreach (List<int> line in lines)
            {
                var a = new float[line.Count];
                var missing = new bool[line.Count];
                float width = 0f;
                for (int i = 0; i < line.Count; i++)
                {
                    a[i] = AdvanceOf(face, line[i], result, out missing[i]) * result.Scale;
                    width += a[i] + (i > 0 ? gap : 0f);
                }

                advances.Add(a);
                absent.Add(missing);
                result.LineWidths.Add(width);
                widest = Math.Max(widest, width);
            }

            float pitch = Math.Max(0f, style.LineSpacing) * height;
            result.Width = widest;
            result.Height = lines.Count == 0 ? 0f : (lines.Count - 1) * pitch + height;
            for (int l = 0; l < lines.Count; l++)
            {
                float width = result.LineWidths[l];
                float x = -widest * 0.5f + (style.Align == TextAlign.Centre ? (widest - width) * 0.5f : style.Align == TextAlign.Right ? widest - width : 0f);
                float baseline = result.Height * 0.5f - l * pitch - face.EmTop * result.Scale;
                for (int i = 0; i < lines[l].Count; i++)
                {
                    int c = lines[l][i];
                    result.Glyphs.Add(new PlacedGlyph { Codepoint = c, Line = l, X = x, Y = baseline, Missing = absent[l][i], Advance = advances[l][i] });
                    x += advances[l][i] + gap;
                }
            }

            return result;
        }

        /// <summary>The advance of a character in font units, with the rules above; a missing character is listed once.</summary>
        private static float AdvanceOf(TextFace face, int c, TextLayoutResult result, out bool missing)
        {
            float advance = face.Advance(c);
            missing = advance < 0f;
            if (missing)
            {
                if (!result.Missing.Contains(c))
                    result.Missing.Add(c);
                return IsWide(c) ? face.UnitsPerEm : face.UnitsPerEm * 0.5f;
            }

            return IsWide(c) ? face.UnitsPerEm : advance;
        }

        private static bool HasText(string text, int from)
        {
            for (int i = from; i < text.Length; i++)
            {
                if (text[i] != '\n' && text[i] != '\r')
                    return true;
            }

            return false;
        }

        /// <summary>The lines of a text as the keys of its clusters (TextClusters: a surrogate pair is one, an emoji sequence is one): split on a newline, a carriage return dropped, trailing empty lines dropped, the limits applied.</summary>
        private static List<List<int>> Split(string text, TextLayoutResult result)
        {
            var lines = new List<List<int>> { new List<int>() };
            int total = 0;
            for (int i = 0; i < text.Length;)
            {
                int length = TextClusters.LengthAt(text, i);
                char ch = text[i];
                int at = i;
                i += length;
                if (ch == '\r')
                    continue;
                if (ch == '\n')
                {
                    if (lines.Count >= MaxLines)
                    {
                        result.Truncated = HasText(text, i);
                        break;
                    }

                    lines.Add(new List<int>());
                    continue;
                }

                int c = length == 1 ? TextClusters.ScalarAt(text, at, out _) : TextClusters.KeyOf(TextClusters.Split(text.Substring(at, length))[0]);
                List<int> line = lines[lines.Count - 1];
                if (line.Count >= MaxPerLine || total >= MaxChars)
                {
                    result.Truncated = true;
                    continue;
                }

                line.Add(c);
                total++;
            }

            while (lines.Count > 0 && lines[lines.Count - 1].Count == 0)
                lines.RemoveAt(lines.Count - 1);
            return lines;
        }
    }
}
