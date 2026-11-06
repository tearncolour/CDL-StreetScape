using System.Collections.Generic;
using System.Globalization;
using CDLC.TS.Geo;
using Unity.Entities;
using UnityEngine;

namespace CDLC.TS
{
    /// <summary>
    /// The span of a gantry, the arm of a cantilever and the clearance under a hung support set by hand (0.12.78; the owner: "门架式支架应该支持滑条调节长度，这样如果自动识别出现问题，玩家还能够手动调整", and
    /// "龙门架也加入高度滑条"). While placing, the player's length and clearance (SpanOverride, for the kind of support they were set for) replace the ones the road and the support gave; a placed support takes the
    /// length through SetReach as before (FlagReachByHand) and the clearance through TsItem.Clearance. The gantry's near post stands at the cursor's side and the span runs across the road from it: a longer
    /// span moves the far post, the near one stays where the road put it; the clearance moves the truss, the boards and the tops of the posts, not the feet.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        private const int kMaxSpanLogs = 30;

        private readonly SpanOverride m_PlaceSpan = new SpanOverride();

        /// <summary>The kind of support the cursor's road gave the last time (the support of the sign chosen, or the planner's for the road), what the planner's own length was, and how it was made.</summary>
        private SupportKind m_SpanKind;
        private bool m_HaveSpanKind, m_SpanByHand;
        private float m_AutoSpan = float.NaN;
        private SpanFacts m_SpanFacts;
        private int m_SpanLogs;

        /// <summary>What the planner read for a span, kept to say on the click why the length was what it was (the log line); numbers only, so that a frame makes no garbage of it.</summary>
        private struct SpanFacts
        {
            public SupportKind Kind;
            public bool OnRoad, CarriagewayOk, Footway;
            public float RoadWidth, Left, Right, PostDistance, InnerReach, Margin, Automatic, Used, Standard, Clearance;
            public bool ClearanceByHand;
        }

        /// <summary>The length a support being placed is made with: the player's for its kind when there is one, else the planner's (automatic). Every frame the cursor is placing.</summary>
        private float PlaceSpanFor(SupportDef support, bool hosted, bool onRoad, Carriageway carriageway, float roadWidth, int roadSide, float postDistance, float innerReach, float automatic)
        {
            m_SpanKind = support.Kind;
            m_HaveSpanKind = true;
            m_SpanByHand = false;
            if (hosted || !SpanSlider.Applies(support.Kind))
            {
                m_AutoSpan = float.NaN;
                return automatic;
            }

            if (float.IsNaN(m_AutoSpan) || Mathf.Abs(m_AutoSpan - automatic) >= 0.05f)
            {
                m_AutoSpan = automatic;
                if (!m_PlaceSpan.Manual)
                    EditorRevision++;
            }

            float own = m_PlaceSpan.For(support.Kind);
            float used = float.IsNaN(own) ? automatic : own;
            m_SpanByHand = !float.IsNaN(own);
            m_SpanFacts = new SpanFacts
            {
                Kind = support.Kind,
                OnRoad = onRoad,
                CarriagewayOk = carriageway != null && carriageway.Ok,
                Footway = carriageway != null && carriageway.Ok && carriageway.Footway(roadSide),
                RoadWidth = roadWidth,
                Left = carriageway != null ? carriageway.Left : 0f,
                Right = carriageway != null ? carriageway.Right : 0f,
                PostDistance = postDistance,
                InnerReach = innerReach,
                Margin = Margin,
                Automatic = automatic,
                Used = used
            };
            return used;
        }

        /// <summary>The clearance a support being placed is made with (m; 0: the support's own): the player's for its kind when there is one. Every frame the cursor is placing, after PlaceSpanFor.</summary>
        private float PlaceClearanceFor(SupportDef support, bool hosted, float standard)
        {
            if (hosted || !SpanSlider.Applies(support.Kind))
                return 0f;
            float own = m_PlaceSpan.ClearanceFor(support.Kind);
            m_SpanFacts.Standard = standard;
            m_SpanFacts.Clearance = float.IsNaN(own) ? standard : own;
            m_SpanFacts.ClearanceByHand = !float.IsNaN(own);
            return float.IsNaN(own) ? 0f : own;
        }

