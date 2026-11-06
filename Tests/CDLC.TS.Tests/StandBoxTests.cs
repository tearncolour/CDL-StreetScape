using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The standing light box (0.12.76): the template and its settings, the box of the text and its margin, the housing leaning back about its lower back edge, the lit face and the flat letters on it
    /// in both ways of lighting, the frames on each post and their feet that follow the ground, the box on a support that is not a post kind, the far tile, the main thread's provisional bars. The look
    /// and the glow are not testable here.
    /// </summary>
    internal static class StandBoxTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("stand box: the template, its fields and its settings", Template);
            Program.Run("stand box: the board is the text's box with its margin and bezel", Box);
            Program.Run("stand box: the housing leans back about its lower back edge", Leans);
            Program.Run("stand box: the lit face and the letters, both ways", Faces);
            Program.Run("stand box: the letters read the right way round", Mirror);
            Program.Run("stand box: the frames, their closure and their clearance from the housing", Frames);
            Program.Run("stand box: the feet follow the ground", Ground);
            Program.Run("stand box: on a post of each kind and on supports that are not posts", Supports);
            Program.Run("stand box: reduced detail, a far tile, and a plan that is not made yet", Details);
            Program.Run("stand box: the catalogue's thumbnail", Thumbnail);
            Program.Run("stand box: the editor's hints and the feet's note", HintLines);
            Program.Run("stand box: the tilt's slider, its keys and the shortcuts they hold", TiltControls);
            Program.Run("stand box: the numbers of a stand in each tier", Numbers);
        }

        private static SignDef Def => SignCatalog.ById(StandBoxes.TemplateId);

        private static GuideSpec Spec(string text = "立式灯箱", string extra = "")
        {
            GuideSpec spec = StandBoxes.Template.Parse("");
            foreach (string pair in extra.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return spec.With("text", text);
        }

        private static SignInstance Instance(string spec) => SignInstances.Of(Def, spec);

        private static SignParts Parts(SignInstance instance, SupportDetail detail = SupportDetail.Full, string support = null)
        {
            SupportDef def = support != null ? SupportCatalog.ById(support) : SupportPlanner.Auto(instance.Def);
            var boards = new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } };
            return SignBuilder.BuildAssembly(boards, def, 0f, detail, default);
        }

        private static void Template()
        {
            GuideTemplate t = StandBoxes.Template;
            Program.Check(t.Id == StandBoxes.TemplateId && t.Group == "custom" && t.Standalone && !t.IsMarking, "a board of the player's tab, listed by itself");
            foreach (string key in new[] { "text", "font", "face", "h", "sp", "ls", "align", "glow", "frame", "margin", "depth", "tilt", "lift" })
                Program.Check(t.FieldOf(key) != null, "has the field " + key);
            GuideSpec d = t.Parse("");
            Program.Check(d.Get("face") == "ground" && d.Get("tilt") == "10" && d.Get("lift") == "30" && d.Get("depth") == "20" && d.Get("glow") == "warm" && d.Get("frame") == "steel", "the defaults");
            Program.Check(SignCatalog.ById(StandBoxes.TemplateId) != null && SignCatalog.ById(TextBoxes.TemplateId) != null && StandBoxes.Is(t.Hash) && !TextBoxes.Is(t.Hash), "in the catalogue beside the text light box, and told from it");
            GuideSpec bad = t.Parse("face=nonsense;glow=nonsense;frame=nonsense;tilt=9x9x;lift=;depth=;text=" + new string('字', 60));
            Program.Check(bad.Get("face") == "ground" && bad.Get("glow") == "warm" && bad.Get("frame") == "steel", "a value that is not a choice is the default");
            Program.Check(bad.Get("text").Length <= 40 && bad.Get("tilt").Length <= 2, "a text and a number are cut to their length: " + bad.Get("text").Length + ", " + bad.Get("tilt"));

            bool before = StandBoxes.Enabled;
            StandBoxes.Enabled = false;
            Program.Check(!t.Standalone, "off takes it out of the catalogue");
            StandBoxes.Enabled = before;
            StandBoxes.RefreshFonts();
            Program.Check(t.FieldOf("font").Choices.Length == 1 + TextFonts.Files.Count, "the font choices follow the folder");
            Program.Check(StandBoxes.Framed(SupportKind.SinglePole) && StandBoxes.Framed(SupportKind.DoublePole) && StandBoxes.Framed(SupportKind.TriplePole), "the post kinds are framed");
            foreach (SupportKind other in new[] { SupportKind.Cantilever, SupportKind.DoubleCantilever, SupportKind.FCantilever, SupportKind.Gantry, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.WallFlat, SupportKind.WallBracket })
                Program.Check(!StandBoxes.Framed(other), other + " is not a post kind: no frame");
        }

        private static void Box()
        {
            SignInstance a = Instance(Spec("立式灯箱", "margin=8").ToString()), b = Instance(Spec("立式灯箱", "margin=20").ToString());
            GuideBoard board = a.Board;
            Program.Check(board.Stand != null && board.Text != null && board.Text.Layout != null && board.Text.Stand == 0f, "the board has the stand's settings and the text's, the text stands on the face");
            float edge = 0.08f + StandBoxes.Bezel;
            Program.Near(board.Main.W, board.Text.Layout.Width + 2f * edge, 1e-4f, "the box is the text's width and a margin and a bezel each side");
            Program.Near(board.Main.H, board.Text.Layout.Height + 2f * edge, 1e-4f, "and its height");
            Program.Near(b.Board.Main.W - a.Board.Main.W, 2f * 0.12f, 1e-4f, "a larger margin is a larger box: " + (b.Board.Main.W - a.Board.Main.W));
            Program.Near(board.Text.Reach, board.Stand.Depth, 1e-6f, "the face is the housing's depth in front of its back");
            Program.Near(board.Stand.Depth, 0.2f, 1e-6f, "the depth is 20 cm");

            SignInstance steep = Instance(Spec("立式灯箱", "tilt=45;lift=500;margin=1;depth=3").ToString());
            Program.Check(steep.Board.Stand.TiltDeg == StandBoxes.MaxTilt && steep.Board.Stand.Lift == 2f && Math.Abs(steep.Board.Stand.Margin - 0.02f) < 1e-6f && Math.Abs(steep.Board.Stand.Depth - 0.08f) < 1e-6f, "tilt, lift, margin and depth are held to their ranges");
            Program.Check(steep.Def.BottomClearance == 2f && Instance(Spec().ToString()).Def.BottomClearance == 0.3f, "the support's clearance is the box's lower edge: " + steep.Def.BottomClearance);
            Program.Check(steep.Def.InkArea == null, "the support planner counts the whole box, not the ink");
            Program.Check(Instance(Spec("立式灯箱", "face=letters").ToString()).Board.Stand.LitGround == false && board.Stand.LitGround, "the way of lighting is read");
            Program.Check(StandBoxes.FrameOf("black") == SolidColour.Black && StandBoxes.FrameOf("white") == SolidColour.White && StandBoxes.FrameOf("gray") == SolidColour.DarkGray && StandBoxes.FrameOf("gold") == SolidColour.Gold && StandBoxes.FrameOf("x") == SolidColour.Steel, "the housing colours");
            SignInstance empty = Instance(Spec("", "").ToString());
            Program.Check(empty.Board.Main.W >= StandBoxes.MinBox && empty.Board.Main.H >= StandBoxes.MinBox, "no text: a box of a least size");
        }

        private static float YMax(MeshBuilder m) => m.Positions.Max(p => p.Y);

        private static void Leans()
        {
            foreach (float tilt in new[] { 0f, 10f, 20f, 30f })
            {
                SignInstance instance = Instance(Spec("立式灯箱", "tilt=" + tilt).ToString());
                SignParts parts = Parts(instance);
                Program.Check(parts.Stand != null, "a post gives it a frame");
                StandPlan plan = parts.Stand;
                MeshBuilder frame = parts.Solids[(int)instance.Board.Stand.Frame];
                float w = instance.Board.Main.W, h = instance.Board.Main.H, d = instance.Board.Stand.Depth;
                float c = (float)Math.Cos(plan.TiltRad), s = (float)Math.Sin(plan.TiltRad);
                float highest = YMax(frame), nearest = frame.Positions.Min(p => p.Z), lowest = frame.Positions.Min(p => p.Y);
                // The top front corner of the housing and its bezel (0.006 proud) is the highest; the top back corner the furthest back; the lower back edge the lowest.
                Program.Near(lowest, plan.YA, 1e-4f, $"tilt {tilt}: the lower back edge stays at the box's height: {lowest:0.0000} against {plan.YA:0.0000}");
                Program.Near(nearest, plan.ZA - h * s, 2e-3f, $"tilt {tilt}: the top back corner leans back by H sin(tilt)");
                Program.Near(highest, plan.YA + h * c + (d + 0.006f) * s, 3e-3f, $"tilt {tilt}: the top front corner is as high as the lean gives");
                // The face leans up: its normal is the tilt up from the facing.
                MeshBuilder lit = parts.Solids[(int)instance.Board.Text.Glow];
                int front = 0;
                for (int i = 0; i < lit.VertexCount; i++)
                {
                    Vec3 n = lit.Normals[i];
                    if (n.Z > 0.5f)
                    {
                        front++;
                        Program.Check(Math.Abs(n.Y - s) < 1e-4f && Math.Abs(n.Z - c) < 1e-4f && Math.Abs(n.X) < 1e-5f, $"tilt {tilt}: a front normal is (0, {s:0.000}, {c:0.000}): {n}");
                    }
                }

                Program.Check(front > 0, "the lit panel has a front");
                PickShape pick = parts.Picks.Find(p => p.Name == "board");
                Program.Check(pick != null && Math.Abs(pick.Tilt + plan.TiltRad) < 1e-6f && Math.Abs(pick.TiltY - plan.YA) < 1e-6f && Math.Abs(pick.TiltZ - plan.ZA) < 1e-6f, "the pick leans with the box, about its lower back edge");
            }
        }

        /// <summary>A point of the world turned back into the box's own frame (the tilt undone about the lower back edge): (x, y up the face, z in front of the back).</summary>
        private static Vec3 Local(StandPlan plan, Vec3 p)
        {
            float s = (float)Math.Sin(plan.TiltRad), c = (float)Math.Cos(plan.TiltRad), dy = p.Y - plan.YA, dz = p.Z - plan.ZA;
            return new Vec3(p.X, dy * c - dz * s, dz * c + dy * s);
        }

        private static void Faces()
        {
            foreach (string face in new[] { "ground", "letters" })
            {
                SignInstance instance = Instance(Spec("立式灯箱", "tilt=15;face=" + face).ToString());
                SignParts parts = Parts(instance);
                StandPlan plan = parts.Stand;
                StandBoard stand = instance.Board.Stand;
                MeshBuilder glow = parts.Solids[(int)instance.Board.Text.Glow], dark = parts.Solids[(int)SolidColour.Black];
                MeshBuilder lit = stand.LitGround ? glow : dark, ink = stand.LitGround ? dark : glow;
                Program.Check(dark != null && glow != null && glow != dark, face + ": a lit mesh and a dark one");
                // The lit panel is a thin box; the letters are flat shapes in one plane in front of it, inside the bezel.
                float w = instance.Board.Main.W, h = instance.Board.Main.H, d = stand.Depth, b = StandBoxes.Bezel;
                float panelFront = d - 0.016f + 0.002f;
                float zInk = d - 0.016f + 0.002f + 0.002f;
                List<Vec3> flat = new List<Vec3>();
                for (int i = 0; i < ink.VertexCount; i++)
                {
                    Vec3 local = Local(plan, ink.Positions[i]);
                    if (Math.Abs(local.Z - zInk) < 1e-3f)
                        flat.Add(local);
                }

                Program.Check(flat.Count > 100, face + ": the letters are flat shapes in the plane in front of the panel: " + flat.Count + " of " + ink.VertexCount + " vertices");
                float cx = parts.Picks.Find(p => p.Name == "board").CentreX;
                float xs = flat.Min(p => p.X), xe = flat.Max(p => p.X), ys = flat.Min(p => p.Y), ye = flat.Max(p => p.Y);
                Program.Check(xs >= cx - w * 0.5f + b - 1e-3f && xe <= cx + w * 0.5f - b + 1e-3f && ys >= b - 1e-3f && ye <= h - b + 1e-3f, $"{face}: all the letters are inside the bezel's opening ({xs:0.00} to {xe:0.00}, {ys:0.00} to {ye:0.00} of {w:0.00} x {h:0.00})");
                Program.Check(flat.Min(p => p.X) < cx - 0.05f && flat.Max(p => p.X) > cx + 0.05f, face + ": the text is spread across the face, not in a corner");

                // The lit panel is behind the letters.
                float panel = lit.Positions.Count(p => Math.Abs(Local(plan, p).Z - panelFront) < 1e-3f);
                Program.Check(panel >= 4, face + ": the lit panel's front is behind the letters: " + panel);

                // A letter reads the right way round: the text light box's own test of orientation: the first character's glyph is at the viewer's left (the sign's +X), as the board is seen from +Z.
                TextPlanResult plan2 = TextPlans.Peek(instance.Board.Text.Job);
                Program.Check(plan2 != null && plan2.Meshes != null, "the plan is made");
                PlacedGlyph first = plan2.Layout.Glyphs[0], last = plan2.Layout.Glyphs[plan2.Layout.Glyphs.Count - 1];
                Program.Check(first.X < last.X, "the layout runs left to right as the viewer sees it");
                float xFirst = cx - first.X, xLast = cx - last.X;
                Program.Check(xFirst > xLast, "so the first character is at a larger sign-frame X than the last (the viewer's left is +X)");
                Program.Check(flat.Count(p => p.X > cx) > 0 && flat.Count(p => p.X < cx) > 0, face + ": letters on both sides of the middle");
            }
        }

        private static void Mirror()
        {
            // Text left-aligned: a short line over a long one. As the viewer sees the face the short line's letter is at the left edge; the viewer's left is +X in the sign's frame, so its x is above the box's middle.
            SignInstance instance = Instance(Spec("口\\n口口口口口口", "align=left;face=ground;tilt=0;h=0.3").ToString());
            SignParts parts = Parts(instance);
            PickShape pick = parts.Picks.Find(p => p.Name == "board");
            MeshBuilder ink = parts.Solids[(int)SolidColour.Black];
            float mid = pick.CentreY, cx = pick.CentreX;
            var upper = new List<Vec3>(); var lower = new List<Vec3>();
            for (int i = 0; i < ink.VertexCount; i++)
            {
                Vec3 p = ink.Positions[i];
                if (Math.Abs(p.Z - (parts.Stand.ZA + instance.Board.Stand.Depth - 0.016f + 0.002f + 0.002f)) > 1e-3f)
                    continue;
                (p.Y > mid ? upper : lower).Add(p);
            }

            Program.Check(upper.Count > 10 && lower.Count > 50, "the two lines have their letters: " + upper.Count + ", " + lower.Count);
            float upperX = upper.Average(p => p.X) - cx, lowerX = lower.Average(p => p.X) - cx;
            Program.Check(upperX > 0.1f && Math.Abs(lowerX) < upperX, $"the first line's letter is at the viewer's left, a larger x than the middle: {upperX:0.00}, and the long line is about centred: {lowerX:0.00}");
        }

        private static void Frames()
        {
            foreach (string support in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole })
            {
                SignInstance instance = Instance(Spec("立式灯箱", "tilt=20;h=0.4").ToString());
                SignParts parts = Parts(instance, SupportDetail.Full, support);
                StandPlan plan = parts.Stand;
                Program.Check(plan != null && plan.FrameCount == parts.Layout.PoleX.Length, support + ": a frame at each post of the support: " + plan?.FrameCount);
                Program.Check(parts.Feet.Count == 2 * plan.FrameCount, "two feet each: " + parts.Feet.Count);
                for (int i = 0; i < plan.FrameCount; i++)
                    Program.Check(plan.ClosureError(i) <= 1e-3f, support + ": frame " + i + " closes");
                for (int k = 0; k < parts.Feet.Count; k++)
                {
                    PostFoot foot = parts.Feet[k];
                    StandFoot spec = plan.Feet[k];
                    Program.Check(Math.Abs(foot.X - spec.X) < 1e-6f && Math.Abs(foot.Z - spec.Z) < 1e-6f && foot.PadHalf == spec.PadHalf, "foot " + k + " is where the plan put it");
                    Program.Check(foot.MetalTo > foot.MetalFrom && foot.ConcreteTo > foot.ConcreteFrom && foot.TubeTo > foot.TubeFrom, "foot " + k + " owns its plate, its concrete and its tube");
                }

                // Nothing of the frame (metal) is in front of the housing's back plane but the arms' ends, which are in its back plate.
                MeshBuilder metal = parts.Metal;
                float worst = float.MinValue;
                for (int i = 0; i < metal.VertexCount; i++)
                {
                    Vec3 p = metal.Positions[i];
                    float from = plan.FromBackPlane(p.Z, p.Y);
                    if (p.Z < -1e-3f)
                        Program.Check(from < -0.01f, support + ": a vertex behind the post is clear of the housing: " + from);
                    worst = Math.Max(worst, from);
                }

                Program.Check(worst <= 0.06f, support + ": the arms end in the back plate and nothing goes further into the housing: " + worst);
                Program.Check(worst > 0f, support + ": and the arms do reach it");
                Program.Check(parts.Picks.Count(p => p.Name == "foot") == plan.FrameCount && parts.Picks.Count(p => p.Name == "post") == plan.FrameCount, "a foot and a post to point at for each frame");
                foreach (StandMember m in plan.Members)
                {
                    float reach = (m.Round ? m.Half : m.Half * 1.7321f) + 2e-3f;
                    bool a = metal.Positions.Any(p => (p - m.A).Length <= reach);
                    bool b = metal.Positions.Any(p => (p - m.B).Length <= reach);
                    Program.Check(a && b, support + ": the " + m.Kind + " of frame " + m.Frame + " is built, end to end");
                }

                foreach (StandMember post in plan.Members.Where(m => m.Kind == StandMemberKind.Post))
                    Program.Check(metal.Positions.Any(p => p.Y > post.B.Y + 0.004f && Math.Abs(p.X - post.A.X) <= post.Half + 0.01f && Math.Abs(p.Z) <= post.Half + 0.01f), support + ": the post has its cap above its top");
                Program.Check(metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)), "every coordinate is a number");
                Program.Check(parts.Min.Y < 0f && parts.Max.Y > plan.YA + plan.Height * (float)Math.Cos(plan.TiltRad), "the box's bounds go from the pads' concrete to the top of the housing");

                // The frame is made of closed solids but for the ends of its upright tubes that a plate, the beam or the cap covers: every edge that is open is a level ring at the plates', the beam's or the post's top height.
                List<(Vec3 A, Vec3 B)> open = OpenEdges(metal);
                int stray = open.Count(e => Math.Abs(e.A.Y - e.B.Y) > 1e-4f || !(Math.Abs(e.A.Y - plan.PlateTop) < 1e-4f || Math.Abs(e.A.Y - plan.BeamY) < 1e-4f || Math.Abs(e.A.Y - plan.PostTop) < 1e-4f));
                Program.Check(stray == 0, support + ": the open edges of the frame are the covered ends of the upright tubes: " + stray + " of " + open.Count + " are not");
                Program.Check(OpenEdges(parts.Concrete).Count == 0, "the concrete is closed");
                MeshBuilder frame = parts.Solids[(int)instance.Board.Stand.Frame];
                Program.Check(OpenEdges(frame).Count == 0, "and the housing: " + OpenEdges(frame).Count);
            }
        }

        private static List<(Vec3 A, Vec3 B)> OpenEdges(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));
                if (!ids.TryGetValue(key, out int id))
                    ids[key] = id = ids.Count;
                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            var where = new Dictionary<(int, int), (Vec3, Vec3)>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 pa = mesh.Positions[mesh.Indices[t]], pb = mesh.Positions[mesh.Indices[t + 1]], pc = mesh.Positions[mesh.Indices[t + 2]];
                int a = Id(pa), b = Id(pb), c = Id(pc);
                foreach (((int, int) e, Vec3 u, Vec3 v) in new[] { ((a, b), pa, pb), ((b, c), pb, pc), ((c, a), pc, pa) })
                {
                    var key = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
                    edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                    where[key] = (u, v);
                }
            }

            // An edge that one triangle has is on the border: a solid that touches another along an edge has four, and is not open.
            return edges.Where(e => e.Value == 1).Select(e => where[e.Key]).ToList();
        }

        private static GroundProfile Profile(Func<float, float, float> ground) => GroundProfile.Sample(ground, -12f, 30f);

        private static MeshBuilder Grounded(MeshBuilder source, int[] tags, float[] top, float[] bottom)
        {
            var mesh = new MeshBuilder();
            PostGround.Append(mesh, source, tags, top, bottom, Vec3.Zero, 0f);
            return mesh;
        }

        private static void Ground()
        {
            SignInstance instance = Instance(Spec("立式灯箱", "tilt=15;lift=40").ToString());
            SignParts parts = Parts(instance, SupportDetail.Full, SupportCatalog.DoublePole);
            StandPlan plan = parts.Stand;
            MeshBuilder housing = parts.Solids[(int)instance.Board.Stand.Frame];
            float housingLow = housing.Positions.Min(p => p.Y);

            // A ground 0.2 m lower than the item's and a slope along the stand's depth: every foot stands on its own ground; the housing stays where it was.
            GroundProfile slope = Profile((x, z) => -0.2f + 0.1f * z);
            PostGround.Offsets(parts, slope, out float[] top, out float[] bottom);
            Program.Check(top.Length == parts.Feet.Count && PostGround.Moves(top, bottom), "the feet move on this ground");
            for (int k = 0; k < parts.Feet.Count; k++)
                Program.Near(top[k], -0.2f + 0.1f * Math.Max(-0.8f, Math.Min(0.8f, parts.Feet[k].Z)), 2e-3f, "foot " + k + " takes the ground under it: " + top[k]);
            parts.FootTags(out int[] metalTags, out int[] concreteTags, out int[][] solidTags);
            MeshBuilder metal = Grounded(parts.Metal, metalTags, top, bottom);
            MeshBuilder concrete = Grounded(parts.Concrete, concreteTags, top, bottom);
            Program.Near(housing.Positions.Min(p => p.Y), housingLow, 1e-6f, "the housing is not moved by the ground");
            for (int k = 0; k < parts.Feet.Count; k++)
            {
                PostFoot foot = parts.Feet[k];
                int upper = 0, lower = 0;
                for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
                {
                    float was = parts.Metal.Positions[i].Y, now = metal.Positions[i].Y;
                    if (was < foot.Split)
                    {
                        lower++;
                        Program.Near(now - was, top[k], 1e-5f, "foot " + k + ": the tube's lower end goes with the ground");
                    }
                    else
                    {
                        upper++;
                        Program.Near(now, was, 1e-6f, "foot " + k + ": and its upper end stays");
                    }
                }

                Program.Check(upper > 0 && lower > 0, "foot " + k + ": a tube has an upper and a lower end: " + upper + ", " + lower);
            }
            for (int f = 0; f < plan.FrameCount; f++)
            {
                StandMember post = plan.Find(StandMemberKind.Post, f);
                int front = 2 * f;
                // The post's top is where it was (it reaches the box's back); its lower end follows its foot.
                float topY = metal.Positions.Where(p => Math.Abs(p.X - post.A.X) < post.Half + 1e-3f && Math.Abs(p.Z) < post.Half + 1e-3f).Max(p => p.Y);
                Program.Near(topY, plan.PostTop + 0.0f, 0.02f, "the post's top stays at the box's back: " + topY);
                float lowest = metal.Positions.Where(p => Math.Abs(p.X - post.A.X) < 0.17f && Math.Abs(p.Z) < 0.17f).Min(p => p.Y);
                Program.Check(lowest < parts.Feet[front].PadBottom || lowest <= top[front] + 0.011f, "the post's pad goes down with its foot: " + lowest + " against " + top[front]);
            }

            // The concrete under a pad goes down to the lowest ground under it.
            float concreteBottom = concrete.Positions.Min(p => p.Y);
            Program.Check(concreteBottom <= bottom.Min() + PostGround.MaxDrop, "the concrete is below the ground");

            // On a ground higher than the box allows the foot is held: it does not lift the housing's lower edge into the ground.
            GroundProfile hill = Profile((x, z) => 1.4f);
            PostGround.Offsets(parts, hill, out float[] hillTop, out _);
            foreach (PostFoot foot in parts.Feet)
                Program.Check(foot.MaxRaise <= instance.Board.Stand.Lift - 0.12f + 1e-5f && foot.MaxRaise >= 0f, "a foot may rise only until the box's lower edge is 12 cm above the ground: " + foot.MaxRaise);
            Program.Check(hillTop.All(t => t <= parts.Feet.Max(f => f.MaxRaise) + 1e-5f), "so the feet are held on a hill");
            Program.Check(parts.Feet.All(f => f.MaxRaise < PostGround.MaxRaise), "and less than the most a post may rise");
            PostGround.Offsets(parts, null, out float[] none, out _);
            Program.Check(none.All(v => v == 0f), "no profile: no foot moves");
        }

        private static void Supports()
        {
            SignInstance instance = Instance(Spec("立式灯箱", "tilt=20").ToString());
            foreach (string support in new[] { SupportCatalog.WallFlat, SupportCatalog.Cantilever, SupportCatalog.Gantry })
            {
                SignParts parts = Parts(instance, SupportDetail.Full, support);
                Program.Check(parts.Stand == null, support + ": no stand frame on a support that is not a post kind");
                MeshBuilder frame = parts.Solids[(int)instance.Board.Stand.Frame];
                Program.Check(frame != null && frame.VertexCount > 100, support + ": the box is built all the same");
                float lean = frame.Positions.Max(p => Math.Abs(p.Z - frame.Positions[0].Z));
                float depth = instance.Board.Stand.Depth;
                Program.Check(lean < depth + 0.05f, support + ": and upright, flat on it (it does not lean): its depth spans " + lean);
                PickShape pick = parts.Picks.Find(p => p.Name == "board");
                Program.Check(pick != null && pick.Tilt == 0f, support + ": its pick does not lean");
            }

            SignParts plain = Parts(instance, SupportDetail.Full, SupportCatalog.SinglePole);
            Program.Check(plain.Stand != null && plain.Metal.VertexCount > 200, "on a post: the frame: " + plain.Metal.VertexCount);
            foreach (string post in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole })
            {
                SignParts again = Parts(instance, SupportDetail.Full, post);
                Program.Check(again.Stand.FrameCount == again.Layout.PoleX.Length, post + ": its posts are the frames' x");
            }

            // Two builds of the same box are the same.
            SignParts one = Parts(instance), two = Parts(instance);
            Program.Check(one.VertexCount == two.VertexCount && one.Metal.VertexCount == two.Metal.VertexCount, "built twice, the same");
        }

        private static void Details()
        {
            TextPlans.Clear();
            SignInstance instance = Instance(Spec("路边灯箱").ToString());
            SignParts full = Parts(instance, SupportDetail.Full);
            SignParts reduced = Parts(instance, SupportDetail.Reduced);
            Program.Check(reduced.VertexCount < full.VertexCount && reduced.Stand != null, $"reduced detail has fewer vertices and the same frame: {reduced.VertexCount} against {full.VertexCount}");
            Program.Check(reduced.Feet.Count == full.Feet.Count && reduced.Picks.Count(p => p.Name == "board") == 1, "the same feet and the same pick");
            MeshBuilder ink = reduced.Solids[(int)SolidColour.Black];
            // 0.12.105: the standing box's letters are flat faces in every tile (a far tile no longer has bars): the same ink as the full detail's.
            MeshBuilder fullInk = full.Solids[(int)SolidColour.Black];
            Program.Check(ink != null && fullInk != null && ink.VertexCount == fullInk.VertexCount && ink.VertexCount > 24 && ink.VertexCount <= SignBuilder.MaxFarTextVertices, "reduced: the letters are their faces, as in the full detail: " + ink?.VertexCount);
            Program.Check(!reduced.Provisional, "a reduced build is not provisional");

            string spec = Spec("路边灯箱").ToString();
            uint post = SupportPlanner.Auto(instance.Def).Hash;
            var record = new ItemRecord(Def.Hash, new Vec3(10f, 0f, 20f), 0.3f, post, spec, 0f, 0u, 0, 0, 0f);
            List<ChunkPart> near = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(TileTier.Near), out int skipped);
            Program.Check(skipped == 0 && near.Count > 0, "in a near tile");
            ChunkPart steel = near.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.Steel));
            Program.Check(steel != null && steel.Mesh.Positions.All(p => Math.Abs(p.X - 10f) < 6f && Math.Abs(p.Z - 20f) < 6f), "the housing is at the item's place");
            List<ChunkPart> far = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(TileTier.Far), out _);
            Program.Check(far.Count > 0 && far.Sum(p => p.Mesh.VertexCount) < near.Sum(p => p.Mesh.VertexCount), "a far tile is smaller");

            // A far tile has the letters up to MaxFarTextVertices and the bars over it (0.12.105); a near tile has the letters whatever their number. Forty of the most intricate characters are over it.
            TextPlans.Clear();
            SignInstance heavy = Instance(Spec(string.Concat(Enumerable.Repeat("龘", 40))).ToString());
            SignParts heavyFull = Parts(heavy, SupportDetail.Full), heavyFar = Parts(heavy, SupportDetail.Reduced);
            int heavyFaces = SignBuilder.TextFaceVertices(TextPlans.Peek(heavy.Board.Text.Job));
            Program.Check(!SignBuilder.FarFacesFit(heavyFaces), $"forty intricate characters are over the far limit: {heavyFaces}");
            MeshBuilder heavyInk = heavyFull.Solids[(int)SolidColour.Black], heavyBars = heavyFar.Solids[(int)SolidColour.Black];
            Program.Check(heavyInk != null && heavyInk.VertexCount == heavyFaces && heavyBars != null && heavyBars.VertexCount < 400 && !heavyFar.Provisional, $"a near tile has all their faces ({heavyInk?.VertexCount}), a far one the bars ({heavyBars?.VertexCount}), finished");

            // A main thread that asks for a plan that is not made: bars, provisional.
            TextPlans.Clear();
            TextPlans.MarkMainThread();
            try
            {
                SignInstance fresh = Instance(Spec("临时灯箱").ToString());
                SignParts provisional = Parts(fresh);
                Program.Check(provisional.Provisional && provisional.Stand != null, "asked on the main thread with no plan: provisional, with its frame");
                MeshBuilder bars = provisional.Solids[(int)SolidColour.Black];
                Program.Check(bars != null && bars.VertexCount < 400, "bars stand in for the letters: " + bars?.VertexCount);
            }
            finally
            {
                TextPlans.ForgetMainThread();
            }
        }

        private static void Thumbnail()
        {
            SignInstance instance = Instance(Spec("立式灯箱").ToString());
            int s = Thumbnails.Size;
            byte[] picture = new byte[16 * 16 * 4];
            for (int i = 0; i < picture.Length; i++)
                picture[i] = 200;
            byte[] rgba = Thumbnails.SignRgba(instance, picture, 16, 16);
            Program.Check(rgba.Length == s * s * 4, "a square RGBA picture");
            int opaque = 0;
            for (int i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] > 200)
                    opaque++;
            }

            Program.Check(rgba[3] == 0 && rgba[(s * s - 1) * 4 + 3] == 0, "the corners are clear");
            Program.Check(opaque > s * s / 20, "the box and its posts are drawn: " + opaque + " opaque pixels of " + s * s);
        }

        private static void HintLines()
        {
            Program.Check(StandBoxes.Hints(Spec("立式灯箱").ToString(), true).Count == 0, "nothing to say of a good box");
            List<string> typed = StandBoxes.Hints(Spec("立式灯箱", "tilt=45").ToString(), true);
            Program.Check(typed.Count == 1 && typed[0].Contains("30"), "a tilt over the range is said to be held: " + string.Join("|", typed));
            Program.Check(StandBoxes.Hints(Spec("立式灯箱", "tilt=45").ToString(), false)[0].Contains("30"), "in English too");
            Program.Check(StandBoxes.TypedTilt(Spec("立式灯箱", "tilt=45")) == 45f && StandBoxes.TypedTilt(StandBoxes.Template.Parse("")) == 10f, "the typed tilt is read as typed");

            StandPlan plan = StandPlanner.Plan(new StandSpec { Width = 2f, Height = 1f, Depth = 0.2f, TiltDeg = 10f, Lift = 0.3f, FrameX = new[] { -0.8f, 0.8f } });
            Program.Check(StandBoxes.FeetNote(plan, new float[] { 0f, 0f, 0f, 0f }) == "", "no note for level ground");
            string note = StandBoxes.FeetNote(plan, new float[] { 0.3f, -0.3f, 0.7f, 0f });
            Program.Check(note.Contains("foot 2 +0.70 m from the ground it was drawn for") && note.Contains("frame at foot 0: its two feet differ by 0.60 m") && note.Contains("frame at foot 2: its two feet differ by 0.70 m"), "a note for a foot that is far off, and for its frame: " + note);
            var lines = new List<string>();
            Action<string> before = StandBoxes.Log;
            StandBoxes.Log = lines.Add;
            try
            {
                StandBoxes.NoteFeet(null, null);
                StandBoxes.NoteFeet(plan, new float[] { 0f, 0f, 0f, 0f });
                Program.Check(lines.Count == 0, "level ground, no plan: nothing is logged");
                for (int i = 0; i < 30; i++)
                    StandBoxes.NoteFeet(plan, new float[] { 0f, 0f, 0.7f, 0f });
                Program.Check(lines.Count >= 1 && lines.Count <= 12, "a far foot is logged, and not more than twelve times: " + lines.Count);
            }
            finally
            {
                StandBoxes.Log = before;
            }
        }

        private static void TiltControls()
        {
            GuideField tilt = StandBoxes.Template.FieldOf("tilt");
            Program.Check(tilt.HasSlider && tilt.SliderMin == -StandBoxes.MaxTilt && tilt.SliderMax == StandBoxes.MaxTilt && tilt.SliderStep == 1.0 && tilt.Negative, "the tilt has a signed slider from the depression's greatest to the elevation's greatest in whole degrees");
            Program.Check(tilt.Kind == GuideFieldKind.Text && tilt.DigitsOnly, "and stays a number typed as text, as the batch interface and the spec know it");
            Program.Check(!StandBoxes.Template.FieldOf("lift").HasSlider && !StandBoxes.Template.FieldOf("depth").HasSlider && !TextBoxes.Template.FieldOf("h").HasSlider, "no other field has one");
            Program.Check(!new GuideField().HasSlider && !new GuideField { SliderMin = 0, SliderMax = 10, SliderStep = 0 }.HasSlider && !new GuideField { SliderMin = 5, SliderMax = 5, SliderStep = 1 }.HasSlider, "a field with no step, or an empty range, has none");
            Program.Check(StandBoxes.Template.Parse("tilt=17").Get("tilt") == "17" && StandBoxes.Template.Parse("").With("tilt", "25").ToString().Contains("tilt=25"), "the value is written in the spec as any digits field");

            // The keys: the - and = keys are the tool's, so a game action on them is held while the tool is active.
            List<GameKeyBinding> game = new List<GameKeyBinding>
            {
                new GameKeyBinding("Shortcuts", "Zoom Out", "<Keyboard>/minus"), new GameKeyBinding("Shortcuts", "Zoom In", "<Keyboard>/equals"),
                new GameKeyBinding("Shortcuts", "Quicksave", "<Keyboard>/f5"), new GameKeyBinding("Photo mode", "Toggle", "<Keyboard>/period")
            };
            ShortcutPlan plan = ShortcutPolicy.Plan(game);
            Program.Check(plan.Held.Contains(new GameActionRef("Shortcuts", "Zoom Out")) && plan.Held.Contains(new GameActionRef("Shortcuts", "Zoom In")), "the game's actions on - and = are held while the tool is active");
            Program.Check(plan.Held.Contains(new GameActionRef("Photo mode", "Toggle")), "and the photo mode on the > key still is");
            Program.Check(!plan.Held.Contains(new GameActionRef("Shortcuts", "Quicksave")) || ShortcutPolicy.KeepsInShortcuts("Quicksave") == false, "what was kept is kept");
            Program.Check(ShortcutPolicy.ToolKeys.Any(k => k.Path == "<Keyboard>/minus" && !k.WithCtrl) && ShortcutPolicy.ToolKeys.Any(k => k.Path == "<Keyboard>/equals" && !k.WithCtrl), "both are among the tool's keys");

            // The key card lists them for a standing light box, and not for any other board.
            foreach (bool zh in new[] { true, false })
            {
                Program.Check(HintText.Keys(new HintInfo { Kind = "place", Tilt = true }, zh).Exists(k => k[0] == "-  ="), "placing a standing light box lists - and = (" + (zh ? "zh" : "en") + ")");
                Program.Check(!HintText.Keys(new HintInfo { Kind = "place" }, zh).Exists(k => k[0] == "-  ="), "another board does not");
                Program.Check(HintText.Keys(new HintInfo { Kind = "move", Tilt = true }, zh).Exists(k => k[0] == "-  ="), "editing one lists them too");
                Program.Check(!HintText.Keys(new HintInfo { Kind = "move" }, zh).Exists(k => k[0] == "-  ="), "and editing another does not");
            }

            Program.Check(!new HintInfo { Kind = "place", Tilt = true }.Equals(new HintInfo { Kind = "place" }) && new HintInfo { Kind = "place", Tilt = true }.Equals(new HintInfo { Kind = "place", Tilt = true }), "the line of help is made again when the tilt's keys come or go");
        }

        private static void Numbers()
        {
            var report = new List<string>();
            foreach ((string text, string extra, string support) in new[]
            {
                ("立式灯箱", "", SupportCatalog.SinglePole),
                ("立式灯箱", "h=0.6", SupportCatalog.DoublePole),
                ("欢迎光临立式灯箱欢迎光临立式灯箱", "h=0.5", SupportCatalog.TriplePole),
                ("一二三四五六七八九十一二\\n一二三四五六七八九十一二\\n一二三四五六七八九十一二", "h=0.3", SupportCatalog.TriplePole)
            })
            {
                SignInstance instance = Instance(Spec(text, extra).ToString());
                SignParts full = Parts(instance, SupportDetail.Full, support), reduced = Parts(instance, SupportDetail.Reduced, support);
                report.Add($"{text.Replace("\\n", "/")} {extra}: {full.Stand.FrameCount} frame(s), {instance.Board.Main.W:0.00} x {instance.Board.Main.H:0.00} m, {full.VertexCount} vertices at Near, {reduced.VertexCount} reduced, about {full.Stand.EstimatedKg:0} kg");
                Program.Check(full.VertexCount < 40000, "a stand is under 40,000 vertices at Near: " + full.VertexCount);
                Program.Check(reduced.VertexCount < full.VertexCount, "reduced is smaller");
            }

            foreach (string line in report)
                Console.WriteLine("  stand box: " + line);

            // The most points a text can have: 36 of the most complicated characters the font has.
            string heavy = "鬱爨龘鑫麤警戀籲疆驚鬱爨龘鑫麤警戀籲疆驚鬱爨龘鑫麤警戀籲疆驚鬱爨龘鑫麤警戀籲疆驚".Substring(0, 36);
            SignInstance complex = Instance(Spec(heavy.Substring(0, 12) + "\n" + heavy.Substring(12, 12) + "\n" + heavy.Substring(24, 12), "h=0.3").ToString());
            SignParts heavyParts = Parts(complex, SupportDetail.Full, SupportCatalog.TriplePole);
            int missing = complex.Board.Text.Layout.Glyphs.Count(g => g.Missing);
            Console.WriteLine($"  stand box: 36 complicated characters at 0.3 m ({missing} missing from the font): {heavyParts.VertexCount} vertices at Near, {Parts(complex, SupportDetail.Reduced, SupportCatalog.TriplePole).VertexCount} reduced");
            Program.Check(heavyParts.VertexCount < 40000, "even then under 40,000 vertices: " + heavyParts.VertexCount);

            // The same box in a tile of each tier: what the renderer is given.
            string spec = Spec("欢迎光临立式灯箱", "h=0.5").ToString();
            SignInstance tiled = Instance(spec);
            uint post = SupportPlanner.Auto(tiled.Def).Hash;
            var record = new ItemRecord(Def.Hash, new Vec3(10f, 0f, 20f), 0.3f, post, spec, 0f, 0u, 0, 0, 0f);
            var counts = new Dictionary<TileTier, int>();
            foreach (TileTier tier in new[] { TileTier.Near, TileTier.Mid, TileTier.Far, TileTier.Horizon })
            {
                List<ChunkPart> parts = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(tier), out int skipped);
                counts[tier] = parts.Sum(p => p.Mesh.VertexCount);
                Program.Check(skipped == 0 || tier == TileTier.Horizon, "nothing is skipped in a " + tier + " tile");
            }

            Console.WriteLine($"  stand box: in a tile: Near {counts[TileTier.Near]}, Mid {counts[TileTier.Mid]}, Far {counts[TileTier.Far]}, Horizon {counts[TileTier.Horizon]} vertices");
            Program.Check(counts[TileTier.Near] > 0 && counts[TileTier.Far] > 0 && counts[TileTier.Far] < counts[TileTier.Near], "Far is smaller than Near");
            Program.Check(counts[TileTier.Horizon] == 0, "a stand is not seen at the horizon: the long-reach kinds only (noise walls)");
        }
    }
}
