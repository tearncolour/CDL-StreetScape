using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The ceiling for near-white surfaces (0.12.92; the owner, 5 Nov: "白色材质白天会过曝"): which colours it holds, by how much, and the experiment file's whiteMax line that moves it live. The engine parts (which
    /// materials get the factor) need the game's materials and are not run here.
    /// </summary>
    internal static class AlbedoCapTests
    {
        public static void RunAll()
        {
            Program.Run("albedo cap: which colours are held and by how much", Factor);
            Program.Run("albedo cap: the same colour on the three routes", Routes);
            Program.Run("albedo cap: the experiment file's whiteMax line", Experiment);
        }

        private static void Factor()
        {
            float cap = AlbedoCap.Default;
            // The detail route's white (0.86, 0.87, 0.86 as light): held to the ceiling, tint kept.
            float f = AlbedoCap.Factor(0.86f, 0.87f, 0.86f, cap);
            Program.Near(f * 0.87f, cap, 1e-5f, "a white of 0.87 comes to the ceiling: " + f);
            Program.Near(f * 0.86f / (f * 0.87f), 0.86f / 0.87f, 1e-5f, "and keeps its tint");
            Program.Check(AlbedoCap.Factor(0.80f, 0.81f, 0.79f, cap) < 1f && AlbedoCap.Factor(0.80f, 0.81f, 0.79f, cap) > f, "the light grey of a louvre panel is held less than the white");
            // At or under the ceiling: nothing.
            Program.Check(AlbedoCap.Factor(0.72f, 0.72f, 0.72f, cap) == 1f && AlbedoCap.Factor(0.5f, 0.5f, 0.5f, cap) == 1f && AlbedoCap.Factor(0.62f, 0.62f, 0.60f, cap) == 1f, "concrete and what is not above the ceiling are left alone");
            // A colour is not a white.
            Program.Check(AlbedoCap.Factor(0.92f, 0.70f, 0.05f, cap) == 1f && AlbedoCap.Factor(0.95f, 0.30f, 0.04f, cap) == 1f && AlbedoCap.Factor(0.9f, 0.7f, 0.7f, cap) == 1f, "yellow, orange and a pink are left alone");
            // The edge of neutral: the dimmest channel at 0.85 of the brightest is held, a little under is not.
            Program.Check(AlbedoCap.Factor(1f, 0.85f, 0.85f, 0.7f) < 1f && AlbedoCap.Factor(1f, 0.84f, 0.84f, 0.7f) == 1f, "the edge of neutral is 0.85 of the brightest channel");
            // A ceiling that holds nothing.
            Program.Check(AlbedoCap.Factor(0.86f, 0.87f, 0.86f, 0f) == 1f && AlbedoCap.Factor(0.86f, 0.87f, 0.86f, -1f) == 1f && AlbedoCap.Factor(0.86f, 0.87f, 0.86f, float.NaN) == 1f, "a ceiling of zero, below zero or not a number holds nothing");
            Program.Check(AlbedoCap.Factor(float.NaN, 0.8f, 0.8f, cap) == 1f, "a colour that is not a number is left alone");
            // A lower ceiling holds more.
            Program.Check(AlbedoCap.Factor(0.86f, 0.87f, 0.86f, 0.5f) < AlbedoCap.Factor(0.86f, 0.87f, 0.86f, 0.6f), "a lower ceiling holds more");
            Program.Check(AlbedoCap.Factor(0.86f, 0.87f, 0.86f, 0.5f) * 0.87f <= 0.5f + 1e-5f, "and the brightest channel never passes it");
        }

        private static void Routes()
        {
            // The same Color number, three routes: the detail bakes it as light, the plain route and HDRP/Lit turn it from gamma to linear.
            float[] white = { 0.86f, 0.87f, 0.86f };
            float plain = AlbedoCap.ToLinear(white[1]), detail = white[1];
            Program.Near(plain, 0.7307f, 2e-3f, "a white of 0.87 as a Color property is 0.73 as light: " + plain);
            Program.Check(detail / plain > 1.15f, "the detail route's light is more than 1.15 times the plain route's: " + (detail / plain));
            float cap = AlbedoCap.Default;
            float detailFactor = AlbedoCap.Factor(white[0], white[1], white[2], cap);
            float plainFactor = AlbedoCap.Factor(AlbedoCap.ToLinear(white[0]), AlbedoCap.ToLinear(white[1]), AlbedoCap.ToLinear(white[2]), cap);
            Program.Check(detailFactor < 0.9f, "the detail route's white is held: " + detailFactor);
            Program.Check(plainFactor > 0.97f && plainFactor <= 1f, "the plain route's white is held hardly or not at all at the default: " + plainFactor);
            Program.Near(detail * detailFactor, cap, 1e-4f, "after the factor the detail route's white is the ceiling");
            Program.Near(AlbedoCap.ToLinear(0f), 0f, 1e-6f, "black is black");
            Program.Near(AlbedoCap.ToLinear(1f), 1f, 1e-4f, "white is white");
            Program.Near(AlbedoCap.ToLinear(0.04045f), 0.04045f / 12.92f, 1e-6f, "the curve's two pieces meet");
        }

        private static void Experiment()
        {
            var problems = new List<string>();
            MaterialExperiment e = MaterialExperiment.Parse("whiteMax = 0.6", problems);
            Program.Check(problems.Count == 0 && e.WhiteMax.HasValue && Math.Abs(e.WhiteMax.Value - 0.6f) < 1e-6f && !e.IsEmpty, "whiteMax = 0.6 is read: " + string.Join("|", problems));
            Program.Check(e.Describe().Contains("whiteMax 0.6"), "the log says it: " + e.Describe());
            Program.Check(MaterialExperiment.Parse("WHITEMAX=0.65  # a comment", null).WhiteMax == 0.65f, "any case, a comment after it");
            Program.Check(MaterialExperiment.Parse("whiteMax = 1", null).WhiteMax == 1f && MaterialExperiment.Parse("whiteMax = 0.2", null).WhiteMax == AlbedoCap.Lowest, "1 and 0.2 are the ends of what is taken");
            foreach (string bad in new[] { "whiteMax = 0.19", "whiteMax = 1.01", "whiteMax = abc", "whiteMax =", "whiteMax = -0.5", "whiteMax = NaN" })
            {
                var p = new List<string>();
                MaterialExperiment r = MaterialExperiment.Parse(bad, p);
                Program.Check(p.Count == 1 && !r.WhiteMax.HasValue && p[0].Contains("whiteMax"), "'" + bad + "' is named and left out: " + string.Join("|", p));
            }

            Program.Check(MaterialExperiment.Parse("whiteMax = 0.6", null).Equals(MaterialExperiment.Parse("whiteMax=0.6", null)) && !MaterialExperiment.Parse("whiteMax = 0.6", null).Equals(MaterialExperiment.Parse("whiteMax = 0.7", null)) && !MaterialExperiment.Parse("whiteMax = 0.6", null).Equals(MaterialExperiment.None), "a change of the line is a change of the experiment");
            Program.Check(MaterialExperiment.Parse("whiteMax = 0.6\ngameShader = lit", null).GameShader == GameShaderMode.Lit && MaterialExperiment.Parse("whiteMax = 0.6\ngameShader = lit", null).WhiteMax == 0.6f, "it sits beside the other lines");
            Program.Check(MaterialExperiment.Parse("whiteMax = 0.6\nwhiteMax = 0.7", null).WhiteMax == 0.7f, "the last line of the key wins");
        }
    }
}
