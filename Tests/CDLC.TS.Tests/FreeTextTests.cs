using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The custom text on the ground (0.12.22, mk.text.free; the owner: "地面中加入自定义地面文字").</summary>
    internal static class FreeTextTests
    {
        public static void RunAll() => Program.Run("custom ground text: lines, sizes, colours and the vertical layout", Layout);

        private static MarkingPlan Plan(string spec) => MarkingTemplates.PlanOf(MarkingTemplates.ById(MarkingTemplates.FreeTextId), spec);

        private static void Layout()
        {
            MarkingLetters.Font = FontLetterTests.Font;
            Program.Check(FontLetterTests.Font != null, "the bundled font is there");
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.FreeTextId);
            Program.Check(t != null && t.Group == "marking" && !MarkingTemplates.IsRun(t), "a marking of one piece in the markings' group");
            MarkingPlan one = Plan("text=文字"), longer = Plan("text=文字文字");
            Program.Check(one != null && one.Parts.Count > 0 && one.VertexCount > 0, "it has paint");
            float w1 = one.MaxX - one.MinX, w2 = longer.MaxX - longer.MinX, h1 = one.MaxZ - one.MinZ;
            Program.Check(w2 > 1.9f * w1 && w2 < 2.3f * w1, $"in a row a text twice as long is twice as wide: {w1:0.00} and {w2:0.00} m");
            Program.Check(h1 > 1.2f && h1 <= 1.5f + 1e-3f, "1.5 m characters by default: " + h1.ToString("0.00"));
            MarkingPlan tall = Plan("text=文字;h=300");
            Program.Check(Math.Abs((tall.MaxZ - tall.MinZ) / h1 - 2f) < 0.05f, "twice the height setting, twice as tall");
            MarkingPlan stretched = Plan("text=文字;ratio=3");
            Program.Check(stretched.MaxX - stretched.MinX < w1 * 0.4f, "1:3 characters are a third as wide");
            MarkingPlan letters = Plan("text=ABCD"), han = Plan("text=文字文字");
            Program.Check(letters.MaxX - letters.MinX < (han.MaxX - han.MinX) * 0.85f, "letters keep the font's narrower proportions");
            MarkingPlan two = Plan("text=文字;text2=文字");
            Program.Check(two.MaxZ - two.MinZ > 2f * h1, "a second line goes under the first");
            MarkingPlan col = Plan("text=公交专用;read=col;ratio=3;text2=7:00-9:00");
            Program.Check(col.MaxZ - col.MinZ > 4f * 1.2f && col.MaxX - col.MinX > 1f, "the vertical layout stacks the Chinese and lays the time across");
            var colours = new HashSet<string>();
            foreach (string c in new[] { "w", "y", "r", "k" })
                colours.Add(ChunkBuilder.PartMaterial(Plan("text=字;colour=" + c).Parts[0]));
            Program.Check(colours.Count == 4, "four colours, four paints: " + string.Join(", ", colours));
            Program.Check(Plan("text=;text2=").VertexCount > 0, "no text: a placeholder word, not an empty item");
        }
    }
}
