using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// The rear of a text light box (0.12.76): what holds the letters to the support. Off (features.textSupports): as 0.12.75 had it (the frame, no rails on a wall, nothing else). On a flat wall the
        /// base frame and its spacers to the wall (or the studs straight to it), on a curved one the same on the surface the facade was fitted to, on a roof the legs and braces or the feet; on any other
        /// support the frame's rails and the carrier's channels and bolts (TextCarrier).
        /// </summary>
        private static void AddTextRear(SignParts parts, SupportLayout layout, Placement placement, TextPlanResult plan, Signs.TextBoard text, in ArcSurface surface, float cx, float cy, float backZ, float zBack)
        {
            FrameStyle style = TextStyles.FrameFor(text.Job.LetterHeight, text.Job.Art);
            if (!Signs.TextSupports.Enabled)
            {
                AddTextFrame(parts, plan.Frame, style, SupportKinds.OnWall(layout.Kind), cx, cy, backZ, zBack);
                return;
            }

            if (layout.Kind == SupportKind.WallFlat)
            {
                parts.TextOwns = parts.TextOwns ?? new HashSet<Placement>();
                parts.TextOwns.Add(placement);
                if (layout.Roof)
                    AddTextRoof(parts, plan, text, style, placement, cx, cy, backZ, zBack);
                else
                    AddTextWall(parts, plan, text, style, surface, cx, cy, backZ, zBack);
                return;
            }

            // A bracket's board hangs under its arm: it has rails like any board that is held from behind.
            AddTextFrame(parts, plan.Frame, style, false, cx, cy, backZ, zBack);
            AddTextCarrier(parts, layout, placement, plan, text, cx, cy, backZ);
        }

        /// <summary>The surface the text stands on: a cylinder where it is on a flat wall support with a radius in its settings, else flat. faceOffset: how far in front of the wall the letters' faces are.</summary>
        private static ArcSurface TextSurfaceOf(SupportLayout layout, Signs.TextBoard text, float faceOffset) =>
            Signs.TextSupports.Enabled && layout.Kind == SupportKind.WallFlat && !layout.Roof && text.Job.Art.Sweep == 0f ? new ArcSurface(text.Radius, faceOffset) : new ArcSurface(0f, faceOffset);

        /// <summary>A glyph on a curved wall: built around its own middle and set on the surface, turned to the tangent there (a character is rigid).</summary>
        private static void AddTextGlyphOnArc(MeshBuilder glow, MeshBuilder ret, GlyphMesh mesh, float metresPerEm, PlacedGlyph glyph, in ArcSurface surface, float cx, float cy, float zBack, float zFront)
        {
            float advance = mesh.Advance * metresPerEm;
            ArcGlyph at = ArcLayout.Place(surface, glyph.X, advance, zBack);
            var g = new MeshBuilder();
            var r = new MeshBuilder();
            mesh.AddTo(g, r, metresPerEm, -advance * 0.5f, cy + glyph.Y, 0f, zFront - zBack, true);
            glow.Append(g, new Vec3(cx - at.X, 0f, at.Z), -at.Theta);
            ret.Append(r, new Vec3(cx - at.X, 0f, at.Z), -at.Theta);
        }

        private static Vec3 OnSurface(in ArcSurface surface, float x, float y, float d, float cx, float cy)
        {
            ArcPoint p = surface.At(x, d);
            return new Vec3(cx - p.X, cy + y, p.Z);
        }

        /// <summary>
        /// The base frame of a text on a wall (TextWallFrame): on a flat wall the frame's rails as on a post, the spacers from the rails to the wall and the diagonal braces; without the frame the
        /// studs reach the wall. On a curved wall everything stands on the surface: the studs and spacers are radial (the same length all along), the rails are chords within 4 mm of the arc.
        /// </summary>
        private static void AddTextWall(SignParts parts, TextPlanResult plan, Signs.TextBoard text, FrameStyle style, in ArcSurface surface, float cx, float cy, float backZ, float zBack)
        {
            WallFramePlan wall = TextWallFrame.Plan(plan.Frame, style, text.Mount);
            if (surface.Flat && !wall.StudsToWall)
                AddTextFrame(parts, plan.Frame, style, false, cx, cy, backZ, zBack);
            else
                AddTextFrameOnSurface(parts, plan.Frame, style, surface, wall.StudsToWall, cx, cy, backZ, zBack);
            if (wall.StudsToWall)
                return;
            float size = Math.Max(0.006f, style.RailSize), zMid = backZ + size * 0.5f;
            foreach (Point2 s in wall.Standoffs)
                Solids.Tube(parts.Metal, OnSurface(surface, s.X, s.Y, 0f, cx, cy), OnSurface(surface, s.X, s.Y, backZ + 0.002f, cx, cy), TextWallFrame.StandoffRadius, TextWallFrame.StandoffRadius, TextStudSides, false, true);
            foreach (FrameSegment brace in wall.Braces)
                Solids.Tube(parts.Metal, OnSurface(surface, brace.A.X, brace.A.Y, zMid, cx, cy), OnSurface(surface, brace.B.X, brace.B.Y, zMid, cx, cy), TextWallFrame.BraceRadius, TextWallFrame.BraceRadius, TextStudSides, true, true);
        }

        /// <summary>The frame of a text on a surface (curved, or flat without a base frame): the studs radially, from the wall (no frame) or the back plane to the letters; the rails, ties and rods on the layer of the rails.</summary>
        private static void AddTextFrameOnSurface(SignParts parts, FramePlan frame, FrameStyle style, in ArcSurface surface, bool studsToWall, float cx, float cy, float backZ, float zBack)
        {
            MeshBuilder metal = parts.Metal;
            float studR = Math.Max(0.0015f, style.StudRadius);
            foreach (FrameStud stud in frame.Studs)
                Solids.Tube(metal, OnSurface(surface, stud.At.X, stud.At.Y, studsToWall ? 0f : backZ, cx, cy), OnSurface(surface, stud.At.X, stud.At.Y, zBack + 0.004f, cx, cy), studR, studR, TextStudSides, false, true);
            if (studsToWall)
                return;
            float size = Math.Max(0.006f, style.RailSize), zRail = backZ + size * 0.5f;
            foreach (FrameSegment rod in frame.Bridges)
                Solids.Tube(metal, OnSurface(surface, rod.A.X, rod.A.Y, zRail, cx, cy), OnSurface(surface, rod.B.X, rod.B.Y, zRail, cx, cy), studR, studR, TextStudSides, true, true);
            foreach (FrameSegment piece in frame.Rails)
                AddTextBar(metal, piece, surface, size * 0.5f, zRail, cx, cy);
            foreach (FrameSegment piece in frame.Ties)
                AddTextBar(metal, piece, surface, size * 0.5f, zRail, cx, cy);
        }

        /// <summary>A rail or a tie on a surface: a horizontal one in chords within 4 mm of the arc (each a box turned to its chord), a vertical one a box turned to the normal there, an oblique one a tube.</summary>
        private static void AddTextBar(MeshBuilder metal, FrameSegment piece, in ArcSurface surface, float halfSize, float d, float cx, float cy)
        {
            if (Math.Abs(piece.A.Y - piece.B.Y) < 1e-4f)
            {
                float y = (piece.A.Y + piece.B.Y) * 0.5f;
                foreach ((ArcPoint A, ArcPoint B) chord in ArcLayout.Chords(surface, piece.A.X, piece.B.X, d))
                {
                    float dx = -(chord.B.X - chord.A.X), dz = chord.B.Z - chord.A.Z, length = (float)Math.Sqrt(dx * dx + dz * dz);
                    if (length < 1e-5f)
                        continue;
                    var box = new MeshBuilder();
                    Solids.Box(box, Vec3.Zero, new Vec3(length * 0.5f + halfSize * 0.5f, halfSize, halfSize));
                    metal.Append(box, new Vec3(cx - (chord.A.X + chord.B.X) * 0.5f, cy + y, (chord.A.Z + chord.B.Z) * 0.5f), (float)Math.Atan2(-dz, dx));
                }

                return;
            }

            if (Math.Abs(piece.A.X - piece.B.X) < 1e-4f)
            {
                ArcPoint p = surface.At(piece.A.X, d);
                float y0 = Math.Min(piece.A.Y, piece.B.Y), y1 = Math.Max(piece.A.Y, piece.B.Y);
                var box = new MeshBuilder();
                Solids.Box(box, Vec3.Zero, new Vec3(halfSize, (y1 - y0) * 0.5f + halfSize, halfSize));
                metal.Append(box, new Vec3(cx - p.X, cy + (y0 + y1) * 0.5f, p.Z), -p.Theta);
                return;
            }

            Solids.Tube(metal, OnSurface(surface, piece.A.X, piece.A.Y, d, cx, cy), OnSurface(surface, piece.B.X, piece.B.Y, d, cx, cy), halfSize, halfSize, TextStudSides, true, true);
        }

        /// <summary>
        /// A text standing on a roof (TextRoofFrame): the frame's rails as on a post, legs from the roof to the top rail (a box behind it, z from the legs' back to the rails' back plane) on foot plates, rear
        /// braces to feet behind for a tall or wide text; without the frame a leg to the lowest stud of each letter of a single line. The roof is y = 0, the board's lower edge is the placement's bottom.
        /// </summary>
        private static void AddTextRoof(SignParts parts, TextPlanResult plan, Signs.TextBoard text, FrameStyle style, Placement placement, float cx, float cy, float backZ, float zBack)
        {
            GuidePart main = placement.Board.Instance.Board.Main;
            RoofFramePlan roof = TextRoofFrame.Plan(plan.Frame, style, text.Mount, placement.Bottom, main.H, plan.Layout.Lines);
            MeshBuilder metal = parts.Metal;
            float studR = Math.Max(0.0015f, style.StudRadius);
            if (roof.OnFeet)
            {
                // Studs to the letters' backs only; each foot is a leg to its stud, and a plate under it.
                foreach (FrameStud stud in plan.Frame.Studs)
                    Solids.Tube(metal, new Vec3(cx - stud.At.X, cy + stud.At.Y, backZ), new Vec3(cx - stud.At.X, cy + stud.At.Y, zBack + 0.004f), studR, studR, TextStudSides, false, true);
                float zLeg = backZ + text.Stand * 0.5f;
                foreach (RoofLeg foot in roof.Feet)
                {
                    Solids.Tube(metal, new Vec3(cx - foot.X, TextRoofFrame.FootThickness, zLeg), new Vec3(cx - foot.X, foot.Top, zLeg), TextRoofFrame.FeetRadius, TextRoofFrame.FeetRadius, TextStudSides, false, false);
                    Solids.Box(metal, new Vec3(cx - foot.X, TextRoofFrame.FootThickness * 0.5f, zLeg), new Vec3(TextRoofFrame.FootHalf, TextRoofFrame.FootThickness * 0.5f, TextRoofFrame.FootHalf));
                }

                return;
            }

            AddTextFrame(parts, plan.Frame, style, false, cx, cy, backZ, zBack);
            float legHalf = TextRoofFrame.LegHalf, zLegBack = backZ - legHalf;
            foreach (RoofLeg leg in roof.Legs)
            {
                float x = cx - leg.X;
                Solids.Box(metal, new Vec3(x, leg.Top * 0.5f, zLegBack), new Vec3(legHalf, leg.Top * 0.5f, legHalf));
                Solids.Box(metal, new Vec3(x, TextRoofFrame.FootThickness * 0.5f, zLegBack), new Vec3(TextRoofFrame.FootHalf, TextRoofFrame.FootThickness * 0.5f, TextRoofFrame.FootHalf));
            }

            foreach (RoofBrace brace in roof.Braces)
            {
                float x = cx - brace.X;
                float footZ = backZ - brace.Depth;
                Solids.Tube(metal, new Vec3(x, brace.From, zLegBack), new Vec3(x, TextRoofFrame.FootThickness, footZ), TextWallFrame.BraceRadius * 2f, TextWallFrame.BraceRadius * 2f, TextStudSides, true, true);
                Solids.Box(metal, new Vec3(x, TextRoofFrame.FootThickness * 0.5f, footZ), new Vec3(TextRoofFrame.FootHalf, TextRoofFrame.FootThickness * 0.5f, TextRoofFrame.FootHalf));
            }
        }

        /// <summary>The height where a wall bracket's flat bars end for a text board: the top of its highest rail (the bars reach into it), else NaN: the bars end at the board's top as for any board.</summary>
        internal static float TextBarBottom(SignParts parts, Placement p)
        {
            Signs.TextBoard text = p.Board.Instance.Board?.Text;
            if (text == null || !Signs.TextSupports.Enabled || t_Reduced)
                return float.NaN;
            TextPlanResult plan = TextPlans.Peek(text.Job);
            if (plan == null || plan.Frame == null || plan.Frame.Rails.Count == 0 || plan.TooHeavy)
                return float.NaN;
            float high = float.MinValue;
            foreach (FrameSegment rail in plan.Frame.Rails)
                high = Math.Max(high, (rail.A.Y + rail.B.Y) * 0.5f);
            return p.Cy + high;
        }
    }
}
