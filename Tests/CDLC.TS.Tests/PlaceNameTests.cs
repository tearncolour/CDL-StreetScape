using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The place-name signs of GB 17733-2008: the layouts of 5.3.1 and Appendix B, the colours of Table 1, the sizes of Table 2, the direction words (5.3.3), the pictogram (5.3.2), the postal code (5.3.4) and the stones.</summary>
    internal static class PlaceNameTests
    {
        public static void RunAll()
        {
            Program.Run("place names: the catalogue", Catalogue);
            Program.Run("place names: the street sign (Fig. B.11)", Street);
            Program.Run("place names: the direction words (5.3.3)", Ends);
            Program.Run("place names: colours and sizes (Tables 1 and 2)", ColoursAndSizes);
            Program.Run("place names: the door plate and its postal code", Door);
            Program.Run("place names: the building, unit and floor plates", Building);
            Program.Run("place names: the pictogram (5.3.2)", Pictogram);
            Program.Run("place names: the stones", Stones);
        }

        private static GuidePart Main(string id, params (string Key, string Value)[] settings)
        {
            SignDef def = SignCatalog.ById(id);
            GuideTemplate t = def.Template;
            GuideSpec s = t.Parse(t.Default().ToString());
            foreach ((string key, string value) in settings)
                s = s.With(key, value);
            return SignInstances.Of(def, s.ToString()).Board.Main;
        }

        private static bool Cjk(string text) => !string.IsNullOrEmpty(text) && text[0] >= 0x3400;

        private static List<GuideElement> Texts(GuidePart p) => p.Elements.FindAll(e => e.Kind == GuideElementKind.Text);

        /// <summary>Every element inside the board, with a margin of tolerance.</summary>
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
            var ids = new HashSet<string>();
            int count = 0;
            foreach (GuideTemplate t in GuideTemplates.All)
            {
                if (t.Group != StandardSigns.PlaceName)
                    continue;
                count++;
                Program.Check(ids.Add(t.Id) && t.Id.StartsWith("gb17733.", StringComparison.Ordinal), t.Id + ": one id, of GB 17733");
                Program.Check(t.Standalone && !string.IsNullOrEmpty(t.Zh) && !string.IsNullOrEmpty(t.En) && !string.IsNullOrEmpty(t.Code), t.Id + ": named, with its figure");
                GuidePart p = Main(t.Id);
                Program.Check(p.W > 0.05f && p.H > 0.05f && p.W <= 7.0f + 1e-4f && p.H <= 2.5f + 1e-4f, t.Id + ": a board of Table 2's sizes: " + p.W + " x " + p.H);
                Program.Check(Inside(p, out string worst), t.Id + ": everything on the board: " + worst);
                List<GuideElement> texts = Texts(p);
                Program.Check(texts.Count > 0, t.Id + ": it says something");
                Program.Check(texts.TrueForAll(e => e.Colour != p.Ground), t.Id + ": the text is not in the ground's colour");
                Program.Check(p.Liner == 0f && p.Border == 0f, t.Id + ": no liner and no border (Appendix B)");
                // A long name and a long pinyin stay on the board.
                GuideField name = Array.Find(t.Fields, f => f.Key == "name"), py = Array.Find(t.Fields, f => f.Key == "py");
                if (name != null && py != null)
                {
                    GuidePart longer = Main(t.Id, ("name", new string('东', name.MaxLength)), ("py", "DONGDONGDONG XIBEIXIBEI NANNAN"));
                    Program.Check(Inside(longer, out worst), t.Id + ": a long name and a long pinyin stay on the board: " + worst);
                }
            }

            Program.Check(count == 16, "16 place-name signs: " + count);
        }

        private static void Street()
        {
            GuidePart p = Main("gb17733.street");
            float h = p.H;
            Program.Near(p.W, 1.5f, 1e-4f, "1500 mm long by default");
            Program.Near(h, 0.6f, 1e-4f, "600 mm high by default");
            Program.Check(p.Ground == GuideColour.Blue, "an east-west street is blue (Table 1)");
            GuideElement name = Texts(p).Find(e => Cjk(e.Text));
            Program.Check(name != null && name.Text == "中山街" && name.Colour == GuideColour.White, "the name, white");
            Program.Near(name.H, PlaceNameSigns.CharK * h, 1e-4f, "the characters 0.44 of the height (Fig. B.11)");
            Program.Near(name.Y + name.H * 0.5f, PlaceNameSigns.CharMidK * h, 1e-4f, "in the middle of the upper three fifths' writing (5.3.1.1)");
            Program.Check(name.Bottom <= 0.6f * h, "the name in the upper three fifths");
            // Spread over the width: the run from the margin to the margin while the gap is under a character.
            Program.Check(name.X >= PlaceNameSigns.MarginK * h - 1e-4f && name.Right <= p.W - PlaceNameSigns.MarginK * h + 1e-4f, "between the margins");
            Program.Near(name.X + name.W * 0.5f, p.W * 0.5f, 1e-4f, "centred");
            Program.Check(name.Gap > 0f && name.Gap <= name.H + 1e-4f, "spread, a gap of a character at most: " + name.Gap);

            List<GuideElement> letters = Texts(p).FindAll(e => !Cjk(e.Text));
            Program.Check(letters.Count == "ZHONGSHANJIE".Length, "the pinyin letter by letter: " + letters.Count);
            float cap = PlaceNameSigns.PinyinK * h, c = letters[0].H, line = PlaceNameSigns.PinyinTopK * h + cap * 0.5f;
            Program.Check(c <= cap + 1e-5f && c > 0.9f * cap && letters.TrueForAll(e => Math.Abs(e.H - c) < 1e-5f && Math.Abs(e.Y + e.H * 0.5f - line) < 1e-4f && e.Colour == GuideColour.White),
                "the pinyin 0.17 of the height (a little less when its line is full), on the line whose top is at 0.66, white: " + c / h);
            GuidePart shortName = Main("gb17733.street", ("name", "中山街"), ("py", "JIE"));
            Program.Check(Texts(shortName).FindAll(e => !Cjk(e.Text)).TrueForAll(e => Math.Abs(e.H - cap) < 1e-5f && Math.Abs(e.Y - PlaceNameSigns.PinyinTopK * h) < 1e-5f), "a short pinyin: 0.17 of the height, its top at 0.66");
            Program.Check(letters.TrueForAll(e => e.Y >= 0.6f * h && e.Bottom <= h), "the pinyin in the lower two fifths");
            letters.Sort((a, b) => a.X.CompareTo(b.X));
            var gaps = new List<float>();
            for (int i = 1; i < letters.Count; i++)
                gaps.Add(letters[i].X - letters[i - 1].Right);
            Program.Near(gaps[8], PlaceNameSigns.WordGapK * c, 1e-4f, "ZHONGSHAN | JIE: a word gap of 1.2 of the letters' height");
            bool track = true;
            for (int i = 0; i < gaps.Count; i++)
                track &= i == 8 || Math.Abs(gaps[i] - PlaceNameSigns.TrackK * c) < 1e-4f;
            Program.Check(track, "letters a quarter of their height apart");
            float mid = (letters[0].X + letters[letters.Count - 1].Right) * 0.5f;
            Program.Near(mid, p.W * 0.5f, 1e-4f, "the pinyin centred");

            Program.Check(Main("gb17733.street", ("run", "ns")).Ground == GuideColour.Green, "a north-south street is green");
            Program.Check(Main("gb17733.lane").Ground == GuideColour.Green && Main("gb17733.lane", ("run", "ew")).Ground == GuideColour.Blue, "a lane too");
            GuidePart lane = Main("gb17733.lane");
            Program.Near(lane.W, 0.46f, 1e-4f, "a lane sign 460 mm (Table 2)");
            Program.Near(lane.H, 0.16f, 1e-4f, "by 160 mm");

            // Too many letters: smaller, not narrower, and still on the middle of the pinyin's line.
            GuidePart longer = Main("gb17733.street", ("py", "ZHONGHUARENMIN GONGHEGUO DADAO NANDUAN"));
            List<GuideElement> small = Texts(longer).FindAll(e => !Cjk(e.Text));
            Program.Check(small.Count > 0 && small.TrueForAll(e => e.H < cap && Math.Abs(e.Y + e.H * 0.5f - (PlaceNameSigns.PinyinTopK * h + cap * 0.5f)) < 1e-4f), "a long pinyin is made smaller, on the middle of its line");
            float x0 = float.MaxValue, x1 = float.MinValue;
            foreach (GuideElement e in small)
            {
                x0 = Math.Min(x0, e.X);
                x1 = Math.Max(x1, e.Right);
            }

            Program.Check(x0 >= PlaceNameSigns.MarginK * h - 1e-4f && x1 <= p.W - PlaceNameSigns.MarginK * h + 1e-4f, "between the margins: " + x0 + ".." + x1);
            // Lower case typed: capitals (Fig. B.11).
            Program.Check(Texts(Main("gb17733.street", ("py", "zhongshan jie"))).FindAll(e => !Cjk(e.Text)).TrueForAll(e => e.Text == e.Text.ToUpperInvariant()), "typed in lower case, written in capitals");
            // Long names: smaller characters, still in the upper three fifths.
            GuideElement many = Texts(Main("gb17733.street", ("name", "中华人民共和国大道南段北口"))).Find(e => Cjk(e.Text));
            Program.Check(many.H < PlaceNameSigns.CharK * h && many.Bottom <= 0.6f * h && many.X >= PlaceNameSigns.MarginK * h - 1e-4f, "a long name: smaller characters, in the upper three fifths");
        }

        private static void Ends()
        {
            foreach ((string ends, string left, string right) in new[] { ("ew", "东", "西"), ("we", "西", "东"), ("ns", "北", "南"), ("sn", "南", "北") })
            {
                GuidePart p = Main("gb17733.street", ("ends", ends));
                float h = p.H, cap = PlaceNameSigns.PinyinK * h;
                List<GuideElement> words = Texts(p).FindAll(e => Cjk(e.Text) && e.Text.Length == 1);
                Program.Check(words.Count == 2, ends + ": two direction words");
                words.Sort((a, b) => a.X.CompareTo(b.X));
                Program.Check(words[0].Text == left && words[1].Text == right, ends + ": " + left + " at the left, " + right + " at the right");
                Program.Check(words.TrueForAll(e => Math.Abs(e.H - cap) < 1e-4f && Math.Abs(e.Y - PlaceNameSigns.PinyinTopK * h) < 1e-4f), ends + ": as high as the pinyin, on its line");
                Program.Near(words[0].X, PlaceNameSigns.MarginK * h, 1e-4f, ends + ": a margin from the left edge");
                Program.Near(p.W - words[1].Right, PlaceNameSigns.MarginK * h, 1e-4f, ends + ": a margin from the right edge");
                List<GuideElement> letters = Texts(p).FindAll(e => !Cjk(e.Text));
                Program.Check(letters.TrueForAll(e => e.X >= words[0].Right + cap - 1e-4f && e.Right <= words[1].X - cap + 1e-4f), ends + ": the pinyin between them, a letter's height away");
            }

            Program.Check(Texts(Main("gb17733.street")).FindAll(e => Cjk(e.Text)).Count == 1, "none by default");
        }

        private static void ColoursAndSizes()
        {
            foreach (string id in new[] { "gb17733.area", "gb17733.estate", "gb17733.village" })
            {
                GuidePart blue = Main(id), metal = Main(id, ("finish", "metal")), red = Main(id, ("finish", "metalred"));
                Program.Check(blue.Ground == GuideColour.Blue && Texts(blue).TrueForAll(e => e.Colour == GuideColour.White), id + ": white on blue");
                Program.Check(metal.Ground == GuideColour.Gray && Texts(metal).TrueForAll(e => e.Colour == GuideColour.Black), id + ": etched metal, black");
                Program.Check(red.Ground == GuideColour.Gray && Texts(red).TrueForAll(e => e.Colour == GuideColour.Red), id + ": etched metal, red");
                Program.Near(Main(id, ("len", "500")).W, 0.8f, 1e-4f, id + ": 800 mm at the shortest");
                Program.Near(Main(id, ("len", "9000")).W, 1.7f, 1e-4f, id + ": 1700 mm at the longest");
                Program.Near(Main(id, ("wid", "100")).H, 0.3f, 1e-4f, id + ": 300 mm at the lowest");
                Program.Near(Main(id, ("wid", "999")).H, 0.7f, 1e-4f, id + ": 700 mm at the highest");
            }

            Program.Near(Main("gb17733.street", ("len", "1000")).W, 1.2f, 1e-4f, "a street sign 1200 mm at the shortest");
            Program.Near(Main("gb17733.street", ("len", "2000"), ("wid", "200")).W, 1.7f, 1e-4f, "1700 mm at the longest");
            Program.Near(Main("gb17733.street", ("wid", "200")).H, 0.3f, 1e-4f, "300 mm at the lowest");
            Program.Near(Main("gb17733.street", ("len", "")).W, 1.5f, 1e-4f, "an empty size: the default");

            foreach ((string id, GuideColour ground, float minW, float maxW, float minH, float maxH) in new[]
            {
                ("gb17733.admin", GuideColour.Blue, 2.0f, 7.0f, 0.9f, 2.5f),
                ("gb17733.zone", GuideColour.Green, 1.5f, 3.0f, 0.6f, 1.2f),
                ("gb17733.facility", GuideColour.Blue, 1.5f, 3.0f, 0.8f, 1.5f),
                ("gb17733.memorial", GuideColour.Brown, 1.5f, 3.0f, 0.8f, 1.5f),
                ("gb17733.nature", GuideColour.Green, 1.5f, 5.7f, 0.6f, 1.8f)
            })
            {
                GuidePart p = Main(id);
                Program.Check(p.Ground == ground && Texts(p).TrueForAll(e => e.Colour == GuideColour.White), id + ": white on " + ground + " (Table 1)");
                Program.Check(p.W >= minW - 1e-4f && p.W <= maxW + 1e-4f && p.H >= minH - 1e-4f && p.H <= maxH + 1e-4f, id + ": in Table 2's range by default");
                Program.Near(Main(id, ("len", "1"), ("wid", "1")).W, minW, 1e-4f, id + ": the shortest");
                Program.Near(Main(id, ("len", "1"), ("wid", "1")).H, minH, 1e-4f, id + ": the lowest");
                Program.Near(Main(id, ("len", "9999"), ("wid", "9999")).W, maxW, 1e-4f, id + ": the longest");
                Program.Near(Main(id, ("len", "9999"), ("wid", "9999")).H, maxH, 1e-4f, id + ": the highest");
            }
        }

        private static void Door()
        {
            foreach ((string size, float w, float h) in new[] { ("l", 0.57f, 0.37f), ("m", 0.27f, 0.17f), ("s", 0.15f, 0.09f) })
            {
                GuidePart p = Main("gb17733.door", ("size", size));
                Program.Near(p.W, w, 1e-4f, size + ": the door plate's width (Table 2)");
                Program.Near(p.H, h, 1e-4f, size + ": its height");
            }

            GuidePart plain = Main("gb17733.door", ("no", "128"));
            float ph = plain.H;
            GuideElement street = Texts(plain).Find(e => Cjk(e.Text));
            Program.Check(street.Text == "幸福大街" && street.Y >= 0f && street.Bottom <= 0.4f * ph, "the street in the upper two fifths (5.3.1.3)");
            List<GuideElement> digits = Texts(plain).FindAll(e => !Cjk(e.Text));
            Program.Check(digits.Count > 0 && digits.TrueForAll(e => e.Y >= 0.4f * ph - 1e-4f && e.Bottom <= ph + 1e-4f), "the number in the lower three fifths");
            Program.Check(string.Concat(digits.ConvertAll(e => e.Text)) == "128", "the number: " + string.Concat(digits.ConvertAll(e => e.Text)));

            GuidePart coded = Main("gb17733.door", ("no", "128"), ("post", "100000"));
            float ch = coded.H;
            Program.Check(Inside(coded, out string worst), "with the postal code, on the board: " + worst);
            GuideElement name = Texts(coded).Find(e => Cjk(e.Text) && e.Text == "幸福大街");
            Program.Check(name.Bottom <= 0.2f * ch + 1e-4f, "with the postal code: the street in the top fifth (5.3.4)");
            GuideElement words = Texts(coded).Find(e => e.Text == "邮政编码");
            Program.Check(words != null && words.Y >= 0.8f * ch - 0.03f * ch && words.Bottom <= ch, "邮政编码 in the lowest fifth");
            List<GuideElement> code = Texts(coded).FindAll(e => !Cjk(e.Text) && e.Y >= 0.75f * ch);
            Program.Check(string.Concat(code.ConvertAll(e => e.Text)) == "100000" && code.TrueForAll(e => e.X > words.Right), "and the code after it: " + string.Concat(code.ConvertAll(e => e.Text)));
            List<GuideElement> number = Texts(coded).FindAll(e => !Cjk(e.Text) && e.Y < 0.75f * ch);
            Program.Check(string.Concat(number.ConvertAll(e => e.Text)) == "128" && number.TrueForAll(e => e.Y >= 0.2f * ch - 1e-4f && e.Bottom <= 0.8f * ch + 1e-4f), "the number in the middle three fifths");

            GuidePart metal = Main("gb17733.door", ("finish", "metalred"));
            Program.Check(metal.Ground == GuideColour.Gray && Texts(metal).TrueForAll(e => e.Colour == GuideColour.Red), "an etched door plate, red");
            Program.Check(Inside(Main("gb17733.door", ("no", "1234-56"), ("size", "s"), ("post", "999999")), out worst), "a long number on the small plate: " + worst);
        }

        private static void Building()
        {
            GuidePart p = Main("gb17733.building", ("no", "12"));
            Program.Near(p.W, 0.9f, 1e-4f, "900 mm (Table 2)");
            Program.Near(p.H, 0.5f, 1e-4f, "by 500 mm");
            Program.Check(p.Ground == GuideColour.Blue, "blue on the left (Table 1)");
            GuideElement white = p.Elements.Find(e => e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.White);
            Program.Check(white != null && Math.Abs(white.X - 0.6f * p.W) < 1e-4f && Math.Abs(white.Right - p.W) < 1e-4f && white.H >= p.H - 1e-4f, "white on the right two fifths (5.3.1.2)");
            List<GuideElement> number = Texts(p).FindAll(e => !Cjk(e.Text) && e.X >= 0.6f * p.W);
            Program.Check(string.Concat(number.ConvertAll(e => e.Text)) == "12" && number.TrueForAll(e => e.Colour == GuideColour.Red), "the number on the white, red");
            Program.Check(number.TrueForAll(e => e.Right <= p.W + 1e-4f), "a number of two figures on the white");
            List<GuideElement> one = Texts(Main("gb17733.building")).FindAll(e => !Cjk(e.Text) && e.X >= 0.6f * p.W);
            Program.Check(string.Concat(one.ConvertAll(e => e.Text)) == "8" && one.TrueForAll(e => e.H >= 0.35f - 1e-4f), "one figure 350 mm high");
            List<GuideElement> rows = Texts(p).FindAll(e => Cjk(e.Text));
            Program.Check(rows.Count == 2 && rows.TrueForAll(e => e.Right <= 0.6f * p.W && e.Bottom <= 0.6f * p.H && e.Colour == GuideColour.White), "a name of four characters in two rows on the blue");
            Program.Check(Texts(Main("gb17733.building", ("name", "清风"))).FindAll(e => Cjk(e.Text)).Count == 1, "a short name in one row");
            List<GuideElement> letters = Texts(p).FindAll(e => !Cjk(e.Text) && e.X < 0.6f * p.W);
            Program.Check(letters.Count > 0 && letters.TrueForAll(e => e.Y >= 0.6f * p.H - 1e-4f && e.Right <= 0.6f * p.W), "the pinyin under the name, on the blue");
            GuidePart metal = Main("gb17733.building", ("finish", "metal"));
            Program.Check(metal.Ground == GuideColour.Gray && !metal.Elements.Exists(e => e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.White) && Texts(metal).TrueForAll(e => e.Colour == GuideColour.Black),
                "etched: all of the base plate, black");

            GuidePart unit = Main("gb17733.unit");
            GuideElement u = Texts(unit).Find(e => Cjk(e.Text));
            Program.Check(u.Text == "一单元" && u.X >= 0.2f * unit.W - 1e-4f && u.Right <= 0.8f * unit.W + 1e-4f, "the unit in the middle three fifths (5.3.1.4)");
            Program.Near(unit.W, 0.35f, 1e-4f, "a unit plate 350 mm by default");
            Program.Near(Main("gb17733.unit", ("len", "100")).W, 0.3f, 1e-4f, "300 mm at the shortest");
            Program.Near(Main("gb17733.unit", ("wid", "900")).H, 0.2f, 1e-4f, "200 mm at the highest");

            GuidePart floor = Main("gb17733.floor");
            Program.Check(floor.Shape == GuidePartShape.Circle && Math.Abs(floor.W - floor.H) < 1e-6f, "the floor plate a disc (5.3.1.5)");
            Program.Near(floor.W, 0.3f, 1e-4f, "300 mm across by default");
            Program.Near(Main("gb17733.floor", ("dia", "900")).W, 0.4f, 1e-4f, "400 mm at the most");
            Program.Near(Main("gb17733.floor", ("dia", "50")).W, 0.2f, 1e-4f, "200 mm at the least");
            List<GuideElement> level = Texts(floor);
            Program.Check(string.Concat(level.ConvertAll(e => e.Text)) == "B1" && level.TrueForAll(e => e.X >= 0.2f * floor.W - 1e-4f && e.Right <= 0.8f * floor.W + 1e-4f), "B1 in the middle three fifths");
        }

        private static void Pictogram()
        {
            foreach (string id in new[] { "gb17733.zone", "gb17733.facility", "gb17733.memorial" })
            {
                GuidePart plain = Main(id), p = Main(id, ("sym", "地铁"));
                Program.Check(!plain.Elements.Exists(e => e.Kind == GuideElementKind.Rect), id + ": none by default");
                GuideElement square = p.Elements.Find(e => e.Kind == GuideElementKind.Rect && e.Colour == GuideColour.White);
                Program.Check(square != null && square.Right <= 0.4f * p.W + 1e-4f && Math.Abs(square.W - square.H) < 1e-5f, id + ": a white square in the left two fifths (Fig. B.22)");
                Program.Check(p.Elements.Exists(e => e.Kind != GuideElementKind.Rect && e.Kind != GuideElementKind.Text && e.Colour == p.Ground), id + ": the figure in the board's colour");
                Program.Check(Texts(p).TrueForAll(e => e.X >= 0.4f * p.W), id + ": the name and the pinyin in the right three fifths");
                Program.Check(Inside(p, out string worst), id + ": on the board: " + worst);
            }

            foreach (string id in new[] { "gb17733.admin", "gb17733.nature", "gb17733.street" })
                Program.Check(!Array.Exists(SignCatalog.ById(id).Template.Fields, f => f.Key == "sym"), id + ": no pictogram");
        }

        private static void Stones()
        {
            GuidePart up = Main("gb17733.stone.v");
            Program.Near(up.W, 0.6f, 1e-4f, "an upright stone 600 mm wide by default");
            Program.Near(up.H, 1.4f, 1e-4f, "1400 mm high");
            Program.Check(up.Ground == GuideColour.Gray, "the stone's own colour");
            List<GuideElement> chars = Texts(up).FindAll(e => Cjk(e.Text));
            Program.Check(chars.Count == 3 && chars.TrueForAll(e => e.Text.Length == 1 && e.Colour == GuideColour.Black), "the name one character at a time, black");
            bool down = true, centred = true;
            for (int i = 0; i < chars.Count; i++)
            {
                centred &= Math.Abs(chars[i].X + chars[i].W * 0.5f - up.W * 0.5f) < 1e-4f;
                down &= i == 0 || chars[i].Y > chars[i - 1].Bottom - 1e-4f;
            }

            Program.Check(down && centred, "one under the other, centred (5.3.1.6)");
            Program.Check(chars.TrueForAll(e => e.Bottom <= 0.8f * up.H + 1e-4f), "in the upper four fifths");
            List<GuideElement> letters = Texts(up).FindAll(e => !Cjk(e.Text));
            Program.Check(letters.Count > 0 && letters.TrueForAll(e => e.Y >= 0.8f * up.H - 1e-4f), "the pinyin in the lowest fifth");
            Program.Check(Texts(Main("gb17733.stone.v", ("ink", "r"))).TrueForAll(e => e.Colour == GuideColour.Red), "or red");
            Program.Near(Main("gb17733.stone.v", ("len", "9999"), ("wid", "9999")).H, 2.0f, 1e-4f, "2000 mm high at the most (Table 2)");
            Program.Near(Main("gb17733.stone.v", ("len", "1"), ("wid", "1")).W, 0.3f, 1e-4f, "300 mm wide at the least");

            GuidePart across = Main("gb17733.stone.h");
            Program.Check(across.Ground == GuideColour.Gray && across.W > across.H, "the stone across, wide");
            GuideElement name = Texts(across).Find(e => Cjk(e.Text));
            Program.Check(name.Bottom <= 0.6f * across.H && Texts(across).FindAll(e => !Cjk(e.Text)).TrueForAll(e => e.Y >= 0.6f * across.H), "three fifths and two fifths, as a name sign");

            // On the ground (5.1.2); the others 2 m up at least (5.1.3).
            SignDef stone = SignCatalog.ById("gb17733.stone.v"), street = SignCatalog.ById("gb17733.street");
            Program.Near(stone.BottomClearance, PlaceNameSigns.StoneClearance, 1e-6f, "a stone stands low");
            Program.Check(PlaceNameSigns.IsStone("gb17733.stone.h") && !PlaceNameSigns.IsStone("gb17733.street") && !PlaceNameSigns.IsStone(null), "the stones are known by their ids");
            Program.Check(street.BottomClearance >= 2.0f, "a street sign's lower edge 2 m up at least (5.1.3): " + street.BottomClearance);
            Program.Near(SupportLayouts.Of(stone, SupportCatalog.ById(SupportCatalog.SinglePole)).BottomClearance, PlaceNameSigns.StoneClearance, 1e-6f, "and so on its post");
        }
    }
}
