using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The road markings of GB 5768.3-2025 as templates of the catalogue (group "marking"): each has its number in the standard, its settings (the sizes the standard lets the player choose among and
    /// the lengths it leaves to the site), and a pure function that makes the shapes from them (the geometry of RoadMarkings, RoadArrows, MarkingLines, MarkingFigures, MarkingWarnings). A marking is
    /// an item like a sign: a position, a facing (+Z the way the traffic runs over it) and the settings; two settings every marking has that the editor does not show: "g", the grade of the road
    /// where it lies, and, for a marking that runs along a road, "path", the polyline of the run (RunPath). Decoration only: nothing here reads or changes a lane.
    /// </summary>
    public static partial class MarkingTemplates
    {
        private static GuideTemplate[] s_All;

        /// <summary>The most vertices of paint kept (twice this at the most): about 40 MB at 32 bytes a vertex and the indices; a city of 15 000 markings has about half a million.</summary>
        public const long PlanBudgetVertices = 1200000;

        private static readonly WeightedTwoGenCache<(string, string, int), MarkingPlan> s_Plans = new WeightedTwoGenCache<(string, string, int), MarkingPlan>(PlanBudgetVertices, p => 64 + p.VertexCount);

        private static long s_Parsed;

        /// <summary>How many look-ups of a plan had to parse the settings (not found by their written text): for the benchmark and the tests.</summary>
        public static long Parsed => System.Threading.Interlocked.Read(ref s_Parsed);

        /// <summary>How many plans and vertices are kept now (for the log and the benchmark).</summary>
        public static int CachedPlans
        {
            get
            {
                lock (s_Plans)
                    return s_Plans.Count;
            }
        }

        public static long CachedVertices
        {
            get
            {
                lock (s_Plans)
                    return s_Plans.Weight;
            }
        }

        /// <summary>Every marking template, in the order of the standard's chapters.</summary>
        public static IReadOnlyList<GuideTemplate> All => s_All ?? (s_All = Build());

        public static GuideTemplate ById(string id)
        {
            foreach (GuideTemplate t in All)
            {
                if (t.Id == id)
                    return t;
            }

            return null;
        }

        /// <summary>The paint of a marking with its written settings, made once and kept (the shapes, the words from the font, the grade of the road). Safe from any thread.</summary>
        public static MarkingPlan PlanOf(GuideTemplate template, string spec) => PlanOf(template, spec, true);

        /// <summary>
        /// As PlanOf; keep false makes a plan that is not kept (and does not push kept ones out) when it is not there already: what the tool shows under the cursor changes at every move of the mouse, and only
        /// the marking that is placed is drawn again.
        /// </summary>
        public static MarkingPlan PlanOf(GuideTemplate template, string spec, bool keep) => PlanOf(template, spec, keep, BarrierLod.Full);

        /// <summary>The level the plan being made is made at (0.12.73, BarrierLod): the templates' functions take the settings only, and the noise walls' read the level from here (Noise). Per thread: plans are made on the workers.</summary>
        [ThreadStatic]
        private static int t_Level;

        /// <summary>The ids of the templates whose items are seen from far and have levels of detail: the noise walls and enclosures (LongReach, BarrierLod).</summary>
        public static readonly string[] LongReachIds = { NoiseBarrierId, NoiseCoverId, CementNoiseId, BrickNoiseId, GreenNoiseId };

        /// <summary>The plan of a marking at a level of detail: a template without levels (all but the noise walls and LeveledIds) has its one plan at every level.</summary>
        public static MarkingPlan PlanOf(GuideTemplate template, string spec, int level) =>
            template == null ? null : PlanOf(template, spec, true, Array.IndexOf(LongReachIds, template.Id) >= 0 || Array.IndexOf(LeveledIds, template.Id) >= 0 ? level : BarrierLod.Full);

        public static MarkingPlan PlanOf(SignDef def, string spec, int level) => def == null ? null : PlanOf(def.Template, spec, level);

        /// <summary>
        /// The plan PlanOf would give at the full level when it is made and kept already (true, the plan in plan); true with no plan for a template that has none; false when making it would take time (the
        /// caller has a worker make it: WarmSet). Never makes a plan.
        /// </summary>
        public static bool TryKept(SignDef def, string spec, out MarkingPlan plan)
        {
            plan = null;
            GuideTemplate template = def?.Template;
            if (template == null || template.Marking == null)
                return true;
            var raw = (template.Id, spec ?? "", PlanVersion(SurfaceNormals.Enabled, BarrierLod.Full));
            lock (s_Plans)
                return s_Plans.TryGet(raw, out plan);
        }

        /// <summary>The version of a plan's cache key: the font's, whether the paint takes the ground's normals (SurfaceNormals, an experiment can change it while the game runs), and the level (four to a version).</summary>
        internal static int PlanVersion(bool surfaceNormals, int level) => (MarkingLetters.Version * 2 + (surfaceNormals ? 1 : 0)) * 4 + level;

        private static MarkingPlan PlanOf(GuideTemplate template, string spec, bool keep, int level)
        {
            if (template == null || template.Marking == null)
                return null;
            // The written settings as they are first: a placed marking's are already in the order and form the parser writes, and parsing them (400 characters of path) is most of the cost of a look-up.
            // The level is in the version of the key: four to a version.
            bool normals = SurfaceNormals.Enabled;
            int version = PlanVersion(normals, level);
            var raw = (template.Id, spec ?? "", version);
            lock (s_Plans)
            {
                if (s_Plans.TryGet(raw, out MarkingPlan quick))
                    return quick;
            }

            System.Threading.Interlocked.Increment(ref s_Parsed);
            GuideSpec parsed = template.Parse(spec);
            var key = (template.Id, parsed.ToString(), version);
            lock (s_Plans)
            {
                if (s_Plans.TryGet(key, out MarkingPlan hit))
                {
                    if (!key.Equals(raw))
                        s_Plans.Set(raw, hit);
                    return hit;
                }
            }

            MarkingPlan plan;
            try
            {
                t_Level = level;
                plan = template.Marking(parsed) ?? new MarkingPlan();
                plan.Bake((float)parsed.Number("g", 0.0), GroundGrid.Parse(parsed.Get(GroundGrid.Key)), MarkingBend.Decode(parsed.Get(MarkingBend.Key)), normals);
            }
            catch (Exception)
            {
                plan = new MarkingPlan();
            }
            finally
            {
                t_Level = BarrierLod.Full;
            }

            if (!keep)
                return plan;
            lock (s_Plans)
            {
                if (s_Plans.TryGet(key, out MarkingPlan other))
                    return other;
                s_Plans.Set(key, plan);
                if (!key.Equals(raw))
                    s_Plans.Set(raw, plan);
                return plan;
            }
        }

        public static MarkingPlan PlanOf(SignDef def, string spec) => def == null ? null : PlanOf(def.Template, spec);

        public static MarkingPlan PlanOf(SignDef def, string spec, bool keep) => def == null ? null : PlanOf(def.Template, spec, keep);

        /// <summary>Forgets the kept plans (a city is left, or the font changed).</summary>
        public static void ClearCaches()
        {
            lock (s_Plans)
                s_Plans.Clear();
        }

        /// <summary>
        /// Whether a marking or a thing put down with one click is laid on the ground under it (GroundGrid): all of them but the two that stand upright on a plinth (the gate and the booth: they would lean
        /// with the ground) and the areas that are fitted to the road on their own (AreaFit).
        /// </summary>
        public static bool Drapes(string id) => id != GateId && id != BoothId && !IsFittedArea(id);

        /// <summary>
        /// The settings of a placed item without what belongs to the place it was put (0.12.50, picking it to place more of the same): the ground under it, its grade, a run's path and its place in the
        /// whole line, a bent marking's line and drawn length, an area's outline and its clearing's heights, the automatic marking's record. What remains is the player's choice.
        /// </summary>
        public static string Unplaced(GuideTemplate template, string spec)
        {
            GuideSpec s = template.Parse(spec);
            foreach (string key in new[] { GroundGrid.Key, "g", RunPath.Key, PhaseKey, TotalKey, MarkingBend.Key, MarkingBend.FillKey, MarkingPolygon.Key, ClearElevation.Key })
            {
                GuideField field = template.FieldOf(key);
                if (field != null)
                    s = s.With(key, field.Default);
            }

            return template.Id == AutoMarking.Id ? AutoMarking.DraftOf(s).ToString() : s.ToString();
        }

        /// <summary>The settings with the heights of the ground under the item set (empty: none, flat as before); settings that have no such field are returned as they are.</summary>
        public static string WithGround(GuideTemplate template, string spec, GroundGrid ground)
        {
            GuideSpec parsed = template.Parse(spec);
            return template.FieldOf(GroundGrid.Key) == null ? parsed.ToString() : parsed.With(GroundGrid.Key, ground == null || ground.Flat ? "" : ground.ToString()).ToString();
        }

        /// <summary>The settings with the grade of the road set (rise over run along the way the traffic runs).</summary>
        public static string WithGrade(GuideTemplate template, string spec, float grade)
        {
            GuideSpec parsed = template.Parse(spec);
            return template.FieldOf("g") == null ? parsed.ToString() : parsed.With("g", Math.Round(grade, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)).ToString();
        }

        /// <summary>The path of a run's settings as they are written (empty for settings that have none).</summary>
        public static string PathOf(GuideTemplate template, string spec) => template == null || template.FieldOf(RunPath.Key) == null ? "" : template.Parse(spec ?? "").Get(RunPath.Key);

        /// <summary>
        /// How many characters the path of one item of a run may have with these settings (0.12.99; the owner, 5 Nov: a long wall had gaps of 2 m at the join of every second item). The settings are
        /// written in at most GuideSpec.MaxBytes; a text over it loses the end of the longest field (GuideSpec.Build), the path, in the middle of its last chord. What the path may take is what the others
        /// leave: the other fields as they are written, the phase and the whole length at their widest (seven digits), the key of the path, and a margin for what an edit adds (a longer name of a
        /// colour: the player edits a wall that is placed). At most MarkingRuns.MaxPathChars, at least MarkingRuns.MinPathChars; settings with no path give the most.
        /// </summary>
        public static int PathBudget(GuideTemplate template, string spec)
        {
            if (template == null || template.FieldOf(RunPath.Key) == null)
                return MarkingRuns.MaxPathChars;
            GuideSpec parsed = template.Parse(spec ?? "").With(RunPath.Key, "");
            if (template.FieldOf(PhaseKey) != null)
                parsed = parsed.With(PhaseKey, "9999999");
            if (template.FieldOf(TotalKey) != null)
                parsed = parsed.With(TotalKey, "9999999");
            string text = parsed.ToString();
            // The key of the path and its separator count when the empty path is not written (a field is left out while it has its default); when it is written they are in the text already.
            int others = System.Text.Encoding.UTF8.GetByteCount(text) + (text.StartsWith(RunPath.Key + "=", StringComparison.Ordinal) || text.Contains(";" + RunPath.Key + "=") ? 0 : RunPath.Key.Length + 2);
            return BudgetFor(others);
        }

        /// <summary>The room for a path when the other settings, the key of the path and its separator take this many bytes: what is left of the limit less the room for an edit, held between the floor and the old figure.</summary>
        public static int BudgetFor(int otherBytes) => Math.Max(MarkingRuns.MinPathChars, Math.Min(MarkingRuns.MaxPathChars, GuideSpec.MaxBytes - otherBytes - EditRoom));

        /// <summary>The bytes of the settings kept free beside the path for what editing a placed item adds (PathBudget).</summary>
        public const int EditRoom = 30;

        /// <summary>The settings with the path of a run set; settings that have no path are returned as they are.</summary>
        public static string WithPath(GuideTemplate template, string spec, string path)
        {
            GuideSpec parsed = template.Parse(spec);
            return template.FieldOf(RunPath.Key) == null ? parsed.ToString() : parsed.With(RunPath.Key, path ?? "").ToString();
        }

        /// <summary>The key of the distance (cm) along the whole line where a piece of a run starts, and of the whole line's length (cm; 0: the piece is the whole line).</summary>
        public const string PhaseKey = "ph", TotalKey = "tl";

        /// <summary>The length of the whole line a piece of a run belongs to (m); 0 when it is not known (the piece is then the whole line).</summary>
        private static float Total(GuideSpec s) => F(s, TotalKey, 0f, 0f, 1e6f) / 100f;

        /// <summary>The length of a piece's own path (m), as its settings say.</summary>
        private static float PieceLength(GuideSpec s)
        {
            List<Vec3> points = RunPath.Decode(s.Get(RunPath.Key));
            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += Vec3.DistanceXZ(points[i - 1], points[i]);
            return points.Count < 2 ? 12f : length;
        }

        /// <summary>Whether a piece of a run is where its whole line starts, and where it ends.</summary>
        private static void Ends(GuideSpec s, out bool first, out bool last)
        {
            float phase = Phase(s), total = Total(s);
            first = phase <= 0.01f;
            last = total <= 0f || phase + PieceLength(s) >= total - 0.05f;
        }

        /// <summary>Where a piece of a run starts along the whole line (m); 0 for a marking that is not a piece of a line.</summary>
        private static float Phase(GuideSpec s) => F(s, PhaseKey, 0f, 0f, 1e6f) / 100f;

        /// <summary>The settings of one piece of a run: its path, and where along the whole line it starts (m), so that a dashed line is not begun again at every piece.</summary>
        public static string WithRun(GuideTemplate template, string spec, string path, float startMetres, float totalMetres = 0f)
        {
            GuideSpec parsed = template.Parse(WithPath(template, spec, path));
            if (template.FieldOf(PhaseKey) == null || float.IsNaN(startMetres) || float.IsInfinity(startMetres))
                return parsed.ToString();
            long cm = (long)Math.Round(Math.Max(0f, Math.Min(10000f, startMetres)) * 100f);
            parsed = parsed.With(PhaseKey, cm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (template.FieldOf(TotalKey) != null && !float.IsNaN(totalMetres) && !float.IsInfinity(totalMetres) && totalMetres > 0f)
                parsed = parsed.With(TotalKey, ((long)Math.Round(Math.Min(10000f, totalMetres) * 100f)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return parsed.ToString();
        }

        /// <summary>The settings with the start of a piece along the whole line and the line's length set again (cm), for a piece the follow found longer or shorter pieces before it; settings that have no phase are returned as they are.</summary>
        public static string WithPhase(GuideTemplate template, string spec, long phaseCm, long totalCm)
        {
            GuideSpec parsed = template.Parse(spec);
            if (template.FieldOf(PhaseKey) == null || phaseCm < 0)
                return parsed.ToString();
            parsed = parsed.With(PhaseKey, phaseCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (template.FieldOf(TotalKey) != null && totalCm > 0)
                parsed = parsed.With(TotalKey, totalCm.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return parsed.ToString();
        }

        /// <summary>The settings of an area marking with its outline (MarkingPolygon, in the item's frame); for a gore, split is the index of the main side's far corner, blunt whether the sides start apart.</summary>
        public static string WithPolygon(GuideTemplate template, string spec, string polygon, int split = -1, bool blunt = false)
        {
            GuideSpec parsed = template.Parse(spec);
            if (template.FieldOf(MarkingPolygon.Key) == null)
                return parsed.ToString();
            parsed = parsed.With(MarkingPolygon.Key, polygon ?? "");
            if (template.FieldOf(SplitKey) != null)
                parsed = parsed.With(SplitKey, split < 0 ? "" : split.ToString(System.Globalization.CultureInfo.InvariantCulture) + (blunt ? "b" : ""));
            return parsed.ToString();
        }

        /// <summary>The key of a gore outline's split: the index of the corner where the main side ends ("12"; "12b" when the two sides start apart, a blunt nose).</summary>
        public const string SplitKey = "split";

        /// <summary>The two sides of a gore outline from its settings (both from the nose to the far end); false when it has none or it is damaged.</summary>
        public static bool GoreSides(GuideSpec s, out List<Vec3> main, out List<Vec3> ramp)
        {
            main = null;
            ramp = null;
            List<Vec3> polygon = MarkingPolygon.Decode(s.Get(MarkingPolygon.Key));
            string split = s.Get(SplitKey) ?? "";
            bool blunt = split.EndsWith("b", StringComparison.Ordinal);
            if (polygon.Count < 3 || !int.TryParse(blunt ? split.Substring(0, split.Length - 1) : split, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int k)
                || k < 1 || k > polygon.Count - (blunt ? 2 : 1))
                return false;
            main = polygon.GetRange(0, k + 1);
            ramp = new List<Vec3>();
            if (!blunt)
                ramp.Add(polygon[0]);
            for (int i = polygon.Count - 1; i > k; i--)
                ramp.Add(polygon[i]);
            return ramp.Count >= 2;
        }

        /// <summary>Whether a template is for a marking that runs along a road (it has a path).</summary>
        public static bool IsRun(GuideTemplate template) => template != null && template.FieldOf(RunPath.Key) != null;

        /// <summary>
        /// A line drawn inside a junction along the turn its traffic makes (0.12.43): the junction guide line (线8) and the left-turn waiting area's lines (线7). Started with the cursor on a junction, or
        /// just before one, it follows one of the junction's lanes; every other line goes along the road there as elsewhere (until 0.12.43 every line and barrier started over a junction's part, or a
        /// plain node's, followed a junction lane).
        /// </summary>
        public static bool FollowsJunctionLanes(string id) => id == "mk.line.junction" || id == "mk.line.waiting";

        /// <summary>A line painted on a kerb's face and top (线48, 线50: MarkingKerb.Line); its path is the foot of the face.</summary>
        public static bool IsKerbLine(string id) => id != null && id.StartsWith("mk.kerb.", StringComparison.Ordinal);

        /// <summary>The arrows' size field says "auto": the size of the road's design speed (5.13.4) is put in its place; the settings of a marking with no size field are returned as they are.</summary>
        public static string ResolveSize(GuideTemplate template, string spec, float kmh, bool known)
        {
            GuideSpec parsed = template.Parse(spec);
            // The design speed of a width transition (7.1) is the road's when it is left at 0.
            if (template.FieldOf("kmh") != null && parsed.Number("kmh", 0.0) <= 0.0)
                parsed = parsed.With("kmh", Math.Round(known && kmh > 0f ? kmh : 60f).ToString("0", System.Globalization.CultureInfo.InvariantCulture));

            // The figures of a speed limit (线34) are sized by the band of design speed (Table 3): "auto" takes the road's.
            if (template.FieldOf("band") != null && parsed.Is("band", "auto"))
            {
                SpeedBand band = known ? MarkingSpeed.BandOf(kmh) : SpeedBand.Mid;
                parsed = parsed.With("band", band == SpeedBand.High ? "high" : band == SpeedBand.Low ? "low" : "mid");
            }

            if (template.FieldOf("size") == null || !parsed.Is("size", "auto"))
                return parsed.ToString();
            ArrowSize size = known ? RoadArrows.SizeFor(kmh) : ArrowSize.Line31;
            return parsed.With("size", size == ArrowSize.Line30 ? "30" : size == ArrowSize.Line32 ? "32" : "31").ToString();
        }

        // ------------------------------------------------------------------ helpers

        private static GuideChoice C(string value, string zh, string en) => new GuideChoice(value, zh, en);

        private static GuideBoard Stand(GuideSpec spec)
        {
            var board = new GuideBoard { H = 0.1f };
            board.Parts.Add(new GuidePart { X = 0f, Y = 0f, W = 0.3f, H = 0.3f, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = 0.15f });
            return board;
        }

        private static float F(GuideSpec s, string key, float fallback, float min, float max)
        {
            double v = s.Number(key, fallback);
            if (double.IsNaN(v) || double.IsInfinity(v))
                v = fallback;
            return (float)Math.Max(min, Math.Min(max, v));
        }

        private static int I(GuideSpec s, string key, int fallback, int min, int max) => (int)Math.Round(F(s, key, fallback, min, max));

        private static MarkingPlan Plan(params MarkingShape[] shapes)
        {
            var plan = new MarkingPlan();
            foreach (MarkingShape s in shapes)
            {
                if (s != null)
                    plan.Shapes.Add(s);
            }

            return plan;
        }

        private static MarkingPlan Plan(IEnumerable<MarkingShape> shapes)
        {
            var plan = new MarkingPlan();
            foreach (MarkingShape s in shapes)
            {
                if (s != null)
                    plan.Shapes.Add(s);
            }

            return plan;
        }

        /// <summary>The road word marking (5.14.1).</summary>
        public const string WordId = "mk.text";

        /// <summary>The custom text on the ground (0.12.22; the owner: "地面中加入自定义地面文字"): not a marking of the standard (5.14.1's words are WordId).</summary>
        public const string FreeTextId = "mk.text.free";

        /// <summary>
        /// The custom text: one or two lines in the colour, the character height and the shape chosen. In a row the characters read from left to right with their tops to +Z (the far side for the one who
        /// reads it), the second line under the first; one under another they read from near to far as 5.14.1's words do, the second line after the first. Letters and figures keep the font's proportions.
        /// </summary>
        private static MarkingShape FreeText(GuideSpec s)
        {
            MarkingColour colour = s.Is("colour", "y") ? MarkingColour.Yellow : s.Is("colour", "r") ? MarkingColour.Red : s.Is("colour", "k") ? MarkingColour.Black : MarkingColour.White;
            var shape = new MarkingShape { Colour = colour };
            string first = (s.Get("text") ?? "").Trim(), second = (s.Get("text2") ?? "").Trim();
            if (first.Length + second.Length == 0)
                first = "文字";
            float h = F(s, "h", 150f, 20f, 2000f) / 100f;
            float em = h * (s.Is("ratio", "2") ? 0.5f : s.Is("ratio", "3") ? 1f / 3f : 1f);
            float gap = h * (s.Is("gap", "tight") ? 0.05f : s.Is("gap", "wide") ? 0.4f : 0.15f), lineGap = h * 0.5f;
            var lines = new List<string>();
            foreach (string line in new[] { first, second })
            {
                if (line.Length > 0)
                    lines.Add(line);
            }

            if (s.Is("read", "col"))
            {
                // A line of letters and figures only (a time, a number) lies across in one row, as the figures of a time under 公交 do (6.9.1); a line with Chinese is one character after another.
                var stacked = lines.ConvertAll(line => Array.Exists(line.ToCharArray(), MarkingLetters.IsIdeograph));
                float total = (lines.Count - 1) * lineGap;
                for (int l = 0; l < lines.Count; l++)
                    total += stacked[l] ? lines[l].Length * h + (lines[l].Length - 1) * gap : h;
                float z = -total * 0.5f;
                for (int l = 0; l < lines.Count; l++)
                {
                    if (!stacked[l])
                    {
                        Row(shape, lines[l], z + h * 0.5f, em, h, gap);
                        z += h + lineGap;
                        continue;
                    }

                    foreach (char c in lines[l])
                    {
                        shape.Texts.Add(new MarkingText { Text = c.ToString(), X = 0f, Z = z + h * 0.5f, Width = em * MarkingLetters.AdvanceOf(c), Length = h, Proportional = true });
                        z += h + gap;
                    }

                    z += lineGap - gap;
                }

                return shape;
            }

            float rows = lines.Count * h + (lines.Count - 1) * lineGap;
            for (int l = 0; l < lines.Count; l++)
                Row(shape, lines[l], rows * 0.5f - h * 0.5f - l * (h + lineGap), em, h, gap);
            return shape;
        }

        /// <summary>
        /// A line of the custom text from left to right, centred across at z: each character as wide as its advance in an em of em metres; gap between two characters when one of them is Chinese (the
        /// advance of a letter or a figure has its own spacing).
        /// </summary>
        private static void Row(MarkingShape shape, string line, float z, float em, float h, float gap)
        {
            float GapAfter(int i) => i + 1 < line.Length && (MarkingLetters.IsIdeograph(line[i]) || MarkingLetters.IsIdeograph(line[i + 1])) ? gap : 0f;
            float width = 0f;
            for (int i = 0; i < line.Length; i++)
                width += em * MarkingLetters.AdvanceOf(line[i]) + GapAfter(i);
            float x = -width * 0.5f;
            for (int i = 0; i < line.Length; i++)
            {
                float w = em * MarkingLetters.AdvanceOf(line[i]);
                if (line[i] != ' ')
                    shape.Texts.Add(new MarkingText { Text = line[i].ToString(), X = x + w * 0.5f, Z = z, Width = w, Length = h, Proportional = true });
                x += w + GapAfter(i);
            }
        }

        /// <summary>A word on the road: its characters one after the other along the traffic from the item's origin, the first the nearest, white; the band and the place in it as Table 3 sizes them.</summary>
        private static MarkingShape Word(GuideSpec s)
        {
            var shape = new MarkingShape { Colour = MarkingColour.White };
            string text = (s.Get("text") ?? "").Trim();
            if (text.Length == 0)
                text = "公交";
            SpeedBand band = s.Is("band", "low") ? SpeedBand.Low : s.Is("band", "high") ? SpeedBand.High : SpeedBand.Mid;
            float position = s.Is("scale", "large") ? 1f : s.Is("scale", "mid") ? 0.5f : 0f;
            float h = MarkingSpeed.Height(band, position), gap = MarkingSpeed.Spacing(band, position), w = h / 3f;
            // The hours first (Fig. 87 reads 7-8, 17-18, then 公 交 from near to far): a row of figures each, its cells 0.4 of a character's width, as high as the characters.
            float z = 0f;
            foreach (string hours in (s.Get("time") ?? "").Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                shape.Texts.Add(new MarkingText { Text = hours, X = 0f, Z = z + h * 0.5f, Width = 0.4f * w * hours.Length, Length = h });
                z += h + gap;
            }

            float total = text.Length * h + (text.Length - 1) * gap;
            shape.Texts.AddRange(MarkingFigures.BayWords(text, new Vec3(0f, 0f, z + total * 0.5f), w, h, gap));
            return shape;
        }

        /// <summary>The picture of the automatic marking (it is never an item): an approach of two lanes, a double yellow line, the lane line dashed and then a guide lane line, two arrows and the stop line.</summary>
        /// <summary>The colours the approaches of a chosen stretch are ringed in on the map, by rank (PlacementTool.kApproachColours; none is the overlay's orange, red or light blue).</summary>
        public static readonly string[] ApproachColoursZh = { "绿", "粉", "黄", "紫" }, ApproachColoursEn = { "green", "pink", "yellow", "purple" };

        private static bool ShowsApproach(GuideSpec s, int k) => s.Flag(AutoMarking.EndsKey) && s.Number(AutoMarking.ApproachesKey, 0) > k;

        /// <summary>0.12.44: the guide lane lines' length of one approach of a chosen stretch (empty: the stretch's; 0: none), shown when the stretch has it.</summary>
        private static GuideField ApproachGuide(int k) => GuideField.Digits(AutoMarking.ApproachGuideKey(k), $"进口{k + 1}（{ApproachColoursZh[k]}圈）导向线 m",
            $"Approach {k + 1} ({ApproachColoursEn[k]}): guides, m", "", 3, s => ShowsApproach(s, k));

        private static GuideField ApproachArrows(int k) => GuideField.Choice(AutoMarking.ApproachArrowsKey(k), $"进口{k + 1}（{ApproachColoursZh[k]}圈）导向箭头", $"Approach {k + 1} ({ApproachColoursEn[k]}): arrows", "",
            new[] { C("", "同上", "As above"), C("1", "画", "Draw"), C("0", "不画", "None") }, s => ShowsApproach(s, k));

        private static MarkingPlan AutoPicture(GuideSpec s)
        {
            // A stretch's record (0.12.44) draws nothing: it only keeps the settings the stretch was marked with.
            if (s.Flag(AutoMarking.RecordKey))
                return new MarkingPlan();
            List<Vec3> Path(float x, float z0, float z1) => new List<Vec3> { new Vec3(x, 0f, z0), new Vec3(x, 0f, z1) };
            var stop = new MarkingShape { Colour = MarkingColour.White };
            stop.Mesh.Append(RoadMarkings.StopLine(7f, 0.3f).Mesh, new Vec3(0f, 0f, 16.6f), 0f);
            var arrows = new MarkingShape { Colour = MarkingColour.White };
            arrows.Mesh.Append(RoadArrows.Shape(ArrowKind.StraightLeft, ArrowSize.Line30).Mesh, new Vec3(-1.75f, 0f, 12.3f), 0f);
            arrows.Mesh.Append(RoadArrows.Shape(ArrowKind.StraightRight, ArrowSize.Line30).Mesh, new Vec3(1.75f, 0f, 12.3f), 0f);
            var shapes = new List<MarkingShape>(MarkingLines.DoubleYellow(Path(-3.6f, 0f, 16.45f), 0.1f, 0.15f))
            {
                RoadMarkings.Dashed(Path(0f, 0f, 9.5f), RoadMarkings.LaneLineSlow(), 0f),
                MarkingLines.GuideLaneLine(Path(0f, 9.5f, 16.45f)),
                MarkingLines.EdgeLine(Path(3.6f, 0f, 16.45f), false, false),
                stop,
                arrows
            };
            return Plan(shapes);
        }

        /// <summary>The plan of an area that clears the game's markings: no paint, only its outline (the tool draws it, the picture shows it); a 12 x 6 m rectangle when it has none yet (the thumbnail).</summary>
        private static MarkingPlan ClearPlan(GuideSpec s)
        {
            List<Vec3> outline = ClearElevation.Decode(s);
            if (outline.Count < 3)
                outline = MarkingFills.Rectangle(-6f, -3f, 6f, 3f);
            var plan = new MarkingPlan();
            plan.Outlines.Add(outline);
            return plan;
        }

        /// <summary>The setting of a marking that spans lanes (a stop line, a crosswalk): when it is placed on a road, its length across and its lanes are taken from the road's lanes (MarkingSnap.Fit).</summary>
        public const string FitKey = "fit";

        /// <summary>
        /// The settings of a marking placed at a spot with the span and lanes the road gave (MarkingSnap.Fit): the span when the template has one and fitting is on and lanes were read, the lane count when it
        /// has a choice of lanes (clamped to its choices). The settings as they are otherwise.
        /// </summary>
        public static string WithFit(GuideTemplate template, string spec, float span, int lanes)
        {
            GuideSpec parsed = template.Parse(spec);
            if (template.FieldOf(FitKey) == null || !parsed.Flag(FitKey) || !(span > 0.5f))
                return parsed.ToString();
            parsed = parsed.With("span", Math.Round(Math.Min(60f, span), 1).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
            GuideField laneField = template.FieldOf("lanes");
            if (laneField != null && lanes > 0)
                parsed = parsed.With("lanes", Math.Max(1, Math.Min(6, lanes)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return parsed.ToString();
        }

        /// <summary>The left-turn waiting area made from the junction (WaitingArea) and the key of its axis (a RunPath; not "path", so it is not a run).</summary>
        public const string WaitingId = "mk.waiting", WaitingAxisKey = "axis";

        /// <summary>The island (6.6) and the box grid (线57): areas the tool draws corner by corner (0.12.3: the grid too).</summary>
        public const string ChannelId = "mk.channel", GridId = "mk.grid";

        public static bool IsDrawnArea(string id) => id == ChannelId || id == GridId;

        /// <summary>
        /// The setting of the area markings (0.12.4) that cuts their paint to the road's surface and lays it on it (AreaFit): on by default, so an item of 0.12.3 or before, which has no such setting, is
        /// fitted too.
        /// </summary>
        public const string AreaFitKey = "surf";

        public const string SimpleGridId = "mk.grid.simple", GoreId = "mk.gore";

        /// <summary>The markings that are areas painted on the road: an island, a box grid, a simplified grid, a gore.</summary>
        public static bool IsFittedArea(string id) => id == ChannelId || id == GridId || id == SimpleGridId || id == GoreId;

        /// <summary>Whether an item of a marking is to be fitted to the road: an area marking whose setting is on.</summary>
        public static bool WantsFit(SignDef def, string spec)
        {
            if (def == null || !def.IsMarking || !IsFittedArea(def.Id) || def.Template == null)
                return false;
            return def.Template.Parse(spec ?? "").Flag(AreaFitKey);
        }

        /// <summary>The path of a run in the settings; a straight 12 m when it has none (what the preview and the thumbnail show).</summary>
        private static List<Vec3> Run(GuideSpec s, float fallbackLength = 12f)
        {
            List<Vec3> points = RunPath.Decode(s.Get(RunPath.Key));
            if (points.Count < 2)
                points = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, fallbackLength) };
            return points;
        }

        /// <summary>The path of a run of small things (markers, delineators) when it has none: 3 m, so that the picture of one shows it and not a speck on a long road.</summary>
        private static List<Vec3> ShortRun(GuideSpec s) => Run(s, 3f);

        /// <summary>
        /// The path of a barrier (8 m when it has none: two posts of a guardrail in its picture), moved away from the traffic by its distance from the drawn line (a guardrail stands behind the edge line,
        /// the line is where the player clicked).
        /// </summary>
        private static List<Vec3> BarrierRun(GuideSpec s)
        {
            List<Vec3> line = Run(s, 8f);
            float away = s.Is("where", "m") ? 0f : F(s, "off", 0f, 0f, 500f) / 100f;
            return away > 0f ? MarkingFills.Offset(line, s.Is("where", "l") ? away : -away) : line;
        }

        /// <summary>How a marking of one piece follows the road (0.12.49, MarkingBend): not at all, bent along it where it is clicked, or that or drawn along it from a start to an end.</summary>
        private enum Bending
        {
            None,
            Click,
            Drawn
        }

        private static readonly GuideChoice[] kPlace =
        {
            C("click", "单击放置（贴路缘或车道线，随弯道）", "One click, bent along the road"), C(MarkingBend.Drawn, "沿线绘制（单击起点和终点）", "Drawn from a start to an end")
        };

        /// <summary>Whether a template is bent along the road where it is placed (it has the bend's field).</summary>
        public static bool Bends(GuideTemplate template) => template != null && template.FieldOf(MarkingBend.Key) != null;

        /// <summary>Whether a template's marking is drawn from a start to an end as its settings are now.</summary>
        public static bool DrawsAlong(GuideTemplate template, GuideSpec s) => template.FieldOf(MarkingBend.PlaceKey) != null && s.Is(MarkingBend.PlaceKey, MarkingBend.Drawn);

        /// <summary>The length (m) a marking was drawn along the road (MarkingBend.FillKey), NaN for one placed with one click or not drawn yet.</summary>
        private static float DrawnLength(GuideSpec s)
        {
            double cm = s.Number(MarkingBend.FillKey, 0.0);
            return s.Is(MarkingBend.PlaceKey, MarkingBend.Drawn) && cm >= 100.0 && cm < 1e6 ? (float)(cm / 100.0) : float.NaN;
        }

        /// <summary>A field that a drawn length takes the place of is shown only for a marking placed with one click.</summary>
        private static bool NotDrawn(GuideSpec s) => !s.Is(MarkingBend.PlaceKey, MarkingBend.Drawn);

        /// <summary>
        /// Where the road's kerb is in a bent marking's frame (x, m): the bays and the no-parking lines lie along it (0), a bus stop's lane-side line is its width from it; NaN for one that lies in a lane
        /// (the distance chevrons).
        /// </summary>
        public static float KerbAt(GuideTemplate template, GuideSpec s) =>
            template.Id == "mk.chevron" ? float.NaN : template.Id == "mk.busbay" || template.Id == "mk.kerbstop" ? F(s, "depth", 3f, 2.5f, 5f) : 0f;

        /// <summary>The most bays a row drawn along the road has.</summary>
        public const int MaxDrawnBays = 40;

        /// <summary>The bays of a row: count, or as many of the pitch as the drawn length holds past the far one's overhang along the road (an angled bay leans on by depth x cos of its angle).</summary>
        private static int Bays(GuideSpec s, int count, float pitch, float overhang = 0f)
        {
            float drawn = DrawnLength(s);
            return float.IsNaN(drawn) ? count : Math.Max(1, Math.Min(MaxDrawnBays, (int)Math.Floor((drawn - overhang) / pitch + 1e-3f)));
        }

        /// <summary>A length along the road: the drawn one, or the field's.</summary>
        private static float Along(GuideSpec s, string key, float fallback, float min, float max)
        {
            float drawn = DrawnLength(s);
            return float.IsNaN(drawn) ? F(s, key, fallback, min, max) : Math.Max(min, Math.Min(max, drawn));
        }

        private static GuideTemplate T(string id, string code, string zh, string en, string pinyin, string[] keywords, GuideField[] fields, Func<GuideSpec, MarkingPlan> make, bool run = false, Func<GuideSpec, bool, string> note = null, string group = "marking",
            Bending bend = Bending.None)
        {
            var all = new List<GuideField>(fields);
            if (bend == Bending.Drawn)
            {
                all.Add(GuideField.Choice(MarkingBend.PlaceKey, "放置方式", "Placing", "click", kPlace).In("放置", "Placing"));
                all.Add(GuideField.Digits(MarkingBend.FillKey, "（内部）沿线长度（cm）", "(internal) drawn length (cm)", "", 6, s => false));
            }

            if (bend != Bending.None)
                all.Add(GuideField.Text(MarkingBend.Key, "（内部）沿路弯曲", "(internal) bend", "", 200, s => false));
            all.Add(GuideField.Text("g", "坡度", "Grade", "0", 6, s => false));
            // A barrier's setting for the junctions it is drawn across (0.12.16): used when it is placed.
            if (group == BarrierGroup && (run || id == BarrierAuto.Id))
                all.Insert(fields.Length, GuideField.Choice(BarrierAuto.JunctionKey, "路口（放置时）", "At junctions (placing)", "side", kJunctionMode));
            // and for the two ends of the line it is drawn along (0.12.73): to the node, or where the lanes end. Not shown for an item that is placed: its path is baked.
            if (group == BarrierGroup && run)
            {
                GuideField ends = GuideField.Choice(BarrierAuto.EndsKey, "线端（放置时）", "Ends of the line (placing)", "j", kLineEnds);
                ends.DraftOnly = true;
                all.Insert(fields.Length + 1, ends);
            }
            // A marking or a thing put down with one click: the heights of the ground under it, read where it is placed (0.12.30, GroundGrid).
            if (!run)
                all.Add(GuideField.Text(GroundGrid.Key, "地面高度", "Ground", "", 200, s => false));
            if (run)
            {
                all.Add(GuideField.Text(RunPath.Key, "路径", "Path", "", 400, s => false));
                // Where along the whole line this piece starts (cm): a dashed line goes on in the pattern the piece before it ended in; and how long the whole line is (cm), so that a piece knows whether it is
                // the first or the last (an end of a strip is edged, the solid part of a thick line is at one end).
                all.Add(GuideField.Text(PhaseKey, "相位", "Phase", "0", 7, s => false));
                all.Add(GuideField.Text(TotalKey, "全长", "Whole length", "0", 7, s => false));
            }
            return new GuideTemplate
            {
                Id = id,
                Code = code,
                Zh = zh,
                En = en,
                Group = group,
                Keywords = keywords,
                Pinyin = pinyin,
                Standalone = true,
                Fields = all.ToArray(),
                Layout = Stand,
                MarkingNote = note,
                Marking = make
            };
        }

        private static string[] W(params string[] words) => words;

        /// <summary>The group of the road barriers (0.12.13): placed like a marking along a road, drawn as solid bodies, decoration only.</summary>
        public const string BarrierGroup = "barrier";

        public const string WBeamId = "bar.wbeam", ConcreteBarrierId = "bar.concrete", CityFenceId = "bar.fence", ConeId = "bar.cone";

        /// <summary>The barriers of the second stage (0.12.14).</summary>
        public const string CableId = "bar.cable", WarningPostId = "bar.post", WarningBlockId = "bar.block", BollardId = "bar.bollard", WaterBarrierId = "bar.water", CrashDrumId = "bar.drum", HoardingId = "bar.hoarding";

        /// <summary>The things of a toll plaza (0.12.26, TollProps): put where the cursor is, not along a road.</summary>
        public const string GateId = "bar.gate", BoothId = "bar.booth", TollIslandId = "bar.island", HumpId = "bar.hump";

        private static readonly GuideChoice[] kLineEnds = { C("j", "拉到路口", "Run on to the junction"), C("l", "到车道末端", "Stop where the lanes end") };

        private static readonly GuideChoice[] kJunctionMode = { C("side", "自动：接入侧断开", "Auto: cut where a road joins"), C("cut", "全部断开", "Cut at every junction"), C("through", "连续通过", "Go on through") };

        private static readonly GuideChoice[] kBarrierSide = { C("r", "路侧（车流在线的右侧）", "Roadside (the traffic on the line's right)"), C("l", "路侧（车流在线的左侧）", "Roadside (the traffic on the line's left)"), C("m", "中央分隔带（两面）", "Median (both sides)") };

        /// <summary>
        /// The road barriers (0.12.13, decoration: the game's traffic does not see them). The W-beam's sections are GB/T 31439.1's DB01 (two-wave, 310 mm) and DB02 (three-wave, 506 mm); the heights, the post
        /// spacing, the New Jersey section, the city fence's pattern and the cones' sizes are the usual products' and installations', not checked against a clause: the notes say so.
        /// </summary>
        private static void AddBarriers(List<GuideTemplate> list)
        {
            list.Add(T(WBeamId, "GB/T 31439", "波形梁护栏", "W-beam guardrail", "bxlhl", W("波形梁", "护栏", "护栏板", "防撞", "路侧护栏", "w-beam", "guardrail", "crash barrier"),
                new[]
                {
                    GuideField.Choice("wave", "波形", "Beam", "2", new[] { C("2", "二波（310 mm）", "Two-wave (310 mm)"), C("3", "三波（506 mm）", "Three-wave (506 mm)") }),
                    GuideField.Choice("where", "位置", "Where", "r", kBarrierSide),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "4", new[] { C("4", "4 m", "4 m"), C("2", "2 m（加密）", "2 m (dense)") }),
                    GuideField.Choice("colour", "颜色", "Colour", "metal", new[] { CMetal, C("green", "绿色（喷涂）", "Green (painted)") }),
                    GuideField.Choice("ends", "端头", "Ends", "auto", new[] { C("auto", "自动（迎车外展、顺车圆头）", "Auto (flared facing the traffic)"), C("round", "圆头式", "Round"), C("flare", "外展式", "Flared"), C("none", "无", "None") }),
                    GuideField.Flag("glare", "防眩板（中央分隔）", "Anti-glare panels (median)", false, s => s.Is("where", "m")),
                    GuideField.Choice("glaregap", "防眩板间距", "Panel spacing", "80", new[] { C("50", "50 cm", "50 cm"), C("80", "80 cm", "80 cm"), C("100", "100 cm", "100 cm") }, s => s.Is("where", "m") && s.Flag("glare")),
                    GuideField.Digits("off", "外移（cm，离开车流）", "Setback (cm, from the traffic)", "0", 3, s => !s.Is("where", "m"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    BeamEnd ends = s.Is("ends", "round") ? BeamEnd.Round : s.Is("ends", "flare") ? BeamEnd.Flare : s.Is("ends", "none") ? BeamEnd.None : BeamEnd.Auto;
                    List<Vec3> line = BarrierRun(s);
                    float spacing = s.Is("post", "2") ? 2f : 4f;
                    List<MarkingShape> shapes = Barriers.WBeam(line, s.Is("wave", "3"), spacing, s.Is("where", "l") ? -1 : 1, s.Is("where", "m"), Phase(s), first, last, ends, Total(s), Colour(s, "colour", SolidColour.Metal));
                    if (s.Is("where", "m") && s.Flag("glare"))
                        shapes.AddRange(Barriers.GlarePanels(line, s.Is("wave", "3") ? 1.0f : 0.85f, F(s, "glaregap", 80f, 50f, 100f) / 100f, spacing, Phase(s), last));
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(ConcreteBarrierId, "新泽西型", "混凝土护栏", "Concrete barrier", "hntlh", W("混凝土", "护栏", "新泽西", "中央分隔", "隔离墩", "concrete", "jersey", "median barrier"),
                new[]
                {
                    GuideField.Choice("where", "位置", "Where", "m", kBarrierSide),
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "81", 3),
                    GuideField.Flag("glare", "防眩板（中央分隔）", "Anti-glare panels (median)", false, s => s.Is("where", "m")),
                    GuideField.Choice("glaregap", "防眩板间距", "Panel spacing", "80", new[] { C("50", "50 cm", "50 cm"), C("80", "80 cm", "80 cm"), C("100", "100 cm", "100 cm") }, s => s.Is("where", "m") && s.Flag("glare")),
                    GuideField.Digits("off", "外移（cm，离开车流）", "Setback (cm, from the traffic)", "0", 3, s => !s.Is("where", "m"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    List<Vec3> line = BarrierRun(s);
                    float height = F(s, "height", 81f, 60f, 150f) / 100f;
                    List<MarkingShape> shapes = Barriers.Concrete(line, height, s.Is("where", "m"), s.Is("where", "l") ? -1 : 1, first, last);
                    if (s.Is("where", "m") && s.Flag("glare"))
                        shapes.AddRange(Barriers.GlarePanels(line, height, F(s, "glaregap", 80f, 50f, 100f) / 100f, 0f, Phase(s), last));
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(CityFenceId, "城市道路", "城市道路隔离栏", "City road fence", "csdlglh", W("隔离栏", "中央隔离", "护栏", "栏杆", "机非隔离", "fence", "median fence", "railing"),
                new[]
                {
                    GuideField.Choice("scheme", "颜色", "Colours", "bw", new[] { C("bw", "蓝白", "Blue and white"), C("w", "白色", "White"), C("yb", "黄黑", "Yellow and black") }),
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "100", 3),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "3", new[] { C("2", "2 m", "2 m"), C("3", "3 m", "3 m"), C("4", "4 m", "4 m") }),
                    GuideField.Flag("feet", "带混凝土底座", "On concrete feet", true),
                    kGapField, kGapLengthField, kGapRampField, kGapPostField
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    List<Vec3> line = BarrierRun(s);
                    var shapes = Barriers.CityFence(line, F(s, "height", 100f, 60f, 160f) / 100f, F(s, "post", 3f, 2f, 4f), s.Is("scheme", "yb") ? FenceScheme.YellowBlack : s.Is("scheme", "w") ? FenceScheme.White : FenceScheme.BlueWhite, s.Flag("feet"), Phase(s), last, Taper(s), Total(s));
                    AddGapPosts(s, line, first, last, shapes);
                    return Plan(shapes);
                },
                true,
                null,
                BarrierGroup));
            // Not an item: a brush that places the barriers of a stretch by its kind of road (BarrierAuto).
            list.Add(T(BarrierAuto.Id, "自动", "按道路类型自动布置护栏", "Barriers by the kind of road", "adlzdbzhl", W("自动", "护栏", "一键", "高速", "国道", "城市", "村道", "auto", "barriers", "guardrail"),
                new[]
                {
                    GuideField.Choice(BarrierAuto.ClassKey, "道路类型", "Kind of road", "auto", new[]
                    {
                        C("auto", "自动判断", "Automatic"), C("hw", "高速公路、快速路", "Motorway, expressway"), C("nat", "国道、省道", "National or provincial road"), C("city", "城市道路", "City road"), C("vil", "村道", "Village road")
                    }),
                    GuideField.Choice(BarrierAuto.RangeKey, "范围", "Where", "s", new[] { C("s", "这一段（到两端路口为止）", "This stretch (to its junctions)"), C("e", "只这个路段", "This segment only") }),
                    GuideField.Flag(BarrierAuto.SidesKey, "路侧（高速、国道、村道）", "Roadsides (by the kind of road)", true),
                    GuideField.Flag(BarrierAuto.MedianKey, "中央分隔（高速、国道、城市）", "Median (motorway, national, city)", true),
                    GuideField.Flag(BarrierAuto.GlareKey, "中央护栏加防眩板", "Anti-glare panels on the median", false, s => s.Flag(BarrierAuto.MedianKey))
                },
                s => Plan(Barriers.WBeam(new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 8f) }, false, 4f, 1, false, 0f, true, true)),
                false,
                null,
                BarrierGroup));
            list.Add(T(CableId, "缆索护栏", "缆索护栏", "Cable guardrail", "lslh", W("缆索", "钢索", "护栏", "cable", "wire rope", "guardrail"),
                new[]
                {
                    GuideField.Choice("cables", "缆索根数", "Cables", "3", new[] { C("3", "3 根", "3"), C("4", "4 根", "4"), C("5", "5 根", "5") }),
                    GuideField.Choice("post", "立柱间距", "Post spacing", "4", new[] { C("2", "2 m", "2 m"), C("4", "4 m", "4 m"), C("7", "7 m", "7 m") })
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(Barriers.Cable(BarrierRun(s), I(s, "cables", 3, 3, 5), F(s, "post", 4f, 2f, 7f), Phase(s), first, last, Total(s)));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(WarningPostId, "示警桩", "示警桩", "Warning posts", "sjz", W("示警桩", "警示桩", "道口桩", "村道", "warning post", "marker post"),
                new[]
                {
                    GuideField.Choice("band", "色带", "Bands", "r", new[] { C("r", "红白", "Red and white"), C("k", "黑白", "Black and white") }),
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "100", 3),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "3", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.WarningPosts(BarrierRun(s), F(s, "height", 100f, 60f, 150f) / 100f, F(s, "gap", 3f, 1f, 20f), s.Is("band", "k") ? SolidColour.Black : SolidColour.Red, Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(WarningBlockId, "示警墩", "示警墩", "Warning blocks", "sjd", W("示警墩", "警示墩", "防撞墩", "村道", "warning block", "kerb block"),
                new[]
                {
                    GuideField.Choice("scheme", "颜色", "Colours", "rw", new[] { C("rw", "红白", "Red and white"), C("yb", "黄黑", "Yellow and black") }),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "2", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.WarningBlocks(BarrierRun(s), F(s, "gap", 2f, 0.8f, 20f), s.Is("scheme", "yb"), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(BollardId, "路桩", "隔离路桩", "Bollards", "gllz", W("路桩", "隔离桩", "挡车桩", "不锈钢", "bollard", "post"),
                new[]
                {
                    GuideField.Choice("scheme", "样式", "Style", "steel", new[] { C("steel", "不锈钢（红色反光带）", "Stainless steel, red bands"), C("yb", "黄黑", "Yellow and black") }),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "1.5", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.Bollards(BarrierRun(s), F(s, "gap", 1.5f, 0.5f, 10f), s.Is("scheme", "yb"), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(WaterBarrierId, "水马", "水马", "Water-filled barriers", "sm", W("水马", "注水", "围挡", "施工", "三孔水马", "水马围挡", "water barrier", "water-filled"),
                new[]
                {
                    GuideField.Choice("style", "样式", "Style", "plain", new[] { C("plain", "普通（1.5×0.8 m）", "Plain (1.5 x 0.8 m)"), C("holes", "三孔水马（1.5×0.8 m）", "Three holes (1.5 x 0.8 m)"), C("fence", "围挡水马（1.0×1.8 m）", "Fence type (1.0 x 1.8 m)") }),
                    GuideField.Choice("scheme", "颜色", "Colours", "rw", new[] { C("rw", "红白相间", "Red and white"), C("ow", "橙白相间", "Orange and white"), C("r", "红色", "Red"), C("y", "黄色", "Yellow") })
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    WaterStyle style = s.Is("style", "holes") ? WaterStyle.ThreeHole : s.Is("style", "fence") ? WaterStyle.Fence : WaterStyle.Plain;
                    SolidColour one = s.Is("scheme", "ow") ? SolidColour.Orange : s.Is("scheme", "y") ? SolidColour.Yellow : SolidColour.Red;
                    return Plan(Barriers.WaterBarriers(BarrierRun(s), style, one, s.Is("scheme", "rw") || s.Is("scheme", "ow") ? SolidColour.White : one, Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(T(CrashDrumId, "防撞桶", "防撞桶", "Crash drums", "fzt", W("防撞桶", "防撞", "隔离桶", "crash drum", "barrel", "cushion"),
                new[]
                {
                    GuideField.Choice("scheme", "颜色", "Colours", "ow", new[] { C("ow", "橙色白带", "Orange, white bands"), C("yb", "黄色黑带", "Yellow, black bands") }),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "1", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.CrashDrums(BarrierRun(s), F(s, "gap", 1f, 0.95f, 20f), s.Is("scheme", "yb"), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            list.Add(HoardingTemplate());
            list.Add(T(ConeId, "交通锥", "交通锥", "Traffic cones", "jtz", W("路锥", "交通锥", "锥桶", "雪糕筒", "施工", "cone", "traffic cone"),
                new[]
                {
                    GuideField.Choice("height", "高度", "Height", "70", new[] { C("45", "450 mm", "450 mm"), C("70", "700 mm", "700 mm"), C("90", "900 mm", "900 mm") }),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "3", 4)
                },
                s =>
                {
                    Ends(s, out _, out bool last);
                    return Plan(Barriers.Cones(BarrierRun(s), F(s, "height", 70f, 45f, 90f) / 100f, F(s, "gap", 3f, 0.5f, 30f), Phase(s), last));
                },
                true,
                null,
                BarrierGroup));
            AddTollProps(list);
            AddCityBarriers(list);
            AddRailings(list);
            AddGreening(list);
            AddLivingWall(list);
            AddMoreBarriers(list);
            AddRoadPosts(list);
        }

        /// <summary>The lifting-arm gate, the toll booth, the toll island and the rubber hump of a toll plaza (0.12.26): stamps, one click each.</summary>
        private static void AddTollProps(List<GuideTemplate> list)
        {
            list.Add(T(GateId, "收费站", "道闸（栏杆机）", "Lifting-arm gate", "dzlgj", W("道闸", "栏杆机", "起落杆", "收费站", "闸机", "gate", "barrier gate", "boom"),
                new[]
                {
                    GuideField.Choice("length", "杆长", "Arm", "4", new[] { C("3", "3 m", "3 m"), C("3.5", "3.5 m", "3.5 m"), C("4", "4 m", "4 m"), C("4.5", "4.5 m", "4.5 m"), C("5", "5 m", "5 m"), C("6", "6 m", "6 m") }),
                    GuideField.Choice("state", "状态", "State", "down", new[] { C("down", "落杆（横在车道上）", "Down, across the lane"), C("open", "起杆（竖起）", "Open, standing up") }),
                    GuideField.Choice("arm", "杆伸向", "Arm reaches", "r", new[] { C("r", "右侧", "To the right"), C("l", "左侧", "To the left") })
                },
                s => Plan(TollProps.Gate(F(s, "length", 4f, 3f, 6f), s.Is("state", "open"), s.Is("arm", "r"))),
                false,
                null,
                BarrierGroup));
            list.Add(T(BoothId, "收费站", "收费亭", "Toll booth", "sft", W("收费亭", "收费站", "岗亭", "收费窗口", "booth", "toll booth", "kiosk"),
                new[]
                {
                    GuideField.Choice("length", "沿路长度", "Length along the road", "3", new[] { C("2.4", "2.4 m", "2.4 m"), C("3", "3 m", "3 m"), C("3.6", "3.6 m", "3.6 m"), C("4", "4 m", "4 m") }),
                    GuideField.Choice("scheme", "顶檐颜色", "Fascia", "blue", new[] { C("blue", "蓝", "Blue"), C("green", "绿", "Green"), C("red", "红", "Red") })
                },
                s => Plan(TollProps.Booth(F(s, "length", 3f, 2.4f, 4f), s.Is("scheme", "green") ? SolidColour.Green : s.Is("scheme", "red") ? SolidColour.Red : SolidColour.Blue)),
                false,
                null,
                BarrierGroup));
            list.Add(T(TollIslandId, "收费站", "收费岛", "Toll island", "sfd", W("收费岛", "收费站", "安全岛", "岛头", "island", "toll island", "plaza"),
                new[]
                {
                    GuideField.Digits("length", "长度（m）", "Length (m)", "28", 2),
                    GuideField.Choice("width", "宽度", "Width", "2.2", new[] { C("1.6", "1.6 m", "1.6 m"), C("2.2", "2.2 m", "2.2 m"), C("2.5", "2.5 m", "2.5 m"), C("3", "3 m", "3 m") })
                },
                s => Plan(TollProps.Island(F(s, "length", 28f, 4f, 60f), F(s, "width", 2.2f, 1.6f, 3.5f))),
                false,
                null,
                BarrierGroup));
            list.Add(T(HumpId, "收费站", "橡胶减速带", "Rubber speed hump", "xjjsd", W("减速带", "减速垄", "减速丘", "橡胶", "收费站", "hump", "speed bump", "rubber"),
                new[]
                {
                    GuideField.Digits("width", "宽度（m）", "Width (m)", "3.5", 4),
                    GuideField.Choice("rows", "排数", "Rows", "1", new[] { C("1", "1 排", "1"), C("2", "2 排", "2"), C("3", "3 排", "3") })
                },
                s => Plan(TollProps.Hump(F(s, "width", 3.5f, 1f, 16f), I(s, "rows", 1, 1, 3))),
                false,
                null,
                BarrierGroup));
        }

        private static readonly GuideChoice[] kStopWidths = { C("20", "20 cm", "20 cm"), C("30", "30 cm", "30 cm"), C("40", "40 cm", "40 cm") };
        private static readonly GuideChoice[] kStripe = { C("40", "40 cm", "40 cm"), C("45", "45 cm", "45 cm") };
        private static readonly GuideChoice[] kLaneLine = { C("69", "6-9 线（设计速度 ≥60 km/h）", "6-9 (design speed 60 km/h or more)"), C("39", "3-9 线（设计速度 ≥60 km/h）", "3-9 (60 km/h or more)"), C("24", "2-4 线（设计速度 <60 km/h）", "2-4 (under 60 km/h)") };
        private static readonly GuideChoice[] kLineWidth = { C("15", "15 cm", "15 cm"), C("10", "10 cm（特殊路段）", "10 cm (special roads)"), C("8", "8 cm（农村、专用道路）", "8 cm (rural, private roads)") };
        private static readonly GuideChoice[] kWidth1015 = { C("15", "15 cm", "15 cm"), C("10", "10 cm（特殊路段）", "10 cm (special roads)") };
        private static readonly GuideChoice[] kSide = { C("r", "虚线在右侧", "Dashed on the right"), C("l", "虚线在左侧", "Dashed on the left") };
        private static readonly GuideChoice[] kSize =
        {
            C("auto", "自动（按道路设计速度）", "Auto (by the road's design speed)"), C("30", "图42（≤40 km/h，3 m）", "Fig. 42 (40 km/h or less, 3 m)"), C("31", "图43（>40～<100，6 m）", "Fig. 43 (over 40, under 100 km/h, 6 m)"),
            C("32", "图44（≥100 km/h，9 m）", "Fig. 44 (100 km/h or more, 9 m)"), C("33", "图45（城市 >40～60，4.5 m）", "Fig. 45 (city, over 40 to 60 km/h, 4.5 m)")
        };

        /// <summary>A figure's size in metres from a setting in centimetres, rounded to a multiple of 10 cm (5.15.1).</summary>
        private static float Figure(GuideSpec s, string key, float fallback) => (float)Math.Round(F(s, key, fallback, 40f, 300f) / 10f) / 10f;

        private static ArrowSize SizeOf(GuideSpec s) => s.Is("size", "30") ? ArrowSize.Line30 : s.Is("size", "32") ? ArrowSize.Line32 : s.Is("size", "33") ? ArrowSize.Line33 : ArrowSize.Line31;

        private static GuideField SizeField() => GuideField.Choice("size", "导向箭头尺寸", "Arrow size", "auto", kSize);

        private static DashSpec LaneDash(GuideSpec s)
        {
            float width = s.Is("width", "8") ? 0.08f : s.Is("width", "10") ? 0.10f : 0.15f;
            bool narrow = width < 0.12f;
            DashSpec dash = s.Is("pattern", "24") ? RoadMarkings.LaneLineSlow(narrow) : RoadMarkings.LaneLineFast(s.Is("pattern", "39"), narrow);
            dash.Width = width;
            return dash;
        }

        private static List<Vec3> LaneCentres(float span, int lanes)
        {
            var list = new List<Vec3>();
            for (int i = 0; i < lanes; i++)
                list.Add(new Vec3(-span * 0.5f + (i + 0.5f) * span / lanes, 0f, 0f));
            return list;
        }

        private static List<float> LaneXs(float span, int lanes)
        {
            var list = new List<float>();
            foreach (Vec3 v in LaneCentres(span, lanes))
                list.Add(v.X);
            return list;
        }

        private static MarkingShape Turned(MarkingShape source, float yaw, Vec3 at)
        {
            var shape = new MarkingShape { Colour = source.Colour };
            shape.Mesh.Append(source.Mesh, at, yaw);
            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
            foreach (MarkingText t in source.Texts)
                shape.Texts.Add(new MarkingText { Text = t.Text, X = t.X * c + t.Z * s + at.X, Z = -t.X * s + t.Z * c + at.Z, Width = t.Width, Length = t.Length, Yaw = t.Yaw + yaw });
            return shape;
        }

        // ------------------------------------------------------------------ the catalogue

        private static GuideTemplate[] Build()
        {
            var list = new List<GuideTemplate>();

            // ---- not a marking of the standard: an area where the game's own markings are not drawn (VanillaClear, ClearMarkingsSystem), so that the player can draw the standard's.
            list.Add(T(VanillaClear.Id, "清除", "清除原版标线", "Clear the game's markings", "qcybbx", W("清除", "原版", "隐藏", "去掉", "车道线", "clear", "remove", "hide", "vanilla", "markings"),
                new[]
                {
                    GuideField.Choice(VanillaClear.ModeKey, "范围", "Where", "e", new[]
                    {
                        C("e", "这个路段", "This road segment"), C("r", "整条道路（到相交路口为止）", "The whole road (to its junctions)"),
                        C("n", "这个路口", "This junction"), C("a", "画一个范围（逐点单击）", "An area (click its corners)")
                    }, s => !HasPolygon(s)),
                    GuideField.Choice(VanillaClear.KeepKey, "清除哪些", "Which markings", "lines", new[]
                    {
                        C("lines", "沿路的线（保留停止线、人行横道）", "Lines along (keep stop lines, crosswalks)"), C("all", "全部原版标线", "All of the game's markings")
                    }),
                    GuideField.Flag(VanillaClear.RedrawKey, "清除后按国标重标", "Then mark it by the standard", false, s => AutoMarking.Offered && !HasPolygon(s) && (s.Is(VanillaClear.ModeKey, "e") || s.Is(VanillaClear.ModeKey, "r"))),
                    GuideField.Choice(VanillaClear.ScopeKey, "清除谁的标线", "Whose markings", "e", new[] { C("e", "路段", "Road segments"), C("n", "路口", "Junctions"), C("a", "全部", "All") }, s => false),
                    GuideField.Text(MarkingPolygon.Key, "轮廓", "Outline", "", 400, s => false),
                    GuideField.Text(ClearElevation.Key, "路面高度", "Surface heights", "", 110, s => false)
                },
                ClearPlan, false,
                (s, zh) => HasPolygon(s)
                    ? (zh ? "范围内的原版标线不显示，删除本条即恢复。" : "Game markings here are hidden; delete this item to restore them.")
                    : (zh ? "单击路段、路口，或逐点画出范围。" : "Click a segment or a junction, or draw an area.")));

            // ---- not a marking of the standard: a brush that marks a stretch of road by the standard (AutoMarking): it clears the game's markings and places the lines, the guide lanes, the arrows and
            // the stop lines as ordinary markings; it is never an item itself.
            list.Add(T(AutoMarking.Id, "重标", "按国标自动重标这一段", "Mark this stretch by the standard", "agbzdcb", W("自动", "重标", "国标", "重画", "车道线", "导向箭头", "导向车道", "停止线", "auto", "remark", "standard", "lane lines", "arrows"),
                new[]
                {
                    GuideField.Choice(AutoMarking.RangeKey, "路段", "Road", "s", new[] { C("s", "这一段（到两端路口）", "This stretch (to its junctions)"), C("e", "只这个路段", "This segment only") }).In("范围", "Where"),
                    GuideField.Choice(AutoMarking.KeepKey, "清除原版的", "Clear the game's", "lines", new[]
                    {
                        C("lines", "沿路的线（保留停止线、人行横道）", "Lines along (keep stop lines, crosswalks)"), C("all", "全部（并画国标停止线）", "All (and draw stop lines)")
                    }),
                    GuideField.Flag(AutoMarking.EndsKey, "画路口标线", "Mark the junctions", true).In("路口", "Junctions"),
                    GuideField.Choice(AutoMarking.GuideModeKey, "导向车道线长度", "Guide lane lines", AutoMarking.ByRoad, new[] { C(AutoMarking.ByRoad, "按道路状态", "By the road"), C("fixed", "固定长度", "A fixed length") },
                        s => s.Flag(AutoMarking.EndsKey)),
                    GuideField.Digits(AutoMarking.GuideKey, "长度（m，≥30）", "Length (m, 30 or more)", "30", 3, s => s.Flag(AutoMarking.EndsKey) && s.Is(AutoMarking.GuideModeKey, "fixed")),
                    GuideField.Choice(AutoMarking.ArrowsKey, "导向箭头", "Guide arrows", AutoMarking.ByRoad, new[] { C(AutoMarking.ByRoad, "按道路状态", "By the road"), C("1", "两组", "Two groups"), C("0", "不画", "None") },
                        s => s.Flag(AutoMarking.EndsKey)),
                    GuideField.Choice(AutoMarking.ArrowSizeKey, "箭头尺寸", "Arrow size", "auto", kSize, s => s.Flag(AutoMarking.EndsKey) && !s.Is(AutoMarking.ArrowsKey, "0")),
                    GuideField.Choice(AutoMarking.PreviewKey, "预告箭头（上游 30 m）", "Preview arrows (30 m upstream)", AutoMarking.ByRoad, new[] { C(AutoMarking.ByRoad, "按道路状态", "By the road"), C("1", "画", "Draw"), C("0", "不画", "None") },
                        s => s.Flag(AutoMarking.EndsKey) && !s.Is(AutoMarking.ArrowsKey, "0")),
                    GuideField.Flag(AutoMarking.FreeStopKey, "无管控的路口也画停止线", "Stop lines at uncontrolled places", false, s => s.Flag(AutoMarking.EndsKey) && s.Is(AutoMarking.KeepKey, "all")),
                    GuideField.Flag(AutoMarking.WaitingKey, "信号路口左弯待转区", "Left-turn waiting areas at signals", true, s => s.Flag(AutoMarking.EndsKey)),
                    GuideField.Choice(AutoMarking.JunctionGuideKey, "路口导向线", "Junction guide lines", "auto", new[]
                    {
                        C("auto", "按国标（5.6.3）", "By the standard (5.6.3)"), C("all", "每个左转都画", "Every left turn"), C("none", "不画", "None")
                    }, s => s.Flag(AutoMarking.EndsKey)),
                    ApproachGuide(0), ApproachArrows(0), ApproachGuide(1), ApproachArrows(1), ApproachGuide(2), ApproachArrows(2), ApproachGuide(3), ApproachArrows(3),
                    GuideField.Choice(AutoMarking.EdgeKey, "车行道边缘线", "Edge lines", "auto", new[]
                    {
                        C("auto", "按国标（4 车道以上或无路缘）", "By the standard (4 lanes, no kerb)"), C("all", "全部画", "All"), C("none", "不画", "None")
                    }).In("沿路", "Along the road"),
                    GuideField.Flag(AutoMarking.TaperKey, "车道增减处的渐变段、换道线", "Transitions where lanes change", true),
                    GuideField.Flag(AutoMarking.SightKey, "视距不足处禁止超车", "No passing where sight is short", true),
                    GuideField.Flag(AutoMarking.SpecialKey, "公交专用道、非机动车道", "Bus lanes, cycle lanes", true),
                    GuideField.Flag(AutoMarking.MidCrossKey, "路段中间的人行横道", "Crosswalks between junctions", true),
                    GuideField.Flag(AutoMarking.RecordKey, "记录", "Record", false, s => false),
                    GuideField.Digits(AutoMarking.SurfaceKey, "路面高度（cm）", "Surface (cm)", "0", 5, s => false),
                    GuideField.Digits(AutoMarking.ApproachesKey, "进口道数", "Approaches", "0", 1, s => false),
                    GuideField.Digits(AutoMarking.RulesKey, "规则版本", "Rules", "0", 2, s => false)
                },
                AutoPicture, false,
                null));

            // ---- 5 指示标线
            list.Add(T("mk.line.centre", "线1", "可跨越对向车行道分界线", "Crossable centre line", "kkydxcxdfjx", W("中心线", "黄虚线", "4-6", "centre line", "yellow dashed"),
                new[] { GuideField.Choice("width", "线宽", "Width", "15", kWidth1015) },
                s => Plan(RoadMarkings.Dashed(Run(s), RoadMarkings.CentreLine(s.Is("width", "10")), Phase(s))), run: true));
            list.Add(T("mk.line.lane", "线2、线3", "可跨越同向车行道分界线", "Crossable lane line", "kkytxcxdfjx", W("车道线", "白虚线", "6-9", "3-9", "2-4", "lane line"),
                new[] { GuideField.Choice("pattern", "线型", "Pattern", "69", kLaneLine), GuideField.Choice("width", "线宽", "Width", "15", kLineWidth) },
                s => Plan(RoadMarkings.Dashed(Run(s), LaneDash(s), Phase(s))), run: true));
            list.Add(T("mk.line.tidal", "线4", "潮汐车道线", "Tidal lane line", "cxcdx", W("潮汐", "reversible", "tidal"),
                new[] { GuideField.Choice("pattern", "线型（与本路段车道线一致）", "Pattern (as the lane line)", "69", kLaneLine), GuideField.Digits("gap", "两线间距（cm）", "Gap (cm)", "10", 2) },
                s => Plan(MarkingLines.TidalLaneLine(Run(s), LaneDash(s), F(s, "gap", 10f, 10f, 15f) / 100f, Phase(s))), run: true));
            list.Add(T("mk.line.tidalstop", "线4（图5）", "潮汐车道停止线", "Tidal lane stop line", "cxcdtzx", W("潮汐", "停止线", "tidal", "stop line"),
                new[] { GuideField.Choice("side", "虚线", "Dashed", "r", kSide), GuideField.Digits("gap", "两线间距（cm）", "Gap (cm)", "10", 2) },
                s => Plan(MarkingLines.TidalStopLine(Run(s), s.Is("side", "r"), F(s, "gap", 10f, 10f, 15f) / 100f, Phase(s))), run: true));
            list.Add(T("mk.line.edge", "线5、线6", "车行道边缘线", "Edge line", "cxdbyx", W("边缘线", "边线", "edge line", "kerb"),
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "solid", new[] { C("solid", "实线（线5）", "Solid (线5)"), C("dashed", "虚线 60/30（线6）", "Dashed 60/30 (线6)") }),
                    GuideField.Choice("side", "位置", "Side", "r", new[] { C("r", "行车方向右侧（白）", "Right of the traffic (white)"), C("l", "行车方向左侧（黄）", "Left of the traffic (yellow)"), C("w", "双向单车道的左侧（白）", "Left of a two-way single lane (white)") }),
                    GuideField.Choice("width", "线宽", "Width", "15", new[] { C("15", "15 cm", "15 cm"), C("20", "20 cm", "20 cm"), C("10", "10 cm（特殊路段）", "10 cm (special roads)") })
                },
                s => Plan(MarkingLines.EdgeLine(Run(s), s.Is("side", "l") || s.Is("side", "w"), s.Is("form", "dashed"), F(s, "width", 15f, 10f, 20f) / 100f, s.Is("side", "w"), Phase(s))), run: true));
            list.Add(T("mk.line.waiting", "线7", "左弯待转区线", "Left-turn waiting area lines", "zwdzqx", W("左弯待转", "待转区", "waiting area"),
                new[] { GuideField.Digits("lane", "两线间距（m）", "Distance between the lines (m)", "3.5", 4) },
                s =>
                {
                    List<Vec3> centre = Run(s);
                    float half = F(s, "lane", 3.5f, 2.5f, 6f) * 0.5f;
                    return Plan(MarkingLines.WaitingAreaLines(MarkingFills.Offset(centre, -half), MarkingFills.Offset(centre, half), Phase(s)));
                }, run: true));
            list.Add(T(WaitingId, "线7（图10）", "左弯待转区（按路口生成）", "Left-turn waiting area (from the junction)", "zwdzq", W("左弯待转", "待转区", "左转待行", "待行区", "waiting area", "left turn"),
                new[]
                {
                    GuideField.Choice("lanes", "待转车道数", "Waiting lanes", "1", new[] { C("1", "1 条", "1"), C("2", "2 条", "2"), C("3", "3 条", "3") }),
                    GuideField.Digits("lane", "车道宽（m）", "Lane width (m)", "3.5", 4),
                    GuideField.Digits("len", "长度（m，放置时按路口自动算）", "Length (m, set from the junction)", "15", 4),
                    GuideField.Digits("start", "起点后退（m，越过人行横道）", "Start (m, past the crosswalk)", "3.5", 4),
                    GuideField.Choice("left", "左侧线", "Left line", "y", new[] { C("y", "黄色（接左侧黄色边缘线）", "Yellow (continues the left edge line)"), C("w", "白色", "White") }),
                    GuideField.Choice("stop", "前端停止线宽", "Front stop line", "20", new[] { C("20", "20 cm", "20 cm"), C("30", "30 cm", "30 cm"), C("40", "40 cm", "40 cm") }),
                    GuideField.Flag("arrows", "区内左转导向箭头", "Left-turn arrows in it", true),
                    GuideField.Text(WaitingAxisKey, "中轴", "Axis", "", 400, s => false)
                },
                s =>
                {
                    List<Vec3> axis = RunPath.Decode(s.Get(WaitingAxisKey));
                    if (axis.Count < 2)
                        axis = WaitingArea.DefaultAxis();
                    return Plan(WaitingArea.Shapes(axis, I(s, "lanes", 1, 1, 3), F(s, "lane", 3.5f, 2.5f, 5f), F(s, "start", 3.5f, 0f, 20f), F(s, "len", 15f, 3f, 60f), !s.Is("left", "w"),
                        F(s, "stop", 20f, 20f, 40f) / 100f, s.Flag("arrows")));
                }, false,
                (s, zh) => RunPath.Decode(s.Get(WaitingAxisKey)).Count >= 2
                    ? (zh ? "沿路口左转车道生成；长度、起点、车道数可改。" : "Laid along left-turn lanes; length, start, lanes settable.")
                    : (zh ? "在路口进口道的左转车道上单击。" : "Click on the left-turn lane of a junction's approach.")));
            list.Add(T("mk.line.junction", "线8", "路口导向线", "Junction guide line", "lkdxx", W("路口", "导向线", "guide line"),
                new[] { GuideField.Choice("colour", "颜色", "Colour", "w", new[] { C("w", "白色（同向分界）", "White (same direction)"), C("y", "黄色（对向分界）", "Yellow (opposing)") }), GuideField.Flag("bicycle", "引导非机动车（8 cm）", "Guides bicycles (8 cm)", false) },
                s => Plan(MarkingLines.JunctionGuideLine(Run(s), s.Is("colour", "y") ? MarkingColour.Yellow : MarkingColour.White, s.Flag("bicycle"), Phase(s))), run: true));
            list.Add(T("mk.line.change", "线9", "换道线", "Lane-change line", "hdx", W("换道", "1-1", "lane change"),
                new[] { GuideField.Choice("width", "线宽（同所接线）", "Width (as the line it joins)", "15", kLineWidth) },
                s => Plan(MarkingLines.LaneChangeLine(Run(s), s.Is("width", "10") ? 0.10f : s.Is("width", "8") ? 0.08f : 0.15f, Phase(s))), run: true));
            list.Add(T("mk.crosswalk", "线10", "人行横道线", "Pedestrian crossing", "rxhdx", W("斑马线", "人行横道", "crosswalk", "zebra"),
                new[] { GuideField.Digits("span", "宽度（m，沿路横向）", "Width across the road (m)", "12", 4), GuideField.Flag(FitKey, "放置时按车道定宽", "Fit the width to the lanes", true), GuideField.Choice("stripe", "线宽", "Stripe width", "40", kStripe), GuideField.Digits("len", "条长（m，≥2）", "Stripe length (m, at least 2)", "4", 3) },
                s => Plan(RoadMarkings.Crosswalk(F(s, "span", 12f, 2f, 60f), s.Is("stripe", "45") ? 0.45f : 0.4f, F(s, "len", 4f, 2f, 12f)))));
            list.Add(T("mk.diamond", "线11", "人行横道预告标识线", "Crossing warning diamond", "rxhdygbsx", W("菱形", "预告", "diamond"), new GuideField[0], s => Plan(MarkingFigures.Diamond())));
            list.Add(T("mk.pedarrow", "5.8.8", "行人分道行走箭头", "Pedestrian lane arrow", "xrfdxzjt", W("行人", "分道", "箭头", "pedestrian arrow"), new GuideField[0], s => Plan(MarkingFigures.PedestrianArrow())));
            list.Add(T("mk.chevron", "线12", "折线车距确认标线", "Chevron distance marks", "zxcjqrbx", W("车距", "确认", "折线", "chevron", "following distance"),
                new[] { GuideField.Digits("len", "长度（m）", "Length (m)", "60", 4, NotDrawn), GuideField.Choice("groups", "组数", "Groups", "2", new[] { C("1", "1 组", "1 group"), C("2", "2 组", "2 groups"), C("3", "3 组", "3 groups"), C("4", "4 组", "4 groups") }), GuideField.Choice("stripe", "线宽", "Width", "40", kStripe) },
                s => Plan(MarkingLines.DistanceChevrons(Along(s, "len", 60f, 6f, 300f), I(s, "groups", 2, 1, 4), s.Is("stripe", "45") ? 0.45f : 0.4f)), bend: Bending.Drawn));
            list.Add(T("mk.halfdisc", "线13", "半圆状车距确认标线", "Half-disc distance marks", "bzzcjqrbx", W("车距", "半圆", "half disc"),
                new[] { GuideField.Choice("side", "位置", "Side", "r", new[] { C("r", "边缘线右侧", "Right of the edge line"), C("l", "边缘线左侧", "Left of the edge line") }), GuideField.Digits("edge", "边缘线宽（cm）", "Edge line width (cm)", "15", 2) },
                s => Plan(MarkingLines.HalfDiscs(Run(s), F(s, "edge", 15f, 10f, 20f) / 100f, s.Is("side", "r"), Phase(s))), run: true));

            // 停车位
            var bayFields = new[]
            {
                GuideField.Choice("count", "车位数", "Bays", "4", new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4"), C("5", "5", "5"), C("6", "6", "6"), C("8", "8", "8"), C("10", "10", "10") }, NotDrawn),
                GuideField.Digits("pitch", "车位宽（m，沿路）", "Bay width along the road (m)", "2.5", 4),
                GuideField.Digits("depth", "车位深（m，垂直路缘）", "Bay depth (m)", "5.5", 4),
                GuideField.Choice("width", "线宽", "Line width", "8", new[] { C("6", "6 cm", "6 cm"), C("8", "8 cm", "8 cm"), C("10", "10 cm", "10 cm") }),
                GuideField.Flag("kerb", "画靠路缘的线", "Draw the kerb-side line", false)
            };
            float BayLine(GuideSpec s) => s.Is("width", "6") ? 0.06f : s.Is("width", "10") ? 0.10f : 0.08f;
            list.Add(T("mk.bay.parallel", "线14", "平行式停车位标线", "Parallel parking bays", "pxstcwbx", W("停车位", "平行", "parking", "parallel"),
                new[]
                {
                    GuideField.Choice("count", "车位数", "Bays", "4", new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4"), C("6", "6", "6"), C("8", "8", "8") }, NotDrawn),
                    GuideField.Digits("pitch", "车位长（m，沿路）", "Bay length along the road (m)", "6", 4), GuideField.Digits("depth", "车位宽（m）", "Bay width (m)", "2.5", 4),
                    GuideField.Choice("width", "线宽", "Line width", "8", new[] { C("6", "6 cm", "6 cm"), C("8", "8 cm", "8 cm"), C("10", "10 cm", "10 cm") }), GuideField.Flag("kerb", "画靠路缘的线", "Draw the kerb-side line", false)
                },
                s =>
                {
                    float pitch = F(s, "pitch", 6f, 4f, 12f);
                    return Plan(MarkingFigures.BayRow(Bays(s, I(s, "count", 4, 1, 12), pitch), pitch, F(s, "depth", 2.5f, 2f, 4f), 90f, LineSide.Solid(BayLine(s), MarkingColour.White), s.Flag("kerb")));
                }, bend: Bending.Drawn));
            list.Add(T("mk.bay.angled", "线15", "倾斜式停车位标线", "Angled parking bays", "qxstcwbx", W("停车位", "倾斜", "parking", "angled"),
                new[] { bayFields[0], bayFields[1], bayFields[2], GuideField.Choice("angle", "角度", "Angle", "45", new[] { C("30", "30°", "30°"), C("45", "45°", "45°"), C("60", "60°", "60°") }), bayFields[3], bayFields[4] },
                s =>
                {
                    float pitch = F(s, "pitch", 2.5f, 2f, 4f), depth = F(s, "depth", 5.5f, 3f, 8f);
                    int angle = I(s, "angle", 45, 30, 60);
                    int count = Bays(s, I(s, "count", 4, 1, 12), pitch, depth * (float)Math.Cos(angle * Math.PI / 180.0));
                    return Plan(MarkingFigures.BayRow(count, pitch, depth, angle, LineSide.Solid(BayLine(s), MarkingColour.White), s.Flag("kerb")));
                }, bend: Bending.Drawn));
            list.Add(T("mk.bay.perpendicular", "线16", "垂直式停车位标线", "Perpendicular parking bays", "czstcwbx", W("停车位", "垂直", "parking", "perpendicular"), bayFields,
                s =>
                {
                    float pitch = F(s, "pitch", 2.5f, 2f, 4f);
                    return Plan(MarkingFigures.BayRow(Bays(s, I(s, "count", 4, 1, 12), pitch), pitch, F(s, "depth", 5.5f, 3f, 8f), 90f, LineSide.Solid(BayLine(s), MarkingColour.White), s.Flag("kerb")));
                }, bend: Bending.Drawn));
            list.Add(T("mk.bay.arrow", "线17", "带停车方向停车位标线", "Parking bays with a heading arrow", "dtcfxtcwbx", W("停车位", "停车方向", "箭头", "parking", "arrow"),
                new[] { bayFields[0], bayFields[1], bayFields[2], bayFields[3], GuideField.Choice("head", "车头朝向", "Vehicle's head", "kerb", new[] { C("kerb", "朝路缘", "Toward the kerb"), C("road", "朝车行道", "Toward the road") }) },
                s =>
                {
                    float pitch = F(s, "pitch", 2.5f, 2f, 4f), depth = F(s, "depth", 5.5f, 3f, 8f);
                    int count = Bays(s, I(s, "count", 4, 1, 12), pitch);
                    var plan = Plan(MarkingFigures.BayRow(count, pitch, depth, 90f, LineSide.Solid(BayLine(s), MarkingColour.White), false));
                    var arrows = new MarkingShape { Colour = MarkingColour.White };
                    for (int i = 0; i < count; i++)
                    {
                        // The arrow's tip is 50 cm below the bay's far edge (Fig. 23): the bay runs from the kerb (x 0) into the road (-X); toward the kerb the tip is at the kerb end.
                        float centreZ = (i + 0.5f) * pitch;
                        bool toKerb = s.Is("head", "kerb");
                        float yaw = toKerb ? (float)Math.PI / 2f : -(float)Math.PI / 2f;
                        float tipX = toKerb ? -MarkingFigures.BayArrowTipFromEdge : -(depth - MarkingFigures.BayArrowTipFromEdge);
                        float tailX = toKerb ? tipX - 1.2f : tipX + 1.2f;
                        MarkingFigures.Append(arrows, Turned(MarkingFigures.BayArrow(), yaw, new Vec3(tailX, 0f, centreZ)));
                    }

                    plan.Shapes.Add(arrows);
                    return plan;
                }, bend: Bending.Drawn));
            list.Add(T("mk.bay.taxi", "线18、线19", "出租车专用停车位标线", "Taxi bay lines", "czczytcwbx", W("出租车", "专用", "待客", "上下客", "校车", "公交车", "网约车", "taxi"),
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "wait", new[] { C("wait", "实线：专用待客（线18）", "Solid: taxi rank (线18)"), C("drop", "虚线：上下客（线19）", "Dashed: drop-off (线19)") }),
                    GuideField.Choice("count", "车位数", "Bays", "2", new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4") }, NotDrawn),
                    GuideField.Text("word", "文字（专用车辆）", "Word (the vehicle)", "出租车", 4)
                },
                s =>
                {
                    const float pitch = 3f, depth = 6f;
                    int count = Bays(s, I(s, "count", 2, 1, 6), pitch);
                    LineSide side = s.Is("form", "drop") ? LineSide.Dashed(0.10f, MarkingColour.White, 0.5f, 0.25f) : LineSide.Solid(0.10f, MarkingColour.White);
                    MarkingShape row = MarkingFigures.BayRow(count, pitch, depth, 90f, side, true);
                    for (int i = 0; i < count; i++)
                    {
                        Vec3 c = MarkingFigures.BayCentre(i, pitch, depth, 90f, out _);
                        row.Texts.AddRange(MarkingFigures.BayWords(s.Get("word"), c, MarkingFigures.BayWordWidth, MarkingFigures.BayWordHeight, MarkingFigures.BayWordGap, new Vec3(1f, 0f, 0f)));
                    }

                    return Plan(row);
                }, bend: Bending.Drawn));
            list.Add(T("mk.bay.disabled", "线20", "残疾人专用停车位标线", "Disabled parking bays", "cjrzytcwbx", W("残疾人", "无障碍", "轮椅", "disabled", "accessible"),
                new[] { GuideField.Digits("pitch", "车位宽（m）", "Bay width (m)", "2.5", 4), GuideField.Digits("depth", "车位深（m）", "Bay depth (m)", "5.5", 4), GuideField.Choice("width", "线宽", "Line width", "8", new[] { C("6", "6 cm", "6 cm"), C("8", "8 cm", "8 cm"), C("10", "10 cm", "10 cm") }) },
                s =>
                {
                    float pitch = F(s, "pitch", 2.5f, 2f, 4f), depth = F(s, "depth", 5.5f, 3f, 8f);
                    // Two bays with the hatched zone of 120 between them (Fig. 26).
                    var plan = new MarkingPlan();
                    float line = BayLine(s);
                    MarkingShape bays = MarkingFigures.BayRow(1, pitch, depth, 90f, LineSide.Solid(line, MarkingColour.White), false);
                    MarkingShape second = MarkingFigures.BayRow(1, pitch, depth, 90f, LineSide.Solid(line, MarkingColour.White), false);
                    MarkingFigures.Append(bays, Turned(second, 0f, new Vec3(0f, 0f, pitch + MarkingFigures.DisabledZoneWidth)));
                    plan.Shapes.Add(bays);
                    plan.Shapes.Add(MarkingFigures.HatchedZone(-depth, pitch, 0f, pitch + MarkingFigures.DisabledZoneWidth, line));
                    MarkingShape wheelchair = MarkingPictograms.Wheelchair(1.2f);
                    plan.Shapes.Add(Turned(wheelchair, (float)Math.PI / 2f, new Vec3(-depth * 0.5f, 0f, pitch * 0.5f)));
                    plan.Shapes.Add(Turned(wheelchair, (float)Math.PI / 2f, new Vec3(-depth * 0.5f, 0f, pitch * 1.5f + MarkingFigures.DisabledZoneWidth)));
                    return plan;
                }, bend: Bending.Click));
            list.Add(T("mk.bay.bicycle", "线21", "非机动车停车位标线", "Bicycle parking area", "fjdctcwbx", W("非机动车", "自行车", "停车", "bicycle parking"),
                new[] { GuideField.Digits("width", "宽（m）", "Width (m)", "6", 4), GuideField.Digits("len", "长（m）", "Length (m)", "2", 4), GuideField.Choice("line", "线宽", "Line width", "8", new[] { C("6", "6 cm", "6 cm"), C("8", "8 cm", "8 cm"), C("10", "10 cm", "10 cm") }) },
                s =>
                {
                    float width = F(s, "width", 6f, 1f, 30f), length = F(s, "len", 2f, 1f, 10f);
                    MarkingShape bay = MarkingFigures.BicycleBay(width, length, s.Is("line", "6") ? 0.06f : s.Is("line", "10") ? 0.10f : 0.08f);
                    float symbolWidth = Math.Min(2.5f, Math.Min(width - 0.4f, (length - 0.4f) * MarkingPictograms.BicycleCells / MarkingPictograms.BicycleRows));
                    MarkingShape bicycle = Turned(MarkingPictograms.Bicycle(symbolWidth, false), 0f, new Vec3(0f, 0f, length * 0.5f));
                    return Plan(new[] { bay, bicycle });
                }, bend: Bending.Click));
            list.Add(T("mk.bay.timed", "线22、23、24", "机动车限时段停车位标线", "Time-limited parking bays", "jdcxsdtcwbx", W("限时", "时段", "停车位", "time limited", "parking"),
                new[]
                {
                    GuideField.Choice("kind", "形式", "Form", "parallel", new[] { C("parallel", "平行式（线22）", "Parallel (线22)"), C("angled", "倾斜式（线23）", "Angled (线23)"), C("perpendicular", "垂直式（线24）", "Perpendicular (线24)") }),
                    GuideField.Choice("count", "车位数", "Bays", "3", new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4"), C("5", "5", "5"), C("6", "6", "6") }, NotDrawn),
                    GuideField.Text("time", "时段（如 19-6）", "Hours (like 19-6)", "19-6", 8)
                },
                s =>
                {
                    bool parallel = s.Is("kind", "parallel"), angled = s.Is("kind", "angled");
                    float pitch = parallel ? 6f : angled ? 3.5f : 2.5f, depth = parallel ? 2.5f : 5.5f, angle = angled ? 45f : 90f;
                    int count = Bays(s, I(s, "count", 3, 1, 8), pitch, angled ? depth * (float)Math.Cos(Math.PI / 4.0) : 0f);
                    MarkingShape row = MarkingFigures.BayRow(count, pitch, depth, angle, LineSide.Dashed(0.10f, MarkingColour.White, 0.6f, 0.6f), false);
                    for (int i = 0; i < count; i++)
                    {
                        Vec3 c = MarkingFigures.BayCentre(i, pitch, depth, angle, out Vec3 along);
                        // The hours are 60 high, laid across the bay (Figs. 28 to 30), upright for the driver that reads them from the road.
                        row.Texts.Add(new MarkingText { Text = s.Get("time"), X = c.X, Z = c.Z, Width = Math.Min(depth * 0.8f, 0.6f * Math.Max(1, s.Get("time").Length) * 0.7f), Length = MarkingFigures.BayTimeHeight, Yaw = (float)Math.PI / 2f });
                    }

                    return Plan(row);
                }, bend: Bending.Drawn));
            list.Add(T("mk.busbay", "线25", "港湾式停靠站标线", "Bus bay (lay-by)", "gwstkzbx", W("港湾", "停靠站", "公交", "bus bay", "lay-by"),
                new[]
                {
                    GuideField.Choice("form", "形式", "Form", "C", new[] { C("A", "图32 a)：虚线渐变段 + 实线", "Fig. 32 a)"), C("B", "图32 b)：渐变段 + 填充线", "Fig. 32 b)"), C("C", "图32 c)：实线 + 折线 + 文字", "Fig. 32 c)"), C("D", "图32 d)：填充 + 折线 + 文字", "Fig. 32 d)"), C("E", "图32 e)：全虚线 + 折线 + 文字", "Fig. 32 e)") }),
                    GuideField.Digits("taper", "渐变段长（m，≥25）", "Taper (m, 25 or more)", "25", 3), GuideField.Digits("straight", "正常段长（m）", "Straight part (m)", "30", 3, NotDrawn), GuideField.Digits("depth", "宽（m）", "Width (m)", "3", 3),
                    GuideField.Text("word", "文字", "Word", "公交车", 4)
                },
                s =>
                {
                    float taper = F(s, "taper", 25f, MarkingFigures.BayTaperMin, 80f), drawn = DrawnLength(s);
                    // Drawn along the road: the straight part is what the two tapers leave of the drawn length.
                    float straight = float.IsNaN(drawn) ? F(s, "straight", 30f, 12f, 120f) : Math.Max(12f, Math.Min(120f, drawn - 2f * taper));
                    return Plan(MarkingFigures.BusBay(s.Is("form", "A") ? BusBayForm.A : s.Is("form", "B") ? BusBayForm.B : s.Is("form", "D") ? BusBayForm.D : s.Is("form", "E") ? BusBayForm.E : BusBayForm.C,
                        taper, straight, taper, F(s, "depth", 3f, 2.5f, 5f), s.Get("word")));
                },
                note: (s, zh) => s.Is("form", "A") || s.Is("form", "B") ? "" : StopLengthNote(s, zh, true), bend: Bending.Drawn));
            list.Add(T("mk.kerbstop", "线26", "路边式停靠站标线", "Kerb-side bus stop", "lbstkzbx", W("路边", "停靠站", "公交", "校车", "kerb stop"),
                new[] { GuideField.Digits("len", "长（m）", "Length (m)", "20", 3, NotDrawn), GuideField.Digits("depth", "宽（m）", "Width (m)", "3", 3), GuideField.Text("word", "文字", "Word", "公交车", 4) },
                s => Plan(MarkingFigures.KerbStop(Along(s, "len", 20f, 12f, 60f), F(s, "depth", 3f, 2.5f, 5f), s.Get("word"))),
                note: (s, zh) => StopLengthNote(s, zh, false), bend: Bending.Drawn));
            list.Add(T("mk.hump.large", "线27", "大型减速丘标线", "Large hump marking", "dxjsqbx", W("减速丘", "大型", "speed hump", "large"), new GuideField[0], s => Plan(MarkingFigures.HumpChevron())));
            list.Add(T("mk.hump.small", "线28", "小型减速丘标线", "Small hump marking", "xxjsqbx", W("减速丘", "小型", "speed hump", "small"),
                new[] { GuideField.Choice("count", "三角数", "Triangles", "5", new[] { C("3", "3", "3"), C("4", "4", "4"), C("5", "5", "5"), C("6", "6", "6"), C("7", "7", "7"), C("8", "8", "8") }) },
                s => Plan(MarkingFigures.SmallHumpTriangles(I(s, "count", 5, 1, 12)))));
            list.Add(T("mk.hump.warning", "线29", "减速丘预告标记", "Hump warning bars", "jsqygbj", W("减速丘", "预告", "hump warning"), new GuideField[0], s => Plan(MarkingFigures.HumpWarningBars())));

            // 导向箭头
            AddArrow(list, "mk.arrow.straight", "直行", "Straight", "zx", ArrowKind.Straight);
            AddArrow(list, "mk.arrow.straightleft", "直行或左转", "Straight or left", "zxhzz", ArrowKind.StraightLeft);
            AddArrow(list, "mk.arrow.left", "左转", "Left", "zz", ArrowKind.Left);
            AddArrow(list, "mk.arrow.straightright", "直行或右转", "Straight or right", "zxhyz", ArrowKind.StraightRight);
            AddArrow(list, "mk.arrow.right", "右转", "Right", "yz", ArrowKind.Right);
            AddArrow(list, "mk.arrow.uturn", "掉头", "U-turn", "dt", ArrowKind.UTurn);
            AddArrow(list, "mk.arrow.straightuturn", "直行或掉头", "Straight or U-turn", "zxhdt", ArrowKind.StraightUTurn);
            AddArrow(list, "mk.arrow.leftuturn", "左转或掉头", "Left or U-turn", "zzhdt", ArrowKind.LeftUTurn);
            AddArrow(list, "mk.arrow.leftright", "仅可左右转弯", "Left and right only", "jkzyzw", ArrowKind.LeftRight);
            AddArrow(list, "mk.arrow.mergeleft", "向左合流", "Merge left", "xzhl", ArrowKind.MergeLeft);
            AddArrow(list, "mk.arrow.mergeright", "向右合流", "Merge right", "xyhl", ArrowKind.MergeRight);

            // ---- 5.14 and 5.15: the speed limit and the figures on the road surface
            list.Add(T("mk.speed", "线34", "路面限速标记", "Road-surface speed limit", "lmxsbj", W("限速", "最高限速", "最低限速", "路面限速", "speed limit", "figures"),
                new[]
                {
                    GuideField.Choice("kind", "标记", "Mark", "max", new[] { C("max", "最高限速（黄）", "Top speed (yellow)"), C("min", "最低限速（白）", "Lowest speed (white)"), C("pair", "最高与最低限速（图49）", "Top and lowest (Fig. 49)") }),
                    GuideField.Digits("value", "最高限速（km/h）", "Top speed (km/h)", "60", 3, s => !s.Is("kind", "min")),
                    GuideField.Digits("low", "最低限速（km/h）", "Lowest speed (km/h)", "40", 3, s => !s.Is("kind", "max")),
                    GuideField.Choice("band", "字高档", "Height band", "auto", new[] { C("auto", "自动（按道路设计速度）", "Auto (by the road's design speed)"), C("low", "≤40 km/h：字高 1.5～3 m", "40 km/h or less: 1.5 to 3 m"), C("mid", ">40～<100：字高 3～6 m", "Over 40, under 100: 3 to 6 m"), C("high", "≥100 km/h：字高 4.5～9 m", "100 km/h or more: 4.5 to 9 m") }),
                    GuideField.Choice("scale", "字高在该档中的位置", "Height within the band", "mid", new[] { C("small", "最小", "Smallest"), C("mid", "居中", "Middle"), C("large", "最大", "Largest") })
                },
                s => Plan(MarkingSpeed.Figures(s.Get("value"), s.Get("low"), s.Get("kind"), s.Is("band", "low") ? SpeedBand.Low : s.Is("band", "high") ? SpeedBand.High : SpeedBand.Mid, s.Is("scale", "small") ? 0f : s.Is("scale", "large") ? 1f : 0.5f))));
            // 5.14.1 路面文字标记: white, read from near to far along the traffic, the height by the band of Table 3, the width a third of it (2.5:1 or 3:1), the spacing Table 3's.
            list.Add(T(WordId, "5.14.1", "路面文字标记", "Word on the road", "lmwzbj", W("文字", "路面文字", "公交", "快速公交", "慢", "学校", "word", "text", "bus"),
                new[]
                {
                    GuideField.Text("text", "文字（自近而远）", "Text (near to far)", "公交", 6),
                    // 6.9.1 and Fig. 87 (0.12.22): the hours of a lane reserved at times, each a row of figures across the lane, nearest first, before the words.
                    GuideField.Text("time", "专用时间（可空，逗号分隔）", "Reserved hours (may be empty)", "", 16),
                    GuideField.Choice("band", "字高档（表3）", "Height band (Table 3)", "auto", new[] { C("auto", "自动（按设计速度）", "Auto (by design speed)"), C("low", "≤40：1.5～3 m", "40 or less: 1.5-3 m"), C("mid", ">40～<100：3～6 m", "Over 40: 3-6 m"), C("high", "≥100：4.5～9 m", "100 or more: 4.5-9 m") }),
                    GuideField.Choice("scale", "字高在该档中的位置", "Height within the band", "small", new[] { C("small", "最小", "Smallest"), C("mid", "居中", "Middle"), C("large", "最大", "Largest") })
                },
                s => Plan(Word(s))));
            list.Add(T(FreeTextId, "自定义（非国标）", "自定义地面文字", "Custom ground text", "zdydmwz", W("自定义", "地面文字", "文字", "广场", "停车场", "站名", "custom", "text", "ground", "words"),
                new[]
                {
                    GuideField.Text("text", "文字", "Text", "文字", 16),
                    GuideField.Text("text2", "第二行（可空）", "Second line (may be empty)", "", 16),
                    GuideField.Choice("colour", "颜色", "Colour", "w", new[] { C("w", "白色", "White"), C("y", "黄色", "Yellow"), C("r", "红色", "Red"), C("k", "黑色", "Black") }),
                    GuideField.Digits("h", "字高（cm）", "Character height (cm)", "150", 4),
                    GuideField.Choice("ratio", "字形（字宽∶字高）", "Shape (width : height)", "1", new[] { C("1", "方正 1∶1", "Square 1:1"), C("2", "瘦长 1∶2", "Tall 1:2"), C("3", "路面拉长 1∶3", "Road-stretched 1:3") }),
                    GuideField.Choice("read", "排列", "Layout", "row", new[] { C("row", "横排：从左到右，字头朝前", "In a row, left to right"), C("col", "竖排：自近而远（同路面文字）", "Near to far, as road words") }),
                    GuideField.Choice("gap", "字距", "Spacing", "normal", new[] { C("tight", "紧", "Tight"), C("normal", "适中", "Normal"), C("wide", "宽", "Wide") })
                },
                s => Plan(FreeText(s))));
            list.Add(T("mk.speedface", "5.14.3（图50）", "限速标志版面图形标记", "Speed-limit sign face on the road", "xsbzbmtxbj", W("限速标志", "限速", "椭圆", "speed limit sign", "ellipse"),
                new[]
                {
                    GuideField.Digits("value", "限速值（km/h）", "Limit (km/h)", "40", 3),
                    GuideField.Choice("len", "长轴（m）", "Long axis (m)", "4", new[] { C("3", "3 m", "3 m"), C("4", "4 m", "4 m"), C("5", "5 m", "5 m"), C("6", "6 m", "6 m") })
                },
                s => Plan(MarkingSpeed.Face(s.Get("value"), F(s, "len", 4f, 1.5f, 6f)))));
            list.Add(T("mk.sym.bicycle", "线35、线36", "非机动车路面标记", "Non-motor-vehicle mark", "fjdclmbj", W("非机动车", "自行车", "bicycle", "non-motor"),
                new[] { GuideField.Digits("width", "宽度（cm）", "Width (cm)", "160", 3), GuideField.Flag("arrow", "前方加箭头", "Arrow in front", false) },
                s => Plan(MarkingPictograms.Bicycle(Figure(s, "width", 160f), s.Is("arrow", "1")))));
            list.Add(T("mk.sym.arrow", "线36", "表示通行方向的箭头标记", "Direction-of-travel arrow", "bstxfxdjtbj", W("通行方向", "箭头", "arrow", "direction"), new GuideField[0], s => Plan(MarkingPictograms.Arrow())));
            list.Add(T("mk.sym.shared", "线37", "非机共享路面标记", "Shared-road mark", "fjgxlmbj", W("非机共享", "共享", "自行车", "shared", "chevron", "bicycle"),
                new[] { GuideField.Digits("width", "宽度（cm，不小于 50）", "Width (cm, at least 50)", "150", 3) },
                s => Plan(MarkingPictograms.Shared(Figure(s, "width", 150f)))));
            list.Add(T("mk.sym.ebike", "线38", "电动自行车路面标记", "Electric-bicycle mark", "ddzxclmbj", W("电动自行车", "电动车", "e-bike", "electric bicycle"),
                new[] { GuideField.Digits("width", "宽度（cm，取 10 的整倍数）", "Width (cm, to 10 cm)", "150", 3), GuideField.Flag("arrow", "前方加箭头", "Arrow in front", false) },
                s => Plan(MarkingPictograms.ElectricBicycle(Figure(s, "width", 150f), s.Is("arrow", "1")))));
            list.Add(T("mk.sym.pedestrian", "线39", "行人路面标记", "Pedestrian mark", "xrlmbj", W("行人", "人行", "pedestrian"),
                new[]
                {
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "160", 3),
                    GuideField.Flag("bicycle", "前面加非机动车标记", "Bicycle mark before it", false),
                    GuideField.Flag("arrow", "后面加箭头", "Arrow after it", false)
                },
                s => Plan(MarkingPictograms.Pedestrian(Figure(s, "height", 160f), s.Is("bicycle", "1"), s.Is("arrow", "1")))));
            list.Add(T("mk.sym.wheelchair", "线40", "残疾人专用停车位路面标记", "Disabled-bay mark", "cjrzytcwlmbj", W("残疾人", "无障碍", "轮椅", "wheelchair", "disabled"),
                new[] { GuideField.Digits("width", "宽度（cm）", "Width (cm)", "100", 3) },
                s => Plan(MarkingPictograms.Wheelchair(Figure(s, "width", 100f)))));
            list.Add(T("mk.zigzag", "线41", "注意前方路面状况标记", "Road-surface warning zigzag", "zyqfmmzkbj", W("路面状况", "折线", "注意", "zigzag", "warning"),
                new[] { GuideField.Digits("len", "长度（m）", "Length (m)", "12", 4), GuideField.Digits("height", "高度（m，横向）", "Height across (m)", "1", 3) },
                s => Plan(MarkingLines.Zigzag(F(s, "len", 12f, 2f, 60f), F(s, "height", 1f, 0.3f, 4f)))));
            list.Add(T("mk.bicyclecrossing", "线42", "非机动车横道线", "Bicycle crossing", "fjdchdx", W("非机动车", "横道", "bicycle crossing"),
                new[] { GuideField.Digits("span", "长度（m，沿路横向）", "Length across the road (m)", "12", 4), GuideField.Flag(FitKey, "放置时按车道定宽", "Fit the width to the lanes", true), GuideField.Digits("lane", "两线中心距（m）", "Line spacing (m)", "2", 3), GuideField.Choice("width", "线宽", "Width", "10", new[] { C("8", "8 cm", "8 cm"), C("10", "10 cm", "10 cm") }) },
                s =>
                {
                    float span = F(s, "span", 12f, 2f, 60f);
                    // The lines run across the road (along +X): built along +Z and turned a quarter turn.
                    var shapes = new List<MarkingShape>();
                    foreach (MarkingShape line in MarkingLines.BicycleCrossing(new List<Vec3> { new Vec3(0f, 0f, -span * 0.5f), new Vec3(0f, 0f, span * 0.5f) }, F(s, "lane", 2f, 2f, 8f), s.Is("width", "8") ? 0.08f : 0.10f))
                        shapes.Add(Turned(line, (float)Math.PI / 2f, Vec3.Zero));
                    return Plan(shapes);
                }));

            // ---- 6 禁止标线
            list.Add(T("mk.line.doubleyellow", "线43", "双黄实线", "Double yellow solid lines", "shsx", W("双黄线", "禁止跨越", "double yellow", "no crossing"),
                new[] { GuideField.Digits("gap", "两线间距（cm）", "Gap (cm)", "10", 3), GuideField.Choice("width", "线宽", "Width", "15", kWidth1015) },
                s => Plan(MarkingLines.DoubleYellow(Run(s), F(s, "gap", 10f, 10f, 200f) / 100f, s.Is("width", "10") ? 0.10f : 0.15f)), run: true));
            list.Add(T("mk.line.yellowsd", "线44", "黄色虚实线", "Yellow solid-and-dashed line", "hsxsx", W("黄色", "虚实线", "yellow", "solid dashed"),
                new[] { GuideField.Choice("side", "虚线", "Dashed", "r", kSide), GuideField.Digits("gap", "两线间距（cm）", "Gap (cm)", "10", 2), GuideField.Choice("width", "线宽", "Width", "15", kWidth1015) },
                s => Plan(MarkingLines.YellowSolidDashed(Run(s), s.Is("side", "r"), F(s, "gap", 10f, 10f, 30f) / 100f, s.Is("width", "10") ? 0.10f : 0.15f, Phase(s))), run: true));
            list.Add(T("mk.line.yellowsingle", "线45", "单黄实线", "Single yellow solid line", "dhsx", W("单黄线", "禁止超车", "single yellow", "no overtaking"),
                new[] { GuideField.Digits("width", "线宽（cm，15～30）", "Width (cm, 15 to 30)", "15", 2) },
                s => Plan(MarkingLines.SingleYellow(Run(s), F(s, "width", 15f, 15f, 30f) / 100f)), run: true));
            list.Add(T("mk.line.whitesolid", "线46", "禁止跨越同向车行道分界线", "White solid lane line", "jzkyxdfjx", W("白实线", "禁止变道", "white solid", "no lane change"),
                new[] { GuideField.Choice("width", "线宽", "Width", "15", kWidth1015) },
                s => Plan(MarkingLines.WhiteSolid(Run(s), s.Is("width", "10") ? 0.10f : 0.15f)), run: true));
            list.Add(T("mk.line.whitesd", "线47", "白色虚实线", "White solid-and-dashed line", "bsxsx", W("白色", "虚实线", "white", "solid dashed"),
                new[] { GuideField.Choice("side", "虚线", "Dashed", "r", kSide), GuideField.Choice("pattern", "线型（与本路段车道线一致）", "Pattern (as the lane line)", "69", kLaneLine), GuideField.Digits("gap", "两线间距（cm）", "Gap (cm)", "10", 2) },
                s => Plan(MarkingLines.WhiteSolidDashed(Run(s), s.Is("side", "r"), LaneDash(s), F(s, "gap", 10f, 10f, 30f) / 100f, Phase(s))), run: true));
            list.Add(T("mk.kerb.nopark", "线48", "缘石上禁止车辆停放线", "No-parking line on the kerb", "ysjzcltfx", W("缘石", "路缘", "禁止停放", "禁停", "kerb", "curb", "no parking"),
                new[]
                {
                    GuideField.Choice("side", "缘石在行车方向的", "The kerb is on the", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left") }),
                    GuideField.Digits("height", "缘石高（cm）", "Kerb height (cm)", "15", 2),
                    GuideField.Digits("width", "线宽（cm）", "Line width (cm)", "15", 3)
                },
                s => Plan(MarkingKerb.Line(Run(s), s.Is("side", "r"), F(s, "height", 15f, 5f, 40f) / 100f, F(s, "width", 15f, 5f, 100f) / 100f, false)), run: true));
            list.Add(T("mk.kerb.nolong", "线50", "缘石上禁止车辆长时停放线", "No-long-stay line on the kerb", "ysjzcltfx2", W("缘石", "路缘", "禁止长时停放", "kerb", "curb", "long stay"),
                new[]
                {
                    GuideField.Choice("side", "缘石在行车方向的", "The kerb is on the", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left") }),
                    GuideField.Digits("height", "缘石高（cm）", "Kerb height (cm)", "15", 2),
                    GuideField.Digits("width", "线宽（cm）", "Line width (cm)", "15", 3)
                },
                s => Plan(MarkingKerb.Line(Run(s), s.Is("side", "r"), F(s, "height", 15f, 5f, 40f) / 100f, F(s, "width", 15f, 5f, 100f) / 100f, true, Phase(s))), run: true));
            list.Add(T("mk.nopark.road", "线49、线51", "路面上禁止车辆停放线", "No-parking line on the road", "lmsjzcltfx", W("禁停", "禁止停放", "长时", "no parking"),
                new[] { GuideField.Digits("len", "长度（m）", "Length (m)", "12", 4, NotDrawn), GuideField.Choice("form", "形式", "Form", "two", new[] { C("two", "双实线（禁止停放，线49）", "Double (no parking, 线49)"), C("one", "单实线（禁止长时停放，线51）", "Single (no long-time parking, 线51)") }) },
                s => Plan(MarkingFigures.NoParkingLine(Along(s, "len", 12f, 1f, 100f), s.Is("form", "two"))), bend: Bending.Drawn));
            list.Add(T("mk.stop", "线52", "停止线", "Stop line", "tzx", W("停止线", "停车线", "stop line"),
                new[] { GuideField.Digits("span", "长度（m，横向）", "Length across (m)", "7", 4), GuideField.Flag(FitKey, "放置时按车道定宽", "Fit the width to the lanes", true), GuideField.Choice("width", "线宽", "Width", "30", kStopWidths) },
                s => Plan(RoadMarkings.StopLine(F(s, "span", 7f, 0.5f, 60f), s.Is("width", "20") ? 0.2f : s.Is("width", "40") ? 0.4f : 0.3f))));
            var laneCount = new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4"), C("5", "5", "5"), C("6", "6", "6") };
            list.Add(T("mk.giveway.stop", "线53", "停车让行线", "Stop-and-give-way line", "tcrxx", W("停车让行", "停", "stop", "give way"),
                new[] { GuideField.Digits("span", "长度（m，横向）", "Length across (m)", "7", 4), GuideField.Flag(FitKey, "放置时按车道定宽", "Fit the width to the lanes", true), GuideField.Choice("lanes", "车道数", "Lanes", "2", laneCount), GuideField.Flag("word", "画“停”字", "Paint the word 停", true) },
                s =>
                {
                    float span = F(s, "span", 7f, 1f, 60f);
                    int lanes = I(s, "lanes", 2, 1, 6);
                    return Plan(RoadMarkings.GiveWay(span, s.Flag("word") ? LaneXs(span, lanes) : null));
                }));
            list.Add(T("mk.giveway.yield", "线54", "减速让行线", "Yield line", "jsrxx", W("减速让行", "让", "yield", "give way"),
                new[] { GuideField.Digits("span", "长度（m，横向）", "Length across (m)", "7", 4), GuideField.Flag(FitKey, "放置时按车道定宽", "Fit the width to the lanes", true), GuideField.Choice("lanes", "车道数", "Lanes", "2", laneCount), GuideField.Flag("triangle", "画倒三角", "Paint the triangle", true) },
                s =>
                {
                    float span = F(s, "span", 7f, 1f, 60f);
                    int lanes = I(s, "lanes", 2, 1, 6);
                    return Plan(MarkingLines.GiveWayDashed(span, s.Flag("triangle") ? LaneXs(span, lanes) : null));
                }));
            list.Add(T(ChannelId, "6.6", "导流线", "Channelising island", "dlx", W("导流线", "导流", "渠化", "导流岛", "gore", "channelising", "island"),
                new[]
                {
                    GuideField.Digits("len", "长度（m）", "Length (m)", "20", 4, s => !HasPolygon(s)), GuideField.Digits("width", "底宽（m）", "Base width (m)", "3", 3, s => !HasPolygon(s)),
                    GuideField.Choice("fill", "填充", "Fill", "chevron", new[] { C("chevron", "V 型线（同向）", "V lines (same direction)"), C("diagonal", "斜纹线（对向）", "Diagonal lines (opposing)"), C("cycle", "斜纹线（机非之间，30 cm/2 m）", "Diagonal (motor to cycle lane)") }),
                    GuideField.Choice("colour", "颜色", "Colour", "w", new[] { C("w", "白色（分隔同向）", "White (same direction)"), C("y", "黄色（分隔对向）", "Yellow (opposing)") }),
                    GuideField.Choice("line", "填充线宽", "Fill line width", "45", kStripe),
                    GuideField.Choice("outline", "轮廓线宽（同所接标线）", "Outline (as the line it joins)", "15", kWidth1520),
                    GuideField.Flag("flip", "V 型线尖端反向", "V lines the other way", false, s => s.Is("fill", "chevron")),
                    GuideField.Flag(AreaFitKey, "贴合路面", "Fit to the road", true),
                    GuideField.Text(MarkingPolygon.Key, "轮廓", "Outline", "", 400, s => false)
                },
                s =>
                {
                    List<Vec3> island = MarkingPolygon.Decode(s.Get(MarkingPolygon.Key));
                    if (island.Count < 3)
                    {
                        float len = F(s, "len", 20f, 3f, 120f), w = F(s, "width", 3f, 1f, 12f);
                        island = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(w * 0.5f, 0f, len), new Vec3(-w * 0.5f, 0f, len) };
                    }

                    return Plan(MarkingAreas.Island(island, s.Is("outline", "20") ? 0.2f : 0.15f, ChannelFillOf(s), s.Is("colour", "y") ? MarkingColour.Yellow : MarkingColour.White, s.Is("line", "45") ? 0.45f : 0.4f, s.Flag("flip")));
                },
                false,
                (s, zh) => HasPolygon(s)
                    ? (zh ? $"{MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} 个顶点；改形状请删掉重画。" : $"{MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} corners; to change the shape, delete and redraw.")
                    : (zh ? "逐点单击画轮廓，点首点闭合；不画放三角形。" : "Click corners, then the first to close; else a triangle.")));
            list.Add(T("mk.centre.circle", "线55", "圆形中心圈", "Circular centre mark", "yxzxq", W("中心圈", "圆形", "centre circle"),
                new[] { GuideField.Digits("diameter", "直径（m，≥1.2）", "Diameter (m, 1.2 or more)", "2", 3), GuideField.Choice("band", "环宽", "Band", "15", new[] { C("15", "15 cm", "15 cm"), C("20", "20 cm", "20 cm") }) },
                s => Plan(MarkingFigures.CentreCircle(F(s, "diameter", 2f, 1.2f, 12f), s.Is("band", "20") ? 0.2f : 0.15f))));
            list.Add(T("mk.centre.diamond", "线56", "菱形中心圈", "Diamond centre mark", "lxzxq", W("中心圈", "菱形", "centre diamond"),
                new[] { GuideField.Digits("diagonal", "对角线（m，≥1.5）", "Diagonal (m, 1.5 or more)", "2", 3), GuideField.Choice("band", "边宽", "Band", "15", new[] { C("15", "15 cm", "15 cm"), C("20", "20 cm", "20 cm") }) },
                s => Plan(MarkingFigures.CentreDiamond(F(s, "diagonal", 2f, 1.5f, 12f), s.Is("band", "20") ? 0.2f : 0.15f))));
            // 0.12.3: drawn corner by corner as an area, like the island (the owner: "禁止停车区也应该可以按照区域形式绘制"); without an outline the rectangle of its width and length.
            list.Add(T(GridId, "线57", "网状线", "Box grid (no stopping)", "wzx", W("网状线", "网格", "禁止停车", "禁停区", "grid", "box junction", "no stopping"),
                new[]
                {
                    GuideField.Digits("width", "宽（m）", "Width (m)", "6", 4, s => !HasPolygon(s)), GuideField.Digits("len", "长（m）", "Length (m)", "8", 4, s => !HasPolygon(s)),
                    GuideField.Digits("pitch", "网格间距（m，1～5）", "Grid pitch (m, 1 to 5)", "1", 3),
                    GuideField.Flag(AreaFitKey, "贴合路面", "Fit to the road", true),
                    GuideField.Text(MarkingPolygon.Key, "轮廓", "Outline", "", 400, s => false)
                },
                s =>
                {
                    float pitch = F(s, "pitch", 1f, 1f, 5f);
                    List<Vec3> area = MarkingPolygon.Decode(s.Get(MarkingPolygon.Key));
                    return Plan(area.Count >= 3 ? MarkingFigures.GridArea(area, pitch) : MarkingFigures.Grid(F(s, "width", 6f, 2f, 40f), F(s, "len", 8f, 2f, 40f), pitch));
                },
                false,
                (s, zh) => HasPolygon(s)
                    ? (zh ? $"{MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} 个顶点；改形状请删掉重画。" : $"{MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} corners; to change the shape, delete and redraw.")
                    : (zh ? "逐点单击画轮廓，点首点或 Enter 闭合；不画放矩形。" : "Click corners, the first or Enter closes; else a rectangle.")));
            list.Add(T(SimpleGridId, "线58", "简化网状线", "Simplified grid", "jhwzx", W("网状线", "简化", "simple grid"),
                new[] { GuideField.Digits("width", "宽（m，≤12）", "Width (m, 12 at most)", "5", 4), GuideField.Digits("len", "长（m，≤12）", "Length (m, 12 at most)", "8", 4), GuideField.Choice("line", "线宽", "Line width", "40", kStripe), GuideField.Flag(AreaFitKey, "贴合路面", "Fit to the road", true) },
                s => Plan(MarkingFigures.SimpleGrid(F(s, "width", 5f, 2f, MarkingFigures.SimpleGridMaxSide), F(s, "len", 8f, 2f, MarkingFigures.SimpleGridMaxSide), s.Is("line", "45") ? 0.45f : 0.4f))));
            list.Add(T("mk.line.reserved", "线59、线60", "公交车、多乘员车专用车道线", "Bus / HOV lane line", "gjchdcydzydcdx", W("公交", "专用车道", "多乘员", "bus lane", "hov"),
                new[] { GuideField.Choice("width", "线宽", "Width", "20", new[] { C("20", "20 cm", "20 cm"), C("25", "25 cm", "25 cm") }) },
                s => Plan(MarkingLines.ReservedLaneLine(Run(s), s.Is("width", "25") ? 0.25f : 0.2f, Phase(s))), run: true));
            list.Add(T("mk.line.guidelane", "线61", "导向车道线", "Guide lane line", "dxcdx", W("导向车道", "实线", "guide lane"),
                new[] { GuideField.Choice("width", "线宽", "Width", "15", kWidth1015) },
                s => Plan(MarkingLines.GuideLaneLine(Run(s), s.Is("width", "10") ? 0.10f : 0.15f)), run: true));
            list.Add(T("mk.line.variable", "线62", "可变导向车道线", "Variable guide lane line", "kbdxcdx", W("可变导向", "齿", "variable lane"),
                new[] { GuideField.Choice("side", "齿在", "Ticks on", "r", new[] { C("r", "右侧", "The right"), C("l", "左侧", "The left") }), GuideField.Choice("width", "线宽", "Width", "15", new[] { C("15", "15 cm", "15 cm"), C("20", "20 cm", "20 cm") }) },
                s => Plan(MarkingFigures.VariableGuideLaneLine(Run(s), s.Is("side", "r"), s.Is("width", "20") ? 0.2f : 0.15f, Phase(s))), run: true));
            list.Add(T("mk.noturn", "线63、线64", "禁止掉头、禁止转弯标记", "No U-turn / no turn mark", "jzdtjzzwbj", W("禁止掉头", "禁止转弯", "no u-turn", "no turn"),
                new[]
                {
                    GuideField.Choice("kind", "箭头", "Arrow", "uturn", new[] { C("uturn", "掉头（线63）", "U-turn (线63)"), C("left", "左转（线64）", "Left (线64)"), C("right", "右转（线64）", "Right (线64)"), C("straight", "直行（线64）", "Straight (线64)") }),
                    SizeField(), GuideField.Digits("gap", "叉与箭头间隔（cm）", "Gap (cm)", "50", 3)
                },
                s => Plan(MarkingFigures.ForbiddenTurn(s.Is("kind", "left") ? ArrowKind.Left : s.Is("kind", "right") ? ArrowKind.Right : s.Is("kind", "straight") ? ArrowKind.Straight : ArrowKind.UTurn, SizeOf(s), F(s, "gap", 50f, 50f, 100f) / 100f))));
            list.Add(T(GoreId, "线65", "出入口标线", "Exit and entry gore marking", "crkbx", W("出口", "入口", "匝道", "出入口", "分流", "合流", "gore", "ramp", "entry", "exit"),
                new[]
                {
                    GuideField.Choice("form", "出入口", "Exit or entry", "exit", new[] { C("exit", "出口（图94 a）", "Exit (Fig. 94 a)"), C("entry", "入口（图94 b）", "Entry (Fig. 94 b)") }),
                    GuideField.Choice("side", "匝道在行车方向的", "The ramp is on the", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left") }, s => !HasPolygon(s)),
                    GuideField.Digits("len", "长度（m；分叉处为最长）", "Length (m; the longest at a fork)", "30", 4), GuideField.Digits("width", "末端宽（m）", "Width at the far end (m)", "4", 3), GuideField.Digits("dashed", "粗虚线长（m）", "Thick dashed line length (m)", "60", 3),
                    GuideField.Digits("solid", "粗实线长（m，≥2）", "Thick solid line (m, 2 or more)", "2", 2),
                    GuideField.Flag(AreaFitKey, "贴合路面", "Fit to the road", true),
                    GuideField.Text(MarkingPolygon.Key, "轮廓", "Outline", "", 400, s => false),
                    GuideField.Text(SplitKey, "分段", "Split", "", 4, s => false)
                },
                s =>
                {
                    // Found at a fork: the gore's own outline; its thick lines are an item of their own (mk.line.thick) along the approach.
                    if (GoreSides(s, out List<Vec3> main, out List<Vec3> ramp))
                        return Plan(MarkingAreas.Gore(main, ramp, !s.Is("form", "entry")));
                    float len = F(s, "len", 30f, 8f, 120f), width = F(s, "width", 4f, 1.5f, 12f), dashed = F(s, "dashed", 60f, 0f, 200f), solid = F(s, "solid", 2f, 2f, 20f);
                    return Plan(s.Is("form", "entry") ? MarkingWarnings.EntryGore(len, width, dashed, solid, !s.Is("side", "l")) : MarkingWarnings.ExitGore(len, width, dashed, solid, !s.Is("side", "l")));
                },
                false,
                (s, zh) => HasPolygon(s)
                    ? (zh ? $"按分叉处路面边缘生成，{MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} 个顶点。" : $"Made from the carriageways' edges at the fork, {MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count} corners.")
                    : (zh ? "靠近匝道分叉或合流时自动生成；别处放标准图。" : "Made at a ramp's fork or merge; elsewhere the standard's figure.")));

            // ---- 7 警告标线
            list.Add(T("mk.line.thick", "线65", "出入口粗虚线、粗实线", "Thick dashed and solid lines of an exit or entry", "crkcxx", W("粗虚线", "3-3", "粗实线", "出口", "入口", "匝道", "thick dashed", "exit", "entry"),
                new[]
                {
                    GuideField.Choice("solid", "粗实线", "Thick solid line", "end", new[] { C("end", "在终点（出口：接到鼻端）", "At the end (exit: up to the nose)"), C("start", "在起点（入口：从鼻端起）", "At the start (entry: from the nose)"), C("none", "没有", "None") }),
                    GuideField.Digits("sl", "粗实线长（m，≥2）", "Thick solid line (m, 2 or more)", "2", 2, s => !s.Is("solid", "none"))
                },
                s =>
                {
                    float total = Total(s), sl = F(s, "sl", 2f, MarkingLines.ThickMinSolid, 30f);
                    float whole = total > 0f ? total : PieceLength(s);
                    float from = 0f, to = 0f;
                    if (s.Is("solid", "end"))
                    {
                        from = Math.Max(0f, whole - sl);
                        to = whole + 1f;
                    }
                    else if (s.Is("solid", "start"))
                    {
                        from = -1f;
                        to = Math.Min(whole, sl);
                    }

                    return Plan(MarkingLines.ThickLine(Run(s), Phase(s), from, to));
                }, run: true));
            list.Add(T("mk.strip", "6.6", "沿路导流带", "Hatched strip along the road", "yldld", W("导流带", "导流线", "中央导流", "斜纹", "填充", "hatched strip", "median", "buffer"),
                new[]
                {
                    GuideField.Digits("width", "带宽（m）", "Strip width (m)", "1.5", 4),
                    GuideField.Choice("side", "带在画线的", "The strip is on the line's", "r", new[] { C("r", "右侧", "Right"), C("l", "左侧", "Left") }),
                    GuideField.Choice("fill", "填充", "Fill", "diagonal", new[] { C("chevron", "V 型线（同向）", "V lines (same direction)"), C("diagonal", "斜纹线（对向）", "Diagonal lines (opposing)"), C("cycle", "斜纹线（机非之间，30 cm/2 m）", "Diagonal (motor to cycle lane)") }),
                    GuideField.Choice("colour", "颜色", "Colour", "y", new[] { C("w", "白色（分隔同向）", "White (same direction)"), C("y", "黄色（分隔对向）", "Yellow (opposing)") }),
                    GuideField.Choice("line", "填充线宽", "Fill line width", "45", kStripe),
                    GuideField.Choice("outline", "轮廓线宽", "Outline", "15", kWidth1520),
                    GuideField.Choice("tip", "V 型线尖端朝", "V lines point to the", "s", new[] { C("s", "起点（车流从起点驶向终点）", "Start (traffic from start to end)"), C("e", "终点", "End") }, s => s.Is("fill", "chevron"))
                },
                s =>
                {
                    Ends(s, out bool first, out bool last);
                    return Plan(MarkingAreas.Strip(Run(s), F(s, "width", 1.5f, 0.3f, 12f), !s.Is("side", "l"), ChannelFillOf(s), s.Is("colour", "y") ? MarkingColour.Yellow : MarkingColour.White,
                        s.Is("outline", "20") ? 0.2f : 0.15f, s.Is("line", "45") ? 0.45f : 0.4f, !s.Is("tip", "e"), first, last, Phase(s), Total(s)));
                }, run: true));
            list.Add(T("mk.taper", "7.1", "宽度渐变段标线", "Width transition line", "kdjbdbx", W("渐变段", "缩减", "车道数减少", "taper", "narrowing", "lane drop"),
                new[]
                {
                    GuideField.Choice("kind", "路面（车行道）宽度", "The road's width", "narrow", new[] { C("narrow", "缩减或车道数减少（按公式 1 和表 4）", "Narrows or loses a lane (formula 1, Table 4)"), C("widen", "增加（20～30 m）", "Widens (20 to 30 m)") }),
                    GuideField.Digits("shift", "线横移的宽度 W（m）", "Width W the line moves over (m)", "3.5", 4),
                    GuideField.Digits("kmh", "设计速度（km/h，0 按本路）", "Design speed (km/h; 0: road's)", "0", 3),
                    GuideField.Choice("toward", "线向行车方向的", "The line moves to the", "l", new[] { C("l", "左侧移", "Left"), C("r", "右侧移", "Right") }),
                    GuideField.Choice("line", "线", "Line", "edge", new[] { C("edge", "边缘线（白实线）", "Edge line (white, solid)"), C("change", "换道线（白虚线 1/1）", "Lane-change line (white, 1/1 dashes)"), C("yellow", "黄实线", "Yellow, solid") }),
                    GuideField.Digits("grow", "渐变段长（m）", "Widening length (m)", "25", 2, s => s.Is("kind", "widen"))
                },
                s => Plan(MarkingTransition.Line(Speed(s), F(s, "shift", 3.5f, 0.5f, 12f), s.Is("kind", "narrow"), s.Is("toward", "r"), s.Get("line"), F(s, "grow", 25f, 20f, 30f))),
                false,
                (s, zh) =>
                {
                    float w = F(s, "shift", 3.5f, 0.5f, 12f), v = Speed(s);
                    bool narrow = s.Is("kind", "narrow");
                    float l = MarkingTransition.Length(v, w, narrow, F(s, "grow", 25f, 20f, 30f));
                    return zh ? $"渐变段长 {l:0.0} m" + (narrow ? $"，终点后线延长 {MarkingTransition.Extension(v, true):0} m。" : "。")
                              : $"Transition {l:0.0} m long" + (narrow ? $", the line goes on {MarkingTransition.Extension(v, true):0} m past its end." : ".");
                }));
            list.Add(T("mk.obstacle", "7.2", "接近障碍物标线", "Obstacle approach marking", "jjzawbx", W("障碍物", "桥墩", "分隔带端头", "obstacle"),
                new[] { GuideField.Digits("len", "长度（m）", "Length (m)", "30", 4), GuideField.Digits("obstacle", "障碍物宽（m）", "Obstacle width (m)", "1", 3), GuideField.Flag("opposing", "障碍物在对向分界线（黄）", "On the centre line (yellow)", true) },
                s => Plan(MarkingWarnings.ObstacleApproach(F(s, "len", 30f, 6f, 120f), F(s, "obstacle", 1f, 0.2f, 12f), s.Flag("opposing")))));
            list.Add(T("mk.rumble.transverse", "线66", "横向减速标线", "Transverse rumble bars", "hxjsbx", W("减速", "横向", "rumble", "speed reduction"),
                new[] { GuideField.Digits("lane", "车道宽（m）", "Lane width (m)", "3.5", 4), GuideField.Choice("groups", "组数", "Groups", "3", new[] { C("1", "1", "1"), C("2", "2", "2"), C("3", "3", "3"), C("4", "4", "4"), C("6", "6", "6"), C("10", "10（表5）", "10 (Table 5)") }), GuideField.Flag("table", "按表5（高速连续下坡）", "Table 5 (a long downgrade)", false) },
                s => Plan(MarkingWarnings.TransverseRumble(F(s, "lane", 3.5f, 2f, 8f), I(s, "groups", 3, 1, 10), s.Flag("table")))));
            list.Add(T("mk.rumble.longitudinal", "线67、线68", "纵向减速标线", "Longitudinal rumble marking", "zxjsbx", W("减速", "纵向", "菱形块", "rumble"),
                new[] { GuideField.Flag("taper", "起始渐变段（线68）", "Taper at the start (线68)", false), GuideField.Choice("side", "位置", "Side", "r", new[] { C("r", "线右侧", "Right of the line"), C("l", "线左侧", "Left of the line") }) },
                s => Plan(MarkingWarnings.LongitudinalRumble(Run(s), s.Flag("taper"), s.Is("side", "r"), Phase(s), Total(s))), run: true));
            list.Add(T("mk.toll.plaza", "线69", "收费广场减速标线", "Toll plaza rumble bars", "sfgcjsbx", W("收费", "广场", "减速", "toll plaza"),
                new[] { GuideField.Digits("road", "路宽（m）", "Road width (m)", "12", 4), GuideField.Choice("groups", "组数", "Groups", "5", new[] { C("5", "5", "5"), C("6", "6", "6"), C("8", "8", "8"), C("10", "10", "10"), C("12", "12", "12") }) },
                s => Plan(MarkingWarnings.TollPlaza(F(s, "road", 12f, 3f, 60f), I(s, "groups", 5, 5, 12)))));
            list.Add(T("mk.toll.island", "线70", "收费岛标线", "Toll island marking", "sfdbx", W("收费岛", "toll island"),
                new[] { GuideField.Digits("width", "岛宽（m）", "Island width (m)", "2.2", 3), GuideField.Flag("divides", "分隔对向（黄）", "Divides the directions (yellow)", false) },
                s => Plan(MarkingWarnings.TollIsland(F(s, "width", 2.2f, 1f, 8f), s.Flag("divides")))));
            list.Add(T("mk.railway", "线71", "铁路道口标线", "Level crossing marking", "tldkbx", W("铁路", "道口", "平交", "level crossing", "railway"),
                new[] { GuideField.Digits("lane", "车道宽（m）", "Lane width (m)", "3.5", 3) },
                s => Plan(MarkingWarnings.LevelCrossing(F(s, "lane", 3.5f, 2.5f, 8f)))));

            list.Add(T("mk.elevation", "线72", "立面标记", "Elevation marking", "lmbj", W("立面", "墩柱", "隧道洞口", "黄黑", "elevation", "pier", "portal", "stripes"),
                new[]
                {
                    GuideField.Digits("width", "宽（cm）", "Width (cm)", "100", 3),
                    GuideField.Digits("height", "高（cm，至少 250）", "Height (cm, at least 250)", "250", 3),
                    GuideField.Choice("lane", "车道在标记的", "The lane is on the", "r", new[] { C("r", "右侧（向下倾斜的一边朝右）", "Right (the low edge faces right)"), C("l", "左侧（向下倾斜的一边朝左）", "Left (the low edge faces left)") })
                },
                s => Plan(MarkingKerb.Elevation(F(s, "width", 100f, 30f, 600f) / 100f, F(s, "height", 250f, 250f, 800f) / 100f, s.Is("lane", "r")))));
            list.Add(T("mk.stud", "8.1", "突起路标", "Raised pavement markers", "tqlb", W("突起路标", "反光", "道钉", "路钮", "stud", "road stud", "cat eye"),
                new[]
                {
                    GuideField.Choice("colour", "颜色（与所配标线一致）", "Colour (as the line it goes with)", "w", new[] { C("w", "白色", "White"), C("y", "黄色", "Yellow"), C("r", "红色", "Red") }),
                    GuideField.Digits("gap", "间隔（m）", "Spacing (m)", "10", 2)
                },
                s => Plan(MarkingStuds.Studs(ShortRun(s), F(s, "gap", 10f, 2f, 15f), s.Is("colour", "y") ? MarkingColour.Yellow : s.Is("colour", "r") ? MarkingColour.Red : MarkingColour.White, Phase(s))),
                true,
                null));
            list.Add(T("mk.delineator", "8.2", "轮廓标", "Delineators", "lkb", W("轮廓标", "反光柱", "视线诱导", "delineator", "post", "guide post"),
                new[]
                {
                    GuideField.Choice("road", "道路", "Road", "other", new[] { C("hs", "高速公路（直线段 50 m）", "Motorway (50 m on a straight)"), C("ex", "一级公路、城市快速路（40 m）", "First-class road, expressway (40 m)"), C("other", "二、三级公路及其他道路（30 m）", "Other roads (30 m)") }),
                    GuideField.Flag("curve", "曲线段按半径取间隔（表7）", "Closer on curves (Table 7)", true),
                    GuideField.Choice("colour", "反射体颜色（避险车道为红）", "Reflector colour (red: escape)", "w", new[] { C("w", "白色", "White"), C("y", "黄色", "Yellow"), C("r", "红色", "Red") }),
                    GuideField.Digits("height", "高度（cm）", "Height (cm)", "70", 3)
                },
                s => Plan(MarkingStuds.Delineators(ShortRun(s), DelineatorSpacing(s), s.Is("colour", "y") ? MarkingColour.Yellow : s.Is("colour", "r") ? MarkingColour.Red : MarkingColour.White, F(s, "height", 70f, 60f, 120f) / 100f, Phase(s))),
                true,
                (s, zh) =>
                {
                    float straight = s.Is("road", "hs") ? MarkingStuds.StraightHighway : s.Is("road", "ex") ? MarkingStuds.StraightFirstClass : MarkingStuds.StraightOther, spacing = DelineatorSpacing(s), radius = MarkingStuds.MinRadius(ShortRun(s));
                    string curve = !s.Flag("curve") || radius > 5000f ? "" : (zh ? $"，本段最小半径 {radius:0} m" : $", tightest radius here {radius:0} m");
                    return zh ? $"间隔 {spacing:0} m（直线段 {straight:0} m{curve}）。" : $"Spacing {spacing:0} m (straight {straight:0} m{curve}).";
                }));

            AddBarriers(list);
            return list.ToArray();
        }

        private static readonly GuideChoice[] kWidth1520 = { new GuideChoice("15", "15 cm", "15 cm"), new GuideChoice("20", "20 cm", "20 cm") };

        private static string StopLengthNote(GuideSpec s, bool zh, bool bay)
        {
            float minimum = MarkingFigures.BusStopMinimumLength(F(s, "depth", 3f, 2.5f, 5f), bay ? 0.45f : 0.2f, s.Get("word"));
            float requested = bay ? F(s, "straight", 30f, 12f, 120f) : F(s, "len", 20f, 12f, 60f);
            return zh ? $"{(bay ? "正常段" : "停靠段")}实际长 {Math.Max(requested, minimum):0.##} m（至少 {minimum:0.##} m）。"
                : $"The {(bay ? "straight" : "stop")} length is {Math.Max(requested, minimum):0.##} m (at least {minimum:0.##} m).";
        }

        /// <summary>Whether an area marking has an outline of its own (drawn by the player, or found at a fork).</summary>
        private static bool HasPolygon(GuideSpec s) => MarkingPolygon.Decode(s.Get(MarkingPolygon.Key)).Count >= 3;

        private static MarkingWarnings.ChannelFill ChannelFillOf(GuideSpec s) =>
            s.Is("fill", "diagonal") ? MarkingWarnings.ChannelFill.Diagonal : s.Is("fill", "cycle") ? MarkingWarnings.ChannelFill.CycleSide : MarkingWarnings.ChannelFill.Chevron;

        /// <summary>The design speed (km/h) a width transition is worked out for; 60 when it has none.</summary>
        private static float Speed(GuideSpec s)
        {
            float v = F(s, "kmh", 0f, 0f, 200f);
            return v > 0f ? v : 60f;
        }

        /// <summary>The spacing of the delineators of a piece (m): the straight spacing of the class of road, closer on a curve by Table 7 when asked.</summary>
        private static float DelineatorSpacing(GuideSpec s)
        {
            float straight = s.Is("road", "hs") ? MarkingStuds.StraightHighway : s.Is("road", "ex") ? MarkingStuds.StraightFirstClass : MarkingStuds.StraightOther;
            if (!s.Flag("curve"))
                return straight;
            float radius = MarkingStuds.MinRadius(ShortRun(s));
            return MarkingStuds.DelineatorSpacing(radius > 5000f ? 0f : radius, straight);
        }

        private static void AddArrow(List<GuideTemplate> list, string id, string zh, string en, string pinyin, ArrowKind kind)
        {
            list.Add(T(id, "线30～33", "导向箭头：" + zh, "Guide arrow: " + en, "dxjt" + pinyin, W("导向箭头", "箭头", zh, "arrow", en),
                new[] { SizeField() }, s => Plan(RoadArrows.Shape(kind, SizeOf(s)))));
        }
    }
}
