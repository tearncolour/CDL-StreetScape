using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The speed limits by vehicle type that hang in a row on an expressway gantry: a green board with the type of vehicle in large white characters and in English under them, and under that the
    /// round maximum-speed sign, a white arrow down and the round minimum-speed sign. The round signs are the catalogue's own 禁39 and 示14 painted on the board (same ring, same figure, in the size
    /// Table 7 gives), not look-alikes. The proportions are read off a low-resolution photo and are approximate; GB 5768.2 gives no figure for this board, so its width is the diameter of the round
    /// signs it carries divided by the share the photo shows (0.24 of the width). The round signs are those of the 40 to 70 km/h band (0.8 m) or of the under 40 km/h band (0.6 m), never the larger
    /// ones: a board of 1.0 or 1.2 m signs would be 4.2 or 5 m wide and could not hang one over each lane of 3.5 to 3.75 m (the lane width is recalled, not checked).
    /// </summary>
    public static class TypeSpeedSigns
    {
        public const string TemplateId = "gb.guide.typespeed";

        private const string G = "guide";
        private const GuideColour Green = GuideColour.Green, White = GuideColour.White;

        /// <summary>The board is 1.12 times as wide as high; a round sign is 0.24 of its width; the round signs stand 0.07 of it from the sides and from the bottom (the photo).</summary>
        private const float Aspect = 1.12f, RoundK = 0.24f, SideK = 0.07f;

        /// <summary>Liner, white border and corner radius, in widths of the board.</summary>
        private const float LinerK = 0.006f, BorderK = 0.012f, RadiusK = 0.05f;

        /// <summary>The characters of a short name are 0.26 wide in a cell, three of them 0.12 cell apart; a short name is spaced out to the width of three. The gap between longer ones is 0.1 cell.</summary>
        private const float NameK = 0.26f, ThreeGapK = 0.12f, LongGapK = 0.1f;

        /// <summary>From the inside of the edge to the name; the free margin left and right of it; the ink height of the English line and its distance from the name; the arrow's square.</summary>
        private const float TopK = 0.05f, SideRoomK = 0.05f, EnglishK = 0.085f, EnglishGapK = 0.03f, ArrowK = 0.20f;

        /// <summary>
        /// The painter puts the lowest ink of a line on the bottom of its box, so a line with a g or a y would stand higher than one without. This is how far the bundled traffic sign font's descender
        /// hangs below the baseline, in ink heights (measured on the painted boards: 0.356; another font, the player's own, will differ by a few pixels), so that the baselines of two boards agree.
        /// </summary>
        private const float DescenderK = 0.356f;

        private sealed class VehicleType
        {
            public readonly string Value, Zh, En;

            public VehicleType(string value, string zh, string en)
            {
                Value = value;
                Zh = zh;
                En = en;
            }
        }

        private static readonly VehicleType[] kTypes =
        {
            new VehicleType("car", "小客车", "Commuter car"),
            new VehicleType("coach", "客车", "Coach"),
            new VehicleType("cartruck", "客货车", "Carriage truck"),
            new VehicleType("truck", "货车", "Truck"),
            new VehicleType("bigcoach", "大型客车", "Large coach"),
            new VehicleType("hazmat", "危险品运输车", "Hazmat vehicle"),
            new VehicleType("all", "全部车辆", "All vehicles")
        };

        private const string Custom = "custom";

        private static GuideChoice[] TypeChoices()
        {
            var list = new List<GuideChoice>();
            foreach (VehicleType t in kTypes)
                list.Add(new GuideChoice(t.Value, t.Zh, t.En));
            list.Add(new GuideChoice(Custom, "自定义", "Custom"));
            return list.ToArray();
        }

        public struct Box
        {
            public float X, Y, W, H;

            public bool Empty => W <= 0f || H <= 0f;

            public float Right => X + W;

            public float Bottom => Y + H;
        }

        /// <summary>Where everything stands on the board, from its top left corner (y down), and what is written; an absent part has an empty box.</summary>
        public sealed class Plan
        {
            public float W, H, Edge, Round;
            public Box Name, English, Max, Min, Arrow;
            public string NameText = "", EnglishText = "", MaxText = "", MinText = "";

            /// <summary>The cell of one character of the name and the space between two cells.</summary>
            public float Cell, NameGap;
        }

        public static IEnumerable<GuideTemplate> All()
        {
            yield return StandardSigns.Make(TemplateId, "分车型限速", "分车型限速标志", "Speed limits by vehicle type", G, "fcxxsbz",
                StandardSigns.Words("小客车", "客车", "客货车", "货车", "分车型", "车型", "限速", "最高限速", "最低限速", "速度", "门架", "危险品", "大型客车", "Commuter car", "Carriage truck", "coach", "truck", "hazmat", "vehicle type", "speed limit", "minimum speed", "speed by vehicle"),
                Layout,
                new[]
                {
                    GuideField.Choice("type", "车型", "Vehicle type", "car", TypeChoices()),
                    GuideField.Text("name", "车型名", "Name", "", 8, s => s.Is("type", Custom)),
                    GuideField.Text("nameEn", "英文名（可空）", "English name (may be empty)", "", 24, s => s.Is("type", Custom)),
                    GuideField.Digits("max", "最高限速（km/h）", "Maximum (km/h)", "120", 3),
                    GuideField.Digits("min", "最低限速（km/h，0=无）", "Minimum (km/h, 0 = none)", "110", 3),
                    GuideField.Flag("english", "显示英文", "English line", true),
                    GuideField.Flag("arrow", "中间箭头", "Arrow between", true, Both)
                });
        }

        private static bool Both(GuideSpec s) => SpeedText(s.Get("max")).Length > 0 && SpeedText(s.Get("min")).Length > 0;

        /// <summary>The band of the round signs on the board: the road's, but not above the 40 to 70 km/h one (see the class).</summary>
        private static int DiscBand(int band) => Math.Max(SignStyles.Band(band), SignStyles.DefaultBand);

        /// <summary>A limit as it is written (no leading zeros, a decimal as typed); empty for no limit: nothing typed, zero, or something that is no number.</summary>
        private static string SpeedText(string text)
        {
            return double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double v) && v > 0.0 ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";
        }

        private static bool HasDescender(string text)
        {
            foreach (char c in text)
            {
                if (c == 'g' || c == 'j' || c == 'p' || c == 'q' || c == 'y')
                    return true;
            }

            return false;
        }

        public static Plan PlanOf(GuideSpec s, int band)
        {
            var p = new Plan { Round = SignStyles.CircleDiameter(DiscBand(band)) };
            p.W = p.Round / RoundK;
            p.H = p.W / Aspect;
            p.Edge = (LinerK + BorderK) * p.W;
            p.MaxText = SpeedText(s.Get("max"));
            p.MinText = SpeedText(s.Get("min"));

            // The row of round signs: the maximum at the left, the minimum at the right, the arrow between; one sign alone is in the middle.
            float side = SideK * p.W, rowY = p.H - side - p.Round;
            bool both = Both(s);
            float centre = (p.W - p.Round) * 0.5f;
            if (p.MaxText.Length > 0)
                p.Max = new Box { X = both ? side : centre, Y = rowY, W = p.Round, H = p.Round };
            if (p.MinText.Length > 0)
                p.Min = new Box { X = both ? p.W - side - p.Round : centre, Y = rowY, W = p.Round, H = p.Round };
            if (both && s.Flag("arrow"))
            {
                float a = ArrowK * p.W;
                p.Arrow = new Box { X = (p.W - a) * 0.5f, Y = rowY + (p.Round - a) * 0.5f, W = a, H = a };
            }

            if (s.Is("type", Custom))
            {
                p.NameText = s.Get("name").Trim();
                p.EnglishText = s.Get("nameEn").Trim();
            }
            else
            {
                foreach (VehicleType t in kTypes)
                {
                    if (t.Value == s.Get("type"))
                    {
                        p.NameText = t.Zh;
                        p.EnglishText = t.En;
                    }
                }
            }

            if (!s.Flag("english"))
                p.EnglishText = "";

            PlaceText(p, rowY);
            return p;
        }

        /// <summary>The name and the English line above the row: both hang from the top; one alone is centred over the free height.</summary>
        private static void PlaceText(Plan p, float rowY)
        {
            float room = p.W - 2f * (p.Edge + SideRoomK * p.W);
            int n = p.NameText.Length;
            float cell = 0f, gap = 0f, total = 0f;
            if (n > 0)
            {
                cell = NameK * p.W;
                gap = n == 1 ? 0f : n <= 3 ? (3f * cell + 2f * ThreeGapK * cell - n * cell) / (n - 1) : LongGapK * cell;
                total = n * cell + (n - 1) * gap;
                if (total > room)
                {
                    float k = room / total;
                    cell *= k;
                    gap *= k;
                    total = room;
                }
            }

            float eh = 0f;
            if (p.EnglishText.Length > 0)
            {
                eh = EnglishK * p.W;
                float need = GuideKit.LatinWidth(p.EnglishText, eh);
                if (need > room)
                    eh *= room / need;
            }

            float egap = n > 0 && eh > 0f ? EnglishGapK * p.W : 0f, block = cell + egap + eh;
            float top = n > 0 && eh > 0f ? p.Edge + TopK * p.W : p.Edge + (rowY - p.Edge - block) * 0.5f;
            p.Cell = cell;
            p.NameGap = gap;
            if (n > 0)
                p.Name = new Box { X = (p.W - total) * 0.5f, Y = top, W = total, H = cell };
            if (eh > 0f)
            {
                float y = top + cell + egap;
                p.English = new Box { X = (p.W - room) * 0.5f, Y = HasDescender(p.EnglishText) ? y + DescenderK * eh : y, W = room, H = eh };
            }
        }

        private static GuideBoard Layout(GuideSpec s, int band)
        {
            Plan p = PlanOf(s, band);
            var part = new GuidePart { W = p.W, H = p.H, Ground = Green, BorderColour = White, Liner = LinerK * p.W, Border = BorderK * p.W, Radius = RadiusK * p.W };
            RoadGuideSigns.SetSafe(part);

            string discSpeed = DiscBand(band) > SignStyles.DefaultBand ? "30" : "60";
            PutDisc(part, "gb.prohib.39", p.MaxText, discSpeed, p.Max);
            PutDisc(part, "gb.mand.14", p.MinText, discSpeed, p.Min);
            if (!p.Arrow.Empty)
                part.Elements.Add(GuideKit.Poly(DownArrow(p.Arrow), White));
            if (!p.Name.Empty)
                part.Elements.Add(GuideKit.Cells(p.NameText, p.Name.X, p.Name.Y, p.Cell, White, p.NameGap));
            if (!p.English.Empty)
                part.Elements.Add(GuideKit.Ink(p.EnglishText, p.English.X, p.English.Y, p.English.W, p.English.H, White, GuideAlign.Centre, "C"));

            return RoadGuideSigns.Single(part, p.Cell > 0f ? p.Cell : NameK * p.W);
        }

        /// <summary>
        /// The arrow down in its box: a stem 0.36 of the width and a triangular head over the whole width from half the height. The framework's ArrowPolygon (Fig. 204) has a stem 0.22 wide, thinner than
        /// the thick arrow of the photo, so it is not used here.
        /// </summary>
        public static float[] DownArrow(Box b)
        {
            float cx = b.X + b.W * 0.5f, stem = b.W * 0.18f, neck = b.Y + b.H * 0.5f;
            return new[]
            {
                cx - stem, b.Y,
                cx + stem, b.Y,
                cx + stem, neck,
                b.Right, neck,
                cx, b.Bottom,
                b.X, neck,
                cx - stem, neck
            };
        }

        /// <summary>The standard sign of the catalogue on the board: its white ground and its elements, moved to the box: as large as that sign alone is in the band.</summary>
        private static void PutDisc(GuidePart part, string id, string value, string speed, Box at)
        {
            if (at.Empty)
                return;
            GuideTemplate t = GuideTemplates.ById(id);
            GuideBoard disc = t.Layout(t.Default().With("limit", value).With("speed", speed));
            part.Elements.Add(GuideKit.Oval(at.X, at.Y, at.W, at.H, White));
            foreach (GuideElement e in disc.Main.Elements)
                part.Elements.Add(e.Moved(at.X, at.Y));
        }
    }
}
