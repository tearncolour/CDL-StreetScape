using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// What is out at the worker for each owner (0.12.74): an item that was read on the main thread and whose cuts are being made. The owner's place (box) is kept so that a road that changes under it
    /// marks the request stale; the cuts are put with it as they come back and the request is handed out when all of them have. A request is only as good as the roads it was made from: the caller
    /// looks at Stale and at whether the item is still what it was (Key) before it uses one. Pure; the main thread's.
    /// </summary>
    public sealed class FitLedger<TOwner>
    {
        public sealed class Request
        {
            public TOwner Owner;

            /// <summary>What the item was when it was read (the caller's key); the request is not used for an item that is not that any more.</summary>
            public string Key;

            /// <summary>Where roads matter to it (world).</summary>
            public float MinX, MinZ, MaxX, MaxZ;

            /// <summary>A road changed in its box after it was read.</summary>
            public bool Stale;

            /// <summary>The cuts made for it, by the read they stand for (a single cut: the default WindowRead).</summary>
            public readonly Dictionary<SurfaceHeights.WindowRead, FitDone> Results = new Dictionary<SurfaceHeights.WindowRead, FitDone>();

            /// <summary>The time the cuts took on the worker and the longest wait for a thread (ms).</summary>
            public double WorkerMs, WaitedMs;

            /// <summary>What the main thread spent on it so far (ms): reading the roads and giving the cuts; the caller adds the taking.</summary>
            public double MainMs;

            /// <summary>What the caller needs again when the cuts are back (what it read, who waits with it).</summary>
            public object State;

            internal int Waiting;

            /// <summary>The caller is still giving it cuts (a read spread over frames): it is not complete before Seal, however many of its cuts are back.</summary>
            public bool Reading;

            /// <summary>A cut of it that threw (the first), else null.</summary>
            public FitDone? Failure
            {
                get
                {
                    foreach (FitDone done in Results.Values)
                    {
                        if (done.Error != null)
                            return done;
                    }

                    return null;
                }
            }

            /// <summary>Whether its cuts are the ones to use for an item that is now key: no road changed under it, the item is what it was, nothing failed.</summary>
            public bool UsableFor(string key) => !Stale && Key == key && Failure == null;
        }

        private readonly Dictionary<TOwner, Request> m_ByOwner = new Dictionary<TOwner, Request>();
        private readonly Dictionary<long, (Request Request, SurfaceHeights.WindowRead Window)> m_ById = new Dictionary<long, (Request, SurfaceHeights.WindowRead)>();
        private readonly Queue<Request> m_Complete = new Queue<Request>();

        /// <summary>Owners with a request out or complete and not yet taken.</summary>
        public int Count => m_ByOwner.Count;

        public bool IsPending(TOwner owner) => m_ByOwner.ContainsKey(owner);

        /// <summary>The owner's request that is out or complete and not yet taken; null when it has none.</summary>
        public Request Of(TOwner owner) => m_ByOwner.TryGetValue(owner, out Request request) ? request : null;

        /// <summary>A new request for an owner; one it had before is forgotten (its cuts, when they come, are dropped).</summary>
        public Request Begin(TOwner owner, string key, float minX, float minZ, float maxX, float maxZ, bool reading = false)
        {
            var request = new Request { Owner = owner, Key = key, MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ, Reading = reading };
            m_ByOwner[owner] = request;
            return request;
        }

        /// <summary>Gives the worker a cut for the request; window says which read of the item it stands for.</summary>
        public void Add(Request request, FitWorker worker, FitJob job, SurfaceHeights.WindowRead window = default)
        {
            m_ById[worker.Submit(job)] = (request, window);
            request.Waiting++;
        }

        /// <summary>The request has all it will have: if nothing is out for it, it is complete now.</summary>
        public void Seal(Request request)
        {
            request.Reading = false;
            if (request.Waiting == 0)
                m_Complete.Enqueue(request);
        }

        /// <summary>A cut the worker made: put with its request. False when it is none of this ledger's (a comparison job, another ledger's, a request that was forgotten).</summary>
        public bool Accept(FitDone done)
        {
            if (!m_ById.TryGetValue(done.Job.Id, out (Request Request, SurfaceHeights.WindowRead Window) at))
                return false;
            m_ById.Remove(done.Job.Id);
            Request request = at.Request;
            request.Results[at.Window] = done;
            request.WorkerMs += done.Result.CutMs + done.Result.RuleMs;
            if (done.WaitedMs > request.WaitedMs)
                request.WaitedMs = done.WaitedMs;
            if (--request.Waiting == 0 && !request.Reading)
                m_Complete.Enqueue(request);
            return true;
        }

        /// <summary>The next request whose cuts are all made (in the order they completed); a forgotten one is passed over. The owner is no longer pending after it.</summary>
        public bool TryTakeComplete(out Request request)
        {
            while (m_Complete.Count > 0)
            {
                request = m_Complete.Dequeue();
                if (!m_ByOwner.TryGetValue(request.Owner, out Request current) || !ReferenceEquals(current, request))
                    continue;
                m_ByOwner.Remove(request.Owner);
                return true;
            }

            request = null;
            return false;
        }

        /// <summary>A road changed in these boxes (world): the requests whose box is within margin of one are stale. How many were marked.</summary>
        public int MarkStale(List<(float, float, float, float)> boxes, float margin)
        {
            int marked = 0;
            foreach (Request request in m_ByOwner.Values)
            {
                if (request.Stale)
                    continue;
                foreach ((float x0, float z0, float x1, float z1) in boxes)
                {
                    if (x1 >= request.MinX - margin && x0 <= request.MaxX + margin && z1 >= request.MinZ - margin && z0 <= request.MaxZ + margin)
                    {
                        request.Stale = true;
                        marked++;
                        break;
                    }
                }
            }

            return marked;
        }

        /// <summary>The owner is not waited for (its item is gone or was changed and is asked again): what comes for it is dropped.</summary>
        public void Forget(TOwner owner) => m_ByOwner.Remove(owner);

        public void Clear()
        {
            m_ByOwner.Clear();
            m_ById.Clear();
            m_Complete.Clear();
        }
    }
}
