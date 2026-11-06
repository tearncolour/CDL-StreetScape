using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>The city route boards of GB 5768.2 that 0.12.1 completes: the pointed board of 路7 a (Fig. 212 a), the upright road name (8.3.4.3) and a pictogram after a place (Fig. 207 b).</summary>
    internal static class CityBoardTests
    {
        public static void RunAll()
        {
            Program.Run("city boards: the pointed outline of 路7 a", Pointed);
            Program.Run("city boards: 路7 a is pointed, its chevron inside", NameDirectionPointed);
            Program.Run("city boards: the upright road name", Upright);
            Program.Run("city boards: a pictogram after a place", Pictograms);
        }

        private static void Pointed()
        {
            float w = 4f, h = 2f, point = 0.64f, r = 0.16f;
            List<Point2> outer = GuideKit.PointedOutline(w, h, point, r, 0f, true);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (Point2 p in outer)
            {
                minX = Math.Min(minX, p.X);
                maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y);
                maxY = Math.Max(maxY, p.Y);
            }

            Program.Check(minX >= -w * 0.5f - 1e-4f && maxX <= w * 0.5f + 1e-4f && minY >= -h * 0.5f - 1e-4f && maxY <= h * 0.5f + 1e-4f, "inside its box");
            Program.Check(maxX > w * 0.5f - 0.05f && minX < -w * 0.5f + 1e-3f, "reaching to the tip and the back: " + minX + ".." + maxX);
            Program.Check(Area(outer) > 0f, "counter-clockwise");
            Program.Check(Convex(outer), "convex");
            // The tip is on the middle line, where the outline is widest.
            Point2 tip = outer[0];
            foreach (Point2 p in outer)
            {
                if (p.X > tip.X)
                    tip = p;
            }

            Program.Check(Math.Abs(tip.Y) < 0.02f, "the tip on the middle: " + tip.Y);
            // Less the point's two corners than a rectangle: its area is the rectangle's less the two triangles cut off.
            float sharp = w * h - point * h * 0.5f;
            Program.Check(Area(outer) < sharp && Area(outer) > sharp - 0.1f, "the area of the point: " + Area(outer) + " of " + sharp);
            // Moved in: inside the outline, smaller each time.
            List<Point2> ring = GuideKit.PointedOutline(w, h, point, r, 0.1f, true), inner = GuideKit.PointedOutline(w, h, point, r, 0.2f, true);
            Program.Check(Area(ring) < Area(outer) && Area(inner) < Area(ring) && Convex(ring) && Convex(inner), "the liner and the border inside it, smaller each");
            bool inside = true;
            foreach (Point2 p in inner)
                inside &= Contains(outer, p.X, p.Y);
            Program.Check(inside, "the inside within the outline");
            // Pointed left: the mirror.
            List<Point2> left = GuideKit.PointedOutline(w, h, point, r, 0f, false);
            float leftMin = float.MaxValue;
            foreach (Point2 p in left)
                leftMin = Math.Min(leftMin, p.X);
            Program.Check(Area(left) > 0f && Math.Abs(Area(left) - Area(outer)) < 1e-3f && Math.Abs(leftMin + maxX) < 1e-3f, "pointed left: the mirror, counter-clockwise too");
            // The outline of a part with the shape is this.
            var part = new GuidePart { W = w, H = h, Side = point, Radius = r, Shape = GuidePartShape.PointRight };
            Program.Check(GuideKit.PartOutline(part).Count == outer.Count, "a part's outline");
            // Fillet: a square's corners rounded by 0.5 leave a circle's area of a unit square... a square of 2 with radius 1 is a circle of radius 1.
            List<Point2> circle = GuideKit.Fillet(new List<Point2> { new Point2(-1f, -1f), new Point2(1f, -1f), new Point2(1f, 1f), new Point2(-1f, 1f) }, new float[] { 1f, 1f, 1f, 1f });
            Program.Near(Area(circle), (float)Math.PI, 0.03f, "a square of 2 rounded by 1 is a circle");
            Program.Check(GuideKit.Fillet(new List<Point2> { new Point2(0f, 0f), new Point2(1f, 0f), new Point2(0f, 1f) }, new float[] { 0f, 0f, 0f }).Count == 3, "radius 0 leaves the corners");
            List<Point2> moved = GuideKit.InsetConvex(new List<Point2> { new Point2(-1f, -1f), new Point2(1f, -1f), new Point2(1f, 1f), new Point2(-1f, 1f) }, 0.25f);
            Program.Near(Area(moved), 2.25f, 1e-4f, "a square of 2 moved in by 0.25 is a square of 1.5");
        }

        private static void NameDirectionPointed()
        {
            SignDef def = SignCatalog.ById("gb.guide8.07");
            GuideTemplate t = def.Template;
            SignInstance right = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("form", "r").With("name", "福宁路").ToString());
            SignInstance left = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("form", "l").With("name", "福宁路").ToString());
            GuidePart pr = right.Board.Main, pl = left.Board.Main;
            Program.Check(pr.Shape == GuidePartShape.PointRight && pl.Shape == GuidePartShape.PointLeft, "the board itself is pointed, to the right or to the left");
            float h = right.Board.H;
            Program.Near(pr.H, 2.5f * h, 1e-4f, "2.5 h high (E.173 a)");
            Program.Near(pr.Side, RoadGuideSigns.PointSlope * pr.H * 0.5f, 1e-4f, "the point's length from its slope");
            // The chevron: white, inside the inside of the board, near the tip.
            GuideElement chevron = pr.Elements.Find(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            Program.Check(chevron != null, "a white chevron");
            List<Point2> inner = GuideKit.PointedOutline(pr.W, pr.H, pr.Side, pr.Radius, pr.Edge, true);
            bool inside = true;
            float chevMin = float.MaxValue;
            for (int i = 0; i + 1 < chevron.Points.Length; i += 2)
            {
                float x = chevron.Points[i] - pr.W * 0.5f, y = pr.H * 0.5f - chevron.Points[i + 1];
                inside &= Contains(inner, x, y) || DistanceToOutline(inner, x, y) < 1e-3f;
                chevMin = Math.Min(chevMin, chevron.Points[i]);
            }

            Program.Check(inside, "inside the border");
            // The name before the chevron: the text's right end 0.3 h before the chevron starts... at least before it.
            float textRight = 0f;
            foreach (GuideElement e in pr.Elements)
            {
                if (e.Kind == GuideElementKind.Text)
                    textRight = Math.Max(textRight, e.X + e.W);
            }

            Program.Check(textRight > 0f && textRight <= chevMin - 0.25f * h, "the name ends before the chevron: " + textRight + " < " + chevMin);
            // Pointed left: the chevron on the left, the name after it.
            GuideElement chevronL = pl.Elements.Find(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            float chevLMax = float.MinValue, textLeft = float.MaxValue;
            for (int i = 0; i + 1 < chevronL.Points.Length; i += 2)
                chevLMax = Math.Max(chevLMax, chevronL.Points[i]);
            foreach (GuideElement e in pl.Elements)
            {
                if (e.Kind == GuideElementKind.Text)
                    textLeft = Math.Min(textLeft, e.X);
            }

            Program.Check(chevLMax < pl.W * 0.5f && textLeft >= chevLMax + 0.25f * h, "to the left: the chevron at the left, the name after it");
            Program.Check(SignInstances.Of(def, t.Parse(t.Default().ToString()).With("form", "b").ToString()).Board.Main.Shape == GuidePartShape.RoundRect, "the other forms stay rectangles");
        }

        private static void Upright()
        {
            SignDef def = SignCatalog.ById("gb.guide8.06");
            GuideTemplate t = def.Template;
            SignInstance across = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("name", "西二旗北路").ToString());
            SignInstance up = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("name", "西二旗北路").With("layout", "v").ToString());
            float h = up.Board.H;
            GuidePart pa = across.Board.Main, pu = up.Board.Main;
            Program.Check(pa.W > pa.H && pu.H > pu.W, "across is wide, upright is tall");
            Program.Near(pu.W, 2f * h, 1e-4f, "upright: 2 h wide (E.172 turned)");
            Program.Near(pu.H, 5f * h + 4f * 0.2f * h + 0.6f * h + 0.4f * h, 1e-3f, "five characters, four gaps of 0.2 h, 0.3 h top and bottom, the edges");
            // The characters one under the other, centred across.
            var tops = new List<float>();
            foreach (GuideElement e in pu.Elements)
            {
                if (e.Kind == GuideElementKind.Text)
                {
                    tops.Add(e.Y);
                    Program.Near(e.X + e.W * 0.5f, pu.W * 0.5f, 1e-3f, "each character centred across");
                }
            }

            Program.Check(tops.Count == 5, "five characters: " + tops.Count);
            bool down = true;
            for (int i = 1; i < tops.Count; i++)
                down &= Math.Abs(tops[i] - tops[i - 1] - 1.2f * h) < 1e-3f;
            Program.Check(down, "from the top down, 1.2 h apart");
            Program.Check(SignInstances.Of(def, t.Parse(t.Default().ToString()).With("name", "").With("layout", "v").ToString()).Board.Main.H > 0f, "an empty name still makes a board");
        }

        private static void Pictograms()
        {
            float h = 0.4f;
            GuideBlock plain = RoadGuideSigns.Info("学府路", h, GuideColour.Blue);
            GuideBlock tagged = RoadGuideSigns.Info("学府路[地铁]", h, GuideColour.Blue);
            Program.Near(tagged.W, plain.W + RoadGuideSigns.SymbolGapK * h + h, 1e-4f, "the pictogram after the place: 0.3 h then a square of h");
            Program.Check(tagged.Elements.Exists(e => e.Kind == GuideElementKind.Path && e.Colour == GuideColour.Blue) && tagged.Elements.Exists(e => e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.White),
                "a white square with the figure in the board's colour");
            Program.Near(RoadGuideSigns.Info("学府路【地铁】", h, GuideColour.Blue).W, tagged.W, 1e-4f, "full-width brackets too");
            Program.Near(RoadGuideSigns.Info("学府路［机场］", h, GuideColour.Blue).W, tagged.W, 1e-4f, "and the airport");
            Program.Near(RoadGuideSigns.Info("[停车]", h, GuideColour.Blue).W, h, 1e-4f, "a pictogram alone");
            Program.Check(RoadGuideSigns.Info("学府路[博物馆]", h, GuideColour.Blue).W > plain.W && !RoadGuideSigns.Info("学府路[博物馆]", h, GuideColour.Blue).Elements.Exists(e => e.Kind == GuideElementKind.Path),
                "an unknown name in brackets stays text");
            foreach (string name in RoadGuideSigns.SymbolNames)
                Program.Check(RoadGuideSigns.SymbolGlyph(name) != null, name + " has a figure");
            Program.Check(RoadGuideSigns.SymbolGlyph("") == null && RoadGuideSigns.SymbolGlyph(null) == null, "nothing has none");
            // On a stacked board the row is as wide as the place with its pictogram.
            SignDef def = SignCatalog.ById("gb.guide8.03");
            GuideTemplate t = def.Template;
            float wPlain = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("to1", "学府路").ToString()).Board.Main.W;
            float wTag = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("to1", "学府路[地铁]").ToString()).Board.Main.W;
            Program.Check(wTag > wPlain, "the stacked board grows for the pictogram: " + wPlain + " -> " + wTag);
            Program.Check(t.Parse(t.Default().ToString()).With("to1", "学府路[地铁]").Get("to1") == "学府路[地铁]", "the field holds the place with its pictogram");
        }

        private static float Area(List<Point2> p)
        {
            double s = 0;
            for (int i = 0; i < p.Count; i++)
            {
                Point2 a = p[i], b = p[(i + 1) % p.Count];
                s += a.X * b.Y - b.X * a.Y;
            }

            return (float)(s * 0.5);
        }

        private static bool Convex(List<Point2> p)
        {
            for (int i = 0; i < p.Count; i++)
            {
                Point2 a = p[i], b = p[(i + 1) % p.Count], c = p[(i + 2) % p.Count];
                if ((b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X) < -1e-6f)
                    return false;
            }

            return true;
        }

        private static bool Contains(List<Point2> p, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
            {
                if ((p[i].Y > y) != (p[j].Y > y) && x < (p[j].X - p[i].X) * (y - p[i].Y) / (p[j].Y - p[i].Y) + p[i].X)
                    inside = !inside;
            }

            return inside;
        }

        private static float DistanceToOutline(List<Point2> p, float x, float y)
        {
            float best = float.MaxValue;
            for (int i = 0; i < p.Count; i++)
            {
                Point2 a = p[i], b = p[(i + 1) % p.Count];
                float dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy;
                float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, ((x - a.X) * dx + (y - a.Y) * dy) / l2));
                float ex = a.X + dx * t - x, ey = a.Y + dy * t - y;
                best = Math.Min(best, (float)Math.Sqrt(ex * ex + ey * ey));
            }

            return best;
        }
    }
}
