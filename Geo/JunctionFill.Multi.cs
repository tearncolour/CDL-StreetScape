using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>The junctions-ahead board (gb.guide8.01c) filled in from the junctions ahead along the road (JunctionSequence).</summary>
    public static partial class JunctionFill
    {
        private static string Names(JrStop stop, bool left, bool ramps)
        {
            var names = new List<string>();
            foreach (JrArm arm in stop.Arms)
            {
                if (JunctionSequence.SideOf(arm) != (left ? JrDirection.Left : JrDirection.Right) || arm.IsRamp != ramps || arm.Name.Length == 0 || names.Contains(arm.Name))
                    continue;
                names.Add(arm.Name);
            }

            return names.Count > 0 ? names[0] : "";
        }

        /// <summary>
        /// The name of an interchange: the road the ramps belong to with 立交 after it (a Chinese naming habit; the game has no names for interchanges), none when the ramps have no name. One that
        /// is longer than the board holds is refused by PutName like any name (empty, and the note says so: a long name made up by the mod is worse than none).
        /// </summary>
        private static string InterchangeName(JrStop stop)
        {
            string name = Names(stop, true, true);
            if (name.Length == 0)
                name = Names(stop, false, true);
            if (name.Length == 0)
                return "";
            bool han = false;
            foreach (char c in name)
                han |= RoadGuideSigns.Wide(c);
            return han && !name.EndsWith("立交") && !name.EndsWith("互通") ? name + "立交" : name;
        }

        /// <summary>
        /// Sets a name of the board (its own limit, RoadGuideSigns.MultiNameMax, not the other boards' seven): a name read from the game that is longer is not cut to a word that is no name (a cut
        /// 克莱斯特伍德公 of 克莱斯特伍德公路 was on the owner's board), it is left empty and the note says so, for the player to type.
        /// </summary>
        private static GuideSpec PutName(GuideSpec s, string key, string value, JrNote note)
        {
            if (value != null && value.Length > RoadGuideSigns.MultiNameMax)
            {
                note.Note($"「{value}」超过 {RoadGuideSigns.MultiNameMax} 个字，没有填写，请自己填。", $"\"{value}\" is longer than {RoadGuideSigns.MultiNameMax} characters and was not filled in: type it.");
                return s.With(key, "");
            }

            return s.With(key, value ?? "");
        }

        private static string FillMulti(SignDef def, GuideSpec s, JrResult r, JrNote note)
        {
            JrSequence sequence = r.Sequence ?? JunctionSequence.OfFirst(r);
            var stops = new List<JrStop>();
            int dropped = 0;
            if (sequence != null)
            {
                foreach (JrStop stop in sequence.Stops)
                {
                    // A junction with no road to the left or the right (a fork both of whose roads go on) has nothing to draw.
                    if (stops.Count < RoadGuideSigns.MultiMax && (stop.Has(true) || stop.Has(false)))
                        stops.Add(stop);
                    else if (!stop.Has(true) && !stop.Has(false))
                        dropped++;
                }
            }

            if (stops.Count == 0)
            {
                note.Zh = "前方路线上没有可以画的路口（没有左右岔路），没有填写。";
                note.En = "There is no junction with a road to the left or the right along the road ahead, nothing was filled in.";
                return null;
            }

            s = PutName(s, "road", r.OwnName, note);
            s = s.With("n", stops.Count.ToString(CultureInfo.InvariantCulture));
            var zh = new List<string>();
            var en = new List<string>();
            for (int i = 1; i <= RoadGuideSigns.MultiMax; i++)
            {
                JrStop stop = i <= stops.Count ? stops[i - 1] : null;
                bool left = stop != null && stop.Has(true), right = stop != null && stop.Has(false);
                bool interchange = stop != null && stop.Kind == JrKind.Interchange;
                // What the reading does not know (the English lines, the name of an interchange the ramps did not give) is emptied: the board's defaults are another city's names.
                s = s.With(RoadGuideSigns.MultiKey("k", i), stop == null ? "p" : interchange ? "d" : "p");
                s = s.With(RoadGuideSigns.MultiKey("b", i), left && right ? "b" : left ? "l" : "r");
                s = PutName(s, RoadGuideSigns.MultiKey("lz", i), stop == null || !left ? "" : Names(stop, true, false), note);
                s = PutName(s, RoadGuideSigns.MultiKey("rz", i), stop == null || !right ? "" : Names(stop, false, false), note);
                s = PutName(s, RoadGuideSigns.MultiKey("xz", i), interchange ? InterchangeName(stop) : "", note);
                foreach (string key in new[] { "le", "re", "xe" })
                    s = s.With(RoadGuideSigns.MultiKey(key, i), "");
                if (stop == null)
                    continue;
                string l = Names(stop, true, false), rt = Names(stop, false, false);
                string where = $"{stop.Total:0} m";
                zh.Add($"{where} {(interchange ? "互通" : "平面")}（左 {(left ? (l.Length > 0 ? l : "无名") : "无")}，右 {(right ? (rt.Length > 0 ? rt : "无名") : "无")}）");
                en.Add($"{where} {(interchange ? "interchange" : "at grade")} (left {(left ? (l.Length > 0 ? l : "unnamed") : "none")}, right {(right ? (rt.Length > 0 ? rt : "unnamed") : "none")})");
            }

            JrStop end = stops[stops.Count - 1];
            string ahead = "";
            if (end.Through != null)
                ahead = end.Through.Name;
            s = PutName(s, "dz", ahead, note);
            s = s.With("de", "").With("cz", "").With("ce", "");
            if (s.Get("dir").Length > 0)
                s = s.With("dir", Compass(r));

            note.Zh = $"已读取前方 {stops.Count} 个路口：" + string.Join("；", zh) + "。";
            note.En = $"Read {stops.Count} junction(s) ahead: " + string.Join("; ", en) + ".";
            note.Note("互通式立交按「单向的高速公路类匝道」判断，立交名为匝道所在道路名加「立交」，英文行与路牌游戏里没有，没有填。", "An interchange is judged by a one-way ramp of the highway kind, its name is the ramp's road with 立交 after it; the English lines and the plate are not known to the game and were not filled in.");
            int joined = 0;
            foreach (JrStop stop in stops)
                joined += stop.Nodes - 1;
            if (joined > 0)
                note.Note($"相距不到 {JunctionSequence.MergeGap:0} m 的路口按一个画（{joined} 处，例如双向分离的横路）。", $"Junctions less than {JunctionSequence.MergeGap:0} m apart are drawn as one ({joined}, for a crossing road with two carriageways).");
            if (dropped > 0)
                note.Note($"{dropped} 个没有左右岔路的路口没有画。", $"{dropped} junction(s) with no road to the left or the right were left out.");
            if (stops.Count < RoadGuideSigns.MultiMax && sequence != null && sequence.Why.Length > 0)
                note.Note("只读到 " + stops.Count + " 个路口：" + sequence.Why + "。", "Only " + stops.Count + " junction(s) were read: " + sequence.Why + ".");
            return s.ToString();
        }
    }
}
