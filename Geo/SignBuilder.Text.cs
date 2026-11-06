using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        private const int TextStudSides = 6;

        /// <summary>
        /// A text light box on its support (0.12.75, Signs.TextBoxes, DESIGN-text-lightbox.md 6.2): each letter a solid, its lit face in the glow colour in front and its return (the back cap and the side
        /// wall) in its own colour, standing Stand in front of the plane the support gives (the rails' and the studs' length) and Depth deep; behind them the rear frame the plan worked out, studs
        /// from each letter back to the rails, rods from a stud to a rail where no rail crosses its island, the rails and the ties between lines, in the support's steel. On a wall there are no rails:
        /// the studs go straight to the wall. The plan is made on a worker: where it is not made yet (the main thread asked), each line is one lit bar (no return, no loops, no frame). In a far tile
        /// (0.12.105) the letters are their lit faces alone, flat at the face's plane: see AddTextFaces.
        /// </summary>
        private static void AddTextBoard(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            GuideBoard board = instance.Board;
            Signs.TextBoard text = board.Text;
            GuidePart main = board.Main;
            int member = placement.Board.Member;
            parts.AddFace(instance);
            if (TryAddTextLeaning(parts, layout, placement))
                return;
            float backZ = BackOf(layout, placement), zBack = backZ + text.Stand, zFront = zBack + text.Depth;
            float cx = placement.Cx, cy = placement.Cy;
            List<Point2> inFrame = MirrorX(GuideKit.PartOutline(main));
            parts.Picks.Add(BoardPick(inFrame, cx, cy, main.W, main.H, backZ, member, zFront, true, 0.01f));
            if (text.Layout == null || text.Layout.Glyphs.Count == 0)
                return;

            TextPlanResult plan = TextPlans.Get(text.Job);
            // On a curved wall (0.12.76) each letter is set on the arc and turned to its tangent; the layer the text's length is measured on is the face.
            ArcSurface surface = TextSurfaceOf(layout, text, zFront);
            if (t_Reduced)
            {
                if (!(surface.Flat && AddTextFaces(parts, text, plan, cx, cy, zFront)))
                {
                    parts.Provisional |= plan == null;
                    AddTextBars(parts, text, cx, cy, zFront);
                }

                return;
            }

            if (plan == null || plan.Frame == null || plan.TooHeavy)
            {
                // The main thread asked and the plan is being made on a worker: these parts stand in until it is there, and are not kept (ChunkBuilder).
                parts.Provisional |= plan == null;
                AddTextBars(parts, text, cx, cy, zFront);
                return;
            }

            MeshBuilder glow = parts.Solid(text.Glow), ret = parts.Solid(text.Return);
            TextLayoutResult placed = plan.Layout;
            for (int g = 0; g < plan.Meshes.Length; g++)
            {
                GlyphMesh mesh = plan.Meshes[g];
                if (mesh == null)
                    continue;
                PlacedGlyph glyph = placed.Glyphs[g];
                if (mesh.Emoji != null && surface.Flat)
                    mesh.AddEmojiTo(parts.Solid, ret, placed.MetresPerEm, glyph.X - cx, cy + glyph.Y, zBack, zFront, true, glyph.Angle);
                else if (surface.Flat)
                    mesh.AddTo(glow, ret, placed.MetresPerEm, glyph.X - cx, cy + glyph.Y, zBack, zFront, true, glyph.Angle);
                else
                    AddTextGlyphOnArc(glow, ret, mesh, placed.MetresPerEm, glyph, surface, cx, cy, zBack, zFront);
            }

            AddTextRear(parts, layout, placement, plan, text, surface, cx, cy, backZ, zBack);
        }

        /// <summary>The most vertices the lit faces of one text may have in a far tile (0.12.105); over it the text is the bars. A text of 7 characters is some 700 of them, a long line of a few dozen 3,000.</summary>
        public const int MaxFarTextVertices = 4000;

        /// <summary>Whether the lit faces of a text, this many vertices, are within what a far tile takes (MaxFarTextVertices, the limit itself included).</summary>
        internal static bool FarFacesFit(int vertices) => vertices <= MaxFarTextVertices;

        /// <summary>The vertices of the lit faces of a text's letters (the flat cap of each: what a far tile draws).</summary>
        internal static int TextFaceVertices(TextPlanResult plan)
        {
            int vertices = 0;
            foreach (GlyphMesh mesh in plan.Meshes)
            {
                if (mesh != null)
                    vertices += mesh.Points.Length;
            }

            return vertices;
        }

        /// <summary>
        /// What a far tile draws of a text light box (0.12.105; the owner: "字体灯箱好像没有遵循这个距离显示，离远了还是变成一个条了": past the middle tier's 250 m every line was one bar, whatever the draw distance set
        /// was): each letter's lit face alone, flat at the face's plane, at its place in the plan: no return, no studs, no frame, no shadow (a far tile has none), so a letter costs the triangles of its face.
        /// False, and the caller draws the bars, where that is not possible or not worth it: where the near tile has bars too (no plan, because the main thread asked; one with no frame; one that is too
        /// heavy), a text on an arc (the letters are turned to it), and faces over MaxFarTextVertices. The plan is the near tile's (cached by the text's job): a far tile asks for it on its worker, which
        /// makes it where it is not there. A text no font can set has no faces, in a far tile as in a near one.
        /// </summary>
        private static bool AddTextFaces(SignParts parts, Signs.TextBoard text, TextPlanResult plan, float cx, float cy, float zFront)
        {
            if (plan == null || plan.Frame == null || plan.TooHeavy || plan.Layout.Arc != null || !FarFacesFit(TextFaceVertices(plan)))
                return false;
            MeshBuilder glow = parts.Solid(text.Glow);
            for (int g = 0; g < plan.Meshes.Length; g++)
            {
                if (plan.Meshes[g] != null)
                    AddGlyphFace(glow, plan.Meshes[g], plan.Layout.MetresPerEm, cx, cy, plan.Layout.Glyphs[g], zFront);
            }

            return true;
        }

        /// <summary>One lit bar for each line of the text, flat at the face's plane: what a far tile draws where its letters are not (AddTextFaces), and what stands in until the plan is made.</summary>
        private static void AddTextBars(SignParts parts, Signs.TextBoard text, float cx, float cy, float zFront)
        {
            TextLayoutResult layout = text.Layout;
            MeshBuilder glow = parts.Solid(text.Glow);
            if (layout.Arc != null)
            {
                AddArcBars(glow, layout, cx, cy, zFront);
                return;
            }

            float height = layout.LetterHeight, pitch = layout.Lines > 1 ? (layout.Height - height) / (layout.Lines - 1) : 0f, bar = height * 0.6f, widest = layout.Width;
            for (int line = 0; line < layout.Lines; line++)
            {
                float width = layout.LineWidths[line];
                if (width <= 0f)
                    continue;
                float x0 = -widest * 0.5f + (text.Job.Align == TextAlign.Centre ? (widest - width) * 0.5f : text.Job.Align == TextAlign.Right ? widest - width : 0f);
                float x = x0 + width * 0.5f, y = layout.Height * 0.5f - line * pitch - height * 0.5f;
                Solids.Box(glow, new Vec3(cx - x, cy + y, zFront - 0.003f), new Vec3(width * 0.5f, bar * 0.5f, 0.003f));
            }
        }

        /// <summary>One lit bar for each line of a text on an arc: a ring of the line's circle, in pieces of at most a few degrees, as high as the straight bars are.</summary>
        private static void AddArcBars(MeshBuilder glow, TextLayoutResult layout, float cx, float cy, float zFront)
        {
            ArcInfo arc = layout.Arc;
            TextLayoutResult straight = arc.Straight;
            float height = straight.LetterHeight, pitch = straight.Lines > 1 ? (straight.Height - height) / (straight.Lines - 1) : 0f, bar = height * 0.3f;
            for (int line = 0; line < straight.Lines; line++)
            {
                float width = straight.LineWidths[line];
                if (width <= 0f)
                    continue;
                float x0 = float.MaxValue, x1 = float.MinValue, y = straight.Height * 0.5f - line * pitch - height * 0.5f;
                foreach (PlacedGlyph glyph in straight.Glyphs)
                {
                    if (glyph.Line != line)
                        continue;
                    x0 = Math.Min(x0, glyph.X);
                    x1 = Math.Max(x1, glyph.X + glyph.Advance);
                }

                int pieces = Math.Max(1, (int)Math.Ceiling((x1 - x0) / arc.RadiusAt(y) / (10f * (float)(Math.PI / 180.0))));
                var corners = new Vec3[4];
                for (int i = 0; i < pieces; i++)
                {
                    float xa = x0 + (x1 - x0) * i / pieces, xb = x0 + (x1 - x0) * (i + 1) / pieces;
                    Point2 a0 = arc.Bend(xa, y - bar), a1 = arc.Bend(xa, y + bar), b1 = arc.Bend(xb, y + bar), b0 = arc.Bend(xb, y - bar);
                    corners[0] = new Vec3(cx - a0.X, cy + a0.Y, zFront - 0.003f);
                    corners[1] = new Vec3(cx - b0.X, cy + b0.Y, zFront - 0.003f);
                    corners[2] = new Vec3(cx - b1.X, cy + b1.Y, zFront - 0.003f);
                    corners[3] = new Vec3(cx - a1.X, cy + a1.Y, zFront - 0.003f);
                    Solids.Slab(glow, corners, new Vec3(0f, 0f, 0.003f));
                }
            }
        }

        /// <summary>
        /// The rear frame (FramePlan, in metres from the middle of the text, x as the viewer sees it): the studs from the letters' backs to the rails' plane (or the wall), the rods from stud to rail, the
        /// rails and the ties. On a wall only the studs are built.
        /// </summary>
        private static void AddTextFrame(SignParts parts, FramePlan frame, FrameStyle style, bool wall, float cx, float cy, float backZ, float zBack)
        {
            MeshBuilder metal = parts.Metal;
            float studR = Math.Max(0.0015f, style.StudRadius);
            foreach (FrameStud stud in frame.Studs)
                Solids.Tube(metal, new Vec3(cx - stud.At.X, cy + stud.At.Y, backZ), new Vec3(cx - stud.At.X, cy + stud.At.Y, zBack + 0.004f), studR, studR, TextStudSides, false, true);
            if (wall)
                return;
            float size = Math.Max(0.006f, style.RailSize), zRail = backZ + size * 0.5f;
            foreach (FrameSegment rod in frame.Bridges)
                TextRod(metal, rod, cx, cy, zRail, studR);
            foreach (FrameSegment rail in frame.Rails)
                TextRod(metal, rail, cx, cy, zRail, size * 0.5f);
            foreach (FrameSegment tie in frame.Ties)
                TextRod(metal, tie, cx, cy, zRail, size * 0.5f);
        }

        /// <summary>A straight piece of the frame at one z: a box when it runs along x or y, a tube when it is oblique.</summary>
        private static void TextRod(MeshBuilder metal, FrameSegment segment, float cx, float cy, float z, float halfSize)
        {
            float ax = cx - segment.A.X, ay = cy + segment.A.Y, bx = cx - segment.B.X, by = cy + segment.B.Y;
            float dx = Math.Abs(bx - ax), dy = Math.Abs(by - ay);
            if (dx < 1e-4f && dy < 1e-4f)
                return;
            if (dy < 1e-4f)
                Solids.Box(metal, new Vec3((ax + bx) * 0.5f, ay, z), new Vec3(dx * 0.5f + halfSize, halfSize, halfSize));
            else if (dx < 1e-4f)
                Solids.Box(metal, new Vec3(ax, (ay + by) * 0.5f, z), new Vec3(halfSize, dy * 0.5f + halfSize, halfSize));
            else
                Solids.Tube(metal, new Vec3(ax, ay, z), new Vec3(bx, by, z), halfSize, halfSize, TextStudSides, true, true);
        }
    }
}
