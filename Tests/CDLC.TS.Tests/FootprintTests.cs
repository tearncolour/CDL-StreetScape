using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// What the mod's meshes and pictures take, and what each draw distance would take (0.12.104; the owner, 6 Nov: "在设置中加入一个当前Mod内存显存占用，并给出档位建议"). The numbers of the scenes are
    /// worked by hand: a near tile of 10 items and 1000 bytes, a middle one of 20 and 2000, two far ones of 30 and 3000 at 400 and 600 m, the faces 5000 + 4000 + 1200 and 700 of textures.
    /// </summary>
    internal static class FootprintTests
    {
        public static void RunAll()
        {
            Program.Run("footprint: the bytes of a mesh and of an item, the budget and the levels", Units);
            Program.Run("footprint: what is held now, counted tile by tile", Measured);
            Program.Run("footprint: a step that is the setting is what is held; nearer ones drop tiles, farther ones add them", Steps);
            Program.Run("footprint: the cost of an item from far tiles, or from all tiles when there are too few far ones", PerItem);
            Program.Run("footprint: the horizon tiles, the tiles with no items, and what is not built", Special);
            Program.Run("footprint: the recommended step is the farthest within the budget", Recommendation);
            Program.Run("footprint: nothing built yet is no figure, and the panel's text", Json);
        }

        private const long MB = 1024L * 1024L;

        private static TileCost Tile(float distance, int items, TileTier tier, long mesh) => new TileCost { Distance = distance, Items = items, Tier = tier, MeshBytes = mesh };

        /// <summary>The scene in the summary: everything inside the setting (700 m) is built, and a tile beyond it is not.</summary>
        private static FootprintInput Scene()
        {
            var input = new FootprintInput { FaceFull = 5000, FaceHalf = 4000, FaceSmall = 1200, DetailBytes = 700, ItemBytes = 3000, PictureBytes = 500, Renderers = 12, Items = 100 };
            input.Tiles.Add(Tile(30f, 10, TileTier.Near, 1000));
            input.Tiles.Add(Tile(150f, 20, TileTier.Mid, 2000));
            input.Tiles.Add(Tile(400f, 30, TileTier.Far, 3000));
            input.Tiles.Add(Tile(600f, 30, TileTier.Far, 3000));
            input.Tiles.Add(Tile(800f, 40, TileTier.None, 0));
            return input;
        }

        private static StepCost Step(FootprintReport report, float far)
        {
            foreach (StepCost step in report.Steps)
            {
                if (step.Far == far)
                    return step;
            }

            throw new InvalidOperationException("no step " + far);
        }

        private static void Units()
        {
            Program.Check(Footprint.MeshBytes(100, 300) == 100 * 48 + 300 * 2, "a mesh of 100 vertices and 300 indices: 48 bytes a vertex, 2 an index: " + Footprint.MeshBytes(100, 300));
            Program.Check(Footprint.MeshBytes(65000, 1000) == 65000L * 48 + 2000 && Footprint.MeshBytes(65001, 1000) == 65001L * 48 + 4000, "16-bit indices up to 65000 vertices, 32-bit above");
            Program.Check(Footprint.MeshBytes(0, 0) == 0 && Footprint.VertexStride == 48 && Footprint.Index16MaxVertices == 65000, "an empty mesh is none; the constants");
            Program.Check(Footprint.ItemBytes(0) == 182 && Footprint.ItemBytes(100) == 382 && Footprint.ItemBytes(-5) == 182, "an item: the record, its string's 22 bytes and 2 a character; a negative length is none");
            Program.Check(Footprint.BudgetFor(0) == 384 * MB && Footprint.BudgetFor(-1) == 384 * MB, "the card's memory not known: 384 MB");
            Program.Check(Footprint.BudgetFor(8192 * MB) == (long)(8192 * MB * 0.05), $"8 GB: 5 %, {Footprint.BudgetFor(8192 * MB) / (double)MB:0.0} MB");
            Program.Check(Footprint.BudgetFor(2048 * MB) == 192 * MB && Footprint.BudgetFor(1024 * MB) == 192 * MB, "a small card: 192 MB at least");
            Program.Check(Footprint.BudgetFor(16384 * MB) == 768 * MB && Footprint.BudgetFor(24576 * MB) == 768 * MB, "a big card: 768 MB at most");
            Program.Check(Footprint.BudgetFor(4096 * MB) == (long)(4096 * MB * 0.05) && Footprint.BudgetFor(4096 * MB) > 192 * MB, "4 GB is 5 %, above the least");
            Program.Check(Footprint.LevelOf(50, 100) == FootprintLevel.Light && Footprint.LevelOf(51, 100) == FootprintLevel.Fine && Footprint.LevelOf(100, 100) == FootprintLevel.Fine && Footprint.LevelOf(101, 100) == FootprintLevel.Heavy, "light up to half, fine up to all, heavy over");
            Program.Check(Footprint.LevelOf(0, 100) == FootprintLevel.Light && Footprint.LevelOf(long.MaxValue / 4, 100) == FootprintLevel.Heavy, "none is light, a huge one is heavy");
        }

        private static void Measured()
        {
            FootprintReport r = Footprint.Build(Scene());
            Program.Check(r.MeshBytes == 9000 && r.FaceBytes == 10200 && r.DetailBytes == 700 && r.Vram == 19900, $"the graphics memory: meshes {r.MeshBytes}, faces {r.FaceBytes}, detail {r.DetailBytes}, all {r.Vram}");
            Program.Check(r.ItemBytes == 3000 && r.PictureBytes == 500 && r.Ram == 3500, "the memory: items 3000 and pictures 500");
            Program.Check(r.Tiles == 4 && r.Renderers == 12 && r.Items == 100, $"4 built tiles (the tile that is not built is none), the renderers and the items as given: {r.Tiles}");
            Program.Check(r.Budget == 384 * MB && r.Level == FootprintLevel.Light && r.HasModel, "384 MB when the card is not known; 19.9 KB is light");
            // A built tile whose items are gone is held until the plan drops it: its meshes count.
            FootprintInput gone = Scene();
            gone.Tiles.Add(Tile(900f, 0, TileTier.Far, 777));
            FootprintReport g = Footprint.Build(gone);
            Program.Check(g.MeshBytes == 9777 && g.Tiles == 5, "a built tile with no items is held: its meshes and the tile count it");
            // The level is the whole of the graphics memory against the card's budget.
            FootprintInput heavy = Scene();
            heavy.VramBytes = 8192 * MB;
            heavy.DetailBytes = 300 * MB;
            FootprintReport h = Footprint.Build(heavy);
            Program.Check(h.Budget == (long)(8192 * MB * 0.05) && h.Level == FootprintLevel.Fine && h.VramBytes == 8192 * MB, $"300 MB of an 8 GB card's 409 MB budget: fine ({h.Level})");
            heavy.DetailBytes = 100 * MB;
            Program.Check(Footprint.Build(heavy).Level == FootprintLevel.Light, "100 MB of it: light");
            heavy.DetailBytes = 500 * MB;
            Program.Check(Footprint.Build(heavy).Level == FootprintLevel.Heavy, "500 MB of it: heavy");
            Program.Check(Footprint.Build(new FootprintInput()).Vram == 0 && Footprint.Build(new FootprintInput()).Tiles == 0, "an empty input holds nothing");
        }

        private static void Steps()
        {
            FootprintReport r = Footprint.Build(Scene());
            Program.Check(r.Steps.Count == StreamOptions.FarSteps.Length && r.Steps[0].Far == 300f && r.Steps[r.Steps.Count - 1].Far == 3000f, "a step for each far limit of the panel");
            Program.Check(Step(r, 700f).Bytes == 19900 && Step(r, 700f).Bytes == r.Vram, $"the step that is the setting is what is held now: {Step(r, 700f).Bytes}");
            // 500 m: the far tile at 600 m is dropped (3000 of meshes) and the far items fall from 60 to 30: the small faces 1200 * 30 / 60 = 600.
            Program.Check(Step(r, 500f).Bytes == 6000 + 9000 + 600 + 700, $"500 m drops the tile at 600 m: {Step(r, 500f).Bytes} (16300)");
            // 300 m: both far tiles are dropped: the near and middle meshes 3000, the faces of near and middle size 9000, no small ones, 700.
            Program.Check(Step(r, 300f).Bytes == 3000 + 9000 + 0 + 700, $"300 m drops both far tiles: {Step(r, 300f).Bytes} (12700)");
            // 1000 m: the tile at 800 m (40 items) is built at the cost of an item of the far tiles, 6000 / 60 = 100: 4000 more; the small faces 1200 * 100 / 60 = 2000.
            Program.Check(Step(r, 1000f).Bytes == 13000 + 9000 + 2000 + 700, $"1000 m adds the tile at 800 m: {Step(r, 1000f).Bytes} (24700)");
            Program.Check(Step(r, 2000f).Bytes == Step(r, 1000f).Bytes && Step(r, 3000f).Bytes == Step(r, 1000f).Bytes, "there is nothing farther: the same at 2 and 3 km");
            long last = 0;
            bool rising = true;
            foreach (StepCost step in r.Steps)
            {
                rising &= step.Bytes >= last;
                last = step.Bytes;
            }

            Program.Check(rising, "the cost never falls as the step grows");
            // A built far tile at exactly the limit is outside it too.
            FootprintInput built = Scene();
            built.Tiles.Add(Tile(1000f, 30, TileTier.Far, 3000));
            // Its 3000 bytes are not in the step: the meshes are 13000 as before; it is a far tile that was built, so the share of the small faces is 100 of 90 far items: 1200 * 100 / 90 = 1333.
            Program.Check(Step(Footprint.Build(built), 1000f).Bytes == 13000 + 9000 + 1333 + 700 && Step(Footprint.Build(built), 1500f).Bytes == 13000 + 3000 + 9000 + 1333 + 700 + (long)Math.Round(1200.0 * 30 / 90) - 0, $"a built far tile at 1000 m is not in the 1000 m step, and is in the 1500 m one: {Step(Footprint.Build(built), 1000f).Bytes} and {Step(Footprint.Build(built), 1500f).Bytes}");
            // A tile at exactly the limit is outside it (the plan's rule: a distance under the limit).
            FootprintInput edge = Scene();
            edge.Tiles.Add(Tile(1000f, 5, TileTier.None, 0));
            Program.Check(Step(Footprint.Build(edge), 1000f).Bytes == Step(r, 1000f).Bytes && Step(Footprint.Build(edge), 1500f).Bytes > Step(r, 1000f).Bytes, "a tile at 1000 m is not in the 1000 m step, and is in the 1500 m one");
            // The near and middle tiles are the same whatever the step.
            FootprintInput nearOnly = new FootprintInput { FaceFull = 100, DetailBytes = 7 };
            nearOnly.Tiles.Add(Tile(10f, 5, TileTier.Near, 40));
            FootprintReport n = Footprint.Build(nearOnly);
            bool same = true;
            foreach (StepCost step in n.Steps)
                same &= step.Bytes == 147;
            Program.Check(same, "only a near tile: 147 bytes at every step");
        }

        private static void PerItem()
        {
            // Five far items only (fewer than 20): the cost of an item is taken from all the built tiles: a near tile of 10 items and 2000 bytes, 200 an item, and the far tile's 5 items of 100.
            var input = new FootprintInput();
            input.Tiles.Add(Tile(20f, 10, TileTier.Near, 2000));
            input.Tiles.Add(Tile(500f, 5, TileTier.Far, 100));
            input.Tiles.Add(Tile(600f, 10, TileTier.None, 0));
            FootprintReport r = Footprint.Build(input);
            double all = 2100.0 / 15.0;
            Program.Check(Step(r, 700f).Bytes == (long)Math.Round(2000 + 100 + all * 10), $"too few far items: an item of the unbuilt tile costs {all:0.0}, the average of all: {Step(r, 700f).Bytes}");
            // Twenty far items: the cost is the far tiles' own, 100 / 20 = 5 an item.
            input = new FootprintInput();
            input.Tiles.Add(Tile(20f, 10, TileTier.Near, 2000));
            input.Tiles.Add(Tile(500f, 20, TileTier.Far, 100));
            input.Tiles.Add(Tile(600f, 10, TileTier.None, 0));
            Program.Check(Step(Footprint.Build(input), 700f).Bytes == 2000 + 100 + 50, $"twenty far items are enough: 5 an item: {Step(Footprint.Build(input), 700f).Bytes}");
            // Nineteen are not.
            input.Tiles[1] = Tile(500f, 19, TileTier.Far, 100);
            double mixed = 2100.0 / 29.0;
            Program.Check(Step(Footprint.Build(input), 700f).Bytes == (long)Math.Round(2000 + 100 + mixed * 10), "nineteen are not: the average of all");
            // Before a far tile is built the small pictures are taken to cost what the near and middle ones do an item: 5000 / 10 = 500 an item (more than they will).
            var early = new FootprintInput { FaceFull = 5000 };
            early.Tiles.Add(Tile(20f, 10, TileTier.Near, 1000));
            early.Tiles.Add(Tile(400f, 6, TileTier.None, 0));
            FootprintReport e = Footprint.Build(early);
            Program.Check(Step(e, 300f).Bytes == 1000 + 5000 && Step(e, 500f).Bytes == (long)Math.Round(1000 + 100.0 * 6 + 5000 + 500.0 * 6), $"no far tile yet: the cost of an item of any tile and of its face: {Step(e, 500f).Bytes} (9600)");
            // A built tile with no items has meshes but no item to share them: it is not in the cost of an item (a far tile of 10 items and 1000, an empty one of 5000, an unbuilt one of 10 items).
            var empty = new FootprintInput();
            empty.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            empty.Tiles.Add(Tile(100f, 0, TileTier.Far, 5000));
            empty.Tiles.Add(Tile(400f, 10, TileTier.None, 0));
            Program.Check(Step(Footprint.Build(empty), 700f).Bytes == 1000 + 100 * 10, $"an empty built tile is not in the cost of an item: {Step(Footprint.Build(empty), 700f).Bytes} (2000)");
            // The pictures of the near tiles are not counted twice: no near item, no cost to take.
            var none = new FootprintInput { FaceHalf = 90 };
            none.Tiles.Add(Tile(300f, 4, TileTier.Mid, 80));
            none.Tiles.Add(Tile(450f, 3, TileTier.None, 0));
            Program.Check(Step(Footprint.Build(none), 500f).Bytes > 0, "a middle tile and an unbuilt one: a figure");
        }

        private static void Special()
        {
            // A horizon tile (a wall) beyond the step stays as it is; inside it, it is a far tile and costs by the items it has (all of them: the horizon has the walls only).
            var input = new FootprintInput { FaceSmall = 400 };
            input.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            input.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            input.Tiles.Add(Tile(2500f, 4, TileTier.Horizon, 50));
            FootprintReport r = Footprint.Build(input);
            Program.Check(r.MeshBytes == 2050 && r.Tiles == 3, "the horizon tile is held with the others");
            Program.Check(Step(r, 2000f).Bytes == 2000 + 50 + 400, $"at 2 km the wall tile at 2.5 km stays as it is (2000 + 50 of meshes, the 400 of small faces): {Step(r, 2000f).Bytes}");
            // 3 km: it is a far tile of 4 items at 100 an item: 400 more, and the small faces 400 * 24 / 20 = 480 (20 far items built, 24 in the step).
            Program.Check(Step(r, 3000f).Bytes == 2000 + 400 + 480, $"at 3 km it is a far tile: {Step(r, 3000f).Bytes} (2880)");
            Program.Check(Step(r, 300f).Bytes == 2000 + 50 + 400, "at 300 m the far tiles at 100 m stay (they are near enough) and the wall tile is as it is: " + Step(r, 300f).Bytes);
            // A built tile with no items is dropped in a step, though it is held now.
            var dropped = new FootprintInput();
            dropped.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            dropped.Tiles.Add(Tile(100f, 0, TileTier.Far, 5000));
            FootprintReport d = Footprint.Build(dropped);
            Program.Check(d.MeshBytes == 6000 && Step(d, 700f).Bytes == 1000, $"held now 6000, in a step 1000: {Step(d, 700f).Bytes}");
            // A horizon tile or a far one with no items and nothing else: no model.
            var empty = new FootprintInput();
            empty.Tiles.Add(Tile(100f, 0, TileTier.Far, 5000));
            empty.Tiles.Add(Tile(100f, 3, TileTier.Horizon, 50));
            FootprintReport m = Footprint.Build(empty);
            Program.Check(!m.HasModel && Step(m, 700f).Bytes == -1 && m.MeshBytes == 5050, "no near or far tile with items: no model, though meshes are held");
            // An unbuilt tile far beyond all the steps is no part of any.
            FootprintInput faraway = Scene();
            faraway.Tiles.Add(Tile(9000f, 500, TileTier.None, 0));
            Program.Check(Step(Footprint.Build(faraway), 3000f).Bytes == Step(Footprint.Build(Scene()), 3000f).Bytes, "a tile at 9 km is in no step");
        }

        private static void Recommendation()
        {
            // A card of 4 GB: the budget is 5 % of it. Tiles of 10 MB of meshes at 1 km each, 100 items: the steps cost 10 MB for every kilometre of them, by the far tiles' own cost.
            long budget = Footprint.BudgetFor(4096 * MB);
            var input = new FootprintInput { VramBytes = 4096 * MB, DetailBytes = 1 * MB };
            input.Tiles.Add(Tile(100f, 100, TileTier.Far, budget / 8));
            input.Tiles.Add(Tile(450f, 100, TileTier.Far, budget / 8));
            for (int i = 0; i < 6; i++)
                input.Tiles.Add(Tile(800f + i * 400f, 100, TileTier.None, 0));
            FootprintReport r = Footprint.Build(input);
            Program.Check(r.Budget == budget && r.HasModel, "the card's budget");
            // Every item costs (budget/4)/200 bytes: the steps hold the tiles within them, 100 items each.
            long perTile = budget / 8 * 8 / 8;
            foreach (StepCost step in r.Steps)
                Program.Check(step.Level == Footprint.LevelOf(step.Bytes, budget), $"step {step.Far:0}: {step.Bytes / (double)MB:0.0} MB is {step.Level}");
            float expected = 0f;
            foreach (StepCost step in r.Steps)
            {
                if (step.Bytes <= budget)
                    expected = step.Far;
            }

            Program.Check(r.Recommended == expected && expected > 300f && expected < 3000f, $"the recommended step is the farthest that is within the budget: {r.Recommended:0} of {budget / (double)MB:0} MB");
            Program.Check(Step(r, r.Recommended).Level != FootprintLevel.Heavy && r.Steps.FindAll(s => s.Far > r.Recommended).TrueForAll(s => s.Level == FootprintLevel.Heavy), "it is not heavy and every step beyond it is");
            // All fits: the farthest.
            var light = new FootprintInput { VramBytes = 8192 * MB };
            light.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            light.Tiles.Add(Tile(1500f, 10, TileTier.None, 0));
            Program.Check(Footprint.Build(light).Recommended == 3000f, "everything within the budget: the farthest step");
            // Nothing fits: the nearest step (the smallest harm), and it is heavy.
            var huge = new FootprintInput { VramBytes = 8192 * MB, DetailBytes = 2048 * MB };
            huge.Tiles.Add(Tile(100f, 10, TileTier.Far, 1000));
            FootprintReport h = Footprint.Build(huge);
            Program.Check(h.Recommended == 300f && Step(h, 300f).Level == FootprintLevel.Heavy, "the fixed textures alone are over the budget: the nearest step is the advice, and it is heavy");
        }

        private static void Json()
        {
            FootprintReport none = Footprint.Build(new FootprintInput());
            Program.Check(!none.HasModel && none.Recommended == 0f && none.Steps.Count == 7 && none.Steps.TrueForAll(s => s.Bytes == -1), "nothing built: no model, no advice, every step unknown");
            Program.Check(none.Steps.TrueForAll(s => s.Level == FootprintLevel.Heavy), "an unknown step carries the heavy level, so that nothing paints it as light");
            Program.Check(MiniJson.TryParse(none.ToJson(), out JsonValue unknown, out string error) && unknown.Get("ok").Bool == false && unknown.Get("steps").Items[0].Get("m").Number == -1, "the text of it parses: " + error);

            FootprintInput input = Scene();
            input.VramBytes = 8192 * MB;
            FootprintReport report = Footprint.Build(input);
            Program.Check(MiniJson.TryParse(report.ToJson(), out JsonValue v, out error), "the report's text is JSON: " + error);
            Program.Check(v.Get("v").Number == 19900 && v.Get("r").Number == 3500 && v.Get("b").Number == report.Budget && v.Get("t").Number == 8192 * MB && v.Get("lv").Number == 0, "volumes, memory, budget, the card and the level");
            Program.Check(v.Get("mesh").Number == 9000 && v.Get("face").Number == 10200 && v.Get("det").Number == 700 && v.Get("item").Number == 3000 && v.Get("pic").Number == 500, "the parts");
            Program.Check(v.Get("rend").Number == 12 && v.Get("tiles").Number == 4 && v.Get("items").Number == 100 && v.Get("ok").Bool == true, "the counts");
            JsonValue steps = v.Get("steps");
            Program.Check(steps.Items.Count == 7 && steps.Items[0].Get("d").Number == 300 && steps.Items[6].Get("d").Number == 3000 && steps.Items[2].Get("m").Number == 19900 && steps.Items[2].Get("l").Number == 0, "seven steps with their far limit, bytes and level");
            Program.Check(v.Get("rec").Number == report.Recommended && report.Recommended == 3000f, "the recommended step: the card is large and the scene small");
            bool all = true;
            for (int i = 0; i < steps.Items.Count; i++)
                all &= steps.Items[i].Get("m").Number == report.Steps[i].Bytes && steps.Items[i].Get("l").Number == (int)report.Steps[i].Level;
            Program.Check(all, "each step's bytes and level are the report's");
            // A card on which the levels differ from step to step: each level is written as it is.
            var mixed = new FootprintInput { VramBytes = 4096 * MB };
            long budget = Footprint.BudgetFor(4096 * MB);
            mixed.Tiles.Add(Tile(100f, 100, TileTier.Far, budget / 8));
            mixed.Tiles.Add(Tile(450f, 100, TileTier.Far, budget / 8));
            for (int i = 0; i < 6; i++)
                mixed.Tiles.Add(Tile(800f + i * 400f, 100, TileTier.None, 0));
            FootprintReport m = Footprint.Build(mixed);
            Program.Check(MiniJson.TryParse(m.ToJson(), out JsonValue mv, out error), "the mixed report is JSON: " + error);
            var seen = new HashSet<int>();
            bool match = true;
            for (int i = 0; i < m.Steps.Count; i++)
            {
                seen.Add((int)mv.Get("steps").Items[i].Get("l").Number);
                match &= (int)mv.Get("steps").Items[i].Get("l").Number == (int)m.Steps[i].Level;
            }

            Program.Check(match && seen.Count >= 2, $"the levels of the steps are written as they are ({seen.Count} different)");
        }
    }
}
