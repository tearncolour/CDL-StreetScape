using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// 杭州框架式悬臂 (0.12.57, SupportLayout.Framed; Hangzhou 2015 guide 5.7 "悬臂式标志应采用框架式结构形式，标志板背面为漆黑色", fig. A.1; photos c8, c9, c32 in
        /// scratch/research-supports-urban): each board in a dark box frame, hung by two rods from the upper arm, which runs over the boards past their outer edge and through a clamp on the post
        /// to beyond it; a lower arm from beyond the post to the frame's edge at the post's side, at the frame's middle height. No channels: the frame holds the board.
        /// </summary>
        private static void FramedArms(SignParts parts, SupportLayout layout, List<Placement> placed)
        {
            float side = layout.ArmSide, r = layout.PoleRadius, armR = layout.ArmRootRadius, y = layout.ArmY, length = layout.ArmLength;
            float behind = SupportLayouts.FrameBeyondPole * length, lowerBehind = SupportLayouts.LowerBeyondPole * length;
            Solids.Tube(parts.Metal, new Vec3(-side * behind, y, 0f), new Vec3(side * length, y, 0f), armR, armR, 12, true, true);
            Clamp(parts.Metal, r, y, armR);
            float nearEdge = float.MaxValue;
            foreach (Placement p in placed)
            {
                if (p.OnPost)
                    continue;
                float w = p.Def.Width, h = p.Def.Height, half = w * 0.5f + SupportLayouts.FrameFace, top = p.Top + SupportLayouts.FrameFace;
                int from = parts.Metal.VertexCount;
                MeshBuilder frame = FrameOf(parts);
                int frameFrom = frame.VertexCount;
                float d = SupportLayouts.FrameDepth * 0.5f, f = SupportLayouts.FrameFace * 0.5f;
                Solids.Box(frame, new Vec3(p.Cx, top - f, 0f), new Vec3(half, f, d));
                Solids.Box(frame, new Vec3(p.Cx, p.Bottom - f, 0f), new Vec3(half, f, d));
                foreach (float s in new[] { -1f, 1f })
                    Solids.Box(frame, new Vec3(p.Cx + s * (half - f), p.Cy, 0f), new Vec3(f, h * 0.5f, d));

                // The hangers: from the arm's underside to the frame's top, one in from each edge (fig. A.1, photo c9), on a small plate; over a plate fixed on top of the board, from the plate's top.
                float outer = p.Cx + side * w * 0.5f, inner = p.Cx - side * w * 0.5f;
                foreach (float x in new[] { outer - side * SupportLayouts.HangerInOuter * w, inner + side * SupportLayouts.HangerInInner * w })
                {
                    float foot = Math.Max(top, TopPlateAt(p, x));
                    Solids.Tube(parts.Metal, new Vec3(x, foot, 0f), new Vec3(x, y - armR * 0.5f, 0f), SupportLayouts.HangerRadius, SupportLayouts.HangerRadius, 8, false, false);
                    Solids.Box(parts.Metal, new Vec3(x, foot + 0.004f, 0f), new Vec3(0.05f, 0.004f, d));
                }

                nearEdge = Math.Min(nearEdge, Math.Abs(inner) - SupportLayouts.FrameFace);
                TurnedOf(parts, p)?.Since(parts.Metal, from);
                TurnedOf(parts, p)?.Since(frame, frameFrom);
            }

            if (nearEdge < float.MaxValue)
            {
                float lowY = layout.LowerArmY;
                Solids.Tube(parts.Metal, new Vec3(-side * lowerBehind, lowY, 0f), new Vec3(side * (nearEdge + 0.01f), lowY, 0f), armR * 0.85f, armR * 0.85f, 12, true, true);
                Clamp(parts.Metal, r, lowY, armR * 0.85f);
                float pickHalf = Math.Max(0.03f, armR - 0.02f);
                parts.Picks.Add(new PickShape { Name = "arm", Min = new Vec3(Math.Min(-side * lowerBehind, side * nearEdge), lowY - armR, -pickHalf), Max = new Vec3(Math.Max(-side * lowerBehind, side * nearEdge), lowY + armR, pickHalf) });
            }
        }

        /// <summary>The top of a plate fixed on top of a board (a guide board's part after its first) over x in the sign's frame, 2 cm round it; float.MinValue where there is none.</summary>
        private static float TopPlateAt(Placement p, float x)
        {
            GuideBoard board = p.Board.Instance.Board;
            float top = float.MinValue;
            for (int i = 1; board != null && i < board.Parts.Count; i++)
            {
                GuidePart part = board.Parts[i];
                float cx = p.Cx + (board.Main.W * 0.5f - (part.X + part.W * 0.5f)), cy = p.Cy + (board.Main.H * 0.5f - (part.Y + part.H * 0.5f));
                if (Math.Abs(x - cx) <= part.W * 0.5f + 0.02f && cy > p.Cy)
                    top = Math.Max(top, cy + part.H * 0.5f);
            }

            return top;
        }

        /// <summary>The frames are black (5.7: the board's back black; photos: the frames dark), the steel's when it is graphite.</summary>
        private static MeshBuilder FrameOf(SignParts parts) => parts.MetalColour == SolidColour.Graphite ? parts.Metal : parts.Solid(SolidColour.Graphite);

        /// <summary>The clamp of an arm through the post at height y (photo c9: a box round the joint; 0.25 tall, as wide as the post and 4 cm over it each way, an estimate).</summary>
        private static void Clamp(MeshBuilder metal, float postR, float y, float armR) =>
            Solids.Box(metal, new Vec3(0f, y, 0f), new Vec3(postR + 0.04f, Math.Max(0.125f, armR + 0.04f), postR + 0.04f));
    }
}
