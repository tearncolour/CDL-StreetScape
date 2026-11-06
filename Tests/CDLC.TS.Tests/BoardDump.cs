using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A laid-out board as text, every number that the painter and the mesh builder read, so that two boards are the same exactly when their dumps are: the checks that a switch left off changes
    /// nothing compare dumps, and the "dump" argument writes the id, the spec and the hash of the dump of every entry of the panel (to put the numbers of two builds side by side).
    /// </summary>
    internal static class BoardDump
    {
        public static string Of(GuideBoard board)
        {
            var sb = new StringBuilder(2048);
            void F(float v) => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            F(board.H);
            F(board.LedPitch);
            sb.Append(board.LedMaxDots).Append(',').Append(board.LedSamples).Append(',').Append(board.Box != null ? 1 : 0).Append(board.PoleAd != null ? 1 : 0).Append(board.Text != null ? 1 : 0).Append(board.Stand != null ? 1 : 0).Append('|');
            foreach (GuidePart p in board.Parts)
            {
                F(p.X); F(p.Y); F(p.W); F(p.H); F(p.Side); F(p.Liner); F(p.Border); F(p.Radius);
                sb.Append((int)p.Shape).Append(',').Append((int)p.Ground).Append(',').Append((int)p.BorderColour).Append(',').Append(p.Cut != null ? p.Cut.Count : -1).Append(',');
                if (p.Safe != null)
                {
                    foreach (float v in p.Safe)
                        F(v);
                }

                sb.Append('{');
                foreach (GuideElement e in p.Elements)
                {
                    sb.Append('E').Append((int)e.Kind).Append(',').Append((int)e.Mode).Append(',').Append((int)e.Align).Append(',').Append((int)e.Colour).Append(',').Append((int)e.Stroke).Append(',').Append((int)e.Arrow).Append(',');
                    sb.Append(e.Art ? 1 : 0).Append(e.Crop ? 1 : 0).Append(e.Opaque ? 1 : 0).Append(',');
                    F(e.X); F(e.Y); F(e.W); F(e.H); F(e.RadiusTop); F(e.RadiusBottom); F(e.StrokeWidth); F(e.Bleed); F(e.Gap);
                    sb.Append(e.Text == null ? -1 : e.Text.Length).Append(':').Append(e.Text).Append(',').Append(e.InkRef == null ? -1 : e.InkRef.Length).Append(':').Append(e.InkRef).Append(',');
                    if (e.Points != null)
                    {
                        sb.Append("pt");
                        foreach (float v in e.Points)
                            F(v);
                    }

                    if (e.Contours != null)
                    {
                        foreach (float[] contour in e.Contours)
                        {
                            sb.Append("ct");
                            foreach (float v in contour)
                                F(v);
                        }
                    }

                    sb.Append(';');
                }

                sb.Append('}');
            }

            return sb.ToString();
        }

        public static string Hash(GuideBoard board)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(Of(board)));
                var hex = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                    hex.Append(bytes[i].ToString("x2"));
                return hex.ToString();
            }
        }

        /// <summary>Writes "id, spec, hash" for every entry of the panel (the standalone templates and the presets) to the file; for putting two builds side by side.</summary>
        public static int Write(string file)
        {
            var lines = new List<string>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                string hash = "-";
                string spec = choice.Spec ?? "";
                if (choice.Def != null && choice.Def.Template != null && !choice.Def.IsMarking)
                {
                    GuideBoard board = choice.Def.Template.Layout(choice.Def.Template.Parse(spec));
                    hash = Hash(board);
                }

                lines.Add(choice.Id + "\t" + spec + "\t" + hash);
            }

            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " entries written to " + file);
            return 0;
        }
    }
}
