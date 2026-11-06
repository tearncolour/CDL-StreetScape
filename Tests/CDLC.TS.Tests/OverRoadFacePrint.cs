using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The support flat on a structure's face (attached.over) as 0.12.80 built it: boards of several sizes and some of the catalogue, at several reaches and leans, in full and in reduced detail, one number
    /// each (LightingPrint.Of: every mesh buffer to the last bit, the picks and the bounds) and the layout's own floats. The golden file was made by the code of 0.12.80 (main 5597bea) in a worktree of it. The
    /// hung variant (0.12.81) shares the kind, the layout function and the builder's dispatch with it, and must not move one vertex of this one.
    /// </summary>
    internal static class OverRoadFacePrint
    {
        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static SignInstance Plain(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static IEnumerable<KeyValuePair<string, SignInstance>> Boards()
        {
            yield return new KeyValuePair<string, SignInstance>("plate", Plain(Make("t.face.plate", 2.4f, 1.2f)));
            yield return new KeyValuePair<string, SignInstance>("tall", Plain(Make("t.face.tall", 1.0f, 3.5f)));
            yield return new KeyValuePair<string, SignInstance>("small", Plain(Make("t.face.small", 0.6f, 0.6f)));
            foreach (string id in new[] { "gb.prohib.01", "gb.warn.01", "gb.exit.adv.500", "gb.guide8.01" })
            {
                SignChoice choice = SignCatalog.ChoiceById(id);
                if (choice != null)
                    yield return new KeyValuePair<string, SignInstance>(id, SignInstances.Of(choice.Def, choice.Spec ?? ""));
            }
        }

        /// <summary>"board|reach|tilt|detail|number" for every build; the number folds the parts' print and the layout's floats.</summary>
        public static List<string> Lines()
        {
            var lines = new List<string>();
            if (!LightingPrint.Fonts())
                return lines;
            SupportDef support = SupportCatalog.ById(SupportCatalog.OverRoad);
            foreach (KeyValuePair<string, SignInstance> board in Boards())
            {
                foreach (float reach in new[] { 0f, 4.9f, 6.1f, 9.5f })
                {
                    foreach (float tilt in new[] { 0f, 10f })
                    {
                        foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                        {
                            SignParts parts = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = board.Value, Tilt = tilt * (float)(Math.PI / 180.0) } }, support, reach, detail);
                            SupportLayout l = parts.Layout;
                            string layout = string.Join(",", Array.ConvertAll(new[] { l.BottomClearance, l.BoardTop, l.BoardCentreY, l.BoardCentreX, l.BackZ, l.FrontZ, l.PoleTop, l.InnerReach }, v => BitConverter.SingleToInt32Bits(v).ToString("x8")));
                            lines.Add(board.Key + "|" + reach.ToString("0.0") + "|" + tilt.ToString("0") + "|" + (detail == SupportDetail.Full ? "F" : "R") + "|" + LightingPrint.Of(parts).ToString("x16") + "." + layout);
                        }
                    }
                }
            }

            return lines;
        }

        public static int Write(string file)
        {
            List<string> lines = Lines();
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " builds written to " + file);
            return 0;
        }
    }
}
