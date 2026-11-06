using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The English of the traffic signs' special nouns, from GB 5768.2-2022 Table A.1 (informative; 4.5.1 d: "交通标志专用名词宜全部大写，如 EXIT"): the only translations this mod carries. A place's English is
    /// its pinyin by GB 17733 and the spelling rules for Chinese place names (4.5.1 d), which is the player's to type: there is no pinyin or translation table here. The words of the table's two rows that
    /// give two Chinese forms (服务区/站, 停车区/点) are taken for each form alone.
    /// </summary>
    public static class TrafficTerms
    {
        private static readonly Dictionary<string, string> s_Terms = new Dictionary<string, string>
        {
            { "避险车道", "TRUCK ESCAPE RAMP" },
            { "隧道", "TUNNEL" },
            { "收费", "TOLL" },
            { "入口", "ENTRANCE" },
            { "出口", "EXIT" },
            { "下一出口", "NEXT EXIT" },
            { "收费站", "TOLL STATION" },
            { "服务区/站", "SERVICE AREA" },
            { "服务区", "SERVICE AREA" },
            { "服务站", "SERVICE AREA" },
            { "停车区/点", "REST AREA" },
            { "停车区", "REST AREA" },
            { "停车点", "REST AREA" },
            { "爬坡车道", "CLIMBING LANE" },
            { "起点", "BEGIN" },
            { "结束", "END" },
            { "道路交通信息", "TRAFFIC INFORMATION" },
            { "超限检测站", "WEIGH STATION" }
        };

        /// <summary>The table's English for a Chinese text that is exactly one of its nouns, else null.</summary>
        public static string English(string chinese)
        {
            if (string.IsNullOrEmpty(chinese))
                return null;
            return s_Terms.TryGetValue(chinese.Trim().Replace('／', '/'), out string english) ? english : null;
        }
    }
}
