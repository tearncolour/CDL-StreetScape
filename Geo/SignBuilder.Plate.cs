using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// A street-name plate on its post's head (0.12.57, Placement.OnHead): the plate PlateHead.PlateDepth thick with its plane through the post's axis, printed on both faces (the back mapped so that it
        /// reads the right way from behind, as a lamp-post ad's), its edge in the steel's colour (only plain boards go there: PlateHead.Fits).
        /// </summary>
        private static void AddPlateBoard(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            int member = placement.Board.Member;
            MeshBuilder face = parts.AddFace(instance);
            float z1 = layout.Head.PlateDepth * 0.5f, z0 = -z1;
            GuideBoard board = instance.Board;
            GuidePart main = board.Main;
            float boxW = instance.BoundsX1 - instance.BoundsX0, boxH = instance.BoundsY1 - instance.BoundsY0;
            for (int i = 0; i < board.Parts.Count; i++)
            {
                GuidePart part = board.Parts[i];
                float cx = placement.Cx + (main.W * 0.5f - (part.X + part.W * 0.5f));
                float cy = placement.Cy + (main.H * 0.5f - (part.Y + part.H * 0.5f));
                List<Point2> outline = GuideKit.PartOutline(part);
                List<Point2> inFrame = MirrorX(outline);
                float centreX = part.X + part.W * 0.5f, centreY = part.Y + part.H * 0.5f, x0 = instance.BoundsX0, y0 = instance.BoundsY0;
                Func<float, float, (float u, float v)> uv = (px, py) => ((centreX + px - x0) / boxW, 1f - (centreY - py - y0) / boxH);
                Func<float, float, (float u, float v)> back = (px, py) => uv(-px, py);
                if (part.Cut != null)
                {
                    Prims.LoopsFace(face, part.Cut, z1, true, cx, cy, uv);
                    Prims.LoopsFace(face, part.Cut, z0, false, cx, cy, uv);
                    Prims.LoopsRim(BackOf(parts), part.Cut, z0, z1, cx, cy);
                }
                else
                {
                    Prims.PolygonFaceMapped(face, outline, z1, true, cx, cy, uv);
                    Prims.PolygonFaceMapped(face, outline, z0, false, cx, cy, back);
                    Prims.Rim(BackOf(parts), inFrame, z0, z1, cy, cx);
                }

                parts.Picks.Add(BoardPick(inFrame, cx, cy, part.W, part.H, z0, member, z1, false));
            }
        }

        /// <summary>
        /// A street-name plate post (0.12.57, SupportLayout.Head): the post from under the paving (its flange buried: Xuzhou 6.2.2, Hangzhou fig. B.1) up into the head under the plate, round or
        /// fluted; the head (PlateHeadMesh); the picks of the post, the head and the foot. Returns nothing else to do: the boards under the head hang on the post as on any.
        /// </summary>
        private static void BuildPlatePost(SupportLayout layout, SignParts parts, Placement plate)
        {
            PlateHead head = layout.Head;
            float r = head.PostRadius, headTop = plate.Bottom, postTop = layout.PoleTop;
            var foot = new PostFoot { X = 0f, Z = 0f, PadHalf = r, PadBottom = PedestalBottom, MaxRaise = RaiseOf(layout), MetalFrom = parts.Metal.VertexCount, ConcreteFrom = parts.Concrete.VertexCount };
            foot.MetalTo = parts.Metal.VertexCount;
            foot.ConcreteTo = parts.Concrete.VertexCount;
            foot.Split = postTop * 0.5f;
            foot.TubeFrom = parts.Metal.VertexCount;
            bool fluted = head.Flutes > 0;
            if (fluted)
                Solids.Faceted(parts.Metal, new Vec3(0f, PedestalBottom, 0f), new Vec3(0f, postTop, 0f), r, r, head.Flutes, false, false);
            else
                Solids.Tube(parts.Metal, new Vec3(0f, PedestalBottom, 0f), new Vec3(0f, postTop, 0f), r, r, PoleSides, false, false);
            foot.TubeTo = parts.Metal.VertexCount;
            PaintRoot(parts, foot, r, r, 0f, postTop, fluted ? head.Flutes : PoleSides, fluted, RoadSide.Left);
            parts.Feet.Add(foot);
            PlateHeadMesh(parts.Metal, head, headTop);
            parts.Picks.Add(PostPick(0f, 0f, r, 0f, postTop));
            float a = head.Flare ? head.FlareTop : head.TopA, b = head.Flare ? head.FlareTop : head.TopB;
            parts.Picks.Add(new PickShape { Name = "post", Min = new Vec3(-a, headTop - head.Height, -b), Max = new Vec3(a, headTop, b) });
            parts.Picks.Add(new PickShape { Name = "foot", Min = new Vec3(-r - 0.05f, PedestalBottom, -r - 0.05f), Max = new Vec3(r + 0.05f, 0.04f, r + 0.05f) });
        }

        /// <summary>A plate's head with its top at top (PlateHead): the trays, the neck and the cone, or the flare (a trumpet: the radius growing with the square of the way up).</summary>
        private static void PlateHeadMesh(MeshBuilder metal, PlateHead h, float top)
        {
            float bottom = top - h.Height;
            if (h.Flare)
            {
                const int steps = 6;
                for (int i = 0; i < steps; i++)
                {
                    float t0 = (float)i / steps, t1 = (float)(i + 1) / steps;
                    Solids.Tube(metal, new Vec3(0f, bottom + h.Height * t0, 0f), new Vec3(0f, bottom + h.Height * t1, 0f), h.FlareBottom + (h.FlareTop - h.FlareBottom) * t0 * t0,
                        h.FlareBottom + (h.FlareTop - h.FlareBottom) * t1 * t1, 24, i == 0, i == steps - 1);
                }

                return;
            }

            float y = top;
            Oval(metal, h.TopA, h.TopB, y - h.TopThick, y);
            y -= h.TopThick;
            Solids.Tube(metal, new Vec3(0f, y - h.Neck, 0f), new Vec3(0f, y, 0f), h.ConeTop, h.ConeTop, 24, false, false);
            y -= h.Neck;
            Oval(metal, h.SecondA, h.SecondB, y - h.SecondThick, y);
            y -= h.SecondThick;
            Solids.Tube(metal, new Vec3(0f, bottom, 0f), new Vec3(0f, y, 0f), h.ConeBottom, h.ConeTop, 24, true, false);
        }

        /// <summary>An oval plate (half axes a along X, b along Z) from y0 to y1.</summary>
        private static void Oval(MeshBuilder metal, float a, float b, float y0, float y1)
        {
            const int sides = 24;
            var outline = new Vec3[sides];
            for (int i = 0; i < sides; i++)
            {
                double angle = 2.0 * Math.PI * i / sides;
                outline[i] = new Vec3(a * (float)Math.Cos(angle), (y0 + y1) * 0.5f, b * (float)Math.Sin(angle));
            }

            Solids.Slab(metal, outline, new Vec3(0f, (y1 - y0) * 0.5f, 0f));
        }
    }
}
