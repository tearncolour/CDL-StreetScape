using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// What a far tile draws of a text light box (0.12.105; the owner, 6 Nov, after the draw distance became a setting: "字体灯箱好像没有遵循这个距离显示，离远了还是变成一个条了"): until then every line
    /// was one lit bar past the middle tier's 250 m, whatever the distance was set to. Now it is the letters' lit faces, the same triangles the near tile has in its glow solid and nothing else (no return, no
    /// studs, no frame). Bars stay for what is not that: a text on an arc, a plan that is not there (the main thread asked), one that is too heavy, and faces over SignBuilder.MaxFarTextVertices.
    /// </summary>
    internal static class TextFarTests
    {
        public static void RunAll()
        {
            if (TextLayoutTests.BundledFont() == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, TextLayoutTests.BundledFont());
            Program.Run("text far: the letters' faces are the near tile's, with no return and no frame", UprightFaces);
            Program.Run("text far: two lines, aligned each way, are the near letters", Lines);
            Program.Run("text far: a leaning text leans, a heavy one, an arc and a missing plan are bars", Fallbacks);
            Program.Run("text far: the other styles and the tile that holds the text", StylesAndTile);
        }

        private static GuideSpec Spec(string text, string extra = "") => TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text);

        private static SignInstance Instance(string text, string extra = "") => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), Spec(text, extra).ToString());

        private static SignParts Build(SignInstance instance, SupportDetail detail, string support = null, float reach = 0f)
        {
            SupportDef def = support != null ? SupportCatalog.ById(support) : SupportPlanner.Auto(instance.Def);
            return SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, def, reach, detail, default);
        }

        /// <summary>The positions of a mesh as one list of (x, y, z) at a ten-thousandth of a metre, in order.</summary>
        private static List<(int, int, int)> Points(MeshBuilder mesh)
        {
            var points = new List<(int, int, int)>();
            if (mesh == null)
                return points;
            foreach (Vec3 p in mesh.Positions)
                points.Add(((int)Math.Round(p.X * 10000f), (int)Math.Round(p.Y * 10000f), (int)Math.Round(p.Z * 10000f)));
            return points;
        }

        private static bool Same(MeshBuilder a, MeshBuilder b) => Points(a).SequenceEqual(Points(b));

        private static void UprightFaces()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱");
            TextBoard text = instance.Board.Text;
            SignParts near = Build(instance, SupportDetail.Full), far = Build(instance, SupportDetail.Reduced);
            MeshBuilder nearGlow = near.Solids[(int)text.Glow], farGlow = far.Solids[(int)text.Glow];
            Program.Check(nearGlow != null && farGlow != null && farGlow.VertexCount > 24 * text.Layout.Lines, $"a far tile has letters, not one bar a line: {farGlow?.VertexCount} vertices ({24 * text.Layout.Lines} for the bars)");
            Program.Check(Same(nearGlow, farGlow), "its lit faces are the near tile's, point for point");
            Program.Check(far.Solids[(int)text.Return] == null || far.Solids[(int)text.Return].VertexCount == 0, "and there is no return");
            Program.Check(far.Metal == null || far.Metal.VertexCount == 0 || far.Metal.VertexCount < near.Metal.VertexCount, "nor the frame of the letters: the metal is the support's");
            Program.Check(!far.Provisional, "a far tile is a finished one: it is kept");
            TextPlanResult plan = TextPlans.Peek(text.Job);
            Program.Check(plan != null && SignBuilder.TextFaceVertices(plan) == farGlow.VertexCount, $"the vertices are the faces of the plan's glyphs: {SignBuilder.TextFaceVertices(plan)}");
            Program.Check(farGlow.VertexCount < SignBuilder.MaxFarTextVertices, "within the far tile's limit");
            // Flat at the face's plane, all facing the viewer.
            float z0 = farGlow.Positions.Min(p => p.Z), z1 = farGlow.Positions.Max(p => p.Z);
            Program.Check(Math.Abs(z1 - z0) < 1e-5f && farGlow.Normals.All(n => n.Z > 0.99f), $"flat at one plane, facing front: z {z0:0.0000} to {z1:0.0000}");
            // Its width is the letters': inside the line's width, and most of it.
            farGlow.Bounds(out Vec3 low, out Vec3 high);
            float width = high.X - low.X;
            Program.Check(width <= text.Layout.LineWidths[0] + 1e-3f && width > 0.7f * text.Layout.LineWidths[0], $"as wide as the letters: {width:0.000} m of a line of {text.Layout.LineWidths[0]:0.000}");
            // The same on a wall and for a second built twice.
            SignParts wall = Build(instance, SupportDetail.Reduced, SupportCatalog.WallFlat), wallNear = Build(instance, SupportDetail.Full, SupportCatalog.WallFlat);
            Program.Check(Same(wall.Solids[(int)text.Glow], wallNear.Solids[(int)text.Glow]), "on a wall too");
            Program.Check(Same(far.Solids[(int)text.Glow], Build(instance, SupportDetail.Reduced).Solids[(int)text.Glow]), "built twice, the same");
        }

        private static void Lines()
        {
            foreach (string align in new[] { "left", "centre", "right" })
            {
                TextPlans.Clear();
                SignInstance instance = Instance("长的一行字\\n短", ";align=" + align);
                TextBoard text = instance.Board.Text;
                SignParts near = Build(instance, SupportDetail.Full), far = Build(instance, SupportDetail.Reduced);
                Program.Check(Same(near.Solids[(int)text.Glow], far.Solids[(int)text.Glow]) && far.Solids[(int)text.Glow].VertexCount > 48, $"{align}: two lines, the near letters ({far.Solids[(int)text.Glow].VertexCount} vertices)");
            }

            // The shorter line's letters are above or below the longer's: two rows of letters, not one.
            TextPlans.Clear();
            SignInstance two = Instance("上行\\n下行");
            SignParts far2 = Build(two, SupportDetail.Reduced);
            MeshBuilder glow = far2.Solids[(int)two.Board.Text.Glow];
            float mid = (glow.Positions.Min(p => p.Y) + glow.Positions.Max(p => p.Y)) * 0.5f;
            int above = glow.Positions.Count(p => p.Y > mid), below = glow.Positions.Count(p => p.Y < mid);
            Program.Check(above > 20 && below > 20 && Math.Abs(above - below) < glow.VertexCount / 2, $"two rows of letters: {above} vertices above the middle, {below} below");
        }

        private static void Fallbacks()
        {
            // Too many vertices for a far tile: the bars (one for each line), kept, not provisional. The field takes 40 characters: forty of the most intricate (龘: 108 vertices a face) are 4,344.
            TextPlans.Clear();
            string longText = string.Concat(Enumerable.Repeat("龘", 40));
            SignInstance heavy = Instance(longText, ";h=0.3");
            TextBoard text = heavy.Board.Text;
            SignParts nearHeavy = Build(heavy, SupportDetail.Full);
            TextPlanResult plan = TextPlans.Peek(text.Job);
            int faces = plan == null ? 0 : SignBuilder.TextFaceVertices(plan);
            Program.Check(plan != null && !plan.TooHeavy && faces > SignBuilder.MaxFarTextVertices, $"forty intricate characters are more than a far tile takes ({faces} vertices) and not too heavy for a near one");
            SignParts farHeavy = Build(heavy, SupportDetail.Reduced);
            MeshBuilder bars = farHeavy.Solids[(int)text.Glow];
            Program.Check(bars != null && bars.VertexCount == 24 * text.Layout.Lines && !farHeavy.Provisional, $"over the limit: one bar a line, finished: {bars?.VertexCount}");
            Program.Check(nearHeavy.Solids[(int)text.Glow].VertexCount > bars.VertexCount, "(the near tile has its letters)");

            // Just under it: forty of the characters that are next in intricacy (鬱: 90 vertices a face, 3,600) are letters.
            TextPlans.Clear();
            SignInstance under = Instance(string.Concat(Enumerable.Repeat("鬱", 40)), ";h=0.3");
            SignParts nearUnder = Build(under, SupportDetail.Full), farUnder = Build(under, SupportDetail.Reduced);
            int underFaces = SignBuilder.TextFaceVertices(TextPlans.Peek(under.Board.Text.Job));
            Program.Check(underFaces > 3000 && underFaces <= SignBuilder.MaxFarTextVertices && Same(nearUnder.Solids[(int)under.Board.Text.Glow], farUnder.Solids[(int)under.Board.Text.Glow]), $"under the limit ({underFaces} vertices) the far tile has the near letters");

            // The limit itself is within it, one vertex over is not.
            Program.Check(SignBuilder.FarFacesFit(0) && SignBuilder.FarFacesFit(SignBuilder.MaxFarTextVertices) && !SignBuilder.FarFacesFit(SignBuilder.MaxFarTextVertices + 1) && SignBuilder.MaxFarTextVertices == 4000, "the limit is 4000 vertices, itself included");

            // A text on a curved wall (the radius in the settings, on a flat wall support): the letters are set on the arc in a near tile; a far tile has the bars, as before, flat.
            TextPlans.Clear();
            SignInstance curved = Instance("文字灯箱", ";h=0.4;arc=12");
            SignParts nearCurved = Build(curved, SupportDetail.Full, SupportCatalog.WallFlat), farCurved = Build(curved, SupportDetail.Reduced, SupportCatalog.WallFlat);
            MeshBuilder curvedBars = farCurved.Solids[(int)curved.Board.Text.Glow];
            Program.Check(nearCurved.Solids[(int)curved.Board.Text.Glow].VertexCount > 100 && curvedBars != null && curvedBars.VertexCount == 24 * curved.Board.Text.Layout.Lines && !farCurved.Provisional, $"on a curved wall a far tile has the bars: {curvedBars?.VertexCount}");

            // A text that no font can set in part, with spaces: the faces are the glyphs' that are there, and the count is theirs (a blank has no mesh).
            TextPlans.Clear();
            SignInstance gaps = Instance("文 字 ☃ 灯");
            SignParts nearGaps = Build(gaps, SupportDetail.Full), farGaps = Build(gaps, SupportDetail.Reduced);
            TextPlanResult gapsPlan = TextPlans.Peek(gaps.Board.Text.Job);
            Program.Check(gapsPlan.Meshes.Any(m => m == null) && SignBuilder.TextFaceVertices(gapsPlan) == gapsPlan.Meshes.Where(m => m != null).Sum(m => m.Points.Length), "a plan with blanks: the count is of the glyphs that have a mesh");
            Program.Check(Same(nearGaps.Solids[(int)gaps.Board.Text.Glow], farGaps.Solids[(int)gaps.Board.Text.Glow]) && farGaps.Solids[(int)gaps.Board.Text.Glow].VertexCount == SignBuilder.TextFaceVertices(gapsPlan), "and the far tile has those, each at its place");
            // A text of blanks only: nothing to light, and the bars (as the near tile has nothing either; the layout has no glyphs: no bars).
            TextPlans.Clear();
            SignInstance blank = Instance("   ");
            SignParts farBlank = Build(blank, SupportDetail.Reduced);
            MeshBuilder blankGlow = farBlank.Solids[(int)blank.Board.Text.Glow];
            Program.Check(!farBlank.Provisional && (blankGlow == null || blankGlow.VertexCount <= 24 * Math.Max(1, blank.Board.Text.Layout.Lines)), "a text of blanks draws no letters, and at most the bars it always had");

            // A text too heavy for a near tile is the bars in a far one too (the plan says so: the limit is lowered to reach it, as the text box's own test does).
            int limitWas = TextPlans.Limit;
            try
            {
                TextPlans.Clear();
                TextPlans.Limit = 1;
                SignInstance tooHeavy = Instance("文字灯箱", ";h=0.5");
                SignParts nearTooHeavy = Build(tooHeavy, SupportDetail.Full), farTooHeavy = Build(tooHeavy, SupportDetail.Reduced);
                int bar = 24 * tooHeavy.Board.Text.Layout.Lines;
                Program.Check(TextPlans.Peek(tooHeavy.Board.Text.Job).TooHeavy && nearTooHeavy.Solids[(int)tooHeavy.Board.Text.Glow].VertexCount == bar && farTooHeavy.Solids[(int)tooHeavy.Board.Text.Glow].VertexCount == bar && !farTooHeavy.Provisional,
                    "a text that is too heavy is the bars in a near tile and in a far one: " + farTooHeavy.Solids[(int)tooHeavy.Board.Text.Glow].VertexCount);
            }
            finally
            {
                TextPlans.Limit = limitWas;
                TextPlans.Clear();
            }

            // A text no font can set has no faces: in a far tile as in a near one.
            SignInstance absent = Instance("☃☃☃");
            SignParts nearAbsent = Build(absent, SupportDetail.Full), farAbsent = Build(absent, SupportDetail.Reduced);
            Program.Check(Same(nearAbsent.Solids[(int)absent.Board.Text.Glow], farAbsent.Solids[(int)absent.Board.Text.Glow]) && !farAbsent.Provisional, "a text of characters no font has: the far tile is what the near tile is");

            // A font that is gone (the plan says so: no frame, no letters): the bars, as the near tile has them.
            FontOutlines bundled = TextLayoutTests.BundledFont();
            try
            {
                SignInstance lost = Instance("文字灯箱", ";h=0.5");
                TextFonts.Use(TextFonts.DefaultId, null);
                TextPlans.Clear();
                SignParts nearLost = Build(lost, SupportDetail.Full), farLost = Build(lost, SupportDetail.Reduced);
                TextPlanResult lostPlan = TextPlans.Peek(lost.Board.Text.Job);
                int lostBars = 24 * lost.Board.Text.Layout.Lines;
                Program.Check(lostPlan != null && lostPlan.Frame == null && lostPlan.Meshes.Length == 0, "the plan of a font that is gone has no frame and no letters");
                Program.Check(nearLost.Solids[(int)lost.Board.Text.Glow].VertexCount == lostBars && farLost.Solids[(int)lost.Board.Text.Glow].VertexCount == lostBars && !farLost.Provisional, "and the far tile is the bars, as the near tile is");
            }
            finally
            {
                TextFonts.Use(TextFonts.DefaultId, bundled);
                TextPlans.Clear();
            }

            // A text on a curved wall is the lit ring of bars in a far tile as before (the radius in the settings, on a flat wall support: checked above); a text on an arc of its own (the curve
            // setting) is the ring too, never the letters unturned: the faces of flat letters would stand at the arc's places, upright.
            TextPlans.Clear();
            string archSpec = TextBoxes.Template.Parse("curve=arch;sweep=120").With("text", "文字灯箱招牌路牌").ToString();
            SignInstance arc = SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), archSpec);
            SignParts nearArc = Build(arc, SupportDetail.Full), farArc = Build(arc, SupportDetail.Reduced);
            MeshBuilder ring = farArc.Solids[(int)arc.Board.Text.Glow];
            Program.Check(TextPlans.Peek(arc.Board.Text.Job).Layout.Arc != null && ring != null && ring.VertexCount > 0 && ring.VertexCount < 1000 && !farArc.Provisional, "a text on an arc is the lit ring: " + ring?.VertexCount);
            Program.Check(ring.Normals.Any(n => Math.Abs(n.Z) < 0.9f), "made of boxes (their sides face away from the viewer), not the flat faces of letters");
            Program.Check(nearArc.Solids[(int)arc.Board.Text.Glow].VertexCount > ring.VertexCount, "(the near tile has its letters)");

            // The main thread asked and there is no plan: the bars stand in, and are not kept (provisional); a worker's tile is a finished one.
            TextPlans.Clear();
            SignInstance fresh = Instance("文字灯箱", ";h=0.5");
            SignParts onMain = null;
            var thread = new Thread(() =>
            {
                TextPlans.MarkMainThread();
                onMain = Build(fresh, SupportDetail.Reduced);
                TextPlans.ForgetMainThread();
            });
            thread.Start();
            thread.Join();
            Program.Check(onMain != null && onMain.Provisional && onMain.Solids[(int)fresh.Board.Text.Glow].VertexCount == 24 * fresh.Board.Text.Layout.Lines, "the main thread has no plan: provisional bars");
            for (int i = 0; i < 400 && TextPlans.Peek(fresh.Board.Text.Job) == null; i++)
                Thread.Sleep(10);
            SignParts later = Build(fresh, SupportDetail.Reduced);
            Program.Check(!later.Provisional && later.Solids[(int)fresh.Board.Text.Glow].VertexCount > 24 * fresh.Board.Text.Layout.Lines, "and once the plan is made: the letters, kept");

            // A leaning text asked on the main thread with no plan: the bars stand in, provisional, and lean (the roof's frame is built).
            TextPlans.Clear();
            SignInstance leaningFresh = Instance("倾斜灯箱", ";h=0.5;tilt=30");
            SignParts leanOnMain = null;
            var leanThread = new Thread(() =>
            {
                TextPlans.MarkMainThread();
                leanOnMain = Build(leaningFresh, SupportDetail.Reduced, SupportCatalog.WallRoof, 0.5f);
                TextPlans.ForgetMainThread();
            });
            leanThread.Start();
            leanThread.Join();
            Program.Check(leanOnMain != null && leanOnMain.Provisional && leanOnMain.Solids[(int)leaningFresh.Board.Text.Glow].VertexCount == 24 * leaningFresh.Board.Text.Layout.Lines, "a leaning text with no plan, asked on the main thread: provisional bars");
            for (int i = 0; i < 400 && TextPlans.Peek(leaningFresh.Board.Text.Job) == null; i++)
                Thread.Sleep(10);

            // A leaning text on a roof: the letters lean with it; the same letters upright as a far tile has them when it does not lean.
            TextPlans.Clear();
            SignInstance up = Instance("文字灯箱", ";h=0.5"), leaning = Instance("文字灯箱", ";h=0.5;tilt=30");
            SignParts roofUp = Build(up, SupportDetail.Reduced, SupportCatalog.WallRoof, 0.5f), roofLean = Build(leaning, SupportDetail.Reduced, SupportCatalog.WallRoof, 0.5f);
            MeshBuilder upGlow = roofUp.Solids[(int)up.Board.Text.Glow], leanGlow = roofLean.Solids[(int)leaning.Board.Text.Glow];
            Program.Check(upGlow != null && leanGlow != null && leanGlow.VertexCount == upGlow.VertexCount && leanGlow.VertexCount > 24, $"on a roof, a leaning text has its letters: {leanGlow?.VertexCount}");
            float upDepth = upGlow.Positions.Max(p => p.Z) - upGlow.Positions.Min(p => p.Z), leanDepth = leanGlow.Positions.Max(p => p.Z) - leanGlow.Positions.Min(p => p.Z);
            Program.Check(upDepth < 1e-4f && leanDepth > 0.05f, $"upright, flat; leaning, the face is tilted: {upDepth:0.000} and {leanDepth:0.000} m of depth");
            Program.Check(!roofLean.Provisional, "and finished");
            // A wall does not lean in a far tile (it never did): the letters stand upright, the same for any angle.
            SignParts wallLean = Build(Instance("文字灯箱", ";h=0.5;tilt=20"), SupportDetail.Reduced, SupportCatalog.WallFlat), wallUp = Build(Instance("文字灯箱", ";h=0.5"), SupportDetail.Reduced, SupportCatalog.WallFlat);
            Program.Check(TextTiltTests.Hash(wallLean) == TextTiltTests.Hash(wallUp) && wallLean.Solids[(int)up.Board.Text.Glow].VertexCount > 24, "on a wall in a far tile: upright letters, whatever the angle");
            // Near, a leaning text has its frame and return; far, the glow solid of a leaning text is the letters' faces and the faces only (no studs: the metal is the roof's frames).
            SignParts roofNear = Build(leaning, SupportDetail.Full, SupportCatalog.WallRoof, 0.5f);
            Program.Check(roofNear.Solids[(int)leaning.Board.Text.Glow].VertexCount == leanGlow.VertexCount && roofNear.Metal.VertexCount > roofLean.Metal.VertexCount, "the leaning letters are the near tile's faces; the near tile has the frame the far one has not");
        }

        private static void StylesAndTile()
        {
            // The outline and neon styles: the far tile has a lit face for each letter (the band, for neon the band the tube lies on), finished, and never more than the limit.
            foreach (string style in new[] { "outline", "neon" })
            {
                TextPlans.Clear();
                SignInstance styled = Instance("文字灯箱", ";h=0.5;style=" + style);
                TextBoard text = styled.Board.Text;
                SignParts farStyled = Build(styled, SupportDetail.Reduced);
                MeshBuilder glow = farStyled.Solids[(int)text.Glow];
                Program.Check(glow != null && glow.VertexCount > 0 && glow.VertexCount <= Math.Max(24 * text.Layout.Lines, SignBuilder.MaxFarTextVertices) && !farStyled.Provisional, $"{style}: lit letters in a far tile, finished: {glow?.VertexCount}");
                Program.Check(glow.Positions.All(p => !float.IsNaN(p.X) && !float.IsNaN(p.Y) && !float.IsNaN(p.Z)), style + ": finite");
            }

            // The tile builder (ChunkBuilder) at each tier: near has the letters and their return, far the letters alone, in the glow solid.
            TextPlans.Clear();
            string spec = Spec("路边招牌").ToString();
            uint post = SupportPlanner.Auto(Instance("路边招牌").Def).Hash;
            ItemRecord record = new ItemRecord(SignCatalog.ById(TextBoxes.TemplateId).Hash, new Vec3(10f, 0f, 20f), 0.3f, post, spec, 0f, 0u, 0, 0, 0f);
            List<ChunkPart> near = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(TileTier.Near), out int skippedNear);
            List<ChunkPart> far = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(TileTier.Far), out int skippedFar);
            List<ChunkPart> horizon = ChunkBuilder.BuildDetailed(new[] { record }, Vec3.Zero, ChunkDetail.Of(TileTier.Horizon), out _);
            ChunkPart nearGlow = near.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm)), farGlow = far.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm));
            Program.Check(skippedNear == 0 && skippedFar == 0 && nearGlow != null && farGlow != null && farGlow.Mesh.VertexCount == nearGlow.Mesh.VertexCount, $"a far tile has the near tile's letters in the glow solid: {farGlow?.Mesh.VertexCount} and {nearGlow?.Mesh.VertexCount}");
            Program.Check(far.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.Black)) == null, "and no return");
            Program.Check(near.Sum(p => p.Mesh.VertexCount) > 2 * far.Sum(p => p.Mesh.VertexCount), $"and is much lighter than a near tile: {far.Sum(p => p.Mesh.VertexCount)} against {near.Sum(p => p.Mesh.VertexCount)}");
            Program.Check(horizon.Count == 0, "the horizon tier is for the walls: a text box has nothing there");
            // Many boxes with the same text share one plan: the far tile makes it once.
            // (a text that no tile has built yet: the tile builder keeps what it built for a spec)
            TextPlans.Clear();
            string spec20 = Spec("二十个灯箱").ToString();
            TextPlans.Counters(out int madeBefore, out _, out _, out _, out _);
            var many = new List<ItemRecord>();
            for (int i = 0; i < 20; i++)
                many.Add(new ItemRecord(record.TypeHash, new Vec3(10f + i * 3f, 0f, 20f), 0.3f, post, spec20, 0f, 0u, 0, 0, 0f));
            List<ChunkPart> farMany = ChunkBuilder.BuildDetailed(many, Vec3.Zero, ChunkDetail.Of(TileTier.Far), out _);
            TextPlans.Counters(out int madeAfter, out _, out _, out _, out _);
            ChunkPart manyGlow = farMany.Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm));
            TextPlans.Clear();
            ChunkPart oneGlow = ChunkBuilder.BuildDetailed(new[] { many[0] }, Vec3.Zero, ChunkDetail.Of(TileTier.Far), out _).Find(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.GlowWarm));
            Program.Check(madeAfter - madeBefore == 1 && manyGlow != null && oneGlow != null && manyGlow.Mesh.VertexCount == 20 * oneGlow.Mesh.VertexCount, $"twenty boxes of one text: one plan made ({madeAfter - madeBefore}), twenty sets of letters ({manyGlow?.Mesh.VertexCount} of {oneGlow?.Mesh.VertexCount} each)");
        }
    }
}
