using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>What the road ahead of a variable-message sign holds, as the content rules need it (all distances along the roads from the sign, in metres; negative: none).</summary>
    public struct VmsFacts
    {
        /// <summary>The nearest traffic accident on the sign's own road ahead, and whether the police are already there.</summary>
        public float AccidentAt;

        public bool AccidentSecured;

        /// <summary>The nearest jammed stretch of the road ahead, and how far the jam goes (the jammed stretches that follow one another, m).</summary>
        public float JamAt, JamLength;

        /// <summary>The nearest slow stretch.</summary>
        public float SlowAt;

        public static VmsFacts None => new VmsFacts { AccidentAt = -1f, JamAt = -1f, SlowAt = -1f };

        public bool Any => AccidentAt >= 0f || JamAt >= 0f || SlowAt >= 0f;
    }

    /// <summary>What an automatic text screen shows (GB 5768.2 4.11.1: nothing when there is nothing to say, the screen is off).</summary>
    public sealed class VmsMessage
    {
        public static readonly VmsMessage Off = new VmsMessage { On = false, Kind = "off", Class = "info", Lines = new string[0] };

        /// <summary>For a speed screen: the limit to show (km/h); 0 when none.</summary>
        public int Value;

        /// <summary>For a lane screen: the state to show ("pass", "closed", "left", "right"); "" for any other screen.</summary>
        public string State = "";

        /// <summary>Why the screen shows what it shows ("the road's limit", "an accident ahead", "the lane ahead is blocked"...): for the log and the editor's note, not drawn.</summary>
        public string Why = "";

        public bool On;

        /// <summary>"accident", "accident-secured", "jam", "slow" or "off": what the message is about (the log, and whether it changed).</summary>
        public string Kind = "off";

        /// <summary>The kind of information for the colour (Table 10): "warn" yellow, "info" green.</summary>
        public string Class = "info";

        /// <summary>At most four lines, each at most MaxLine characters (the length of the field a player types a line in).</summary>
        public string[] Lines = new string[0];

        public bool SameAs(VmsMessage other) =>
            other != null && On == other.On && Kind == other.Kind && Class == other.Class && Value == other.Value && State == other.State && string.Join("\n", Lines) == string.Join("\n", other.Lines);
    }

    /// <summary>
    /// The automatic content of a variable-message text screen (design phase D, 0.8.0): what is on the road ahead of it, read from the game (accidents, congestion), becomes the words of the screen.
    /// Pure: the game's reader makes the network (TrafficWalk), this makes the facts and the message, the tests check both. **Only what the game's log has shown real values of is used**: the
    /// accident sites (flags and place read in the 0.5.7 probe: TrafficAccident on a road edge, Secured when the police are there) and the speed ratio of the lanes (the traffic screen's own).
    /// Weather is not used yet: the one weather sample read so far was clear (every figure 0), so the units of precipitation and fog are not known.
    /// </summary>
    public static class VmsAuto
    {
        public const string TextTemplateId = "gb.vms.text", SpeedTemplateId = "gb.vms.speed", LaneTemplateId = "gb.vms.lane", LaneClosedTemplateId = "gb.vms.lane.closed";

        /// <summary>What an automatic screen shows: lines of text, a speed limit, or the state of a lane.</summary>
        public enum Kind
        {
            None = 0,
            Text = 1,
            Speed = 2,
            Lane = 3
        }

        private static readonly uint s_SpeedHash = SignCatalog.HashOf(SpeedTemplateId), s_LaneHash = SignCatalog.HashOf(LaneTemplateId), s_LaneClosedHash = SignCatalog.HashOf(LaneClosedTemplateId);

        /// <summary>The kind of screen a saved type is (None for any sign that has no automatic content).</summary>
        public static Kind KindOf(uint typeHash) =>
            typeHash == s_Hash ? Kind.Text : typeHash == s_SpeedHash ? Kind.Speed : typeHash == s_LaneHash || typeHash == s_LaneClosedHash ? Kind.Lane : Kind.None;

        /// <summary>The longest line a text screen is given (the field a player types a line in holds 10 characters).</summary>
        public const int MaxLine = 10;

        /// <summary>The farthest ahead (m) a road is looked at for a message, and the length of a jam that is worth saying (m).</summary>
        public const float DefaultRange = 1500f;

        public const float JamLengthWorthSaying = 300f;

        private static readonly uint s_Hash = SignCatalog.HashOf(TextTemplateId);

        // ------------------------------------------------------------------ the facts

        /// <summary>
        /// The facts from the network a walk made: the stretches of the sign's own road (the road of the first stretch, or the same name when the game gave no road), within range metres. A cross road
        /// that the walk also drew is not "ahead": a jam on it is not one the traffic of this road meets.
        /// </summary>
        public static VmsFacts FactsOf(TmNetwork net, float range)
        {
            VmsFacts facts = VmsFacts.None;
            if (net == null || !net.Known || net.Edges.Count == 0)
                return facts;
            TmEdge first = net.Edges[0];
            float jamEnd = -1f;
            foreach (TmEdge edge in net.Edges)
            {
                if (!OnRoadOf(first, edge) || edge.Distance > range)
                    continue;
                if (edge.Accident != TmAccident.None && (facts.AccidentAt < 0f || edge.Distance < facts.AccidentAt))
                {
                    facts.AccidentAt = edge.Distance;
                    facts.AccidentSecured = edge.Accident == TmAccident.Secured;
                }

                if (edge.Level == TrafficLevel.Jam && (facts.JamAt < 0f || edge.Distance < facts.JamAt))
                    facts.JamAt = edge.Distance;
                if (edge.Level == TrafficLevel.Slow && (facts.SlowAt < 0f || edge.Distance < facts.SlowAt))
                    facts.SlowAt = edge.Distance;
            }

            // The length of the jam that starts at its nearest end: the jammed stretches that follow on (a stretch starts where the one before ended, give or take a metre or two).
            if (facts.JamAt >= 0f)
            {
                jamEnd = facts.JamAt;
                bool grew = true;
                while (grew)
                {
                    grew = false;
                    foreach (TmEdge edge in net.Edges)
                    {
                        if (!OnRoadOf(first, edge) || edge.Level != TrafficLevel.Jam || edge.Distance > range)
                            continue;
                        if (edge.Distance + edge.Length > jamEnd + 0.5f && edge.Distance <= jamEnd + 5f)
                        {
                            jamEnd = edge.Distance + edge.Length;
                            grew = true;
                        }
                    }
                }

                facts.JamLength = jamEnd - facts.JamAt;
            }

            return facts;
        }

        private static bool OnRoadOf(TmEdge first, TmEdge edge)
        {
            if (ReferenceEquals(first, edge))
                return true;
            if (first.Road != 0)
                return edge.Road == first.Road;
            return first.Name.Length > 0 && edge.Name == first.Name;
        }

        // ------------------------------------------------------------------ the message

        /// <summary>
        /// A distance as a Chinese screen says it: 米 in 50 m steps under 200 m (at least 50), in 100 m steps to 1 km, then 公里 with one decimal. Rounded half up (Math.Round alone rounds 1250 to
        /// 1.2). No spaces, so that a line fits the ten characters of a screen's line: "前方1.5公里事故".
        /// </summary>
        public static string Distance(float metres)
        {
            if (metres < 1000f)
            {
                int step = metres < 200f ? 50 : 100;
                int rounded = Math.Max(50, (int)Math.Round(metres / step, MidpointRounding.AwayFromZero) * step);
                if (rounded >= 1000)
                    return "1.0公里";
                return rounded.ToString(CultureInfo.InvariantCulture) + "米";
            }

            return (Math.Round(metres / 100.0, MidpointRounding.AwayFromZero) / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "公里";
        }

        /// <summary>The message for the facts: an accident first, then a jam, then slow traffic; nothing when the road ahead is free (the screen is off).</summary>
        public static VmsMessage Decide(VmsFacts f)
        {
            if (f.AccidentAt >= 0f)
            {
                return f.AccidentSecured
                    ? new VmsMessage { On = true, Kind = "accident-secured", Class = "warn", Lines = new[] { "前方" + Distance(f.AccidentAt) + "事故", "警方已到场", "请谨慎驾驶" } }
                    : new VmsMessage { On = true, Kind = "accident", Class = "warn", Lines = new[] { "前方" + Distance(f.AccidentAt) + "事故", "请减速慢行" } };
            }

            if (f.JamAt >= 0f)
            {
                var lines = new List<string> { "前方" + Distance(f.JamAt) + "拥堵", "请减速慢行" };
                if (f.JamLength >= JamLengthWorthSaying)
                    lines.Insert(1, "拥堵约" + Distance(f.JamLength));
                return new VmsMessage { On = true, Kind = "jam", Class = "warn", Lines = lines.ToArray() };
            }

            if (f.SlowAt >= 0f)
                return new VmsMessage { On = true, Kind = "slow", Class = "info", Lines = new[] { "前方" + Distance(f.SlowAt) + "车多", "请保持车距" } };

            return VmsMessage.Off;
        }

        // ------------------------------------------------------------------ the screen

        /// <summary>
        /// Whether a placed item (by its saved type hash and settings) is a screen that follows the game (its field "src" is "auto"). None is without the extension that reads the game (LiveContent):
        /// such a screen shows what was set by hand.
        /// </summary>
        public static bool IsAutoScreen(uint typeHash, string spec)
        {
            if (!LiveContent.On || KindOf(typeHash) == Kind.None)
                return false;
            SignDef def = SignCatalog.ByHash(typeHash);
            return def?.Template != null && def.Template.Parse(spec ?? "").Is("src", "auto");
        }

        /// <summary>
        /// The settings of an automatic screen as it is drawn: its own with what the game says now, written into the fields the screen is drawn from (the lines and the colour of a text screen, the
        /// value of a speed screen, the state of a lane screen). A screen that has not been read yet is dark (a lane screen off, a speed screen the ring only). The player's own settings are kept in
        /// the item and are what the screen shows when it is changed back to manual. Not saved. A screen that is not automatic is returned as it is.
        /// </summary>
        public static string Decorate(uint typeHash, string spec, VmsMessage message)
        {
            Kind kind = KindOf(typeHash);
            SignDef def = SignCatalog.ByHash(typeHash);
            GuideTemplate template = def?.Template;
            if (kind == Kind.None || template == null)
                return spec;
            GuideSpec s = template.Parse(spec ?? "");
            if (!s.Is("src", "auto"))
                return spec;
            bool on = message != null && message.On;
            switch (kind)
            {
                case Kind.Text:
                    string[] keys = { "l1", "l2", "l3", "l4" };
                    for (int i = 0; i < keys.Length; i++)
                        s = s.With(keys[i], on && i < message.Lines.Length ? message.Lines[i] : "");
                    if (on)
                        s = s.With("cls", message.Class);
                    break;
                case Kind.Speed:
                    s = s.With("value", on && message.Value > 0 ? message.Value.ToString(CultureInfo.InvariantCulture) : "");
                    break;
                default:
                    s = s.With("state", on && message.State.Length > 0 ? message.State : "off");
                    break;
            }

            return s.ToString();
        }

        // ------------------------------------------------------------------ the speed screen

        /// <summary>The lowest and the highest limit an automatic speed screen shows (km/h): a limit sign of the standard has values from 5 to 120.</summary>
        public const int MinLimit = 20, MaxLimit = 120;

        /// <summary>
        /// The limit an automatic speed screen shows: the road's own (rounded down to a multiple of 5, kept between MinLimit and MaxLimit), lowered by what is ahead: **40 for an accident that the
        /// police have not secured, 60 for a secured one or a jam, 80 for slow traffic**, never higher than the road's limit. These steps are this mod's: the standard says nothing of what a variable
        /// speed limit is to be. roadKmh is 0 or less when the road's limit is not known: the screen then shows the ring only (Value 0).
        /// </summary>
        public static VmsMessage SpeedFor(float roadKmh, VmsFacts f)
        {
            if (!(roadKmh > 0f) || float.IsNaN(roadKmh) || float.IsInfinity(roadKmh))
                return new VmsMessage { On = true, Kind = "speed-unknown", Class = "warn", Value = 0, Why = "the road's limit is not known" };
            int limit = Math.Max(MinLimit, Math.Min(MaxLimit, (int)Math.Floor(roadKmh / 5f) * 5));
            int shown = limit;
            string why = "the road's limit";
            if (f.AccidentAt >= 0f && !f.AccidentSecured)
            {
                shown = Math.Min(limit, 40);
                why = "an accident ahead";
            }
            else if (f.AccidentAt >= 0f)
            {
                shown = Math.Min(limit, 60);
                why = "an accident ahead (the police are there)";
            }
            else if (f.JamAt >= 0f)
            {
                shown = Math.Min(limit, 60);
                why = "a jam ahead";
            }
            else if (f.SlowAt >= 0f)
            {
                shown = Math.Min(limit, 80);
                why = "slow traffic ahead";
            }

            if (shown >= limit)
                why = "the road's limit";
            return new VmsMessage { On = true, Kind = shown < limit ? "speed-lowered" : "speed", Class = "warn", Value = shown, Why = why };
        }

        // ------------------------------------------------------------------ the lane screen

        /// <summary>How far ahead (m) a blocked lane counts for a lane screen, and how near the blockage has to be for the screen to say "closed" instead of "move over".</summary>
        public const float LaneRange = 600f, LaneCloseMetres = 60f;

        /// <summary>
        /// The state of the lane screen over lane lane (1 is the driver's leftmost) when blockedAhead[i] is how far ahead lane i+1 is blocked (m from the sign; negative: not blocked). **A lane that is
        /// blocked ahead tells its own traffic to move over** (a yellow arrow slanting toward the lane it can move into: the one on the right if that is open, else the one on the left), **and says
        /// closed (a red cross) when the blockage is within LaneCloseMetres or when there is no open lane beside it to move into**; the open lanes beside it show a green arrow (the traffic of the
        /// blocked lane merges into them). A lane that is not blocked, one that is not one of the lanes, or none assigned (0), is shown open. The steps are the usual practice of lane-control
        /// signals; the standard (4.11) shows none of these figures.
        /// </summary>
        public static VmsMessage LaneStateFor(int lane, IReadOnlyList<float> blockedAhead)
        {
            bool known = blockedAhead != null && lane >= 1 && lane <= blockedAhead.Count;
            if (!known)
                return new VmsMessage { On = true, Kind = "lane-open", Class = "info", State = "pass", Why = lane <= 0 ? "no lane is assigned to this board" : "the lane is not one the road has" };
            bool Blocked(int i) => i >= 0 && i < blockedAhead.Count && blockedAhead[i] >= 0f && blockedAhead[i] <= LaneRange;
            if (!Blocked(lane - 1))
                return new VmsMessage { On = true, Kind = "lane-open", Class = "info", State = "pass", Why = "the lane is open" };
            if (blockedAhead[lane - 1] <= LaneCloseMetres)
                return new VmsMessage { On = true, Kind = "lane-closed", Class = "red", State = "closed", Why = "the lane is blocked here" };
            bool rightOpen = lane < blockedAhead.Count && !Blocked(lane), leftOpen = lane > 1 && !Blocked(lane - 2);
            if (rightOpen)
                return new VmsMessage { On = true, Kind = "lane-merge-right", Class = "warn", State = "right", Why = "the lane is blocked ahead, move to the right" };
            if (leftOpen)
                return new VmsMessage { On = true, Kind = "lane-merge-left", Class = "warn", State = "left", Why = "the lane is blocked ahead, move to the left" };
            return new VmsMessage { On = true, Kind = "lane-closed", Class = "red", State = "closed", Why = "the lane is blocked ahead and there is no open lane beside it" };
        }

        // ------------------------------------------------------------------ the editor's note

        /// <summary>What an automatic screen shows now, in words, for the editor's note; zh for Chinese.</summary>
        public static string Describe(Kind kind, VmsMessage now, bool zh)
        {
            if (now == null)
                return zh ? "还没有读到前方路况" : "the road ahead has not been read yet";
            switch (kind)
            {
                case Kind.Speed:
                    return now.Value > 0
                        ? (zh ? now.Value + " km/h（" + WhyZh(now.Why) + "）" : now.Value + " km/h (" + now.Why + ")")
                        : (zh ? "只有红圈（" + WhyZh(now.Why) + "）" : "the ring only (" + now.Why + ")");
                case Kind.Lane:
                    string state = now.State == "closed" ? (zh ? "红叉，车道关闭" : "a red cross, the lane is closed") : now.State == "left" ? (zh ? "黄色箭头，向左并道" : "a yellow arrow, merge left") : now.State == "right" ? (zh ? "黄色箭头，向右并道" : "a yellow arrow, merge right") : (zh ? "绿色箭头，可通行" : "a green arrow, open");
                    return state + (zh ? "（" + WhyZh(now.Why) + "）" : " (" + now.Why + ")");
                default:
                    return now.On ? "“" + string.Join(zh ? "，" : ", ", now.Lines) + "”" : (zh ? "关闭（前方没有事故或拥堵）" : "off (no accident or jam ahead)");
            }
        }

        private static string WhyZh(string why)
        {
            switch (why)
            {
                case "the road's limit": return "路段限速";
                case "the road's limit is not known": return "读不到这条路的限速";
                case "an accident ahead": return "前方有事故";
                case "an accident ahead (the police are there)": return "前方有事故，警察已到";
                case "a jam ahead": return "前方拥堵";
                case "slow traffic ahead": return "前方车多";
                case "the lane is blocked here": return "这条车道在这里被堵";
                case "the lane is blocked ahead, move to the right": return "这条车道前方被堵，请向右并道";
                case "the lane is blocked ahead, move to the left": return "这条车道前方被堵，请向左并道";
                case "the lane is blocked ahead and there is no open lane beside it": return "这条车道前方被堵，旁边没有可并入的车道";
                case "the lane is open": return "车道畅通";
                case "no lane is assigned to this board": return "这块牌没有分配车道：在板块列表里给它选一条车道";
                case "the lane is not one the road has": return "路上没有这条车道";
                default: return why;
            }
        }

        // ------------------------------------------------------------------ what the game's reader has made

        private static readonly Dictionary<int, VmsMessage> s_Live = new Dictionary<int, VmsMessage>();
        private static int s_Changes;

        /// <summary>Raised by one whenever a screen's message changes or one is removed (the renderer looks at it every frame).</summary>
        public static int Changes => s_Changes;

        /// <summary>Publishes what a screen shows now. False (and no change) when it is what it showed.</summary>
        public static bool Set(int id, VmsMessage message)
        {
            lock (s_Live)
            {
                if (s_Live.TryGetValue(id, out VmsMessage known) && known.SameAs(message))
                    return false;
                s_Live[id] = message;
                s_Changes++;
                return true;
            }
        }

        /// <summary>What a screen shows now; null when it has not been read yet.</summary>
        public static VmsMessage Get(int id)
        {
            lock (s_Live)
                return s_Live.TryGetValue(id, out VmsMessage message) ? message : null;
        }

        /// <summary>Forgets a screen (its item is gone).</summary>
        public static void Remove(int id)
        {
            lock (s_Live)
                s_Live.Remove(id);
        }

        public static void Clear()
        {
            lock (s_Live)
            {
                if (s_Live.Count > 0)
                    s_Changes++;
                s_Live.Clear();
            }
        }
    }
}
