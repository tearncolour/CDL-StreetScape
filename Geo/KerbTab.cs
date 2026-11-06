namespace CDLC.TS.Geo
{
    /// <summary>
    /// The mod's tab in the game's Roads menu (0.12.85; the owner: "修改一下logo，把我们的路缘石样式移进去，并去掉里边目前唯一一个条目"): the kerb styles' road upgrades are the tab's members, and its
    /// icon is the CDL MOD badge of the listing logo. The toolbar lists a category only when it has a member, so with "kerbTab": "off" in preferences.json the upgrades stay in the group of the game's
    /// template and the tab is not listed; the way in is then the button in the right-hand column.
    /// </summary>
    public static class KerbTab
    {
        /// <summary>The tab's icon in the Icons folder (IconSources/cdl-logo.py makes it from Properties/Thumbnail.png).</summary>
        public const string IconFile = "cdl-logo.png";

        /// <summary>
        /// The order of an upgrade in the grid it is listed in (UIObject.m_Priority): in our tab the order of the styles; in the template's group after the template's own entry, as before.
        /// </summary>
        public static int Priority(KerbStyle style, int templatePriority, bool inOwnTab) => inOwnTab ? (int)style : templatePriority + (int)style;
    }
}
