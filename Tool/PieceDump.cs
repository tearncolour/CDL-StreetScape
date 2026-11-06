using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace CDLC.TS
{
    /// <summary>
    /// What route C of the kerb styles needs to know of the game (0.12.33, scratch/research-kerb-c/REPORT.md): the sections of some of the game's roads with their pieces and requirements, the sections that
    /// use a "- Flat" piece, and the meshes of the footway and flat pieces vertex by vertex (layout, positions, normals, tangents, texture coordinates, triangles, LODs). Written once a version, at the
    /// first city, to Logs/CDLC.TS.pieces-&lt;version&gt;.txt. Read only. Main thread.
    /// </summary>
    internal static class PieceDump
    {
        private static readonly string[] kRoads = { "Small Road", "Medium Road", "Large Road", "Alley" };
        private const int kMaxMeshes = 12;

        /// <summary>
        /// Writes the dump when there is none for this version. False when the game is still loading the geometry of a piece to be dumped (reading it then breaks that loading for good: GeometryGate): nothing
        /// is written and the caller tries again later; true when there is nothing more to do.
        /// </summary>
        internal static bool WriteOnce(EntityManager em, PrefabSystem prefabs)
        {
            string path = Path.Combine(Application.persistentDataPath, "Logs", $"CDLC.TS.pieces-{Mod.kVersion}.txt");
            if (File.Exists(path))
                return true;
            var clock = Stopwatch.StartNew();
            var text = new StringBuilder();
            var pieces = new List<NetPiecePrefab>();
            bool busy = false;
            try
            {
                foreach (string name in kRoads)
                {
                    if (!prefabs.TryGetPrefab(new PrefabID(nameof(RoadPrefab), name), out PrefabBase found) || !(found is NetGeometryPrefab road))
                    {
                        text.Append("road ").Append(name).AppendLine(": not found");
                        continue;
                    }

                    text.Append("road ").AppendLine(name);
                    foreach (NetSectionInfo info in road.m_Sections)
                    {
                        text.Append("  section ").Append(info.m_Section?.name).Append(" invert ").Append(info.m_Invert).Append(" flip ").Append(info.m_Flip).Append(" median ").Append(info.m_Median)
                            .Append(" half ").Append(info.m_HalfLength).Append(" offset ").Append(F(info.m_Offset.x)).Append(',').Append(F(info.m_Offset.y)).Append(',').Append(F(info.m_Offset.z))
                            .Append(" hidden ").Append(info.m_HiddenLayers).Append(Requirements(info.m_RequireAll, info.m_RequireAny, info.m_RequireNone)).AppendLine();
                        Section(text, info.m_Section, "    ", pieces);
                    }
                }

                using (EntityQuery query = em.CreateEntityQuery(ComponentType.ReadOnly<PrefabData>(), ComponentType.ReadOnly<NetSectionData>()))
                using (NativeArray<Entity> sections = query.ToEntityArray(Allocator.Temp))
                {
                    text.AppendLine("sections with a flat piece");
                    foreach (Entity e in sections)
                    {
                        if (!prefabs.TryGetPrefab(e, out NetSectionPrefab section) || section.m_Pieces == null)
                            continue;
                        foreach (NetPieceInfo info in section.m_Pieces)
                        {
                            if (info.m_Piece == null || info.m_Piece.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            text.Append("  ").Append(section.name).Append(": ").Append(info.m_Piece.name).Append(Requirements(info.m_RequireAll, info.m_RequireAny, info.m_RequireNone)).AppendLine();
                            if (!pieces.Contains(info.m_Piece))
                                pieces.Add(info.m_Piece);
                        }
                    }
                }

                int meshes = 0;
                foreach (NetPiecePrefab piece in pieces)
                {
                    text.Append("piece ").Append(piece.name).Append(" layer ").Append(piece.m_Layer).Append(" width ").Append(F(piece.m_Width)).Append(" length ").Append(F(piece.m_Length))
                        .Append(" heights ").Append(F(piece.m_HeightRange.min)).Append("..").Append(F(piece.m_HeightRange.max)).Append(" surface ").Append(F(piece.m_SurfaceHeights.x)).Append(',')
                        .Append(F(piece.m_SurfaceHeights.y)).Append(',').Append(F(piece.m_SurfaceHeights.z)).Append(',').Append(F(piece.m_SurfaceHeights.w)).Append(" widthOffset ").Append(F(piece.m_WidthOffset))
                        .Append(" nodeOffset ").Append(F(piece.m_NodeOffset)).Append(" vertices ").Append(piece.vertexCount).Append(" bounds ").Append(F(piece.bounds.min.x)).Append(',').Append(F(piece.bounds.min.y))
                        .Append(',').Append(F(piece.bounds.min.z)).Append("..").Append(F(piece.bounds.max.x)).Append(',').Append(F(piece.bounds.max.y)).Append(',').Append(F(piece.bounds.max.z));
                    for (int i = 0; i < piece.materialCount; i++)
                        text.Append(" material ").Append(piece.GetSurfaceAsset(i)?.name);
                    if (piece.TryGet(out LodProperties lods) && lods.m_LodMeshes != null)
                    {
                        foreach (RenderPrefab lod in lods.m_LodMeshes)
                            text.Append(" lod ").Append(lod?.name).Append(':').Append(lod != null ? lod.vertexCount : 0);
                    }

                    text.Append(" components");
                    foreach (ComponentBase component in piece.components)
                        text.Append(' ').Append(component.GetType().Name);
                    text.AppendLine();
                    bool wanted = piece.name.IndexOf("Sidewalk", StringComparison.OrdinalIgnoreCase) >= 0 || piece.name.IndexOf("Flat", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (wanted && meshes < kMaxMeshes && piece.hasGeometryAsset)
                    {
                        meshes++;
                        if (!Meshes(text, piece))
                        {
                            busy = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                text.AppendLine("failed: " + ex);
            }

            if (busy)
                return false;
            try
            {
                File.WriteAllText(path, text.ToString());
                Mod.log.Info($"Kerb styles: the game's road pieces written to {path} in {clock.ElapsedMilliseconds} ms ({pieces.Count} piece(s)).");
            }
            catch (Exception ex)
            {
                Mod.log.Warn("Kerb styles: the road pieces could not be written: " + ex.Message);
            }

            return true;
        }

        private static void Section(StringBuilder text, NetSectionPrefab section, string indent, List<NetPiecePrefab> pieces)
        {
            if (section == null)
                return;
            if (section.m_Pieces != null)
            {
                foreach (NetPieceInfo info in section.m_Pieces)
                {
                    text.Append(indent).Append("piece ").Append(info.m_Piece?.name).Append(" offset ").Append(F(info.m_Offset.x)).Append(',').Append(F(info.m_Offset.y)).Append(',').Append(F(info.m_Offset.z))
                        .Append(Requirements(info.m_RequireAll, info.m_RequireAny, info.m_RequireNone)).AppendLine();
                    if (info.m_Piece != null && !pieces.Contains(info.m_Piece))
                        pieces.Add(info.m_Piece);
                }
            }

            if (section.m_SubSections == null)
                return;
            foreach (NetSubSectionInfo sub in section.m_SubSections)
            {
                text.Append(indent).Append("sub ").Append(sub.m_Section?.name).Append(Requirements(sub.m_RequireAll, sub.m_RequireAny, sub.m_RequireNone)).AppendLine();
                Section(text, sub.m_Section, indent + "  ", pieces);
            }
        }

        /// <summary>
        /// The piece's meshes: their layout, every vertex and the triangles of each sub-mesh. The meshes are obtained and released again (the game counts who holds them). False, and nothing obtained, when the
        /// game is still loading the piece's geometry.
        /// </summary>
        private static bool Meshes(StringBuilder text, RenderPrefab piece)
        {
            if (!GeometryGate.CanRead(SurfaceReader.StateOf(piece.geometryAsset)))
                return false;
            Mesh[] meshes;
            try
            {
                meshes = piece.ObtainMeshes();
            }
            catch (Exception ex)
            {
                // As in SurfaceReader.ReadMesh: a geometry asset without vertex data; nothing was obtained, so nothing is released.
                text.Append("  meshes could not be read: ").Append(ex.GetType().Name).Append(": ").AppendLine(ex.Message);
                return true;
            }

            if (meshes == null)
            {
                text.AppendLine("  no meshes");
                return true;
            }

            try
            {
                foreach (Mesh mesh in meshes)
                {
                    text.Append("  mesh ").Append(mesh.name).Append(" readable ").Append(mesh.isReadable).Append(" vertices ").Append(mesh.vertexCount).Append(" submeshes ").Append(mesh.subMeshCount)
                        .Append(" bounds ").Append(V(mesh.bounds.min)).Append("..").Append(V(mesh.bounds.max)).Append(" layout");
                    foreach (VertexAttributeDescriptor a in mesh.GetVertexAttributes())
                        text.Append(' ').Append(a.attribute).Append(':').Append(a.format).Append('x').Append(a.dimension).Append("@s").Append(a.stream);
                    text.AppendLine();
                    if (!mesh.isReadable)
                        continue;
                    Vector3[] positions = mesh.vertices, normals = mesh.normals;
                    Vector4[] tangents = mesh.tangents;
                    Color32[] colours = mesh.colors32;
                    var uvs = new List<Vector4>[4];
                    for (int channel = 0; channel < 4; channel++)
                    {
                        uvs[channel] = new List<Vector4>();
                        if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
                            mesh.GetUVs(channel, uvs[channel]);
                    }

                    for (int i = 0; i < positions.Length; i++)
                    {
                        text.Append("    v").Append(i).Append(" p ").Append(V(positions[i]));
                        if (normals.Length == positions.Length)
                            text.Append(" n ").Append(V(normals[i]));
                        if (tangents.Length == positions.Length)
                            text.Append(" t ").Append(V(tangents[i])).Append(',').Append(F(tangents[i].w));
                        for (int channel = 0; channel < 4; channel++)
                        {
                            if (uvs[channel].Count == positions.Length)
                                text.Append(" uv").Append(channel).Append(' ').Append(F(uvs[channel][i].x)).Append(',').Append(F(uvs[channel][i].y)).Append(',').Append(F(uvs[channel][i].z)).Append(',').Append(F(uvs[channel][i].w));
                        }

                        if (colours.Length == positions.Length)
                            text.Append(" c ").Append(colours[i].r).Append(',').Append(colours[i].g).Append(',').Append(colours[i].b).Append(',').Append(colours[i].a);
                        text.AppendLine();
                    }

                    for (int s = 0; s < mesh.subMeshCount; s++)
                    {
                        text.Append("    triangles ").Append(s).Append(':');
                        foreach (int index in mesh.GetTriangles(s))
                            text.Append(' ').Append(index);
                        text.AppendLine();
                    }
                }
            }
            finally
            {
                piece.ReleaseMeshes();
            }

            return true;
        }

        private static string Requirements(NetPieceRequirements[] all, NetPieceRequirements[] any, NetPieceRequirements[] none)
        {
            string List(NetPieceRequirements[] list) => list == null || list.Length == 0 ? "" : string.Join("|", list);
            return $" all [{List(all)}] any [{List(any)}] none [{List(none)}]";
        }

        private static string F(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    }
}
