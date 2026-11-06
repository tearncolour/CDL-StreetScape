using CDLC.TS.Geo;

namespace CDLC.TS
{
    /// <summary>
    /// The support style profile in the tool (0.12.75, DESIGN-support-styles.md; the owner delegated the questions: a profile is a default for the boards placed next, kept in the preferences file, the player's
    /// own paint stays part by part). The state is pure (StyleState); here it meets the tool: the paint of the next boards, the support the planner gives a board, and the log.
    /// </summary>
    internal sealed partial class PlacementTool
    {
        internal static readonly StyleState Profiles = new StyleState();
        private static readonly PlainShadow s_PlainShadow = new PlainShadow();

        /// <summary>The paint of the board being shown or placed this frame: the planner's when the support is automatic under a profile, else the paint the player has (PlaceStyle). Made again each frame.</summary>
        private SupportStyle m_PlaceStyleNow;

        /// <summary>The paint a line of signs was started with (0.12.69): the signs of a line are placed with what was planned at its start.</summary>
        private SupportStyle m_LineStyle;

        /// <summary>At load: the profile the file says and whether the feature is on; the paint of the first boards is the profile's.</summary>
        internal static void InitStyle(string profileId, bool enabled)
        {
            Profiles.Enabled = enabled;
            StyleProfile profile = Profiles.Choose(profileId);
            if (enabled && profile != StyleProfiles.Plain)
                PlaceStyle = StyleState.PaintOf(profile);
            Mod.log.Info($"Support styles: {(enabled ? "on" : "off")}, profile {profile.Id}{(enabled && profile.Id != profileId ? " (the file says '" + profileId + "', which this mod does not have)" : "")}.");
        }

        /// <summary>
        /// The player chose a profile (the panel, or the settings page): the boards placed next take its paint and the planner's variants; what the player had set is let go, as choosing a support with a finish
        /// of its own sets the finish. A board already placed is not touched. A support picked by hand keeps its own finish.
        /// </summary>
        internal static void SetProfile(string id)
        {
            if (!Profiles.Enabled)
                return;
            StyleProfile profile = Profiles.Choose(id);
            SupportStyle paint = StyleState.PaintOf(profile);
            if (SupportCatalog.ById(SelectedSupport)?.DefaultFinish is SupportFinish own)
                paint.Finish = own;
            PlaceStyle = paint;
            PreferencesStore.SaveProfile(profile.Id);
            EditorRevision++;
            Mod.log.Info($"Support style: {profile.Id}; the next boards are painted {paint.Finish}{(paint.BlackBack ? ", the boards' backs black" : "")}, root paint {paint.Root}.");
        }

        /// <summary>
        /// The support and the paint the planner gives a board with an automatic support. click: this is a placement, the first ones are compared with the planner as it was (the plain profile must equal it).
        /// </summary>
        private PlannedSupport PlanSupport(SignDef def, SupportContext context, bool click)
        {
            if (click && s_PlainShadow.Checked < PlainShadow.Limit)
            {
                bool same = s_PlainShadow.Check(def, context, PlaceStyle, false);
                if (!same && s_PlainShadow.Different <= PlainShadow.KeepDifferences)
                    Mod.log.Warn("Support styles: " + s_PlainShadow.Summary);
                else if (s_PlainShadow.Checked == 50 || s_PlainShadow.Checked == 500 || s_PlainShadow.Checked == PlainShadow.Limit)
                    Mod.log.Info("Support styles: " + s_PlainShadow.Summary + ".");
            }

            return Profiles.Plan(def, context, PlaceStyle);
        }
    }
}
