using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The elevation angle of the text light box (0.12.78; the owner, 2 Nov: "第一种纯字形灯箱也应该支持仰角"): the letters lean back about the board's lower back edge on a post and on a roof, on the triangular
    /// frames of the standing light box's planner; an upright text is built exactly as before.
    /// </summary>
    internal static class TextTiltTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("text tilt: the template's fields and the keys", Fields);
            Program.Run("text tilt: an upright text is built as it always was (hash of the vertex arrays)", Regression);
            Program.Run("text tilt: which supports lean it", Modes);
            Program.Run("text tilt: the letters lean about the lower back edge on a post", OnPost);
            Program.Run("text tilt: the frames meet the channels behind the rails", ArmsMeetRails);
            Program.Run("text tilt: the triangular frame of an upright text", Upright);
            Program.Run("text tilt: the plan does not depend on the angle", PlanShared);
            Program.Run("text tilt: where it is not built it is built upright and said once", Refused);
            Program.Run("text tilt: the arms' shares, the roof's frames and the open board's plan", Pure);
            Program.Run("text tilt: on a roof", OnRoof);
            Program.Run("text tilt: on a flat wall (spacers to the turned rails, ties from above)", OnWall);
            Program.Run("text tilt: the ties of a wall, as a plan", WallTies);
        }

        private static void OnRoof()
        {
            foreach (float tilt in new[] { 0f, 10f, 30f })
            {
                foreach (string extra in new[] { ";h=0.5", ";h=1;style=outline" })
                {
                    TextPlans.Clear();
                    string spec = extra + ";tilt=" + tilt.ToString(System.Globalization.CultureInfo.InvariantCulture) + (tilt == 0f ? ";tri=1" : "");
                    SignInstance leaning = Instance("文字灯箱文字", spec), upright = Instance("文字灯箱文字", extra);
                    SignParts roof = Build(leaning, SupportCatalog.WallRoof, 0.5f), flat = Build(upright, SupportCatalog.WallRoof, 0.5f);
                    float tiltRad = tilt * (float)Math.PI / 180f;
                    Program.Check(roof.Layout.Roof && roof.Stand == null, "the roof builds its own frames: " + tilt);
                    Program.Check(roof.Metal.Positions.All(p => p.Y >= -1e-4f), $"{extra} {tilt}°: nothing below the roof");
                    MeshBuilder glow = roof.Solids[(int)leaning.Board.Text.Glow], glowFlat = flat.Solids[(int)upright.Board.Text.Glow];
                    Program.Check(glow.VertexCount == glowFlat.VertexCount && glow.VertexCount > 100, "the same letters");
                    float topZ = glow.Positions.Where(p => p.Y > glow.Positions.Max(q => q.Y) - 0.02f).Average(p => p.Z), bottomZ = glow.Positions.Where(p => p.Y < glow.Positions.Min(q => q.Y) + 0.02f).Average(p => p.Z);
                    float lean = bottomZ - topZ;
                    float height = leaning.Board.Main.H;
                    Program.Check(Math.Abs(lean - (glow.Positions.Max(p => p.Y) - glow.Positions.Min(p => p.Y)) * (float)Math.Tan(tiltRad)) < 0.03f, $"{extra} {tilt}°: the letters lean by the tilt: {lean:0.000}");

                    // The plates on the roof: at least one plate under each frame's post and leg (the metal's vertices at the plate's top).
                    int plates = roof.Metal.Positions.Count(p => Math.Abs(p.Y - 0.005f) < 1e-4f);
                    Program.Check(plates >= 16, $"{extra} {tilt}°: plates on the roof: {plates}");

                    // Behind the letters: the posts are behind the board's back plane, none of the frame is in front of the letters' faces.
                    float frontMost = glow.Positions.Max(p => p.Z);
                    var metalBeyond = roof.Metal.Positions.Where(p => p.Z > frontMost + 0.01f).Count();
                    Program.Check(metalBeyond == 0, $"{extra} {tilt}°: no steel in front of the letters: {metalBeyond}");
                    Program.Check(roof.Picks.Exists(p => p.Name == "board" && Math.Abs(p.Tilt + tiltRad) < 1e-5f), "the board's pick leans with it");
                }
            }

            // Without the frame the letters stand on feet and do not lean: said once.
            var lines = new List<string>();
            Action<string> before = StandBoxes.Log;
            StandBoxes.Log = lines.Add;
            TextTilt.ForgetNotes();
            try
            {
                TextPlans.Clear();
                SignInstance feet = Instance("文字灯箱", ";h=0.5;mount=none;tilt=20");
                SignParts onFeet = Build(feet, SupportCatalog.WallRoof, 0.5f);
                SignInstance feetUp = Instance("文字灯箱", ";h=0.5;mount=none");
                Program.Check(Hash(onFeet) == Hash(Build(feetUp, SupportCatalog.WallRoof, 0.5f)), "on feet the text is upright as before");
                Program.Check(lines.Count == 1 && lines[0].Contains("elevation angle"), "and the log says so once: " + lines.Count);
            }
            finally
            {
                StandBoxes.Log = before;
            }
        }

        private static void OnWall()
        {
            bool anyTies = false, anyWithout = false, anyBraces = false;
            foreach (float tilt in new[] { 6f, 15f, 30f })
            {
                foreach ((string text, string extra) in new[] { ("文字灯箱文字", ";h=0.5"), ("文字灯箱文字", ";h=1;style=outline"), ("文字\n灯箱", ";h=0.4"), ("文字\n灯箱", ";h=0.3;style=outline") })
                {
                    TextPlans.Clear();
                    string label = $"{text.Length} {extra} {tilt}°", spec = extra + ";tilt=" + tilt.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    SignInstance leaning = Instance(text, spec), upright = Instance(text, extra);
                    SignParts lean = Build(leaning, SupportCatalog.WallFlat), flat = Build(upright, SupportCatalog.WallFlat);
                    float rad = tilt * (float)Math.PI / 180f, s = (float)Math.Sin(rad), c = (float)Math.Cos(rad);
                    PickShape pick = lean.Picks.Find(p => p.Name == "board");
                    PickShape pickFlat = flat.Picks.Find(p => p.Name == "board");
                    Program.Check(lean.Stand == null && !lean.Layout.Roof && pick != null && Math.Abs(pick.Tilt + rad) < 1e-5f, label + ": the wall builds its own spacers, the pick leans");
                    Program.Check(Math.Abs(pick.CentreY - pickFlat.CentreY) < 1e-4f && Math.Abs(pick.TiltY - (pickFlat.CentreY - leaning.Board.Main.H * 0.5f)) < 1e-4f, label + ": the board turns about its lower back edge and keeps its middle's height");
                    Program.Check(lean.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-5f) == flat.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-5f) && lean.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-5f) > 0, label + ": the wall's own spacers only, none of the generic rails of the support");
                    MeshBuilder glow = lean.Solids[(int)leaning.Board.Text.Glow], glowFlat = flat.Solids[(int)upright.Board.Text.Glow];
                    Program.Check(glow.VertexCount == glowFlat.VertexCount && glow.VertexCount > 100, label + ": the same letters");
                    float high = glow.Positions.Max(p => p.Y), low = glow.Positions.Min(p => p.Y);
                    float topZ = glow.Positions.Where(p => p.Y > high - 0.02f).Average(p => p.Z), bottomZ = glow.Positions.Where(p => p.Y < low + 0.02f).Average(p => p.Z);
                    float highFlat = glowFlat.Positions.Max(p => p.Y);
                    float topFlat = glowFlat.Positions.Where(p => p.Y > highFlat - 0.02f).Average(p => p.Z);
                    Program.Check(Math.Abs((bottomZ - topZ) - (high - low) * (float)Math.Tan(rad)) < 0.03f, label + ": the letters lean by the tilt");
                    Program.Check(Math.Abs(topZ - topFlat) < 0.05f, $"{label}: the top keeps the upright text's distance from the wall: {topZ:0.000} against {topFlat:0.000}");
                    Program.Check(lean.Metal.Positions.All(p => p.Z >= -1e-4f), label + ": nothing behind the wall");
                    float frontMost = glow.Positions.Max(p => p.Z);
                    Program.Check(!lean.Metal.Positions.Any(p => p.Z > frontMost + 0.01f), label + ": no steel in front of the letters");

                    TextPlanResult plan = TextPlans.Peek(leaning.Board.Text.Job);
                    FrameStyle style = TextStyles.FrameFor(leaning.Board.Text.Job.LetterHeight, leaning.Board.Text.Job.Art);
                    WallFramePlan wall = TextWallFrame.Plan(plan.Frame, style, TextMount.Auto);
                    float height = leaning.Board.Main.H, yA = pick.TiltY, zA = pick.TiltZ, cx = pick.CentreX, cyUp = yA + height * 0.5f;
                    anyBraces |= wall.Braces.Count > 0;
                    var spacers = new List<(Vec3 A, Vec3 B, float R)>();
                    float zRail = zA + Math.Max(0.006f, style.RailSize) * 0.5f;
                    foreach (FrameSegment brace in wall.Braces)
                    {
                        // Both ends of a diagonal: the six vertices of its end ring are at its radius from the axis point, in the rails' layer turned with them.
                        foreach (Point2 end in new[] { brace.A, brace.B })
                        {
                            TextTilt.Turned(cyUp + end.Y, zRail, yA, zA, s, c, out float by, out float bz);
                            var at = new Vec3(cx - end.X, by, bz);
                            int onRing = lean.Metal.Positions.Count(p => Math.Abs((p - at).Length - TextWallFrame.BraceRadius) < 2e-5f);
                            Program.Check(onRing >= 6, label + ": a diagonal's end is turned with the rails: " + onRing);
                        }
                    }

                    foreach (Point2 standoff in wall.Standoffs)
                    {
                        TextTilt.Turned(cyUp + standoff.Y, zA, yA, zA, s, c, out float y, out float z);
                        spacers.Add((new Vec3(cx - standoff.X, y, 0f), new Vec3(cx - standoff.X, y, z + 0.002f), TextWallFrame.StandoffRadius));
                    }

                    Program.Check(spacers.Count >= 4, label + ": spacers: " + spacers.Count);
                    Program.Check(spacers.Max(p => p.B.Z) - spacers.Min(p => p.B.Z) > height * s * 0.25f, label + ": the lower the rail the longer its spacer");
                    foreach ((Vec3 A, Vec3 B, float R) spacer in spacers)
                        Program.Check(HasRing(lean.Metal, spacer.A, spacer.R) && HasRing(lean.Metal, spacer.B, spacer.R), label + ": a spacer from the wall to the turned rail at " + spacer.A.Y.ToString("0.000"));

                    List<TextTilt.WallTie> ties = TextTilt.WallTies(wall.Standoffs, plan.Frame.Rails, cyUp, yA, zA, rad, zA - height * s);
                    anyTies |= ties.Count > 0;
                    Program.Check(ties.All(tie => tie.WallY < yA + height * c), label + ": no tie is anchored above the board's top");
                    anyWithout |= ties.Count == 0;
                    var pieces = new List<(Vec3 A, Vec3 B, float R)>(spacers);
                    foreach (TextTilt.WallTie tie in ties)
                    {
                        var a = new Vec3(cx - tie.X, tie.WallY, tie.WallZ);
                        var b = new Vec3(cx - tie.X, tie.Y, tie.Z);
                        Program.Check(HasRing(lean.Metal, a, TextTilt.TieRadius) && HasRing(lean.Metal, b, TextTilt.TieRadius), label + ": a tie is built from the wall to the rail");
                        pieces.Add((a, b, TextTilt.TieRadius));
                        for (int k = 0; k <= 19; k++)
                        {
                            float v = k / 20f, py = a.Y + (b.Y - a.Y) * v, pz = a.Z + (b.Z - a.Z) * v;
                            Program.Check(s * (py - yA) + c * (pz - zA) <= 1e-4f, label + ": a tie stays behind the board's back plane");
                        }
                    }

                    // No two of the spacers and ties touch; two ties may meet where they come from the same anchor under the top.
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        for (int j = i + 1; j < pieces.Count; j++)
                        {
                            float d = SegmentDistance(pieces[i].A, pieces[i].B, pieces[j].A, pieces[j].B);
                            Program.Check(d >= pieces[i].R + pieces[j].R, $"{label}: pieces {i} and {j} come within {d:0.0000} m");
                        }
                    }
                }
            }

            Program.Check(anyTies && anyWithout, "ties where the lower rails stand far out, none where they hardly do");
            Program.Check(anyBraces, "the cases reach the diagonals");

            // Far away there is no frame: the letters' faces are upright with the wall's own spacers, whatever the angle (0.12.105: they were bars before).
            TextPlans.Clear();
            SignInstance farUp = Instance("文字灯箱", ";h=0.5"), farLeaning = Instance("文字灯箱", ";h=0.5;tilt=20");
            SignParts far = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = farLeaning } }, SupportCatalog.ById(SupportCatalog.WallFlat), 0f, SupportDetail.Reduced, default);
            SignParts farFlat = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = farUp } }, SupportCatalog.ById(SupportCatalog.WallFlat), 0f, SupportDetail.Reduced, default);
            Program.Check(Hash(far) == Hash(farFlat) && far.Metal.VertexCount > 0, "reduced detail: upright bars with the wall's spacers");

            var lines = new List<string>();
            Action<string> before = StandBoxes.Log;
            StandBoxes.Log = lines.Add;
            TextTilt.ForgetNotes();
            try
            {
                foreach (string extra in new[] { ";h=0.5;mount=none", ";h=0.5;arc=6" })
                {
                    TextPlans.Clear();
                    SignInstance up = Instance("文字灯箱", extra), tilted = Instance("文字灯箱", extra + ";tilt=20");
                    SignParts a = Build(up, SupportCatalog.WallFlat), b = Build(tilted, SupportCatalog.WallFlat);
                    Program.Check(Hash(a) == Hash(b), extra + ": built upright, to the bit");
                    Build(tilted, SupportCatalog.WallFlat);
                }

                Program.Check(lines.Count == 1 && lines[0].Contains("elevation angle"), "without the base frame, or on a curved wall: upright and said once for the wall's kind: " + lines.Count);
            }
            finally
            {
                StandBoxes.Log = before;
            }
        }

        /// <summary>Whether the mesh has a vertex on the circle of a tube's end: in the plane of the end, within the radius of the axis point.</summary>
        private static bool HasRing(MeshBuilder mesh, Vec3 at, float radius) =>
            mesh.Positions.Any(p => Math.Abs(p.Z - at.Z) < 1e-4f && Math.Abs(p.X - at.X) < radius + 1e-4f && Math.Abs(p.Y - at.Y) < radius + 1e-4f);

        /// <summary>The least distance between two segments (the closest points of the two lines held to their ends).</summary>
        private static float SegmentDistance(Vec3 p1, Vec3 q1, Vec3 p2, Vec3 q2)
        {
            Vec3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r), sc, tc;
            if (a < 1e-12f && e < 1e-12f)
                return r.Length;
            if (a < 1e-12f)
            {
                sc = 0f;
                tc = Math.Max(0f, Math.Min(1f, f / e));
            }
            else
            {
                float cc = Dot(d1, r);
                if (e < 1e-12f)
                {
                    tc = 0f;
                    sc = Math.Max(0f, Math.Min(1f, -cc / a));
                }
                else
                {
                    float b = Dot(d1, d2), denom = a * e - b * b;
                    sc = denom > 1e-12f ? Math.Max(0f, Math.Min(1f, (b * f - cc * e) / denom)) : 0f;
                    tc = (b * sc + f) / e;
                    if (tc < 0f)
                    {
                        tc = 0f;
                        sc = Math.Max(0f, Math.Min(1f, -cc / a));
                    }
                    else if (tc > 1f)
                    {
                        tc = 1f;
                        sc = Math.Max(0f, Math.Min(1f, (b - cc) / a));
                    }
                }
            }

            return ((p1 + d1 * sc) - (p2 + d2 * tc)).Length;
        }

        private static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static void WallTies()
        {
            // Two lines of text: four rails, spacers at the ends and the middle of each.
            var rails = new List<FrameSegment>();
            var standoffs = new List<Point2>();
            foreach (float y in new[] { 0.45f, 0.25f, -0.25f, -0.45f })
            {
                rails.Add(new FrameSegment { A = new Point2(-1f, y), B = new Point2(1f, y) });
                foreach (float x in new[] { -0.92f, 0f, 0.92f })
                    standoffs.Add(new Point2(x, y));
            }

            float tilt = 20f * (float)Math.PI / 180f, s = (float)Math.Sin(tilt), c = (float)Math.Cos(tilt), height = 1f, yA = -0.5f, zA = 0.08f + height * s;
            float topY = yA + height * c, topZ = zA - height * s;
            List<TextTilt.WallTie> ties = TextTilt.WallTies(standoffs, rails, 0f, yA, zA, tilt, topZ);
            Program.Check(ties.Count == 6, "the two lower rails have a tie at each spacer, the two upper ones stand out too little: " + ties.Count);
            foreach (TextTilt.WallTie tie in ties)
            {
                Program.Check(tie.Z - 0.002f - topZ >= TextTilt.TieMinReach - 1e-4f, "a tie only for a rail that stands out");
                Program.Check(tie.WallY > tie.Y && tie.WallY < topY && tie.WallZ > 0f && tie.WallZ < 0.02f, "from above, under the board's top, a little off the wall");
                float rise = tie.WallY - tie.Y, run = tie.Z - tie.WallZ;
                Program.Check(Math.Abs(rise - run) < 1e-4f, "at 45°");
                Program.Check(standoffs.All(q => Math.Abs(q.X - tie.X) >= 0.02f), "clear of every spacer");
                Program.Check(rails.Any(r => tie.X > r.A.X && tie.X < r.B.X), "on its rail");
            }

            for (int i = 0; i < ties.Count; i++)
            {
                for (int j = i + 1; j < ties.Count; j++)
                    Program.Check(Math.Abs(ties[i].X - ties[j].X) >= 0.02f, "ties in planes of their own");
            }

            // Hardly leaning: the lower rails stand out less than the least that gets a tie.
            float small = 3f * (float)Math.PI / 180f, ss = (float)Math.Sin(small), cs = (float)Math.Cos(small);
            Program.Check(TextTilt.WallTies(standoffs, rails, 0f, yA, 0.08f + height * ss, small, 0.08f).Count == 0, "no tie for a small lean");

            // Spacers crowd one rail: a tie runs clear of every one of them, or there is none.
            var tight = new List<Point2> { new Point2(0f, -0.45f), new Point2(0.03f, -0.45f), new Point2(-0.03f, -0.45f), new Point2(0.06f, -0.45f), new Point2(-0.06f, -0.45f) };
            var tightRails = new List<FrameSegment> { new FrameSegment { A = new Point2(-1f, -0.45f), B = new Point2(1f, -0.45f) } };
            foreach (TextTilt.WallTie tie in TextTilt.WallTies(tight, tightRails, 0f, yA, zA, tilt, topZ))
                Program.Check(tight.All(q => Math.Abs(q.X - tie.X) >= 0.02f), "between crowded spacers: clear or none");
            var edge = new List<Point2> { new Point2(0.99f, -0.45f) };
            List<TextTilt.WallTie> atEnd = TextTilt.WallTies(edge, tightRails, 0f, yA, zA, tilt, topZ);
            Program.Check(atEnd.Count == 1 && Math.Abs(atEnd[0].X - 0.96f) < 1e-4f, "at the end of a rail the plane is on the inside");
            Program.Check(TextTilt.WallTies(standoffs, new List<FrameSegment>(), 0f, yA, zA, tilt, topZ).Count == 0, "no rail, no tie");
        }

        private static void Pure()
        {
            // Turned is the builder's turn (LeanBack) for a point.
            var mesh = new MeshBuilder();
            var points = new[] { new Vec3(0.3f, 0.7f, 0.2f), new Vec3(-0.1f, 0.1f, 0.9f), new Vec3(0.5f, -0.4f, -0.3f) };
            foreach (Vec3 p in points)
                mesh.Vertex(p, new Vec3(0f, 0f, 1f));
            float angle = 23f * (float)Math.PI / 180f;
            SignBuilder.LeanBack(mesh, 0, angle, 0.2f, 0.1f);
            for (int i = 0; i < points.Length; i++)
            {
                TextTilt.Turned(points[i].Y, points[i].Z, 0.2f, 0.1f, (float)Math.Sin(angle), (float)Math.Cos(angle), out float y, out float z);
                Program.Check(Math.Abs(mesh.Positions[i].Y - y) < 1e-5f && Math.Abs(mesh.Positions[i].Z - z) < 1e-5f, "Turned: a point turns as the builder's mesh does");
            }

            // Shares: the highest rail and the lowest as shares of the board's height from its lower edge.
            TextTilt.Shares(null, 1f, out float none, out float noneLow);
            Program.Check(none == 0f && noneLow == 0f, "no frame: no share (the builder takes its own)");
            var frame = new FramePlan();
            frame.Rails.Add(new FrameSegment { A = new Point2(-1f, 0.2f), B = new Point2(1f, 0.2f) });
            frame.Rails.Add(new FrameSegment { A = new Point2(-1f, -0.2f), B = new Point2(1f, -0.2f) });
            TextTilt.Shares(frame, 0.6f, out float high, out float low);
            Program.Near(high, (0.2f + 0.3f) / 0.6f, 1e-5f, "the top rail's share");
            Program.Near(low, (-0.2f + 0.3f) / 0.6f, 1e-5f, "the lowest rail's share");
            var one = new FramePlan();
            one.Rails.Add(new FrameSegment { A = new Point2(-1f, 0f), B = new Point2(1f, 0f) });
            TextTilt.Shares(one, 0.6f, out float onlyHigh, out float onlyLow);
            Program.Check(onlyHigh > 0.4f && onlyLow == 0f, "one rail: one share");
            var close = new FramePlan();
            close.Rails.Add(new FrameSegment { A = new Point2(-1f, 0.05f), B = new Point2(1f, 0.05f) });
            close.Rails.Add(new FrameSegment { A = new Point2(-1f, -0.05f), B = new Point2(1f, -0.05f) });
            TextTilt.Shares(close, 0.6f, out _, out float closeLow);
            Program.Check(closeLow == 0f, "two rails 10 cm apart: one arm");

            // RoofFrameX: the frames across a board, no further apart than a leg pitch.
            float[] narrow = TextTilt.RoofFrameX(0.2f, 3f), wide = TextTilt.RoofFrameX(7f, 3f), mid = TextTilt.RoofFrameX(1.5f, 0f);
            Program.Check(narrow.Length == 1 && narrow[0] == 3f, "a board too narrow for two: one frame at its middle");
            Program.Check(mid.Length == 2 && Math.Abs(mid[0] + mid[1]) < 1e-5f && mid[1] - mid[0] > 1.2f, "a metre and a half: two frames in from its ends");
            Program.Check(wide.Length == 5 && Math.Abs(wide[0] - (3f - 3.4f)) < 1e-4f && Math.Abs(wide[4] - (3f + 3.4f)) < 1e-4f, "seven metres: five frames 1.7 m apart, 0.1 m in from each end: " + string.Join(", ", wide));
            for (int i = 0; i + 1 < wide.Length; i++)
                Program.Check(wide[i + 1] - wide[i] <= TextRoofFrame.LegPitch + 1e-4f, "no further apart than a leg pitch");

            // The open board's plan: arms at the shares, ending behind the back plane by the channel, no wider than it, no housing in the mass.
            StandSpec open = new StandSpec { Width = 2f, Height = 0.6f, Depth = 0.1f, TiltDeg = 20f, Lift = 1f, FrameX = new[] { -0.7f, 0.7f }, ArmShare = 0.6f, LowShare = 0.25f, ArmBehind = 0.019f, Open = true };
            StandPlan plan = StandPlanner.Plan(open);
            for (int i = 0; i < 2; i++)
            {
                StandMember top = plan.Find(StandMemberKind.ArmTop, i), bottom = plan.Members.Find(m => m.Kind == StandMemberKind.ArmLow && m.Frame == i);
                Program.Near(top.A.Y, plan.YA + 0.6f * 0.6f * (float)Math.Cos(plan.TiltRad), 1e-4f, "the top arm at its share");
                Program.Check(bottom.Half > 0f, "and a low arm");
                Program.Near(bottom.A.Y, plan.YA + 0.25f * 0.6f * (float)Math.Cos(plan.TiltRad), 1e-4f, "the low arm at its share");
                Program.Check(plan.PostTop < plan.YA + 0.7f * 0.6f * (float)Math.Cos(plan.TiltRad), "the post reaches a little over the top arm, not the housing's four fifths: " + plan.PostTop);
                float end = plan.FromBackPlane(top.B.Z, top.B.Y);
                Program.Check(end < 0.006f - 0.019f + 2f * top.Half * (float)Math.Sin(plan.TiltRad) + 1e-3f && end > -0.019f, $"the arm's end is in the channel (behind the plane by up to its depth): {end:0.0000}");
                Program.Check(top.Half <= 0.032f + 1e-6f && bottom.Half <= 0.032f + 1e-6f, "no wider than the channel");
            }

            StandSpec housing = open;
            housing.Open = false;
            housing.ArmShare = housing.LowShare = housing.ArmBehind = 0f;
            StandPlan boxed = StandPlanner.Plan(housing);
            Program.Check(boxed.EstimatedKg > plan.EstimatedKg + 20f, $"a housing's sheet is in the mass of a box and not of letters on rails: {boxed.EstimatedKg:0} and {plan.EstimatedKg:0} kg");
            Program.Check(plan.Open && !boxed.Open && boxed.PostTop > plan.PostTop - 0.5f, "open flag");
            StandSpec wild = open;
            wild.ArmShare = 5f;
            wild.LowShare = -1f;
            wild.ArmBehind = 3f;
            StandPlan held = StandPlanner.Plan(wild);
            Program.Check(held.Members.All(m => (m.A + m.B).IsFinite) && held.Find(StandMemberKind.ArmTop, 0).A.Y < held.YA + 0.6f, "shares and depth held to their ranges");
        }

        internal static GuideSpec Spec(string text, string extra = "") => TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text);

        internal static SignInstance Instance(string text, string extra = "") => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), Spec(text, extra).ToString());

        internal static SignParts Build(SignInstance instance, string support, float reach = 0f) =>
            SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, SupportCatalog.ById(support), reach, SupportDetail.Full, default);

        /// <summary>A hash of every position and normal of every mesh of the parts, in order: two builds with the same hash are the same arrays to the bit.</summary>
        internal static ulong Hash(SignParts parts)
        {
            ulong h = 1469598103934665603UL;
            void Mix(float v)
            {
                h = (h ^ (uint)BitConverter.SingleToInt32Bits(v)) * 1099511628211UL;
            }

            var meshes = new List<MeshBuilder> { parts.Metal, parts.Concrete };
            foreach (FacePart face in parts.Faces)
                meshes.Add(face.Mesh);
            foreach (MeshBuilder solid in parts.Solids)
            {
                if (solid != null)
                    meshes.Add(solid);
            }

            foreach (MeshBuilder mesh in meshes)
            {
                Mix(mesh.VertexCount);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    Mix(mesh.Positions[i].X);
                    Mix(mesh.Positions[i].Y);
                    Mix(mesh.Positions[i].Z);
                    Mix(mesh.Normals[i].X);
                    Mix(mesh.Normals[i].Y);
                    Mix(mesh.Normals[i].Z);
                }
            }

            return h;
        }

        private static void Fields()
        {
            GuideField tilt = TextBoxes.Template.FieldOf("tilt"), tri = TextBoxes.Template.FieldOf("tri");
            Program.Check(tilt != null && tilt.HasSlider && tilt.SliderMin == -TextTilt.MaxDeg && tilt.SliderMax == TextTilt.MaxDeg && tilt.SliderStep == 1 && tilt.Negative, "a signed slider from the depression's greatest to the elevation's greatest in steps of 1");
            Program.Check(tri != null, "a flag for the triangular frame");
            GuideSpec d = TextBoxes.Template.Parse("");
            Program.Check(d.Get("tilt") == "0" && d.Get("tri") == "0", "an upright text by default: " + d.Get("tilt") + " " + d.Get("tri"));
            Program.Check(Instance("字", ";tilt=12").Board.Text.TiltDeg == 12f && Instance("字", ";tilt=99").Board.Text.TiltDeg == TextTilt.MaxDeg && Instance("字", ";tilt=x").Board.Text.TiltDeg == 0f, "the tilt is read and held to its range");
            Program.Check(Instance("字", ";tri=1").Board.Text.Triangular && !Instance("字").Board.Text.Triangular, "the flag");
            Program.Check(TextTilt.Typed(Spec("字", ";tilt=45")) == 45f && TextTilt.Typed(Spec("字", ";tilt=")) == 0f, "the typed tilt");
            Program.Check(ShortcutPolicy.ToolKeys.Any(k => k.Path.EndsWith("/minus")) || ShortcutPolicy.ToolKeys.Any(k => k.Path.EndsWith("minus")), "the keys of the standing light box are held while the tool is on");
        }

        /// <summary>The same texts on the same supports as 0.12.77 built them: the hashes were taken from a build of the tree before this change.</summary>
        private static readonly (string Text, string Extra, string Support, ulong Hash)[] s_Golden =
        {
            ("文字灯箱", ";h=0.5", SupportCatalog.SinglePole, 4156439333020871821UL),
            ("文字灯箱文字灯箱", ";h=0.6", SupportCatalog.DoublePole, 6020696815911717055UL),
            ("欢迎光临文字灯箱欢迎光临", ";h=0.8", SupportCatalog.TriplePole, 4096957699916818870UL),
            ("文字\n灯箱", ";h=0.4;style=outline", SupportCatalog.SinglePole, 10397725548363864572UL),
            ("文字灯箱", ";h=0.5", SupportCatalog.Cantilever, 17918812819556098282UL),
            ("文字灯箱", ";h=0.5", SupportCatalog.Gantry, 313724801033694427UL),
            ("文字灯箱文字灯箱", ";h=1", SupportCatalog.WallFlat, 15744116779289685353UL),
            ("文字灯箱", ";h=0.5", SupportCatalog.WallRoof, 15553239626204649295UL)
        };

        private static void Regression()
        {
            var report = new List<string>();
            foreach ((string text, string extra, string support, ulong golden) in s_Golden)
            {
                TextPlans.Clear();
                SignParts parts = Build(Instance(text, extra), support, support == SupportCatalog.WallRoof ? 0.5f : 0f);
                ulong hash = Hash(parts);
                report.Add($"(\"{text}\", \"{extra}\", {support}) {hash}UL");
                if (golden != 0UL)
                    Program.Check(hash == golden, $"{support} \"{text}\" {extra}: built as before ({hash} against {golden})");
            }

            if (Environment.GetEnvironmentVariable("TEXTTILT_GOLDEN") == "1")
            {
                foreach (string line in report)
                    Console.WriteLine("  GOLDEN " + line);
            }
        }

        private static void Modes()
        {
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, 10f, false, true, true, false) == TextLeanMode.Stand, "a single post");
            Program.Check(TextTilt.ModeOf(SupportKind.DoublePole, false, 10f, false, true, true, false) == TextLeanMode.Stand && TextTilt.ModeOf(SupportKind.TriplePole, false, 10f, false, true, true, false) == TextLeanMode.Stand, "a double and a triple post");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, 0f, false, true, true, false) == TextLeanMode.None, "upright and no triangular frame: as ever");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, 0f, true, true, true, false) == TextLeanMode.Stand, "the triangular frame asked for, upright");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, 10f, false, true, true, true) == TextLeanMode.None, "another board made the support's frame: it does not lean");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, true, 10f, false, true, true, false) == TextLeanMode.Roof && TextTilt.ModeOf(SupportKind.WallFlat, true, 0f, true, true, true, false) == TextLeanMode.Roof, "a roof");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, true, 10f, false, false, true, false) == TextLeanMode.None, "a roof without the text supports of 0.12.76 is not built");
            foreach (SupportKind kind in new[] { SupportKind.Cantilever, SupportKind.Gantry, SupportKind.DoubleCantilever, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.WallBracket })
                Program.Check(TextTilt.ModeOf(kind, false, 20f, true, true, true, false) == TextLeanMode.None, kind + ": a hung or attached board is not turned back");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, float.NaN, false, true, true, false) == TextLeanMode.None, "NaN is no tilt");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, false, 10f, false, true, true, false) == TextLeanMode.Wall && TextTilt.ModeOf(SupportKind.WallFlat, false, 10f, true, true, true, true) == TextLeanMode.Wall, "a flat wall leans, whether or not another board has taken a post's frame");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, false, 0f, true, true, true, false) == TextLeanMode.None, "a wall has no triangular frame: upright it is as ever");
            Program.Check(TextTilt.ModeOf(SupportKind.WallFlat, false, 10f, false, true, false, false) == TextLeanMode.None && TextTilt.ModeOf(SupportKind.WallFlat, false, 10f, false, false, true, false) == TextLeanMode.None, "a curved wall, or without the text supports: upright");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, TextTilt.MinDeg, false, true, true, false) == TextLeanMode.Stand && TextTilt.ModeOf(SupportKind.SinglePole, false, 0.5f, false, true, true, false) == TextLeanMode.Stand, "the least angle and half a degree are a lean");
            Program.Check(TextTilt.ModeOf(SupportKind.SinglePole, false, TextTilt.MinDeg * 0.9f, false, true, true, false) == TextLeanMode.None, "under the least angle: upright");
            Program.Near(TextTilt.Radians(30f), 30f * (float)Math.PI / 180f, 1e-6f, "radians");
            Program.Near(TextTilt.Radians(90f), TextTilt.MaxDeg * (float)Math.PI / 180f, 1e-6f, "held to the range");
            Program.Near(TextTilt.Radians(-3f), -3f * (float)Math.PI / 180f, 1e-6f, "a depression is a negative angle");
            Program.Near(TextTilt.Radians(-90f), -TextTilt.MaxDeg * (float)Math.PI / 180f, 1e-6f, "held to the range on the depression's side too");
            Program.Check(TextTilt.Radians(float.NaN) == 0f, "no NaN");
            TextTilt.ForgetNotes();
            Program.Check(TextTilt.Note(SupportKind.WallFlat, true, 10f) != null && TextTilt.Note(SupportKind.WallFlat, false, 10f) != null && TextTilt.Note(SupportKind.WallFlat, true, 10f) == null, "a note for a roof and one for a wall: two keys, each said once");
        }

        private static void OnPost()
        {
            foreach (string support in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole })
            {
                foreach (float tilt in new[] { 5f, 15f, 30f })
                {
                    TextPlans.Clear();
                    SignInstance upright = Instance("文字灯箱", ";h=0.5"), leaning = Instance("文字灯箱", ";h=0.5;tilt=" + tilt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    SignParts a = Build(upright, support), b = Build(leaning, support);
                    Program.Check(b.Stand != null && a.Stand == null, support + ": the leaning text has the stand's frames");
                    StandPlan stand = b.Stand;
                    Program.Near(stand.TiltDeg, tilt, 1e-4f, "the planner's tilt is the text's");
                    Program.Check(stand.Open, "the board is no housing");
                    MeshBuilder glow = b.Solids[(int)leaning.Board.Text.Glow], glowUp = a.Solids[(int)upright.Board.Text.Glow];
                    Program.Check(glow != null && glowUp != null && glow.VertexCount == glowUp.VertexCount, support + ": the same letters, " + glow?.VertexCount + " and " + glowUp?.VertexCount);

                    // The faces are in a plane that leans back: every normal of the lit faces is turned up by the tilt.
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

                    Program.Check(front > 50 && right >= front * 9 / 10, $"{support} {tilt}°: the lit faces look up by the tilt: {right} of {front} front normals");

                    // The top of the letters is nearer the post (toward -z) than their bottom: the top vertex of a face is further back.
                    float topZ = glow.Positions.Where(p => p.Y > glow.Positions.Max(q => q.Y) - 0.02f).Average(p => p.Z), bottomZ = glow.Positions.Where(p => p.Y < glow.Positions.Min(q => q.Y) + 0.02f).Average(p => p.Z);
                    Program.Check(bottomZ - topZ > 0.2f * (float)Math.Tan(tilt * Math.PI / 180.0) * 0.5f, $"{support} {tilt}°: the top leans back: {topZ:0.000} against {bottomZ:0.000}");

                    // The letters stand over the frame: nothing of the post is in front of the back plane but what the arms put into the channel.
                    for (int i = 0; i < stand.FrameCount; i++)
                    {
                        StandMember post = stand.Find(StandMemberKind.Post, i);
                        Program.Check(stand.FromBackPlane(0f, post.B.Y) < -stand.PostRadius + 1e-4f, "the post is behind the back plane");
                    }

                    Program.Check(b.Picks.Exists(p => p.Name == "board" && Math.Abs(p.Tilt + stand.TiltRad) < 1e-5f), "the board's pick leans with it");
                    Program.Check(b.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)) && glow.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), "finite");
                }
            }
        }

        private static void ArmsMeetRails()
        {
            foreach (string support in new[] { SupportCatalog.SinglePole, SupportCatalog.DoublePole, SupportCatalog.TriplePole })
            {
                foreach (string extra in new[] { ";h=0.5;tilt=10", ";h=0.5;tilt=30", ";h=1.2;tilt=20", ";h=0.3;tilt=25" })
                {
                    TextPlans.Clear();
                    SignInstance leaning = Instance("文字灯箱文字灯箱", extra);
                    SignParts parts = Build(leaning, support);
                    StandPlan stand = parts.Stand;
                    FramePlan frame = TextPlans.Peek(leaning.Board.Text.Job).Frame;
                    Program.Check(frame != null && frame.Rails.Count > 0, "a frame with rails");
                    float height = leaning.Board.Main.H;
                    TextTilt.Shares(frame, height, out float armShare, out float lowShare);
                    float cyUp = stand.YA + height * 0.5f;
                    foreach (int i in Enumerable.Range(0, stand.FrameCount))
                    {
                        StandMember top = stand.Find(StandMemberKind.ArmTop, i);
                        float want = stand.YA + armShare * height * (float)Math.Cos(stand.TiltRad);
                        Program.Check(Math.Abs(top.A.Y - want) < 1e-4f, $"{support} {extra}: the top arm is at the highest rail's level: {top.A.Y:0.000} against {want:0.000}");
                        float end = stand.FromBackPlane(top.B.Z, top.B.Y);
                        Program.Check(end <= 0.0045f * 3f && end >= -SupportLayouts.RailDepth - 0.001f, $"{support} {extra}: the arm ends in the channel behind the rail or in the rail, not in the letters: {end:0.0000}");
                        Program.Check(top.Half <= 0.032f + 1e-6f, "an arm is no wider than the channel: " + top.Half);
                    }

                    // A channel behind the top rail at every post's x: the corners of its back face (the depth of a channel behind the back plane) lie on both sides of the post.
                    float topLevel = stand.YA + armShare * height * (float)Math.Cos(stand.TiltRad);
                    foreach (float px in stand.FrameX)
                    {
                        var back = new List<Vec3>();
                        for (int k = 0; k < parts.Metal.VertexCount; k++)
                        {
                            Vec3 p = parts.Metal.Positions[k];
                            if (Math.Abs(p.Y - topLevel) < SupportLayouts.RailWidth && Math.Abs(stand.FromBackPlane(p.Z, p.Y) + SupportLayouts.RailDepth) < 0.002f)
                                back.Add(p);
                        }

                        Program.Check(back.Count >= 4 && back.Min(p => p.X) <= px - 0.1f && back.Max(p => p.X) >= px + 0.1f, $"{support} {extra}: a channel behind the rail at the post x {px:0.00} (its back face: {back.Count} corners)");
                    }
                }
            }
        }

        private static void Upright()
        {
            TextPlans.Clear();
            SignInstance plain = Instance("文字灯箱", ";h=0.5"), framed = Instance("文字灯箱", ";h=0.5;tri=1");
            SignParts a = Build(plain, SupportCatalog.SinglePole), b = Build(framed, SupportCatalog.SinglePole);
            Program.Check(a.Stand == null && b.Stand != null && b.Stand.TiltDeg == 0f, "upright on the frames of the stand");
            MeshBuilder glow = b.Solids[(int)framed.Board.Text.Glow];
            Program.Check(glow.Normals.Count(n => n.Z > 0.5f && Math.Abs(n.Y) < 1e-4f) > 50, "the faces are upright");
            Program.Check(b.Metal.VertexCount > a.Metal.VertexCount, "heavier steel than a post: " + b.Metal.VertexCount + " against " + a.Metal.VertexCount);
        }

        private static void PlanShared()
        {
            TextPlans.Clear();
            SignInstance up = Instance("文字灯箱", ";h=0.5"), tilted = Instance("文字灯箱", ";h=0.5;tilt=20");
            Build(up, SupportCatalog.SinglePole);
            TextPlanResult first = TextPlans.Peek(up.Board.Text.Job);
            Build(tilted, SupportCatalog.SinglePole);
            TextPlanResult second = TextPlans.Peek(tilted.Board.Text.Job);
            Program.Check(first != null && ReferenceEquals(first, second), "one plan for both: the plan's key has no angle in it");
            Program.Check(up.Board.Text.Job.Equals(tilted.Board.Text.Job), "the job is the same");
        }

        private static void Refused()
        {
            var lines = new List<string>();
            Action<string> before = StandBoxes.Log;
            StandBoxes.Log = lines.Add;
            TextTilt.ForgetNotes();
            try
            {
                foreach (string support in new[] { SupportCatalog.Cantilever, SupportCatalog.Gantry })
                {
                    TextPlans.Clear();
                    SignInstance up = Instance("文字灯箱", ";h=0.5"), tilted = Instance("文字灯箱", ";h=0.5;tilt=20");
                    SignParts a = Build(up, support), b = Build(tilted, support);
                    Program.Check(b.Stand == null && Hash(a) == Hash(b), support + ": built upright, to the bit");
                    Build(tilted, support);
                }

                Program.Check(lines.Count == 2 && lines.All(l => l.Contains("elevation angle")), "said once for each kind of support: " + lines.Count);
            }
            finally
            {
                StandBoxes.Log = before;
            }
        }
    }
}
