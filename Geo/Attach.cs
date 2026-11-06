using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>Why a sign cannot go on a support.</summary>
    public enum AttachReason
    {
        None,

        /// <summary>The support has the most boards it may have (4, GB 5768.2 4.8.2).</summary>
        Full,

        /// <summary>A post takes small boards only: this one, or one that is on it, is large (Attach.IsLarge).</summary>
        Large,

        /// <summary>The stack on a post would be higher than Attach.MaxPostStack.</summary>
        NoHeight,

        /// <summary>There is no room along the beam or the arm for a board of this width.</summary>
        NoSpace
    }

    /// <summary>A closed interval of the beam (sign-frame X, m).</summary>
    public struct BeamSpan
    {
        public float Lo, Hi;

        public BeamSpan(float lo, float hi)
        {
            Lo = lo;
            Hi = hi;
        }

        public float Length => Hi - Lo;
    }

    /// <summary>What is asked of Attach.Evaluate: a sign to be put on a support that already carries boards, or (Moving) a board of it to be moved to another place on it.</summary>
    public sealed class AttachRequest
    {
        /// <summary>The boards that are on the support, with the numbers they are known by (AssemblyBoard.Member). When a board is moved, the others.</summary>
        public IReadOnlyList<AssemblyBoard> Existing;

        /// <summary>The support's own form; null is the planner's choice for the whole.</summary>
        public SupportDef Support;

        /// <summary>The span or the arm as it is kept with the items (0: the support's own).</summary>
        public float Reach;

        /// <summary>The sign that is put on, or the board that is moved.</summary>
        public SignInstance Board;

        /// <summary>The number the new board is to have (-1: the next free one). A board that is moved keeps its own.</summary>
        public int Member = -1;

        /// <summary>The slot a moved board had (-1: the next one).</summary>
        public int Slot = -1;

        /// <summary>Posts: where in the stack from the top (0 is the top) the board goes; -1 is the standard's order (4.8.6), or the end of a stack the player has put in an order of their own.</summary>
        public int Index = -1;

        /// <summary>Beams and arms: the sign-frame X of the board's middle that is asked for; NaN is the middle of the beam. The nearest place that is free is the one that is given.</summary>
        public float Offset = float.NaN;

        /// <summary>A board that is already on the support is being moved: it is not counted again as large or against the height of the stack.</summary>
        public bool Moving;

        /// <summary>The board goes on the back of a gantry's beam (AssemblyBoard.Back): the boards of the front take no room from it, nor it from them. Ignored on any other support.</summary>
        public bool Back;

        /// <summary>
        /// On a post (posts of the support's own, a host, the post of a hung support; not a wall): the height asked for the board's lower edge (m, 0.12.24); the nearest free height is given, the
        /// boards that are there are kept where they stand. NaN: the board goes into the stack as before.
        /// </summary>
        public float Height = float.NaN;

        /// <summary>
        /// On an arm or a beam (0.12.73): how far the board's lower edge is above the support's clearance (m); a board that is moved keeps its own. Kept within 0 to Assembly.MaxRise; ignored on
        /// any other support.
        /// </summary>
        public float Rise;

        /// <summary>
        /// 0.12.51, an attached support on another mod's pole (HostCandidate.Highest and Busy; 0 and null: a post of the game's): the highest the top of a board may reach and the heights its heads take
        /// (lo, hi pairs over its foot), which a board asked for at a height keeps out of.
        /// </summary>
        public float HostHighest;

        public float[] HostBusy;

        /// <summary>
        /// The beam may be made longer for the board. An arm always may (its end is free); a gantry only when this is set (the player pressed a key to take the board past the end, or the editor
        /// adds a board) or when there is no room at all in the span it has: its posts stand where the road's edges are, and a board that goes with the mouse over the span stays inside it.
        /// </summary>
        public bool Grow;

        /// <summary>
        /// A hung support whose post takes boards (SupportKinds.PostBoards): the board goes on the post, stacked as on a single post under the lowest arm's or chord's joint (the player pointed at the post or its foot),
        /// not on the arm or the beam. Ignored for any other support.
        /// </summary>
        public bool OnPost;
    }

    /// <summary>What Attach.Evaluate worked out.</summary>
    public sealed class AttachResult
    {
        public bool Ok;
        public AttachReason Reason;

        /// <summary>True for an arm or a beam, false for posts.</summary>
        public bool Hung;

        /// <summary>The board goes on the post of the hung support (AttachRequest.OnPost): Index, Count and the room are those of the stack on the post.</summary>
        public bool OnPost;

        /// <summary>The support the boards are on after (its own, or the planner's for the whole).</summary>
        public SupportDef Support;

        /// <summary>
        /// All the boards after, in the order they hang (top or viewer's left first), as copies that carry what is to be written back: slots, lanes (a board that was side by side and is now at a
        /// place of its own is pinned where it stands, lane Attach.LaneHand), places, and whether the order is the player's. The new board is the one whose member is NewMember.
        /// </summary>
        public List<AssemblyBoard> Boards = new List<AssemblyBoard>();

        public int NewMember;

        /// <summary>Posts: the place in the stack, from the top (0), and the number of boards after.</summary>
        public int Index;
        public int Count;

        /// <summary>Whether the boards are in the player's order (so that the standard's rank is not applied again).</summary>
        public bool Manual;

        /// <summary>Beams and arms: the sign-frame X of the new board's middle, the span or arm it needs (0: no change), and the intervals of X the middle of the board may be in.</summary>
        public float Offset;
        public float Reach;
        public List<BeamSpan> Free = new List<BeamSpan>();

        /// <summary>The room that is left after: posts, the height of the stack that is still free (m); beams, the length of the beam that is still free (m, with the gaps counted). Never negative.</summary>
        public float SpaceLeft;

        /// <summary>The room there was: the same measures before the board was put on. For the message when it does not fit.</summary>
        public float SpaceBefore;

        /// <summary>The most boards this kind of support carries (Assembly.MaxFor), and, on a beam or an arm, how long it is after (m) and whether it was made longer for the board.</summary>
        public int MaxBoards;
        public float Span;
        public bool Grew;
    }

    /// <summary>
    /// Putting a sign on a support that has boards on it, and moving a board on it (the tool's "move the sign onto a support and it joins it", and the arrow keys). Pure: it decides whether the
    /// sign may go on (at most four boards; a post takes small boards only and no more than a stack of MaxPostStack; a beam needs room), where, and what has to be written to the items; the
    /// tool only reads the entities into AssemblyBoards, asks, and writes what comes back. GB 5768.2 4.8.2 gives four boards to a support; the rest (small boards on posts, the height) are the
    /// owner's request and a size that a roadside post of this kind carries.
    /// </summary>
    public static class Attach
    {
        /// <summary>The tallest stack of boards a post carries (m, from the lower edge of the lowest board to the top of the highest, with the gaps and plates).</summary>
        public const float MaxPostStack = 3.3f;

        /// <summary>The tallest stack a wall bracket carries (m: the boards hang from its arm, one under the other), and a wall the boards are fixed flat on (a number for the hints; the wall is not the limit).</summary>
        public const float MaxBracketStack = 2.0f, MaxWallStack = 10f;

        /// <summary>The tallest stack a support of this kind carries: a post's, a bracket's, a wall's.</summary>
        public static float MaxStackFor(SupportKind kind) => kind == SupportKind.WallBracket ? MaxBracketStack : kind == SupportKind.WallFlat ? MaxWallStack : MaxPostStack;

        /// <summary>The height the stack on the post of a hung support may have (m): from the roadside clearance to under the lowest arm's or chord's joint, and no more than a post's.</summary>
        public static float PostRoom(SupportLayout layout) => Math.Max(0f, Math.Min(MaxPostStack, layout.PostTop - layout.PostClearance));

        /// <summary>The lane of a board on the post of a hung support (Assembly.OnPost).</summary>
        public const byte LanePost = (byte)Assembly.OnPost;

        /// <summary>A board wider or higher than this is large (m): the round and triangular boards of the fastest band are 1.2 to 1.3 m; the exit boards and the boards over several lanes are not.</summary>
        public const float LargeBoard = 1.4f;

        /// <summary>The lane that stands for "put there by hand": over no lane, at a place of its own (Placement keeps it clear of the posts and the end).</summary>
        public const byte LaneHand = 255;

        /// <summary>A press of the up or down arrow on a post moves a board this far (m), and with Shift the coarse step.</summary>
        public const float HeightStep = 0.1f, HeightStepCoarse = 0.5f;

        /// <summary>
        /// The lower edges a board of the given height (with its plate) may have between lo and hi when the others stand at (lower edge, height with plate) with StackGap between: the intervals of
        /// FreeIntervals, of the middles turned into lower edges.
        /// </summary>
        public static List<BeamSpan> FreeHeights(float lo, float hi, IReadOnlyList<KeyValuePair<float, float>> others, float size)
        {
            var middles = new List<KeyValuePair<float, float>>(others.Count);
            foreach (KeyValuePair<float, float> o in others)
                middles.Add(new KeyValuePair<float, float>(o.Key + o.Value * 0.5f, o.Value));
            var free = FreeIntervals(lo + size * 0.5f, hi + size * 0.5f, middles, size, Assembly.StackGap);
            for (int i = 0; i < free.Count; i++)
                free[i] = new BeamSpan(free[i].Lo - size * 0.5f, free[i].Hi - size * 0.5f);
            return free;
        }

        /// <summary>
        /// The board at the height asked among the others of a stack (boards; the post boards of a hung support when onPost), those kept at the heights they have in now: the nearest free lower
        /// edge between lo and hi. False (NoHeight) when there is none.
        /// </summary>
        private static bool AtHeight(AttachRequest r, AttachResult res, List<AssemblyBoard> boards, AssemblyBoard added, AssemblyPlan now, bool onPost, float asked, float lo, float hi)
        {
            var others = new List<KeyValuePair<float, float>>();
            foreach (AssemblyBoard b in boards)
            {
                if (onPost != Assembly.IsPostBoard(b))
                    continue;
                Placement at = now?.Placed.Find(p => p.Board.Member == b.Member);
                if (b.OffsetX <= 0f && at != null)
                    b.OffsetX = at.Bottom;
                others.Add(new KeyValuePair<float, float>(b.OffsetX, b.Instance.Def.Height + b.Instance.Def.ExtraTop));
            }

            // A host's heads take their heights as boards do (0.12.51).
            if (!onPost && r.HostBusy != null)
            {
                for (int i = 0; i + 1 < r.HostBusy.Length; i += 2)
                    others.Add(new KeyValuePair<float, float>(Math.Min(r.HostBusy[i], r.HostBusy[i + 1]), Math.Abs(r.HostBusy[i + 1] - r.HostBusy[i])));
            }

            res.Free = FreeHeights(lo, hi, others, r.Board.Def.Height + r.Board.Def.ExtraTop);
            float bottom = Nearest(res.Free, Math.Max(lo, asked));
            if (float.IsNaN(bottom))
            {
                res.Reason = AttachReason.NoHeight;
                return false;
            }

            added.OffsetX = (float)Math.Round(bottom, 3);
            res.Offset = added.OffsetX;
            res.Index = 0;
            foreach (KeyValuePair<float, float> o in others)
                res.Index += o.Key > bottom ? 1 : 0;
            return true;
        }

        /// <summary>One press of an arrow key along a beam (m).</summary>
        public const float Step = 0.25f;

        /// <summary>
        /// One press of the up or down arrow on a board hung on an arm or a beam (0.12.73): its rise moved by HeightStep (HeightStepCoarse with Shift), up when dir is +1, within 0 to Assembly.MaxRise: the
        /// lowest a board hangs is the support's clearance, and the highest is as far as MaxRise.
        /// </summary>
        public static float StepRise(float rise, int dir, bool coarse) => Assembly.ClampRise(Assembly.ClampRise(rise) + dir * (coarse ? HeightStepCoarse : HeightStep));

        public static bool IsLarge(SignDef def) => def.Width > LargeBoard + 1e-4f || def.Height > LargeBoard + 1e-4f;

        /// <summary>The 500 m exit advance board (路42, 44 or 46 at 500 m): the one 9.3.11 says the next-exit board is put under.</summary>
        public static bool IsExitAdvance500(SignInstance board) =>
            board.Def.Id == Assembly.ExitAdvanceId && board.Settings != null && board.Settings.Is("dist", "500") && board.Settings.Is("unit", "m");

        /// <summary>
        /// The one arrangement of large boards that a post does take: the exit advance board of 500 m with the next-exit board (路54) under it, as GB 5768.2 Fig. 277 shows it (on two posts, or on
        /// one). Exactly those two, in either order of putting them on. The rules for the size and the height of a stack do not apply to them: the exit board alone is 3.6 m high.
        /// </summary>
        public static bool IsNextExitPair(IReadOnlyList<AssemblyBoard> boards)
        {
            if (boards.Count != 2)
                return false;
            bool advance = false, next = false;
            foreach (AssemblyBoard b in boards)
            {
                advance |= IsExitAdvance500(b.Instance);
                next |= Assembly.IsNextExit(b.Instance.Def);
            }

            return advance && next;
        }

        /// <summary>The intervals of the beam in which the middle of a board of the given width may be: inside [lo, hi], and (width / 2 + the other's half width + gap) away from the middle of every other board.</summary>
        public static List<BeamSpan> FreeIntervals(float lo, float hi, IReadOnlyList<KeyValuePair<float, float>> others, float width, float gap)
        {
            var blocks = new List<BeamSpan>();
            foreach (KeyValuePair<float, float> other in others)
            {
                float reach = (other.Value + width) * 0.5f + gap;
                blocks.Add(new BeamSpan(other.Key - reach, other.Key + reach));
            }

            blocks.Sort((a, b) => a.Lo.CompareTo(b.Lo));
            var free = new List<BeamSpan>();
            float from = lo;
            foreach (BeamSpan block in blocks)
            {
                if (block.Lo > from + 1e-4f)
                    free.Add(new BeamSpan(from, Math.Min(block.Lo, hi)));
                from = Math.Max(from, block.Hi);
                if (from > hi)
                    break;
            }

            if (from <= hi + 1e-4f)
                free.Add(new BeamSpan(from, hi));

            free.RemoveAll(s => s.Hi < s.Lo - 1e-4f || s.Hi < lo - 1e-4f || s.Lo > hi + 1e-4f);
            return free;
        }

        /// <summary>The nearest place the middle of a board may be to x: x itself when it is free. NaN when there is no free place.</summary>
        public static float Nearest(IReadOnlyList<BeamSpan> free, float x)
        {
            float best = float.NaN, bestDistance = float.MaxValue;
            foreach (BeamSpan span in free)
            {
                float at = Math.Max(span.Lo, Math.Min(span.Hi, x));
                float distance = Math.Abs(at - x);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = at;
                }
            }

            return best;
        }

        /// <summary>
        /// One press of an arrow key: x moved by step in direction dir (+1 toward larger X, -1 toward smaller). Inside a free interval it moves by the step; at the end of one it jumps over what is in
        /// the way to the start of the next free interval in that direction; at the last it stays at the end. x is first brought to the nearest free place.
        /// </summary>
        public static float Move(IReadOnlyList<BeamSpan> free, float x, int dir, float step)
        {
            float at = Nearest(free, x);
            if (float.IsNaN(at) || dir == 0)
                return float.IsNaN(at) ? x : at;

            float target = at + dir * step;
            foreach (BeamSpan span in free)
            {
                if (target >= span.Lo - 1e-4f && target <= span.Hi + 1e-4f)
                    return Math.Max(span.Lo, Math.Min(span.Hi, target));
            }

            // Not in a free interval: the next one in that direction, else the end of the one we are in.
            float next = float.NaN;
            foreach (BeamSpan span in free)
            {
                if (dir > 0 && span.Lo > at + 1e-4f && (float.IsNaN(next) || span.Lo < next))
                    next = span.Lo;
                if (dir < 0 && span.Hi < at - 1e-4f && (float.IsNaN(next) || span.Hi > next))
                    next = span.Hi;
            }

            if (!float.IsNaN(next))
                return next;
            foreach (BeamSpan span in free)
            {
                if (at >= span.Lo - 1e-4f && at <= span.Hi + 1e-4f)
                    return dir > 0 ? span.Hi : span.Lo;
            }

            return at;
        }

        private static AssemblyBoard Copy(AssemblyBoard b) => new AssemblyBoard { Instance = b.Instance, Slot = b.Slot, Member = b.Member, Lane = b.Lane, OffsetX = b.OffsetX, Manual = b.Manual, Turn = b.Turn, Back = b.Back, Rise = b.Rise, Clearance = b.Clearance };

        private static int NextMember(IReadOnlyList<AssemblyBoard> boards)
        {
            int next = 0;
            foreach (AssemblyBoard b in boards)
                next = Math.Max(next, b.Member + 1);
            return next;
        }

        private static int NextSlot(IReadOnlyList<AssemblyBoard> boards)
        {
            int next = 0;
            foreach (AssemblyBoard b in boards)
                next = Math.Max(next, b.Slot + 1);
            return next;
        }

        /// <summary>Decides whether the board may go on the support and where; see AttachRequest and AttachResult.</summary>
        public static AttachResult Evaluate(AttachRequest r)
        {
            var res = new AttachResult();
            List<AssemblyBoard> existing = Assembly.Order(r.Existing);
            var boards = new List<AssemblyBoard>();
            foreach (AssemblyBoard b in existing)
                boards.Add(Copy(b));

            // The new board is the support's too: it has the clearance the others were set to.
            var added = new AssemblyBoard { Instance = r.Board, Member = r.Member >= 0 ? r.Member : NextMember(r.Existing), Slot = r.Slot >= 0 ? r.Slot : NextSlot(existing), Clearance = Assembly.ClearanceOf(existing) };
            res.NewMember = added.Member;
            res.Boards = new List<AssemblyBoard>(boards);

            bool manualGroup = false;
            foreach (AssemblyBoard b in existing)
                manualGroup |= b.Manual;

            var candidate = new List<AssemblyBoard>(boards) { added };
            SupportDef support = r.Support ?? SupportPlanner.Auto(Assembly.TotalDef(Assembly.Order(candidate), false), candidate.Count > 1);
            res.Support = support;
            res.Hung = SupportKinds.Hung(support.Kind);
            res.MaxBoards = Assembly.MaxFor(support.Kind);
            if (existing.Count >= res.MaxBoards)
            {
                res.Reason = AttachReason.Full;
                return res;
            }

            return res.Hung
                ? (r.OnPost && SupportKinds.PostBoards(support.Kind) ? PostOfHung(r, res, existing, boards, added, support) : Beam(r, res, existing, boards, added, support))
                : Post(r, res, existing, boards, added, manualGroup, support);
        }

        private static AttachResult Post(AttachRequest r, AttachResult res, List<AssemblyBoard> existing, List<AssemblyBoard> boards, AssemblyBoard added, bool manualGroup, SupportDef support)
        {
            SupportKind kind = support.Kind;
            // Fig. 277: the 500 m exit board with the next-exit board under it is the exception to "small boards only" and to the height of a stack. A wall takes large boards flat on it.
            bool pair = IsNextExitPair(new List<AssemblyBoard>(boards) { added });
            float limit = MaxStackFor(kind);
            if (!r.Moving && !pair && kind != SupportKind.WallFlat)
            {
                bool large = IsLarge(r.Board.Def);
                foreach (AssemblyBoard b in existing)
                    large |= IsLarge(b.Instance.Def);
                if (large)
                {
                    res.Reason = AttachReason.Large;
                    return res;
                }
            }

            // At a height asked for (0.12.24), on posts and on a host: the boards there stay where they stand and the board takes the nearest free height; the posts of the support's own grow to it.
            // Among boards put at heights by hand a board that asks for none goes as near its own height as there is room: a stack would not keep clear of them.
            float asked = !float.IsNaN(r.Height) ? r.Height : Assembly.ByHand(kind, boards) ? r.Board.Def.BottomClearance : float.NaN;
            if (!float.IsNaN(asked) && !SupportKinds.OnWall(kind))
            {
                AssemblyPlan now = existing.Count > 0 ? Assembly.Plan(existing, support, r.Reach) : null;
                float highest = kind == SupportKind.Attached ? Assembly.HighestOnHost : Assembly.HighestOnPost;
                if (kind == SupportKind.Attached && r.HostHighest > 0f)
                    highest = Math.Min(highest, r.HostHighest - r.Board.Def.Height - r.Board.Def.ExtraTop);
                if (!AtHeight(r, res, boards, added, now, false, asked, Assembly.LowestOnPost, highest))
                    return res;
                List<AssemblyBoard> all = Assembly.Order(new List<AssemblyBoard>(boards) { added });
                float boardsBefore = existing.Count == 0 ? 0f : Assembly.TotalDef(existing, false).Height, boardsAfter = Assembly.TotalDef(all, false).Height;
                res.Boards = all;
                res.Count = all.Count;
                res.SpaceBefore = Math.Max(0f, limit - boardsBefore);
                res.SpaceLeft = Math.Max(0f, limit - boardsAfter);
                if (!r.Moving && !pair && boardsAfter > limit + 1e-4f)
                {
                    res.Reason = AttachReason.NoHeight;
                    return res;
                }

                res.Ok = true;
                return res;
            }

            // The order after: the player's (an index was asked for, or the stack already is in an order of the player's) or the standard's.
            List<AssemblyBoard> ordered;
            bool manual = r.Index >= 0 || manualGroup;
            if (manual)
            {
                ordered = new List<AssemblyBoard>(boards);
                int at = r.Index >= 0 ? Math.Min(r.Index, ordered.Count) : ordered.Count;
                ordered.Insert(at, added);
                for (int i = 0; i < ordered.Count; i++)
                {
                    ordered[i].Slot = i;
                    ordered[i].Manual = true;
                }

                res.Index = at;
            }
            else
            {
                var all = new List<AssemblyBoard>(boards) { added };
                ordered = Assembly.Order(all);
                res.Index = ordered.IndexOf(added);
            }

            float before = existing.Count == 0 ? 0f : Assembly.TotalDef(existing, false).Height;
            float height = Assembly.TotalDef(ordered, false).Height;
            res.Manual = manual;
            res.Boards = ordered;
            res.Count = ordered.Count;
            res.SpaceBefore = Math.Max(0f, limit - before);
            res.SpaceLeft = Math.Max(0f, limit - height);
            if (!r.Moving && !pair && height > limit + 1e-4f)
            {
                res.Reason = AttachReason.NoHeight;
                return res;
            }

            res.Ok = true;
            return res;
        }

        /// <summary>
        /// A small board on the post of a hung support (0.12.2): stacked with the others there in the standard's order, from the roadside clearance up; refused when it is large (as on any post) or when
        /// the stack would reach the lowest arm's or chord's joint. The boards on the arm or the beam do not move.
        /// </summary>
        private static AttachResult PostOfHung(AttachRequest r, AttachResult res, List<AssemblyBoard> existing, List<AssemblyBoard> boards, AssemblyBoard added, SupportDef support)
        {
            res.OnPost = true;
            if (!r.Moving && IsLarge(r.Board.Def))
            {
                res.Reason = AttachReason.Large;
                return res;
            }

            added.Lane = Assembly.OnPost;
            added.OffsetX = 0f;
            List<AssemblyBoard> ordered = Assembly.Order(new List<AssemblyBoard>(boards) { added });
            AssemblyPlan plan = Assembly.Plan(ordered, support, r.Reach);
            float room = PostRoom(plan.Layout);

            // At a height asked for (0.12.24): down to the ground, up to under the lowest arm's or chord's joint; the other boards on the post stay where they stand. Among boards put at heights by hand a board that
            // asks for none goes as near the foot of the stack as there is room.
            float asked = !float.IsNaN(r.Height) ? r.Height : boards.Exists(b => Assembly.IsPostBoard(b) && b.OffsetX > 0f) ? plan.Layout.PostClearance : float.NaN;
            if (!float.IsNaN(asked))
            {
                AssemblyPlan now = existing.Count > 0 ? Assembly.Plan(existing, support, r.Reach) : null;
                float highest = plan.Layout.PostTop - r.Board.Def.Height - r.Board.Def.ExtraTop;
                if (!AtHeight(r, res, boards, added, now, true, asked, Assembly.LowestOnPost, highest))
                    return res;
                ordered = Assembly.Order(new List<AssemblyBoard>(boards) { added });
                res.Boards = ordered;
                res.Count = ordered.FindAll(Assembly.IsPostBoard).Count;
                res.Reach = r.Reach;
                res.Span = plan.Layout.ArmLength;
                res.SpaceBefore = res.SpaceLeft = Math.Max(0f, highest - added.OffsetX);
                res.Ok = true;
                return res;
            }

            float before = Assembly.PostStackHeight(existing), height = Assembly.PostStackHeight(ordered);
            var onPost = ordered.FindAll(Assembly.IsPostBoard);
            res.Boards = ordered;
            res.Index = onPost.IndexOf(added);
            res.Count = onPost.Count;
            res.Reach = r.Reach;
            res.Span = plan.Layout.ArmLength;
            res.SpaceBefore = Math.Max(0f, room - before);
            res.SpaceLeft = Math.Max(0f, room - height);
            if (!r.Moving && height > room + 1e-4f)
            {
                res.Reason = AttachReason.NoHeight;
                return res;
            }

            res.Ok = true;
            return res;
        }

        private static AttachResult Beam(AttachRequest r, AttachResult res, List<AssemblyBoard> existing, List<AssemblyBoard> boards, AssemblyBoard added, SupportDef support)
        {
            // Where the boards stand now: the ones that are side by side are pinned where they are (the new board must not make them move), the ones over a lane or by hand stay. The boards on the
            // post take no room on the arm or the beam.
            AssemblyPlan now = existing.Count > 0 ? Assembly.Plan(existing, support, r.Reach) : null;
            var others = new List<KeyValuePair<float, float>>();
            added.Back = r.Back && support.Kind == SupportKind.Gantry;
            for (int i = 0; i < boards.Count; i++)
            {
                AssemblyBoard b = boards[i];
                if (Assembly.IsPostBoard(b))
                    continue;
                float cx = now != null ? now.Placed[i].Cx : 0f;
                if (b.Lane == 0)
                {
                    b.Lane = LaneHand;
                    b.OffsetX = cx;
                }

                // The boards on the other face of a gantry are not in the way.
                if (Assembly.OnBack(support.Kind, b) == added.Back)
                    others.Add(new KeyValuePair<float, float>(cx, b.Instance.Def.Width));
            }

            added.Lane = LaneHand;
            added.Rise = Assembly.ClampRise(r.Rise);
            float half = r.Board.Def.Width * 0.5f;

            // The bounds come from the layout of the whole, which does not depend on where the new board is (but its width and height count).
            var trial = new List<AssemblyBoard>(boards);
            var probe = Copy(added);
            probe.OffsetX = 0f;
            trial.Add(probe);
            List<AssemblyBoard> trialOrder = Assembly.Order(trial);
            AssemblyPlan plan = Assembly.Plan(trialOrder, support, r.Reach);
            float beforeSpan = now != null ? now.Layout.ArmLength : plan.Layout.ArmLength;
            float lo, hi;
            Assembly.LaneBounds(plan.Layout, half, out lo, out hi);

            // The longest the beam can be (the longest span): how far a board could stand then. The beam is made longer for the board when the board is taken past the end (or there is no room
            // at all in the length it has): an arm whenever it is asked, a gantry only when the player asked for it (Grow) or has no room left.
            AssemblyPlan longest = Assembly.Plan(trialOrder, support, SupportLayouts.MaxSpan);
            Assembly.LaneBounds(longest.Layout, half, out _, out float hiMax);
            if (hiMax < hi)
                hiMax = hi;
            if (hiMax < lo)
                hiMax = lo;
            res.Span = plan.Layout.ArmLength;

            float width = r.Board.Def.Width;
            bool twin = support.Kind == SupportKind.DoubleCantilever;

            // A double cantilever is two arms that meet at the post: a board's place along its arm is |x| and its arm is the sign of x. Each arm has its own free places and its own limit
            // (four boards). The signed places are what comes out, so the arrow keys step over the post from one arm to the other. A single arm reaches the way of its layout (ArmSide).
            float[] armSides = twin ? new[] { 1f, -1f } : new[] { plan.Layout.ArmSide };
            var armOthers = new List<KeyValuePair<float, float>>[] { new List<KeyValuePair<float, float>>(), new List<KeyValuePair<float, float>>() };
            foreach (KeyValuePair<float, float> o in others)
            {
                int arm = twin && o.Key < 0f ? 1 : 0;
                armOthers[arm].Add(new KeyValuePair<float, float>(o.Key * armSides[arm], o.Value));
            }

            float usable = hiMax - lo + width;
            float roomBefore = 0f, roomLeft = 0f;
            for (int arm = 0; arm < (twin ? 2 : 1); arm++)
            {
                float used = 0f;
                foreach (KeyValuePair<float, float> o in armOthers[arm])
                    used += o.Value + Assembly.SideGap;
                roomBefore = Math.Max(roomBefore, Math.Max(0f, usable - used));
                roomLeft = Math.Max(roomLeft, Math.Max(0f, usable - used - width - Assembly.SideGap));
            }

            res.SpaceBefore = roomBefore;
            res.SpaceLeft = roomLeft;

            List<BeamSpan> FreePlaces(float top)
            {
                var all = new List<BeamSpan>();
                for (int arm = 0; arm < (twin ? 2 : 1); arm++)
                {
                    if (twin && armOthers[arm].Count >= Assembly.MaxBoardsPerArm)
                        continue;
                    foreach (BeamSpan span in FreeIntervals(lo, top, armOthers[arm], width, Assembly.SideGap))
                        all.Add(armSides[arm] > 0f ? span : new BeamSpan(-span.Hi, -span.Lo));
                }

                all.Sort((p, q) => p.Lo.CompareTo(q.Lo));
                return all;
            }

            List<BeamSpan> freeNow = FreePlaces(hi);
            bool extend = SupportKinds.Arm(support.Kind) || r.Grow || freeNow.Count == 0;
            res.Free = extend ? FreePlaces(hiMax) : freeNow;
            res.Boards = trialOrder;
            res.Count = boards.Count + 1;
            if (res.Free.Count == 0)
            {
                res.Reason = AttachReason.NoSpace;
                return res;
            }

            // Nothing asked for: the middle of the arm, on a double cantilever the one that has fewer boards (the left one when equal).
            float wanted = float.IsNaN(r.Offset) ? (lo + hi) * 0.5f * (twin && armOthers[1].Count < armOthers[0].Count ? -1f : armSides[0]) : r.Offset;
            float x = Nearest(res.Free, wanted);
            added.OffsetX = x;
            res.Offset = x;
            float end = support.Kind == SupportKind.Gantry ? Assembly.GantryClear : -Assembly.ArmTipInside;
            float need = Math.Abs(x) + half + end;
            res.Reach = need > plan.Layout.ArmLength + 1e-3f ? Math.Min(SupportLayouts.MaxSpan, (float)Math.Ceiling(need * 2f) / 2f) : r.Reach;

            var final = new List<AssemblyBoard>(boards) { added };
            res.Boards = Assembly.Order(final);
            res.Span = Assembly.Plan(res.Boards, support, res.Reach).Layout.ArmLength;
            res.Grew = res.Span > beforeSpan + 1e-3f;
            res.Ok = true;
            return res;
        }
    }
}
