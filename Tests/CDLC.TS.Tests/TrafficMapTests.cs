using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Tests
{
    /// <summary>The traffic-state screen: the congestion levels, the layout of a network on the screen (the way up, the sign's mark, the lines, the names), the live networks and the template.</summary>
    internal static class TrafficMapTests
    {
        public static void RunAll()
        {
            Program.Run("traffic map: the speed ratio of a flow offset and the levels", Levels);
            Program.Run("traffic map: a level keeps its colour until the ratio is a margin past the limit", Margins);
            Program.Run("traffic map: the compass word of the way the traffic goes", Compass);
            Program.Run("traffic map: the way the traffic goes is up, the sign's mark is at the foot", Orientation);
            Program.Run("traffic map: lines fit the screen, the worse level is on top", LinesAndOrder);
            Program.Run("traffic map: names beside their roads, once each, never over a line or another name", Names);
            Program.Run("traffic map: a network that is empty, unknown, huge or odd", Odd);
            Program.Run("traffic map: the live networks and the template", Template);
        }

        private static TmOptions Options(float w = 3.2f, float h = 2.4f, bool names = true, bool dir = true) =>
            new TmOptions { Width = w, Height = h, CharHeight = 0.085f * h, Names = names, Direction = dir };

        private static TmEdge Edge(TrafficLevel level, string name, int road, params float[] xz) => new TmEdge { Level = level, Name = name, Road = road, Points = xz };

        private static IEnumerable<(GuideElement e, GuideColour colour)> Polygons(List<GuideElement> elements)
        {
            foreach (GuideElement e in elements)
            {
                if (e.Kind == GuideElementKind.Polygon)
                    yield return (e, e.Colour);
            }
        }

        private static void Levels()
        {
            // The game's flow offset: 0 is free, 255 a jam; the ratio is what the offset says of the speed.
            Program.Near(TrafficMap.RatioOfOffset(0f), 1f, 1e-6f, "offset 0 is a ratio of 1");
            Program.Near(TrafficMap.RatioOfOffset(128f), 0.5f, 1e-6f, "offset 128 is 0.5");
            Program.Near(TrafficMap.RatioOfOffset(255f), 1f / 256f, 1e-6f, "offset 255 is 1/256");
            Program.Near(TrafficMap.RatioOfOffset(-10f), 1f, 1e-6f, "never above 1");
            Program.Near(TrafficMap.RatioOfOffset(400f), 0f, 1e-6f, "never below 0");

            Program.Check(TrafficMap.LevelOf(1f, 0.7f, 0.4f) == TrafficLevel.Free && TrafficMap.LevelOf(0.7f, 0.7f, 0.4f) == TrafficLevel.Free, "at the slow limit it is free");
            Program.Check(TrafficMap.LevelOf(0.69f, 0.7f, 0.4f) == TrafficLevel.Slow && TrafficMap.LevelOf(0.4f, 0.7f, 0.4f) == TrafficLevel.Slow, "between the limits it is slow (the jam limit itself is slow)");
            Program.Check(TrafficMap.LevelOf(0.39f, 0.7f, 0.4f) == TrafficLevel.Jam && TrafficMap.LevelOf(0f, 0.7f, 0.4f) == TrafficLevel.Jam, "below the jam limit it is a jam");

            Program.Check(TrafficMap.ColourOf(TrafficLevel.Free) == GuideColour.Green && TrafficMap.ColourOf(TrafficLevel.Slow) == GuideColour.Yellow && TrafficMap.ColourOf(TrafficLevel.Jam) == GuideColour.Red, "green, yellow, red (Table 10)");

            // The colours light as the LED colours the standard asks for.
            foreach ((TrafficLevel level, LedColour led) in new[] { (TrafficLevel.Free, LedColour.Green), (TrafficLevel.Slow, LedColour.Amber), (TrafficLevel.Jam, LedColour.Red) })
            {
                GuideColours.Rgb(TrafficMap.ColourOf(level), out byte r, out byte g, out byte b);
                Program.Check(Led.Classify(r / 255f, g / 255f, b / 255f) == led, level + " lights as " + led);
            }
        }

        private static void Margins()
        {
            const float slow = 0.7f, jam = 0.4f;
            // Unknown before: the plain level.
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Unknown, 0.69f, slow, jam) == TrafficLevel.Slow, "the first reading is the plain level");
            // Free stays free until the ratio is a margin below the limit.
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Free, 0.68f, slow, jam) == TrafficLevel.Free, "free: 0.68 is within the margin of 0.70, still free");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Free, 0.64f, slow, jam) == TrafficLevel.Slow, "free: 0.64 is past it, slow");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Free, 0.2f, slow, jam) == TrafficLevel.Jam, "free: a ratio of 0.2 is a jam at once");
            // Slow goes back to free only a margin above the limit, and to a jam only a margin below it.
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Slow, 0.72f, slow, jam) == TrafficLevel.Slow, "slow: 0.72 is within the margin, still slow");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Slow, 0.75f, slow, jam) == TrafficLevel.Free, "slow: 0.75 is free");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Slow, 0.38f, slow, jam) == TrafficLevel.Slow, "slow: 0.38 is within the margin of the jam limit, still slow");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Slow, 0.33f, slow, jam) == TrafficLevel.Jam, "slow: 0.33 is a jam");
            // A jam stays a jam until the ratio is a margin above its limit.
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Jam, 0.42f, slow, jam) == TrafficLevel.Jam, "jam: 0.42 is within the margin, still a jam");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Jam, 0.46f, slow, jam) == TrafficLevel.Slow, "jam: 0.46 is slow");
            Program.Check(TrafficMap.LevelWithMargin(TrafficLevel.Jam, 0.95f, slow, jam) == TrafficLevel.Free, "jam: 0.95 is free at once");

            // Flicker: a ratio that wobbles round the limit does not change the colour.
            TrafficLevel level = TrafficLevel.Free;
            int changes = 0;
            foreach (float ratio in new[] { 0.71f, 0.69f, 0.70f, 0.68f, 0.72f, 0.69f, 0.67f, 0.71f })
            {
                TrafficLevel next = TrafficMap.LevelWithMargin(level, ratio, slow, jam);
                changes += next != level ? 1 : 0;
                level = next;
            }

            Program.Check(changes == 0 && level == TrafficLevel.Free, "a ratio wobbling round the limit changes nothing: " + changes);
        }

        private static void Compass()
        {
            Program.Check(TrafficMap.CompassOf(0f, 1f) == "北" && TrafficMap.CompassOf(1f, 0f) == "东" && TrafficMap.CompassOf(0f, -1f) == "南" && TrafficMap.CompassOf(-1f, 0f) == "西", "the four ways");
            Program.Check(TrafficMap.CompassOf(1f, 1f) == "东" || TrafficMap.CompassOf(1f, 1f) == "北", "north-east is one of the two it lies between");
            Program.Check(TrafficMap.CompassOf(0.3f, 1f) == "北" && TrafficMap.CompassOf(1f, 0.3f) == "东" && TrafficMap.CompassOf(-0.3f, -1f) == "南" && TrafficMap.CompassOf(-1f, -0.2f) == "西", "within 45 degrees of a way it is that way");
            Program.Check(TrafficMap.CompassOf(0f, 0f) == "北", "no direction: north");
        }

        private static void Orientation()
        {
            // A straight stretch going north from the sign: up the screen, the mark below it, x the same all the way.
            var net = new TmNetwork { Known = true, OriginX = 100f, OriginZ = 200f, HeadX = 0f, HeadZ = 1f };
            net.Edges.Add(Edge(TrafficLevel.Free, "", 0, 100, 200, 100, 700));
            List<GuideElement> northern = TrafficMap.Layout(net, Options(names: false, dir: false));
            var xs = new List<float>();
            float topY = float.MaxValue, bottomY = float.MinValue;
            foreach ((GuideElement e, GuideColour c) in Polygons(northern))
            {
                if (c != GuideColour.Green)
                    continue;
                topY = Math.Min(topY, e.Y);
                bottomY = Math.Max(bottomY, e.Y + e.H);
                xs.Add(e.X + e.W * 0.5f);
            }

            Program.Check(xs.Count > 0 && topY < bottomY - 0.5f, "the stretch has length on the screen: " + topY + " to " + bottomY);
            Program.Check(topY < 0.4f && bottomY > 1.6f, "going north is going up: " + topY + " to " + bottomY);
            Program.Near(xs[0], 1.6f, 0.05f, "and in the middle of the screen: " + xs[0]);

            // The same road, the traffic going east: the stretch is a line east of the sign, and the screen shows it going up all the same.
            var east = new TmNetwork { Known = true, OriginX = 0f, OriginZ = 0f, HeadX = 1f, HeadZ = 0f };
            east.Edges.Add(Edge(TrafficLevel.Free, "", 0, 0, 0, 500, 0));
            float eastTop = float.MaxValue, eastBottom = float.MinValue, eastLeft = float.MaxValue, eastRight = float.MinValue;
            foreach ((GuideElement e, GuideColour c) in Polygons(TrafficMap.Layout(east, Options(names: false, dir: false))))
            {
                if (c != GuideColour.Green)
                    continue;
                eastTop = Math.Min(eastTop, e.Y);
                eastBottom = Math.Max(eastBottom, e.Y + e.H);
                eastLeft = Math.Min(eastLeft, e.X);
                eastRight = Math.Max(eastRight, e.X + e.W);
            }

            Program.Check(eastTop < 0.4f && eastBottom > 1.6f && eastRight - eastLeft < 0.2f, "heading east, a road going east is up the screen: " + eastTop + " to " + eastBottom + ", " + (eastRight - eastLeft) + " wide");

            // A road to the traffic's right is on the right of the screen: the traffic goes north, a road goes east from the middle of the stretch.
            var cross = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            cross.Edges.Add(Edge(TrafficLevel.Free, "", 0, 0, 0, 0, 400));
            cross.Edges.Add(Edge(TrafficLevel.Jam, "", 0, 0, 200, 300, 200));
            float redLeft = float.MaxValue, redRight = float.MinValue, greenMid = 0f;
            foreach ((GuideElement e, GuideColour c) in Polygons(TrafficMap.Layout(cross, Options(names: false, dir: false))))
            {
                if (c == GuideColour.Red && e.Points.Length == 8 && e.W > e.H)
                {
                    redLeft = Math.Min(redLeft, e.X);
                    redRight = Math.Max(redRight, e.X + e.W);
                }
                else if (c == GuideColour.Green)
                {
                    greenMid = e.X + e.W * 0.5f;
                }
            }

            Program.Check(redLeft >= greenMid - 0.05f && redRight > greenMid + 0.5f, "a road to the right of the traffic is on the right of the screen: " + redLeft + " to " + redRight + " from " + greenMid);

            // The sign's mark: a red triangle pointing up at the foot, under the end of the stretch, not above the middle of the screen.
            GuideElement mark = null;
            foreach ((GuideElement e, GuideColour c) in Polygons(northern))
            {
                if (c == GuideColour.Red && e.Points != null && e.Points.Length == 6)
                    mark = e;
            }

            Program.Check(mark != null, "the mark is there");
            if (mark != null)
            {
                Program.Check(mark.Y + mark.H * 0.5f > 2.0f, "at the foot: " + (mark.Y + mark.H * 0.5f));
                Program.Near(mark.X + mark.W * 0.5f, 1.6f, 0.05f, "under the stretch: " + (mark.X + mark.W * 0.5f));
                Program.Check(mark.Points[1] < mark.Points[3] && mark.Points[1] < mark.Points[5], "its apex is up");
            }
        }

        private static void LinesAndOrder()
        {
            TmNetwork net = TrafficMap.Demo();
            TmOptions options = Options();
            List<GuideElement> elements = TrafficMap.Layout(net, options);

            // Everything is inside the screen; the roads inside its margins.
            foreach (GuideElement e in elements)
            {
                Program.Check(e.X >= -1e-3f && e.Y >= -1e-3f && e.X + e.W <= options.Width + 1e-3f && e.Y + e.H <= options.Height + 1e-3f, "an element is on the screen: " + e.Kind + " " + e.X + "," + e.Y + " " + e.W + "x" + e.H);
            }

            // The three colours are used by the demonstration network, and no other colour is lit but the names' white and the mark's red.
            var seen = new HashSet<GuideColour>();
            foreach (GuideElement e in elements)
                seen.Add(e.Colour);
            Program.Check(seen.Contains(GuideColour.Green) && seen.Contains(GuideColour.Yellow) && seen.Contains(GuideColour.Red) && seen.Contains(GuideColour.White), "green, yellow, red and white are all drawn");
            Program.Check(seen.Count == 4, "and nothing else: " + string.Join(",", seen));

            // The worse is on top: no green element after a yellow one, no yellow after a red one (the red mark and the white names come after the lines, so look at the lines only).
            int lastGreen = -1, firstYellow = int.MaxValue, lastYellow = -1, firstRed = int.MaxValue;
            for (int i = 0; i < elements.Count; i++)
            {
                GuideElement e = elements[i];
                if (e.Kind != GuideElementKind.Polygon && e.Kind != GuideElementKind.Oval)
                    continue;
                if (e.Kind == GuideElementKind.Polygon && e.Points != null && e.Points.Length == 6)
                    continue;
                if (e.Colour == GuideColour.Green) lastGreen = i;
                else if (e.Colour == GuideColour.Yellow) { firstYellow = Math.Min(firstYellow, i); lastYellow = i; }
                else if (e.Colour == GuideColour.Red) firstRed = Math.Min(firstRed, i);
            }

            Program.Check(lastGreen < firstYellow && lastYellow < firstRed, $"green, then yellow, then red: {lastGreen} < {firstYellow}, {lastYellow} < {firstRed}");

            // A road in one colour is drawn once in it: the number of red line elements follows the number of jammed stretches (2 in the demonstration: east of the first crossing, the road beside).
            int redQuads = 0;
            foreach ((GuideElement e, GuideColour c) in Polygons(elements))
                redQuads += c == GuideColour.Red && e.Points.Length == 8 ? 1 : 0;
            Program.Check(redQuads == 2, "two jammed stretches are two red quads: " + redQuads);

            // The scale does not change the picture's proportions: a screen twice as large draws the same picture twice as large.
            List<GuideElement> big = TrafficMap.Layout(net, Options(6.4f, 4.8f));
            Program.Check(big.Count == elements.Count, "the same number of elements at twice the size: " + big.Count + " and " + elements.Count);
            float widest = 0f, widestBig = 0f;
            foreach ((GuideElement e, GuideColour c) in Polygons(elements))
                widest = Math.Max(widest, e.X + e.W);
            foreach ((GuideElement e, GuideColour c) in Polygons(big))
                widestBig = Math.Max(widestBig, e.X + e.W);
            Program.Near(widestBig, widest * 2f, 0.02f, "twice the size, twice the width: " + widest + " and " + widestBig);
        }

        private static void Names()
        {
            TmNetwork net = TrafficMap.Demo();
            TmOptions options = Options();
            List<GuideElement> withNames = TrafficMap.Layout(net, options);
            options.Names = false;
            List<GuideElement> without = TrafficMap.Layout(net, options);

            var texts = new List<GuideElement>();
            foreach (GuideElement e in withNames)
            {
                if (e.Kind == GuideElementKind.Text && e.Mode == GuideTextMode.Cell && e.Text != "北")
                    texts.Add(e);
            }

            var texts2 = new List<string>();
            foreach (GuideElement e in without)
            {
                if (e.Kind == GuideElementKind.Text && e.Text != "北")
                    texts2.Add(e.Text);
            }

            Program.Check(texts2.Count == 0, "without names there is no text but the direction: " + string.Join(",", texts2));
            Program.Check(texts.Count >= 4, "with names the roads are named: " + texts.Count);

            // Once each: no name twice.
            var names = new HashSet<string>();
            foreach (GuideElement e in texts)
                Program.Check(names.Add(e.Text), "the name " + e.Text + " is there once");
            Program.Check(names.Contains("东海西路") && names.Contains("延安三路") && names.Contains("山东路"), "the long roads are named: " + string.Join(",", names));

            // No name over another, none over a line (the lines of the layout: the quads of the stretches).
            var quads = new List<GuideElement>();
            foreach ((GuideElement e, GuideColour c) in Polygons(withNames))
            {
                if (e.Points.Length == 8)
                    quads.Add(e);
            }

            for (int i = 0; i < texts.Count; i++)
            {
                GuideElement a = texts[i];
                for (int j = i + 1; j < texts.Count; j++)
                {
                    GuideElement b = texts[j];
                    bool overlap = a.X < b.X + b.W && a.X + a.W > b.X && a.Y < b.Y + b.H && a.Y + a.H > b.Y;
                    Program.Check(!overlap, "the names " + a.Text + " and " + b.Text + " do not overlap");
                }

                foreach (GuideElement q in quads)
                {
                    // The quad's own box can be larger than the line for a slanted one; the demonstration's lines are straight and horizontal or vertical, so the box is the line.
                    bool over = a.X < q.X + q.W && a.X + a.W > q.X && a.Y < q.Y + q.H && a.Y + a.H > q.Y;
                    Program.Check(!over, "the name " + a.Text + " is clear of the lines");
                }

                Program.Check(a.H >= 0.8f * options.CharHeight - 1e-4f, "the characters are at least four fifths of the size: " + a.H);
            }

            // A road without a name is not labelled; a name that is only spaces is none; the same road in two stretches is named once.
            var plain = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            plain.Edges.Add(Edge(TrafficLevel.Free, "", 1, 0, 0, 0, 500));
            plain.Edges.Add(Edge(TrafficLevel.Free, "   ", 2, 0, 500, 0, 900));
            plain.Edges.Add(Edge(TrafficLevel.Free, "中山路", 3, 0, 500, -400, 500));
            plain.Edges.Add(Edge(TrafficLevel.Slow, "中山路", 3, -400, 500, -800, 500));
            int labels = 0;
            foreach (GuideElement e in TrafficMap.Layout(plain, Options()))
                labels += e.Kind == GuideElementKind.Text && e.Text != "北" ? 1 : 0;
            Program.Check(labels == 1, "one name for a road of two stretches, none for the unnamed: " + labels);

            // A name that cannot be placed anywhere is left out and the rest still is: a road across the whole screen with no room beside it.
            var crowded = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            for (int i = 0; i < 12; i++)
                crowded.Edges.Add(Edge(TrafficLevel.Free, "很长很长的路名" + i, 100 + i, -400, i * 70f, 400, i * 70f));
            List<GuideElement> crowdedElements = TrafficMap.Layout(crowded, Options());
            int crowdedLabels = 0;
            foreach (GuideElement e in crowdedElements)
                crowdedLabels += e.Kind == GuideElementKind.Text && e.Text != "北" ? 1 : 0;
            Program.Check(crowdedLabels < 12, "twelve parallel roads cannot all be named in the room there is: " + crowdedLabels);
        }

        private static void Odd()
        {
            // Nothing: the mark and the direction only.
            List<GuideElement> none = TrafficMap.Layout(new TmNetwork(), Options());
            Program.Check(none.Count >= 2, "an unknown network is the mark and the direction at least: " + none.Count);
            int quads = 0;
            foreach ((GuideElement e, GuideColour c) in Polygons(none))
                quads += e.Points.Length == 8 ? 1 : 0;
            Program.Check(quads == 0, "and no road");
            Program.Check(TrafficMap.Layout(null, Options()).Count >= 2, "a null network is drawn the same way");

            var empty = new TmNetwork { Known = true };
            Program.Check(TrafficMap.Layout(empty, Options(dir: false)).Count == 1, "a known network without roads is the mark alone");

            // Edges with nothing to draw are left out: fewer than two points, an unknown level.
            var odd = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            odd.Edges.Add(Edge(TrafficLevel.Free, "", 0, 0, 0));
            odd.Edges.Add(Edge(TrafficLevel.Unknown, "", 0, 0, 0, 0, 100));
            odd.Edges.Add(new TmEdge { Level = TrafficLevel.Free, Points = null });
            Program.Check(TrafficMap.Layout(odd, Options(names: false, dir: false)).Count == 1, "a stretch with one point, one of unknown level and one without points are not drawn");

            // A heading that is not a unit vector, or is zero, is still a way.
            var skew = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 5f };
            skew.Edges.Add(Edge(TrafficLevel.Jam, "", 0, 0, 0, 0, 300));
            var zero = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 0f };
            zero.Edges.Add(Edge(TrafficLevel.Jam, "", 0, 0, 0, 0, 300));
            Program.Check(TrafficMap.Layout(skew, Options()).Count == TrafficMap.Layout(zero, Options()).Count, "a heading of length 5, and one of zero, give a picture");

            // A huge network (20 km) still fits; so does a tiny one (5 m), which is not blown up beyond 200 m across.
            var huge = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            huge.Edges.Add(Edge(TrafficLevel.Free, "", 0, 0, 0, 0, 20000));
            huge.Edges.Add(Edge(TrafficLevel.Slow, "", 0, 0, 10000, -15000, 10000));
            TmOptions o = Options();
            foreach (GuideElement e in TrafficMap.Layout(huge, o))
                Program.Check(e.X >= -1e-3f && e.X + e.W <= o.Width + 1e-3f && e.Y >= -1e-3f && e.Y + e.H <= o.Height + 1e-3f, "a huge network is inside the screen");
            var tiny = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            tiny.Edges.Add(Edge(TrafficLevel.Free, "", 0, 0, 0, 0, 5));
            float tinyHeight = 0f;
            foreach ((GuideElement e, GuideColour c) in Polygons(TrafficMap.Layout(tiny, Options(dir: false))))
                tinyHeight = c == GuideColour.Green && e.Points.Length == 8 ? Math.Max(tinyHeight, e.H) : tinyHeight;
            Program.Check(tinyHeight < 0.2f, "a stretch of 5 m is small, not blown up to fill the screen: " + tinyHeight);

            // A curve (many points) is drawn as one piece with round bends; a thousand edges do not throw.
            var curve = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            var points = new List<float>();
            for (int i = 0; i <= 20; i++)
            {
                double t = i / 20.0 * Math.PI * 0.5;
                points.Add((float)(Math.Sin(t) * 300.0));
                points.Add((float)((1.0 - Math.Cos(t)) * 300.0));
            }

            curve.Edges.Add(new TmEdge { Level = TrafficLevel.Slow, Points = points.ToArray() });
            int pieces = 0, discs = 0;
            foreach (GuideElement e in TrafficMap.Layout(curve, Options(dir: false)))
            {
                pieces += e.Kind == GuideElementKind.Polygon && e.Points.Length == 8 ? 1 : 0;
                discs += e.Kind == GuideElementKind.Oval ? 1 : 0;
            }

            Program.Check(pieces == 20 && discs >= 2, "a curve of 21 points is 20 quads with round ends: " + pieces + " quads, " + discs + " discs");

            var many = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            var rng = new Random(5);
            for (int i = 0; i < 1000; i++)
                many.Edges.Add(Edge((TrafficLevel)rng.Next(1, 4), i % 3 == 0 ? "路" + i : "", i % 50, rng.Next(-2000, 2000), rng.Next(0, 3000), rng.Next(-2000, 2000), rng.Next(0, 3000)));
            Program.Check(TrafficMap.Layout(many, Options()).Count > 1000, "a thousand edges are laid out");
        }

        private static void Template()
        {
            SignChoice choice = SignCatalog.ChoiceById(TrafficMap.TemplateId);
            int boards = 0;
            foreach (SignChoice each in SignCatalog.Choices)
                boards += each.Def.IsMarking ? 0 : 1;
            int placeNames = 0, brt = 0, boxes = 0, tolls = 0, custom = 0, lampAds = 0, ways = 0, lanePick = 0, urban = 0;
            foreach (SignChoice each in SignCatalog.Choices)
            {
                placeNames += each.Group == CDLC.TS.Geo.Signs.StandardSigns.PlaceName ? 1 : 0;
                brt += each.Group == CDLC.TS.Geo.Signs.BrtSigns.Group ? 1 : 0;
                boxes += each.Group == CDLC.TS.Geo.Signs.Cabinets.Group ? 1 : 0;
                tolls += each.Group == CDLC.TS.Geo.Signs.TollSigns.Group ? 1 : 0;
                custom += each.Group == CDLC.TS.Geo.Signs.CustomSigns.Group ? 1 : 0;
                lampAds += each.Group == CDLC.TS.Geo.Signs.LampAds.Group ? 1 : 0;
                ways += each.Group == CDLC.TS.Geo.Signs.WayfindingSigns.Group ? 1 : 0;
                lanePick += each.Id == CDLC.TS.Geo.Signs.LanePickSigns.Id ? 1 : 0;
                urban += each.Code == CDLC.TS.Geo.Signs.MandatorySigns.UrbanCode ? 1 : 0;
            }

            Console.WriteLine($"  catalogue: {SignCatalog.Choices.Count} choices, {boards - placeNames - brt - boxes - tolls - lampAds - custom} GB 5768.2 signs, {placeNames} place names, {brt} BRT boards, {boxes} electric boxes, {tolls} toll plaza boards, {lampAds} lamp-post ads, {SignCatalog.Choices.Count - boards} markings, barriers and tools");
            // The BRT series' own boards are the common practice, not the standard's (0.12.22), and the electric boxes are not signs (0.12.23): counted apart.
            Program.Check(brt == 4, "and 4 boards of the BRT series that are not in the standard: " + brt);
            Program.Check(boxes == 9, "and 9 kinds of electric box: " + boxes);
            // The toll plaza's own boards (0.12.26): the name, the lane, the signal and the notice, and the notice's six presets.
            Program.Check(tolls == 4 + 6, "and 4 toll plaza boards with 6 presets of the notice, not in the standard: " + tolls);
            // The bare custom sign (0.12.29: not listed, what an imported picture starts from) and the player's saved ones are not of the standard either.
            // The text light box (0.12.75, the same tab) is one more.
            Program.Check(custom == 1 + CDLC.TS.Geo.Signs.CustomSigns.Saved.Count + (CDLC.TS.Geo.Signs.TextBoxes.Enabled ? 1 : 0) + (CDLC.TS.Geo.Signs.StandBoxes.Enabled ? 1 : 0), "and the custom sign with what the player saved: " + custom);
            // The lamp-post ads and decorations (0.12.49) are not signs either.
            Program.Check(lampAds == 46, "and 46 lamp-post ads and decorations: " + lampAds);
            // The pedestrian and metro boards (0.12.57) are this mod's, from local standards.
            Program.Check(ways == 2, "and 2 pedestrian and metro boards, not in the standard: " + ways);
            // The cities' lane choice board (0.12.78, 非国标) sits among the guide boards.
            Program.Check(lanePick == 1, "and the lane choice board of the cities, not in the standard: " + lanePick);
            // The urban signs of 0.12.78 (the blue curve sign, the interchange signs and the green distance plate) are from photographs, not in the 2022 standard.
            Program.Check(urban == 3, "and 3 urban signs from photographs, not in the standard: " + urban);
            boards -= brt + boxes + tolls + custom + lampAds + ways + lanePick + urban;
            // The speed limits by vehicle type (gb.guide.typespeed) stand in the guide group but are not a sign of GB 5768.2: one board, counted apart (the standard's 273 did not change).
            int typeSpeed = SignCatalog.ChoiceById(CDLC.TS.Geo.Signs.TypeSpeedSigns.TemplateId) != null ? 1 : 0;
            Program.Check(typeSpeed == 1, "and the speed-by-vehicle-type board, not in the standard: " + typeSpeed);
            boards -= typeSpeed;
            // The junctions-ahead board (gb.guide8.01c, 0.12.78) is one more, not in the standard: counted apart as well.
            int junctionsAhead = SignCatalog.ById(CDLC.TS.Geo.Signs.RoadGuideSigns.MultiId) != null ? 1 : 0;
            Program.Check(junctionsAhead == 1, "and the junctions-ahead board, not in the standard: " + junctionsAhead);
            boards -= junctionsAhead;
            Program.Check(boards - placeNames == 273, "the catalogue has 273 signs of GB 5768.2 (README and CHANGELOG say so; the road markings are listed with them under their own group): " + (boards - placeNames));
            Program.Check(placeNames == 16, "and 16 place-name signs of GB 17733 (README says so): " + placeNames);
            Program.Check(choice != null && choice.Group == "variable" && choice.Code == "4.11" && choice.Def.Template != null && choice.Def.Template.Live, "the traffic-state screen is in the catalogue, in the variable group, and is live");
            SignInstance demo = SignInstances.Of(choice.Def, choice.Spec);
            Program.Check(demo.IsLed && demo.Def.Width == 4.0f && demo.Def.Height == 3.0f, "an LED screen of 4.0 x 3.0 m by default (a map to be read from afar; 0.5.4 had 3.2 x 2.4)");
            Program.Check(SupportPlanner.Auto(demo.Def).Kind == SupportKind.Cantilever, "it stands on an arm at the road's side; the other screens on a gantry");
            Program.Check(SupportPlanner.Auto(SignInstances.Of(SignCatalog.ChoiceById("gb.vms.lane").Def, null).Def).Kind == SupportKind.Gantry, "a lane screen on a gantry");

            foreach ((string size, float w, float h) in new[] { ("s", 3.2f, 2.4f), ("m", 4.0f, 3.0f), ("l", 5.0f, 3.75f) })
            {
                GuideSpec spec = choice.Def.Template.Default().With("size", size);
                SignInstance instance = SignInstances.Of(choice.Def, spec.ToString());
                Program.Check(Math.Abs(instance.Def.Width - w) < 1e-4f && Math.Abs(instance.Def.Height - h) < 1e-4f, "size " + size + ": " + instance.Def.Width + " x " + instance.Def.Height);
            }

            // The dots: 480 across whatever the size, 360 down, so about 30 dots to a character on all three (0.5.4 had 16, at the text screens' pitch, and was blurred), and the screen is let have that many.
            foreach (string size in new[] { "s", "m", "l" })
            {
                SignInstance sized = SignInstances.Of(choice.Def, choice.Def.Template.Default().With("size", size).ToString());
                Program.Check(sized.IsLed && sized.Board.LedMaxDots >= 480 * 360, size + ": the screen may have 480 x 360 dots: " + sized.Board.LedMaxDots);
                Led.Dimensions(sized.Board, sized.Def.Width, sized.Def.Height, out int dotsX, out int dotsY);
                Program.Check(dotsX == VariableSigns.TrafficDotsAcross && dotsY == 360, size + ": " + dotsX + " x " + dotsY + " dots, not coarsened by the limit of the other screens");
                SignInstance textScreen = SignInstances.Of(SignCatalog.ChoiceById("gb.vms.text").Def, null);
                Led.Dimensions(textScreen.Board, textScreen.Def.Width, textScreen.Def.Height, out int textX, out int textY);
                Program.Check(textX * textY <= Led.MaxDots && textX * textY > 10000, "the other screens keep the common limit of " + Led.MaxDots + " dots: " + textX + " x " + textY);
                float perCharacter = sized.Board.H / (sized.Def.Height / dotsY);
                Program.Check(perCharacter >= 28f && perCharacter <= 33f, size + ": dots to the character: " + perCharacter);
                Led.Dimensions(sized.Def.Width, sized.Def.Height, sized.Board.LedPitch, out int cappedX, out _);
                Program.Check(cappedX < dotsX, size + ": with the limit of the other screens it would have been coarsened (the test sees the cap)");
            }

            // The settings: names and direction are flags, the limits digits, the item and the revision are hidden.
            GuideTemplate template = choice.Def.Template;
            Program.Check(template.FieldOf("names").Kind == GuideFieldKind.Flag && template.FieldOf("dir").Kind == GuideFieldKind.Flag && template.FieldOf("slow").Kind == GuideFieldKind.Text, "fields");
            GuideSpec defaults = template.Default();
            Program.Check(!template.FieldOf("id").Visible(defaults) && !template.FieldOf("rev").Visible(defaults) && template.FieldOf("size").Visible == null, "the item and the revision are not shown");
            Program.Check(defaults.Get("slow") == "70" && defaults.Get("jam") == "40" && defaults.Get("jn") == "3" && defaults.Get("range") == "800", "the defaults: 70, 40, 3 junctions, 800 m");

            // The screen without an item shows the demonstration; with a live network it shows that, and a revision is another instance.
            int id = 987654;
            try
            {
                var net = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
                net.Edges.Add(Edge(TrafficLevel.Jam, "", 1, 0, 0, 0, 500));
                TrafficMap.Set(id, net, 1);
                Program.Check(TrafficMap.RevisionOf(id) == 1 && ReferenceEquals(TrafficMap.Get(id), net), "published");
                int changes = TrafficMap.Changes;
                TrafficMap.Set(id, net, 1);
                Program.Check(TrafficMap.Changes == changes, "the same revision again changes nothing");

                string live = defaults.With("id", id.ToString()).With("rev", "1").ToString();
                SignInstance one = SignInstances.Of(choice.Def, live);
                int redLines = 0, greenLines = 0;
                foreach (GuideElement e in one.Board.Main.Elements)
                {
                    redLines += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Red && e.Points.Length == 8 ? 1 : 0;
                    greenLines += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Green && e.Points.Length == 8 ? 1 : 0;
                }

                Program.Check(redLines == 1 && greenLines == 0, "the live screen shows its own network, one jammed road: " + redLines + " red, " + greenLines + " green");
                int demoGreen = 0;
                foreach (GuideElement e in demo.Board.Main.Elements)
                    demoGreen += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Green && e.Points.Length == 8 ? 1 : 0;
                Program.Check(demoGreen > 3, "the screen without an item shows the demonstration: " + demoGreen + " green");

                // A new revision is a new instance (the cache is by the written settings).
                var better = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
                better.Edges.Add(Edge(TrafficLevel.Free, "", 1, 0, 0, 0, 500));
                TrafficMap.Set(id, better, 2);
                string next = defaults.With("id", id.ToString()).With("rev", "2").ToString();
                SignInstance two = SignInstances.Of(choice.Def, next);
                Program.Check(!ReferenceEquals(one, two) && one.Key != two.Key, "another revision is another instance");
                int green2 = 0;
                foreach (GuideElement e in two.Board.Main.Elements)
                    green2 += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Green && e.Points.Length == 8 ? 1 : 0;
                Program.Check(green2 == 1, "the second revision is green: " + green2);

                // An item with no network yet (or an id that is not a number) shows the sign's mark only, not the demonstration.
                SignInstance missing = SignInstances.Of(choice.Def, defaults.With("id", "111").With("rev", "0").ToString());
                int missingQuads = 0;
                foreach (GuideElement e in missing.Board.Main.Elements)
                    missingQuads += e.Kind == GuideElementKind.Polygon && e.Points.Length == 8 ? 1 : 0;
                Program.Check(missingQuads == 0, "an item that has not been read yet draws no road: " + missingQuads);
                SignInstance garbled = SignInstances.Of(choice.Def, defaults.With("id", "abc").ToString());
                Program.Check(garbled.IsLed, "an id that is not a number does not throw");

                // Clear removes everything and says so.
                int before = TrafficMap.Changes;
                TrafficMap.Clear();
                Program.Check(TrafficMap.Changes == before + 1 && TrafficMap.Get(id) == null && TrafficMap.RevisionOf(id) == 0, "clear");
            }
            finally
            {
                TrafficMap.Clear();
            }
        }
    }
}
