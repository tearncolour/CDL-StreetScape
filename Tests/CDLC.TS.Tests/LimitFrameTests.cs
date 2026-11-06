using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The height-limit frame (0.12.78; the owner's photo of a tunnel entrance's yellow-black frame): the bands of its tubes, the catalogue entry, its layout by the span and the clearance, the built frame
    /// (closed, finite, the board hung in front of the beam, every tube in bands), the ground under its feet, and the numbers of vertices.
    /// </summary>
    internal static class LimitFrameTests
    {
        public static void RunAll()
        {
            Program.Run("limit frame: the bands of a tube alternate, end in yellow and cover it", Bands);
            Program.Run("limit frame: the catalogue entry, its hash, its family and that no planner chooses it", Catalogue);
            Program.Run("limit frame: the layout by the span and the clearance", Layout);
            Program.Run("limit frame: built closed and finite, the board in front of the beam", Built);
            Program.Run("limit frame: the bands are in the coloured solids, the far level is plain yellow", Coloured);
            Program.Run("limit frame: the columns are ladders, behind the beam, no room for small boards", Columns);
            Program.Run("limit frame: the banded tubes are closed but at the ends that sit on a plate or inside a chord or a tube", Watertight);
            Program.Run("limit frame: the beam is a capped, zigzag Warren truss with a vertical at each end", Beam);
            Program.Run("limit frame: the feet follow the ground down and never up", Ground);
            Program.Run("limit frame: boards on the back and on the column", Boards);
            Program.Run("limit frame: the vertices of a tube, of a frame", Numbers);
            Program.Run("gantry tiles: the carrier, the files, the colours of the limit frame's tile, the thickening", Tiles);
        }

        private static SupportDef Limit => SupportCatalog.ById(SupportCatalog.GantryLimit);

        private static SignInstance Board(string height = "4.5") => SignInstances.Of(SignCatalog.ById("gb.prohib.36"), "height=" + height);

        private static SignParts Build(float span, SupportDetail detail = SupportDetail.Full) =>
            SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = Board(), Lane = 1, OffsetX = span * 0.5f } }, Limit, span, detail);

        private static void Bands()
        {
            foreach (float length in new[] { 0.01f, 0.1f, 0.24f, 0.25f, 0.3f, 0.37f, 0.5f, 0.6f, 0.62f, 0.75f, 1f, 1.37f, 2f, 3.14f, 5.2f, 8.77f, 9.3f, 17.5f, 40f })
            {
                int count = LimitBands.CountFor(length);
                Program.Check(count >= 1 && count % 2 == 1, length + " m: an odd number of bands, " + count);
                List<(float From, float To, SolidColour Colour)> bands = LimitBands.Of(length);
                Program.Check(bands.Count == count, length + " m: as many bands as the count says");
                Program.Near(bands[0].From, 0f, 1e-6f, length + " m: starts at the start");
                Program.Near(bands[bands.Count - 1].To, length, 1e-5f, length + " m: ends at the end");
                Program.Check(bands[0].Colour == SolidColour.Yellow && bands[bands.Count - 1].Colour == SolidColour.Yellow, length + " m: yellow at both ends");
                float first = bands[0].To - bands[0].From;
                for (int i = 0; i < bands.Count; i++)
                {
                    Program.Check(i == 0 || Math.Abs(bands[i].From - bands[i - 1].To) < 1e-6f, length + " m: band " + i + " follows the one before without a gap or overlap");
                    Program.Check(bands[i].Colour == (i % 2 == 0 ? SolidColour.Yellow : SolidColour.Black), length + " m: band " + i + " alternates");
                    Program.Near(bands[i].To - bands[i].From, first, 1e-5f, length + " m: equal bands");
                }

                Program.Check(bands.Count(b => b.Colour == SolidColour.Yellow) == bands.Count(b => b.Colour == SolidColour.Black) + 1, length + " m: one more yellow than black");
                if (length >= LimitBands.Pitch)
                    Program.Check(first >= LimitBands.Pitch * 0.66f && first <= LimitBands.Pitch * 2f + 1e-4f, length + " m: a band of a quarter metre, at the most twice and at the least two thirds of it (an odd count): " + first);
            }

            Program.Check(LimitBands.CountFor(0.25f) == 1 && LimitBands.CountFor(0.6f) == 3 && LimitBands.CountFor(1f) == 3 && LimitBands.CountFor(2f) == 7 && LimitBands.CountFor(0.62f) == 3, "the nearest odd number to length over the pitch");
            Program.Check(LimitBands.CountFor(0f) == 1 && LimitBands.CountFor(-3f) == 1, "a tube of no length has one band");
        }

        private static void Catalogue()
        {
            SupportDef limit = Limit;
            Program.Check(limit != null && limit.Kind == SupportKind.Gantry && limit.HeightLimit && !limit.SpaceTruss && limit.Clearance == 4.5f && limit.ArmLength == 8f && !limit.Hidden, "the entry: a gantry kind with 4.5 m under it, 8 m by default");
            Program.Check(limit.Zh == "限高架（黄黑）" && limit.En == "Height-limit frame (yellow-black)", "its names");
            Program.Check(limit.Family == SupportFamilies.Gantry && SupportCatalog.Listed.Contains(limit), "in the gantry family, listed");
            Program.Check(SupportCatalog.ByHash(limit.Hash) == limit && limit.Hash == SignCatalog.HashOf("gantry.limit"), "its hash is the id's");
            Program.Check(limit.Hash != SupportCatalog.ById(SupportCatalog.Gantry).Hash && limit.Hash != SupportCatalog.ById(SupportCatalog.GantrySpace).Hash, "not the others'");
            Program.Check(SupportCatalog.ById(SupportCatalog.Gantry).HeightLimit == false && SupportCatalog.ById(SupportCatalog.GantrySpace).HeightLimit == false, "no other gantry is a limit frame");
            // Only the player picks it: no board's own support is the frame and the road's choice is not either.
            foreach (SignDef def in SignCatalog.All)
            {
                string id = SupportPlanner.Auto(def).Id;
                Program.Check(id != SupportCatalog.GantryLimit && id != SupportCatalog.GantrySpace, def.Id + ": the planner's own choice is not a new gantry: " + id);
            }

            foreach (int lanes in new[] { 0, 1, 2, 3, 5 })
            {
                foreach (float span in new[] { 0f, 6f, 14f, 40f })
                {
                    string id = SupportPlanner.AutoFor(Board().Def, new SupportContext { LanesSameDirection = lanes, ExitLeftOrWide = lanes == 1, Span = span }).Id;
                    Program.Check(id != SupportCatalog.GantryLimit, "the road never asks for the limit frame: " + lanes + " lanes, " + span + " m");
                }
            }
        }

        private static void Layout()
        {
            Program.Near(SupportLayouts.LimitDepth(SupportLayouts.MinSpan), 0.4f, 1e-6f, "0.4 m deep at the shortest span");
            Program.Near(SupportLayouts.LimitDepth(20f), 0.6f, 1e-6f, "0.6 m from 20 m");
            Program.Near(SupportLayouts.LimitDepth(13f), 0.5f, 1e-6f, "halfway at 13 m");
            Program.Near(SupportLayouts.LimitDepth(2f), 0.4f, 1e-6f, "not shallower");
            Program.Near(SupportLayouts.LimitDepth(40f), 0.6f, 1e-6f, "not deeper");
            Program.Near(SupportLayouts.LimitBay(0.4f), 0.56f, 1e-6f, "a bay of 1.4 depths");
            float last = 0f;
            for (float span = 0f; span <= 40f; span += 0.1f)
            {
                float depth = SupportLayouts.LimitDepth(span);
                Program.Check(depth >= last - 1e-6f, "the depth does not fall: " + span);
                last = depth;
            }

            SignDef board = Board().Def;
            Program.Near(SupportLayouts.Of(board, Limit).ArmLength, 8f, 1e-6f, "the default span is 8 m");
            Program.Near(SupportLayouts.Of(board, Limit, 3f).ArmLength, SupportLayouts.MinSpan, 1e-6f, "not under the shortest span");
            Program.Near(SupportLayouts.Of(board, Limit, 99f).ArmLength, SupportLayouts.MaxSpan, 1e-6f, "not over the longest");
            foreach (float span in new[] { 6f, 7.5f, 8f, 10f, 12f, 16f, 20f, 25f, 40f })
            {
                SupportLayout l = SupportLayouts.Of(board, Limit, span);
                Program.Near(l.ArmLength, span, 1e-5f, span + ": the span asked for");
                Program.Check(l.HeightLimit && !l.RoundLegs && l.BeamWidth == 0f && l.ChordYs.Length == 2, span + ": a frame of its own with one plane of chords");
                Program.Near(l.BeamDepth, SupportLayouts.LimitDepth(span), 1e-6f, span + ": the depth of the rule");
                Program.Near(l.BottomClearance, 4.5f, 1e-5f, span + ": the board's lower edge, the free height, is 4.5 m");
                Program.Near(l.HangY - l.ChordRadius, 4.5f, 1e-5f, span + ": the beam's underside is at the free height");
                Program.Near(l.ChordYs[1] - l.ChordYs[0], l.BeamDepth, 1e-6f, span + ": the chords are a depth apart");
                Program.Near(l.ChordRadius, SupportLayouts.LimitChordRadius, 0f, span + ": the chords' radius");
                Program.Near(l.PoleRadius, SupportLayouts.LimitColumnRadius, 0f, span + ": the columns' tubes");
                Program.Near(l.PoleTop, l.ChordYs[1] + l.ChordRadius + SupportLayouts.LimitPostAbove, 1e-5f, span + ": the columns end over the beam");
                Program.Near(l.BoardCentreX, span * 0.5f, 1e-5f, span + ": the board over the middle");
                Program.Check(l.PoleX.Length == 2 && l.PoleX[0] == 0f && Math.Abs(l.PoleX[1] - span) < 1e-5f, span + ": a column at each end");
                Program.Check(l.BackZ > l.ChordRadius && l.BackZ < l.ChordRadius + 0.1f, span + ": the board's back clears the chords by the clamp's room: " + l.BackZ);
                Program.Check(l.Base != null && l.Base.HalfX < 0.3f && l.Base.HalfZ < 0.2f, span + ": a plate under the two tubes, not a gantry's");
                Program.Check(Attach.PostRoom(l) == 0f, span + ": no room for small boards on a column");
                Program.Check(SupportKinds.RootPaintable(SupportKind.Gantry, l) == false, span + ": no root paint over the bands");
            }

            // The portal's and the space truss's layouts have nothing of it.
            SupportLayout portal = SupportLayouts.Of(board, SupportCatalog.ById(SupportCatalog.Gantry), 14f), space = SupportLayouts.Of(board, SupportCatalog.ById(SupportCatalog.GantrySpace), 14f);
            Program.Check(!portal.HeightLimit && !space.HeightLimit, "the other gantries are not limit frames");
            // A wide board (a board hung on it, as any gantry's) makes the span it needs.
            SignDef wide = SignCatalog.ById("guide.exit.advance");
            Program.Check(SupportLayouts.Of(wide, Limit, 6f).ArmLength >= wide.Width + 2f * Assembly.GantryClear - 1e-3f, "a wide board makes its span");
            Program.Check(SupportLayouts.Of(wide, Limit, 12f).BottomClearance == 4.5f, "and hangs from the free height");
        }

        private static string Problem(SignParts parts)
        {
            string problem = SpaceGantryTests.MeshProblem(parts.Metal) ?? SpaceGantryTests.MeshProblem(parts.Concrete);
            for (int c = 0; problem == null && c < parts.Solids.Length; c++)
            {
                if (parts.Solids[c] != null)
                    problem = SpaceGantryTests.MeshProblem(parts.Solids[c]);
            }

            return problem;
        }

        private static void Built()
        {
            foreach (float span in new[] { 6f, 8f, 10f, 14f, 20f, 40f })
            {
                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                {
                    string what = "limit frame " + span + " m " + detail;
                    SignParts parts = Build(span, detail);
                    string problem = Problem(parts);
                    Program.Check(problem == null, what + ": " + problem);
                    SupportLayout l = parts.Layout;
                    parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                    Program.Near(fMin.Y, 4.5f, 1e-3f, what + ": the board's lower edge is the free height");
                    Program.Check(fMin.Z >= l.ChordRadius + SupportLayouts.ClampRoom - 1e-3f, what + ": the board is in front of the chords and their clamps: " + fMin.Z);
                    // Nothing in front of the board's face across its outline, steel or banded tube; no banded tube behind it nearer than the chords' radius.
                    int front = 0;
                    var meshes = new List<MeshBuilder> { parts.Metal };
                    meshes.AddRange(parts.Solids.Where(s => s != null));
                    foreach (MeshBuilder mesh in meshes)
                    {
                        for (int i = 0; i < mesh.VertexCount; i++)
                        {
                            Vec3 p = mesh.Positions[i];
                            if (p.X > fMin.X && p.X < fMax.X && p.Y > fMin.Y && p.Y < fMax.Y && p.Z > fMax.Z + 1e-4f)
                                front++;
                        }
                    }

                    Program.Check(front == 0, what + ": " + front + " vertices in front of the board");
                    // No banded tube reaches the board's back plane: the tubes are where the layout puts the chords.
                    foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
                    {
                        MeshBuilder mesh = parts.Solids[(int)colour];
                        if (mesh == null)
                            continue;
                        float nearest = float.MinValue;
                        for (int i = 0; i < mesh.VertexCount; i++)
                        {
                            Vec3 p = mesh.Positions[i];
                            if (p.X > fMin.X && p.X < fMax.X && p.Y > fMin.Y && p.Y < fMax.Y)
                                nearest = Math.Max(nearest, p.Z);
                        }

                        Program.Check(nearest <= l.ChordRadius + 1e-4f, what + ": the " + colour + " tubes behind the board stay inside the chords' radius: " + nearest + " against " + l.BackZ);
                    }

                    Program.Check(parts.Min.Y > -0.5f && parts.Min.Y < 0f, what + ": the pedestals are under the ground: " + parts.Min.Y);
                    // The round board is taller than the beam and stands over it (its back is steel); of the tubes the columns are the highest.
                    float highest = parts.Solids.Where(m => m != null).Max(m => m.Positions.Max(p => p.Y));
                    Program.Check(Math.Abs(highest - l.PoleTop) < 1e-3f, what + ": the columns' tops are the highest of the tubes: " + highest + " against " + l.PoleTop);
                    float chordEnd = span + (SupportLayouts.LimitColumnGap * 0.5f + SupportLayouts.LimitColumnRadius + SupportLayouts.LimitOverhang);
                    Program.Check(parts.Max.X >= chordEnd - 1e-3f && parts.Min.X <= -(chordEnd - span) + 1e-3f, what + ": the beam reaches past the outer tubes by " + SupportLayouts.LimitOverhang + " m");
                    Program.Check(parts.Feet.Count == 2, what + ": a foot at each end");
                }
            }
        }

        private static void Coloured()
        {
            SignParts full = Build(8f, SupportDetail.Full), far = Build(8f, SupportDetail.Reduced);
            MeshBuilder yellow = full.Solids[(int)SolidColour.Yellow], black = full.Solids[(int)SolidColour.Black];
            Program.Check(yellow != null && black != null && yellow.VertexCount > 0 && black.VertexCount > 0, "both colours are there at the full level");
            Program.Check(far.Solids[(int)SolidColour.Black] == null || far.Solids[(int)SolidColour.Black].VertexCount == 0, "none of the black at the far level");
            Program.Check(far.Solids[(int)SolidColour.Yellow] != null && far.Solids[(int)SolidColour.Yellow].VertexCount > 0, "the far level is plain yellow");
            Program.Check(far.VertexCount * 3 < full.VertexCount, "and light: " + far.VertexCount + " against " + full.VertexCount);
            Program.Check(full.Metal.VertexCount > 0 && far.Metal.VertexCount > 0, "the plates and the clamps are steel");
            // Every banded tube is solids only: none of the beam's chords is in the steel (a chord of the metal would show grey through the paint).
            for (int i = 0; i < full.Metal.VertexCount; i++)
            {
                Vec3 p = full.Metal.Positions[i];
                float height = full.Layout.ChordYs[0];
                bool onChord = Math.Abs(p.Y - height) < 0.02f && Math.Abs(p.Z) < 0.02f && p.X > 0.5f && p.X < 7.5f && Math.Abs(p.X - 4f) > 0.6f;
                if (onChord)
                {
                    Program.Check(false, "steel on the lower chord at x = " + p.X);
                    break;
                }
            }

            // The bands of the lower chord: along it the colour alternates and the changes are where LimitBands puts them.
            SupportLayout l = full.Layout;
            float x0 = -(SupportLayouts.LimitColumnGap * 0.5f + SupportLayouts.LimitColumnRadius + SupportLayouts.LimitOverhang), length = 8f - 2f * x0;
            var seam = new List<float>();
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder mesh = full.Solids[(int)colour];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    Vec3 p = mesh.Positions[i];
                    if (Math.Abs(p.Z - SupportLayouts.LimitChordRadius) < 1e-4f && Math.Abs(p.Y - l.ChordYs[0]) < 1e-4f)
                        seam.Add((float)Math.Round(p.X, 3));
                }
            }

            List<(float From, float To, SolidColour Colour)> bands = LimitBands.Of(length);
            for (int i = 1; i < bands.Count; i++)
                Program.Check(seam.Contains((float)Math.Round(x0 + bands[i].From, 3)), "a seam of the chord at band " + i + ", x = " + (x0 + bands[i].From));
        }

        private static void Columns()
        {
            SignParts parts = Build(10f);
            SupportLayout l = parts.Layout;
            float zc = SupportLayouts.LimitColumnZ, half = SupportLayouts.LimitColumnGap * 0.5f, r = l.PoleRadius;
            Program.Check(zc < -(l.ChordRadius) && zc + r > -l.ChordRadius - 1e-4f && zc + r < l.ChordRadius, "a column's tubes are behind the chords and touch them, not through them: z " + zc);
            foreach (float px in l.PoleX)
            {
                foreach (float side in new[] { -half, half })
                {
                    // Each tube is a column of banded tubes standing on the plate: coloured vertices on its axis at the plate's top and at the columns' top.
                    float low = float.MaxValue, high = float.MinValue;
                    foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
                    {
                        MeshBuilder mesh = parts.Solids[(int)colour];
                        for (int i = 0; i < mesh.VertexCount; i++)
                        {
                            Vec3 p = mesh.Positions[i];
                            if (Math.Abs(p.X - (px + side)) <= r + 1e-4f && Math.Abs(p.Z - zc) <= r + 1e-4f && p.Y < l.ChordYs[0] - 0.3f)
                                low = Math.Min(low, p.Y);
                            if (Math.Abs(p.X - (px + side)) <= r + 1e-4f && Math.Abs(p.Z - zc) <= r + 1e-4f)
                                high = Math.Max(high, p.Y);
                        }
                    }

                    Program.Check(low > 0f && low < 0.1f, "the tube at " + (px + side) + " stands on its plate: " + low);
                    Program.Near(high, l.PoleTop, 1e-4f, "and rises to the columns' top at " + (px + side));
                }

                // The rungs between a column's tubes: coloured tubes across, thinner than the tubes, spaced by LimitRungPitch.
                // A rung's end is a ring of the rung's radius round a tube's axis: vertices a rung's radius to either side of it (a tube has none there).
                foreach (float side in new[] { -half, half })
                {
                    var rungs = new SortedSet<float>();
                    foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
                    {
                        MeshBuilder mesh = parts.Solids[(int)colour];
                        for (int i = 0; i < mesh.VertexCount; i++)
                        {
                            Vec3 p = mesh.Positions[i];
                            if (Math.Abs(p.X - (px + side)) < 1e-3f && Math.Abs(Math.Abs(p.Z - zc) - SupportLayouts.LimitRungRadius) < 1e-3f)
                                rungs.Add((float)Math.Round(p.Y, 1));
                        }
                    }

                    Program.Check(rungs.Count >= 6, "the rungs of the column at " + px + " reach the tube at " + (px + side) + ": " + rungs.Count + " heights");
                }
            }

            // The columns are the board's neighbours only at the ends: the board hangs GantryClear from them.
            parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
            Program.Check(fMin.X > half + r && fMax.X < 10f - half - r, "the board hangs between the columns");
            Program.Check(parts.Picks.Count(p => p.Name == "post") == 2 && parts.Picks.Count(p => p.Name == "foot") == 2 && parts.Picks.Count(p => p.Name == "arm") == 1, "the columns, the feet and the beam can be pointed at");
            PickShape arm = parts.Picks.First(p => p.Name == "arm");
            Program.Check(arm.Min.Y <= l.ChordYs[0] - l.ChordRadius + 1e-4f && arm.Max.Y >= l.ChordYs[1] + l.ChordRadius - 1e-4f, "the beam's pick is round both chords");
        }

        /// <summary>The vertices of the coloured solids that satisfy a test, counted.</summary>
        private static int Count(SignParts parts, Func<Vec3, Vec3, bool> where)
        {
            int count = 0;
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder mesh = parts.Solids[(int)colour];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    if (where(mesh.Positions[i], mesh.Normals[i]))
                        count++;
                }
            }

            return count;
        }

        /// <summary>The midpoints of the edges that belong to one triangle of the coloured solids together (welded by position across the colours): where the tubes are open.</summary>
        private static List<Vec3> OpenEdges(SignParts parts)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Weld(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                    ids[key] = id = ids.Count;
                return id;
            }

            var edges = new Dictionary<(int, int), (int Count, Vec3 Middle)>();
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder mesh = parts.Solids[(int)colour];
                if (mesh == null)
                    continue;
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    int[] corner = { mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2] };
                    for (int e = 0; e < 3; e++)
                    {
                        int a = Weld(mesh.Positions[corner[e]]), b = Weld(mesh.Positions[corner[(e + 1) % 3]]);
                        if (a == b)
                            continue;
                        var edge = a < b ? (a, b) : (b, a);
                        Vec3 middle = (mesh.Positions[corner[e]] + mesh.Positions[corner[(e + 1) % 3]]) * 0.5f;
                        edges[edge] = edges.TryGetValue(edge, out var seen) ? (seen.Count + 1, seen.Middle) : (1, middle);
                    }
                }
            }

            return edges.Values.Where(v => v.Count == 1).Select(v => v.Middle).ToList();
        }

        private static void Watertight()
        {
            foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
            {
                foreach (float span in new[] { 6f, 10f, 24f })
                {
                    SignParts parts = Build(span, detail);
                    SupportLayout l = parts.Layout;
                    float half = SupportLayouts.LimitColumnGap * 0.5f, r = l.PoleRadius, zc = SupportLayouts.LimitColumnZ, plateTop = l.Base.Thick + l.Base.LowerThick + 0.01f;
                    float chordApothem = l.ChordRadius * (float)Math.Cos(Math.PI / 8) - 1e-4f;
                    var open = OpenEdges(parts);
                    int stray = 0;
                    string first = null;
                    foreach (Vec3 m in open)
                    {
                        // A tube may end on a plate, inside a chord, or inside a column's tube; anywhere else a hole shows.
                        bool onPlate = Math.Abs(m.Y - plateTop) < 1e-3f;
                        // Well inside, not on the surface: a tube's own open end would pass a test of "within its radius".
                        bool inChord = l.ChordYs.Any(y => Math.Sqrt((m.Y - y) * (m.Y - y) + m.Z * m.Z) < chordApothem * 0.8f);
                        bool inColumn = l.PoleX.Any(px => new[] { -half, half }.Any(side => Math.Sqrt((m.X - px - side) * (m.X - px - side) + (m.Z - zc) * (m.Z - zc)) < r * 0.66f));
                        if (!onPlate && !inChord && !inColumn)
                        {
                            stray++;
                            first = first ?? $"({m.X:0.###}, {m.Y:0.###}, {m.Z:0.###})";
                        }
                    }

                    Program.Check(stray == 0, "limit frame " + span + " m " + detail + ": " + stray + " open edges of the tubes show, the first at " + first + " (of " + open.Count + " open)");
                    Program.Check(open.Count > 0, "the check sees the tube ends it allows: " + open.Count);
                }
            }
        }

        private static void Beam()
        {
            float span = 10f, half = SupportLayouts.LimitColumnGap * 0.5f, r = SupportLayouts.LimitColumnRadius;
            SignParts parts = Build(span);
            SupportLayout l = parts.Layout;
            float x0 = -(half + r + SupportLayouts.LimitOverhang), x1 = span + half + r + SupportLayouts.LimitOverhang, yLow = l.ChordYs[0], yTop = l.ChordYs[1];
            // A chord is capped at each end and has no cap along it (a cap is a disc of nine vertices facing along the tube).
            foreach (float y in l.ChordYs)
            {
                int left = Count(parts, (p, n) => Math.Abs(p.X - x0) < 1e-4f && Math.Abs(p.Y - y) < 0.031f && Math.Abs(p.Z) < 0.031f && n.X < -0.99f);
                int right = Count(parts, (p, n) => Math.Abs(p.X - x1) < 1e-4f && Math.Abs(p.Y - y) < 0.031f && Math.Abs(p.Z) < 0.031f && n.X > 0.99f);
                int along = Count(parts, (p, n) => p.X > x0 + 1e-3f && p.X < x1 - 1e-3f && Math.Abs(p.Y - y) < 0.031f && Math.Abs(p.Z) < 0.031f && Math.Abs(n.X) > 0.99f);
                Program.Check(left == 9 && right == 9, "the chord at " + y + " is capped at both ends: " + left + " and " + right);
                Program.Check(along == 0, "and nowhere along it (a disc inside a tube is steel nobody sees): " + along);
            }

            // A column's tube is capped at its top only: nine vertices facing up at the top, none lower.
            foreach (float px in l.PoleX)
            {
                foreach (float side in new[] { -half, half })
                {
                    float zc = SupportLayouts.LimitColumnZ;
                    int top = Count(parts, (p, n) => Math.Abs(p.X - (px + side)) <= r + 1e-4f && Math.Abs(p.Z - zc) <= r + 1e-4f && Math.Abs(p.Y - l.PoleTop) < 1e-4f && n.Y > 0.99f);
                    int inside = Count(parts, (p, n) => Math.Abs(p.X - (px + side)) <= r + 1e-4f && Math.Abs(p.Z - zc) <= r + 1e-4f && p.Y < l.PoleTop - 1e-3f && Math.Abs(n.Y) > 0.99f);
                    Program.Check(top == 9, "the tube at " + (px + side) + " is capped at its top: " + top);
                    Program.Check(inside == 0, "and open between its bands (they are one tube): " + inside);
                }
            }

            // The beam is a Warren truss: a vertical at each end of the zigzag, and at every node between two diagonals both of them meet at one chord.
            float n0 = x0 + 0.05f, last = x1 - 0.05f;
            foreach (float x in new[] { n0, last })
            {
                int low = Count(parts, (p, n) => Math.Abs(p.X - x) < 1e-3f && Math.Abs(p.Y - yLow) < 1e-3f && Math.Abs(Math.Abs(p.Z) - SupportLayouts.LimitWebRadius) < 1e-3f);
                int high = Count(parts, (p, n) => Math.Abs(p.X - x) < 1e-3f && Math.Abs(p.Y - yTop) < 1e-3f && Math.Abs(Math.Abs(p.Z) - SupportLayouts.LimitWebRadius) < 1e-3f);
                Program.Check(low >= 2 && high >= 2, "a vertical between the chords at the end of the zigzag, x = " + x + ": " + low + " at the lower chord, " + high + " at the upper");
            }

            float bay = SupportLayouts.LimitBay(l.BeamDepth);
            int bays = Math.Max(2, (int)Math.Round((last - n0) / bay));
            if (bays % 2 == 1)
                bays++;
            float step = (last - n0) / bays;
            Program.Check(bays >= 14 && bays <= 22, "about sixteen bays over 10 m, the photo's triangles: " + bays);
            for (int i = 1; i < bays; i++)
            {
                float x = n0 + step * i;
                int low = Count(parts, (p, n) => Math.Abs(p.X - x) < 1e-3f && Math.Abs(p.Y - yLow) < 1e-3f && Math.Abs(Math.Abs(p.Z) - SupportLayouts.LimitWebRadius) < 1e-3f);
                int high = Count(parts, (p, n) => Math.Abs(p.X - x) < 1e-3f && Math.Abs(p.Y - yTop) < 1e-3f && Math.Abs(Math.Abs(p.Z) - SupportLayouts.LimitWebRadius) < 1e-3f);
                // Node i joins bay i - 1, which rises (to the upper chord) when i - 1 is even, and bay i, which rises when i is even: odd i meets at the upper chord, even i at the lower.
                bool atTop = i % 2 == 1;
                Program.Check(atTop ? high >= 4 && low == 0 : low >= 4 && high == 0, "node " + i + " of a Warren zigzag meets at the " + (atTop ? "upper" : "lower") + " chord: " + high + " there, " + low + " at the other");
            }
        }

        private static void Ground()
        {
            SignParts parts = Build(8f);
            parts.FootTags(out int[] metal, out int[] concrete, out int[][] solids);
            int[] yellowTags = solids[(int)SolidColour.Yellow];
            Program.Check(yellowTags != null && yellowTags.Length == parts.Solids[(int)SolidColour.Yellow].VertexCount, "the yellow tubes are tagged");
            int[] blackTags = solids[(int)SolidColour.Black];
            Program.Check(blackTags == null || blackTags.All(t => t < 0), "no black band moves with a foot");
            foreach (PostFoot foot in parts.Feet)
                Program.Check(foot.MaxRaise == 0f, "a foot goes down with the ground, never up: " + foot.MaxRaise);

            var top = new[] { -1f, -1f };
            var bottom = new[] { -1.5f, -1.5f };
            var moved = new MeshBuilder();
            MeshBuilder yellow = parts.Solids[(int)SolidColour.Yellow];
            PostGround.Append(moved, yellow, yellowTags, top, bottom, Vec3.Zero, 0f, parts.Feet);
            Program.Check(moved.VertexCount == yellow.VertexCount, "the same vertices");
            float lowBefore = float.MaxValue, lowAfter = float.MaxValue;
            int movedCount = 0;
            float plate = parts.Layout.Base.Thick + parts.Layout.Base.LowerThick + 0.01f;
            for (int i = 0; i < yellow.VertexCount; i++)
            {
                float dy = moved.Positions[i].Y - yellow.Positions[i].Y;
                lowBefore = Math.Min(lowBefore, yellow.Positions[i].Y);
                lowAfter = Math.Min(lowAfter, moved.Positions[i].Y);
                if (Math.Abs(dy) > 1e-5f)
                {
                    movedCount++;
                    Program.Near(dy, -1f, 1e-5f, "a vertex that moves goes with its foot");
                    Program.Near(yellow.Positions[i].Y, plate, 1e-4f, "and is on the lowest ring of a tube, the plate's top: the tube stretches, it does not break");
                }
                else
                {
                    Program.Check(moved.Positions[i].X == yellow.Positions[i].X && moved.Positions[i].Z == yellow.Positions[i].Z, "x and z stay");
                }
            }

            Program.Check(movedCount > 0, "the lowest ring of each tube moves: " + movedCount);
            Program.Near(lowAfter, lowBefore - 1f, 1e-4f, "the tubes stand a metre lower");
            // The top of every tube stays where it is: the column is longer on lower ground, its bands above the foot are the same.
            float highBefore = yellow.Positions.Max(p => p.Y), highAfter = moved.Positions.Max(p => p.Y);
            Program.Near(highAfter, highBefore, 1e-5f, "the columns' tops stay");
            // The reduced level moves too: its one tube is tagged.
            SignParts far = Build(8f, SupportDetail.Reduced);
            far.FootTags(out _, out _, out int[][] farSolids);
            Program.Check(farSolids[(int)SolidColour.Yellow] != null && farSolids[(int)SolidColour.Yellow].Any(t => t >= 0), "the far level's tubes are tagged as well");
        }

        private static void Boards()
        {
            // A board on the back of the beam, and a small board on the column (refused by the tool, but an item can carry it from another support): neither breaks the build.
            SignParts two = SignBuilder.BuildAssembly(new List<AssemblyBoard>
            {
                new AssemblyBoard { Instance = Board(), Lane = 1, OffsetX = 3f, Slot = 0, Member = 0 },
                new AssemblyBoard { Instance = Board("4"), Lane = 2, OffsetX = 6f, Slot = 1, Member = 1, Back = true }
            }, Limit, 10f);
            Program.Check(two.Faces.Count == 2 && Problem(two) == null, "one board in front, one on the back: " + Problem(two));
            two.Faces[1].Mesh.Bounds(out Vec3 bMin, out Vec3 bMax);
            Program.Check(bMax.Z < 0f, "the one on the back faces the other way: " + bMax.Z);
            var post = new List<AssemblyBoard>
            {
                new AssemblyBoard { Instance = Board(), Lane = 1, OffsetX = 5f, Slot = 0, Member = 0 },
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), Lane = Assembly.OnPost, Slot = 1, Member = 1 }
            };
            SignParts withPost = SignBuilder.BuildAssembly(post, Limit, 10f);
            Program.Check(withPost.Faces.Count == 2 && Problem(withPost) == null, "a board on a column does not break it: " + Problem(withPost));
            // The tool says there is no room: the attach rules refuse a board for the column.
            var request = new AttachRequest { Existing = new List<AssemblyBoard>(), Support = Limit, Reach = 10f, Board = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), OnPost = true };
            AttachResult onPost = Attach.Evaluate(request);
            Program.Check(!onPost.Ok && onPost.Reason == AttachReason.NoHeight, "a small board on a column: " + onPost.Reason);
            // A tall board hangs from the free height and covers the beam.
            SignParts tall = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("guide.exit.advance"), null), Lane = 1, OffsetX = 6f } }, Limit, 12f);
            tall.Face.Bounds(out Vec3 tMin, out Vec3 tMax);
            Program.Near(tMin.Y, 4.5f, 1e-3f, "an exit board hangs from 4.5 m too");
            Program.Check(Problem(tall) == null && tMax.Y > tall.Layout.ChordYs[1], "and stands over the beam: " + Problem(tall));
        }

        private static byte[] Flat(int w, int h, byte r, byte g, byte b)
        {
            var rgba = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                rgba[i * 4] = r;
                rgba[i * 4 + 1] = g;
                rgba[i * 4 + 2] = b;
                rgba[i * 4 + 3] = 255;
            }

            return rgba;
        }

        private static void Tiles()
        {
            SupportDef portal = SupportCatalog.ById(SupportCatalog.Gantry), space = SupportCatalog.ById(SupportCatalog.GantrySpace);
            SignInstance carrier = Thumbnails.CarrierFor(Limit);
            Program.Check(carrier.Def.Id == "gb.prohib.36" && carrier.Spec.Contains("height=4.5"), "the limit frame's tile carries the height-limit board at its own clearance: " + carrier.Def.Id + " " + carrier.Spec);
            Program.Check(Thumbnails.CarrierFor(portal).Def.Id == "gb.prohib.01" && Thumbnails.CarrierFor(space).Def.Id == "gb.prohib.01", "the other gantries' tiles carry what they always did");
            string a = Thumbnails.FileName("p", portal.Id), b = Thumbnails.FileName("p", space.Id), c = Thumbnails.FileName("p", Limit.Id);
            Program.Check(a != b && b != c && a != c && b.Contains("gantry.space") && c.Contains("gantry.limit"), "a file of its own for each: " + b + ", " + c);
            // Ids written before this version name the same files: no tile is made again for them.
            Program.Check(Thumbnails.FileName("p", "gantry.portal") == a && Thumbnails.Revision == 5, "the portal gantry's tile is the file it was (the art's revision is as it was)");

            // The window of a whole-frame tile holds every vertex of the frame, at the default span and at others.
            Program.Check(Thumbnails.ShownWhole(space) && Thumbnails.ShownWhole(Limit) && !Thumbnails.ShownWhole(portal) && !Thumbnails.ShownWhole(SupportCatalog.ById(SupportCatalog.Cantilever)), "the space truss and the limit frame are shown whole, the portal gantry and the cantilever as they were");
            foreach (SupportDef support in new[] { space, Limit })
            {
                foreach (float reach in new[] { 0f, 6f, 8f, 14f, 24f, 40f })
                {
                    SignParts shown = SignBuilder.Build(Thumbnails.CarrierFor(support), support, reach);
                    SupportLayout l = shown.Layout;
                    Vec3 focus = Thumbnails.WholeFocus(l);
                    float half = Thumbnails.WholeHalf(l);
                    shown.Metal.Bounds(out Vec3 lo, out Vec3 hi);
                    Vec3 min = lo, max = hi;
                    foreach (MeshBuilder mesh in shown.Solids.Where(m => m != null && m.VertexCount > 0).Concat(new[] { shown.Concrete }))
                    {
                        mesh.Bounds(out Vec3 lowest, out Vec3 highest);
                        min = new Vec3(Math.Min(min.X, lowest.X), Math.Min(min.Y, lowest.Y), Math.Min(min.Z, lowest.Z));
                        max = new Vec3(Math.Max(max.X, highest.X), Math.Max(max.Y, highest.Y), Math.Max(max.Z, highest.Z));
                    }

                    Program.Near(focus.X, l.ArmLength * 0.5f, 1e-5f, support.Id + " " + reach + ": the window is centred on the middle of the span");
                    Program.Check(min.X >= focus.X - half && max.X <= focus.X + half && min.Y >= focus.Y - half && max.Y <= focus.Y + half, support.Id + " " + reach + ": the whole frame is in the window: x " + min.X + " to " + max.X + ", y " + min.Y + " to " + max.Y + " in " + (focus.X - half) + " to " + (focus.X + half));
                    Program.Check(half <= l.ArmLength * 0.5f + l.PoleTop * 0.5f + 1f, support.Id + " " + reach + ": and it is not much larger than the frame: " + half);
                }
            }

            // The tile: whole frame in a transparent square, the yellow and the black of the tubes to be seen, not drowned in the outline.
            byte[] picture = Flat(16, 16, 200, 40, 40);
            byte[] tile = Thumbnails.SupportRgba(Limit, picture, 16, 16), plain = Thumbnails.SupportRgba(space, picture, 16, 16);
            int s = Thumbnails.Size, yellow = 0, black = 0, solid = 0;
            Program.Check(tile.Length == s * s * 4 && plain.Length == s * s * 4, "square tiles");
            for (int i = 0; i < s * s; i++)
            {
                byte r = tile[i * 4], g = tile[i * 4 + 1], bl = tile[i * 4 + 2], alpha = tile[i * 4 + 3];
                if (alpha < 200)
                    continue;
                solid++;
                // Shaded yellow is a dark ochre (the lit side is 232, 182, 30); the black of a band has no tint, the outline's (28, 34, 42) has one.
                if (r > 90 && g > 70 && bl < 60 && r >= g && g * 10 > r * 6 && r > bl + 50)
                    yellow++;
                if (r < 50 && Math.Abs(r - g) < 6 && Math.Abs(g - bl) < 6)
                    black++;
            }

            Program.Check(yellow >= 40, "the yellow of the bands shows in the tile: " + yellow + " pixels");
            Program.Check(black >= 40, "and the black: " + black + " pixels");
            Program.Check(solid > 400 && solid < s * s / 3, "the frame is drawn, not the whole tile: " + solid);
            // Corners clear, the frame inside the tile with its margin: nothing touches the edge.
            int touching = 0;
            for (int i = 0; i < s; i++)
            {
                touching += tile[(i * s) * 4 + 3] > 0 ? 1 : 0;
                touching += tile[(i * s + s - 1) * 4 + 3] > 0 ? 1 : 0;
                touching += tile[i * 4 + 3] > 0 ? 1 : 0;
                touching += tile[((s - 1) * s + i) * 4 + 3] > 0 ? 1 : 0;
            }

            Program.Check(touching == 0, "the whole frame is in the tile: " + touching + " edge pixels drawn");
            byte[] tilePortal = Thumbnails.SupportRgba(portal, picture, 16, 16);
            Program.Check(!tile.SequenceEqual(plain) && !plain.SequenceEqual(tilePortal) && !tile.SequenceEqual(tilePortal), "three different tiles");
            int spaceTouch = 0;
            for (int i = 0; i < s; i++)
            {
                spaceTouch += plain[(i * s) * 4 + 3] > 0 ? 1 : 0;
                spaceTouch += plain[(i * s + s - 1) * 4 + 3] > 0 ? 1 : 0;
                spaceTouch += plain[i * 4 + 3] > 0 ? 1 : 0;
                spaceTouch += plain[((s - 1) * s + i) * 4 + 3] > 0 ? 1 : 0;
            }

            Program.Check(spaceTouch == 0, "the space truss is shown whole too: " + spaceTouch + " edge pixels drawn");

            // The thickening: every tube's radius grows by the amount, its place and its bands do not change; the steel is not touched.
            SignParts parts = Build(8f), grown = Build(8f);
            Thumbnails.Thicken(grown, 0.05f);
            Program.Check(parts.Metal.VertexCount == grown.Metal.VertexCount && parts.Metal.Positions.SequenceEqual(grown.Metal.Positions), "the steel is as it was");
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder was = parts.Solids[(int)colour], now = grown.Solids[(int)colour];
                Program.Check(was.VertexCount == now.VertexCount && was.Indices.SequenceEqual(now.Indices), colour + ": the same triangles");
                float worst = 0f;
                for (int i = 0; i < was.VertexCount; i++)
                    worst = Math.Max(worst, Math.Abs((now.Positions[i] - was.Positions[i]).Length - 0.05f));
                Program.Check(worst < 1e-5f, colour + ": every vertex moved by the amount along its normal: " + worst);
            }

            // The tube of a column: its radius, measured across, is the amount larger.
            float zc = SupportLayouts.LimitColumnZ, x = 0f - SupportLayouts.LimitColumnGap * 0.5f;
            float was0 = float.MaxValue, now0 = float.MaxValue;
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder was = parts.Solids[(int)colour], now = grown.Solids[(int)colour];
                for (int i = 0; i < was.VertexCount; i++)
                {
                    if (Math.Abs(was.Positions[i].Y - 3f) < 0.5f && Math.Abs(was.Positions[i].X - x) < 0.0451f && Math.Abs(was.Positions[i].Z - zc) < 0.0451f && was.Normals[i].X < -0.99f)
                    {
                        was0 = Math.Min(was0, was.Positions[i].X);
                        now0 = Math.Min(now0, now.Positions[i].X);
                    }
                }
            }

            Program.Near(was0 - now0, 0.05f, 1e-4f, "a column's tube is the amount thicker at its side");
            // The support's own mesh in the game is not thickened: the tile is the only caller.
            Program.Check(parts.Solids[(int)SolidColour.Yellow].Positions.SequenceEqual(Build(8f).Solids[(int)SolidColour.Yellow].Positions), "building again gives the tubes as they were");
        }

        private static void Numbers()
        {
            // The vertices of one tube as the frame makes it, by difference: a longer span adds beam and nothing else (the columns, the board and the plates are the same).
            SignParts a = Build(10f, SupportDetail.Full), b = Build(20f, SupportDetail.Full), fa = Build(10f, SupportDetail.Reduced), fb = Build(20f, SupportDetail.Reduced);
            float perMetreFull = (b.VertexCount - a.VertexCount) / 10f, perMetreFar = (fb.VertexCount - fa.VertexCount) / 10f;
            // A chord is two tubes along the beam: the beam's own vertices a metre, in the frame (both chords and the diagonals), in bands of a quarter metre.
            Console.WriteLine($"  limit frame: a metre of the beam {perMetreFull:0} vertices full, {perMetreFar:0} far; 10 m frame {a.VertexCount} full ({a.Metal.VertexCount} steel, {a.Solids[(int)SolidColour.Yellow].VertexCount} yellow, {a.Solids[(int)SolidColour.Black].VertexCount} black), {fa.VertexCount} far; 20 m frame {b.VertexCount} full, {fb.VertexCount} far");
            var tube = new MeshBuilder();
            Solids.Tube(tube, Vec3.Zero, new Vec3(0f, 1f, 0f), 0.045f, 0.045f, 8, false, false);
            int oneBand = tube.VertexCount;
            Console.WriteLine($"  limit frame: a tube of 8 sides makes {oneBand} vertices a band, {oneBand * 4} a metre of bands at 0.25 m, {Math.Min(oneBand, 14)} a tube at the far level (6 sides)");
            Program.Check(a.VertexCount < 25000 && b.VertexCount < 40000, "a frame of 10 m: " + a.VertexCount + ", of 20 m: " + b.VertexCount);
            Program.Check(fa.VertexCount < 2500 && fb.VertexCount < 4000, "the far level of 10 m: " + fa.VertexCount + ", of 20 m: " + fb.VertexCount);
            Program.Check(perMetreFull > 0f && perMetreFull < 600f && perMetreFar > 0f && perMetreFar < 120f, "the beam's vertices a metre: " + perMetreFull + " full, " + perMetreFar + " far");
            Program.Check(fa.VertexCount * 4 < a.VertexCount, "the far level is a fraction of the near one");
        }
    }
}
