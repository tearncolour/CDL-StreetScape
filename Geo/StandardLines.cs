using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>One line of the standard to draw along a road segment: where across it (m right of the edge's centre line, looking the way the edge runs), which marking, and the settings that differ from its defaults.</summary>
    public struct StandardLine
    {
        public float Lateral;
        public string Id;

        /// <summary>The key and value set on the marking's default settings ("" for none).</summary>
        public string Key, Value;

        /// <summary>A second setting (0.12.2: the form of an edge line beside parking); "" for none.</summary>
        public string Key2, Value2;

        public override string ToString() => $"{Id} at {Lateral:+0.00;-0.00} m" + (string.IsNullOrEmpty(Key) ? "" : $" ({Key}={Value}" + (string.IsNullOrEmpty(Key2) ? "" : $", {Key2}={Value2}") + ")");
    }

    /// <summary>
    /// The lines of GB 5768.3 along a road segment (the automatic marking's lines along, AutoMarking.PieceLines), from where its car lanes are and its speed: between two lanes of one direction a crossable lane line
    /// (线2/线3, 6-9 where the design speed is 60 km/h or more, else 2-4; the road's speed limit stands for the design speed, which the game does not keep); between the two directions the centre line, a
    /// double yellow solid line (线43) when each direction has two lanes or more, else the crossable yellow dashed line (线1); along the outer side of the outermost lanes and on each side of a median the edge
    /// line (线5, white on the right of the traffic, yellow on its left). Each is an ordinary marking item afterwards, to be changed or deleted on its own. Pure.
    /// </summary>
    public static class StandardLines
    {
        /// <summary>The design speed (km/h) from which the lane lines are 6-9 (GB 5768.3 5.2.3).</summary>
        public const float FastFrom = 60f;

        /// <summary>The markings Plan draws.</summary>
        public static readonly HashSet<string> Ids = new HashSet<string> { "mk.line.edge", "mk.line.lane", "mk.line.doubleyellow", "mk.line.centre" };

        public static List<StandardLine> Plan(IList<float> forward, IList<float> backward, float kmh, bool hasSpeed)
        {
            var lines = new List<StandardLine>();
            List<float> with = MarkingSnap.Merged(forward), against = MarkingSnap.Merged(backward);
            var lanes = new List<(float Lateral, bool Forward)>();
            foreach (float v in with)
                lanes.Add((v, true));
            foreach (float v in against)
            {
                // A lane read both ways (a road of one lane for both directions) is one lane: it has edge lines and nothing between.
                bool twice = false;
                foreach (float w in with)
                    twice |= Math.Abs(w - v) <= MarkingSnap.MergeWithin;
                if (!twice)
                    lanes.Add((v, false));
            }

            if (lanes.Count == 0)
                return lines;
            lanes.Sort((a, b) => a.Lateral.CompareTo(b.Lateral));
            string pattern = hasSpeed && kmh >= FastFrom ? "69" : "24";
            bool doubleYellow = with.Count >= 2 && against.Count >= 2;

            // The side of the traffic of a lane that a place is on: to the right of a lane that runs with the edge is +lateral; for a lane against it, -lateral.
            string SideOf(bool laneForward, bool placeAbove) => laneForward == placeAbove ? "r" : "l";

            float firstGap = lanes.Count > 1 ? Math.Min(lanes[1].Lateral - lanes[0].Lateral, MarkingSnap.MaxNeighbourGap) : MarkingSnap.DefaultLane;
            float lastGap = lanes.Count > 1 ? Math.Min(lanes[lanes.Count - 1].Lateral - lanes[lanes.Count - 2].Lateral, MarkingSnap.MaxNeighbourGap) : MarkingSnap.DefaultLane;
            lines.Add(new StandardLine { Lateral = lanes[0].Lateral - firstGap * 0.5f, Id = "mk.line.edge", Key = "side", Value = SideOf(lanes[0].Forward, false) });
            for (int i = 1; i < lanes.Count; i++)
            {
                var a = lanes[i - 1];
                var b = lanes[i];
                if (b.Lateral - a.Lateral > MarkingSnap.MaxNeighbourGap)
                {
                    // A median or a verge between: each side has its own edge line.
                    lines.Add(new StandardLine { Lateral = a.Lateral + MarkingSnap.DefaultLane * 0.5f, Id = "mk.line.edge", Key = "side", Value = SideOf(a.Forward, true) });
                    lines.Add(new StandardLine { Lateral = b.Lateral - MarkingSnap.DefaultLane * 0.5f, Id = "mk.line.edge", Key = "side", Value = SideOf(b.Forward, false) });
                    continue;
                }

                float middle = (a.Lateral + b.Lateral) * 0.5f;
                if (a.Forward == b.Forward)
                    lines.Add(new StandardLine { Lateral = middle, Id = "mk.line.lane", Key = "pattern", Value = pattern });
                else
                    lines.Add(new StandardLine { Lateral = middle, Id = doubleYellow ? "mk.line.doubleyellow" : "mk.line.centre", Key = "", Value = "" });
            }

            var last = lanes[lanes.Count - 1];
            lines.Add(new StandardLine { Lateral = last.Lateral + lastGap * 0.5f, Id = "mk.line.edge", Key = "side", Value = SideOf(last.Forward, true) });
            return lines;
        }
    }
}
