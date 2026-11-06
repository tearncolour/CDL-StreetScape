using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The picture sizes of the faces and the tiers of a tile (0.7.0): the materials are named for their detail, the pictures are painted at a size that fits it, and a tile in each tier is the same signs in the same places.</summary>
    internal static class FaceDetailTests
    {
        public static void RunAll()
        {
            Program.Run("face detail: the names of the materials", Names);
            Program.Run("face detail: the size of a picture at each detail", Sizes);
            Program.Run("face detail: the colour a face has before its picture is there", Placeholders);
            Program.Run("tile detail: a tile in each tier is the same signs, with the faces and the metal of its tier", Tiles);
        }

        private static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            if (def == null)
                throw new InvalidOperationException("no sign " + id);
            return def;
        }

        private static void Names()
        {
            foreach (string key in new[] { "gb.prohib.09", "guide.exit.advance#road=g;code=G15;to1=北京;to2=上海", "a#face1:b", "x#face2:y;z=1", "#" })
            {
                foreach (int detail in new[] { FaceDetail.Full, FaceDetail.Half, FaceDetail.Small })
                {
                    string material = FaceDetail.Material(key, detail);
                    Program.Check(FaceDetail.IsFace(material) && FaceDetail.TryParse(material, out string back, out int got) && back == key && got == detail, $"'{key}' at {detail}: round trip of {material}");
                }
            }

            Program.Check(FaceDetail.Material("k", FaceDetail.Full) == ChunkBuilder.FacePrefix + "k", "full size keeps the name it always had");
            Program.Check(FaceDetail.Material("k", FaceDetail.Full) != FaceDetail.Material("k", FaceDetail.Half) && FaceDetail.Material("k", FaceDetail.Half) != FaceDetail.Material("k", FaceDetail.Small), "three details are three materials");
            foreach (string notFace in new[] { ChunkBuilder.MetalMaterial, ChunkBuilder.ConcreteMaterial, ChunkBuilder.LedPrefix + "2", "", "faces:x", "Face:x" })
                Program.Check(!FaceDetail.IsFace(notFace) && !FaceDetail.TryParse(notFace, out _, out _), "'" + notFace + "' is not a face");
            Program.Check(!FaceDetail.TryParse(null, out _, out _), "null is not a face");
        }

        private static void Sizes()
        {
            Program.Check(FaceDetail.MaxSide(FaceDetail.Full) == SignInstance.MaxTexture && FaceDetail.MaxSide(FaceDetail.Half) < FaceDetail.MaxSide(FaceDetail.Full) && FaceDetail.MaxSide(FaceDetail.Small) < FaceDetail.MaxSide(FaceDetail.Half), "the sizes go down");
            Program.Check(FaceDetail.OfTier(TileTier.Near) == FaceDetail.Full && FaceDetail.OfTier(TileTier.Mid) == FaceDetail.Half && FaceDetail.OfTier(TileTier.Far) == FaceDetail.Small, "near, middle, far");
            Program.Check(FaceDetail.SupportOf(TileTier.Near) == SupportDetail.Full && FaceDetail.SupportOf(TileTier.Mid) == SupportDetail.Full && FaceDetail.SupportOf(TileTier.Far) == SupportDetail.Reduced, "only far tiles have reduced supports");

            long fullBytes = 0, halfBytes = 0, smallBytes = 0;
            int checkedBoards = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                SignInstance instance = SignInstances.Of(def, null);
                if (instance.Board == null)
                    continue;
                checkedBoards++;
                foreach (int detail in new[] { FaceDetail.Full, FaceDetail.Half, FaceDetail.Small })
                {
                    int max = FaceDetail.MaxSide(detail);
                    SignInstance sized = FaceDetail.Sized(instance, max);
                    string what = def.Id + " at " + detail;
                    Program.Check(sized.TexW <= Math.Max(max, instance.TexW) && sized.TexH <= Math.Max(max, instance.TexH), what + ": never larger than the original");
                    Program.Check(Math.Max(sized.TexW, sized.TexH) <= Math.Max(max, 8) + 4 || Math.Max(instance.TexW, instance.TexH) <= max, what + ": the longest side fits: " + sized.TexW + "x" + sized.TexH);
                    Program.Check(sized.TexW >= 8 && sized.TexH >= 8 && sized.TexW % 4 == 0 && sized.TexH % 4 == 0, what + ": at least 8 pixels and a multiple of 4: " + sized.TexW + "x" + sized.TexH);
                    Program.Check(ReferenceEquals(sized.Board, instance.Board) && sized.Key == instance.Key && sized.Spec == instance.Spec && sized.BoundsX1 == instance.BoundsX1 && sized.BoundsY0 == instance.BoundsY0, what + ": the same board");
                    if (Math.Max(instance.TexW, instance.TexH) > max)
                    {
                        double aspect = (double)instance.TexW / instance.TexH, sizedAspect = (double)sized.TexW / sized.TexH;
                        Program.Check(Math.Abs(aspect - sizedAspect) / aspect < 0.25, what + $": the shape is kept: {aspect:0.00} / {sizedAspect:0.00}");
                    }
                    else
                    {
                        Program.Check(ReferenceEquals(sized, instance), what + ": a picture that is small enough is not copied");
                    }

                    long bytes = (long)sized.TexW * sized.TexH * 4 * 4 / 3;
                    if (detail == FaceDetail.Full) fullBytes += bytes; else if (detail == FaceDetail.Half) halfBytes += bytes; else smallBytes += bytes;
                }
            }

            Console.WriteLine($"  face detail: {checkedBoards} boards: pictures of {fullBytes / 1048576} MB at full size, {halfBytes / 1048576} MB at half, {smallBytes / 1048576} MB small");
            Program.Check(smallBytes * 20 < fullBytes && halfBytes * 2 < fullBytes, $"small pictures are a small part of full ones: {smallBytes} / {halfBytes} / {fullBytes}");
            Program.Check(FaceDetail.Sized(null, 96) == null, "no instance, no picture");
            SignInstance classic = SignInstances.Of(Def("gb.prohib.09"), null);
            Program.Check(classic.Board != null, "(the catalogue's signs are boards)");
        }

        private static void Placeholders()
        {
            foreach (SignDef def in SignCatalog.All)
            {
                SignInstance instance = SignInstances.Of(def, null);
                FaceDetail.Placeholder(instance, out byte r, out byte g, out byte b);
                if (instance.IsLed)
                {
                    Program.Check(r < 40 && g < 40 && b < 40, def.Id + ": an LED screen is dark");
                }
                else if (instance.Board != null)
                {
                    GuideColours.Rgb(instance.Board.Main.Ground, out byte er, out byte eg, out byte eb);
                    Program.Check(r == er && g == eg && b == eb, def.Id + ": the ground colour of the main board");
                }
            }

            FaceDetail.Placeholder(null, out byte nr, out byte ng, out byte nb);
            Program.Check(nr == nb && nr == ng && nr > 100 && nr < 200, "nothing known: a grey");

            SignInstance led = SignInstances.Of(Def("gb.vms.lane"), null);
            SignInstance board = SignInstances.Of(Def("gb.prohib.09"), null);
            Program.Check(led.IsLed && !board.IsLed, "(the test signs are what they should be)");
            Program.Check(FaceDetail.NeverPainted(led, FaceDetail.Small) && !FaceDetail.NeverPainted(led, FaceDetail.Half) && !FaceDetail.NeverPainted(led, FaceDetail.Full) && !FaceDetail.NeverPainted(board, FaceDetail.Small) && !FaceDetail.NeverPainted(null, FaceDetail.Small), "only an LED screen seen from afar is never painted");
        }

        private static ItemRecord Rec(string id, float x, float z, string spec = null, uint group = 0u, int slot = 0, uint support = 0u, int lane = 0) =>
            new ItemRecord(Def(id).Hash, new Vec3(x, 0f, z), 0.3f, support, spec, 0f, group, slot, lane);

        private static void Tiles()
        {
            Func<SignInstance, LedGrid> keep = SignBuilder.LedGrids;
            try
            {
                SignBuilder.LedGrids = i =>
                {
                    var grid = new LedGrid(6, 4);
                    grid.Dots[1] = LedColour.Red;
                    grid.Dots[8] = LedColour.Green;
                    return grid;
                };

                var items = new List<ItemRecord>
                {
                    Rec("gb.prohib.09", 10f, 10f),
                    Rec("gb.warn.01", 20f, 12f),
                    Rec("guide.exit.advance", 30f, 14f),
                    Rec("gb.guide8.01", 40f, 16f),
                    Rec("gb.vms.lane", 50f, 18f, null, 0u, 0, SupportCatalog.ById(SupportCatalog.Gantry).Hash),
                    Rec("gb.prohib.09", 60f, 20f, null, 700u, 0),
                    Rec("gb.warn.01", 60f, 20f, null, 700u, 1),
                    Rec("gb.prohib.09", 70f, 22f, null, 0u, 0, 0u, 2)
                };

                var origin = new Vec3(40f, 0f, 16f);
                List<ChunkPart> full = ChunkBuilder.Build(items, origin, out int skippedFull);
                List<ChunkPart> same = ChunkBuilder.BuildDetailed(items, origin, ChunkDetail.Full, out int skippedSame);
                Program.Check(Describe(full) == Describe(same) && skippedFull == skippedSame, "Build is BuildDetailed at full detail");
                Program.Check(skippedFull == 0, "nothing is skipped");

                int ledFull = Count(full, p => p.Material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal));
                Program.Check(ledFull > 0, "the full tile has LED dots: " + ledFull);

                foreach (TileTier tier in new[] { TileTier.Near, TileTier.Mid, TileTier.Far })
                {
                    ChunkDetail detail = ChunkDetail.Of(tier);
                    List<ChunkPart> parts = ChunkBuilder.BuildDetailed(items, origin, detail, out int skipped);
                    string what = "tier " + tier;
                    Program.Check(skipped == 0, what + ": nothing is skipped");

                    // The faces: the same boards, at the detail of the tier, the same number of vertices.
                    var fullFaces = new Dictionary<string, int>();
                    foreach (ChunkPart part in full)
                    {
                        if (FaceDetail.TryParse(part.Material, out string key, out _))
                            fullFaces[key] = (fullFaces.TryGetValue(key, out int n) ? n : 0) + part.Mesh.VertexCount;
                    }

                    var faces = new Dictionary<string, int>();
                    foreach (ChunkPart part in parts)
                    {
                        if (!FaceDetail.TryParse(part.Material, out string key, out int got))
                            continue;
                        Program.Check(got == detail.FaceDetail, what + ": " + part.Material + " is at the detail of the tier");
                        faces[key] = (faces.TryGetValue(key, out int n) ? n : 0) + part.Mesh.VertexCount;
                    }

                    Program.Check(faces.Count == fullFaces.Count, what + ": the same boards: " + faces.Count + " / " + fullFaces.Count);
                    foreach (KeyValuePair<string, int> pair in fullFaces)
                        Program.Check(faces.TryGetValue(pair.Key, out int count) && count == pair.Value, what + ": the face of " + pair.Key + " has the same vertices");

                    int led = Count(parts, p => p.Material.StartsWith(ChunkBuilder.LedPrefix, StringComparison.Ordinal));
                    int metal = Count(parts, p => p.Material == ChunkBuilder.MetalMaterial), metalFull = Count(full, p => p.Material == ChunkBuilder.MetalMaterial);
                    if (tier == TileTier.Far)
                    {
                        Program.Check(led == 0, what + ": no LED dots");
                        Program.Check(metal < metalFull * 0.5f, what + ": the metal is under half of the full: " + metal + " / " + metalFull);
                    }
                    else
                    {
                        Program.Check(led == ledFull && metal == metalFull, what + ": the LED dots and the metal are those of the full tile");
                    }

                    // Every vertex of every part is inside the tile's neighbourhood (finite), and the indices are inside the meshes.
                    foreach (ChunkPart part in parts)
                    {
                        Program.Check(part.Mesh.VertexCount <= ChunkBuilder.MaxVertices && part.Mesh.Indices.Count % 3 == 0, what + ": " + part.Material + " is a mesh of a legal size");
                        foreach (int index in part.Mesh.Indices)
                        {
                            if (index < 0 || index >= part.Mesh.VertexCount)
                            {
                                Program.Check(false, what + ": " + part.Material + " has an index outside its mesh");
                                break;
                            }
                        }
                    }
                }
            }
            finally
            {
                SignBuilder.LedGrids = keep;
            }
        }

        private static int Count(List<ChunkPart> parts, Func<ChunkPart, bool> match)
        {
            int n = 0;
            foreach (ChunkPart part in parts)
            {
                if (match(part))
                    n += part.Mesh.VertexCount;
            }

            return n;
        }

        private static string Describe(List<ChunkPart> parts)
        {
            var lines = new List<string>();
            foreach (ChunkPart part in parts)
                lines.Add(part.Material + ":" + part.Mesh.VertexCount + ":" + part.Mesh.Indices.Count);
            lines.Sort(StringComparer.Ordinal);
            return string.Join("|", lines);
        }
    }
}
