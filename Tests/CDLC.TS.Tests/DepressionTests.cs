using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The depression angle of the text light box and the standing light box (0.12.80; the owner, 3 Nov: "文字灯牌支持俯角"): the tilt is signed, a negative one makes the letters look down. On a post the
    /// stand's post stands behind the lower back edge and its arms reach out to the back line, on a flat wall the lower edge keeps the spacers' length and the outer rails have the ties, on a roof it is refused;
    /// a tilt of 0 or more is built exactly as before (the golden hashes of TextTiltTests).
    /// </summary>
    internal static class DepressionTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("depression: every board with a tilt of 0 or more is built as the base build (golden hashes)", Golden);
            Program.Run("depression: the signed field, its keys and its range", Field);
            Program.Run("depression: which supports take it", Modes);
            Program.Run("depression: the planner's post stands behind the board and its arms reach the back line", Planner);
            Program.Run("depression: the letters look down on a post, the same letters as upright", OnPost);
            Program.Run("depression: a standing light box looks down on its frames", OnStandBox);
            Program.Run("depression: on a flat wall (spacers longest at the top, ties to the outer rails)", OnWall);
            Program.Run("depression: a roof refuses it and the log says so once", OnRoof);
            Program.Run("depression: the tilt through zero is one range", ThroughZero);
            Program.Run("depression: the keys go through zero into the depression", Keys);
            Program.Run("depression: the batch interface says which field is signed", BatchSigned);
        }

        /// <summary>Hashes of the vertex arrays made by the build of 0.12.79 (the commit before the depression angle), in a temporary worktree: a standing light box and a text light box with a tilt of 0 or more, on each support that takes one.</summary>
        private static readonly (string Support, string Extra, ulong Hash)[] s_StandGolden =
        {
            (SupportCatalog.SinglePole, "", 12748096491169524391UL),
            (SupportCatalog.SinglePole, "tilt=0", 9027748553539351881UL),
            (SupportCatalog.SinglePole, "tilt=10", 12748096491169524391UL),
            (SupportCatalog.SinglePole, "tilt=25", 3938999731309442993UL),
            (SupportCatalog.SinglePole, "tilt=30;face=letters;h=0.8", 1322415044333449881UL),
            (SupportCatalog.DoublePole, "", 8823578387290196087UL),
            (SupportCatalog.DoublePole, "tilt=0", 6662738158627887261UL),
            (SupportCatalog.DoublePole, "tilt=10", 8823578387290196087UL),
            (SupportCatalog.DoublePole, "tilt=25", 7881833686131527637UL),
            (SupportCatalog.DoublePole, "tilt=30;face=letters;h=0.8", 3505629426323685580UL),
            (SupportCatalog.TriplePole, "", 17857064278695878067UL),
            (SupportCatalog.TriplePole, "tilt=0", 10018090776669708605UL),
            (SupportCatalog.TriplePole, "tilt=10", 17857064278695878067UL),
            (SupportCatalog.TriplePole, "tilt=25", 17422230589036946229UL),
            (SupportCatalog.TriplePole, "tilt=30;face=letters;h=0.8", 13732676133996155965UL)
        };

        private static readonly (string Support, string Extra, ulong Hash)[] s_ElevationGolden =
        {
            (SupportCatalog.SinglePole, ";h=0.5;tilt=5", 1364251469358447623UL),
            (SupportCatalog.SinglePole, ";h=0.5;tilt=20", 8072396016824282854UL),
            (SupportCatalog.SinglePole, ";h=0.8;tilt=30;style=outline", 12292804669248283156UL),
            (SupportCatalog.SinglePole, ";h=0.5;tilt=15;tri=1", 8722831825837602169UL),
            (SupportCatalog.DoublePole, ";h=0.5;tilt=5", 2079137460809479020UL),
            (SupportCatalog.DoublePole, ";h=0.5;tilt=20", 10151633571725118881UL),
            (SupportCatalog.DoublePole, ";h=0.8;tilt=30;style=outline", 16204434006779824580UL),
            (SupportCatalog.DoublePole, ";h=0.5;tilt=15;tri=1", 8314999934076226860UL),
            (SupportCatalog.WallFlat, ";h=0.5;tilt=5", 5118050096435652774UL),
            (SupportCatalog.WallFlat, ";h=0.5;tilt=20", 8011425652015536394UL),
            (SupportCatalog.WallFlat, ";h=0.8;tilt=30;style=outline", 16258301701572080444UL),
            (SupportCatalog.WallFlat, ";h=0.5;tilt=15;tri=1", 1136597591314324743UL),
            (SupportCatalog.WallRoof, ";h=0.5;tilt=5", 4455290847613503348UL),
            (SupportCatalog.WallRoof, ";h=0.5;tilt=20", 2238307689142911158UL),
            (SupportCatalog.WallRoof, ";h=0.8;tilt=30;style=outline", 6503438940334663531UL),
            (SupportCatalog.WallRoof, ";h=0.5;tilt=15;tri=1", 8313282039350545218UL)
        };

        private static void Golden()
        {
            foreach ((string support, string extra, ulong golden) in s_StandGolden)
            {
                GuideSpec s = StandBoxes.Template.Parse("").With("text", "立式灯箱");
                foreach (string pair in extra.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                    s = s.With(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
                TextPlans.Clear();
                SignParts p = TextTiltTests.Build(SignInstances.Of(SignCatalog.ById(StandBoxes.TemplateId), s.ToString()), support);
                ulong hash = TextTiltTests.Hash(p);
                Program.Check(hash == golden, $"standing light box on {support} [{extra}]: built as before ({hash} against {golden})");
            }

            foreach ((string support, string extra, ulong golden) in s_ElevationGolden)
            {
                TextPlans.Clear();
                SignParts p = TextTiltTests.Build(TextTiltTests.Instance("文字灯箱文字", extra), support, support == SupportCatalog.WallRoof ? 0.5f : 0f);
                ulong hash = TextTiltTests.Hash(p);
                Program.Check(hash == golden, $"text light box on {support} [{extra}]: built as before ({hash} against {golden})");
            }
        }

        private static string Num(float v) => v.ToString(CultureInfo.InvariantCulture);

        private static void Field()
        {
            foreach (GuideTemplate template in new[] { TextBoxes.Template, StandBoxes.Template })
            {
                GuideField tilt = template.FieldOf("tilt");
                Program.Check(tilt.Negative && tilt.DigitsOnly, template.Id + ": a signed number typed as text");
                Program.Check(tilt.SliderMin == -30.0 && tilt.SliderMax == 30.0 && tilt.SliderStep == 1.0, template.Id + ": a slider from -30 to 30");
                Program.Check(tilt.Sanitize("-12") == "-12" && tilt.Sanitize("12") == "12" && tilt.Sanitize("-") == "-" && tilt.Sanitize("1-2") == "12", template.Id + ": the minus is kept as the first character only: " + tilt.Sanitize("1-2"));
                Program.Check(tilt.Sanitize("-12.5") == "-12" && tilt.Sanitize("--3") == "-3" && tilt.Sanitize("a-b4") == "-4" && tilt.Sanitize("4-") == "4", template.Id + ": and nothing else is: " + tilt.Sanitize("--3") + " " + tilt.Sanitize("a-b4"));
                Program.Check(template.Parse("tilt=-15").Get("tilt") == "-15" && template.Parse("").With("tilt", "-8").ToString().Contains("tilt=-8"), template.Id + ": a negative value is written in the spec as any digits field's");
                Program.Check(tilt.Zh.Length <= 10 && tilt.En.Length <= 28, template.Id + ": the label is short");
            }

            // No other digits field takes a minus: the sign is the tilt's alone.
            foreach (GuideTemplate template in new[] { TextBoxes.Template, StandBoxes.Template })
            {
                foreach (GuideField field in template.Fields.Where(f => f.DigitsOnly && f.Key != "tilt"))
                    Program.Check(!field.Negative && field.Sanitize("-5") == field.Sanitize("5"), template.Id + "." + field.Key + ": no minus");
            }

            Program.Check(TextBoxes.Template.Parse("tilt=-20").Get("tilt") == "-20" && TextBoxes.Template.Parse("tilt=-20").Number("tilt", 0) == -20.0, "the spec reads it back as a number");
        }

        private static void Modes()
        {
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, -10f, false, true, true, false) == TextLeanMode.Stand, "a single post carries a depression");
            Program.Check(TextTilt.ModeOf(SupportKind.DoublePole, false, -10f, false, true, true, false) == TextLeanMode.Stand && TextTilt.ModeOf(SupportKind.TriplePole, false, -10f, false, true, true, false) == TextLeanMode.Stand, "so do a double and a triple post");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, false, -10f, false, true, true, false) == TextLeanMode.Wall, "a flat wall carries it with spacers");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, false, -10f, false, true, false, false) == TextLeanMode.None, "a curved wall does not (as for a elevation)");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, true, -10f, false, true, true, false) == TextLeanMode.None, "a roof does not: the box would lean out over its edge");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, true, -10f, true, true, true, false) == TextLeanMode.None, "not even with the triangular frame asked for");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, true, 10f, false, true, true, false) == TextLeanMode.Roof, "an elevation on a roof is as before");
            foreach (SupportKind kind in new[] { SupportKind.Cantilever, SupportKind.Gantry, SupportKind.DoubleCantilever, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.WallBracket })
                Program.Check(TextTilt.ModeOf(kind, false, -20f, false, true, true, false) == TextLeanMode.None, kind + ": a hung or attached board is not turned");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, -TextTilt.MinDeg, false, true, true, false) == TextLeanMode.Stand && TextTilt.ModeOf(SupportKind.SinglePole, false, -TextTilt.MinDeg * 0.9f, false, true, true, false) == TextLeanMode.None, "the least depression is the least tilt");
            Program.Check(TextTilt.Dips(-1f) && !TextTilt.Dips(1f) && !TextTilt.Dips(0f) && !TextTilt.Dips(float.NaN) && !TextTilt.Dips(-TextTilt.MinDeg * 0.5f), "what looks down");
        }

        /// <summary>
        /// Whether a point of the side view (z toward the viewer of the box, y up) is inside the slab of a board that leans by tilt about its lower back edge (zA, yA): between its back plane and its
        /// face plane (depth deep, square to the plane), along the plane from the lower edge for height. Shrunk by margin on every side.
        /// </summary>
        private static bool InSlab(float z, float y, float zA, float yA, float tilt, float height, float deep, float margin)
        {
            float s = (float)Math.Sin(tilt), c = (float)Math.Cos(tilt);
            float dy = y - yA, dz = z - zA;
            float along = dy * c - dz * s, across = dy * s + dz * c;
            return along > margin && along < height - margin && across > margin && across < deep - margin;
        }

        private static StandPlan Plan(float tilt, float height = 0.6f, float lift = 0.4f, float armShare = 0.8f, float lowShare = 0.2f, bool open = true) =>
            StandPlanner.Plan(new StandSpec { Width = 1.2f, Height = height, Depth = 0.2f, TiltDeg = tilt, Lift = lift, FrameX = new[] { -0.4f, 0.4f }, ArmShare = open ? armShare : 0f, LowShare = open ? lowShare : 0f, ArmBehind = open ? SupportLayouts.RailDepth : 0f, Open = open });

        private static void Planner()
        {
            foreach (bool open in new[] { true, false })
            {
                foreach (float tilt in new[] { -1f, -5f, -15f, -30f })
                {
                    StandPlan plan = Plan(tilt, open: open);
                    Program.Near(plan.TiltDeg, tilt, 1e-4f, "the planner keeps the depression: " + tilt);
                    Program.Check(plan.TiltRad < 0f && !plan.Warnings.Exists(w => w.Kind == StandWarningKind.TiltClamped), $"{tilt}°: a negative angle in radians, not clamped");
                    for (int i = 0; i < plan.FrameCount; i++)
                        Program.Check(plan.ClosureError(i) <= 1e-3f, $"{tilt}° open={open}: frame {i} closes");

                    // The post stands behind the box over the height the box occupies: from its lower back edge to the post's top.
                    StandMember post = plan.Find(StandMemberKind.Post, 0);
                    float low = plan.FromBackPlane(0f, plan.YA), high = plan.FromBackPlane(0f, post.B.Y);
                    Program.Check(Math.Max(low, high) < -plan.PostRadius + 1e-4f, $"{tilt}° open={open}: the post is behind the back plane where the box is: {Math.Max(low, high):0.000} against {-plan.PostRadius:0.000}");
                    Program.Near(Math.Max(low, high), -(plan.PostRadius + StandPlanner.Clearance), 2e-3f, $"{tilt}° open={open}: and the post's skin is clear of it by the planner's clearance, whatever the angle");

                    // The lower back edge is the most backward point of the box: the post is behind the whole of it, the box's top stands out in front.
                    Program.Near(plan.ZA, (plan.PostRadius + StandPlanner.Clearance) / (float)Math.Cos(plan.TiltRad), 1e-4f, $"{tilt}° open={open}: the lower back edge stands where the post's skin and its clearance put it, square to the plane");

                    // The arms end 6 mm inside the back line at their most forward edge, whatever the angle (a board on rails: in the channel's depth).
                    foreach (StandMember arm in plan.Members.Where(m => m.Kind == StandMemberKind.ArmTop || m.Kind == StandMemberKind.ArmLow))
                    {
                        float tan = (float)Math.Tan(plan.TiltRad), y = arm.A.Y, h = arm.Half;
                        float forward = Math.Max(plan.BackZAt(y - h), plan.BackZAt(y + h));
                        float inside = arm.B.Z - forward;
                        float channel = open ? SupportLayouts.RailDepth / (float)Math.Cos(plan.TiltRad) : 0f;
                        Program.Near(inside, 0.006f - channel, 1.5e-3f, $"{tilt}° open={open}: the {arm.Kind} ends {inside * 1000f:0.0} mm in");
                        Program.Check(arm.B.Z > arm.A.Z, $"{tilt}° {arm.Kind}: it reaches out of the post, not back into it");
                    }

                    // No member of the frame enters the slab of the board, however it is cut at the board's back plane: the arms end 6 mm in (the margin is 1 mm), nothing else is near.
                    float height = 0.6f, deep = 0.2f;
                    foreach (StandMember m in plan.Members)
                    {
                        if (m.Kind == StandMemberKind.ArmTop || m.Kind == StandMemberKind.ArmLow)
                            continue;
                        for (int k = 0; k <= 10; k++)
                        {
                            Vec3 q = m.A + (m.B - m.A) * (k / 10f);
                            Program.Check(!InSlab(q.Z, q.Y, plan.ZA, plan.YA, plan.TiltRad, height, deep, 0.001f), $"{tilt}° open={open}: a point of the {m.Kind} is in the board's slab: z {q.Z:0.000} y {q.Y:0.000}");
                        }
                    }

                    // The top arm is the longer one: the box leans forward, the higher the further out it is.
                    StandMember top = plan.Members.FirstOrDefault(m => m.Kind == StandMemberKind.ArmTop), lowArm = plan.Members.FirstOrDefault(m => m.Kind == StandMemberKind.ArmLow);
                    if (lowArm.Kind == StandMemberKind.ArmLow && tilt <= -5f)
                        Program.Check(top.B.Z > lowArm.B.Z + 0.02f, $"{tilt}° open={open}: the top arm reaches out further than the low arm");
                }
            }

            // A leaning-back plan is as it was: the post is where the back line passes it.
            StandPlan back = Plan(20f);
            Program.Check(back.ZA > Plan(0f).ZA, "an elevation puts the lower back edge in front of the post by the lean");

            // A box that looks down far out in front of its post is said so.
            StandPlan tall = StandPlanner.Plan(new StandSpec { Width = 1f, Height = 4f, Depth = 0.2f, TiltDeg = -30f, Lift = 0.5f, FrameX = new[] { 0f } });
            Program.Check(tall.Warnings.Exists(w => w.Kind == StandWarningKind.DipReach && w.Value > StandPlanner.DipReachWarning), "a tall box at -30° stands out in front of its post and the planner says so");
            Program.Check(!Plan(-10f).Warnings.Exists(w => w.Kind == StandWarningKind.DipReach) && !Plan(25f, 4f).Warnings.Exists(w => w.Kind == StandWarningKind.DipReach), "a small one, and any elevation, are not warned of");
        }

        private static void OnPost()
        {
            foreach (string support in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole })
            {
                foreach (float tilt in new[] { -5f, -15f, -30f })
                {
                    TextPlans.Clear();
                    SignInstance upright = TextTiltTests.Instance("文字灯箱", ";h=0.5"), dipped = TextTiltTests.Instance("文字灯箱", ";h=0.5;tilt=" + Num(tilt));
                    SignParts a = TextTiltTests.Build(upright, support), b = TextTiltTests.Build(dipped, support);
                    Program.Check(b.Stand != null && a.Stand == null, support + ": the depressed text has the stand's frames");
                    StandPlan stand = b.Stand;
                    Program.Near(stand.TiltDeg, tilt, 1e-4f, "the planner's tilt is the text's: " + tilt);
                    MeshBuilder glow = b.Solids[(int)dipped.Board.Text.Glow], glowUp = a.Solids[(int)upright.Board.Text.Glow];
                    Program.Check(glow.VertexCount == glowUp.VertexCount && glow.VertexCount > 100, support + ": the same letters");

                    // The lit faces look down by the tilt: every front normal is turned toward the ground (negative y), by the angle.
                    float s = (float)Math.Sin(tilt * Math.PI / 180.0), c = (float)Math.Cos(tilt * Math.PI / 180.0);
                    int front = 0, right = 0;
                    for (int i = 0; i < glow.VertexCount; i++)
                    {
                        Vec3 n = glow.Normals[i];
                        if (n.Z > 0.5f)
                        {
                            front++;
                            if (Math.Abs(n.Y - s) < 1e-3f && Math.Abs(n.Z - c) < 1e-3f)
                                right++;
                        }
                    }

                    Program.Check(front > 50 && right >= front * 9 / 10, $"{support} {tilt}°: the lit faces look down by the tilt: {right} of {front} front normals; y {s:0.000}");
                    Program.Check(s < 0f, "and a face that looks down has a negative y normal");

                    // The top of the letters is further out (toward +z) than their bottom.
                    float topZ = glow.Positions.Where(p => p.Y > glow.Positions.Max(q => q.Y) - 0.02f).Average(p => p.Z), bottomZ = glow.Positions.Where(p => p.Y < glow.Positions.Min(q => q.Y) + 0.02f).Average(p => p.Z);
                    float expected = (glow.Positions.Max(p => p.Y) - glow.Positions.Min(p => p.Y)) * (float)Math.Tan(-tilt * Math.PI / 180.0);
                    Program.Check(topZ - bottomZ > 0.5f * expected, $"{support} {tilt}°: the top stands out: {topZ:0.000} against {bottomZ:0.000}");

                    // Nothing of the frame is in front of the letters' faces, and the posts do not run into the letters.
                    float frontMost = glow.Positions.Max(p => p.Z);
                    Program.Check(b.Metal.Positions.All(p => p.Z <= frontMost + 0.01f), support + ": no steel in front of the faces");
                    Program.Check(b.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)) && glow.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), "finite");
                    Program.Check(b.Picks.Exists(p => p.Name == "board" && Math.Abs(p.Tilt + stand.TiltRad) < 1e-5f && p.Tilt > 0f), "the board's pick looks down with it");

                    // The letters are in front of the post's top, so the arms (to the channels) carry them: every arm lands behind the lit faces.
                    foreach (StandMember arm in stand.Members.Where(m => m.Kind == StandMemberKind.ArmTop || m.Kind == StandMemberKind.ArmLow))
                        Program.Check(arm.B.Z < frontMost, $"{support} {tilt}°: the {arm.Kind} stays behind the faces");
                }
            }
        }

        private static void OnStandBox()
        {
            foreach (float tilt in new[] { -5f, -15f, -30f })
            {
                SignInstance box = SignInstances.Of(SignCatalog.ById(StandBoxes.TemplateId), StandBoxes.Template.Parse("").With("text", "立式灯箱").With("tilt", Num(tilt)).With("h", "0.4").ToString());
                SignParts parts = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = box } }, SupportCatalog.ById(SupportCatalog.SinglePole), 0f, SupportDetail.Full, default);
                StandPlan plan = parts.Stand;
                Program.Check(plan != null && plan.TiltDeg == tilt, $"{tilt}°: the planner has the depression");
                Program.Check(parts.Picks.Exists(p => p.Name == "board" && p.Tilt > 0f && Math.Abs(p.Tilt + plan.TiltRad) < 1e-5f), $"{tilt}°: the pick looks down");
                Program.Check(parts.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), $"{tilt}°: finite");
                for (int i = 0; i < plan.FrameCount; i++)
                    Program.Check(plan.ClosureError(i) <= 1e-3f, $"{tilt}°: frame {i} closes");

                // The housing's front looks down: its lit panel's normals have a negative y.
                MeshBuilder lit = parts.Solids[(int)box.Board.Text.Glow];
                Program.Check(lit != null && lit.VertexCount > 0 && lit.Normals.Count(n => n.Z > 0.5f && n.Y < -0.05f) > 0, $"{tilt}°: a face looks down");

                // The arms land in the housing's back plate (a housing has no channel): the same 6 mm.
                foreach (StandMember arm in plan.Members.Where(m => m.Kind == StandMemberKind.ArmTop || m.Kind == StandMemberKind.ArmLow))
                {
                    float forward = Math.Max(plan.BackZAt(arm.A.Y - arm.Half), plan.BackZAt(arm.A.Y + arm.Half));
                    Program.Near(arm.B.Z - forward, 0.006f, 1.5e-3f, $"{tilt}°: the {arm.Kind} ends in the back plate");
                }
            }

            // The default (10° back) and an elevation are built as before: the golden of TextTiltTests covers the text box, this the stand box's own default.
            SignInstance def = SignInstances.Of(SignCatalog.ById(StandBoxes.TemplateId), StandBoxes.Template.Parse("").ToString());
            SignParts built = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = def } }, SupportCatalog.ById(SupportCatalog.SinglePole), 0f, SupportDetail.Full, default);
            Program.Check(built.Stand != null && built.Stand.TiltDeg == 10f, "the default stand box still leans back by ten degrees");
        }

        private static void OnWall()
        {
            foreach (float tilt in new[] { -5f, -15f, -30f })
            {
                TextPlans.Clear();
                string spec = ";h=0.6;tilt=" + Num(tilt);
                SignInstance dipped = TextTiltTests.Instance("文字灯箱\n文字灯箱", spec), upright = TextTiltTests.Instance("文字灯箱\n文字灯箱", ";h=0.6");
                SignParts wall = TextTiltTests.Build(dipped, SupportCatalog.WallFlat), flat = TextTiltTests.Build(upright, SupportCatalog.WallFlat);
                MeshBuilder glow = wall.Solids[(int)dipped.Board.Text.Glow], glowFlat = flat.Solids[(int)upright.Board.Text.Glow];
                Program.Check(glow.VertexCount == glowFlat.VertexCount && glow.VertexCount > 100, $"{tilt}°: the same letters on the wall");
                Program.Check(wall.Stand == null, $"{tilt}°: a wall has no stand's frames");

                // The wall is z = 0: nothing is behind it, and the letters' top is further out than their bottom (the face looks down).
                Program.Check(wall.Metal.Positions.All(p => p.Z >= -1e-3f) && glow.Positions.All(p => p.Z >= 0f), $"{tilt}°: nothing goes into the wall");
                float topZ = glow.Positions.Where(p => p.Y > glow.Positions.Max(q => q.Y) - 0.02f).Average(p => p.Z), bottomZ = glow.Positions.Where(p => p.Y < glow.Positions.Min(q => q.Y) + 0.02f).Average(p => p.Z);
                float height = glow.Positions.Max(p => p.Y) - glow.Positions.Min(p => p.Y);
                Program.Check(topZ > bottomZ + 0.5f * height * (float)Math.Tan(-tilt * Math.PI / 180.0), $"{tilt}°: the top stands out further: {topZ:0.000} against {bottomZ:0.000}");

                // The lower edge keeps the spacers of an upright text; the nearest letters are as near the wall as upright letters are.
                float nearestUp = glowFlat.Positions.Min(p => p.Z), nearestDip = glow.Positions.Min(p => p.Z);
                Program.Check(nearestDip >= nearestUp - 0.02f, $"{tilt}°: the nearest letters are no nearer the wall than upright ones: {nearestDip:0.000} against {nearestUp:0.000}");

                // The spacers: horizontal tubes from z = 0 to the rails, longer the higher the rail is: the rows of tube ends at z > 0.05 by height.
                int stud = wall.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-3f);
                Program.Check(stud >= 8, $"{tilt}°: the spacers' ends are on the wall: {stud} vertices");
                Program.Check(wall.Picks.Exists(p => p.Name == "board" && p.Tilt > 0f && Math.Abs(p.Tilt + TextTilt.Radians(tilt)) < 1e-5f), $"{tilt}°: the board's pick looks down");
                Program.Check(wall.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), $"{tilt}°: finite");
            }

            // The built wall carries the ties its plan has, with the reference the builder chooses: the lowest edge's for a depression (measured from the top rail it would have none at all). The count the
            // builder made is on the parts; the plan's is asked for here with the same rails and standoffs.
            foreach (float angle in new[] { -15f, -30f, 15f, 30f })
            {
                TextPlans.Clear();
                SignInstance board = TextTiltTests.Instance("灯箱文字\n文字灯箱", ";h=0.6;tilt=" + Num(angle));
                SignParts built = TextTiltTests.Build(board, SupportCatalog.WallFlat);
                TextPlanResult planned = TextPlans.Peek(board.Board.Text.Job);
                WallFramePlan frame = TextWallFrame.Plan(planned.Frame, TextStyles.FrameFor(board.Board.Text.Job.LetterHeight, board.Board.Text.Job.Art), board.Board.Text.Mount);
                PickShape pick = built.Picks.Find(p => p.Name == "board");
                float rad = TextTilt.Radians(angle), height = board.Board.Main.H, sine = (float)Math.Sin(rad);
                float middle = pick.TiltY + height * 0.5f;
                float lower = rad > 0f ? pick.TiltZ - height * sine : pick.TiltZ;
                int expected = TextTilt.WallTies(frame.Standoffs, planned.Frame.Rails, middle, pick.TiltY, pick.TiltZ, rad, lower).Count;
                Program.Check(expected > 0, $"{angle}°: the plan has ties");
                Program.Check(built.WallTieCount == expected, $"{angle}°: the builder made the plan's {expected} ties: {built.WallTieCount}");
            }

            // From the top rail a depression has no ties at all: the reference is what the builder must get right.
            {
                TextPlans.Clear();
                SignInstance board = TextTiltTests.Instance("灯箱文字\n文字灯箱", ";h=0.6;tilt=-30");
                SignParts built = TextTiltTests.Build(board, SupportCatalog.WallFlat);
                TextPlanResult planned = TextPlans.Peek(board.Board.Text.Job);
                WallFramePlan frame = TextWallFrame.Plan(planned.Frame, TextStyles.FrameFor(board.Board.Text.Job.LetterHeight, board.Board.Text.Job.Art), board.Board.Text.Mount);
                PickShape pick = built.Picks.Find(p => p.Name == "board");
                float rad = TextTilt.Radians(-30f), height = board.Board.Main.H;
                int fromTop = TextTilt.WallTies(frame.Standoffs, planned.Frame.Rails, pick.TiltY + height * 0.5f, pick.TiltY, pick.TiltZ, rad, pick.TiltZ - height * (float)Math.Sin(rad)).Count;
                Program.Check(fromTop == 0 && built.WallTieCount > 0, $"from the top rail the depression has {fromTop} ties; the build has {built.WallTieCount}");
            }

            // The ties of a depression, as a plan (the wall frame's own numbers of TextTiltTests.WallTies, the other way round): the rails stand out further the higher they are, so the
            // ties go to the rails beyond the lowest edge's reach, from the wall above each rail's back.
            var rails = new List<FrameSegment>();
            var standoffs = new List<Point2>();
            foreach (float y in new[] { 0.45f, 0.25f, -0.25f, -0.45f })
            {
                rails.Add(new FrameSegment { A = new Point2(-1f, y), B = new Point2(1f, y) });
                foreach (float x in new[] { -0.92f, 0f, 0.92f })
                    standoffs.Add(new Point2(x, y));
            }

            float dip = TextTilt.Radians(-20f), s = (float)Math.Sin(dip), c = (float)Math.Cos(dip), span = 1f, yA = -0.5f, back = 0.08f;
            float topY = yA + span * c;
            List<TextTilt.WallTie> ties = TextTilt.WallTies(standoffs, rails, 0f, yA, back, dip, back);
            Program.Check(ties.Count >= 3, "a depression of 20° has ties to its outer rails: " + ties.Count);
            Program.Check(ties.All(tie => tie.WallY > tie.Y && tie.Z > 0f), "each from the wall above its rail's back, down to it");
            Program.Check(ties.All(tie => tie.Y > yA + 0.1f), "none for the lowest rail: its spacer is as short as an upright text's");
            Program.Check(ties.All(tie => tie.WallY > tie.Y + 0.05f), "and each anchor is clearly above the rail it holds, not level with it");

            // No tie enters the slab of the board: the board turned by the depression about its lower back edge (yA, back), height span, a depth of 6 cm of letters.
            foreach (TextTilt.WallTie tie in ties)
            {
                for (int k = 0; k <= 20; k++)
                {
                    float y = tie.WallY + (tie.Y - tie.WallY) * (k / 20f), z = tie.WallZ + (tie.Z - tie.WallZ) * (k / 20f);
                    Program.Check(!InSlab(z, y, back, yA, dip, span, 0.06f, 0.002f) || k == 20, $"a tie at x {tie.X:0.00} is in the board's slab at {k}/20: z {z:0.000} y {y:0.000}");
                }
            }
            Program.Check(TextTilt.WallTies(standoffs, rails, 0f, yA, back, TextTilt.Radians(-3f), back).Count == 0, "no tie for a small depression");
            Program.Check(TextTilt.WallTies(standoffs, new List<FrameSegment>(), 0f, yA, back, dip, back).Count == 0, "no rail, no tie");
            Program.Check(TextTilt.WallTies(standoffs, rails, 0f, yA, back, dip, back - 0.5f * span * s).Count != ties.Count || s >= 0f, "measured from the top rail (the elevation's reference) none or other ties come out: the reference matters");
        }

        private static void OnRoof()
        {
            var lines = new List<string>();
            Action<string> before = StandBoxes.Log;
            StandBoxes.Log = lines.Add;
            TextTilt.ForgetNotes();
            try
            {
                TextPlans.Clear();
                SignInstance dipped = TextTiltTests.Instance("文字灯箱", ";h=0.5;tilt=-20"), upright = TextTiltTests.Instance("文字灯箱", ";h=0.5");
                SignParts roof = TextTiltTests.Build(dipped, SupportCatalog.WallRoof, 0.5f), flat = TextTiltTests.Build(upright, SupportCatalog.WallRoof, 0.5f);
                Program.Check(TextTiltTests.Hash(roof) == TextTiltTests.Hash(flat), "on a roof a depression is refused: the text is built upright, as before");
                Program.Check(roof.Stand == null && roof.Layout.Roof, "no frames of the stand on a roof");
                Program.Check(lines.Count == 1 && lines[0].Contains("depression") && lines[0].Contains("20"), "and the log says so once, as a depression: " + string.Join(" | ", lines));
                TextPlans.Clear();
                TextTiltTests.Build(dipped, SupportCatalog.WallRoof, 0.5f);
                Program.Check(lines.Count == 1, "and does not say it again");
            }
            finally
            {
                StandBoxes.Log = before;
                TextTilt.ForgetNotes();
            }
        }

        private static void BatchSigned()
        {
            int signed = 0, fields = 0;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Def.IsMarking || choice.Def.Template == null)
                    continue;
                bool parsed = MiniJson.TryParse(BatchRequest.Describe(choice.Id), out JsonValue d, out string error);
                Program.Check(parsed, choice.Id + ": the description is JSON: " + error);
                JsonValue list = d.Get("fields");
                GuideTemplate template = choice.Def.Template;
                for (int i = 0; i < template.Fields.Length; i++)
                {
                    GuideField field = template.Fields[i];
                    JsonValue each = list.Items[i];
                    fields++;
                    bool says = each.Get("signed") != null;
                    if (says)
                        signed++;
                    Program.Check(says == (field.Kind == GuideFieldKind.Text && field.Negative), choice.Id + "." + field.Key + ": the description says signed exactly when the field is");
                }
            }

            // The tilt of the text light box and of the standing light box, and nothing else of the catalogue (a template shows once for each choice that has it).
            int expected = SignCatalog.Choices.Count(c => !c.Def.IsMarking && c.Def.Template != null && c.Def.Template.Fields.Any(f => f.Negative));
            Program.Check(expected >= 2 && signed == expected, $"the catalogue has {expected} choices with a signed field and the description says it {signed} times of {fields} fields");
            Program.Check(BatchRequest.Describe(TextBoxes.TemplateId).Contains("\"signed\":true") && BatchRequest.Describe(StandBoxes.TemplateId).Contains("\"signed\":true"), "both light boxes say it");
        }

        private static void Keys()
        {
            float max = TextTilt.MaxDeg;
            Program.Near(TextTilt.Step(0f, -1, false, max), -1f, 1e-6f, "the - key at upright goes one degree into the depression");
            Program.Near(TextTilt.Step(0f, 1, false, max), 1f, 1e-6f, "the = key at upright goes one degree back");
            Program.Near(TextTilt.Step(2f, -1, true, max), -3f, 1e-6f, "Shift takes five, through zero: 2 minus 5");
            Program.Near(TextTilt.Step(-28f, -1, true, max), -max, 1e-6f, "held to the depression's greatest");
            Program.Near(TextTilt.Step(28f, 1, true, max), max, 1e-6f, "held to the elevation's greatest");
            Program.Near(TextTilt.Step(-12.4f, 1, false, max), -11f, 1e-6f, "from the whole degree the angle is at");
            Program.Near(TextTilt.Step(float.NaN, -1, false, max), -1f, 1e-6f, "NaN is upright");
            Program.Near(TextTilt.Step(100f, -1, false, max), max - 1f, 1e-6f, "a typed value beyond the range is held first");
            // Walking from upright to the end and back is one path with no gap at zero.
            float v = 5f;
            var seen = new HashSet<float>();
            for (int k = 0; k < 12; k++)
            {
                v = TextTilt.Step(v, -1, false, max);
                seen.Add(v);
            }

            Program.Check(seen.Contains(0f) && seen.Contains(-1f) && seen.Contains(-6f), "twelve presses of - from 5 pass through 0 and reach -6");
        }

        private static void ThroughZero()
        {
            // One signed range: -1, 0 and 1 are a depression, upright and an elevation; the least angle is the same either way.
            foreach (string support in new[] { SupportCatalog.SinglePole, SupportCatalog.WallFlat })
            {
                TextPlans.Clear();
                SignParts down = TextTiltTests.Build(TextTiltTests.Instance("文字灯箱", ";h=0.5;tilt=-1"), support);
                SignParts up = TextTiltTests.Build(TextTiltTests.Instance("文字灯箱", ";h=0.5;tilt=1"), support);
                SignParts none = TextTiltTests.Build(TextTiltTests.Instance("文字灯箱", ";h=0.5;tilt=0"), support);
                Program.Check(TextTiltTests.Hash(none) == TextTiltTests.Hash(TextTiltTests.Build(TextTiltTests.Instance("文字灯箱", ";h=0.5"), support)), support + ": tilt 0 is upright");
                Program.Check(TextTiltTests.Hash(down) != TextTiltTests.Hash(up) && TextTiltTests.Hash(down) != TextTiltTests.Hash(none) && TextTiltTests.Hash(up) != TextTiltTests.Hash(none), support + ": -1, 0 and 1 are three builds");
            }

            // A value outside the range is held to it on both sides, and the editor says so for the stand box.
            double held = StandBoxes.Template.Parse("tilt=-80").Number("tilt", 0);
            Program.Check(held == -80.0, "the spec keeps what was typed");
            SignInstance box = SignInstances.Of(SignCatalog.ById(StandBoxes.TemplateId), StandBoxes.Template.Parse("").With("tilt", "-80").ToString());
            Program.Check(box.Board.Stand.TiltDeg == -StandBoxes.MaxTilt, "and the board is built at the greatest depression");
            foreach (bool zh in new[] { true, false })
                Program.Check(StandBoxes.Hints(StandBoxes.Template.Parse("").With("tilt", "-80").ToString(), zh).Exists(l => l.Contains("30")), "and the editor says so: " + (zh ? "zh" : "en"));
            Program.Check(!StandBoxes.Hints(StandBoxes.Template.Parse("").With("tilt", "-20").ToString(), true).Exists(l => l.Contains("30")), "a depression in the range is not remarked on");
        }
    }
}
