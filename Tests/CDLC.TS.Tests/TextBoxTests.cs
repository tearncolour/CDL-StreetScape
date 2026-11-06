using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The text light box of form 1 (0.12.75): the template and its settings, the fonts, the shared plans (a worker waits, the main thread does not), the boxes built on a post and on a wall (the glow
    /// face and the return, the studs and rails, closed letters), the far tile's bars, the support planner's ink area, the editor's hints. The look and the glow are not testable here.
    /// </summary>
    internal static class TextBoxTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("text box: the template, its fields and its settings", Template);
            Program.Run("text box: the box of the text is the board", Box);
            Program.Run("text box: fonts, ids and the folder", Fonts);
            Program.Run("text box: plans are shared, a worker waits and the main thread does not", Plans);
            Program.Run("text box: built on a post, the letters and their frame", OnPost);
            Program.Run("text box: built on a wall and in a far tile", WallAndFar);
            Program.Run("text box: the letters are closed solids", Closed);
            Program.Run("text box: the support planner counts the ink", Ink);
            Program.Run("text box: the editor's hints", HintLines);
            Program.Run("text box: eight threads share one plan", Threads);
            Program.Run("text box: the letters read the right way round", Orientation);
            Program.Run("text box: every support takes it", EverySupport);
            Program.Run("text box: a text with too many points for one mesh", TooDetailed);
            Program.Run("text box: a text with many points is kept in few places", HeavyParts);
            Program.Run("text box: the main thread builds provisional parts that are not kept", Provisional);
            Program.Run("text box: in a tile, and its thumbnail", InTile);
            Program.Run("text box: the editor's picture is the letters lit, not a black face", LitPreview);
            Program.Run("text box: the editor says only what the player can act on", ShownHints);
            TextPlans.Counters(out int made, out int deferred, out int waited, out double ms, out double worst);
            Console.WriteLine($"  text box plans: {made} made in {ms:0} ms (the longest {worst:0.0} ms), {waited} waited for by a worker, {deferred} asked by a thread that does not wait");
        }

        /// <summary>The settings with this text as it is typed (a backslash and an n are a line break) and the other fields written as pairs after a semicolon.</summary>
        private static GuideSpec Spec(string text = "文字灯箱", string extra = "") => TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text);

        private static SignDef Def => SignCatalog.ById(TextBoxes.TemplateId);

        private static SignInstance Instance(string spec) => SignInstances.Of(Def, spec);

        private static SignParts Parts(SignInstance instance, SupportDetail detail = SupportDetail.Full, string support = null)
        {
            SupportDef def = support != null ? SupportCatalog.ById(support) : SupportPlanner.Auto(instance.Def);
            var boards = new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } };
            return SignBuilder.BuildAssembly(boards, def, 0f, detail, default);
        }

        private static void Template()
        {
            GuideTemplate t = TextBoxes.Template;
            Program.Check(t.Id == TextBoxes.TemplateId && t.Group == "custom" && t.Standalone && !t.IsMarking, "a board of the player's tab, listed by itself");
            foreach (string key in new[] { "text", "font", "h", "sp", "ls", "align", "glow", "ret", "depth" })
                Program.Check(t.FieldOf(key) != null, "the field " + key);
            Program.Check(Def != null && Def.Template == t && SignCatalog.ChoiceById(TextBoxes.TemplateId) != null, "the catalogue has the sign and its choice");

            GuideSpec d = t.Default();
            Program.Check(d.Get("font") == TextFonts.DefaultChoice && d.Get("glow") == "warm" && d.Get("ret") == "black" && d.Get("align") == "centre", "the defaults");
            Program.Check(t.Parse("font=nothing.ttf;glow=purple;align=up;ret=pink").ToString() == d.ToString().Replace("text=文字灯箱", "text=文字灯箱"), "a font, glow, alignment or return that is not a choice is the default");
            Program.Check(Spec(new string('字', 60)).Get("text").Length == 40, "the text is cut at 40 characters of the field");
            string typed = Spec("上\\n下").ToString();
            Program.Check(TextBoxes.TextOf(t.Parse(typed)) == "上\n下", "a typed \\n is a line break and survives the written settings: " + typed);
            Program.Check(Encoding_Bytes(Spec(new string('字', 40), ";h=3;sp=50;ls=200;depth=12").ToString()) <= GuideSpec.MaxBytes, "the longest settings fit the saved field");

            var colours = new HashSet<SolidColour>();
            foreach (string glow in new[] { "warm", "white", "red", "gold", "blue", "pink", "green" })
            {
                SolidColour colour = TextBoxes.GlowOf(glow);
                Program.Check(SolidGlow.Is(colour), "the glow " + glow + " is a glowing material");
                colours.Add(colour);
            }

            Program.Check(colours.Count == 7, "seven different glows");
            foreach (string ret in new[] { "black", "white", "steel", "gray", "gold" })
                Program.Check(!SolidGlow.Is(TextBoxes.ReturnOf(ret)) && !SolidGlass.Is(TextBoxes.ReturnOf(ret)), "the return " + ret + " is not a glow or a glass");
            Program.Check(Enum.GetValues(typeof(SolidColour)).Cast<int>().Max() < SignParts.SolidCount, "no colour needs a new material slot");
        }

        private static int Encoding_Bytes(string text) => System.Text.Encoding.UTF8.GetByteCount(text);

        private static void Box()
        {
            SignInstance one = Instance(Spec("文字灯箱").ToString());
            TextBoard text = one.Board.Text;
            Program.Check(text != null && text.Layout != null && !text.NoFont && !text.FontFell, "the board carries its text and its layout");
            Program.Near(one.Def.Width, text.Layout.Width, 1e-4f, "the board is as wide as the layout: " + one.Def.Width);
            Program.Near(one.Def.Height, text.Layout.Height, 1e-4f, "and as tall");
            Program.Near(one.Def.Height, 0.5f, 0.01f, "one line of letters 0.5 m: " + one.Def.Height);
            Program.Check(one.Def.Width > 4 * 0.5f * 0.9f && one.Def.Width < 4 * 0.5f * 1.2f, "four wide characters are about four letter heights wide: " + one.Def.Width);

            SignInstance two = Instance(Spec("上行\\n下行").ToString());
            Program.Check(two.Board.Text.Layout.Lines == 2 && two.Def.Height > one.Def.Height * 1.9f, "a typed \\n makes a second line: " + two.Def.Height);
            Program.Near(one.Board.Text.Depth, 0.06f, 1e-6f, "the return is 6 cm deep by default");
            Program.Near(Instance(Spec("文字灯箱", ";depth=9").ToString()).Board.Text.Depth, 0.09f, 1e-6f, "and as deep as the setting says");
            Program.Check(Instance(Spec("文字灯箱", ";depth=99").ToString()).Board.Text.Depth <= 0.12f + 1e-6f && Instance(Spec("文字灯箱", ";depth=0").ToString()).Board.Text.Depth >= 0.03f - 1e-6f, "held to 3 to 12 cm");
            SignInstance big = Instance(Spec("文字灯箱", ";h=2").ToString());
            Program.Near(big.Def.Height, 2f, 0.05f, "a letter of 2 m");
            Program.Check(Instance(Spec("文字灯箱", ";h=9").ToString()).Def.Height <= TextBoxes.MaxLetter + 0.01f && Instance(Spec("文字灯箱", ";h=0.01").ToString()).Def.Height >= TextBoxes.MinLetter - 0.01f, "the letter height is held to 0.15 to 3 m");
            SignInstance empty = Instance(Spec("").ToString());
            Program.Check(empty.Def.Width >= TextBoxes.MinBox && empty.Def.Height >= TextBoxes.MinBox, "no text is still a box");
            Program.Check(one.Def.InkArea != null && one.Def.InkArea() > 0f && one.Def.InkArea() < one.Def.Width * one.Def.Height, "the sign's ink is less than its box");
            Program.Check(ReferenceEquals(one, Instance(Spec("文字灯箱").ToString())), "one spec, one instance");
        }

        private static void Fonts()
        {
            Program.Check(TextFonts.IdOf("bundled") == TextFonts.DefaultId && TextFonts.IdOf("") == TextFonts.DefaultId && TextFonts.IdOf(null) == TextFonts.DefaultId, "the default font's id");
            int a = TextFonts.IdOf("a.ttf"), b = TextFonts.IdOf("b.ttf");
            Program.Check(a != b && a != TextFonts.DefaultId && b != TextFonts.DefaultId && a == TextFonts.IdOf("a.ttf"), "a file's id is stable, its own and never the default's");

            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-fonts-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                foreach (string name in new[] { "b.TTF", "a.otf", "notes.txt", "c.ttf" })
                    File.WriteAllText(Path.Combine(dir, name), "x");
                List<string> found = TextFonts.Scan(dir);
                Program.Check(found.Count == 3 && found[0] == "a.otf" && found[1] == "b.TTF" && found[2] == "c.ttf", "the folder's fonts, sorted, and nothing else: " + string.Join(",", found));
                Program.Check(TextFonts.Scan(Path.Combine(dir, "missing")).Count == 0 && TextFonts.Scan(null).Count == 0, "no folder, no fonts");
                string before = TextFonts.Folder;
                TextFonts.Begin(dir);
                GuideChoice[] choices = TextFonts.Choices();
                Program.Check(choices.Length == 4 && choices[0].Value == TextFonts.DefaultChoice && choices[1].Value == "a.otf", "the choices: the default and the files");
                TextBoxes.RefreshFonts();
                Program.Check(TextBoxes.Template.FieldOf("font").Choices.Length == 4, "the template's list is the folder's");
                // A file that is not a font is a font that cannot be read: its text is set in the default one.
                string name2 = "c.ttf";
                Program.Check(TextBoxes.Template.Parse("font=" + name2).Get("font") == name2, "a listed font is kept in the settings");
                SignInstance fell = Instance(TextBoxes.Template.Parse("text=文字;font=" + name2).ToString());
                Program.Check(fell.Board.Text.FontFell && fell.Board.Text.Job.FontId == TextFonts.DefaultId && fell.Board.Text.Layout != null, "an unreadable font falls back to the default");
                Program.Check(TextBoxes.Hints(fell.Spec, false).Any(l => l.Contains("Font not found")) && TextBoxes.Hints(fell.Spec, true).Any(l => l.Contains("找不到所选字体")), "and the editor says so, in both languages");
                // The fonts held are at most MaxLoaded: the oldest of four real files goes.
                string bundled = SignFonts.Cjk ?? FontLetterTests.RepoFile("Fonts", SignFonts.BundledCjk);
                foreach (string copy in new[] { "p1.ttf", "p2.ttf", "p3.ttf", "p4.ttf" })
                    File.Copy(bundled, Path.Combine(dir, copy), true);
                TextFonts.Release();
                TextFonts.Begin(dir);
                var copies = new List<FontOutlines>();
                foreach (string copy in new[] { "p1.ttf", "p2.ttf", "p3.ttf" })
                    copies.Add(TextFonts.Get(TextFonts.IdOf(copy)));
                Program.Check(ReferenceEquals(TextFonts.Get(TextFonts.IdOf("p1.ttf")), copies[0]), "a font that was used is held and is the newest");
                copies.Add(TextFonts.Get(TextFonts.IdOf("p4.ttf")));
                Program.Check(copies.All(f => f != null && f.Ok), "four files in the folder are four fonts");
                Program.Check(ReferenceEquals(TextFonts.Get(TextFonts.IdOf("p4.ttf")), copies[3]) && ReferenceEquals(TextFonts.Get(TextFonts.IdOf("p3.ttf")), copies[2]) && ReferenceEquals(TextFonts.Get(TextFonts.IdOf("p1.ttf")), copies[0]), "the three used last are held (the first, used again, with them)");
                Program.Check(!ReferenceEquals(TextFonts.Get(TextFonts.IdOf("p2.ttf")), copies[1]), "the one not used for longest was let go and is read again: at most " + TextFonts.MaxLoaded + " are held");
                Program.Check(TextFonts.Get(TextFonts.IdOf("../c.ttf")) == null, "a name that goes elsewhere is no font");
                TextFonts.Begin(before);
                TextBoxes.RefreshFonts();
                Program.Check(TextBoxes.Template.FieldOf("font").Choices.Length == 1 + TextFonts.Files.Count, "the list follows the folder it is given");
                TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Held at most MaxLoaded: the oldest goes (fonts of ids that have no file are tried once and fail).
            var loaded = TextLayoutTests.BundledFont();
            for (int i = 0; i < TextFonts.MaxLoaded + 2; i++)
                TextFonts.Use(1000 + i, loaded);
            Program.Check(TextFonts.Get(1000 + TextFonts.MaxLoaded + 1) != null, "a font that was handed over is held");
            TextFonts.Use(TextFonts.DefaultId, loaded);
        }

        private static TextPlanJob JobOf(string text, float height = 0.5f) =>
            new TextPlanJob(text, TextFonts.DefaultId, height, 0f, 1.15f, TextAlign.Centre, TextPlans.RelativeFlatness(height));

        private static void Plans()
        {
            TextPlans.Clear();
            TextPlanJob job = JobOf("口小");
            Program.Check(TextPlans.Peek(job) == null, "nothing is kept at first");
            int epoch = TextPlans.Epoch;
            TextPlanResult made = TextPlans.Get(job);
            Program.Check(made != null && made.Frame != null && made.Meshes.Length == 2 && TextPlans.Epoch != epoch, "a thread that is not the main one makes the plan and waits for it");
            Program.Check(ReferenceEquals(made, TextPlans.Get(job)) && ReferenceEquals(made, TextPlans.Peek(job)), "the next ask is the kept plan");
            Program.Check(TextPlans.RelativeFlatness(0.5f) > TextPlans.RelativeFlatness(3f) && TextPlans.RelativeFlatness(0.001f) <= TextGlyphs.MaxRelative, "a larger letter is cut with a smaller share of its height");

            // The main thread does not wait: it gets null, and the plan is made on a worker.
            TextPlans.Clear();
            TextPlanJob other = JobOf("文字灯箱");
            bool nullOnMain = false, arrived = false;
            var thread = new Thread(() =>
            {
                TextPlans.MarkMainThread();
                nullOnMain = TextPlans.Get(other) == null;
                for (int i = 0; i < 400 && !arrived; i++)
                {
                    Thread.Sleep(10);
                    arrived = TextPlans.Peek(other) != null;
                }

                TextPlans.ForgetMainThread();
            });
            thread.Start();
            thread.Join();
            Program.Check(nullOnMain, "the main thread was given no plan and did not make one");
            Program.Check(arrived, "the plan was made on a worker in the meantime");
            Program.Check(!TextPlans.OnMainThread, "(the test's main thread is forgotten again)");
        }

        private static float ZRange(MeshBuilder mesh, out float lo)
        {
            lo = float.MaxValue;
            float hi = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                lo = Math.Min(lo, p.Z);
                hi = Math.Max(hi, p.Z);
            }

            return hi;
        }

        private static void OnPost()
        {
            TextPlans.Clear();
            SignInstance instance = Instance(Spec("文字灯箱").ToString());
            SignParts parts = Parts(instance);
            Program.Check(!parts.Provisional, "built where the plan can be waited for: not provisional");
            TextBoard text = instance.Board.Text;
            MeshBuilder glow = parts.Solids[(int)text.Glow], ret = parts.Solids[(int)text.Return];
            Program.Check(glow != null && glow.VertexCount > 100 && ret != null && ret.VertexCount > glow.VertexCount, "a glow face and a return: " + glow?.VertexCount + ", " + ret?.VertexCount);
            Program.Check(glow.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)) && ret.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), "every coordinate is a number");

            float face = SignBuilder.FacePlane(parts.Layout, instance);
            float top = ZRange(glow, out float glowLow);
            Program.Near(top, face, 1e-4f, "the face of the letters is the board's face plane");
            Program.Near(glowLow, face, 1e-4f, "and all of the glow is in it: " + glowLow + " to " + top);
            float retTop = ZRange(ret, out float retLow);
            Program.Near(retTop, face, 1e-4f, "the return reaches the face");
            Program.Near(retLow, face - text.Depth, 1e-4f, "and is as deep as the setting says");
            Program.Near(face - parts.Layout.BackZ, text.Reach, 1e-4f, "the letters stand Stand + Depth in front of the support's plane");

            // The frame: studs back to the plane of the support, rails in front of it, in the support's steel.
            MeshBuilder metal = parts.Metal;
            int studs = instance.Board.Text.Job != null ? TextPlans.Peek(text.Job).Frame.Studs.Count : 0;
            Program.Check(studs >= 8, "the plan has studs for the letters: " + studs);
            int steel = 0;
            foreach (Vec3 p in metal.Positions)
            {
                if (p.Z > parts.Layout.BackZ + 0.0005f && p.Z < parts.Layout.BackZ + text.Stand + 0.005f)
                    steel++;
            }

            Program.Check(steel >= studs * 4, "studs and rails stand between the support and the letters: " + steel);
            PickShape pick = parts.Picks.Find(p => p.Name == "board");
            FramePlan framePlan = TextPlans.Peek(text.Job).Frame;
            int met = 0, near = 0;
            foreach (FrameStud stud in framePlan.Studs)
            {
                float sx = pick.CentreX - stud.At.X, sy = pick.CentreY + stud.At.Y;
                if (metal.Positions.Any(p => Math.Abs(p.X - sx) < 7e-3f && Math.Abs(p.Y - sy) < 7e-3f && Math.Abs(p.Z - parts.Layout.BackZ) < 1e-4f))
                    met++;
                if (glow.Positions.Any(p => Math.Abs(p.X - sx) < 0.4f * text.Job.LetterHeight && Math.Abs(p.Y - sy) < 0.4f * text.Job.LetterHeight))
                    near++;
            }

            Program.Check(met == framePlan.Studs.Count && near == framePlan.Studs.Count, "every stud stands where the plan put it (as the viewer sees the letters) and is by a letter: " + met + " and " + near + " of " + framePlan.Studs.Count);
            Program.Check(parts.Picks.Exists(p => p.Name == "board"), "the board can be pointed at");

            // The same text twice: the plan is the one kept, the mesh the same.
            SignParts again = Parts(instance);
            Program.Check(again.Solids[(int)text.Glow].VertexCount == glow.VertexCount && again.Metal.VertexCount == metal.VertexCount, "built again, the same");

            // The colours follow the settings.
            SignInstance blue = Instance(Spec("文字灯箱", ";glow=blue;ret=white").ToString());
            SignParts blueParts = Parts(blue);
            Program.Check(blueParts.Solids[(int)SolidColour.GlowBlue] != null && blueParts.Solids[(int)SolidColour.White] != null && (blueParts.Solids[(int)SolidColour.GlowWarm] == null || blueParts.Solids[(int)SolidColour.GlowWarm].VertexCount == 0), "the glow and the return colours are the chosen ones");
        }

        private static void WallAndFar()
        {
            SignInstance instance = Instance(Spec("文字灯箱").ToString());
            SignParts post = Parts(instance);
            // 0.12.75's wall: the studs and no frame (features.textSupports off; TextSupportTests has the base frame).
            SignParts wall;
            bool supportsWere = TextSupports.Enabled;
            try
            {
                TextSupports.Enabled = false;
                wall = Parts(instance, SupportDetail.Full, SupportCatalog.WallFlat);
            }
            finally
            {
                TextSupports.Enabled = supportsWere;
            }

            Program.Check(wall.Layout.Kind == SupportKind.WallFlat, "a flat wall support");
            TextBoard text = instance.Board.Text;
            TextPlanResult plan = TextPlans.Peek(text.Job);
            int studVertices = plan.Frame.Studs.Count;
            Program.Check(wall.Metal.VertexCount > 0 && wall.Metal.VertexCount < post.Metal.VertexCount, "on a wall there are studs and no rails: " + wall.Metal.VertexCount + " against " + post.Metal.VertexCount);
            float railFront = wall.Layout.BackZ + FrameStyle.For(text.Job.LetterHeight).RailSize;
            Program.Check(post.Metal.Positions.Any(p => Math.Abs(p.Z - railFront) < 1e-5f), "on a post the rails' front is at the rail's size in front of the support");
            Program.Check(!wall.Metal.Positions.Any(p => Math.Abs(p.Z - railFront) < 1e-5f), "on a wall there is nothing there: no rail");
            float face = SignBuilder.FacePlane(wall.Layout, instance);
            ZRange(wall.Solids[(int)text.Glow], out float low);
            Program.Near(low, face, 1e-4f, "the letters' face on the wall is the face plane");
            int toWall = 0;
            foreach (Vec3 p in wall.Metal.Positions)
            {
                if (Math.Abs(p.Z - wall.Layout.BackZ) < 1e-4f)
                    toWall++;
            }

            Program.Check(toWall >= studVertices * 3, "every stud goes back to the wall: " + toWall + " of " + studVertices);

            // A far tile has the letters' lit faces (0.12.105; TextFarTests has the rest): more than one bar a line, no return, a finished build.
            SignParts far = Parts(instance, SupportDetail.Reduced);
            MeshBuilder letters = far.Solids[(int)text.Glow];
            Program.Check(letters != null && letters.VertexCount > 24 * text.Layout.Lines, "a far tile has the letters, not one lit bar for each line: " + letters?.VertexCount);
            Program.Check(far.Solids[(int)text.Return] == null || far.Solids[(int)text.Return].VertexCount == 0, "and no return");
            Program.Check(!far.Provisional, "a far tile is a finished one");
            SignInstance leftOne = Instance(Spec("文字灯箱", ";align=left").ToString());
            Program.Near(leftOne.Def.Width, instance.Def.Width, 1e-4f, "(the same box)");
            letters.Bounds(out Vec3 letterLow, out Vec3 letterHigh);
            Program.Check(letterHigh.X - letterLow.X <= text.Layout.LineWidths[0] + 1e-3f && letterHigh.X - letterLow.X > 0.7f * text.Layout.LineWidths[0], "the letters are as wide as their line: " + (letterHigh.X - letterLow.X));
            Program.Near((letterHigh.X + letterLow.X) * 0.5f, 0f, 0.05f, "and a single line is in the middle of the box");
            SignInstance rightLines = Instance(Spec("长的一行字\\n短", ";align=right").ToString());
            MeshBuilder rightLetters = Parts(rightLines, SupportDetail.Reduced).Solids[(int)rightLines.Board.Text.Glow];
            // Right-aligned: the viewer's right is -X, so both lines end on the same, smallest X: the two rows of letters have the same edge to within a letter's side bearing.
            float mid = (rightLetters.Positions.Min(p => p.Y) + rightLetters.Positions.Max(p => p.Y)) * 0.5f;
            float edgeUpper = rightLetters.Positions.Where(p => p.Y > mid).Min(p => p.X), edgeLower = rightLetters.Positions.Where(p => p.Y < mid).Min(p => p.X);
            Program.Check(Math.Abs(edgeUpper - edgeLower) < 0.05f, $"right-aligned lines end together: {edgeUpper:0.000}, {edgeLower:0.000}");
            SignParts two = Parts(Instance(Spec("上行\\n下行").ToString()), SupportDetail.Reduced);
            Program.Check(two.Solids[(int)SolidColour.GlowWarm].VertexCount > 48, "two lines of letters, not two bars");
        }

        /// <summary>Whether every edge of the triangles (the positions welded at a hundredth of a millimetre) is shared by exactly two.</summary>
        private static int OpenEdges(params MeshBuilder[] meshes)
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
            foreach (MeshBuilder mesh in meshes)
            {
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int a = Id(mesh.Positions[mesh.Indices[t]]), b = Id(mesh.Positions[mesh.Indices[t + 1]]), c = Id(mesh.Positions[mesh.Indices[t + 2]]);
                    foreach ((int, int) e in new[] { (a, b), (b, c), (c, a) })
                    {
                        var key = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1);
                        edges[key] = edges.TryGetValue(key, out int n) ? n + 1 : 1;
                    }
                }
            }

            return edges.Values.Count(n => n != 2);
        }

        private static void Closed()
        {
            foreach (string word in new[] { "口", "小", "A", "文字灯箱", "AB口8" })
            {
                SignInstance instance = Instance(Spec(word).ToString());
                SignParts parts = Parts(instance);
                TextBoard text = instance.Board.Text;
                int open = OpenEdges(parts.Solids[(int)text.Glow], parts.Solids[(int)text.Return]);
                Program.Check(open == 0, "the letters of '" + word + "' are closed solids: " + open + " open edge(s)");
                TextPlanResult plan = TextPlans.Peek(text.Job);
                foreach (GlyphMesh mesh in plan.Meshes.Where(m => m != null))
                {
                    Program.Check(mesh.IsSound(out string why), "a glyph of '" + word + "' is sound: " + why);
                    break;
                }
            }
        }

        private static void Ink()
        {
            SignInstance three = Instance(Spec("三个字").ToString());
            Program.Check(SupportPlanner.AreaOf(three.Def) <= three.Def.Width * three.Def.Height, "the planner counts at most the box");
            Program.Check(SupportPlanner.Auto(three.Def).Kind == SupportKind.SinglePole, "three characters of 0.5 m stand on one post: " + three.Def.Width);
            SignInstance nine = Instance(Spec("三个字\\n牌子上\\n有九字", ";h=0.5").ToString());
            TextPlans.Get(nine.Board.Text.Job);
            Program.Check(nine.Def.Width <= SupportPlanner.SinglePostMaxWidth && nine.Def.Height <= SupportPlanner.SinglePostMaxHeight && nine.Def.Width * nine.Def.Height > SupportPlanner.SinglePostMaxArea, "nine characters in three lines: a box over a post's area: " + nine.Def.Width + " x " + nine.Def.Height);
            Program.Check(SupportPlanner.Auto(nine.Def).Kind == SupportKind.SinglePole, "but one post carries its ink");
            SignInstance six = Instance(Spec("六个字的招牌", ";h=0.6").ToString());
            Program.Check(six.Def.Width > SupportPlanner.SinglePostMaxWidth && SupportPlanner.Auto(six.Def).Kind == SupportKind.DoublePole, "six wide characters of 0.6 m need two: " + six.Def.Width);
            TextPlanResult plan = TextPlans.Get(six.Board.Text.Job);
            Program.Near(SupportPlanner.AreaOf(six.Def), plan.Frame.EffectiveArea, 1e-5f, "once the plan is there the planner counts its ink");
            Program.Check(plan.Frame.EffectiveArea < 0.6f * six.Def.Width * six.Def.Height, "the ink is well under the box: " + plan.Frame.EffectiveArea + " of " + six.Def.Width * six.Def.Height);

            // Before the plan: an estimate from the characters, less than the box.
            TextPlans.Clear();
            TextBoard fresh = Instance(Spec("六个字的招牌", ";h=0.6;depth=7").ToString()).Board.Text;
            float estimate = fresh.InkArea();
            Program.Check(estimate > 0f && estimate < fresh.Layout.Width * fresh.Layout.Height, "before the plan: an estimate below the box: " + estimate);
            Program.Near(TextBoard.EstimatedInk(null, 0.5f), 0f, 0f, "no layout, no ink");
            TextLayoutResult wideOne = TextLayout.Layout("字", TextStyle.Of(1f), TextFace.Of(TextLayoutTests.BundledFont())), latinOne = TextLayout.Layout("A", TextStyle.Of(1f), TextFace.Of(TextLayoutTests.BundledFont()));
            Program.Near(TextBoard.EstimatedInk(wideOne, 1f), 0.33f, 1e-4f, "a wide character is a third of the letter height squared");
            Program.Check(TextBoard.EstimatedInk(latinOne, 1f) > 0.05f && TextBoard.EstimatedInk(latinOne, 1f) < 0.2f, "a letter is about a ninth: " + TextBoard.EstimatedInk(latinOne, 1f));
            Program.Near(TextBoard.EstimatedInk(TextLayout.Layout(" ", TextStyle.Of(1f), TextFace.Of(TextLayoutTests.BundledFont())), 1f), 0f, 1e-6f, "a space has no ink");
            // The arm: a row of letters fits where a plate of its box would not.
            SignInstance arm = Instance(Spec("七个字的牌子呢\\n七个字的牌子呢\\n七个字的牌子呢", ";h=0.5;ls=100").ToString());
            TextPlans.Get(arm.Board.Text.Job);
            Program.Check(arm.Def.Width <= SupportPlanner.MaxArmWidth && arm.Def.Height <= SupportPlanner.MaxArmHeight && arm.Def.Width * arm.Def.Height > SupportPlanner.MaxArmArea, "a box too large for an arm's area: " + arm.Def.Width + " x " + arm.Def.Height);
            Program.Check(SupportPlanner.FitsArm(arm.Def), "but its ink fits a signal arm");
            var box = new SignDef { Width = 2f, Height = 1f, InkArea = () => 5f };
            Program.Near(SupportPlanner.AreaOf(box), 2f, 0f, "an ink area larger than the box is the box");
            box.InkArea = () => 0f;
            Program.Near(SupportPlanner.AreaOf(box), 2f, 0f, "an ink area of nothing is the box");
            box.InkArea = null;
            Program.Near(SupportPlanner.AreaOf(box), 2f, 0f, "no ink area, the box");
        }

        private static void LitPreview()
        {
            TextPlans.Clear();
            string spec = Spec("口").ToString();
            SignInstance instance = Instance(spec);
            TextBoard text = instance.Board.Text;
            byte[] glow = Raster.SolidRgb(text.Glow);
            Program.Check(TextFacePreview.Raster(null, null, glow, out _, out _) == null && TextFacePreview.Raster(text, null, glow, out _, out _) == null && TextFacePreview.Raster(text, TextPlans.Get(text.Job), null, out _, out _) == null, "no board, no plan or no colour: no picture");
            byte[] raw = TextFacePreview.Raster(text, TextPlans.Get(text.Job), glow, out int w, out int h);
            Program.Check(raw != null && raw.Length == w * h * 4 && Math.Max(w, h) == TextFacePreview.LongSide, "the long side is " + TextFacePreview.LongSide + ": " + w + " x " + h);
            Program.Near((float)w / h, text.Layout.Width / text.Layout.Height, 0.02f, "the picture has the board's proportions");
            int lit = 0, ground = 0, other = 0;
            for (int i = 0; i < w * h; i++)
            {
                int o = i * 4;
                bool isGlow = raw[o] == glow[0] && raw[o + 1] == glow[1] && raw[o + 2] == glow[2];
                bool isGround = raw[o] == TextFacePreview.Ground[0] && raw[o + 1] == TextFacePreview.Ground[1] && raw[o + 2] == TextFacePreview.Ground[2];
                lit += isGlow ? 1 : 0;
                ground += isGround ? 1 : 0;
                other += !isGlow && !isGround ? 1 : 0;
                if (raw[o + 3] != 255)
                    other += 1000000;
            }

            Program.Check(lit > 0 && ground > 0 && other < lit + ground, "some pixels lit in the glow colour, some the dark ground, the rest the antialiased edge: " + lit + " / " + ground + " / " + other);
            Program.Check(raw[3] == 255 && raw[0] == TextFacePreview.Ground[0], "the corner is the ground, opaque");

            // The box letter is a ring: its middle is the ground.
            int mid = ((h / 2) * w + w / 2) * 4;
            Program.Check(raw[mid] == TextFacePreview.Ground[0] && raw[mid + 1] == TextFacePreview.Ground[1], "the middle of a box letter is the ground");

            // An L has its stem on the left and its foot at the bottom: nothing is mirrored or turned over, and the bottom row comes first.
            TextBoard bar = Instance(Spec("L").ToString()).Board.Text;
            byte[] l = TextFacePreview.Raster(bar, TextPlans.Get(bar.Job), glow, out int lw, out int lh);
            var side = new int[2];
            var rows = new int[2];
            for (int y = 0; y < lh; y++)
            {
                for (int x = 0; x < lw; x++)
                {
                    int o = (y * lw + x) * 4;
                    if (l[o] != glow[0] || l[o + 1] != glow[1])
                        continue;
                    side[x < lw / 2 ? 0 : 1]++;
                    rows[y < lh / 2 ? 0 : 1]++;
                }
            }

            Program.Check(side[0] > side[1], "an L has its stem on the left of the picture: " + side[0] + " left, " + side[1] + " right");
            Program.Check(rows[0] > rows[1], "the bottom row comes first and an L has its foot at the bottom: " + rows[0] + " in the first half of the rows, " + rows[1] + " in the second");
            TextBoard red = Instance(Spec("口", ";glow=red").ToString()).Board.Text;
            byte[] r = TextFacePreview.Raster(red, TextPlans.Get(red.Job), Raster.SolidRgb(red.Glow), out _, out _);
            Program.Check(r.Any(b => b == 214), "the glow colour is the one chosen");

            // Two triangles of one square share a diagonal that runs exactly through the samples of the pixels on it: a sample on a shared edge is in both, and the share must not go over a whole pixel.
            var square = new GlyphMesh { Points = new[] { new Point2(0f, 0f), new Point2(1f, 0f), new Point2(1f, 1f), new Point2(0f, 1f) }, Triangles = new[] { 0, 1, 2, 0, 2, 3 } };
            var squareLayout = new TextLayoutResult { Width = 1f, Height = 1f, MetresPerEm = 1f, LetterHeight = 1f };
            squareLayout.Glyphs.Add(new PlacedGlyph { Codepoint = 'x', X = -0.5f, Y = -0.5f });
            var squareText = new TextBoard { Layout = squareLayout };
            var squarePlan = new TextPlanResult { Layout = squareLayout, Meshes = new[] { square } };
            byte[] sq = TextFacePreview.Raster(squareText, squarePlan, glow, out int qw, out int qh);
            bool within = true;
            for (int i = 0; i < qw * qh; i++)
            {
                for (int c = 0; c < 3; c++)
                    within &= sq[i * 4 + c] >= Math.Min(TextFacePreview.Ground[c], glow[c]) && sq[i * 4 + c] <= Math.Max(TextFacePreview.Ground[c], glow[c]);
            }

            int diagonal = (qh / 2 * qw + qh / 2) * 4;
            Program.Check(within && sq[diagonal] == glow[0], "a pixel on a shared edge is the glow colour, never beyond it: the picture stays between the ground and the glow");

            // Written beside the test for Tests/preview-dock.mjs: a look at the panel with this picture.
            string folder = Path.Combine(AppContext.BaseDirectory, "tmp");
            Directory.CreateDirectory(folder);
            TextBoard sample = Instance(Spec("文字灯箱").ToString()).Board.Text;
            byte[] sampleRaw = TextFacePreview.Raster(sample, TextPlans.Get(sample.Job), Raster.SolidRgb(sample.Glow), out int sw, out int sh);
            File.WriteAllBytes(Path.Combine(folder, "textbox-preview.png"), Png.Encode(sampleRaw, sw, sh));
        }

        private static void ShownHints()
        {
            foreach (TextWarningKind kind in Enum.GetValues(typeof(TextWarningKind)))
            {
                bool shown = TextHints.Shown(new TextWarning(kind, 2, 'x', 0.2f));
                bool expected = kind != TextWarningKind.HeldByRods && kind != TextWarningKind.SingleStud;
                Program.Check(shown == expected, kind + (expected ? " is said" : " is not said") + " (the planner works it out)");
            }

            Program.Check(TextHints.Shown(new TextWarning(TextWarningKind.TooDetailed, 1, 0, 1f)) && !TextHints.Shown(new TextWarning(TextWarningKind.TooDetailed, 2, 0, 0f)), "too detailed: said when it is drawn as bars, not when the curves were only cut coarser");
            TextPlans.Clear();
            string spec = Spec("文字灯箱").ToString();
            Program.Check(TextBoxes.Hints(spec, true).Count == 0 && TextBoxes.Hints(spec, false).Count == 0, "a plain text has no notes at all before the plan");
            TextPlans.Get(Instance(spec).Board.Text.Job);
            Program.Check(TextBoxes.Hints(spec, true).Count == 0 && TextBoxes.Hints(spec, false).Count == 0, "and none after it: the frame's figures and the font folder's note are not on the panel");
            string many = Spec("田田田田田田\\n田田田田田田").ToString();
            TextPlans.Get(Instance(many).Board.Text.Job);
            Program.Check(!TextBoxes.Hints(many, false).Any(l => l.StartsWith("Frame") || l.Contains("held by rods") || l.Contains("Working out")), "nothing of the frame or of the waiting is said");
            Program.Check(TextBoxes.Template.Fields.Where(f => f.Key == "text").All(f => f.Wide) && TextBoxes.Template.Fields.Where(f => f.Key != "text").All(f => !f.Wide), "the text is the one field over the whole width");
            Program.Check(TextBoxes.Template.Fields.All(f => f.Zh.Length <= 10 && f.En.Length <= 28 && !f.Zh.Contains("到") && !f.En.Contains("to 12")), "the labels carry no range or explanation");
        }

        private static void HintLines()
        {
            TextPlans.Clear();
            string spec = Spec("文字灯箱").ToString();
            int absent = new[] { 0x0E01, 0x0531, 0x05D0, 0x10A0 }.First(c => !TextLayoutTests.BundledFont().TryAdvance(c, out _));
            string absentSpec = Spec("字" + char.ConvertFromUtf32(absent)).ToString();
            Program.Check(TextBoxes.Hints(absentSpec, false).Any(l => l.Contains("is left blank")), "a missing character is named before the plan is made too");
            Program.Check(!TextBoxes.Hints(spec, true).Any(l => l.Contains("正在计算")), "before the plan: it does not say it is working (0.12.75: the owner found the notes too many)");
            TextPlans.Get(Instance(spec).Board.Text.Job);
            Program.Check(!TextBoxes.Hints(spec, false).Any(l => l.StartsWith("Frame:")) && !TextBoxes.Hints(spec, true).Any(l => l.StartsWith("支撑架")), "after: the frame's figures are not on the panel, in either language (they are in the log)");

            // A character the font lacks, a text cut at the limits, a font that is gone.
            int lacking = new[] { 0x0E01, 0x0531, 0x05D0, 0x10A0 }.First(c => !TextLayoutTests.BundledFont().TryAdvance(c, out _));
            string lackingText = char.ConvertFromUtf32(lacking);
            string missing = Spec("字" + lackingText + "字").ToString();
            TextPlans.Get(Instance(missing).Board.Text.Job);
            Program.Check(TextBoxes.Hints(missing, false).Any(l => l.Contains("no \"" + lackingText + "\"")) && TextBoxes.Hints(missing, true).Any(l => l.Contains("没有“" + lackingText + "”")), "a character the font lacks is named");
            string cut = Spec(new string('字', 20) + "\\n" + new string('字', 20)).ToString();
            TextPlans.Get(Instance(cut).Board.Text.Job);
            Program.Check(TextBoxes.Hints(cut, true).Any(l => l.Contains("每行 12 字")), "a text over the limits says what was left out");
            string gone = TextBoxes.Template.Parse("text=字;font=gone.ttf").ToString();
            Program.Check(TextBoxes.Hints(gone, false).Count == 0 && !gone.Contains("gone.ttf"), "a font that is not listed is the default (the spec holds the default), and there is nothing to say");

            // Every hint kind has its sentence in both languages.
            foreach (TextWarningKind kind in Enum.GetValues(typeof(TextWarningKind)))
            {
                var warning = new TextWarning(kind, 3, 'x', 0.2f);
                Program.Check(TextHints.Of(warning, true).Length > 0 && TextHints.Of(warning, false).Length > 0, "a sentence for " + kind + " in both languages");
            }

            Program.Check(TextHints.Of(new TextWarning(TextWarningKind.LongRod, 2, 0, 0.23f), false).Contains("0.23") && TextHints.Of(new TextWarning(TextWarningKind.LongRod, 2, 0, 0.23f), true).Contains("0.23"), "the long rod's length is in its sentence, in both languages");
            Program.Check(TextHints.Of(new TextWarning(TextWarningKind.MissingCharacter, 0, 0xD800), false).Contains("?") && TextHints.Of(new TextWarning(TextWarningKind.MissingCharacter, 0, 0x20), false).Contains("?"), "a code point that cannot be shown is a question mark");
        }

        /// <summary>The mean x (as the sign's frame has it) of the vertices of a mesh between two heights.</summary>
        private static float MeanX(MeshBuilder mesh, float y0, float y1)
        {
            double sum = 0.0;
            int n = 0;
            foreach (Vec3 p in mesh.Positions)
            {
                if (p.Y >= y0 && p.Y <= y1)
                {
                    sum += p.X;
                    n++;
                }
            }

            return n == 0 ? float.NaN : (float)(sum / n);
        }

        private static void Orientation()
        {
            // An L has its stem on the left of the viewer and its foot reaching right; the viewer's right is -X of the sign's frame.
            SignInstance instance = Instance(Spec("L", ";h=1").ToString());
            SignParts parts = Parts(instance);
            MeshBuilder glow = parts.Solids[(int)instance.Board.Text.Glow];
            glow.Bounds(out Vec3 lo, out Vec3 hi);
            float height = hi.Y - lo.Y;
            float top = MeanX(glow, hi.Y - height * 0.3f, hi.Y), bottom = MeanX(glow, lo.Y, lo.Y + height * 0.15f);
            Program.Check(top > bottom + 0.02f, "an L: its top (the stem) is on the viewer's left (larger X), its foot reaches to the right: " + top + " against " + bottom);
            Program.Check(glow.Normals.All(n => n.Z > 0.99f), "the lit face looks toward the viewer");
            // The letters of a line go from the viewer's left to right: the first character has the larger X.
            SignInstance two = Instance(Spec("口小").ToString());
            MeshBuilder g2 = Parts(two).Solids[(int)two.Board.Text.Glow];
            TextPlanResult plan = TextPlans.Peek(two.Board.Text.Job);
            Program.Check(plan.Layout.Glyphs[0].X < plan.Layout.Glyphs[1].X, "the layout puts the first character on the left");
            float cx = 0f;
            g2.Bounds(out Vec3 a, out Vec3 b);
            cx = (a.X + b.X) * 0.5f;
            Program.Check(Math.Abs(cx) < 0.1f, "the line is centred on the board's middle: " + cx);
        }

        private static void EverySupport()
        {
            SignInstance instance = Instance(Spec("文字灯箱").ToString());
            int built = 0;
            foreach (SupportDef support in SupportCatalog.All)
            {
                SignParts parts;
                try
                {
                    parts = Parts(instance, SupportDetail.Full, support.Id);
                }
                catch (Exception ex)
                {
                    Program.Check(false, support.Id + ": built without failing: " + ex.GetType().Name + ": " + ex.Message);
                    continue;
                }

                built++;
                MeshBuilder glow = parts.Solids[(int)instance.Board.Text.Glow];
                Program.Check(glow != null && glow.VertexCount > 100, support.Id + ": the letters are there");
                bool finite = true;
                foreach (Vec3 p in glow.Positions)
                    finite &= !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z);
                Program.Check(finite && parts.Picks.Exists(p => p.Name == "board"), support.Id + ": finite, and pickable");
            }

            Program.Check(built == SupportCatalog.All.Count && built > 8, "all " + built + " supports");
        }

        private static void TooDetailed()
        {
            TextPlans.Clear();
            int was = TextPlans.Limit;
            try
            {
                // A cap so low that even the coarsest cut is over it.
                TextPlans.Limit = 300;
                string spec = Spec("文字灯箱").ToString();
                SignInstance instance = Instance(spec);
                TextPlanResult heavy = TextPlans.Get(instance.Board.Text.Job);
                Program.Check(heavy.TooHeavy && heavy.Coarsened == TextPlans.MaxCoarsening && heavy.Warnings.Exists(w => w.Kind == TextWarningKind.TooDetailed && w.Metres > 0f), "over the cap even at the coarsest: too heavy, said so");
                SignParts bars = Parts(instance);
                MeshBuilder glow = bars.Solids[(int)instance.Board.Text.Glow];
                Program.Check(glow.VertexCount == 24 && !bars.Provisional && (bars.Solids[(int)instance.Board.Text.Return] == null), "a text that is too heavy is a lit bar, finished and not provisional");
                Program.Check(TextBoxes.Hints(spec, false).Any(l => l.Contains("lit bars")) && TextBoxes.Hints(spec, true).Any(l => l.Contains("发光条")), "the editor says why, in both languages");

                // A cap that the finest cut is over and a coarser one is not.
                TextPlans.Clear();
                TextPlanResult fine = TextPlans.Get(JobOf("文字灯箱"));
                TextPlans.Clear();
                TextPlans.Limit = fine.Vertices - 1;
                TextPlanResult coarse = TextPlans.Get(JobOf("文字灯箱"));
                Program.Check(coarse.Vertices <= fine.Vertices && (coarse.Coarsened > 0 || coarse.TooHeavy), "a cap just under the fine cut cuts the letters coarser or gives them up: " + coarse.Coarsened + " step(s), " + fine.Vertices + " -> " + coarse.Vertices);
            }
            finally
            {
                TextPlans.Limit = was;
                TextPlans.Clear();
            }

            // The largest text that is allowed: 36 characters, as complex as the font has them; no mesh of it reaches the limit of one mesh.
            int rich = new[] { 0x9E98, 0x9B31, 0x7228, 0x99A8, 0x7C72, 0x946B }.FirstOrDefault(c => TextLayoutTests.BundledFont().TryAdvance(c, out _));
            string glyph = char.ConvertFromUtf32(rich == 0 ? '字' : rich);
            string row = string.Concat(Enumerable.Repeat(glyph, 12));
            foreach (float height in new[] { 0.3f, 1f, 3f })
            {
                SignInstance big = Instance(Spec(row + "\\n" + row + "\\n" + row, ";h=" + height.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString());
                TextPlanResult plan = TextPlans.Get(big.Board.Text.Job);
                SignParts parts = Parts(big);
                int worst = 0;
                foreach (MeshBuilder solid in parts.Solids)
                    worst = Math.Max(worst, solid == null ? 0 : solid.VertexCount);
                Program.Check(worst <= ChunkBuilder.MaxVertices && plan.Layout.Glyphs.Count == 36, "36 characters of " + height + " m: the biggest mesh is " + worst + " vertices (" + plan.Coarsened + " coarsening step(s)" + (plan.TooHeavy ? ", too heavy" : "") + "), " + plan.Frame.Islands.Count + " islands");
                Console.WriteLine($"  text box, 36 x '{glyph}' at {height} m: return {plan.Vertices} vertices, coarsened {plan.Coarsened}, too heavy {plan.TooHeavy}, frame {plan.Frame.Studs.Count} studs {plan.Frame.Rails.Count} rails, glyph cache {TextPlans.Glyphs.Bytes / 1024} KB");
            }
        }

        private static void HeavyParts()
        {
            ChunkBuilder.ClearCaches();
            string row = string.Concat(Enumerable.Repeat("文字灯箱", 3));
            SignInstance[] heavy = Enumerable.Range(0, 15).Select(i => Instance(Spec(row + "\\n" + row + "\\n" + row, ";h=" + (1 + i * 0.1f).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString())).ToArray();
            uint post = SupportPlanner.Auto(heavy[0].Def).Hash;
            SignParts first = ChunkBuilder.PartsOf(heavy[0], post, 0f);
            Program.Check(first.VertexCount > 20000, "a big text is heavy: " + first.VertexCount);
            Program.Check(ReferenceEquals(first, ChunkBuilder.PartsOf(heavy[0], post, 0f)), "and is kept all the same");
            for (int i = 1; i < heavy.Length; i++)
                ChunkBuilder.PartsOf(heavy[i], post, 0f);
            Program.Check(!ReferenceEquals(first, ChunkBuilder.PartsOf(heavy[0], post, 0f)), "but only a few: the first of fifteen is let go");
            SignInstance plain = Instance(Spec("字").ToString());
            SignParts small = ChunkBuilder.PartsOf(plain, SupportPlanner.Auto(plain.Def).Hash, 0f);
            for (int i = 0; i < heavy.Length; i++)
                ChunkBuilder.PartsOf(heavy[i], post, 0f);
            Program.Check(ReferenceEquals(small, ChunkBuilder.PartsOf(plain, SupportPlanner.Auto(plain.Def).Hash, 0f)), "a small text is not pushed out by them");
            ChunkBuilder.ClearCaches();
        }

        private static void Provisional()
        {
            TextPlans.Clear();
            SignInstance instance = Instance(Spec("临时的计划").ToString());
            SupportDef support = SupportPlanner.Auto(instance.Def);
            SignParts first = null, second = null, last = null, kept = null;
            bool arrived = false;
            var thread = new Thread(() =>
            {
                TextPlans.MarkMainThread();
                ChunkBuilder.ClearCaches();
                first = ChunkBuilder.PartsOf(instance, support.Hash, 0f);
                second = ChunkBuilder.PartsOf(instance, support.Hash, 0f);
                for (int i = 0; i < 400 && !arrived; i++)
                {
                    Thread.Sleep(10);
                    arrived = TextPlans.Peek(instance.Board.Text.Job) != null;
                }

                last = ChunkBuilder.PartsOf(instance, support.Hash, 0f);
                kept = ChunkBuilder.PartsOf(instance, support.Hash, 0f);
                TextPlans.ForgetMainThread();
            });
            thread.Start();
            thread.Join();
            TextBoard text = instance.Board.Text;
            Program.Check(first != null && first.Provisional && !ReferenceEquals(first, second), "on the main thread without a plan the parts are provisional and not kept");
            Program.Check(first.Solids[(int)text.Glow].VertexCount == 24 && first.Solids[(int)text.Return] == null, "they are one lit bar for the line");
            Program.Check(arrived && last != null && !last.Provisional && ReferenceEquals(last, kept), "when the plan is there the parts are the whole ones, and kept");
            Program.Check(last.Solids[(int)text.Glow].VertexCount > 100, "with the letters");
        }

        private static ItemRecord RecordOf(string spec, uint support) => new ItemRecord(Def.Hash, new Vec3(10f, 0f, 20f), 0.3f, support, spec, 0f, 0u, 0, 0, 0f);

        private static void InTile()
        {
            string spec = Spec("路边招牌").ToString();
            uint post = SupportPlanner.Auto(Instance(spec).Def).Hash;
            List<ChunkPart> near = ChunkBuilder.BuildDetailed(new[] { RecordOf(spec, post) }, Vec3.Zero, ChunkDetail.Of(TileTier.Near), out int skipped);
            Program.Check(skipped == 0, "nothing is skipped");
            ChunkPart glow = near.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm));
            ChunkPart ret = near.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.Black));
            Program.Check(glow != null && glow.Mesh.VertexCount > 100 && ret != null && ret.Mesh.VertexCount > glow.Mesh.VertexCount, "the tile has the glow material's letters and the return's");
            Program.Check(glow.Mesh.Positions.All(p => Math.Abs(p.X - 10f) < 4f && Math.Abs(p.Z - 20f) < 4f), "at the item's place");

            List<ChunkPart> far = ChunkBuilder.BuildDetailed(new[] { RecordOf(spec, post) }, Vec3.Zero, ChunkDetail.Of(TileTier.Far), out _);
            ChunkPart bars = far.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm));
            Program.Check(bars != null && bars.Mesh.VertexCount > 24 && bars.Mesh.VertexCount <= glow.Mesh.VertexCount && far.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.Black)) == null, "a far tile has the letters' faces and not the return: " + bars?.Mesh.VertexCount);
            Program.Check(near.Sum(p => p.Mesh.VertexCount) > far.Sum(p => p.Mesh.VertexCount), "and is lighter than a near one");

            // A text that no font can set (none is read): nothing is drawn, nothing fails.
            List<ChunkPart> blank = ChunkBuilder.Build(new[] { RecordOf(Spec("").ToString(), post) }, Vec3.Zero, out int skippedBlank);
            Program.Check(skippedBlank == 0 && blank.All(p => p.Material != ChunkBuilder.SolidMaterial(SolidColour.GlowWarm)), "no text, no letters");

            // The thumbnail of the entry in the catalogue: the letters are in it.
            byte[] picture = new byte[16 * 16 * 4];
            SignInstance sign = Instance(SignCatalog.ChoiceById(TextBoxes.TemplateId).Spec);
            byte[] rgba = Thumbnails.SignRgba(sign, picture, 16, 16);
            int lit = 0;
            for (int i = 3; i < rgba.Length; i += 4)
                lit += rgba[i] > 0 ? 1 : 0;
            Program.Check(lit > 200 && lit < rgba.Length / 4, "the thumbnail shows the letters and not a plate: " + lit + " of " + rgba.Length / 4 + " pixels");
        }

        private static void Threads()
        {
            TextPlans.Clear();
            TextPlanJob job = JobOf("八线程共享一个计划");
            var results = new TextPlanResult[8];
            using (var start = new Barrier(8))
            {
                Task[] tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
                {
                    start.SignalAndWait();
                    results[i] = TextPlans.Get(job);
                })).ToArray();
                Task.WaitAll(tasks);
            }

            Program.Check(results.All(r => r != null && ReferenceEquals(r, results[0])), "eight threads asking at once all got the one plan");
            Program.Check(results[0].Frame != null && results[0].Meshes.Length == 9, "and it is a whole one");
        }
    }
}
