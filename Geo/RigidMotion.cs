using System;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// A road's curve that went to another by a translation and a turn about the vertical (Move It moving or turning a whole road: every edge between two moved nodes is written as its old curve moved, not bent): then
    /// everything that stood by the road goes by the same motion, and an item needs no search, no table and no evaluation of the curve (0.12.74 follow work). TryDetect is the test; Map and MapYaw the motion.
    /// A curve is a convex mix of its four control points with weights that sum to 1, so when each control point of the new curve is within a tolerance of the old one's image, so is every point of the curve, and
    /// the arc length is the same to the same: the answer of the full follow is within that tolerance of the motion's (plus the float rounding the full follow has too). Pure.
    /// </summary>
    public readonly struct RigidMotion
    {
        /// <summary>The distance (m) each control point of the new curve may be from the old one's image for the motion to count: 0.1 mm.</summary>
        public const float Tolerance = 1e-4f;

        private readonly float m_Cos, m_Sin;
        private readonly Vec3 m_From, m_To;

        /// <summary>The turn about the vertical (radians): what is added to an item's yaw.</summary>
        public float Turn { get; }

        private RigidMotion(Vec3 from, Vec3 to, float turn)
        {
            m_From = from;
            m_To = to;
            Turn = turn;
            m_Cos = (float)Math.Cos(turn);
            m_Sin = (float)Math.Sin(turn);
        }

        /// <summary>Where a point goes: turned about the old curve's start by Turn and carried to the new curve's start (the height by the difference of the starts).</summary>
        public Vec3 Map(Vec3 p)
        {
            float x = p.X - m_From.X, z = p.Z - m_From.Z;
            return new Vec3(m_To.X + x * m_Cos + z * m_Sin, m_To.Y + (p.Y - m_From.Y), m_To.Z + z * m_Cos - x * m_Sin);
        }

        /// <summary>The motion that takes one pose (a place and a yaw) to another: what a post that was moved and turned does to what is fixed to it.</summary>
        public static RigidMotion Between(Vec3 from, float fromYaw, Vec3 to, float toYaw) => new RigidMotion(from, to, FollowCurve.Fold(toYaw - fromYaw));

        private static float Reach(Vec3 p) => Math.Max(1f, Math.Max(Math.Abs(p.X), Math.Max(Math.Abs(p.Y), Math.Abs(p.Z))));

        /// <summary>The yaw an item has after the motion.</summary>
        public float MapYaw(float yaw) => FollowCurve.Fold(yaw + Turn);

        /// <summary>
        /// The motion that takes the old curve to the new one, when there is one: every control point of the new curve within tolerance (m, never less than the float rounding of the coordinates, which is 2 steps
        /// of the largest of them) of the image of the old one's. False for a curve that is bent, stretched or shortened, and for one that is not finite.
        /// </summary>
        public static bool TryDetect(Bezier3 old, Bezier3 now, float tolerance, out RigidMotion motion)
        {
            motion = default;
            if (!(old.A.IsFinite && old.B.IsFinite && old.C.IsFinite && old.D.IsFinite && now.A.IsFinite && now.B.IsFinite && now.C.IsFinite && now.D.IsFinite))
                return false;
            // The turn is the difference of the headings of the longest chord from the start: the end if it is far enough, else the handles.
            Vec3 a = old.A, na = now.A;
            Vec3 chord = old.D - a, nchord = now.D - na;
            if (chord.LengthXZ < 1f)
            {
                Vec3 c2 = old.C - a, n2 = now.C - na;
                Vec3 c1 = old.B - a, n1 = now.B - na;
                if (c2.LengthXZ >= c1.LengthXZ)
                {
                    chord = c2;
                    nchord = n2;
                }
                else
                {
                    chord = c1;
                    nchord = n1;
                }
            }

            float turn = chord.LengthXZ < 1e-3f || nchord.LengthXZ < 1e-3f ? 0f : RoadRelation.Wrap(RoadRelation.HeadingYaw(nchord) - RoadRelation.HeadingYaw(chord));
            var candidate = new RigidMotion(a, na, turn);
            float biggest = Math.Max(Math.Max(Math.Max(Reach(old.A), Reach(old.B)), Math.Max(Reach(old.C), Reach(old.D))), Math.Max(Math.Max(Reach(now.A), Reach(now.B)), Math.Max(Reach(now.C), Reach(now.D))));
            float limit = Math.Max(tolerance, 2f * biggest * 1.1920929e-7f);
            if ((candidate.Map(old.B) - now.B).Length > limit || (candidate.Map(old.C) - now.C).Length > limit || (candidate.Map(old.D) - now.D).Length > limit)
                return false;
            motion = candidate;
            return true;
        }
    }
}
