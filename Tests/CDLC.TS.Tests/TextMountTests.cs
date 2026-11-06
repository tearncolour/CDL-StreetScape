using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>A text light box on a building (0.12.76): the base frame on a wall (spacers, braces, or the studs straight to the wall), the legs and feet on a roof, a bracket's bars, a curved wall.</summary>
    internal static class TextMountTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("text mount: the wall frame's spacers and braces", WallPlan);
            Program.Run("text mount: without the frame the studs carry it, and the planner counts the load", NonePlan);
            Program.Run("text mount: the roof's legs, braces and feet", RoofPlan);
            Program.Run("text mount: built on a flat wall", BuiltWall);
            Program.Run("text mount: built on a roof", BuiltRoof);
            Program.Run("text mount: built on a wall bracket", BuiltBracket);
            Program.Run("text mount: built on a curved wall", BuiltArc);
            Program.Run("text mount: the settings", Settings);
        }

        private static GuideSpec Spec(string text, string extra = "") => TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text);

        private static SignInstance Instance(string text, string extra = "") => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), Spec(text, extra).ToString());

        private static SignParts Build(SignInstance instance, string support, float reach = 0f)
        {
            SupportDef def = SupportCatalog.ById(support);
            return SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, def, reach, SupportDetail.Full, default);
        }

        private static FramePlan Frame(SignInstance instance) => TextPlans.Get(instance.Board.Text.Job)?.Frame;

        private static void Settings()
        {
            Program.Check(TextBoxes.Template.FieldOf("mount") != null && TextBoxes.Template.FieldOf("arc") != null, "the template has the fields mount and arc");
            Program.Check(TextBoxes.Template.FieldOf("arc").Visible != null && !TextBoxes.Template.FieldOf("arc").Visible(TextBoxes.Template.Default()), "the arc is not a field to type: hidden");
            Program.Check(TextBoxes.Template.FieldOf("mount").Visible == null, "the wall frame choice is shown");
            Program.Check(Instance("文字").Board.Text.Mount == TextMount.Auto && Instance("文字", ";mount=none").Board.Text.Mount == TextMount.None, "the mount: automatic or none");
            Program.Check(Instance("文字", ";mount=banana").Board.Text.Mount == TextMount.Auto, "anything else is automatic");
            Program.Near(Instance("文字", ";arc=12.5").Board.Text.Radius, 12.5f, 1e-4f, "a radius in the settings");
            Program.Near(Instance("文字", ";arc=-30").Board.Text.Radius, -30f, 1e-4f, "a negative one: a concave wall");
            Program.Check(Instance("文字", ";arc=900").Board.Text.Radius == 200f && Instance("文字", ";arc=-900").Board.Text.Radius == -200f, "limited to 200 m either way");
            Program.Check(Instance("文字", ";arc=abc").Board.Text.Radius == 0f && Instance("文字").Board.Text.Radius == 0f, "no radius is a flat wall");
            string written = Spec("文字", ";arc=-12.5;mount=none").ToString();
            Program.Check(written.Contains("arc=-12.5") && written.Contains("mount=none"), "both are in the written settings: " + written);
        }

        private static void WallPlan()
        {
            SignInstance small = Instance("文字灯箱", ";h=0.4");
            FramePlan frame = Frame(small);
            FrameStyle style = FrameStyle.For(0.4f);
            WallFramePlan plan = TextWallFrame.Plan(frame, style, TextMount.Auto);
            Program.Check(!plan.StudsToWall && !plan.Large && plan.Braces.Count == 0, "a small text: no braces, studs stay off the wall");
            Program.Check(plan.Standoffs.Count >= frame.Rails.Count * 2 - plan.DroppedStandoffs, "a spacer at each end of each rail: " + plan.Standoffs.Count + " for " + frame.Rails.Count + " rails");
            float clear = TextWallFrame.StandoffRadius + Math.Max(0.0015f, style.StudRadius);
            foreach (Point2 s in plan.Standoffs)
            {
                Program.Check(frame.Rails.Any(r => Math.Abs((r.A.Y + r.B.Y) * 0.5f - s.Y) < 1e-4f && s.X >= Math.Min(r.A.X, r.B.X) - 1e-4f && s.X <= Math.Max(r.A.X, r.B.X) + 1e-4f), "a spacer is on a rail");
                Program.Check(frame.Studs.All(st => (st.At.X - s.X) * (st.At.X - s.X) + (st.At.Y - s.Y) * (st.At.Y - s.Y) >= clear * clear - 1e-7f), "and clear of every stud");
            }

            // No rail goes more than the pitch (and the slide to miss a stud) between two of its spacers.
            foreach (FrameSegment rail in frame.Rails)
            {
                List<float> xs = plan.Standoffs.Where(s => Math.Abs(s.Y - (rail.A.Y + rail.B.Y) * 0.5f) < 1e-4f && s.X >= Math.Min(rail.A.X, rail.B.X) - 1e-4f && s.X <= Math.Max(rail.A.X, rail.B.X) + 1e-4f).Select(s => s.X).OrderBy(x => x).ToList();
                for (int i = 1; i < xs.Count; i++)
                    Program.Check(xs[i] - xs[i - 1] <= TextWallFrame.StandoffPitch + 0.13f, "spacers at most a pitch apart: " + (xs[i] - xs[i - 1]));
            }

            // A large text is braced between the two rails of each line, clear of the studs.
            SignInstance big = Instance("文字灯箱文字灯箱", ";h=0.8");
            FramePlan bigFrame = Frame(big);
            WallFramePlan bigPlan = TextWallFrame.Plan(bigFrame, FrameStyle.For(0.8f), TextMount.Auto);
            Program.Check(bigPlan.Large && bigPlan.Braces.Count >= 4, "a large text has diagonal braces: " + bigPlan.Braces.Count);
            float braceClear = TextWallFrame.BraceRadius + Math.Max(0.0015f, FrameStyle.For(0.8f).StudRadius);
            foreach (FrameSegment brace in bigPlan.Braces)
            {
                var heights = bigFrame.RailHeights.Where(h => h.Line == brace.Line).ToList();
                Program.Check(heights.Count == 1 && ((Math.Abs(brace.A.Y - heights[0].Low) < 1e-4f && Math.Abs(brace.B.Y - heights[0].High) < 1e-4f) || (Math.Abs(brace.A.Y - heights[0].High) < 1e-4f && Math.Abs(brace.B.Y - heights[0].Low) < 1e-4f)), "a brace runs from one rail of its line to the other");
                Program.Check(bigFrame.Studs.All(st => TextWallFrame.DistanceToSegment(st.At, brace.A, brace.B) >= braceClear - 1e-6f), "and misses every stud");
            }

            // A stud on the line of a brace moves it aside or leaves it out: none passes within its clearance of any stud.
            var truss = new FramePlan { EffectiveArea = 2f };
            truss.Rails.Add(new FrameSegment { A = new Point2(-1.5f, 0f), B = new Point2(1.5f, 0f), Line = 0 });
            truss.Rails.Add(new FrameSegment { A = new Point2(-1.5f, 0.4f), B = new Point2(1.5f, 0.4f), Line = 0 });
            truss.RailHeights.Add((0, 0f, 0.4f));
            float bay = 3f / 8f;
            // 4 bays of two braces each: the first goes from (-1.5, 0) to (-1.125, 0.4); a stud at its middle.
            truss.Studs.Add(new FrameStud { At = new Point2(-1.5f + bay * 0.5f, 0.2f) });
            WallFramePlan trussPlan = TextWallFrame.Plan(truss, FrameStyle.For(0.4f), TextMount.Auto);
            float trussClear = TextWallFrame.BraceRadius + Math.Max(0.0015f, FrameStyle.For(0.4f).StudRadius);
            Program.Check(trussPlan.Braces.Count + trussPlan.DroppedBraces == 8 && trussPlan.Braces.Count >= 7, "eight braces in the truss: " + trussPlan.Braces.Count + " built, " + trussPlan.DroppedBraces + " left out");
            Program.Check(trussPlan.Braces.All(b => truss.Studs.All(s => TextWallFrame.DistanceToSegment(s.At, b.A, b.B) >= trussClear - 1e-6f)), "and none touches the stud on the first one's line");
            // The braces of a line stay between its own rails: a second line whose rails are at the same heights but far to the side does not widen the first's.
            var twin = new FramePlan { EffectiveArea = 2f };
            twin.Rails.Add(new FrameSegment { A = new Point2(-1.5f, 0f), B = new Point2(-0.5f, 0f), Line = 0 });
            twin.Rails.Add(new FrameSegment { A = new Point2(-1.5f, 0.4f), B = new Point2(-0.5f, 0.4f), Line = 0 });
            twin.Rails.Add(new FrameSegment { A = new Point2(4f, 0f), B = new Point2(6f, 0f), Line = 1 });
            twin.Rails.Add(new FrameSegment { A = new Point2(4f, 0.4f), B = new Point2(6f, 0.4f), Line = 1 });
            twin.RailHeights.Add((0, 0f, 0.4f));
            twin.RailHeights.Add((1, 0f, 0.4f));
            WallFramePlan twinPlan = TextWallFrame.Plan(twin, FrameStyle.For(0.4f), TextMount.Auto);
            Program.Check(twinPlan.Braces.Count > 0 && twinPlan.Braces.All(b => b.Line == 0 ? b.A.X >= -1.5f - 1e-4f && b.B.X <= -0.5f + 1e-4f && b.A.X <= -0.5f + 1e-4f : b.A.X >= 4f - 1e-4f), "each line's braces are over its own rails: " + string.Join(" ", twinPlan.Braces.Select(b => b.Line + ":" + b.A.X.ToString("0.0") + ".." + b.B.X.ToString("0.0"))));

            // Neighbouring braces alternate in direction.
            var one = bigPlan.Braces.Where(b => b.Line == bigPlan.Braces[0].Line).ToList();
            Program.Check(one.Count < 2 || (one[0].B.Y > one[0].A.Y) != (one[1].B.Y > one[1].A.Y), "the braces of a line alternate");

            Program.Check(TextWallFrame.IsLarge(bigFrame) && !TextWallFrame.IsLarge(frame) && !TextWallFrame.IsLarge(null), "the text that is large");
            var longRail = new FramePlan { EffectiveArea = 0.5f };
            longRail.Rails.Add(new FrameSegment { A = new Point2(0f, 0f), B = new Point2(3f, 0f) });
            Program.Check(TextWallFrame.IsLarge(longRail), "a rail over 2.4 m makes it large, whatever the area");
            var shortRail = new FramePlan { EffectiveArea = 0.5f };
            shortRail.Rails.Add(new FrameSegment { A = new Point2(0f, 0f), B = new Point2(2f, 0f) });
            Program.Check(!TextWallFrame.IsLarge(shortRail) && TextWallFrame.IsLarge(new FramePlan { EffectiveArea = 1.6f }) && !TextWallFrame.IsLarge(new FramePlan { EffectiveArea = 1.5f }), "a short rail and a small area: not large; over 1.5 m2: large");

            // The first spacer of a rail stands inset from its end.
            foreach (FrameSegment rail in frame.Rails)
            {
                float x0 = Math.Min(rail.A.X, rail.B.X), x1 = Math.Max(rail.A.X, rail.B.X);
                float first = plan.Standoffs.Where(s => Math.Abs(s.Y - (rail.A.Y + rail.B.Y) * 0.5f) < 1e-4f && s.X >= x0 - 1e-4f && s.X <= x1 + 1e-4f).Min(s => s.X);
                Program.Check(first >= x0 + Math.Min(TextWallFrame.StandoffInset, (x1 - x0) * 0.25f) - 0.061f, "the first spacer is inset from the rail's end: " + (first - x0));
            }

            // Spacers that were moved to miss a stud do not land on each other: two targets 8 cm apart, studs on both and beside, the first moves in, the second out of its way.
            var crowded = new FramePlan();
            crowded.Rails.Add(new FrameSegment { A = new Point2(0f, 0f), B = new Point2(0.16f, 0f) });
            foreach (float sx in new[] { 0.04f, 0.12f, 0.01f, 0.15f })
                crowded.Studs.Add(new FrameStud { At = new Point2(sx, 0f) });
            WallFramePlan crowdedPlan = TextWallFrame.Plan(crowded, FrameStyle.For(0.4f), TextMount.Auto);
            for (int i = 0; i < crowdedPlan.Standoffs.Count; i++)
            {
                for (int j = i + 1; j < crowdedPlan.Standoffs.Count; j++)
                    Program.Check(Math.Abs(crowdedPlan.Standoffs[i].X - crowdedPlan.Standoffs[j].X) >= 2f * TextWallFrame.StandoffRadius + 0.004f - 1e-5f, "two spacers are not on top of each other: " + crowdedPlan.Standoffs[i].X + " " + crowdedPlan.Standoffs[j].X);
            }

            // A spacer with no free place is left out and counted.
            var packed = new FramePlan();
            packed.Rails.Add(new FrameSegment { A = new Point2(-0.1f, 0f), B = new Point2(0.1f, 0f) });
            for (int ix = -25; ix <= 25; ix++)
            {
                for (int iy = -8; iy <= 8; iy++)
                    packed.Studs.Add(new FrameStud { At = new Point2(ix * 0.01f, iy * 0.01f) });
            }

            WallFramePlan packedPlan = TextWallFrame.Plan(packed, FrameStyle.For(0.4f), TextMount.Auto);
            Program.Check(packedPlan.Standoffs.Count == 0 && packedPlan.DroppedStandoffs >= 2, "no room among the studs: " + packedPlan.Standoffs.Count + " spacers, " + packedPlan.DroppedStandoffs + " left out");
            Program.Near(TextWallFrame.DistanceToSegment(new Point2(1f, 1f), new Point2(0f, 0f), new Point2(2f, 0f)), 1f, 1e-6f, "the distance to a segment: from its side");
            Program.Near(TextWallFrame.DistanceToSegment(new Point2(5f, 0f), new Point2(0f, 0f), new Point2(2f, 0f)), 3f, 1e-6f, "from its end");
            Program.Near(TextWallFrame.DistanceToSegment(new Point2(1f, 1f), new Point2(0f, 0f), new Point2(0f, 0f)), 1.4142135f, 1e-5f, "to a point");
            Program.Check(TextWallFrame.Plan(null, style, TextMount.Auto).Standoffs.Count == 0, "no frame: nothing");
        }

        private static void NonePlan()
        {
            SignInstance board = Instance("文字灯箱", ";h=0.4");
            FramePlan frame = Frame(board);
            WallFramePlan none = TextWallFrame.Plan(frame, FrameStyle.For(0.4f), TextMount.None);
            Program.Check(none.StudsToWall && none.Standoffs.Count == 0 && none.Braces.Count == 0, "no frame: the studs reach the wall, no spacers, no braces");
            Program.Near(none.KgPerStud, frame.MassKg / frame.Studs.Count, 1e-4f, "the load of a stud: " + none.KgPerStud);
            Program.Check(!none.TooHeavyForStuds, "a small text's studs carry it: " + none.KgPerStud + " kg a stud");

            var heavy = new FramePlan { MassKg = 100f };
            for (int i = 0; i < 10; i++)
                heavy.Studs.Add(new FrameStud { At = new Point2(i, 0f) });
            Program.Check(TextWallFrame.Plan(heavy, FrameStyle.For(1f), TextMount.None).TooHeavyForStuds, "10 kg on a stud is too much");
            heavy.MassKg = TextWallFrame.StudCapacityKg * 10f;
            Program.Check(!TextWallFrame.Plan(heavy, FrameStyle.For(1f), TextMount.None).TooHeavyForStuds, "exactly the capacity is not");
            heavy.MassKg = TextWallFrame.StudCapacityKg * 10f + 0.1f;
            Program.Check(TextWallFrame.Plan(heavy, FrameStyle.For(1f), TextMount.None).TooHeavyForStuds, "a little over is");
            Program.Check(TextWallFrame.Plan(new FramePlan { MassKg = 50f }, FrameStyle.For(1f), TextMount.None).KgPerStud == 0f, "no studs: no load to divide");
            // A big text on its studs alone.
            SignInstance big = Instance("文字灯箱文字灯箱", ";h=1.2");
            WallFramePlan bigNone = TextWallFrame.Plan(Frame(big), FrameStyle.For(1.2f), TextMount.None);
            Program.Check(bigNone.KgPerStud > none.KgPerStud, "bigger letters, more on each stud: " + bigNone.KgPerStud + " against " + none.KgPerStud);
        }

        private static void RoofPlan()
        {
            SignInstance board = Instance("文字灯箱", ";h=0.4");
            FramePlan frame = Frame(board);
            FrameStyle style = FrameStyle.For(0.4f);
            float height = board.Def.Height, lower = 0.5f;
            RoofFramePlan roof = TextRoofFrame.Plan(frame, style, TextMount.Auto, lower, height, 1);
            Program.Check(roof.Legs.Count >= 2 && !roof.OnFeet && roof.Refused == "", "legs under a text with a frame");
            Program.Check(roof.Legs[0].X <= frame.Rails.Min(r => Math.Min(r.A.X, r.B.X)) + TextRoofFrame.LegInset + 1e-4f && roof.Legs[roof.Legs.Count - 1].X >= frame.Rails.Max(r => Math.Max(r.A.X, r.B.X)) - TextRoofFrame.LegInset - 1e-4f, "legs near the ends of the rails");
            for (int i = 1; i < roof.Legs.Count; i++)
                Program.Check(roof.Legs[i].X - roof.Legs[i - 1].X <= TextRoofFrame.LegPitch + 1e-4f, "legs at most a pitch apart");
            float topRail = frame.Rails.Max(r => (r.A.Y + r.B.Y) * 0.5f);
            Program.Near(roof.Legs[0].Top, lower + height * 0.5f + topRail + 0.03f, 1e-4f, "a leg reaches the top rail: " + roof.Legs[0].Top);
            Program.Check(roof.Braces.Count == 0 && roof.DepthBehind == 0f, "a low, narrow text needs no rear brace: " + roof.Braces.Count);
            Program.Near(roof.Top, lower + height, 1e-4f, "the top of the text above the roof");

            SignInstance tall = Instance("文字灯箱文字灯箱", ";h=1");
            RoofFramePlan tallRoof = TextRoofFrame.Plan(Frame(tall), FrameStyle.For(1f), TextMount.Auto, 0.5f, tall.Def.Height, 1);
            Program.Check(tallRoof.Braces.Count >= 2 && tallRoof.DepthBehind > 0.3f, "a wide, tall text has rear braces: " + tallRoof.Braces.Count + " " + tallRoof.DepthBehind);
            for (int i = 1; i < tallRoof.Legs.Count; i++)
                Program.Check(tallRoof.Legs[i].X - tallRoof.Legs[i - 1].X <= TextRoofFrame.LegPitch + 1e-4f, "a wide text: legs at most a pitch apart: " + (tallRoof.Legs[i].X - tallRoof.Legs[i - 1].X));
            Program.Check(tallRoof.Legs.Count >= 4, "a wide text has several legs: " + tallRoof.Legs.Count);
            Program.Check(tallRoof.Braces[0].X == tallRoof.Legs[0].X && tallRoof.Braces[tallRoof.Braces.Count - 1].X == tallRoof.Legs[tallRoof.Legs.Count - 1].X, "the outer legs are braced");
            Program.Check(tallRoof.Braces.All(b => b.Depth <= 2.0f + 1e-5f && b.Depth >= 0.4f - 1e-5f && b.From < tallRoof.Top), "a brace goes back 0.4 to 2 m from under the top");
            Program.Check(tallRoof.MinX < tallRoof.Legs[0].X && tallRoof.MaxX > tallRoof.Legs[tallRoof.Legs.Count - 1].X, "the footprint covers the legs");

            // Without the frame: one foot under the lowest stud of each letter of a line.
            RoofFramePlan feet = TextRoofFrame.Plan(frame, style, TextMount.None, lower, height, 1);
            int islands = frame.Studs.Select(s => s.Island).Distinct().Count();
            Program.Check(feet.OnFeet && feet.Legs.Count == 0 && feet.Feet.Count >= islands / 2 && feet.Feet.Count <= islands, "on feet: " + feet.Feet.Count + " for " + islands + " islands");
            for (int i = 1; i < feet.Feet.Count; i++)
                Program.Check(feet.Feet[i].X - feet.Feet[i - 1].X >= 2f * TextRoofFrame.FootHalf + 0.01f - 1e-5f, "two feet are further apart than a foot plate");
            foreach (RoofLeg foot in feet.Feet)
            {
                Program.Check(frame.Studs.Any(s => Math.Abs(s.At.X - foot.X) < 1e-5f && Math.Abs(lower + height * 0.5f + s.At.Y - foot.Top) < 1e-4f), "a foot is under a stud");
                Program.Check(foot.Top >= lower - 1e-4f, "and no lower than the text's edge");
                FrameStud under = frame.Studs.First(s => Math.Abs(s.At.X - foot.X) < 1e-5f);
                float lowestOfIsland = frame.Studs.Where(s => s.Island == under.Island).Min(s => s.At.Y);
                Program.Near(foot.Top, lower + height * 0.5f + lowestOfIsland, 1e-4f, "under the lowest stud of its letter");
            }

            // A letter whose two studs are at different heights stands on the lower one: the foot is under it.
            var tilted = new FramePlan();
            tilted.Rails.Add(new FrameSegment { A = new Point2(-0.5f, 0.1f), B = new Point2(0.5f, 0.1f) });
            tilted.Studs.Add(new FrameStud { At = new Point2(-0.3f, 0.35f), Island = 1 });
            tilted.Studs.Add(new FrameStud { At = new Point2(0.0f, -0.05f), Island = 1 });
            tilted.Studs.Add(new FrameStud { At = new Point2(0.3f, 0.2f), Island = 2 });
            RoofFramePlan tiltedFeet = TextRoofFrame.Plan(tilted, style, TextMount.None, 0.5f, 0.8f, 1);
            Program.Check(tiltedFeet.OnFeet && tiltedFeet.Feet.Count == 2 && Math.Abs(tiltedFeet.Feet[0].X - 0.0f) < 1e-5f && Math.Abs(tiltedFeet.Feet[0].Top - (0.5f + 0.4f - 0.05f)) < 1e-4f && Math.Abs(tiltedFeet.Feet[1].X - 0.3f) < 1e-5f, "the foot of a letter is under its lowest stud: " + string.Join(" ", tiltedFeet.Feet.Select(ft => ft.X + "/" + ft.Top)));

            // More than one line cannot stand on feet: the frame is built, and it says why.
            SignInstance two = Instance("上行\\n下行", ";h=0.4");
            RoofFramePlan refused = TextRoofFrame.Plan(Frame(two), FrameStyle.For(0.4f), TextMount.None, lower, two.Def.Height, 2);
            Program.Check(!refused.OnFeet && refused.Legs.Count >= 2 && refused.Refused.Contains("line"), "two lines on feet: refused, legs instead: " + refused.Refused);
            Program.Check(TextRoofFrame.Plan(null, style, TextMount.Auto, 0.5f, 1f, 1).Legs.Count == 0, "no frame: nothing");
        }

        /// <summary>A typed backslash and an n: a line break in a text field.</summary>
        private static readonly string NewLine = new string(new[] { (char)92, 'n' });

        private static float MinZ(MeshBuilder mesh) => mesh.Positions.Count == 0 ? 0f : mesh.Positions.Min(p => p.Z);

        private static void BuiltWall()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱", ";h=0.4");
            SignParts wall = Build(instance, SupportCatalog.WallFlat);
            TextBoard text = instance.Board.Text;
            FramePlan frame = TextPlans.Peek(text.Job).Frame;
            FrameStyle style = FrameStyle.For(0.4f);
            WallFramePlan plan = TextWallFrame.Plan(frame, style, TextMount.Auto);
            float railFront = wall.Layout.BackZ + style.RailSize;
            Program.Check(wall.Metal.Positions.Any(p => Math.Abs(p.Z - railFront) < 1e-5f), "on a wall with the frame: the rails' front is there");
            Program.Check(MinZ(wall.Metal) >= -1e-4f, "nothing goes into the wall: " + MinZ(wall.Metal));
            int atWall = wall.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-5f);
            Program.Check(atWall >= plan.Standoffs.Count * 6, "every spacer reaches the wall: " + atWall + " vertices for " + plan.Standoffs.Count + " spacers");
            int atRail = wall.Metal.Positions.Count(p => Math.Abs(p.Z - (wall.Layout.BackZ + 0.002f)) < 1e-5f);
            Program.Check(atRail >= plan.Standoffs.Count * 6, "and goes into the rail's back: " + atRail);
            // The generic spacers of the support (by the board's box) are not built for the text: nothing at the wall but the spacers.
            Program.Check(atWall <= plan.Standoffs.Count * 8, "no other spacer at the wall: " + atWall);

            SignInstance big = Instance("文字灯箱文字灯箱", ";h=0.8");
            SignParts bigWall = Build(big, SupportCatalog.WallFlat);
            WallFramePlan bigPlan = TextWallFrame.Plan(TextPlans.Peek(big.Board.Text.Job).Frame, FrameStyle.For(0.8f), TextMount.Auto);
            Program.Check(bigPlan.Braces.Count > 0 && bigWall.Metal.VertexCount > wall.Metal.VertexCount, "a big text builds its braces");
            // A brace is built in the plane of the rails (half a rail's size in front of the support), where it is fixed to them.
            PickShape bigPick = bigWall.Picks.Find(p => p.Name == "board");
            float bigMid = bigWall.Layout.BackZ + FrameStyle.For(0.8f).RailSize * 0.5f;
            foreach (FrameSegment brace in bigPlan.Braces)
            {
                int ends = bigWall.Metal.Positions.Count(p => Math.Abs(p.X - (bigPick.CentreX - brace.A.X)) < 0.012f && Math.Abs(p.Y - (bigPick.CentreY + brace.A.Y)) < 0.012f && Math.Abs(p.Z - bigMid) <= TextWallFrame.BraceRadius + 1e-4f);
                Program.Check(ends >= 6, "a brace starts in the rails' plane: " + ends + " vertices");
            }

            // No frame: the studs go all the way to the wall, nothing else.
            SignInstance none = Instance("文字灯箱", ";h=0.4;mount=none");
            SignParts bare = Build(none, SupportCatalog.WallFlat);
            FramePlan bareFrame = TextPlans.Peek(none.Board.Text.Job).Frame;
            int bareAtWall = bare.Metal.Positions.Count(p => Math.Abs(p.Z) < 1e-5f);
            Program.Check(bareAtWall >= bareFrame.Studs.Count * 6 && bareAtWall <= bareFrame.Studs.Count * 8, "every stud reaches the wall: " + bareAtWall + " for " + bareFrame.Studs.Count + " studs");
            Program.Check(!bare.Metal.Positions.Any(p => Math.Abs(p.Z - railFront) < 1e-5f), "and there is no rail");
            Program.Check(bare.Metal.VertexCount < wall.Metal.VertexCount, "no frame is less metal: " + bare.Metal.VertexCount + " against " + wall.Metal.VertexCount);
            Program.Near(MinZ(bare.Solids[(int)text.Glow]), SignBuilder.FacePlane(bare.Layout, none), 1e-4f, "the letters are where they were");

            // The editor's one line when the studs alone would carry too much.
            SignInstance huge = Instance("文字灯箱文字灯箱", ";h=3;mount=none");
            TextPlans.Get(huge.Board.Text.Job);
            Program.Check(TextBoxes.Hints(huge.Spec, true).Any(l => l.Contains("螺柱") && l.Contains("kg")) && TextBoxes.Hints(huge.Spec, false).Any(l => l.Contains("stud")), "heavy letters on studs alone: a line in each language: " + TextWallFrame.Plan(TextPlans.Get(huge.Board.Text.Job).Frame, FrameStyle.For(3f), TextMount.None).KgPerStud + " kg a stud, " + TextBoxes.Hints(huge.Spec, true).Count + " hints");
            Program.Check(!TextBoxes.Hints(none.Spec, true).Any(l => l.Contains("螺柱")), "small ones: no line");
            Program.Check(!TextBoxes.Hints(Instance("文字灯箱文字灯箱", ";h=3").Spec, true).Any(l => l.Contains("螺柱")), "with the frame: no line");

            // Off: as 0.12.75 had it.
            bool was = TextSupports.Enabled;
            try
            {
                TextSupports.Enabled = false;
                SignParts off = Build(instance, SupportCatalog.WallFlat);
                Program.Check(!off.Metal.Positions.Any(p => Math.Abs(p.Z - railFront) < 1e-5f), "off: no rails on a wall");
            }
            finally
            {
                TextSupports.Enabled = was;
            }
        }

        private static void BuiltRoof()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱文字灯箱", ";h=1");
            float lower = 0.6f;
            SignParts roof = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, SupportCatalog.ById(SupportCatalog.WallRoof), lower, SupportDetail.Full, default);
            Program.Check(roof.Layout.Roof && roof.Layout.Kind == SupportKind.WallFlat && roof.Layout.BackZ == SupportLayouts.RoofBackZ, "the roof support's layout");
            TextBoard text = instance.Board.Text;
            FramePlan frame = TextPlans.Peek(text.Job).Frame;
            Program.Check(roof.Metal.Positions.All(p => p.Y >= -1e-4f), "nothing below the roof");
            RoofFramePlan roofPlan = TextRoofFrame.Plan(frame, FrameStyle.For(1f), TextMount.Auto, lower, instance.Def.Height, 1);
            float legHalf = TextRoofFrame.LegHalf, legZ = roof.Layout.BackZ - legHalf;
            foreach (RoofLeg leg in roofPlan.Legs)
                Program.Check(roof.Metal.Positions.Any(p => Math.Abs(p.Y - leg.Top) < 1e-4f && Math.Abs(p.Z - legZ) <= legHalf + 1e-4f && Math.Abs(p.X - (roof.Picks.Find(q => q.Name == "board").CentreX - leg.X)) <= legHalf + 1e-4f), "a leg reaches the top rail: " + leg.Top);
            Program.Check(roof.Metal.Positions.Count(p => Math.Abs(p.Y) < 1e-5f) >= 8, "there are foot plates on it");
            Program.Check(MinZ(roof.Metal) < -0.3f, "rear braces reach back to feet: " + MinZ(roof.Metal));
            Program.Check(roof.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)), "finite");
            Program.Check(roof.Solids[(int)text.Glow].VertexCount > 400, "with its letters");

            // On feet: one line, a leg to each letter.
            SignInstance line = Instance("文字灯箱", ";h=0.4;mount=none");
            SignParts feet = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = line } }, SupportCatalog.ById(SupportCatalog.WallRoof), 0.3f, SupportDetail.Full, default);
            Program.Check(feet.Metal.Positions.All(p => p.Y >= -1e-4f), "feet: nothing below the roof");
            Program.Check(!feet.Metal.Positions.Any(p => Math.Abs(p.Z - (feet.Layout.BackZ + FrameStyle.For(0.4f).RailSize)) < 1e-5f), "feet: there is no rail");
            Program.Check(MinZ(feet.Metal) > -0.05f && MinZ(roof.Metal) < -0.3f, "feet: nothing behind but a foot plate's reach, where a frame has braces: " + MinZ(feet.Metal));
            SignInstance twoLines = Instance("上行\\n下行", ";h=0.4;mount=none");
            SignParts framed = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = twoLines } }, SupportCatalog.ById(SupportCatalog.WallRoof), 0.3f, SupportDetail.Full, default);
            Program.Check(framed.Metal.VertexCount > feet.Metal.VertexCount, "two lines on feet are built with the frame");
            Program.Check(framed.Metal.Positions.Any(p => Math.Abs(p.Z - (framed.Layout.BackZ + FrameStyle.For(0.4f).RailSize)) < 1e-5f), "and has rails");

            // A board that is not a text is not the roof's business: built as any flat wall support, finite.
            SignParts other = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.36"), "speed=60") } }, SupportCatalog.ById(SupportCatalog.WallRoof), 0f, SupportDetail.Full, default);
            Program.Check(other.Metal.VertexCount > 0 && other.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z)), "any other board builds");
        }

        private static void BuiltBracket()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱", ";h=0.4");
            SignParts bracket = Build(instance, SupportCatalog.WallBracket);
            TextBoard text = instance.Board.Text;
            FramePlan frame = TextPlans.Peek(text.Job).Frame;
            float style = FrameStyle.For(0.4f).RailSize;
            Program.Check(bracket.Metal.Positions.Any(p => Math.Abs(p.Z - (bracket.Layout.BackZ + style)) < 1e-5f), "the frame's rails are built on a bracket");
            PickShape pick = bracket.Picks.Find(p => p.Name == "board");
            float cy = pick.CentreY, cx = pick.CentreX;
            float high = frame.Rails.Max(r => (r.A.Y + r.B.Y) * 0.5f);
            float barX = 0.3f * instance.Def.Width;
            // The flat bars (at the middle of the board's width plus and minus 0.3 of it) come down to the highest rail: their bottom is the rail's middle.
            Func<Vec3, bool> atBarBottom = p => Math.Abs(p.Y - (cy + high)) < 1e-4f && Math.Abs(p.Z) < 0.02f && Math.Abs(Math.Abs(p.X - cx) - barX) < 0.0126f;
            Program.Check(bracket.Metal.Positions.Any(atBarBottom), "the bars reach the highest rail");
            bool was = TextSupports.Enabled;
            try
            {
                TextSupports.Enabled = false;
                SignParts off = Build(instance, SupportCatalog.WallBracket);
                Program.Check(!off.Metal.Positions.Any(atBarBottom), "off: the bars end at the board's top");
            }
            finally
            {
                TextSupports.Enabled = was;
            }
        }

        private static void BuiltArc()
        {
            TextPlans.Clear();
            foreach (float radius in new[] { 12f, -12f, 5f })
            {
                SignInstance instance = Instance("文字灯箱", ";h=0.4;arc=" + radius.ToString(System.Globalization.CultureInfo.InvariantCulture));
                SignParts wall = Build(instance, SupportCatalog.WallFlat);
                TextBoard text = instance.Board.Text;
                FramePlan frame = TextPlans.Peek(text.Job).Frame;
                float cx = wall.Picks.Find(p => p.Name == "board").CentreX;
                float zFront = wall.Layout.BackZ + text.Reach;
                // The wall's circle: its centre at (cx, -R) in the sign's frame.
                Func<Vec3, float> ring = p => (float)Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Z + radius) * (p.Z + radius));
                MeshBuilder glow = wall.Solids[(int)text.Glow];
                float sag = Math.Abs(radius) - (float)Math.Sqrt(radius * radius - 0.5f * 0.5f * 4f);
                float lo = glow.Positions.Min(p => ring(p)), hi = glow.Positions.Max(p => ring(p));
                Program.Check(lo >= Math.Abs(radius + zFront) - 0.012f && hi <= Math.Abs(radius + zFront) + 0.012f, radius + ": the letters' faces are on the ring R + face: " + lo + " to " + hi + " against " + Math.Abs(radius + zFront));
                float zSpread = glow.Positions.Max(p => p.Z) - glow.Positions.Min(p => p.Z);
                Program.Check(zSpread > 0.015f, radius + ": the faces are not all in one plane: " + zSpread);
                // The spacers reach the wall's circle; nothing is behind it (a convex wall; a concave one is in front of the circle).
                int onWall = wall.Metal.Positions.Count(p => Math.Abs(ring(p) - Math.Abs(radius)) < 1e-3f);
                WallFramePlan plan = TextWallFrame.Plan(frame, FrameStyle.For(0.4f), TextMount.Auto);
                Program.Check(onWall >= plan.Standoffs.Count * 6, radius + ": the spacers reach the wall's surface: " + onWall + " for " + plan.Standoffs.Count);
                Program.Check(wall.Metal.Positions.All(p => radius > 0f ? ring(p) >= radius - 1e-3f : ring(p) <= -radius + 1e-3f), radius + ": nothing is in the wall");
                Program.Check(wall.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)), radius + ": finite");
            }

            // Flat is flat: the same letters as without a radius.
            SignInstance flat = Instance("文字灯箱", ";h=0.4");
            SignInstance wide = Instance("文字灯箱", ";h=0.4;arc=900");
            SignParts a = Build(flat, SupportCatalog.WallFlat), b = Build(wide, SupportCatalog.WallFlat);
            Program.Check(a.Metal.VertexCount == b.Metal.VertexCount && a.Solids[(int)flat.Board.Text.Glow].VertexCount == b.Solids[(int)wide.Board.Text.Glow].VertexCount, "a radius over 200 m is a flat wall");

            // The return and the face of the letters are on the same side: the middle of the two meshes is where the other's is, line of unequal length so that the text is not symmetric.
            foreach (float radius in new[] { 8f, -8f })
            {
                SignInstance left = Instance("文字灯箱文字" + NewLine + "字", ";h=0.4;align=left;arc=" + radius.ToString(System.Globalization.CultureInfo.InvariantCulture));
                SignParts leftParts = Build(left, SupportCatalog.WallFlat);
                MeshBuilder glowMesh = leftParts.Solids[(int)left.Board.Text.Glow], retMesh = leftParts.Solids[(int)left.Board.Text.Return];
                float glowX = glowMesh.Positions.Average(p => p.X), retX = retMesh.Positions.Average(p => p.X);
                Program.Check(Math.Abs(glowX - retX) < 0.06f, radius + ": the return is under its letters: " + glowX + " against " + retX);
            }
            SignParts onPost = Build(Instance("文字灯箱", ";h=0.4;arc=12"), SupportCatalog.SinglePole);
            Program.Check(onPost.Solids[(int)flat.Board.Text.Glow].Positions.Max(p => p.Z) - onPost.Solids[(int)flat.Board.Text.Glow].Positions.Min(p => p.Z) < 1e-3f, "a post is not curved by a radius");
        }
    }
}
