namespace CDLC.TS.Geo.Signs
{
    /// <summary>The series of the catalogue (0.12.22 BRT, 0.12.26 the toll plaza, 0.12.49 the road greening): tabs that list entries of other kinds besides their own boards.</summary>
    public static class SignSeries
    {
        /// <summary>The series an entry of the catalogue is also listed in ("" for none); an entry is in one.</summary>
        public static string Of(string id, string group)
        {
            string series = BrtSigns.SeriesOf(id, group);
            if (series.Length == 0)
                series = TollSigns.SeriesOf(id, group);
            return series.Length > 0 ? series : MarkingTemplates.GreenSeriesOf(id);
        }

        /// <summary>Whether an entry is listed in its series' tab only, not in the tab of its kind (0.12.107): what belongs to a toll plaza or to the greening and to nothing else.</summary>
        public static bool Exclusive(string id) => TollSigns.Only.Contains(id) || MarkingTemplates.GreenOnly(id);
    }
}
