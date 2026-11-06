using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// One piece of a road's cross-section as the game keeps it (NetCompositionPiece and its NetPieceData, read by SurfaceReader): where its middle is across the road (m from the composition's middle,
    /// negative to the left looking along the segment), how wide it is, the height of its surface above the road's, and what it is.
    /// </summary>
    public struct SectionPiece
    {
        public float X, Width, Top;

        /// <summary>A surface (NetPieceFlags.Surface), one that stops traffic (BlockTraffic: a barrier, a median), one of the side, top or bottom layers (the faces of a deck, a roof), one on another level
        /// (underground, overhead, hidden), one with road lanes on it.</summary>
        public bool Surface, BlocksTraffic, Layer, OtherLevel, RoadLanes;

        public string Name;

        /// <summary>A piece whose mesh has a kerb that is not straight along it (a bus bay's, 0.12.37): no kerb of it is drawn.</summary>
        public bool Uneven;
    }

    /// <summary>A stretch across a road that paint may go on: from F0 to F1 of the road's width (0 its left side, 1 its right) at the height Y above the road's.</summary>
    public struct PaintableStretch
    {
        public float F0, F1, Y;

        public PaintableStretch(float f0, float f1, float y)
        {
            F0 = f0;
            F1 = f1;
            Y = y;
        }
    }

    /// <summary>
    /// Which parts of a road's cross-section can be painted (0.12.4): the pieces that are a surface at the carriageway's height (within Tolerance of the lowest top of the pieces with road lanes: a footway, a
    /// kerb, a raised median are higher), that do not stop traffic (a barrier, a median) and are not a face of the deck or on another level; side by side ones joined. And the bands of surface along a
    /// segment of the road from its two sides. Pure.
    /// </summary>
    public static class SurfaceSection
    {
        /// <summary>How far (m) a piece's top may be from the carriageway's to be painted with it.</summary>
        public const float Tolerance = 0.06f;

        /// <summary>How far above the carriageway (m) a piece that stops traffic may be and still be stood on (a raised median); higher is a barrier or a wall.</summary>
        public const float MaxStandRise = 0.4f;

        /// <summary>
        /// The stretches across the road a post may stand on (0.12.5, PostGround): every surface that is not a face of the deck or on another level, at its own height (the carriageway, a footway, a kerb,
        /// a median), but not a barrier or a wall (a piece that stops traffic higher than MaxStandRise above the carriageway).
        /// </summary>
        public static List<PaintableStretch> Standable(IList<SectionPiece> pieces, float width, List<string> log = null) => Stretches(pieces, width, log, true);

        /// <summary>
        /// The stretches across a railway, a metro or a tram line a post or a wall may stand on (0.12.66, the owner: "支持吸附到铁路和地铁线路"): as Standable, the surfaces that are no face of the deck and
        /// not on another level; and where the composition has no piece flagged a surface at all (which the game's flags of a track piece may well be: only road, footway and median pieces were seen
        /// flagged), the bed: every piece that is no face of the deck and not on another level, no more than MaxStandRise above the lowest of them. Nothing for a composition that is all on another level
        /// (a metro in a tunnel) or all faces. The log has the pieces and what became of each, for the first reads of a session (SurfaceReader).
        /// </summary>
        public static List<PaintableStretch> StandableTrack(IList<SectionPiece> pieces, float width, List<string> log = null)
        {
            var flaggedLog = log == null ? null : new List<string>();
            List<PaintableStretch> flagged = Stretches(pieces, width, flaggedLog, true);
            if (flagged.Count > 0 || pieces == null || !(width > 0.1f))
            {
                log?.AddRange(flaggedLog);
                return flagged;
            }

            float lowest = float.MaxValue;
            foreach (SectionPiece p in pieces)
            {
                if (!p.Layer && !p.OtherLevel && p.Width > 0.01f)
                    lowest = Math.Min(lowest, p.Top);
            }

            var kept = new List<PaintableStretch>();
            foreach (SectionPiece p in pieces)
            {
                string why = p.Layer ? "a face of the deck" : p.OtherLevel ? "on another level" : !(p.Width > 0.01f) ? "no width" : p.Top - lowest > MaxStandRise ? $"a wall ({p.Top - lowest:+0.00} m)" : "";
                if (why.Length == 0)
                    kept.Add(new PaintableStretch((p.X - p.Width * 0.5f) / width + 0.5f, (p.X + p.Width * 0.5f) / width + 0.5f, p.Top));
                log?.Add($"{(string.IsNullOrEmpty(p.Name) ? "?" : p.Name)} x {p.X:+0.00;-0.00} w {p.Width:0.00} top {p.Top:0.00}: {(why.Length == 0 ? "bed (no piece of the composition is a surface)" : why)}");
            }

            return Join(kept, width);
        }

        /// <summary>The stretches across the road that may be painted, left to right, and a line on each piece for the log.</summary>
        public static List<PaintableStretch> Paintable(IList<SectionPiece> pieces, float width, List<string> log = null) => Stretches(pieces, width, log, false);

        private static List<PaintableStretch> Stretches(IList<SectionPiece> pieces, float width, List<string> log, bool standable)
        {
            var result = new List<PaintableStretch>();
            if (pieces == null || !(width > 0.1f))
                return result;
            float reference = float.MaxValue;
            foreach (SectionPiece p in pieces)
            {
                if (p.Surface && p.RoadLanes && !p.OtherLevel && !p.Layer)
                    reference = Math.Min(reference, p.Top);
            }

            if (reference == float.MaxValue)
            {
                // No piece with road lanes (a junction's own composition can have none): the lowest surface that does not stop traffic.
                foreach (SectionPiece p in pieces)
                {
                    if (p.Surface && !p.BlocksTraffic && !p.OtherLevel && !p.Layer)
                        reference = Math.Min(reference, p.Top);
                }
            }

            var kept = new List<PaintableStretch>();
            foreach (SectionPiece p in pieces)
            {
                string why = standable
                    ? (!p.Surface ? "not a surface" : p.Layer ? "a face of the deck" : p.OtherLevel ? "on another level" : !(p.Width > 0.01f) ? "no width"
                        : p.BlocksTraffic && reference != float.MaxValue && p.Top - reference > MaxStandRise ? $"a barrier or a wall ({p.Top - reference:+0.00} m)" : "")
                    : (!p.Surface ? "not a surface" : p.BlocksTraffic ? "stops traffic (a barrier or a median)" : p.Layer ? "a face of the deck" : p.OtherLevel ? "on another level"
                        : reference == float.MaxValue ? "no carriageway" : Math.Abs(p.Top - reference) > Tolerance ? $"{p.Top - reference:+0.00;-0.00} m from the carriageway's height" : !(p.Width > 0.01f) ? "no width" : "");
                if (why.Length == 0)
                    kept.Add(new PaintableStretch((p.X - p.Width * 0.5f) / width + 0.5f, (p.X + p.Width * 0.5f) / width + 0.5f, p.Top));
                log?.Add($"{(string.IsNullOrEmpty(p.Name) ? "?" : p.Name)} x {p.X:+0.00;-0.00} w {p.Width:0.00} top {p.Top:0.00}: {(why.Length == 0 ? (standable ? "stand" : "paint") : why)}");
            }

            return Join(kept, width);
        }

        /// <summary>The stretches left to right, side by side ones that are as high joined into one band (the end of Stretches, as it was, so that the two ways of choosing them join alike).</summary>
        private static List<PaintableStretch> Join(List<PaintableStretch> kept, float width)
        {
            var result = new List<PaintableStretch>();
            kept.Sort((a, b) => a.F0.CompareTo(b.F0));
            // Joined when they meet (to 5 cm) and are as high (to the tolerance): one band, not a row of thin ones.
            float join = 0.05f / width;
            foreach (PaintableStretch s in kept)
            {
                float f0 = Math.Max(0f, s.F0), f1 = Math.Min(1f, s.F1);
                if (f1 <= f0)
                    continue;
                if (result.Count > 0)
                {
                    PaintableStretch last = result[result.Count - 1];
                    if (f0 <= last.F1 + join && Math.Abs(s.Y - last.Y) <= Tolerance)
                    {
                        float w0 = last.F1 - last.F0, w1 = f1 - f0;
                        result[result.Count - 1] = new PaintableStretch(last.F0, Math.Max(last.F1, f1), (last.Y * w0 + s.Y * w1) / Math.Max(1e-6f, w0 + w1));
                        continue;
                    }
                }

                result.Add(new PaintableStretch(f0, f1, s.Y));
            }

            return result;
        }

        /// <summary>
        /// The raised strips between a road's carriageways (0.12.22; the owner's BRT road: "不能吸附中间的路沿"): the kerbs and medians that lie between the first and the last piece with road lanes, higher
        /// than the carriageway by more than Tolerance or stopping traffic, on this level; side by side ones joined (to 5 cm). Left to right, in the composition's X (m from its middle). The footways at the
        /// road's sides are outside the carriageways and not listed.
        /// </summary>
        public static List<(float X0, float X1)> Raised(IList<SectionPiece> pieces)
        {
            var strips = new List<(float X0, float X1)>();
            if (pieces == null)
                return strips;
            float reference = float.MaxValue, first = float.MaxValue, last = float.MinValue;
            foreach (SectionPiece p in pieces)
            {
                if (!p.Surface || !p.RoadLanes || p.OtherLevel || p.Layer)
                    continue;
                reference = Math.Min(reference, p.Top);
                first = Math.Min(first, p.X - p.Width * 0.5f);
                last = Math.Max(last, p.X + p.Width * 0.5f);
            }

            if (reference == float.MaxValue)
                return strips;
            var raised = new List<(float X0, float X1)>();
            foreach (SectionPiece p in pieces)
            {
                if (!p.Surface || p.RoadLanes || p.OtherLevel || p.Layer || !(p.Width > 0.01f) || !(p.BlocksTraffic || p.Top - reference > Tolerance))
                    continue;
                float x0 = p.X - p.Width * 0.5f, x1 = p.X + p.Width * 0.5f;
                if (x0 >= first - 0.05f && x1 <= last + 0.05f)
                    raised.Add((x0, x1));
            }

            raised.Sort((a, b) => a.X0.CompareTo(b.X0));
            foreach ((float x0, float x1) in raised)
            {
                if (strips.Count > 0 && x0 <= strips[strips.Count - 1].X1 + 0.05f)
                    strips[strips.Count - 1] = (strips[strips.Count - 1].X0, Math.Max(strips[strips.Count - 1].X1, x1));
                else
                    strips.Add((x0, x1));
            }

            return strips;
        }

        /// <summary>
        /// A stretch of a junction part's cross-section as it falls in the part's two halves (0.12.10): an ordinary junction part (EdgeNodeGeometry with no middle radius) is a left half from its left
        /// side to its middle line and a right half from the middle line to its right side, the middle at split of the width from the left (0.5 + MiddleOffset / width: GeometrySystem). Each piece is the
        /// half (false: the left one, true: the right) and the stretch in that half's own fractions, 0 at its left side and 1 at its right; a stretch on one side only has one piece.
        /// </summary>
        public static List<(bool Right, PaintableStretch Local)> SplitAcross(PaintableStretch stretch, float split)
        {
            var pieces = new List<(bool Right, PaintableStretch Local)>(2);
            if (float.IsNaN(split))
                split = 0.5f;
            split = Math.Max(0.01f, Math.Min(0.99f, split));
            if (stretch.F0 < split - 1e-4f)
                pieces.Add((false, new PaintableStretch(stretch.F0 / split, Math.Min(stretch.F1, split) / split, stretch.Y)));
            if (stretch.F1 > split + 1e-4f)
                pieces.Add((true, new PaintableStretch((Math.Max(stretch.F0, split) - split) / (1f - split), (stretch.F1 - split) / (1f - split), stretch.Y)));
            return pieces;
        }

        /// <summary>
        /// The band of a stretch along a piece of road whose sides are the curves left and right (the road's own sides, as functions of the piece's parameter from 0 to 1), sampled at samples + 1 places;
        /// the heights are the sides' and the stretch's own. A place F of the width across is F of the way from the left side to the right (as the game puts its lanes: LaneSystem).
        /// </summary>
        public static List<SurfaceStrip> Bands(Func<float, Vec3> left, Func<float, Vec3> right, PaintableStretch stretch, int samples, string source)
        {
            if (left == null || right == null || samples < 1)
                return new List<SurfaceStrip>();
            var at = new float[samples + 1];
            for (int i = 0; i <= samples; i++)
                at[i] = i / (float)samples;
            return BandsAt(left, right, stretch, at, source);
        }

        /// <summary>
        /// The band of a stretch of a part whose sides are the cubics left and right (0.12.73), made at the places of Flatten.Params: as many steps as the curves need within tolXz in the plan and tolY in
        /// height, and the same places for every stretch of the part (Params gives the same list for the same curves); zones keeps the stretch next to a junction on a regular fine grid. The result is
        /// what Bands gives for those places.
        /// </summary>
        public static List<SurfaceStrip> BandsAdaptive(in Bezier3 left, in Bezier3 right, PaintableStretch stretch, float tolXz, float tolY, string source, Zones zones = default)
        {
            Bezier3 l = left, r = right;
            return BandsAt(t => l.Point(t), t => r.Point(t), stretch, Flatten.Params(l, r, tolXz, tolY, zones), source);
        }

        /// <summary>The band of a stretch at the given places of the parameter (from 0 to 1, in order, two at least): Bands and BandsAdaptive are this.</summary>
        public static List<SurfaceStrip> BandsAt(Func<float, Vec3> left, Func<float, Vec3> right, PaintableStretch stretch, IList<float> at, string source)
        {
            var bands = new List<SurfaceStrip>();
            if (left == null || right == null || at == null || at.Count < 2)
                return bands;
            var band = new SurfaceStrip { Source = source };
            for (int i = 0; i < at.Count; i++)
            {
                float t = at[i];
                Vec3 l = left(t), r = right(t);
                band.Add(Vec3.Lerp(l, r, stretch.F0) + new Vec3(0f, stretch.Y, 0f), Vec3.Lerp(l, r, stretch.F1) + new Vec3(0f, stretch.Y, 0f));
            }

            bool finite = true;
            foreach (Vec3 p in band.Left)
                finite &= p.IsFinite;
            foreach (Vec3 p in band.Right)
                finite &= p.IsFinite;
            if (finite)
                bands.Add(band);
            return bands;
        }
    }
}
