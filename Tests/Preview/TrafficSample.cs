using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Networks for looking at the traffic-state screen with the real painter: made by the real walk (TrafficWalk) over a made-up city (a curved avenue, a ring road, a river road, an
    /// elevated road, short and long edges), published as items 9001 (heading north, an avenue with seven junctions) and 9002 (heading east over the same city), so that
    /// `--variant gb.vms.traffic "size=m;id=9001;rev=1" label` draws what a walked network looks like on the screen, not only the hand-made demonstration.
    /// </summary>
    internal static class TrafficSample
    {
        private sealed class Graph : ITrafficGraph
        {
            private readonly Dictionary<int, TwEdge> m_Edges = new Dictionary<int, TwEdge>();
            private readonly Dictionary<int, List<int>> m_Nodes = new Dictionary<int, List<int>>();
            private int m_Next = 1;

            public int Add(int a, int b, string name, int road, float forward, float backward, params float[] xz)
            {
                int id = m_Next++;
                m_Edges[id] = new TwEdge { Id = id, StartNode = a, EndNode = b, Points = xz, RatioForward = forward, RatioBackward = backward, Name = name, Road = road };
                foreach (int node in new[] { a, b })
                {
                    if (!m_Nodes.TryGetValue(node, out List<int> list))
                        m_Nodes[node] = list = new List<int>();
                    list.Add(id);
                }

                return id;
            }

            public bool TryEdge(int id, out TwEdge edge) => m_Edges.TryGetValue(id, out edge);

            public IReadOnlyList<int> EdgesAt(int node) => m_Nodes.TryGetValue(node, out List<int> list) ? list : (IReadOnlyList<int>)new int[0];
        }

        private static float[] Arc(float cx, float cz, float r, float from, float to, int n)
        {
            var p = new float[n * 2];
            for (int i = 0; i < n; i++)
            {
                double a = (from + (to - from) * i / (n - 1)) * Math.PI / 180.0;
                p[i * 2] = cx + (float)(Math.Sin(a) * r);
                p[i * 2 + 1] = cz + (float)(Math.Cos(a) * r);
            }

            return p;
        }

        public static void Register()
        {
            var g = new Graph();
            // The avenue (东海西路) going north from the sign at (0, 0): junctions at 280, 560 (a skewed one), 900, 1200; short edges between them.
            g.Add(1, 2, "东海西路", 1, 0.95f, 0.9f, 0, -150, 0, 280);
            g.Add(2, 3, "东海西路", 1, 0.5f, 0.9f, 0, 280, 8, 420, 30, 560);
            g.Add(3, 4, "东海西路", 1, 0.3f, 0.85f, 30, 560, 40, 740, 40, 900);
            g.Add(4, 5, "东海西路", 1, 0.9f, 0.9f, 40, 900, 40, 1200);
            // Cross roads: 延安三路 at 280 (two-way), 山东路 at 560 (skewed, one-way east), 太平角四路 at 900, 胶宁高架 at 1200.
            g.Add(2, 10, "延安三路", 2, 0.9f, 0.9f, 0, 280, -150, 290, -300, 330, -380, 330);
            g.Add(2, 11, "延安三路", 2, 0.2f, 0.9f, 0, 280, 160, 275, 330, 270);
            g.Add(3, 12, "山东路", 3, 0.9f, 0.9f, 30, 560, -120, 520, -300, 500);
            g.Add(13, 3, "山东路", 3, 0.9f, -1f, 330, 610, 150, 590, 30, 560);
            g.Add(4, 14, "太平角四路", 4, 0.9f, 0.9f, 40, 900, -150, 905, -300, 905);
            g.Add(4, 15, "太平角四路", 4, 0.45f, 0.3f, 40, 900, 200, 905, 360, 905);
            g.Add(5, 16, "胶宁高架", 5, 0.9f, 0.9f, 40, 1200, -200, 1210);
            g.Add(5, 17, "胶宁高架", 5, 0.2f, 0.9f, 40, 1200, 220, 1195);
            // A ring road that crosses the avenue's east side, curved, and joins two cross roads; a river road beside the avenue.
            g.Add(11, 18, "环湖路", 6, 0.9f, 0.9f, Arc(330, 270, 60, 0, 90, 8));
            g.Add(15, 19, "环湖路", 6, 0.9f, 0.4f, 360, 905, 380, 800, 360, 700);
            g.Add(12, 20, "", 0, 0.9f, 0.9f, -300, 500, -330, 420);

            foreach ((int id, float hx, float hz, float ox, float oz) in new[] { (9001, 0f, 1f, 0f, 0f), (9002, 1f, 0f, 0f, 0f) })
            {
                TmNetwork net = TrafficWalk.Walk(g, 1, ox, oz, hx, hz, new TwOptions { Junctions = 4, Range = 1400f, MaxEdges = 80, Stub = 160f, Behind = 20f },
                    (key, ratio) => TrafficMap.LevelOf(ratio, 0.7f, 0.4f));
                TrafficMap.Set(id, net, 1);
                Console.WriteLine($"traffic sample {id}: {net.Edges.Count} stretches, known {net.Known}");
            }
        }
    }
}
