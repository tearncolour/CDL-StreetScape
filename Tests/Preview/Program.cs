using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Render;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Writes, for every entry the panel lists, (1) the painted picture as a PNG and (2) the sign as a viewer in front of it sees it: the real face mesh, with its picture
    /// coordinates, rasterised in software with the picture on it. The second is what catches a mirrored picture, which the first cannot (0.1.1 drew right pictures on a mesh that
    /// mirrored them, and it was found only in the game). Then a few boards whole on each support, and the thumbnails exactly as the mod makes them, on a sheet.
    /// Usage: Preview.exe &lt;repo root&gt; &lt;output folder&gt; [choice id ...]   (ids after the folder limit the run; without them every entry is drawn)
    /// </summary>
    internal static class Program
    {
        private static readonly string[] kWhole = { "gb.exit.adv.500", "gb.exit.sign", "gb.count.300" };

        /// <summary>Pictures of markings (MarkingRaster, as the panel's thumbnails and editor have them) side by side in one PNG, four to a row.</summary>
        private static int MarkingPictures(string output, List<string> wanted)
        {
            // TS_PICTURE_SIZE: a larger picture to look at a body up close (the panel's are 360).
            int size = int.TryParse(Environment.GetEnvironmentVariable("TS_PICTURE_SIZE"), out int asked) && asked >= 64 ? asked : 360;
            int count = wanted.Count, cols = Math.Min(4, Math.Max(1, count)), rows = Math.Max(1, (count + cols - 1) / cols);
            var sheet = new byte[cols * size * rows * size * 4];
            for (int n = 0; n < count; n++)
            {
                int bar = wanted[n].IndexOf('|');
                string[] parts = bar < 0 ? new[] { wanted[n] } : new[] { wanted[n].Substring(0, bar), wanted[n].Substring(bar + 1) };
                SignDef def = SignCatalog.ById(parts[0]);
                if (def == null || !def.IsMarking)
                {
                    Console.WriteLine("no such marking: " + parts[0]);
                    continue;
                }

                GuideSpec spec = def.Template.Default();
                if (parts.Length > 1)
                {
                    foreach (string pair in parts[1].Split(';'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0)
                            spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
                    }
                }

                MarkingPlan plan = MarkingTemplates.PlanOf(def, spec.ToString());
                byte[] raw = MarkingRaster.Render(plan, size, size, out int w, out int h, 0.06f);
                Console.WriteLine(parts[0] + ": " + plan.TriangleCount + " triangles, " + (plan.MaxX - plan.MinX).ToString("0.00") + " x " + (plan.MaxZ - plan.MinZ).ToString("0.00") + " m");
                int ox = n % cols * size, oy = n / cols * size;
                for (int y = 0; y < h; y++)
                    Array.Copy(raw, y * w * 4, sheet, ((oy + y) * cols * size + ox) * 4, w * 4);
            }

            File.WriteAllBytes(Path.Combine(output, "marking-pictures.png"), Png.Encode(sheet, cols * size, rows * size, false));
            return 0;
        }

        private static int Main(string[] args)
        {
            string root = args.Length > 0 ? args[0] : ".";
            string output = args.Length > 1 ? args[1] : Path.Combine(root, "out");
            var only = new HashSet<string>();
            for (int i = 2; i < args.Length; i++)
                only.Add(args[i]);
            Directory.CreateDirectory(output);
            // The fonts as the mod chooses them; TS_FONTS: a folder of a player's (cjk.ttf, latin.ttf, as ModsData/CDLC.TS/Fonts) to look at them in place of the mod's.
            SignFonts.Resolve(Environment.GetEnvironmentVariable("TS_FONTS"), Path.Combine(root, "Fonts"));
            SignBuilder.LedGrids = LedScreens.Grid;
            TrafficSample.Register();
            if (only.Contains("--codes"))
            {
                // Every entry of the catalogue: group, standard's code, id, Chinese name (tab separated), for checking what the standard has against what is made.
                foreach (SignChoice choice in SignCatalog.Choices)
                    Console.WriteLine(string.Join("	", new[] { choice.Group ?? "", choice.Code ?? "", choice.Id, choice.Zh }));
                return 0;
            }

            if (only.Contains("--markings"))
                return MarkingSheet.Run(output, SignFonts.Cjk);

            if (only.Contains("--detail"))
                return DetailSheet.Run(output);

            // A sign of the player's own (0.12.29) from a PNG file, in each shape and fill: Preview.exe root out --custom picture.png
            if (only.Contains("--custom") && args.Length > 3)
                return CustomSheet(output, args[3]);

            if (only.Contains("--island"))
            {
                var corners = new List<string>();
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] != "--island")
                        corners.Add(args[i]);
                }

                return IslandProbe.Run(output, corners);
            }

            if (only.Contains("--support-tiles"))
                return SupportTiles.Run(output, only.Where(a => !a.StartsWith("--", StringComparison.Ordinal)));

            if (only.Contains("--area-demos"))
                return AreaDemos.Run(output);

            if (only.Contains("--auto-demos"))
                return AutoDemos.Run(output);

            if (only.Contains("--fit-demos"))
                return FitDemos.Run(Path.Combine(output, "fit"));

            if (only.Contains("--marking-normals"))
                return NormalDemos.Run(Path.Combine(output, "normals"));

            if (only.Contains("--bend-demos"))
                return BendDemos.Run(Path.Combine(output, "bend"));

            if (only.Contains("--green-demos"))
                return GreenDemos.Run(Path.Combine(output, "green"));

            if (only.Contains("--marking-pictures"))
            {
                // The ids that follow (id, or id|key=value;key=value to change settings), each as the picture a thumbnail has, side by side in one file.
                var wanted = new List<string>();
                for (int i = 2; i < args.Length; i++)
                {
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        wanted.Add(args[i]);
                }

                return MarkingPictures(output, wanted);
            }

            if (only.Contains("--barrier-views"))
                return BarrierViews.Run(output, args);

            if (only.Contains("--props"))
                return PropSheet(output);

            if (only.Contains("--editor-rows"))
            {
                string[] ids = args.Skip(2).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToArray();
                return EditorRows.Run(output, ids.Length > 0 ? ids[0] : "gb.guide8.01", !ids.Contains("en"));
            }

            if (only.Contains("--assembly"))
            {
                // The ids that follow, in the order they were put on the support.
                var ids = new List<string>();
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] != "--assembly")
                        ids.Add(args[i]);
                }

                return AssemblySheet(output, ids);
            }

            if (only.Contains("--glyphs"))
            {
                only.Remove("--glyphs");
                return GlyphSheet(output, only);
            }

            if (args.Length > 2 && args[2] == "--variant")
            {
                // Preview.exe root out --variant id "k=v;k=v" label: the entry's own spec with the settings after it changed, drawn as label.png and label.view.png (for a board the
                // catalogue has no preset for: the provincial naming plate, a long name...).
                if (args.Length < 6)
                {
                    Console.WriteLine("usage: --variant <id> <k=v;k=v> <label>");
                    return 1;
                }

                return VariantSheet(output, args[3], args[4], args[5]);
            }

            if (args.Length > 2 && args[2] == "--closeup")
                return Closeup(output, args);

            var sheet = new List<byte[]>();
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (only.Count > 0 && !only.Contains(choice.Id))
                    continue;
                SignInstance instance = SignInstances.Of(choice.Def, choice.Spec);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
                long painted = clock.ElapsedMilliseconds;
                if (raw.Length != w * h * 4)
                {
                    Console.WriteLine($"FAIL {choice.Id}: {raw.Length} bytes for {w}x{h}");
                    return 1;
                }

                Save(raw, w, h, Path.Combine(output, choice.Id + ".png"));
                byte[] view = ViewFromFront(instance, raw, w, h, out int vw, out int vh);
                Save(view, vw, vh, Path.Combine(output, choice.Id + ".view.png"));
                ImageOps.AverageLinear(raw, w, h, out float tr, out float tg, out float tb);
                Console.WriteLine($"{choice.Id}: picture {w}x{h} painted in {painted} ms, seen through the mesh {vw}x{vh}, night glow colour {tr:0.00} {tg:0.00} {tb:0.00}");

                byte[] png = Thumbnails.SignPng(instance, raw, w, h);
                File.WriteAllBytes(Path.Combine(output, "thumb." + Thumbnails.FileName("s", choice.Id)), png);
                if (!Png.TryDecode(png, out byte[] back, out int bw, out int bh) || bw != Thumbnails.Size || bh != Thumbnails.Size)
                {
                    Console.WriteLine($"FAIL thumbnail of {choice.Id} does not decode");
                    return 1;
                }

                sheet.Add(back);

                if (only.Count > 0 || Array.IndexOf(kWhole, choice.Id) >= 0)
                {
                    // The whole sign on every support, from the front, from above at an angle and from behind.
                    foreach (SupportDef support in SupportCatalog.All)
                    {
                        SignParts parts = SignBuilder.Build(instance, support);
                        // A wall support is drawn on a piece of wall (in the game the wall is the building's).
                        if (SupportKinds.OnWall(support.Kind))
                            SignBuilder.AddWallStandIn(parts, support);
                        else if (SupportKinds.Hosted(support.Kind))
                            SignBuilder.AddHostStandIn(parts);
                        else if (SupportKinds.OverRoad(support.Kind))
                            SignBuilder.AddOverStandIn(parts);
                        foreach ((string name, Vec3 direction) in new[] { ("front", new Vec3(0f, 0f, -1f)), ("iso", new Vec3(-0.55f, -0.30f, -0.78f)), ("back", new Vec3(0.45f, -0.25f, 0.85f)) })
                        {
                            byte[] image = Raster.Render(parts, raw, w, h, direction, name == "front" ? 900 : 700, out int iw, out int ih);
                            Save(image, iw, ih, Path.Combine(output, $"{choice.Id}.{support.Id}.{name}.png"));
                        }

                        Console.WriteLine($"{choice.Id} on {support.Id}: {parts.VertexCount} vertices, post {parts.Layout.PoleRadius * 2000f:0} mm, board {parts.Layout.BottomClearance:0.0} m up");
                    }
                }
            }

            if (only.Count == 0)
            {
                foreach (SupportDef support in SupportCatalog.Offerable)
                {
                    SignInstance carrier = Thumbnails.CarrierFor(support);
                    byte[] picture = LedScreens.PaintFace(carrier, out int tw, out int th);
                    byte[] png = Thumbnails.SupportPng(support, picture, tw, th);
                    File.WriteAllBytes(Path.Combine(output, "thumb." + Thumbnails.FileName("p", support.Id)), png);
                    if (!Png.TryDecode(png, out byte[] back, out int bw, out int bh) || bw != Thumbnails.Size || bh != Thumbnails.Size)
                    {
                        Console.WriteLine($"FAIL thumbnail of {support.Id} does not decode");
                        return 1;
                    }

                    sheet.Add(back);
                }

                WriteSheet(sheet, Path.Combine(output, "thumbnails.sheet.png"), 8);
            }

            Console.WriteLine("font: " + LegendPainter.FontSource);
            return 0;
        }

        /// <summary>The picture kept as the mod keeps an imported one (out/custom/images), then a sign of it in every shape and fill, the face and the view through the mesh of each.</summary>
        private static int CustomSheet(string output, string file)
        {
            CustomPictures.Folder = Path.Combine(output, "custom", "images");
            Directory.CreateDirectory(CustomPictures.Folder);
            CustomPictures.Warn = Console.WriteLine;
            CDLC.TS.Geo.Signs.CustomSigns.Masks = k => File.Exists(CustomPictures.MaskPathOf(k)) ? CustomMask.Decode(File.ReadAllBytes(CustomPictures.MaskPathOf(k))) : null;
            byte[] data = File.ReadAllBytes(file);
            string key = CDLC.TS.Geo.Signs.CustomSigns.KeyOf(data);
            if (!CustomPictures.Keep(data, key, out int pw, out int ph, out string why))
            {
                Console.WriteLine("FAIL " + file + ": " + why);
                return 1;
            }

            File.WriteAllBytes(Path.Combine(output, "add-tile.png"), Png.Encode(Thumbnails.AddTileRgba(), Thumbnails.Size, Thumbnails.Size, false));
            string spec = CDLC.TS.Geo.Signs.CustomSigns.SpecForPicture(key, pw, ph, file);
            Console.WriteLine($"custom: {file} kept as {key} ({pw} x {ph} px): {spec}");
            int failed = 0;
            foreach (string shape in new[] { "round", "rect", "circle", "octagon", "triangle", "triangle.down" })
            {
                foreach (string fit in new[] { "contain", "cover", "fill" })
                    failed += VariantSheet(output, CDLC.TS.Geo.Signs.CustomSigns.TemplateId, spec + ";shape=" + shape + ";fit=" + fit + ";ground=blue", "custom-" + shape + "-" + fit);
            }

            return failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// A close-up of a sign on a support, with the stand-in of the host post on the attached one (0.12.49, to look at how a lamp-post ad is held): Preview.exe root out --closeup id "k=v;k=v"|-
        /// support[@reach[@lean degrees[@style byte[@move m: a board on a signal's arm moved up (+) or down]]]] x,y,z half label [view ...], each view (front, iso, back, side, top, below, or a direction dx,dy,dz) drawn round the point as label.view.png, 900 px over twice half (m).
        /// Reduced detail when the label starts with "far".
        /// </summary>
        private static int Closeup(string output, string[] args)
        {
            if (args.Length < 8)
            {
                Console.WriteLine("usage: --closeup <id> <k=v;k=v|-> <support> <x,y,z> <half> <label> [view ...]");
                return 1;
            }

            SignChoice choice = SignCatalog.ChoiceById(args[3]);
            if (choice == null)
            {
                Console.WriteLine("FAIL no such entry: " + args[3]);
                return 1;
            }

            GuideSpec spec = choice.Def.Template.Parse(choice.Spec);
            if (args[4] != "-")
            {
                foreach (string pair in args[4].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
                }
            }

            SignInstance instance = SignInstances.Of(choice.Def, spec.ToString());
            string[] supportAndReach = args[5].Split('@');
            SupportDef support = SupportCatalog.ById(supportAndReach[0]);
            float reach = supportAndReach.Length > 1 ? float.Parse(supportAndReach[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
            float tilt = supportAndReach.Length > 2 ? float.Parse(supportAndReach[2], System.Globalization.CultureInfo.InvariantCulture) * (float)(Math.PI / 180.0) : 0f;
            SupportStyle style = supportAndReach.Length > 3 ? SupportStyle.Of(byte.Parse(supportAndReach[3], System.Globalization.CultureInfo.InvariantCulture)) : default;
            float moved = supportAndReach.Length > 4 ? float.Parse(supportAndReach[4], System.Globalization.CultureInfo.InvariantCulture) : 0f;
            string[] at = args[6].Split(',');
            var focus = new Vec3(float.Parse(at[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(at[2], System.Globalization.CultureInfo.InvariantCulture));
            float half = float.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture);
            string label = args[8];
            SignParts parts = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = instance, Tilt = tilt, OffsetX = moved } }, support, reach, label.StartsWith("far", StringComparison.Ordinal) ? SupportDetail.Reduced : SupportDetail.Full, style);
            if (support.Kind == SupportKind.Attached || support.Kind == SupportKind.AttachedArm)
                SignBuilder.AddHostStandIn(parts);
            else if (SupportKinds.OverRoad(support.Kind))
                SignBuilder.AddOverStandIn(parts);
            byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
            var views = new Dictionary<string, Vec3>
            {
                { "front", new Vec3(0f, 0f, -1f) }, { "iso", new Vec3(-0.55f, -0.30f, -0.78f) }, { "back", new Vec3(0.45f, -0.25f, 0.85f) }, { "side", new Vec3(-1f, -0.05f, 0.02f) },
                { "top", new Vec3(0.02f, -1f, -0.05f) }, { "below", new Vec3(0.02f, 1f, -0.05f) }
            };
            for (int i = 9; i < Math.Max(args.Length, 10); i++)
            {
                string name = i < args.Length ? args[i] : "iso";
                Vec3 direction;
                if (!views.TryGetValue(name, out direction))
                {
                    string[] d = name.Split(',');
                    direction = new Vec3(float.Parse(d[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(d[1], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(d[2], System.Globalization.CultureInfo.InvariantCulture));
                    name = "v" + (i - 8);
                }

                byte[] image = Raster.Render(parts, raw, w, h, direction, 900, out int iw, out int ih, focus, half);
                Save(image, iw, ih, Path.Combine(output, $"{label}.{name}.png"));
            }

            var solids = new List<string>();
            for (int c = 0; c < parts.Solids.Length; c++)
            {
                if (parts.Solids[c] != null && parts.Solids[c].VertexCount > 0)
                    solids.Add($"{(SolidColour)c} {parts.Solids[c].VertexCount}");
            }

            Console.WriteLine($"{label}: {args[3]} on {support.Id}, {parts.VertexCount} vertices (face {parts.Face.VertexCount}, metal {parts.Metal.VertexCount}, {string.Join(", ", solids)}), the board's lower edge {parts.Layout.BottomClearance:0.000} m");
            return 0;
        }

        private static int VariantSheet(string output, string id, string overrides, string label)
        {
            SignChoice choice = null;
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.Id == id)
                    choice = c;
            }

            if (choice == null || choice.Def.Template == null)
            {
                Console.WriteLine($"FAIL {id}: no such template entry");
                return 1;
            }

            GuideSpec spec = choice.Def.Template.Parse(choice.Spec);
            foreach (string pair in overrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            SignInstance instance = SignInstances.Of(choice.Def, spec.ToString());
            var paintClock = System.Diagnostics.Stopwatch.StartNew();
            byte[] raw = LedScreens.PaintFace(instance, out int w, out int h);
            long paintMs = paintClock.ElapsedMilliseconds;
            byte[] seen = (byte[])raw.Clone();
            LegendPainter.CutAway(instance, seen, w, h);
            Save(seen, w, h, Path.Combine(output, label + ".png"));
            byte[] view = ViewFromFront(instance, raw, w, h, out int vw, out int vh);
            Save(view, vw, vh, Path.Combine(output, label + ".view.png"));
            Console.WriteLine($"{label}: {id} with {spec}: picture {w}x{h}, seen through the mesh {vw}x{vh}");
            if (instance.IsLed)
            {
                LedGrid grid = LedScreens.Grid(instance);
                Led.SampleSize(grid.DotsX, grid.DotsY, out int sw, out int sh, instance.Board.LedSamples);
                float made = instance.Def.Height / grid.DotsY;
                int perCharacter = instance.Board.H > 0f ? (int)Math.Round(instance.Board.H / made) : 0;
                float percent = 100f * grid.Lit / (grid.DotsX * grid.DotsY);
                var meshClock = System.Diagnostics.Stopwatch.StartNew();
                SignParts timed = SignBuilder.Build(instance, SupportCatalog.ById(SupportCatalog.Cantilever));
                Console.WriteLine($"{label}: first paint and read of the dots {paintMs} ms, then the mesh of the sign on a cantilever {meshClock.ElapsedMilliseconds} ms, {timed.LedVertexCount} dot vertices");
                Console.WriteLine($"{label}: LED {instance.Def.Width:0.00} x {instance.Def.Height:0.00} m, pitch {instance.Board.LedPitch * 1000f:0.0} mm nominal, {made * 1000f:0.0} mm as made ({perCharacter} dots to the character), {grid.DotsX} x {grid.DotsY} = {grid.DotsX * grid.DotsY} dots, {grid.Lit} lit ({percent:0} %), {timed.LedVertexCount} vertices for the lit cells, read from a picture of {sw} x {sh}");
            }

            return 0;
        }

        /// <summary>
        /// Several boards on one support, each with its own picture, on every support from the front, at an angle and from behind: Preview.exe root out --assembly id id ... (ids of panel entries,
        /// in the order they were put on). This is how the stacking and the hanging are looked at.
        /// </summary>
        /// <summary>The things of a toll plaza (0.12.26, TollProps) seen from the front and from an oblique: one picture each, props.NAME.view.png. Usage: Preview.exe root out --props</summary>
        private static int PropSheet(string output)
        {
            var props = new List<(string name, List<MarkingShape> shapes)>
            {
                ("gate-down", TollProps.Gate(4f, false, true)), ("gate-open", TollProps.Gate(4f, true, false)), ("booth", TollProps.Booth(3f, SolidColour.Blue)),
                ("island", TollProps.Island(12f, 2.2f)), ("hump", TollProps.Hump(3.5f, 2))
            };
            foreach ((string name, List<MarkingShape> shapes) in props)
            {
                var bodies = new List<(MeshBuilder Mesh, byte[] Rgb)>();
                foreach (MarkingShape shape in shapes)
                {
                    if (shape.Mesh.VertexCount > 0)
                        bodies.Add((shape.Mesh, Raster.SolidRgb(shape.Solid ?? SolidColour.White)));
                }

                foreach ((string view, Vec3 direction) in new[] { ("front", new Vec3(0f, -0.1f, -1f)), ("iso", new Vec3(-0.6f, -0.45f, 0.65f)), ("side", new Vec3(-1f, -0.1f, 0f)) })
                {
                    byte[] image = Raster.RenderBodies(bodies, direction, 700, out int w, out int h);
                    Save(image, w, h, Path.Combine(output, $"props.{name}.{view}.png"));
                }

                Console.WriteLine($"prop {name}: {bodies.Count} colour(s), {System.Linq.Enumerable.Sum(bodies, b => b.Mesh.VertexCount)} vertices");
            }

            return 0;
        }

        private static int AssemblySheet(string output, List<string> ids)
        {
            var boards = new List<AssemblyBoard>();
            foreach (string asked in ids)
            {
                // "id@6.5": the board is over a lane whose middle is 6.5 m from the first post (the lane is its number among the boards, only for show); "id@post": on the post of a hung support.
                // "...^90" (0.12.8): the board turned 90 degrees on its post (clockwise seen from above). "!..." (0.12.22): on the back of a gantry's beam.
                bool back = asked.StartsWith("!", StringComparison.Ordinal);
                string written = back ? asked.Substring(1) : asked;
                float turn = 0f;
                int caret = written.IndexOf('^');
                if (caret > 0)
                {
                    turn = float.Parse(written.Substring(caret + 1), System.Globalization.CultureInfo.InvariantCulture) * (float)(Math.PI / 180.0);
                    written = written.Substring(0, caret);
                }

                // "id|key=value;key=value" (0.12.80): the entry with these settings changed, for a lit board (lt=i, lt=e).
                string changed = null;
                int bar = written.IndexOf('|');
                if (bar > 0)
                {
                    changed = written.Substring(bar + 1);
                    written = written.Substring(0, bar);
                }

                string id = written;
                int lane = 0;
                float offset = 0f;
                int at = written.IndexOf('@');
                if (at > 0 && written.Substring(at + 1) == "post")
                {
                    id = written.Substring(0, at);
                    lane = Assembly.OnPost;
                }
                else if (at > 0)
                {
                    id = written.Substring(0, at);
                    offset = float.Parse(written.Substring(at + 1), System.Globalization.CultureInfo.InvariantCulture);
                    lane = boards.Count + 1;
                }

                SignChoice found = null;
                foreach (SignChoice choice in SignCatalog.Choices)
                {
                    if (choice.Id == id)
                    {
                        found = choice;
                        break;
                    }
                }

                if (found == null)
                {
                    Console.WriteLine("FAIL no entry " + id);
                    return 1;
                }

                string spec = found.Spec;
                if (changed != null && found.Def.Template != null)
                {
                    GuideSpec edited = found.Def.Template.Parse(found.Spec ?? "");
                    foreach (string pair in changed.Split(';'))
                    {
                        int eq = pair.IndexOf('=');
                        if (eq > 0)
                            edited = edited.With(pair.Substring(0, eq), pair.Substring(eq + 1));
                    }

                    spec = edited.ToString();
                }

                boards.Add(new AssemblyBoard { Instance = SignInstances.Of(found.Def, spec), Slot = boards.Count, Member = boards.Count, Lane = lane, OffsetX = offset, Turn = turn, Back = back });
            }

            var supports = new List<SupportDef> { null };
            foreach (SupportDef support in SupportCatalog.All)
                supports.Add(support);
            foreach (SupportDef support in supports)
            {
                SignParts parts = SignBuilder.BuildAssembly(boards, support, 0f);
                if (support != null && SupportKinds.OnWall(support.Kind))
                    SignBuilder.AddWallStandIn(parts, support);
                else if (support != null && SupportKinds.OverRoad(support.Kind))
                    SignBuilder.AddOverStandIn(parts);
                var pictures = new List<RasterPicture>();
                foreach (FacePart face in parts.Faces)
                {
                    byte[] raw = LedScreens.PaintFace(face.Instance, out int pw, out int ph);
                    pictures.Add(new RasterPicture { Pixels = raw, Width = pw, Height = ph });
                }

                string name = support == null ? "planned" : support.Id;
                foreach ((string view, Vec3 direction) in new[] { ("front", new Vec3(0f, 0f, -1f)), ("iso", new Vec3(-0.55f, -0.30f, -0.78f)), ("back", new Vec3(0.45f, -0.25f, 0.85f)), ("side", new Vec3(-1f, -0.05f, 0f)) })
                {
                    byte[] image = Raster.Render(parts, pictures, direction, view == "front" ? 1000 : 760, out int iw, out int ih);
                    Save(image, iw, ih, Path.Combine(output, $"assembly.{name}.{view}.png"));
                }

                Console.WriteLine($"assembly on {name} ({parts.Layout.Kind}): {parts.Faces.Count} boards, {parts.VertexCount} vertices, post {parts.Layout.PoleRadius * 2000f:0} mm, lower edge {parts.Layout.BottomClearance:0.0} m");
            }

            return 0;
        }

        /// <summary>
        /// The glyph sheet: every glyph of Geo/Signs/Glyphs*.cs (or the ones named) drawn as the signs draw them, at 200 px, in black on white and in white on blue, with its name, as one
        /// PNG per sheet of 24 (glyphs-1.png, ...). This is how a glyph is looked at while it is drawn. Usage: Preview.exe root out --glyphs [name ...]
        /// </summary>
        private static int GlyphSheet(string output, HashSet<string> names)
        {
            var glyphs = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> g in CDLC.TS.Geo.Signs.Glyphs.All())
            {
                if (names.Count == 0 || names.Contains(g.Key))
                    glyphs.Add(g);
            }

            Console.WriteLine(glyphs.Count + " glyph(s)");
            const int cell = 200, label = 26, gap = 10, columns = 6, perSheet = 24;
            int sheetNo = 0;
            for (int start = 0; start < glyphs.Count; start += perSheet)
            {
                int count = Math.Min(perSheet, glyphs.Count - start);
                int rows = (count + columns - 1) / columns;
                int width = columns * (cell * 2 + gap) + gap, height = rows * (cell + label + gap) + gap;
                using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
                using (Graphics g = Graphics.FromImage(bitmap))
                using (var labelFont = new Font("Arial", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.FromArgb(58, 66, 78));
                    for (int n = 0; n < count; n++)
                    {
                        KeyValuePair<string, string> glyph = glyphs[start + n];
                        int ox = gap + (n % columns) * (cell * 2 + gap), oy = gap + (n / columns) * (cell + label + gap);
                        for (int variant = 0; variant < 2; variant++)
                        {
                            bool onBlue = variant == 1;
                            // A board of 1 x 1 with the glyph over all of it, painted by the same painter as the signs (a square picture of 200 px).
                            var part = new GuidePart { X = 0f, Y = 0f, W = 1f, H = 1f, Shape = GuidePartShape.RoundRect, Ground = onBlue ? GuideColour.Blue : GuideColour.White, Radius = 0f };
                            part.Elements.Add(GuideKit.Picture(glyph.Value, 0f, 0f, 1f, 1f, onBlue ? GuideColour.White : GuideColour.Black));
                            var board = new GuideBoard { H = 0.1f };
                            board.Parts.Add(part);
                            var instance = new SignInstance { Board = board, BoundsX0 = 0f, BoundsY0 = 0f, BoundsX1 = 1f, BoundsY1 = 1f, TexW = cell, TexH = cell };
                            using (var picture = new Bitmap(cell, cell, PixelFormat.Format32bppArgb))
                            using (Graphics pg = Graphics.FromImage(picture))
                            {
                                CDLC.TS.Render.GuidePainter.Draw(pg, LegendPainter.Fonts(), instance, cell, cell);
                                g.DrawImage(picture, ox + variant * cell, oy);
                            }
                        }

                        g.DrawString(glyph.Key, labelFont, Brushes.White, ox + 2f, oy + cell + 4f);
                    }

                    sheetNo++;
                    string file = Path.Combine(output, $"glyphs-{sheetNo}.png");
                    bitmap.Save(file, ImageFormat.Png);
                    Console.WriteLine("wrote " + file);
                }
            }

            return 0;
        }

        private static void WriteSheet(List<byte[]> sheet, string path, int columns)
        {
            int s = Thumbnails.Size, gap = 12;
            int rows = (sheet.Count + columns - 1) / columns;
            int sw2 = columns * (s + gap) + gap, sh2 = rows * (s + gap) + gap;
            var image = new byte[sw2 * sh2 * 4];
            for (int i = 0; i < sw2 * sh2; i++)
            {
                image[i * 4] = 58; image[i * 4 + 1] = 66; image[i * 4 + 2] = 78; image[i * 4 + 3] = 255;
            }

            for (int n = 0; n < sheet.Count; n++)
            {
                int ox = gap + (n % columns) * (s + gap), oy = gap + (n / columns) * (s + gap);
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        int source = (y * s + x) * 4;
                        int target = ((y + oy) * sw2 + ox + x) * 4;
                        float a = sheet[n][source + 3] / 255f;
                        for (int k = 0; k < 3; k++)
                            image[target + k] = (byte)(image[target + k] * (1f - a) + sheet[n][source + k] * a);
                    }
                }
            }

            Save(image, sw2, sh2, path);
        }

        /// <summary>Saves RGBA bytes whose first row is the bottom one.</summary>
        private static void Save(byte[] raw, int w, int h, string path)
        {
            using (var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int i = ((h - 1 - y) * w + x) * 4;
                        bitmap.SetPixel(x, y, Color.FromArgb(raw[i + 3], raw[i], raw[i + 1], raw[i + 2]));
                    }
                }

                bitmap.Save(path, ImageFormat.Png);
            }
        }

        /// <summary>
        /// The face mesh seen by a viewer in front of the sign: the viewer looks along -Z (the sign faces +Z), Y is up, and in Unity's left-handed frame
        /// the viewer's right is -X. Orthographic, 400 pixels to the metre; each pixel samples the picture at the interpolated picture coordinates. Back-facing
        /// triangles (by the winding the game culls on) are skipped, so a face that were wound the wrong way would show up as an empty image.
        /// </summary>
        private static byte[] ViewFromFront(SignInstance instance, byte[] picture, int pw, int ph, out int w, out int h)
        {
            const float scale = 400f;
            MeshBuilder face = SignBuilder.Build(instance, null).Face;
            face.Bounds(out Vec3 min, out Vec3 max);
            float left = -max.X, right = -min.X;   // screen x = -X
            w = (int)Math.Ceiling((right - left) * scale) + 2;
            h = (int)Math.Ceiling((max.Y - min.Y) * scale) + 2;
            var view = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                // A grey background so that the board's edge is visible.
                view[i * 4] = 70; view[i * 4 + 1] = 78; view[i * 4 + 2] = 86; view[i * 4 + 3] = 255;
            }

            for (int t = 0; t < face.TriangleCount; t++)
            {
                int ia = face.Indices[t * 3], ib = face.Indices[t * 3 + 1], ic = face.Indices[t * 3 + 2];
                // Screen space: x to the viewer's right, y up (row 0 at the bottom).
                float ax = (-face.Positions[ia].X - left) * scale + 1, ay = (face.Positions[ia].Y - min.Y) * scale + 1;
                float bx = (-face.Positions[ib].X - left) * scale + 1, by = (face.Positions[ib].Y - min.Y) * scale + 1;
                float cx = (-face.Positions[ic].X - left) * scale + 1, cy = (face.Positions[ic].Y - min.Y) * scale + 1;

                // Unity front faces are clockwise as seen from the front; in a screen with y up that is a negative signed area.
                float area = (bx - ax) * (cy - ay) - (cx - ax) * (by - ay);
                if (area >= 0f)
                    continue;

                int x0 = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
                int x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
                int y0 = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
                int y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((bx - px) * (cy - py) - (cx - px) * (by - py)) / area;
                        float w1 = ((cx - px) * (ay - py) - (ax - px) * (cy - py)) / area;
                        float w2 = 1f - w0 - w1;
                        if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f)
                            continue;

                        float u = w0 * face.U[ia] + w1 * face.U[ib] + w2 * face.U[ic];
                        float v = w0 * face.V[ia] + w1 * face.V[ib] + w2 * face.V[ic];
                        int sx = Math.Min(pw - 1, Math.Max(0, (int)(u * (pw - 1) + 0.5f)));
                        int sy = Math.Min(ph - 1, Math.Max(0, (int)(v * (ph - 1) + 0.5f)));
                        int s = (sy * pw + sx) * 4;
                        int d = (y * w + x) * 4;
                        view[d] = picture[s]; view[d + 1] = picture[s + 1]; view[d + 2] = picture[s + 2]; view[d + 3] = 255;
                    }
                }
            }

            return view;
        }
    }
}
