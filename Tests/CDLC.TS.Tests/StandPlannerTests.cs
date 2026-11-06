using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The frame of a standing light box (StandPlanner, 0.12.76): each frame's triangle closes at every tilt, the post is plumb and stands clear of the box's back plane, the brace and the beam are behind
    /// it, the arms reach the back, the feet stand on the ground, the ties and crossed braces end on the braces, the planner holds its inputs to range and says so, and the ground under the feet is
    /// weighed. Offline: the look, the real ground and the game are not.
    /// </summary>
    internal static class StandPlannerTests
    {
        public static void RunAll()
        {
            Program.Run("stand planner: every frame's triangle closes within a millimetre, at every tilt", Closure);
            Program.Run("stand planner: the post is plumb, the box's back is clear of it, the brace and the beam are behind", Clearance);
            Program.Run("stand planner: the arms reach the box's back, the top arm's end is in the back plate", Arms);
            Program.Run("stand planner: the feet stand on the ground and the beam is above it", Feet);
            Program.Run("stand planner: frames across the box, ties and crossed braces on the braces", Frames);
            Program.Run("stand planner: inputs held to range, and what is said of them", Ranges);
            Program.Run("stand planner: the ground under the feet", Ground);
            Program.Run("stand planner: mirror and the back line", Geometry);
        }

        private static StandSpec Spec(float width = 1.2f, float height = 0.8f, float depth = 0.2f, float tilt = 10f, float lift = 0.3f, params float[] frames) =>
            new StandSpec { Width = width, Height = height, Depth = depth, TiltDeg = tilt, Lift = lift, FrameX = frames.Length > 0 ? frames : new[] { 0f } };

        private static IEnumerable<StandSpec> Specs()
        {
            foreach (float tilt in new[] { 0f, 5f, 10f, 15f, 20f, 30f, 40f, 45f })
            {
                yield return Spec(0.8f, 0.5f, 0.15f, tilt, 0.25f);
                yield return Spec(1.2f, 0.8f, 0.2f, tilt, 0.3f, -0.5f, 0.5f);
                yield return Spec(2.4f, 1.0f, 0.25f, tilt, 0.5f, -1f, 0f, 1f);
                yield return Spec(4.0f, 1.6f, 0.3f, tilt, 1.0f, -1.5f, 1.5f);
                yield return Spec(0.4f, 3.0f, 0.2f, tilt, 2.0f);
                yield return Spec(7.0f, 2.2f, 0.4f, tilt, 0.3f, -3f, 0f, 3f);
            }
        }

        private static void Closure()
        {
            int frames = 0;
            foreach (StandSpec spec in Specs())
            {
                StandPlan plan = StandPlanner.Plan(spec);
                for (int i = 0; i < plan.FrameCount; i++)
                {
                    frames++;
                    float error = plan.ClosureError(i);
                    Program.Check(error <= 1e-3f, $"tilt {spec.TiltDeg} {spec.Width}x{spec.Height}: frame {i} closes within {error * 1000f:0.000} mm");
                    plan.Triangle(i, out Vec3 f, out Vec3 n, out Vec3 s);
                    float a = (n - f).Length, b = (s - f).Length, c = (n - s).Length;
                    Program.Check(a + b > c + 1e-3f && a + c > b + 1e-3f && b + c > a + 1e-3f && a > 0.15f && b > 0.2f, $"tilt {spec.TiltDeg}: the triangle is a triangle: {a:0.00}, {b:0.00}, {c:0.00} m");
                    StandMember brace = plan.Find(StandMemberKind.Strut, i);
                    Program.Check((brace.B - brace.A).Length > 0.25f, "the brace is a brace");
                }

                // The post's top is the box's top fraction, at the box's back: on the back line at TopFraction of the height up to the standoff.
                float along = StandPlanner.TopFraction * spec.Height;
                float yTop = plan.YA + along * (float)Math.Cos(plan.TiltRad);
                Program.Near(plan.PostTop, yTop, 1e-4f, "the post reaches the back line at the top fraction of the box's height");
                float away = -plan.FromBackPlane(0f, plan.PostTop);
                Program.Near(away, plan.PostRadius + StandPlanner.Clearance, 1e-3f, $"tilt {spec.TiltDeg}: the post's axis is its radius and the clearance from the back plane: {away:0.0000}");
            }

            Program.Check(frames >= 96, "many frames were checked: " + frames);
        }

        private static void Clearance()
        {
            foreach (StandSpec spec in Specs())
            {
                StandPlan plan = StandPlanner.Plan(spec);
                foreach (StandMember m in plan.Members)
                {
                    if (m.Kind == StandMemberKind.ArmLow || m.Kind == StandMemberKind.ArmTop)
                        continue;
                    float reach = m.Round ? m.Half : m.Half * 1.4143f;
                    for (int k = 0; k <= 20; k++)
                    {
                        Vec3 p = m.A + (m.B - m.A) * (k / 20f);
                        float from = plan.FromBackPlane(p.Z, p.Y);
                        Program.Check(from <= -reach + 1e-4f, $"tilt {spec.TiltDeg}: {m.Kind} is clear of the box's back plane by {-from - reach:0.0000} m");
                    }
                }

                StandMember post = plan.Find(StandMemberKind.Post, 0);
                Program.Check(post.A.X == post.B.X && post.A.Z == 0f && post.B.Z == 0f && post.B.Y > post.A.Y, "the post is plumb, at z = 0");
                StandMember brace = plan.Find(StandMemberKind.Strut, 0), beam = plan.Find(StandMemberKind.Beam, 0);
                Program.Check(brace.B.Z < -0.2f && brace.A.Z == 0f && brace.B.Y < brace.A.Y, "the brace goes down and back");
                Program.Check(beam.B.Z == brace.B.Z && beam.A.Z == 0f, "the beam runs from the post to the brace's foot");
                Program.Check(plan.Depth2 >= StandPlanner.MinDepth - 1e-5f && plan.Depth2 <= StandPlanner.MaxDepth + 1e-5f, "the brace's foot is as far behind as the limits say: " + plan.Depth2);
            }
        }

        private static void Arms()
        {
            foreach (StandSpec spec in Specs())
            {
                StandPlan plan = StandPlanner.Plan(spec);
                for (int i = 0; i < plan.FrameCount; i++)
                {
                    StandMember top = plan.Find(StandMemberKind.ArmTop, i);
                    float end = plan.FromBackPlane(top.B.Z, top.B.Y);
                    Program.Check(end >= 0f && end <= 0.05f, $"tilt {spec.TiltDeg}: the top arm ends inside the back plate: {end:0.0000} m past the plane");
                    Program.Check(top.A.Z == 0f && top.B.Z > 0f && top.A.Y == top.B.Y, "it is level and starts on the post's axis");
                    StandMember low = plan.Members.Find(m => m.Kind == StandMemberKind.ArmLow && m.Frame == i);
                    if (low.Half > 0f)
                    {
                        float lowEnd = plan.FromBackPlane(low.B.Z, low.B.Y);
                        Program.Check(lowEnd >= 0f && lowEnd <= 0.06f, $"tilt {spec.TiltDeg}: the low arm ends inside the back plate: {lowEnd:0.0000}");
                        Program.Check(low.B.Z >= top.B.Z - 1e-5f, "the low arm is as long as the top one or longer");
                        if (spec.TiltDeg >= 10f)
                            Program.Check(low.B.Z > top.B.Z + 0.01f, "and longer when the box leans: " + low.B.Z + " against " + top.B.Z);
                    }
                }
            }

            StandPlan small = StandPlanner.Plan(Spec(0.4f, 0.25f, 0.1f, 10f, 0.25f));
            Program.Check(!small.Members.Exists(m => m.Kind == StandMemberKind.ArmLow), "a very small box has the top arm only");
            StandPlan tall = StandPlanner.Plan(Spec(1f, 2f, 0.2f, 10f, 0.5f));
            Program.Check(tall.Members.Exists(m => m.Kind == StandMemberKind.ArmLow), "a tall box has both");
        }

        private static void Feet()
        {
            foreach (StandSpec spec in Specs())
            {
                StandPlan plan = StandPlanner.Plan(spec);
                Program.Check(plan.Feet.Count == 2 * plan.FrameCount, "two feet for each frame: " + plan.Feet.Count);
                for (int i = 0; i < plan.FrameCount; i++)
                {
                    StandFoot front = plan.Feet[2 * i], rear = plan.Feet[2 * i + 1];
                    Program.Check(!front.Rear && rear.Rear && front.Frame == i && rear.Frame == i && front.X == plan.FrameX[i] && rear.X == plan.FrameX[i], "front then rear, at the frame's x");
                    Program.Near(front.Z, 0f, 1e-6f, "the front foot is under the post");
                    Program.Near(rear.Z, -plan.Depth2, 1e-6f, "the rear foot is under the brace's foot");
                    Program.Check(front.PadHalf > 0.1f && rear.PadHalf > 0.08f, "pads of a size");
                    StandMember post = plan.Find(StandMemberKind.Post, i), leg = plan.Find(StandMemberKind.Leg, i), beam = plan.Find(StandMemberKind.Beam, i);
                    Program.Near(post.A.Y, plan.PlateTop, 1e-6f, "the post stands on the plate");
                    Program.Near(leg.A.Y, plan.PlateTop, 1e-6f, "the leg stands on the plate");
                    Program.Check(beam.A.Y - beam.Half >= plan.PlateTop + StandPlanner.LegHeight - 1e-5f, "the base beam clears the plates by the leg's height: " + (beam.A.Y - beam.Half));
                    Program.Near(leg.B.Y, beam.A.Y, 1e-6f, "the leg ends at the beam's axis");
                    Program.Check(plan.Members.All(m => Math.Min(m.A.Y, m.B.Y) >= plan.PlateTop - 1e-5f), "nothing is below the plates");
                }

                Program.Check(plan.Lift - plan.PlateTop >= 0.2f - 1e-5f, "the box's lower edge is clear of the plates: " + plan.Lift);
            }
        }

        private static void Frames()
        {
            StandPlan three = StandPlanner.Plan(Spec(4f, 2.2f, 0.25f, 15f, 0.5f, -1.5f, 0f, 1.5f));
            Program.Check(three.FrameCount == 3 && three.Members.Count(m => m.Kind == StandMemberKind.Post) == 3, "a frame at each x");
            Program.Check(three.Members.Count(m => m.Kind == StandMemberKind.Tie) == 2, "a tie between neighbours: " + three.Members.Count(m => m.Kind == StandMemberKind.Tie));
            Program.Check(three.Members.Count(m => m.Kind == StandMemberKind.Cross) == 4, "and two crossed braces in each gap of a wide or tall box: " + three.Members.Count(m => m.Kind == StandMemberKind.Cross));
            foreach (StandMember m in three.Members.Where(m => m.Kind == StandMemberKind.Tie || m.Kind == StandMemberKind.Cross))
            {
                Program.Check(StandPlan.DistanceToSegment(m.A, three.Find(StandMemberKind.Strut, m.Frame).A, three.Find(StandMemberKind.Strut, m.Frame).B) < 1e-4f
                    && StandPlan.DistanceToSegment(m.B, three.Find(StandMemberKind.Strut, m.Frame + 1).A, three.Find(StandMemberKind.Strut, m.Frame + 1).B) < 1e-4f, $"a {m.Kind} ends on the braces of its two frames");
                Program.Check(Math.Abs(m.A.X - three.FrameX[m.Frame]) < 1e-5f && Math.Abs(m.B.X - three.FrameX[m.Frame + 1]) < 1e-5f, "from one frame's x to the next");
            }

            foreach (StandMember tie in three.Members.Where(m => m.Kind == StandMemberKind.Tie))
                Program.Check(Math.Abs(tie.A.Y - tie.B.Y) < 1e-5f && Math.Abs(tie.A.Z - tie.B.Z) < 1e-5f, "a tie is level and square to the frames");
            foreach (StandMember cross in three.Members.Where(m => m.Kind == StandMemberKind.Cross))
                Program.Check(Math.Abs(cross.A.Y - cross.B.Y) > 0.05f, "a crossed brace is not level");
            StandMember[] pair = three.Members.Where(m => m.Kind == StandMemberKind.Cross && m.Frame == 0).ToArray();
            Program.Check(pair.Length == 2 && Math.Abs(pair[0].A.Y - pair[1].B.Y) < 1e-4f && Math.Abs(pair[0].B.Y - pair[1].A.Y) < 1e-4f, "the two cross each other (one's top is the other's foot height)");

            StandPlan small = StandPlanner.Plan(Spec(1.2f, 0.8f, 0.2f, 10f, 0.3f, -0.5f, 0.5f));
            Program.Check(small.Members.Count(m => m.Kind == StandMemberKind.Tie) == 1 && small.Members.Count(m => m.Kind == StandMemberKind.Cross) == 0, "a small box has a tie and no crossed braces");
            StandPlan one = StandPlanner.Plan(Spec(0.5f, 0.5f));
            Program.Check(one.FrameCount == 1 && !one.Members.Exists(m => m.Kind == StandMemberKind.Tie), "one frame has no ties");
            Program.Check(StandPlanner.Plan(new StandSpec { Width = 1f, Height = 1f, Depth = 0.2f, TiltDeg = 5f, Lift = 0.3f, FrameX = null }).FrameCount == 1
                && StandPlanner.Plan(new StandSpec { Width = 1f, Height = 1f, Depth = 0.2f, TiltDeg = 5f, Lift = 0.3f, FrameX = new float[0] }).FrameCount == 1, "no frame given: one, at x = 0");
            // The frames are copied: changing the caller's array later does not move the plan.
            float[] xs = { -1f, 1f };
            StandPlan copied = StandPlanner.Plan(Spec(2f, 1f, 0.2f, 5f, 0.3f, xs));
            xs[0] = 5f;
            Program.Check(copied.FrameX[0] == -1f, "the plan keeps its own copy of the frames' x");
        }

        private static void Ranges()
        {
            StandPlan high = StandPlanner.Plan(Spec(1f, 1f, 0.2f, 60f, 0.3f));
            Program.Check(high.TiltDeg == StandPlanner.MaxTiltDeg && high.Warnings.Exists(w => w.Kind == StandWarningKind.TiltClamped && w.Value == 60f), "a tilt over the range is held to it, with a warning");
            StandPlan negative = StandPlanner.Plan(Spec(1f, 1f, 0.2f, -5f, 0.3f));
            Program.Check(negative.TiltDeg == -5f && !negative.Warnings.Exists(w => w.Kind == StandWarningKind.TiltClamped), "a small negative tilt is a depression, not clamped");
            StandPlan dipped = StandPlanner.Plan(Spec(1f, 1f, 0.2f, -60f, 0.3f));
            Program.Check(dipped.TiltDeg == -StandPlanner.MaxTiltDeg && dipped.Warnings.Exists(w => w.Kind == StandWarningKind.TiltClamped && w.Value == -60f), "a depression over the range is held to it, with a warning");
            StandPlan nan = StandPlanner.Plan(Spec(1f, 1f, 0.2f, float.NaN, float.NaN));
            Program.Check(nan.TiltDeg == 0f && nan.Lift >= StandPlanner.MinLift && nan.Members.All(m => (m.A + m.B).IsFinite), "NaN is no tilt and the least lift, and every number is finite");
            StandPlan inRange = StandPlanner.Plan(Spec(1f, 1f, 0.2f, 20f, 0.5f));
            Program.Check(inRange.Warnings.Count == 0, "nothing is said of a box in range: " + inRange.Warnings.Count);
            Program.Check(StandPlanner.Plan(Spec(1f, 1f, 0.2f, 0f, 9f)).Lift == StandPlanner.MaxLift, "the lift is held to its greatest");

            // A short box low on the ground has no room for the brace under the post's top: raised, and said.
            StandPlan raised = StandPlanner.Plan(Spec(0.5f, 0.2f, 0.1f, 10f, 0.25f));
            Program.Check(raised.Warnings.Exists(w => w.Kind == StandWarningKind.LiftRaised) && raised.Lift > 0.25f, "a short box is raised: " + raised.Lift);
            Program.Check(raised.NodeY - raised.BeamY >= 0.2f - 1e-5f, "so that the brace has a rise of at least 0.2 m: " + (raised.NodeY - raised.BeamY));
            Program.Check(!StandPlanner.Plan(Spec(1f, 1f, 0.2f, 10f, 0.3f)).Warnings.Exists(w => w.Kind == StandWarningKind.LiftRaised), "a box that has the room is not raised");

            StandPlan tall = StandPlanner.Plan(Spec(1f, 3.5f, 0.2f, 10f, 2f));
            Program.Check(tall.Warnings.Exists(w => w.Kind == StandWarningKind.TooTall && w.Value > StandPlanner.TallWarning), "a box over 5 m is said to be tall");
            StandPlan shallow = StandPlanner.Plan(Spec(1f, 0.6f, 0.2f, 10f, 0.3f));
            StandPlan deep = StandPlanner.Plan(Spec(1f, 5f, 0.2f, 10f, 2f));
            Program.Check(shallow.MinBehind >= StandPlanner.MinDepth && Math.Abs(shallow.Depth2 - shallow.MinBehind) < 1e-5f && Math.Abs(deep.Depth2 - StandPlanner.MaxDepth) < 1e-5f, $"the brace's foot is held between {StandPlanner.MinDepth} and {StandPlanner.MaxDepth} m behind the post: {shallow.Depth2}, {deep.Depth2}");
            Program.Check(StandPlanner.Plan(Spec(0f, 0f, 0f)).Members.Count > 0 && StandPlanner.Plan(Spec(0.5f, 0.5f)).EstimatedKg > 0f, "a zero size is held to a least one and the mass is positive");
            StandPlan oneBox = StandPlanner.Plan(Spec(1f, 1f, 0.2f, 10f, 0.3f));
            float sheet = (1f * 1f * 2f + 2f * (1f + 1f) * 0.2f) * 11.8f;
            Program.Check(oneBox.EstimatedKg > sheet + 5f && oneBox.EstimatedKg < sheet + 120f, $"a metre square box weighs its sheet ({sheet:0} kg) and a frame's steel: {oneBox.EstimatedKg:0} kg");
            Program.Check(StandPlanner.Plan(Spec(2f, 1f)).EstimatedKg > StandPlanner.Plan(Spec(1f, 1f)).EstimatedKg && StandPlanner.Plan(Spec(1f, 2f)).EstimatedKg > StandPlanner.Plan(Spec(1f, 1f)).EstimatedKg, "a larger box weighs more");
        }

        private static void Ground()
        {
            StandPlan plan = StandPlanner.Plan(Spec(2f, 1f, 0.2f, 10f, 0.3f, -0.8f, 0.8f));
            Program.Check(StandPlanner.FootWarnings(plan, new float[] { 0f, 0.1f, -0.2f, 0.3f }).Count == 0, "the ground within half a metre is not remarked on");
            List<StandWarning> far = StandPlanner.FootWarnings(plan, new float[] { 0f, 0f, 0.6f, 0f });
            Program.Check(far.Count(w => w.Kind == StandWarningKind.FootOff) == 1 && far.First(w => w.Kind == StandWarningKind.FootOff).Foot == 2, "a foot that is further than half a metre from the ground it was drawn for");
            Program.Check(far.Exists(w => w.Kind == StandWarningKind.FrameSkew && w.Foot == 2 && Math.Abs(w.Value - 0.6f) < 1e-5f), "and the frame whose feet differ that much: " + far.Count);
            List<StandWarning> down = StandPlanner.FootWarnings(plan, new float[] { -0.6f, 0f, 0f, 0f });
            Program.Check(down.Exists(w => w.Kind == StandWarningKind.FootOff && w.Foot == 0 && w.Value < 0f), "below, the same");
            List<StandWarning> skew = StandPlanner.FootWarnings(plan, new float[] { 0.3f, -0.3f, 0f, 0f });
            Program.Check(skew.Count == 1 && skew[0].Kind == StandWarningKind.FrameSkew, "a frame whose feet differ by 0.6 m with neither over half a metre: the skew alone");
            Program.Check(StandPlanner.FootWarnings(plan, new float[] { float.NaN, 0f, 0f, 0.4f }).Count == 0, "a foot whose ground is not known is not judged");
            Program.Check(StandPlanner.FootWarnings(plan, new float[] { 0f, 0f, 0.5f, 0f }).Count == 0 && StandPlanner.FootWarnings(plan, new float[] { 0f, 0f, 0.5001f, 0f }).Count == 2, "half a metre is not too much, a little more is: the foot, and its frame's two feet");
            Program.Check(StandPlanner.FootWarnings(plan, new float[] { 0f, -0.3f, 0f, 0f }).Count == 0 && StandPlanner.FootWarnings(plan, new float[] { 0.3f, 0f, 0f, -0.3f }).Count == 0, "feet in different frames are not compared with each other");
            Program.Check(StandPlanner.FootWarnings(null, new float[] { 1f }).Count == 0 && StandPlanner.FootWarnings(plan, null).Count == 0 && StandPlanner.FootWarnings(plan, new float[0]).Count == 0, "nothing given: nothing said");
            Program.Check(StandPlanner.FootWarnings(plan, new float[] { 0.2f }).Count == 0, "fewer offsets than feet: what is there is judged");
        }

        private static void Geometry()
        {
            StandPlan plan = StandPlanner.Plan(Spec(2f, 1f, 0.2f, 20f, 0.4f, -0.8f, 0.8f));
            Program.Near(plan.BackZAt(plan.YA), plan.ZA, 1e-6f, "the back line starts at A");
            Program.Near(plan.FromBackPlane(plan.ZA, plan.YA), 0f, 1e-6f, "A is on the back plane");
            float ty = plan.YA + 0.6f * (float)Math.Cos(plan.TiltRad);
            Program.Near(plan.FromBackPlane(plan.BackZAt(ty), ty), 0f, 1e-5f, "a point of the back line is on the plane");
            Program.Check(plan.FromBackPlane(plan.ZA + 0.1f, plan.YA) > 0f && plan.FromBackPlane(plan.ZA - 0.1f, plan.YA) < 0f, "toward the face is positive, behind the back negative");
            Program.Near(plan.BackZAt(plan.YA + 1f) - plan.ZA, -(float)Math.Tan(plan.TiltRad), 1e-5f, "the back line leans back by the tilt");

            // Mirror: the frames at -x and +x are mirror images, whatever is in between.
            StandPlan mirrored = StandPlanner.Plan(Spec(3f, 1.2f, 0.2f, 25f, 0.4f, -1.2f, 0f, 1.2f));
            for (StandMemberKind kind = StandMemberKind.Post; kind <= StandMemberKind.ArmTop; kind++)
            {
                if (!mirrored.Members.Exists(m => m.Kind == kind && m.Frame == 0))
                    continue;
                StandMember a = mirrored.Find(kind, 0), b = mirrored.Find(kind, 2);
                Program.Check(Math.Abs(a.A.X + b.A.X) < 1e-5f && Math.Abs(a.B.X + b.B.X) < 1e-5f && a.A.Y == b.A.Y && a.B.Y == b.B.Y && a.A.Z == b.A.Z && a.B.Z == b.B.Z && a.Half == b.Half, $"the {kind} of the first and the last frame are mirror images");
            }

            StandPlan again = StandPlanner.Plan(Spec(3f, 1.2f, 0.2f, 25f, 0.4f, -1.2f, 0f, 1.2f));
            Program.Check(again.Members.Count == mirrored.Members.Count && again.EstimatedKg == mirrored.EstimatedKg && again.Depth2 == mirrored.Depth2 && again.PostTop == mirrored.PostTop, "the same input, the same plan");
        }
    }
}
