using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public static partial class SignBuilder
    {
        /// <summary>The housing's wall, the lit panel's thickness, how far the panel sits behind the housing's front, and the letters' lift off the panel (m).</summary>
        private const float StandShell = 0.012f, StandPanel = 0.004f, StandPanelBack = 0.016f, StandDecalLift = 0.002f;

        /// <summary>
        /// A standing light box (0.12.76, Signs.StandBoxes): the housing (back, top, bottom, sides, a bezel round the face), the lit panel, the letters as flat shapes on it (dark on a lit face, or lit on a dark
        /// one: the glyphs of the text light box's plan, their faces only), and the frame of StandPlanner when the support is a post kind (parts.Stand is then set and BuildStandFrame makes the frame): the
        /// housing is made upright and then turned about its lower back edge by the tilt, so that its face looks up. On any other support it is made upright and flat on it. Where the plan of the text is not
        /// made yet (the main thread asked) and in a far tile a bar stands for each line.
        /// </summary>
        private static void AddStandBoard(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            GuideBoard board = instance.Board;
            Signs.StandBoard stand = board.Stand;
            Signs.TextBoard text = board.Text;
            GuidePart main = board.Main;
            int member = placement.Board.Member;
            parts.AddFace(instance);

            float width = main.W, height = main.H, depth = stand.Depth, cx = placement.Cx;
            float yA = placement.Cy - height * 0.5f, zA = BackOf(layout, placement), tilt = 0f;
            if (Signs.StandBoxes.Framed(layout.Kind) && layout.PoleX != null && layout.PoleX.Length > 0)
            {
                StandPlan plan = StandPlanner.Plan(new StandSpec { Width = width, Height = height, Depth = depth, TiltDeg = stand.TiltDeg, Lift = yA, FrameX = layout.PoleX });
                parts.Stand = plan;
                yA = plan.YA;
                zA = plan.ZA;
                tilt = plan.TiltRad;
            }

            float yMid = yA + height * 0.5f, zFace = zA + depth;
            var touched = new List<KeyValuePair<MeshBuilder, int>>();
            Func<MeshBuilder, MeshBuilder> track = mesh =>
            {
                foreach (KeyValuePair<MeshBuilder, int> seen in touched)
                {
                    if (ReferenceEquals(seen.Key, mesh))
                        return mesh;
                }

                touched.Add(new KeyValuePair<MeshBuilder, int>(mesh, mesh.VertexCount));
                return mesh;
            };

            MeshBuilder frame = track(parts.Solid(stand.Frame));
            MeshBuilder glow = track(parts.Solid(text.Glow)), dark = track(parts.Solid(SolidColour.Black));
            MeshBuilder lit = stand.LitGround ? glow : dark, ink = stand.LitGround ? dark : glow;
            float t = StandShell, b = Signs.StandBoxes.Bezel, halfW = width * 0.5f, halfH = height * 0.5f;
            Solids.Box(frame, new Vec3(cx, yMid, zA + t * 0.5f), new Vec3(halfW, halfH, t * 0.5f));
            Solids.Box(frame, new Vec3(cx, yA + height - t * 0.5f, zA + depth * 0.5f), new Vec3(halfW, t * 0.5f, depth * 0.5f));
            Solids.Box(frame, new Vec3(cx, yA + t * 0.5f, zA + depth * 0.5f), new Vec3(halfW, t * 0.5f, depth * 0.5f));
            foreach (float k in new[] { -1f, 1f })
                Solids.Box(frame, new Vec3(cx + k * (halfW - t * 0.5f), yMid, zA + depth * 0.5f), new Vec3(t * 0.5f, halfH - t, depth * 0.5f));
            Solids.Box(lit, new Vec3(cx, yMid, zFace - StandPanelBack), new Vec3(halfW - t, halfH - t, StandPanel * 0.5f));
            float bezelZ = zFace - 0.007f, bezelHalfZ = 0.013f;
            foreach (float k in new[] { -1f, 1f })
            {
                Solids.Box(frame, new Vec3(cx, yMid + k * (halfH - b * 0.5f), bezelZ), new Vec3(halfW, b * 0.5f, bezelHalfZ));
                Solids.Box(frame, new Vec3(cx + k * (halfW - b * 0.5f), yMid, bezelZ), new Vec3(b * 0.5f, halfH - b, bezelHalfZ));
            }

            if (text.Layout != null && text.Layout.Glyphs.Count > 0)
            {
                float zInk = zFace - StandPanelBack + StandPanel * 0.5f + StandDecalLift;
                TextPlanResult result = TextPlans.Get(text.Job);
                if (result == null || result.Meshes == null || result.TooHeavy || (t_Reduced && !FarFacesFit(TextFaceVertices(result))))
                {
                    // The main thread asked and the plan is being made on a worker: these bars stand in until it is there, and are not kept (ChunkBuilder). A far tile has the letters (0.12.105: this
                    // box's letters are their faces in any tile) unless they are over MaxFarTextVertices.
                    parts.Provisional |= result == null;
                    AddStandBars(track(ink), text, cx, yMid, zInk);
                }
                else
                {
                    TextLayoutResult placed = result.Layout;
                    MeshBuilder letters = track(ink);
                    for (int g = 0; g < result.Meshes.Length; g++)
                    {
                        if (result.Meshes[g] != null)
                            AddGlyphFace(letters, result.Meshes[g], placed.MetresPerEm, cx, yMid, placed.Glyphs[g], zInk);
                    }
                }
            }

            if (Math.Abs(tilt) > 1e-5f)
            {
                foreach (KeyValuePair<MeshBuilder, int> range in touched)
                    LeanBack(range.Key, range.Value, tilt, yA, zA);
            }

            PickShape pick = BoardPick(MirrorX(GuideKit.PartOutline(main)), cx, yMid, width, height, zA, member, zFace, true, 0.01f);
            pick.Tilt = -tilt;
            pick.TiltY = yA;
            pick.TiltZ = zA;
            parts.Picks.Add(pick);
        }

        /// <summary>The vertices of a mesh from first on turned back about the line along X at y0, z0 by tilt (radians): the top of what stood upright goes toward -z, its face looks up.</summary>
        internal static void LeanBack(MeshBuilder mesh, int first, float tilt, float y0, float z0)
        {
            float s = (float)Math.Sin(tilt), c = (float)Math.Cos(tilt);
            for (int i = first; i < mesh.VertexCount; i++)
            {
                Vec3 p = mesh.Positions[i], n = mesh.Normals[i];
                float dy = p.Y - y0, dz = p.Z - z0;
                mesh.Positions[i] = new Vec3(p.X, y0 + dy * c + dz * s, z0 - dy * s + dz * c);
                mesh.Normals[i] = new Vec3(n.X, n.Y * c + n.Z * s, -n.Y * s + n.Z * c);
            }
        }

        /// <summary>The face of a glyph (its triangles, no return) as a flat shape in the plane z, put as the viewer sees it: the vertex is at (cx - x, y), as the text light box puts its letters.</summary>
        private static void AddGlyphFace(MeshBuilder mesh, GlyphMesh glyph, float metresPerEm, float cx, float cy, PlacedGlyph at, float z)
        {
            var front = new Vec3(0f, 0f, 1f);
            int first = mesh.VertexCount;
            foreach (Point2 p in glyph.Points)
                mesh.Vertex(new Vec3(cx - (at.X + p.X * metresPerEm), cy + at.Y + p.Y * metresPerEm, z), front);
            for (int i = 0; i + 2 < glyph.Triangles.Length; i += 3)
                mesh.TriFacing(first + glyph.Triangles[i], first + glyph.Triangles[i + 1], first + glyph.Triangles[i + 2], front);
        }

        /// <summary>One bar for each line of the text on the face: what a far tile draws, and what stands in until the plan is made.</summary>
        private static void AddStandBars(MeshBuilder mesh, Signs.TextBoard text, float cx, float cy, float z)
        {
            TextLayoutResult layout = text.Layout;
            float height = layout.LetterHeight, pitch = layout.Lines > 1 ? (layout.Height - height) / (layout.Lines - 1) : 0f, bar = height * 0.6f, widest = layout.Width;
            for (int line = 0; line < layout.Lines; line++)
            {
                float width = layout.LineWidths[line];
                if (width <= 0f)
                    continue;
                float x0 = -widest * 0.5f + (text.Job.Align == TextAlign.Centre ? (widest - width) * 0.5f : text.Job.Align == TextAlign.Right ? widest - width : 0f);
                float x = x0 + width * 0.5f, y = layout.Height * 0.5f - line * pitch - height * 0.5f;
                Solids.Box(mesh, new Vec3(cx - x, cy + y, z), new Vec3(width * 0.5f, bar * 0.5f, 0.0015f));
            }
        }

        /// <summary>
        /// The frames of a standing box (StandPlan): at each post a pad on the ground with the post up to the box's back and the brace's foot behind with its pad and leg, so that each foot follows the ground
        /// (PostFoot); the base beam, the brace, the arms to the box's back, and the ties and crossed braces between neighbouring frames. All in the support's steel.
        /// </summary>
        private static void BuildStandFrame(SupportLayout layout, SignParts parts)
        {
            StandPlan plan = parts.Stand;
            for (int i = 0; i < plan.FrameCount; i++)
            {
                StandMember post = plan.Find(StandMemberKind.Post, i), leg = plan.Find(StandMemberKind.Leg, i);
                StandFoot front = plan.Feet[2 * i], rear = plan.Feet[2 * i + 1];
                StandPad(parts, plan, front, post, true);
                StandPad(parts, plan, rear, leg, false);
                PostCap(parts.Metal, post.A.X, 0f, post.B.Y, post.Half);
                parts.Picks.Add(PostPick(post.A.X, 0f, post.Half, plan.PlateTop, post.B.Y + 0.005f));
                parts.Picks.Add(new PickShape { Name = "foot", Min = new Vec3(front.X - front.PadHalf, PedestalBottom, -front.PadHalf), Max = new Vec3(front.X + front.PadHalf, plan.PlateTop + 0.04f, front.PadHalf) });
            }

            BuildStandMembers(parts, plan, 0f);
        }

        /// <summary>
        /// The members of the frames (the base beams, the arms, the braces, the ties, the web and the X) and the plates at the joints, in the support's steel; dz: how far along z the whole plan is moved (0
        /// for a stand on the ground; a text on a roof, whose board is where the layout put it, moves its frames to stand behind it).
        /// </summary>
        private static void BuildStandMembers(SignParts parts, StandPlan plan, float dz)
        {
            foreach (StandMember m in plan.Members)
            {
                Vec3 a = m.A + new Vec3(0f, 0f, dz), b = m.B + new Vec3(0f, 0f, dz);
                switch (m.Kind)
                {
                    case StandMemberKind.Beam:
                    case StandMemberKind.ArmLow:
                    case StandMemberKind.ArmTop:
                        float z0 = Math.Min(a.Z, b.Z), z1 = Math.Max(a.Z, b.Z);
                        Solids.Box(parts.Metal, new Vec3(a.X, a.Y, (z0 + z1) * 0.5f), new Vec3(m.Half, m.Half, (z1 - z0) * 0.5f + (m.Kind == StandMemberKind.Beam ? m.Half : 0f)));
                        break;
                    case StandMemberKind.Strut:
                        Solids.Tube(parts.Metal, a, b, m.Half, m.Half, 8, true, true);
                        break;
                    case StandMemberKind.Tie:
                    case StandMemberKind.Cross:
                        Solids.Tube(parts.Metal, a, b, m.Half, m.Half, 6, true, true);
                        break;
                    case StandMemberKind.Web:
                    case StandMemberKind.Diag:
                        if (!t_Reduced)
                            Solids.Tube(parts.Metal, a, b, m.Half, m.Half, 6, true, true);
                        break;
                }
            }

            // The plates at the joints (reduced detail leaves them out).
            if (!t_Reduced)
            {
                var thickness = new Vec3(StandPlanner.GussetThickness * 0.5f, 0f, 0f);
                var shift = new Vec3(0f, 0f, dz);
                foreach (StandGusset g in plan.Gussets)
                    Solids.Prism(parts.Metal, g.A + shift, g.B + shift, g.C + shift, thickness);
            }
        }

        /// <summary>The four bolts of a base plate and the four ribs between the plate and the tube (this mod's design: a rib three tube radii high, 0.1 to 0.3 m, 10 mm thick).</summary>
        private static void StandPadFittings(MeshBuilder metal, float x, float z, float pad, float plateTop, float tubeHalf)
        {
            float boltR = Math.Max(0.008f, 0.14f * tubeHalf), inset = 0.02f + 2.2f * boltR;
            foreach (float kx in new[] { -1f, 1f })
            {
                foreach (float kz in new[] { -1f, 1f })
                {
                    var at = new Vec3(x + kx * (pad - inset), plateTop, z + kz * (pad - inset));
                    Solids.Tube(metal, at, at + new Vec3(0f, 0.012f + 3f * boltR, 0f), boltR, boltR, 6, false, true);
                }
            }

            float rib = Math.Max(0.1f, Math.Min(0.3f, 3f * tubeHalf)), outer = pad - 0.03f, inner = 0.9f * tubeHalf;
            foreach (float k in new[] { -1f, 1f })
            {
                Solids.Prism(metal, new Vec3(x + k * inner, plateTop, z), new Vec3(x + k * outer, plateTop, z), new Vec3(x + k * inner, plateTop + rib, z), new Vec3(0f, 0f, 0.005f));
                Solids.Prism(metal, new Vec3(x, plateTop, z + k * inner), new Vec3(x, plateTop, z + k * outer), new Vec3(x, plateTop + rib, z + k * inner), new Vec3(0.005f, 0f, 0f));
            }
        }

        /// <summary>
        /// One pad of a frame: the concrete, the plate and its bolts, and the upright tube that stands on it (the post at the front, the leg under the beam's rear end), registered as a foot that follows
        /// the ground: the tube's lower end and the pad move with it, the tube's top stays. The root of a post is painted as any post's.
        /// </summary>
        private static void StandPad(SignParts parts, StandPlan plan, StandFoot spec, StandMember tube, bool post)
        {
            float x = spec.X, z = spec.Z, pad = spec.PadHalf, plateTop = plan.PlateTop;
            var foot = new PostFoot
            {
                X = x,
                Z = z,
                PadHalf = pad,
                PadBottom = PedestalBottom,
                MaxRaise = Math.Max(0f, Math.Min(PostGround.MaxRaise, plan.Lift - 0.12f)),
                MetalFrom = parts.Metal.VertexCount,
                ConcreteFrom = parts.Concrete.VertexCount
            };
            AddPedestal(parts, x, z, pad, pad);
            Solids.Box(parts.Metal, new Vec3(x, (PedestalTop + plateTop) * 0.5f, z), new Vec3(pad - 0.02f, (plateTop - PedestalTop) * 0.5f, pad - 0.02f));
            if (!t_Reduced)
                StandPadFittings(parts.Metal, x, z, pad, plateTop, tube.Half);

            foot.MetalTo = parts.Metal.VertexCount;
            foot.ConcreteTo = parts.Concrete.VertexCount;
            foot.Split = (plateTop + tube.B.Y) * 0.5f;
            foot.TubeFrom = parts.Metal.VertexCount;
            Solids.Tube(parts.Metal, tube.A, tube.B, tube.Half, tube.Half, post ? PoleSides : 8, false, false);
            foot.TubeTo = parts.Metal.VertexCount;
            if (post)
                PaintRoot(parts, foot, tube.Half, tube.Half, plateTop, tube.B.Y, PoleSides, false, RoadSide.Left);
            parts.Feet.Add(foot);
        }
    }
}
