using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>
        /// A text light box whose letters lean back (the owner, 2 Nov: "第一种纯字形灯箱也应该支持仰角"), or that stands on the triangular frames: the letters and their rear frame are built upright in the plane
        /// of the board as ever, the slide channels behind the rails, then everything is turned about the board's lower back edge by the tilt (the standing light box's way, LeanBack), and the frames of
        /// StandPlanner stand behind it: on a single, a double or a triple post (BuildStandFrame makes them) and on a roof (BuildTextRoofFrames). The plan of the text does not depend on the angle: it
        /// is made in the board's plane and the turn is the builder's. False (nothing was built) when the board does not lean on this support, and it is built upright by AddTextBoard.
        /// </summary>
        private static bool TryAddTextLeaning(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            Signs.TextBoard text = instance.Board.Text;
            if (text == null || (Math.Abs(text.TiltDeg) < TextTilt.MinDeg && !text.Triangular) || text.Layout == null || text.Layout.Glyphs.Count == 0)
                return false;
            TextLeanMode mode = TextTilt.ModeOf(layout.Kind, layout.Roof, text.TiltDeg, text.Triangular, Signs.TextSupports.Enabled, text.Radius == 0f, parts.Stand != null);
            if (mode == TextLeanMode.None || (mode != TextLeanMode.Stand && text.Mount == TextMount.None))
            {
                // Without the base frame the letters' own studs would have to carry a leaned text from a wall or stand on feet on a roof: they are not built so.
                SayNotBuilt(layout, text);
                return false;
            }

            GuidePart main = instance.Board.Main;
            int member = placement.Board.Member;
            float width = main.W, height = main.H, cx = placement.Cx;
            TextPlanResult plan = TextPlans.Get(text.Job);
            bool complete = !t_Reduced && plan != null && plan.Frame != null && !plan.TooHeavy;
            FrameStyle style = TextStyles.FrameFor(text.Job.LetterHeight, text.Job.Art);

            // A wall holds the text on spacers made from the plan's rails: until there is a plan (a far tile, or a worker still making it) or where the text is too heavy for one, the bars are built upright
            // with the wall's own spacers, as an upright text has them.
            if (mode == TextLeanMode.Wall && !complete)
                return false;

            StandPlan stand = null;
            float[] frameX = null;
            float yA, tilt, zA, dz = 0f;
            if (mode == TextLeanMode.Wall)
            {
                // A text that leans back keeps the spacers' length of an upright text at the top of the back plane, and its lower back edge stands out further by the board's height times the sine of the
                // angle; one that looks down keeps it at the lower edge, and its top stands out (turned about the lower back edge, as the stand's).
                tilt = TextTilt.Radians(text.TiltDeg);
                yA = placement.Cy - height * 0.5f;
                zA = BackOf(layout, placement) + (tilt > 0f ? height * (float)Math.Sin(tilt) : 0f);
            }
            else
            {
                frameX = mode == TextLeanMode.Roof ? TextTilt.RoofFrameX(width, cx) : layout.PoleX;
                if (frameX == null || frameX.Length == 0)
                    return false;
                TextTilt.Shares(complete ? plan.Frame : null, height, out float armShare, out float lowShare);
                stand = StandPlanner.Plan(new StandSpec
                {
                    Width = width,
                    Height = height,
                    Depth = text.Reach,
                    TiltDeg = text.TiltDeg,
                    Lift = placement.Bottom,
                    FrameX = frameX,
                    ArmShare = armShare,
                    LowShare = lowShare,
                    ArmBehind = SupportLayouts.RailDepth,
                    Open = true
                });
                yA = stand.YA;
                tilt = stand.TiltRad;
                zA = mode == TextLeanMode.Roof ? BackOf(layout, placement) : stand.ZA;
                dz = zA - stand.ZA;
            }

            float cyUp = yA + height * 0.5f;
            float backZ = zA, zBack = backZ + text.Stand, zFront = zBack + text.Depth;
            WallFramePlan wall = mode == TextLeanMode.Wall ? TextWallFrame.Plan(plan.Frame, style, text.Mount) : null;

            MeshBuilder glow = parts.Solid(text.Glow), ret = parts.Solid(text.Return), metal = parts.Metal;
            var touched = new List<KeyValuePair<MeshBuilder, int>>();
            foreach (MeshBuilder mesh in new[] { glow, ret, metal })
            {
                bool seen = false;
                foreach (KeyValuePair<MeshBuilder, int> known in touched)
                    seen |= ReferenceEquals(known.Key, mesh);
                if (!seen)
                    touched.Add(new KeyValuePair<MeshBuilder, int>(mesh, mesh.VertexCount));
            }

            if (complete)
            {
                TextLayoutResult placed = plan.Layout;
                for (int g = 0; g < plan.Meshes.Length; g++)
                {
                    GlyphMesh mesh = plan.Meshes[g];
                    if (mesh == null)
                        continue;
                    PlacedGlyph glyph = placed.Glyphs[g];
                    mesh.AddTo(glow, ret, placed.MetresPerEm, glyph.X - cx, cyUp + glyph.Y, zBack, zFront, true, glyph.Angle);
                }

                AddTextFrame(parts, plan.Frame, style, false, cx, cyUp, backZ, zBack);
                if (mode == TextLeanMode.Wall)
                {
                    // The diagonals between a line's two rails lie in the rails' layer and turn with them.
                    float zBrace = backZ + Math.Max(0.006f, style.RailSize) * 0.5f;
                    foreach (FrameSegment brace in wall.Braces)
                        Solids.Tube(metal, new Vec3(cx - brace.A.X, cyUp + brace.A.Y, zBrace), new Vec3(cx - brace.B.X, cyUp + brace.B.Y, zBrace), TextWallFrame.BraceRadius, TextWallFrame.BraceRadius, TextStudSides, true, true);
                }
                else
                {
                    AddTextChannels(parts, plan.Frame, style, frameX, cx, cyUp, backZ);
                }
            }
            else
            {
                // A far tile draws the letters' lit faces (AddTextFaces), as the upright text does, and they lean with the rest; where it cannot, and while the plan is being made on a worker (the main
                // thread asked), the bars stand in: those are not kept (ChunkBuilder).
                if (!AddTextFaces(parts, text, plan, cx, cyUp, zFront))
                {
                    parts.Provisional |= plan == null;
                    AddTextBars(parts, text, cx, cyUp, zFront);
                }
            }

            if (Math.Abs(tilt) > 1e-5f)
            {
                foreach (KeyValuePair<MeshBuilder, int> range in touched)
                    LeanBack(range.Key, range.Value, tilt, yA, zA);
            }

            if (mode == TextLeanMode.Stand)
            {
                parts.Stand = stand;
            }
            else
            {
                parts.TextOwns = parts.TextOwns ?? new HashSet<Placement>();
                parts.TextOwns.Add(placement);
                if (mode == TextLeanMode.Roof)
                    BuildTextRoofFrames(parts, stand, dz);
                else
                    parts.WallTieCount += BuildLeanedSpacers(parts, wall, plan.Frame, cx, cyUp, yA, zA, tilt, height);
            }

            PickShape pick = BoardPick(MirrorX(GuideKit.PartOutline(main)), cx, cyUp, width, height, backZ, member, zFront, true, 0.01f);
            pick.Tilt = -tilt;
            pick.TiltY = yA;
            pick.TiltZ = zA;
            parts.Picks.Add(pick);
            return true;
        }

        /// <summary>
        /// The spacers of a leaned text on a wall (the wall is z = 0): horizontal from the wall to the turned back of the rail they stood on, so longer the lower the rail is, and the ties that take the
        /// weight of the lower rails (TextTilt.WallTies).
        /// </summary>
        private static int BuildLeanedSpacers(SignParts parts, WallFramePlan wall, FramePlan frame, float cx, float cyUp, float yA, float zA, float tilt, float height)
        {
            float s = (float)Math.Sin(tilt), c = (float)Math.Cos(tilt), r = TextWallFrame.StandoffRadius;
            foreach (Point2 standoff in wall.Standoffs)
            {
                TextTilt.Turned(cyUp + standoff.Y, zA, yA, zA, s, c, out float y, out float z);
                Solids.Tube(parts.Metal, new Vec3(cx - standoff.X, y, 0f), new Vec3(cx - standoff.X, y, z + 0.002f), r, r, TextStudSides, false, true);
            }

            // The ties are for the rails that stand out TieMinReach beyond the nearest to the wall: the top rail's back when the text leans back, the lowest edge's when it looks down.
            float nearest = tilt > 0f ? zA - height * s : zA;
            int ties = 0;
            foreach (TextTilt.WallTie tie in TextTilt.WallTies(wall.Standoffs, frame.Rails, cyUp, yA, zA, tilt, nearest))
            {
                Solids.Tube(parts.Metal, new Vec3(cx - tie.X, tie.WallY, tie.WallZ), new Vec3(cx - tie.X, tie.Y, tie.Z), TextTilt.TieRadius, TextTilt.TieRadius, TextStudSides, true, true);
                ties++;
            }

            return ties;
        }

        /// <summary>One line in the log, once for each kind of support, that a tilt that was asked for is not built there.</summary>
        private static void SayNotBuilt(SupportLayout layout, Signs.TextBoard text)
        {
            if (Math.Abs(text.TiltDeg) < TextTilt.MinDeg || t_Reduced || Signs.StandBoxes.Log == null)
                return;
            string note = TextTilt.Note(layout.Kind, layout.Roof, text.TiltDeg);
            if (note != null)
                Signs.StandBoxes.Log(note);
        }

        /// <summary>The slide channels behind the frame's rails (TextCarrier.Post: the lowest rail, the highest, a middle one for a tall text, long enough to reach each frame) and the bolts through the rails; the arms of the frames end in them.</summary>
        private static void AddTextChannels(SignParts parts, FramePlan frame, FrameStyle style, float[] frameX, float cx, float cy, float backZ)
        {
            if (frame == null || frame.Rails.Count == 0)
                return;
            var poles = new List<float>();
            foreach (float px in frameX)
                poles.Add(cx - px);
            CarrierPlan carrier = TextCarrier.Post(frame, style, poles);
            float depth = SupportLayouts.RailDepth, width = SupportLayouts.RailWidth, size = Math.Max(0.006f, style.RailSize);
            foreach (CarrierChannel channel in carrier.Channels)
            {
                float y = cy + channel.Level, x0 = cx - channel.To, x1 = cx - channel.From;
                Solids.Box(parts.Metal, new Vec3((x0 + x1) * 0.5f, y, backZ - depth * 0.5f), new Vec3((x1 - x0) * 0.5f, width * 0.5f, depth * 0.5f));
            }

            foreach (CarrierBolt bolt in carrier.Bolts)
                Solids.Box(parts.Metal, new Vec3(cx - bolt.X, cy + bolt.Y, backZ + size + TextCarrier.BoltThickness * 0.5f), new Vec3(TextCarrier.BoltHalf, TextCarrier.BoltHalf, TextCarrier.BoltThickness * 0.5f));
        }

        /// <summary>
        /// The frames of a text on a roof (StandPlan moved dz along z so that they stand behind the board where the layout put it): a plate on the roof under each post and under each brace's leg, the tubes on
        /// them, and the members and plates of the frame. The roof is y = 0; there is no concrete and nothing follows the ground.
        /// </summary>
        private static void BuildTextRoofFrames(SignParts parts, StandPlan plan, float dz)
        {
            var shift = new Vec3(0f, 0f, dz);
            for (int i = 0; i < plan.FrameCount; i++)
            {
                StandMember post = plan.Find(StandMemberKind.Post, i), leg = plan.Find(StandMemberKind.Leg, i);
                StandRoofPad(parts, plan, plan.Feet[2 * i], post, dz);
                StandRoofPad(parts, plan, plan.Feet[2 * i + 1], leg, dz);
                PostCap(parts.Metal, post.A.X, post.B.Z + dz, post.B.Y, post.Half);
            }

            BuildStandMembers(parts, plan, dz);
        }

        /// <summary>A base plate on the roof with its bolts and ribs, and the tube that stands on it.</summary>
        private static void StandRoofPad(SignParts parts, StandPlan plan, StandFoot spec, StandMember tube, float dz)
        {
            float x = spec.X, z = spec.Z + dz, pad = spec.PadHalf, plateTop = plan.PlateTop;
            Solids.Box(parts.Metal, new Vec3(x, (0.005f + plateTop) * 0.5f, z), new Vec3(pad - 0.02f, (plateTop - 0.005f) * 0.5f, pad - 0.02f));
            if (!t_Reduced)
                StandPadFittings(parts.Metal, x, z, pad, plateTop, tube.Half);
            Solids.Tube(parts.Metal, tube.A + new Vec3(0f, 0f, dz), tube.B + new Vec3(0f, 0f, dz), tube.Half, tube.Half, 8, false, false);
        }
    }
}
