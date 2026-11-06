using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// The prohibition signs of GB 5768.2-2022 chapter 5 (禁1 to 禁48). Round signs with a red ring (Table 7: D 80 cm at 40 to 70 km/h), the stop sign, the yield sign, the zone signs. The
    /// figures are redrawn here from the printed ones (the standard's own artwork is not used): the pictograms are the P_ glyphs of Glyphs.Prohibition.cs, written in the units of the
    /// dimensioned drawings of Appendix E (D/100), and every round sign here puts its figure on a canvas over the whole sign, so that a figure stands where the drawing has it. As in the
    /// printed figures the red bar lies under the figure and shows through its openings (windows, wheels, the gaps of a frame).
    /// </summary>
    public static class ProhibitionSigns
    {
        private const string G = StandardSigns.Prohibition;
        private const GuideColour K = GuideColour.Black;
        private const float Sqrt2 = 1.41421356f;

        // Table 7, round signs: the diameter D, the red ring a, the red bar b and the liner c (m), for the four bands (100-120, 71-99, 40-70, under 40 km/h).
        private static readonly float[] kD = { 1.20f, 1.00f, 0.80f, 0.60f };
        private static readonly float[] kA = { 0.12f, 0.10f, 0.08f, 0.06f };
        private static readonly float[] kB = { 0.09f, 0.075f, 0.06f, 0.045f };
        private static readonly float[] kC = { 0.010f, 0.008f, 0.006f, 0.004f };

        public static IEnumerable<GuideTemplate> All()
        {
            // 禁1 停车让行 (5.2): the id of the first builds' stop sign is its legacy id, so that signs placed then are still drawn.
            yield return StandardSigns.Make("gb.prohib.01", "禁1", "停车让行", "Stop", G, "tcrx", StandardSigns.Words("停", "让行", "stop"), band => SignStyles.Stop(band), "gb.stop");

            // 禁2 减速让行 (5.3).
            yield return StandardSigns.Make("gb.prohib.02", "禁2", "减速让行", "Yield", G, "jsrx", StandardSigns.Words("减速", "让行", "yield"), band => SignStyles.Yield(band));

            // 禁3 会车让行 (5.4, Fig. E.3): a black arrow down (the oncoming traffic that goes first) and a thin red arrow up, no bar.
            yield return StandardSigns.Make("gb.prohib.03", "禁3", "会车让行", "Give way to oncoming traffic", G, "hcrx", StandardSigns.Words("会车", "让行", "对向", "oncoming", "give way"),
                band => Disc(band, GuideColour.Red, GuideColour.White, null, c =>
                {
                    float[] f = Glyphs.P_OncomingFrame;
                    c.Pic(Glyphs.P_OncomingDown, K, f[0], f[1], f[2], f[2]);
                    c.Pic(Glyphs.P_OncomingUp, GuideColour.Red, f[0], f[1], f[2], f[2]);
                }));

            // 禁4 禁止通行 (5.5): a red ring on white, nothing in it.
            yield return StandardSigns.Make("gb.prohib.04", "禁4", "禁止通行", "No entry for all", G, "jztx", StandardSigns.Words("禁止", "通行", "禁行"), band => SignStyles.Prohibition(band, null, bar: false));

            // 禁5 禁止驶入 (5.6, Fig. E.5): a red disc with a white bar, 62 x 14.5 % of D.
            yield return StandardSigns.Make("gb.prohib.05", "禁5", "禁止驶入", "No entry", G, "jzsr", StandardSigns.Words("禁止", "驶入", "do not enter"),
                band => SignStyles.MandatoryRound(band, c => c.Rect(GuideColour.White, 19f, 42.75f, 62f, 14.5f), 1f, GuideColour.Red));

            // 禁6 to 禁21: a vehicle (or a pedestrian) under the bar.
            yield return Ban("gb.prohib.06", "禁6", "禁止机动车驶入", "No entry for motor vehicles", "jzjdcsr", Glyphs.P_CarFront, "机动车", "汽车", "motor", "car");
            yield return StandardSigns.Make("gb.prohib.07", "禁7、禁8", "禁止大型（或小型）载客汽车驶入", "No entry for large (or small) passenger vehicles", G, "jzdxzkqcsr",
                StandardSigns.Words("大型", "小型", "客车", "载客", "bus", "car"),
                (spec, band) => Ban(band, Figure(spec.Is("kind", "small") ? Glyphs.P_Car : Glyphs.P_Bus)),
                new[] { GuideField.Choice("kind", "车型", "Vehicle", "large", new[] { new GuideChoice("large", "大型载客汽车（禁7）", "Large passenger vehicle (禁7)"), new GuideChoice("small", "小型载客汽车（禁8）", "Small passenger vehicle (禁8)") }) });
            yield return Ban("gb.prohib.09", "禁9", "禁止载货汽车驶入", "No entry for goods vehicles", "jzzhqcsr", Glyphs.P_Truck, "货车", "载货", "卡车", "truck", "lorry");
            yield return Ban("gb.prohib.10", "禁10", "禁止挂车、半挂车驶入", "No entry for trailers and semi-trailers", "jzgcbgcsr", Glyphs.P_Trailer, "挂车", "半挂车", "拖挂", "trailer");
            yield return Ban("gb.prohib.11", "禁11", "禁止拖拉机驶入", "No entry for tractors", "jztljsr", Glyphs.P_Tractor, "拖拉机", "农用", "tractor");
            yield return Ban("gb.prohib.12", "禁12", "禁止三轮汽车、低速货车驶入", "No entry for three-wheeled vehicles and low-speed lorries", "jzslqcdshcsr", Glyphs.P_ThreeWheelTruck, "三轮汽车", "低速货车", "农用车", "three-wheeler");
            yield return Ban("gb.prohib.13", "禁13", "禁止摩托车驶入", "No entry for motorcycles", "jzmtcsr", Glyphs.P_Motorcycle, "摩托车", "摩托", "motorcycle");
            yield return Ban("gb.prohib.14", "禁14", "禁止非机动车进入", "No entry for non-motor vehicles", "jzfjdcjr", Glyphs.P_Bicycle, "非机动车", "自行车", "单车", "bicycle");
            yield return Ban("gb.prohib.15", "禁15", "禁止电动自行车进入", "No entry for electric bicycles", "jzddzxcjr", Glyphs.P_EBike, "电动自行车", "电动车", "电瓶车", "e-bike");
            yield return Ban("gb.prohib.16", "禁16", "禁止畜力车进入", "No entry for animal-drawn vehicles", "jzclcjr", Glyphs.P_AnimalCart, "畜力车", "马车", "牲畜", "horse");
            yield return Ban("gb.prohib.17", "禁17", "禁止三轮车驶入", "No entry for tricycles", "jzslcsr", Glyphs.P_Tricycle, "三轮车", "电动三轮", "tricycle");
            yield return StandardSigns.Make("gb.prohib.18", "禁18、禁19", "禁止人力客运（或货运）三轮车进入", "No entry for pedal tricycles (passengers or goods)", G, "jzrlkyslcjr",
                StandardSigns.Words("人力三轮", "客运", "货运", "黄包车", "rickshaw", "tricycle"),
                (spec, band) => Ban(band, Figure(spec.Is("kind", "goods") ? Glyphs.P_CargoTrike : Glyphs.P_Rickshaw)),
                new[] { GuideField.Choice("kind", "类型", "Kind", "passenger", new[] { new GuideChoice("passenger", "人力客运三轮车（禁18）", "Passengers (禁18)"), new GuideChoice("goods", "人力货运三轮车（禁19）", "Goods (禁19)") }) });
            yield return Ban("gb.prohib.20", "禁20", "禁止人力车进入", "No entry for hand carts", "jzrlcjr", Glyphs.P_HandCart, "人力车", "手推车", "板车", "hand cart");
            yield return Ban("gb.prohib.21", "禁21", "禁止行人进入", "No pedestrians", "jzxrjr", Glyphs.P_Walker, "行人", "步行", "pedestrian");

            // 禁22 禁止某两种车辆驶入 (5.21, Fig. E.22): two vehicles one above the other, the standard's example a lorry and a tractor; never more than two.
            yield return StandardSigns.Make("gb.prohib.22", "禁22", "禁止某两种车辆驶入", "No entry for two kinds of vehicles", G, "jzmlzclsr", StandardSigns.Words("两种", "车辆", "货车", "拖拉机", "vehicles"),
                (spec, band) => Ban(band, c =>
                {
                    // The boxes of the lorry and the tractor on Fig. E.22; another vehicle is fitted into the same box.
                    c.Fit(VehicleGlyph(spec.Get("v1")), K, 35f, 20f, 39.6f, 23.5f);
                    c.Fit(VehicleGlyph(spec.Get("v2")), K, 29.5f, 55.5f, 34.9f, 26.1f);
                }),
                new[] { GuideField.Choice("v1", "上方车辆", "Upper vehicle", "truck", Vehicles(false)), GuideField.Choice("v2", "下方车辆", "Lower vehicle", "tractor", Vehicles(false)) });

            // 禁23 to 禁29: the turning bans. The standard lets a vehicle be added to the sign with the arrow where it is (Fig. 42); the boxes for 禁25 to 禁28 are this file's choice (the
            // standard has no example for them).
            yield return StandardSigns.Make("gb.prohib.23", "禁23、禁24", "禁止向左（或向右）转弯", "No left (or right) turn", G, "jzxzzw", StandardSigns.Words("左转", "右转", "转弯", "no left turn", "no right turn"),
                (spec, band) =>
                {
                    bool right = spec.Is("dir", "right");
                    return Ban(band, c =>
                    {
                        Figure(right ? Glyphs.Mirror(Glyphs.P_TurnLeft) : Glyphs.P_TurnLeft)(c);
                        Attach(c, spec, right ? 44f : 22f, 57.5f, 34f, 20.5f);
                    });
                },
                new[] { Direction("禁止向左转弯（禁23）", "No left turn (禁23)", "禁止向右转弯（禁24）", "No right turn (禁24)"), AttachField() });
            yield return StandardSigns.Make("gb.prohib.25", "禁25", "禁止直行", "No straight ahead", G, "jzzx", StandardSigns.Words("直行", "禁止直行", "no straight ahead"),
                (spec, band) => Ban(band, c =>
                {
                    Figure(Glyphs.P_Straight)(c);
                    Attach(c, spec, 57f, 50f, 28f, 14f);
                }),
                new[] { AttachField() });
            yield return StandardSigns.Make("gb.prohib.26", "禁26", "禁止向左和向右转弯", "No left or right turn", G, "jzxzhxyzw", StandardSigns.Words("左转", "右转", "转弯", "no turns"),
                (spec, band) => Ban(band, c =>
                {
                    Figure(Glyphs.P_TurnBoth)(c);
                    Attach(c, spec, 56f, 61f, 23f, 11.5f);
                }),
                new[] { AttachField() });
            yield return StandardSigns.Make("gb.prohib.27", "禁27、禁28", "禁止直行和向左（或向右）转弯", "No straight ahead or left (or right) turn", G, "jzzxhxzzw",
                StandardSigns.Words("直行", "左转", "右转", "转弯", "straight", "turn"),
                (spec, band) =>
                {
                    bool right = spec.Is("dir", "right");
                    return Ban(band, c =>
                    {
                        Figure(right ? Glyphs.Mirror(Glyphs.P_StraightLeft) : Glyphs.P_StraightLeft)(c);
                        Attach(c, spec, right ? 14.5f : 62.5f, 44.5f, 23f, 12f);
                    });
                },
                new[] { Direction("禁止直行和向左转弯（禁27）", "Straight and left (禁27)", "禁止直行和向右转弯（禁28）", "Straight and right (禁28)"), AttachField() });
            yield return Ban("gb.prohib.29", "禁29", "禁止掉头", "No U-turn", "jzdt", Glyphs.P_UTurn, "掉头", "调头", "回转", "u-turn");

            // 禁30 禁止超车 (5.27, Fig. E.30): the overtaking figure over a red line down the middle (6.7 % of D).
            yield return StandardSigns.Make("gb.prohib.30", "禁30", "禁止超车", "No overtaking", G, "jzcc", StandardSigns.Words("超车", "禁止超车", "overtaking"),
                band => Disc(band, GuideColour.Red, GuideColour.White, (p, b) =>
                {
                    float d = kD[b], w = 0.067f * d, r = d * 0.5f - kC[b] - kA[b] * 0.5f;
                    p.Elements.Add(GuideKit.Rect(d * 0.5f - w * 0.5f, d * 0.5f - r, w, 2f * r, GuideColour.Red));
                }, Figure(Glyphs.P_Overtaking)));

            // 禁31 解除禁止超车 (5.28, Fig. E.31): white ground, black ring, the figure in black, crossed by five thin black stripes.
            yield return StandardSigns.Make("gb.prohib.31", "禁31", "解除禁止超车", "End of no overtaking", G, "jcjzcc", StandardSigns.Words("解除", "超车", "end", "overtaking"),
                band => Disc(band, K, GuideColour.White, null, c =>
                {
                    Figure(Glyphs.P_Overtaking)(c);
                    ReleaseStripes(c);
                }));

            // 禁32 禁止车辆停放 (5.29, Fig. 54, 55, E.32): blue ground, red ring and a red cross; the range arrows of Fig. 55 are a setting. 禁33 has one bar (Fig. 57, E.33).
            yield return StandardSigns.Make("gb.prohib.32", "禁32", "禁止车辆停放", "No parking", G, "jzcltf", StandardSigns.Words("停车", "停放", "禁停", "no parking", "no stopping"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.Blue, (p, b) =>
                {
                    Bar(p, b, true);
                    Bar(p, b, false);
                }, c => RangeArrows(c, spec.Get("range"))),
                new[] { RangeField() });
            yield return StandardSigns.Make("gb.prohib.33", "禁33", "禁止车辆长时停放", "No long-time parking", G, "jzclcstf", StandardSigns.Words("长时", "停放", "临时停车", "no waiting", "parking"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.Blue, (p, b) => Bar(p, b, true), c => RangeArrows(c, spec.Get("range"))),
                new[] { RangeField() });

            yield return Ban("gb.prohib.34", "禁34", "禁止鸣喇叭", "No horns", "jzmlb", Glyphs.P_Horn, "鸣笛", "喇叭", "禁鸣", "horn");

            // 禁35 to 禁40: the limits. The number is a setting with the standard's example as the default; a decimal part is set smaller, the unit smaller still (Fig. E.35 to E.41).
            yield return StandardSigns.Make("gb.prohib.35", "禁35", "限制宽度", "Width limit", G, "xzkd", StandardSigns.Words("限宽", "宽度", "width"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, (p, b) =>
                {
                    Wedge(p, b, 26f, 50f, 151.8f, 208.2f);
                    Wedge(p, b, 74f, 50f, -28.2f, 28.2f);
                }, c => Value(c, spec.Get("width"), "m", 50.25f, 69f, 38.5f, 0.65f, 13f, 0.04f, 50f, K)),
                new[] { GuideField.Digits("width", "限制宽度（m）", "Width (m)", "3", 4) });
            yield return StandardSigns.Make("gb.prohib.36", "禁36", "限制高度", "Height limit", G, "xzgd", StandardSigns.Words("限高", "高度", "height"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, (p, b) =>
                {
                    Wedge(p, b, 50f, 26f, -118.2f, -61.8f);
                    Wedge(p, b, 50f, 74f, 61.8f, 118.2f);
                }, c => Value(c, spec.Get("height"), "m", 50f, 66f, 31.5f, 0.65f, 11.5f, 0.06f, 64f, K)),
                new[] { GuideField.Digits("height", "限制高度（m）", "Height (m)", "3.5", 4) });
            yield return StandardSigns.Make("gb.prohib.37", "禁37", "限制质量", "Weight limit", G, "xzzl", StandardSigns.Words("限重", "质量", "吨", "weight", "mass"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, null, c => Value(c, spec.Get("mass"), "t", 50f, 70.5f, 41f, 0.65f, 24.5f, 0.07f, 66f, K)),
                new[] { GuideField.Digits("mass", "限制质量（t）", "Mass (t)", "10", 4) });
            yield return StandardSigns.Make("gb.prohib.38", "禁38", "限制轴重", "Axle weight limit", G, "xzzz", StandardSigns.Words("轴重", "限重", "axle", "weight"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, null, c =>
                {
                    Value(c, spec.Get("axle"), "t", 49.75f, 53.5f, 31f, 0.65f, 19.5f, 0.07f, 52f, K);
                    Figure(Glyphs.P_Axle)(c);
                }),
                new[] { GuideField.Digits("axle", "限制轴重（t）", "Axle weight (t)", "10", 4) });

            // 禁39 限制速度 (5.36, Fig. E.39, E.40): the number is a setting. The old test sign "gb.speed60" is this sign at 60.
            yield return StandardSigns.Make("gb.prohib.39", "禁39", "限制速度", "Speed limit", G, "xzsd", StandardSigns.Words("限速", "速度", "speed", "limit"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, null, c => Speed(c, spec.Get("limit"), K, 1f)),
                new[] { GuideField.Digits("limit", "限速值（km/h）", "Limit (km/h)", "60", 3) }, "gb.speed60");

            // 禁40 解除限制速度 (5.37, Fig. E.41): black ring, black number, five thin black stripes.
            yield return StandardSigns.Make("gb.prohib.40", "禁40", "解除限制速度", "End of speed limit", G, "jcxzsd", StandardSigns.Words("解除", "限速", "end", "speed"),
                (spec, band) => Disc(band, K, GuideColour.White, null, c =>
                {
                    Speed(c, spec.Get("limit"), K, 37.5f / 41f);
                    ReleaseStripes(c);
                }),
                new[] { GuideField.Digits("limit", "解除的限速值（km/h）", "Limit that ends (km/h)", "40", 3) });

            // 禁41 停车检查 (5.38, Fig. 67, 68 b, E.42): a black bar and 检查; the reason (口岸, ...) may be written above the bar, the bar staying where it is.
            yield return StandardSigns.Make("gb.prohib.41", "禁41", "停车检查", "Stop for inspection", G, "tcjc", StandardSigns.Words("检查", "停车检查", "口岸", "inspection", "check"),
                (spec, band) => Disc(band, GuideColour.Red, GuideColour.White, null, c =>
                {
                    c.Rect(K, 18f, 44.5f, 64.5f, 11f, 5.5f);
                    c.Cells("检查", K, 30f, 60.2f, 16.5f, 7f);
                    string reason = spec.Get("reason");
                    if (reason.Length > 0)
                    {
                        float[] ratio = { 0f, 0f, 0.5f, 0.25f, 0.15f };
                        int n = Math.Min(4, reason.Length);
                        float cell = Math.Min(16.5f, 58f / (n + (n - 1) * ratio[n]));
                        float gap = cell * ratio[n], w = n * cell + (n - 1) * gap;
                        c.Cells(reason.Substring(0, n), K, 50f - w * 0.5f, 34f - cell * 0.5f, cell, gap);
                    }
                }),
                new[] { GuideField.Text("reason", "横杠上方的说明（如口岸）", "Reason above the bar (e.g. 口岸)", "", 4) });

            // 禁42 禁止危险物品运输车辆驶入 (5.39, Fig. E.43): a car seen from the front with an explosion over it (the cloud in yellow).
            yield return StandardSigns.Make("gb.prohib.42", "禁42", "禁止危险物品运输车辆驶入", "No entry for vehicles carrying dangerous goods", G, "jzwxwpysclsr",
                StandardSigns.Words("危险品", "危化品", "易燃易爆", "dangerous goods", "hazmat"),
                band => Ban(band, c =>
                {
                    c.Pic(Glyphs.P_Blast, GuideColour.Yellow, Glyphs.P_FrameX, Glyphs.P_FrameY, Glyphs.P_FrameSide, Glyphs.P_FrameSide);
                    c.Pic(Glyphs.P_BlastRays, K, Glyphs.P_FrameX, Glyphs.P_FrameY, Glyphs.P_FrameSide, Glyphs.P_FrameSide);
                    c.Fit(Glyphs.P_CarFront, K, 29.8f, 44.8f, 40.2f, 32.7f);
                }));

            // 禁43 to 禁48 (5.40, Fig. 70 to 75, E.44 to E.48): the zone signs.
            yield return StandardSigns.Make("gb.prohib.43", "禁43", "区域限制速度", "Zone speed limit", G, "qyxzsd", StandardSigns.Words("区域", "限速", "zone", "speed"),
                (spec, band) => Zone(band, false, c => ZoneRound(c, false, s => Speed(s, spec.Get("limit"), K, 1f), 0)),
                new[] { GuideField.Digits("limit", "限速值（km/h）", "Limit (km/h)", "30", 3) });
            yield return StandardSigns.Make("gb.prohib.44", "禁44", "区域限制速度解除", "End of zone speed limit", G, "qyxzsdjc", StandardSigns.Words("区域", "限速", "解除", "zone", "end"),
                (spec, band) => Zone(band, true, c => ZoneRound(c, true, s => Speed(s, spec.Get("limit"), K, 1f), 0)),
                new[] { GuideField.Digits("limit", "解除的限速值（km/h）", "Limit that ends (km/h)", "30", 3) });
            yield return StandardSigns.Make("gb.prohib.45", "禁45", "区域禁止车辆长时停放", "Zone: no long-time parking", G, "qyjzclcstf", StandardSigns.Words("区域", "长时", "停放", "zone", "parking"),
                band => Zone(band, false, c => ZoneRound(c, false, null, 1)));
            yield return StandardSigns.Make("gb.prohib.46", "禁46", "区域禁止车辆长时停放解除", "End of zone no long-time parking", G, "qyjzclcstfjc", StandardSigns.Words("区域", "长时", "停放", "解除", "end"),
                band => Zone(band, true, c => ZoneRound(c, true, null, 1)));
            yield return StandardSigns.Make("gb.prohib.47", "禁47", "区域禁止车辆停放", "Zone: no parking", G, "qyjzcltf", StandardSigns.Words("区域", "停放", "禁停", "zone", "no parking"),
                band => Zone(band, false, c => ZoneRound(c, false, null, 2)));
            yield return StandardSigns.Make("gb.prohib.48", "禁48", "区域禁止车辆停放解除", "End of zone no parking", G, "qyjzcltfjc", StandardSigns.Words("区域", "停放", "解除", "zone", "end"),
                band => Zone(band, true, c => ZoneRound(c, true, null, 2)));
        }

        // ------------------------------------------------------------------ round signs

        /// <summary>
        /// A round sign of Table 7: a white board of diameter D, a ring a wide of the ring colour at the liner c from the edge, the field inside it; then what lies under the figure (the bar, which
        /// meets the ring and is not part of the figure), then the figure on a canvas over the whole sign, in units of D/100. The safe region is the inside of the ring.
        /// </summary>
        private static GuideBoard Disc(int band, GuideColour ring, GuideColour field, Action<GuidePart, int> under, Action<Canvas> art)
        {
            band = SignStyles.Band(band);
            float d = kD[band], a = kA[band], c = kC[band];
            var part = new GuidePart { X = 0f, Y = 0f, W = d, H = d, Shape = GuidePartShape.Circle, Ground = GuideColour.White, Radius = d * 0.5f };
            part.Elements.Add(GuideKit.Oval(c, c, d - 2f * c, d - 2f * c, ring));
            part.Elements.Add(GuideKit.Oval(c + a, c + a, d - 2f * (c + a), d - 2f * (c + a), field));
            part.Safe = CircleSafe(d, d * 0.5f - c - a);
            under?.Invoke(part, band);
            art?.Invoke(new Canvas(part, 0f, 0f, d, d));
            var board = new GuideBoard { H = 0f };
            board.Parts.Add(part);
            return board;
        }

        /// <summary>A circle as x, y pairs (48 corners, a little inside the radius) round the middle of a d x d part: the safe region of a round sign.</summary>
        private static float[] CircleSafe(float d, float radius)
        {
            var points = new float[96];
            float r = radius * 0.998f;
            for (int i = 0; i < 48; i++)
            {
                double a = 2.0 * Math.PI * i / 48;
                points[i * 2] = d * 0.5f + r * (float)Math.Cos(a);
                points[i * 2 + 1] = d * 0.5f + r * (float)Math.Sin(a);
            }

            return points;
        }

        /// <summary>The red bar at 45 degrees (Fig. 1 c), b wide, its square ends in the middle of the ring; down: from the top left to the bottom right, else from the bottom left to the top right.</summary>
        private static void Bar(GuidePart part, int band, bool down)
        {
            float d = kD[band], m = d * 0.5f, reach = (d * 0.5f - kC[band] - kA[band] * 0.5f) / Sqrt2;
            part.Elements.Add(down ? GuideKit.Bar(m - reach, m - reach, m + reach, m + reach, kB[band], GuideColour.Red)
                                   : GuideKit.Bar(m - reach, m + reach, m + reach, m - reach, kB[band], GuideColour.Red));
        }

        /// <summary>The usual prohibition sign: red ring, the red bar, and the figure over the bar.</summary>
        private static GuideBoard Ban(int band, Action<Canvas> art) => Disc(band, GuideColour.Red, GuideColour.White, (p, b) => Bar(p, b, true), art);

        private static GuideTemplate Ban(string id, string code, string zh, string en, string pinyin, string glyph, params string[] keywords) =>
            StandardSigns.Make(id, code, zh, en, G, pinyin, keywords, band => Ban(band, Figure(glyph)));

        /// <summary>A P_ glyph in black, in the square of the sign its coordinates were written for.</summary>
        private static Action<Canvas> Figure(string glyph) => c => c.Pic(glyph, K, Glyphs.P_FrameX, Glyphs.P_FrameY, Glyphs.P_FrameSide, Glyphs.P_FrameSide);

        // ------------------------------------------------------------------ vehicles as a setting

        private static readonly string[][] kVehicles =
        {
            new[] { "motor", "机动车", "Motor vehicle" },
            new[] { "bus", "大型载客汽车", "Large passenger vehicle" },
            new[] { "car", "小型载客汽车", "Small passenger vehicle" },
            new[] { "truck", "载货汽车", "Goods vehicle" },
            new[] { "trailer", "挂车、半挂车", "Trailer, semi-trailer" },
            new[] { "tractor", "拖拉机", "Tractor" },
            new[] { "tri", "三轮汽车、低速货车", "Three-wheeled vehicle, low-speed lorry" },
            new[] { "moto", "摩托车", "Motorcycle" },
            new[] { "bike", "非机动车", "Non-motor vehicle" },
            new[] { "ebike", "电动自行车", "Electric bicycle" },
            new[] { "animal", "畜力车", "Animal-drawn vehicle" },
            new[] { "trike", "三轮车", "Tricycle" },
            new[] { "rickshaw", "人力客运三轮车", "Pedal tricycle (passengers)" },
            new[] { "cargo", "人力货运三轮车", "Pedal tricycle (goods)" },
            new[] { "hand", "人力车", "Hand cart" }
        };

        private static GuideChoice[] Vehicles(bool none)
        {
            var list = new List<GuideChoice>();
            if (none)
                list.Add(new GuideChoice("none", "不附加", "None"));
            foreach (string[] v in kVehicles)
                list.Add(new GuideChoice(v[0], v[1], v[2]));
            return list.ToArray();
        }

        private static string VehicleGlyph(string key)
        {
            switch (key)
            {
                case "motor": return Glyphs.P_CarFront;
                case "bus": return Glyphs.P_Bus;
                case "car": return Glyphs.P_Car;
                case "truck": return Glyphs.P_Truck;
                case "trailer": return Glyphs.P_Trailer;
                case "tractor": return Glyphs.P_Tractor;
                case "tri": return Glyphs.P_ThreeWheelTruck;
                case "moto": return Glyphs.P_Motorcycle;
                case "bike": return Glyphs.P_Bicycle;
                case "ebike": return Glyphs.P_EBike;
                case "animal": return Glyphs.P_AnimalCart;
                case "trike": return Glyphs.P_Tricycle;
                case "rickshaw": return Glyphs.P_Rickshaw;
                case "cargo": return Glyphs.P_CargoTrike;
                case "hand": return Glyphs.P_HandCart;
                default: return null;
            }
        }

        private static GuideField AttachField() => GuideField.Choice("with", "附加车辆图形", "Added vehicle", "none", Vehicles(true));

        private static GuideField Direction(string leftZh, string leftEn, string rightZh, string rightEn) =>
            GuideField.Choice("dir", "方向", "Direction", "left", new[] { new GuideChoice("left", leftZh, leftEn), new GuideChoice("right", rightZh, rightEn) });

        /// <summary>The vehicle added to a turning ban (Fig. 42), fitted into the box; nothing when none is chosen.</summary>
        private static void Attach(Canvas c, GuideSpec spec, float x, float y, float w, float h)
        {
            string glyph = VehicleGlyph(spec.Get("with"));
            if (glyph != null)
                c.Fit(glyph, K, x, y, w, h);
        }

        // ------------------------------------------------------------------ parking

        private static GuideField RangeField() => GuideField.Choice("range", "范围箭头", "Range arrows", "none", new[]
        {
            new GuideChoice("none", "无（图54）", "None (Fig. 54)"),
            new GuideChoice("a", "下方→（图55 a，路段一端）", "Arrow right below (Fig. 55 a, one end)"),
            new GuideChoice("b", "←与→（图55 b，路段中间）", "Both arrows (Fig. 55 b, in between)"),
            new GuideChoice("c", "上方←（图55 c，路段另一端）", "Arrow left above (Fig. 55 c, other end)")
        });

        /// <summary>The white arrows of Fig. 55: the one pointing left in the upper quarter, the one pointing right in the lower quarter.</summary>
        private static void RangeArrows(Canvas c, string range)
        {
            float[] f = Glyphs.P_ParkArrowFrame;
            if (range == "b" || range == "c")
                c.Pic(Glyphs.P_ParkArrow, GuideColour.White, f[0], f[1], f[2], f[2]);
            if (range == "a" || range == "b")
                c.Pic(Glyphs.Mirror(Glyphs.P_ParkArrow), GuideColour.White, 100.5f - f[0] - f[2], f[1] + 50f, f[2], f[2]);
        }

        // ------------------------------------------------------------------ limits

        // The bundled font's ink widths over the ink height (Noto Sans SC Bold, measured): digits about 0.65, "1" 0.60, the point 0.24, "m" 1.42 and "t" 0.53 of their own height, and
        // 0.115 of the height between two characters.
        private static float InkWidth(string text, float h)
        {
            float w = 0f;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                float k;
                switch (ch)
                {
                    case '1': k = 0.601f; break;
                    case '4': k = 0.730f; break;
                    case '5': k = 0.675f; break;
                    case '7': k = 0.620f; break;
                    case '.': k = 0.243f; break;
                    case 'm': k = 1.415f; break;
                    case 't': k = 0.526f; break;
                    default: k = 0.655f; break;
                }

                w += k * h + (i > 0 ? 0.115f * h : 0f);
            }

            return w;
        }

        /// <summary>
        /// A value and its unit as the drawings set them (Fig. E.35 to E.38): the whole part h high, the point and the decimals fracK h high after it, the unit unitH high (the ink of "m" or "t"),
        /// gap h before the unit, all on one baseline and centred on cx; made smaller (baseline kept) when wider than maxW. An empty value draws nothing.
        /// </summary>
        private static void Value(Canvas c, string value, string unit, float cx, float baseline, float h, float fracK, float unitH, float gap, float maxW, GuideColour colour)
        {
            if (string.IsNullOrEmpty(value))
                return;
            int dot = value.IndexOf('.');
            string whole = dot < 0 ? value : value.Substring(0, dot), frac = dot < 0 ? "" : value.Substring(dot);
            if (frac == ".")
                frac = "";
            if (whole.Length == 0 && frac.Length > 0)
                whole = "0";   // ".5" is written 0.5
            float fh = fracK * h;
            float ww = whole.Length > 0 ? InkWidth(whole, h) : 0f, fw = frac.Length > 0 ? InkWidth(frac, fh) : 0f, uw = unit != null ? InkWidth(unit, unitH) : 0f;
            float g1 = ww > 0f && fw > 0f ? 0.03f * h : 0f, g2 = uw > 0f ? gap * h : 0f;
            float total = ww + g1 + fw + g2 + uw;
            if (total <= 0f)
                return;
            float k = Math.Min(1f, maxW / total);
            float x = cx - total * k * 0.5f;
            if (ww > 0f)
                c.Ink(whole, colour, x, baseline - h * k, ww * k, h * k, GuideAlign.Left);
            x += (ww + g1) * k;
            if (fw > 0f)
                c.Ink(frac, colour, x, baseline - fh * k, fw * k, fh * k, GuideAlign.Left);
            x += (fw + g2) * k;
            if (uw > 0f)
                c.Ink(unit, colour, x, baseline - unitH * k, uw * k, unitH * k, GuideAlign.Left);
        }

        /// <summary>The number of a speed limit (Fig. E.39, E.40): two digits 41 % of D high on the baseline 70.5, three digits 33 % on 66.5; scale: 37.5/41 for the release sign (Fig. E.41).</summary>
        private static void Speed(Canvas c, string value, GuideColour colour, float scale)
        {
            if (value.Length >= 3)
                Value(c, value, null, 50.25f, 50f + 16.5f * scale, 33f * scale, 0.65f, 0f, 0f, 70f * Math.Min(1f, scale + 0.05f), colour);
            else
                Value(c, value, null, 50.25f, 50f + 20.5f * scale, 41f * scale, 0.65f, 0f, 0f, 67f, colour);
        }

        /// <summary>
        /// The black wedge of a width or height limit (Fig. E.35, E.36): from its point (units of D/100) to the inside of the ring between two angles (degrees, y down). Like the bar it meets
        /// the ring, so it is drawn under the figure as a part element whose arc is the edge of the field itself (through the Canvas it would have to stop short of the ring and leave a white hairline).
        /// </summary>
        private static void Wedge(GuidePart part, int band, float px, float py, float a0, float a1)
        {
            float d = kD[band], m = d * 0.5f, r = m - kC[band] - kA[band];
            var p = new List<float> { px * d / 100f, py * d / 100f };
            for (int i = 0; i <= 16; i++)
            {
                double a = (a0 + (a1 - a0) * i / 16.0) * Math.PI / 180.0;
                p.Add(m + r * (float)Math.Cos(a));
                p.Add(m + r * (float)Math.Sin(a));
            }

            part.Elements.Add(GuideKit.Poly(p.ToArray(), K));
        }

        // ------------------------------------------------------------------ the release stripes

        /// <summary>
        /// The five thin black stripes of a release sign (禁31, 禁40; Fig. E.31, E.41, Fig. 50 and 68) from the bottom left to the top right: 1.05 % of D wide, 3.5 apart, the middle one
        /// through the centre, cut at the inside of the ring. They lie in the white field only: the figure stays whole (the printed figures show no white cut through it), so they are
        /// simply black over a black figure. The colour figures put the middle stripe 0.2 above the centre, the drawings 0.6 below it; the centre is taken.
        /// </summary>
        private static void ReleaseStripes(Canvas c)
        {
            for (int i = -2; i <= 2; i++)
                c.Poly(K, StripInCircle(50f, 50f, 39f, i * 3.5f - 0.525f, i * 3.5f + 0.525f));
        }

        /// <summary>The part of a disc between two lines x + y = const (offsets u1, u2 along (1, 1)/sqrt 2 from the centre), as a polygon.</summary>
        private static float[] StripInCircle(float cx, float cy, float r, float u1, float u2)
        {
            var p = new List<float>();
            void At(float u, float s)
            {
                // n = (1, 1)/sqrt 2, t = (1, -1)/sqrt 2.
                p.Add(cx + (u + s) / Sqrt2);
                p.Add(cy + (u - s) / Sqrt2);
            }

            float s1 = (float)Math.Sqrt(Math.Max(0f, r * r - u1 * u1)), s2 = (float)Math.Sqrt(Math.Max(0f, r * r - u2 * u2));
            float f1 = (float)Math.Atan2(s1, u1), f2 = (float)Math.Atan2(s2, u2);
            At(u1, -s1);
            At(u1, s1);
            for (int i = 1; i < 6; i++)
            {
                float f = f1 + (f2 - f1) * i / 6f;
                At(r * (float)Math.Cos(f), r * (float)Math.Sin(f));
            }

            At(u2, s2);
            At(u2, -s2);
            for (int i = 1; i < 6; i++)
            {
                float f = -f2 + (-f1 + f2) * i / 6f;
                At(r * (float)Math.Cos(f), r * (float)Math.Sin(f));
            }

            return p.ToArray();
        }

        // ------------------------------------------------------------------ zones

        /// <summary>
        /// A zone sign (Table 7: 170 x 120 cm with a black edge e of 3 cm at 40 to 70 km/h, 130 x 90 and 2 cm under 40; the standard has no larger size, the faster bands take the 40 to 70 one).
        /// Fig. E.44 to E.48: the round sign 80 (60) cm across, 17.4 (14.6) cm below the edge; 区域 in characters of 35 (25) cm, 10 (8) apart, 13 (11) below the circle; a release sign is
        /// crossed by five thin stripes from the bottom left to the top right corner of the inside.
        /// </summary>
        private static GuideBoard Zone(int band, bool release, Action<Canvas> round)
        {
            bool small = SignStyles.Band(band) == 3;
            float w = small ? 0.90f : 1.20f, h = small ? 1.30f : 1.70f, e = small ? 0.02f : 0.03f;
            float d = small ? 0.60f : 0.80f, top = small ? 0.146f : 0.174f, below = small ? 0.11f : 0.13f, ch = small ? 0.25f : 0.35f, chGap = small ? 0.08f : 0.10f;
            return SignStyles.Rectangle(small ? 3 : 2, w, h, GuideColour.White, K, c =>
            {
                round(new Canvas(c.Part, c.X + (c.W - d) * 0.5f, c.Y + top, d, d));
                float run = 2f * ch + chGap;
                c.Cells("区域", K, (c.W - run) * 0.5f / c.W * 100f, (top + d + below) / c.H * 100f, ch / c.H * 100f, chGap / c.H * 100f, GuideAlign.Left, run / c.W * 100f);
                if (release)
                    ZoneStripes(c, d / 0.80f);
            }, 0f, 0.04f, e);
        }

        /// <summary>
        /// The round sign of a zone board on its own canvas (units of its diameter): a ring 10 % wide (8 of 80 cm, as the round signs of Table 7), red, or black on a release sign; inside, the
        /// number, or for the parking bans the blue field with one red bar (kind 1, long-time parking) or two (kind 2); on a release sign the field is white and the bars black.
        /// </summary>
        private static void ZoneRound(Canvas s, bool release, Action<Canvas> number, int kind)
        {
            GuideColour ring = release ? K : GuideColour.Red;
            s.Oval(ring, 0f, 0f, 100f, 100f);
            s.Oval(kind > 0 && !release ? GuideColour.Blue : GuideColour.White, 10f, 10f, 80f, 80f);
            if (kind > 0)
            {
                // The bars of Table 7 for D 80: 6 cm, 7.5 % of D; their ends in the middle of the ring.
                float reach = 45f / Sqrt2;
                s.Bar(ring, 50f - reach, 50f - reach, 50f + reach, 50f + reach, 7.5f);
                if (kind == 2)
                    s.Bar(ring, 50f - reach, 50f + reach, 50f + reach, 50f - reach, 7.5f);
            }

            number?.Invoke(s);
        }

        /// <summary>
        /// The release stripes of a zone board (Fig. 71, 73, 75, E.45 to E.48): five, parallel to the diagonal of the inside from the bottom left to the top right corner, 0.84 cm wide and 2.8 cm
        /// apart (as on the 80 cm sign), cut at the inside; black over the round sign and 区域, which stay whole as on the printed figures.
        /// </summary>
        private static void ZoneStripes(Canvas c, float scale)
        {
            float w = c.W, h = c.H, len = (float)Math.Sqrt(w * w + h * h);
            // Along the diagonal (w, -h) and across it (h, w), in metres from the bottom left corner of the inside.
            float tx = w / len, ty = -h / len, nx = h / len, ny = w / len;
            float pitch = 0.028f * scale, width = 0.0084f * scale;
            for (int i = -2; i <= 2; i++)
            {
                float[] clipped = GuideKit.ClipToBox(Band(i * pitch - width * 0.5f, i * pitch + width * 0.5f), 0.05f, 0.05f, 99.95f, 99.95f);
                if (clipped.Length >= 6)
                    c.Poly(K, clipped);
            }

            float[] Band(float o0, float o1)
            {
                // A long strip between the offsets o0 and o1 across the diagonal, in canvas units.
                float x0 = 0f, y0 = h, ext = 0.2f;
                float[] m =
                {
                    x0 - tx * ext + nx * o0, y0 - ty * ext + ny * o0,
                    x0 + tx * (len + ext) + nx * o0, y0 + ty * (len + ext) + ny * o0,
                    x0 + tx * (len + ext) + nx * o1, y0 + ty * (len + ext) + ny * o1,
                    x0 - tx * ext + nx * o1, y0 - ty * ext + ny * o1
                };
                for (int k = 0; k < m.Length; k += 2)
                {
                    m[k] = m[k] / w * 100f;
                    m[k + 1] = m[k + 1] / h * 100f;
                }

                return m;
            }
        }
    }
}
