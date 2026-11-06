using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.69 (the owner, of 警46 线形诱导标: "这个应该可以线性摆放"): a sign placed along a line. The pure parts: the setting on the sign's template (on only where the form allows, kept out of what is placed),
    /// the spacing (typed, or the borrowed curve rule), the stations along the chain (the start, every spacing toward the end, none in a junction's cut, the grid going on over it), where a sign stands
    /// beside the edge and which way it faces. The reading of the game's roads, the tool's click and the preview (ECS, Unity) are not here.
    /// </summary>
    internal static class SignLineTests
    {
        public static void RunAll()
        {
            Program.Run("sign line: the setting is the sign's template's, on where the form allows, and not kept in what is placed", Setting);
            Program.Run("sign line: the spacing is typed, or the borrowed curve rule", Spacing);
            Program.Run("sign line: the stations along the road", Stations);
            Program.Run("sign line: no station in the cut of a junction, and the grid goes on after it", Junction);
            Program.Run("sign line: a sign stands beside the edge and faces the traffic that comes to it", PostSnap);
            Program.Run("sign line: the support, the side and the hints", Others);
        }

        private static SignDef Chevron() => SignCatalog.ById("gb.warn.46");

        private static List<ChainSpan> One(float from, float to) => new List<ChainSpan> { new ChainSpan(from, to) };

        private static string Show(List<float> list) => string.Join(" ", list.ConvertAll(s => s.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)));

        private static void Setting()
        {
            SignDef def = Chevron();
            GuideTemplate template = def.Template;
            string plain = template.Default().ToString();
            Program.Check(template.Linear && template.FieldOf(SignLine.LineKey) != null && template.FieldOf(SignLine.GapKey) != null, "警46 takes the line settings");
            int line = Array.FindIndex(template.Fields, f => f.Key == SignLine.LineKey), gap = Array.FindIndex(template.Fields, f => f.Key == SignLine.GapKey), speed = Array.FindIndex(template.Fields, f => f.Key == "speed");
            Program.Check(line > 0 && gap == line + 1 && speed == gap + 1 && template.Fields.Skip(speed + 1).All(f => f.Key == SignLighting.Key), $"the settings come before the speed band, and only the lighting after it: line {line}, gap {gap}, speed {speed}");
            Program.Check(!SignLine.On(def, plain) && !SignLine.On(def, template.Parse(plain).With(SignLine.GapKey, "12").ToString()), "off by default: one click places one sign, as before");
            string on = template.Parse(plain).With(SignLine.LineKey, "1").ToString();
            foreach (string form in new[] { "1", "2", "3" })
                Program.Check(SignLine.On(def, template.Parse(on).With("form", form).ToString()), "form " + form + " makes a line");
            Program.Check(!SignLine.On(def, template.Parse(on).With("form", "4").ToString()), "the upright board at the end of a median (form 4) stays a single sign even with the setting on");
            GuideField lineField = template.FieldOf(SignLine.LineKey), gapField = template.FieldOf(SignLine.GapKey);
            Program.Check(lineField.DraftOnly && gapField.DraftOnly, "both are the drawing's, not shown for a sign that is placed");
            Program.Check(!gapField.Visible(template.Parse(plain)) && gapField.Visible(template.Parse(on)) && !gapField.Visible(template.Parse(on).With("form", "4")), "the spacing is shown only with the line on, and not for form 4");
            Program.Check(!lineField.Visible(template.Parse(on).With("form", "4")) && lineField.Visible(template.Parse(on).With("form", "2")), "the setting is not shown for form 4");

            // The spacing typed: digits and one point; empty is automatic.
            Program.Check(SignLine.TypedGap(def, plain) == 0f && SignLine.TypedGap(def, template.Parse(on).With(SignLine.GapKey, "12.5").ToString()) == 12.5f && SignLine.TypedGap(def, template.Parse(on).With(SignLine.GapKey, "abc").ToString()) == 0f, "a typed spacing is read, empty or text is automatic");

            // What is placed has the settings of the drawing put back: the same sign whether it was made by a click or by a line, and the same spec.
            string typed = template.Parse(on).With(SignLine.GapKey, "9").With("form", "2").With("count", "4").ToString();
            string placed = SignLine.Stripped(def, typed);
            GuideSpec back = template.Parse(placed);
            Program.Check(!back.Flag(SignLine.LineKey) && back.Get(SignLine.GapKey).Length == 0 && back.Is("form", "2") && back.Is("count", "4"), "the line settings are put back and the sign's own are kept: " + placed);
            Program.Check(SignLine.Stripped(def, plain) == plain, "a spec without them is as it was");
            Program.Check(SignLine.WithGap(def, on, "20") != on && template.Parse(SignLine.WithGap(def, on, "20")).Get(SignLine.GapKey) == "20", "the spacing can be set in the settings");

            // Another sign has none of it.
            SignDef water = SignCatalog.ById("gb.warn.47");
            Program.Check(water != null && !water.Template.Linear && water.Template.FieldOf(SignLine.LineKey) == null && !SignLine.On(water, water.Template.Default().ToString()), "a sign that did not ask for it has no such setting");
            Program.Check(SignLine.Stripped(water, "x=1") == "x=1" && !SignLine.On(null, "") && SignLine.TypedGap(water, "") == 0f, "and it is left as it is");
        }

        private static void Spacing()
        {
            Program.Check(SignLine.Spacing(12f, float.MaxValue) == 12f && SignLine.Spacing(12f, 20f) == 12f, "a typed spacing is kept whatever the road is");
            Program.Check(SignLine.Spacing(0.5f, 100f) == SignLine.MinGap && SignLine.Spacing(900f, 100f) == SignLine.MaxGap, "a typed spacing is kept between 2 and 200 m");
            // The rule is the delineators' (GB 5768.3 Table 7 from the tightest radius, 30 m on a straight); it is borrowed, and the test says only that it is that rule.
            Program.Check(SignLine.Spacing(0f, 20f) == 4f && SignLine.Spacing(0f, 50f) == 8f && SignLine.Spacing(0f, 100f) == 12f && SignLine.Spacing(0f, 200f) == 16f, "automatic: by the radius, 4, 8, 12, 16 m");
            Program.Check(SignLine.Spacing(0f, float.MaxValue) == 30f && SignLine.Spacing(0f, 5000f) == 30f && SignLine.Spacing(0f, 1500f) == 30f, "automatic: a straight and a gentle curve are 30 m, as the delineators' straight");
            Program.Check(SignLine.Spacing(0f, 20f) == MarkingStuds.DelineatorSpacing(20f, MarkingStuds.StraightOther), "the same function as the delineators'");
            Program.Check(SignLine.Spacing(-3f, 50f) == 8f, "a spacing that is not positive is automatic");
        }

        private static void Stations()
        {
            List<float> a = SignLine.Stations(One(0f, 200f), 10f, 60f, 10f, 100, out bool capped);
            Program.Check(Show(a) == "10 20 30 40 50 60" && !capped, "the start, every spacing, and the end when it is on the grid: " + Show(a));
            List<float> b = SignLine.Stations(One(0f, 200f), 10f, 60f, 15f, 100, out _);
            Program.Check(Show(b) == "10 25 40 55", "the last is not past the end: " + Show(b));
            List<float> c = SignLine.Stations(One(0f, 200f), 60f, 10f, 10f, 100, out _);
            Program.Check(Show(c) == "60 50 40 30 20 10", "drawn toward the start of the road: the stations come the same way: " + Show(c));
            List<float> d = SignLine.Stations(One(0f, 200f), 10f, 14f, 10f, 100, out _);
            Program.Check(Show(d) == "10", "a line shorter than a spacing is the start alone: " + Show(d));
            List<float> e = SignLine.Stations(One(0f, 200f), 10f, 10f, 10f, 100, out _);
            Program.Check(Show(e) == "10", "a line of no length too: " + Show(e));
            List<float> f = SignLine.Stations(One(0f, 200f), 10f, 40f, 10f, 3, out bool cut);
            Program.Check(Show(f) == "10 20 30" && cut, "at most as many as asked, and it says the line was cut: " + Show(f));
            Program.Check(!SignLine.Stations(One(0f, 200f), 10f, 40f, 10f, 4, out bool exact).Count.Equals(0) && !exact, "exactly as many as asked is not cut");
            Program.Check(SignLine.Stations(new List<ChainSpan>(), 10f, 40f, 10f, 100, out _).Count == 0 && SignLine.Stations(null, 10f, 40f, 10f, 100, out _).Count == 0, "no road, no station");
            Program.Check(SignLine.Stations(One(0f, 200f), 10f, 40f, 0f, 100, out _).Count == 0 && SignLine.Stations(One(0f, 200f), float.NaN, 40f, 10f, 100, out _).Count == 0, "a spacing of 0 or a start that is not a number, none");
            List<float> g = SignLine.Stations(One(25f, 200f), 10f, 60f, 10f, 100, out _);
            Program.Check(Show(g) == "30 40 50 60", "a start before the road's stretch begins: the grid goes on from the start and the first on the road is the first sign: " + Show(g));
            List<float> h = SignLine.Stations(One(0f, 200f), 0f, 100f, 25f, 100, out _);
            Program.Check(Show(h) == "0 25 50 75 100", "a station on the very end of the line is placed (the end is on the grid): " + Show(h));
        }

        /// <summary>The chain of the junction tests' kind: two pieces of 200 m, the first ending in a junction whose lanes end at 170 m, the second's begin at 230; a road joins on the right.</summary>
        private static RoadChain Chain(bool joinRight, bool joinLeft)
        {
            ChainPiece Piece(Vec3 a, Vec3 d, ChainJoint after, float laneFrom = float.NaN, float laneTo = float.NaN)
            {
                var piece = new ChainPiece { Curve = Bezier3.Line(a, d), Kmh = 80f, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo, HalfWidth = 7f };
                piece.Forward.Add(1.75f);
                piece.Backward.Add(-1.75f);
                return piece;
            }

            ChainPiece first = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 200f), ChainJoint.Junction, laneTo: 170f);
            first.JoinRight = joinRight;
            first.JoinLeft = joinLeft;
            ChainPiece second = Piece(new Vec3(0f, 0f, 200f), new Vec3(0f, 0f, 400f), ChainJoint.End, laneFrom: 30f);
            return new RoadChain(new[] { first, second });
        }

        private static void Junction()
        {
            RoadChain chain = Chain(true, false);
            // A sign stands beside the road as an edge line does: through a junction on a side no road joins, and not in the cut on a side a road joins.
            List<ChainSpan> right = chain.Spans(8f, true), left = chain.Spans(-8f, true);
            List<float> onRight = SignLine.Stations(right, 0f, 400f, 20f, 100, out _), onLeft = SignLine.Stations(left, 0f, 400f, 20f, 100, out _);
            Program.Check(onLeft.Count == 21, "the side no road joins has a sign at every 20 m: " + onLeft.Count);
            Program.Check(onRight.Count == 18 && !onRight.Exists(s => s > 170f && s < 230f), "the side a road joins has none in the node (180, 200, 220): " + Show(onRight));
            Program.Check(onRight.Contains(160f) && onRight.Contains(240f) && onRight.Contains(260f), "the grid goes on after the node, as it was: " + Show(onRight));
            // A line started in the node itself: the first sign is the first station on the road.
            List<float> from = SignLine.Stations(right, 190f, 300f, 20f, 100, out _);
            Program.Check(Show(from) == "230 250 270 290", "a line that starts in the node has its first sign where the lanes begin again when the grid is on it, else after it (190 + 40): " + Show(from));
            Program.Check(Show(SignLine.Stations(right, 195f, 300f, 20f, 100, out _)) == "235 255 275 295", "and on the grid from the start when it is not: 195 + 40");
            // A line that ends in the node: the signs stop before it.
            List<float> to = SignLine.Stations(right, 100f, 200f, 20f, 100, out _);
            Program.Check(Show(to) == "100 120 140 160", "and one that ends in it has its last before it: " + Show(to));
            Program.Check(SignLine.Stations(right, 175f, 225f, 10f, 100, out _).Count == 0, "a line inside the node has no sign");
        }

        private static ArcTable Straight() => new ArcTable(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f)));

        private static ArcTable Bend(float radius)
        {
            const float k = 0.5523f;
            return new ArcTable(new Bezier3(new Vec3(radius, 0f, 0f), new Vec3(radius, 0f, k * radius), new Vec3(k * radius, 0f, radius), new Vec3(0f, 0f, radius)));
        }

        private static void PostSnap()
        {
            ArcTable straight = Straight();
            Vec3 right = new Vec3(0f, 0f, 1f).RightXZ();
            Vec3 mid = new Vec3(0f, 0f, 50f);
            EdgeSnapResult r = SignLine.PostSnap(straight, 12f, mid, 1, 8f, 0.3f);
            Program.Check(Math.Abs(Vec3.Dot(r.Position - mid, right) - 8f) < 0.001f && Math.Abs(r.Position.Z - 50f) < 0.01f, $"on the right: 8 m from the centre line, along the road where it is asked for: {r.Position}");
            Program.Check(Math.Abs(r.Position.Y - 0.3f) < 0.001f, "at the road's height plus the surface");
            Program.Check(Math.Abs(r.Yaw - (float)Math.PI) < 0.001f || Math.Abs(r.Yaw + (float)Math.PI) < 0.001f, "it faces back along the road at the traffic that comes along it: " + r.Yaw);
            EdgeSnapResult l = SignLine.PostSnap(straight, 12f, mid, -1, 8f, 0f);
            Program.Check(Math.Abs(Vec3.Dot(l.Position - mid, right) + 8f) < 0.001f && Math.Abs(l.Yaw) < 0.001f, $"on the left: the other side, facing the other way: {l.Position}, yaw {l.Yaw}");
            Program.Check(Math.Abs(Vec3.Dot(SignLine.PostSnap(straight, 12f, mid, 1, 6.5f, 0f).Position - mid, right) - 6.5f) < 0.001f, "the post distance is what Carriageway gives (not the road's half width and a margin)");

            // The same as the tool's click makes it: the options of EdgeSnap with the side and the margin that give that distance.
            var options = EdgeSnapOptions.Default;
            options.Side = SnapSide.Right;
            options.Margin = 8f - 6f;
            EdgeSnapResult click = EdgeSnap.Solve(straight, 12f, mid, options);
            Program.Check(Vec3.DistanceXZ(click.Position, r.Position) < 1e-4f && Math.Abs(click.Yaw - r.Yaw) < 1e-5f && click.T == r.T, "a place of a line is the place of a click there");

            // On a bend of 50 m radius the posts keep their distance from the road's curve all the way round, and face against the way the traffic on their side runs.
            ArcTable bend = Bend(50f);
            for (int i = 1; i < 6; i++)
            {
                float s = bend.Length * i / 6f;
                Vec3 on = bend.Curve.Point(bend.TAt(s));
                EdgeSnapResult p = SignLine.PostSnap(bend, 12f, on, 1, 7f, 0f);
                Vec3 heading = bend.Curve.Tangent(p.T).HeadingXZ();
                float across = Vec3.Dot(p.Position - bend.Curve.Point(p.T), heading.RightXZ());
                Vec3 facing = new Vec3((float)Math.Sin(p.Yaw), 0f, (float)Math.Cos(p.Yaw));
                Program.Check(Math.Abs(across - 7f) < 0.01f && Vec3.Dot(facing, heading) < -0.999f, $"on the bend at {s:0} m: {across:0.00} m to the right of the curve, facing {Vec3.Dot(facing, heading):0.000} of the way along it");
            }

            // A road that is read against its segment's direction has its edge's other side for the chain's right.
            Program.Check(SignLine.EdgeSide(1, false) == 1 && SignLine.EdgeSide(1, true) == -1 && SignLine.EdgeSide(-1, true) == 1 && SignLine.EdgeSide(-1, false) == -1, "the chain's side on a piece that runs against its segment");
        }

        private static void Others()
        {
            foreach (SupportKind kind in new[] { SupportKind.SinglePole, SupportKind.DoublePole, SupportKind.TriplePole })
                Program.Check(SignLine.CanStand(kind), kind + " is a post of its own");
            foreach (SupportKind kind in new[] { SupportKind.Cantilever, SupportKind.DoubleCantilever, SupportKind.FCantilever, SupportKind.Gantry, SupportKind.Attached, SupportKind.AttachedArm, SupportKind.WallFlat, SupportKind.WallBracket })
                Program.Check(!SignLine.CanStand(kind), kind + " is not");

            foreach (bool zh in new[] { true, false })
            {
                string start = HintText.Of(new HintInfo { Kind = "line-start" }, zh);
                string end = HintText.Of(new HintInfo { Kind = "line-end", Count = 7, Left = 187.5f, Need = 30f }, zh);
                Program.Check(start.Length > 8 && end.Contains("187.5") && end.Contains("30.0") && end.Contains("7"), "the line's hints say the length, the spacing and the number: " + end);
                Program.Check(HintText.Of(new HintInfo { Kind = "line-end", Bad = true }, zh).Length > HintText.Of(new HintInfo { Kind = "line-end" }, zh).Length, "a line that cannot end here says so first");
                Program.Check(HintText.Of(new HintInfo { Kind = "line-end", Count = 200, Max = 200, Left = 900f, Need = 4f }, zh).Contains("200"), "a line that is cut says where");
                Program.Check(HintText.Of(new HintInfo { Kind = "line-start", Bad = true }, zh).Length > start.Length, "a place that cannot start it says so first");
                string strip = HintText.Of(new HintInfo { Kind = "line-strip", Bad = true }, zh);
                Program.Check(strip.Length > 10 && (zh ? strip.Contains("隔离带") : strip.Contains("strip")) && !strip.Contains("{"), "a line on a strip between the carriageways says why not: " + strip);
                List<string[]> stripKeys = HintText.Keys(new HintInfo { Kind = "line-strip", Bad = true }, zh);
                Program.Check(stripKeys.Count > 0 && stripKeys[stripKeys.Count - 1][0].Contains("Esc"), "and has the way out");
                List<string[]> keys = HintText.Keys(new HintInfo { Kind = "line-end" }, zh);
                Program.Check(keys.Count >= 2 && keys[keys.Count - 1][0].Contains("Esc") && keys.Exists(k => k[0] == (zh ? "左键" : "Left click")), "the keys of the end: the click and the way out that cancels the line");
                Program.Check(HintText.Keys(new HintInfo { Kind = "line-start" }, zh).Exists(k => k[0] == (zh ? "左键" : "Left click")), "and of the start");
            }
        }
    }
}
