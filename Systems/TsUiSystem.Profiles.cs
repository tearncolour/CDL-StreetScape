using System.Collections.Generic;
using CDLC.TS.Geo;
using Colossal.UI.Binding;
using Newtonsoft.Json;

namespace CDLC.TS
{
    /// <summary>The panel's support styles (0.12.75): the profiles with how far each is backed, the one chosen, and the choice.</summary>
    internal sealed partial class TsUiSystem
    {
        private ValueBinding<string> m_Profiles;
        private ValueBinding<string> m_Profile;
        private static string s_ProfilesJson;

        private sealed class ProfileEntry
        {
            [JsonProperty("id")] public string Id;
            [JsonProperty("zh")] public string Zh;
            [JsonProperty("en")] public string En;

            /// <summary>How far the paint, the root paint and the forms are backed: std, photo, supplier or estimate; empty where the profile has none (the plain profile has no evidence: it is the default).</summary>
            [JsonProperty("pe")] public string PaintEvidence;
            [JsonProperty("re")] public string RootEvidence;
            [JsonProperty("fe")] public string FormsEvidence;
        }

        private void AddProfileBindings()
        {
            AddBinding(m_Profiles = new ValueBinding<string>(kGroup, "Profiles", ProfilesJson()));
            AddBinding(m_Profile = new ValueBinding<string>(kGroup, "Profile", PlacementTool.Profiles.Effective.Id));
            AddBinding(new TriggerBinding<string>(kGroup, "SetProfile", PlacementTool.SetProfile));
        }

        /// <summary>The list is empty when the feature is off: the panel shows no row. A reference, not a text, is compared each frame.</summary>
        private void UpdateProfileBindings()
        {
            m_Profiles.Update(ProfilesJson());
            m_Profile.Update(PlacementTool.Profiles.Effective.Id);
        }

        private static string ProfilesJson()
        {
            if (!PlacementTool.Profiles.Enabled)
                return "[]";
            if (s_ProfilesJson != null)
                return s_ProfilesJson;
            var list = new List<ProfileEntry>();
            foreach (StyleProfile profile in StyleProfiles.All)
                list.Add(new ProfileEntry { Id = profile.Id, Zh = profile.Zh, En = profile.En, PaintEvidence = StyleEvidence.PaintOf(profile), RootEvidence = StyleEvidence.RootOf(profile), FormsEvidence = StyleEvidence.FormsOf(profile) });
            return s_ProfilesJson = JsonConvert.SerializeObject(list);
        }
    }
}