        /// <summary>One line for a gantry or an arm that is placed, to say why the planner's length was what it was and whether the player's was used instead (the first few of a session).</summary>
        private void LogSpan(SupportDef support)
        {
            if (!SpanSlider.Applies(support.Kind) || m_SpanFacts.Kind != support.Kind || m_SpanLogs >= kMaxSpanLogs)
                return;
            m_SpanLogs++;
            SpanFacts f = m_SpanFacts;
            CultureInfo c = CultureInfo.InvariantCulture;
            string road = !f.OnRoad ? "no road under the cursor"
                : $"road width {f.RoadWidth.ToString("0.0", c)} m, " + (f.CarriagewayOk ? $"carriageway {f.Left.ToString("+0.0;-0.0", c)} to {f.Right.ToString("+0.0;-0.0", c)} m{(f.Footway ? " with a footway beyond the near kerb" : ", no footway beyond the near kerb")}" : "carriageway not read")
                    + $", near post {f.PostDistance.ToString("0.0", c)} m from the centre line at margin {f.Margin.ToString("0.0", c)} m (the board reaches {f.InnerReach.ToString("0.0", c)} m toward the road)";
            Mod.log.Info($"Span: {support.Id} ({(support.Kind == SupportKind.Gantry ? "span" : "arm")}): the planner's {f.Automatic.ToString("0.0", c)} m, {(m_SpanByHand ? $"the player's {f.Used.ToString("0.0", c)} m used" : "used as it is")}; clearance {f.Clearance.ToString("0.0", c)} m ({(f.ClearanceByHand ? $"the player's, the support's own is {f.Standard.ToString("0.0", c)} m" : "the support's own")}); {road}.");
        }

        /// <summary>
        /// The slider, the box and the buttons of the span or the arm: a support that stands is set through SetReach (the boards then keep it as the least they make it); the next one to be placed
        /// takes it in place of the road's. A number of 0 (or less) is the support's own length, or the planner's again when placing.
        /// </summary>
        internal void SetSpan(float metres)
        {
            if (Mode != 2 && Mode != 0)
                return;
            EditorView view = GetEditorView();
            if (!view.HasReach)
                return;
            float value = SpanSlider.Typed(view.ReachFor, metres);
            if (float.IsNaN(value))
                return;
            if (Mode == 2)
            {
                SetReach(value);
                return;
            }

            m_PlaceSpan.Set(view.ReachFor, value);
            EditorRevision++;
            if (m_SpanLogs < kMaxSpanLogs)
                Mod.log.Info(m_PlaceSpan.Manual ? $"Span: the next {(view.ReachFor == SupportKind.Gantry ? "span" : "arm")} is set to {m_PlaceSpan.For(view.ReachFor).ToString("0.0", CultureInfo.InvariantCulture)} m by hand." : "Span: the next one is the planner's again.");
        }

        /// <summary>
        /// The clearance of the support being edited, or of the next one to be placed (m; 0.12.78, the owner: the height slider and a height-limit frame at a tunnel): every board of the support carries it
        /// (TsItem.Clearance), so the boards that are put on it later hang at it too. 0 is the support's own (5.5 m).
        /// </summary>
        internal void SetClearance(float metres)
        {
            if (Mode != 2 && Mode != 0)
                return;
            EditorView view = GetEditorView();
            if (!view.HasReach)
                return;
            float value = SpanSlider.TypedClearance(view.ReachFor, metres);
            if (float.IsNaN(value))
                return;
            if (Mode == 0)
            {
                m_PlaceSpan.SetClearance(view.ReachFor, value);
                EditorRevision++;
                if (m_SpanLogs < kMaxSpanLogs)
                    Mod.log.Info(m_PlaceSpan.ClearanceManual ? $"Span: the next support's clearance is set to {m_PlaceSpan.ClearanceFor(view.ReachFor).ToString("0.0", CultureInfo.InvariantCulture)} m by hand." : "Span: the next support's clearance is its own again.");
                return;
            }

            if (!TryReadEdit(out TsItem item))
                return;
            // The boards stacked on the post stay where they are: a clearance lower than they leave room for is held at the lowest that does.
            if (value > 0f)
                value = SpanSlider.WithPostRoom(view.ReachFor, value, PostOverrunAt(item, value));
            if (Mathf.Abs(item.Clearance - value) < 0.005f)
            {
                EditorRevision++;
                return;
            }

            SetClearanceOf(item, value);
            if (m_SpanLogs < kMaxSpanLogs)
                Mod.log.Info(value > 0f ? $"Edit: the clearance of the support set to {value.ToString("0.0", CultureInfo.InvariantCulture)} m by hand." : "Edit: the clearance of the support is its own again.");
        }

