using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>How heavy a footprint is against the budget: up to half of it, up to all of it, over it.</summary>
    public enum FootprintLevel
    {
        Light = 0,
        Fine = 1,
        Heavy = 2
    }

    /// <summary>A tile as the advice sees it (ChunkSystem.Footprint): how far it is, how many items it has, the tier it is built at (None: not built) and what its meshes take.</summary>
    public struct TileCost
    {
        public float Distance;

        /// <summary>The items of the tile; 0 for a built tile whose items are gone (the plan drops it).</summary>
        public int Items;

        public TileTier Tier;
        public long MeshBytes;
    }

    /// <summary>What the mod holds and what the city asks of it, as the renderer and the materials report it (Footprint.Build).</summary>
    public sealed class FootprintInput
    {
        public readonly List<TileCost> Tiles = new List<TileCost>();

        /// <summary>The face pictures by the detail they were painted at (FaceDetail), bytes on the graphics card.</summary>
        public long FaceFull, FaceHalf, FaceSmall;

        /// <summary>The textures that do not depend on the tiles: the baked detail maps and the small flat ones.</summary>
        public long DetailBytes;

        /// <summary>What the mod holds in memory that was counted: the item records (an estimate by their number) and the kept custom pictures.</summary>
        public long ItemBytes, PictureBytes;

        public int Renderers, Items;

        /// <summary>The graphics card's memory (bytes); 0 when the game does not say.</summary>
        public long VramBytes;
    }

    /// <summary>What a far limit would take on the graphics card if the camera stayed where it is, and how heavy that is. Bytes is -1 when it cannot be told yet.</summary>
    public struct StepCost
    {
        public float Far;
        public long Bytes;
        public FootprintLevel Level;
    }

    /// <summary>The numbers the panel shows (Footprint.Build).</summary>
    public sealed class FootprintReport
    {
        public long MeshBytes, FaceBytes, DetailBytes, ItemBytes, PictureBytes;
        public long VramBytes, Budget;
        public int Renderers, Tiles, Items;
        public FootprintLevel Level;

        /// <summary>False until a tile is built: the cost of an item is not known, and no step has a figure.</summary>
        public bool HasModel;

        public readonly List<StepCost> Steps = new List<StepCost>();

        /// <summary>The farthest step that is within the budget (the nearest when none is); 0 when it cannot be told.</summary>
        public float Recommended;

        public long Vram => MeshBytes + FaceBytes + DetailBytes;

        public long Ram => ItemBytes + PictureBytes;

        /// <summary>The panel's binding: bytes as numbers, the card's memory in bytes (0 unknown), the steps with their far limit, bytes (-1 unknown) and level.</summary>
        public string ToJson()
        {
            var w = new JsonWriter();
            w.BeginObject();
            w.Key("v").Number(Vram).Key("r").Number(Ram).Key("b").Number(Budget).Key("t").Number(VramBytes).Key("lv").Number((long)Level);
            w.Key("mesh").Number(MeshBytes).Key("face").Number(FaceBytes).Key("det").Number(DetailBytes).Key("item").Number(ItemBytes).Key("pic").Number(PictureBytes);
            w.Key("rend").Number(Renderers).Key("tiles").Number(Tiles).Key("items").Number(Items).Key("ok").Bool(HasModel);
            w.Key("rec").Number(Recommended);
            w.Key("steps").BeginArray();
            foreach (StepCost step in Steps)
                w.BeginObject().Key("d").Number(step.Far).Key("m").Number(step.Bytes).Key("l").Number((long)step.Level).EndObject();
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }
    }

    /// <summary>
    /// What the mod's meshes and pictures take, and what each draw distance would take (0.12.104; the owner: "在设置中加入一个当前Mod内存显存占用，并给出档位建议"). Only what the mod holds and can count is here:
    /// the meshes of its tiles, its pictures and textures on the graphics card; the item records and the kept custom pictures in memory. The game's own memory and the other mods' cannot be told from
    /// this mod's (0.12.3 put the game's heap beside the mod's pictures and the owner took it for the mod's), and are not here. The cost of a step is an estimate by the cost per item that was
    /// measured on the tiles that are built, with the camera where it is. Pure.
    /// </summary>
    public static class Footprint
    {
        /// <summary>Position, normal, tangent and one texture coordinate as floats, which is how MeshUpload makes every mesh: 12 + 12 + 16 + 8 bytes a vertex.</summary>
        public const int VertexStride = 48;

        /// <summary>A mesh with this many vertices or fewer has 16-bit indices (MeshUpload), more has 32-bit ones.</summary>
        public const int Index16MaxVertices = 65000;

        /// <summary>The graphics memory a mesh takes: its vertices and its indices (the CPU copy is let go at the upload).</summary>
        public static long MeshBytes(int vertices, int indices) => vertices * (long)VertexStride + indices * (vertices <= Index16MaxVertices ? 2L : 4L);

        /// <summary>
        /// What an item record takes in memory, about: the struct (some 110 bytes), its entry in the store's dictionary and the tile's key set (some 50), and its text (a string of 22 bytes and two for each
        /// character). An estimate by size, not a measurement: the count of the items is exact.
        /// </summary>
        public const int ItemRecordBytes = 160;

        public static long ItemBytes(int specChars) => ItemRecordBytes + 22L + 2L * Math.Max(0, specChars);

        private const long MB = 1024L * 1024L;

        /// <summary>The budget when the game does not say how much graphics memory the card has (the same as the picture budget, TextureBudget), and the least and the most of it.</summary>
        public const long DefaultBudget = 384 * MB, LeastBudget = 192 * MB, MostBudget = 768 * MB;

        /// <summary>The share of the card's memory the mod may take within its budget.</summary>
        public const double BudgetShare = 0.05;

        /// <summary>The most the mod should take on the graphics card: 5 % of the card's memory, 192 MB at least and 768 MB at most; 384 MB when the card's memory is not known. The thresholds are a choice, not a measurement.</summary>
        public static long BudgetFor(long vramBytes) =>
            vramBytes <= 0 ? DefaultBudget : Math.Max(LeastBudget, Math.Min(MostBudget, (long)(vramBytes * BudgetShare)));

        /// <summary>Light up to half of the budget, fine up to the budget, heavy over it.</summary>
        public static FootprintLevel LevelOf(long bytes, long budget) => bytes * 2 <= budget ? FootprintLevel.Light : bytes <= budget ? FootprintLevel.Fine : FootprintLevel.Heavy;

        /// <summary>How many items of far tiles it takes to tell the cost of an item of a far tile from them; fewer, and the cost of an item of any tile is taken instead (dearer, for the near tiles are).</summary>
        public const int MinFarSample = 20;

        public static FootprintReport Build(FootprintInput input)
        {
            long budget = BudgetFor(input.VramBytes);
            var report = new FootprintReport
            {
                FaceBytes = input.FaceFull + input.FaceHalf + input.FaceSmall,
                DetailBytes = input.DetailBytes,
                ItemBytes = input.ItemBytes,
                PictureBytes = input.PictureBytes,
                VramBytes = input.VramBytes,
                Budget = budget,
                Renderers = input.Renderers,
                Items = input.Items
            };
            long farMesh = 0, allMesh = 0;
            int farItems = 0, allItems = 0, nearMidItems = 0;
            foreach (TileCost t in input.Tiles)
            {
                if (t.Tier == TileTier.None)
                    continue;
                report.Tiles++;
                report.MeshBytes += t.MeshBytes;
                if (t.Items <= 0 || t.Tier == TileTier.Horizon)
                    continue;
                allMesh += t.MeshBytes;
                allItems += t.Items;
                if (t.Tier == TileTier.Far)
                {
                    farMesh += t.MeshBytes;
                    farItems += t.Items;
                }
                else
                {
                    nearMidItems += t.Items;
                }
            }

            report.Level = LevelOf(report.Vram, budget);
            report.HasModel = allItems > 0;
            double meshPerItem = report.HasModel ? (farItems >= MinFarSample ? (double)farMesh / farItems : (double)allMesh / allItems) : 0.0;
            // Until a far tile is built a far picture is taken to cost what a near one does, per item: more than it will.
            double facePerItem = nearMidItems > 0 ? (double)(input.FaceFull + input.FaceHalf) / nearMidItems : 0.0;
            foreach (float far in StreamOptions.FarSteps)
            {
                long bytes = report.HasModel ? Predict(input, far, meshPerItem, facePerItem, farItems) : -1L;
                report.Steps.Add(new StepCost { Far = far, Bytes = bytes, Level = bytes < 0 ? FootprintLevel.Heavy : LevelOf(bytes, budget) });
            }

            if (report.HasModel)
            {
                report.Recommended = report.Steps[0].Far;
                foreach (StepCost step in report.Steps)
                {
                    if (step.Level != FootprintLevel.Heavy)
                        report.Recommended = step.Far;
                }
            }

            return report;
        }

        /// <summary>
        /// The graphics memory with a far limit, the camera where it is: the near and middle tiles, the far tiles inside the limit (as they are built, or by the cost of an item for those that are not),
        /// the horizon tiles beyond it as they are and the ones inside it as far tiles, the face pictures of near and middle size as they are and the small ones by the share of far items, and the
        /// textures that do not depend on the tiles. A built tile whose items are gone is dropped; a tile that is not built and has none of the camera's reach is not counted.
        /// </summary>
        private static long Predict(FootprintInput input, float far, double meshPerItem, double facePerItem, int farItemsBuilt)
        {
            double mesh = 0.0;
            long farItemsAt = 0;
            foreach (TileCost t in input.Tiles)
            {
                if (t.Items <= 0)
                    continue;
                switch (t.Tier)
                {
                    case TileTier.Near:
                    case TileTier.Mid:
                        mesh += t.MeshBytes;
                        break;
                    case TileTier.Far:
                        if (t.Distance < far)
                        {
                            mesh += t.MeshBytes;
                            farItemsAt += t.Items;
                        }

                        break;
                    case TileTier.Horizon:
                        if (t.Distance < far)
                        {
                            mesh += meshPerItem * t.Items;
                            farItemsAt += t.Items;
                        }
                        else
                        {
                            mesh += t.MeshBytes;
                        }

                        break;
                    default:
                        if (t.Distance < far)
                        {
                            mesh += meshPerItem * t.Items;
                            farItemsAt += t.Items;
                        }

                        break;
                }
            }

            double small = farItemsBuilt > 0 ? input.FaceSmall * (double)farItemsAt / farItemsBuilt : facePerItem * farItemsAt;
            return (long)Math.Round(mesh + input.FaceFull + input.FaceHalf + small + input.DetailBytes);
        }
    }
}
