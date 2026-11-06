using System;
using System.Globalization;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>A board as one string: every part's outline and colours and every element's kind, colour and geometry to a tenth of a millimetre, so that two boards are the same board when their prints are equal.</summary>
    internal static class BoardPrint
    {
        private static string F(float v) => Math.Round(v * 10000f).ToString(CultureInfo.InvariantCulture);

        public static string Of(GuideBoard board)
        {
            var sb = new StringBuilder();
            foreach (GuidePart p in board.Parts)
            {
                sb.Append('P').Append(p.Shape).Append(',').Append(F(p.X)).Append(',').Append(F(p.Y)).Append(',').Append(F(p.W)).Append(',').Append(F(p.H)).Append(',').Append(p.Ground).Append(',').Append(p.BorderColour)
                    .Append(',').Append(F(p.Liner)).Append(',').Append(F(p.Border)).Append(',').Append(F(p.Radius)).Append(',').Append(F(p.Side));
                if (p.Safe != null)
                {
                    foreach (float s in p.Safe)
                        sb.Append(',').Append(F(s));
                }

                foreach (GuideElement e in p.Elements)
                {
                    sb.Append(";E").Append(e.Kind).Append(',').Append(e.Colour).Append(',').Append(e.Art ? 1 : 0).Append(',').Append(F(e.X)).Append(',').Append(F(e.Y)).Append(',').Append(F(e.W)).Append(',').Append(F(e.H));
                    if (e.Text != null)
                        sb.Append(',').Append(e.Text);
                    if (e.Points != null)
                    {
                        foreach (float v in e.Points)
                            sb.Append(',').Append(F(v));
                    }

                    if (e.Contours != null)
                    {
                        foreach (float[] c in e.Contours)
                        {
                            sb.Append('|');
                            foreach (float v in c)
                                sb.Append(',').Append(F(v));
                        }
                    }
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>A short hash of the print (FNV-1a, eight hex digits): what a test keeps of a board that must not change.</summary>
        public static string Hash(GuideBoard board)
        {
            uint h = 2166136261u;
            foreach (char c in Of(board))
                h = (h ^ c) * 16777619u;
            return h.ToString("x8");
        }
    }
}