        /// <summary>How far the boards on the post of the support the item is on would stand above its top with this clearance (Assembly.PostOverrun).</summary>
        private float PostOverrunAt(TsItem item, float clearance)
        {
            List<KeyValuePair<Entity, ItemRecord>> members = MembersOf(EditEntity, item);
            List<AssemblyBoard> boards = ItemGroups.Boards(RecordsOf(members));
            if (boards.Count == 0)
                return 0f;
            foreach (AssemblyBoard board in boards)
                board.Clearance = clearance;
            ItemRecord lead = members[0].Value;
            return Assembly.PostOverrun(Assembly.Plan(Assembly.Order(boards), SupportCatalog.ByHash(lead.SupportHash), lead.Reach));
        }

        private void ClearReachHand(TsItem item)
        {
            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Flags &= unchecked((byte)~TsItem.FlagReachByHand);
                WriteItem(member.Key, each);
            }

            EditorRevision++;
        }

        /// <summary>Writes the clearance to every board of the support the item is on.</summary>
        private void SetClearanceOf(TsItem item, float value)
        {
            foreach (KeyValuePair<Entity, ItemRecord> member in MembersOf(EditEntity, item))
            {
                TsItem each = EntityManager.GetComponentData<TsItem>(member.Key);
                each.Clearance = value;
                WriteItem(member.Key, each);
            }

            EditorRevision++;
        }

        /// <summary>Auto: a support that stands has its span set from its road (as the Fit the road button always did) and its clearance its own; the one being placed is made by the planner again.</summary>
        internal void FitSpan()
        {
            if (Mode == 0)
            {
                if (m_PlaceSpan.Manual || m_PlaceSpan.ClearanceManual)
                {
                    m_PlaceSpan.Clear();
                    EditorRevision++;
                }

                return;
            }

            if (Mode != 2 || !TryReadEdit(out TsItem item))
                return;
            EditorView view = GetEditorView();
            if (!view.HasReach || view.Support == null)
                return;
            if (item.Clearance > 0f)
                SetClearanceOf(item, 0f);
            float fitted = FittedReach(item, view);
            if (fitted <= 0f)
                return;
            SetReach(fitted);
            // A gantry keeps its own span whatever the flag says (RefitReach), so the flag is only the button's mark: the span from the road is not one set by hand. An arm's is kept as the least, as it was.
            if (view.Support.Kind == SupportKind.Gantry)
                ClearReachHand(item);
            Mod.log.Info($"Edit: the {(view.Support.Kind == SupportKind.Gantry ? "span" : "arm")} of the support set from the road: {fitted.ToString("0.0", CultureInfo.InvariantCulture)} m.");
        }

        /// <summary>The length set for the next support is for the support chosen: another one, or leaving the tool, gives the planner's back.</summary>
        private void ResetSpan()
        {
            m_PlaceSpan.Clear();
            m_HaveSpanKind = false;
            m_AutoSpan = float.NaN;
        }

        /// <summary>What the editor shows for the span of the support being placed (the draft's view): the kind it is for, and the length the slider is at (the layout's, which makes it long enough for the board).</summary>
        private void DraftSpan(ref EditorView view, SignDef def, SupportDef drafted)
        {
            SupportKind kind = m_HaveSpanKind ? m_SpanKind : drafted.Kind;
            if (!SpanSlider.Applies(kind))
                return;
            float shown = SpanSlider.Shown(kind, m_PlaceSpan, float.IsNaN(m_AutoSpan) ? 0f : m_AutoSpan);
            view.HasReach = true;
            view.ReachFor = kind;
            view.ReachByHand = m_PlaceSpan.For(kind) > 0f;
            float clearance = m_PlaceSpan.ClearanceFor(kind);
            // The support's own clearance: what the road's support has when that is the kind shown, else the drafted one's (the draft's layout is made without the player's).
            float standard = m_SpanFacts.Kind == kind && m_SpanFacts.Standard > 0f ? m_SpanFacts.Standard : view.Layout.BottomClearance;
            if (drafted.Kind == kind && !float.IsNaN(clearance))
                view.Layout = SupportLayouts.Of(def, drafted, 0f, clearance);
            view.Reach = drafted.Kind == kind ? SupportLayouts.Of(def, drafted, shown).ArmLength : shown;
            view.ClearanceByHand = !float.IsNaN(clearance);
            view.Clearance = float.IsNaN(clearance) ? standard : clearance;
        }
    }
}
