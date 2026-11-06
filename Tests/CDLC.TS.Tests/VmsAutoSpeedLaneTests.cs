using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The automatic speed screen and lane screen (0.9.0): the limit from the road and what is ahead, the state of a lane from the blockage on the lanes, and how both are drawn.</summary>
    internal static class VmsAutoSpeedLaneTests
    {
        public static void RunAll()
        {
            Program.Run("auto speed: the limit of the road, kept to the standard's range, lowered by what is ahead", Speed);
            Program.Run("auto lane: closed when blocked, a merge arrow beside a blocked lane, open otherwise", Lane);
            Program.Run("auto lane: how far ahead a lane is blocked, from the game's bytes", Bytes);
            Program.Run("auto lane: the lanes of an edge in the order the editor counts them, with their blockage", Order);
            Program.Run("auto lane and speed: the kinds, decorating, the editor's fields and the words", Decorating);
        }

        private static VmsFacts Facts(float accident = -1f, bool secured = false, float jam = -1f, float slow = -1f) =>
            new VmsFacts { AccidentAt = accident, AccidentSecured = secured, JamAt = jam, JamLength = jam >= 0f ? 400f : 0f, SlowAt = slow };

        private static void Speed()
        {
            // The road's limit, rounded down to 5 and kept between 20 and 120.
            foreach ((float road, int shown) in new[] { (50f, 50), (49.9f, 45), (30f, 30), (100f, 100), (120f, 120), (121f, 120), (240f, 120), (66.7f, 65), (20f, 20), (19.9f, 20), (7f, 20) })
            {
                VmsMessage m = VmsAuto.SpeedFor(road, VmsFacts.None);
                Program.Check(m.On && m.Value == shown && m.Kind == "speed", road + " km/h road shows " + shown + ": " + m.Value);
            }

            // Not known: the ring only (value 0), still a message that is on.
            foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                VmsMessage m = VmsAuto.SpeedFor(bad, VmsFacts.None);
                Program.Check(m.On && m.Value == 0 && m.Kind == "speed-unknown", bad + " is not a limit: the ring only");
            }

            // Lowered by what is ahead, never raised: 40 for an accident, 60 for a secured one or a jam, 80 for slow traffic.
            Program.Check(VmsAuto.SpeedFor(100f, Facts(accident: 500f)).Value == 40 && VmsAuto.SpeedFor(100f, Facts(accident: 500f)).Kind == "speed-lowered", "an accident: 40");
            Program.Check(VmsAuto.SpeedFor(100f, Facts(accident: 500f, secured: true)).Value == 60, "an accident with the police there: 60");
            Program.Check(VmsAuto.SpeedFor(100f, Facts(jam: 300f)).Value == 60, "a jam: 60");
            Program.Check(VmsAuto.SpeedFor(100f, Facts(slow: 300f)).Value == 80, "slow traffic: 80");
            Program.Check(VmsAuto.SpeedFor(100f, Facts(accident: 500f, jam: 100f, slow: 50f)).Value == 40, "an accident beats a jam and slow traffic");
            Program.Check(VmsAuto.SpeedFor(100f, Facts(jam: 100f, slow: 50f)).Value == 60, "a jam beats slow traffic");
            Program.Check(VmsAuto.SpeedFor(30f, Facts(accident: 500f)).Value == 30 && VmsAuto.SpeedFor(30f, Facts(accident: 500f)).Kind == "speed", "a road of 30 is not raised to 40 by an accident, and says it is its own limit");
            Program.Check(VmsAuto.SpeedFor(50f, Facts(slow: 100f)).Value == 50, "a limit under the step stays");
            Program.Check(VmsAuto.SpeedFor(60f, Facts(jam: 100f)).Value == 60 && VmsAuto.SpeedFor(60f, Facts(jam: 100f)).Why == "the road's limit", "60 on a road of 60: the road's own limit, so it is not said to be lowered");

            // Every road limit and every combination: between 20 and 120, never above the road's own (when that is in the range), a multiple of 5.
            foreach (float road in new[] { 10f, 25f, 30f, 45f, 50f, 60f, 80f, 100f, 120f, 140f })
            {
                foreach (float accident in new[] { -1f, 100f })
                {
                    foreach (bool secured in new[] { false, true })
                    {
                        foreach (float jam in new[] { -1f, 200f })
                        {
                            foreach (float slow in new[] { -1f, 300f })
                            {
                                VmsMessage m = VmsAuto.SpeedFor(road, Facts(accident, secured, jam, slow));
                                int own = Math.Max(20, Math.Min(120, (int)Math.Floor(road / 5f) * 5));
                                Program.Check(m.Value >= VmsAuto.MinLimit && m.Value <= VmsAuto.MaxLimit && m.Value <= own && m.Value % 5 == 0, $"road {road}, accident {accident}/{secured}, jam {jam}, slow {slow}: shows {m.Value} (own {own})");
                            }
                        }
                    }
                }
            }
        }

        private static List<float> Lanes(params float[] blocked) => new List<float>(blocked);

        private static void Lane()
        {
            // Nothing blocked: every lane is open (a green arrow), whatever the number of lanes.
            for (int count = 1; count <= 6; count++)
            {
                var clear = new List<float>();
                for (int i = 0; i < count; i++)
                    clear.Add(-1f);
                for (int lane = 1; lane <= count; lane++)
                {
                    VmsMessage m = VmsAuto.LaneStateFor(lane, clear);
                    Program.Check(m.On && m.State == "pass" && m.Kind == "lane-open" && m.Class == "info", "lane " + lane + " of " + count + " on a clear road is open");
                }
            }

            // The middle lane of three is blocked 200 m ahead: it tells its own traffic to move over (to the right, the open lane it prefers), and the lanes beside it stay open.
            List<float> middle = Lanes(-1f, 200f, -1f);
            VmsMessage mid = VmsAuto.LaneStateFor(2, middle);
            Program.Check(mid.State == "right" && mid.Kind == "lane-merge-right" && mid.Class == "warn", "a lane blocked 200 m ahead says move to the right when that lane is open");
            Program.Check(VmsAuto.LaneStateFor(1, middle).State == "pass" && VmsAuto.LaneStateFor(3, middle).State == "pass", "the lanes beside it are open: its traffic merges into them");

            // Nearer than 60 m the lane is closed, and exactly there the sign is at the blockage.
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 60f, -1f)).State == "closed" && VmsAuto.LaneStateFor(2, Lanes(-1f, 60.5f, -1f)).State == "right", "60 m is the limit between closed and move over");
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 0f, -1f)).State == "closed" && VmsAuto.LaneStateFor(2, Lanes(-1f, 0f, -1f)).Class == "red", "a blockage at the sign closes the lane (red)");

            // Which way: the right when it is open, else the left, else closed.
            Program.Check(VmsAuto.LaneStateFor(1, Lanes(300f, -1f)).State == "right", "the leftmost lane has only the right to move to");
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 300f)).State == "left", "the rightmost lane has only the left to move to");
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 300f, 300f)).State == "left", "with the right lane blocked too it moves left");
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(300f, 300f, 300f)).State == "closed" && VmsAuto.LaneStateFor(2, Lanes(300f, 300f, 300f)).Why.Contains("no open lane"), "with every lane blocked there is nowhere to go: closed");
            Program.Check(VmsAuto.LaneStateFor(1, Lanes(300f)).State == "closed", "a one-lane road with the lane blocked: closed");
            Program.Check(VmsAuto.LaneStateFor(1, Lanes(300f, 300f)).State == "closed" && VmsAuto.LaneStateFor(2, Lanes(300f, 300f)).State == "closed", "two lanes both blocked: both closed");

            // Beyond the range it does not count.
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 600f, -1f)).State == "right" && VmsAuto.LaneStateFor(2, Lanes(-1f, 601f, -1f)).State == "pass", "a blockage farther than 600 m is not one");
            Program.Check(VmsAuto.LaneStateFor(2, Lanes(-1f, 100f, 650f)).State == "right", "a neighbour blocked beyond the range is open");

            // A lane that is not one of the lanes, none assigned, or nothing read: open, and says why.
            foreach (int lane in new[] { 0, -1, 4, 99 })
            {
                VmsMessage m = VmsAuto.LaneStateFor(lane, middle);
                Program.Check(m.State == "pass" && (m.Why.Contains("no lane is assigned") || m.Why.Contains("not one the road has")), "lane " + lane + " of three: open, and the reason is the lane: " + m.Why);
            }

            Program.Check(VmsAuto.LaneStateFor(1, null).State == "pass" && VmsAuto.LaneStateFor(1, new List<float>()).State == "pass", "no lanes read: open");

            // Every combination of up to four lanes, each blocked or not at three distances: a legal state, and an open lane is never told to move over or closed.
            var rng = new Random(8);
            for (int n = 0; n < 4000; n++)
            {
                int count = rng.Next(1, 5);
                var blocked = new List<float>();
                for (int i = 0; i < count; i++)
                    blocked.Add(rng.Next(3) == 0 ? -1f : rng.Next(3) == 0 ? 20f : rng.Next(2) == 0 ? 200f : 700f);
                for (int lane = 1; lane <= count; lane++)
                {
                    VmsMessage m = VmsAuto.LaneStateFor(lane, blocked);
                    Program.Check(m.State == "pass" || m.State == "closed" || m.State == "left" || m.State == "right", "a legal state");
                    bool blockedHere = blocked[lane - 1] >= 0f && blocked[lane - 1] <= VmsAuto.LaneRange;
                    Program.Check(blockedHere == (m.State != "pass"), "a lane says move over or closed exactly when it is blocked within range: " + string.Join(",", blocked) + " lane " + lane + " -> " + m.State);
                    if (m.State == "right")
                        Program.Check(lane < count && !(blocked[lane] >= 0f && blocked[lane] <= VmsAuto.LaneRange), "it only says right when the lane on the right is open");
                    if (m.State == "left")
                        Program.Check(lane > 1 && !(blocked[lane - 2] >= 0f && blocked[lane - 2] <= VmsAuto.LaneRange), "it only says left when the lane on the left is open");
                }
            }
        }

        private static LaneMath.LaneSample Sample(float x, byte start = 255, byte end = 0, float t = 0.5f, float length = 100f) =>
            new LaneMath.LaneSample { X = x, T = t, Length = length, BlockStart = start, BlockEnd = end };

        private static void Bytes()
        {
            Program.Check(LaneMath.BlockedAhead(Sample(0f)) == -1f, "the game's value for no blockage (255, 0) is none");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 255, 255)) == -1f || LaneMath.BlockedAhead(Sample(0f, 255, 255)) >= 0f, "(a start of 255 and an end of 255 is a blockage at the very end: it is either)");
            Program.Near(LaneMath.BlockedAhead(Sample(0f, 204, 230, 0.5f, 100f)), 30f, 0.1f, "a blockage from 0.8 of a lane 100 m long, the beam at 0.5: 30 m ahead");
            Program.Near(LaneMath.BlockedAhead(Sample(0f, 128, 200, 0.2f, 200f)), (128f / 255f - 0.2f) * 200f, 0.1f, "the length scales it");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 100, 200, 0.5f, 100f)) == 0f, "a blockage that covers the beam is at 0");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 100, 120, 0.5f, 100f)) == -1f, "a blockage that ended before the beam is behind it: not ahead");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 128, 128, 0.5f, 100f)) == 0f || LaneMath.BlockedAhead(Sample(0f, 128, 128, 0.5f, 100f)) > 0f, "a blockage of one byte is a blockage");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 0, 255, 0f, 100f)) == 0f && LaneMath.BlockedAhead(Sample(0f, 0, 255, 1f, 100f)) == 0f, "a whole lane blocked is at 0 wherever the beam is");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 255, 255, 0.99f, 100f)) >= 0f, "a blockage at the very end of the lane is ahead of a beam before it");
            Program.Check(LaneMath.BlockedAhead(Sample(0f, 200, 220, 0.5f, -5f)) == 0f, "a negative length does not give a negative distance");
        }

        private static void Order()
        {
            // Four lanes across a gantry: X from the left (+) to the right (-); the second from the left is blocked 40 m ahead.
            var lanes = new List<LaneMath.LaneSample> { Sample(-5.5f), Sample(5.4f), Sample(1.9f, 140, 180, 0.5f, 100f), Sample(-1.8f) };
            List<float> blocked = LaneMath.BlockedByLane(lanes, 0.3f - 100f);
            Program.Check(blocked.Count == 4, "four lanes: " + blocked.Count);
            Program.Check(blocked[0] < 0f && blocked[1] > 0f && blocked[2] < 0f && blocked[3] < 0f, "from the driver's left: the lane at X 1.9 is the second, and the only one blocked");
            Program.Near(blocked[1], (140f / 255f - 0.5f) * 100f, 0.1f, "and how far ahead it is");

            // Same order as the editor's lanes (Arrange): the number of lanes and the places agree, whatever order they came in.
            var rng = new Random(2);
            for (int n = 0; n < 500; n++)
            {
                var samples = new List<LaneMath.LaneSample>();
                int count = rng.Next(1, 9);
                for (int i = 0; i < count; i++)
                    samples.Add(Sample((float)(rng.NextDouble() * 24 - 12), (byte)rng.Next(256), (byte)rng.Next(256), (float)rng.NextDouble(), 50f + (float)rng.NextDouble() * 150f));
                var xs = new List<float>();
                foreach (LaneMath.LaneSample s in samples)
                    xs.Add(s.X);
                foreach (float minX in new[] { 0.3f, float.NegativeInfinity })
                {
                    List<float> centres = LaneMath.Arrange(xs, minX), byLane = LaneMath.BlockedByLane(samples, minX);
                    Program.Check(centres.Count == byLane.Count, "as many lanes as the editor counts: " + centres.Count + " / " + byLane.Count);
                }
            }

            // Lanes within half a metre are one lane, and the nearest blockage of them counts; a lane beside the first post (X under minX) is not a lane.
            var overlap = new List<LaneMath.LaneSample> { Sample(3.0f), Sample(3.3f, 150, 160, 0.5f, 100f), Sample(-3.0f) };
            List<float> merged = LaneMath.BlockedByLane(overlap, 0.3f);
            Program.Check(merged.Count == 1 && merged[0] > 0f, "two lanes 0.3 m apart are one, blocked when either is: " + merged.Count + " lane(s)");
            List<float> both = LaneMath.BlockedByLane(overlap, float.NegativeInfinity);
            Program.Check(both.Count == 2 && both[0] > 0f && both[1] < 0f, "with both sides (the post stands in the traffic) the lane at X -3 is the second");
            var tie = new List<LaneMath.LaneSample> { Sample(3.0f, 200, 210, 0.5f, 100f), Sample(3.2f, 150, 160, 0.5f, 100f) };
            Program.Near(LaneMath.BlockedByLane(tie, 0f)[0], (150f / 255f - 0.5f) * 100f, 0.1f, "of two blockages in one lane the nearer");
            Program.Check(LaneMath.BlockedByLane(new List<LaneMath.LaneSample>(), 0f).Count == 0, "no lanes, no list");
        }

        private static void Decorating()
        {
            SignDef speed = SignCatalog.ById(VmsAuto.SpeedTemplateId), lane = SignCatalog.ById(VmsAuto.LaneTemplateId), closed = SignCatalog.ById(VmsAuto.LaneClosedTemplateId);

            Program.Check(VmsAuto.KindOf(speed.Hash) == VmsAuto.Kind.Speed && VmsAuto.KindOf(lane.Hash) == VmsAuto.Kind.Lane && VmsAuto.KindOf(closed.Hash) == VmsAuto.Kind.Lane && VmsAuto.KindOf(SignCatalog.ById(VmsAuto.TextTemplateId).Hash) == VmsAuto.Kind.Text && VmsAuto.KindOf(SignCatalog.ById("gb.prohib.09").Hash) == VmsAuto.Kind.None, "the kinds");

            // Manual by default, and a spec from before the field existed is manual.
            Program.Check(speed.Template.Default().Get("src") == "manual" && lane.Template.Default().Get("src") == "manual" && closed.Template.Default().Get("src") == "manual", "manual by default");
            Program.Check(!VmsAuto.IsAutoScreen(speed.Hash, "value=60;speed=auto") && !VmsAuto.IsAutoScreen(lane.Hash, "state=closed;speed=auto"), "an older spec is not followed");

            // The speed screen: the value is the message's, ring only when none.
            string autoSpeed = speed.Template.Default().With("src", "auto").With("value", "99").ToString();
            Program.Check(VmsAuto.IsAutoScreen(speed.Hash, autoSpeed), "an automatic speed screen is one");
            VmsMessage forty = VmsAuto.SpeedFor(100f, new VmsFacts { AccidentAt = 100f, JamAt = -1f, SlowAt = -1f });
            Program.Check(speed.Template.Parse(VmsAuto.Decorate(speed.Hash, autoSpeed, forty)).Get("value") == "40", "the value is the message's");
            Program.Check(speed.Template.Parse(VmsAuto.Decorate(speed.Hash, autoSpeed, VmsAuto.SpeedFor(0f, VmsFacts.None))).Get("value") == "", "an unknown limit: the ring only");
            Program.Check(speed.Template.Parse(VmsAuto.Decorate(speed.Hash, autoSpeed, null)).Get("value") == "", "not read yet: the ring only");
            Program.Check(speed.Template.Parse(autoSpeed).Get("value") == "99", "the item's own value is kept");
            string manualSpeed = speed.Template.Default().ToString();
            Program.Check(VmsAuto.Decorate(speed.Hash, manualSpeed, forty) == manualSpeed, "a manual speed screen is not changed");

            // The lane screens: the state is the message's, off when not read.
            string autoLane = lane.Template.Default().With("src", "auto").ToString();
            string autoClosed = closed.Template.Default().With("src", "auto").ToString();
            VmsMessage right = VmsAuto.LaneStateFor(2, Lanes(-1f, 300f, -1f));
            Program.Check(lane.Template.Parse(VmsAuto.Decorate(lane.Hash, autoLane, right)).Get("state") == "right", "the lane state is the message's");
            Program.Check(closed.Template.Parse(VmsAuto.Decorate(closed.Hash, autoClosed, VmsAuto.LaneStateFor(1, Lanes(-1f)))).Get("state") == "pass", "the closed-lane preset follows the game too: a clear lane is open");
            Program.Check(lane.Template.Parse(VmsAuto.Decorate(lane.Hash, autoLane, null)).Get("state") == "off", "not read yet: off");
            Program.Check(VmsAuto.Decorate(lane.Hash, lane.Template.Default().ToString(), right) == lane.Template.Default().ToString(), "a manual lane screen is not changed");

            // What is drawn: the right figure for every state (the arrow of a state is one polygon, the cross two).
            foreach ((string state, int polygons) in new[] { ("pass", 1), ("closed", 2), ("left", 1), ("right", 1), ("off", 0) })
            {
                SignInstance one = SignInstances.Of(lane, lane.Template.Default().With("state", state).ToString());
                Program.Check(one.Board.Main.Elements.Count == polygons, state + " draws " + polygons + " figure(s)");
            }

            // The editor asks for the value and the state only when the screen is manual.
            Program.Check(speed.Template.FieldOf("value").Visible(speed.Template.Default()) && !speed.Template.FieldOf("value").Visible(speed.Template.Default().With("src", "auto")), "the speed value is hidden for an automatic speed screen");
            foreach (SignDef def in new[] { lane, closed })
                Program.Check(def.Template.FieldOf("state").Visible(def.Template.Default()) && !def.Template.FieldOf("state").Visible(def.Template.Default().With("src", "auto")), def.Id + ": the state is hidden for an automatic lane screen");

            // The words of the note, in both languages, for every state and for the unread screen.
            foreach (bool zh in new[] { true, false })
            {
                Program.Check(VmsAuto.Describe(VmsAuto.Kind.Text, null, zh).Length > 5 && VmsAuto.Describe(VmsAuto.Kind.Speed, null, zh).Length > 5 && VmsAuto.Describe(VmsAuto.Kind.Lane, null, zh).Length > 5, "unread: " + zh);
                Program.Check(VmsAuto.Describe(VmsAuto.Kind.Speed, forty, zh).Contains("40"), "the speed is in the note");
                Program.Check(VmsAuto.Describe(VmsAuto.Kind.Speed, VmsAuto.SpeedFor(0f, VmsFacts.None), zh).Length > 5, "an unknown limit has words");
                foreach (VmsMessage m in new[] { VmsAuto.LaneStateFor(1, Lanes(-1f)), VmsAuto.LaneStateFor(1, Lanes(10f, -1f)), VmsAuto.LaneStateFor(2, Lanes(-1f, 200f, -1f)), VmsAuto.LaneStateFor(2, Lanes(-1f, 200f)), VmsAuto.LaneStateFor(1, Lanes(200f)), VmsAuto.LaneStateFor(0, Lanes(-1f)), VmsAuto.LaneStateFor(5, Lanes(-1f)) })
                {
                    string words = VmsAuto.Describe(VmsAuto.Kind.Lane, m, zh);
                    Program.Check(words.Length > 8 && !words.Contains("{"), "the lane note: " + words);
                    if (zh)
                        Program.Check(IsChinese(words), "the Chinese note is Chinese, with the reason in Chinese: " + words);
                }

                // Every reason a message can carry is translated.
                foreach (VmsMessage m in new[] { VmsAuto.SpeedFor(100f, VmsFacts.None), VmsAuto.SpeedFor(100f, new VmsFacts { AccidentAt = 1f, JamAt = -1f, SlowAt = -1f }), VmsAuto.SpeedFor(100f, new VmsFacts { AccidentAt = 1f, AccidentSecured = true, JamAt = -1f, SlowAt = -1f }), VmsAuto.SpeedFor(100f, new VmsFacts { AccidentAt = -1f, JamAt = 1f, SlowAt = -1f }), VmsAuto.SpeedFor(100f, new VmsFacts { AccidentAt = -1f, JamAt = -1f, SlowAt = 1f }), VmsAuto.SpeedFor(0f, VmsFacts.None) })
                {
                    string words = VmsAuto.Describe(VmsAuto.Kind.Speed, m, zh);
                    if (zh)
                        Program.Check(!HasLatinWord(words), "the Chinese speed note has no English reason: " + words);
                }
            }
        }

        private static bool IsChinese(string text)
        {
            foreach (char c in text)
            {
                if (c >= 0x4E00 && c <= 0x9FFF)
                    return true;
            }

            return false;
        }

        private static bool HasLatinWord(string text)
        {
            int run = 0;
            foreach (char c in text)
            {
                run = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ? run + 1 : 0;
                if (run >= 4)
                    return true;
            }

            return false;
        }
    }
}
