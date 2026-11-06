using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The distance of a support from the kerb (0.12.74; the owner has no numpad: the < and > keys and the slider set it in tenths of a metre, for a post that stands too).</summary>
    internal static class KerbMarginTests
    {
        public static void RunAll()
        {
            Program.Run("kerb margin: the range and the tenths", Range);
            Program.Run("kerb margin: a key press is one tenth, never a drift", Presses);
            Program.Run("kerb margin: only a post is moved across by it", Kinds);
            Program.Run("kerb margin: the margin of a post that stands, and the move that sets one", Standing);
            Program.Run("kerb margin: the keys the hints name", Hints);
        }

        private static void Range()
        {
            Program.Check(KerbMargin.Clamp(-0.6f) == -0.6f, "the default is kept");
            Program.Check(KerbMargin.Clamp(-0.6000001f) == -0.6f && KerbMargin.Clamp(-0.5999999f) == -0.6f, "a slider's float noise lands on the tenth");
            Program.Check(KerbMargin.Clamp(0.04f) == 0f && KerbMargin.Clamp(0.06f) == 0.1f && KerbMargin.Clamp(-0.06f) == -0.1f, "rounded to the nearest tenth, both signs");
            Program.Check(KerbMargin.Clamp(0.25f) == 0.3f && KerbMargin.Clamp(-0.25f) == -0.3f, "a half tenth goes away from zero, as the slider would show it");
            Program.Check(KerbMargin.Clamp(100f) == 6f && KerbMargin.Clamp(-100f) == -8f, "inside -8 to 6");
            Program.Check(KerbMargin.Clamp(float.PositiveInfinity) == Carriageway.DefaultMargin && KerbMargin.Clamp(float.NaN) == Carriageway.DefaultMargin, "not a number: the default");
            Program.Check(KerbMargin.Min == -8f && KerbMargin.Max == 6f && KerbMargin.Step == 0.1f, "the range the tool had");
        }

        private static void Presses()
        {
            float m = Carriageway.DefaultMargin;
            m = KerbMargin.Stepped(m, 1);
            Program.Check(m == -0.5f, $"> from the default is -0.5 ({m})");
            m = KerbMargin.Stepped(m, -1);
            Program.Check(m == Carriageway.DefaultMargin, "< goes back to the very same number");
            Program.Check(KerbMargin.Stepped(1f, 0) == 1f, "no direction, no move");
            Program.Check(KerbMargin.Stepped(1f, 5) == 1.1f && KerbMargin.Stepped(1f, -7) == 0.9f, "only the sign of the direction counts");
            float up = Carriageway.DefaultMargin;
            for (int i = 0; i < 1000; i++)
                up = KerbMargin.Stepped(up, 1);
            Program.Check(up == 6f, $"a thousand > stop at 6 exactly ({up})");
            float down = Carriageway.DefaultMargin;
            for (int i = 0; i < 1000; i++)
                down = KerbMargin.Stepped(down, -1);
            Program.Check(down == -8f, $"a thousand < stop at -8 exactly ({down})");
            float there = Carriageway.DefaultMargin;
            for (int i = 0; i < 30; i++)
                there = KerbMargin.Stepped(there, 1);
            for (int i = 0; i < 30; i++)
                there = KerbMargin.Stepped(there, -1);
            Program.Check(there == Carriageway.DefaultMargin, $"30 up and 30 down are where it began ({there})");
            Program.Check(KerbMargin.Stepped(KerbMargin.Stepped(6f, 1), -1) == 5.9f, "at the end a press that does nothing is not taken back by the next");
            float all = -8f;
            for (int i = 0; i < 140; i++)
                all = KerbMargin.Stepped(all, 1);
            Program.Check(all == 6f && Math.Abs(KerbMargin.Stepped(5.95f, 1) - 6f) < 1e-6f, "140 steps from -8 are 6");
        }

        private static void Kinds()
        {
            var posts = new HashSet<SupportKind> { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole };
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
                Program.Check(KerbMargin.Applies(kind) == posts.Contains(kind), $"{kind}: {(posts.Contains(kind) ? "a post" : "not a post: an arm, a beam, a wall or a host depends on more")}");
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                foreach (string kind in new[] { "place", "wall", "attach", "attach-post", "host", "arm", "arm-none", "arm-space", "arm-large" })
                {
                    List<string[]> keys = HintText.Keys(new HintInfo { Kind = kind }, zh);
                    Program.Check(!keys.Exists(k => k[0].Contains("Numpad") || k[0] == ",  ."), $"{kind}: no numpad key and no comma-period pair in the list ({(zh ? "zh" : "en")})");
                    if (kind.StartsWith("arm"))
                        Program.Check(keys.Exists(k => k[0] == ";  '") && !keys.Exists(k => k[0] == "<  >"), $"{kind}: the support is changed with ; ' and an arm has no kerb distance");
                }

                List<string[]> placing = HintText.Keys(new HintInfo { Kind = "place" }, zh);
                Program.Check(placing.Exists(k => k[0] == "<  >") && placing.Exists(k => k[0] == ";  '"), $"placing lists < > for the distance from the kerb and ; ' for the support ({(zh ? "zh" : "en")})");
                string says = placing.Find(k => k[0] == "<  >")[1];
                Program.Check(zh ? says == "离路缘" : says == "kerb distance", "the distance line says what it does");
                Program.Check(HintText.Keys(new HintInfo { Kind = "move", Kerb = true }, zh).Exists(k => k[0] == "<  >"), "editing a post lists < >");
                Program.Check(!HintText.Keys(new HintInfo { Kind = "move" }, zh).Exists(k => k[0] == "<  >"), "editing anything else does not");
            }

            Program.Check(!new HintInfo { Kind = "move" }.Equals(new HintInfo { Kind = "move", Kerb = true }), "the kerb flag is part of the hint's equality (the panel's line is made again when it changes)");
        }

        private static CrossLane Car(float x) => new CrossLane(x, 3f, CrossLaneKind.Carriageway);

        private static CrossLane Foot(float x) => new CrossLane(x, 2f, CrossLaneKind.Pedestrian);

        private static void Standing()
        {
            Carriageway bare = Carriageway.Of(new List<CrossLane> { Car(-4.5f), Car(-1.5f), Car(1.5f), Car(4.5f) }, 24f);
            Carriageway kerbed = Carriageway.Of(new List<CrossLane> { Car(-4.5f), Car(-1.5f), Car(1.5f), Car(4.5f), Foot(-7.5f), Foot(7.5f) }, 24f);
            foreach (Carriageway road in new[] { bare, kerbed })
            {
                foreach (int side in new[] { 1, -1 })
                {
                    foreach (float inner in new[] { 0.4f, 0.9f, 1.6f })
                    {
                        float reachOut = Math.Max(0f, inner - 0.4f);
                        float atDefault = road.PostDistance(side, inner, Carriageway.DefaultMargin, reachOut);
                        for (int tenth = -80; tenth <= 60; tenth++)
                        {
                            float placed = tenth / 10f;
                            float distance = road.PostDistance(side, inner, placed, reachOut);
                            // PostDistance never goes below the centre line: a margin that asks for less is not told apart from the one that reaches it.
                            if (distance <= 0f)
                                continue;
                            float of = KerbMargin.Of(side * distance, atDefault);
                            Program.Check(!float.IsNaN(of) && Math.Abs(of - placed) < 1e-4f, $"{(road == bare ? "bare" : "kerbed")} road, side {side}, reach {inner}: a post placed with {placed} reads back as {of}");
                        }
                    }
                }
            }

            Program.Check(float.IsNaN(KerbMargin.Of(0.5f, 11.4f)), "a post by the centre line (on a strip) is not a margin the player can set");
            Program.Check(float.IsNaN(KerbMargin.Of(float.NaN, 11.4f)), "not a number is not a margin");
            Program.Check(float.IsNaN(KerbMargin.Of(20f, 11.4f)) && !float.IsNaN(KerbMargin.Of(11.4f + 6.6f, 11.4f)) && float.IsNaN(KerbMargin.Of(11.4f + 6.7f, 11.4f)), "a post farther out than the range reaches is not a margin either, and the end of the range is");
            // The move: the slider's target against the margin the post has now, the post moved out by the difference, and it reads as the target.
            float stands = 11.4f - 0.37f, atDef = 11.4f;
            float current = KerbMargin.Of(stands, atDef);
            Program.Check(Math.Abs(current - (Carriageway.DefaultMargin - 0.37f)) < 1e-5f, "a post 0.37 m in from the default reads as -0.97");
            foreach (float target in new[] { -8f, -3.3f, -0.6f, 0f, 2.5f, 6f })
            {
                float across = KerbMargin.Across(target, current);
                float after = KerbMargin.Of(stands + across, atDef);
                Program.Check(Math.Abs(after - target) < 1e-4f, $"set to {target}: moved {across:0.000} m across, reads {after}");
            }

            Program.Check(KerbMargin.Across(99f, -1f) == 7f && KerbMargin.Across(-99f, 1f) == -9f, "a target out of range is the range's end");
            Program.Check(KerbMargin.Across(-0.6f, -0.6f) == 0f, "already there: no move");
        }
    }
}
