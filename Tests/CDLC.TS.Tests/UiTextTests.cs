using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The texts the editor shows have to fit its layout (0.3.0: the design speed's options were cut and wrapped, and nothing had checked how long a choice may be). Since 0.12.3 the editor is the
    /// game's panel, 380 rem wide inside its rows; a choice is buttons beside its label when it is short, else the game's dropdown, whose toggle and items are an option's text plus 40 rem (the
    /// arrow and the padding); a label or an option that does not fit beside the other (the script's choiceLayout) puts the control under the label at the whole width. The widths here are the
    /// script's (textWidth): a CJK or full-width character 13 rem, anything else 7.2 rem.
    /// </summary>
    internal static class UiTextTests
    {
        private const float Cjk = 13f, Latin = 7.2f, Inside = 380f, ItemExtra = 40f;

        public static void RunAll()
        {
            Program.Run("ui text: every choice and label fits the editor on one line", Fits);
            Program.Run("ui text: the estimate of a text's width", Estimate);
        }

        internal static float Width(string text)
        {
            float width = 0f;
            foreach (char c in text ?? "")
                width += c >= 0x2E80 ? Cjk : Latin;
            return width;
        }

        private static void Estimate()
        {
            Program.Near(Width("向左"), 26f, 1e-4f, "two CJK characters");
            Program.Near(Width("km/h"), 28.8f, 1e-4f, "four Latin characters");
            Program.Near(Width("（字高 65 cm）"), 13f * 4 + 7.2f * 6, 1e-3f, "full-width brackets count as wide");
            Program.Near(Width(""), 0f, 1e-6f, "nothing is nothing");
            Program.Near(Width(null), 0f, 1e-6f, "null is nothing");
        }

        private static void Fits()
        {
            int fields = 0, choices = 0;
            float widest = 0f;
            string widestText = "";
            foreach (GuideTemplate template in GuideTemplates.All)
            {
                foreach (GuideField field in template.Fields)
                {
                    fields++;
                    foreach (string label in new[] { field.Zh, field.En })
                    {
                        // A label that is stacked has the whole width; none may be wider.
                        Program.Check(Width(label) <= Inside, $"{template.Id}.{field.Key}: the label '{label}' fits on a line ({Width(label):0} rem)");
                        if (field.Kind == GuideFieldKind.Choice)
                        {
                            // A choice may have a label as long as the whole row.
                            continue;
                        }
                    }

                    if (field.Kind != GuideFieldKind.Choice)
                        continue;
                    foreach (GuideChoice choice in field.Choices)
                    {
                        choices++;
                        foreach (string text in new[] { choice.Zh, choice.En })
                        {
                            float width = Width(text) + ItemExtra;
                            if (width > widest)
                            {
                                widest = width;
                                widestText = text;
                            }

                            Program.Check(width <= Inside, $"{template.Id}.{field.Key}: the option '{text}' fits on a line of the editor ({width:0} of {Inside:0} rem)");
                        }
                    }
                }
            }

            Program.Check(fields > 100 && choices > 100, $"the check saw {fields} field(s) and {choices} option(s)");
            Program.Check(widest > 0f, "the widest option was found: " + widestText + " " + widest);
        }
    }
}
