using System;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The span of a gantry, the arm of a cantilever and the clearance under a hung support as the player sets them (0.12.78; the owner: "门架式支架应该支持滑条调节长度，这样如果自动识别出现问题，玩家还能够手动调整",
    /// and the height slider for a height-limit frame at a tunnel): the range of each, tenths of a metre, the steps of the sliders, and the value shown when none is set. Pure.
    /// </summary>
    public static class SpanSlider
    {
        /// <summary>What the span's slider, and a button of the fallback, moves a length by (the script's buttons add it to the length shown).</summary>
        public const float Step = 0.5f;

        /// <summary>The clearance's step.</summary>
        public const float ClearanceStep = 0.1f;

        /// <summary>The shortest and the longest an arm is set to: the octagonal arms are listed to SupportPlanner.OctaArmMax.</summary>
        public const float MinArm = 1f, MaxArm = SupportPlanner.OctaArmMax;

        /// <summary>The supports that have a length and a clearance to set: the hung ones (a gantry's span, the arm of a cantilever, each arm of a double one).</summary>
        public static bool Applies(SupportKind kind) => SupportKinds.Hung(kind);

        public static float Min(SupportKind kind) => kind == SupportKind.Gantry ? SupportLayouts.MinSpan : MinArm;

        public static float Max(SupportKind kind) => kind == SupportKind.Gantry ? SupportLayouts.MaxSpan : MaxArm;

        /// <summary>A length as the slider sets it: inside the kind's range, in whole tenths (the float noise of a slider lands on the tenth); NaN when the kind has none or it is not a number.</summary>
        public static float Within(SupportKind kind, float value) => Tenths(kind, value, Min(kind), Max(kind));

        /// <summary>A clearance as its slider sets it: inside SupportLayouts.MinClearance and MaxClearance, in tenths; NaN when the kind has none or it is not a number.</summary>
        public static float WithinClearance(SupportKind kind, float value) => Tenths(kind, value, SupportLayouts.MinClearance, SupportLayouts.MaxClearance);

        private static float Tenths(SupportKind kind, float value, float min, float max)
        {
            if (!Applies(kind) || float.IsNaN(value) || float.IsInfinity(value))
                return float.NaN;
            return (float)Math.Round(Math.Max(min, Math.Min(max, value)) * 10.0, MidpointRounding.AwayFromZero) / 10f;
        }

        /// <summary>A number typed in the box: 0 or less is 0 (the support's own length, as the box always said), else as the slider sets it; NaN when it is not a number, and nothing is set.</summary>
        public static float Typed(SupportKind kind, float value) => Entered(value, Within(kind, value));

        /// <summary>As Typed, for the clearance.</summary>
        public static float TypedClearance(SupportKind kind, float value) => Entered(value, WithinClearance(kind, value));

        private static float Entered(float value, float within) => float.IsNaN(value) || float.IsInfinity(value) ? float.NaN : value <= 0f ? 0f : within;

        /// <summary>
        /// A clearance the player asked for, raised to the lowest one at which the boards on the post still fit under the truss or the arm (overrun: Assembly.PostOverrun at the asked clearance; the post's top
        /// moves with the clearance one for one), in tenths and within the range. No overrun: as asked.
        /// </summary>
        public static float WithPostRoom(SupportKind kind, float clearance, float overrun) =>
            overrun > 0f ? WithinClearance(kind, (float)Math.Ceiling((clearance + overrun) * 10.0 - 1e-3) / 10f) : clearance;

        /// <summary>The length the slider is at: the player's when there is one for this kind, else the planner's.</summary>
        public static float Shown(SupportKind kind, SpanOverride manual, float automatic)
        {
            float own = manual == null ? float.NaN : manual.For(kind);
            return float.IsNaN(own) ? automatic : own;
        }

        /// <summary>The clearance the slider is at: the player's when there is one for this kind, else the support's own.</summary>
        public static float ShownClearance(SupportKind kind, SpanOverride manual, float standard)
        {
            float own = manual == null ? float.NaN : manual.ClearanceFor(kind);
            return float.IsNaN(own) ? standard : own;
        }
    }

    /// <summary>
    /// The span and the clearance the player set for the next hung support (placing): tied to the kind of support they were set for, so that a span of 24 m set for a gantry is never the arm of a
    /// cantilever that the road then asks for, and a height-limit frame's 4.5 m is not the next cantilever's. Clear gives the planner's and the support's own back (both: one Auto).
    /// </summary>
    public sealed class SpanOverride
    {
        private SupportKind m_Kind;
        private float m_Span = float.NaN, m_Clearance = float.NaN;

        public bool Manual => !float.IsNaN(m_Span);

        public bool ClearanceManual => !float.IsNaN(m_Clearance);

        public SupportKind Kind => m_Kind;

        /// <summary>Sets the span or arm for a kind; 0 or not a number clears it.</summary>
        public void Set(SupportKind kind, float metres)
        {
            if (!SpanSlider.Applies(kind))
            {
                Clear();
                return;
            }

            float value = SpanSlider.Typed(kind, metres);
            if (!(value > 0f))
            {
                m_Span = float.NaN;
                return;
            }

            Retarget(kind);
            m_Span = value;
        }

        /// <summary>Sets the clearance for a kind; 0 or not a number clears it.</summary>
        public void SetClearance(SupportKind kind, float metres)
        {
            if (!SpanSlider.Applies(kind))
            {
                Clear();
                return;
            }

            float value = SpanSlider.TypedClearance(kind, metres);
            if (!(value > 0f))
            {
                m_Clearance = float.NaN;
                return;
            }

            Retarget(kind);
            m_Clearance = value;
        }

        public void Clear() => m_Span = m_Clearance = float.NaN;

        /// <summary>The span or arm set for this kind of support; NaN when none is, or it was set for another kind.</summary>
        public float For(SupportKind kind) => Manual && kind == m_Kind ? m_Span : float.NaN;

        /// <summary>The clearance set for this kind of support; NaN when none is, or it was set for another kind.</summary>
        public float ClearanceFor(SupportKind kind) => ClearanceManual && kind == m_Kind ? m_Clearance : float.NaN;

        /// <summary>What was set for another kind is dropped: one kind at a time.</summary>
        private void Retarget(SupportKind kind)
        {
            if (kind == m_Kind)
                return;
            Clear();
            m_Kind = kind;
        }
    }
}
