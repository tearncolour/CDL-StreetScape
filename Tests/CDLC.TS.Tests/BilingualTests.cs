using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The English line under Chinese (Bilingual): with the switch off nothing changes (the spec strings and the boards are what the templates make), with it on the English stands under its text at the
    /// height of Table 4, a long line is made smaller and then wider and then left out, and no opted-in board has the line over anything else.
    /// </summary>
    internal static class BilingualTests
    {
        public static void RunAll()
        {
            Program.Run("bilingual: the table is Table A.1 of GB 5768.2", Terms);
            Program.Run("bilingual: every opted-in template is whole", Table);
            Program.Run("bilingual: the specs written before are the same strings, and an older build ignores the new keys", Specs);
            Program.Run("bilingual: with the switch off a board is the one the template laid out", SwitchOff);
            Program.Run("bilingual: the English is under its Chinese, centred, at the height of Table 4", Placement);
            Program.Run("bilingual: a noun of the table is filled in, a typed line wins", Fill);
            Program.Run("bilingual: a text with no English leaves the board as it was", Empty);
            Program.Run("bilingual: a long line is made smaller, then wider, then left out", Fit);
            Program.Run("bilingual: a form of a fixed size has no English", Upright);
            Program.Run("bilingual: the exit boards set the English in capitals", Capitals);
            Program.Run("bilingual: the Chinese texts of a row stay level", Level);
            Program.Run("bilingual: no opted-in board has the English over anything", Clear);
            Program.Run("bilingual: layouts on other threads do not meet", Threads);
            Program.Run("bilingual: the words of a board are the Chinese", Words);
        }

        // ------------------------------------------------------------------ helpers

        private static GuideTemplate Template(string id) => SignCatalog.ById(id).Template;

        private static GuideSpec Spec(string id, string settings)
        {
            GuideTemplate template = Template(id);
            GuideSpec spec = template.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return spec;
        }

        private static GuideBoard Lay(string id, string settings) => Template(id).Layout(Spec(id, settings));

        private static List<GuideElement> Lines(GuidePart part) => part.Elements.Where(e => e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Line).ToList();

        private static List<GuideElement> Cells(GuidePart part) => part.Elements.Where(e => e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Cell).ToList();

        /// <summary>The spec of a template with an English line typed for each of its keys, and the switch on.</summary>
        private static string Typed(string id, string english = "Placename")
        {
            string settings = "en=1";
            foreach (string key in Template(id).Bilingual.Keys)
                settings += ";" + Bilingual.TextPrefix + key + "=" + english;
            return settings;
        }

        /// <summary>Settings that bring the boards to the forms the layouts differ in (a junction's four forms, six lanes, the exit boards' through lane...).</summary>
        private static readonly (string Id, string Settings)[] kVariants =
        {
            ("gb.guide8.01", ""), ("gb.guide8.01", "form=t"), ("gb.guide8.01", "form=r"), ("gb.guide8.01", "form=l"), ("gb.guide8.01", "xroad=环城路"),
            ("gb.guide8.01", "to2=沪宁;tl2=北京路;tr2=东路"), ("gb.guide8.01", "to1=学府路[地铁];tl1=机场[机场]"),
            ("gb.guide8.02", ""), ("gb.guide8.02", "tnw2=西关;te2=东关;tw1=石桥;tne1=北村"),
            ("gb.guide8.203c", ""), ("gb.guide8.203c", "to2=海安;tl2=泰州;tr2=南通"),
            ("gb.guide8.03", ""), ("gb.guide8.03", "to2=沪宁;tl2=北京路;tr2=东路"),
            ("gb.guide8.04", ""), ("gb.guide8.04", "n=6;to3=甲路;to4=乙路;to5=丙路;to6=丁路"), ("gb.guide8.04", "n=2;to1=东莞;to2="), ("gb.guide8.04b", ""), ("gb.guide8.04b", "to1=东莞;to2=深圳;to3=惠州;to4=广州"),
            ("gb.guide8.06", ""), ("gb.guide8.06", "name=人民南路"), ("gb.guide8.08", ""), ("gb.guide8.08", "to3=;d3=;unit=m;ground=green"),
            ("gb.guide8.09", ""), ("gb.guide8.10", ""), ("gb.guide8.11", ""),
            ("guide.exit.advance", ""), ("guide.exit.advance", "road=name;rname=前进路"), ("guide.exit.advance", "road=none;to2="), ("guide.exit.advance", "road=ge;code=G15;sub=11;xnum=50;xside=l"),
            ("guide.exit.direction", ""), ("guide.exit.direction", "lane=1"), ("guide.exit.direction", "lane=1;lanes=2;to2="), ("guide.exit.direction", "road=name;rname=前进路;dir=南"),
            ("guide.exit.sign", ""), ("guide.exit.sign", "xnum=101;xside=l"),
            ("gb.guide9.38", ""), ("gb.guide9.38b", ""), ("gb.guide9.39", "form=b"), ("gb.guide9.39", "form=b;k1=blue;k2=white"),
            ("gb.aux.text", ""), ("gb.aux.02", ""), ("gb.aux.11", ""), ("gb.aux.11", "l2=")
        };

        // ------------------------------------------------------------------ the table of terms and of opt-ins

        private static void Terms()
        {
            Program.Check(TrafficTerms.English("出口") == "EXIT" && TrafficTerms.English("下一出口") == "NEXT EXIT" && TrafficTerms.English("超限检测站") == "WEIGH STATION", "the nouns of Table A.1");
            Program.Check(TrafficTerms.English(" 隧道 ") == "TUNNEL", "a noun is found with spaces round it");
            Program.Check(TrafficTerms.English("服务区/站") == "SERVICE AREA" && TrafficTerms.English("服务区／站") == "SERVICE AREA" && TrafficTerms.English("服务站") == "SERVICE AREA", "the row with two forms is one noun in either");
            Program.Check(TrafficTerms.English("停车区") == "REST AREA" && TrafficTerms.English("停车点") == "REST AREA", "the second row with two forms");
            Program.Check(TrafficTerms.English("东出口") == null && TrafficTerms.English("出口 ") != null && TrafficTerms.English("如皋") == null && TrafficTerms.English("") == null && TrafficTerms.English(null) == null, "a noun is the whole text: nothing is guessed");
            string[] rows = { "避险车道", "隧道", "收费", "入口", "出口", "下一出口", "收费站", "服务区/站", "停车区/点", "爬坡车道", "起点", "结束", "道路交通信息", "超限检测站" };
            Program.Check(rows.All(r => TrafficTerms.English(r) != null && TrafficTerms.English(r) == TrafficTerms.English(r).ToUpperInvariant()), "all fourteen rows of Table A.1 are there, in capitals");
        }

        private static void Table()
        {
            Program.Check(Bilingual.Failed.Count == 0, "every row of the table applies: " + string.Join("; ", Bilingual.Failed));
            int count = 0;
            foreach (string id in Bilingual.OptInIds)
            {
                count++;
                SignDef def = SignCatalog.ById(id);
                GuideTemplate template = def == null ? null : def.Template;
                Program.Check(template != null && template.Bilingual != null, id + " is in the table and has its English line");
                if (template == null || template.Bilingual == null)
                    continue;
                GuideField flag = template.FieldOf(Bilingual.FlagKey);
                Program.Check(flag != null && flag.Kind == GuideFieldKind.Flag && flag.Default == "0" && flag.Sparse, id + ": the switch is off and sparse");
                foreach (string key in template.Bilingual.Keys)
                {
                    GuideField field = template.FieldOf(Bilingual.TextPrefix + key);
                    Program.Check(field != null && field.Sparse && field.Default == "" && field.MaxLength == Bilingual.MaxLength, id + ": a sparse field for " + key);
                    Program.Check(field != null && template.FieldOf(key) != null, id + ": " + key + " is a field of the template");
                    GuideSpec off = template.Default(), on = template.Default().With(Bilingual.FlagKey, "1");
                    GuideField chinese = template.FieldOf(key);
                    bool shown = on.Get(key).Length > 0 && (chinese.Visible == null || chinese.Visible(on));
                    Program.Check(field != null && !field.Visible(off) && field.Visible(on) == shown, id + ": the English of " + key + " shows with the switch only (and a Chinese text that shows)");
                }
            }

            Program.Check(count >= 15, "the table has the route boards, the exit boards, 路38 and the auxiliary texts: " + count);
        }

        // ------------------------------------------------------------------ nothing written before changes

        private static void Specs()
        {
            // The strings the main build writes (captured from it before this change).
            Program.Check(Template("gb.guide8.08").Default().ToString() == "to1=如皋;d1=5;to2=海安;d2=40;to3=盐城;d3=105;unit=km;ground=blue;speed=auto", "路8's default spec is the string it always was");
            Program.Check(Template("gb.guide8.04").Default().ToString() == "n=0;to1=东莞;a1=s;to2=深圳;a2=r;to3=;a3=r;to4=;a4=s;to5=;a5=s;to6=;a6=s;speed=auto", "路4's default spec is the string it always was");
            Program.Check(Template("gb.guide8.03").Default().ToString() == "to1=学府路;to2=;tl1=公滨路;tl2=;tr1=黄河路;tr2=;speed=auto", "路3's default spec is the string it always was");
            foreach ((string id, string settings) in kVariants)
            {
                GuideSpec spec = Spec(id, settings);
                string text = spec.ToString();
                Program.Check(!text.Contains(Bilingual.FlagKey + "=") && !text.Contains(Bilingual.TextPrefix), id + " [" + settings + "]: a spec with the switch off does not have the keys: " + text);
                Program.Check(Template(id).Parse(text).ToString() == text, id + " [" + settings + "]: reading a written spec and writing it is the same string");
            }

            // With the switch on the keys are written (in the order of the fields) and read back.
            string on = Spec("gb.guide8.08", "en=1;en_to1=Rugao;en_to3=Yancheng").ToString();
            Program.Check(on == "to1=如皋;d1=5;to2=海安;d2=40;to3=盐城;d3=105;unit=km;ground=blue;speed=auto;en=1;en_to1=Rugao;en_to3=Yancheng", "the English is written after the rest: " + on);
            Program.Check(Template("gb.guide8.08").Parse(on).ToString() == on && Template("gb.guide8.08").Parse(on).Get("en_to3") == "Yancheng", "and read back");

            // An older build, whose template has no such fields, drops the keys and keeps the rest.
            GuideTemplate plain = Template("gb.guide8.05");
            Program.Check(plain.FieldOf(Bilingual.FlagKey) == null && plain.Parse("code=G105;en=1;en_code=Route;speed=auto").ToString() == "code=G105;speed=auto", "a template without the fields ignores the keys");

            // A sparse field takes the default for a missing key, and a value that is the default is not written; a field that is not sparse is written whatever it is.
            GuideSpec back = Template("gb.guide8.08").Parse("to1=如皋;en_to1=Rugao");
            Program.Check(back.Get("en") == "0" && back.Get("en_to1") == "Rugao", "a missing key is the default");
            Program.Check(Template("gb.guide8.08").Default().With("en", "1").With("en", "0").ToString() == Template("gb.guide8.08").Default().ToString(), "the switch turned on and off leaves the spec as it was");

            // The longest English of every lane still fits the field a spec is saved in.
            string longest = "en=1";
            for (int i = 1; i <= 6; i++)
                longest += ";to" + i + "=东莞市中心城区;en_to" + i + "=" + new string('M', Bilingual.MaxLength);
            Program.Check(System.Text.Encoding.UTF8.GetByteCount(Spec("gb.guide8.04", longest).ToString()) <= GuideSpec.MaxBytes, "a spec with the longest English everywhere is within " + GuideSpec.MaxBytes + " bytes");
        }

        private static void SwitchOff()
        {
            foreach ((string id, string settings) in kVariants)
            {
                GuideTemplate template = Template(id);
                GuideSpec plain = Spec(id, settings);
                string inner = BoardDump.Of(template.Bilingual.Inner(plain));
                Program.Check(BoardDump.Of(template.Layout(plain)) == inner, id + " [" + settings + "]: the board with the switch off is the template's own");
                // English typed but the switch off: still the template's own, with no report.
                GuideSpec typed = Spec(id, settings + ";" + Typed(id).Replace("en=1;", "").Replace("en=1", ""));
                GuideBoard board = template.Layout(typed);
                Program.Check(BoardDump.Of(board) == inner && board.English == null, id + " [" + settings + "]: typed English with the switch off is not drawn");
            }

            // A board laid out with the switch on does not change the next with it off (the scope is left, nothing is kept in a layout).
            Lay("gb.guide8.08", Typed("gb.guide8.08"));
            GuideTemplate t = Template("gb.guide8.08");
            Program.Check(BoardDump.Of(t.Layout(t.Default())) == BoardDump.Of(t.Bilingual.Inner(t.Default())), "after a board with English the next is as before");
            var block = new GuideBlock(1f, 1f);
            Program.Check(ReferenceEquals(Bilingual.Under(block, "出口", 1f, GuideColour.White), block), "outside a layout the builders' hook gives the block itself");
        }

        // ------------------------------------------------------------------ what it draws

        private static void Placement()
        {
            string[] english = { "Rugao", "Haian", "Yancheng" };
            GuideBoard off = Lay("gb.guide8.08", "");
            GuideBoard on = Lay("gb.guide8.08", "en=1;en_to1=" + english[0] + ";en_to2=" + english[1] + ";en_to3=" + english[2]);
            float h = on.H;
            GuidePart part = on.Main;
            List<GuideElement> lines = Lines(part), cells = Cells(part);
            Program.Check(lines.Count == 3 && cells.Count == 3 && on.English != null && on.English.Lines == 3 && on.English.Empty == 0, "three Chinese texts, three English lines: " + lines.Count);
            float grown = 0f;
            for (int i = 0; i < Math.Min(lines.Count, cells.Count); i++)
            {
                GuideElement line = lines[i], zh = cells[i];
                Program.Check(line.Text == english[i], "line " + i + " is the typed text: " + line.Text);
                Program.Near(line.Y, zh.Bottom + 0.2f * h, 1e-4f, "line " + i + " stands 1/5 h under the Chinese (Table 6: 1/5 h to 1/3 h)");
                Program.Near(line.X + line.W * 0.5f, zh.X + zh.W * 0.5f, 1e-3f, "line " + i + " is centred on the Chinese");
                Program.Check(line.H <= 0.5f * h + 1e-5f && line.H >= h / 3f - 1e-5f, "line " + i + " is 1/3 h to 1/2 h high (Table 4): " + line.H / h);
                Program.Check(line.Colour == zh.Colour && line.Align == GuideAlign.Centre, "line " + i + " has the colour of the Chinese");
                Program.Check(line.W <= zh.W + 1e-4f, "line " + i + " is not wider than the Chinese");
                grown += 0.2f * h + line.H;
            }

            Program.Near(on.Main.H - off.Main.H, grown, 1e-3f, "the board grows by the lines and their spacing");
            Program.Near(on.Main.W, off.Main.W, 1e-4f, "and not wider");
            Program.Check(lines.All(l => l.Mode == GuideTextMode.Line), "the lines are the painter's Line mode");
            // The distances stay level with their Chinese row (the numbers are in the row, the English under it).
            List<GuideElement> numbers = part.Elements.Where(e => e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Ink).ToList();
            Program.Check(numbers.Count > 0, "the distances are still drawn");
        }

        private static void Fill()
        {
            GuideBoard noun = Lay("gb.guide8.08", "to1=隧道;to2=服务区;to3=停车区");
            Program.Check(noun.English == null, "the switch is off: no English");
            noun = Lay("gb.guide8.08", "to1=隧道;to2=服务区;to3=停车区;en=1");
            string[] texts = Lines(noun.Main).Select(l => l.Text).ToArray();
            Program.Check(texts.SequenceEqual(new[] { "TUNNEL", "SERVICE AREA", "REST AREA" }), "the nouns of the table are filled in: " + string.Join("|", texts));
            noun = Lay("gb.guide8.08", "to1=隧道;en=1;en_to1=Tunnel Rd");
            Program.Check(Lines(noun.Main).Select(l => l.Text).First() == "Tunnel Rd", "a typed line wins over the table");
            // A fixed noun the template writes itself gets its English without a field: the exit sign's 出口.
            GuideBoard sign = Lay("guide.exit.sign", "en=1");
            Program.Check(Lines(sign.Main).Count == 1 && Lines(sign.Main)[0].Text == "EXIT", "出口 on the exit sign is followed by EXIT");
            GuideBoard signOff = Lay("guide.exit.sign", "");
            Program.Check(Lines(signOff.Main).Count == 0 && sign.Main.H > signOff.Main.H, "and the board is the taller by that line");
            // A text with a pictogram after it ([地铁]) has its English under the place.
            GuideBoard tagged = Lay("gb.guide8.03", "to1=学府路[地铁];en=1;en_to1=Xuefu Lu");
            Program.Check(Lines(tagged.Main).Count == 1 && Lines(tagged.Main)[0].Text == "Xuefu Lu", "a place with a pictogram has its English");
            // A road number is a plate, not Chinese: no English is set for it.
            GuideBoard plate = Lay("gb.guide8.01", "en=1;en_to1=Nothing");
            Program.Check(Lines(plate.Main).Count == 0 && plate.English.Lines == 0, "the number plate G2 takes no English");
        }

        private static void Empty()
        {
            GuideTemplate template = Template("gb.guide8.08");
            GuideSpec on = Spec("gb.guide8.08", "en=1");
            GuideBoard board = template.Layout(on);
            Program.Check(BoardDump.Of(board) == BoardDump.Of(template.Bilingual.Inner(on)), "the switch on and nothing typed: the board is as without it");
            Program.Check(board.English != null && board.English.Empty == 3 && board.English.Lines == 0, "and the report says three texts have no English");
            Program.Check(Bilingual.Notes(board.English, true).Any(n => n.Contains("3")) && Bilingual.Notes(board.English, false).Any(), "the editor is told in a short note, in both languages");
            Program.Check(!Bilingual.Notes(Lay("gb.guide8.08", "en=1;en_to1=A;en_to2=B;en_to3=C").English, true).Any(), "and nothing when all is as asked");
            Program.Check(!Bilingual.Notes(null, true).Any(), "and for a board that was not asked");
        }

        private static void Fit()
        {
            // 如皋: two characters, 2.2 h wide.
            GuideBoard off = Lay("gb.guide8.08", "to2=;d2=;to3=;d3=");
            float h = off.H, zhW = Cells(off.Main)[0].W;

            GuideBoard fits = Lay("gb.guide8.08", "to2=;d2=;to3=;d3=;en=1;en_to1=Rugao");
            GuideElement line = Lines(fits.Main)[0];
            Program.Near(line.H, 0.5f * h, 1e-5f, "a line that fits is 1/2 h high");
            Program.Check(fits.English.Shrunk == 0 && fits.English.Widened == 0, "and is not reported");

            GuideBoard smaller = Lay("gb.guide8.08", "to2=;d2=;to3=;d3=;en=1;en_to1=Dongxin");
            line = Lines(smaller.Main)[0];
            Program.Check(smaller.English.Shrunk == 1 && line.H < 0.5f * h - 1e-4f && line.H >= h / 3f - 1e-5f, "a line wider than the Chinese is made smaller, not below 1/3 h: " + line.H / h);
            Program.Check(line.W <= zhW + 1e-4f && smaller.English.Widened == 0 && smaller.Main.W == fits.Main.W, "and then fits the Chinese, and the board is not wider");
            Program.Check(Bilingual.Notes(smaller.English, true).Any(n => n.Contains("缩小")), "the editor says it was made smaller");

            GuideBoard wider = Lay("gb.guide8.08", "to2=;d2=;to3=;d3=;en=1;en_to1=Rugao Dongzhan");
            line = Lines(wider.Main)[0];
            Program.Near(line.H, h / 3f, 1e-5f, "one that is wider at the least height stays at 1/3 h");
            GuideElement chinese = Cells(wider.Main)[0];
            Program.Near(line.X + line.W * 0.5f, chinese.X + chinese.W * 0.5f, 1e-3f, "the Chinese is centred over the wider line, as the line under it");
            Program.Check(wider.English.Widened == 1 && line.W > zhW && wider.Main.W > fits.Main.W, "and makes the board wider");
            Program.Check(line.X >= wider.Main.Edge - 1e-4f && line.X + line.W <= wider.Main.W - wider.Main.Edge + 1e-4f, "it is within the board");
            Program.Check(Bilingual.Notes(wider.English, false).Any(n => n.Contains("wider")), "the editor says so");

            string tooLong = new string('M', Bilingual.MaxLength);
            GuideBoard refused = Lay("gb.guide8.08", "to2=;d2=;to3=;d3=;en=1;en_to1=" + tooLong);
            Program.Check(Lines(refused.Main).Count == 0 && refused.English.Refused == 1, "a line far wider than the Chinese is left out and reported");
            Program.Check(BoardDump.Of(refused) == BoardDump.Of(off), "and the board is as without it");
            Program.Check(Bilingual.Notes(refused.English, true).Any(n => n.Contains("太长")), "the editor says it is too long");
        }

        private static void Upright()
        {
            // 路6 upright: the characters stand one under the other in a board of a fixed width: no English, no switch to show.
            GuideSpec spec = Spec("gb.guide8.06", "layout=v;en=1;en_name=Nanjing Lu");
            GuideBoard board = Template("gb.guide8.06").Layout(spec);
            Program.Check(board.English == null && Lines(board.Main).Count == 0, "the upright road name has no English line");
            Program.Check(!Template("gb.guide8.06").FieldOf(Bilingual.FlagKey).Visible(spec) && Template("gb.guide8.06").FieldOf(Bilingual.FlagKey).Visible(Spec("gb.guide8.06", "layout=h")), "and the switch is not shown for it");
        }

        private static void Capitals()
        {
            GuideBoard exit = Lay("guide.exit.advance", "en=1;en_to1=Rugao;en_to2=Hai An");
            Program.Check(Lines(exit.Main).Select(l => l.Text).SequenceEqual(new[] { "RUGAO", "HAI AN" }), "the exit boards' letters are capitals (4.5.1 e): " + string.Join("|", Lines(exit.Main).Select(l => l.Text)));
            GuideBoard route = Lay("gb.guide8.08", "en=1;en_to1=Rugao");
            Program.Check(Lines(route.Main)[0].Text == "Rugao", "a route board's English is as typed");
            Program.Check(Lines(Lay("guide.exit.sign", "en=1").Main)[0].Text == "EXIT", "the nouns of the table are capitals");
        }

        private static void Level()
        {
            // Two names in a row, one with English: both Chinese texts stand on the same line (the one without English keeps the room of a line).
            GuideBoard board = Lay("gb.guide8.03", "to1=学府路;to2=东湖路;tl1=;tr1=;en=1;en_to1=Xuefu Lu");
            List<GuideElement> cells = Cells(board.Main).Where(c => c.Text == "学府路" || c.Text == "东湖路").ToList();
            Program.Check(cells.Count == 2 && Math.Abs(cells[0].Y - cells[1].Y) < 1e-4f, "the Chinese of both names is on one line: " + string.Join(",", cells.Select(c => c.Y)));
            Program.Check(board.English.Empty == 1 && board.English.Lines == 1, "one has English, one has none: " + board.English.Empty + ", " + board.English.Lines);
        }

        // ------------------------------------------------------------------ nothing over anything

        /// <summary>Whether the point is inside the paint of an element (a figure by its contours, a rectangle and an oval by their boxes).</summary>
        private static bool Hits(GuideElement e, float x, float y)
        {
            switch (e.Kind)
            {
                case GuideElementKind.Rect:
                    return x >= e.X && x <= e.Right && y >= e.Y && y <= e.Bottom;
                case GuideElementKind.Oval:
                {
                    float cx = e.X + e.W * 0.5f, cy = e.Y + e.H * 0.5f, a = e.W * 0.5f, b = e.H * 0.5f;
                    return a > 0f && b > 0f && (x - cx) * (x - cx) / (a * a) + (y - cy) * (y - cy) / (b * b) <= 1f;
                }

                case GuideElementKind.Polygon:
                case GuideElementKind.Arrow:
                    return e.Points != null && Winding(new List<float[]> { e.Points }, x, y) != 0;
                case GuideElementKind.Path:
                    return e.Contours != null && Winding(e.Contours, x, y) != 0;
                default:
                    return x >= e.X && x <= e.Right && y >= e.Y && y <= e.Bottom;
            }
        }

        private static int Winding(List<float[]> contours, float x, float y)
        {
            int w = 0;
            foreach (float[] c in contours)
            {
                int n = c.Length / 2;
                for (int i = 0; i < n; i++)
                {
                    float x0 = c[i * 2], y0 = c[i * 2 + 1], x1 = c[(i + 1) % n * 2], y1 = c[(i + 1) % n * 2 + 1];
                    if (y0 <= y)
                    {
                        if (y1 > y && (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0) > 0f)
                            w++;
                    }
                    else if (y1 <= y && (x1 - x0) * (y - y0) - (x - x0) * (y1 - y0) < 0f)
                    {
                        w--;
                    }
                }
            }

            return w;
        }

        /// <summary>The problems of one board: an English line outside the board's inside, or over any other element (a text by its box, a figure by its paint; a rectangle or an oval may be its ground, whole).</summary>
        private static List<string> Problems(GuideBoard board)
        {
            var problems = new List<string>();
            foreach (GuidePart part in board.Parts)
            {
                foreach (GuideElement line in Lines(part))
                {
                    float edge = part.Edge;
                    if (line.X < edge - 1e-4f || line.Right > part.W - edge + 1e-4f || line.Y < edge - 1e-4f || line.Bottom > part.H - edge + 1e-4f)
                        problems.Add($"\"{line.Text}\" is outside the board's inside ({line.X:0.000},{line.Y:0.000},{line.Right:0.000},{line.Bottom:0.000} of {part.W:0.000} x {part.H:0.000})");
                    var samples = new List<(float, float)>();
                    for (int i = 0; i <= 24; i++)
                    {
                        for (int j = 0; j <= 4; j++)
                            samples.Add((line.X + line.W * i / 24f, line.Y + line.H * (0.02f + 0.96f * j / 4f)));
                    }

                    foreach (GuideElement other in part.Elements)
                    {
                        if (ReferenceEquals(other, line))
                            continue;
                        if (other.Kind == GuideElementKind.Text)
                        {
                            float w = Math.Min(line.Right, other.Right) - Math.Max(line.X, other.X), hh = Math.Min(line.Bottom, other.Bottom) - Math.Max(line.Y, other.Y);
                            if (w > 1e-3f && hh > 1e-3f)
                                problems.Add($"\"{line.Text}\" overlaps the text \"{other.Text}\" by {w:0.000} x {hh:0.000}");
                            continue;
                        }

                        int hits = samples.Count(p => Hits(other, p.Item1, p.Item2));
                        bool ground = (other.Kind == GuideElementKind.Rect || other.Kind == GuideElementKind.Oval) && hits == samples.Count;
                        if (hits > 0 && !ground)
                            problems.Add($"\"{line.Text}\" is over a {other.Kind} ({hits} of {samples.Count} points)");
                    }
                }
            }

            return problems;
        }

        private static void Clear()
        {
            foreach ((string id, string settings) in kVariants)
            {
                // The English of a medium length, then one that is long (made smaller, wider), then one left out.
                foreach (string english in new[] { "Placename", "Guangdong Dongguan Shi", new string('W', Bilingual.MaxLength) })
                {
                    GuideBoard board = Lay(id, settings + ";" + Typed(id, english));
                    int lines = board.Parts.Sum(p => Lines(p).Count);
                    List<string> problems = Problems(board);
                    Program.Check(problems.Count == 0, id + " [" + settings + "] with \"" + english + "\": " + string.Join("; ", problems.Take(3)));
                    if (english == "Placename" && settings == "" && HasSlot(id))
                        Program.Check(lines > 0, id + ": the English of the default board is drawn");
                }
            }

            // Every board that was asked for English and has Chinese in a slot draws it, on the boards of the table.
            foreach (string id in Bilingual.OptInIds)
            {
                if (!HasSlot(id))
                    continue;
                GuideBoard board = Lay(id, Typed(id));
                Program.Check(board.English != null && board.English.Lines > 0, id + ": the default board has at least one English line (a slot's Chinese is drawn as one run)");
                Program.Check(Problems(board).Count == 0, id + ": the default board with English has nothing over anything: " + string.Join("; ", Problems(board).Take(3)));
            }
        }

        /// <summary>The default board has Chinese in one of the template's slots that its form shows.</summary>
        private static bool HasSlot(string id)
        {
            GuideTemplate template = Template(id);
            GuideSpec spec = template.Default().With(Bilingual.FlagKey, "1");
            return template.Bilingual.Keys.Any(k => spec.Get(k).Length > 0 && (template.FieldOf(k).Visible == null || template.FieldOf(k).Visible(spec)));
        }

        // ------------------------------------------------------------------ threads, words

        private static void Threads()
        {
            string[] ids = { "gb.guide8.08", "gb.guide8.01", "guide.exit.advance", "gb.aux.11", "gb.guide9.38", "gb.guide8.04" };
            var specs = new List<(string Id, GuideSpec Spec)>();
            var expected = new List<string>();
            foreach (string id in ids)
            {
                foreach (bool on in new[] { false, true })
                {
                    GuideSpec spec = Spec(id, on ? Typed(id) : "");
                    specs.Add((id, spec));
                    expected.Add(BoardDump.Of(Template(id).Layout(spec)));
                }
            }

            int wrong = 0;
            Parallel.For(0, 240, new ParallelOptions { MaxDegreeOfParallelism = 8 }, n =>
            {
                int k = n % specs.Count;
                if (BoardDump.Of(Template(specs[k].Id).Layout(specs[k].Spec)) != expected[k])
                    System.Threading.Interlocked.Increment(ref wrong);
            });
            Program.Check(wrong == 0, "layouts with and without English on eight threads are each as on one: " + wrong + " differ");
        }

        private static void Words()
        {
            SignDef def = SignCatalog.ById("gb.guide8.08");
            string plain = TsAheadRule.WordsOf(SignInstances.Of(def, Spec("gb.guide8.08", "").ToString()));
            string with = TsAheadRule.WordsOf(SignInstances.Of(def, Spec("gb.guide8.08", "en=1;en_to1=Rugao;en_to2=Haian;en_to3=Yancheng").ToString()));
            Program.Check(plain.Length > 0 && plain == with, "the words of a board the map's labels read do not take the English: " + with);
        }
    }
}
