using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Markings in a tile (ChunkBuilder), what can be pointed at on them, their picture from above (MarkingRaster) and their absence from the map's list of signs.</summary>
    internal static class MarkingBuildTests
    {
        public static void RunAll()
        {
            Program.Run("marking tiles: the paint is built at the item's place, turned and lifted", InTile);
            Program.Run("marking tiles: signs and markings together, hidden and unknown items", Mixed);
            Program.Run("marking tiles: pointing at a marking", Pointing);
            Program.Run("marking picture: the paint seen from above", Picture);
            Program.Run("marking map: markings are not signs for MiniMap", Markers);
        }

        private static ItemRecord Marking(string id, Vec3 position, float yaw, string spec = null)
        {
            SignDef def = SignCatalog.ById(id);
            Program.Check(def != null && def.IsMarking, id + " is a marking of the catalogue");
            return new ItemRecord(def.Hash, position, yaw, 0u, spec ?? def.Template.Default().ToString());
        }

        private static void Extents(MeshBuilder mesh, out float x0, out float x1, out float y0, out float y1, out float z0, out float z1)
        {
            x0 = y0 = z0 = float.MaxValue;
            x1 = y1 = z1 = float.MinValue;
            foreach (Vec3 p in mesh.Positions)
            {
                x0 = Math.Min(x0, p.X);
                x1 = Math.Max(x1, p.X);
                y0 = Math.Min(y0, p.Y);
                y1 = Math.Max(y1, p.Y);
                z0 = Math.Min(z0, p.Z);
                z1 = Math.Max(z1, p.Z);
            }
        }

        private static ChunkPart Part(List<ChunkPart> parts, string material)
        {
            foreach (ChunkPart p in parts)
            {
                if (p.Material == material)
                    return p;
            }

            return null;
        }

        private static void InTile()
        {
            var origin = new Vec3(960f, 0f, 1920f);
            // A stop line of 8 m, 30 cm wide, at (1000, 5, 2000) facing north (yaw 0): the paint is 8 m across X, 30 cm along Z, at height 5.03.
            SignDef stopDef = SignCatalog.ById("mk.stop");
            string spec = stopDef.Template.Default().With("span", "8").With("width", "30").ToString();
            List<ChunkPart> parts = ChunkBuilder.Build(new[] { Marking("mk.stop", new Vec3(1000f, 5f, 2000f), 0f, spec) }, origin, out int skipped);
            Program.Check(skipped == 0 && parts.Count == 1 && parts[0].Material == ChunkBuilder.MarkingMaterial(MarkingColour.White), "one white part: " + parts.Count + " / " + (parts.Count > 0 ? parts[0].Material : ""));
            Extents(parts[0].Mesh, out float x0, out float x1, out float y0, out float y1, out float z0, out float z1);
            Program.Near(x0, 40f - 4f, 1e-3f, "from 4 m left of the item (relative to the tile's origin)");
            Program.Near(x1, 40f + 4f, 1e-3f, "to 4 m right of it");
            Program.Near(z0, 80f - 0.15f, 1e-3f, "30 cm along the way");
            Program.Near(z1, 80f + 0.15f, 1e-3f, "30 cm along the way");
            Program.Near(y0, 5f + ChunkBuilder.MarkingRaise, 1e-4f, "lifted 3 cm above the item");
            Program.Near(y1, 5f + ChunkBuilder.MarkingRaise, 1e-4f, "flat");
            for (int i = 0; i + 2 < parts[0].Mesh.Indices.Count; i += 3)
            {
                Vec3 a = parts[0].Mesh.Positions[parts[0].Mesh.Indices[i]], b = parts[0].Mesh.Positions[parts[0].Mesh.Indices[i + 1]], c = parts[0].Mesh.Positions[parts[0].Mesh.Indices[i + 2]];
                Program.Check(MeshBuilder.Cross(b - a, c - a).Y > 0f, "a triangle of the paint faces up");
            }

            // Turned a quarter turn (yaw 90 degrees: the way the traffic runs is +X): the line now runs along Z and is 30 cm along X.
            List<ChunkPart> turned = ChunkBuilder.Build(new[] { Marking("mk.stop", new Vec3(1000f, 5f, 2000f), (float)Math.PI / 2f, spec) }, origin, out _);
            Extents(turned[0].Mesh, out float tx0, out float tx1, out _, out _, out float tz0, out float tz1);
            Program.Near(tx1 - tx0, 0.3f, 1e-3f, "turned: 30 cm along X");
            Program.Near(tz1 - tz0, 8f, 1e-3f, "turned: 8 m along Z");
            // The way the traffic runs maps to (sin yaw, cos yaw): a crosswalk's far edge (local +Z) lies toward +X at yaw 90.
            GuideTemplate cross = MarkingTemplates.ById("mk.crosswalk");
            MarkingPlan plan = MarkingTemplates.PlanOf(cross, cross.Default().ToString());
            List<ChunkPart> walk = ChunkBuilder.Build(new[] { Marking("mk.crosswalk", new Vec3(1000f, 0f, 2000f), (float)Math.PI / 2f) }, origin, out _);
            Extents(walk[0].Mesh, out float wx0, out float wx1, out _, out _, out _, out _);
            Program.Near(wx1 - 40f, plan.MaxZ, 1e-3f, "the far edge of the crosswalk is toward +X at yaw 90: " + (wx1 - 40f));
            Program.Near(wx0 - 40f, plan.MinZ, 1e-3f, "and the near edge toward -X");

            // Two colours: a double yellow line and a white lane line make two parts.
            List<ChunkPart> both = ChunkBuilder.Build(new[] { Marking("mk.line.doubleyellow", new Vec3(1000f, 0f, 2000f), 0f), Marking("mk.line.whitesolid", new Vec3(1010f, 0f, 2000f), 0f) }, origin, out _);
            Program.Check(both.Count == 2 && Part(both, "marking:0") != null && Part(both, "marking:1") != null, "white and yellow are separate parts");

            // Many markings pass the vertex limit of a mesh: the part is cut, and every item is there.
            var many = new List<ItemRecord>();
            for (int i = 0; i < 30; i++)
                many.Add(Marking("mk.crosswalk", new Vec3(1000f + i * 3f, 0f, 2000f), 0f));
            int total = 0;
            List<ChunkPart> manyParts = ChunkBuilder.Build(many, origin, out int skippedMany, 200);
            foreach (ChunkPart p in manyParts)
            {
                Program.Check(p.Mesh.VertexCount <= 200 + 100, "a cut part stays near the limit");
                total += p.Mesh.VertexCount;
            }

            Program.Check(skippedMany == 0 && total == 30 * ChunkBuilder.Build(new[] { many[0] }, origin, out _)[0].Mesh.VertexCount, "all thirty are there: " + total);

            // A grade: 5 % uphill lifts the far end.
            GuideTemplate stop = MarkingTemplates.ById("mk.crosswalk");
            string graded = MarkingTemplates.WithGrade(stop, stop.Default().ToString(), 0.05f);
            List<ChunkPart> hill = ChunkBuilder.Build(new[] { Marking("mk.crosswalk", new Vec3(1000f, 10f, 2000f), 0f, graded) }, origin, out _);
            Extents(hill[0].Mesh, out _, out _, out float hy0, out float hy1, out _, out _);
            Program.Near(hy1, 10f + ChunkBuilder.MarkingRaise + 0.05f * plan.MaxZ, 1e-3f, "the far end of the crosswalk is higher by the grade: " + hy1);
            Program.Near(hy0, 10f + ChunkBuilder.MarkingRaise + 0.05f * plan.MinZ, 1e-3f, "and the near end lower");
        }

        private static void Mixed()
        {
            var origin = new Vec3(960f, 0f, 1920f);
            SignDef sign = SignCatalog.ById("gb.prohib.09");
            var records = new List<ItemRecord>
            {
                new ItemRecord(sign.Hash, new Vec3(1000f, 0f, 2000f), 0f),
                Marking("mk.stop", new Vec3(1005f, 0f, 2000f), 0f),
                Marking("mk.arrow.straight", new Vec3(1010f, 0f, 2000f), 0f)
            };
            List<ChunkPart> parts = ChunkBuilder.Build(records, origin, out int skipped);
            Program.Check(skipped == 0, "nothing skipped");
            Program.Check(Part(parts, ChunkBuilder.MetalMaterial) != null && Part(parts, "marking:0") != null, "the sign's metal and the marking's paint, in separate parts");
            // The sign alone builds the same parts as with the markings beside it.
            List<ChunkPart> alone = ChunkBuilder.Build(new[] { records[0] }, origin, out _);
            Program.Check(alone.Count == parts.Count - 1, "markings add one part (the paint), the sign's parts are the same: " + alone.Count + " / " + parts.Count);
            Program.Check(Part(alone, "marking:0") == null, "the sign alone has no paint");

            // Hidden: not drawn and not skipped. Unknown type: skipped. Not a number: skipped.
            ItemRecord hidden = records[1];
            hidden.Hidden = true;
            Program.Check(ChunkBuilder.Build(new[] { hidden }, origin, out int skippedHidden).Count == 0 && skippedHidden == 0, "hidden: not drawn, not skipped");
            ItemRecord bad = Marking("mk.stop", new Vec3(float.NaN, 0f, 0f), 0f);
            ChunkBuilder.Build(new[] { bad }, origin, out int skippedBad);
            Program.Check(skippedBad == 1, "a position that is not a number is skipped");
            ItemRecord badYaw = Marking("mk.stop", new Vec3(1000f, 0f, 2000f), float.PositiveInfinity);
            ChunkBuilder.Build(new[] { badYaw }, origin, out int skippedYaw);
            Program.Check(skippedYaw == 1, "a yaw that is not a number is skipped");
            // Damaged settings still draw (the defaults take over).
            ItemRecord junk = Marking("mk.stop", new Vec3(1000f, 0f, 2000f), 0f, "span=zzz;width=999;;;=");
            Program.Check(ChunkBuilder.Build(new[] { junk }, origin, out int skippedJunk).Count == 1 && skippedJunk == 0, "damaged settings: the defaults are drawn");
            // A marking item that is given a group or a lane (it never is) is still paint.
            var odd = new ItemRecord(SignCatalog.ById("mk.stop").Hash, new Vec3(1000f, 0f, 2000f), 0f, 0u, null, 0f, 77u, 1, 2, 3f);
            Program.Check(ChunkBuilder.Build(new[] { odd }, origin, out _).Count == 1, "a group or lane on a marking is ignored");
        }

        private static void Pointing()
        {
            // A crosswalk 12 m across, 4 m along, at (100, 0, 200) facing north: a ray from above through its middle hits; a ray 3 m beside it misses.
            ItemRecord walk = Marking("mk.crosswalk", new Vec3(100f, 0f, 200f), 0f);
            SignParts parts = ChunkBuilder.PartsOfRecord(walk);
            Program.Check(parts.Picks.Count > 0 && parts.Picks.Count <= 120, "pick shapes: " + parts.Picks.Count);
            bool hitStripe = Picking.Pick(parts.Picks, walk.Position, walk.Yaw, new Vec3(100.2f, 5f, 200f), new Vec3(100.2f, -5f, 200f), out float distance);
            var boxes = new System.Text.StringBuilder();
            foreach (PickShape ps in parts.Picks)
                boxes.Append(ps.Min + ".." + ps.Max + " ");
            Program.Check(hitStripe && distance > 4f && distance < 6f, "a ray down through a stripe hits it: " + hitStripe + " " + distance + " in " + boxes);
            Program.Check(!Picking.Pick(parts.Picks, walk.Position, walk.Yaw, new Vec3(120f, 5f, 200f), new Vec3(120f, -5f, 200f), out _), "a ray 20 m beside it misses");
            Program.Check(!Picking.Pick(parts.Picks, walk.Position, walk.Yaw, new Vec3(100f, 5f, 210f), new Vec3(100f, -5f, 210f), out _), "a ray 10 m ahead misses");

            // A bent run of 60 m: the empty road inside the bend is not picked, the paint is.
            GuideTemplate lane = MarkingTemplates.ById("mk.line.whitesolid");
            var arc = new List<Vec3>();
            for (int i = 0; i <= 40; i++)
                arc.Add(new Vec3((float)(40.0 * (1.0 - Math.Cos(i * 1.5 / 40.0 * 1.0))), 0f, (float)(40.0 * Math.Sin(i * 1.5 / 40.0 * 1.0))));
            string path = RunPath.Encode(arc, out float yaw);
            string spec = MarkingTemplates.WithPath(lane, lane.Default().ToString(), path);
            ItemRecord run = new ItemRecord(lane.Hash, new Vec3(0f, 0f, 0f), yaw, 0u, spec);
            SignParts runParts = ChunkBuilder.PartsOfRecord(run);
            Program.Check(runParts.Picks.Count > 1, "a run has several pick boxes: " + runParts.Picks.Count);
            // A point on the line (the 12th sample, in the world) is hit; the middle of the chord between the ends (far from the line) is not.
            Vec3 on = arc[12];
            Program.Check(Picking.Pick(runParts.Picks, run.Position, run.Yaw, new Vec3(on.X, 5f, on.Z), new Vec3(on.X, -5f, on.Z), out _), "a point of the run is hit");
            Vec3 mid = (arc[0] + arc[40]) * 0.5f;
            Program.Check(!Picking.Pick(runParts.Picks, run.Position, run.Yaw, new Vec3(mid.X, 5f, mid.Z), new Vec3(mid.X, -5f, mid.Z), out _), "the empty road inside the bend is not");
        }

        private static void Picture()
        {
            SignDef stop = SignCatalog.ById("mk.stop");
            MarkingPlan plan = MarkingTemplates.PlanOf(stop, stop.Template.Default().ToString());
            byte[] raw = MarkingRaster.Render(plan, 160, out int w, out int h);
            Program.Check(w == 160 && h == 160 && raw.Length == 160 * 160 * 4, "160 x 160 RGBA");
            int Pixel(int x, int y, int channel) => raw[(y * w + x) * 4 + channel];
            Program.Check(Pixel(80, 80, 0) > 220 && Pixel(80, 80, 1) > 220, "the middle is white paint");
            Program.Check(Pixel(80, 20, 0) == 0x4a && Pixel(80, 140, 0) == 0x4a, "above and below it is the road");
            Program.Check(Pixel(80, 80, 3) == 255 && Pixel(2, 2, 3) == 255, "opaque");
            // A yellow marking is yellow (more red and green than blue).
            SignDef yellow = SignCatalog.ById("mk.line.yellowsingle");
            byte[] y = MarkingRaster.Render(MarkingTemplates.PlanOf(yellow, yellow.Template.Default().ToString()), 160, out _, out _);
            bool any = false;
            for (int i = 0; i < 160 * 160; i++)
                any |= y[i * 4] > 200 && y[i * 4 + 1] > 150 && y[i * 4 + 2] < 80;
            Program.Check(any, "yellow paint is yellow");
            // No plan, no paint: the plain road.
            byte[] none = MarkingRaster.Render(null, 32, out _, out _);
            Program.Check(none[0] == 0x4a && none[none.Length - 1] == 255, "no plan: the road");
            // Every marking draws something in its picture (not blank), and the picture is a valid PNG of the size.
            int blank = 0;
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                MarkingPlan p = MarkingTemplates.PlanOf(t, t.Default().ToString());
                byte[] pixels = MarkingRaster.Render(p, 96, out _, out _);
                // Something other than the road (a dark railing too).
                bool paint = false;
                for (int i = 0; i < 96 * 96 && !paint; i++)
                    paint = Math.Abs(pixels[i * 4] - pixels[0]) + Math.Abs(pixels[i * 4 + 1] - pixels[1]) + Math.Abs(pixels[i * 4 + 2] - pixels[2]) > 24;
                if (!paint)
                    blank++;
                byte[] png = MarkingRaster.Png(p, 64);
                Program.Check(Png.TryDecode(png, out byte[] back, out int bw, out int bh) && bw == 64 && bh == 64 && back.Length == 64 * 64 * 4, t.Id + ": the thumbnail is a PNG of 64 x 64");
            }

            Program.Check(blank == 0, "every marking's picture has paint: " + blank + " blank");
            // The picture starts at its top and the traffic runs up it: the head of the direction arrow (its widest row) is in the upper half, and the PNG, read the way it was written, is the picture.
            SignDef arrowDef = SignCatalog.ById("mk.sym.arrow");
            MarkingPlan arrowPlan = MarkingTemplates.PlanOf(arrowDef, arrowDef.Template.Default().ToString());
            byte[] arrowRaw = MarkingRaster.Render(arrowPlan, 200, 400, out int aw, out int ah, 0.05f);
            int widest = 0, widestRow = -1;
            for (int row = 0; row < ah; row++)
            {
                int paint = 0;
                for (int col = 0; col < aw; col++)
                    paint += arrowRaw[(row * aw + col) * 4] > 0xc0 ? 1 : 0;
                if (paint > widest)
                {
                    widest = paint;
                    widestRow = row;
                }
            }

            Program.Check(widestRow >= 0 && widestRow < ah / 2, "the head of the arrow (the way the traffic runs) is in the upper half of the picture: row " + widestRow + " of " + ah);
            byte[] arrowPng = MarkingRaster.Png(arrowPlan, 128);
            byte[] sameRaw = MarkingRaster.Render(arrowPlan, 128, out int sw, out int sh);
            Program.Check(Png.TryDecode(arrowPng, out byte[] arrowBack, out int abw, out int abh, false) && abw == sw && abh == sh && arrowBack.Length == sameRaw.Length, "the thumbnail decodes to the picture's size");
            bool identical = arrowBack != null && arrowBack.Length == sameRaw.Length;
            for (int i = 0; identical && i < sameRaw.Length; i++)
                identical = arrowBack[i] == sameRaw[i];
            Program.Check(identical, "and, read with its first row at the top, to the picture itself: the thumbnail is not upside down");

            // Thin lines survive the scaling: a 12 m yellow line 15 cm wide fitted into 160 pixels is still seen.
            SignDef line = SignCatalog.ById("mk.line.whitesolid");
            byte[] thin = MarkingRaster.Render(MarkingTemplates.PlanOf(line, line.Template.Default().ToString()), 160, out _, out _);
            int paintPixels = 0;
            for (int i = 0; i < 160 * 160; i++)
                paintPixels += thin[i * 4] > 0x70 ? 1 : 0;
            Program.Check(paintPixels > 40, "a thin line is not lost in the picture: " + paintPixels);
        }

        private static void Markers()
        {
            SignDef sign = SignCatalog.ById("gb.prohib.09");
            var records = new List<ItemRecord>
            {
                new ItemRecord(sign.Hash, new Vec3(1000f, 0f, 2000f), 0f),
                Marking("mk.stop", new Vec3(1005f, 0f, 2000f), 0f),
                Marking("mk.arrow.straight", new Vec3(1010f, 0f, 2000f), 0f)
            };
            List<TsMarker> markers = TsMarkers.Build(records);
            Program.Check(markers.Count == 1, "one marker, the sign's: " + markers.Count);
            Program.Check(TsMarkers.Build(new[] { records[1], records[2] }).Count == 0, "markings alone make no markers");
        }
    }
}
