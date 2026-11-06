using System;
using System.Collections.Generic;
using System.Diagnostics;
using CDLC.TS.Geo;
using Game.UI;
using Unity.Entities;
using Unity.Mathematics;

namespace CDLC.TS
{
    /// <summary>
    /// Reads the junction ahead of a sign from the game (the pure JunctionRead over GameRoadGraph): the road the sign stands at, the way the traffic that sees it goes, the first junction ahead, its roads
    /// with their names, the lanes of the approach. Read only. Main thread. The first readings of a session are written to the log with what was read from the game (the lanes and the flags of the junction
    /// lanes they lead to), so that the lane reading can be checked against the game rather than the code. The graph that is built from the game (the edges, their car lanes and the turns they lead to,
    /// the roads' names) is kept while the network stands still (NetRevision) and is not too big (kMaxEdges): what depends on where the sign is, the walk along it, is made again for every reading, at
    /// the sign's own place.
    /// </summary>
    internal static class JunctionReader
    {
        private const int kMaxLogged = 24, kMaxEdges = 300;
        private static int s_Logged, s_Errors;
        private static readonly RevisionSlot<GameRoadGraph> s_Graph = new RevisionSlot<GameRoadGraph>();

        /// <summary>How many readings used the kept graph and how many made one (for the log).</summary>
        internal static string Counters => $"{s_Graph.Hits} reading(s) on the kept graph, {s_Graph.Made} graph(s) made";

        /// <summary>
        /// The junction ahead of a sign at position, on the road edge, facing yaw (the angle about Y: 0 faces +Z): the traffic that sees it goes the other way. The result says why when nothing was found.
        /// sequence: the junctions after it along the road are read too (JunctionSequence, for the junctions-ahead board).
        /// </summary>
        internal static JrResult Read(EntityManager em, NameSystem names, Entity edge, float3 position, float yaw, string why, bool sequence = false)
        {
            var clock = Stopwatch.StartNew();
            GameRoadGraph graph = null;
            int builtBefore = 0, lanesBefore = 0, logBefore = 0;
            JrResult result;
            try
            {
                graph = s_Graph.Get(NetRevision.Value, () => new GameRoadGraph(em, names, true), g => g.EdgesBuilt >= kMaxEdges);
                builtBefore = graph.EdgesBuilt;
                lanesBefore = graph.LanesRead;
                logBefore = graph.LaneLog.Count;
                if (!RoadReader.IsLiveEdge(em, edge))
                {
                    result = new JrResult { Why = "the sign is not on a road" };
                }
                else
                {
                    graph.Register(edge);
                    result = JunctionRead.Read(graph, edge.Index, position.x, position.z, -(float)Math.Sin(yaw), -(float)Math.Cos(yaw), JrOptions.Default);
                    if (sequence)
                        result.Sequence = JunctionSequence.Read(graph, result, JrOptions.Default);
                }
            }
            catch (Exception ex)
            {
                // Whatever the game's data does, placing a sign must not fail because of it: the sign keeps the settings it had and the editor says the reading failed.
                if (s_Errors++ < 3)
                    Mod.log.Error("Junction read failed: " + ex);
                // What a read that failed left in the graph may be partial.
                s_Graph.Clear();
                result = new JrResult { Why = "reading failed" };
            }

            // The first readings are written, and later ones only when they made something (a new edge of the graph): a reading on the kept graph is the same lanes again.
            if (graph != null && s_Logged < kMaxLogged && (s_Logged == 0 || graph.EdgesBuilt != builtBefore))
            {
                s_Logged++;
                Log(graph, result, edge, position, yaw, clock.Elapsed.TotalMilliseconds, why, graph.EdgesBuilt - builtBefore, graph.LanesRead - lanesBefore, logBefore);
            }

            return result;
        }

        private static void Log(GameRoadGraph graph, JrResult r, Entity edge, float3 position, float yaw, double ms, string why, int built, int lanes, int laneLogFrom)
        {
            var lines = new List<string>
            {
                $"Junction read ({why}): edge {edge.Index}:{edge.Version} at {position.x:0.#}, {position.z:0.#}, facing {yaw * 180f / (float)Math.PI:0} deg, traffic going {r.HeadX:0.00},{r.HeadZ:0.00}: " +
                (r.Found ? $"junction {r.Distance:0} m ahead (approach edge {r.ApproachEdge} \"{r.OwnName}\")" : "none: " + r.Why) +
                $"; {built} edge(s) and {lanes} car lane(s) read from the game ({graph.EdgesBuilt} in the graph) in {ms:0.0} ms"
            };
            foreach (JrArm arm in r.Arms)
                lines.Add($"  arm edge {arm.Edge} \"{arm.Name}\" road {arm.Road}: {arm.Angle:0} deg, {arm.Lanes} lane(s), {(arm.CanLeave ? "can be left" : "only comes in")} -> {arm.Direction}");
            if (r.Found)
            {
                var moves = new List<string>();
                foreach (int m in r.LaneMoves)
                    moves.Add(m == 0 ? "?" : ((m & JrMoves.Left) != 0 ? "L" : "") + ((m & JrMoves.Ahead) != 0 ? "A" : "") + ((m & JrMoves.Right) != 0 ? "R" : "") + ((m & JrMoves.UTurn) != 0 ? "U" : ""));
                lines.Add("  approach lanes from the driver's left: " + (moves.Count > 0 ? string.Join(" ", moves) : "none read"));
            }

            if (r.Sequence != null)
            {
                lines.Add($"  sequence: {r.Sequence.Stops.Count} junction(s) along the road{(r.Sequence.Why.Length > 0 ? ", ended: " + r.Sequence.Why : "")}");
                foreach (JrStop stop in r.Sequence.Stops)
                {
                    lines.Add($"  stop {stop.Total:0} m, node {stop.Read.Node} ({stop.Nodes} node(s)), judged {stop.Kind}, approach on {(stop.Read.ApproachHighway ? "highway" : "ordinary")} rules");
                    foreach (JrArm arm in stop.Arms)
                        lines.Add($"    arm edge {arm.Edge} \"{arm.Name}\" {arm.Angle:0} deg, {(arm.OneWay ? "one-way" : "two-way")}, {(arm.Highway ? "highway" : "ordinary")} rules, ends {arm.Rise:0.0} m from the ground, {arm.Direction}{(arm.IsRamp ? ", a ramp" : "")}");
                }
            }

            for (int i = laneLogFrom; i < graph.LaneLog.Count; i++)
                lines.Add("  " + graph.LaneLog[i]);
            Mod.log.Info(string.Join("\n", lines));
        }
    }
}
