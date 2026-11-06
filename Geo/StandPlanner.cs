using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    public enum StandMemberKind
    {
        Post,
        Leg,
        Beam,
        Strut,
        ArmLow,
        ArmTop,
        Tie,
        Cross,
        Web,
        Diag
    }

    /// <summary>One piece of the stand's frame: a straight member from A to B (the sign's frame: x across, y up, +z the way the box faces), round (a tube of radius Half) or square (a box of half-width Half).</summary>
    public readonly struct StandMember
    {
        public readonly StandMemberKind Kind;
        public readonly Vec3 A, B;
        public readonly float Half;
        public readonly bool Round;
        public readonly int Frame;

        public StandMember(StandMemberKind kind, Vec3 a, Vec3 b, float half, bool round, int frame)
        {
            Kind = kind;
            A = a;
            B = b;
            Half = half;
            Round = round;
            Frame = frame;
        }
    }

    /// <summary>A foot of the stand: where a pad stands on the ground (x across, z along the facing; negative z is behind the post) and how far the pad reaches each way.</summary>
    public readonly struct StandFoot
    {
        public readonly float X, Z, PadHalf;
        public readonly bool Rear;
        public readonly int Frame;

        public StandFoot(float x, float z, float padHalf, bool rear, int frame)
        {
            X = x;
            Z = z;
            PadHalf = padHalf;
            Rear = rear;
            Frame = frame;
        }
    }

    /// <summary>A triangular plate in a frame's plane (the three corners as the side view has them: y up, z toward the box) at the frame's x, GussetThickness thick: where the post, the brace and the beam meet, and under an arm.</summary>
    public readonly struct StandGusset
    {
        public readonly int Frame;
        public readonly Vec3 A, B, C;

        public StandGusset(int frame, Vec3 a, Vec3 b, Vec3 c)
        {
            Frame = frame;
            A = a;
            B = b;
            C = c;
        }
    }

    public enum StandWarningKind
    {
        /// <summary>The tilt asked for was outside -MaxTiltDeg to MaxTiltDeg and was held to it.</summary>
        TiltClamped,

        /// <summary>The box's lower edge was raised so that the post and its brace have room (Value: the new height).</summary>
        LiftRaised,

        /// <summary>The top of the box is higher than TallWarning (Value: the height).</summary>
        TooTall,

        /// <summary>The box looks down and its top edge stands out further than DipReachWarning in front of the post (Value: how far).</summary>
        DipReach,

        /// <summary>A foot's ground is further from the one the stand was drawn for than FootLimit (Value: the offset, Foot: which).</summary>
        FootOff,

        /// <summary>The two feet of a frame stand at heights that differ by more than FootLimit: the brace leans another way (Value: the difference, Foot: the frame's front foot).</summary>
        FrameSkew
    }

    public readonly struct StandWarning
    {
        public readonly StandWarningKind Kind;
        public readonly float Value;
        public readonly int Foot;

        public StandWarning(StandWarningKind kind, float value, int foot = -1)
        {
            Kind = kind;
            Value = value;
            Foot = foot;
        }
    }

    /// <summary>What the stand is made for: the box's outer size, its depth, the tilt, the height of its lower edge and where the frames stand across it.</summary>
    public struct StandSpec
    {
        public float Width, Height, Depth, TiltDeg, Lift;

        /// <summary>
        /// For a board that is not a housing (the text light box's rails): the share of its height, along its plane, at which the top arm and the low arm meet it (0: as for a housing, the post reaches
        /// TopFraction and the low arm is at LowFraction); and how far behind the board's back plane the arms land (the depth of the channel they carry: 0 for a housing).
        /// </summary>
        public float ArmShare, LowShare, ArmBehind;

        /// <summary>The board is no housing (letters on rails): the plan's mass is the frame's alone.</summary>
        public bool Open;

        /// <summary>The x of each frame (the support's posts); at least one.</summary>
        public float[] FrameX;
    }

    /// <summary>
    /// The frame of a standing light box (立式灯箱, 0.12.76): a box that leans back by the tilt about its lower back edge, held by one triangular frame at each post of the support: a vertical post behind
    /// the box's back, a brace from just under the post's top down and back to a foot behind, a base beam from the post's foot to the brace's foot, which with the post and the brace closes the triangle;
    /// two arms from the post to the box's back (a short one at the top, a longer one lower, longer as the tilt is larger); ties and crossed braces between the braces of neighbouring frames. Pure. The
    /// numbers (sizes, the share of the box's height the post reaches, the mass) are this mod's design and estimates, not a structural calculation or a standard.
    /// <para>
    /// The side view: z across the page (+z toward the viewer of the box), y up. The box's lower back edge A is at (ZA, Lift); its back line leans back by the tilt from A: up and toward -z. The post is
    /// at z = 0 and reaches the back line at TopFraction of the box's height, a little short of it (the standoff, so that the post's skin is Clearance from the back plane whatever the tilt).
    /// </para>
    /// </summary>
    public sealed class StandPlan
    {
        public float TiltRad, TiltDeg, Lift, Width, Height, Depth;

        /// <summary>The board is no housing: its weight is not in EstimatedKg.</summary>
        public bool Open;

        /// <summary>The lower back edge of the box: its z and y (the box turns about it).</summary>
        public float ZA, YA;

        /// <summary>Radius of the post, the post's top, where the brace joins the post, the height of the base beam's axis, the top of the pads' plates.</summary>
        public float PostRadius, PostTop, NodeY, BeamY, PlateTop;

        /// <summary>The sections that grow with the box: the base beam's and the arms' half side, the leg's, the ties' and the crossed braces' radius (m).</summary>
        public float BeamHalf, ArmHalf, LegRadius, TieRadius, CrossRadius;

        /// <summary>The least the brace's foot may stand behind the post for the two pads not to meet.</summary>
        public float MinBehind;

        /// <summary>How far behind the post the brace's foot stands.</summary>
        public float Depth2;

        /// <summary>The x of each frame, in the order given.</summary>
        public float[] FrameX;

        public readonly List<StandMember> Members = new List<StandMember>();
        public readonly List<StandFoot> Feet = new List<StandFoot>();
        public readonly List<StandGusset> Gussets = new List<StandGusset>();
        public readonly List<StandWarning> Warnings = new List<StandWarning>();

        /// <summary>About what the steel of the box and the frame weighs (kg): an estimate of this mod (a sheet of 1.5 mm for the box, tubes of 3 mm wall), not a figure of a maker.</summary>
        public float EstimatedKg;

        /// <summary>The z of the box's back line at a height (the back line leans back from A as the tilt says).</summary>
        public float BackZAt(float y) => ZA - (y - YA) * (float)Math.Tan(TiltRad);

        /// <summary>The signed distance (m) of a point in the side view from the box's back plane: positive toward the face (inside the box), negative behind the back.</summary>
        public float FromBackPlane(float z, float y) => (z - ZA) * (float)Math.Cos(TiltRad) + (y - YA) * (float)Math.Sin(TiltRad);

        public int FrameCount => FrameX.Length;

        /// <summary>The three corners of the frame's triangle in the side view: where the post meets the base beam, where the brace meets the post, where the brace meets the beam's rear end.</summary>
        public void Triangle(int frame, out Vec3 postFoot, out Vec3 postNode, out Vec3 braceFoot)
        {
            float x = FrameX[frame];
            postFoot = new Vec3(x, BeamY, 0f);
            postNode = new Vec3(x, NodeY, 0f);
            braceFoot = new Vec3(x, BeamY, -Depth2);
        }

        /// <summary>How far the three joints of a frame are from where the frame says they are, the largest of them (m): the post's axis through the beam's front end and the brace's top; the brace's lower end on the beam's rear end.</summary>
        public float ClosureError(int frame)
        {
            Triangle(frame, out Vec3 f, out Vec3 n, out Vec3 s);
            StandMember post = Find(StandMemberKind.Post, frame), beam = Find(StandMemberKind.Beam, frame), brace = Find(StandMemberKind.Strut, frame);
            float e1 = DistanceToSegment(beam.A, post.A, post.B);
            float e2 = DistanceToSegment(brace.A, post.A, post.B);
            float e3 = (brace.B - new Vec3(beam.B.X, beam.B.Y, -Depth2)).Length;
            float e4 = (brace.A - n).Length;
            float e5 = (beam.A - f).Length;
            return Math.Max(Math.Max(e1, e2), Math.Max(e3, Math.Max(e4, e5)));
        }

        public StandMember Find(StandMemberKind kind, int frame)
        {
            foreach (StandMember m in Members)
            {
                if (m.Kind == kind && m.Frame == frame)
                    return m;
            }

            throw new InvalidOperationException("no member of that kind in the frame");
        }

        public static float DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 ab = b - a;
            float l2 = Vec3.Dot(ab, ab);
            float t = l2 < 1e-12f ? 0f : Math.Max(0f, Math.Min(1f, Vec3.Dot(p - a, ab) / l2));
            return (p - (a + ab * t)).Length;
        }
    }

    public static class StandPlanner
    {
        public const float MaxTiltDeg = 45f, MinLift = 0.25f, MaxLift = 2f, TallWarning = 5f, FootLimit = 0.5f;

        /// <summary>How far (m) the top edge of a box that looks down may stand out in front of the post before it is warned of.</summary>
        public const float DipReachWarning = 1.5f;

        /// <summary>The share of the box's height, along its back, at which the post's top meets it, and at which the lower arm does.</summary>
        public const float TopFraction = 0.8f, LowFraction = 0.2f;

        /// <summary>The air between the post's skin and the box's back plane (m), measured square to the plane.</summary>
        public const float Clearance = 0.015f;

        public const float PlateTop = 0.04f, LegHeight = 0.10f, GussetThickness = 0.012f;

        /// <summary>The frame's rise (m) from which it has a web across it, and from which the web has an X of two diagonals.</summary>
        public const float WebRise = 1.2f, DiagRise = 1.8f;

        /// <summary>The brace foot's distance behind the post as a share of the brace's height, and its least and greatest (the ground profile has rows to 0.8 m behind; a foot further back takes the last row's ground).</summary>
        public const float DepthShare = 0.45f, MinDepth = 0.25f, MaxDepth = 1.5f;

        public static StandPlan Plan(StandSpec spec)
        {
            var plan = new StandPlan();
            float tiltDeg = float.IsNaN(spec.TiltDeg) ? 0f : spec.TiltDeg;
            if (tiltDeg < -MaxTiltDeg || tiltDeg > MaxTiltDeg)
                plan.Warnings.Add(new StandWarning(StandWarningKind.TiltClamped, tiltDeg));
            tiltDeg = Math.Max(-MaxTiltDeg, Math.Min(MaxTiltDeg, tiltDeg));
            float tilt = tiltDeg * (float)Math.PI / 180f;
            float sin = (float)Math.Sin(tilt), cos = (float)Math.Cos(tilt), tan = (float)Math.Tan(tilt);
            float width = Math.Max(0.1f, spec.Width), height = Math.Max(0.1f, spec.Height), depth = Math.Max(0.02f, spec.Depth);
            float[] frames = spec.FrameX != null && spec.FrameX.Length > 0 ? (float[])spec.FrameX.Clone() : new[] { 0f };

            // The sections grow with the box (this mod's design: a roadside pylon's posts are 120 to 220 mm, its braces about four fifths of that, the arms and the beam a square tube of 60 to 140 mm).
            float area = width * height;
            float r = Math.Max(0.06f, Math.Min(0.11f, 0.05f + 0.02f * (float)Math.Sqrt(area)));
            float braceR = 0.8f * r, legR = Math.Max(0.035f, 0.5f * r);
            float beamHalf = Math.Max(0.04f, Math.Min(0.07f, 0.6f * r)), armHalf = Math.Max(0.03f, Math.Min(0.05f, 0.45f * r));
            // An arm to a board on rails ends in the slide channel behind a rail, which is 68 mm across: it is no wider than that.
            if (spec.Open)
                armHalf = Math.Min(armHalf, 0.032f);
            float padFront = Math.Max(0.16f, 2.4f * r), padRear = Math.Max(0.12f, 1.8f * r);
            float standoff = (r + Clearance) / cos;
            float beamY = PlateTop + LegHeight + beamHalf;
            bool armed = spec.ArmShare > 0f;
            float armShare = armed ? Math.Max(0.3f, Math.Min(0.99f, spec.ArmShare)) : TopFraction;
            float lowShare = spec.LowShare > 0f ? Math.Max(0.02f, Math.Min(armShare - 0.1f, spec.LowShare)) : LowFraction;
            float armBehind = Math.Max(0f, Math.Min(0.1f, spec.ArmBehind));
            // How far the post's top is above the board's lower back edge: a housing's post reaches TopFraction of its height; for a board on rails the top arm sits at the share asked for.
            float reach = armed ? armShare * height * cos + armHalf + 0.01f : TopFraction * height * cos;

            // The post must have room for the brace under its top: the box is raised when it is too low to give it (the node sits under the top, the brace needs a rise to be one).
            float lift = Math.Max(MinLift, Math.Min(MaxLift, float.IsNaN(spec.Lift) ? MinLift : spec.Lift));
            float needTop = beamY + 0.30f;
            float lowest = needTop - reach;
            if (lift < lowest)
            {
                lift = lowest;
                plan.Warnings.Add(new StandWarning(StandWarningKind.LiftRaised, lift));
            }

            float postTop = lift + reach;
            // A box that leans back has its back line pass the post at the height the post reaches; one that looks down leans forward, so the post stands behind the whole of it, at the lower back edge:
            // the arms reach out to the back line from there (the longer the higher), and the box's top edge stands out in front of the post.
            float zA = standoff + Math.Max(0f, reach * tan);
            float nodeY = postTop - Math.Max(0.08f, 1.2f * braceR);
            float rise = nodeY - beamY;
            float minBehind = Math.Max(MinDepth, padFront + padRear + 0.06f);
            float behind = Math.Max(minBehind, Math.Min(MaxDepth, DepthShare * rise));

            plan.TiltDeg = tiltDeg;
            plan.TiltRad = tilt;
            plan.Lift = lift;
            plan.Width = width;
            plan.Height = height;
            plan.Depth = depth;
            plan.Open = spec.Open;
            plan.ZA = zA;
            plan.YA = lift;
            plan.PostRadius = r;
            plan.PostTop = postTop;
            plan.NodeY = nodeY;
            plan.BeamY = beamY;
            plan.PlateTop = PlateTop;
            plan.Depth2 = behind;
            plan.MinBehind = minBehind;
            plan.BeamHalf = beamHalf;
            plan.ArmHalf = armHalf;
            plan.LegRadius = legR;
            plan.TieRadius = Math.Max(0.02f, 0.4f * r);
            plan.CrossRadius = Math.Max(0.016f, 0.3f * r);
            plan.FrameX = frames;

            float topY = lift + height * cos;
            if (topY > TallWarning)
                plan.Warnings.Add(new StandWarning(StandWarningKind.TooTall, topY));
            float topReach = zA - height * sin;
            if (topReach > DipReachWarning)
                plan.Warnings.Add(new StandWarning(StandWarningKind.DipReach, topReach));

            float topArmY = armed ? lift + armShare * height * cos : postTop - armHalf - 0.01f;
            float lowY = lift + lowShare * height * cos;
            bool lowArm = lowY - armHalf > beamY + 0.12f && lowY < nodeY - 0.12f && topArmY - lowY > 0.15f;
            float webR = Math.Max(0.02f, 0.45f * braceR);
            for (int i = 0; i < frames.Length; i++)
            {
                float x = frames[i];
                plan.Members.Add(new StandMember(StandMemberKind.Post, new Vec3(x, PlateTop, 0f), new Vec3(x, postTop, 0f), r, true, i));
                plan.Members.Add(new StandMember(StandMemberKind.Leg, new Vec3(x, PlateTop, -behind), new Vec3(x, beamY, -behind), legR, true, i));
                plan.Members.Add(new StandMember(StandMemberKind.Beam, new Vec3(x, beamY, 0f), new Vec3(x, beamY, -behind), beamHalf, false, i));
                plan.Members.Add(new StandMember(StandMemberKind.Strut, new Vec3(x, nodeY, 0f), new Vec3(x, beamY, -behind), braceR, true, i));
                plan.Members.Add(Arm(plan, StandMemberKind.ArmTop, x, topArmY, tan, armBehind, cos, i));
                if (lowArm)
                    plan.Members.Add(Arm(plan, StandMemberKind.ArmLow, x, lowY, tan, armBehind, cos, i));
                plan.Feet.Add(new StandFoot(x, 0f, padFront, false, i));
                plan.Feet.Add(new StandFoot(x, -behind, padRear, true, i));
                Gussets(plan, i, x, topArmY, plan.Find(StandMemberKind.ArmTop, i).B.Z, lowArm ? lowY : float.NaN, lowArm ? plan.Find(StandMemberKind.ArmLow, i).B.Z : 0f);
                if (rise >= WebRise)
                {
                    // A web across the triangle at half its rise; from a rise of DiagRise two diagonals cross in the panel under it (an X).
                    float yMid = beamY + 0.5f * rise, along = (nodeY - yMid) / rise;
                    var mid = new Vec3(x, yMid, 0f);
                    var onBrace = new Vec3(x, yMid, -behind * (1f - along));
                    plan.Members.Add(new StandMember(StandMemberKind.Web, mid, onBrace, webR, true, i));
                    if (rise >= DiagRise)
                    {
                        plan.Members.Add(new StandMember(StandMemberKind.Diag, mid, new Vec3(x, beamY, -behind), webR, true, i));
                        plan.Members.Add(new StandMember(StandMemberKind.Diag, onBrace, new Vec3(x, beamY, 0f), webR, true, i));
                    }
                }
            }

            bool braced = width > 3f || height > 2f;
            for (int i = 0; i + 1 < frames.Length; i++)
            {
                Vec3 a = BraceAt(plan, i, 0.5f), b = BraceAt(plan, i + 1, 0.5f);
                plan.Members.Add(new StandMember(StandMemberKind.Tie, a, b, plan.TieRadius, true, i));
                if (braced)
                {
                    plan.Members.Add(new StandMember(StandMemberKind.Cross, BraceAt(plan, i, 0.25f), BraceAt(plan, i + 1, 0.75f), plan.CrossRadius, true, i));
                    plan.Members.Add(new StandMember(StandMemberKind.Cross, BraceAt(plan, i, 0.75f), BraceAt(plan, i + 1, 0.25f), plan.CrossRadius, true, i));
                }
            }

            plan.EstimatedKg = Mass(plan);
            return plan;
        }

        /// <summary>
        /// An arm from the post to the box's back at a height: it ends a little inside the box's back so that it is fixed to it whatever the tilt (behind: how far behind the back plane the thing it holds
        /// to is, a channel's depth for a board on rails, measured square to the plane).
        /// </summary>
        private static StandMember Arm(StandPlan plan, StandMemberKind kind, float x, float y, float tan, float behind, float cos, int frame)
        {
            // The back line is furthest forward at the arm's lower edge when the box leans back and at its upper edge when it looks down: the arm ends 6 mm inside the line at that edge.
            float end = plan.BackZAt(y) + plan.ArmHalf * Math.Abs(tan) + 0.006f - behind / cos;
            return new StandMember(kind, new Vec3(x, y, 0f), new Vec3(x, y, end), plan.ArmHalf, false, frame);
        }

        /// <summary>
        /// The plates of a frame's joints (a quarter of a metre of each member from the joint at most, less in a small frame): where the brace meets the post, where the post meets the base beam, where
        /// the brace meets the beam's rear end, and a knee under each arm.
        /// </summary>
        private static void Gussets(StandPlan plan, int frame, float x, float topArmY, float topArmEnd, float lowArmY, float lowArmEnd)
        {
            float rise = plan.NodeY - plan.BeamY, behind = plan.Depth2;
            float braceLength = (float)Math.Sqrt(rise * rise + behind * behind);
            Vec3 down = new Vec3(0f, -1f, 0f), up = new Vec3(0f, 1f, 0f), back = new Vec3(0f, 0f, -1f), front = new Vec3(0f, 0f, 1f);
            Vec3 along = new Vec3(0f, -rise / braceLength, -behind / braceLength), upBrace = new Vec3(0f, rise / braceLength, behind / braceLength);
            float s = Math.Min(0.25f, Math.Max(0.1f, 0.22f * Math.Min(rise, braceLength)));
            Vec3 node = new Vec3(x, plan.NodeY, 0f), foot = new Vec3(x, plan.BeamY, 0f), rear = new Vec3(x, plan.BeamY, -behind);
            plan.Gussets.Add(new StandGusset(frame, node, node + down * s, node + along * s));
            plan.Gussets.Add(new StandGusset(frame, foot, foot + up * s, foot + back * Math.Min(s, 0.4f * behind)));
            plan.Gussets.Add(new StandGusset(frame, rear, rear + front * Math.Min(s, 0.4f * behind), rear + upBrace * s));
            // A knee under an arm is as long as the arm allows (it stays short of the arm's end, which is inside the box's back plate) and at most as long as the post is thick, twice over.
            foreach ((float y, float end) in new[] { (topArmY, topArmEnd), (lowArmY, lowArmEnd) })
            {
                float knee = Math.Min(Math.Max(0.1f, 1.6f * plan.PostRadius), 0.6f * (end - plan.PostRadius));
                if (float.IsNaN(y) || knee < 0.04f)
                    continue;
                var corner = new Vec3(x, y - plan.ArmHalf, plan.PostRadius);
                plan.Gussets.Add(new StandGusset(frame, corner, corner + down * knee, corner + front * knee));
            }
        }

        /// <summary>The point of a frame's brace at a share of its length from the top (0) to the foot (1).</summary>
        public static Vec3 BraceAt(StandPlan plan, int frame, float share)
        {
            StandMember brace = plan.Find(StandMemberKind.Strut, frame);
            return brace.A + (brace.B - brace.A) * share;
        }

        private static float Mass(StandPlan plan)
        {
            // The box: sheet of 1.5 mm, 7,850 kg/m3: 11.8 kg per m2 of surface (both faces of the front are one: a lit panel of acrylic is counted as the same). The tubes: 3 mm wall.
            float surface = plan.Width * plan.Height * 2f + 2f * (plan.Width + plan.Height) * plan.Depth;
            float kg = plan.Open ? 0f : surface * 11.8f;
            // The plates at the joints (12 mm steel) and the base plates (40 mm of the pad's square: the pads are feet of the frame).
            foreach (StandGusset g in plan.Gussets)
                kg += 0.5f * Math.Abs((g.B.Y - g.A.Y) * (g.C.Z - g.A.Z) - (g.B.Z - g.A.Z) * (g.C.Y - g.A.Y)) * GussetThickness * 7850f;
            foreach (StandFoot foot in plan.Feet)
                kg += 4f * (foot.PadHalf - 0.02f) * (foot.PadHalf - 0.02f) * (PlateTop - 0.01f) * 7850f;
            foreach (StandMember m in plan.Members)
            {
                float length = (m.B - m.A).Length;
                float perimeter = m.Round ? 2f * (float)Math.PI * m.Half : 8f * m.Half;
                kg += length * perimeter * 0.003f * 7850f;
            }

            return kg;
        }

        /// <summary>
        /// What the ground says of the feet: groundOffsets[k] is how far the ground at foot k (in the order of StandPlan.Feet) is above (+) or below (-) the one the stand was drawn for. A foot further than
        /// FootLimit, and a frame whose two feet differ by more than that (its brace would lean another way), are warned of.
        /// </summary>
        public static List<StandWarning> FootWarnings(StandPlan plan, IList<float> groundOffsets)
        {
            var warnings = new List<StandWarning>();
            if (plan == null || groundOffsets == null)
                return warnings;
            int n = Math.Min(plan.Feet.Count, groundOffsets.Count);
            for (int k = 0; k < n; k++)
            {
                if (float.IsNaN(groundOffsets[k]))
                    continue;
                if (Math.Abs(groundOffsets[k]) > FootLimit)
                    warnings.Add(new StandWarning(StandWarningKind.FootOff, groundOffsets[k], k));
            }

            for (int k = 0; k + 1 < n; k++)
            {
                if (plan.Feet[k].Rear || !plan.Feet[k + 1].Rear)
                    continue;
                float a = groundOffsets[k], b = groundOffsets[k + 1];
                if (!float.IsNaN(a) && !float.IsNaN(b) && Math.Abs(a - b) > FootLimit)
                    warnings.Add(new StandWarning(StandWarningKind.FrameSkew, a - b, k));
            }

            return warnings;
        }
    }
}
