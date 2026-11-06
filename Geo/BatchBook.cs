using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public enum BatchState
    {
        /// <summary>Accepted, nothing done yet.</summary>
        Queued,

        /// <summary>Being matched to roads, over several frames.</summary>
        Preparing,

        /// <summary>Written in one frame (the old items of the batch are gone, the new ones are there).</summary>
        Written,

        /// <summary>Not written (an error while preparing or writing); Error says what.</summary>
        Failed
    }

    /// <summary>One accepted batch on its way: its plan, how far it has got and what came of it. State changes go through Begin, Finish and Fail, each only from the state it may leave.</summary>
    public sealed class BatchJob
    {
        public readonly BatchPlan Plan;
        public BatchState State = BatchState.Queued;
        public int Written;

        /// <summary>The request's numbers of the accepted items that stood beside no road, so were not written. Known when the job is done.</summary>
        public readonly List<int> Unmatched = new List<int>();

        public string Error = "";

        /// <summary>The same batch was submitted again before this one was written: the system drops it where it is.</summary>
        public bool Superseded;

        public BatchJob(BatchPlan plan)
        {
            Plan = plan;
        }

        public bool Done => State == BatchState.Written || State == BatchState.Failed;

        public bool Begin()
        {
            if (State != BatchState.Queued)
                return false;
            State = BatchState.Preparing;
            return true;
        }

        public bool Finish(int written, IEnumerable<int> unmatched)
        {
            if (State != BatchState.Preparing)
                return false;
            State = BatchState.Written;
            Written = written;
            Unmatched.AddRange(unmatched);
            return true;
        }

        /// <summary>The job did not write anything: every accepted item is unmatched, none written.</summary>
        public bool Fail(string error)
        {
            if (Done)
                return false;
            State = BatchState.Failed;
            Error = error;
            Written = 0;
            Unmatched.Clear();
            foreach (BatchItem item in Plan.Items)
            {
                if (item.Accepted)
                    Unmatched.Add(item.Index);
            }

            return true;
        }
    }

    /// <summary>
    /// The batches of this session (0.12.67): at most one job for each batch id, the newest. A batch submitted again before it was written replaces the waiting one (the waiting one is dropped,
    /// never written); one that was written is replaced by the new one when that is written. The last MaxKept finished jobs are kept to answer Status. Main thread.
    /// </summary>
    public sealed class BatchBook
    {
        public const int MaxKept = 16;

        private readonly List<BatchJob> m_Jobs = new List<BatchJob>();

        public int Count => m_Jobs.Count;

        public BatchJob Submit(BatchPlan plan)
        {
            for (int i = m_Jobs.Count - 1; i >= 0; i--)
            {
                if (m_Jobs[i].Plan.BatchId != plan.BatchId)
                    continue;
                m_Jobs[i].Superseded |= !m_Jobs[i].Done;
                m_Jobs.RemoveAt(i);
            }

            var job = new BatchJob(plan);
            m_Jobs.Add(job);
            int finished = 0;
            for (int i = m_Jobs.Count - 1; i >= 0; i--)
            {
                if (m_Jobs[i].Done && ++finished > MaxKept)
                    m_Jobs.RemoveAt(i);
            }

            return job;
        }

        /// <summary>The oldest job that is not done: batches are written in the order they came.</summary>
        public BatchJob Next()
        {
            foreach (BatchJob job in m_Jobs)
            {
                if (!job.Done)
                    return job;
            }

            return null;
        }

        public BatchJob Find(uint batchId)
        {
            for (int i = m_Jobs.Count - 1; i >= 0; i--)
            {
                if (m_Jobs[i].Plan.BatchId == batchId)
                    return m_Jobs[i];
            }

            return null;
        }

        /// <summary>The city was left: what is waiting is dropped (and says so), what is done is forgotten.</summary>
        public void Clear()
        {
            foreach (BatchJob job in m_Jobs)
                job.Superseded |= !job.Done;
            m_Jobs.Clear();
        }

        /// <summary>The answer to Submit: the accepted count, the ignored keys of each accepted item and the items refused one by one; a refused batch has "error" and nothing else of use.</summary>
        public static string SubmitJson(BatchPlan plan)
        {
            var w = new JsonWriter().BeginObject().Key("batch").String(plan.Batch ?? "");
            if (plan.Error.Length > 0)
                return w.Key("error").String(plan.Error).EndObject().ToString();
            w.Key("queued").Number((long)plan.AcceptedCount);
            WriteItems(w, plan);
            return w.EndObject().ToString();
        }

        /// <summary>
        /// The answer to Status: batch, state (queued, preparing, written, failed), queued (accepted items), written, items (i and ignoredKeys of the accepted), rejected (i and reason of the
        /// items refused one by one) and, once the job is done only, unmatched (the numbers of the accepted items that stood beside no road). A failed job has "error". Nothing is claimed that is
        /// not known: while the job is not done there is no "unmatched".
        /// </summary>
        public static string StatusJson(string batch, BatchJob job)
        {
            var w = new JsonWriter().BeginObject().Key("batch").String(batch ?? "");
            if (job == null)
                return w.Key("state").String("failed").Key("written").Number(0L).Key("error").String("unknown batch").EndObject().ToString();
            w.Key("state").String(StateName(job.State)).Key("queued").Number((long)job.Plan.AcceptedCount).Key("written").Number((long)job.Written);
            if (job.Done)
            {
                w.Key("unmatched").BeginArray();
                foreach (int index in job.Unmatched)
                    w.Number((long)index);
                w.EndArray();
            }

            if (job.Error.Length > 0)
                w.Key("error").String(job.Error);
            WriteItems(w, job.Plan);
            return w.EndObject().ToString();
        }

        public static string StateName(BatchState state)
        {
            switch (state)
            {
                case BatchState.Queued: return "queued";
                case BatchState.Preparing: return "preparing";
                case BatchState.Written: return "written";
                default: return "failed";
            }
        }

        private static void WriteItems(JsonWriter w, BatchPlan plan)
        {
            w.Key("items").BeginArray();
            foreach (BatchItem item in plan.Items)
            {
                if (!item.Accepted)
                    continue;
                w.BeginObject().Key("i").Number((long)item.Index).Key("ignoredKeys").BeginArray();
                foreach (string key in item.IgnoredKeys)
                    w.String(key);
                w.EndArray().EndObject();
            }

            w.EndArray();
            if (plan.Items.Exists(i => !i.Accepted))
            {
                w.Key("rejected").BeginArray();
                foreach (BatchItem item in plan.Items)
                {
                    if (!item.Accepted)
                        w.BeginObject().Key("i").Number((long)item.Index).Key("reason").String(item.Rejected).EndObject();
                }

                w.EndArray();
            }
        }
    }

    /// <summary>Which entities each batch wrote, so that a batch submitted again finds its old items without looking at all of them. Generic over the key (the entity); pure.</summary>
    public sealed class BatchIndex<TKey>
    {
        private readonly Dictionary<uint, List<TKey>> m_ByBatch = new Dictionary<uint, List<TKey>>();
        private readonly Dictionary<TKey, uint> m_BatchOf = new Dictionary<TKey, uint>();

        public int Count => m_BatchOf.Count;

        public int BatchCount => m_ByBatch.Count;

        public int CountOf(uint batch) => m_ByBatch.TryGetValue(batch, out List<TKey> keys) ? keys.Count : 0;

        public void Add(uint batch, TKey key)
        {
            if (batch == 0u)
                return;
            // The renderer reads an item again at each change of it: the same key in the same batch is the usual case.
            if (m_BatchOf.TryGetValue(key, out uint held))
            {
                if (held == batch)
                    return;
                Remove(key);
            }

            if (!m_ByBatch.TryGetValue(batch, out List<TKey> keys))
                m_ByBatch[batch] = keys = new List<TKey>();
            keys.Add(key);
            m_BatchOf[key] = batch;
        }

        /// <summary>The key is gone (deleted by hand, or replaced): no batch holds it any more. False when none did.</summary>
        public bool Remove(TKey key)
        {
            if (!m_BatchOf.TryGetValue(key, out uint batch))
                return false;
            m_BatchOf.Remove(key);
            List<TKey> keys = m_ByBatch[batch];
            keys.Remove(key);
            if (keys.Count == 0)
                m_ByBatch.Remove(batch);
            return true;
        }

        /// <summary>Everything the batch holds, and the batch is empty after it.</summary>
        public List<TKey> Take(uint batch)
        {
            if (!m_ByBatch.TryGetValue(batch, out List<TKey> keys))
                return new List<TKey>();
            m_ByBatch.Remove(batch);
            foreach (TKey key in keys)
                m_BatchOf.Remove(key);
            return keys;
        }

        public void Clear()
        {
            m_ByBatch.Clear();
            m_BatchOf.Clear();
        }
    }

    /// <summary>Which road an item put at a place stands beside, for the batch: the same reach as the game's own check of an item's road (EdgeHint.Beside past the road's edge), searched by place.</summary>
    public static class BatchHost
    {
        /// <summary>Half the widest road the game builds (m): the search looks this far from a centre line for a road whose edge is within EdgeHint.Beside of the item.</summary>
        public const float MaxHalfWidth = 40f;

        public const float SearchRadius = MaxHalfWidth + EdgeHint.Beside;

        /// <summary>
        /// The index of the edge the point is most inside (its distance from the centre line less half the road's width, the least), provided it is no farther than EdgeHint.Beside beyond the road's
        /// edge; -1 when no road is that near. t is the place on the edge's curve nearest the point. A board stands at the roadside of a road 29 m wide 16 m from its centre line, which EdgeHint's own
        /// search (a radius from the centre line) would call no road.
        /// </summary>
        public static int Find(EdgeBoxes boxes, Func<int, Bezier3> curveOf, Func<int, float> widthOf, float x, float z, List<int> scratch, out float t)
        {
            t = 0f;
            int best = -1;
            float bestOver = float.MaxValue;
            scratch.Clear();
            boxes.Candidates(x, z, SearchRadius, scratch);
            var at = new Vec3(x, 0f, z);
            for (int i = 0; i < scratch.Count; i++)
            {
                Bezier3 curve = curveOf(scratch[i]);
                float ct = curve.ClosestT(at, 8);
                float over = Vec3.DistanceXZ(curve.Point(ct), at) - widthOf(scratch[i]) * 0.5f;
                if (over <= EdgeHint.Beside && over < bestOver)
                {
                    bestOver = over;
                    best = scratch[i];
                    t = ct;
                }
            }

            return best;
        }
    }
}
