using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>What a reading of the junction ahead said, in the player's two languages: what was filled in, and the notes about it.</summary>
    public sealed class JrNote
    {
        public string Zh = "", En = "";

        /// <summary>The notes after the summary: a distance the standard does not allow, a name that was cut, lanes that were not known.</summary>
        public readonly List<string> ZhNotes = new List<string>(), EnNotes = new List<string>();

        public string Text(bool zh) => zh ? Zh : En;

        public List<string> Notes(bool zh) => zh ? ZhNotes : EnNotes;

        public void Note(string zh, string en)
        {
            ZhNotes.Add(zh);
            EnNotes.Add(en);
        }
    }

    /// <summary>
    /// Fills in the junction signs of GB 5768.2 8.3 from the junction ahead (JunctionRead): which signs can be read, and for each the settings that the reading decides (the form of the
    /// junction, the name of every direction, the lanes and their arrows, the road's own name). The settings are written into the sign's spec: nothing is read again when the sign is drawn,
    /// and the player may change any of it afterwards. Pure.
    /// </summary>
    public static partial class JunctionFill
    {
        /// <summary>The signs this fills in: 路1 (diagram), 路3 (stacked), 路4 (lanes) and its strip of arrows, 路6 (road name), 路7 (road name and direction), and the junctions-ahead board (0.12.78).</summary>
        public static readonly string[] Ids = { "gb.guide8.01", "gb.guide8.03", "gb.guide8.04", "gb.guide8.04b", "gb.guide8.06", "gb.guide8.07", RoadGuideSigns.MultiId };

        /// <summary>The longest name a place field holds (RoadGuideSigns.Place).</summary>
        public const int MaxName = 7;

        /// <summary>How near the junction a sign stands to count as "at" it (the road name on its arrow).</summary>
        public const float AtJunction = 40f;

        public static bool CanRead(string id) => Array.IndexOf(Ids, id) >= 0;

        /// <summary>The signs that read the junctions after the first one as well (JunctionSequence).</summary>
        public static bool WantsSequence(string id) => id == RoadGuideSigns.MultiId;

        // ------------------------------------------------------------------ what the arms say

        private static float Centre(JrDirection d) => d == JrDirection.Left ? 90f : d == JrDirection.Right ? -90f : 0f;

        /// <summary>The arms of a direction, the most important first (the most lanes, then the nearest to the middle of the direction, then by name).</summary>
        private static List<JrArm> ArmsOf(JrResult r, JrDirection direction)
        {
            var arms = new List<JrArm>();
            foreach (JrArm arm in r.Arms)
            {
                if (arm.Direction == direction)
                    arms.Add(arm);
            }

            arms.Sort((a, b) =>
            {
                int c = b.Lanes.CompareTo(a.Lanes);
                if (c != 0)
                    return c;
                c = Math.Abs(a.Angle - Centre(direction)).CompareTo(Math.Abs(b.Angle - Centre(direction)));
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            return arms;
        }

        /// <summary>The names of a direction: at most two (8.1.4 c), each once, the unnamed roads left out.</summary>
        public static List<string> NamesOf(JrResult r, JrDirection direction)
        {
            var names = new List<string>();
            foreach (JrArm arm in ArmsOf(r, direction))
            {
                if (arm.Name.Length > 0 && !names.Contains(arm.Name) && names.Count < 2)
                    names.Add(arm.Name);
            }

            return names;
        }

        /// <summary>The form of the junction as 路1 draws it: "x" crossroads, "t" left and right, "r" ahead and right, "l" ahead and left; null for any other.</summary>
        public static string FormOf(JrResult r)
        {
            bool a = r.Has(JrDirection.Ahead), l = r.Has(JrDirection.Left), rt = r.Has(JrDirection.Right);
            if (a && l && rt)
                return "x";
            if (l && rt)
                return "t";
            if (a && rt)
                return "r";
            if (a && l)
                return "l";
            return null;
        }

        /// <summary>The name of the road that crosses the junction when its left and its right are one road (the same road, or the same name); "" when they are two or there is none.</summary>
        public static string CrossName(JrResult r)
        {
            List<JrArm> left = ArmsOf(r, JrDirection.Left), right = ArmsOf(r, JrDirection.Right);
            if (left.Count == 0 || right.Count == 0)
                return "";
            JrArm l = left[0], rt = right[0];
            bool same = (l.Road != 0 && l.Road == rt.Road) || (l.Name.Length > 0 && l.Name == rt.Name);
            return same ? (l.Name.Length > 0 ? l.Name : rt.Name) : "";
        }

        // ------------------------------------------------------------------ lanes

        /// <summary>The arrow kind of a lane (路4's: s, l, r, sl, sr) from the movements it leads to; a lane that leads left and right without going ahead is drawn as left, one that leads everywhere as ahead.</summary>
        public static string ArrowOf(int moves)
        {
            bool l = (moves & JrMoves.Left) != 0, a = (moves & JrMoves.Ahead) != 0, r = (moves & JrMoves.Right) != 0;
            if (a && l && r)
                return "s";
            if (a && l)
                return "sl";
            if (a && r)
                return "sr";
            if (a)
                return "s";
            if (l && r)
                return "l";
            if (r)
                return "r";
            return "l"; // left, or a U-turn only (it leaves on the left)
        }

        /// <summary>The direction whose destination names a lane: ahead when it goes ahead, else the way it turns.</summary>
        private static JrDirection MainOf(int moves)
        {
            bool l = (moves & JrMoves.Left) != 0, a = (moves & JrMoves.Ahead) != 0, r = (moves & JrMoves.Right) != 0;
            if (a)
                return JrDirection.Ahead;
            if (r && !l)
                return JrDirection.Right;
            return JrDirection.Left;
        }

        /// <summary>
        /// The lanes of the road at the junction as arrows, from the left: the lanes that were read, or (when none were) one lane for each way out, left, ahead, right. At most MaxLanes: a longer
        /// row has its longest run of lanes that are the same arrow shortened until it fits. known is false when the lanes were not read; shortened is how many lanes were dropped.
        /// </summary>
        public static List<int> LaneRow(JrResult r, out bool known, out int shortened)
        {
            shortened = 0;
            var row = new List<int>();
            known = r.LaneMoves.Count > 0;
            if (known)
            {
                row.AddRange(r.LaneMoves);
            }
            else
            {
                if (r.Has(JrDirection.Left))
                    row.Add(JrMoves.Left);
                if (r.Has(JrDirection.Ahead))
                    row.Add(JrMoves.Ahead);
                if (r.Has(JrDirection.Right))
                    row.Add(JrMoves.Right);
            }

            while (row.Count > RoadGuideSigns.MaxLanes)
            {
                int bestStart = -1, bestLength = 1;
                for (int i = 0; i < row.Count;)
                {
                    int j = i;
                    while (j + 1 < row.Count && ArrowOf(row[j + 1]) == ArrowOf(row[i]))
                        j++;
                    if (j - i + 1 > bestLength)
                    {
                        bestLength = j - i + 1;
                        bestStart = i;
                    }

                    i = j + 1;
                }

                if (bestStart < 0)
                {
                    shortened += row.Count - RoadGuideSigns.MaxLanes;
                    row.RemoveRange(RoadGuideSigns.MaxLanes, row.Count - RoadGuideSigns.MaxLanes);
                    break;
                }

                row.RemoveAt(bestStart);
                shortened++;
            }

            return row;
        }

        // ------------------------------------------------------------------ the spec

        /// <summary>
        /// The settings of a sign of Ids with what the reading found filled in; null (and the reason in the note) when nothing could be filled in. spec is the written settings the sign has now: what the
        /// reading does not decide (the character height, the compass mark if the player removed it) is kept.
        /// </summary>
        public static string Apply(SignDef def, string spec, JrResult r, out JrNote note)
        {
            note = new JrNote();
            if (def == null || def.Template == null || !CanRead(def.Id))
            {
                note.Zh = "这块牌不能自动读取路口。";
                note.En = "This sign cannot be filled in from a junction.";
                return null;
            }

            if (r == null || !r.Found)
            {
                string why = r == null ? "" : r.Why;
                note.Zh = "没有读到前方路口：" + WhyText(why, true) + "。";
                note.En = "No junction was read: " + WhyText(why, false) + ".";
                return null;
            }

            GuideSpec s = def.Template.Parse(spec);
            string result;
            switch (def.Id)
            {
                case "gb.guide8.01": result = FillDiagram(def, s, r, note); break;
                case "gb.guide8.03": result = FillStack(def, s, r, note); break;
                case "gb.guide8.04": result = FillLanes(def, s, r, note, true); break;
                case "gb.guide8.04b": result = FillLanes(def, s, r, note, false); break;
                case "gb.guide8.06": result = FillName(def, s, r, note); break;
                case RoadGuideSigns.MultiId: result = FillMulti(def, s, r, note); break;
                default: result = FillNameDirection(def, s, r, note); break;
            }

            // How far the sign stands from the junction is not remarked on (4 Nov, the owner: "把这个和类似的删了": the notes of 8.3.2.4, 8.3.3 and 8.3.4): the player puts a sign where they like.
            return result;
        }

        /// <summary>Sets a text field and notes when the name did not fit (a place field holds MaxName characters).</summary>
        private static GuideSpec Put(GuideSpec s, string key, string value, JrNote note)
        {
            // Cut here (a place field holds more since 0.12.1, for a pictogram after the name: 学府路[地铁]); a name read from the game is a name only.
            GuideSpec made = s.With(key, value != null && value.Length > MaxName ? value.Substring(0, MaxName) : value);
            if (value != null && value.Length > MaxName && made.Get(key).Length < value.Length)
                note.Note($"「{value}」超过 {MaxName} 个字，已截成「{made.Get(key)}」。", $"\"{value}\" is longer than {MaxName} characters and was cut to \"{made.Get(key)}\".");
            return made;
        }

        private static GuideSpec PutNames(GuideSpec s, string key1, string key2, List<string> names, JrNote note)
        {
            s = Put(s, key1, names.Count > 0 ? names[0] : "", note);
            return Put(s, key2, names.Count > 1 ? names[1] : "", note);
        }

        private static string Join(List<string> names, bool zh) => names.Count == 0 ? (zh ? "（无名）" : "(unnamed)") : string.Join(zh ? "、" : ", ", names);

        private static string Summary(JrResult r, bool zh, string form)
        {
            var sb = new StringBuilder();
            string kind = form == "x" ? (zh ? "十字" : "crossroads") : form == "t" ? (zh ? "丁字" : "T junction") : form == "r" ? (zh ? "右侧岔路" : "junction with a road on the right") : form == "l" ? (zh ? "左侧岔路" : "junction with a road on the left") : (zh ? "路口" : "junction");
            sb.Append(zh ? $"前方 {r.Distance:0} m {kind}：" : $"{kind} {r.Distance:0} m ahead: ");
            var parts = new List<string>();
            foreach ((JrDirection d, string zhName, string enName) in new[] { (JrDirection.Ahead, "直行", "ahead"), (JrDirection.Left, "左", "left"), (JrDirection.Right, "右", "right") })
            {
                if (r.Has(d))
                    parts.Add((zh ? zhName : enName) + " " + Join(NamesOf(r, d), zh));
            }

            sb.Append(string.Join(zh ? "；" : "; ", parts));
            return sb.ToString();
        }

        private static string Compass(JrResult r) => TrafficMap.CompassOf(r.HeadX, r.HeadZ);

        private static string FillDiagram(SignDef def, GuideSpec s, JrResult r, JrNote note)
        {
            string form = FormOf(r);
            if (form == null)
            {
                note.Zh = "前方路口的形状图中没有（只有一个方向可走），没有填写。";
                note.En = "The junction ahead has a shape this sign does not draw (only one way on), nothing was filled in.";
                return null;
            }

            s = s.With("form", form);
            string cross = CrossName(r);
            s = PutNames(s, "to1", "to2", NamesOf(r, JrDirection.Ahead), note);
            if (cross.Length > 0)
            {
                // One road crosses: its name is on the bar in the hexagon, the ends carry no name (the game does not know where it leads: the standard puts the places reached there, 8.1.7 b).
                s = Put(s, "xroad", cross, note);
                s = Put(s, "tl1", "", note);
                s = Put(s, "tl2", "", note);
                s = Put(s, "tr1", "", note);
                s = Put(s, "tr2", "", note);
                note.Note("左右是同一条路：路名放在箭杆上（图 205 d），两端的地名游戏里没有，没有填。", "Left and right are one road: its name is on the bar (Fig. 205 d); the places at the ends are not known to the game and were not filled in.");
            }
            else
            {
                s = Put(s, "xroad", "", note);
                s = PutNames(s, "tl1", "tl2", NamesOf(r, JrDirection.Left), note);
                s = PutNames(s, "tr1", "tr2", NamesOf(r, JrDirection.Right), note);
            }

            if (s.Get("dir").Length > 0)
                s = s.With("dir", Compass(r));
            note.Zh = "已读取" + Summary(r, true, form) + "。";
            note.En = "Read: " + Summary(r, false, form) + ".";
            return s.ToString();
        }

        private static string FillStack(SignDef def, GuideSpec s, JrResult r, JrNote note)
        {
            string form = FormOf(r);
            if (form == null)
            {
                note.Zh = "前方路口只有一个方向可走，没有填写。";
                note.En = "The junction ahead has only one way on, nothing was filled in.";
                return null;
            }

            s = PutNames(s, "to1", "to2", NamesOf(r, JrDirection.Ahead), note);
            s = PutNames(s, "tl1", "tl2", NamesOf(r, JrDirection.Left), note);
            s = PutNames(s, "tr1", "tr2", NamesOf(r, JrDirection.Right), note);
            note.Zh = "已读取" + Summary(r, true, form) + "。";
            note.En = "Read: " + Summary(r, false, form) + ".";
            return s.ToString();
        }

        private static string FillLanes(SignDef def, GuideSpec s, JrResult r, JrNote note, bool names)
        {
            List<int> row = LaneRow(r, out bool known, out int shortened);
            if (row.Count == 0)
            {
                note.Zh = "没有读到前方路口的车道，没有填写。";
                note.En = "No lanes were read at the junction ahead, nothing was filled in.";
                return null;
            }

            s = s.With("n", row.Count.ToString(CultureInfo.InvariantCulture));
            string previous = "";
            for (int i = 0; i < RoadGuideSigns.MaxLanes; i++)
            {
                string place = "", arrow = "s";
                if (i < row.Count)
                {
                    arrow = ArrowOf(row[i]);
                    if (names)
                    {
                        List<string> n = NamesOf(r, MainOf(row[i]));
                        string name = n.Count > 0 ? n[0] : "";
                        // A name is written once over a run of neighbouring lanes that lead to the same place.
                        place = name.Length > 0 && name != previous ? name : "";
                        previous = name;
                    }
                }

                s = Put(s, "to" + (i + 1), place, note);
                s = s.With("a" + (i + 1), arrow);
            }

            var zh = new StringBuilder();
            var en = new StringBuilder();
            foreach (int moves in row)
            {
                string a = ArrowOf(moves);
                zh.Append(a == "s" ? "↑" : a == "l" ? "↰" : a == "r" ? "↱" : a == "sl" ? "↰↑" : "↑↱").Append(' ');
                en.Append(a == "s" ? "↑" : a == "l" ? "↰" : a == "r" ? "↱" : a == "sl" ? "↰↑" : "↑↱").Append(' ');
            }

            note.Zh = $"已读取前方 {r.Distance:0} m 路口的 {row.Count} 条车道（从左起）：{zh.ToString().Trim()}";
            note.En = $"Read the {row.Count} lane(s) of the junction {r.Distance:0} m ahead (from the left): {en.ToString().Trim()}";
            if (!known)
                note.Note("车道没有读到：按每个出口一条车道画（左、直、右）。", "The lanes were not read: one lane for each way out (left, ahead, right) was drawn.");
            if (shortened > 0)
                note.Note($"车道超过 {RoadGuideSigns.MaxLanes} 条：同样箭头的相邻车道合并，少画了 {shortened} 条。", $"More than {RoadGuideSigns.MaxLanes} lanes: neighbouring lanes with the same arrow were merged, {shortened} fewer are drawn.");
            return s.ToString();
        }

        private static string FillName(SignDef def, GuideSpec s, JrResult r, JrNote note)
        {
            // At the junction (notice, 8.3.3.3) the sign names the road that crosses it; after one (confirmation, 8.3.4.3) or mid-block, the road the driver is on.
            string name, which;
            if (r.Distance <= AtJunction)
            {
                name = CrossName(r);
                if (name.Length == 0)
                {
                    List<string> left = NamesOf(r, JrDirection.Left), right = NamesOf(r, JrDirection.Right);
                    name = left.Count > 0 ? left[0] : right.Count > 0 ? right[0] : "";
                }

                which = "crossing road";
            }
            else
            {
                name = r.OwnName;
                which = "own road";
            }

            if (name.Length == 0)
            {
                note.Zh = which == "own road" ? "这条路没有名字，没有填写。" : "路口的横路没有名字，没有填写。";
                note.En = which == "own road" ? "This road has no name, nothing was filled in." : "The crossing road has no name, nothing was filled in.";
                return null;
            }

            s = Put(s, "name", name, note);
            note.Zh = (which == "own road" ? "已读取本路路名：" : $"已读取横路路名（路口在前方 {r.Distance:0} m）：") + name;
            note.En = (which == "own road" ? "Read the name of this road: " : $"Read the name of the crossing road (junction {r.Distance:0} m ahead): ") + name;
            return s.ToString();
        }

        private static string FillNameDirection(SignDef def, GuideSpec s, JrResult r, JrNote note)
        {
            string form = s.Get("form");
            JrDirection direction = form == "l" ? JrDirection.Left : form == "u" ? JrDirection.Ahead : JrDirection.Right;
            string name;
            if (form == "b")
            {
                name = CrossName(r);
                if (name.Length == 0)
                {
                    List<string> left = NamesOf(r, JrDirection.Left);
                    name = left.Count > 0 ? left[0] : "";
                }
            }
            else
            {
                List<string> names = NamesOf(r, direction);
                name = names.Count > 0 ? names[0] : "";
            }

            if (name.Length == 0)
            {
                note.Zh = "这个方向没有读到路名，没有填写。";
                note.En = "No road name was read for this direction, nothing was filled in.";
                return null;
            }

            s = Put(s, "name", name, note);
            note.Zh = "已读取" + (form == "u" ? "前方" : form == "l" ? "左侧" : form == "b" ? "横路" : "右侧") + "路名：" + name;
            note.En = "Read the name of the road " + (form == "u" ? "ahead" : form == "l" ? "to the left" : form == "b" ? "across" : "to the right") + ": " + name;
            return s.ToString();
        }

        // ------------------------------------------------------------------ the reasons

        /// <summary>The reason JunctionRead gave for finding nothing, in the player's language.</summary>
        public static string WhyText(string why, bool zh)
        {
            why = why ?? "";
            if (why.StartsWith("no junction within ", StringComparison.Ordinal) && why.EndsWith(" m", StringComparison.Ordinal))
            {
                string metres = why.Substring("no junction within ".Length, why.Length - "no junction within ".Length - 2);
                return zh ? metres + " m 内没有路口" : "no junction within " + metres + " m";
            }

            switch (why)
            {
                case "the sign is not on a road": return zh ? "牌子不在道路上" : "the sign is not on a road";
                case "the road ends before a junction": return zh ? "前方道路在路口之前就到头了" : "the road ends before a junction";
                case "no junction within reach": return zh ? "前方一段路内没有路口" : "no junction within reach";
                case "no road or no direction": return zh ? "没有道路或方向" : "no road or no direction";
                case "reading failed": return zh ? "读取时出错（详见日志）" : "the reading failed (see the log)";
                default: return zh ? "不明原因" : (why.Length > 0 ? why : "unknown");
            }
        }
    }
}
