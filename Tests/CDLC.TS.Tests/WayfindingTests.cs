using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 行人和地铁导向牌 (the owner's list): the metro board outside a station (Shanghai draft 9.1.1: 1640 × 420, lower edge 2.5 m, the pieces of fig. 20, the lines' colours of fig. A.1) and the
    /// pedestrian strip (Hangzhou 2015 guide figs. B.2, B.3: 700 × 180 mm each side of the post, lower edge 1.9 m).
    /// </summary>
    internal static class WayfindingTests
    {
        public static void RunAll()
        {
            Program.Run("wayfinding: the catalogue and the sizes", Catalogue);
            Program.Run("wayfinding: the metro board's pieces (fig. 20) and the line colours (fig. A.1)", Metro);
            Program.Run("wayfinding: the pedestrian strip's two plates", Strip);
            Program.Run("wayfinding: on the plate post's stack and a post of its own", Hung);
        }

        private static GuidePart Main(string id, params (string Key, string Value)[] settings)
        {
            SignDef def = SignCatalog.ById(id);
            GuideSpec s = def.Template.Parse(def.Template.Default().ToString());
            foreach ((string key, string value) in settings)
                s = s.With(key, value);
            return SignInstances.Of(def, s.ToString()).Board.Main;
        }

        private static bool Inside(GuidePart p, out string worst)
        {
            worst = "";
            foreach (GuideElement e in p.Elements)
            {
                if (e.X < -1e-4f || e.Y < -1e-4f || e.Right > p.W + 1e-4f || e.Bottom > p.H + 1e-4f)
                {
                    worst = $"{e.Kind} '{e.Text}' at {e.X:0.000},{e.Y:0.000} {e.W:0.000}x{e.H:0.000} on {p.W:0.000}x{p.H:0.000}";
                    return false;
                }
            }

            return true;
        }

        private static void Catalogue()
        {
            SignDef metro = SignCatalog.ById(WayfindingSigns.MetroId), ped = SignCatalog.ById(WayfindingSigns.PedId);
            Program.Check(metro != null && ped != null && metro.Group == "wayfinding" && ped.Group == "wayfinding" && metro.Template.Standalone && ped.Template.Standalone, "two standalone boards of their own group");
            Program.Check(Math.Abs(metro.Width - 1.64f) < 1e-4f && Math.Abs(metro.Height - 0.42f) < 1e-4f && Math.Abs(metro.BottomClearance - 2.5f) < 1e-4f, $"the metro board 1640 × 420, its lower edge at 2.5 m ({metro.Width} × {metro.Height}, {metro.BottomClearance})");
            Program.Check(Math.Abs(ped.Width - 1.4f) < 1e-4f && Math.Abs(ped.Height - 0.18f) < 1e-4f && Math.Abs(ped.BottomClearance - 1.9f) < 1e-4f, $"the strip 2 × 700 by 180, at 1.9 m ({ped.Width} × {ped.Height}, {ped.BottomClearance})");
            Program.Check(!string.IsNullOrEmpty(metro.Zh) && !string.IsNullOrEmpty(ped.En) && SignCatalog.ById("gb17733.street").BottomClearance == SupportCatalog.RoadsideClearance, "named; the street plate keeps the roadside's clearance");
        }

        private static List<GuideElement> Rects(GuidePart p, GuideColour colour) => p.Elements.FindAll(e => e.Kind == GuideElementKind.Rect && e.Colour == colour);

        private static void Metro()
        {
            GuidePart p = Main(WayfindingSigns.MetroId);
            bool onBoard = Inside(p, out string worst);
            Program.Check(p.Ground == GuideColour.Blue && onBoard, "blue, everything on the board: " + worst);
            List<GuideElement> whites = Rects(p, GuideColour.White);
            GuideElement sign = whites.Find(e => Math.Abs(e.W - WayfindingSigns.SignSide) < 1e-4f);
            Program.Check(sign != null && Math.Abs(sign.H - WayfindingSigns.SignSide) < 1e-4f, "the metro sign a white square of 328 mm");
            GuideElement number = Rects(p, WayfindingSigns.LineColour(12)).Find(e => Math.Abs(e.W - WayfindingSigns.NumberW) < 1e-4f);
            Program.Check(number != null && Math.Abs(number.H - WayfindingSigns.NumberH) < 1e-4f, "the line's box 195 × 178 on line 12's colour");
            GuideElement arrow = p.Elements.Find(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            Program.Check(arrow != null && Math.Abs(arrow.W - WayfindingSigns.ArrowW) < 1e-4f && Math.Abs(arrow.H - WayfindingSigns.ArrowH) < 1e-4f && arrow.X < sign.X, $"the arrow 154 × 332 at the left, before the sign ({arrow?.X:0.000} / {sign.X:0.000})");
            GuideElement words = p.Elements.Find(e => e.Kind == GuideElementKind.Text && e.Text == "号线");
            Program.Check(words != null && words.X > number.Right && Math.Abs(words.H - WayfindingSigns.CharH) < 1e-4f, "号线 after the number, 104 mm");
            Program.Check(p.Elements.Exists(e => e.Kind == GuideElementKind.Text && e.Text == "Line 12") || p.Elements.Exists(e => e.Kind == GuideElementKind.Text && (e.Text ?? "").StartsWith("Line", StringComparison.Ordinal)), "Line 12 under it");
            Program.Check(p.Elements.Exists(e => e.Kind == GuideElementKind.Text && e.Text == "50"), "the distance 50 m by default");
            Program.Check(Rects(p, GuideColour.White).Count == 2, "the sign and the distance's box are white");

            // Pointing right: the order is turned about (fig. 20, the lower board).
            GuidePart right = Main(WayfindingSigns.MetroId, ("side", "right"), ("line", "13"), ("dist", "100"));
            GuideElement rsign = Rects(right, GuideColour.White).Find(e => Math.Abs(e.W - WayfindingSigns.SignSide) < 1e-4f);
            GuideElement rnumber = Rects(right, WayfindingSigns.LineColour(13)).Find(e => Math.Abs(e.W - WayfindingSigns.NumberW) < 1e-4f);
            GuideElement rarrow = right.Elements.Find(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            Program.Check(rnumber != null && rnumber.Right < rsign.X && rsign.Right < rarrow.X && Math.Abs(rarrow.Right - (right.W - WayfindingSigns.MetroMargin)) < 1e-3f, "pointing right: the block, the sign, the arrow, flush to the right");
            Program.Check(right.Elements.Exists(e => e.Kind == GuideElementKind.Text && e.Text == "100") && Inside(right, out worst), "100 m, on the board: " + worst);
            Program.Check(WayfindingSigns.DistanceOf("75") == 100 && WayfindingSigns.DistanceOf("30") == 50 && WayfindingSigns.DistanceOf("900") == 200 && WayfindingSigns.DistanceOf("abc") == 50, "the distance in steps of 50, 50 to 200");
            Program.Check(WayfindingSigns.LineOf("0") == 1 && WayfindingSigns.LineOf("99") == 23 && WayfindingSigns.LineOf("x") == 12, "the line 1 to 23");

            // Fig. A.1's colours: line 1 red, 2 green, 9 light blue; the figures black on the light ones (13), white on the dark.
            byte[] one = WayfindingSigns.LineRgb(1), two = WayfindingSigns.LineRgb(2), nine = WayfindingSigns.LineRgb(9), thirteen = WayfindingSigns.LineRgb(13);
            Program.Check(one[0] == 220 && one[1] == 30 && one[2] == 37 && two[1] == 186 && nine[2] == 233 && thirteen[0] == 228, "the colours read off fig. A.1");
            GuideColours.Rgb(WayfindingSigns.LineColour(12), out byte r, out byte g, out byte b);
            Program.Check(r == 0 && g == 116 && b == 90, "the guide colour of line 12 is its green");
            Program.Check(WayfindingSigns.BlackOn(13) && WayfindingSigns.BlackOn(3) && !WayfindingSigns.BlackOn(12) && !WayfindingSigns.BlackOn(4), "black figures on pink and yellow, white on green and violet (fig. 20, 13)");
            GuideElement digits = Texts(right).Find(e => e.Text == "13");
            Program.Check(digits != null && digits.Colour == GuideColour.Black, "line 13's figures black");
        }

        private static List<GuideElement> Texts(GuidePart p) => p.Elements.FindAll(e => e.Kind == GuideElementKind.Text);

        private static void Strip()
        {
            GuidePart p = Main(WayfindingSigns.PedId);
            bool onBoard = Inside(p, out string worst);
            Program.Check(p.Ground == GuideColour.Green && onBoard, "green, everything on the board: " + worst);
            float post = p.W * 0.5f;
            List<GuideElement> texts = Texts(p);
            GuideElement ditie = texts.Find(e => e.Text == "地铁"), gongce = texts.Find(e => e.Text == "公厕");
            Program.Check(ditie != null && gongce != null && ditie.Right <= post && gongce.X >= post, "the left name on the left plate, the right one on the right");
            Program.Check(Math.Abs(ditie.H - 0.5f * WayfindingSigns.PlateH) < 1e-4f, $"the characters half the plate's height ({ditie.H:0.0000})");
            Program.Check(texts.Exists(e => e.Text == "180") && texts.Exists(e => e.Text == "340"), "the distances 180 and 340 m");
            List<GuideElement> arrows = p.Elements.FindAll(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White);
            Program.Check(arrows.Count == 4 && arrows.FindAll(e => e.X < post).Count == 2, "a double chevron at each outer end");
            Program.Check(p.Elements.FindAll(e => e.Kind == GuideElementKind.Path).Count == 2 && Rects(p, GuideColour.White).Count == 2, "a pictogram on a white square at each side of the post");
            GuideElement lsquare = Rects(p, GuideColour.White).Find(e => e.X < post), rsquare = Rects(p, GuideColour.White).Find(e => e.X >= post);
            Program.Check(lsquare.Right > ditie.Right && rsquare.X < gongce.X, "the pictograms between the names and the post");
            GuidePart none = Main(WayfindingSigns.PedId, ("d1", "0"), ("s1", ""), ("n2", "东湖景区"), ("w2", "up"));
            Program.Check(!Texts(none).Exists(e => e.Text == "180") && Rects(none, GuideColour.White).Count == 1 && Inside(none, out worst), "no distance, no pictogram, an arrow up: still on the board: " + worst);
            GuidePart longer = Main(WayfindingSigns.PedId, ("n1", "西湖风景名胜区"), ("p1", "XIHU FENGJING MINGSHENG QU"), ("d1", "1200"));
            Program.Check(Inside(longer, out worst), "a long name and a long pinyin stay on the board: " + worst);
        }

        private static void Hung()
        {
            // The strip under a street plate on a plate post (Hangzhou fig. B.2: 2960 in all) and alone on a plain post.
            SupportDef support = SupportCatalog.ById(SupportCatalog.PlateHangzhou);
            var ordered = new List<AssemblyBoard>
            {
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb17733.street"), "len=1120;wid=360;run=ns"), Member = 0, Slot = 0 },
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(WayfindingSigns.PedId), null), Member = 1, Slot = 1 }
            };
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(ordered), support, 0f);
            Placement plate = plan.Placed.Find(p => p.OnHead), strip = plan.Placed.Find(p => !p.OnHead);
            Program.Check(plate != null && plate.Def.Id == "gb17733.street" && strip.Def.Id == WayfindingSigns.PedId && Math.Abs(strip.Bottom - 1.9f) < 1e-4f, "the plate on the head, the strip from 1.9 m");
            SignParts parts = SignBuilder.BuildAssembly(ordered, support, 0f);
            Program.Check(parts.Faces.Count == 2 && parts.Metal.VertexCount > 0, "both drawn");
            SignParts alone = SignBuilder.Build(SignInstances.Of(SignCatalog.ById(WayfindingSigns.MetroId), null), SupportCatalog.ById(SupportCatalog.SinglePole));
            Program.Check(alone.Layout.Head == null && Math.Abs(alone.Layout.BottomClearance - 2.5f) < 1e-4f && alone.Layout.BackZ > alone.Layout.PoleRadius, "the metro board on a plain post at 2.5 m");
            Program.Check(SupportPlanner.Auto(SignCatalog.ById(WayfindingSigns.MetroId)).Kind == SupportKind.SinglePole, "one post carries it (1640 wide)");
        }
    }
}
