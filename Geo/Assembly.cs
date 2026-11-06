using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>One board on a support: what it is, its place in the order the boards were put on, and a number the caller uses to tell it apart (the index of the item it comes from).</summary>
    public sealed class AssemblyBoard
    {
        public SignInstance Instance;
        public int Slot;
        public int Member;

        /// <summary>
        /// Over a lane of the road, counted from the driver's left (1 is the leftmost of the lanes the board faces); 0 puts the board side by side with the others; Assembly.OnPost puts it on the post of
        /// the hung support. Only an arm or a beam has lanes: on posts the boards are stacked and this is ignored.
        /// </summary>
        public int Lane;

        /// <summary>
        /// With a lane: the sign-frame X of the board's middle (m from the first post, +X toward the driver's left), as it was when the lane was chosen. On a post (a stack on posts or on a host, and the
        /// post of a hung support; not a wall): the height of its lower edge set by hand (0.12.24, the arrow keys; Assembly.ByHand), 0 when it is stacked.
        /// </summary>
        public float OffsetX;

        /// <summary>The boards of this support are in an order of the player's (the slots are the order), not in the standard's (4.8.6: prohibition, mandatory, warning).</summary>
        public bool Manual;

        /// <summary>How far the board is turned about its post from the way the support faces (radians, TsItem.Turn); it counts only where Assembly.TurnAxis says a board can turn.</summary>
        public float Turn;

        /// <summary>
        /// On the back of a gantry's beam, facing the other way (0.12.22, TsItem.FlagBack; the owner: "门架式应该允许两面都能放牌子"): one gantry across a two-way road carries the boards of both ways.
        /// Its OffsetX is in the support's frame as the front boards' are. Ignored on any other support and on the post.
        /// </summary>
        public bool Back;

        /// <summary>How far the board leans forward on an arm or a beam (radians, 0.12.55: BoardTilt); nothing on a post.</summary>
        public float Tilt;

        /// <summary>How far the lower edge of a board on an arm or a beam is above the support's clearance (m, 0 to Assembly.MaxRise; TsItem.Rise, 0.12.73); nothing on a post, a wall or the post of a hung support.</summary>
        public float Rise;

        /// <summary>The height of the lower edge of the boards of a hung support as the player set it (m; TsItem.Clearance, 0.12.78); 0: the support's own. The support's, so every board of it carries the same.</summary>
        public float Clearance;
    }

    /// <summary>Everything the build of a support with its boards needs: the support, whether it is hung, the layout made for the whole and where each board is.</summary>
    public sealed class AssemblyPlan
    {
        public SupportDef Support;
        public bool Hung;
        public SignDef Total;
        public SupportLayout Layout;
        public List<Placement> Placed;
    }

    /// <summary>Where a board hangs on its support: the sign-frame X of its middle and the Y of its lower edge (m); the boards' own sizes are the instances'.</summary>
    public sealed class Placement
    {
        public AssemblyBoard Board;
        public float Cx;
        public float Bottom;

        /// <summary>On the post of a hung support (Assembly.OnPost): stacked in front of the post, its back at the layout's PostBackZ, not hung from the arm or the beam.</summary>
        public bool OnPost;

        /// <summary>On the back of a gantry's beam (Assembly.OnBack): built as a front board turned half round about its own middle.</summary>
        public bool Back;

        /// <summary>On the head of a street-name plate post (0.12.57, Assembly.ArrangePlate): its plane through the post's axis, printed on both faces (SignBuilder.AddPlateBoard).</summary>
        public bool OnHead;

        public SignDef Def => Board.Instance.Def;

        /// <summary>The height of the board itself; and of the board with a plate fixed on top of it.</summary>
        public float Height => Def.Height;
        public float HeightWithPlate => Def.Height + Def.ExtraTop;

        public float Cy => Bottom + Def.Height * 0.5f;
        public float Top => Bottom + Def.Height;
    }

    /// <summary>
    /// Several boards on one support (GB 5768.2 4.8.2: at most four on one support; 4.8.6: in the order prohibition, mandatory, warning, from the top down and from the left to the right;
    /// 4.8.12 c: on a gantry the lower edges of the boards at one height). The boards of one support are items that share a place and a support; this puts them in order and finds where each
    /// hangs, for any form of support: on posts they are stacked, on a cantilever's arm or a gantry's beam they hang side by side. An auxiliary sign (辅助标志) goes under the board it explains,
    /// whatever its place in the order: it is a part of that board.
    /// </summary>
    public static class Assembly
    {
        /// <summary>The most boards one support carries (4.8.2).</summary>
        public const int MaxBoards = 4;

        /// <summary>
        /// A gantry carries more: a board over each lane of a wide carriageway (the owner's request; GB 5768.2 4.8.2 says "at most 4" of the signs that are put at one place, and the boards
        /// of a gantry that stand side by side across the road are not at one place). The limit is for the editor's list and the cost of the mesh, not a rule of the standard.
        /// </summary>
        public const int MaxBoardsGantry = 12;

        /// <summary>The most boards the order ever keeps: the gantry's.</summary>
        public const int HardMax = MaxBoardsGantry;

        /// <summary>The most boards a support of this kind carries: 4 on a post or an arm, 12 on a gantry.</summary>
        public static int MaxFor(SupportKind kind) => kind == SupportKind.Gantry ? MaxBoardsGantry : kind == SupportKind.DoubleCantilever ? MaxBoardsDouble : kind == SupportKind.AttachedArm || kind == SupportKind.OverRoad ? 1 : MaxBoards;

        /// <summary>A double cantilever carries four boards on each arm, eight in all (the owner's reading of 4.8.2: the boards of the two arms are over different lanes, not at one place).</summary>
        public const int MaxBoardsPerArm = 4, MaxBoardsDouble = 2 * MaxBoardsPerArm;

        /// <summary>The space between boards one over the other, and between boards side by side (m).</summary>
        public const float StackGap = 0.08f, SideGap = 0.40f;

        /// <summary>
        /// The lane number of a board on the post of a hung support (0.12.2; SupportKinds.PostBoards: a cantilever's or a double cantilever's post, a gantry's first post): it is stacked there as on a single
        /// post, from the roadside clearance up and under the lowest arm's or chord's joint, facing the traffic the boards over the road face, and takes no room on the arm or the beam. Saved in the item's lane byte. A support
        /// that has no such post (posts, a wall) stacks every board and ignores it.
        /// </summary>
        public const int OnPost = 254;

        /// <summary>
        /// Whether a board of a support turns on its own about a post (0.12.8, AssemblyBoard.Turn), and the sign-frame X of that post's axis (which runs up through z = 0): a board in the stack on one
        /// post (a single post, a lamp post or a signal pole of the game's) and a board on the post of a hung support (Assembly.OnPost). Not a board on two or three posts, on an arm or a beam, or on a wall:
        /// those face as their support does.
        /// </summary>
        public static bool TurnAxis(SupportKind kind, SupportLayout layout, AssemblyBoard board, out float axisX)
        {
            axisX = 0f;
            if (board == null)
                return false;
            if (kind == SupportKind.SinglePole || kind == SupportKind.Attached)
            {
                if (layout.PoleX != null && layout.PoleX.Length > 1)
                    return false;
                axisX = layout.PoleX != null && layout.PoleX.Length == 1 ? layout.PoleX[0] : 0f;
                return true;
            }

            return SupportKinds.PostBoards(kind) && IsPostBoard(board);
        }

        /// <summary>The turn of a board in tenths of a degree, for the keys of built meshes (two turns that look the same are one mesh).</summary>
        public static int TurnKey(AssemblyBoard board) => board == null ? 0 : (int)Math.Round(board.Turn * 1800.0 / Math.PI);

        /// <summary>The lean of a board as a key of a parts cache (0.12.56; tenths of a degree): a lean changed is another mesh.</summary>
        public static int TiltKey(AssemblyBoard board) => board == null ? 0 : (int)Math.Round(board.Tilt * 1800.0 / Math.PI);

        /// <summary>The clearance the boards of a support ask for: the first that has one (they are all written together); 0 when none does.</summary>
        public static float ClearanceOf(IReadOnlyList<AssemblyBoard> boards)
        {
            foreach (AssemblyBoard board in boards)
            {
                if (board.Clearance > 0f)
                    return board.Clearance;
            }

            return 0f;
        }

        /// <summary>
        /// How far the boards stacked on the post of a hung support stand above its top (m; 0 when they all fit under it, as Attach.PostOfHung asks of a board put there): the stack does not move with the
        /// clearance, so a lower one brings the truss or the arm down onto it (0.12.78).
        /// </summary>
        public static float PostOverrun(AssemblyPlan plan)
        {
            float over = 0f;
            foreach (Placement p in plan.Placed)
            {
                if (p.OnPost)
                    over = Math.Max(over, p.Bottom + p.HeightWithPlate - plan.Layout.PostTop);
            }

            return over;
        }

        /// <summary>The clearance of a board as a key of a parts cache (0.12.78; centimetres): a clearance changed is another mesh.</summary>
        public static int ClearanceKey(AssemblyBoard board) => board == null || !(board.Clearance > 0f) ? 0 : (int)Math.Round(board.Clearance * 100f);

        /// <summary>The rise of a board as a key of a parts cache (0.12.73; centimetres): a rise changed is another mesh.</summary>
        public static int RiseKey(AssemblyBoard board) => board == null ? 0 : (int)Math.Round(ClampRise(board.Rise) * 100f);

        /// <summary>A turn kept within half a turn either way (radians).</summary>
        public static float WrapTurn(float turn)
        {
            if (float.IsNaN(turn) || float.IsInfinity(turn))
                return 0f;
            double t = Math.IEEERemainder(turn, 2.0 * Math.PI);
            return (float)t;
        }

        /// <summary>The board is on the post of its hung support.</summary>
        public static bool IsPostBoard(AssemblyBoard board) => board.Lane == OnPost;

        /// <summary>
        /// The most a board on an arm or a beam may be raised above the support's clearance (m; 0.12.73, the arrow keys). A figure of this mod's (no drawing read gives one): the clearance is the lowest
        /// a board hangs, and the arm or the beam is made for the highest board, so a board raised by this much takes its support up with it.
        /// </summary>
        public const float MaxRise = 2f;

        /// <summary>A rise within 0 to MaxRise, in centimetres (not a number, or below 0: 0).</summary>
        public static float ClampRise(float rise) => float.IsNaN(rise) || rise <= 0f ? 0f : (float)Math.Round(Math.Min(rise, MaxRise), 2);

        /// <summary>The lower edge of a board hung on an arm or a beam: the support's clearance and its rise.</summary>
        private static float HungBottom(SupportLayout layout, AssemblyBoard board) => layout.BottomClearance + ClampRise(board.Rise);

        /// <summary>
        /// The lowest a board's lower edge goes on a post (0.12.24; the owner: "最低可以到地面高度"): just above the flange of the post's foot; and the highest on a post of the support's own (it grows to
        /// carry the board) and on the game's lamp post or signal pole, whose height is not read.
        /// </summary>
        public const float LowestOnPost = 0.15f, HighestOnPost = 8f, HighestOnHost = 6f;

        /// <summary>The boards on the posts of a support stand at heights of their own (AssemblyBoard.OffsetX) rather than stacked: on posts and on a host, not on a wall (its stack has its own height) nor on an arm (its OffsetX is how far up or down it is moved).</summary>
        public static bool ByHand(SupportKind kind, IReadOnlyList<AssemblyBoard> boards)
        {
            if (SupportKinds.Hung(kind) || SupportKinds.OnWall(kind) || SupportKinds.OnArm(kind) || SupportKinds.OverRoad(kind))
                return false;
            foreach (AssemblyBoard board in boards)
            {
                if (board.OffsetX > 0f)
                    return true;
            }

            return false;
        }

        /// <summary>The board hangs on the back of a gantry's beam: only a gantry has a back the boards of the other way can face from (a cantilever's arm reaches over one way's lanes).</summary>
        public static bool OnBack(SupportKind kind, AssemblyBoard board) => kind == SupportKind.Gantry && board.Back && !IsPostBoard(board);

        /// <summary>The board stands over a lane, or at a place of its own (Attach.LaneHand): not side by side with the others and not on the post.</summary>
        public static bool OverLane(AssemblyBoard board) => board.Lane > 0 && board.Lane != OnPost;

        /// <summary>
        /// Boards over lanes (0.12.53, 22MR601): on a cantilever they start BoardFromPost from the post's axis (pp.78-92: 1000) and its arm ends ArmTipInside short of the farthest board's edge, behind
        /// it (p.80: the arm L + 600 from its flange for a board of L from 1000, so its end about 5 cm inside); on a gantry a board's edge keeps GantryClear from each post's axis (this mod's: clear of
        /// the chords' joints, which reach 0.6 m, p.94).
        /// </summary>
        public const float BoardFromPost = 1.0f, ArmTipInside = 0.05f, GantryClear = 1.0f;

        /// <summary>The rank of a kind of sign in 4.8.6: prohibition first (top, left), then mandatory, then warning; anything else (guide, tourist, notice) after them.</summary>
        public static int Rank(SignDef def)
        {
            switch (def.Group)
            {
                case "prohibition": return 0;
                case "mandatory": return 1;
                case "warning": return 2;
                // An electric box hangs under the signs of its post (and at the end of a beam).
                case Signs.Cabinets.Group: return 5;
                default: return IsNextExit(def) ? 4 : 3;
            }
        }

        public const string NextExitId = "guide.exit.next", ExitAdvanceId = "guide.exit.advance";

        /// <summary>路54, the next-exit board (9.3.11): it is always below the exit advance board, whichever was put on first.</summary>
        public static bool IsNextExit(SignDef def) => def.Id == NextExitId;

        public static bool IsAuxiliary(SignDef def) => def.Group == "auxiliary";

        /// <summary>
        /// The boards in the order they are put on the support: the main boards by rank (4.8.6), then by the order they were added; each auxiliary board right after the main board that was
        /// there when it was added (the last main board with a smaller slot), or first of all when it was added before any. At most HardMax are kept (the first ones); the limit of a kind of support is Attach's.
        /// </summary>
        public static List<AssemblyBoard> Order(IEnumerable<AssemblyBoard> boards)
        {
            var bySlot = new List<AssemblyBoard>(boards);
            bySlot.Sort((a, b) => a.Slot != b.Slot ? a.Slot.CompareTo(b.Slot) : a.Member.CompareTo(b.Member));
            if (bySlot.Count > HardMax)
                bySlot.RemoveRange(HardMax, bySlot.Count - HardMax);

            // The player has put them in an order of their own (moved a board up or down a post): the slots are the order, whatever the ranks are.
            foreach (AssemblyBoard board in bySlot)
            {
                if (board.Manual)
                    return bySlot;
            }

            var mains = new List<AssemblyBoard>();
            foreach (AssemblyBoard board in bySlot)
            {
                if (!IsAuxiliary(board.Instance.Def))
                    mains.Add(board);
            }

            // Stable: rank, and for equal ranks the order of the slots (bySlot is in that order already, and List.Sort is not stable, so the position is the tie-break).
            var indexed = new List<KeyValuePair<AssemblyBoard, int>>();
            for (int i = 0; i < mains.Count; i++)
                indexed.Add(new KeyValuePair<AssemblyBoard, int>(mains[i], i));
            indexed.Sort((a, b) =>
            {
                int rank = Rank(a.Key.Instance.Def).CompareTo(Rank(b.Key.Instance.Def));
                return rank != 0 ? rank : a.Value.CompareTo(b.Value);
            });

            var ordered = new List<AssemblyBoard>();
            foreach (KeyValuePair<AssemblyBoard, int> pair in indexed)
                ordered.Add(pair.Key);

            foreach (AssemblyBoard aux in bySlot)
            {
                if (!IsAuxiliary(aux.Instance.Def))
                    continue;
                AssemblyBoard owner = null;
                foreach (AssemblyBoard candidate in bySlot)
                {
                    if (!IsAuxiliary(candidate.Instance.Def) && candidate.Slot < aux.Slot)
                        owner = candidate;
                }

                int at = owner == null ? 0 : ordered.IndexOf(owner) + 1;
                // Behind any auxiliary boards of the same owner that came earlier.
                while (owner != null && at < ordered.Count && IsAuxiliary(ordered[at].Instance.Def) && ordered[at].Slot < aux.Slot)
                    at++;
                ordered.Insert(at, aux);
            }

            return ordered;
        }

        /// <summary>
        /// The one board the layout of the support is made for: when the boards are stacked (posts) as wide as the widest and as high as the stack, when they hang side by side (arm, beam)
        /// as wide as all of them with the gaps, as high as the highest. The plate on top of a board is counted in ExtraTop (of the top board, or of the highest of those side by side).
        /// A rounded rectangle, whatever the boards are (a support does not care about the outlines).
        /// </summary>
        public static SignDef TotalDef(IReadOnlyList<AssemblyBoard> ordered, bool hung, bool twoArms = false, bool gantry = false, bool byHand = false)
        {
            SignDef first = ordered[0].Instance.Def;
            // The boards on the post of a hung support take no room on its arm or its beam: a support with only those is laid out for a board of no size.
            if (ordered.Count == 1 && !(hung && (IsPostBoard(ordered[0]) || ordered[0].Rise > 0f)))
            {
                if (!byHand)
                    return first;
                // One board at a height of its own: the board with that lower edge, so that its post is as long as it then needs.
                SignDef alone = first.Derive();
                alone.BottomClearance = ordered[0].OffsetX;
                return alone;
            }

            SignDef total = first.Derive();
            total.Shape = ShapeKind.RoundRect;
            total.CornerRadius = 0f;
            total.Side = 0f;
            total.Template = null;
            float width = 0f, height = 0f, extra = 0f;
            if (hung)
            {
                // The boards that are over a lane stand where their lane is and do not take room from the others: the width is that of the boards that are side by side (all of them when none
                // is over a lane), and at least that of the widest board.
                bool anyLane = false;
                foreach (AssemblyBoard board in ordered)
                    anyLane |= OverLane(board);
                // A gantry's two faces are two rows of their own: the wider of them counts.
                float widest = 0f, front = 0f, back = 0f;
                int frontAutos = 0, backAutos = 0;
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (IsPostBoard(ordered[i]))
                        continue;
                    SignDef d = ordered[i].Instance.Def;
                    widest = Math.Max(widest, d.Width);
                    // The layout is made for the highest top: a raised board takes the arm or the beam up with it (0.12.73).
                    height = Math.Max(height, ClampRise(ordered[i].Rise) + d.Height);
                    extra = Math.Max(extra, d.ExtraTop);
                    if (anyLane && ordered[i].Lane != 0)
                        continue;
                    if (gantry && ordered[i].Back)
                        back += d.Width + (backAutos++ > 0 ? SideGap : 0f);
                    else
                        front += d.Width + (frontAutos++ > 0 ? SideGap : 0f);
                }

                width = Math.Max(front, back);

                // Two arms: the boards side by side are shared between them, and the layout is made for the wider group.
                if (twoArms)
                    width = WidestArmGroup(ordered);
                width = Math.Max(width, widest);
            }
            else if (byHand)
            {
                // Boards at heights of their own (ByHand): the layout is made for all of them, from the lowest lower edge to the highest top, so that the posts reach the highest (and are no
                // longer than that). The planner and the room on a post count the boards themselves (no byHand), not the gaps between them.
                float bottom = float.MaxValue, top = float.MinValue;
                foreach (AssemblyBoard board in ordered)
                {
                    SignDef d = board.Instance.Def;
                    float y0 = board.OffsetX > 0f ? board.OffsetX : first.BottomClearance;
                    width = Math.Max(width, d.Width);
                    bottom = Math.Min(bottom, y0);
                    if (y0 + d.Height + d.ExtraTop > top + extra)
                    {
                        top = y0 + d.Height;
                        extra = d.ExtraTop;
                    }
                }

                total.BottomClearance = bottom;
                height = top - bottom;
            }
            else
            {
                for (int i = 0; i < ordered.Count; i++)
                {
                    SignDef d = ordered[i].Instance.Def;
                    width = Math.Max(width, d.Width);
                    // Everything except the top board's plate is inside the stack.
                    height += d.Height + (i > 0 ? ordered[i].Instance.Def.ExtraTop : 0f) + (i > 0 ? StackGap : 0f);
                    if (i == 0)
                        extra = d.ExtraTop;
                }
            }

            total.Width = width;
            total.Height = height;
            total.ExtraTop = extra;
            return total;
        }

        /// <summary>The lower edge of the lowest board under a street-name plate on its post (Hangzhou 2015 guide figs. B.2, B.3: the guide plates' at 1.9 m, the post rising with them).</summary>
        public const float PlateStackBottom = 1.9f;

        /// <summary>The board of a street-name plate post's head: the first in the order that is a plate (PlateHead.Fits); -1 when none is.</summary>
        public static int HeadBoard(IReadOnlyList<AssemblyBoard> ordered)
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                if (PlateHead.Fits(ordered[i].Instance))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// The boards on a street-name plate post (0.12.57): the plate on the head (HeadBoard, whatever its place in the order: 4.8.6 puts a prohibition first), the others in their order under it,
        /// stacked up from PlateStackBottom with their gaps and the head's room (PlateHead.GapUnder) under the plate; a plate alone at its style's height (the layout's clearance); a board at a height of
        /// its own where it was put. The list is in the order of the boards.
        /// </summary>
        private static List<Placement> ArrangePlate(IReadOnlyList<AssemblyBoard> ordered, SupportLayout layout)
        {
            int head = HeadBoard(ordered);
            var placed = new Placement[ordered.Count];
            float y = ordered.Count > 1 ? PlateStackBottom : layout.BottomClearance;
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                if (i == head)
                    continue;
                SignDef d = ordered[i].Instance.Def;
                float bottom = ordered[i].OffsetX > 0f ? ordered[i].OffsetX : y;
                placed[i] = new Placement { Board = ordered[i], Cx = layout.BoardCentreX, Bottom = bottom };
                y = bottom + d.Height + d.ExtraTop + StackGap;
            }

            if (ordered.Count > 1)
                y += layout.Head.GapUnder - StackGap;
            placed[head] = new Placement { Board = ordered[head], Cx = layout.BoardCentreX, Bottom = ordered[head].OffsetX > 0f ? ordered[head].OffsetX : y, OnHead = true };
            return new List<Placement>(placed);
        }

        /// <summary>
        /// Where each board hangs, given the layout of the support (made for TotalDef). Stacked: all in the middle line of the support, the last of the order lowest, its lower edge at the
        /// clearance, each board above it with its gap and the plate of the one above. Side by side: the first of the order at the viewer's left (the larger X: in the sign's frame the
        /// viewer's right is -X), lower edges at the clearance.
        /// </summary>
        public static List<Placement> Arrange(IReadOnlyList<AssemblyBoard> ordered, bool hung, SupportLayout layout)
        {
            if (layout.Head != null)
                return ArrangePlate(ordered, layout);
            var placed = new List<Placement>();

            if (!hung && ByHand(layout.Kind, ordered))
            {
                foreach (AssemblyBoard board in ordered)
                    placed.Add(new Placement { Board = board, Cx = layout.BoardCentreX, Bottom = board.OffsetX > 0f ? board.OffsetX : layout.BottomClearance });
                return placed;
            }

            if (!hung)
            {
                float y = layout.BottomClearance;
                bool onArm = layout.Kind == SupportKind.AttachedArm;
                for (int i = ordered.Count - 1; i >= 0; i--)
                {
                    SignDef d = ordered[i].Instance.Def;
                    // On another mod's signal arm (0.12.57) a board is where it hangs by default, moved up or down by what the player set (its OffsetX: + up, within the room the arm's heads leave, HostPick.DropRange).
                    placed.Insert(0, new Placement { Board = ordered[i], Cx = layout.BoardCentreX, Bottom = y + (onArm ? ordered[i].OffsetX : 0f) });
                    // The plate on top of this board is between it and the board above: leave the room for it.
                    y += d.Height + d.ExtraTop + StackGap;
                }

                return placed;
            }

            float[] postBottoms = PostBottoms(ordered, layout);
            if (layout.Kind == SupportKind.DoubleCantilever)
                return ArrangeTwoArms(ordered, layout, postBottoms);

            // The boards that are over a lane stand at their offsets (kept clear of the posts and within the arm); the others are side by side, centred where the support's layout
            // says, the first of the order at the viewer's left; the ones on the post are stacked on it. The list is in the order of the boards either way.
            bool anyLane = false;
            foreach (AssemblyBoard board in ordered)
                anyLane |= OverLane(board);
            // A gantry's back is a row of its own, read by the traffic of the other way: its first board at that traffic's left, the smaller X.
            float total = 0f, totalBack = 0f;
            int autos = 0, autosBack = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (IsPostBoard(ordered[i]) || (anyLane && ordered[i].Lane != 0))
                    continue;
                if (OnBack(layout.Kind, ordered[i]))
                    totalBack += ordered[i].Instance.Def.Width + (autosBack++ > 0 ? SideGap : 0f);
                else
                    total += ordered[i].Instance.Def.Width + (autos++ > 0 ? SideGap : 0f);
            }

            float x = layout.BoardCentreX + total * 0.5f, xBack = layout.BoardCentreX - totalBack * 0.5f;
            for (int i = 0; i < ordered.Count; i++)
            {
                SignDef d = ordered[i].Instance.Def;
                if (IsPostBoard(ordered[i]))
                {
                    placed.Add(new Placement { Board = ordered[i], Cx = 0f, Bottom = postBottoms[i], OnPost = true });
                    continue;
                }

                bool back = OnBack(layout.Kind, ordered[i]);
                if (anyLane && ordered[i].Lane > 0)
                {
                    // The bounds are along the arm; a cantilever's arm may reach to -X (ArmSide).
                    LaneBounds(layout, d.Width * 0.5f, out float lo, out float hi);
                    float along = layout.ArmSide * ordered[i].OffsetX;
                    float cx = layout.ArmSide * (hi >= lo ? Math.Max(lo, Math.Min(hi, along)) : (lo + hi) * 0.5f);
                    placed.Add(new Placement { Board = ordered[i], Cx = cx, Bottom = HungBottom(layout, ordered[i]), Back = back });
                    continue;
                }

                if (back)
                {
                    placed.Add(new Placement { Board = ordered[i], Cx = xBack + d.Width * 0.5f, Bottom = HungBottom(layout, ordered[i]), Back = true });
                    xBack += d.Width + SideGap;
                    continue;
                }

                placed.Add(new Placement { Board = ordered[i], Cx = x - d.Width * 0.5f, Bottom = HungBottom(layout, ordered[i]) });
                x -= d.Width + SideGap;
            }

            return placed;
        }

        /// <summary>
        /// The arm each board of a double cantilever is on, in the order of the boards: +1 the left arm (X above 0, the driver's left), -1 the right arm, 0 the post (OnPost). A board over a lane is on the
        /// arm its X says; the boards side by side are shared between the arms, the first half (rounded up) on the left, so that reading from the left to the right is the order of 4.8.6.
        /// </summary>
        public static int[] Arms(IReadOnlyList<AssemblyBoard> ordered)
        {
            var arms = new int[ordered.Count];
            int autos = 0;
            foreach (AssemblyBoard board in ordered)
            {
                if (board.Lane <= 0)
                    autos++;
            }

            int onLeft = (autos + 1) / 2, seen = 0;
            for (int i = 0; i < ordered.Count; i++)
                arms[i] = IsPostBoard(ordered[i]) ? 0 : ordered[i].Lane > 0 ? (ordered[i].OffsetX < 0f ? -1 : 1) : (seen++ < onLeft ? 1 : -1);
            return arms;
        }

        /// <summary>
        /// The lower edges of the boards on the post of a hung support (NaN for the others), in the order of the boards: stacked as on a single post, the last of them lowest, its lower edge at the layout's
        /// PostClearance, each one above with the gap and the plate of the one above.
        /// </summary>
        public static float[] PostBottoms(IReadOnlyList<AssemblyBoard> ordered, SupportLayout layout)
        {
            var bottoms = new float[ordered.Count];
            float y = layout.PostClearance;
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                bottoms[i] = float.NaN;
                if (!IsPostBoard(ordered[i]))
                    continue;
                // At a height of its own (0.12.24), else the next in the stack.
                if (ordered[i].OffsetX > 0f)
                {
                    bottoms[i] = ordered[i].OffsetX;
                    continue;
                }

                SignDef d = ordered[i].Instance.Def;
                bottoms[i] = y;
                y += d.Height + d.ExtraTop + StackGap;
            }

            return bottoms;
        }

        /// <summary>The height of the stack on the post of a hung support (m, from the lower edge of its lowest board to the top of its highest, with the gaps and plates); 0 when there is none.</summary>
        public static float PostStackHeight(IReadOnlyList<AssemblyBoard> ordered)
        {
            var onPost = new List<AssemblyBoard>();
            foreach (AssemblyBoard board in ordered)
            {
                if (IsPostBoard(board))
                    onPost.Add(board);
            }

            return onPost.Count == 0 ? 0f : TotalDef(onPost, false).Height;
        }

        /// <summary>The width of the wider of the two groups of boards that are side by side on a double cantilever's arms (with the gaps).</summary>
        public static float WidestArmGroup(IReadOnlyList<AssemblyBoard> ordered)
        {
            int[] arms = Arms(ordered);
            float left = 0f, right = 0f;
            int nl = 0, nr = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Lane > 0)
                    continue;
                float w = ordered[i].Instance.Def.Width;
                if (arms[i] > 0)
                    left += w + (nl++ > 0 ? SideGap : 0f);
                else
                    right += w + (nr++ > 0 ? SideGap : 0f);
            }

            return Math.Max(left, right);
        }

        /// <summary>
        /// The boards of a double cantilever: the ones over a lane stand at their (signed) offsets, kept clear of the post and within their arm; the others are side by side on
        /// their arm, the group centred at the same distance from the post on both arms, the first of the order at the viewer's left on each (so left to right across the whole is the order).
        /// </summary>
        private static List<Placement> ArrangeTwoArms(IReadOnlyList<AssemblyBoard> ordered, SupportLayout layout, float[] postBottoms)
        {
            int[] arms = Arms(ordered);
            float totalLeft = 0f, totalRight = 0f;
            int nl = 0, nr = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Lane > 0)
                    continue;
                float w = ordered[i].Instance.Def.Width;
                if (arms[i] > 0)
                    totalLeft += w + (nl++ > 0 ? SideGap : 0f);
                else
                    totalRight += w + (nr++ > 0 ? SideGap : 0f);
            }

            float xLeft = layout.BoardCentreX + totalLeft * 0.5f, xRight = -layout.BoardCentreX + totalRight * 0.5f;
            var placed = new List<Placement>();
            for (int i = 0; i < ordered.Count; i++)
            {
                SignDef d = ordered[i].Instance.Def;
                if (IsPostBoard(ordered[i]))
                {
                    placed.Add(new Placement { Board = ordered[i], Cx = 0f, Bottom = postBottoms[i], OnPost = true });
                    continue;
                }

                if (ordered[i].Lane > 0)
                {
                    LaneBounds(layout, d.Width * 0.5f, out float lo, out float hi);
                    float magnitude = Math.Abs(ordered[i].OffsetX);
                    float at = hi >= lo ? Math.Max(lo, Math.Min(hi, magnitude)) : (lo + hi) * 0.5f;
                    placed.Add(new Placement { Board = ordered[i], Cx = arms[i] * at, Bottom = HungBottom(layout, ordered[i]) });
                    continue;
                }

                if (arms[i] > 0)
                {
                    placed.Add(new Placement { Board = ordered[i], Cx = xLeft - d.Width * 0.5f, Bottom = HungBottom(layout, ordered[i]) });
                    xLeft -= d.Width + SideGap;
                }
                else
                {
                    placed.Add(new Placement { Board = ordered[i], Cx = xRight - d.Width * 0.5f, Bottom = HungBottom(layout, ordered[i]) });
                    xRight -= d.Width + SideGap;
                }
            }

            return placed;
        }

        /// <summary>The range of sign-frame X that the middle of a board of the half width half may be in on a hung support: clear of a gantry's posts, on a cantilever from BoardFromPost to its arm's end.</summary>
        public static void LaneBounds(SupportLayout layout, float half, out float lo, out float hi)
        {
            if (layout.Kind == SupportKind.Gantry)
            {
                // GantryClear from each post: the same figure the span a board needs is worked out with (RequiredReach), so that a board at the end of the span does not ask for a longer one.
                lo = GantryClear + half;
                hi = layout.ArmLength - GantryClear - half;
            }
            else
            {
                lo = layout.BoardFrom + half;
                hi = layout.ArmLength + ArmTipInside - half;
            }
        }

        /// <summary>
        /// The span of a gantry or the length of an arm (m) that the boards over lanes need: the farthest edge of any of them and the room the end asks for. 0 when none is over a lane (or the
        /// support is not hung).
        /// </summary>
        public static float RequiredReach(IReadOnlyList<AssemblyBoard> ordered, SupportKind kind)
        {
            if (kind != SupportKind.Gantry && !SupportKinds.Arm(kind))
                return 0f;
            float need = 0f;
            foreach (AssemblyBoard board in ordered)
            {
                if (!OverLane(board))
                    continue;
                float right = Math.Abs(board.OffsetX) + board.Instance.Def.Width * 0.5f;
                need = Math.Max(need, right + (kind == SupportKind.Gantry ? GantryClear : -ArmTipInside));
            }

            return need;
        }

        /// <summary>
        /// The reach to keep for a hung support (0.12.9): its own (ownReach: what it was placed with or the player set; 0 the support's default), unless the boards over lanes or at places of their own
        /// need a longer one, which is then what they need in half metres, as Attach makes it when a board is put on. So a board taken off or moved in gives back what it took (the owner:
        /// "悬架原本的路牌删除或者移动之后，长度应该能自动缩小"); the boards that are left stay where they are. Not hung: ownReach as it is.
        /// </summary>
        public static float ReachFor(IReadOnlyList<AssemblyBoard> ordered, SupportDef support, float ownReach)
        {
            if (support == null || !SupportKinds.Hung(support.Kind))
                return ownReach;
            float need = RequiredReach(ordered, support.Kind);
            float own = ownReach > 0f ? ownReach : support.ArmLength;
            return need > own + 1e-3f ? Math.Min(SupportLayouts.MaxSpan, (float)Math.Ceiling(need * 2f) / 2f) : ownReach;
        }

        /// <summary>
        /// The support for the boards with its layout and where each board is. The support is the one given (null: the planner's for the whole); reach is the span or arm asked for (0: the
        /// support's own), made longer when a board over a lane needs it.
        /// </summary>
        public static AssemblyPlan Plan(IReadOnlyList<AssemblyBoard> ordered, SupportDef support, float reach)
        {
            if (support == null)
                support = SupportPlanner.Auto(TotalDef(ordered, false), ordered.Count > 1);
            // The framed cantilever's frames hold plates (0.12.57): with an LED cabinet, an electric box or a lamp-post ad on it, it is the plain cantilever reaching the same way.
            if (support.Framed)
            {
                foreach (AssemblyBoard b in ordered)
                {
                    if (b.Instance.IsLed || b.Instance.Board?.Box != null || b.Instance.Board?.PoleAd != null)
                    {
                        support = SupportCatalog.ById(support.Side < 0 ? SupportCatalog.CantileverRight : SupportCatalog.Cantilever);
                        break;
                    }
                }
            }

            bool hung = SupportKinds.Hung(support.Kind);
            SignDef total = TotalDef(ordered, hung, support.Kind == SupportKind.DoubleCantilever, support.Kind == SupportKind.Gantry, ByHand(support.Kind, ordered));
            float need = RequiredReach(ordered, support.Kind);
            if (need > 0f)
                reach = Math.Max(reach > 0f ? reach : support.ArmLength, need);
            SupportLayout layout = SupportLayouts.Of(total, support, reach, hung ? ClearanceOf(ordered) : 0f);
            // A plate post with no plate for its head (PlateHead.Fits) is a plain post.
            if (layout.Head != null && HeadBoard(ordered) < 0)
                layout = SupportLayouts.Of(total, SupportCatalog.ById(SupportCatalog.SinglePole), reach);
            List<Placement> placed = Arrange(ordered, hung, layout);
            // A post ends just over the top channel of its highest board, behind it (0.12.53, SupportLayouts.PostTopFor).
            if (!hung && placed.Count > 0 && !SupportKinds.OnWall(support.Kind) && !SupportKinds.OnArm(support.Kind) && !SupportKinds.OverRoad(support.Kind))
            {
                layout.PoleTop = float.MinValue;
                foreach (Placement p in placed)
                    layout.PoleTop = Math.Max(layout.PoleTop, SupportLayouts.PostTopFor(p.Cy, p.Def.Height));
                // A street-name plate post ends 3 cm up its head's cone or flare.
                if (layout.Head != null)
                    layout.PoleTop = placed.Find(p => p.OnHead).Bottom - layout.Head.Height + 0.03f;
            }

            return new AssemblyPlan { Support = support, Hung = hung, Total = total, Layout = layout, Placed = placed };
        }

        /// <summary>
        /// One lane for each board: the boards in the order they hang (the first at the driver's left) go to the lanes from the driver's left, the first board to the first lane, and so on. Per
        /// board, the lane (1-based) and the X of its middle; a board for which there is no lane left is side by side (0, 0). centres are the lanes' X from the driver's left (the largest first).
        /// </summary>
        public static List<KeyValuePair<int, float>> SpreadOverLanes(IReadOnlyList<AssemblyBoard> ordered, IReadOnlyList<float> centres)
        {
            var result = new List<KeyValuePair<int, float>>();
            for (int i = 0; i < ordered.Count; i++)
                result.Add(i < centres.Count ? new KeyValuePair<int, float>(i + 1, centres[i]) : new KeyValuePair<int, float>(0, 0f));
            return result;
        }

        /// <summary>The pairs of boards (their members) that overlap sideways on a hung support: two over one lane, or a board over a lane that runs into the side-by-side ones. Empty on posts; the stack on the post of a hung support is one above the other, never side by side.</summary>
        public static List<int[]> Overlaps(IReadOnlyList<Placement> placed, bool hung)
        {
            var pairs = new List<int[]>();
            if (!hung)
                return pairs;
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    if (placed[i].OnPost || placed[j].OnPost || placed[i].Back != placed[j].Back)
                        continue;
                    float left = Math.Max(placed[i].Cx - placed[i].Def.Width * 0.5f, placed[j].Cx - placed[j].Def.Width * 0.5f);
                    float right = Math.Min(placed[i].Cx + placed[i].Def.Width * 0.5f, placed[j].Cx + placed[j].Def.Width * 0.5f);
                    if (right - left > 0.02f)
                        pairs.Add(new[] { placed[i].Board.Member, placed[j].Board.Member });
                }
            }

            return pairs;
        }
    }
}
