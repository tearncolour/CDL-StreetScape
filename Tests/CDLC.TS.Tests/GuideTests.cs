using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Offline checks of the guide boards: the spec's text form, the sizes the standard gives, the layout's invariants over a sweep of settings, and the way boards become meshes.</summary>
    internal static class GuideTests
    {
        internal static void RunAll()
        {
            Program.Run("guide: a spec is written, read back, escaped and made valid", SpecText);
            Program.Run("guide: a digits-only field keeps digits and one point", DigitsField);
            Program.Run("guide: every preset sets only fields and values its template accepts", PresetsAreValid);
            Program.Run("guide: exit boards have the sizes of Fig. E.208 to E.213", ExitSizes);
            Program.Run("guide: the exit number plate has the size of Fig. E.207", ExitNumberSizes);
            Program.Run("guide: the exit sign, the next exit and the countdown boards", OtherSizes);
            Program.Run("guide: the expressway plates have the sizes of Appendix C", PlateSizes);
            Program.Run("guide: the naming plate after an entrance has the rows of Fig. E.202", EntryPlateE202);
            Program.Run("guide: the through lane's board (E.215) and the main line's direction board (E.214)", ThroughLaneAndMainLine);
            Program.Run("guide: the route plate has the rows of Fig. E.201", RoutePlateE201);
            Program.Run("guide: 路38's two entries have the examples of Fig. 260 a and b", PlaceDistanceExamples);
            Program.Run("guide: the interchange exit boards have table C.1's plate (E.212, E.213 a) and E.166 b's diagonal arrow", HubPlateAndArrow);
            Program.Run("guide: nothing overlaps or leaves its board in any combination of settings", LayoutSweep);
            Program.Run("guide: the arrows point where they say and stay in their square", Arrows);
            Program.Run("guide: clipping a polygon to a box", Clipping);
            Program.Run("guide: instances are cached by their settings and size their pictures", Instances);
            Program.Run("guide: boards become meshes with their plates and one picture", BoardMeshes);
            Program.Run("guide: the catalogue lists every template and keeps the old test board alive", GuideCatalogue);
            Program.Run("support: the planner picks the form from the board and sizes it by the board", AutoSupport);
            Program.Run("guide: the size follows the speed of the road (Table 3 bands)", SpeedBands);
            Program.Run("road: the game's speed limits are metres per second (0.4.8's log: 27.778 on a road of 100 km/h)", SpeedUnits);
            Program.Run("support: the gantry and the choice of the form by the road", GantryRules);
            Program.Run("glyphs: the path reader and the pictogram builder", GlyphBuilder);
            Program.Run("glyphs: every pictogram is inside its box, not empty, and has its holes", EveryGlyph);
            Program.Run("signs: every fixed sign is laid out for every band and setting", EverySignEveryBand);
            Program.Run("signs: the yield sign's character is clear of its edge", YieldCharacter);
        }

        // ------------------------------------------------------------------ helpers

        private static GuideBoard Lay(string template, string overrides)
        {
            GuideTemplate t = GuideTemplates.ById(template);
            GuideSpec spec = t.Default();
            foreach (string pair in overrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return t.Layout(spec);
        }

        // ------------------------------------------------------------------ spec

        private static void SpecText()
        {
            GuideTemplate t = GuideTemplates.ById("guide.exit.advance");
            GuideSpec d = t.Default();
            Program.Check(d.ToString().StartsWith("road="), "the spec starts with the first field");
            Program.Check(t.Parse(d.ToString()).ToString() == d.ToString(), "a written spec reads back the same");

            GuideSpec s = d.With("to1", "你;世=界\\").With("code", "G1;2");
            GuideSpec back = t.Parse(s.ToString());
            Program.Check(back.Get("to1") == "你;世=界\\", "the separators and the backslash survive");
            Program.Check(back.Get("code") == "G1;2", "a semicolon in a value survives");
            Program.Check(back.ToString() == s.ToString(), "and the text is the same");

            Program.Check(d.With("road", "nonsense").Get("road") == t.FieldOf("road").Default, "a choice that is not offered takes the default");
            Program.Check(d.With("lane", "1").ToString() == d.ToString() || t.FieldOf("lane") == null, "an unknown field is ignored");
            Program.Check(d.With("to1", "一二三四五六七八九十").Get("to1").Length == t.FieldOf("to1").MaxLength, "a text is cut to the field's length");
            Program.Check(d.With("to1", "  海安 ").Get("to1") == "海安", "a text is trimmed");
            Program.Check(d.With("to1", "a\nb\tc").Get("to1") == "abc", "control characters are dropped");
            Program.Check(t.Parse("road=s;zzz=1;to1=x").Get("road") == "s" && t.Parse("road=s;zzz=1;to1=x").Get("to2") == t.FieldOf("to2").Default, "unknown keys are dropped and missing fields get their default");
            Program.Check(t.Parse(null).ToString() == d.ToString() && t.Parse("").ToString() == d.ToString() && t.Parse("garbage").ToString() == d.ToString(), "nothing and garbage give the defaults");
            Program.Check(t.Parse("road=s=x;to1=a=b").Get("to1") == "a=b", "an unescaped = in a value belongs to the value");

            // The longest possible spec fits the saved field (509 bytes).
            foreach (GuideTemplate template in GuideTemplates.All)
            {
                GuideSpec longest = template.Default();
                foreach (GuideField f in template.Fields)
                {
                    if (f.Kind == GuideFieldKind.Text)
                        longest = longest.With(f.Key, new string('龘', f.MaxLength));
                }

                Program.Check(Encoding.UTF8.GetByteCount(longest.ToString()) <= GuideSpec.MaxBytes, template.Id + ": the longest spec fits: " + Encoding.UTF8.GetByteCount(longest.ToString()) + " bytes");
            }

            Program.Check(d.Hash == t.Parse(d.ToString()).Hash && d.Hash != d.With("to1", "别处").Hash, "the hash follows the content");
        }

        private static void DigitsField()
        {
            GuideField f = GuideField.Digits("limit", "限速", "Limit", "60", 3);
            Program.Check(f.Sanitize("120") == "120" && f.Sanitize("1a2b0") == "120" && f.Sanitize("高速") == "" && f.Sanitize("  45 ") == "45", "only digits are kept");
            Program.Check(f.Sanitize("12345") == "123", "and cut to the length");
            GuideField h = GuideField.Digits("height", "限高", "Height", "3.5", 4);
            Program.Check(h.Sanitize("3.5") == "3.5" && h.Sanitize("3.5.2") == "3.52" && h.Sanitize("a.b") == "." , "one decimal point is kept, the second is dropped: " + h.Sanitize("3.5.2"));
        }

        private static void PresetsAreValid()
        {
            var ids = new HashSet<string>();
            foreach (GuidePreset p in GuideTemplates.Presets)
            {
                Program.Check(ids.Add(p.Id), "duplicate preset " + p.Id);
                GuideTemplate t = GuideTemplates.ById(p.Template);
                Program.Check(t != null, p.Id + ": the template exists");
                if (t == null)
                    continue;
                GuideSpec spec = GuideTemplates.SpecOf(p);
                foreach (string pair in p.Overrides.Split(';'))
                {
                    int eq = pair.IndexOf('=');
                    string key = pair.Substring(0, eq), value = pair.Substring(eq + 1);
                    Program.Check(t.FieldOf(key) != null, $"{p.Id}: {key} is a field of {t.Id}");
                    Program.Check(spec.Get(key) == value, $"{p.Id}: {key}={value} is accepted (got '{spec.Get(key)}')");
                }

                Program.Check(!string.IsNullOrEmpty(p.Zh) && !string.IsNullOrEmpty(p.En) && !string.IsNullOrEmpty(p.Code), p.Id + " has names");
            }
        }

        // ------------------------------------------------------------------ the sizes of the figures

        private static void ExitSizes()
        {
            const float h = 0.65f;
            // Fig. E.208 a: 6.2 h x 5.6 h for two names of two characters, the distance, the road plate and the arrow.
            GuideBoard adv = Lay("guide.exit.advance", "road=g;code=G204;to1=如皋;to2=海安;dist=500;unit=m;xnum=145");
            Program.Near(adv.H, h, 1e-6f, "the 100 km/h band has characters of 65 cm");
            Program.Near(adv.Main.W / h, 6.2f, 0.01f, "E.208: 6.2 h wide");
            Program.Near(adv.Main.H / h, 5.6f, 0.01f, "E.208: 5.6 h high");
            Program.Check(adv.Parts.Count == 2 && adv.Parts[1].Y < 0f, "the exit number plate is a second part above the board");
            Program.Near(adv.Main.Liner / h, 0.1f, 1e-4f, "Fig. 3: the liner is 0.1 h");
            Program.Near(adv.Main.Border / h, 0.1f, 1e-4f, "Fig. 3: the border is 0.1 h");
            Program.Near(adv.Main.Radius / h, 0.4f, 1e-4f, "Fig. 3: the outer radius is 0.4 h");
            Program.Near(adv.Main.InnerRadius / h, 0.2f, 1e-4f, "Fig. 3: the inside's radius is 0.2 h");

            // Without a plate: nothing above.
            GuideBoard noPlate = Lay("guide.exit.advance", "xnum=");
            Program.Check(noPlate.Parts.Count == 1, "no exit number: no plate");

            // The direction board is as high; a 2 km board is narrower than a 500 m one only when the distance row is the widest.
            GuideBoard dir = Lay("guide.exit.direction", "road=g;code=G204;to1=如皋;to2=海安");
            Program.Near(dir.Main.W / h, 6.2f, 0.01f, "the direction board is 6.2 h wide (the names decide)");
            Program.Near(dir.Main.H / h, 5.6f, 0.01f, "the direction board is 5.6 h high");

            // An unnumbered road (路44, E.210): the name band replaces the plate, the board is as high.
            GuideBoard named = Lay("guide.exit.advance", "road=name;rname=前进路;to1=广州;to2=番禺;dist=2;unit=km;arrow=d;xnum=32;xside=l");
            Program.Near(named.Main.H / h, 5.6f, 0.01f, "E.210: 5.6 h high with the name band");

            // A through lane (Fig. 270): the yellow strip takes the place of the last row.
            GuideBoard lane = Lay("guide.exit.direction", "road=ge;code=G55;to1=永州;to2=;lane=1;xnum=");
            // E.215 a: 0.2 edge, 0.4, the plate 100 (70) cm, 0.4, the names 1, 0.3, then the strip: 0.3, the arrow 1, 0.4, 0.2 edge (in h): 4.2 h and the plate (1.00 m at 100 km/h).
            float plateH = CDLC.TS.Geo.Signs.RoadGuideSigns.ExpresswayPlateSize(h);
            Program.Near(lane.Main.H / h, 4.2f + plateH / h, 0.01f, "E.215 a: the through lane's board is 4.2 h and the plate high: " + lane.Main.H / h);
            GuideElement strip = null, arrowOnStrip = null;
            foreach (GuideElement e in lane.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Yellow && e.Bottom <= lane.Main.H)
                    strip = e;
                else if (e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Black)
                    arrowOnStrip = e;
            }

            Program.Check(strip != null, "the through lane has a yellow strip");
            if (strip != null && arrowOnStrip != null)
            {
                Program.Near(strip.H / h, 1.7f, 1e-3f, "E.215 a: the strip is 1.7 h high");
                Program.Near((arrowOnStrip.Y - strip.Y) / h, 0.3f, 0.02f, "the arrow's box starts 0.3 h under the strip's top");
                Program.Near((strip.Bottom - arrowOnStrip.Bottom) / h, 0.4f, 0.02f, "and ends 0.4 h over its foot");
            }

            // The plate keeps the figure's proportions: 1.5 h high, centred.
            GuideElement plate = null;
            foreach (GuideElement e in adv.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                    plate = e;
            }

            Program.Check(plate != null && Math.Abs(plate.H / h - 1.5f) < 0.01f, "the road plate is 1.5 h high");
            Program.Check(plate != null && Math.Abs((plate.X + plate.W * 0.5f) - adv.Main.W * 0.5f) < 1e-3f, "the road plate is centred");

            // Longer names make a wider board; a three-character name is still one row of cells.
            GuideBoard wide = Lay("guide.exit.advance", "to1=连云港市;to2=石家庄市");
            Program.Check(wide.Main.W > adv.Main.W, "longer names make the board wider");

            // The speed bands (Table 3 and 5): the board scales with the character height.
            GuideBoard slow = Lay("guide.exit.advance", "road=g;code=G204;to1=如皋;to2=海安;dist=500;unit=m;xnum=145;speed=60");
            Program.Near(slow.Main.W / slow.H, 6.2f, 0.01f, "the board is 6.2 h wide at every speed");
            Program.Near(slow.H, 0.40f, 1e-6f, "the 40 to 70 km/h band has characters of 40 cm");
        }

        private static void ExitNumberSizes()
        {
            // Fig. E.207 a: 235 x 100 cm for three digits of 50 cm (the digit pitch of the font is a little wider than the figure's, so the oval is up to 5 cm wider).
            GuidePart plate = GuideKit.ExitNumberPlate(0.50f, "119", "", false, 0f, 0f);
            Program.Near(plate.H, 1.00f, 0.005f, "E.207: the plate is 100 cm high");
            Program.Near(plate.W, 2.35f, 0.06f, "E.207: the plate is about 235 cm wide");
            Program.Near(plate.Liner + plate.Border, 0.06f, 1e-4f, "E.207: edge 6 cm for characters of 30 cm");
            GuideElement oval = null;
            foreach (GuideElement e in plate.Elements)
            {
                if (e.Kind == GuideElementKind.Oval)
                    oval = e;
            }

            Program.Near(oval.H, 0.80f, 1e-4f, "E.207: the oval is 80 cm high");
            Program.Check(oval.Right <= plate.W - plate.Edge + 1e-4f && oval.Y >= plate.Edge && oval.Bottom <= plate.H - plate.Edge, "the oval is inside the plate's edge");

            GuidePart ab = GuideKit.ExitNumberPlate(0.50f, "180", "A-B", false, 0f, 0f);
            Program.Check(ab.W > plate.W + 0.4f, "E.207 b: a suffix makes the plate wider (65 cm in the figure)");
            GuidePart left = GuideKit.ExitNumberPlate(0.50f, "272", "", true, 0f, 0f);
            Program.Near(left.H, plate.H, 1e-5f, "E.207 c: the left plate is as high");
            int cells = 0;
            foreach (GuideElement e in left.Elements)
                cells += e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Cell ? 1 : 0;
            Program.Check(cells == 2, "the left exit plate has 左 and 出口 in two lines");

            // Table 5: 出口 is 25 or 30 cm; the digits 40 to 50, 35 to 40, 25 to 30.
            Program.Near(GuideKit.ExitNumberPlate(0.45f, "1", "", false, 0, 0).Liner * 10f, 0.30f, 1e-4f, "a digit of 45 cm: 出口 30 cm");
            Program.Near(GuideKit.ExitNumberPlate(0.275f, "1", "", false, 0, 0).Liner * 10f, 0.25f, 1e-4f, "a digit of 27.5 cm: 出口 25 cm");
        }

        private static void OtherSizes()
        {
            const float h = 0.65f;
            // 路53 (Fig. E.220): 5.5 h high, 6.5 h with 左; about 4 h wide for three digits.
            GuideBoard sign = Lay("guide.exit.sign", "xnum=321;xside=r");
            Program.Near(sign.Main.H / h, 5.5f, 0.01f, "E.220 a: 5.5 h high");
            Program.Check(sign.Main.W / h > 3.8f && sign.Main.W / h < 4.4f, "E.220: about 4 h wide: " + sign.Main.W / h);
            GuideBoard signLeft = Lay("guide.exit.sign", "xnum=101;xside=l");
            Program.Near(signLeft.Main.H / h, 6.5f, 0.01f, "E.220 b: 6.5 h high with 左");

            // 路54 (Fig. E.221): 2.2 h and 3.5 h high.
            GuideBoard one = Lay("guide.exit.next", "form=h");
            Program.Near(one.Main.H / h, 2.2f, 0.01f, "E.221 a: 2.2 h high");
            GuideBoard two = Lay("guide.exit.next", "form=v");
            Program.Near(two.Main.H / h, 3.5f, 0.01f, "E.221 b: 3.5 h high");
            Program.Check(one.Main.W > two.Main.W, "the one line board is the wider");

            // 路52 (Fig. E.219): 60 x 142 cm; 3, 2, 1 stripes.
            foreach (string dist in new[] { "300", "200", "100" })
            {
                GuideBoard count = Lay("guide.countdown", "dist=" + dist);
                Program.Near(count.Main.W, 0.60f, 1e-5f, "E.219: 60 cm wide");
                Program.Near(count.Main.H, 1.42f, 1e-5f, "E.219: 142 cm high");
                int stripes = 0;
                foreach (GuideElement e in count.Main.Elements)
                    stripes += e.Kind == GuideElementKind.Polygon ? 1 : 0;
                Program.Check(stripes == (dist == "300" ? 3 : dist == "200" ? 2 : 1), dist + " m: the number of stripes: " + stripes);
            }

            // The stripes slope as Fig. 274: down to the right on a board on the left side.
            GuideBoard l = Lay("guide.countdown", "dist=100;side=l"), r = Lay("guide.countdown", "dist=100;side=r");
            GuideElement ls = FirstPolygon(l), rs = FirstPolygon(r);
            Program.Check(ls.Points[1] < ls.Points[ls.Points.Length - 1] || Slope(ls) > 0f, "left side: the stripe runs down to the right");
            Program.Check(Slope(ls) > 0f && Slope(rs) < 0f, "the two sides slope opposite ways");
        }

        private static GuideElement FirstPolygon(GuideBoard b)
        {
            foreach (GuideElement e in b.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Polygon)
                    return e;
            }

            return null;
        }

        /// <summary>The slope of a stripe's lower edge (y down): the vertex furthest to the left and the one furthest to the right, along the bottom of the polygon.</summary>
        private static float Slope(GuideElement stripe)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < stripe.Points.Length; i += 2)
            {
                minX = Math.Min(minX, stripe.Points[i]);
                maxX = Math.Max(maxX, stripe.Points[i]);
            }

            float yLeft = 0f, yRight = 0f;
            int nl = 0, nr = 0;
            for (int i = 0; i < stripe.Points.Length; i += 2)
            {
                if (Math.Abs(stripe.Points[i] - minX) < 1e-4f) { yLeft += stripe.Points[i + 1]; nl++; }
                if (Math.Abs(stripe.Points[i] - maxX) < 1e-4f) { yRight += stripe.Points[i + 1]; nr++; }
            }

            return (yRight / nr) - (yLeft / nl);
        }

        private static void PlateSizes()
        {
            // Table C.1 (guide boards): 100 x 100, 125 x 100, 170 x 100 cm at 80 to 120 km/h; 70 x 70, 85 x 70, 115 x 70 at 80 km/h or less.
            (string code, string sub, float w)[] fast = { ("G3", "", 1.00f), ("G15", "", 1.25f), ("G40", "01", 1.70f) };
            foreach ((string code, string sub, float w) in fast)
            {
                GuideBoard b = Lay("guide.road.plate", $"road=ge;code={code};sub={sub};use=guide;speed=100");
                Program.Near(b.Main.W, w, 0.005f, $"C.1 {code}{sub}: width at 100 km/h");
                Program.Near(b.Main.H, 1.00f, 0.005f, $"C.1 {code}{sub}: height at 100 km/h");
            }

            (string code, string sub, float w)[] slow = { ("G3", "", 0.70f), ("G15", "", 0.85f), ("G40", "01", 1.15f) };
            foreach ((string code, string sub, float w) in slow)
            {
                GuideBoard b = Lay("guide.road.plate", $"road=ge;code={code};sub={sub};use=guide;speed=60");
                Program.Near(b.Main.W, w, 0.005f, $"C.1 {code}{sub}: width at 80 km/h or less");
                Program.Near(b.Main.H, 0.70f, 0.005f, $"C.1 {code}{sub}: height at 80 km/h or less");
            }

            // C.1.1.2.1: 330 x 300 at the start; C.1.1.2.2: 125 x 120 (170 x 120 for four digits) after an entrance.
            GuideBoard start = Lay("guide.road.plate", "road=ge;code=G2;rname=京沪高速;use=start");
            Program.Near(start.Main.W, 3.30f, 1e-4f, "C.1.1.2.1: 330 cm wide");
            Program.Near(start.Main.H, 3.00f, 1e-4f, "C.1.1.2.1: 300 cm high");
            GuideBoard entry = Lay("guide.road.plate", "road=ge;code=G3;rname=京台高速;use=entry");
            Program.Near(entry.Main.W, 1.25f, 1e-4f, "C.1.1.2.2: 125 cm wide");
            Program.Near(entry.Main.H, 1.20f, 1e-4f, "C.1.1.2.2: 120 cm high");
            Program.Near(Lay("guide.road.plate", "road=ge;code=G40;sub=01;use=entry").Main.W, 1.70f, 1e-4f, "C.1.1.2.2: 170 cm wide for four digits");

            // The strip: red with 国家高速 for national roads, yellow with X高速 for provincial ones.
            GuideElement strip = null;
            foreach (GuideElement e in start.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                    strip = e;
            }

            Program.Check(strip != null, "the national plate has a red strip");
            GuideBoard province = Lay("guide.road.plate", "road=se;code=S1;prov=沪;use=guide");
            bool yellow = false, text = false;
            foreach (GuideElement e in province.Main.Elements)
            {
                yellow |= e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Yellow;
                text |= e.Kind == GuideElementKind.Text && e.Text == "沪高速";
            }

            Program.Check(yellow && text, "the provincial plate has a yellow strip with 沪高速");

            // The name shrinks to fit when it is long (C.1.1.2.2: 10 to 20 cm for more than four characters).
            GuideBoard longName = Lay("guide.road.plate", "road=ge;code=G3;rname=杭州湾环线高速;use=entry");
            float nameH = 1f;
            foreach (GuideElement e in longName.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Cell && e.Text == "杭州湾环线高速")
                    nameH = e.H;
            }

            Program.Check(nameH >= 0.10f - 1e-4f && nameH < 0.20f, "a long name is 10 to 20 cm: " + nameH);
        }

        private static void HubPlateAndArrow()
        {
            const float h = 0.65f;
            // E.212 and E.213 a: the expressway plate has the size of table C.1, 100 x 170 cm for G1511 at 100 km/h, so that the board is 0.4 + 0.4 + 1.54 + 0.4 + 1 + 0.5 + 1 + 0.4 = 5.64 h high.
            GuideBoard hub = Lay("guide.exit.direction", "road=ge;code=G15;sub=11;to1=日照;to2=济宁;arrow=b;xnum=50");
            Program.Near(hub.Main.H / h, 4.1f + 1.00f / h, 0.005f, "E.213 a: 4.1 h and the plate: " + hub.Main.H / h);
            Program.Near(hub.Main.H / h, 5.64f, 0.01f, "which is the 5.64 h of the drawing");
            GuideElement ground = null;
            foreach (GuideElement e in hub.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green && e.StrokeWidth > 0f)
                    ground = e;
            }

            Program.Check(ground != null, "the road plate is on the board");
            if (ground != null)
            {
                Program.Near(ground.H, 1.00f, 1e-4f, "E.213 a: the plate is 100 cm high");
                Program.Near(ground.W, 1.70f, 1e-4f, "and 170 cm wide for four digits");
            }

            // At 80 km/h and less the plate is 70 cm (table C.1).
            GuideBoard slow = Lay("guide.exit.direction", "road=ge;code=G15;sub=11;to1=日照;to2=济宁;arrow=b;xnum=50;speed=60");
            GuideElement slowGround = null;
            foreach (GuideElement e in slow.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green && e.StrokeWidth > 0f)
                    slowGround = e;
            }

            Program.Check(slowGround != null && Math.Abs(slowGround.H - 0.70f) < 1e-4f && Math.Abs(slowGround.W - 1.15f) < 1e-4f, "70 x 115 cm below");

            // The plate of a national road stays 1.5 h (E.208 to E.211).
            GuideBoard national = Lay("guide.exit.direction", "road=g;code=G204;to1=如皋;to2=海安");
            GuideElement red = null;
            foreach (GuideElement e in national.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                    red = e;
            }

            Program.Check(red != null && Math.Abs(red.H / h - 1.5f) < 0.01f, "the plate of a national road is 1.5 h");

            // The 45 degree arrow of E.166 b: nine corners, the head a wide and a long with the stem 0.28 a thick, 1.6 a long in all; up and to the right or up and to the left.
            foreach (bool left in new[] { false, true })
            {
                float[] p = GuideKit.DiagonalArrowPolygon(left, 0f, 0f, 1f);
                Program.Check(p.Length == 18, "nine corners");
                float Dist(int i, int j) => (float)Math.Sqrt((p[2 * i] - p[2 * j]) * (p[2 * i] - p[2 * j]) + (p[2 * i + 1] - p[2 * j + 1]) * (p[2 * i + 1] - p[2 * j + 1]));
                float a = Dist(2, 7), stem = Dist(4, 5);
                float tailX = (p[8] + p[10]) * 0.5f, tailY = (p[9] + p[11]) * 0.5f;
                float length = (float)Math.Sqrt((p[0] - tailX) * (p[0] - tailX) + (p[1] - tailY) * (p[1] - tailY));
                Program.Near(length / a, 1.6f, 1e-3f, "E.213: 1.6 a long");
                Program.Near(stem / a, 0.28f, 1e-3f, "the stem is 0.28 a");
                Program.Near(Dist(1, 8) / a, 1f, 1e-3f, "the head is a wide behind the tip too");
                Program.Check(p[1] < tailY, "it points up");
                Program.Check(left ? p[0] < tailX : p[0] > tailX, "to the " + (left ? "left" : "right"));
                Program.Near(Math.Abs(p[0] - tailX), Math.Abs(p[1] - tailY), 1e-3f, "at 45 degrees");
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                for (int i = 0; i < p.Length; i += 2)
                {
                    x0 = Math.Min(x0, p[i]);
                    x1 = Math.Max(x1, p[i]);
                    y0 = Math.Min(y0, p[i + 1]);
                    y1 = Math.Max(y1, p[i + 1]);
                }

                Program.Check(x0 >= -1e-4f && y0 >= -1e-4f && x1 <= 1f + 1e-4f && y1 <= 1f + 1e-4f, "inside its square");
                Program.Near(Math.Max(x1 - x0, y1 - y0), 1f, 1e-3f, "and as large as the square allows");
            }
        }

        private static void ThroughLaneAndMainLine()
        {
            const float h = 0.65f;
            float plate = CDLC.TS.Geo.Signs.RoadGuideSigns.ExpresswayPlateSize(h) / h;

            // 路48 a (E.215 a, Fig. 270 a): 6.2 h wide whatever the names, the direction mark 0.3 h right of the plate and 0.3 h under its top, the plate and the mark centred as a group.
            GuideBoard a = Lay("guide.exit.direction", "road=ge;code=G55;dir=北;to1=永州;to2=;arrow=f;lane=1;xnum=;speed=100");
            Program.Near(a.Main.W / h, 6.2f, 0.01f, "E.215 a: 6.2 h wide: " + a.Main.W / h);
            GuideElement ground = null, markBox = null;
            foreach (GuideElement e in a.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green && e.StrokeWidth > 0f)
                    ground = e;
                else if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.White && Math.Abs(e.W - h) < 1e-4f)
                    markBox = e;
            }

            Program.Check(ground != null && markBox != null, "the plate and the direction mark are there");
            if (ground != null && markBox != null)
            {
                Program.Near((markBox.X - ground.Right) / h, 0.3f, 0.005f, "the mark is 0.3 h right of the plate");
                Program.Near((markBox.Y - ground.Y) / h, 0.3f, 0.005f, "and 0.3 h under its top");
                float edge = a.Main.Edge, groupW = ground.W + 0.3f * h + h;
                Program.Near(ground.X - edge, (a.Main.W - 2f * edge - groupW) * 0.5f, 1e-3f, "the group is centred");
            }

            // Without the mark the plate alone is centred; the board is as wide.
            GuideBoard plain = Lay("guide.exit.direction", "road=ge;code=G55;dir=;to1=永州;to2=;arrow=f;lane=1;xnum=;speed=100");
            Program.Near(plain.Main.W / h, 6.2f, 0.01f, "6.2 h without the mark");

            // 路48 c (E.215 b, Fig. 270 c): two arrows over the lanes' centres, 8.4 h wide outside, the arrows 0.4 h from the inside's edges.
            GuideBoard c = Lay("guide.exit.direction", "road=se;code=S51;prov=粤;to1=阳江;to2=;arrow=b;lane=1;lanes=2;xnum=;speed=100");
            Program.Near(c.Main.W / h, 8.4f, 0.01f, "E.215 b: 8.4 h wide: " + c.Main.W / h);
            var arrows = new List<GuideElement>();
            foreach (GuideElement e in c.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Black)
                    arrows.Add(e);
            }

            Program.Check(arrows.Count == 2, "two arrows: " + arrows.Count);
            if (arrows.Count == 2)
            {
                float edge = c.Main.Edge, innerW = c.Main.W - 2f * edge;
                float lo = Math.Min(arrows[0].X, arrows[1].X), hi = Math.Max(arrows[0].Right, arrows[1].Right);
                Program.Check(lo - edge >= 0.4f * h - 1e-3f && lo - edge <= 0.5f * h, "the left arrow is 0.4 h from the inside's edge: " + (lo - edge) / h);
                Program.Check(innerW - (hi - edge) >= 0.4f * h - 1e-3f && innerW - (hi - edge) <= 0.5f * h, "and the right one too");
                Program.Near(arrows[0].Y, arrows[1].Y, 1e-4f, "on one line");
            }

            // Fig. 268 a (E.214 a): 4.1 h and the plate high, the names 1.2 h from the inside's edges: 6.2 h wide outside for three characters.
            GuideBoard m1 = Lay("gb.guide9.268a", "code=G25;dir=南;to=连云港;speed=100");
            Program.Near(m1.Main.W / h, 6.2f, 0.01f, "E.214 a: 6.2 h wide: " + m1.Main.W / h);
            Program.Near(m1.Main.H / h, 4.1f + plate, 0.01f, "E.214 a: 4.1 h and the plate high: " + m1.Main.H / h);

            // Fig. 268 b (E.214 b): an arrow down over each of three lanes, 14.8 h wide outside, the outer arrows 0.4 h from the inside's edges, centres 6.3 h apart.
            GuideBoard m3 = Lay("gb.guide9.268b", "code=G25;dir=南;to=连云港;lanes=3;speed=100");
            Program.Near(m3.Main.W / h, 14.8f, 0.02f, "E.214 b: 14.8 h wide: " + m3.Main.W / h);
            Program.Near(m3.Main.H / h, 4.1f + plate, 0.01f, "E.214 b: 0.4 + plate + 0.4 + 1 + 0.5 + arrows 1 + 0.4 inside, the same height as a: " + m3.Main.H / h);
            var down = new List<GuideElement>();
            foreach (GuideElement e in m3.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White && e.Y > m3.Main.H * 0.6f)
                    down.Add(e);
            }

            Program.Check(down.Count == 3, "three arrows: " + down.Count);
            if (down.Count == 3)
            {
                down.Sort((p, q) => p.X.CompareTo(q.X));
                Program.Near(((down[1].X + down[1].Right) - (down[0].X + down[0].Right)) * 0.5f / h, 6.3f, 0.01f, "centres 6.3 h apart");
                Program.Near(((down[2].X + down[2].Right) - (down[1].X + down[1].Right)) * 0.5f / h, 6.3f, 0.01f, "all three");
                float mid = m3.Main.W * 0.5f;
                Program.Near((down[1].X + down[1].Right) * 0.5f, mid, 1e-3f, "the middle one is in the middle");
            }

            // A lane width sets the spacing (m): 4 m apart.
            GuideBoard lw = Lay("gb.guide9.268b", "code=G25;dir=南;to=连云港;lanes=3;lw=4;speed=100");
            var wide = new List<GuideElement>();
            foreach (GuideElement e in lw.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White && e.Y > lw.Main.H * 0.6f)
                    wide.Add(e);
            }

            wide.Sort((p, q) => p.X.CompareTo(q.X));
            if (wide.Count == 3)
                Program.Near(((wide[1].X + wide[1].Right) - (wide[0].X + wide[0].Right)) * 0.5f, 4.0f, 0.01f, "a lane width of 4 m puts the arrows 4 m apart");
        }

        private static void PlaceDistanceExamples()
        {
            // Fig. 260 a: 采育 14 km, 廊坊 37 km, 天津 95 km. Fig. 260 b: 璜塘 8 km, the road G42 17 km, 上海 25 km.
            GuideSpec a = GuideTemplates.ById("gb.guide9.38").Default(), b = GuideTemplates.ById("gb.guide9.38b").Default();
            Program.Check(a.Is("form", "a") && a.Get("to1") == "采育" && a.Get("d1") == "14" && a.Get("to2") == "廊坊" && a.Get("d2") == "37" && a.Get("to3") == "天津" && a.Get("d3") == "95", "Fig. 260 a: " + a);
            Program.Check(b.Is("form", "b") && b.Get("to1") == "璜塘" && b.Get("d1") == "8" && b.Get("code2") == "G42" && b.Get("d2") == "17" && b.Get("to3") == "上海" && b.Get("d3") == "25", "Fig. 260 b: " + b);

            // The second line of form b is a road's plate: green with the red strip, taller than the characters.
            GuideBoard board = GuideTemplates.ById("gb.guide9.38b").Layout(b);
            bool plate = false, shanghai = false;
            foreach (GuideElement e in board.Main.Elements)
            {
                plate |= e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red;
                shanghai |= e.Kind == GuideElementKind.Text && e.Text == "上海";
            }

            Program.Check(plate && shanghai, "form b has the plate of G42 and the place 上海");

            // The form is the one setting that differs between the two entries; the player can still switch.
            Program.Check(GuideTemplates.ById("gb.guide9.38").Layout(a.With("form", "b")).Main.W > 0f && GuideTemplates.ById("gb.guide9.38b").Layout(b.With("form", "a")).Main.W > 0f, "either entry lays out in either form");
        }

        private static void RoutePlateE201()
        {
            // Fig. E.201 a, 100 cm high: edge 3, strip 20 (5 + 10 + 5) with characters of 10 cm 10 apart, the number 45 high 14 under the strip and 15 above the edge; b (70 high) is the
            // same scaled by 0.7. Checked on the plate that is drawn on guide boards (RoadPlate) and on the plate of its own (the Appendix C template, use "guide").
            foreach (float P in new[] { 1.00f, 0.70f })
            {
                GuideBlock block = GuideKit.RoadPlate(RoadPlateKind.NationalExpressway, "G3", "", "", P);
                GuideElement ground = null, strip = null, label = null, number = null;
                foreach (GuideElement e in block.Elements)
                {
                    if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Green)
                        ground = e;
                    else if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                        strip = e;
                    else if (e.Kind == GuideElementKind.Text && e.Text == "国家高速")
                        label = e;
                    else if (e.Kind == GuideElementKind.Text && e.Text == "G3")
                        number = e;
                }

                Program.Check(ground != null && strip != null && label != null && number != null, $"E.201 at {P}: ground, strip, label and number are there");
                if (ground == null || strip == null || label == null || number == null)
                    continue;
                Program.Near(ground.StrokeWidth, 0.03f * P, 1e-5f, $"E.201 at {P}: the edge is 3 cm scaled");
                Program.Near(strip.H, 0.20f * P, 1e-5f, "the strip is 20 cm scaled");
                Program.Near(label.H, 0.10f * P, 1e-5f, "its characters are 10 cm scaled");
                Program.Near(label.Gap, 0.10f * P, 1e-5f, "and 10 cm apart");
                Program.Near(label.Y - strip.Y, 0.05f * P, 1e-5f, "5 cm above them");
                Program.Near(number.Y - strip.Bottom, 0.14f * P, 1e-5f, "the number starts 14 cm under the strip");
                Program.Near(number.H, 0.45f * P, 1e-5f, "and is 45 cm high");
                Program.Near(P - ground.StrokeWidth - number.Bottom, 0.15f * P, 1e-5f, "15 cm above the edge");
            }

            foreach ((string speed, float P) in new[] { ("100", 1.00f), ("60", 0.70f) })
            {
                GuideBoard own = Lay("guide.road.plate", $"road=ge;code=G3;use=guide;speed={speed}");
                GuidePart p = own.Main;
                Program.Near(p.Liner, 0f, 1e-6f, $"the plate on its own at {P}: no outer liner (E.201 has one edge)");
                Program.Near(p.Border, 0.03f * P, 1e-5f, "the edge is 3 cm scaled");
                GuideElement strip = null, number = null;
                foreach (GuideElement e in p.Elements)
                {
                    if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                        strip = e;
                    else if (e.Kind == GuideElementKind.Text && e.Text == "G3")
                        number = e;
                }

                Program.Check(strip != null && number != null, "strip and number");
                if (strip != null && number != null)
                {
                    Program.Near(strip.H, 0.20f * P, 1e-5f, "the strip is 20 cm scaled");
                    Program.Near(number.Y - strip.Bottom, 0.14f * P, 1e-5f, "the number 14 cm under it");
                    Program.Near(p.H - p.Edge - number.Bottom, 0.15f * P, 1e-5f, "and 15 cm above the edge");
                }
            }
        }

        private static void EntryPlateE202()
        {
            // Fig. E.202 a (national, four characters of 国家高速), 125 x 120: edge 3 + 3, strip 20 (5 + 10 + 5), characters 10 cm 10 apart, number's box 45 high 8 below the strip, the
            // name's row 20 high 7 below it with characters 20 cm 5 apart and 9 cm from the inside edge, 8 above the edge.
            GuideBoard a = Lay("guide.road.plate", "road=ge;code=G3;rname=京台高速;use=entry");
            GuidePart p = a.Main;
            Program.Near(p.Liner, 0.03f, 1e-5f, "E.202: the liner is 3 cm");
            Program.Near(p.Border, 0.03f, 1e-5f, "E.202: the border is 3 cm");
            Program.Near(p.Radius, 0.11f, 1e-5f, "the outer radius is the 11 cm measured on the drawing");
            float edge = 0.06f;
            GuideElement strip = null, label = null, number = null, name = null;
            foreach (GuideElement e in p.Elements)
            {
                if (e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.Red)
                    strip = e;
                else if (e.Kind == GuideElementKind.Text && e.Text == "国家高速")
                    label = e;
                else if (e.Kind == GuideElementKind.Text && e.Text == "G3")
                    number = e;
                else if (e.Kind == GuideElementKind.Text && e.Text == "京台高速")
                    name = e;
            }

            Program.Check(strip != null && label != null && number != null && name != null, "E.202 a: strip, label, number and name are there");
            if (strip == null || label == null || number == null || name == null)
                return;
            Program.Near(strip.Y, edge, 1e-5f, "the strip starts at the inside of the edge");
            Program.Near(strip.H, 0.20f, 1e-5f, "the strip is 20 cm high");
            Program.Near(strip.W, 1.25f - 2f * edge, 1e-5f, "and as wide as the inside");
            Program.Near(label.H, 0.10f, 1e-5f, "the strip's characters are 10 cm");
            Program.Near(label.Y - strip.Y, 0.05f, 1e-5f, "5 cm above them in the strip");
            Program.Near(strip.Bottom - label.Bottom, 0.05f, 1e-5f, "and 5 cm below");
            Program.Near(label.Gap, 0.10f, 1e-5f, "10 cm apart");
            Program.Near(label.X - edge, 0.215f, 1e-4f, "21.5 cm from the inside edge to the first character");
            Program.Near(number.H, 0.45f, 1e-5f, "the number is 45 cm high");
            Program.Near(number.Y, strip.Bottom + 0.08f, 1e-4f, "8 cm under the strip");
            Program.Near(name.H, 0.20f, 1e-5f, "the name's characters are 20 cm");
            Program.Near(name.Y, number.Bottom + 0.07f, 1e-4f, "7 cm under the number");
            Program.Near(p.H - edge - name.Bottom, 0.08f, 1e-4f, "8 cm above the edge");
            Program.Near(name.Gap, 0.05f, 1e-5f, "the name's characters are 5 cm apart");
            Program.Near(name.X - edge, 0.09f, 1e-4f, "and the run is 9 cm from the inside edge: " + (name.X - edge));
            Program.Near(strip.Bottom + 0.08f + 0.45f + 0.07f + 0.20f + 0.08f + edge, p.H, 1e-4f, "the sizes of the rows add up to 120 cm");

            // Fig. E.202 b: provincial, three characters (X高速): the same plate, the label centred (3 + 34.5 cm from the outer edge in the drawing).
            GuideBoard b = Lay("guide.road.plate", "road=se;code=S1;prov=沪;rname=沪宁高速;use=entry");
            GuideElement prov = null;
            foreach (GuideElement e in b.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Text && e.Text == "沪高速")
                    prov = e;
            }

            Program.Check(prov != null, "E.202 b: the label is 沪高速");
            if (prov != null)
                Program.Near(prov.X, 0.375f, 1e-4f, "E.202 b: the first character is 37.5 cm from the outer edge: " + prov.X);

            // A long name and the plate for four digits keep the rows; a name of up to four characters is 20 cm, a longer one shrinks to the run of four.
            GuideBoard wide = Lay("guide.road.plate", "road=ge;code=G40;sub=01;rname=杭州湾环线高速;use=entry");
            Program.Near(wide.Main.W, 1.70f, 1e-4f, "the plate for four digits is 170 cm wide");
            GuideElement longName = null;
            foreach (GuideElement e in wide.Main.Elements)
            {
                if (e.Kind == GuideElementKind.Text && e.Text == "杭州湾环线高速")
                    longName = e;
            }

            Program.Check(longName != null && longName.Right - longName.X <= 1.70f - 2f * edge + 1e-4f, "the long name is inside the plate");
            Program.Check(longName != null && longName.Y >= number.Bottom + 0.07f - 1e-3f && longName.Bottom <= wide.Main.H - edge - 0.08f + 1e-3f, "and inside its row");
        }

        // ------------------------------------------------------------------ invariants

        private static List<GuideElement> Leaves(GuidePart part)
        {
            var list = new List<GuideElement>();
            foreach (GuideElement e in part.Elements)
            {
                if (e.Kind == GuideElementKind.Text || (e.Kind == GuideElementKind.Polygon && e.Points.Length == 14))
                    list.Add(e);
            }

            return list;
        }

        private static bool Overlap(GuideElement a, GuideElement b, float eps = 1e-4f) =>
            a.X < b.Right - eps && b.X < a.Right - eps && a.Y < b.Bottom - eps && b.Y < a.Bottom - eps;

        private static void LayoutSweep()
        {
            var random = new Random(12345);
            string[] names = { "", "海安", "如皋", "连云港", "石家庄市", "乌鲁木齐", "上海虹桥", "A", "北京" };
            string[] numbers = { "", "1", "32", "145", "321", "1234" };
            int boards = 0;
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                for (int round = 0; round < 250; round++)
                {
                    GuideSpec spec = t.Default();
                    foreach (GuideField f in t.Fields)
                    {
                        string value;
                        switch (f.Kind)
                        {
                            case GuideFieldKind.Choice: value = f.Choices[random.Next(f.Choices.Length)].Value; break;
                            case GuideFieldKind.Flag: value = random.Next(2).ToString(); break;
                            default:
                                value = f.Key.StartsWith("to") || f.Key == "rname" ? names[random.Next(names.Length)] : f.Key == "xnum" ? numbers[random.Next(numbers.Length)] : f.Key == "dist" ? new[] { "1", "2", "3", "500", "0.5", "15" }[random.Next(6)] : f.Key == "code" ? new[] { "G3", "G15", "G204", "S334", "G4001", "G40" }[random.Next(6)] : f.Key == "sub" ? new[] { "", "11", "01" }[random.Next(3)] : f.Key == "xsuf" ? new[] { "", "A", "B", "A-B" }[random.Next(4)] : new[] { f.Default, "", "8", "120", "高速公路", "m" }[random.Next(6)];
                                break;
                        }

                        spec = spec.With(f.Key, value);
                    }

                    GuideBoard board = t.Layout(spec);
                    boards++;
                    string what = t.Id + " [" + spec + "]";
                    Program.Check(board.Parts.Count >= 1 && board.Main.W > 0f && board.Main.H > 0f && !float.IsNaN(board.Main.W + board.Main.H), what + ": a board with a size");

                    foreach (GuidePart part in board.Parts)
                    {
                        if (part.Safe != null)
                        {
                            foreach (GuideElement figure in part.Elements)
                            {
                                if (figure.Art)
                                    CheckInside(part.Safe, figure, what + ": the figure");
                            }
                        }

                        float inL = part.Edge - 1e-4f, inR = part.W - part.Edge + 1e-4f, inT = part.Edge - 1e-4f, inB = part.H - part.Edge + 1e-4f;
                        foreach (GuideElement e in part.Elements)
                        {
                            // A player's picture may be larger than its board or moved off it (0.12.31): the painter clips it to the board.
                            bool inside = e.Kind == GuideElementKind.Image || (e.X >= inL && e.Right <= inR && e.Y >= inT && e.Bottom <= inB);
                            Program.Check(inside, $"{what}: a {e.Kind} '{e.Text}' at ({e.X:0.000},{e.Y:0.000}) {e.W:0.000}x{e.H:0.000} is inside the part {part.W:0.000}x{part.H:0.000}");
                            Program.Check(e.W >= 0f && e.H >= 0f && !float.IsNaN(e.X + e.Y + e.W + e.H), what + ": a finite element");
                        }

                        // Texts and arrows never overlap each other (the plates' own parts are inside Rect elements, and the strip's text is above the number).
                        List<GuideElement> leaves = Leaves(part);
                        for (int i = 0; i < leaves.Count; i++)
                        {
                            for (int j = i + 1; j < leaves.Count; j++)
                            {
                                // The ring roads' two digits stand against the number: their boxes touch at most.
                                Program.Check(!Overlap(leaves[i], leaves[j]), $"{what}: '{leaves[i].Text}' and '{leaves[j].Text}' do not overlap");
                            }
                        }

                        // A text never needs more room than its box (the painter would have to squeeze it): cells and digits by the advance table.
                        foreach (GuideElement e in part.Elements)
                        {
                            if (e.Kind != GuideElementKind.Text || e.Text.Length == 0)
                                continue;
                            if (e.Mode == GuideTextMode.Cell)
                                Program.Check(GuideKit.CellWidth(e.Text.Length, e.H, e.Gap) <= e.W + 1e-3f, $"{what}: the cells of '{e.Text}' fit their box");
                            else if (e.InkRef == null && !e.Art)
                                Program.Check(GuideKit.LatinWidth(e.Text, e.H) <= e.W * 1.02f + 1e-3f, $"{what}: '{e.Text}' fits its box");
                        }
                    }

                    // A plate above the board does not touch it, and sits over it (a lamp-post ad's second part is the other one of a pair: beside it).
                    for (int i = 1; i < board.Parts.Count && board.PoleAd == null; i++)
                    {
                        GuidePart plate = board.Parts[i];
                        Program.Check(plate.Y + plate.H <= 0f, what + ": the plate is above the board's top edge");
                        Program.Check(plate.X >= -1e-4f && plate.X + plate.W <= board.Main.W + 1e-4f, what + ": the plate is within the board's width");
                    }
                }
            }

            Program.Check(boards >= 1500, "the sweep covered many boards: " + boards);
        }

        // ------------------------------------------------------------------ arrows and clipping

        private static void Arrows()
        {
            // The tip is the vertex furthest from the middle; its direction is the arrow's.
            (GuideArrow arrow, float dx, float dy)[] directions =
            {
                (GuideArrow.Up, 0f, -1f), (GuideArrow.Down, 0f, 1f), (GuideArrow.Left, -1f, 0f), (GuideArrow.Right, 1f, 0f),
                (GuideArrow.UpRight, 0.7071f, -0.7071f), (GuideArrow.UpLeft, -0.7071f, -0.7071f)
            };
            foreach ((GuideArrow arrow, float dx, float dy) in directions)
            {
                float[] p = GuideKit.ArrowPolygon(arrow, 10f, 20f, 2f);
                Program.Check(p.Length == 14, arrow + ": seven vertices");
                for (int i = 0; i < p.Length; i += 2)
                    Program.Check(p[i] >= 10f - 1e-4f && p[i] <= 12f + 1e-4f && p[i + 1] >= 20f - 1e-4f && p[i + 1] <= 22f + 1e-4f, $"{arrow}: the vertex {i / 2} is inside its square");

                // The first vertex is the tip; the middle of the tail edge (vertices 3 and 4) is the other end: the arrow points from the one to the other.
                float tailX = (p[6] + p[8]) * 0.5f, tailY = (p[7] + p[9]) * 0.5f;
                float vx = p[0] - tailX, vy = p[1] - tailY;
                float length = (float)Math.Sqrt(vx * vx + vy * vy);
                float tx = vx / length, ty = vy / length;
                Program.Check(tx * dx + ty * dy > 0.999f, $"{arrow}: the arrow points ({tx:0.00}, {ty:0.00}), expected ({dx:0.00}, {dy:0.00})");
                // It fills the square: straight arrows touch two sides, the diagonal ones touch it at the limit of their fit.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < p.Length; i += 2)
                {
                    minX = Math.Min(minX, p[i]); maxX = Math.Max(maxX, p[i]); minY = Math.Min(minY, p[i + 1]); maxY = Math.Max(maxY, p[i + 1]);
                }

                Program.Check(Math.Max(maxX - minX, maxY - minY) > 1.9f, $"{arrow}: it fills the square");
            }

            // A diagonal arrow is the straight one turned 45 degrees: its head is as wide.
            float[] up = GuideKit.ArrowPolygon(GuideArrow.Up, 0f, 0f, 1f);
            Program.Near(up[2] - up[12], 0.62f, 1e-4f, "the head of the straight arrow is 0.62 of its square");
        }

        private static void Clipping()
        {
            float[] big = { -10f, -10f, 10f, -10f, 10f, 10f, -10f, 10f };
            float[] c = GuideKit.ClipToBox(big, 0f, 0f, 2f, 3f);
            Program.Check(c.Length == 8, "a polygon that covers the box clips to the box");
            Program.Near(Area(c), 6f, 1e-4f, "its area is the box's");
            float[] triangle = { 0f, 0f, 4f, 0f, 0f, 4f };
            Program.Near(Area(GuideKit.ClipToBox(triangle, 0f, 0f, 2f, 2f)), 4f - 0f, 1e-4f, "a triangle cut by a box");
            Program.Check(GuideKit.ClipToBox(new[] { 5f, 5f, 6f, 5f, 6f, 6f }, 0f, 0f, 1f, 1f).Length == 0, "a polygon outside the box leaves nothing");
        }

        private static float Area(float[] p)
        {
            float a = 0f;
            for (int i = 0; i < p.Length; i += 2)
            {
                int j = (i + 2) % p.Length;
                a += p[i] * p[j + 1] - p[j] * p[i + 1];
            }

            return Math.Abs(a) * 0.5f;
        }

        // ------------------------------------------------------------------ instances and meshes

        private static void Instances()
        {
            SignDef exit = SignCatalog.ById("guide.exit.advance");
            SignInstance a = SignInstances.Of(exit, "road=g;code=G204");
            SignInstance b = SignInstances.Of(exit, "road=g;code=G204");
            SignInstance c = SignInstances.Of(exit, "road=g;code=G205");
            Program.Check(ReferenceEquals(a, b), "the same settings give the same instance");
            Program.Check(a.Key != c.Key, "other settings, another key");
            Program.Check(ReferenceEquals(SignInstances.FromKey(a.Key), a), "an instance is found again from its key");
            Program.Check(SignInstances.FromKey("nothing#x") == null, "a key of no kind gives null");
            Program.Check(SignInstances.Of(exit, null).Spec == exit.Template.Default().ToString() && SignInstances.Of(exit, "").Key == SignInstances.Of(exit, null).Key, "no spec is the defaults");
            Program.Check(a.Def != exit && a.Def.Template == exit.Template && Math.Abs(a.Def.Width - a.Board.Main.W) < 1e-6f, "an instance's def has the board's size and the catalogue's is not touched");

            foreach (SignChoice choice in SignCatalog.Choices)
            {
                SignInstance i = SignInstances.Of(choice.Def, choice.Spec);
                Program.Check(i.TexW >= 16 && i.TexH >= 16 && i.TexW <= SignInstance.MaxTexture + 4 && i.TexH <= SignInstance.MaxTexture + 4, choice.Id + ": the picture's size " + i.TexW + "x" + i.TexH);
                if (i.IsBoard)
                {
                    Program.Check(i.PixelsPerMetre >= SignInstance.MinPixelsPerMetre - 1e-3f && i.PixelsPerMetre <= SignInstance.MaxPixelsPerMetre + 1e-3f, choice.Id + ": pixels to the metre " + i.PixelsPerMetre);
                    Program.Check(i.TexW % 4 == 0 && i.TexH % 4 == 0, choice.Id + ": a picture whose sides are multiples of 4");
                }
            }

            // A board with a plate has a taller picture box than its main board.
            SignInstance plated = SignInstances.Of(exit, "xnum=145");
            SignInstance bare = SignInstances.Of(exit, "xnum=");
            Program.Check(plated.BoundsY0 < 0f && bare.BoundsY0 == 0f, "the plate is in the picture's box");
            Program.Near(plated.Def.ExtraTop, -plated.BoundsY0, 1e-6f, "ExtraTop is how far the plate reaches");
        }

        private static void BoardMeshes()
        {
            SignDef exit = SignCatalog.ById("guide.exit.advance");
            foreach (SupportDef support in SupportCatalog.All)
            {
                SignInstance plated = SignInstances.Of(exit, "xnum=145;xside=r");
                SignParts parts = SignBuilder.Build(plated, support);
                string what = "plated board on " + support.Id;
                Program.Check(parts.Picks.Count >= (SupportKinds.Hosted(support.Kind) || support.Kind == SupportKind.WallFlat || SupportKinds.OverRoad(support.Kind) ? 2 : 3), what + ": the board, the plate and the support can be pointed at (an attached support and one flat on a wall have no post to point at)");
                int boards = 0;
                foreach (PickShape p in parts.Picks)
                    boards += p.Name == "board" ? 1 : 0;
                Program.Check(boards == 2, what + ": two pick shapes for boards (the board and the plate): " + boards);

                // The plate sits above the board's top, at the right of the viewer = -X of the frame.
                parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                Program.Near(fMax.Y - fMin.Y, plated.BoundsY1 - plated.BoundsY0, 0.01f, what + ": the face's height includes the plate");
                Program.Check(parts.Layout.PoleTop >= fMax.Y + 0.09f || SupportKinds.Hung(support.Kind) && parts.Layout.PoleTop >= parts.Layout.ArmY && parts.Layout.ArmY < parts.Layout.BoardTop || support.Kind == SupportKind.WallFlat || SupportKinds.OverRoad(support.Kind)
                    || support.Kind == SupportKind.AttachedArm && parts.Layout.HangY < parts.Layout.BoardTop && parts.Layout.HangY > parts.Layout.BottomClearance
                    || parts.Layout.PoleTop < parts.Layout.BoardTop,
                    what + ": the support clears the plate (a post ends behind the board, an arm or beam passes behind it, the plate stands above; flat on a wall nothing is above it)");

                // The picture covers the plate too: the plate's vertices map into the upper right of the picture.
                float boardTopV = 1f - (0f - plated.BoundsY0) / (plated.BoundsY1 - plated.BoundsY0);
                int above = 0;
                float maxU = 0f;
                for (int i = 0; i < parts.Face.VertexCount; i++)
                {
                    if (parts.Face.Positions[i].Y > parts.Layout.BoardTop + 0.01f)
                    {
                        above++;
                        Program.Check(parts.Face.V[i] >= boardTopV - 1e-3f, what + ": a vertex of the plate maps above the board's part of the picture");
                        maxU = Math.Max(maxU, parts.Face.U[i]);
                    }
                }

                Program.Check(above > 0 && maxU > 0.9f, what + ": the plate is at the right of the picture (u up to " + maxU + ")");

                // A left plate is on the left.
                SignInstance left = SignInstances.Of(exit, "xnum=145;xside=l");
                SignParts leftParts = SignBuilder.Build(left, support);
                float minU = 1f;
                for (int i = 0; i < leftParts.Face.VertexCount; i++)
                {
                    if (leftParts.Face.Positions[i].Y > leftParts.Layout.BoardTop + 0.01f)
                        minU = Math.Min(minU, leftParts.Face.U[i]);
                }

                Program.Check(minU < 0.01f, what + ": a left plate reaches the left of the picture");
            }
        }

        // ------------------------------------------------------------------ glyphs

        /// <summary>The filled fraction of the 100 x 100 box by the non-zero winding rule (as SVG and the painter fill), on a grid of n x n samples.</summary>
        internal static float FilledFraction(List<float[]> contours, int n = 80)
        {
            int filled = 0;
            for (int iy = 0; iy < n; iy++)
            {
                for (int ix = 0; ix < n; ix++)
                {
                    float x = (ix + 0.5f) * 100f / n, y = (iy + 0.5f) * 100f / n;
                    int winding = 0;
                    foreach (float[] c in contours)
                    {
                        int count = c.Length / 2;
                        for (int i = 0; i < count; i++)
                        {
                            int j = (i + 1) % count;
                            float x0 = c[i * 2], y0 = c[i * 2 + 1], x1 = c[j * 2], y1 = c[j * 2 + 1];
                            if (y0 <= y)
                            {
                                if (y1 > y && (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0) > 0f)
                                    winding++;
                            }
                            else if (y1 <= y && (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0) < 0f)
                            {
                                winding--;
                            }
                        }
                    }

                    if (winding != 0)
                        filled++;
                }
            }

            return (float)filled / (n * n);
        }

        private static void GlyphBuilder()
        {
            // The path reader: lines, relative moves, arcs, curves, flags written without separators.
            List<float[]> square = GuidePath.Parse("M10 10H90V90H10Z");
            Program.Check(square.Count == 1 && square[0].Length == 8, "a square is one contour of four corners");
            Program.Near(GuidePath.Area(square[0]), 6400f, 1f, "its area");
            Program.Check(GuidePath.Parse("m10 10h80v80h-80z").Count == 1 && Math.Abs(GuidePath.Area(GuidePath.Parse("m10 10h80v80h-80z")[0]) - 6400f) < 1f, "relative commands give the same square");
            Program.Check(GuidePath.Parse("M10 10L90 10 90 90 10 90ZM30 30L70 30 70 70 30 70Z").Count == 2, "two subpaths, two contours");
            Program.Check(GuidePath.Parse("").Count == 0 && GuidePath.Parse(null).Count == 0 && GuidePath.Parse("garbage").Count == 0, "nothing and garbage give nothing");
            Program.Check(GuidePath.Parse("M10 10L90 10L50 90Zxyz").Count == 1, "unknown commands end the reading but keep what was read");
            List<float[]> run = GuidePath.Parse("M1.5.5 20 20 10-5Z");
            Program.Check(run.Count == 1 && run[0].Length == 6 && Math.Abs(run[0][0] - 1.5f) < 1e-4f && Math.Abs(run[0][1] - 0.5f) < 1e-4f && Math.Abs(run[0][4] - 10f) < 1e-4f && Math.Abs(run[0][5] + 5f) < 1e-4f, "numbers run together as SVG allows (1.5.5 is 1.5 and .5, 10-5 is 10 and -5)");

            // A circle by arcs: the area of pi r^2, within the cutting of the arc into straight pieces.
            List<float[]> circle = GuidePath.Parse(GuidePath.Circle(50f, 50f, 30f));
            Program.Check(circle.Count == 1, "a circle is one contour");
            Program.Near(GuidePath.Area(circle[0]), (float)(Math.PI * 900.0), 25f, "a circle of 30: area");
            // The flags without separators: "a30 30 0 1 0" and "a30 30 0 10".
            Program.Near(GuidePath.Area(GuidePath.Parse("M20 50a30 30 0 1030 0")[0]), GuidePath.Area(GuidePath.Parse("M20 50a30 30 0 1 0 30 0")[0]), 1f, "arc flags written together");
            // Cubic and quadratic curves end where they say.
            float[] cubic = GuidePath.Parse("M0 0C30 0 70 100 100 100L0 100Z")[0];
            Program.Check(cubic.Length / 2 >= GuidePath.CurveSteps && Math.Abs(cubic[cubic.Length - 4] - 100f) < 1e-3f, "a cubic ends at its end point");

            // The builder: a solid is counter-clockwise on the screen (negative area when y is down: the sum of x0 y1 - x1 y0 is negative), a hole is clockwise.
            float Signed(float[] c)
            {
                float a = 0f;
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    int j = (i + 2) % c.Length;
                    a += c[i] * c[j + 1] - c[j] * c[i + 1];
                }

                return a * 0.5f;
            }

            List<float[]> solid = GuidePath.Parse(new PicBuilder().Rect(20f, 20f, 60f, 60f).ToString());
            List<float[]> hole = GuidePath.Parse(new PicBuilder().Rect(20f, 20f, 60f, 60f, true).ToString());
            Program.Check(Signed(solid[0]) < 0f && Signed(hole[0]) > 0f, "a solid and a hole are wound opposite ways");
            Program.Check(Signed(GuidePath.Parse(new PicBuilder().Poly(false, 10f, 10f, 10f, 90f, 90f, 90f).ToString())[0]) < 0f && Signed(GuidePath.Parse(new PicBuilder().Poly(false, 10f, 10f, 90f, 90f, 10f, 90f).ToString())[0]) < 0f, "a polygon is made counter-clockwise whichever way it is given");

            // A ring has a hole: the filled fraction is the ring's, not the disc's.
            List<float[]> ring = GuidePath.Parse(new PicBuilder().Ring(50f, 50f, 40f, 25f).ToString());
            float ringFraction = FilledFraction(ring);
            Program.Near(ringFraction, (float)(Math.PI * (1600.0 - 625.0) / 10000.0), 0.02f, "a ring is filled between its two circles only");
            // Two overlapping solids make one shape: no hole where they overlap.
            List<float[]> two = GuidePath.Parse(new PicBuilder().Rect(10f, 10f, 50f, 50f).Rect(40f, 40f, 50f, 50f).ToString());
            Program.Near(FilledFraction(two), (2500f + 2500f - 400f) / 10000f, 0.02f, "overlapping solids join (the overlap is not cut out)");
            // A hole cuts only what is under it.
            List<float[]> cut = GuidePath.Parse(new PicBuilder().Rect(10f, 10f, 80f, 80f).Rect(30f, 30f, 40f, 40f, true).ToString());
            Program.Near(FilledFraction(cut), (6400f - 1600f) / 10000f, 0.02f, "a hole is cut out of the solid under it");

            // A capsule: its area is the rectangle and the two half circles; both caps outward (the first version had them inward).
            List<float[]> capsule = GuidePath.Parse(new PicBuilder().Capsule(20f, 50f, 80f, 50f, 20f).ToString());
            Program.Near(FilledFraction(capsule), (60f * 20f + (float)(Math.PI * 100.0)) / 10000f, 0.02f, "a capsule is a bar with round ends");
            GuidePath.Bounds(capsule, out float minX, out _, out float maxX, out _);
            Program.Check(Math.Abs(minX - 10f) < 0.5f && Math.Abs(maxX - 90f) < 0.5f, "a capsule reaches a radius beyond each end: " + minX + ", " + maxX);
            List<float[]> diagonal = GuidePath.Parse(new PicBuilder().Capsule(20f, 20f, 80f, 80f, 10f).ToString());
            GuidePath.Bounds(diagonal, out float dx0, out float dy0, out float dx1, out float dy1);
            Program.Check(Math.Abs(dx0 - 15f) < 0.5f && Math.Abs(dx1 - 85f) < 0.5f && Math.Abs(dy0 - 15f) < 0.5f && Math.Abs(dy1 - 85f) < 0.5f, "a diagonal capsule's ends are round: " + dx0 + ".." + dx1);

            // Mirror: x' = 100 - x, the fill is the same (the contours are turned round so that they keep their winding).
            string flipped = Glyphs.Mirror(new PicBuilder().Rect(10f, 20f, 30f, 60f).Circle(70f, 50f, 15f).ToString());
            List<float[]> mirrored = GuidePath.Parse(flipped);
            GuidePath.Bounds(mirrored, out float mx0, out _, out float mx1, out _);
            Program.Check(Math.Abs(mx0 - 15f) < 0.5f && Math.Abs(mx1 - 90f) < 0.5f, "a mirrored glyph is on the other side (the rectangle 10..40 goes to 60..90, the circle 55..85 to 15..45): " + mx0 + ".." + mx1);
            Program.Near(FilledFraction(mirrored), FilledFraction(GuidePath.Parse(new PicBuilder().Rect(10f, 20f, 30f, 60f).Circle(70f, 50f, 15f).ToString())), 0.01f, "and as large");

            // The rounded rectangle and the helper paths.
            Program.Near(FilledFraction(GuidePath.Parse(GuidePath.RoundRect(10f, 10f, 80f, 80f, 10f))), (6400f - (4f - (float)Math.PI) * 100f) / 10000f, 0.02f, "a rounded rectangle");
            Program.Near(FilledFraction(GuidePath.Parse(new PicBuilder().RoundRect(10f, 10f, 80f, 80f, 10f).ToString())), FilledFraction(GuidePath.Parse(GuidePath.RoundRect(10f, 10f, 80f, 80f, 10f))), 0.01f, "the builder's and the helper's are the same");
        }

        private static void EveryGlyph()
        {
            IReadOnlyList<KeyValuePair<string, string>> glyphs = Glyphs.All();
            Program.Check(glyphs.Count >= 1, "there are glyphs");
            var seen = new HashSet<string>();
            foreach (KeyValuePair<string, string> glyph in glyphs)
            {
                string what = "glyph " + glyph.Key;
                Program.Check(seen.Add(glyph.Key), what + ": a name once");
                List<float[]> contours = GuidePath.Parse(glyph.Value);
                Program.Check(contours.Count >= 1, what + ": has a contour");
                if (contours.Count == 0)
                    continue;
                foreach (float[] c in contours)
                {
                    Program.Check(c.Length >= 6 && GuidePath.Area(c) > 0.05f, what + ": no degenerate contour (area " + GuidePath.Area(c) + ")");
                    foreach (float v in c)
                        Program.Check(!float.IsNaN(v) && !float.IsInfinity(v), what + ": finite coordinates");
                }

                GuidePath.Bounds(contours, out float minX, out float minY, out float maxX, out float maxY);
                Program.Check(minX >= -0.5f && minY >= -0.5f && maxX <= 100.5f && maxY <= 100.5f, $"{what}: inside the box ({minX:0.#},{minY:0.#})-({maxX:0.#},{maxY:0.#})");
                Program.Check(Math.Max(maxX - minX, maxY - minY) >= 55f, $"{what}: fills the box ({maxX - minX:0.#} x {maxY - minY:0.#})");
                float fraction = FilledFraction(contours);
                Program.Check(fraction >= 0.03f && fraction <= 0.80f, $"{what}: neither empty nor a solid block ({fraction:0.00} filled)");
                Program.Check(Glyphs.Mirror(glyph.Value).Length > 0 && Math.Abs(FilledFraction(GuidePath.Parse(Glyphs.Mirror(glyph.Value))) - fraction) < 0.01f, what + ": mirrors to the same fill");
            }
        }

        /// <summary>
        /// Every vertex of what an element draws is inside the safe region (a polygon, x, y pairs, in the part's frame). The points are: all vertices of a path or polygon, the four corners of a text
        /// or a rectangle (a text's ink is inside its box), the eight extreme points of an oval. A figure that crosses the ring of a prohibition sign or the edge of a warning sign fails here.
        /// </summary>
        internal static void CheckInside(float[] safe, GuideElement e, string what)
        {
            var points = new List<float>();
            switch (e.Kind)
            {
                case GuideElementKind.Path:
                    foreach (float[] c in e.Contours)
                        points.AddRange(c);
                    break;
                case GuideElementKind.Polygon:
                case GuideElementKind.Arrow:
                    points.AddRange(e.Points);
                    break;
                case GuideElementKind.Oval:
                    for (int k = 0; k < 8; k++)
                    {
                        double a = Math.PI * 0.25 * k;
                        points.Add(e.X + e.W * 0.5f + e.W * 0.5f * (float)Math.Cos(a));
                        points.Add(e.Y + e.H * 0.5f + e.H * 0.5f * (float)Math.Sin(a));
                    }

                    break;
                default:
                {
                    float x0 = e.X, x1 = e.Right;
                    if (e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Ink && !string.IsNullOrEmpty(e.Text))
                    {
                        // The ink of letters and digits is as wide as the font makes it (estimated by the advance table), squeezed to the box if wider, and aligned in it.
                        float ink = Math.Min(GuideKit.LatinWidth(e.Text, e.H), e.W);
                        x0 = e.Align == GuideAlign.Left ? e.X : e.Align == GuideAlign.Right ? e.Right - ink : e.X + (e.W - ink) * 0.5f;
                        x1 = x0 + ink;
                    }

                    points.AddRange(new[] { x0, e.Y, x1, e.Y, x1, e.Bottom, x0, e.Bottom });
                    break;
                }
            }

            for (int i = 0; i + 1 < points.Count; i += 2)
            {
                if (!PointInPolygon(safe, points[i], points[i + 1]))
                {
                    Program.Check(false, $"{what} has a point ({points[i]:0.000}, {points[i + 1]:0.000}) outside the safe region");
                    return;
                }
            }

            Program.Check(true, what + " is inside the safe region");
        }

        internal static bool PointInPolygon(float[] polygon, float x, float y)
        {
            bool inside = false;
            int n = polygon.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = polygon[i * 2], yi = polygon[i * 2 + 1], xj = polygon[j * 2], yj = polygon[j * 2 + 1];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    inside = !inside;
            }

            return inside;
        }

        private static void YieldCharacter()
        {
            // The character of the yield sign clears the red edge and the slanting sides by at least a tenth of its size, in every band (the first version was too big and too high).
            for (int band = 0; band < 4; band++)
            {
                GuideBoard board = SignStyles.Yield(band);
                GuidePart part = board.Main;
                GuideElement ch = null;
                foreach (GuideElement e in part.Elements)
                {
                    if (e.Kind == GuideElementKind.Text && e.Text == "让")
                        ch = e;
                }

                Program.Check(ch != null && ch.Art, "band " + band + ": the character is part of the figure");
                float margin = 0.06f * ch.W;
                // Grow the cell by the margin and ask again: still inside the white field.
                var grown = new GuideElement { Kind = GuideElementKind.Rect, X = ch.X - margin, Y = ch.Y - margin, W = ch.W + 2f * margin, H = ch.H + 2f * margin };
                CheckInside(part.Safe, grown, $"yield band {band}: the character with a margin of a sixteenth");
                // It is not tiny either: a quarter of the side or more.
                Program.Check(ch.W >= 0.22f * part.Side, $"yield band {band}: the character is a quarter of the side ({ch.W / part.Side:0.00})");
                // And centred across.
                Program.Near(ch.X + ch.W * 0.5f, part.W * 0.5f, 1e-4f, $"yield band {band}: centred across");
            }
        }

        private static void EverySignEveryBand()
        {
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                if (!t.Standalone)
                    continue;
                foreach (string band in new[] { "100", "80", "60", "30", "auto" })
                {
                    GuideSpec spec = t.Default().With("speed", band);
                    GuideBoard board = t.Layout(spec);
                    string what = t.Id + " at " + band;
                    Program.Check(board.Parts.Count >= 1 && board.Main.W > 0f && board.Main.H > 0f, what + ": a board with a size");
                    foreach (GuidePart part in board.Parts)
                    {
                        foreach (GuideElement e in part.Elements)
                        {
                            if (e.Art && part.Safe != null)
                                CheckInside(part.Safe, e, $"{what}: the figure ({e.Kind} '{e.Text}')");
                            Program.Check(!float.IsNaN(e.X + e.Y + e.W + e.H), what + ": a finite element");
                            // An element stays in the part's box (a little over for rounding).
                            Program.Check(e.X >= -1e-3f && e.Y >= -1e-3f && e.Right <= part.W + 1e-3f && e.Bottom <= part.H + 1e-3f, $"{what}: a {e.Kind} at ({e.X:0.000},{e.Y:0.000}) {e.W:0.000}x{e.H:0.000} is inside the part {part.W:0.000}x{part.H:0.000}");
                        }
                    }
                }

                // Smaller bands give smaller signs.
                float w100 = t.Layout(t.Default().With("speed", "100")).Main.W, w30 = t.Layout(t.Default().With("speed", "30")).Main.W;
                Program.Check(w30 <= w100 + 1e-4f, t.Id + ": the slowest band is not larger than the fastest");
            }
        }

        private static void SpeedUnits()
        {
            // The log of 0.4.8: on a road of 100 km/h the road's figure and the lanes' were both 27.778 (metres per second). 0.4.2 to 0.4.8 used the road's as km/h: 27.8 and the smallest band.
            Program.Check(SpeedMath.TryKmh(27.778f, 27.778f, out float kmh), "a road with both figures has a limit");
            Program.Near(kmh, 100f, 0.01f, "27.778 from the road's figure is 100 km/h: " + kmh);
            Program.Check(GuideTemplates.BandForSpeed(kmh) == "100", "and is the 100 band, not the 30 band (it was 27.8 km/h)");

            // The road's own figure is taken first, the lanes' when there is none; both in metres per second.
            Program.Check(SpeedMath.TryKmh(-1f, 22.222f, out float lanesOnly), "no road figure: the lanes'");
            Program.Near(lanesOnly, 80f, 0.01f, "22.222 from the lanes is 80 km/h");
            Program.Check(SpeedMath.TryKmh(13.889f, 27.778f, out float both), "the road's figure first");
            Program.Near(both, 50f, 0.01f, "13.889 beats the lanes' 27.778: 50 km/h");
            Program.Check(!SpeedMath.TryKmh(-1f, 0f, out float none) && none == 0f, "neither figure: no limit");
            Program.Check(!SpeedMath.TryKmh(0f, -1f, out _), "a lane with no limit and no road figure: no limit");

            // The limits the game's roads state (km/h, as the prefab) kept as the game keeps them, and the band they fall in.
            (float prefabKmh, string band)[] roads = { (120f, "100"), (100f, "100"), (80f, "80"), (70f, "60"), (60f, "60"), (50f, "60"), (40f, "60"), (30f, "30"), (20f, "30") };
            foreach ((float prefabKmh, string band) in roads)
            {
                SpeedMath.TryKmh(prefabKmh / 3.6f, prefabKmh / 3.6f, out float back);
                Program.Near(back, prefabKmh, 0.01f, $"{prefabKmh} km/h kept as {prefabKmh / 3.6f:0.###} comes back as {back}");
                Program.Check(GuideTemplates.BandForSpeed(back) == band, $"{prefabKmh} km/h is the {band} band: {GuideTemplates.BandForSpeed(back)}");
            }
        }

        private static void SpeedBands()
        {
            // Table 3: 100 to 120, 71 to 99, 40 to 70, under 40 km/h.
            Program.Check(GuideTemplates.BandForSpeed(120f) == "100" && GuideTemplates.BandForSpeed(100f) == "100" && GuideTemplates.BandForSpeed(99f) == "80" && GuideTemplates.BandForSpeed(80f) == "80" && GuideTemplates.BandForSpeed(71f) == "80", "100 and above: the 100 band; 71 to 99: the 80 band");
            Program.Check(GuideTemplates.BandForSpeed(70f) == "60" && GuideTemplates.BandForSpeed(60f) == "60" && GuideTemplates.BandForSpeed(50f) == "60" && GuideTemplates.BandForSpeed(40f) == "60", "40 to 70: the 60 band");
            Program.Check(GuideTemplates.BandForSpeed(39f) == "30" && GuideTemplates.BandForSpeed(20f) == "30" && GuideTemplates.BandForSpeed(0f) == "30", "under 40: the 30 band");
            Program.Near(GuideTemplates.CharHeightOf("100"), 0.65f, 1e-6f, "65 cm");
            Program.Near(GuideTemplates.CharHeightOf("80"), 0.55f, 1e-6f, "55 cm");
            Program.Near(GuideTemplates.CharHeightOf("60"), 0.40f, 1e-6f, "40 cm");
            Program.Near(GuideTemplates.CharHeightOf("30"), 0.30f, 1e-6f, "30 cm");
            Program.Check(GuideTemplates.CharHeightOf("auto") == GuideTemplates.CharHeightOf("100"), "a board laid out before the road is known has the largest characters (it is only a thumbnail then)");

            // The Table 3 ranges: the character heights are inside them (60 to 70, 50 to 60, 35 to 50, 25 to 30 cm).
            Program.Check(GuideTemplates.CharHeightOf("100") >= 0.60f && GuideTemplates.CharHeightOf("100") <= 0.70f, "100 band in 60 to 70 cm");
            Program.Check(GuideTemplates.CharHeightOf("80") >= 0.50f && GuideTemplates.CharHeightOf("80") <= 0.60f, "80 band in 50 to 60 cm");
            Program.Check(GuideTemplates.CharHeightOf("60") >= 0.35f && GuideTemplates.CharHeightOf("60") <= 0.50f, "60 band in 35 to 50 cm");
            Program.Check(GuideTemplates.CharHeightOf("30") >= 0.25f && GuideTemplates.CharHeightOf("30") <= 0.30f, "30 band in 25 to 30 cm");

            // A draft that says auto takes the road's band; anything else is left as it is; a sign without the setting is untouched.
            SignDef exit = SignCatalog.ById("guide.exit.advance");
            string draft = exit.Template.Default().ToString();
            Program.Check(exit.Template.Parse(draft).Is("speed", "auto"), "the default of a guide board is automatic");
            Program.Check(exit.Template.Parse(GuideTemplates.ResolveSpeed(exit, draft, 50f, true)).Is("speed", "60"), "50 km/h: the 60 band");
            Program.Check(exit.Template.Parse(GuideTemplates.ResolveSpeed(exit, draft, 100f, true)).Is("speed", "100"), "100 km/h: the 100 band");
            Program.Check(exit.Template.Parse(GuideTemplates.ResolveSpeed(exit, draft, 0f, false)).Is("speed", "60"), "no road: the 60 band (a city)");
            string fixedSpec = exit.Template.Parse(draft).With("speed", "80").ToString();
            Program.Check(GuideTemplates.ResolveSpeed(exit, fixedSpec, 30f, true) == fixedSpec, "a band that was chosen by hand is kept whatever the road says");
            SignDef countdown = SignCatalog.ById("guide.countdown");
            Program.Check(GuideTemplates.ResolveSpeed(countdown, "", 30f, true) == "", "a sign without the setting is untouched");

            // The boards get smaller with the band, in proportion: 40 to 70 km/h is 40/65 of the 100 band.
            SignInstance big = SignInstances.Of(exit, "speed=100;xnum=145"), small = SignInstances.Of(exit, "speed=60;xnum=145");
            Program.Near(small.Def.Width / big.Def.Width, 0.40f / 0.65f, 1e-3f, "the board of the 60 band is 40/65 of the 100 band's");
            Program.Near(small.Def.Height / big.Def.Height, 0.40f / 0.65f, 1e-3f, "and as high");
            Program.Check(SignInstances.Of(exit, "speed=auto").Def.Width == big.Def.Width, "automatic is laid out like the 100 band until the road is known");
        }

        private static void GantryRules()
        {
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            Program.Check(gantry != null && gantry.Kind == SupportKind.Gantry && gantry.Clearance >= 5f && SupportCatalog.ByHash(gantry.Hash) == gantry, "the gantry is in the catalogue");
            SignDef board = Board(ShapeKind.RoundRect, 4.0f, 3.0f, 0.9f);

            // The span is the reach asked for, but never less than the board and a metre each side, never under 6 m, never over 40.
            Program.Near(SupportLayouts.Of(board, gantry, 20f).ArmLength, 20f, 1e-4f, "a span of 20 m is 20 m");
            Program.Near(SupportLayouts.Of(board, gantry, 0f).ArmLength, SupportLayouts.DefaultSpan, 1e-4f, "no reach: the default span");
            Program.Near(SupportLayouts.Of(board, gantry, 3f).ArmLength, 6f, 1e-4f, "a small span is made 6 m: the board needs 6 m (4 + 2 x 1)");
            Program.Near(SupportLayouts.Of(board, gantry, 80f).ArmLength, SupportLayouts.MaxSpan, 1e-4f, "never more than 40 m");
            Program.Near(SupportLayouts.Of(Board(ShapeKind.RoundRect, 9f, 3f), gantry, 8f).ArmLength, 11f, 1e-4f, "a board of 9 m on a span asked at 8 m: 11 m");

            // The posts get thicker with the span; the beam deeper over 20 m; the board is in the middle.
            SupportLayout shortSpan = SupportLayouts.Of(board, gantry, 10f), longSpan = SupportLayouts.Of(board, gantry, 30f);
            Program.Check(longSpan.BeamWidth > 0f && shortSpan.BeamWidth == 0f, "over 16 m a space truss on twin legs, to 16 m a planar truss (22MR601 pp.94-107)");
            Program.Near(shortSpan.BoardCentreX, 5f, 1e-4f, "the board hangs in the middle of the span");
            Program.Check(shortSpan.PoleX.Length == 2 && shortSpan.PoleX[0] == 0f && Math.Abs(shortSpan.PoleX[1] - 10f) < 1e-4f, "posts at both ends: 0 and the span");

            // The mesh: two foundations, a beam as long as the span, the board hung under it.
            SignInstance exit = SignInstances.Of(SignCatalog.ById("guide.exit.advance"), "xnum=145");
            SignParts parts = SignBuilder.Build(exit, gantry, 18f);
            Program.Check(parts.Layout.ArmLength >= 18f - 1e-4f, "the built gantry has the span asked: " + parts.Layout.ArmLength);
            parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
            Program.Check(mMax.X - mMin.X >= 18f, "the metal spans the road: " + (mMax.X - mMin.X));
            parts.Concrete.Bounds(out Vec3 cMin, out Vec3 cMax);
            Program.Check(cMax.X - cMin.X >= 18f, "and so does the concrete (a foundation each side)");
            int posts = 0, beams = 0, feet = 0, grown = 0;
            foreach (PickShape p in parts.Picks)
            {
                posts += p.Name == "post" && p.Grow == 0f ? 1 : 0;
                grown += p.Name == "post" && p.Grow > 0f ? 1 : 0;
                beams += p.Name == "arm" ? 1 : 0;
                feet += p.Name == "foot" ? 1 : 0;
            }

            Program.Check(posts == 2 && beams == 1 && feet == 2, $"two posts, one beam, two feet can be pointed at: {posts} {beams} {feet}");
            Program.Check(grown == 1, "and the first post's room for small boards, grown for the ray (0.12.8): " + grown);
            SignParts longer = SignBuilder.Build(exit, gantry, 30f);
            Program.Check(longer.Metal.VertexCount > parts.Metal.VertexCount, "a longer span has more truss");

            // The choice of the form by the road (4.8.12 c): three lanes or more in the direction, an exit on the left or on a wide ramp: a gantry, for the guide boards.
            SignDef advance = SignCatalog.ById("guide.exit.advance");
            SignDef inst = SignInstances.Of(advance, null).Def;
            Program.Check(SupportPlanner.AutoFor(inst, new SupportContext { LanesSameDirection = 3 }).Kind == SupportKind.Gantry, "three lanes in the direction: a gantry");
            Program.Check(SupportPlanner.AutoFor(inst, new SupportContext { LanesSameDirection = 2 }).Kind == SupportKind.DoublePole, "two lanes: the board's own choice");
            Program.Check(SupportPlanner.AutoFor(inst, new SupportContext { LanesSameDirection = 1, ExitLeftOrWide = true }).Kind == SupportKind.Gantry, "an exit on the left or a wide ramp: a gantry");
            Program.Check(SupportPlanner.AutoFor(inst, default).Kind == SupportKind.DoublePole, "nothing known: the board's own choice");
            SignDef stop = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null).Def;
            Program.Check(SupportPlanner.AutoFor(stop, new SupportContext { LanesSameDirection = 4 }).Kind == SupportKind.SinglePole, "a stop sign stays on its post whatever the road");
        }

        private static SignDef Board(ShapeKind shape, float w, float h, float extraTop = 0f) =>
            new SignDef { Id = "t", Shape = shape, Width = w, Height = h, ExtraTop = extraTop, BottomClearance = SupportCatalog.RoadsideClearance };

        private static void AutoSupport()
        {
            // GB 5768.2 4.8.12 a: one post for small and middle signs, more for rectangular boards.
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.Circle, 1.2f, 1.2f)).Id == SupportCatalog.SinglePole, "a round sign on one post");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.Octagon, 0.8f, 0.8f)).Id == SupportCatalog.SinglePole, "a stop sign on one post");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.Rect, 1.0f, 1.0f)).Id == SupportCatalog.SinglePole, "a small square board on one post");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.RoundRect, 1.0f, 1.6f)).Id == SupportCatalog.SinglePole && SupportPlanner.Auto(Board(ShapeKind.RoundRect, 1.3f, 1.3f)).Id == SupportCatalog.DoublePole,
                "the limit of one post: 1.6 m² (22MR601 p.76)");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.RoundRect, 1.7f, 1.0f)).Id == SupportCatalog.DoublePole, "a wider rectangular board on two posts");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.RoundRect, 1.0f, 2.5f)).Id == SupportCatalog.DoublePole, "a taller one too");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.RoundRect, 6.0f, 3.0f)).Id == SupportCatalog.DoublePole, "6 m on two posts");
            Program.Check(SupportPlanner.Auto(Board(ShapeKind.RoundRect, 6.1f, 3.0f)).Id == SupportCatalog.TriplePole, "more than 6 m on three");

            // The guide boards: the countdown board on one post, the exit advance and the next exit boards on two.
            Program.Check(SupportCatalog.For(SignInstances.Of(SignCatalog.ById("guide.countdown"), null).Def, 0u).Id == SupportCatalog.SinglePole, "the 60 x 142 cm board has one post");
            Program.Check(SupportCatalog.For(SignInstances.Of(SignCatalog.ById("guide.exit.advance"), null).Def, 0u).Id == SupportCatalog.DoublePole, "the exit advance board has two posts");
            Program.Check(SupportCatalog.For(SignInstances.Of(SignCatalog.ById("guide.exit.next"), "form=h").Def, 0u).Id == SupportCatalog.DoublePole, "the next exit board has two posts");
            Program.Check(SupportCatalog.For(SignInstances.Of(SignCatalog.ById("guide.road.plate"), "road=ge;code=G2;rname=京沪高速;use=start").Def, 0u).Id == SupportCatalog.DoublePole, "the 330 x 300 board has two posts");
            Program.Check(SupportCatalog.For(SignCatalog.ById("gb.stop"), SupportCatalog.ById(SupportCatalog.Cantilever).Hash).Id == SupportCatalog.Cantilever, "a support that is picked wins over the plan");

            // The post follows the area the board puts on it: the tube grows with the board, the foot with the tube.
            float last = 0f;
            foreach (float h in new[] { 0.8f, 1.5f, 2.5f, 3.5f, 4.5f })
            {
                float r = SupportLayouts.PoleRadiusFor(4.0f, h, SupportKind.DoublePole);
                Program.Check(r >= last, $"a taller board does not get a thinner post ({h} m: {r})");
                last = r;
            }

            Program.Check(SupportLayouts.PoleRadiusFor(4.0f, 3.6f, SupportKind.DoublePole) >= 0.09f, "a 4 x 3.6 m board on two posts has tubes of 180 mm or more");
            Program.Check(SupportLayouts.PoleRadiusFor(4.0f, 3.6f, SupportKind.TriplePole) < SupportLayouts.PoleRadiusFor(4.0f, 3.6f, SupportKind.DoublePole), "three posts share the wind: thinner tubes than two");
            Program.Check(SupportLayouts.PoleRadiusFor(0.8f, 0.8f, SupportKind.SinglePole) == 0.0445f, "an 80 cm stop sign on an 89 mm tube (22MR601 p.74; S5, S10, S11)");
            Program.Check(BasePlate.Post(0.11f).HalfX > BasePlate.Post(0.057f).HalfX && BasePlate.Post(0.057f).HalfX > BasePlate.Post(0.0445f).HalfX && BasePlate.Post(0.038f).HalfX == BasePlate.Post(0.0445f).HalfX,
                "the base grows with the tube (φ76 on φ89's)");

            // Three posts: symmetric, one in the middle, at most 3 m apart.
            SignDef wide = Board(ShapeKind.RoundRect, 8.0f, 3.0f);
            SupportLayout triple = SupportLayouts.Of(wide, SupportCatalog.ById(SupportCatalog.TriplePole));
            Program.Check(triple.PoleX.Length == 3 && triple.PoleX[1] == 0f && Math.Abs(triple.PoleX[0] + triple.PoleX[2]) < 1e-6f, "three posts, symmetric, one in the middle");
            Program.Check(triple.PoleX[2] - triple.PoleX[1] <= 3.0f + 1e-5f, "not more than 3 m between posts");

            // Rails (22MR601 pp.136, 141): two behind a board under 0.8 m, three at the least on a taller one, one more for every 0.4 m (a 1.2 m board four).
            Program.Check(SupportLayouts.RailKFor(0.6f).Length == 2 && SupportLayouts.RailKFor(0.8f).Length == 3 && SupportLayouts.RailKFor(1.2f).Length == 4, "two rails behind a 0.6 m board, three behind 0.8 m, four behind 1.2 m");
            Program.Check(SupportLayouts.Of(Board(ShapeKind.RoundRect, 3f, 3.6f), SupportCatalog.ById(SupportCatalog.DoublePole)).RailK.Length == 10, "ten behind a 3.6 m board");

            // The cantilever's post and arms by the board's area (22MR601 pp.78-85).
            SupportDef cantilever = SupportCatalog.ById(SupportCatalog.Cantilever);
            SupportLayout small = SupportLayouts.Of(Board(ShapeKind.Octagon, 0.8f, 0.8f), cantilever);
            SupportLayout big = SupportLayouts.Of(Board(ShapeKind.RoundRect, 4.0f, 3.6f, 1.0f), cantilever);
            Program.Near(small.PoleRadius, 0.1095f, 1e-6f, "a small board: φ219 and one arm");
            Program.Check(small.ArmYs.Length == 1 && big.ArmYs.Length == 3, "three arms behind 18 m²");
            Program.Check(big.PoleRadius > small.PoleRadius && big.ArmRootRadius > small.ArmRootRadius && big.ArmY > small.ArmY, "a thicker post and arm for the large board, its upper arm higher");

            // The clearance of the lower edge: 4.8.11.
            Program.Check(SupportCatalog.RoadsideClearance >= 2.3f && SupportCatalog.RoadsideClearance <= 2.5f, "2.3 m: at least 2.1 with pedestrians and 2.3 with non-motor traffic, at most 2.5");
            foreach (SignDef def in SignCatalog.All)
                Program.Check(def.Group == Geo.Signs.LampAds.Group || def.BottomClearance == (Geo.Signs.PlaceNameSigns.IsStone(def.Id) ? Geo.Signs.PlaceNameSigns.StoneClearance : def.Template != null && def.Template.Clearance > 0f ? def.Template.Clearance : SupportCatalog.RoadsideClearance),
                    def.Id + ": a roadside board is at the standard's clearance (a name stone of GB 17733 stands on the ground, the pedestrian and metro boards at their own)");

            // The reach toward the road: half the board for posts, the foundation for the cantilever.
            SupportLayout two = SupportLayouts.Of(Board(ShapeKind.RoundRect, 4.0f, 3.0f), SupportCatalog.ById(SupportCatalog.DoublePole));
            Program.Near(two.InnerReach, 2.0f, 1e-4f, "two posts: the board's half width");
            Program.Check(big.InnerReach > big.Base.HalfX, "the cantilever's pedestal reaches beyond its base plate");
        }

        private static void GuideCatalogue()
        {
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                SignDef def = SignCatalog.ById(t.Id);
                Program.Check(def != null && def.Template == t && def.Group == t.Group, t.Id + " is in the catalogue under its kind");
                Program.Check(SignCatalog.ByHash(t.Hash) == def, t.Id + " is found by its hash");
                Program.Check(def.Shape == SignCatalog.ShapeOf(t.Layout(t.Default()).Main.Shape), t.Id + " has the outline of its main board");
                var keys = new HashSet<string>();
                foreach (GuideField f in t.Fields)
                {
                    Program.Check(keys.Add(f.Key), $"{t.Id}: duplicate field {f.Key}");
                    Program.Check(!string.IsNullOrEmpty(f.Zh) && !string.IsNullOrEmpty(f.En), $"{t.Id}.{f.Key} has names");
                    Program.Check(f.Sanitize(f.Default) == f.Default, $"{t.Id}.{f.Key}: the default is valid");
                    if (f.Kind == GuideFieldKind.Choice)
                    {
                        // The font list of a text light box is the default and what the player's folder holds: one choice until a font is put there.
                        Program.Check(f.Choices.Length >= 2 || ((t.Id == CDLC.TS.Geo.Signs.TextBoxes.TemplateId || t.Id == CDLC.TS.Geo.Signs.StandBoxes.TemplateId) && f.Key == "font"), $"{t.Id}.{f.Key}: a choice has choices");
                        foreach (GuideChoice c in f.Choices)
                            Program.Check(!string.IsNullOrEmpty(c.Zh) && !string.IsNullOrEmpty(c.En), $"{t.Id}.{f.Key}.{c.Value} has names");
                    }
                }
            }

            // Items saved by the first builds with the test board still resolve.
            SignDef legacy = SignCatalog.ByHash(SignCatalog.HashOf(SignCatalog.LegacyTestExit));
            Program.Check(legacy != null && legacy.Template != null, "the old test board is drawn as an exit advance board");

            var ids = new HashSet<string>();
            foreach (SignChoice c in SignCatalog.Choices)
            {
                Program.Check(ids.Add(c.Id), "duplicate choice " + c.Id);
                Program.Check(c.Def != null && SignCatalog.ChoiceById(c.Id) == c, c.Id + " is found by id");
            }

            Program.Check(SignCatalog.Choices.Count >= 25, "the panel lists the classic signs and the presets: " + SignCatalog.Choices.Count);

            // A change of the spec is a change of the record, so the renderer rebuilds the tile.
            var before = new Dictionary<int, ItemRecord> { [1] = new ItemRecord(1u, new Vec3(10f, 0f, 10f), 0f, 0u, "a=1") };
            var after = new Dictionary<int, ItemRecord> { [1] = new ItemRecord(1u, new Vec3(10f, 0f, 10f), 0f, 0u, "a=2") };
            var same = new Dictionary<int, ItemRecord> { [1] = new ItemRecord(1u, new Vec3(10f, 0f, 10f), 0f, 0u, "a=1") };
            Program.Check(TileDiff.Dirty(before, after).Count == 1, "a changed spec rebuilds its tile");
            Program.Check(TileDiff.Dirty(before, same).Count == 0, "an unchanged spec does not");
            var noSpec = new Dictionary<int, ItemRecord> { [1] = new ItemRecord(1u, new Vec3(10f, 0f, 10f), 0f, 0u, null) };
            var emptySpec = new Dictionary<int, ItemRecord> { [1] = new ItemRecord(1u, new Vec3(10f, 0f, 10f), 0f, 0u, "") };
            Program.Check(TileDiff.Dirty(noSpec, emptySpec).Count == 0, "no spec and an empty spec are the same");
        }
    }
}
