using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The barrier styles of 0.12.49 (Barriers.More, Barriers.Noise; scratch/research-barriers/REPORT.md): the noise barriers (metal, enclosures, cement, brick, planted), the classical and the ring
    /// railings, the water barriers' new kinds: sound closed bodies held to the ground, the suppliers' sizes, on a bend, on a slope, across the pieces of a run and at its ends.
    /// </summary>
    internal static class MoreBarrierTests
    {
        private static readonly string[] kIds =
        {
            MarkingTemplates.NoiseBarrierId, MarkingTemplates.NoiseCoverId, MarkingTemplates.CementNoiseId, MarkingTemplates.BrickNoiseId, MarkingTemplates.GreenNoiseId, MarkingTemplates.FretRailingId,
            MarkingTemplates.RingRailingId
        };

        /// <summary>Settings that are a different body (a style, a top), each checked like the defaults.</summary>
        private static readonly (string Id, string Settings)[] kVariants =
        {
            (MarkingTemplates.NoiseBarrierId, ""), (MarkingTemplates.NoiseBarrierId, "top=crank;band=wide"), (MarkingTemplates.NoiseBarrierId, "top=arc;band=upper;base=pl"),
            (MarkingTemplates.NoiseBarrierId, "band=all;base=w100;post=2.5"), (MarkingTemplates.NoiseCoverId, ""), (MarkingTemplates.NoiseCoverId, "form=semi"),
            (MarkingTemplates.NoiseCoverId, "form=semi;where=l;arm=8"), (MarkingTemplates.CementNoiseId, ""), (MarkingTemplates.CementNoiseId, "band=upper;base=c100"),
            (MarkingTemplates.BrickNoiseId, ""), (MarkingTemplates.BrickNoiseId, "band=mid;brick=grey"), (MarkingTemplates.GreenNoiseId, ""), (MarkingTemplates.GreenNoiseId, "bed=back;where=l"), (MarkingTemplates.FretRailingId, ""),
            (MarkingTemplates.FretRailingId, "pattern=bars"), (MarkingTemplates.RingRailingId, ""), (MarkingTemplates.WaterBarrierId, "style=holes"), (MarkingTemplates.WaterBarrierId, "style=fence;scheme=y")
        };

        public static void RunAll()
        {
            Program.Run("more barriers: the catalogue, sound closed bodies on a straight, a bend and a slope", Catalogue);
            Program.Run("more barriers: nothing floats (every body held to the ground through the others)", Held);
            Program.Run("more barriers: the pieces of a run in step, its ends", Pieces);
            Program.Run("more barriers: the metal noise barrier's posts, modules, glass and tops", Noise);
            Program.Run("more barriers: the enclosures (semi and full)", Covers);
            Program.Run("more barriers: the cement, the brick and the planted sound walls", Walls);
            Program.Run("more barriers: the classical railing and the ring railing", Railings);
            Program.Run("more barriers: three-hole and fence water barriers", Water);
            Program.Run("more barriers: the colours keep their numbers, glass and the new surfaces", Colours);
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        /// <summary>The height of a straight path along Z at z.</summary>
        private static float HeightAt(List<Vec3> path, float z)
        {
            Vec3 a = path[0], b = path[path.Count - 1];
            return a.Y + (b.Y - a.Y) * (z - a.Z) / (b.Z - a.Z);
        }

        private static List<Vec3> Straight(float length, float rise = 0f) => new List<Vec3> { P(0f, 0f), P(0f, length, rise) };

        /// <summary>A bend of radius 15 m through 80 degrees in chords of about 2 m, as a road's lanes give it.</summary>
        private static List<Vec3> Bend()
        {
            var line = new List<Vec3>();
            for (int i = 0; i <= 11; i++)
            {
                double a = i / 11.0 * 80.0 * Math.PI / 180.0;
                line.Add(P((float)(15.0 - 15.0 * Math.Cos(a)), (float)(15.0 * Math.Sin(a))));
            }

            return line;
        }

        private static MarkingPlan PlanOf(string id, string settings, List<Vec3> line, float phase = 0f, float total = 0f)
        {
            GuideTemplate t = MarkingTemplates.ById(id);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in (settings ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        private static string Name((string Id, string Settings) v) => v.Id + (v.Settings.Length > 0 ? "|" + v.Settings : "");

        private static bool Wound(MeshBuilder mesh)
        {
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                Vec3 n = MeshBuilder.Cross(b - a, c - a);
                if (n.Length > 1e-8f && Vec3.Dot(n, mesh.Normals[mesh.Indices[t]]) < 0f)
                    return false;
            }

            return true;
        }

        /// <summary>The welded index of each vertex: vertices within 0.2 mm of one another are one.</summary>
        private static int[] Weld(MeshBuilder mesh)
        {
            const float cell = 0.001f, within = 0.0002f;
            var grid = new Dictionary<(int, int, int), List<int>>();
            var ids = new int[mesh.VertexCount];
            var firsts = new List<Vec3>();
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vec3 p = mesh.Positions[i];
                int cx = (int)Math.Floor(p.X / cell), cy = (int)Math.Floor(p.Y / cell), cz = (int)Math.Floor(p.Z / cell);
                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                {
                    for (int dy = -1; dy <= 1 && found < 0; dy++)
                    {
                        for (int dz = -1; dz <= 1 && found < 0; dz++)
                        {
                            if (!grid.TryGetValue((cx + dx, cy + dy, cz + dz), out List<int> near))
                                continue;
                            foreach (int j in near)
                            {
                                if ((firsts[j] - p).Length <= within)
                                {
                                    found = j;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (found < 0)
                {
                    found = firsts.Count;
                    firsts.Add(p);
                    if (!grid.TryGetValue((cx, cy, cz), out List<int> list))
                        grid[(cx, cy, cz)] = list = new List<int>();
                    list.Add(found);
                }

                ids[i] = found;
            }

            return ids;
        }

        /// <summary>
        /// The edges of a mesh that are not closed: an edge of a triangle with no edge of another triangle the other way along it (welded). Two-sided sheets are closed by their other side. Edges at or
        /// under the ground (the open feet of what stands in it) do not count.
        /// </summary>
        private static List<(Vec3 A, Vec3 B)> OpenEdges(MeshBuilder mesh, Func<Vec3, float> ground)
        {
            int[] ids = Weld(mesh);
            var count = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int a = ids[mesh.Indices[t + e]], b = ids[mesh.Indices[t + (e + 1) % 3]];
                    if (a == b)
                        continue;
                    count.TryGetValue((a, b), out int n);
                    count[(a, b)] = n + 1;
                }
            }

            var open = new List<(Vec3, Vec3)>();
            var at = new Dictionary<int, Vec3>();
            for (int i = 0; i < mesh.VertexCount; i++)
                at[ids[i]] = mesh.Positions[i];
            foreach (KeyValuePair<(int, int), int> pair in count)
            {
                count.TryGetValue((pair.Key.Item2, pair.Key.Item1), out int back);
                if (back >= pair.Value)
                    continue;
                Vec3 a = at[pair.Key.Item1], b = at[pair.Key.Item2];
                if (a.Y <= ground(a) + 1e-3f && b.Y <= ground(b) + 1e-3f)
                    continue;
                open.Add((a, b));
            }

            return open;
        }

        private static void Catalogue()
        {
            foreach (string id in kIds)
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(def.Template), id + ": in the catalogue, in the barriers' group, drawn along a road");
                Program.Check(MarkingGround.Lies(def), id + ": laid on the ground");
                Program.Check(MarkingTemplates.PlanOf(def, "").HasSolids, id + ": a body of its own for its picture");
            }

            foreach ((string Id, string Settings) v in kVariants)
            {
                foreach ((string shape, List<Vec3> line) in new[] { ("straight", Straight(12f)), ("bend", Bend()), ("slope", Straight(12f, 1f)) })
                {
                    MarkingPlan plan = PlanOf(v.Id, v.Settings, line);
                    // The ground: the line's height as the item's path has it (RunPath keeps heights to a few millimetres).
                    List<Vec3> path = RunPath.Decode(RunPath.Encode(line, out _));
                    Func<Vec3, float> ground = p => shape == "slope" ? HeightAt(path, p.Z) : 0f;
                    string what = Name(v) + " " + shape;
                    Program.Check(plan != null && plan.HasSolids && plan.TriangleCount > 100, what + ": a body: " + plan?.TriangleCount);
                    int open = 0;
                    string example = "";
                    foreach (MarkingPart part in plan.Parts)
                    {
                        Program.Check(part.Solid.HasValue, what + ": only solid parts");
                        Program.Check(Wound(part.Mesh), what + ": every triangle faces its normal (" + part.Solid + " " + part.Detail + ")");
                        bool finite = true;
                        for (int i = 0; i < part.Mesh.VertexCount; i++)
                            finite &= part.Mesh.Positions[i].IsFinite && !float.IsNaN(part.Mesh.U[i]) && !float.IsNaN(part.Mesh.V[i]) && !float.IsInfinity(part.Mesh.U[i]) && !float.IsInfinity(part.Mesh.V[i]);
                        Program.Check(finite, what + ": every point and texture coordinate a number");
                        List<(Vec3 A, Vec3 B)> edges = OpenEdges(part.Mesh, ground);
                        for (int e = 0; e < edges.Count && e < 3; e++)
                            example += " " + part.Solid + " " + part.Detail + " " + edges[e].A + "-" + edges[e].B;
                        open += edges.Count;
                    }

                    Program.Check(open == 0, what + ": closed bodies (no open edge over the ground): " + open + " " + example);
                    // As heavy as the heaviest railings of 0.12.34 (the Beijing-style railing: 400 triangles to the metre), an enclosure (two walls and a roof) not twice that.
                    float metres = 0f;
                    for (int i = 1; i < line.Count; i++)
                        metres += Vec3.DistanceXZ(line[i - 1], line[i]);
                    Program.Check(plan.TriangleCount < (v.Id == MarkingTemplates.NoiseCoverId ? 700f : 500f) * metres, $"{what}: not too heavy: {plan.TriangleCount} triangles on {metres:0.0} m");
                }
            }
        }

        /// <summary>The bodies of a plan (the connected parts of each of its meshes, welded) and their boxes, the box grown by grow each way.</summary>
        private static List<(Vec3 Lo, Vec3 Hi, string What)> Bodies(MarkingPlan plan, float grow)
        {
            var bodies = new List<(Vec3, Vec3, string)>();
            foreach (MarkingPart part in plan.Parts)
            {
                MeshBuilder mesh = part.Mesh;
                int[] ids = Weld(mesh);
                int n = 0;
                foreach (int id in ids)
                    n = Math.Max(n, id + 1);
                var parent = new int[n];
                for (int i = 0; i < n; i++)
                    parent[i] = i;
                int Find(int x)
                {
                    while (parent[x] != x)
                        x = parent[x] = parent[parent[x]];
                    return x;
                }

                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int a = Find(ids[mesh.Indices[t]]), b = Find(ids[mesh.Indices[t + 1]]), c = Find(ids[mesh.Indices[t + 2]]);
                    parent[b] = a;
                    parent[Find(c)] = a;
                }

                var boxes = new Dictionary<int, (Vec3 Lo, Vec3 Hi)>();
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    int root = Find(ids[i]);
                    Vec3 p = mesh.Positions[i];
                    boxes[root] = boxes.TryGetValue(root, out (Vec3 Lo, Vec3 Hi) b)
                        ? (new Vec3(Math.Min(b.Lo.X, p.X), Math.Min(b.Lo.Y, p.Y), Math.Min(b.Lo.Z, p.Z)), new Vec3(Math.Max(b.Hi.X, p.X), Math.Max(b.Hi.Y, p.Y), Math.Max(b.Hi.Z, p.Z)))
                        : (p, p);
                }

                foreach ((Vec3 lo, Vec3 hi) in boxes.Values)
                    bodies.Add((new Vec3(lo.X - grow, lo.Y - grow, lo.Z - grow), new Vec3(hi.X + grow, hi.Y + grow, hi.Z + grow), part.Solid + " " + part.Detail));
            }

            return bodies;
        }

        private static bool Overlap((Vec3 Lo, Vec3 Hi, string) a, (Vec3 Lo, Vec3 Hi, string) b) =>
            a.Lo.X <= b.Hi.X && b.Lo.X <= a.Hi.X && a.Lo.Y <= b.Hi.Y && b.Lo.Y <= a.Hi.Y && a.Lo.Z <= b.Hi.Z && b.Lo.Z <= a.Hi.Z;

        private static void Held()
        {
            // A body is held when it reaches the ground or touches (its box within 1 mm of the box of) a held body: what is left over floats.
            foreach ((string Id, string Settings) v in kVariants)
            {
                foreach ((string shape, List<Vec3> line) in new[] { ("straight", Straight(9f)), ("bend", Bend()) })
                {
                    List<(Vec3 Lo, Vec3 Hi, string What)> bodies = Bodies(PlanOf(v.Id, v.Settings, line), 0.001f);
                    var held = new bool[bodies.Count];
                    var queue = new Queue<int>();
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        if (bodies[i].Lo.Y <= 0.002f)
                        {
                            held[i] = true;
                            queue.Enqueue(i);
                        }
                    }

                    while (queue.Count > 0)
                    {
                        int i = queue.Dequeue();
                        for (int j = 0; j < bodies.Count; j++)
                        {
                            if (!held[j] && Overlap(bodies[i], bodies[j]))
                            {
                                held[j] = true;
                                queue.Enqueue(j);
                            }
                        }
                    }

                    int floating = 0;
                    string example = "";
                    for (int i = 0; i < bodies.Count; i++)
                    {
                        if (held[i])
                            continue;
                        floating++;
                        if (example.Length == 0)
                            example = bodies[i].What + " " + bodies[i].Lo + " .. " + bodies[i].Hi;
                    }

                    Program.Check(floating == 0, $"{Name(v)} {shape}: {bodies.Count} bodies, none floating: {floating} {example}");
                }
            }
        }

        /// <summary>The middles along Z of the base plates (their tops at y within dy) near X = x.</summary>
        private static List<float> PostsAt(MarkingPlan plan, SolidColour colour, float y, float x, float dy = 1e-3f)
        {
            var zs = new List<float>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid != colour)
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    if (Math.Abs(p.Y - y) <= dy && Math.Abs(p.X - x) < 0.2f)
                        zs.Add(p.Z);
                }
            }

            zs.Sort();
            var middles = new List<float>();
            int first = 0;
            for (int i = 1; i <= zs.Count; i++)
            {
                if (i == zs.Count || zs[i] - zs[i - 1] > 0.5f)
                {
                    middles.Add((zs[first] + zs[i - 1]) * 0.5f);
                    first = i;
                }
            }

            return middles;
        }

        private static int Near(List<Vec3> points, Vec3 p, float within)
        {
            int n = 0;
            foreach (Vec3 q in points)
                n += (q - p).Length <= within ? 1 : 0;
            return n;
        }

        /// <summary>The triangles of a plan's parts by part (colour and detail): their middles moved by offset and their normals; those that reach within 1 mm of z = cut or across it left out.</summary>
        private static Dictionary<string, List<(Vec3 Middle, Vec3 Normal)>> Triangles(MarkingPlan plan, Vec3 offset, float cut)
        {
            var all = new Dictionary<string, List<(Vec3, Vec3)>>();
            foreach (MarkingPart part in plan.Parts)
            {
                string key = part.Solid + " " + part.Detail;
                if (!all.TryGetValue(key, out List<(Vec3, Vec3)> list))
                    all[key] = list = new List<(Vec3, Vec3)>();
                MeshBuilder m = part.Mesh;
                for (int t = 0; t + 2 < m.Indices.Count; t += 3)
                {
                    Vec3 a = m.Positions[m.Indices[t]] + offset, b = m.Positions[m.Indices[t + 1]] + offset, c = m.Positions[m.Indices[t + 2]] + offset;
                    float lo = Math.Min(a.Z, Math.Min(b.Z, c.Z)), hi = Math.Max(a.Z, Math.Max(b.Z, c.Z));
                    if (hi > cut - 1e-3f && lo < cut + 1e-3f)
                        continue;
                    list.Add(((a + b + c) * (1f / 3f), m.Normals[m.Indices[t]]));
                }
            }

            return all;
        }

        private static void Pieces()
        {
            // A straight run of 20 m in two pieces (the second from 7.3 m): every triangle of the whole run away from the cut is drawn by one of the pieces, once (posts, units and figures are not drawn
            // twice where the pieces meet, nor left out), and the pieces draw nothing else.
            foreach ((string Id, string Settings) v in kVariants)
            {
                Dictionary<string, List<(Vec3 Middle, Vec3 Normal)>> whole = Triangles(PlanOf(v.Id, v.Settings, Straight(20f), 0f, 20f), Vec3.Zero, 7.3f);
                Dictionary<string, List<(Vec3 Middle, Vec3 Normal)>> a = Triangles(PlanOf(v.Id, v.Settings, Straight(7.3f), 0f, 20f), Vec3.Zero, 7.3f);
                Dictionary<string, List<(Vec3 Middle, Vec3 Normal)>> b = Triangles(PlanOf(v.Id, v.Settings, Straight(12.7f), 7.3f, 20f), P(0f, 7.3f), 7.3f);
                int missing = 0, extra = 0;
                string example = "";
                foreach (KeyValuePair<string, List<(Vec3 Middle, Vec3 Normal)>> part in whole)
                {
                    var pieces = new List<(Vec3 Middle, Vec3 Normal)>();
                    if (a.TryGetValue(part.Key, out List<(Vec3, Vec3)> la))
                        pieces.AddRange(la);
                    if (b.TryGetValue(part.Key, out List<(Vec3, Vec3)> lb))
                        pieces.AddRange(lb);
                    var used = new bool[pieces.Count];
                    foreach ((Vec3 middle, Vec3 normal) in part.Value)
                    {
                        int match = -1;
                        for (int i = 0; i < pieces.Count && match < 0; i++)
                        {
                            if (!used[i] && (pieces[i].Middle - middle).Length < 5e-4f && Vec3.Dot(pieces[i].Normal, normal) > 0.99f)
                                match = i;
                        }

                        if (match < 0)
                        {
                            missing++;
                            if (example.Length == 0)
                                example = "missing " + part.Key + " " + middle;
                            continue;
                        }

                        used[match] = true;
                    }

                    foreach (bool u in used)
                        extra += u ? 0 : 1;
                }

                Program.Check(missing == 0 && extra == 0, $"{Name(v)}: the pieces draw the whole run once: {missing} triangles missing, {extra} more {example}");
            }

            // On a bend, where two pieces meet (7.3 m along a curve of 15 m) their sections are one: every corner of either piece on the plane they share has the other's there.
            List<Vec3> bend = Bend();
            double cut = 7.3 / 15.0;
            Vec3 j = P((float)(15.0 - 15.0 * Math.Cos(cut)), (float)(15.0 * Math.Sin(cut))), tangent = P((float)Math.Sin(cut), (float)Math.Cos(cut));
            List<Vec3> first = new List<Vec3>(), second = new List<Vec3> { j };
            foreach (Vec3 p in bend)
                (Math.Atan2(p.Z, 15.0 - p.X) < cut ? first : second).Add(p);
            first.Add(j);
            float length = 0f;
            for (int i = 1; i < first.Count; i++)
                length += Vec3.DistanceXZ(first[i - 1], first[i]);
            var style = new NoiseStyle { Height = 3f, Spacing = 2f, Base = NoiseBase.CrashWall, BaseHeight = 0.81f, Band = GlassBand.Middle, Panel = SolidColour.LightGray, Glass = SolidColour.Glass, Post = SolidColour.Metal };
            NoiseStyle With(NoiseTop top, float height = 3f)
            {
                NoiseStyle copy = style;
                copy.Top = top;
                copy.Height = height;
                return copy;
            }

            var builders = new (string Name, Func<List<Vec3>, float, bool, bool, List<MarkingShape>> Build)[]
            {
                ("noise", (l, ph, f, z) => Barriers.NoiseBarrier(l, style, 1, ph, f, z)),
                ("noise arc", (l, ph, f, z) => Barriers.NoiseBarrier(l, With(NoiseTop.Arc), 1, ph, f, z)),
                ("noise cranked left", (l, ph, f, z) => Barriers.NoiseBarrier(l, With(NoiseTop.Cranked), -1, ph, f, z)),
                ("full cover", (l, ph, f, z) => Barriers.NoiseCoverBarrier(l, NoiseCover.Full, With(NoiseTop.Upright, 4f), 4f, 12f, 1, ph, f, z)),
                ("semi cover", (l, ph, f, z) => Barriers.NoiseCoverBarrier(l, NoiseCover.Semi, With(NoiseTop.Upright, 4f), 6f, 12f, 1, ph, f, z)),
                ("cement", (l, ph, f, z) => Barriers.SoundWallBarrier(l, SoundWall.Cement, With(NoiseTop.Upright, 2.4f), 0f, SolidColour.Brick, 1, ph, f, z)),
                ("brick", (l, ph, f, z) => Barriers.SoundWallBarrier(l, SoundWall.Brick, style, 1.2f, SolidColour.Brick, 1, ph, f, z)),
                ("green", (l, ph, f, z) => Barriers.GreenNoiseBarrier(l, style, 1, 1, ph, f, z, null)),
                ("fret", (l, ph, f, z) => Barriers.FretRailing(l, 1.1f, FretPattern.Fret, SolidColour.DarkBrown, ph, f, z)),
                ("ring", (l, ph, f, z) => Barriers.RingRailing(l, 1.1f, SolidColour.White, ph, f, z))
            };

            foreach ((string name, Func<List<Vec3>, float, bool, bool, List<MarkingShape>> build) in builders)
            {
                var mine = new Dictionary<string, List<Vec3>>();
                var theirs = new Dictionary<string, List<Vec3>>();
                foreach ((List<MarkingShape> shapes, Dictionary<string, List<Vec3>> into) in new[] { (build(first, 0f, true, false), mine), (build(second, length, false, true), theirs) })
                {
                    foreach (MarkingShape shape in shapes)
                    {
                        string key = shape.Solid + " " + shape.Detail;
                        if (!into.TryGetValue(key, out List<Vec3> list))
                            into[key] = list = new List<Vec3>();
                        foreach (Vec3 p in shape.Mesh.Positions)
                        {
                            if (Math.Abs(Vec3.Dot(p - j, tangent)) < 1e-4f)
                                list.Add(p);
                        }
                    }
                }

                int alone = 0, shared = 0;
                string example = "";
                foreach ((Dictionary<string, List<Vec3>> from, Dictionary<string, List<Vec3>> to) in new[] { (mine, theirs), (theirs, mine) })
                {
                    foreach (KeyValuePair<string, List<Vec3>> part in from)
                    {
                        List<Vec3> other = to.TryGetValue(part.Key, out List<Vec3> o) ? o : new List<Vec3>();
                        foreach (Vec3 p in part.Value)
                        {
                            if (Near(other, p, 1e-3f) > 0)
                            {
                                shared++;
                                continue;
                            }

                            alone++;
                            if (example.Length == 0)
                                example = part.Key + " " + p;
                        }
                    }
                }

                Program.Check(shared > 0 && alone == 0, $"{name}: on a bend the pieces meet on one plane: {shared} corners shared, {alone} alone {example}");
            }

            // The ends of the whole run: a post at each (the noise barrier's base plates at 0 and 19 m) and the foundation closed there, not where the pieces meet.
            MarkingPlan run = PlanOf(MarkingTemplates.NoiseBarrierId, "base=pl", Straight(19f), 0f, 19f);
            List<float> plates = PostsAt(run, SolidColour.Metal, 0.32f, 0f);
            Program.Check(plates.Count == 11 && Math.Abs(plates[0]) < 0.1f && Math.Abs(plates[plates.Count - 1] - 19f) < 0.1f, "posts every 2 m and one at the run's end, 1 m after the last: " + string.Join(", ", plates));
            Program.Check(Caps(run, SolidColour.Concrete) == 2, "the footing closed at both ends");
            Program.Check(Caps(PlanOf(MarkingTemplates.NoiseBarrierId, "base=pl", Straight(10f), 5f, 30f), SolidColour.Concrete) == 0, "a piece in the middle has no caps");
        }

        /// <summary>The places along Z where a colour's solid has faces looking along the line (a body's ends).</summary>
        private static int Caps(MarkingPlan plan, SolidColour colour)
        {
            var seen = new List<float>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid != colour)
                    continue;
                MeshBuilder mesh = part.Mesh;
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    if (Math.Abs(mesh.Normals[mesh.Indices[t]].Z) < 0.99f)
                        continue;
                    float z = mesh.Positions[mesh.Indices[t]].Z;
                    if (!seen.Exists(s => Math.Abs(s - z) < 1e-3f))
                        seen.Add(z);
                }
            }

            return seen.Count;
        }

        private static void Bounds(MarkingPlan plan, Func<MarkingPart, bool> which, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MarkingPart part in plan.Parts)
            {
                if (!which(part))
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    lo = new Vec3(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                    hi = new Vec3(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                }
            }
        }

        private static void Noise()
        {
            // On a crash wall of 0.81 m: a base plate of 20 mm, 3 m of panels (six modules of 500 mm), the cap strip 10 mm over them; the posts every 2 m.
            MarkingPlan plan = PlanOf(MarkingTemplates.NoiseBarrierId, "band=none", Straight(8f));
            Bounds(plan, p => p.Solid == SolidColour.LightGray && p.Detail != DetailKind.Louvre, out Vec3 lo, out Vec3 hi);
            Program.Near(lo.Y, 0.83f, 1e-3f, "the panels on the base plates on the crash wall (0.81 m and 20 mm)");
            Program.Near(hi.Y, 0.83f + 3f + 0.01f, 1e-3f, "3 m of panels and the cap strip");
            Program.Check(PostsAt(plan, SolidColour.Metal, 0.83f, 0f).Count == 5, "posts every 2 m on 8 m");
            // The panels go into the posts' channels: HW100's 84 mm inside, the panels 80 mm; their ends 17 mm short of the web.
            Program.Check(hi.X <= 0.043f + 1e-3f && lo.X >= -0.043f - 1e-3f, $"the panels and the cap within the channel and its lip: {lo.X:0.000} .. {hi.X:0.000}");
            Bounds(plan, p => p.Solid == SolidColour.Metal, out Vec3 slo, out Vec3 shi);
            Program.Check(shi.X <= 0.13f && slo.X >= -0.13f, "HW100 posts and their plates for 3 m");
            Bounds(PlanOf(MarkingTemplates.NoiseBarrierId, "height=400", Straight(8f)), p => p.Solid == SolidColour.Metal && p.Detail == DetailKind.Galvanised, out Vec3 l4, out Vec3 h4);
            Program.Check(h4.X - l4.X > 0.125f + 0.159f, "HW125 posts for 4 m (their plates 125 + 160 mm across)");
            // The louvred face toward the traffic: on the line's right by default, on its left with where=l.
            Bounds(plan, p => p.Detail == DetailKind.Louvre, out Vec3 flo, out _);
            Program.Check(flo.X > 0.039f, "the louvres face the traffic on the right: " + flo.X);
            Bounds(PlanOf(MarkingTemplates.NoiseBarrierId, "band=none;where=l", Straight(8f)), p => p.Detail == DetailKind.Louvre, out _, out Vec3 fhi);
            Program.Check(fhi.X < -0.039f, "and on the left: " + fhi.X);
            // The glass band: 1 m half way up (the modules 1.0 to 2.0 m of 3 m), PC sheet 15 mm in frames.
            MarkingPlan glass = PlanOf(MarkingTemplates.NoiseBarrierId, "band=mid", Straight(8f));
            Bounds(glass, p => p.Solid.HasValue && SolidGlass.Is(p.Solid.Value), out Vec3 glo, out Vec3 ghi);
            Program.Near(glo.Y, 0.83f + 1.0f + 0.05f, 1e-3f, "the glass from 1 m up (over its frame)");
            Program.Near(ghi.Y, 0.83f + 2.0f - 0.05f, 1e-3f, "to 2 m");
            Program.Near(ghi.X - glo.X, 0.015f, 1e-3f, "15 mm sheet");
            Program.Check(glass.Parts.Exists(p => p.Solid == SolidColour.Glass && p.Detail == DetailKind.None), "the glass is the game's glass: no baked detail");
            // A cranked top: 1 m more at 30 degrees toward the traffic; an arched one 45 degrees in three modules.
            Bounds(PlanOf(MarkingTemplates.NoiseBarrierId, "top=crank;band=none", Straight(8f)), p => p.Solid == SolidColour.LightGray && p.Detail != DetailKind.Louvre, out Vec3 clo, out Vec3 chi);
            Program.Check(chi.X > 0.5f && chi.X < 0.56f && chi.Y > 0.83f + 3f + 0.85f && chi.Y < 0.83f + 3f + 0.9f, $"the cranked top leans 0.5 m over the traffic, 0.87 m up: {chi}");
            Bounds(PlanOf(MarkingTemplates.NoiseBarrierId, "top=arc;band=none", Straight(8f)), p => p.Solid == SolidColour.LightGray && p.Detail != DetailKind.Louvre, out _, out Vec3 ahi);
            float lean = 0.5f * (float)(Math.Sin(7.5 * Math.PI / 180) + Math.Sin(22.5 * Math.PI / 180) + Math.Sin(37.5 * Math.PI / 180));
            Program.Check(Math.Abs(ahi.X - lean) < 0.06f, $"the arched top leans {lean:0.00} m over the traffic: {ahi.X:0.00}");
            // The posts follow a cranked top: steel reaches the top's tip.
            Bounds(PlanOf(MarkingTemplates.NoiseBarrierId, "top=crank;band=none", Straight(8f)), p => p.Solid == SolidColour.Metal, out _, out Vec3 phi);
            Program.Check(phi.X > 0.5f, "the posts go on along the cranked top: " + phi.X);
            // On a slope the panels keep their height over the line.
            MarkingPlan slope = PlanOf(MarkingTemplates.NoiseBarrierId, "band=none", Straight(12f, 1f));
            List<Vec3> slopePath = RunPath.Decode(RunPath.Encode(Straight(12f, 1f), out _));
            float worst = 0f;
            foreach (MarkingPart part in slope.Parts)
            {
                if (part.Solid != SolidColour.LightGray || part.Detail == DetailKind.Louvre)
                    continue;
                foreach (Vec3 p in part.Mesh.Positions)
                {
                    float over = p.Y - HeightAt(slopePath, p.Z);
                    if (over > 3.835f)
                        worst = Math.Max(worst, Math.Abs(over - (0.83f + 3.01f)));
                }
            }

            Program.Check(worst < 1e-3f, "on an 8 % slope the top follows the line: " + worst);
        }

        private static void Covers()
        {
            // Full: two walls 12 m apart facing in, an arch over the road, PC roof over the ribs; symmetric about the line.
            MarkingPlan full = PlanOf(MarkingTemplates.NoiseCoverId, "span=12", Straight(8f));
            Bounds(full, p => p.Solid == SolidColour.Concrete, out Vec3 clo, out Vec3 chi);
            Program.Check(Math.Abs(clo.X + chi.X) < 1e-3f && chi.X > 6f && chi.X < 7f, $"two crash walls, the walls 12 m apart: {clo.X:0.00} .. {chi.X:0.00}");
            Bounds(full, p => p.Solid.HasValue && SolidGlass.Is(p.Solid.Value), out Vec3 glo, out Vec3 ghi);
            Program.Check(ghi.Y > 0.83f + 4f + 1.5f && ghi.Y < 0.83f + 4f + 4f && Math.Abs(glo.X + ghi.X) < 0.01f, $"a glass roof arched over the road: {glo} .. {ghi}");
            Bounds(full, p => p.Detail == DetailKind.Louvre, out Vec3 flo, out Vec3 fhi);
            Program.Check(flo.X < -5.9f && fhi.X > 5.9f && Math.Abs(flo.X + fhi.X) < 1e-3f, "the walls' louvres face in");
            foreach (MarkingPart part in full.Parts)
            {
                if (part.Detail != DetailKind.Louvre)
                    continue;
                bool inward = true;
                foreach (Vec3 p in part.Mesh.Positions)
                    inward &= Math.Abs(p.X) < 6f - 0.039f;
                Program.Check(inward, "each wall's louvres on its road side");
            }

            // Semi: one wall on the line, the roof reaching 4 m over the traffic (its right), the edge beam at its tip.
            MarkingPlan semi = PlanOf(MarkingTemplates.NoiseCoverId, "form=semi;arm=4", Straight(8f));
            Bounds(semi, p => true, out Vec3 slo, out Vec3 shi);
            Program.Check(shi.X > 4f && shi.X < 4.4f && slo.X > -0.7f, $"a semi-enclosure reaches 4 m over the traffic: {slo.X:0.00} .. {shi.X:0.00}");
            Bounds(PlanOf(MarkingTemplates.NoiseCoverId, "form=semi;arm=4;where=l", Straight(8f)), p => true, out Vec3 llo, out _);
            Program.Check(llo.X < -4f, "and to the left with the traffic there: " + llo.X);
        }

        private static void Walls()
        {
            // Cement: panels 600 mm high and 100 mm thick between HW150 posts every 4 m; 2.4 m of them on a footing of 0.3 m.
            MarkingPlan cement = PlanOf(MarkingTemplates.CementNoiseId, "", Straight(12f));
            Bounds(cement, p => p.Solid == SolidColour.Concrete && p.Detail == DetailKind.CementSlots, out Vec3 flo, out Vec3 fhi);
            Program.Check(fhi.Y > 0.32f + 2.4f - 0.04f && fhi.Y < 0.32f + 2.4f && flo.X > 0.05f, $"the slotted face toward the traffic up to the cap: {flo} .. {fhi}");
            Program.Check(PostsAt(cement, SolidColour.Metal, 0.32f, 0f).Count == 4, "posts every 4 m on 12 m");
            Bounds(cement, p => p.Solid == SolidColour.Concrete && p.Detail == DetailKind.Concrete, out Vec3 plo, out Vec3 phi);
            Program.Check(phi.Y > 0.32f + 2.4f - 1e-3f, "the panels to 2.4 m over the footing's plates");
            // Brick: a wall 240 mm thick about 1.2 m high, made up so that whole modules of metal panels over its coping (80 mm) and their plates reach 3 m: 1.4 m and three modules; pilasters 370 mm.
            MarkingPlan brick = PlanOf(MarkingTemplates.BrickNoiseId, "", Straight(10f));
            Bounds(brick, p => p.Solid == SolidColour.Brick, out Vec3 blo, out Vec3 bhi);
            Program.Near(bhi.Y, 1.4f, 1e-3f, "the brick wall made up to 1.4 m");
            Program.Near(bhi.X - blo.X, 0.37f, 1e-3f, "its pilasters one and a half bricks");
            Program.Check(brick.Parts.Exists(p => p.Solid == SolidColour.Brick && p.Detail == DetailKind.Brick), "bricks in courses");
            Bounds(brick, p => p.Solid == SolidColour.LightGray && p.Detail != DetailKind.Louvre, out Vec3 mlo, out Vec3 mhi);
            Program.Check(Math.Abs(mlo.Y - 1.5f) < 1e-3f && Math.Abs(mhi.Y - 3.01f) < 1e-3f, $"metal panels from the coping's plates to 3 m (and the cap strip): {mlo.Y:0.000} .. {mhi.Y:0.000}");
            Program.Check(PlanOf(MarkingTemplates.BrickNoiseId, "brick=grey", Straight(10f)).Parts.Exists(p => p.Solid == SolidColour.GreyBrick), "grey bricks");
            // Planted: a bed 1.5 m inside on the traffic's side of a 3 m wall, the game's shrubs down its middle every 1.6 m, none modelled; a plant up to three times its room across (PlantSystem.Choose)
            // stays clear of the panels (their face 41 mm off the line) and inside the bed's walls.
            MarkingPlan green = PlanOf(MarkingTemplates.GreenNoiseId, "", Straight(8f));
            Bounds(green, p => p.Solid == SolidColour.Soil, out Vec3 slo, out Vec3 shi);
            Program.Near(shi.X - slo.X, 1.5f, 1e-3f, "the bed 1.5 m inside");
            Program.Check(slo.X > 0.25f, "on the traffic's side, against the plinth: " + slo.X);
            Program.Check(green.BakedProps.Count == 5, "a plant every 1.6 m from 0.8 m in, none at the very ends: " + green.BakedProps.Count);
            foreach (PropAnchor prop in green.BakedProps)
            {
                float reach = prop.Room * 1.5f;
                Program.Check(prop.Kind == "planter" && prop.Local.X - reach >= 0.041f + 0.3f && prop.Local.X + reach <= shi.X + 1e-3f && Math.Abs(prop.Local.Y - 0.45f) < 1e-3f,
                    $"a plant on the bed's soil, as wide as it may be clear of the wall and in the bed: {prop.Local} room {prop.Room}");
            }

            Program.Check(green.Parts.TrueForAll(p => p.Solid != SolidColour.Green || p.Detail == DetailKind.Louvre || p.Detail == DetailKind.Painted), "no modelled foliage: the green is the panels'");
            MarkingPlan away = PlanOf(MarkingTemplates.GreenNoiseId, "bed=back", Straight(8f));
            Bounds(away, p => p.Solid == SolidColour.Soil, out _, out Vec3 ahi);
            Bounds(away, p => p.Detail == DetailKind.Louvre, out Vec3 alo, out _);
            Program.Check(ahi.X < -0.25f && alo.X > 0.039f, "or on the far side, the louvres still toward the traffic");
        }

        private static void Railings()
        {
            // The classical railing: main posts 120 mm every 3 m, small ones 50 mm every metre between; the handrail's top at the height; its lattice inside its frames.
            MarkingPlan fret = PlanOf(MarkingTemplates.FretRailingId, "height=110", Straight(9f));
            Bounds(fret, p => true, out Vec3 lo, out Vec3 hi);
            Program.Near(hi.Y, 1.1f + 0.1f + 0.07f + 0.03f, 1e-3f, "the main posts' heads over the handrail");
            int mains = 0, smalls = 0;
            foreach (float z in new[] { 0f, 3f, 6f, 9f })
                mains += fret.Parts[0].Mesh.Positions.Exists(p => Math.Abs(p.Z - z) < 0.07f && p.Y > 1.15f) ? 1 : 0;
            foreach (float z in new[] { 1f, 2f, 4f, 5f, 7f, 8f })
                smalls += fret.Parts[0].Mesh.Positions.Exists(p => Math.Abs(p.Z - z) < 0.026f && Math.Abs(p.Y - 0.012f) < 1e-4f && Math.Abs(p.X) <= 0.026f) ? 1 : 0;
            Program.Check(mains == 4 && smalls == 6, $"main posts every 3 m and small ones every metre: {mains} and {smalls}");
            Program.Check(fret.Parts[0].Mesh.Positions.Exists(p => Math.Abs(p.Y - 1.1f) < 1e-4f && Math.Abs(p.X) < 0.024f), "the handrail's top at 1.1 m");
            // The bars: no more than 100 mm clear (the 0.11 m of GB 51192-2016 8.2.3).
            MarkingPlan bars = PlanOf(MarkingTemplates.FretRailingId, "pattern=bars", Straight(3f));
            var zs = new SortedSet<float>();
            foreach (Vec3 p in bars.Parts[0].Mesh.Positions)
            {
                if (p.Y > 0.1f && p.Y < 1f && Math.Abs(Math.Abs(p.X) - 0.01f) < 1e-4f && p.Z > 0.1f && p.Z < 0.9f)
                    zs.Add((float)Math.Round(p.Z, 4));
            }

            var edges = new List<float>(zs);
            float widest = 0f;
            for (int i = 1; i < edges.Count; i++)
            {
                if (edges[i] - edges[i - 1] > 0.03f)
                    widest = Math.Max(widest, edges[i] - edges[i - 1]);
            }

            Program.Check(edges.Count >= 10 && widest <= 0.1f + 1e-3f, $"the bars' clear gaps no more than 100 mm: {widest:0.000}");
            // The ring railing: a ring in each panel, its figure drawn once by the piece its middle is in.
            MarkingPlan ring = PlanOf(MarkingTemplates.RingRailingId, "", Straight(9f));
            Program.Check(RingCount(ring, 0f) == 3, "three rings in 9 m: " + RingCount(ring, 0f));
            int split = RingCount(PlanOf(MarkingTemplates.RingRailingId, "", Straight(4.5f), 0f, 9f), 0f) + RingCount(PlanOf(MarkingTemplates.RingRailingId, "", Straight(4.5f), 4.5f, 9f), 4.5f);
            Program.Check(split == 3, "and three in its two pieces: " + split);
            Bounds(ring, p => p.Solid == SolidColour.White, out _, out Vec3 rhi);
            Program.Near(rhi.Y, 1.1f + 0.02f + 0.04f, 1e-3f, "the posts' caps over the 1.1 m rail");
        }

        /// <summary>
        /// The rings of a ring railing of 1.1 m (a piece of it phase along its run): its white vertices on the rings' sides (the ring 0.25 m across at 0.66 m up, its tube 32 mm, either side of a panel's
        /// middle, 1.5 m into each 3 m panel), counted by panel when both sides are there.
        /// </summary>
        private static int RingCount(MarkingPlan plan, float phase)
        {
            var sides = new HashSet<(int, int)>();
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Solid != SolidColour.White)
                    continue;
                foreach (Vec3 q in part.Mesh.Positions)
                {
                    Vec3 p = q + P(0f, phase);
                    int panel = (int)Math.Floor(p.Z / 3f);
                    float off = p.Z - (panel * 3f + 1.5f);
                    if (Math.Abs(Math.Abs(off) - 0.126f) < 0.018f && Math.Abs(p.Y - 0.66f) < 0.02f)
                        sides.Add((panel, Math.Sign(off)));
                }
            }

            int rings = 0;
            foreach ((int panel, int side) in sides)
                rings += side > 0 && sides.Contains((panel, -1)) ? 1 : 0;
            return rings;
        }

        private static void Water()
        {
            // Three-hole units: 1.5 m, 0.8 m high, 420 mm at the foot and 220 mm at the top, end to end every 1.5 m along the whole run, three holes through each.
            MarkingPlan holes = PlanOf(MarkingTemplates.WaterBarrierId, "style=holes", Straight(6f));
            Bounds(holes, p => p.Solid == SolidColour.Red || p.Solid == SolidColour.White && p.Detail == DetailKind.Plastic, out Vec3 lo, out Vec3 hi);
            Program.Near(hi.Y, 0.8f, 1e-3f, "0.8 m high");
            // 420 mm at the ground: its faces go on sloping to its foot under the line (FootSink).
            Program.Near(hi.X - lo.X, 0.42f + 2f * 0.125f * Barriers.FootSink, 1e-3f, "420 mm at the foot");
            Program.Check(lo.Y <= -Barriers.FootSink + 1e-4f, "its foot under the line");
            Program.Check(holes.Parts.Exists(p => p.Solid == SolidColour.White && p.Detail == DetailKind.Reflective), "reflective patches");
            // Through a hole's middle (0.4 m up, a quarter of each unit along) no face is crossed.
            int crossed = 0;
            foreach (MarkingPart part in holes.Parts)
            {
                MeshBuilder m = part.Mesh;
                for (int t = 0; t + 2 < m.Indices.Count; t += 3)
                {
                    Vec3 a = m.Positions[m.Indices[t]], b = m.Positions[m.Indices[t + 1]], c = m.Positions[m.Indices[t + 2]];
                    foreach (float unit in new[] { 0f, 1.5f, 3f, 4.5f })
                    {
                        foreach (float f in new[] { 0.25f, 0.5f, 0.75f })
                            crossed += InsideZY(a, b, c, unit + f * 1.47f, 0.4f) ? 1 : 0;
                    }
                }
            }

            Program.Check(crossed == 0, "the holes go through: " + crossed + " faces across them");
            // Fence-type: 1.0 m units, 1.8 m high, alternately the two colours (one colour here: yellow).
            MarkingPlan fence = PlanOf(MarkingTemplates.WaterBarrierId, "style=fence;scheme=y", Straight(5f));
            Bounds(fence, p => p.Solid == SolidColour.Yellow, out Vec3 flo, out Vec3 fhi);
            Program.Near(fhi.Y, 1.8f, 1e-3f, "1.8 m high");
            Program.Near(fhi.X - flo.X, 0.35f, 1e-3f, "350 mm at the foot");
            Program.Check(Caps(fence, SolidColour.Yellow) >= 10, "five units, each closed at both ends");
            // In two pieces: a unit starts every 1.5 m along the whole run, none cut.
            MarkingPlan first = PlanOf(MarkingTemplates.WaterBarrierId, "style=holes", Straight(4f), 0f, 9f);
            MarkingPlan second = PlanOf(MarkingTemplates.WaterBarrierId, "style=holes", Straight(5f), 4f, 9f);
            Bounds(second, p => p.Solid == SolidColour.Red || p.Solid == SolidColour.White && p.Detail == DetailKind.Plastic, out Vec3 slo, out _);
            Program.Near(slo.Z, 0.5f, 1e-3f, "the second piece's first unit starts 4.5 m along the run");
            Bounds(first, p => p.Solid == SolidColour.Red || p.Solid == SolidColour.White && p.Detail == DetailKind.Plastic, out _, out Vec3 ahi);
            Program.Check(ahi.Z > 4.4f, "the first piece draws the unit it starts whole, past its end: " + ahi.Z);
            // The plain units are as they were.
            Program.Check(PlanOf(MarkingTemplates.WaterBarrierId, "", Straight(6f)).Parts.TrueForAll(p => p.Detail == DetailKind.Plastic), "the plain style unchanged");
        }

        /// <summary>Whether (z, y) is inside the triangle seen along X (a face across the line).</summary>
        private static bool InsideZY(Vec3 a, Vec3 b, Vec3 c, float z, float y)
        {
            float d = (b.Z - a.Z) * (c.Y - a.Y) - (c.Z - a.Z) * (b.Y - a.Y);
            if (Math.Abs(d) < 1e-8f)
                return false;
            float w1 = ((z - a.Z) * (c.Y - a.Y) - (c.Z - a.Z) * (y - a.Y)) / d, w2 = ((b.Z - a.Z) * (y - a.Y) - (z - a.Z) * (b.Y - a.Y)) / d;
            return w1 > 1e-4f && w2 > 1e-4f && w1 + w2 < 1f - 1e-4f;
        }

        /// <summary>HSL (hue in degrees, saturation, lightness) of an RGB colour.</summary>
        private static (float H, float S, float L) Hsl(float r, float g, float b)
        {
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min, l = (max + min) * 0.5f;
            float s = d < 1e-6f ? 0f : d / (1f - Math.Abs(2f * l - 1f));
            float h = d < 1e-6f ? 0f : max == r ? ((g - b) / d % 6f) * 60f : max == g ? ((b - r) / d + 2f) * 60f : ((r - g) / d + 4f) * 60f;
            return (h < 0f ? h + 360f : h, s, l);
        }

        /// <summary>
        /// The glass tints (0.12.106; the owner: "绿色时候太亮而且颜色不够深", with the Photoshop reading green range, saturation +5, lightness -44 on a picture of it): the green is the old tint (0.48, 0.80, 0.62)
        /// with its lightness taken down to 56 % and its saturation up by 5 points, the same hue, and it shows more (0.46, not 0.34); the clear and the blue glass are as they were.
        /// </summary>
        private static void GlassTints()
        {
            SolidGlass.Tint(SolidColour.GlassGreen, out float r, out float g, out float b, out float a);
            (float h, float s, float l) = Hsl(r, g, b);
            (float oldH, float oldS, float oldL) = Hsl(0.48f, 0.80f, 0.62f);
            Program.Check(Math.Abs(h - oldH) < 2f, $"the green keeps its hue: {h:0.0} against {oldH:0.0}");
            Program.Check(Math.Abs(l - oldL * 0.56f) < 0.01f, $"its lightness is 56 % of what it was (the reading's -44): {l:0.000} against {oldL * 0.56f:0.000}");
            Program.Check(Math.Abs(s - (oldS + 0.05f)) < 0.01f, $"its saturation is 5 points more: {s:0.000} against {oldS + 0.05f:0.000}");
            Program.Check(g > r && g > b && r < 0.25f && g < 0.6f, "a deep green: green leads, red low");
            Program.Check(Math.Abs(a - 0.46f) < 1e-6f, "and it shows over what is behind at 0.46");
            Program.Check(l < oldL && g < 0.80f && r < 0.48f && b < 0.62f, "darker than the tint of before in every channel");
            SolidGlass.Tint(SolidColour.Glass, out float cr, out float cg, out float cb, out float ca);
            SolidGlass.Tint(SolidColour.GlassBlue, out float br, out float bg, out float bb, out float ba);
            Program.Check(cr == 0.80f && cg == 0.88f && cb == 0.86f && ca == 0.22f, "clear glass as it was");
            Program.Check(br == 0.42f && bg == 0.66f && bb == 0.86f && ba == 0.34f, "blue glass as it was");
            SolidGlass.Tint(SolidColour.White, out float wr, out float wg, out float wb, out float wa);
            Program.Check(wr == 1f && wg == 1f && wb == 1f && wa == 1f, "a colour that is not glass has no tint");
            // The picture of a barrier (Raster) has the same darker green: the same hue, lightness 56 % of its old light mint.
            byte[] pic = Raster.SolidRgb(SolidColour.GlassGreen);
            (float ph, float ps, float pl) = Hsl(pic[0] / 255f, pic[1] / 255f, pic[2] / 255f);
            (float oph, float ops, float opl) = Hsl(150f / 255f, 214f / 255f, 176f / 255f);
            Program.Check(Math.Abs(ph - oph) < 3f && Math.Abs(pl - opl * 0.56f) < 0.01f && Math.Abs(ps - (ops + 0.05f)) < 0.02f && pic[1] > pic[0] && pic[1] > pic[2], $"the picture's green is the old one through the same reading: {pic[0]}, {pic[1]}, {pic[2]}");
        }

        private static void Colours()
        {
            Program.Check((int)SolidColour.DarkBrown == 23 && (int)SolidColour.Glass == 24 && (int)SolidColour.GlassBlue == 25 && (int)SolidColour.GlassGreen == 26 && (int)SolidColour.Brick == 27
                && (int)SolidColour.GreyBrick == 28, "the new colours keep their numbers");
            int highest = 0;
            foreach (SolidColour c in (SolidColour[])Enum.GetValues(typeof(SolidColour)))
                highest = Math.Max(highest, (int)c);
            Program.Check(new SignParts().Solids.Length > highest, "a support's parts have room for every colour");
            foreach (SolidColour c in new[] { SolidColour.Glass, SolidColour.GlassBlue, SolidColour.GlassGreen })
            {
                Program.Check(SolidGlass.Is(c) && MarkingPlan.DetailOf(c) == DetailKind.None, c + ": glass, no baked detail");
                Program.Check(ChunkBuilder.SolidColourOf(ChunkBuilder.SolidMaterial(c)) == c, c + ": its material says its colour");
            }

            GlassTints();
            Program.Check(!SolidGlass.Is(SolidColour.White) && MarkingPlan.DetailOf(SolidColour.Brick) == DetailKind.Brick && MarkingPlan.DetailOf(SolidColour.GreyBrick) == DetailKind.Brick, "bricks have their courses");
            Program.Check(Raster.SolidRgb(SolidColour.Glass)[0] != Raster.SolidRgb(SolidColour.White)[0] && Raster.SolidRgb(SolidColour.Brick)[1] != 234, "the new colours have their own colours in the pictures");
            // A noise barrier's picture: what is behind its glass shows through.
            MarkingPlan plan = PlanOf(MarkingTemplates.NoiseBarrierId, "band=all", Straight(4f));
            byte[] picture = MarkingRaster.Render(plan, 128, out int w, out int h);
            Program.Check(w == 128 && h == 128 && picture.Length == w * h * 4, "its picture");
            foreach (DetailKind kind in new[] { DetailKind.Louvre, DetailKind.CementSlots, DetailKind.Brick })
            {
                DetailSurface s = DetailSurfaces.Make(kind);
                Program.Check(s != null && s.Alpha == null && !DetailSurfaces.HasHoles(kind), kind + ": a whole surface");
            }

            // A louvre's rows show in the relief: across a module the louvres' hoods stand 3.5 mm out, the joint of two modules a groove.
            DetailSurface louvre = DetailSurfaces.Make(DetailKind.Louvre);
            float top = float.MinValue, bottom = float.MaxValue;
            foreach (float r in louvre.Relief)
            {
                top = Math.Max(top, r);
                bottom = Math.Min(bottom, r);
            }

            Program.Check(top > 0.003f && bottom < -0.002f, $"the louvres' hoods and the modules' joint: {top * 1000f:0.0} and {bottom * 1000f:0.0} mm");
        }
    }
}
