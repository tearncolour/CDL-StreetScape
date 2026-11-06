using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>The speed limits by vehicle type (Signs.TypeSpeedSigns): the template, the catalogue and its search, the round signs that are the catalogue's own, the layout in every combination, the sizes and the gantry.</summary>
    internal static class TypeSpeedTests
    {
        private const float Tol = 1e-4f;

        public static void RunAll()
        {
            Program.Run("type speed: the template, its fields, defaults and visibility", Fields);
            Program.Run("type speed: the catalogue lists it in the guide tab and the search finds it by its words", Catalogue);
            Program.Run("type speed: the round signs are the catalogue's own 禁39 and 示14, at the numbers typed", RoundSigns);
            Program.Run("type speed: everything is inside the board and nothing overlaps, in every combination", Sweep);
            Program.Run("type speed: the sizes follow the round sign's diameter and the photo's proportions", Sizes);
            Program.Run("type speed: the four boards of the owner's photo", Photo);
            Program.Run("type speed: the arrow", Arrow);
            Program.Run("type speed: the support the planner gives and a row of boards on a gantry", Supports);
        }

        private static GuideTemplate Template => GuideTemplates.ById(TypeSpeedSigns.TemplateId);

        private static GuideSpec Spec(string settings)
        {
            GuideSpec spec = Template.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return spec;
        }

        private static readonly string[] kBandIds = { "100", "80", "60", "30" };

        private static void Fields()
        {
            GuideTemplate t = Template;
            Program.Check(t != null && t.Group == "guide" && t.Standalone && t.Zh == "分车型限速标志" && t.En == "Speed limits by vehicle type", "the template is a standalone guide sign with its names");
            Program.Check(string.Join(",", t.Fields.Select(f => f.Key)) == "type,name,nameEn,max,min,english,arrow,speed,lt", "the fields, the speed band, then the lighting that every board of the standard has: " + string.Join(",", t.Fields.Select(f => f.Key)));
            foreach (GuideField f in t.Fields)
                Program.Check(!string.IsNullOrEmpty(f.Zh) && !string.IsNullOrEmpty(f.En) && f.Zh.Length <= 14 && f.En.Length <= 36, "short labels in both languages: " + f.Key);

            GuideSpec d = t.Default();
            Program.Check(d.Get("type") == "car" && d.Get("max") == "120" && d.Get("min") == "110" && d.Flag("english") && d.Flag("arrow"), "defaults: the small car 120 down to 110, English line and arrow on: " + d);
            Program.Check(t.FieldOf("type").Choices.Select(c => c.Value).SequenceEqual(new[] { "car", "coach", "cartruck", "truck", "bigcoach", "hazmat", "all", "custom" }), "the vehicle types and the custom one");

            // Visibility: the custom names only for a custom type, the arrow only between two limits.
            Program.Check(!t.FieldOf("name").Visible(d) && !t.FieldOf("nameEn").Visible(d), "a named type has no name fields");
            GuideSpec custom = d.With("type", "custom");
            Program.Check(t.FieldOf("name").Visible(custom) && t.FieldOf("nameEn").Visible(custom), "a custom type has them");
            Program.Check(t.FieldOf("arrow").Visible(d), "both limits: the arrow row is shown");
            Program.Check(!t.FieldOf("arrow").Visible(d.With("min", "0")) && !t.FieldOf("arrow").Visible(d.With("min", "")) && !t.FieldOf("arrow").Visible(d.With("max", "")), "one limit or none: no arrow row");

            // Parsing: a word that is not a type is the default one, the limits keep digits and one point, the text round-trips.
            GuideSpec parsed = t.Parse("type=banana;max=12a0;min=1.5.5");
            Program.Check(parsed.Get("type") == "car" && parsed.Get("max") == "120" && parsed.Get("min").Replace(".", "").Length == 2, "an unknown type is the default, letters are dropped from a limit: " + parsed);
            GuideSpec round = Spec("type=custom;name=危险品;nameEn=Hazmat;max=80;min=0;english=0;arrow=0");
            Program.Check(t.Parse(round.ToString()).ToString() == round.ToString(), "a spec is written and read back: " + round);
            Program.Check(round.ToString().Length <= GuideSpec.MaxBytes, "a spec with every field set fits the spec's size limit");
        }

        // The search of the panel (matches() in UI/src/ts-ui.js): every word of the query is found in the name, the English name, the keywords or the pinyin, or the code.
        private static bool Matches(SignChoice c, string query)
        {
            string N(string text) => (text ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            string code = N(c.Code);
            string[] fields = { N(c.Zh), N(c.En), N(string.Join(" ", c.Keywords ?? new string[0])), N(c.Pinyin) };
            foreach (string word in N(query).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool inFields = fields.Any(f => f.Contains(word));
                bool ok = Regex.IsMatch(word, @"^\D+\d+$") ? code == word || inFields : code.Contains(word) || inFields;
                if (!ok)
                    return false;
            }

            return true;
        }

        private static void Catalogue()
        {
            SignChoice c = SignCatalog.ChoiceById(TypeSpeedSigns.TemplateId);
            Program.Check(c != null && c.Group == "guide" && c.Def.Template == Template, "a choice of the catalogue, in the guide group");
            Program.Check(SignCatalog.Choices.Count(x => x.Id == TypeSpeedSigns.TemplateId) == 1, "listed once, and no preset of it for each combination");
            foreach (string query in new[] { "小客车", "客货车", "客车", "货车", "分车型", "限速", "车型", "最低限速", "最高限速", "速度", "门架", "speed by vehicle", "commuter car", "carriage truck", "coach", "hazmat", "危险品", "大型客车", "minimum speed", "vehicle type", "fcxx" })
                Program.Check(Matches(c, query), "the search for '" + query + "' finds it");
            Program.Check(!Matches(c, "鱼"), "and a word that is not about it does not");
            Program.Check(c.Def.Id == TypeSpeedSigns.TemplateId && c.Def.Width > 0f && c.Def.Height > 0f, "its def has the board's size");
        }

        private static bool Same(GuideElement a, GuideElement b) =>
            a.Kind == b.Kind && a.Colour == b.Colour && a.Text == b.Text && a.Mode == b.Mode && Math.Abs(a.X - b.X) < Tol && Math.Abs(a.Y - b.Y) < Tol && Math.Abs(a.W - b.W) < Tol && Math.Abs(a.H - b.H) < Tol
            && (a.Points == null) == (b.Points == null) && (a.Points == null || a.Points.Length == b.Points.Length && a.Points.Zip(b.Points, (p, q) => Math.Abs(p - q) < Tol).All(ok => ok));

        /// <summary>Every element of the catalogue's sign (at the limit typed), moved to the box, is on the board: the same ring, the same figure, not one like it.</summary>
        private static void ProvesSame(GuideBoard ours, string id, string limit, string speed, TypeSpeedSigns.Box at, string what)
        {
            GuideTemplate sign = GuideTemplates.ById(id);
            GuideBoard alone = sign.Layout(sign.Default().With("limit", limit).With("speed", speed));
            Program.Near(alone.Main.W, at.W, Tol, what + ": the sign alone is as wide as its box");
            Program.Check(alone.Main.Elements.Count >= 2, what + ": the sign alone has its ring and figure");
            foreach (GuideElement e in alone.Main.Elements)
                Program.Check(ours.Main.Elements.Any(o => Same(o, e.Moved(at.X, at.Y))), what + ": " + e.Kind + " " + e.Colour + " " + e.Text + " of the sign alone is on the board");
        }

        private static void RoundSigns()
        {
            for (int band = 0; band < 4; band++)
            {
                GuideSpec spec = Spec("speed=" + kBandIds[band] + ";max=120;min=110");
                TypeSpeedSigns.Plan plan = TypeSpeedSigns.PlanOf(spec, band);
                GuideBoard board = Template.Layout(spec);
                // The round signs are as large as the catalogue's at the 40 to 70 km/h band, and for the slowest band at that band: the larger ones would make a board no lane could carry.
                string discSpeed = band == 3 ? "30" : "60";
                ProvesSame(board, "gb.prohib.39", "120", discSpeed, plan.Max, "band " + band + " max");
                ProvesSame(board, "gb.mand.14", "110", discSpeed, plan.Min, "band " + band + " min");
                Program.Check(board.Main.Elements.Any(e => e.Kind == GuideElementKind.Oval && e.Colour == GuideColour.White && Math.Abs(e.X - plan.Max.X) < Tol && Math.Abs(e.W - plan.Round) < Tol), "band " + band + ": the white ground of the round max sign");
            }

            // The numbers typed are the numbers drawn, including three digits and a decimal; a leading zero is dropped.
            foreach (string max in new[] { "5", "60", "120", "99.5", "070" })
            {
                GuideSpec spec = Spec("max=" + max + ";min=0");
                string written = TypeSpeedSigns.PlanOf(spec, 2).MaxText;
                TypeSpeedSigns.Plan plan = TypeSpeedSigns.PlanOf(spec, 2);
                GuideBoard board = Template.Layout(spec);
                Program.Check(board.Main.Elements.Any(o => o.Text == written), "the number " + max + " is drawn as " + written);
                ProvesSame(board, "gb.prohib.39", written, "60", plan.Max, "max " + max);
            }

            Program.Check(TypeSpeedSigns.PlanOf(Spec("max=070;min=0"), 2).MaxText == "70", "a leading zero is dropped");
            foreach (string none in new[] { "0", "00", "0.0", "", "." })
            {
                TypeSpeedSigns.Plan p = TypeSpeedSigns.PlanOf(Spec("max=" + none + ";min=" + none), 2);
                Program.Check(p.MaxText.Length == 0 && p.MinText.Length == 0 && p.Max.Empty && p.Min.Empty && p.Arrow.Empty, "a limit of '" + none + "' is no limit: no round sign, no arrow");
            }
        }

        private static bool Overlap(TypeSpeedSigns.Box a, TypeSpeedSigns.Box b) =>
            !a.Empty && !b.Empty && a.X < b.Right - Tol && b.X < a.Right - Tol && a.Y < b.Bottom - Tol && b.Y < a.Bottom - Tol;

        private static void Inside(TypeSpeedSigns.Box b, TypeSpeedSigns.Plan p, string what)
        {
            if (b.Empty)
                return;
            Program.Check(b.X >= p.Edge - Tol && b.Right <= p.W - p.Edge + Tol && b.Y >= p.Edge - Tol && b.Bottom <= p.H - p.Edge + Tol, what + " is inside the board's edge: " + b.X + ".." + b.Right + " x " + b.Y + ".." + b.Bottom + " of " + p.W + " x " + p.H);
        }

        private static void Sweep()
        {
            string[] names = { "", "客", "客车", "客货车", "危险品运输车", "危险品运输车辆", "ABCDEFGH" };
            string[] english = { "", "Bus", "Hazardous goods vehicle", "WWWWWWWWWWWWWWWWWWWWWWWW" };
            string[] limits = { "120|110", "100|60", "80|0", "0|60", "0|0", "|", "1|999", "99.5|60.5" };
            var types = new List<string>(Template.FieldOf("type").Choices.Select(c => c.Value));
            int boards = 0;
            foreach (string type in types)
            foreach (string name in type == "custom" ? names : new[] { "" })
            foreach (string en in type == "custom" ? english : new[] { "" })
            foreach (string limit in limits)
            foreach (string flags in new[] { "english=1;arrow=1", "english=0;arrow=1", "english=1;arrow=0", "english=0;arrow=0" })
            {
                string[] pair = limit.Split('|');
                GuideSpec spec = Spec("type=" + type + ";name=" + name + ";nameEn=" + en + ";max=" + pair[0] + ";min=" + pair[1] + ";" + flags);
                for (int band = 0; band < 4; band++)
                {
                    spec = spec.With("speed", kBandIds[band]);
                    TypeSpeedSigns.Plan p = TypeSpeedSigns.PlanOf(spec, band);
                    GuideBoard board = Template.Layout(spec);
                    string what = spec + " band " + band;
                    boards++;
                    Program.Check(board.Parts.Count == 1 && Math.Abs(board.Main.W - p.W) < Tol && Math.Abs(board.Main.H - p.H) < Tol, what + ": one part of the plan's size");
                    var boxes = new[] { p.Name, p.English, p.Max, p.Min, p.Arrow };
                    var labels = new[] { "name", "English", "max", "min", "arrow" };
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        Inside(boxes[i], p, what + " " + labels[i]);
                        for (int j = i + 1; j < boxes.Length; j++)
                            Program.Check(!Overlap(boxes[i], boxes[j]), what + ": " + labels[i] + " and " + labels[j] + " overlap");
                    }

                    // The text is over the round signs and the name over the English line; the row of round signs stands on one level, symmetric about the middle.
                    float rowTop = !p.Max.Empty ? p.Max.Y : !p.Min.Empty ? p.Min.Y : p.H - p.Edge;
                    if (!p.English.Empty)
                        Program.Check(p.English.Bottom <= rowTop + Tol, what + ": the English line is over the round signs");
                    if (!p.Name.Empty)
                        Program.Check(p.Name.Bottom <= rowTop + Tol && (p.English.Empty || p.Name.Bottom <= p.English.Y + Tol), what + ": the name is over the English line and the round signs");
                    if (!p.Max.Empty && !p.Min.Empty)
                    {
                        Program.Check(Math.Abs(p.Max.Y - p.Min.Y) < Tol && p.Max.Right < p.Min.X, what + ": the two round signs side by side on one level");
                        Program.Near((p.Max.X + p.Min.Right) * 0.5f, p.W * 0.5f, Tol, what + ": the row is centred");
                        Program.Check(p.Arrow.Empty == !spec.Flag("arrow"), what + ": the arrow is there when it is asked for");
                        if (!p.Arrow.Empty)
                            Program.Near(p.Arrow.X - p.Max.Right, p.Min.X - p.Arrow.Right, Tol, what + ": the arrow is as far from one round sign as from the other");
                    }
                    else
                    {
                        Program.Check(p.Arrow.Empty, what + ": no arrow with one limit or none");
                        foreach (TypeSpeedSigns.Box lone in new[] { p.Max, p.Min })
                        {
                            if (!lone.Empty)
                                Program.Near(lone.X + lone.W * 0.5f, p.W * 0.5f, Tol, what + ": a lone round sign is in the middle");
                        }
                    }

                    Program.Check(p.Max.Empty == (p.MaxText.Length == 0) && p.Min.Empty == (p.MinText.Length == 0), what + ": a limit of zero or nothing has no round sign");
                    if (!p.Name.Empty)
                        Program.Near(p.Name.X + p.Name.W * 0.5f, p.W * 0.5f, Tol, what + ": the name is centred");
                    if (!p.English.Empty)
                        Program.Near(p.English.X + p.English.W * 0.5f, p.W * 0.5f, Tol, what + ": the English line is centred");

                    // The elements as they are laid down: each inside the board's inside, the name's cells as wide as the plan says.
                    foreach (GuideElement e in board.Main.Elements)
                    {
                        Program.Check(e.X >= p.Edge - Tol && e.Right <= p.W - p.Edge + Tol && e.Y >= p.Edge - Tol && e.Bottom <= p.H - p.Edge + Tol, what + ": " + e.Kind + " " + e.Text + " is inside the board's edge");
                        if (e.Kind == GuideElementKind.Polygon)
                        {
                            for (int i = 0; i + 1 < e.Points.Length; i += 2)
                                Program.Check(!float.IsNaN(e.Points[i]) && !float.IsNaN(e.Points[i + 1]) && e.Points[i] >= p.Edge - Tol && e.Points[i] <= p.W - p.Edge + Tol && e.Points[i + 1] >= p.Edge - Tol && e.Points[i + 1] <= p.H - p.Edge + Tol, what + ": a polygon corner is inside the board");
                        }
                    }

                    GuideElement cells = board.Main.Elements.FirstOrDefault(e => e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Cell);
                    Program.Check((cells != null) == !p.Name.Empty && (cells == null || Math.Abs(cells.W - p.Name.W) < Tol && Math.Abs(cells.H - p.Name.H) < Tol), what + ": the name is one run of cells as wide as its box");
                    GuideElement ink = board.Main.Elements.FirstOrDefault(e => e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Ink && e.Colour == GuideColour.White && e.Text == p.EnglishText);
                    Program.Check((ink != null) == !p.English.Empty, what + ": the English line is drawn when it has room");
                    Program.Check(p.English.Empty == (p.EnglishText.Length == 0), what + ": an English line that is off or empty has no box");
                }
            }

            Program.Check(boards > 3000, "the sweep covered the combinations: " + boards);
        }

        private static void Sizes()
        {
            GuideTemplate t = Template;
            float[] round = { 0.8f, 0.8f, 0.8f, 0.6f };
            for (int band = 0; band < 4; band++)
            {
                TypeSpeedSigns.Plan p = TypeSpeedSigns.PlanOf(t.Default().With("speed", kBandIds[band]), band);
                Program.Near(p.Round, round[band], 1e-6f, "band " + band + ": the round signs are " + round[band] + " m (table 7's 40 to 70 and under 40 km/h sizes, never the larger ones)");
                Program.Near(p.Round / p.W, 0.24f, Tol, "band " + band + ": a round sign is 0.24 of the board's width");
                Program.Near(p.W / p.H, 1.12f, Tol, "band " + band + ": the board is 1.12 times as wide as high");
                Program.Near(p.Max.X, 0.07f * p.W, Tol, "band " + band + ": the max sign stands 0.07 of the width from the left");
                Program.Near(p.W - p.Min.Right, 0.07f * p.W, Tol, "band " + band + ": and the min sign from the right");
                Program.Near(p.H - p.Max.Bottom, 0.07f * p.W, Tol, "band " + band + ": and from the bottom");
                Program.Check(p.Name.Y + p.Name.H < p.H * 0.45f && p.Name.Y >= p.Edge, "band " + band + ": the name is in the upper part: ends at " + (p.Name.Bottom / p.H));
                Program.Check(p.English.H / p.H > 0.08f && p.English.H / p.H < 0.12f, "band " + band + ": the English ink is about a tenth of the height: " + (p.English.H / p.H));
                GuideBoard board = t.Layout(t.Default().With("speed", kBandIds[band]));
                Program.Near(board.Main.W, p.W, Tol, "band " + band + ": the laid out board has the plan's width");
            }

            // The catalogue's def has the default band's size (a street of 40 to 70 km/h); the sizes in metres: 3.33 x 2.98 and, for the slowest band, 2.5 x 2.23.
            SignDef def = SignCatalog.ById(TypeSpeedSigns.TemplateId);
            Program.Near(def.Width, 0.8f / 0.24f, 1e-3f, "the catalogue's width: " + def.Width);
            Program.Near(def.Height, 0.8f / 0.24f / 1.12f, 1e-3f, "the catalogue's height: " + def.Height);
            Program.Near(TypeSpeedSigns.PlanOf(t.Default().With("speed", "30"), 3).W, 2.5f, 1e-3f, "the slowest band: 2.5 m wide");

            // A name of three characters is as wide as a name of two spread out to the same width; four or more are 0.1 cell apart; a long one is made smaller to the room it has.
            TypeSpeedSigns.Plan three = TypeSpeedSigns.PlanOf(Spec("type=cartruck"), 2), two = TypeSpeedSigns.PlanOf(Spec("type=coach"), 2), one = TypeSpeedSigns.PlanOf(Spec("type=custom;name=车"), 2);
            Program.Near(two.Name.W, three.Name.W, Tol, "'客 车' is spaced out to the width of '客货车'");
            Program.Near(three.Name.W, 3f * three.Cell + 2f * 0.12f * three.Cell, Tol, "three characters are 0.12 cell apart");
            Program.Check(one.Name.W == one.Cell && two.NameGap > three.NameGap, "one character stands alone, two are further apart than three");
            TypeSpeedSigns.Plan seven = TypeSpeedSigns.PlanOf(Spec("type=custom;name=危险品运输车辆"), 2);
            Program.Check(seven.Cell < three.Cell && seven.Name.W <= seven.W - 2f * seven.Edge, "a name of seven characters is made smaller to fit: cell " + seven.Cell + " against " + three.Cell);
            Program.Check(TypeSpeedSigns.PlanOf(Spec("type=custom;name=;nameEn="), 2).Name.Empty, "a custom type without a name has no name box");
            TypeSpeedSigns.Plan noEnglish = TypeSpeedSigns.PlanOf(Spec("type=custom;name=客车;nameEn="), 2), withEnglish = TypeSpeedSigns.PlanOf(Spec("type=custom;name=客车;nameEn=Coach"), 2);
            Program.Check(noEnglish.English.Empty && !withEnglish.English.Empty && noEnglish.Name.Y > withEnglish.Name.Y, "an empty English name has no line and the name moves to the middle of the free height");
        }

        /// <summary>The four boards of the photo: the name and the English name, the two limits, the arrow; each text is on the board as an element of its own.</summary>
        private static void Photo()
        {
            var panels = new[]
            {
                new { Type = "car", Zh = "小客车", En = "Commuter car", Max = "120", Min = "110" },
                new { Type = "coach", Zh = "客车", En = "Coach", Max = "120", Min = "100" },
                new { Type = "cartruck", Zh = "客货车", En = "Carriage truck", Max = "100", Min = "80" },
                new { Type = "cartruck", Zh = "客货车", En = "Carriage truck", Max = "100", Min = "60" }
            };
            foreach (var panel in panels)
            {
                GuideSpec spec = Spec("type=" + panel.Type + ";max=" + panel.Max + ";min=" + panel.Min);
                GuideBoard board = Template.Layout(spec);
                TypeSpeedSigns.Plan p = TypeSpeedSigns.PlanOf(spec, 2);
                string what = panel.Zh + " " + panel.Max + "/" + panel.Min;
                Program.Check(p.NameText == panel.Zh && p.EnglishText == panel.En && p.MaxText == panel.Max && p.MinText == panel.Min, what + ": the plan's texts");
                Program.Check(board.Main.Elements.Count(e => e.Mode == GuideTextMode.Cell && e.Text == panel.Zh && e.Colour == GuideColour.White) == 1, what + ": the name is one white run of cells");
                Program.Check(board.Main.Elements.Count(e => e.Mode == GuideTextMode.Ink && e.Text == panel.En && e.Colour == GuideColour.White) == 1, what + ": the English name is white ink");
                Program.Check(board.Main.Elements.Any(e => e.Mode == GuideTextMode.Ink && e.Text == panel.Max && e.Colour == GuideColour.Black), what + ": the max number is black");
                Program.Check(board.Main.Elements.Any(e => e.Mode == GuideTextMode.Ink && e.Text == panel.Min && e.Colour == GuideColour.White), what + ": the min number is white");
                Program.Check(board.Main.Ground == GuideColour.Green && board.Main.BorderColour == GuideColour.White, what + ": a green board with a white border");
            }

            // The English line is a setting: off, there is no line, no ink for it and the name takes the middle of the free height as it does with no English name.
            foreach (string type in new[] { "car", "coach", "cartruck", "truck", "bigcoach", "hazmat", "all" })
            {
                TypeSpeedSigns.Plan on = TypeSpeedSigns.PlanOf(Spec("type=" + type + ";english=1"), 2), off = TypeSpeedSigns.PlanOf(Spec("type=" + type + ";english=0"), 2);
                GuideBoard board = Template.Layout(Spec("type=" + type + ";english=0"));
                Program.Check(!on.English.Empty && on.EnglishText.Length > 0, type + ": English on gives a line: '" + on.EnglishText + "'");
                Program.Check(off.English.Empty && off.EnglishText.Length == 0 && !board.Main.Elements.Any(e => e.Mode == GuideTextMode.Ink && e.Text == on.EnglishText), type + ": English off gives none");
                Program.Check(off.Name.Y > on.Name.Y, type + ": and the name moves down to the middle of the free height");
            }

            // The baseline of the English line stands where it does whether its letters hang below it or not.
            TypeSpeedSigns.Plan plain = TypeSpeedSigns.PlanOf(Spec("type=custom;name=客车;nameEn=Coach"), 2), hanging = TypeSpeedSigns.PlanOf(Spec("type=custom;name=客车;nameEn=Coachg"), 2);
            Program.Near(hanging.English.Bottom - plain.English.Bottom, 0.356f * hanging.English.H, Tol, "a descender hangs 0.356 of the ink height below the baseline, which stays where it is");
        }

        private static void Arrow()
        {
            TypeSpeedSigns.Plan p = TypeSpeedSigns.PlanOf(Template.Default(), 2);
            float[] a = TypeSpeedSigns.DownArrow(p.Arrow);
            Program.Check(a.Length == 14, "an arrow of seven corners");
            float min = a.Where((v, i) => i % 2 == 0).Min(), max = a.Where((v, i) => i % 2 == 0).Max(), top = a.Where((v, i) => i % 2 == 1).Min(), bottom = a.Where((v, i) => i % 2 == 1).Max();
            Program.Near(min, p.Arrow.X, Tol, "the head's left corner is the box's");
            Program.Near(max, p.Arrow.Right, Tol, "the head's right corner is the box's");
            Program.Near(top, p.Arrow.Y, Tol, "the stem starts at the top of the box");
            Program.Near(bottom, p.Arrow.Bottom, Tol, "the tip is at the bottom of the box");
            Program.Near(a[8], p.Arrow.X + p.Arrow.W * 0.5f, Tol, "the tip is in the middle");
            Program.Check(a[1] < a[9] && a[9] > a[5], "the tip is below the head's corners");
            float stem = a[2] - a[0];
            Program.Check(stem > 0.3f * p.Arrow.W && stem < 0.45f * p.Arrow.W, "a thick stem, over a third of the head: " + stem / p.Arrow.W);
            Program.Check(Template.Layout(Template.Default()).Main.Elements.Any(e => e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.White && e.Points.Length == 14), "and it is on the board in white");
        }

        private static AssemblyBoard Board(string settings, int slot) =>
            new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(TypeSpeedSigns.TemplateId), Spec(settings).ToString()), Slot = slot, Member = slot };

        private static void Supports()
        {
            SignDef def = SignCatalog.ById(TypeSpeedSigns.TemplateId);
            Console.WriteLine($"  type speed: default board {def.Width:0.00} x {def.Height:0.00} m, Auto {SupportPlanner.Auto(def).Id}, AutoFor(3 lanes) {SupportPlanner.AutoFor(def, new SupportContext { LanesSameDirection = 3 }).Id}");
            Program.Check(SupportPlanner.Auto(def).Kind == SupportKind.DoublePole, "a board wider than 1.6 m and not over 6 m is on two posts by itself: " + SupportPlanner.Auto(def).Id);
            Program.Check(SupportPlanner.AutoFor(def, new SupportContext { LanesSameDirection = 3 }).Kind == SupportKind.Gantry, "on a road of three lanes in a direction the planner gives the gantry");
            Program.Check(!SupportPlanner.FitsHost(def) && !SupportPlanner.FitsArm(def), "it is too large for a lamp post or a signal's arm");

            var gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            string[] four = { "type=car;max=120;min=110", "type=coach;max=120;min=100", "type=cartruck;max=100;min=80", "type=cartruck;max=100;min=60" };
            foreach (string band in new[] { "60", "30" })
            {
                for (int count = 1; count <= 4; count++)
                {
                    var boards = new List<AssemblyBoard>();
                    for (int i = 0; i < count; i++)
                        boards.Add(Board(four[i] + ";speed=" + band, i));
                    AssemblyPlan plan = Assembly.Plan(boards, gantry, 0f);
                    float width = boards.Sum(b => b.Instance.Def.Width) + (count - 1) * Assembly.SideGap;
                    float needed = width + 2f * Assembly.GantryClear;
                    Console.WriteLine($"  type speed: {count} boards of band {band} ({boards[0].Instance.Def.Width:0.00} m each) on a gantry: span {plan.Layout.ArmLength:0.0} m (needs {needed:0.0}), planar up to {SupportLayouts.PlanarGantryMax:0}");
                    Program.Check(plan.Layout.ArmLength >= needed - 1e-3f, count + " boards of band " + band + ": the span holds them with the posts' clearance: " + plan.Layout.ArmLength + " against " + needed);
                    Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, count + " boards of band " + band + ": no board over another");
                    float lo, hi;
                    foreach (Placement p in plan.Placed)
                    {
                        Assembly.LaneBounds(plan.Layout, p.Def.Width * 0.5f, out lo, out hi);
                        Program.Check(p.Cx >= lo - 1e-3f && p.Cx <= hi + 1e-3f, count + " boards of band " + band + ": a board's edge keeps the clearance from the posts");
                    }

                    SignParts parts = SignBuilder.BuildAssembly(boards, gantry, 0f);
                    Program.Check(parts.Faces.Count == count, count + " boards of band " + band + ": one face each: " + parts.Faces.Count);
                }
            }
        }
    }
}
