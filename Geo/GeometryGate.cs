using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The state of one of the game's geometry assets (Colossal.IO.AssetDatabase.GeometryAsset) as a synchronous read of its meshes (ObtainMeshes ... ReleaseMeshes) sees it. The game loads a road piece's
    /// geometry through GeometryAssetLoadingSystem over the asset's one mutable Loading struct: BatchMeshSystem requests it (Scheduled), the loader starts it (Started) and, when it is done (Done: the data
    /// stays until the game unloads it), BatchMeshSystem takes what it needs and waits for Done until then. ObtainMeshes works in the same struct: with the load started it takes the loader's read handle and
    /// throws "required less data" (it asks for every attribute, the game for a part); ReleaseMeshes resets all of it (Loading.Dispose), so the loader's own completion of a scheduled asset throws "not
    /// scheduled". Either way the asset never reaches Done, BatchMeshSystem waits for it for ever, and so does the loading screen, in the next load too: the struct belongs to the asset database, not to the
    /// world (0.12.78: the owner's loading screen). So a read is made only of an asset that is idle, or whose meshes someone else holds (Obtain then only counts them).
    /// </summary>
    public readonly struct GeometryState
    {
        public readonly bool Scheduled, Started, Done, HeaderLoaded, DataValid, HasMeshes;

        public GeometryState(bool scheduled, bool started, bool done, bool headerLoaded, bool dataValid, bool hasMeshes)
        {
            Scheduled = scheduled;
            Started = started;
            Done = done;
            HeaderLoaded = headerLoaded;
            DataValid = dataValid;
            HasMeshes = hasMeshes;
        }

        /// <summary>Nothing is loaded, requested or under way: what a finished unload leaves (GeometryAsset.UnloadPartial).</summary>
        public bool Idle => !Scheduled && !Started && !Done && !HeaderLoaded && !DataValid;

        public override string ToString()
        {
            var flags = new List<string>(6);
            if (Scheduled)
                flags.Add("scheduled");
            if (Started)
                flags.Add("started");
            if (HeaderLoaded)
                flags.Add("header");
            if (DataValid)
                flags.Add("data");
            if (Done)
                flags.Add("done");
            if (HasMeshes)
                flags.Add("meshes");
            return flags.Count == 0 ? "idle" : string.Join(" ", flags);
        }
    }

    /// <summary>What reading a road piece's meshes came to: its vertices, a piece without any (the answer is kept), or an asset the game is still loading (nothing was touched, and nothing is kept).</summary>
    public enum PieceRead
    {
        Ready,
        NoData,
        Busy
    }

    public static class GeometryGate
    {
        /// <summary>Whether ObtainMeshes and ReleaseMeshes may be called on an asset in this state without disturbing the game's loading of it.</summary>
        public static bool CanRead(in GeometryState state) => state.HasMeshes || state.Idle;
    }

    /// <summary>
    /// What was read of the pieces (pure): a piece that was read, or has nothing to read, is kept for good; one the game was still loading is not, and is retried (Poll). Epoch moves each time a piece
    /// that was busy becomes known, which is when what was made without it is out of date (ProvisionalLedger). Main thread.
    /// </summary>
    public sealed class FaceBook<TKey, TFaces>
    {
        private readonly Dictionary<TKey, TFaces> m_Known = new Dictionary<TKey, TFaces>();
        private readonly List<TKey> m_Busy = new List<TKey>();
        private readonly HashSet<TKey> m_EverBusy = new HashSet<TKey>();

        public int Epoch { get; private set; }

        /// <summary>The pieces kept.</summary>
        public int Count => m_Known.Count;

        public int BusyNow => m_Busy.Count;

        /// <summary>How many distinct pieces were busy when they were first asked for (some of them are known by now).</summary>
        public int Deferred => m_EverBusy.Count;

        public bool TryGetKnown(TKey key, out TFaces faces) => m_Known.TryGetValue(key, out faces);

        /// <summary>The piece's faces when it is known, else read now (read is called); false when the piece is busy: it is then on the list that Poll goes through, and faces is the default.</summary>
        public bool TryResolve(TKey key, Func<TKey, (PieceRead Kind, TFaces Faces)> read, out TFaces faces)
        {
            if (m_Known.TryGetValue(key, out faces))
                return true;
            (PieceRead kind, TFaces made) = read(key);
            if (kind == PieceRead.Busy)
            {
                if (!m_Busy.Contains(key))
                    m_Busy.Add(key);
                m_EverBusy.Add(key);
                faces = default;
                return false;
            }

            m_Known[key] = faces = made;
            if (m_Busy.Remove(key))
                Epoch++;
            return true;
        }

        /// <summary>Tries the busy pieces again, once each; how many became known.</summary>
        public int Poll(Func<TKey, (PieceRead Kind, TFaces Faces)> read)
        {
            if (m_Busy.Count == 0)
                return 0;
            int before = m_Busy.Count;
            foreach (TKey key in m_Busy.ToArray())
                TryResolve(key, read, out _);
            return before - m_Busy.Count;
        }

        /// <summary>The city was loaded or left: what was kept is forgotten (Epoch stays where it is: nothing is out of date).</summary>
        public void Clear()
        {
            m_Known.Clear();
            m_Busy.Clear();
            m_EverBusy.Clear();
        }
    }
}
