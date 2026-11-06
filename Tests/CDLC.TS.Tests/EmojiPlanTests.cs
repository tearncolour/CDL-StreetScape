using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Emoji through the whole text pipeline (0.12.78): the layout counts a sequence as a cell, the planner draws it from the machine's emoji fonts (colour layers from Segoe UI Emoji, plain outlines from
    /// Segoe UI Symbol), the frame holds its silhouette, the cache keeps it, and the preview paints its layers. The fonts are the machine's: the tests say plainly when they are not there.
    /// </summary>
    internal static class EmojiPlanTests
    {
        private static List<FontOutlines> s_Fonts;

        public static void RunAll()
        {
            FontOutlines bundled = TextLayoutTests.BundledFont();
            if (bundled == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, bundled);
            string emojiPath = EmojiFontTests.SystemFont("seguiemj.ttf"), symbolPath = EmojiFontTests.SystemFont("seguisym.ttf");
            if (emojiPath == null && symbolPath == null)
            {
                Console.WriteLine("  (no emoji or symbol font on this machine: the emoji plan tests are skipped)");
                return;
            }

            var fonts = new List<FontOutlines>();
            if (emojiPath != null)
                fonts.Add(new FontOutlines(File.ReadAllBytes(emojiPath), 0));
            if (symbolPath != null)
                fonts.Add(new FontOutlines(File.ReadAllBytes(symbolPath), 0));
            s_Fonts = fonts;
            TextFaces.UseEmojiFonts(fonts);
            try
            {
                Program.Run("emoji plan: a colour emoji has layers, a silhouette and a frame", ColourPlan);
                Program.Run("emoji plan: sequences, a skin tone, a family, a symbol, a missing cluster", Sequences);
                Program.Run("emoji plan: no emoji font: as before", NoFont);
                Program.Run("emoji plan: the plain texts are unchanged", PlainUnchanged);
                Program.Run("emoji plan: a board built with an emoji", BoardBuilt);
                Program.Run("emoji plan: thirty-six emoji stay under one mesh", ThirtySix);
                Program.Run("emoji plan: every emoji of the common blocks", Census);
                Program.Run("emoji plan: pictures to look at", Pictures);
            }
            finally
            {
                TextFaces.UseEmojiFonts(null);
            }
        }

        private static string U(params int[] scalars) => string.Concat(scalars.Select(char.ConvertFromUtf32));

        private static TextPlanResult Plan(string text, float height = 0.5f, string art = null)
        {
            TextClusters.ResetRegistry();
            var job = new TextPlanJob(text, TextFonts.DefaultId, height, 0f, 1.15f, TextAlign.Centre, 0.002f);
            var planner = new TextPlanner(TextFonts.Get, new GlyphCache(GlyphCache.DefaultBudgetBytes));
            return planner.Plan(job);
        }

        private static void ColourPlan()
        {
            TextPlanResult plan = Plan(U(0x1F600));
            Program.Check(plan.Meshes.Length == 1 && plan.Meshes[0] != null, "a grinning face has a mesh");
            GlyphMesh mesh = plan.Meshes[0];
            Program.Check(mesh.Emoji != null && mesh.Emoji.Layers.Count >= 3, "from Segoe UI Emoji: layers " + (mesh.Emoji == null ? 0 : mesh.Emoji.Layers.Count));
            Program.Check(mesh.IsSound(out string why), "the silhouette is a sound mesh: " + why);
            Program.Check(mesh.Emoji.Layers.All(l => l.Mesh != null && l.Mesh.Triangles.Length >= 3 && l.Mesh.IsSound(out _)), "every layer is a sound mesh");
            Program.Check(mesh.Emoji.Layers.Count <= EmojiPlan.MaxLayers, "at most " + EmojiPlan.MaxLayers + " layers");
            Program.Check(mesh.Emoji.Layers.Sum(l => l.Mesh.LoopPoints) <= EmojiPlan.MaxPoints * 2, "bounded points: " + mesh.Emoji.Layers.Sum(l => l.Mesh.LoopPoints));
            Program.Check(mesh.Area > 0.2f && mesh.Area < 1.2f, "the silhouette's area is about a face's: " + mesh.Area);
            Program.Check(plan.Frame != null && plan.Frame.Islands.Count >= 1 && plan.Frame.Studs.Count >= 1, "the frame holds the silhouette: " + (plan.Frame == null ? "no frame" : plan.Frame.Islands.Count + " island(s), " + plan.Frame.Studs.Count + " stud(s)"));
            Program.Check(plan.Layout.Glyphs[0].Advance > 0.4f && !plan.Layout.Glyphs[0].Missing && plan.Layout.Missing.Count == 0, "laid out, not missing");
            // The palette: the colours are glow colours or black.
            Program.Check(mesh.Emoji.Layers.All(l => EmojiColours.Palette.Contains(l.Colour)), "the layers are in the palette's colours");
            Program.Check(mesh.Emoji.Layers.Select(l => l.Colour).Distinct().Count() >= 2, "a face is lit in more than one colour");
            // The same glyph a second time comes from the cache: the same object.
            var cache = new GlyphCache(GlyphCache.DefaultBudgetBytes);
            var planner = new TextPlanner(TextFonts.Get, cache);
            var job = new TextPlanJob(U(0x1F600, 0x1F600), TextFonts.DefaultId, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.002f);
            TextPlanResult two = planner.Plan(job);
            Program.Check(two.Meshes.Length == 2 && ReferenceEquals(two.Meshes[0], two.Meshes[1]), "two of the same emoji share one mesh");
            // The cut depends on the flatness asked for: a coarser job does not get the finer mesh out of the cache.
            TextPlanResult fine = planner.Plan(new TextPlanJob(U(0x1F600), TextFonts.DefaultId, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.0005f));
            TextPlanResult coarse = planner.Plan(new TextPlanJob(U(0x1F600), TextFonts.DefaultId, 0.5f, 0f, 1.15f, TextAlign.Centre, 0.016f));
            Program.Check(coarse.Meshes[0].LoopPoints < fine.Meshes[0].LoopPoints, "a coarser cut has fewer points: " + coarse.Meshes[0].LoopPoints + " against " + fine.Meshes[0].LoopPoints);
        }

        private static void Sequences()
        {
            TextPlanResult thumb = Plan(U(0x1F44D, 0x1F3FD));
            Program.Check(thumb.Layout.Glyphs.Count == 1 && thumb.Meshes[0] != null, "a thumbs up with a skin tone is one cell with a mesh");
            TextPlanResult family = Plan(U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467));
            Program.Check(family.Layout.Glyphs.Count == 1 && family.Meshes[0] != null && family.Meshes[0].Emoji != null, "a family is one cell");
            TextPlanResult technologist = Plan(U(0x1F469, 0x1F3FD, 0x200D, 0x1F4BB));
            Program.Check(technologist.Layout.Glyphs.Count == 1 && technologist.Meshes[0] != null, "a woman technologist is one cell");
            TextPlanResult heart = Plan(U(0x2764, 0xFE0F));
            Program.Check(heart.Layout.Glyphs.Count == 1 && heart.Meshes[0] != null && heart.Meshes[0].Emoji != null, "a heart with the selector has layers");
            TextPlanResult star = Plan(U(0x2B50));
            Program.Check(star.Meshes[0] != null, "a symbol that has an emoji form");
            TextPlanResult flag = Plan(U(0x1F1E8, 0x1F1F3));
            Program.Check(flag.Layout.Glyphs.Count == 1 && flag.Meshes[0] != null, "a flag is one cell (drawn as the font draws it: its two letters)");
            TextPlanResult keycap = Plan(U('1', 0xFE0F, 0x20E3));
            Program.Check(keycap.Layout.Glyphs.Count == 1, "a keycap is one cell");

            // A text mixing Chinese, Latin and emoji: every cell has a mesh, and the Chinese ones are as they were.
            TextPlanResult mixed = Plan("A文" + U(0x1F600) + "b");
            Program.Check(mixed.Layout.Glyphs.Count == 4 && mixed.Meshes.All(m => m != null) && mixed.Layout.Missing.Count == 0, "a mix of four cells");
            Program.Check(mixed.Meshes[2].Emoji != null && mixed.Meshes[0].Emoji == null && mixed.Meshes[1].Emoji == null && mixed.Meshes[3].Emoji == null, "only the emoji has layers");

            // A cluster that no font draws is a missing character, listed by its key, with a blank cell.
            TextPlanResult none = Plan(U(0x10FFFD));
            Program.Check(none.Layout.Missing.Count == 1 && none.Meshes[0] == null && none.Warnings.Any(w => w.Kind == TextWarningKind.MissingCharacter), "no font has it: a missing character with its hint");
            TextPlanResult noneSeq = Plan(U(0x10FFFD, 0x200D, 0x10FFFE));
            Program.Check(noneSeq.Meshes[0] == null && noneSeq.Layout.Missing.Count >= 1, "a sequence no font has is missing too");
            // The hint names it.
            string hint = TextHints.Of(none.Warnings.First(w => w.Kind == TextWarningKind.MissingCharacter), false);
            Program.Check(hint.Contains("?") || hint.Contains(U(0x10FFFD)), "its hint: " + hint);
        }

        private static void NoFont()
        {
            TextFaces.UseEmojiFonts(null);
            try
            {
                TextPlanResult plan = Plan(U(0x1F600) + "a");
                Program.Check(plan.Layout.Glyphs.Count == 2 && plan.Layout.Glyphs[0].Missing && plan.Meshes[0] == null && plan.Meshes[1] != null, "without an emoji font the emoji is a missing character, as before");
                Program.Check(plan.Layout.Missing.Contains(0x1F600), "and it is listed");
            }
            finally
            {
                TextFaces.UseEmojiFonts(s_Fonts);
            }
        }

        private static void PlainUnchanged()
        {
            // Texts with no emoji: the plan is what it was with no emoji font at all (the chain adds nothing).
            string[] texts = { "文字灯箱", "Hello 123", "公交 BUS 12\n路", "" };
            foreach (string text in texts)
            {
                TextPlanResult with = Plan(text);
                TextFaces.UseEmojiFonts(null);
                TextPlanResult without = Plan(text);
                TextFaces.UseEmojiFonts(s_Fonts);
                Program.Check(with.Layout.Glyphs.Count == without.Layout.Glyphs.Count && with.Layout.Width == without.Layout.Width && with.Layout.Missing.Count == without.Layout.Missing.Count, "the layout of '" + text.Replace("\n", "/") + "' is the same");
                Program.Check(with.Meshes.Length == without.Meshes.Length && Enumerable.Range(0, with.Meshes.Length).All(i => (with.Meshes[i] == null) == (without.Meshes[i] == null) && (with.Meshes[i] == null || (with.Meshes[i].Vertices == without.Meshes[i].Vertices && with.Meshes[i].Area == without.Meshes[i].Area))), "and the meshes");
                Program.Check((with.Frame == null) == (without.Frame == null) && (with.Frame == null || (with.Frame.Studs.Count == without.Frame.Studs.Count && with.Frame.Rails.Count == without.Frame.Rails.Count)), "and the frame");
            }
        }

        // ------------------------------------------------------------------ the board

        private static SignInstance Instance(string text, string extra = "") => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text).ToString());

        private static SignParts Build(SignInstance instance) => SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, SupportCatalog.ById(SupportCatalog.WallFlat), 0f, SupportDetail.Full, default);

        private static float[] ZOf(SignParts parts) => parts.Solids.Where(m => m != null).SelectMany(m => m.Positions).Select(p => p.Z).ToArray();

        private static void BoardBuilt()
        {
            TextPlans.Clear();
            SignInstance grin = Instance(U(0x1F600), ";h=0.4");
            SignParts parts = Build(grin);
            TextBoard text = grin.Board.Text;
            EmojiPlan emoji = TextPlans.Peek(text.Job)?.Meshes[0]?.Emoji;
            Program.Check(emoji != null && emoji.Layers.Count >= 3, "the board's plan has the layers");
            int count = emoji.Layers.Count;
            float zFront = parts.Layout.BackZ + text.Reach;
            float step = Math.Min(EmojiPlan.LayerStep, text.Depth / 3f / count);
            Program.Check(step >= 0.0005f, "the layers are apart: " + step);

            // Each layer is emitted in the solid of its colour, in its own plane: the last in the face's plane, the ones before it one step further back each.
            for (int i = 0; i < count; i++)
            {
                EmojiLayer layer = emoji.Layers[i];
                MeshBuilder solid = parts.Solids[(int)layer.Colour];
                float z = zFront - (count - 1 - i) * step;
                Program.Check(solid != null && solid.Positions.Any(p => Math.Abs(p.Z - z) < 1e-5f), "layer " + i + " (" + layer.Colour + ") has its face at z " + z);
            }

            // The coloured solids hold nothing but the layers' faces (the return's colour has the back and the rim as well): every face vertex of the layers is there.
            foreach (SolidColour colour in emoji.Layers.Select(l => l.Colour).Distinct())
            {
                if (colour == text.Return)
                    continue;
                int expected = emoji.Layers.Where(l => l.Colour == colour).Sum(l => l.Mesh.FaceVertices);
                Program.Check(parts.Solids[(int)colour].VertexCount == expected, colour + ": the faces' vertices " + parts.Solids[(int)colour].VertexCount + " against " + expected);
                float zLow = zFront - (count - 1) * step - 1e-5f;
                Program.Check(parts.Solids[(int)colour].Positions.All(p => p.Z >= zLow && p.Z <= zFront + 1e-5f), colour + ": the faces are inside the layers' depth");
            }

            float[] zs = ZOf(parts);
            Program.Check(zs.Max() <= zFront + 1e-5f && zs.Min() >= parts.Layout.BackZ + text.Stand - 1e-5f, "all of it is inside the letter's depth");
            Program.Check(parts.Solids.Where(m => m != null).All(m => m.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z))), "every vertex is finite");
            Program.Check(parts.Solids[(int)text.Return].VertexCount >= 2 * emoji.Layers[0].Mesh.FaceVertices, "the return (back and rim) is there");

            // A plain letter next to the emoji has its face in the plane of the emoji's front layer.
            SignInstance mixed = Instance("A" + U(0x1F600), ";h=0.4");
            SignParts both = Build(mixed);
            float mixedFront = both.Layout.BackZ + mixed.Board.Text.Reach;
            Program.Check(both.Solids[(int)mixed.Board.Text.Glow].Positions.Max(p => p.Z) >= mixedFront - 1e-5f && both.Solids.Where(m => m != null).SelectMany(m => m.Positions).Max(p => p.Z) <= mixedFront + 1e-5f, "the letter and the emoji meet in the face's plane");

            // A text without an emoji builds as it did with no emoji font at all, vertex for vertex.
            SignParts withFonts = Build(Instance("文字灯箱", ";h=0.4"));
            TextFaces.UseEmojiFonts(null);
            TextPlans.Clear();
            SignParts withoutFonts;
            try
            {
                withoutFonts = Build(Instance("文字灯箱", ";h=0.4"));
            }
            finally
            {
                TextFaces.UseEmojiFonts(s_Fonts);
                TextPlans.Clear();
            }

            bool same = true;
            for (int i = 0; i < withFonts.Solids.Length; i++)
            {
                MeshBuilder a = withFonts.Solids[i], b = withoutFonts.Solids[i];
                same &= (a == null || a.VertexCount == 0) == (b == null || b.VertexCount == 0) && (a == null || b == null || a.Positions.SequenceEqual(b.Positions));
            }

            Program.Check(same, "a plain text is built the same with and without the emoji fonts");

            // On a curved wall the emoji is its silhouette in the glow colour: no layers, no other colours.
            SignInstance arc = Instance(U(0x1F600), ";h=0.4;arc=12");
            SignParts bent = Build(arc);
            SolidColour glow = arc.Board.Text.Glow, back = arc.Board.Text.Return;
            Program.Check(bent.Solids[(int)glow] != null && bent.Solids[(int)glow].VertexCount > 0, "on a curved wall it is drawn in the glow colour");
            foreach (SolidColour colour in EmojiColours.Palette)
            {
                if (colour != glow && colour != back)
                    Program.Check(bent.Solids[(int)colour] == null || bent.Solids[(int)colour].VertexCount == 0, "on a curved wall nothing is lit in " + colour);
            }
        }

        private static void ThirtySix()
        {
            // Three lines of twelve different emoji (the text's limits): the board's solids hold what the plan counted, or the plan says it is too heavy and the letters are bars.
            int[] twelve = { 0x1F600, 0x2764, 0x1F525, 0x1F44D, 0x1F680, 0x2B50, 0x2705, 0x1F308, 0x1F389, 0x1F68C, 0x1F34E, 0x1F338 };
            string line = string.Concat(twelve.Select(char.ConvertFromUtf32));
            string br = U(92) + "n", text = line + br + line + br + line;
            TextPlans.Clear();
            SignInstance instance = Instance(text, ";h=0.4");
            Program.Check(instance.Board.Text.Layout.Glyphs.Count == 36, "thirty-six cells: " + instance.Board.Text.Layout.Glyphs.Count);
            SignParts parts = Build(instance);
            TextPlanResult plan = TextPlans.Peek(instance.Board.Text.Job);
            int counted = TextPlans.ReturnVertices(plan);
            int largest = parts.Solids.Where(m => m != null).Max(m => m.VertexCount);
            Console.WriteLine("  thirty-six emoji: counted " + counted + " vertices (limit " + TextPlans.Limit + "), coarsened " + plan.Coarsened + " time(s), too heavy " + plan.TooHeavy + "; the fullest solid holds " + largest + "; glyph cache " + TextPlans.Glyphs.Bytes / 1024 + " KB");
            Program.Check(plan.TooHeavy || (counted <= TextPlans.Limit && largest <= counted), "no solid is over what the plan counted: " + largest + " against " + counted);
            Program.Check(largest < 60000, "every solid fits one mesh: " + largest);
            Program.Check(TextPlans.Glyphs.Bytes < GlyphCache.DefaultBudgetBytes, "the glyph cache is inside its budget");

            // The same under a lower limit: the letters are cut coarser and the count still bounds the solids.
            int keep = TextPlans.Limit;
            try
            {
                TextPlans.Limit = counted / 2;
                TextPlans.Clear();
                SignInstance tight = Instance(text, ";h=0.4");
                SignParts squeezed = Build(tight);
                TextPlanResult plan2 = TextPlans.Peek(tight.Board.Text.Job);
                Program.Check(plan2.Coarsened > 0 || plan2.TooHeavy, "a lower limit cuts the letters coarser or says it is too heavy");
                if (!plan2.TooHeavy)
                    Program.Check(squeezed.Solids.Where(m => m != null).Max(m => m.VertexCount) <= TextPlans.ReturnVertices(plan2), "and the solids are inside what is counted");
            }
            finally
            {
                TextPlans.Limit = keep;
                TextPlans.Clear();
            }
        }

        private static void Census()
        {
            // Every code point of the common emoji blocks, one at a time: the meshes of all that are drawn are sound (their triangles add up to their area), within the caps, and the heaviest forty together are
            // still one mesh's worth (a text of forty is the longest the field takes).
            var planner = new TextPlanner(TextFonts.Get, new GlyphCache(GlyphCache.DefaultBudgetBytes));
            var blocks = new[] { (From: 0x1F300, To: 0x1F64F), (From: 0x1F680, To: 0x1F6FF), (From: 0x1F900, To: 0x1F9FF), (From: 0x2600, To: 0x27BF) };
            int total = 0, drawn = 0, layered = 0, unsound = 0, overCaps = 0;
            string firstUnsound = null;
            var counted = new List<int>();
            foreach (var block in blocks)
            {
                for (int cp = block.From; cp <= block.To; cp++)
                {
                    total++;
                    TextPlanResult plan = planner.Plan(new TextPlanJob(U(cp), TextFonts.DefaultId, 0.4f, 0f, 1.15f, TextAlign.Centre, 0.002f));
                    GlyphMesh mesh = plan.Meshes.Length > 0 ? plan.Meshes[0] : null;
                    if (mesh == null)
                        continue;
                    drawn++;
                    string why;
                    bool sound = mesh.IsSound(out why) && (mesh.Emoji == null || mesh.Emoji.Layers.All(l => l.Mesh.IsSound(out why)));
                    if (!sound)
                    {
                        unsound++;
                        firstUnsound = firstUnsound ?? "U+" + cp.ToString("X") + ": " + why;
                    }

                    if (mesh.Emoji == null)
                        continue;
                    layered++;
                    counted.Add(mesh.ReturnVertices);
                    if (mesh.Emoji.Layers.Count > EmojiPlan.MaxLayers || mesh.Emoji.Layers.Sum(l => l.Mesh.LoopPoints) > EmojiPlan.MaxPoints * 2)
                        overCaps++;
                }
            }

            // Two the triangulator knots on at the first trace (a crane's stripes, a tram's windows) keep their holes through the second.
            int craneHoles = planner.Plan(new TextPlanJob(U(0x1F3D7), TextFonts.DefaultId, 0.4f, 0f, 1.15f, TextAlign.Centre, 0.002f)).Meshes[0].Loops.Count(l => CutOutline.Area(l) < 0f);
            int tramHoles = planner.Plan(new TextPlanJob(U(0x1F68B), TextFonts.DefaultId, 0.4f, 0f, 1.15f, TextAlign.Centre, 0.002f)).Meshes[0].Loops.Count(l => CutOutline.Area(l) < 0f);
            Program.Check(craneHoles >= 5 && tramHoles >= 2, "the crane keeps its holes (" + craneHoles + ") and the tram its windows (" + tramHoles + ")");

            // The finest cut a letter is asked for (a letter of 3 m: half a millimetre) keeps every emoji inside the points' budget; at the finest cut the glyph cache has (1e-5 of the em, which no letter
            // asks for) the emoji with the most curves are coarsened by the budget's guard to about half of the 6945 points they have without it.
            int most = 0, over = 0, mostAtLimit = 0;
            foreach (float flat in new[] { 0.0005f / 3f, TextGlyphs.MinRelative })
            {
                var fine = new TextPlanner(TextFonts.Get, new GlyphCache(GlyphCache.DefaultBudgetBytes));
                for (int cp = 0x1F300; cp <= 0x1F64F; cp++)
                {
                    GlyphMesh m = fine.Plan(new TextPlanJob(U(cp), TextFonts.DefaultId, 0.4f, 0f, 1.15f, TextAlign.Centre, flat)).Meshes[0];
                    if (m == null || m.Emoji == null)
                        continue;
                    int points = m.Emoji.Layers.Sum(l => l.Mesh.LoopPoints);
                    if (flat > TextGlyphs.MinRelative)
                    {
                        most = Math.Max(most, points);
                        over += points > EmojiPlan.MaxPoints ? 1 : 0;
                    }
                    else
                    {
                        mostAtLimit = Math.Max(mostAtLimit, points);
                    }
                }
            }

            Program.Check(over == 0 && most > EmojiPlan.MaxPoints / 2, "no emoji has more points than the budget at the finest cut a letter asks for: the most " + most + " of " + EmojiPlan.MaxPoints);
            Program.Check(mostAtLimit < 4500, "and the guard bounds the points where nothing asks for it: " + mostAtLimit);

            counted.Sort();
            long forty = counted.Skip(Math.Max(0, counted.Count - 40)).Sum(v => (long)v);
            Console.WriteLine("  census of " + total + " code points: " + drawn + " drawn, " + layered + " with colour layers; vertices counted per emoji: median " + (counted.Count == 0 ? 0 : counted[counted.Count / 2]) + ", worst " + (counted.Count == 0 ? 0 : counted[counted.Count - 1]) + "; the forty heaviest together " + forty + " (limit " + TextPlans.Limit + ")");
            Program.Check(layered > 100, "the colour font draws hundreds of the emoji: " + layered);
            Program.Check(unsound == 0, unsound + " of them have a mesh whose triangles do not add up, the first " + firstUnsound);
            Program.Check(overCaps == 0, "the layers and points stay under their caps");
            Program.Check(forty < TextPlans.Limit, "forty of the heaviest are under one mesh's limit: " + forty);
        }

        // ------------------------------------------------------------------ pictures

        private static void Pictures()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "emoji-pictures");
            Directory.CreateDirectory(dir);
            var cases = new (string Name, string Text)[]
            {
                ("grin-heart-fire", U(0x1F600, 0x2764, 0xFE0F, 0x1F525)), ("thumb-skin", U(0x1F44D, 0x1F44D, 0x1F3FB, 0x1F44D, 0x1F3FD, 0x1F44D, 0x1F3FF)),
                ("family-tech", U(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467, 0x1F469, 0x1F3FD, 0x200D, 0x1F4BB)), ("mixed", "OK" + U(0x2705) + "文" + U(0x2B50) + "12" + U(0x1F680)),
                ("flags", U(0x1F1E8, 0x1F1F3, 0x1F1FA, 0x1F1F8, 0x1F3F3, 0xFE0F, 0x200D, 0x1F308))
            };
            foreach (var c in cases)
            {
                TextPlanResult plan = Plan(c.Text, 0.4f);
                var board = new TextBoard { Job = plan.Job, Layout = plan.Layout, Glow = SolidColour.GlowWarm };
                byte[] picture = TextFacePreview.Raster(board, plan, new byte[] { 250, 226, 180 }, out int w, out int h);
                Program.Check(picture != null && w > 16 && h > 16, c.Name + ": a picture of " + w + " by " + h);
                if (picture != null)
                    File.WriteAllBytes(Path.Combine(dir, c.Name + ".png"), Png.Encode(picture, w, h));
                int emoji = plan.Meshes.Count(m => m != null && m.Emoji != null);
                Console.WriteLine("  " + c.Name + ": " + plan.Layout.Glyphs.Count + " cell(s), " + emoji + " coloured, vertices " + plan.Vertices + ", frame " + (plan.Frame == null ? "none" : plan.Frame.Studs.Count + " studs") + ", missing " + plan.Layout.Missing.Count);
            }
        }
    }
}
