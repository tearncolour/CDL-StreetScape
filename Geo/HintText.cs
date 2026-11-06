using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo
{
    /// <summary>What the tool tells the player now, as a kind and numbers; HintText puts it into words in the game's language.</summary>
    public struct HintInfo : IEquatable<HintInfo>
    {
        /// <summary>
        /// "": nothing; "choose", "road", "place", "delete", "edit", "attach" (the sign is over a support: it joins it), "move" (a board of the edited support can be moved with the arrow keys),
        /// "refuse-full", "refuse-large", "refuse-height", "refuse-space"; for an attached support (0.6.0) "host" (the board goes on the post that is marked), "host-none" (there is no lamp post or signal pole
        /// within Left metres of the cursor), "host-large" (the board is too large for a post of the game's), "host-road" (there is no road beside the post to face).
        /// On a wall (0.12.2): "wall" (the board goes on the wall under the cursor: Left the height of its lower edge, Need the standard height it snaps to, Hung a bracket, Name the support), "wall-none"
        /// (the cursor is not on a building's wall), "wall-large" (a bracket carries small boards only). On the post of a hung support (0.12.2): "attach-post" (Count the boards on the post with the new one,
        /// Left the height still free under the lowest arm or chord), "refuse-post-height" (no room under it: Left what there is), "refuse-bracket-height" (a bracket's stack is full: Left what is left).
        /// For a road marking (0.10.0): "mark-stamp" (a marking of one piece is put where the cursor is, in the lane and facing the traffic), "mark-run-start" (a marking that runs along a road: the first click sets where it
        /// starts), "mark-run-end" (the start is set; Left is the length so far in metres, Count the items it will be, Need the longest in metres), "mark-run-road" (a marking that runs along a road needs a road under the cursor).
        /// For the area markings (0.11.0): "mark-poly" (an island drawn corner by corner: Count the corners so far, Manual the cursor would close it, Hung the corner snapped to a line), "mark-gore-fork" (the gore of a fork:
        /// Manual an exit, Hung the ramp on the right, Left the gore's length, Need its width at the far end, X the lead line's length, all in metres), "mark-gore-free" (no fork near: the standard's figure).
        /// </summary>
        public string Kind;

        /// <summary>
        /// The boards on the support (before adding, or with the new one for "attach"), the room that is left (m), the room that is needed (m), the place of the new board along a beam (m); on a post
        /// ("place", "host", "attach" off a beam, "attach-post", "move"; 0.12.24) X is the lower edge of the board the arrow keys move up and down, 0 where they do not.
        /// </summary>
        public int Count;
        public float Left, Need, X;

        /// <summary>The most boards this kind of support carries, how long the beam or arm is (m), and whether it was made longer for the board that is being put on (on a post: the post grows with the board).</summary>
        public int Max;
        public float Span;
        public bool Grew;

        /// <summary>The order of a stack is the player's; the support is a beam or an arm; the state is one the sign cannot be put in.</summary>
        public bool Manual, Hung, Bad;

        /// <summary>The name of the sign or the support in Chinese, and in English for the line that names it ("pick"); the other lines do not name it.</summary>
        public string Name, NameEn;

        /// <summary>For "host": what the board is fixed to, "lamp" (a street-lamp post) or "signal" (a signal's pole); anything else says "a lamp post or a signal pole".</summary>
        public string Host;

        /// <summary>For "move": the board being edited turns on its post (0.12.8): the keys that turn it are listed.</summary>
        public bool Turns;

        /// <summary>For "move": the support being edited is a post, whose distance from the kerb the < and > keys set (0.12.74).</summary>
        public bool Kerb;

        /// <summary>The board is a standing light box, whose tilt the - and = keys set (0.12.76).</summary>
        public bool Tilt;

        /// <summary>For "place": the support is a post, which the / key puts in the middle of the road (MidKey), and the mode is on (MidOn: on a support that is no post it is refused in the line of help).</summary>
        public bool MidKey, MidOn;

        /// <summary>For "place" with the mode on (0.12.84): the post stands in the road at the cursor now; false while the cursor is not over the carriageway (the post would stand at the road's side).</summary>
        public bool MidHere;

        /// <summary>For "attach" on a beam: the support is a gantry, whose two faces take boards (0.12.22), and the board goes on its back (the camera is behind it).</summary>
        public bool TwoSided, Back;

        /// <summary>
        /// Field by field (the default struct equality reflects over the fields, which is slow and allocates): the panel's line of help is made again only when it changed (0.7.1), and that is asked
        /// at every frame the tool is open.
        /// </summary>
        public bool Equals(HintInfo o) =>
            Count == o.Count && Left == o.Left && Need == o.Need && X == o.X && Max == o.Max && Span == o.Span && Grew == o.Grew && Manual == o.Manual && Hung == o.Hung && Bad == o.Bad && MidKey == o.MidKey && MidOn == o.MidOn && MidHere == o.MidHere && Turns == o.Turns && Tilt == o.Tilt && Kerb == o.Kerb && TwoSided == o.TwoSided && Back == o.Back &&
            string.Equals(Kind, o.Kind, StringComparison.Ordinal) && string.Equals(Name, o.Name, StringComparison.Ordinal) && string.Equals(NameEn, o.NameEn, StringComparison.Ordinal) && string.Equals(Host, o.Host, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is HintInfo other && Equals(other);

        public override int GetHashCode() => (Kind ?? "").GetHashCode() ^ (Count * 397) ^ (Name ?? "").GetHashCode();
    }

    /// <summary>
    /// The line of help of the panel and the keys of the tool's own little panel (0.12.47; the owner: "精简文字说明……同时独立小窗口提示按键"): the line says what a click does and the numbers, the keys
    /// are listed apart (Keys). Pure, so that every kind has text and keys in both languages and the numbers read right.
    /// </summary>
    public static class HintText
    {
        private static string N(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        private static string N2(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>What the automatic marking drew at one approach of the chosen stretch (0.12.45), with the state of the road it went by: a line of the panel and the log.</summary>
        public static string AutoEnd(AutoEnd e, bool zh)
        {
            int k = Math.Max(0, Math.Min(AutoMarking.MaxApproaches - 1, e.Rank));
            string colour = zh ? MarkingTemplates.ApproachColoursZh[k] : MarkingTemplates.ApproachColoursEn[k];
            string metres = e.Guide.ToString("0", CultureInfo.InvariantCulture);
            string guide = e.Guide >= 1f ? (zh ? $"导向车道线 {metres} m" : $"guide lane lines {metres} m") : (zh ? "无导向车道线" : "no guide lane lines");
            string by = e.GuideByRoad ? (zh ? "（按道路状态）" : " (by the road)") : "";
            string arrows = e.Groups > 0 ? (zh ? $"箭头 {e.Groups} 组" : $"{e.Groups} group(s) of arrows") : (zh ? "无箭头" : "no arrows");
            var state = new List<string>();
            if (!float.IsNaN(e.Kmh))
                state.Add(zh ? $"限速 {e.Kmh:0} km/h" : $"{e.Kmh:0} km/h");
            if (!float.IsNaN(e.Queue))
                state.Add(e.Queue >= 1f ? (zh ? $"排队 {e.Queue:0} m" : $"a queue of {e.Queue:0} m") : (zh ? "无排队" : "no queue"));
            if (!float.IsNaN(e.Flow))
                state.Add(zh ? $"通行速度 {e.Flow * 100f:0}%" : $"traffic at {e.Flow * 100f:0} % of free speed");
            return zh
                ? $"进口{k + 1}（{colour}圈）：{guide}{by}，{arrows}" + (state.Count > 0 ? "；" + string.Join("，", state) : "")
                : $"Approach {k + 1} ({colour}): {guide}{by}, {arrows}" + (state.Count > 0 ? "; " + string.Join(", ", state) : "");
        }

        /// <summary>The lower edge of a board the arrow keys move up and down (m; 0.12.24).</summary>
        private static string Edge(float lowerEdge, bool zh) => zh ? $"下沿 {N2(lowerEdge)} m。" : $"Lower edge {N2(lowerEdge)} m.";

        /// <summary>How far a line was moved nearer the road or away from it with the arrow keys (0.12.22; m, outward); nothing when it was not. For the automatic barriers, the roadside ones only.</summary>
        private static string Moved(float moved, bool zh)
        {
            if (Math.Abs(moved) < 0.005f)
                return "";
            return zh ? $"（{(moved > 0f ? "远离" : "靠近")} {N2(Math.Abs(moved))} m）" : $" ({N2(Math.Abs(moved))} m {(moved > 0f ? "farther" : "nearer")})";
        }

        private static string Refused(HintInfo h, bool zh) => h.Bad ? (zh ? "这里不能放。" : "Not allowed here. ") : "";

        /// <summary>The / key is on: a post stands in the road; on a support that is no post the mode is refused with what it is for.</summary>
        private static string MidLine(HintInfo h, bool zh) => !h.MidOn ? "" : !h.MidKey ? (zh ? "路中仅限立柱：按 ; ' 换立架。" : "In the road, posts only: ; ' changes it. ")
            : h.MidHere ? (zh ? "路中：" : "In the road: ") : (zh ? "路中：光标移到车行道上。" : "In the road: put the cursor on the lanes. ");

        public static string Of(HintInfo h, bool zh)
        {
            switch (h.Kind ?? "")
            {
                case "choose":
                    return zh ? "先在列表里选一个。" : "Choose an entry first.";
                case "road":
                    return zh ? "指向道路或地面。" : "Point at a road or the ground.";
                case "place":
                    return Refused(h, zh) + MidLine(h, zh) + (zh ? "单击放置。" : "Click to place.") + (h.X > 0f ? " " + Edge(h.X, zh) : "");
                case "delete":
                    return zh ? "指牌删牌，指立柱或横梁删整架。" : "Board: delete it. Post: the whole support.";
                case "cut-start":
                    return zh ? "Shift + 单击起点和终点，删这段。" : "Shift: click start and end to cut a stretch.";
                case "cut-end":
                    return (h.Bad ? (zh ? "离线太远：" : "Too far from the line: ") : "")
                        + (zh ? $"{N(h.Left)} m（{h.Count} 块）：单击终点删除。" : $"{N(h.Left)} m ({h.Count} pieces): click the end to delete.");
                case "edit":
                    return zh ? "单击牌子或立架来修改。" : "Click a sign or support to edit it.";
                case "move":
                    return MoveLine(h, zh);
                case "attach":
                    return AttachLine(h, zh);
                case "attach-post":
                    return (zh ? $"杆上 {h.Count} 块，余 {N(h.Left)} m。" : $"Post: {h.Count} boards, {N(h.Left)} m left.")
                        + (h.X > 0f ? " " + Edge(h.X, zh) : "");
                case "attach-line":
                    if (h.Hung)
                    {
                        string beam = h.TwoSided ? (h.Back ? (zh ? "门架背面" : "gantry back") : (zh ? "门架正面" : "gantry front")) : (zh ? "横梁" : "beam");
                        // A beam that grew says how long it is now; one that did not says how much room is left.
                        string tail = h.Grew ? (zh ? $"已加长到 {N(h.Span)} m" : $"lengthened to {N(h.Span)} m") : (zh ? $"余 {N(h.Left)} m" : $"{N(h.Left)} m left");
                        return zh ? $"{beam}：{N(h.X)} m 处，{tail}。" : $"On the {beam}: at {N(h.X)} m, {tail}.";
                    }

                    if (h.X > 0f)
                        return zh ? $"杆上 {h.Count}/4 块，余 {N(h.Left)} m。{Edge(h.X, zh)}" : $"On the post: {h.Count} of 4, {N(h.Left)} m left. {Edge(h.X, zh)}";
                    return zh
                        ? $"杆上 {h.Count}/4 块，余 {N(h.Left)} m，{(h.Manual ? "手动" : "标准")}顺序。"
                        : $"On the post: {h.Count} of 4, {N(h.Left)} m left, {(h.Manual ? "your" : "standard")} order.";
                case "host":
                    return Refused(h, zh) + (zh ? $"附着于{HostWord(h.Host, zh)}。" : $"Attach to the {HostWord(h.Host, zh)}.") + (h.X > 0f ? " " + Edge(h.X, zh) : "");
                case "host-none":
                    return zh ? $"鼠标 {N(h.Left)} m 内没有路灯杆或信号灯杆。" : $"No lamp post or signal pole within {N(h.Left)} m.";
                case "host-large":
                    // Need is the board's area (m2) when the tool knows it: a board inside the width and the height can still be over the area one post carries.
                    if (h.Need > 0f)
                        return zh ? $"太大：杆上最多 {N(SupportPlanner.SinglePostMaxArea)} m²，此牌 {N2(h.Need)} m²。" : $"Too large for a pole: {N(SupportPlanner.SinglePostMaxArea)} m² at most; this one is {N2(h.Need)} m².";
                    return zh ? $"太大：杆上最多 {N(SupportPlanner.SinglePostMaxArea)} m²（宽 {N(SupportPlanner.SinglePostMaxWidth)}、高 {N(SupportPlanner.SinglePostMaxHeight)} m）。"
                        : $"Too large for a pole: {N(SupportPlanner.SinglePostMaxArea)} m² at most ({N(SupportPlanner.SinglePostMaxWidth)} m wide, {N(SupportPlanner.SinglePostMaxHeight)} m high).";
                case "host-road":
                    return zh ? "把鼠标放在这根杆旁的道路上。" : "Put the cursor on the road beside the pole.";
                case "wall":
                    return Refused(h, zh) + (zh ? $"墙面：下缘距地 {N(h.Left)} m。" : $"Wall: lower edge {N(h.Left)} m.");
                case "roof":
                    return Refused(h, zh) + (zh ? $"屋顶：下缘距屋面 {N(h.Left)} m。" : $"Roof: lower edge {N(h.Left)} m.");
                case "roof-off":
                    return zh ? "屋顶支撑已关闭（textSupports）。" : "Roof supports are off (textSupports).";
                case "wall-roof":
                    return zh ? "这是屋顶：只有文字灯箱能立上去。" : "A roof: only a text light box stands on it.";
                case "roof-none":
                    return zh ? "指向建筑的屋顶。" : "Point at a roof.";
                case "roof-text":
                    return zh ? "屋顶上只能立文字灯箱。" : "Only a text light box stands on a roof.";
                case "roof-edge":
                    return zh ? $"超出屋顶边缘 {N(h.Need)} m：往里挪或缩小。" : $"{N(h.Need)} m over the edge: move it in or shrink it.";
                case "wall-none":
                    return zh ? "指向建筑的外墙。" : "Point at a building's wall.";
                case "wall-large":
                    return zh ? $"太大：托架最大 {N(SupportPlanner.SinglePostMaxWidth)} × {N(SupportPlanner.SinglePostMaxHeight)} m。" : $"Too large for a bracket: {N(SupportPlanner.SinglePostMaxWidth)} x {N(SupportPlanner.SinglePostMaxHeight)} m at most.";
                case "mark-stamp":
                    return Refused(h, zh) + (zh ? "单击放置，朝向车流。仅作装饰。" : "Click to place, facing traffic. Decoration only.");
                case "mark-run-start":
                    return Refused(h, zh) + (zh ? "单击起点和终点。" : "Click start, then end.") + Moved(h.X, zh);
                case "mark-run-end":
                    return (h.Bad ? (zh ? "不能在此结束：" : "Cannot end here: ") : "")
                        + (zh ? $"{N(h.Left)} m（{h.Count} 段）：单击终点。" : $"{N(h.Left)} m ({h.Count} pieces): click the end.")
                        + Moved(h.X, zh);
                case "line-start":
                    return Refused(h, zh) + (zh ? "单击起点，再单击终点，按间距立杆。" : "Click start, then end: one post per spacing.");
                case "line-strip":
                    return zh ? "隔离带上不能线性摆放：移到路侧。" : "No line on a median strip: move to the roadside.";
                case "line-end":
                    return (h.Bad ? (zh ? "不能在此结束：" : "Cannot end here: ") : "")
                        + (h.Max > 0
                            ? (zh ? $"{N(h.Left)} m，间距 {N(h.Need)} m：只放前 {h.Max} 块。" : $"{N(h.Left)} m, every {N(h.Need)} m: click the end (first {h.Max} only).")
                            : (zh ? $"{N(h.Left)} m，间距 {N(h.Need)} m，{h.Count} 块：单击终点。" : $"{N(h.Left)} m, every {N(h.Need)} m, {h.Count} signs: click the end."));
                case "pick-mode":
                    return zh ? "单击物件吸取样式；Shift 取整杆。" : "Click an item to pick it; Shift: whole pole.";
                case "pick":
                    // With Shift held and a pole of several boards under the cursor (Count): all of them are taken.
                    return h.Bad ? (zh ? "按住 Ctrl：单击已放的物件吸取样式。" : "Hold Ctrl and click an item to pick its style.")
                        : h.Count > 1 ? (zh ? $"单击吸取整杆 {h.Count} 块牌。" : $"Click to pick all {h.Count} boards of the pole.")
                        : (zh ? $"单击吸取「{h.Name}」。" : $"Click to pick {h.NameEn}.");
                case "arm":
                    return Refused(h, zh) + (zh ? $"单击放在横杆上（距杆根 {N(h.Left)} m）。" : $"Click to place on the arm ({N(h.Left)} m from root).");
                case "over":
                    return Refused(h, zh) + (h.Hung ? (zh ? $"单击吊在桥下（下沿高 {N(h.Left)} m）。" : $"Click to hang it under the bridge ({N(h.Left)} m up).")
                        : zh ? $"单击贴在桥梁侧面（下沿高 {N(h.Left)} m）。" : $"Click to fix it on the bridge ({N(h.Left)} m up).");
                case "over-none":
                    return zh ? "指向桥梁、立交下方的道路。" : "Point at a road under a bridge.";
                case "over-low":
                    return h.Hung ? (zh ? $"桥底距路面 {N(h.Left)} m，需要 {N(h.Need)} m。" : $"The bridge is {N(h.Left)} m up; {N(h.Need)} m needed.")
                        : zh ? $"桥侧只有 {N(h.Left)} m 高，需要 {N(h.Need)} m。" : $"The bridge face is {N(h.Left)} m high; {N(h.Need)} m needed.";
                case "over-thin":
                    return zh ? $"桥底只有 {N(h.Left)} m 深，吊挂需要 {N(h.Need)} m。" : $"The bridge is {N(h.Left)} m deep; {N(h.Need)} m needed.";
                case "support-over":
                    return zh ? "桥梁上的牌不能换立架：删掉重放。" : "A bridge board keeps its support: delete and re-place.";
                case "arm-none":
                    return zh ? $"指向信号灯横杆下方（{N(h.Left)} m 内）。" : $"Point under a signal arm (within {N(h.Left)} m).";
                case "arm-space":
                    return zh ? $"横杆上放不下 {N(h.Left)} m 宽的牌子。" : $"No room for a board {N(h.Left)} m wide on this arm.";
                case "arm-large":
                    return zh ? $"牌子太大，横杆挂不下（最大 {N(h.Left)} × {N(h.Need)} m）。" : $"Too large for a signal arm ({N(h.Left)} x {N(h.Need)} m at most).";
                case "host-space":
                    return zh ? "这根杆上放不下这块牌。" : "No room for the board on this pole.";
                case "support-arm":
                    return zh ? "信号横杆上的牌不能换立架：删掉重放。" : "A signal-arm board keeps its support: delete and re-place.";
                case "mark-run-road":
                    return zh ? "这种标线沿道路画：指向道路。" : "This marking runs along a road: point at a road.";
                case "mark-bend":
                    return Refused(h, zh) + (zh ? "单击放置，随路弯。" : "Click to place along the road.") + Moved(h.X, zh);
                case "mark-bend-start":
                    return (zh ? "单击起点和终点。" : "Click start, then end.") + Moved(h.X, zh);
                case "mark-bend-end":
                    return (h.Bad ? (zh ? "不能在此结束：" : "Cannot end here: ") : "") + (zh ? $"{N(h.Left)} m：单击终点。" : $"{N(h.Left)} m: click the end.") + Moved(h.X, zh);
                case "mark-waiting":
                    if (h.Name == "road")
                        return zh ? "指向进口道的左转车道。" : "Point at a left-turn lane of an approach.";
                    if (h.Name == "room")
                        return zh ? "空间不足 3 m，放不下待转区。" : "Under 3 m of room: no waiting area fits.";
                    if (h.Name == "none" || h.Count == 0)
                        return zh ? "这条进口道没有左转车道：换一条，或用“线7”手画。" : "No left-turn lane here: try another, or draw \"线7\" by hand.";
                    return zh
                        ? $"待转区：{h.Count} 条，长 {N(h.Left)} m，左线{(h.Manual ? "黄" : "白")}{(h.Hung ? "，无对向车流" : "")}。"
                        : $"Place: {h.Count} lane(s), {N(h.Left)} m, {(h.Manual ? "yellow" : "white")} left line{(h.Hung ? ", no oncoming traffic" : "")}.";
                case "mark-auto-circuit":
                    return zh ? "Racing 赛道的路，标线由 Racing 画。" : "A Racing circuit road: Racing draws its lines.";
                case "bar-auto":
                    {
                        // Left the stretch's length, Need the lines, Name the kind of road (RoadClass) or "road".
                        if (h.Name == "road")
                            return zh ? "指向道路：单击按道路类型布置护栏。" : "Point at a road: a click places its barriers.";
                        string kind = h.Name == "Highway" ? (zh ? "高速公路" : "a motorway") : h.Name == "National" ? (zh ? "国道" : "a national road") : h.Name == "City" ? (zh ? "城市道路" : "a city road") : (zh ? "村道" : "a village road");
                        if (h.Bad)
                            return zh ? $"按{kind}放不出护栏：改道路类型或路侧、分隔带。" : $"No barriers as {kind}: change the kind of road or the sides.";
                        return (zh ? $"按{kind}：{N(h.Left)} m，{(int)h.Need} 条。" : $"As {kind}: {N(h.Left)} m, {(int)h.Need} lines.") + Moved(h.X, zh);
                    }
                case "mark-auto":
                    {
                        // Count the items, Left the stretch's length, Name the range.
                        if (h.Name == "road")
                            return zh ? "指向道路：单击按国标重标这一段。" : "Point at a road: a click marks it by the standard.";
                        string where = h.Name == "e" ? (zh ? "这个路段" : "this segment") : (zh ? "这一段" : "this stretch");
                        if (h.Bad)
                            return zh ? $"{where}画不了：读不到车道。" : $"Nothing can be drawn on {where}: no lanes were read.";
                        return zh ? $"单击重标{where}：{N(h.Left)} m，{h.Count} 条，清除原版线。" : $"Mark {where}: {N(h.Left)} m, {h.Count} items; clears game lines.";
                    }
                case "mark-auto-marked":
                    {
                        // Name the range, Manual it was drawn by older rules (0.12.48).
                        string where = h.Name == "e" ? (zh ? "这个路段" : "This segment") : (zh ? "这一段" : "This stretch");
                        if (h.Manual)
                            return zh ? $"{where}按旧规则标的：单击重画。" : $"{where} follows older rules: click to redraw.";
                        return zh ? $"{where}已重标：单击选中。" : $"{where} is marked: click to choose it.";
                    }
                case "mark-auto-chosen":
                    {
                        // Left the length, Manual the panel's settings are still to be drawn, Bad they cannot be.
                        string state = h.Bad ? (zh ? "按现在的设置画不了。" : "Cannot be drawn with these settings.")
                            : h.Manual ? (zh ? "设置已改，稍后自动重画。" : "Settings changed: redrawn in a moment.")
                            : (zh ? $"改设置会自动重画（{h.Count} 条）。" : $"A change of settings redraws it ({h.Count} items).");
                        return zh ? $"已选中（{N(h.Left)} m）。{state}" : $"Chosen ({N(h.Left)} m). {state}";
                    }
                case "mark-clear":
                    {
                        string where = h.Name == "n" ? (zh ? "这个路口" : "this junction") : h.Name == "r" ? (zh ? $"整条路，{h.Count} 处，{N(h.Left)} m" : $"whole road, {h.Count} areas, {N(h.Left)} m") : (zh ? $"这个路段，{N(h.Left)} m" : $"this segment, {N(h.Left)} m");
                        if (h.Count == 0)
                            return zh ? "指向道路或路口：单击清除原版线。" : "Point at a road or a junction to clear its markings.";
                        if (h.Bad)
                            return zh ? $"{where}已清除过。" : $"Already cleared: {where}.";
                        return zh ? $"单击清除：{where}{(h.Hung ? "（留停止线）" : "")}。" : $"Click to clear: {where}{(h.Hung ? " (stop lines stay)" : "")}.";
                    }
                case "mark-poly":
                    if (h.Name == "clear")
                        return (h.Bad ? (zh ? "这里不能加点。" : "No corner here. ") : "")
                            + (zh ? $"逐点单击画清除范围：{h.Count}/{MarkingPolygon.MaxPoints} 点，点首点闭合。" : $"Corners of the clear area: {h.Count} of {MarkingPolygon.MaxPoints}; click the first to close.");
                    if (h.Count == 0)
                        return (h.Bad ? (zh ? "这里不能加点。" : "No corner here. ") : "")
                            + (zh ? $"逐点单击画{(h.Name == "grid" ? "网状线" : "导流区")}轮廓；点首点闭合。" : $"Click the corners of the {(h.Name == "grid" ? "box grid" : "island")}; click the first to close.");
                    return (h.Bad ? (zh ? "这里不能加点。" : "No corner here. ") : "")
                        + (zh ? $"已画 {h.Count}/{MarkingPolygon.MaxPoints} 点{(h.Hung ? "，已吸附" : "")}。{(h.Manual ? "单击闭合。" : "单击加点；点首点闭合。")}"
                        : $"{h.Count} of {MarkingPolygon.MaxPoints} corners{(h.Hung ? ", snapped" : "")}. {(h.Manual ? "Click to close." : "Click to add; the first closes.")}");
                case "mark-gore-fork":
                    return Refused(h, zh)
                        + (zh ? $"{(h.Manual ? "出口" : "入口")}分叉：导流区 {N(h.Left)} × {N(h.Need)} m，匝道在{(h.Hung ? "右" : "左")}。"
                        : $"{(h.Manual ? "Exit" : "Entry")} fork: gore {N(h.Left)} x {N(h.Need)} m, ramp on the {(h.Hung ? "right" : "left")}.");
                case "mark-gore-free":
                    return Refused(h, zh) + (zh ? "无分叉：放标准图形。" : "No fork near: the standard figure.");
                case "refuse-full":
                    return zh
                        ? $"已有 {h.Count} 块，到上限（杆和臂 {Assembly.MaxBoards} 块，门架 {Assembly.MaxBoardsGantry} 块）。"
                        : $"{h.Count} boards: the limit ({Assembly.MaxBoards} on a post or arm, {Assembly.MaxBoardsGantry} on a gantry).";
                case "refuse-large":
                    return zh
                        ? "杆上只放 ≤ 1.4 m 的小牌：改用悬臂或门架。"
                        : "A post takes boards up to 1.4 m: use a cantilever or gantry.";
                case "refuse-height":
                    return zh ? $"杆上最多叠 3.3 m：只剩 {N(h.Left)} m。" : $"A post stack is 3.3 m at most: {N(h.Left)} m left.";
                case "support-to-wall":
                    return zh ? "不能直接改墙面安装：删除后重选墙面立架。" : "Cannot be moved onto a wall: delete and place again.";
                case "support-from-wall":
                    return zh ? "墙上的牌只能换墙面支架：落地请删除重放。" : "A wall sign keeps a wall support: delete it to change.";
                case "refuse-post-height":
                    return zh
                        ? $"需距地 ≥ {N(SupportCatalog.RoadsideClearance)} m，余 {N(h.Left)} m；可挂横梁。"
                        : $"Needs {N(SupportCatalog.RoadsideClearance)} m up: {N(h.Left)} m left. Hang it on the beam.";
                case "refuse-bracket-height":
                    return zh ? $"托架下最多 {N(Attach.MaxBracketStack)} m：只剩 {N(h.Left)} m。" : $"A bracket stack is {N(Attach.MaxBracketStack)} m at most: {N(h.Left)} m left.";
                case "refuse-space":
                    return zh
                        ? $"横梁放不下宽 {N(h.Need)} m 的牌（还剩 {N(h.Left)} m）。"
                        : $"No room on the beam for a board {N(h.Need)} m wide ({N(h.Left)} m left).";
                default:
                    return "";
            }
        }

        // ------------------------------------------------------------------ the keys

        /// <summary>The kinds of the line of help while a sign is being placed: the keys of placing a sign.</summary>
        private static readonly HashSet<string> s_Placing = new HashSet<string>
        {
            "place", "attach", "attach-post", "host", "host-none", "host-large", "host-road", "host-space", "wall", "wall-none", "wall-large", "roof", "roof-none", "roof-text", "roof-edge", "roof-off", "wall-roof", "refuse-full", "refuse-large", "refuse-height", "refuse-space",
            "refuse-post-height", "refuse-bracket-height", "support-to-wall", "support-from-wall", "support-arm", "over", "over-none", "over-low", "over-thin", "support-over"
        };

        /// <summary>
        /// The keys of the tool in the state the line of help is for (0.12.47), each a key and what it does, for the keys' own little panel; the right click and Esc always last. None for a kind
        /// that is not known.
        /// </summary>
        public static List<string[]> Keys(HintInfo h, bool zh)
        {
            var keys = new List<string[]>();
            string kind = h.Kind ?? "";
            void Add(string key, string zhDo, string enDo) => keys.Add(new[] { key, zh ? zhDo : enDo });
            string click = zh ? "左键" : "Left click", leave = zh ? "退出" : "leave";
            string upDown = zh ? "升降（Shift）" : "up / down (Shift)";
            string rise = upDown;
            string nudge = zh ? "近/远（Shift）" : "in / out (Shift)";
            if (s_Placing.Contains(kind))
            {
                Add(click, "放置 / 并入", "place / join");
                if (kind == "wall")
                    Add("← → ↑ ↓", "沿墙（Shift）", "along wall (Shift)");
                else if (kind == "roof" || kind == "roof-edge")
                    Add("← → ↑ ↓", "平移（Shift）", "across (Shift)");
                if (kind == "attach" && h.Hung)
                {
                    Add("← →", "沿梁（Shift）", "along beam (Shift)");
                    Add("↑ ↓", rise, rise);
                }
                else if (h.X > 0f)
                    Add("↑ ↓", upDown, upDown);
                else if (kind == "attach")
                    Add("↑ ↓", "调顺序", "change the order");
                if (kind == "attach" && h.Hung && h.TwoSided && !h.Back)
                    Add(zh ? "绕到另一侧" : "View from behind", "挂背面", "hang on the back");
                if (kind == "attach-post")
                    Add(zh ? "指向横梁" : "Aim at the beam", "挂梁上", "hang there");
                Add(zh ? "按住 Alt" : "Hold Alt", "另立新架", "a support of its own");
                Add("[  ]", "旋转", "turn");
                Add("Shift/Alt  [  ]", "步进 5°/1°", "step 5°/1°");
                Add("\\", kind == "wall" && h.Hung ? "换朝向" : "掉头", kind == "wall" && h.Hung ? "turn the other way" : "turn round");
                Add(";  '", "换立架", "change the support");
                Add("<  >", "离路缘", "kerb distance");
                if (kind == "place" && h.MidKey)
                    Add("/", h.MidOn ? "路中（再按回路侧）" : "路中", h.MidOn ? "in the road (again: roadside)" : "in the road");
                if (h.Tilt)
                    Add("-  =", "倾角", "tilt");
                    Add("Shift  -  =", "步进 5°", "step 5°");
            }
            else
            {
                switch (kind)
                {
                    case "edit":
                        Add(click, "选牌 / 立架", "pick board / support");
                        break;
                    case "move":
                        Add(click, "选牌 / 立架", "pick board / support");
                        if (h.Kerb)
                            Add("<  >", "离路缘", "kerb distance");
                        if (h.Tilt)
                            Add("-  =", "倾角", "tilt");
                            Add("Shift  -  =", "步进 5°", "step 5°");
                        if (h.X > 0f)
                            Add("↑ ↓", upDown, upDown);
                        else if (h.Hung && !h.Turns)
                        {
                            Add("← →", "沿梁（Shift）", "along beam (Shift)");
                            Add("↑ ↓", rise, rise);
                        }
                        else if (!h.Hung && h.Count > 1)
                            Add("↑ ↓", "调顺序", "change the order");
                        if (h.Turns)
                        {
                            Add("[  ]", "旋转", "turn");
                            Add("Shift/Alt  [  ]", "步进 5°/1°", "step 5°/1°");
                            Add("\\", "掉头", "turn round");
                        }

                        break;
                    case "delete":
                        Add(click, "删除", "delete");
                        Add(zh ? "Shift + 单击" : "Shift + click", "只删一段", "delete a stretch");
                        break;
                    case "cut-start":
                        Add(click, "起点", "start");
                        break;
                    case "cut-end":
                        Add(click, "终点并删除", "end, delete");
                        Add(zh ? "右键 / Esc" : "Right click / Esc", "取消", "cancel");
                        return keys;
                    case "mark-stamp":
                        Add(click, "放置", "place");
                        Add("[  ]", "旋转", "turn");
                        Add("Shift/Alt  [  ]", "步进 5°/1°", "step 5°/1°");
                        Add("\\", "掉头", "turn round");
                        break;
                    case "mark-run-start":
                    case "mark-run-road":
                    case "mark-bend-start":
                        Add(click, "起点", "where it starts");
                        Add("← →", nudge, nudge);
                        break;
                    case "mark-bend":
                        Add(click, "放置", "place");
                        Add("← →", nudge, nudge);
                        Add("\\", "掉头", "turn round");
                        break;
                    case "line-start":
                        Add(click, "起点", "where it starts");
                        Add("[  ]", "旋转", "turn");
                        Add("Shift/Alt  [  ]", "步进 5°/1°", "step 5°/1°");
                        break;
                    case "line-end":
                        Add(click, "终点", "where it ends");
                        Add(zh ? "右键 / Esc" : "Right click / Esc", "取消", "cancel");
                        return keys;
                    case "mark-bend-end":
                    case "mark-run-end":
                        Add(click, "终点", "where it ends");
                        Add("← →", nudge, nudge);
                        Add(zh ? "右键 / Esc" : "Right click / Esc", "取消", "cancel");
                        return keys;
                    case "mark-poly":
                        Add(click, "加点（首点闭合）", "add (1st closes)");
                        Add("Enter", "闭合放置", "close and place");
                        Add("Backspace", "退一点", "back one corner");
                        Add(zh ? "右键 / Esc" : "Right click / Esc", "取消", "give up");
                        return keys;
                    case "mark-gore-fork":
                    case "mark-gore-free":
                    case "mark-waiting":
                        Add(click, "放置", "place");
                        break;
                    case "bar-auto":
                        Add(click, "放置", "place");
                        Add("← →", "路侧 近 / 远", "roadside in / out");
                        break;
                    case "mark-auto":
                        Add(click, "按国标重标", "mark by standard");
                        break;
                    case "mark-auto-marked":
                        Add(click, h.Manual ? "选中并重画" : "选中这一段", h.Manual ? "choose, redraw" : "choose it");
                        break;
                    case "mark-auto-chosen":
                        Add(click, "重画 / 换路", "redraw / another");
                        Add(zh ? "右键 / Esc" : "Right click / Esc", "取消选中", "let it go");
                        return keys;
                    case "mark-clear":
                        Add(click, "清除", "clear");
                        break;
                    case "pick":
                    case "pick-mode":
                        Add(click, "吸取样式", "pick its style");
                        Add(zh ? "Shift + 单击" : "Shift + click", "吸取整杆", "the whole pole");
                        break;
                    case "arm":
                        Add(click, "放置", "place");
                        Add("\\", "换侧", "other side");
                        Add(";  '", "换立架", "change the support");
                        break;
                    case "arm-none":
                    case "arm-space":
                    case "arm-large":
                    case "over-none":
                    case "over-low":
                        Add(";  '", "换立架", "change the support");
                        break;
                    case "over":
                        Add(click, "放置", "place");
                        Add("\\", "换朝向", "turn round");
                        Add(";  '", "换立架", "change the support");
                        break;
                    case "choose":
                    case "road":
                    case "line-strip":
                    case "mark-auto-circuit":
                        break;
                    default:
                        return keys;
                }
            }

            // Taking back and picking (0.12.50) whatever the tool is doing.
            Add("Ctrl+Z / Ctrl+Y", "撤销 / 重做", "undo / redo");
            if (kind != "pick" && (s_Placing.Contains(kind) || kind.StartsWith("mark-", StringComparison.Ordinal) || kind == "bar-auto"))
                Add(zh ? "按住 Ctrl 单击" : "Ctrl + click", "吸取样式", "pick its style");
            Add(zh ? "右键 / Esc" : "Right click / Esc", leave, leave);
            return keys;
        }

        // ------------------------------------------------------------------ notes

        /// <summary>The note in the editor of a placed attached sign whose post is not there any more (it was demolished or moved): the sign stays where it is, in the air.</summary>
        public static string HostGone(bool zh) =>
            zh ? $"⚠ {N(HostPick.Fixed)} m 内没有路灯杆或信号灯杆：牌子悬空。"
                : $"⚠ No lamp post or signal pole within {N(HostPick.Fixed)} m: it hangs.";

        private static string HostWord(string host, bool zh)
        {
            switch (host ?? "")
            {
                case "lamp":
                    return zh ? "路灯杆" : "lamp post";
                case "signal":
                    return zh ? "信号灯杆" : "signal pole";
                default:
                    return zh ? "杆" : "pole";
            }
        }

        

        /// <summary>Which face of a gantry the board goes on (0.12.22): the one the camera looks at.</summary>
        

        private static string AttachLine(HintInfo h, bool zh)
        {
            HintInfo line = h;
            line.Kind = "attach-line";
            return Of(line, zh);
        }

        /// <summary>The line of help while a board of the edited support is selected: what can be done with it (the keys are in Keys).</summary>
        private static string MoveLine(HintInfo h, bool zh)
        {
            string turns = h.Turns ? (zh ? "可旋转。" : " It turns.") : "";
            if (h.X > 0f)
                return (zh ? "编辑：" : "Editing: ") + Edge(h.X, zh) + (h.Turns ? (zh ? " " : "") + turns : "");
            if (h.Hung && !h.Turns)
                return zh ? "编辑：沿横梁移动。" : "Editing: moves along the beam.";
            if (h.Hung)
                return (zh ? "编辑：挂在立柱上。" : "Editing: on the post.") + (zh ? turns : turns);
            return (h.Count > 1
                ? (zh ? "编辑：可调上下顺序。" : "Editing: move it up or down the post.")
                : (zh ? "编辑：参数在右边窗口。" : "Editing: settings in the right window.")) + turns;
        }

        /// <summary>The kinds that exist (for the tests: every one has text and keys).</summary>
        public static readonly string[] Kinds = { "pick-mode", "line-start", "line-end", "line-strip", "cut-start", "cut-end", "choose", "road", "place", "delete", "edit", "move", "attach", "refuse-full", "refuse-large", "refuse-height", "refuse-space", "host", "host-none", "host-large", "host-road", "attach-post", "wall", "wall-none", "wall-large", "roof", "roof-none", "roof-text", "roof-edge", "roof-off", "wall-roof", "refuse-post-height", "refuse-bracket-height", "support-to-wall", "support-from-wall", "mark-stamp", "mark-run-start", "mark-run-end", "mark-run-road", "mark-bend", "mark-bend-start", "mark-bend-end", "mark-poly", "mark-gore-fork", "mark-gore-free", "mark-waiting", "mark-auto-circuit", "mark-auto", "mark-auto-marked", "mark-auto-chosen", "mark-clear", "bar-auto", "pick", "arm", "arm-none", "arm-space", "arm-large", "host-space", "support-arm", "over", "over-none", "over-low", "over-thin", "support-over" };
    }
}
