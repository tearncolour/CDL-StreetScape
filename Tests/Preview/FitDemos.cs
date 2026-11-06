using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Pictures of the area markings fitted to the road (0.12.4), from made-up roads (no game): an exit's gore whose two roads part 20 m past the nose with a barrier and grass between them, as 0.12.3
    /// drew it (over the barrier and the grass) and as it is drawn now (stopped at the barrier's nose); a lopsided island with the points of its Vs on its middle line; a box grid drawn as an L. A side view
    /// of the fitted gore on a road that rises 4 % and twists shows its height. Usage: Preview.exe root out --fit-demos. Geometry only; not the game.
    /// </summary>
    internal static class FitDemos
    {
        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static SurfaceStrip Strip(float x0, float x1, float z0, float z1, Func<float, float, float> y, int n = 12)
        {
            var strip = new SurfaceStrip();
            for (int i = 0; i <= n; i++)
            {
                float z = z0 + (z1 - z0) * i / n;
                strip.Add(P(x0, z, y(x0, z)), P(x1, z, y(x1, z)));
            }

            return strip;
        }

        /// <summary>A plan of the given meshes (the world's frame) in one colour each, and the outlines of the road's surface as thin grey lines.</summary>
        private static MarkingPlan Picture(IEnumerable<(MeshBuilder Mesh, MarkingColour Colour)> meshes, IEnumerable<SurfaceStrip> road)
        {
            var plan = new MarkingPlan();
            if (road != null)
            {
                foreach (SurfaceStrip s in road)
                {
                    var edge = new MarkingShape { Colour = MarkingColour.Black };
                    RoadMarkings.Ribbon(edge.Mesh, s.Left, 0.12f);
                    RoadMarkings.Ribbon(edge.Mesh, s.Right, 0.12f);
                    plan.Shapes.Add(edge);
                }
            }

            foreach ((MeshBuilder mesh, MarkingColour colour) in meshes)
            {
                var shape = new MarkingShape { Colour = colour };
                shape.Mesh.Append(mesh, Vec3.Zero, 0f);
                plan.Shapes.Add(shape);
            }

            plan.Bake(0f);
            return plan;
        }

        public static int Run(string output)
        {
            var tiles = new List<MarkingPlan>();
            var notes = new List<string>();

            // The gore: the main road's right edge straight north from the nose, the ramp's left edge curving away to 8 m at 60 m.
            var main = new List<Vec3>();
            var ramp = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                float z = i * 2.5f;
                main.Add(P(0f, z));
                ramp.Add(P(8f * (z / 60f) * (z / 60f) + 0.0f, z));
            }

            MarkingShape gore = MarkingAreas.Gore(main, ramp, true);
            // The road: the junction's pavement 20 m past the nose, then each road on its own, with a barrier 0.5 m inside each side of the gore and grass between.
            Func<float, float, float> rise = (x, z) => 0.04f * z + 0.02f * x;
            // The ramp's own surface beyond 20 m: from its left edge (the gore's side) moved 0.5 m out to 10 m beyond it, following the curve.
            var rampRoad = new SurfaceStrip();
            for (int i = 8; i <= 28; i++)
            {
                float z = i * 2.5f, edge = 8f * (z / 60f) * (z / 60f);
                rampRoad.Add(P(edge + 0.5f, z, rise(edge + 0.5f, z)), P(edge + 10f, z, rise(edge + 10f, z)));
            }

            var road = new List<SurfaceStrip>
            {
                Strip(-10f, 18f, -20f, 20f, rise),
                Strip(-10f, -0.5f, 20f, 70f, rise),
                rampRoad
            };
            AreaFit fit = AreaFit.FromStrips(road, Vec3.Zero, 0f, -1f, 9f, 0f, 60f);
            MeshBuilder draped = fit.Drape(gore.Mesh);
            tiles.Add(Picture(new[] { (gore.Mesh, MarkingColour.White) }, road));
            notes.Add($"gore as 0.12.3: {gore.Mesh.TriangleCount} triangle(s) over the barrier and the grass");
            tiles.Add(Picture(new[] { (draped, MarkingColour.White) }, road));
            float worst = 0f, far = 0f;
            foreach (Vec3 p in draped.Positions)
            {
                worst = Math.Max(worst, Math.Abs(p.Y - rise(p.X, p.Z)));
                far = Math.Max(far, p.Z);
            }

            notes.Add($"gore fitted: {draped.TriangleCount} triangle(s), up to {far:0.0} m from the nose (the barrier at 20 m), off the rising road by at most {worst * 1000f:0.0} mm");

            // A lopsided island: its middle line and the points of its Vs.
            var wedge = new List<Vec3> { P(0f, 0f), P(7f, 30f), P(-1.5f, 31f) };
            var apexes = new List<Vec3>();
            MarkingShape island = MarkingAreas.Island(wedge, 0.15f, MarkingWarnings.ChannelFill.Chevron, MarkingColour.White, 0.45f, false, apexes);
            var axis = new MarkingShape { Colour = MarkingColour.Red };
            RoadMarkings.Ribbon(axis.Mesh, MarkingAreas.MarchingAxis(MarkingFills.Inset(wedge, 0.15f) ?? wedge), 0.08f);
            foreach (Vec3 a in apexes)
                RoadMarkings.Ribbon(axis.Mesh, new List<Vec3> { a - P(0.25f, 0f), a + P(0.25f, 0f) }, 0.25f);
            tiles.Add(Picture(new[] { (island.Mesh, MarkingColour.White), (axis.Mesh, MarkingColour.Red) }, null));
            notes.Add($"island: {apexes.Count} V point(s) on its middle line (red)");

            // A box grid drawn as an L.
            var l = new List<Vec3> { P(0f, 0f), P(14f, 0f), P(14f, 5f), P(5f, 5f), P(5f, 14f), P(0f, 14f) };
            MarkingShape grid = MarkingFigures.GridArea(l, 1f);
            tiles.Add(Picture(new[] { (grid.Mesh, MarkingColour.White) }, null));
            notes.Add($"box grid: an L of {l.Count} corners, {grid.Mesh.TriangleCount} triangle(s)");

            const int w = 420, h = 840;
            var sheet = new byte[tiles.Count * w * h * 4];
            for (int t = 0; t < tiles.Count; t++)
            {
                byte[] raw = MarkingRaster.Render(tiles[t], w, h, out int rw, out int rh, 0.04f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet, (y * tiles.Count * w + t * w) * 4, rw * 4);
            }

            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "fit-demos.png"), Png.Encode(sheet, tiles.Count * w, h, false));

            // The side view of the fitted gore: its height along z (the paint's points, dark) against the road's (a line).
            const int sw = 840, sh = 300;
            var side = new byte[sw * sh * 4];
            for (int i = 0; i < side.Length; i += 4)
            {
                side[i] = side[i + 1] = side[i + 2] = 60;
                side[i + 3] = 255;
            }

            void Dot(float z, float y, byte r, byte g, byte b)
            {
                int px = (int)((z + 2f) / 64f * (sw - 1)), py = sh - 1 - (int)((y + 0.5f) / 4f * (sh - 1));
                if (px < 0 || py < 0 || px >= sw || py >= sh)
                    return;
                int k = (py * sw + px) * 4;
                side[k] = r;
                side[k + 1] = g;
                side[k + 2] = b;
            }

            for (float z = -2f; z <= 62f; z += 0.05f)
                Dot(z, rise(4f, z), 120, 200, 255);
            foreach (Vec3 p in draped.Positions)
                Dot(p.Z, p.Y, 255, 255, 255);
            File.WriteAllBytes(Path.Combine(output, "fit-demo-side.png"), Png.Encode(side, sw, sh, false));
            notes.Add(PostsDemo(output));
            foreach (string note in notes)
                Console.WriteLine(note);
            return 0;
        }

        /// <summary>
        /// 0.12.5: posts on the ground, seen from the road (the sign's X across, Y up): a wide board on two posts and a gantry of 16 m, as 0.12.4 stood them (every foot at the road's height) and as they
        /// stand now, beside a road whose verge falls away (1.2 m down at the far post) and onto a footway 0.2 m up. The ground is the light line. File fit/posts-demo.png.
        /// </summary>
        private static string PostsDemo(string output)
        {
            var board = new SignDef { Id = "demo.board", Code = "d", Zh = "d", En = "d", Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = 4f, Height = 2f, BottomClearance = SupportCatalog.RoadsideClearance, TextureWidth = 400, TextureHeight = 200, Keywords = new string[0] };
            var instance = new SignInstance { Def = board, Key = board.Id, Spec = "", TexW = 400, TexH = 200 };
            SignParts twin = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.DoublePole), 0f);
            SignParts gantry = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.Gantry), 16f);
            // The verge falls 0.4 m a metre from x = 0.5 (under the twin's posts), and beside the gantry's far post the ground is 1.2 m lower; its near post on a footway 0.2 m up.
            Func<float, float> verge = x => x < 0.5f ? 0f : -(x - 0.5f) * 0.4f;
            Func<float, float> gantryGround = x => x < 1f ? 0.2f : x < 14f ? 0f : -1.2f;
            var cases = new List<(string, SignParts, Func<float, float>)> { ("twin", twin, verge), ("gantry", gantry, gantryGround) };
            const int cw = 520, ch = 420;
            var sheet = new byte[cw * 4 * ch * 4];
            for (int i = 0; i < sheet.Length; i += 4)
            {
                sheet[i] = sheet[i + 1] = sheet[i + 2] = 58;
                sheet[i + 3] = 255;
            }

            var lines = new List<string>();
            for (int c = 0; c < cases.Count; c++)
            {
                (string name, SignParts parts, Func<float, float> ground) = cases[c];
                GroundProfile profile = GroundProfile.Sample((x, z) => ground(x), -3f, 24f);
                PostGround.Offsets(parts, profile, out float[] top, out float[] bottom);
                parts.FootTags(out int[] metalTags, out int[] concreteTags, out _);
                var metal = new MeshBuilder();
                var concrete = new MeshBuilder();
                PostGround.Append(metal, parts.Metal, metalTags, top, bottom, Vec3.Zero, 0f);
                PostGround.Append(concrete, parts.Concrete, concreteTags, top, bottom, Vec3.Zero, 0f);
                lines.Add($"{name}: feet moved {string.Join(", ", Array.ConvertAll(top, t => t.ToString("+0.00;-0.00")))} m");
                parts.Metal.Bounds(out Vec3 min, out Vec3 max);
                float x0 = Math.Min(min.X, -1f) - 0.5f, x1 = max.X + 0.5f, y0 = -2f, y1 = max.Y + 0.4f;
                float scale = Math.Min((cw - 20) / (x1 - x0), (ch - 20) / (y1 - y0));
                for (int panel = 0; panel < 2; panel++)
                {
                    int ox = (c * 2 + panel) * cw;
                    void Fill(MeshBuilder mesh, byte r, byte g, byte b)
                    {
                        for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                        {
                            Vec3 a = mesh.Positions[mesh.Indices[t]], bb = mesh.Positions[mesh.Indices[t + 1]], cc = mesh.Positions[mesh.Indices[t + 2]];
                            float ax = 10 + (a.X - x0) * scale, ay = ch - 10 - (a.Y - y0) * scale, bx = 10 + (bb.X - x0) * scale, by = ch - 10 - (bb.Y - y0) * scale, cx = 10 + (cc.X - x0) * scale, cy = ch - 10 - (cc.Y - y0) * scale;
                            int px0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx)))), px1 = Math.Min(cw - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                            int py0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy)))), py1 = Math.Min(ch - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                            float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                            if (Math.Abs(area) < 1e-6f)
                                continue;
                            for (int py = py0; py <= py1; py++)
                            {
                                for (int px = px0; px <= px1; px++)
                                {
                                    float qx = px + 0.5f, qy = py + 0.5f;
                                    float w0 = ((bx - qx) * (cy - qy) - (by - qy) * (cx - qx)) / area, w1 = ((cx - qx) * (ay - qy) - (cy - qy) * (ax - qx)) / area, w2 = 1f - w0 - w1;
                                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f)
                                        continue;
                                    int k = (py * cw * 4 + ox + px) * 4;
                                    sheet[k] = r;
                                    sheet[k + 1] = g;
                                    sheet[k + 2] = b;
                                }
                            }
                        }
                    }

                    bool grounded = panel == 1;
                    Fill(grounded ? concrete : parts.Concrete, 150, 150, 145);
                    Fill(grounded ? metal : parts.Metal, 205, 210, 215);
                    // The ground.
                    for (int px = 0; px < cw; px++)
                    {
                        float x = x0 + (px - 10) / scale;
                        int py = (int)(ch - 10 - (ground(x) - y0) * scale);
                        for (int d = 0; d < 2; d++)
                        {
                            int yy = py + d;
                            if (yy < 0 || yy >= ch)
                                continue;
                            int k = (yy * cw * 4 + ox + px) * 4;
                            sheet[k] = 140;
                            sheet[k + 1] = 220;
                            sheet[k + 2] = 120;
                        }
                    }
                }
            }

            Directory.CreateDirectory(output);
            File.WriteAllBytes(Path.Combine(output, "posts-demo.png"), Png.Encode(sheet, cw * 4, ch, false));
            return "posts: " + string.Join("; ", lines) + " (posts-demo.png: before and after, the twin posts then the gantry)";
        }
    }
}
