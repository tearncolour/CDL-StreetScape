using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The heavier frame of the standing light box (the owner, 2 Nov: "现在的三角支架太单薄"): sections that grow with the box, pads that do not meet, plates at the joints, a web and an X in a tall frame.</summary>
    internal static class StandHeavyTests
    {
        public static void RunAll()
        {
            Program.Run("stand frame: the sections grow with the box", Sections);
            Program.Run("stand frame: the pads do not meet, the beam is above them", Pads);
            Program.Run("stand frame: plates at the joints, in the frame's plane, behind the box's back", Gussets);
            Program.Run("stand frame: a web in a tall frame and an X in a taller one", WebAndX);
        }

        private static StandSpec Spec(float width, float height, float tilt = 10f, float lift = 0.3f, params float[] frames) =>
            new StandSpec { Width = width, Height = height, Depth = 0.2f, TiltDeg = tilt, Lift = lift, FrameX = frames.Length > 0 ? frames : new[] { 0f } };

        private static void Sections()
        {
            float last = 0f;
            foreach (float side in new[] { 0.3f, 0.6f, 1f, 1.6f, 2.5f, 4f, 7f })
            {
                StandPlan plan = StandPlanner.Plan(Spec(side, side * 0.6f));
                Program.Check(plan.PostRadius >= 0.06f - 1e-6f && plan.PostRadius <= 0.11f + 1e-6f, "the post is 120 to 220 mm across: " + plan.PostRadius * 2f);
                Program.Check(plan.PostRadius >= last - 1e-6f, "a larger box has a post that is not smaller: " + plan.PostRadius + " after " + last);
                last = plan.PostRadius;
                Program.Near(plan.Find(StandMemberKind.Strut, 0).Half, 0.8f * plan.PostRadius, 1e-5f, "the brace is four fifths of the post");
                Program.Near(plan.Find(StandMemberKind.Post, 0).Half, plan.PostRadius, 1e-6f, "the post member has the post's radius");
                Program.Check(plan.BeamHalf >= 0.04f - 1e-6f && plan.BeamHalf <= 0.07f + 1e-6f && Math.Abs(plan.Find(StandMemberKind.Beam, 0).Half - plan.BeamHalf) < 1e-6f, "the beam: " + plan.BeamHalf);
                Program.Check(plan.ArmHalf >= 0.03f - 1e-6f && plan.ArmHalf <= 0.05f + 1e-6f && Math.Abs(plan.Find(StandMemberKind.ArmTop, 0).Half - plan.ArmHalf) < 1e-6f, "the arms: " + plan.ArmHalf);
                Program.Check(plan.LegRadius >= 0.035f - 1e-6f && plan.Find(StandMemberKind.Leg, 0).Half == plan.LegRadius, "the leg: " + plan.LegRadius);
            }

            // Heavier than the first frame (0.12.76: post 80 to 120 mm, arms 40 mm, beam 60 mm, a tie 30 mm): every section of a 1 m box is larger.
            StandPlan one = StandPlanner.Plan(Spec(1f, 1f, 10f, 0.3f, -0.5f, 0.5f));
            Program.Check(one.PostRadius > 0.04f && one.ArmHalf > 0.02f && one.BeamHalf > 0.03f && one.TieRadius > 0.015f && one.CrossRadius > 0.012f && one.LegRadius > 0.025f, "every section of a metre box is heavier than the first frame's");
            Program.Check(StandPlanner.Plan(Spec(4f, 1.6f, 10f, 1f, -1.5f, 1.5f)).EstimatedKg > StandPlanner.Plan(Spec(1f, 1f, 10f, 0.3f, -0.5f, 0.5f)).EstimatedKg, "a larger box weighs more");
        }

        private static void Pads()
        {
            foreach (StandSpec spec in new[] { Spec(0.4f, 0.3f), Spec(7f, 0.5f, 10f, 0.3f, -3f, 0f, 3f), Spec(2f, 3f, 20f, 1.5f), Spec(7f, 2.2f, 30f, 0.3f, -3f, 0f, 3f) })
            {
                StandPlan plan = StandPlanner.Plan(spec);
                for (int i = 0; i < plan.FrameCount; i++)
                {
                    StandFoot front = plan.Feet[2 * i], rear = plan.Feet[2 * i + 1];
                    Program.Check(front.PadHalf >= 0.16f - 1e-6f && rear.PadHalf >= 0.12f - 1e-6f && front.PadHalf > rear.PadHalf, "the pads are as large as the first frame's or larger: " + front.PadHalf + ", " + rear.PadHalf);
                    Program.Check(front.PadHalf + rear.PadHalf + 0.05f <= plan.Depth2 + 1e-5f, $"the two pads do not meet ({spec.Width}x{spec.Height}): {front.PadHalf} + {rear.PadHalf} against {plan.Depth2}");
                    Program.Check(plan.MinBehind >= StandPlanner.MinDepth && plan.Depth2 >= plan.MinBehind - 1e-5f, "the brace's foot is not nearer than the pads allow");
                }

                if (plan.FrameCount > 1)
                {
                    float gap = plan.FrameX[1] - plan.FrameX[0];
                    Program.Check(gap > 2f * plan.Feet[0].PadHalf, "the pads of neighbouring frames do not meet either: " + gap);
                }
            }
        }

        private static void Gussets()
        {
            foreach (float tilt in new[] { 0f, 10f, 30f, 45f })
            {
                foreach (StandSpec spec in new[] { Spec(0.8f, 0.5f, tilt, 0.25f), Spec(2.4f, 1f, tilt, 0.5f, -1f, 0f, 1f), Spec(0.4f, 3f, tilt, 2f), Spec(7f, 2.2f, tilt, 0.3f, -3f, 0f, 3f) })
                {
                    StandPlan plan = StandPlanner.Plan(spec);
                    for (int i = 0; i < plan.FrameCount; i++)
                    {
                        List<StandGusset> mine = plan.Gussets.Where(g => g.Frame == i).ToList();
                        int arms = plan.Members.Count(m => m.Frame == i && (m.Kind == StandMemberKind.ArmTop || m.Kind == StandMemberKind.ArmLow));
                        Program.Check(mine.Count >= 3 && mine.Count <= 3 + arms, $"tilt {tilt}: three plates at the joints and a knee under an arm at most: {mine.Count} for {arms} arm(s)");
                        plan.Triangle(i, out Vec3 foot, out Vec3 node, out Vec3 rear);
                        foreach (Vec3 joint in new[] { foot, node, rear })
                            Program.Check(mine.Any(g => (g.A - joint).Length < 1e-5f), "a plate at each joint of the triangle");
                    }

                    foreach (StandGusset g in plan.Gussets)
                    {
                        Program.Check(Math.Abs(g.A.X - plan.FrameX[g.Frame]) < 1e-6f && Math.Abs(g.B.X - g.A.X) < 1e-6f && Math.Abs(g.C.X - g.A.X) < 1e-6f, "in the frame's plane");
                        float area = 0.5f * Math.Abs((g.B.Y - g.A.Y) * (g.C.Z - g.A.Z) - (g.B.Z - g.A.Z) * (g.C.Y - g.A.Y));
                        Program.Check(area > 7e-4f, $"a plate of a size, not a sliver (a knee is at least 0.04 m a side): {area:0.0000} m2");
                        foreach (Vec3 p in new[] { g.A, g.B, g.C })
                            Program.Check(plan.FromBackPlane(p.Z, p.Y) <= 1e-4f, $"tilt {tilt}: a plate's corner is behind the box's back plane: {plan.FromBackPlane(p.Z, p.Y):0.0000}");
                        Program.Check(Math.Min(g.A.Y, Math.Min(g.B.Y, g.C.Y)) >= plan.PlateTop + StandPlanner.LegHeight - 1e-5f - plan.BeamHalf, "none is down on the plates");
                    }
                }
            }

            // A plate is mass.
            StandPlan with = StandPlanner.Plan(Spec(1f, 1f));
            float gussetKg = with.Gussets.Sum(g => 0.5f * Math.Abs((g.B.Y - g.A.Y) * (g.C.Z - g.A.Z) - (g.B.Z - g.A.Z) * (g.C.Y - g.A.Y)) * StandPlanner.GussetThickness * 7850f);
            Program.Check(gussetKg > 0.5f && with.EstimatedKg > gussetKg, "the plates are in the mass: " + gussetKg + " of " + with.EstimatedKg + " kg");
        }

        private static void WebAndX()
        {
            StandPlan low = StandPlanner.Plan(Spec(1f, 0.6f, 10f, 0.3f));
            Program.Check(low.NodeY - low.BeamY < StandPlanner.WebRise && !low.Members.Exists(m => m.Kind == StandMemberKind.Web || m.Kind == StandMemberKind.Diag), "a low frame has no web");
            StandPlan medium = StandPlanner.Plan(Spec(1f, 1.6f, 10f, 0.4f));
            float rise = medium.NodeY - medium.BeamY;
            Program.Check(rise >= StandPlanner.WebRise && rise < StandPlanner.DiagRise, "the medium frame's rise: " + rise);
            Program.Check(medium.Members.Count(m => m.Kind == StandMemberKind.Web) == 1 && !medium.Members.Exists(m => m.Kind == StandMemberKind.Diag), "a web and no diagonals at a medium rise");
            StandPlan tall = StandPlanner.Plan(Spec(1f, 3f, 10f, 0.4f, -0.5f, 0.5f));
            Program.Check(tall.NodeY - tall.BeamY >= StandPlanner.DiagRise, "a tall frame's rise: " + (tall.NodeY - tall.BeamY));
            Program.Check(tall.Members.Count(m => m.Kind == StandMemberKind.Web) == 2 && tall.Members.Count(m => m.Kind == StandMemberKind.Diag) == 4, "two frames: a web each and an X each");
            for (int i = 0; i < tall.FrameCount; i++)
            {
                StandMember post = tall.Find(StandMemberKind.Post, i), brace = tall.Find(StandMemberKind.Strut, i), beam = tall.Find(StandMemberKind.Beam, i), web = tall.Find(StandMemberKind.Web, i);
                Program.Check(StandPlan.DistanceToSegment(web.A, post.A, post.B) < 1e-5f && StandPlan.DistanceToSegment(web.B, brace.A, brace.B) < 1e-5f && Math.Abs(web.A.Y - web.B.Y) < 1e-5f, "the web runs level from the post to the brace");
                Program.Check(Math.Abs(web.A.Y - (tall.BeamY + 0.5f * (tall.NodeY - tall.BeamY))) < 1e-5f, "at half the rise");
                List<StandMember> diags = tall.Members.Where(m => m.Kind == StandMemberKind.Diag && m.Frame == i).ToList();
                Program.Check(diags.Count == 2, "two diagonals");
                Program.Check(diags.Any(d => (d.A - web.A).Length < 1e-5f && (d.B - new Vec3(beam.A.X, beam.A.Y, beam.B.Z)).Length < 1e-5f), "one from the post's end of the web to the brace's foot");
                Program.Check(diags.Any(d => (d.A - web.B).Length < 1e-5f && (d.B - new Vec3(beam.A.X, beam.A.Y, 0f)).Length < 1e-5f), "one from the brace's end of the web to the post's foot");
                Program.Check(web.Half < brace.Half && diags.All(d => d.Half == web.Half), "lighter than the brace");
            }
        }
    }
}
