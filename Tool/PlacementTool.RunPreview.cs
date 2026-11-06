using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// The preview of a line drawn along a road, item by item (0.12.51; the owner: "优化超长链摆放时候的掉帧问题"). Until 0.12.50 every item's meshes were made again at every half metre the end moved:
    /// 30 ms on .NET, more on the game's Mono, for 1 km of noise barrier (Tests/Bench, BENCH_DRAG). Now each item has its own renderers in the preview, kept while its settings and place stay the same;
    /// those that changed are made again from the end back, so the end follows the cursor and the rest catch up once it stops. While it moves the others differ only in the whole line's length in their
    /// settings, which shapes the line's ends. 0.12.65 (the owner: "性能占用不要超过1ms"; the game's profile had PlacementTool at 1.51 ms, and this part was 7.4 ms of the 116 frames of a drawn line
    /// that had to make an item, 66 ms at worst): the plan and the meshes of an item are made on a worker (RunBuilt), the main thread puts on the renderers of what is ready, kRunPreviewBudgetMs a
    /// frame; the picture of an item that is not ready stays the one it had. 0.12.75: the arrays Unity takes (MeshUpload.Prepare: the tangents and the copies of every vertex) are made on the
    /// worker too; until then the first item put on each frame made them on the main thread, 15 ms for a fret and more for a noise wall (the owner's 0.12.73 log: five frames of 15 ms in a row).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>How long (ms) a frame may spend putting on the renderers of the items of a run's preview that a worker has made (the first is always put on).</summary>
        private const double kRunPreviewBudgetMs = 0.8;

        /// <summary>An item asked for from a worker: what it is to be, and the key of those.</summary>
        private struct RunBuild
        {
            public GuideTemplate Template;
            public string Spec, Key;
            public Vec3 Position;
            public float Yaw;
        }

        /// <summary>An item made: its meshes by material as the arrays Unity takes, and the key they were made for.</summary>
        private sealed class RunBuilt
        {
            public string Key;
            public Dictionary<string, PreparedMesh> Materials;
        }

        private sealed class RunPreviewItem
        {
            /// <summary>What the item is to be: its settings, place and heading, and the key of those.</summary>
            public string Spec, Key;
            public Vec3 Position;
            public float Yaw;

            /// <summary>The renderers shown for it and the key they were made from (null before any).</summary>
            public readonly List<GameObject> Shown = new List<GameObject>();
            public string ShownKey;

            /// <summary>The key last given to the worker, and the worker (one for each item: its newest request is the one it makes).</summary>
            public string Asked;
            public readonly LatestWorker<RunBuild, RunBuilt> Worker = new LatestWorker<RunBuild, RunBuilt>(Build, ex => Mod.log.Error("Run preview: an item could not be made: " + ex.GetType().Name + ": " + ex.Message));
        }

        private readonly List<RunPreviewItem> m_RunPreview = new List<RunPreviewItem>();

        /// <summary>The preview object the run's items are in; the tool's other previews replace m_Preview with one of their own (DestroyPreview first).</summary>
        private GameObject m_RunPreviewRoot;

        /// <summary>What the items of the line are now (null or empty: none shown, the overlay's line alone); made when the line changes.</summary>
        private void SetRunPreview(GuideTemplate template, string spec, List<RunItem> items)
        {
            int count = items == null ? 0 : items.Count;
            for (int i = m_RunPreview.Count - 1; i >= count; i--)
            {
                DestroyShown(m_RunPreview[i]);
                m_RunPreview.RemoveAt(i);
            }

            for (int i = 0; i < count; i++)
            {
                if (i == m_RunPreview.Count)
                    m_RunPreview.Add(new RunPreviewItem());
                RunPreviewItem each = m_RunPreview[i];
                RunItem item = items[i];
                each.Spec = WithRunOf(template, spec, item);
                each.Position = item.Position;
                each.Yaw = item.Yaw;
                each.Key = $"{each.Spec}|{item.Position.X:0.000}|{item.Position.Y:0.000}|{item.Position.Z:0.000}|{item.Yaw:0.0000}";
            }
        }

        /// <summary>Makes the items that changed, within the frame's budget, and shows the preview (show: the line can be placed).</summary>
        private void StepRunPreview(GuideTemplate template, bool show)
        {
            if (m_RunPreview.Count == 0)
            {
                if (m_Preview != null && m_Preview == m_RunPreviewRoot)
                    m_Preview.SetActive(false);
                return;
            }

            if (m_Preview == null || m_Preview != m_RunPreviewRoot)
            {
                DestroyPreview();
                foreach (RunPreviewItem each in m_RunPreview)
                {
                    each.Shown.Clear();
                    each.ShownKey = null;
                    // Its preview was taken away: what a worker made before is made again.
                    each.Asked = null;
                }

                m_Preview = CreatePreviewRoot();
                m_RunPreviewRoot = m_Preview;
                m_PreviewKey = "run";
                PreviewFaceKeys = new string[0];
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            bool put = false;
            for (int i = m_RunPreview.Count - 1; i >= 0; i--)
            {
                RunPreviewItem each = m_RunPreview[i];
                if (each.ShownKey == each.Key)
                    continue;
                if (each.Asked != each.Key)
                {
                    each.Asked = each.Key;
                    each.Worker.Submit(new RunBuild { Template = template, Spec = each.Spec, Key = each.Key, Position = each.Position, Yaw = each.Yaw });
                }

                if (put && (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency > kRunPreviewBudgetMs)
                    continue;
                // What a worker has made is put on if it is still what the item is to be; a result that is not goes unseen (the item was asked again).
                if (each.Worker.TryTake(out RunBuilt built) && built.Key == each.Key)
                {
                    PutShown(each, built);
                    put = true;
                }
            }

            m_Preview.SetActive(show);
            m_Preview.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        }

        /// <summary>Forgets the run's items and takes their preview away (the line is placed or given up).</summary>
        private void ForgetRunPreview()
        {
            if (m_Preview != null && m_Preview == m_RunPreviewRoot)
                DestroyPreview();
            m_RunPreview.Clear();
            m_RunPreviewRoot = null;
        }

        /// <summary>The meshes of an item by material: its plan (made again, not kept: its settings change with the line's end) laid at its place. Pure; made on a worker.</summary>
        private static RunBuilt Build(RunBuild request)
        {
            var built = new RunBuilt { Key = request.Key, Materials = new Dictionary<string, PreparedMesh>() };
            MarkingPlan plan = MarkingTemplates.PlanOf(request.Template, request.Spec, false);
            if (plan == null)
                return built;
            var meshes = new Dictionary<string, MeshBuilder>();
            foreach (MarkingPart part in plan.Parts)
            {
                string material = ChunkBuilder.PartMaterial(part);
                if (!meshes.TryGetValue(material, out MeshBuilder mesh))
                    meshes[material] = mesh = new MeshBuilder();
                mesh.Append(part.Mesh, request.Position, request.Yaw);
            }

            foreach (KeyValuePair<string, MeshBuilder> pair in meshes)
            {
                if (pair.Value.VertexCount > 0)
                    built.Materials[pair.Key] = MeshUpload.Prepare(pair.Value);
            }

            return built;
        }

        /// <summary>Takes the renderers an item had away and puts on those of what a worker made. Main thread.</summary>
        private void PutShown(RunPreviewItem each, RunBuilt built)
        {
            DestroyShown(each);
            each.ShownKey = built.Key;
            foreach (KeyValuePair<string, PreparedMesh> pair in built.Materials)
            {
                GameObject child = AddPreparedPartRenderer(pair.Key, pair.Value);
                if (child != null)
                    each.Shown.Add(child);
            }
        }

        /// <summary>AddPartRenderer for a mesh whose arrays a worker made (MeshUpload.Prepare): what is left for the main thread is handing them to Unity.</summary>
        private GameObject AddPreparedPartRenderer(string materialName, PreparedMesh prepared)
        {
            Material material = TsMaterials.OfBody(materialName);
            if (material == null || prepared.VertexCount == 0)
                return null;
            bool paint = ChunkBuilder.IsMarkingMaterial(materialName);
            Mesh mesh = null;
            GameObject child = null;
            try
            {
                mesh = MeshUpload.ToMesh(prepared, paint ? "CDLTS preview paint" : "CDLTS preview solid");
                child = NewChild(paint ? "paint" : "solid");
                if (paint)
                    child.transform.localPosition = new Vector3(0f, ChunkBuilder.MarkingRaise, 0f);
                MeshUpload.AddRenderer(child, mesh, material, false);
                return child;
            }
            catch (System.Exception error)
            {
                MeshUpload.CleanupFailed(mesh, child, error);
                throw;
            }
        }

        private static void DestroyShown(RunPreviewItem each)
        {
            foreach (GameObject child in each.Shown)
            {
                if (child == null)
                    continue;
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                    Object.Destroy(filter.sharedMesh);
                Object.Destroy(child);
            }

            each.Shown.Clear();
            each.ShownKey = null;
        }
    }
}
