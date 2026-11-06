using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The boards moved up and down a post by hand (0.12.24, AssemblyBoard.OffsetX the lower edge; the owner: "在竖杆上放置的时候应该可以按上下方向键调整位置，最低可以到地面高度。如果当前是就是一个单竖杆，则自动长度调节"):
    /// the posts of the support's own grow and shrink with them, the others stay where they stand, a wall keeps its stack.
    /// </summary>
    internal static class HeightTests
    {
        public static void RunAll()
        {
            Program.Run("heights: a lone board and the length of its post", LonePost);
            Program.Run("heights: a board put among the others", AmongOthers);
            Program.Run("heights: the bounds", Bounds);
            Program.Run("heights: a board with no height among boards by hand", Mixed);
            Program.Run("heights: on the post of a cantilever", OnCantileverPost);
            Program.Run("heights: walls keep their stacks", Walls);
            Program.Run("heights: built and drawn", Built);
            Program.Run("heights: the lines of help", Hints);
        }

        private static SignDef Make(string id, string group, float w, float h, ShapeKind shape = ShapeKind.RoundRect) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = group, Shape = shape, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Small = Make("h.small", "prohibition", 0.8f, 0.8f, ShapeKind.Circle), Warn = Make("h.warn", "warning", 0.9f, 0.8f, ShapeKind.Triangle), Plate = Make("h.plate", "guide", 1.2f, 0.5f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, float lowerEdge = 0f, int lane = 0) => new AssemblyBoard { Instance = Inst(def), Member = member, Slot = member, Lane = lane, OffsetX = lowerEdge };

        private static AttachResult At(List<AssemblyBoard> existing, SignDef add, SupportDef support, float height, bool onPost = false, int member = -1, bool moving = false) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Board = Inst(add), Height = height, OnPost = onPost, Member = member, Moving = moving });

        private static SupportDef S(string id) => SupportCatalog.ById(id);

        private static Placement Of(AssemblyPlan plan, int member) => plan.Placed.Find(p => p.Board.Member == member);

        /// <summary>No two boards of the list on one post (or both off it) overlap in height.</summary>
        private static bool Clear(IReadOnlyList<Placement> placed)
        {
            for (int i = 0; i < placed.Count; i++)
            {
                for (int j = i + 1; j < placed.Count; j++)
                {
                    if (placed[i].OnPost != placed[j].OnPost)
                        continue;
                    if (Math.Min(placed[i].Top + placed[i].Def.ExtraTop, placed[j].Top + placed[j].Def.ExtraTop) - Math.Max(placed[i].Bottom, placed[j].Bottom) > 1e-3f)
                        return false;
                }
            }

            return true;
        }

        private static void LonePost()
        {
            SupportDef pole = S(SupportCatalog.SinglePole);
            AssemblyPlan stacked = Assembly.Plan(new List<AssemblyBoard> { B(Small, 0) }, pole, 0f);
            Program.Check(!Assembly.ByHand(pole.Kind, new List<AssemblyBoard> { B(Small, 0) }), "a board with no height of its own is in the stack");
            Program.Near(Of(stacked, 0).Bottom, Small.BottomClearance, 1e-5f, "at the standard's clearance");

            AssemblyPlan low = Assembly.Plan(new List<AssemblyBoard> { B(Small, 0, Assembly.LowestOnPost) }, pole, 0f);
            Program.Near(Of(low, 0).Bottom, Assembly.LowestOnPost, 1e-5f, "down to the ground");
            Program.Near(low.Layout.PoleTop, SupportLayouts.PostTopFor(Assembly.LowestOnPost + Small.Height * 0.5f, Small.Height), 1e-4f, "and the post as short as the board needs: " + low.Layout.PoleTop);

            AssemblyPlan high = Assembly.Plan(new List<AssemblyBoard> { B(Small, 0, 4.5f) }, pole, 0f);
            Program.Near(Of(high, 0).Bottom, 4.5f, 1e-5f, "up to 4.5 m");
            Program.Near(high.Layout.PoleTop - low.Layout.PoleTop, 4.5f - Assembly.LowestOnPost, 1e-4f, "and the post grows by as much, as the arm does for a board past its end");
            Program.Check(high.Support.Kind == SupportKind.SinglePole && high.Layout.PoleX.Length == 1, "still one post");
        }

        private static void AmongOthers()
        {
            SupportDef pole = S(SupportCatalog.SinglePole);
            var stack = new List<AssemblyBoard> { B(Small, 0), B(Warn, 1) };
            AssemblyPlan before = Assembly.Plan(Assembly.Order(stack), pole, 0f);

            AttachResult low = At(stack, Plate, pole, 0.2f);
            Program.Check(low.Ok, "a plate under the stack: " + low.Reason);
            AssemblyPlan after = Assembly.Plan(low.Boards, pole, 0f);
            Program.Near(Of(after, low.NewMember).Bottom, 0.2f, 1e-4f, "where it was asked");
            Program.Near(Of(after, 0).Bottom, Of(before, 0).Bottom, 1e-4f, "the others stay where they stood");
            Program.Near(Of(after, 1).Bottom, Of(before, 1).Bottom, 1e-4f, "both of them");
            Program.Check(Clear(after.Placed), "nothing overlaps");
            Program.Check(low.Index == 2, "below both: " + low.Index);

            // Asked where a board is, it is put at the nearest free height, and the free heights jump over the boards.
            Placement lower = Of(before, 1).Bottom < Of(before, 0).Bottom ? Of(before, 1) : Of(before, 0);
            AttachResult into = At(stack, Plate, pole, lower.Bottom + 0.1f);
            Program.Check(into.Ok && Clear(Assembly.Plan(into.Boards, pole, 0f).Placed), "asked inside a board it goes beside it");
            float up = Attach.Move(into.Free, into.Offset, 1, Attach.HeightStep);
            Program.Check(up > into.Offset, "up from there");
            float far = into.Offset;
            for (int i = 0; i < 80; i++)
                far = Attach.Move(into.Free, far, 1, Attach.HeightStep);
            AttachResult top = At(stack, Plate, pole, far);
            AssemblyPlan atTop = Assembly.Plan(top.Boards, pole, 0f);
            Program.Check(top.Ok && Of(atTop, top.NewMember).Bottom > Of(atTop, 0).Top && Of(atTop, top.NewMember).Bottom > Of(atTop, 1).Top && Clear(atTop.Placed), "and up past both, clear of them");
            Program.Check(atTop.Layout.PoleTop > before.Layout.PoleTop + Plate.Height, "the post grew to carry it");
        }

        private static void Bounds()
        {
            SupportDef pole = S(SupportCatalog.SinglePole), host = S(SupportCatalog.AttachedPole);
            AttachResult ground = At(new List<AssemblyBoard>(), Small, pole, -2f);
            Program.Check(ground.Ok && Math.Abs(ground.Offset - Assembly.LowestOnPost) < 1e-4f, "no lower than just above the foot: " + ground.Offset);
            AttachResult sky = At(new List<AssemblyBoard>(), Small, pole, 30f);
            Program.Check(sky.Ok && sky.Offset <= Assembly.HighestOnPost + 1e-4f, "no higher than a post of the support's own goes: " + sky.Offset);
            AttachResult hosted = At(new List<AssemblyBoard>(), Small, host, 30f);
            Program.Check(hosted.Ok && hosted.Offset <= Assembly.HighestOnHost + 1e-4f, "nor than on the game's lamp post: " + hosted.Offset);
            Program.Near(Attach.HeightStep, 0.1f, 1e-6f, "a press is 0.1 m");
            Program.Near(Attach.HeightStepCoarse, 0.5f, 1e-6f, "with Shift 0.5 m");

            // The boards a post carries are as many metres by hand as in the stack (Attach.MaxPostStack): the gaps between them do not count.
            SignDef metre = Make("h.metre", "mandatory", 0.8f, 1.0f);
            var three = new List<AssemblyBoard> { B(metre, 0, 0.3f), B(metre, 1, 1.5f), B(metre, 2, 2.7f) };
            AttachResult over = At(three, Small, pole, 4.2f);
            Program.Check(!over.Ok && over.Reason == AttachReason.NoHeight, "3.8 m of boards is more than a post carries: " + over.Reason);
            AttachResult spaced = At(new List<AssemblyBoard> { B(metre, 0, 0.3f), B(metre, 1, 3.0f) }, Small, pole, 5f);
            Program.Check(spaced.Ok, "2.8 m is not, however far apart they stand: " + spaced.Reason);
        }

        private static void Mixed()
        {
            SupportDef pole = S(SupportCatalog.SinglePole);
            // One board put low by hand; the next asks for no height: it does not go where the first is.
            var byHand = new List<AssemblyBoard> { B(Small, 0, SupportCatalog.RoadsideClearance) };
            Program.Check(Assembly.ByHand(pole.Kind, byHand), "a board at a height of its own");
            AttachResult next = Attach.Evaluate(new AttachRequest { Existing = byHand, Support = pole, Board = Inst(Warn) });
            Program.Check(next.Ok, "the next one goes on: " + next.Reason);
            AssemblyPlan plan = Assembly.Plan(next.Boards, pole, 0f);
            Program.Check(Clear(plan.Placed), "clear of it");
            Program.Near(Of(plan, 0).Bottom, SupportCatalog.RoadsideClearance, 1e-4f, "which stays");
            AssemblyBoard added = next.Boards.Find(b => b.Member == next.NewMember);
            Program.Check(added != null && added.OffsetX > 0f, "the new one has a height of its own too");
        }

        private static void OnCantileverPost()
        {
            SupportDef arm = S(SupportCatalog.Cantilever);
            var boards = new List<AssemblyBoard> { B(Plate, 0) };
            AttachResult low = At(boards, Small, arm, 0.2f, true);
            Program.Check(low.Ok && low.OnPost, "a small board low on the post: " + low.Reason);
            AssemblyPlan plan = Assembly.Plan(low.Boards, arm, 0f);
            Placement onPost = Of(plan, low.NewMember);
            Program.Check(onPost.OnPost, "on the post");
            Program.Near(onPost.Bottom, 0.2f, 1e-4f, "at the height asked");
            AssemblyPlan plain = Assembly.Plan(Assembly.Order(boards), arm, 0f);
            Program.Near(Of(plan, 0).Bottom, Of(plain, 0).Bottom, 1e-4f, "the board on the arm does not move");

            AttachResult high = At(boards, Small, arm, 30f, true);
            Placement under = Of(Assembly.Plan(high.Boards, arm, 0f), high.NewMember);
            Program.Check(high.Ok && under.Top + Small.ExtraTop <= plan.Layout.PostTop + 1e-3f, "no higher than under the lowest arm: " + under.Top + " / " + plan.Layout.PostTop);

            // Two on the post: the second goes clear of the first, wherever it was put.
            var two = new List<AssemblyBoard>(low.Boards);
            AttachResult second = Attach.Evaluate(new AttachRequest { Existing = two, Support = arm, Board = Inst(Warn), OnPost = true });
            Program.Check(second.Ok && Clear(Assembly.Plan(second.Boards, arm, 0f).Placed), "a second one clear of it: " + second.Reason);
        }

        private static void Walls()
        {
            SupportDef wall = S(SupportCatalog.WallFlat);
            var stack = new List<AssemblyBoard> { B(Small, 0) };
            AttachResult r = At(stack, Warn, wall, 0.3f);
            Program.Check(r.Ok, "a wall takes the board: " + r.Reason);
            Program.Check(r.Boards.TrueForAll(b => b.OffsetX == 0f), "in its stack, with no height of its own");
            Program.Check(!Assembly.ByHand(SupportKind.WallFlat, new List<AssemblyBoard> { B(Small, 0, 1f) }) && !Assembly.ByHand(SupportKind.Cantilever, new List<AssemblyBoard> { B(Small, 0, 1f) }),
                "a wall and a beam never by hand (on a beam OffsetX is where the board is along it)");
        }

        private static void Built()
        {
            SupportDef pole = S(SupportCatalog.SinglePole);
            SignDef sign = SignCatalog.ById("gb.prohib.09");
            SignParts low = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = SignInstances.Of(sign, null), OffsetX = 0.3f } }, pole, 0f);
            SignParts std = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = SignInstances.Of(sign, null) } }, pole, 0f);
            low.Faces[0].Mesh.Bounds(out Vec3 fMin, out _);
            Program.Near(fMin.Y, 0.3f, 0.02f, "the face's lower edge where it was put: " + fMin.Y);
            low.Metal.Bounds(out _, out Vec3 lowTop);
            std.Metal.Bounds(out _, out Vec3 stdTop);
            Program.Check(lowTop.Y < stdTop.Y - 1f, "the post is shorter with it: " + lowTop.Y + " / " + stdTop.Y);

            // A placed item at a height of its own is drawn with it (not from the cache of the plain sign).
            var record = new ItemRecord(sign.Hash, Vec3.Zero, 0f, pole.Hash, null, 0f, 0u, 0, 0, 0.3f);
            SignParts drawn = ChunkBuilder.PartsOfRecord(record);
            drawn.Faces[0].Mesh.Bounds(out Vec3 dMin, out _);
            Program.Near(dMin.Y, fMin.Y, 1e-4f, "a placed item at its height");
            var plain = new ItemRecord(sign.Hash, Vec3.Zero, 0f, pole.Hash);
            ChunkBuilder.PartsOfRecord(plain).Faces[0].Mesh.Bounds(out Vec3 pMin, out _);
            Program.Check(pMin.Y > 1f, "and one with no height at the standard's: " + pMin.Y);
        }

        private static void Hints()
        {
            foreach (bool zh in new[] { true, false })
            {
                string place = HintText.Of(new HintInfo { Kind = "place", X = 0.35f }, zh);
                Program.Check(place.Contains("0.35"), "place: the keys and the lower edge: " + place);
                string host = HintText.Of(new HintInfo { Kind = "host", X = 1.2f, Host = "lamp" }, zh);
                Program.Check(host.Contains("1.20") && (!zh || !host.Contains("立杆随之")), "host: the lower edge, the game's post does not grow: " + host);
                string attach = HintText.Of(new HintInfo { Kind = "attach", Count = 2, Left = 0.6f, X = 1.1f, Grew = true }, zh);
                Program.Check(attach.Contains("1.10") && !attach.Contains(zh ? "顺序" : "order"), "attach on a post: up and down, not the order: " + attach);
                string wall = HintText.Of(new HintInfo { Kind = "attach", Count = 2, Left = 0.6f }, zh);
                Program.Check(wall.Contains(zh ? "顺序" : "order"), "on a wall still the order: " + wall);
                string onPost = HintText.Of(new HintInfo { Kind = "attach-post", Count = 1, Left = 0.6f, X = 0.5f }, zh);
                Program.Check(onPost.Contains("0.50"), "on the post of a hung support: " + onPost);
                string move = HintText.Of(new HintInfo { Kind = "move", Count = 1, X = 2.25f, Grew = true }, zh);
                Program.Check(move.Contains("2.25") && move.StartsWith(zh ? "编辑：" : "Editing: "), "editing a board on a post, even alone on it: " + move);
            }
        }
    }
}
