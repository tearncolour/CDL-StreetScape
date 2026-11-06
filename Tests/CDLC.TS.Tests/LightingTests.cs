using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The lighting of a board (0.12.80, SignLighting; GB 5768.2-2022 4.8.14 to 4.8.16): the setting is there only on the standard's boards and written only when set; a board with no lighting is built
    /// bit for bit as 0.12.79 built it; an internally lit board is a light box, an externally lit one has a lamp bar over and under its face, laid out by a model of the face's evenness.
    /// </summary>
    internal static class LightingTests
    {
        public static void RunAll()
        {
            Program.Run("lighting: a board with no lighting is built as 0.12.79 built it, on every support in both details", Unchanged);
            Program.Run("lighting: the setting is on the standard's boards, written only when set, and an older build ignores it", Setting);
            Program.Run("lighting: the model says opposed bars are even where one bar is not", Model);
            Program.Run("lighting: an internally lit board is a light box behind its face", Box);
            Program.Run("lighting: the lamp bars stand off the face, over and under it, no wider than the outline", Bars);
            Program.Run("lighting: the same on every support, in both details, finite, and the cost is fixed", EverySupport);
            Program.Run("lighting: the numbers are the layout's own, not read back from its constants", Literals);
            Program.Run("lighting: only the standard's groups have the setting, and the lens faces the board", Groups);
            Program.Run("lighting: a board with a plate on it is lit once, on its main board", Plate);
        }

        private static SignChoice Choice(string id) => SignCatalog.Choices.First(c => c.Id == id);

        private static SignInstance Lit(string id, string lighting)
        {
            SignChoice choice = Choice(id);
            string spec = choice.Def.Template.Parse(choice.Spec ?? "").With(SignLighting.Key, lighting).ToString();
            return SignInstances.Of(choice.Def, spec);
        }

        private static SignParts Build(SignInstance instance, string support, SupportDetail detail = SupportDetail.Full) =>
            SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, SupportCatalog.ById(support), 0f, detail);

        /// <summary>The vertices of a mesh that the other does not have, by position: the lighting is built in the middle of the board's own parts, not at the end of the mesh.</summary>
        private static List<Vec3> Added(MeshBuilder before, MeshBuilder after)
        {
            var seen = new Dictionary<(int, int, int), int>();
            foreach (Vec3 p in before.Positions)
            {
                var key = ((int)Math.Round(p.X * 10000f), (int)Math.Round(p.Y * 10000f), (int)Math.Round(p.Z * 10000f));
                seen[key] = seen.TryGetValue(key, out int n) ? n + 1 : 1;
            }

            var added = new List<Vec3>();
            foreach (Vec3 p in after.Positions)
            {
                var key = ((int)Math.Round(p.X * 10000f), (int)Math.Round(p.Y * 10000f), (int)Math.Round(p.Z * 10000f));
                if (seen.TryGetValue(key, out int n) && n > 0)
                    seen[key] = n - 1;
                else
                    added.Add(p);
            }

            return added;
        }

        private static void Unchanged()
        {
            string golden = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "lighting-0.12.79.txt");
            Program.Check(golden != null, "the golden file is found");
            if (golden == null)
                return;
            var want = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(golden, new System.Text.UTF8Encoding(false)))
            {
                string[] p = line.Split('|');
                want[p[0] + "|" + p[1]] = p[2];
            }

            List<string> now = LightingPrint.Lines();
            Program.Check(now.Count == want.Count, "the same boards: " + now.Count + " against " + want.Count);
            int differ = 0, redrawn = 0, farRedrawn = 0;
            foreach (string line in now)
            {
                string[] p = line.Split('|');
                string key = p[0] + "|" + p[1];
                if (!want.TryGetValue(key, out string was) || was != p[2])
                {
                    // The junctions-ahead board was redrawn on purpose after 0.12.79 (the owner: "太糙了"); no other board is allowed to differ. Its own tests pin the new drawing in literal numbers.
                    if (p[0] == "gb.guide8.01c")
                    {
                        redrawn++;
                        continue;
                    }

                    // The two text light boxes' far tiles were redrawn on purpose (0.12.105: the letters, not one bar a line); their full-detail builds are compared below against a table of their own.
                    if (LightingPrint.FarRedrawn.Contains(p[0]))
                    {
                        farRedrawn++;
                        continue;
                    }

                    differ++;
                    if (differ <= 5)
                        Program.Check(false, key + " is not as 0.12.79 built it");
                }
            }

            Program.Check(differ == 0, differ + " board(s) differ from 0.12.79's");
            Program.Check(redrawn == 1, "the redrawn junctions-ahead board is the one the golden table has: " + redrawn);
            Program.Check(farRedrawn == LightingPrint.FarRedrawn.Count, "both text light boxes' far builds are redrawn (the golden table's whole line differs for each): " + farRedrawn);
            // What is left of the redrawn boards that must not move: their builds in the full detail, on every support, from 0.12.104's code (lighting-full-0.12.79.txt: made with the far tile still bars).
            string full = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "lighting-full-0.12.79.txt");
            Program.Check(full != null, "the golden of the full details is found");
            if (full == null)
                return;
            var wantFull = new Dictionary<string, string>();
            foreach (string line in File.ReadAllLines(full, new System.Text.UTF8Encoding(false)))
            {
                string[] p = line.Split('|');
                wantFull[p[0] + "|" + p[1]] = p[2];
            }

            List<string> nowFull = LightingPrint.FullLines();
            int fullDiffer = 0;
            foreach (string line in nowFull)
            {
                string[] p = line.Split('|');
                if (!wantFull.TryGetValue(p[0] + "|" + p[1], out string was) || was != p[2])
                {
                    fullDiffer++;
                    Program.Check(false, p[0] + " is not as 0.12.104 built it in the full detail");
                }
            }

            Program.Check(nowFull.Count == LightingPrint.FarRedrawn.Count && nowFull.Count == wantFull.Count && fullDiffer == 0, "the full-detail builds of the two text light boxes are as they were: " + nowFull.Count + " boards, " + fullDiffer + " differ");
        }

        private static void Setting()
        {
            // The standard's boards have it; a marking, an electric box, an LED screen and a lamp-post ad do not.
            foreach (string id in new[] { "gb.prohib.01", "gb.warn.01", "gb.guide8.01", "gb.mand.04", "gb.aux.text" })
            {
                GuideTemplate t = Choice(id).Def.Template;
                Program.Check(t.FieldOf(SignLighting.Key) != null, id + " has the lighting setting");
                Program.Check(!t.Parse("").ToString().Contains(SignLighting.Key + "="), id + ": the default spec does not mention it");
            }

            foreach (SignChoice choice in SignCatalog.Choices)
            {
                GuideTemplate t = choice.Def?.Template;
                if (t == null)
                    continue;
                Program.Check((t.FieldOf(SignLighting.Key) != null) == SignLighting.Applies(t), choice.Id + ": the setting is where Applies says");
            }

            GuideTemplate speed = Choice("gb.prohib.01").Def.Template;
            string written = speed.Parse("speed=auto;lt=i").ToString();
            Program.Check(written.Contains("lt=i"), "set, it is written: " + written);
            Program.Check(SignLighting.Of(speed.Parse(written)) == LightingKind.Internal, "and read back");
            Program.Check(!speed.Parse("speed=auto;lt=n").ToString().Contains("lt="), "set to none it is not written");
            Program.Check(SignLighting.Of(speed.Parse("speed=auto;lt=zzz")) == LightingKind.None, "a value that is not one means none");
        }

        /// <summary>The model that chose the layout (IlluminanceRatio): what it says, and what it does not.</summary>
        private static void Model()
        {
            // The standard (4.8.16 c 2): the greatest over the least illuminance below 4. On the rectangles of the catalogue, from a small plate to the largest guide board, a bar over and a bar under the face
            // at the reach of the layout are inside it; one bar on one edge is not, on any board from 1.2 m wide.
            foreach ((float w, float h) in new[] { (0.6f, 0.8f), (1.2f, 0.8f), (2f, 1.2f), (3f, 1.5f), (4.8f, 2.22f) })
            {
                float both = SignBuilder.IlluminanceRatio(w, h, true, true, SignBuilder.BarReach);
                float one = SignBuilder.IlluminanceRatio(w, h, true, false, SignBuilder.BarReach);
                Program.Check(both < SignLighting.MaxIlluminanceRatio, $"{w} x {h}: two bars, ratio {both:0.0} is under {SignLighting.MaxIlluminanceRatio}");
                Program.Check(one > SignLighting.MaxIlluminanceRatio && one > both * 2f, $"{w} x {h}: one bar, ratio {one:0.0}, is not");
            }

            // More reach makes it more even, never less (so the layout's fixed reach is not a knife edge): from the layout's to twice it.
            for (float reach = SignBuilder.BarReach; reach <= SignBuilder.BarReach * 1.5f; reach += 0.05f)
                Program.Check(SignBuilder.IlluminanceRatio(2f, 1.2f, true, true, reach) < SignLighting.MaxIlluminanceRatio, "at a reach of " + reach.ToString("0.00") + " m the evenness holds");
            // A board lit from the bottom only is the mirror of one lit from the top only.
            Program.Near(SignBuilder.IlluminanceRatio(2f, 1.2f, true, false, SignBuilder.BarReach), SignBuilder.IlluminanceRatio(2f, 1.2f, false, true, SignBuilder.BarReach), 0.05f, "top only and bottom only are mirrors");
            Program.Check(float.IsPositiveInfinity(SignBuilder.IlluminanceRatio(2f, 1.2f, false, false, SignBuilder.BarReach)), "no lamp lights nothing");
        }

        private static void Box()
        {
            foreach (string id in new[] { "gb.guide8.01", "gb.prohib.01" })
            {
                SignInstance none = Lit(id, SignLighting.ValueNone), box = Lit(id, SignLighting.ValueInternal);
                SignParts plain = Build(none, SupportCatalog.SinglePole), lit = Build(box, SupportCatalog.SinglePole);
                float w = box.Def.Width, h = box.Def.Height, depth = SignBuilder.BoxDepthOf(w, h);
                Program.Check(depth >= SignBuilder.BoxDepthMin && depth <= SignBuilder.BoxDepthMax, id + ": the housing is between " + SignBuilder.BoxDepthMin + " and " + SignBuilder.BoxDepthMax + " m deep: " + depth);
                Program.Check(lit.Metal.VertexCount == plain.Metal.VertexCount + 24, id + ": a housing is one box of 24 vertices: " + (lit.Metal.VertexCount - plain.Metal.VertexCount));
                Program.Check(lit.Faces.Count == plain.Faces.Count && lit.Faces[0].Mesh.VertexCount == plain.Faces[0].Mesh.VertexCount, id + ": the face is the face it was");
                // The box is behind the board's back (smaller z than every vertex of the board's own back), over its width and height.
                PickShape board = lit.Picks.Find(p => p.Name == "board");
                float backZ = board.Min.Z;
                List<Vec3> added = Added(plain.Metal, lit.Metal);
                Program.Check(added.Count == 24 && added.All(p => p.Z <= backZ + 1e-4f && p.Z >= backZ - depth - 1e-4f), id + ": the housing lies in the " + depth + " m behind the board's back");
                Program.Near(added.Max(p => p.X) - added.Min(p => p.X), w, 1e-3f, id + ": as wide as the board");
                Program.Near(added.Max(p => p.Y) - added.Min(p => p.Y), h, 1e-3f, id + ": as high as the board");
                Program.Near((added.Max(p => p.X) + added.Min(p => p.X)) * 0.5f, (board.Min.X + board.Max.X) * 0.5f, 1e-3f, id + ": centred on the board");
                Program.Check(added.All(p => p.IsFinite), id + ": finite");
            }

            // A deeper housing for a larger face, within the bounds.
            Program.Check(SignBuilder.BoxDepthOf(0.6f, 0.6f) <= SignBuilder.BoxDepthOf(4.8f, 2.22f), "the housing is no shallower on a larger face");
            Program.Check(SignBuilder.BoxDepthOf(0.1f, 0.1f) == SignBuilder.BoxDepthMin && SignBuilder.BoxDepthOf(20f, 20f) == SignBuilder.BoxDepthMax, "and it stays within its bounds");
        }

        private static void Bars()
        {
            // A rectangle (the junction board, 4.8 x 2.22 m): a bar over and under, each 90 % of the width, BarReach in front of the face, BarOffset beyond the edge.
            SignInstance rect = Lit("gb.guide8.01", SignLighting.ValueExternal);
            SignParts plain = Build(Lit("gb.guide8.01", SignLighting.ValueNone), SupportCatalog.SinglePole), lit = Build(rect, SupportCatalog.SinglePole);
            PickShape board = lit.Picks.Find(p => p.Name == "board");
            float faceZ = board.Max.Z, cx = (board.Min.X + board.Max.X) * 0.5f, top = board.Max.Y, bottom = board.Min.Y;
            MeshBuilder lens = lit.Solids[(int)SolidColour.GlowWhite];
            Program.Check(lens != null && lens.VertexCount == 48, "two lenses, one for each bar: " + (lens == null ? 0 : lens.VertexCount) + " vertices");
            Program.Check(plain.Solids[(int)SolidColour.GlowWhite] == null || plain.Solids[(int)SolidColour.GlowWhite].VertexCount == 0, "and the board with none has no glowing solid");
            float lowest = lens.Positions.Min(p => p.Y), highest = lens.Positions.Max(p => p.Y);
            Program.Check(lowest < bottom && highest > top, "the lenses are under the bottom edge and over the top edge");
            Program.Check(lens.Positions.All(p => p.Z > faceZ + SignBuilder.BarReach - SignBuilder.BarBody && p.Z < faceZ + SignBuilder.BarReach + SignBuilder.BarBody), "the lenses are BarReach in front of the face");
            Program.Near(lens.Positions.Max(p => p.X) - lens.Positions.Min(p => p.X), rect.Def.Width * SignBuilder.BarFill * 0.96f, 1e-3f, "each bar is 90 % of the board's width");
            Program.Check(lens.Positions.All(p => p.IsFinite), "finite");
            // The lens looks at the face: over the top edge it is on the lower side of its housing, under the bottom edge on the upper side.
            List<Vec3> steel = Added(plain.Metal, lit.Metal);
            Program.Check(steel.Count > 0 && steel.All(p => p.IsFinite), "the housings and the arms are finite: " + steel.Count);

            // A circle and an octagon: the bars follow the outline and never go past it; a triangle: a bar on its wide side only.
            foreach (string id in new[] { "gb.mand.01", "gb.prohib.01", "gb.warn.01" })
            {
                SignInstance round = Lit(id, SignLighting.ValueExternal);
                SignParts built = Build(round, SupportCatalog.SinglePole);
                MeshBuilder glow = built.Solids[(int)SolidColour.GlowWhite];
                int bars = glow == null ? 0 : glow.VertexCount / 24;
                int want = round.Def.Shape == ShapeKind.Triangle || round.Def.Shape == ShapeKind.TriangleDown ? 1 : 2;
                Program.Check(bars == want, id + " (" + round.Def.Shape + ") has " + want + " bar(s): " + bars);
                if (glow == null)
                    continue;
                PickShape p = built.Picks.Find(q => q.Name == "board");
                float half = (glow.Positions.Max(q => q.X) - glow.Positions.Min(q => q.X)) * 0.5f;
                Program.Check(half * 2f <= round.Def.Width * SignBuilder.BarFill + 1e-3f, id + ": no bar is wider than the board's 90 %");
                // The bar stands where the board is at least as wide as the bar is.
                foreach (var group in glow.Positions.GroupBy(q => q.Y > (p.Min.Y + p.Max.Y) * 0.5f))
                {
                    float barHalf = (group.Max(q => q.X) - group.Min(q => q.X)) * 0.5f;
                    float edgeY = group.Key ? p.Max.Y : p.Min.Y;
                    float dy = (group.Key ? 1f : -1f) * (round.Def.Height * 0.5f - SignBuilder.BarChordIn * round.Def.Height);
                    Program.Check(barHalf * 2f <= SignCatalog.ChordAt(round.Def, dy) + 1e-3f, id + ": the " + (group.Key ? "upper" : "lower") + " bar is inside the outline's width there");
                }
            }
        }

        /// <summary>The figures of the layout as numbers, so that a changed constant is a changed test: a test that reads the constant back cannot see it move.</summary>
        private static void Literals()
        {
            // The housing: 0.10 m at the least, 0.22 m at the most; a 4.8 x 2.22 m board is held at the most, a 0.6 m one at the least.
            Program.Near(SignBuilder.BoxDepthOf(4.8f, 2.22f), 0.22f, 1e-6f, "the largest board's housing is 0.22 m deep");
            Program.Near(SignBuilder.BoxDepthOf(0.6f, 0.6f), 0.10f, 1e-6f, "a small board's is 0.10 m");
            Program.Near(SignBuilder.BoxDepthOf(2f, 1.2f), 0.07f + 0.045f * 2f, 1e-6f, "in between it grows with the larger side: 0.16 m at 2 m");
            Program.Near(SignBuilder.BoxDepthOf(20f, 20f), 0.22f, 1e-6f, "and 0.22 m is the most of any");

            // The bars: 0.40 m in front of the face, 0.10 m beyond the edge, 90 % of a rectangle's width.
            SignInstance rect = Lit("gb.guide8.01", SignLighting.ValueExternal);
            SignParts plain = Build(Lit("gb.guide8.01", SignLighting.ValueNone), SupportCatalog.SinglePole), lit = Build(rect, SupportCatalog.SinglePole);
            PickShape board = lit.Picks.Find(p => p.Name == "board");
            MeshBuilder lens = lit.Solids[(int)SolidColour.GlowWhite];
            Program.Near(rect.Def.Width, 4.8f, 1e-3f, "the junction board is 4.8 m wide");
            Program.Near(lens.Positions.Max(p => p.X) - lens.Positions.Min(p => p.X), 4.8f * 0.9f * 0.96f, 1e-3f, "a bar is 90 % of 4.8 m, its lens 96 % of that: 4.147 m");
            Program.Near(lens.Positions.Max(p => p.Z) - board.Max.Z, 0.40f + 0.06f * 0.4f, 1e-3f, "the lens is 0.40 m in front of the face, its own half depth (0.024 m) more at its front");
            Program.Near(lens.Positions.Max(p => p.Y) - board.Max.Y, 0.10f - 0.06f * 0.5f - 0.012f * 0.5f + 0.012f * 0.5f, 1e-3f, "the upper lens's top is 0.07 m over the top edge: the bar 0.10 m beyond it, the lens on the housing's lower face, toward the board");
            Program.Near(board.Min.Y - lens.Positions.Min(p => p.Y), 0.07f, 1e-3f, "and the lower lens's bottom as far under the bottom edge");

            // Two arms to each bar, six-sided and open: two rings of seven vertices each, 14 a tube, 56 for four. A reduced tile has none of them.
            SignParts reducedLit = Build(rect, SupportCatalog.SinglePole, SupportDetail.Reduced), reducedPlain = Build(Lit("gb.guide8.01", SignLighting.ValueNone), SupportCatalog.SinglePole, SupportDetail.Reduced);
            Program.Check((lit.VertexCount - plain.VertexCount) - (reducedLit.VertexCount - reducedPlain.VertexCount) == 4 * 14, "four arms of 14 vertices are what a full tile has more than a reduced one: " + ((lit.VertexCount - plain.VertexCount) - (reducedLit.VertexCount - reducedPlain.VertexCount)));
            Program.Check(reducedLit.VertexCount - reducedPlain.VertexCount == 2 * 24 + 2 * 24, "a reduced tile has the two housings and the two lenses: " + (reducedLit.VertexCount - reducedPlain.VertexCount));
            Program.Check(lit.VertexCount - plain.VertexCount == 2 * 24 + 2 * 24 + 4 * 14, "a lit board adds 152 vertices on a post: two housings, two lenses and four arms: " + (lit.VertexCount - plain.VertexCount));
            Program.Check(Build(Lit("gb.guide8.01", SignLighting.ValueInternal), SupportCatalog.SinglePole).VertexCount - plain.VertexCount == 24, "a light box adds 24");
        }

        private static void Groups()
        {
            // The groups that have the setting, by name.
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                GuideTemplate t = choice.Def?.Template;
                if (t == null)
                    continue;
                bool has = t.FieldOf(SignLighting.Key) != null;
                string group = t.Group ?? "";
                bool should = !t.IsMarking && (group == "prohibition" || group == "mandatory" || group == "warning" || group == "auxiliary" || group == "guide" || group == "tourist" || group == "notice");
                Program.Check(has == should, choice.Id + " [" + group + (t.IsMarking ? ", marking" : "") + "]: the setting is " + (should ? "there" : "not there"));
            }

            foreach (string group in new[] { "brt", "toll", "wayfinding", "cabinet", "lampad", "variable", "placename", "custom" })
            {
                int boards = SignCatalog.Choices.Count(c => c.Def?.Template != null && (c.Def.Template.Group ?? "") == group);
                Program.Check(boards == 0 || SignCatalog.Choices.Where(c => c.Def?.Template != null && (c.Def.Template.Group ?? "") == group).All(c => c.Def.Template.FieldOf(SignLighting.Key) == null), group + ": none of its " + boards + " boards has the setting");
            }

            // A road marking has no board to light: its templates are in none of the groups, and none has the field.
            Program.Check(SignCatalog.Choices.Count(c => c.Def?.Template != null && c.Def.Template.IsMarking) > 100, "the catalogue has its markings");
            Program.Check(SignCatalog.Choices.Where(c => c.Def?.Template != null && c.Def.Template.IsMarking).All(c => c.Def.Template.FieldOf(SignLighting.Key) == null), "no marking has it");

            // Giving the setting twice gives it once.
            SignLighting.OptIn(GuideTemplates.All);
            foreach (GuideTemplate template in GuideTemplates.All)
                Program.Check(template.Fields.Count(f => f.Key == SignLighting.Key) == (SignLighting.Applies(template) ? 1 : 0), template.Id + ": the lighting setting is there once, not twice");

            // The lens is on the board's side of its housing: the upper bar's lens is lower than the bar's centre, the lower bar's lens higher.
            SignParts lit = Build(Lit("gb.guide8.01", SignLighting.ValueExternal), SupportCatalog.SinglePole);
            SignParts plain = Build(Lit("gb.guide8.01", SignLighting.ValueNone), SupportCatalog.SinglePole);
            PickShape board = lit.Picks.Find(p => p.Name == "board");
            float middle = (board.Min.Y + board.Max.Y) * 0.5f;
            List<Vec3> housings = Added(plain.Metal, lit.Metal).Where(p => p.Z > board.Max.Z + 0.3f).ToList();
            foreach (bool upper in new[] { true, false })
            {
                float housing = housings.Where(p => (p.Y > middle) == upper).Average(p => p.Y);
                float lens = lit.Solids[(int)SolidColour.GlowWhite].Positions.Where(p => (p.Y > middle) == upper).Average(p => p.Y);
                Program.Check(upper ? lens < housing : lens > housing, "the " + (upper ? "upper" : "lower") + " bar's lens is on the board's side of its housing: lens " + lens.ToString("0.000") + ", housing " + housing.ToString("0.000"));
            }

            // And the lighting of a board that is not lit is on no support: nothing glows.
            Program.Check(plain.Solids[(int)SolidColour.GlowWhite] == null || plain.Solids[(int)SolidColour.GlowWhite].VertexCount == 0, "an unlit board has no glow solid");
        }

        /// <summary>The exit advance board with its exit-number plate is two parts; the lamps are the main board's, once.</summary>
        private static void Plate()
        {
            SignDef def = SignCatalog.ById(Assembly.ExitAdvanceId);
            Program.Check(def != null && def.Template != null, "the exit advance board is in the catalogue");
            SignChoice choice = new SignChoice { Def = def, Spec = def.Template.Default().ToString() };
            string spec = choice.Def.Template.Parse(choice.Spec ?? "").With("road", "g").With("code", "G204").With("to1", "如皋").With("to2", "海安").With("dist", "500").With("unit", "m").With("xnum", "145").ToString();
            SignInstance plainBoard = SignInstances.Of(choice.Def, spec), litBoard = SignInstances.Of(choice.Def, choice.Def.Template.Parse(spec).With(SignLighting.Key, SignLighting.ValueExternal).ToString());
            Program.Check(plainBoard.Board.Parts.Count == 2 && litBoard.Board.Parts.Count == 2, "the board has its number plate as a second part");
            SignParts lit = Build(litBoard, SupportCatalog.SinglePole), plain = Build(plainBoard, SupportCatalog.SinglePole);
            MeshBuilder lens = lit.Solids[(int)SolidColour.GlowWhite];
            Program.Check(lens != null && lens.VertexCount == 48, "two bars, not four: " + (lens == null ? 0 : lens.VertexCount) + " lens vertices");
            Program.Check(lit.VertexCount - plain.VertexCount == 152, "the cost is the same 152 vertices as for a board of one part: " + (lit.VertexCount - plain.VertexCount));
        }

        private static void EverySupport()
        {
            // Every support takes both kinds, in both details; what they add does not depend on the support, and is finite.
            SignInstance none = Lit("gb.prohib.01", SignLighting.ValueNone), box = Lit("gb.prohib.01", SignLighting.ValueInternal), lamps = Lit("gb.prohib.01", SignLighting.ValueExternal);
            int checkedSupports = 0;
            foreach (SupportDef support in SupportCatalog.All)
            {
                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                {
                    SignParts plain = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = none } }, support, 0f, detail);
                    SignParts inside = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = box } }, support, 0f, detail);
                    SignParts outside = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = lamps } }, support, 0f, detail);
                    string where = support.Id + "/" + detail;
                    Program.Check(inside.VertexCount == plain.VertexCount + 24, where + ": a light box adds 24 vertices: " + (inside.VertexCount - plain.VertexCount));
                    Program.Check(outside.VertexCount > plain.VertexCount && outside.VertexCount <= plain.VertexCount + 200, where + ": lamp bars add less than 200 vertices: " + (outside.VertexCount - plain.VertexCount));
                    Program.Check(detail == SupportDetail.Reduced ? outside.VertexCount - plain.VertexCount == 96 : outside.VertexCount - plain.VertexCount > 96, where + ": a reduced tile has the bars without their arms");
                    Program.Check(outside.Min.IsFinite && outside.Max.IsFinite && inside.Min.IsFinite && inside.Max.IsFinite, where + ": the bounds are finite");
                    checkedSupports++;
                }
            }

            Program.Check(checkedSupports == SupportCatalog.All.Count * 2, "every support was checked: " + checkedSupports);
            // The cost of a lit board is the same on every support: the same number of vertices added.
            var added = SupportCatalog.All.Select(s => Build(lamps, s.Id).VertexCount - Build(none, s.Id).VertexCount).Distinct().ToList();
            Program.Check(added.Count == 1, "the lamp bars add the same on every support: " + string.Join(",", added));
        }
    }
}
