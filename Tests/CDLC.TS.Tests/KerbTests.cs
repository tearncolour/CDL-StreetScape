using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The kerb styles of 0.12.33 (Kerbs): the flags of the upgrades, where the outer kerb is in a cross-section, the bodies drawn and the upgrades' icons.</summary>
    internal static class KerbTests
    {
        public static void RunAll()
        {
            Program.Run("kerbs: the style in the two side flags", Flags);
            Program.Run("kerbs: the outer kerb of a cross-section", Outer);
            Program.Run("kerbs: the face in the game's footway meshes (cycle lane, parking, bus bay)", Faces);
            Program.Run("kerbs: the bodies of each style, on either side of the line", Bodies);
            Program.Run("kerbs: closed bodies on a bend", Closed);
            Program.Run("kerbs: the upgrades' icons", Icons);
            Program.Run("kerbs: a kerb line on a bend snapped onto its kerb (0.12.49)", SnapOnBend);
        }

        private static SectionPiece Piece(float x, float width, float top, bool lanes) => new SectionPiece { X = x, Width = width, Top = top, Surface = true, RoadLanes = lanes };

        private static KerbLine Line(KerbStyle style, int footway, params Vec3[] foot)
        {
            var line = new KerbLine { Style = style, Rise = 0.2f, Footway = footway };
            line.Foot.AddRange(foot);
            return line;
        }

        /// <summary>
        /// A kerb line drawn at one lateral that is off its kerb on a bend (0.8 m out, 0.6 m in) lies on the kerb's foot once snapped, or the nudge outward from it, at its own heights; a point farther
        /// than the reach stays, and two that come onto one place are one.
        /// </summary>
        private static void SnapOnBend()
        {
            // The foot of a kerb round a bend of 20 m radius in 1 m chords, the footway outside.
            var foot = new List<Vec3>();
            for (int i = 0; i <= 31; i++)
            {
                double a = Math.PI * 0.5 * i / 31;
                foot.Add(new Vec3((float)(20 * Math.Cos(a)), 0f, (float)(20 * Math.Sin(a))));
            }

            Vec3 outward = foot[0] - new Vec3(0f, 0f, 0f);
            var kerb = Line(KerbStyle.None, Vec3.Dot((foot[1] - foot[0]).HeadingXZ().RightXZ(), outward) >= 0f ? 1 : -1, foot.ToArray());
            var line = new List<Vec3>();
            for (int i = 0; i <= 45; i++)
            {
                double a = Math.PI * 0.5 * i / 45, r = i < 25 ? 20.8 : 19.4;
                line.Add(new Vec3((float)(r * Math.Cos(a)), 0.5f, (float)(r * Math.Sin(a))));
            }

            float Radius(Vec3 p) => (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
            List<Vec3> on = Kerbs.SnapToFeet(line, new[] { kerb }, 0f, Kerbs.SnapReach);
            Program.Check(on.Count == line.Count, $"every point is kept ({on.Count} of {line.Count})");
            Program.Check(on.TrueForAll(p => Math.Abs(Radius(p) - 20f) < 0.01f), "every point is on the kerb's foot within 1 cm");
            Program.Check(on.TrueForAll(p => Math.Abs(p.Y - 0.5f) < 1e-5f), "the points keep their heights");
            List<Vec3> nudged = Kerbs.SnapToFeet(line, new[] { kerb }, 0.3f, Kerbs.SnapReach + 0.3f);
            Program.Check(nudged.TrueForAll(p => Math.Abs(Radius(p) - 20.3f) < 0.01f), "nudged 0.3 m: every point 0.3 m out onto the footway");
            List<Vec3> inward = Kerbs.SnapToFeet(line, new[] { kerb }, -0.3f, Kerbs.SnapReach + 0.3f);
            Program.Check(inward.TrueForAll(p => Math.Abs(Radius(p) - 19.7f) < 0.01f), "nudged -0.3 m: every point 0.3 m in on the carriageway");
            var far = new List<Vec3> { new Vec3(26f, 0f, 1f), new Vec3(20.5f, 0f, 1f) };
            List<Vec3> kept = Kerbs.SnapToFeet(far, new[] { kerb }, 0f, Kerbs.SnapReach);
            Program.Check(kept.Count == 2 && Vec3.DistanceXZ(kept[0], far[0]) < 1e-5f, "a point 6 m from the kerb stays");
            Program.Check(Math.Abs(Radius(kept[1]) - 20f) < 0.01f, "the next one is snapped");
            var twice = new List<Vec3> { new Vec3(20.5f * (float)Math.Cos(0.1), 0f, 20.5f * (float)Math.Sin(0.1)), new Vec3(19.5f * (float)Math.Cos(0.1), 0f, 19.5f * (float)Math.Sin(0.1)) };
            Program.Check(Kerbs.SnapToFeet(twice, new[] { kerb }, 0f, Kerbs.SnapReach).Count == 1, "two points that come onto one place are one");
            Program.Check(Kerbs.SnapToFeet(line, new KerbLine[0], 0f, Kerbs.SnapReach).Count == line.Count, "no kerb: the line as it is");
        }

        private static void Flags()
        {
            Program.Check(Kerbs.StyleOf(0u) == KerbStyle.None, "no flags: the game's kerb");
            foreach (KerbStyle style in Kerbs.Styles)
            {
                uint set = Kerbs.SetBits(style), clear = Kerbs.ClearBits(style);
                Program.Check(Kerbs.StyleOf(set) == style, $"{style}: its flags give it back");
                Program.Check((set & clear) == 0 && (set | clear) == Kerbs.Bits, $"{style}: it sets and clears the two bits between them");
                // The other side flags of a road (its grass, trees, a wide footway) do not change the style.
                Program.Check(Kerbs.StyleOf(set | 0x1u | 0x4u | 0x100u) == style, $"{style}: other side flags do not change it");
                // An upgrade over another style leaves its own: (old | set) & ~clear.
                foreach (KerbStyle old in Kerbs.Styles)
                    Program.Check(Kerbs.StyleOf((Kerbs.SetBits(old) | set) & ~clear) == style, $"{style} over {old}");
            }
        }

        private static void Outer()
        {
            // A two-lane road of the game: footways 3.5 m wide at the road's height, the carriageway 0.2 m lower.
            var road = new List<SectionPiece> { Piece(-6.25f, 3.5f, 0f, false), Piece(-2.5f, 4f, -0.2f, true), Piece(2.5f, 4f, -0.2f, true), Piece(6.25f, 3.5f, 0f, false) };
            Program.Check(Kerbs.Outer(road, true, out float x, out float foot, out float top), "the right kerb is found");
            Program.Near(x, 4.5f, 1e-4f, "the right kerb's x");
            Program.Near(foot, -0.2f, 1e-4f, "the carriageway's height");
            Program.Near(top, 0f, 1e-4f, "the footway's height");
            Program.Check(Kerbs.Outer(road, false, out x, out _, out _), "the left kerb is found");
            Program.Near(x, -4.5f, 1e-4f, "the left kerb's x");

            // A sliver between the carriageway and the footway: the face half way.
            var sliver = new List<SectionPiece> { Piece(2.5f, 4f, -0.2f, true), Piece(6.35f, 3.5f, 0f, false) };
            Program.Check(Kerbs.Outer(sliver, true, out x, out _, out _), "a kerb with a sliver is found");
            Program.Near(x, 4.55f, 1e-4f, "the face half way across the sliver");

            // A highway's shoulder at the carriageway's height, a deck's side layer, a piece on another level: no kerb.
            var shoulder = new List<SectionPiece> { Piece(2.5f, 4f, -0.2f, true), Piece(5.5f, 2f, -0.2f, false) };
            Program.Check(!Kerbs.Outer(shoulder, true, out _, out _, out _), "a shoulder is no kerb");
            var layer = new List<SectionPiece> { Piece(2.5f, 4f, -0.2f, true), new SectionPiece { X = 6.25f, Width = 3.5f, Top = 0f, Surface = true, Layer = true } };
            Program.Check(!Kerbs.Outer(layer, true, out _, out _, out _), "a deck's side layer is no footway");
            Program.Check(!Kerbs.Outer(new List<SectionPiece> { Piece(6.25f, 3.5f, 0f, false) }, true, out _, out _, out _), "no carriageway, no kerb");
            Program.Check(!Kerbs.Outer(road.GetRange(0, 3), true, out _, out _, out _), "nothing outside the carriageway, no kerb");
        }

        /// <summary>A piece mesh in its own frame as the game has it (the carriageway's side at +x, one row across every 4 m along it, Logs/CDLC.TS.pieces-0.12.33.txt); bend moves its kerb along it.</summary>
        private static List<Vec3> Own((float x, float y)[] across, Func<float, float> bend = null)
        {
            var points = new List<Vec3>();
            for (float z = -32f; z <= 32f; z += 4f)
            {
                foreach ((float x, float y) in across)
                    points.Add(new Vec3(x + (bend != null && x < 1.7f ? bend(z) : 0f), y, z));
            }

            return points;
        }

        // The game's footways: 3.5 m (the face 0.4 m in), 3.5 m with a cycle lane (1.5 m in) and 5 m with parking (2 m in).
        private static readonly (float, float)[] Sidewalk35 = { (-1.75f, 0f), (1.15f, 0f), (1.35f, 0f), (1.35f, -0.2f), (1.75f, -0.2f) };
        private static readonly (float, float)[] Bike350 = { (-1.75f, 0f), (0.05f, 0f), (0.25f, 0f), (0.25f, -0.2f), (1.75f, -0.2f) };
        private static readonly (float, float)[] Parking5 = { (-2.5f, 0f), (0.3f, 0f), (0.5f, 0f), (0.5f, -0.2f), (2.5f, -0.2f) };

        /// <summary>A road of two 6 m carriageways (x -6 .. 6, at -0.2) with a footway piece of the mesh on either side (its top at 0), split at its kerbs as SurfaceReader reads it.</summary>
        private static List<SectionPiece> Road((float x, float y)[] footway, float width)
        {
            Kerbs.PieceFaces own = Kerbs.FacesOf(Own(footway));
            float half = width * 0.5f;
            var pieces = new List<SectionPiece>();
            Kerbs.Split(Piece(-6f - half, width, 0f, false), own.Placed(true, -6f - half, 0f), pieces);
            pieces.Add(Piece(-3f, 6f, -0.2f, true));
            pieces.Add(Piece(3f, 6f, -0.2f, true));
            Kerbs.Split(Piece(6f + half, width, 0f, false), own.Placed(false, 6f + half, 0f), pieces);
            return pieces;
        }

        private static void Faces()
        {
            Kerbs.PieceFaces own = Kerbs.FacesOf(Own(Sidewalk35));
            Program.Check(own.Right && !own.Left && !own.Uneven, "a footway's mesh: one kerb, toward the carriageway (+x in its own frame)");
            Program.Near(own.RightX, 1.35f, 1e-3f, "its face 0.4 m in from its edge");
            Program.Near(own.RightFoot, -0.2f, 1e-4f, "the strip in front at the carriageway's level");
            Program.Near(own.Top, 0f, 1e-4f, "the footway behind it");
            foreach (((float, float)[] mesh, float width, float inset, string what) in new[] { (Sidewalk35, 3.5f, 0.4f, "a plain footway"), (Bike350, 3.5f, 1.5f, "a footway with a cycle lane"), (Parking5, 5f, 2f, "a footway with parking") })
            {
                List<SectionPiece> road = Road(mesh, width);
                foreach (bool right in new[] { true, false })
                {
                    float sign = right ? 1f : -1f;
                    Program.Check(Kerbs.Outer(road, right, out float x, out float foot, out float top), $"{what}, {(right ? "right" : "left")}: a kerb");
                    Program.Near(x, sign * (6f + inset), 1e-3f, $"{what}, {(right ? "right" : "left")}: the face {inset} m behind the carriageway's edge");
                    Program.Near(foot, -0.2f, 1e-4f, $"{what}: the foot at the carriageway's level");
                    Program.Near(top, 0f, 1e-4f, $"{what}: the top at the footway's");
                }

                // The strip in front of each kerb is painted at the carriageway's height (the owner's screenshot of 0.12.36: a line by a cycle lane stood 20 cm up where it crossed onto the footway piece).
                float total = 12f + 2f * width;
                List<PaintableStretch> paint = SurfaceSection.Paintable(road, total);
                Program.Check(paint.Count == 1, $"{what}: one band of paint across: {paint.Count}");
                if (paint.Count == 1)
                {
                    Program.Near((paint[0].F0 - 0.5f) * total, -(6f + inset), 1e-3f, $"{what}: the paint reaches the left kerb");
                    Program.Near((paint[0].F1 - 0.5f) * total, 6f + inset, 1e-3f, $"{what}: and the right one");
                    Program.Near(paint[0].Y, -0.2f, 1e-4f, $"{what}: at the carriageway's height");
                }
            }

            // A bus bay: its kerb steps 1 m back along the piece. The piece is not split and no kerb of it is drawn (a straight one would cut across the bay).
            Kerbs.PieceFaces bay = Kerbs.FacesOf(Own(Parking5, z => Math.Abs(z) < 30f ? -1f : 0f));
            Program.Check(bay.Uneven && !bay.Left && !bay.Right, "a bus bay's kerb is uneven");
            var bayRoad = new List<SectionPiece> { Piece(-3f, 6f, -0.2f, true), Piece(3f, 6f, -0.2f, true) };
            Kerbs.Split(Piece(8.5f, 5f, 0f, false), bay.Placed(false, 8.5f, 0f), bayRoad);
            Program.Check(bayRoad.Count == 3 && bayRoad[2].Uneven && !Kerbs.Outer(bayRoad, true, out _, out _, out _), "a bay's piece is whole and has no kerb");
            // A flat piece (a level crossing) and a piece without a mesh: whole, the kerb where the pieces meet.
            Kerbs.PieceFaces flat = Kerbs.FacesOf(Own(new[] { (-1.75f, 0f), (1.75f, 0f) }));
            Program.Check(!flat.Left && !flat.Right && !flat.Uneven, "a flat piece has no kerb");
            Program.Check(!Kerbs.FacesOf(null).Left && !Kerbs.FacesOf(null).Right, "no mesh, no kerb");
            var whole = new List<SectionPiece> { Piece(-3f, 6f, -0.2f, true), Piece(3f, 6f, -0.2f, true) };
            Kerbs.Split(Piece(7.75f, 3.5f, 0f, false), Kerbs.FacesOf(null), whole);
            Program.Check(whole.Count == 3 && Kerbs.Outer(whole, true, out float mx, out _, out _) && Math.Abs(mx - 6f) < 1e-3f, "without a mesh the kerb is where the pieces meet");
            // A median piece with kerbs on both sides (a raised strip between carriageways): split into its two strips and the raised middle.
            Kerbs.PieceFaces median = Kerbs.FacesOf(Own(new[] { (-1f, -0.2f), (-0.8f, -0.2f), (-0.8f, 0f), (0.8f, 0f), (0.8f, -0.2f), (1f, -0.2f) }));
            Program.Check(median.Left && median.Right, "a raised median's two kerbs");
            var parts = new List<SectionPiece>();
            Kerbs.Split(Piece(0f, 2f, 0f, false), median.Placed(false, 0f, 0f), parts);
            Program.Check(parts.Count == 3 && Math.Abs(parts[1].Width - 1.6f) < 1e-3f && Math.Abs(parts[0].Top + 0.2f) < 1e-4f && Math.Abs(parts[1].Top) < 1e-4f, "its strips at the carriageway's level and the 1.6 m between them raised");
        }

        private static void Bodies()
        {
            Vec3 a = new Vec3(0f, -0.2f, 0f), b = new Vec3(0f, -0.2f, 6f);
            foreach (int footway in new[] { 1, -1 })
            {
                foreach (KerbStyle style in Kerbs.Styles)
                {
                    KerbLine line = Line(style, footway, a, b);
                    line.Along = 3f;
                    List<MarkingShape> shapes = Kerbs.Shapes(line);
                    MeshBuilder stone = shapes[0].Mesh, gutter = shapes[1].Mesh;
                    Program.Check(shapes[0].Solid == SolidColour.Granite && shapes[0].Detail == DetailKind.KerbStone, $"{style} {footway}: the stone is granite with its detail");
                    Program.Check(stone.VertexCount > 0, $"{style} {footway}: a stone");
                    Program.Check((gutter.VertexCount > 0) == (style == KerbStyle.Gutter), $"{style} {footway}: a gutter only for the gutter style");
                    stone.Bounds(out Vec3 lo, out Vec3 hi);
                    // Toward the footway is +X for a footway on the line's right (the line runs +Z).
                    float near = footway * (style == KerbStyle.Flush ? -Kerbs.SlopeWidth : -Kerbs.Proud), far = footway * Kerbs.TopWidth;
                    Program.Near(lo.X, Math.Min(near, far), 1e-4f, $"{style} {footway}: the stone's side toward -X");
                    Program.Near(hi.X, Math.Max(near, far), 1e-4f, $"{style} {footway}: the stone's side toward +X");
                    Program.Near(hi.Y, -0.2f + 0.2f + Kerbs.Over, 1e-4f, $"{style} {footway}: the stone's top just over the footway");
                    Program.Check(lo.Y < -0.2f, $"{style} {footway}: the stone goes into the carriageway");
                    // On the faces along the line (not the end caps, which face along it), v is the distance along the kerb.
                    float lowV = float.MaxValue;
                    for (int i = 0; i < stone.VertexCount; i++)
                    {
                        if (Math.Abs(stone.Normals[i].Z) < 0.5f)
                            lowV = Math.Min(lowV, stone.V[i]);
                    }

                    Program.Near(lowV, 3f, 1e-4f, $"{style} {footway}: the joints start where the line does along its kerb");
                    if (style == KerbStyle.Gutter)
                    {
                        gutter.Bounds(out Vec3 glo, out Vec3 ghi);
                        Program.Near(footway > 0 ? glo.X : -ghi.X, -Kerbs.Proud - Kerbs.GutterWidth, 1e-4f, $"{footway}: the gutter's outer edge");
                        Program.Near(ghi.Y, -0.2f + Kerbs.Over, 1e-4f, $"{footway}: the gutter's top just over the carriageway");
                    }
                }
            }

            foreach (List<MarkingShape> none in new[] { Kerbs.Shapes(Line(KerbStyle.None, 1, a, b)), Kerbs.Shapes(Line(KerbStyle.Raised, 1, a)), Kerbs.Shapes(null) })
                Program.Check(none[0].Mesh.VertexCount == 0 && none[1].Mesh.VertexCount == 0, "no style, no length or no line: nothing");
        }

        /// <summary>Every edge of the bodies (by where its ends are) is used once each way: closed and turned one way round.</summary>
        private static void Closed()
        {
            var bend = new List<Vec3>();
            for (int i = 0; i <= 12; i++)
            {
                double t = i / 12.0 * Math.PI * 0.5;
                bend.Add(new Vec3((float)(20.0 - 20.0 * Math.Cos(t)), 0f, (float)(20.0 * Math.Sin(t))));
            }

            foreach (int footway in new[] { 1, -1 })
            {
                foreach (KerbStyle style in Kerbs.Styles)
                {
                    foreach (MarkingShape shape in Kerbs.Shapes(Line(style, footway, bend.ToArray())))
                    {
                        if (shape.Mesh.VertexCount == 0)
                            continue;
                        string open = OpenEdge(shape.Mesh);
                        Program.Check(open == null, $"{style} {footway} {shape.Detail}: closed ({open})");
                    }
                }
            }
        }

        private static string OpenEdge(MeshBuilder mesh)
        {
            string Key(Vec3 p) => $"{Math.Round(p.X * 1e4)},{Math.Round(p.Y * 1e4)},{Math.Round(p.Z * 1e4)}";
            var edges = new Dictionary<(string, string), int>();
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                for (int k = 0; k < 3; k++)
                {
                    (string, string) edge = (Key(mesh.Positions[mesh.Indices[t + k]]), Key(mesh.Positions[mesh.Indices[t + (k + 1) % 3]]));
                    if (edge.Item1 != edge.Item2)
                        edges[edge] = edges.TryGetValue(edge, out int n) ? n + 1 : 1;
                }
            }

            foreach (KeyValuePair<(string, string), int> pair in edges)
            {
                if (pair.Value != 1 || !edges.TryGetValue((pair.Key.Item2, pair.Key.Item1), out int back) || back != 1)
                    return pair.Key.Item1 + " -> " + pair.Key.Item2;
            }

            return null;
        }

        private static void Icons()
        {
            var pictures = new List<byte[]>();
            foreach (KerbStyle style in Kerbs.Styles)
            {
                byte[] rgba = Thumbnails.KerbRgba(style);
                Program.Check(rgba.Length == Thumbnails.Size * Thumbnails.Size * 4, $"{style}: a thumbnail's size");
                int solid = 0;
                for (int i = 3; i < rgba.Length; i += 4)
                {
                    if (rgba[i] >= 128)
                        solid++;
                }

                Program.Check(solid > Thumbnails.Size * Thumbnails.Size / 5, $"{style}: the kerb fills its icon ({solid} px)");
                Program.Check(Thumbnails.KerbFile(style).StartsWith("k-kerb" + (int)style + ".2-", StringComparison.Ordinal), $"{style}: its file");
                foreach (byte[] other in pictures)
                {
                    bool same = true;
                    for (int i = 0; i < rgba.Length && same; i++)
                        same = rgba[i] == other[i];
                    Program.Check(!same, $"{style}: its own picture");
                }

                pictures.Add(rgba);
            }
        }
    }
}
