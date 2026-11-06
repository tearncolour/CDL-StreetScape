using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The area markings fitted to the road (0.12.4): the middle line the points of the Vs lie on (the owner: "引导线的尖的连线位置应该是能智能识别中线位置"); the paint cut to where the road can be painted and
    /// laid on its surface (the owner: "根据护栏等等抛去无效区域，然后填充导流线，且需要贴合地面").
    /// </summary>
    internal static class AreaFitTests
    {
        public static void RunAll()
        {
            Program.Run("area fit: the middle line of an uneven gore and of a lopsided wedge", MiddleLines);
            Program.Run("area fit: which pieces of a cross-section are painted", Sections);
            Program.Run("area fit: the paint cut to the road and laid on it", Drapes);
            Program.Run("area fit: a gore stops at the barrier's nose", GoreAtBarrier);
            Program.Run("area fit: the settings, and the box grid drawn as an area", Settings);
            Program.Run("area fit: a six-road junction is cut to the same pieces, and its cost", Junction);
        }

        private static SectionPiece Piece(float x, float w, float top, bool surface = true, bool blocks = false, bool lanes = false, bool layer = false, bool other = false) =>
            new SectionPiece { X = x, Width = w, Top = top, Surface = surface, BlocksTraffic = blocks, RoadLanes = lanes, Layer = layer, OtherLevel = other, Name = "p" };

        private static void Sections()
        {
            // A one-way motorway 12 m wide: a barrier 0.6 m at the left, a shoulder 1.4 m, three lanes of 3.5 m, a shoulder of 0.5 m; and a raised verge piece at the right.
            var pieces = new List<SectionPiece>
            {
                Piece(-5.7f, 0.6f, 0.9f, true, true),
                Piece(-4.7f, 1.4f, 0f),
                Piece(-2.25f, 3.5f, 0f, lanes: true),
                Piece(1.25f, 3.5f, 0f, lanes: true),
                Piece(4.75f, 3.5f, 0f, lanes: true),
                Piece(6.75f, 0.5f, 0.2f)
            };
            var log = new List<string>();
            List<PaintableStretch> s = SurfaceSection.Paintable(pieces, 14f, log);
            Program.Check(s.Count == 1, "the shoulder and the lanes are one stretch: " + s.Count);
            Program.Near(s[0].F0, (-5.4f + 7f) / 14f, 1e-4f, "from the barrier's inner face");
            Program.Near(s[0].F1, (6.5f + 7f) / 14f, 1e-4f, "to the raised verge");
            Program.Check(log.Count == 6 && log[0].Contains("stops traffic") && log[5].Contains("from the carriageway's height"), "the log says why each piece was left: " + string.Join(" | ", log));
            // A raised median between two carriageways: two stretches.
            var divided = new List<SectionPiece> { Piece(-5f, 7f, 0f, lanes: true), Piece(0f, 3f, 0.15f, true, true), Piece(5f, 7f, 0f, lanes: true) };
            Program.Check(SurfaceSection.Paintable(divided, 17f).Count == 2, "a median parts the road in two");
            // A deck's side faces, another level and a piece that is not a surface are never painted; a junction's composition without lanes takes its lowest surface.
            Program.Check(SurfaceSection.Paintable(new List<SectionPiece> { Piece(0f, 4f, 0f, layer: true), Piece(0f, 4f, 0f, other: true), Piece(0f, 4f, 0f, false) }, 4f).Count == 0, "faces, other levels, no surface");
            Program.Check(SurfaceSection.Paintable(new List<SectionPiece> { Piece(-2f, 4f, 0f), Piece(2f, 4f, 0f) }, 8f).Count == 1, "a junction without lanes: its surface");
            Program.Check(SurfaceSection.Paintable(null, 4f).Count == 0 && SurfaceSection.Paintable(pieces, 0f).Count == 0, "nothing for nothing");
            // A band along a segment, the halves of a bent junction part.
            var band = SurfaceSection.Bands(t => P(-5f, t * 10f), t => P(5f, t * 10f), new PaintableStretch(0.2f, 0.8f, 0.01f), 4, "test");
            Program.Check(band.Count == 1 && band[0].Left.Count == 5 && Math.Abs(band[0].Left[0].X + 3f) < 1e-4f && Math.Abs(band[0].Right[4].X - 3f) < 1e-4f && Math.Abs(band[0].Left[2].Y - 0.01f) < 1e-5f, "a band from 0.2 to 0.8 of the width, on the piece's height");
            // A junction's part whose middle bends is two pieces one after the other (SurfaceReader gives each its own sides): each is a band across its whole width.
            var first = SurfaceSection.Bands(t => P(-5f, t * 5f), t => P(5f, t * 5f), new PaintableStretch(0.1f, 0.9f, 0f), 2, "test");
            var second = SurfaceSection.Bands(t => P(-5f + t, 5f + t * 5f), t => P(5f + t, 5f + t * 5f), new PaintableStretch(0.1f, 0.9f, 0f), 2, "test");
            Program.Check(first.Count == 1 && second.Count == 1 && Math.Abs(first[0].Left[2].Z - 5f) < 1e-4f && Math.Abs(second[0].Left[0].Z - 5f) < 1e-4f && Math.Abs(second[0].Right[2].X - 5f) < 1e-4f,
                "the two pieces of a bent junction part follow one another, each across its whole width");

            // 0.12.10: an ordinary junction part is two halves across, split at its middle. A stretch from 0.038 to 0.962 (shoulders and lanes of a 13 m highway) is 0.077..1 of the left half and 0..0.923
            // of the right; a stretch on one side is in that half only; a middle offset moves the split.
            List<(bool Right, PaintableStretch Local)> both = SurfaceSection.SplitAcross(new PaintableStretch(0.038f, 0.962f, -0.2f), 0.5f);
            Program.Check(both.Count == 2 && !both[0].Right && both[1].Right, "a stretch over the middle is in both halves");
            Program.Near(both[0].Local.F0, 0.076f, 1e-4f, "the left half's from its left side");
            Program.Near(both[0].Local.F1, 1f, 1e-5f, "to the middle");
            Program.Near(both[1].Local.F0, 0f, 1e-5f, "the right half's from the middle");
            Program.Near(both[1].Local.F1, 0.924f, 1e-4f, "to near its right side");
            Program.Check(both[0].Local.Y == -0.2f && both[1].Local.Y == -0.2f, "at the stretch's height");
            List<(bool Right, PaintableStretch Local)> leftOnly = SurfaceSection.SplitAcross(new PaintableStretch(0.1f, 0.4f, 0f), 0.5f);
            Program.Check(leftOnly.Count == 1 && !leftOnly[0].Right && Math.Abs(leftOnly[0].Local.F0 - 0.2f) < 1e-5f && Math.Abs(leftOnly[0].Local.F1 - 0.8f) < 1e-5f, "a stretch on the left is in the left half only");
            List<(bool Right, PaintableStretch Local)> offset = SurfaceSection.SplitAcross(new PaintableStretch(0f, 1f, 0f), 0.6f);
            Program.Check(offset.Count == 2 && Math.Abs(offset[0].Local.F1 - 1f) < 1e-5f && Math.Abs(offset[1].Local.F0) < 1e-5f, "a middle off the centre: the halves meet there");
            Program.Check(SurfaceSection.SplitAcross(new PaintableStretch(0.5f, 0.5f, 0f), 0.5f).Count == 0 && SurfaceSection.SplitAcross(new PaintableStretch(0f, 1f, 0f), float.NaN).Count == 2, "nothing for no width; no number is the middle");

            // The halves fan out to the junction's middle: a part 10 m wide that runs 10 m from the segment's end, its middle line bending 6 m off to a junction's middle (as at a merge), its sides
            // straight. The two halves cover the corner between the sides' ends and that middle, which a band from side to side leaves out.
            Func<float, Vec3> outerL = t => P(-5f, t * 10f), outerR = t => P(5f, t * 10f), middle = t => P(6f * t * t, t * 10f + 4f * t * t);
            var fan = new List<SurfaceStrip>();
            fan.AddRange(SurfaceSection.Bands(outerL, middle, new PaintableStretch(0f, 1f, 0f), 16, "test"));
            fan.AddRange(SurfaceSection.Bands(middle, outerR, new PaintableStretch(0f, 1f, 0f), 16, "test"));
            var straight = SurfaceSection.Bands(outerL, outerR, new PaintableStretch(0f, 1f, 0f), 16, "test");
            AreaFit byHalves = AreaFit.FromStrips(fan, Vec3.Zero, 0f, -6f, 8f, 8f, 14f), byBand = AreaFit.FromStrips(straight, Vec3.Zero, 0f, -6f, 8f, 8f, 14f);
            bool inHalves = byHalves.HeightAt(5f, 12.5f, out _), inBand = byBand.HeightAt(5f, 12.5f, out _);
            Program.Check(inHalves && !inBand, "the corner up to the junction's middle is road when read by halves (" + inHalves + "), not by one band (" + inBand + ")");
        }

        private static SurfaceStrip Strip(float x0, float x1, float z0, float z1, Func<float, float, float> y, int n = 10)
        {
            var strip = new SurfaceStrip();
            for (int i = 0; i <= n; i++)
            {
                float z = z0 + (z1 - z0) * i / n;
                strip.Add(P(x0, z, y(x0, z)), P(x1, z, y(x1, z)));
            }

            return strip;
        }

        private static MeshBuilder Filled(List<Vec3> polygon)
        {
            var mesh = new MeshBuilder();
            Polygon.Fill(mesh, polygon);
            return mesh;
        }

        private static void Drapes()
        {
            // A road 10 m wide along +Z rising 5 %; an area from x = -5 to 10 (half of it off the road), z 0 to 40.
            List<SurfaceStrip> road = new List<SurfaceStrip> { Strip(-5f, 5f, -10f, 60f, (x, z) => 0.05f * z) };
            AreaFit fit = AreaFit.FromStrips(road, Vec3.Zero, 0f, -5f, 10f, 0f, 40f);
            Program.Check(fit.OnRoad && fit.PieceCount > 0, "the road's cells near the area are kept: " + fit.PieceCount);
            MeshBuilder draped = fit.Drape(Filled(MarkingFills.Rectangle(-5f, 0f, 10f, 40f)));
            Program.Near(AreaFitRules.Area(draped), 400f, 0.5f, "only the part on the road is painted: 10 x 40");
            float worst = 0f, maxX = float.MinValue;
            foreach (Vec3 p in draped.Positions)
            {
                worst = Math.Max(worst, Math.Abs(p.Y - 0.05f * p.Z));
                maxX = Math.Max(maxX, p.X);
            }

            Program.Check(worst < 1e-3f, "and it lies on the rising road: " + worst);
            Program.Check(maxX <= 5f + 1e-3f, "nothing beyond the road's edge: " + maxX);
            foreach (Vec3 n in draped.Normals)
                Program.Check(n.Y > 0.99f && n.Z < 0f, "the normals are the slope's");

            // 0.12.12: two pieces of one surface that meet at a crease (a road rising 4 % meets a junction's part rising 1 %, at z = 20, the junction's part laid 1 cm higher): the paint has no step at the
            // seam (each side was laid on its own plane, the lower side under the road) and is nowhere below either surface it lies on.
            var crease = new List<SurfaceStrip> { Strip(-5f, 5f, 0f, 20f, (x, z) => 0.04f * z), Strip(-5f, 5f, 20f, 40f, (x, z) => 0.81f + 0.01f * (z - 20f)) };
            MeshBuilder overCrease = AreaFit.FromStrips(crease, Vec3.Zero, 0f, -5f, 5f, 0f, 40f).Drape(Filled(MarkingFills.Rectangle(-4f, 2f, 4f, 38f)));
            float step = 0f, below = 0f;
            for (int i = 0; i < overCrease.VertexCount; i++)
            {
                Vec3 p = overCrease.Positions[i];
                float surface = p.Z <= 20f ? 0.04f * p.Z : 0.81f + 0.01f * (p.Z - 20f);
                below = Math.Max(below, surface - p.Y);
                for (int j = i + 1; j < overCrease.VertexCount; j++)
                {
                    Vec3 q = overCrease.Positions[j];
                    if (Math.Abs(p.X - q.X) < 1e-3f && Math.Abs(p.Z - q.Z) < 1e-3f)
                        step = Math.Max(step, Math.Abs(p.Y - q.Y));
                }
            }

            Program.Check(step < 1e-3f, "no step where two pieces meet: " + step);
            Program.Check(below < 1e-3f, "and the paint is not under the surface: " + below);

            // 0.12.15: a filler band (the gore between a junction part's halves, its surface guessed) keeps only what the real ones leave: where a real band 5 cm higher lies over it, the paint is on the real one.
            var real = Strip(-5f, 0f, 0f, 20f, (x, z) => 0.05f);
            var guess = Strip(-5f, 5f, 0f, 20f, (x, z) => 0.04f);
            guess.Filler = true;
            AreaFit layered = AreaFit.FromStrips(new List<SurfaceStrip> { guess, real }, Vec3.Zero, 0f, -5f, 5f, 0f, 20f);
            bool onReal = layered.HeightAt(-2f, 10f, out float realY), onGuess = layered.HeightAt(2f, 10f, out float guessY);
            Program.Check(onReal && Math.Abs(realY - 0.05f) < 1e-4f, "under a real band the real surface: " + realY);
            Program.Check(onGuess && Math.Abs(guessY - 0.04f) < 1e-4f, "beside it the filler: " + guessY);
            Program.Near(AreaFitRules.Area(layered.Drape(Filled(MarkingFills.Rectangle(-5f, 0f, 5f, 20f)))), 200f, 0.5f, "painted once over both");

            // 0.12.18: the gore between a part's halves laid as the game lays it, on the junction's middle line between the two halves' lines: where the middle line bows out (here 3 m beyond the straight
            // line from one half's line to the other), the two bands cover the asphalt that one band straight across leaves out.
            Func<float, Vec3> leftLine = t => P(-2f, t * 10f), rightLine = t => P(2f, t * 10f), middleLine = t => P(0f, t * 10f + 3f * (float)Math.Sin(Math.PI * t));
            var twoBands = new List<SurfaceStrip>();
            twoBands.AddRange(SurfaceSection.Bands(leftLine, middleLine, new PaintableStretch(0f, 1f, 0f), 16, "test"));
            twoBands.AddRange(SurfaceSection.Bands(middleLine, rightLine, new PaintableStretch(0f, 1f, 0f), 16, "test"));
            var oneBand = SurfaceSection.Bands(leftLine, rightLine, new PaintableStretch(0f, 1f, 0f), 16, "test");
            bool inTwo = AreaFit.FromStrips(twoBands, Vec3.Zero, 0f, -3f, 3f, 0f, 14f).HeightAt(0f, 7.5f, out _), inOne = AreaFit.FromStrips(oneBand, Vec3.Zero, 0f, -3f, 3f, 0f, 14f).HeightAt(0f, 10.5f, out _);
            Program.Check(inTwo, "the gore laid on the middle line covers where it bows out");
            Program.Check(!inOne, "(one band straight across does not reach there)");

            // Two bands that overlap by 2 m are painted once.
            var overlapping = new List<SurfaceStrip> { Strip(-5f, 1f, 0f, 20f, (x, z) => 0f), Strip(-1f, 5f, 0f, 20f, (x, z) => 0f) };
            AreaFit both = AreaFit.FromStrips(overlapping, Vec3.Zero, 0f, -5f, 5f, 0f, 20f);
            Program.Near(AreaFitRules.Area(both.Drape(Filled(MarkingFills.Rectangle(-5f, 0f, 5f, 20f)))), 200f, 0.5f, "two bands that overlap: the paint once, 10 x 20");

            // A bridge 8 m above crosses the road: the area on the road keeps the road's height.
            var crossing = new List<SurfaceStrip> { Strip(-5f, 5f, -10f, 30f, (x, z) => 0f), Strip(-20f, 20f, 8f, 14f, (x, z) => 8f) };
            AreaFit under = AreaFit.FromStrips(crossing, Vec3.Zero, 0f, -5f, 5f, 0f, 20f);
            MeshBuilder underPaint = under.Drape(Filled(MarkingFills.Rectangle(-4f, 0f, 4f, 20f)));
            float highest = float.MinValue;
            foreach (Vec3 p in underPaint.Positions)
                highest = Math.Max(highest, p.Y);
            Program.Check(highest < 0.01f, "under a bridge the paint stays on the road it was put on: " + highest);
            Program.Near(AreaFitRules.Area(underPaint), 160f, 0.5f, "all of it");
            // A deck far above with nothing below is not a surface for this area.
            Program.Check(AreaFit.FromStrips(new List<SurfaceStrip> { Strip(-5f, 5f, 0f, 20f, (x, z) => 20f) }, Vec3.Zero, 0f, -5f, 5f, 0f, 20f).PieceCount == 0, "a deck 20 m above is left out");

            // The item's frame: an item at (100, 10, 50) turned a quarter: the fit is in its frame, heights relative to its own.
            var east = new List<SurfaceStrip> { Strip(90f, 110f, 40f, 60f, (x, z) => 10.5f) };
            AreaFit turned = AreaFit.FromStrips(east, P(100f, 50f, 10f), (float)(Math.PI / 2), -5f, 5f, -5f, 5f);
            bool atTurned = turned.HeightAt(0f, 0f, out float y);
            Program.Check(atTurned && Math.Abs(y - 0.5f) < 1e-4f, "heights relative to the item: " + y);

            // The ground: an area on no road follows the terrain.
            AreaFit ground = AreaFit.FromGround((x, z) => 0.1f * x, -10f, 10f, 0f, 10f);
            bool atGround = ground.HeightAt(4f, 3f, out float g);
            Program.Check(!ground.OnRoad && atGround && Math.Abs(g - 0.4f) < 1e-3f, "the ground's height: " + g);
            Program.Near(AreaFitRules.Area(ground.Drape(Filled(MarkingFills.Rectangle(-10f, 0f, 10f, 10f)))), 200f, 0.5f, "an area on the ground is painted whole");

            // Subtracting a convex piece leaves pieces that do not overlap and cover the rest.
            var square = new List<Vec3> { P(0f, 0f), P(4f, 0f), P(4f, 4f), P(0f, 4f) };
            var parts = new List<List<Vec3>>();
            AreaFit.Subtract(square, new[] { P(1f, 1f), P(3f, 1f), P(3f, 3f), P(1f, 3f) }, parts);
            float rest = 0f;
            foreach (List<Vec3> part in parts)
                rest += Math.Abs(Polygon.SignedArea2(part)) * 0.5f;
            Program.Near(rest, 12f, 1e-3f, "a square less a square in it: 16 - 4");
            parts.Clear();
            AreaFit.Subtract(square, new[] { P(5f, 5f), P(6f, 5f), P(6f, 6f) }, parts);
            Program.Check(parts.Count == 1 && parts[0].Count == 4, "a piece that does not meet it leaves it whole");
        }

        private static void GoreAtBarrier()
        {
            // An exit's gore 60 m long, 8 m wide at its far end: the junction's pavement reaches 20 m from the nose, then the two roads part with barriers and grass between them; each road's surface is
            // outside the gore's sides. The paint of the gore (its lines and Vs) stops at 20 m.
            var main = new List<Vec3>();
            var ramp = new List<Vec3>();
            for (int i = 0; i <= 12; i++)
            {
                main.Add(P(0f, i * 5f));
                ramp.Add(P(i * 5f * 8f / 60f, i * 5f));
            }

            MarkingShape gore = MarkingAreas.Gore(main, ramp, true);
            Program.Check(gore.Mesh.TriangleCount > 50, "the gore has its paint: " + gore.Mesh.TriangleCount);
            var surface = new List<SurfaceStrip>
            {
                Strip(-6f, 12f, -10f, 20f, (x, z) => 0.02f * z),
                Strip(-12f, -0.6f, 20f, 80f, (x, z) => 0.02f * z),
                Strip(9f, 20f, 20f, 80f, (x, z) => 0.02f * z)
            };
            AreaFit fit = AreaFit.FromStrips(surface, Vec3.Zero, 0f, -1f, 9f, 0f, 60f);
            MeshBuilder draped = fit.Drape(gore.Mesh);
            float far = float.MinValue, off = 0f;
            foreach (Vec3 p in draped.Positions)
            {
                far = Math.Max(far, p.Z);
                off = Math.Max(off, Math.Abs(p.Y - 0.02f * p.Z));
            }

            Program.Check(draped.TriangleCount > 10, "the part before the barrier is painted: " + draped.TriangleCount);
            Program.Check(far <= 20f + 1e-3f, "and nothing past the barrier's nose at 20 m: " + far);
            Program.Check(off < 1e-3f, "on the road's surface: " + off);
            // Where the grass lies between the roads the area is not covered: a cover of about the share the junction's part has.
            float whole = AreaFitRules.Area(Filled(MarkingAreas.GorePolygon(main, ramp))), covered = AreaFitRules.Area(fit.Drape(Filled(MarkingAreas.GorePolygon(main, ramp))));
            Program.Near(covered / whole, (20f * 20f * 8f / 60f * 0.5f) / (60f * 8f * 0.5f), 0.01f, "the road covers the first 20 m of it");
        }

        private static void Settings()
        {
            foreach (string id in new[] { MarkingTemplates.ChannelId, MarkingTemplates.GridId, MarkingTemplates.SimpleGridId, MarkingTemplates.GoreId })
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def != null && MarkingTemplates.IsFittedArea(id) && MarkingTemplates.WantsFit(def, ""), id + ": fitted to the road by default (an item of 0.12.3 too)");
                Program.Check(def != null && !MarkingTemplates.WantsFit(def, def.Template.Parse("").With(MarkingTemplates.AreaFitKey, "0").ToString()), id + ": unless it is turned off");
                Program.Check(def != null && AreaFitRules.IsAreaHash(def.Hash), id + ": the renderer knows it by its hash");
            }

            SignDef stop = SignCatalog.ById("mk.stop");
            Program.Check(stop != null && !MarkingTemplates.WantsFit(stop, "") && !AreaFitRules.IsAreaHash(stop.Hash), "a stop line is not an area");
            Program.Check(MarkingTemplates.IsDrawnArea(MarkingTemplates.GridId) && MarkingTemplates.IsDrawnArea(MarkingTemplates.ChannelId) && !MarkingTemplates.IsDrawnArea(MarkingTemplates.GoreId), "the grid and the island are drawn corner by corner");

            // The box grid on an L-shaped outline: the outline and the grid lines inside it, none outside.
            SignDef grid = SignCatalog.ById(MarkingTemplates.GridId);
            var l = new List<Vec3> { P(0f, 0f), P(12f, 0f), P(12f, 4f), P(4f, 4f), P(4f, 10f), P(0f, 10f) };
            string spec = MarkingTemplates.WithPolygon(grid.Template, "", MarkingPolygon.Encode(l));
            MarkingPlan plan = MarkingTemplates.PlanOf(grid, spec, false);
            Program.Check(plan != null && plan.Parts.Count > 0 && plan.Parts[0].Mesh.TriangleCount > 40, "an L-shaped box grid has its paint");
            bool inside = true;
            foreach (Vec3 p in plan.Parts[0].Mesh.Positions)
                inside &= p.X >= -1e-3f && p.Z >= -1e-3f && p.X <= 12.001f && p.Z <= 10.001f && !(p.X > 4.01f && p.Z > 4.01f);
            Program.Check(inside, "all of it inside the L");
            Program.Check(plan.Parts[0].Colour == MarkingColour.White, "white (6.8.2 of the 2025 edition)");
            Program.Check(grid.Template.FieldOf("width").Visible(grid.Template.Parse(spec)) == false, "a drawn grid has no width or length to set");
        }

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        /// <summary>
        /// Six roads of three bands each (a carriageway and two shoulders) meeting in one point, as the surface reader gives a junction: the bands lie over each other near the middle, and every band is
        /// made of many quads. The cut pieces are summed up (their number, area and planes) so that a change to how they are found shows when it changes them; the time of the fit is written out.
        /// </summary>
        private static void Junction()
        {
            var strips = new List<SurfaceStrip>();
            for (int road = 0; road < 6; road++)
            {
                float angle = road * (float)Math.PI / 3f + 0.07f * road;
                float dx = (float)Math.Cos(angle), dz = (float)Math.Sin(angle), rx = -dz, rz = dx;
                for (int band = 0; band < 3; band++)
                {
                    float across0 = band == 0 ? -7f : band == 1 ? -2.3f : 2.3f, across1 = band == 0 ? -2.3f : band == 1 ? 2.3f : 7f;
                    var strip = new SurfaceStrip { Elevated = false };
                    for (int i = 0; i <= 60; i++)
                    {
                        float along = 1.5f * i;
                        float y = 0.02f * along + (band == 1 ? 0f : -0.05f);
                        strip.Add(P(dx * along + rx * across0, dz * along + rz * across0, y), P(dx * along + rx * across1, dz * along + rz * across1, y));
                    }

                    strips.Add(strip);
                }
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            AreaFit fit = AreaFit.FromStrips(strips, Vec3.Zero, 0f, -100f, 100f, -100f, 100f);
            double ms = clock.Elapsed.TotalMilliseconds;
            double area = 0.0, planes = 0.0;
            foreach (Vec3[] piece in fit.Pieces)
                area += Math.Abs(Polygon.SignedArea2(piece)) * 0.5;
            for (int i = 0; i < 400; i++)
            {
                float x = -60f + 0.3f * i, z = 20f - 0.17f * i;
                if (fit.HeightAt(x, z, out float y))
                    planes += y;
            }

            Console.WriteLine($"  area fit: a six-road junction of {strips.Count} bands: {fit.PieceCount} pieces, area {area:0.000} m2, heights {planes:0.0000}, made in {ms:0.0} ms");
            Program.Check(fit.PieceCount > 0 && area > 1000.0, "the junction is cut to pieces that cover the roads");
        }

        /// <summary>The distance on the ground from a point to a line of points.</summary>
        internal static float DistanceTo(IList<Vec3> line, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], b = line[i + 1];
                float dx = b.X - a.X, dz = b.Z - a.Z, l2 = dx * dx + dz * dz;
                float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / l2));
                best = Math.Min(best, Vec3.DistanceXZ(p, Vec3.Lerp(a, b, t)));
            }

            return best;
        }

        private static void MiddleLines()
        {
            // A gore whose ramp side bends away and is drawn with points close together near the nose and far apart beyond, the main side straight with points evenly apart: at the same share of
            // each side's length the two are not across from each other.
            var main = new List<Vec3>();
            for (int i = 0; i <= 12; i++)
                main.Add(P(0f, i * 7f));
            var ramp = new List<Vec3> { P(0f, 0f), P(0.05f, 2f), P(0.2f, 4f), P(0.45f, 6f), P(0.8f, 8f), P(1.8f, 12f), P(4.5f, 20f), P(9f, 30f), P(16f, 42f), P(25f, 55f) };
            List<Vec3> axis = MarkingAreas.MedialAxis(main, ramp);
            float worst = 0f, worstOld = 0f;
            int counted = 0;
            foreach (Vec3 p in axis)
            {
                if (p.Z < 3f || p.Z > 50f)
                    continue;
                counted++;
                worst = Math.Max(worst, Math.Abs(DistanceTo(main, p) - DistanceTo(ramp, p)));
            }

            foreach (Vec3 p in MarkingAreas.MidAxis(main, ramp))
            {
                if (p.Z >= 3f && p.Z <= 50f)
                    worstOld = Math.Max(worstOld, Math.Abs(DistanceTo(main, p) - DistanceTo(ramp, p)));
            }

            Program.Check(counted > 40, "the middle line runs the gore's length a point every half metre: " + counted);
            Program.Check(worst < 0.01f, $"each point is as far from one side as from the other (worst {worst:0.000} m)");
            Program.Check(worstOld > 0.5f, $"(0.12.3's line at the same share of each side's length was {worstOld:0.00} m off: the test can tell)");
            for (int i = 1; i < axis.Count; i++)
                Program.Check(Vec3.Dot(axis[i] - axis[i - 1], (axis[Math.Min(axis.Count - 1, i + 1)] - axis[i - 1]).HeadingXZ()) > 0f, "it goes forward");

            // A lopsided wedge drawn by hand: the tip at the back, one long side nearly straight and the other leaning out: the marched axis stays between them, as far from each.
            var wedge = new List<Vec3> { P(0f, 0f), P(7f, 30f), P(-1.5f, 31f) };
            List<Vec3> marched = MarkingAreas.MarchingAxis(wedge);
            var sideA = new List<Vec3> { wedge[0], wedge[1] };
            var sideB = new List<Vec3> { wedge[0], wedge[2] };
            float wedgeWorst = 0f;
            int wedgeCounted = 0;
            foreach (Vec3 p in marched)
            {
                if (p.Z < 2f || p.Z > 24f)
                    continue;
                wedgeCounted++;
                wedgeWorst = Math.Max(wedgeWorst, Math.Abs(DistanceTo(sideA, p) - DistanceTo(sideB, p)));
            }

            Program.Check(wedgeCounted > 30, "the wedge's axis runs most of its length: " + wedgeCounted);
            Program.Check(wedgeWorst < 0.08f, $"and keeps to its middle line (worst {wedgeWorst:0.000} m)");

            // The band round a bend keeps to its middle as before (MarkingAreaTests); a symmetric gore's middle line is its axis of symmetry.
            var left = new List<Vec3> { P(0f, 0f), P(-1f, 10f), P(-3f, 20f), P(-6f, 30f) };
            var right = new List<Vec3> { P(0f, 0f), P(1f, 10f), P(3f, 20f), P(6f, 30f) };
            foreach (Vec3 p in MarkingAreas.MedialAxis(left, right))
                Program.Near(p.X, 0f, 0.01f, "a symmetric gore's middle line is its axis");
        }
    }
}
