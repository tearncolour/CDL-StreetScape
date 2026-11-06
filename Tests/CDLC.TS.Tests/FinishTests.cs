using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 (the owner: "杆件涂装选项（黑色哑光、深灰、浅灰），以及黑色牌背"): a support's finish draws its steel in its colour in a tile, alone and in a group; black backs on another steel are drawn
    /// apart in graphite; the style is in the parts' caches' keys.
    /// </summary>
    internal static class FinishTests
    {
        public static void RunAll()
        {
            Program.Run("finish: the steel in its colour, alone and in a group", Tiles);
            Program.Run("finish: black backs drawn apart", Backs);
        }

        private static ItemRecord Item(string id, SupportStyle style, uint group = 0u, int slot = 0)
        {
            var record = new ItemRecord(SignCatalog.ById(id).Hash, new Vec3(0f, 0f, 0f), 0f, 0u, null, 0f, group, slot);
            record.Style = style.Packed;
            return record;
        }

        private static Dictionary<string, int> Materials(IEnumerable<ItemRecord> items)
        {
            var counts = new Dictionary<string, int>();
            foreach (ChunkPart part in ChunkBuilder.Build(items, Vec3.Zero, out _))
                counts[part.Material] = (counts.TryGetValue(part.Material, out int n) ? n : 0) + part.Mesh.VertexCount;
            return counts;
        }

        private static void Tiles()
        {
            string graphite = ChunkBuilder.SolidMaterial(SolidColour.Graphite), light = ChunkBuilder.SolidMaterial(SolidColour.PoleLightGrey);
            Dictionary<string, int> plain = Materials(new[] { Item("gb.stop", default) });
            Dictionary<string, int> black = Materials(new[] { Item("gb.stop", new SupportStyle { Finish = SupportFinish.Graphite }) });
            Program.Check(plain.ContainsKey(ChunkBuilder.MetalMaterial) && !plain.ContainsKey(graphite), "galvanised: the steel's material");
            Program.Check(!black.ContainsKey(ChunkBuilder.MetalMaterial) && black.TryGetValue(graphite, out int g) && g == plain[ChunkBuilder.MetalMaterial], "graphite: all of the steel in graphite, nothing in the steel's material");
            Program.Check(black[ChunkBuilder.ConcreteMaterial] == plain[ChunkBuilder.ConcreteMaterial], "the foundation as it was");
            Dictionary<string, int> group = Materials(new[] { Item("gb.prohib.01", new SupportStyle { Finish = SupportFinish.LightGrey }, 7u, 0), Item("gb.warn.01", new SupportStyle { Finish = SupportFinish.LightGrey }, 7u, 1) });
            Program.Check(group.ContainsKey(light) && !group.ContainsKey(ChunkBuilder.MetalMaterial), "a group's support in its lead's finish");
            SignParts a = ChunkBuilder.PartsOfRecord(Item("gb.stop", default)), b = ChunkBuilder.PartsOfRecord(Item("gb.stop", new SupportStyle { Finish = SupportFinish.DarkGrey }));
            Program.Check(!ReferenceEquals(a, b) && a.MetalColour == SolidColour.Metal && b.MetalColour == SolidColour.PoleDarkGrey, "the single-board cache keeps the styles apart");
        }

        private static void Backs()
        {
            string graphite = ChunkBuilder.SolidMaterial(SolidColour.Graphite);
            SignParts plain = ChunkBuilder.PartsOfRecord(Item("gb.stop", default));
            SignParts backs = ChunkBuilder.PartsOfRecord(Item("gb.stop", new SupportStyle { BlackBack = true }));
            MeshBuilder back = backs.Solids[(int)SolidColour.Graphite];
            Program.Check(back != null && back.VertexCount > 0 && backs.Metal.VertexCount + back.VertexCount == plain.Metal.VertexCount, "the backs and edges out of the steel, into graphite: as many vertices in all");
            back.Bounds(out Vec3 lo, out Vec3 hi);
            backs.Face.Bounds(out Vec3 flo, out Vec3 fhi);
            Program.Check(hi.Z <= fhi.Z + 1e-3f && lo.Z >= backs.Layout.BackZ - 1e-3f && hi.Y > flo.Y, "behind the face, from the board's back");
            Dictionary<string, int> tile = Materials(new[] { Item("gb.stop", new SupportStyle { BlackBack = true }) });
            Program.Check(tile.ContainsKey(ChunkBuilder.MetalMaterial) && tile.ContainsKey(graphite), "in a tile: the steel and the black backs");
            SignParts both = ChunkBuilder.PartsOfRecord(Item("gb.stop", new SupportStyle { Finish = SupportFinish.Graphite, BlackBack = true }));
            Program.Check(both.Solids[(int)SolidColour.Graphite] == null && both.Metal.VertexCount == plain.Metal.VertexCount, "on graphite steel the backs stay in the steel");
        }
    }
}
