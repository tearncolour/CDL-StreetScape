using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The code of a sign for an English game (0.12.80): the tile, the tooltip, the editor's title and the boards' names show it. The standard's own designations (禁7, 示17, 警11, 路8, 线5, 旅9) stay as they
    /// are, being what the standard calls the figure; the words that a code is made of (灯箱, 声屏障, 图268) are put into English. NativeEnglishTests fails on a code that keeps a word of Chinese.
    /// </summary>
    public static class CodeNames
    {
        /// <summary>Longest first, so that 城市道路 is taken before 城市 and 示警桩 before anything of 示.</summary>
        private static readonly (string Zh, string En)[] Words =
        {
            ("上海征求意见稿", "Shanghai draft"), ("杭州导则", "Hangzhou guide"), ("分车型限速", "Speed by type"), ("自定义", "Custom"), ("城市道路", "City road"), ("深标Ⅰ型", "Shenzhen type I"),
            ("锌钢护栏", "Zinc-steel rail"), ("缆索护栏", "Cable rail"), ("草坪护栏", "Lawn fence"), ("花箱护栏", "Planter rail"), ("花箱隔离", "Planter divider"), ("景观护栏", "Garden rail"),
            ("围墙护栏", "Wall fence"), ("仿古栏杆", "Antique rail"), ("新泽西型", "Jersey"), ("施工围挡", "Hoarding"), ("垂直绿化", "Green wall"), ("声屏障", "Noise wall"), ("宣传牌", "Ad board"),
            ("非国标", "Non-GB"), ("示警墩", "Marker block"), ("示警桩", "Marker post"), ("防撞桶", "Crash barrel"), ("交通锥", "Cone"), ("不锈钢", "Stainless"), ("中国结", "Knot"), ("石栏杆", "Stone rail"),
            ("收费站", "Toll"), ("造型灯", "Figure lamp"), ("附录", "App. "), ("城市", "City"), ("京式", "Beijing"), ("仿木", "Faux wood"), ("信号", "Signal"), ("接线", "Wiring"), ("水马", "Water barrier"),
            ("铁马", "Barricade"), ("清除", "Clear"), ("灯笼", "Lantern"), ("灯箱", "Light box"), ("电表", "Meter"), ("监控", "Camera"), ("石球", "Stone ball"), ("自动", "Auto"), ("花钵", "Flower pot"),
            ("路桩", "Bollard"), ("路灯", "Street lamp"), ("辅助", "Aux."), ("通信", "Telecom"), ("道旗", "Banner"), ("配电", "Power"), ("重标", "Re-mark"), ("霓虹", "Neon"), ("图", "Fig. ")
        };

        private static readonly (string Zh, string En)[] Marks = { ("（", " ("), ("）", ")"), ("、", ", "), ("～", "–") };

        /// <summary>The code for an English game: each word put into English, the figure's number kept, a full-width mark made an ordinary one.</summary>
        public static string En(string code)
        {
            if (string.IsNullOrEmpty(code))
                return code ?? "";
            string text = code;
            foreach ((string zh, string en) in Words)
                text = text.Replace(zh, en);
            foreach ((string zh, string en) in Marks)
                text = text.Replace(zh, en);
            return text.Replace("  ", " ").Trim();
        }
    }
}
