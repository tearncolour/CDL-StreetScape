namespace CDLC.TS.Geo
{
    /// <summary>
    /// Whether the screens that follow the game are drawn with what it says (0.12.29): true only while the Realistic Information Extension, the mod that reads the game's traffic, has joined this one
    /// (TsExtension). Without it a traffic-state screen shows its default picture (TrafficMap.Decorate gives it no live item) and an automatic screen is drawn and edited as a manual one
    /// (VmsAuto.IsAutoScreen, the fields of Signs.VariableSigns): the player's own lines, value or state. Set once when the mods are loaded.
    /// </summary>
    public static class LiveContent
    {
        public static bool On { get; set; }
    }
}
