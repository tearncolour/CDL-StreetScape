using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Where each piece of a wall (or any run of items) starts along the whole line, kept right when the follow changes the pieces' lengths (0.12.83 follow work; the owner: "隔音墙的MoveIt跟随不是很好").
    /// The pieces of a run carry the distance from the line's start to theirs (the phase, cm) and the line's whole length (the total, cm): a noise wall puts its posts every few metres counted from the line's start
    /// and ends its last panel at the total, so when a road got longer or shorter and the pieces followed it with the phases they had, every joint between two pieces had a panel of the wrong width (2 to 7 m for
    /// 4 m panels in the simulation) and a wall that got shorter lost its end. A piece's phase is the sum of the lengths of the pieces before it. Pure.
    /// </summary>
    public static class LinePhases
    {
        /// <summary>How far (cm) the phases may be from the sum of the lengths before them for the pieces to be taken as one run laid end to end (the slack LineCut takes for the same: the lengths are kept in whole centimetres a chord); a run cut by hand, or two walls whose settings are alike, is not one.</summary>
        public const long ChainToleranceCm = LineCut.PhaseSlackCm;

        /// <summary>
        /// The phases the pieces have when each is as long as nowMetres says (phase 0's piece starts where it did), and the total. phaseCm and atPhaseMetres are what the pieces have: the phase of each and its
        /// length when that phase was made; they must be a chain (each starts where the one before, by phase, ended, and the total, when known, is where the last ends). False when they are not, or when a number is
        /// not one: nothing is to be made again then. newPhaseCm gets the new phase of each piece in the order the caller gave them.
        /// </summary>
        public static bool TryRebase(IList<long> phaseCm, IList<float> atPhaseMetres, IList<float> nowMetres, long totalCm, long[] newPhaseCm, out long newTotalCm)
        {
            newTotalCm = 0;
            int n = phaseCm.Count;
            if (n < 1 || atPhaseMetres.Count != n || nowMetres.Count != n || newPhaseCm.Length < n)
                return false;
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                order[i] = i;
                if (phaseCm[i] < 0 || !(atPhaseMetres[i] >= 0f) || !(nowMetres[i] >= 0f) || float.IsInfinity(atPhaseMetres[i]) || float.IsInfinity(nowMetres[i]))
                    return false;
            }

            Array.Sort(order, (a, b) => phaseCm[a] != phaseCm[b] ? phaseCm[a].CompareTo(phaseCm[b]) : a.CompareTo(b));
            for (int k = 1; k < n; k++)
            {
                long expected = phaseCm[order[k - 1]] + (long)Math.Round(atPhaseMetres[order[k - 1]] * 100.0);
                if (Math.Abs(phaseCm[order[k]] - expected) > ChainToleranceCm)
                    return false;
            }

            long end = phaseCm[order[n - 1]] + (long)Math.Round(atPhaseMetres[order[n - 1]] * 100.0);
            if (totalCm > 0 && Math.Abs(totalCm - end) > ChainToleranceCm)
                return false;
            long first = phaseCm[order[0]];
            double run = 0.0;
            for (int k = 0; k < n; k++)
            {
                newPhaseCm[order[k]] = first + (long)Math.Round(run * 100.0);
                run += nowMetres[order[k]];
            }

            newTotalCm = first + (long)Math.Round(run * 100.0);
            return true;
        }
    }
}
