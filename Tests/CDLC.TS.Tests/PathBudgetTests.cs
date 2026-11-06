using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.99 (the owner, 5 Nov 20:2x, "长距离放置断点又出现了"): the log of his session has noise walls whose items are 2 m short at the end: items 2, 3 and 4 of a wall of seven end in `|199:-3;ph=…` (a chord
    /// without its grade), and each is followed by a gap of 1.98 to 1.99 m before the next item starts. The settings of a wall are written in at most GuideSpec.MaxBytes (480) bytes; a longer text
    /// loses the end of its longest field (GuideSpec.Build), the path, in the middle of its last chord. A path on a bend and a slope costs 11 characters a chord ("199:-23:-19"), 31 chords 371, the other
    /// settings of the wall and the phase and the whole length 120 and more: 491 bytes. MarkingRuns.MaxPathChars (380) had been fixed for a marking line whose other settings are about 40. Four of 13 items of
    /// that session, two of 32 of the one before and one of 17 of the one before that were cut. These tests are made of his numbers.
    /// </summary>
    internal static class PathBudgetTests
    {
        public static void RunAll()
        {
            Program.Run("path budget: a wall's items on a bend and a slope were cut in the middle of their last chord, with the old budget", TheBug);
            Program.Run("path budget: with the budget of the item's own settings every path is written whole and the wall is in one piece", Whole);
            Program.Run("path budget: short settings keep the old budget, so that nothing a marking line did is changed", Unchanged);
            Program.Run("path budget: every barrier of the catalogue, on bends and slopes, writes every path whole", EveryBarrier);
            Program.Run("path budget: the budget's own rules", Rules);
        }

        /// <summary>The owner's settings of a noise wall, as the log of 20:19:18 prints them (everything but the path, the phase and the whole length).</summary>
        private const string WallSpec = "height=300;top=up;band=mid;pn=grey;gl=cl;pc=metal;base=c81;post=2;where=r;off=0;jn=side;nd=j;g=0";

        /// <summary>A line of points every 2 m along an arc of the given radius (turning right when the radius is positive), falling at grade (rise over run), length metres long.</summary>
        private static List<Vec3> Arc(float length, float radius, float grade, float x0 = 0f, float z0 = 0f, float heading = 0f)
        {
            var line = new List<Vec3>();
            double x = x0, z = z0, h = heading;
            for (float s = 0f; s <= length + 1e-3f; s += 2f)
            {
                line.Add(new Vec3((float)x, 410f + grade * s, (float)z));
                x += 2.0 * Math.Sin(h);
                z += 2.0 * Math.Cos(h);
                h += 2.0 / radius;
            }

            return line;
        }

        /// <summary>A line made of the text of a path, as the owner's log has them: its points in the world, starting where the owner's first item did.</summary>
        private static List<Vec3> OfText(string text) => MarkingPolygon.ToWorld(RunPath.Decode(text), new Vec3(-4012.26f, 410.71f, -1984.72f), -0.8988f);

        /// <summary>For each item: the spec as it is written, and whether its path is what the item has (nothing cut). The whole line is the decoded items one after another.</summary>
        private static void Check(GuideTemplate template, string spec, List<Vec3> line, int budget, out int items, out int cut, out int widest, out float worstGap, out float worstMiss)
        {
            List<RunItem> made = MarkingRuns.Items(line, budget);
            items = made.Count;
            cut = 0;
            widest = 0;
            worstGap = 0f;
            worstMiss = 0f;
            var decoded = new List<List<Vec3>>();
            float total = 0f;
            for (int i = 1; i < line.Count; i++)
                total += Vec3.DistanceXZ(line[i - 1], line[i]);
            foreach (RunItem item in made)
            {
                string written = MarkingTemplates.WithRun(template, spec, item.Path, item.Start, total);
                widest = Math.Max(widest, Encoding.UTF8.GetByteCount(written));
                string back = MarkingTemplates.PathOf(template, written);
                if (back != item.Path)
                    cut++;
                decoded.Add(MarkingPolygon.ToWorld(RunPath.Decode(back), item.Position, item.Yaw));
            }

            for (int i = 0; i + 1 < decoded.Count; i++)
            {
                if (decoded[i].Count > 0 && decoded[i + 1].Count > 0)
                    worstGap = Math.Max(worstGap, Vec3.DistanceXZ(decoded[i][decoded[i].Count - 1], decoded[i + 1][0]));
            }

            // Every point of the line is on the decoded line, to the text's own rounding.
            var all = new List<Vec3>();
            foreach (List<Vec3> part in decoded)
                all.AddRange(part);
            foreach (Vec3 p in line)
            {
                float best = float.MaxValue;
                for (int i = 0; i < all.Count; i++)
                    best = Math.Min(best, Vec3.DistanceXZ(all[i], p));
                worstMiss = Math.Max(worstMiss, best);
            }
        }

        private static void TheBug()
        {
            SignDef wall = SignCatalog.ById(MarkingTemplates.NoiseBarrierId);
            Program.Check(wall != null && wall.Template != null, "the noise wall is in the catalogue");
            // The owner's numbers: a run of 31 chords of 1.99 m turning left by 0.0023 rad and falling 1.9 % a chord ("199:-23:-19" 31 times: 371 characters, which the old budget of 380 let through), as the
            // log of 20:19:18 has for the items 2, 3 and 4 of its wall before the settings cut them.
            string text = string.Join("|", Enumerable.Repeat("199:-23:-19", 31));
            List<Vec3> line = OfText(text);
            Check(wall.Template, WallSpec, line, MarkingRuns.MaxPathChars, out int items, out int cut, out _, out _, out _);
            Program.Check(items == 1 && cut == 1, $"with the old budget of {MarkingRuns.MaxPathChars} characters the run is one item of 371 characters and its settings lose the end of the path: {items} item(s), {cut} cut");
            List<RunItem> one = MarkingRuns.Items(line, MarkingRuns.MaxPathChars);
            string written = MarkingTemplates.WithRun(wall.Template, WallSpec, one[0].Path, one[0].Start, 62f);
            string back = MarkingTemplates.PathOf(wall.Template, written);
            string[] chords = back.Split('|');
            Program.Check(Encoding.UTF8.GetByteCount(written) == GuideSpec.MaxBytes && back.Length < one[0].Path.Length, $"the settings are exactly the 480 bytes and the path is {back.Length} characters of {one[0].Path.Length}");
            Program.Check(chords[chords.Length - 1].Split(':').Length < 3, $"it ends in \"{chords[chords.Length - 1]}\", a chord without its grade, as the owner's log has them");
            Program.Check(RunPath.Decode(back).Count == line.Count - 1, $"which reads back as one chord less: {RunPath.Decode(back).Count - 1} of 30 (2 m of the wall are not there)");
            // Walls of 33 to 130 chords along a left bend of 870 m falling 1.9 % (Split cuts a run at every 32 chords, and the last of a wall has the rest: 31 chords one time in 32): with the old budget some
            // of them are cut and have a gap of one chord (2 m) before the next item, as the owner's walls had.
            int cutWalls = 0, shortWalls = 0;
            float widestMiss = 0f;
            for (int chordsInWall = 33; chordsInWall <= 130; chordsInWall++)
            {
                Check(wall.Template, WallSpec, Arc(chordsInWall * 2f, -870f, -0.019f, -4012f, -1985f, -0.9f), MarkingRuns.MaxPathChars, out _, out int wallCut, out _, out float wallGap, out float wallMiss);
                cutWalls += wallCut > 0 ? 1 : 0;
                // A cut path leaves a gap before the next item, or, in the last item of a wall, a point of the line that is not on the wall: the same chord of 2 m.
                shortWalls += Math.Max(wallGap, wallMiss) > 1.8f ? 1 : 0;
                widestMiss = Math.Max(widestMiss, Math.Max(wallGap, wallMiss));
            }

            Program.Check(cutWalls >= 3 && shortWalls == cutWalls && widestMiss > 1.8f && widestMiss < 2.2f, $"of 98 walls with the old budget {cutWalls} have a path cut and {shortWalls} are a chord short, in a gap or at the end: the widest {widestMiss:0.00} m (the owner's were 1.98, 1.99 and 1.99)");
        }

        private static void Whole()
        {
            SignDef wall = SignCatalog.ById(MarkingTemplates.NoiseBarrierId);
            int budget = MarkingTemplates.PathBudget(wall.Template, WallSpec);
            Program.Check(budget >= MarkingRuns.MinPathChars && budget < MarkingRuns.MaxPathChars, $"the room of a wall's path is under the old figure: {budget}");
            // The arithmetic, tight to the byte: a path of the budget's size, with the widest phase and total (seven digits each), makes the settings exactly the limit less the room for an edit, and one
            // character more is over it.
            int exact = Encoding.UTF8.GetByteCount(MarkingTemplates.WithRun(wall.Template, WallSpec, new string('1', budget), 10000f, 10000f));
            int over = Encoding.UTF8.GetByteCount(MarkingTemplates.WithRun(wall.Template, WallSpec, new string('1', budget + 1), 10000f, 10000f));
            Program.Check(exact == GuideSpec.MaxBytes - MarkingTemplates.EditRoom && over == exact + 1, $"it is what the other settings leave, less the room for an edit: {exact} bytes with {budget} characters, {over} with one more");
            // The owner's own run of 31 chords with the budget of its settings: two items, both whole.
            Check(wall.Template, WallSpec, OfText(string.Join("|", Enumerable.Repeat("199:-23:-19", 31))), budget, out int ownerItems, out int ownerCut, out _, out float ownerGap, out float ownerMiss);
            Program.Check(ownerItems == 2 && ownerCut == 0 && ownerGap < 0.03f && ownerMiss < 0.03f, $"the owner's run of 31 chords is {ownerItems} items, {ownerCut} cut, gap {ownerGap * 100f:0.0} cm, miss {ownerMiss * 100f:0.0} cm");
            int sweptCut = 0, swept = 0;
            for (int chordsInWall = 33; chordsInWall <= 130; chordsInWall++)
            {
                Check(wall.Template, WallSpec, Arc(chordsInWall * 2f, -870f, -0.019f, -4012f, -1985f, -0.9f), budget, out _, out int wallCut, out _, out float wallGap, out float wallMiss);
                sweptCut += wallCut + (wallGap < 0.03f && wallMiss < 0.03f ? 0 : 1);
                swept++;
            }

            Program.Check(sweptCut == 0, $"the same {swept} walls with the budget of the settings: nothing cut, every wall in one piece (gaps and misses under 3 cm): {sweptCut}");
            foreach ((float length, float radius, float grade) in new[] { (400f, 870f, -0.019f), (1000f, 870f, -0.019f), (600f, -300f, 0.043f), (300f, 120f, -0.08f), (500f, 100000f, 0.0f), (800f, -1500f, 0.012f), (500f, -870f, -0.019f) })
            {
                List<Vec3> line = Arc(length, radius, grade, -4012f, -1985f, -0.9f);
                Check(wall.Template, WallSpec, line, budget, out int items, out int cut, out int widest, out float gap, out float miss);
                Program.Check(cut == 0, $"{length:0} m, radius {radius:0}, grade {grade * 100:0.0} %: every path of the {items} items is read back as it was written: {cut} cut");
                Program.Check(widest <= GuideSpec.MaxBytes - MarkingTemplates.EditRoom, $"  the widest settings are {widest} bytes, with the room for an edit ({GuideSpec.MaxBytes - MarkingTemplates.EditRoom} bytes)");
                Program.Check(gap < 0.03f, $"  the items join end to start: the widest gap {gap * 100f:0.0} cm");
                Program.Check(miss < 0.03f, $"  every point of the line is on the written wall: the worst miss {miss * 100f:0.0} cm");
            }

            // An edit that lengthens the other settings by what the room is for cuts none of the placed items: another panel colour, a taller wall.
            List<Vec3> bent = Arc(250f, -870f, -0.019f, -4012f, -1985f, -0.9f);
            List<RunItem> placed = MarkingRuns.Items(bent, budget);
            int cutAfterEdit = 0;
            string longestEdit = "";
            foreach (RunItem item in placed)
            {
                string spec = MarkingTemplates.WithRun(wall.Template, WallSpec, item.Path, item.Start, 250f);
                GuideSpec edited = wall.Template.Parse(spec);
                // The longest value every choice field of the wall can take, one after another (a name of 4 to 10 characters instead of "grey" and "metal").
                foreach (GuideField field in wall.Template.Fields)
                {
                    if (field.Kind == GuideFieldKind.Text || field.Key == RunPath.Key || field.Key == MarkingTemplates.PhaseKey || field.Key == MarkingTemplates.TotalKey)
                        continue;
                    string widest = edited.Get(field.Key);
                    if (field.Choices != null)
                    {
                        foreach (GuideChoice choice in field.Choices)
                        {
                            if (choice.Value.Length > widest.Length)
                                widest = choice.Value;
                        }
                    }

                    edited = edited.With(field.Key, widest);
                }

                longestEdit = edited.ToString();
                if (MarkingTemplates.PathOf(wall.Template, longestEdit) != item.Path)
                    cutAfterEdit++;
            }

            Program.Check(longestEdit.Length > WallSpec.Length + 6, $"the edit made the other settings longer: {WallSpec.Length} to {longestEdit.Length} characters (the room is {MarkingTemplates.EditRoom} bytes)");
            Program.Check(cutAfterEdit == 0, $"and cuts none of the {placed.Count} placed items: {cutAfterEdit}");
        }

        private static void Unchanged()
        {
            // A marking line has about 40 characters besides its path: the budget is the old one and the line is cut into the items it was cut into.
            int tried = 0;
            foreach (string id in new[] { "mk.line.edge", "mk.line.center", "mk.line.lane", "mk.line.junction" })
            {
                SignDef def = SignCatalog.ById(id);
                if (def == null || def.Template == null || def.Template.FieldOf(RunPath.Key) == null)
                    continue;
                tried++;
                int budget = MarkingTemplates.PathBudget(def.Template, def.Template.Default().ToString());
                Program.Check(budget == MarkingRuns.MaxPathChars, $"{id}: the budget is the old {MarkingRuns.MaxPathChars}: {budget}");
                List<Vec3> line = Arc(700f, -870f, -0.019f, 0f, 0f, 0.3f);
                List<RunItem> old = MarkingRuns.Items(line);
                List<RunItem> now = MarkingRuns.Items(line, budget);
                bool same = old.Count == now.Count;
                for (int i = 0; same && i < old.Count; i++)
                    same = old[i].Path == now[i].Path && old[i].Start == now[i].Start;
                Program.Check(same, $"{id}: the same {old.Count} items with the same paths as before");
            }

            Program.Check(tried >= 2, $"at least two of the marking lines are in the catalogue and were tried: {tried}");
            Program.Check(MarkingTemplates.PathBudget(null, "") == MarkingRuns.MaxPathChars, "no template: the old budget");
            SignDef sign = SignCatalog.ById("gb.prohib.01");
            Program.Check(MarkingTemplates.PathBudget(sign?.Template, "") == MarkingRuns.MaxPathChars, "a sign with no path: the old budget");
            Program.Check(MarkingRuns.Items(Arc(300f, 870f, -0.019f)).Count == MarkingRuns.Items(Arc(300f, 870f, -0.019f), MarkingRuns.MaxPathChars).Count, "Items without a budget is Items with the old one");
        }

        private static void EveryBarrier()
        {
            int tested = 0, widest = 0;
            var random = new Random(97);
            foreach (SignDef def in SignCatalog.All)
            {
                if (def == null || !def.IsMarking || def.Template == null || def.Template.FieldOf(RunPath.Key) == null || def.Group != MarkingTemplates.BarrierGroup)
                    continue;
                string spec = def.Template.Default().ToString();
                int budget = MarkingTemplates.PathBudget(def.Template, spec);
                int worstCut = 0;
                float worstGap = 0f, worstMiss = 0f;
                for (int k = 0; k < 6; k++)
                {
                    float radius = (float)(random.NextDouble() * 1900.0 + 80.0) * (random.Next(2) == 0 ? 1f : -1f);
                    float grade = (float)(random.NextDouble() * 0.16 - 0.08);
                    float length = (float)(random.NextDouble() * 700.0 + 60.0);
                    Check(def.Template, spec, Arc(length, radius, grade, 100f, -300f, (float)random.NextDouble() * 6f), budget, out _, out int cut, out int wide, out float gap, out float miss);
                    worstCut += cut;
                    widest = Math.Max(widest, wide);
                    worstGap = Math.Max(worstGap, gap);
                    worstMiss = Math.Max(worstMiss, miss);
                }

                Program.Check(worstCut == 0 && worstGap < 0.03f && worstMiss < 0.03f, $"{def.Id}: budget {budget}, 6 random lines: {worstCut} cut path(s), widest gap {worstGap * 100f:0.0} cm, worst miss {worstMiss * 100f:0.0} cm");
                // The arithmetic is exact for every one of them: a path of the budget's size with the widest phase and total makes the settings the limit less the room for an edit, unless the budget is held
                // at the old figure (the others are short) or at the floor.
                int bytes = Encoding.UTF8.GetByteCount(MarkingTemplates.WithRun(def.Template, spec, new string('1', budget), 10000f, 10000f));
                bool tight = budget == MarkingRuns.MaxPathChars ? bytes <= GuideSpec.MaxBytes - MarkingTemplates.EditRoom : budget == MarkingRuns.MinPathChars || bytes == GuideSpec.MaxBytes - MarkingTemplates.EditRoom;
                Program.Check(tight, $"{def.Id}: a path of {budget} characters is {bytes} bytes with the settings");
                tested++;
            }

            Program.Check(tested >= 10, $"the catalogue has {tested} barriers with a path (at least 10), tried on random bends and slopes");
            Program.Check(widest <= GuideSpec.MaxBytes - MarkingTemplates.EditRoom, $"the widest settings of all of them: {widest} bytes");
        }

        private static void Rules()
        {
            SignDef wall = SignCatalog.ById(MarkingTemplates.NoiseBarrierId);
            // The budget is the room the others leave, never under the floor, never over the old figure; the catalogue's barriers have different rooms (their other settings are not as long).
            int plain = MarkingTemplates.PathBudget(wall.Template, WallSpec);
            Program.Check(plain >= MarkingRuns.MinPathChars && plain <= MarkingRuns.MaxPathChars, $"the budget is between the floor and the old figure: {plain}");
            var rooms = new HashSet<int>();
            foreach (SignDef def in SignCatalog.All)
            {
                if (def != null && def.IsMarking && def.Template != null && def.Template.FieldOf(RunPath.Key) != null && def.Group == MarkingTemplates.BarrierGroup)
                    rooms.Add(MarkingTemplates.PathBudget(def.Template, def.Template.Default().ToString()));
            }

            Program.Check(rooms.Count >= 3, $"the barriers have {rooms.Count} different rooms for a path: {string.Join(", ", rooms)}");
            // The phase and the whole length are counted at their widest: a path of the budget's size with a phase and a total of seven digits writes within the byte limit less the room for an edit.
            string path = new string('1', plain);
            string full = MarkingTemplates.WithRun(wall.Template, WallSpec, path, 99999.99f, 99999.99f);
            Program.Check(Encoding.UTF8.GetByteCount(full) == GuideSpec.MaxBytes - MarkingTemplates.EditRoom, $"a path of the budget's size with the widest phase and total: {Encoding.UTF8.GetByteCount(full)} bytes");
            Program.Check(MarkingTemplates.PathOf(wall.Template, full).Length == plain, "and it is read back whole");
            // PathOf: the path as written; none for a template without one, and for no text.
            Program.Check(MarkingTemplates.PathOf(null, "x") == "" && MarkingTemplates.PathOf(wall.Template, null) == "" && MarkingTemplates.PathOf(SignCatalog.ById("gb.prohib.01")?.Template, "") == "", "PathOf: nothing for no template, no text, a sign");
            // A path as long as the old budget is cut by Build: the cut the budget is to prevent.
            string over = MarkingTemplates.WithRun(wall.Template, WallSpec, new string('1', MarkingRuns.MaxPathChars), 0f, 0f);
            Program.Check(MarkingTemplates.PathOf(wall.Template, over).Length < MarkingRuns.MaxPathChars && Encoding.UTF8.GetByteCount(over) <= GuideSpec.MaxBytes, "GuideSpec.Build cuts a path that does not fit: the reason for the budget");
            // The arithmetic of the room, at both ends: short settings give the old figure, long ones the floor, and in between what is left of the limit less the room for an edit.
            // The limit is 480 bytes and the room for an edit 30: the old figure holds while the others take 70 bytes or less, and the floor from 390 bytes on.
            Program.Check(MarkingTemplates.BudgetFor(0) == 380 && MarkingTemplates.BudgetFor(40) == 380 && MarkingTemplates.BudgetFor(70) == 380, "budget for 0, 40 and 70 bytes of other settings: the old figure 380");
            Program.Check(MarkingTemplates.BudgetFor(71) == 379 && MarkingTemplates.BudgetFor(120) == 330 && MarkingTemplates.BudgetFor(200) == 250, "budget for 71, 120 and 200 bytes: what is left, 379, 330 and 250");
            Program.Check(MarkingTemplates.BudgetFor(389) == 61 && MarkingTemplates.BudgetFor(390) == 60 && MarkingTemplates.BudgetFor(391) == 60 && MarkingTemplates.BudgetFor(480) == 60 && MarkingTemplates.BudgetFor(5000) == 60, "budget for 389: 61; for 390 and more: the floor 60, never less");
            // Settings with no path, however long they are, have the old figure (a sign of text: four lines of twenty characters).
            SignDef text = SignCatalog.ById("gb.vms.text");
            if (text?.Template != null && text.Template.FieldOf(RunPath.Key) == null)
            {
                GuideSpec lines = text.Template.Default();
                foreach (string key in new[] { "l1", "l2", "l3", "l4" })
                    lines = lines.With(key, new string('字', 20));
                Program.Check(Encoding.UTF8.GetByteCount(lines.ToString()) > 150 && MarkingTemplates.PathBudget(text.Template, lines.ToString()) == MarkingRuns.MaxPathChars, $"settings of {Encoding.UTF8.GetByteCount(lines.ToString())} bytes with no path: the old figure");
            }

            // A line made of parts (a wall cut by a junction): the budget goes to every part. The parts of 64 m each fit 100 characters in 4 or more items, with the default 380 in 1 or 2.
            List<List<Vec3>> parts = new List<List<Vec3>> { Arc(64f, -870f, -0.019f, 0f, 0f, 0.3f), Arc(64f, -870f, -0.019f, 300f, 300f, 0.3f) };
            List<RunItem> loose = MarkingRuns.Items(parts, out float looseTotal);
            List<RunItem> tight = MarkingRuns.Items(parts, out float tightTotal, 100);
            Program.Check(tight.Count > loose.Count && tight.TrueForAll(item => item.Path.Length <= 100) && Math.Abs(looseTotal - tightTotal) < 0.01f, $"the parts of a line take the budget too: {loose.Count} items with the default, {tight.Count} with 100 characters, the same length {tightTotal:0.0} m");
            // The floor: a run of 32 chords is split into pieces that fit it.
            List<RunItem> tiny = MarkingRuns.Items(Arc(64f, 870f, -0.019f), MarkingRuns.MinPathChars);
            Program.Check(tiny.Count >= 4 && tiny.TrueForAll(item => item.Path.Length <= MarkingRuns.MinPathChars + 12), $"the floor splits a run into pieces that fit it: {tiny.Count} items");
        }
    }
}
