using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One half of a segment as the adaptive sampler is given it: the two sides and which of its ends are at a junction.</summary>
    public readonly struct ShadowHalf
    {
        public readonly Bezier3 Left, Right;
        public readonly Zones Zones;

        public ShadowHalf(in Bezier3 left, in Bezier3 right, Zones zones)
        {
            Left = left;
            Right = right;
            Zones = zones;
        }
    }

    /// <summary>
    /// What the shadow comparison of one part needs, and nothing of the game: the fixed sampler's bands as they were made (read only, shared with the cache), the curves and the stretches the adaptive
    /// sampler is to lay them from, and what the fixed one cost. Made on the main thread while the part is made (ShadowCapture), made use of on a worker (ShadowRunner). Immutable: the arrays are copies.
    /// </summary>
    public sealed class ShadowJob
    {
        public string Part, Source;
        public bool Node;
        public double FixedMs;
        public SurfaceStrip[] FixedBands;
        public ShadowHalf[] Halves;
        public PaintableStretch[] Stretches;
        public NodePiece[] Pieces;
    }

    /// <summary>
    /// What a part's making hands to its shadow (0.12.75): SurfaceReader's fixed path says what it lays the bands from, as it lays them, so that the adaptive sampler can lay the same part later, on
    /// another thread. Main thread; nothing here reads or keeps the game's objects.
    /// </summary>
    public sealed class ShadowCapture
    {
        private readonly List<ShadowHalf> m_Halves = new List<ShadowHalf>();
        private PaintableStretch[] m_Stretches;
        private NodePiece[] m_Pieces;
        private string m_Source = "";

        public bool Node { get; private set; }

        /// <summary>A segment: the stretches of its cross-section (the same for both halves) and the name its bands are laid under.</summary>
        public void Segment(string source, IList<PaintableStretch> stretches)
        {
            m_Source = source;
            if (m_Stretches == null)
                m_Stretches = Copy(stretches);
        }

        public void Half(in Bezier3 left, in Bezier3 right, Zones zones) => m_Halves.Add(new ShadowHalf(left, right, zones));

        /// <summary>A junction part: its pieces, each with its stretches (copied) and its curves.</summary>
        public void Pieces(string source, IList<NodePiece> pieces)
        {
            Node = true;
            m_Source = source;
            m_Pieces = new NodePiece[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
                m_Pieces[i] = new NodePiece(pieces[i].Left, pieces[i].Right, Copy(pieces[i].Across), pieces[i].Filler);
        }

        /// <summary>The job for the part whose fixed bands are strips from first on (copied): false when nothing was captured for it.</summary>
        public bool TryJob(string part, IList<SurfaceStrip> strips, int first, double fixedMs, out ShadowJob job)
        {
            job = null;
            if (strips == null || strips.Count <= first || (Node ? m_Pieces == null || m_Pieces.Length == 0 : m_Halves.Count == 0 || m_Stretches == null))
                return false;
            var bands = new SurfaceStrip[strips.Count - first];
            for (int i = 0; i < bands.Length; i++)
                bands[i] = strips[first + i];
            job = new ShadowJob
            {
                Part = part,
                Source = m_Source,
                Node = Node,
                FixedMs = fixedMs,
                FixedBands = bands,
                Halves = Node ? null : m_Halves.ToArray(),
                Stretches = Node ? null : m_Stretches,
                Pieces = Node ? m_Pieces : null
            };
            return true;
        }

        private static PaintableStretch[] Copy(IList<PaintableStretch> list)
        {
            var copy = new PaintableStretch[list.Count];
            list.CopyTo(copy, 0);
            return copy;
        }
    }
}
