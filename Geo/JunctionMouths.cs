using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>A road that joins the chain at a junction (0.12.67): the way it leaves the node (level, length 1) and half its width (m), as the reader found them.</summary>
    public struct ChainMouth
    {
        public Vec3 Dir;
        public float HalfWidth;
    }

    /// <summary>
    /// Where a wall beside the chain is open at a junction (0.12.67; the owner: "岔路节点还是会空"). Until now a barrier was cut from the end of the lanes before a junction to the start of the lanes
    /// after it on the side a road joins: 64 m in the owner's log, where the joining road is 12 to 16 m wide. Now the wall is open only where the joining road meets the line the wall stands on: the
    /// strip of the road (its centre line through the node, half its width each side) crossed with the wall's line, a margin each end, and runs along the carriageway's edge through the rest of the
    /// node. Where it cannot be worked out the old cut is used (TryOpenings says false). Barriers only; the markings keep the cut by the lanes. Pure.
    /// </summary>
    public static class JunctionMouths
    {
        /// <summary>The least sine of the angle between a joining road and the chain (about 25 degrees): a road that joins more slantwise has an opening of 2 w / sin, more than the node.</summary>
        public const float MinSin = 0.42f;

        /// <summary>The chain turns at the node by 45 degrees at most (the cosine): beyond that the node is no straight run to put a wall along.</summary>
        public const float MinTurnCos = 0.7071f;

        /// <summary>How far (m) the opening goes beyond the strip of the road at each end.</summary>
        public const float Margin = 0.5f;

        /// <summary>How far (m) the opening may be outside the lane-free stretch of the node before it is not believed: the node is where the roads meet.</summary>
        public const float Tolerance = 1f;

        /// <summary>The way the chain heads at the end of a piece (level, length 1).</summary>
        public static Vec3 HeadingAtEnd(ChainPiece piece) => piece.Curve.Tangent(piece.Reversed ? 0f : 1f).HeadingXZ() * (piece.Reversed ? -1f : 1f);

        /// <summary>The way the chain heads at the start of a piece (level, length 1).</summary>
        public static Vec3 HeadingAtStart(ChainPiece piece) => piece.Curve.Tangent(piece.Reversed ? 1f : 0f).HeadingXZ() * (piece.Reversed ? -1f : 1f);

        /// <summary>
        /// The openings (chain distances, in order, not overlapping) of a wall on the side of side (+1 the chain's right, -1 its left) at the junction after piece, whose wall stands wallLateral m right of
        /// the chain's direction there: one for each road that joins on that side. False when the old cut is to be used: the piece's mouths are not known (the reader could not read them, or it is a
        /// roundabout), the chain turns more than 45 degrees, a road joins at less than 25 degrees, one has no width, an opening reaches out of the node, or the junction is joined on that side
        /// according to the piece but no mouth is there.
        /// </summary>
        public static bool TryOpenings(ChainPiece piece, ChainPiece next, float wallLateral, int side, List<ChainSpan> openings)
        {
            openings.Clear();
            if (piece == null || next == null || !piece.MouthsKnown || float.IsNaN(wallLateral) || float.IsInfinity(wallLateral) || wallLateral * side <= 0f)
                return false;
            Vec3 u = HeadingAtEnd(piece);
            if (Vec3.Dot(u, HeadingAtStart(next)) < MinTurnCos)
                return false;
            Vec3 r = u.RightXZ();
            // Where the lanes end and begin: the node as the old cut has it, in which every opening must lie.
            float lo = piece.LanesS1At(wallLateral), hi = next.LanesS0At(wallLateral);
            if (!(hi > lo))
                return false;
            foreach (ChainMouth mouth in piece.Mouths)
            {
                float across = Vec3.Dot(mouth.Dir, r);
                if (across * side <= 0f)
                    continue;
                if (Math.Abs(across) < MinSin || !(mouth.HalfWidth >= 0.5f))
                    return false;
                // The road's strip is |across * x - along * y| <= w (x along the chain from the node, y across it, the road leaving as (along, across)); on the wall's line y = wallLateral it is
                // x in [(along y - w) / across, (along y + w) / across]. A road at a right angle: [-w, w]; a slanted one is wider and moves along.
                float along = Vec3.Dot(mouth.Dir, u);
                float x1 = (along * wallLateral - mouth.HalfWidth) / across, x2 = (along * wallLateral + mouth.HalfWidth) / across;
                float from = piece.S1 + Math.Min(x1, x2) - Margin, to = piece.S1 + Math.Max(x1, x2) + Margin;
                if (from < lo - Tolerance || to > hi + Tolerance)
                    return false;
                openings.Add(new ChainSpan(Math.Max(from, lo), Math.Min(to, hi)));
            }

            if (openings.Count == 0)
                return false;
            openings.Sort((a, b) => a.From.CompareTo(b.From));
            // Roads that open over each other are one opening.
            for (int i = openings.Count - 1; i > 0; i--)
            {
                if (openings[i].From <= openings[i - 1].To)
                {
                    openings[i - 1] = new ChainSpan(openings[i - 1].From, Math.Max(openings[i - 1].To, openings[i].To));
                    openings.RemoveAt(i);
                }
            }

            return true;
        }
    }
}
