using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One curve pair of a part as it was sampled: the places of the parameter and the stretches across it. What the shadow comparison measures.</summary>
    public readonly struct SamplingContext
    {
        public readonly Bezier3 Left, Right;
        public readonly IList<float> At;
        public readonly IList<PaintableStretch> Across;

        public SamplingContext(in Bezier3 left, in Bezier3 right, IList<float> at, IList<PaintableStretch> across)
        {
            Left = left;
            Right = right;
            At = at;
            Across = across;
        }
    }

    /// <summary>What a part's adaptive bands were made from, kept for SamplingCheck (null where nothing is measured).</summary>
    public sealed class SamplingProbe
    {
        public readonly List<SamplingContext> Contexts = new List<SamplingContext>();

        public void Add(in Bezier3 left, in Bezier3 right, IList<float> at, IList<PaintableStretch> across) => Contexts.Add(new SamplingContext(left, right, at, across));
    }

    /// <summary>One piece of a junction part: the sides of a band across it, the stretches of the cross-section there, and whether it is a gore's filler.</summary>
    public readonly struct NodePiece
    {
        public readonly Bezier3 Left, Right;
        public readonly IList<PaintableStretch> Across;
        public readonly bool Filler;

        public NodePiece(in Bezier3 left, in Bezier3 right, IList<PaintableStretch> across, bool filler)
        {
            Left = left;
            Right = right;
            Across = across;
            Filler = filler;
        }
    }

    /// <summary>
    /// How SurfaceReader samples a part of a road when the adaptive sampler is on (0.12.75, pure; DESIGN-adaptive-sampling.md 10, DESIGN-wiring-plan.md B): the tolerances, the places of a segment's half
    /// (open road adaptive, the stretch next to a junction on a regular grid) and of a junction part (one list for all its curves), and what goes into a cached part's fingerprint so that a part made by one
    /// sampler is never served as another's.
    /// </summary>
    public static class PartSampling
    {
        /// <summary>The tolerances (m): the paint's edge in the plan, and the height where it floats or sinks. Changing one is a new Version.</summary>
        public const float TolXz = 0.010f, TolY = 0.003f;

        /// <summary>Goes into every fingerprint of an adaptive part: raise it when the sampling changes in a way the tolerances do not say.</summary>
        public const int Version = 1;

        /// <summary>The steps every band had until 0.12.73 (and has with the sampler off or in the shadow).</summary>
        public const int FixedSamples = 16;

        private static readonly IList<float> s_Fixed = Array.AsReadOnly(Places(FixedSamples));

        /// <summary>The 17 equal places of the fixed sampler (read only).</summary>
        public static IList<float> FixedAt => s_Fixed;

        private static float[] Places(int samples)
        {
            var at = new float[samples + 1];
            for (int i = 0; i <= samples; i++)
                at[i] = i / (float)samples;
            return at;
        }

        /// <summary>What a part's fingerprint takes from the sampler: which one, and for the adaptive one everything its output depends on.</summary>
        public static void AddPrint(ref Fingerprint fingerprint, bool adaptive)
        {
            fingerprint.Add(adaptive ? 1 : 0);
            if (!adaptive)
                return;
            fingerprint.Add(Version);
            fingerprint.Add(TolXz);
            fingerprint.Add(TolY);
            fingerprint.Add(Zones.DefaultLength);
            fingerprint.Add(Zones.DefaultStep);
            fingerprint.Add(Flatten.MaxSegments);
            fingerprint.Add(FixedSamples);
        }

        /// <summary>
        /// The bands of one half of a segment: the sides left and right, and every stretch across them at one list of places (the stretches of a half meet exactly); zones say which ends are at a
        /// junction. The curves and the places go to the probe.
        /// </summary>
        public static List<SurfaceStrip> HalfBands(in Bezier3 left, in Bezier3 right, IList<PaintableStretch> stretches, Zones zones, string source, SamplingProbe probe = null)
        {
            float[] at = Flatten.Params(left, right, TolXz, TolY, zones);
            probe?.Add(left, right, at, stretches);
            var bands = new List<SurfaceStrip>();
            AddStretches(bands, left, right, stretches, at, source);
            return bands;
        }

        /// <summary>
        /// The bands of a junction part: one list of places for every curve of its pieces (the two halves, the middle line between them and a gore are cut at the same places, or a sliver opens along
        /// the curve they share), and each piece's stretches across its sides. A band of a filler piece is marked.
        /// </summary>
        public static List<SurfaceStrip> NodeBands(IList<NodePiece> pieces, string source, SamplingProbe probe = null)
        {
            var curves = new List<Bezier3>(pieces.Count * 2);
            foreach (NodePiece piece in pieces)
            {
                curves.Add(piece.Left);
                curves.Add(piece.Right);
            }

            float[] at = Flatten.PartParams(curves, TolXz, TolY, FixedSamples);
            var bands = new List<SurfaceStrip>();
            foreach (NodePiece piece in pieces)
            {
                probe?.Add(piece.Left, piece.Right, at, piece.Across);
                int first = bands.Count;
                AddStretches(bands, piece.Left, piece.Right, piece.Across, at, source);
                for (int i = first; i < bands.Count; i++)
                    bands[i].Filler = piece.Filler;
            }

            return bands;
        }

        private static void AddStretches(List<SurfaceStrip> into, in Bezier3 left, in Bezier3 right, IList<PaintableStretch> stretches, float[] at, string source)
        {
            Bezier3 l = left, r = right;
            foreach (PaintableStretch stretch in stretches)
                into.AddRange(SurfaceSection.BandsAt(t => l.Point(t), t => r.Point(t), stretch, at, source));
        }
    }
}
