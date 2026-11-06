using System;
using System.Collections.Generic;
using System.Globalization;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Ads and decorations on street-lamp posts (0.12.49; the owner: "加入中式路灯广告牌…多做一些样式。同样支持导入PNG"): banners between two arms (灯杆道旗), light boxes (灯杆灯箱), rigid panels
    /// (宣传牌), Chinese knots and LED figures (中国结, 福字灯…), lanterns (灯笼) and neon figures (霓虹造型灯, Neon), one on a side of the post or a pair, one each side, the arms at right angles to the
    /// road and the faces along it.
    /// Not signs: the rules cap a face at 1.0 x 2.4 m (CJJ/T 149-2021 4.1.8; 0.9 x 1.8 m in Shanghai and Guangzhou) with its lower edge 3 m above a footway (4.5 m over the carriageway); the stock
    /// sizes, arms and hoops are the trade's (scratch/research-lampads/REPORT.md). A banner, a box or a panel shows a print made from its settings or a PNG picture the player imported. Each is a
    /// board whose PoleAd says how it hangs (SignBuilder.PoleAds), so it goes on the game's lamp posts (the attached support, its default) and on a post of its own.
    /// </summary>
    public static class LampAds
    {
        public const string Group = "lampad";
        public const string BannerId = "lamp.banner", BoxId = "lamp.box", PanelId = "lamp.panel", KnotId = "lamp.knot", FigureId = "lamp.figure", LanternId = "lamp.lantern", NeonId = "lamp.neon";

        private static GuideChoice C(string value, string zh, string en) => new GuideChoice(value, zh, en);

        private static readonly GuideChoice[] kForms = { C("pair", "一对（灯杆两侧）", "A pair (both sides of the post)"), C("one", "单侧", "One side") };

        private static readonly GuideChoice[] kStyles =
        {
            C("vert", "竖排大字", "Tall characters"), C("band", "色块标题", "Coloured head"), C("fest", "喜庆（金边、福字）", "Festive (gold edge, fu)"), C("card", "简约", "Plain"), C("wide", "横排大字", "Characters across"),
            C("pic", "导入的图片", "Imported picture")
        };

        private static readonly GuideChoice[] kColours =
        {
            C("red", "中国红", "China red"), C("dred", "深红", "Dark red"), C("gold", "金黄", "Gold"), C("yellow", "黄色", "Yellow"), C("blue", "蓝色", "Blue"), C("navy", "深蓝", "Navy"),
            C("green", "绿色", "Green"), C("orange", "橙色", "Orange"), C("white", "白色", "White"), C("black", "黑色", "Black")
        };

        private static readonly GuideChoice[] kFits = { C("cover", "裁切填满", "Fill, cropped"), C("contain", "完整显示（留边）", "Whole picture (margins)"), C("fill", "拉伸填满", "Fill, stretched") };

        private static readonly GuideChoice[] kHardware =
        {
            C("silver", "银色", "Silver"), C("white", "白色", "White"), C("black", "黑色", "Black"), C("gold", "金色", "Gold"), C("red", "红色", "Red")
        };

        /// <summary>The light boxes' designs (0.12.49, LampBoxes; the photos in REPORT.md 9).</summary>
        private static readonly GuideChoice[] kDesigns =
        {
            C("arms", "简约（两臂）", "Plain (two arms)"), C("ruyi", "如意云头（上下发光装饰）", "Cloud crests (lit above and below)"),
            C("knot", "中国结坠（夜间发光）", "Chinese knot under it (lit)"), C("lattice", "圆形·窗格框", "Round, in a lattice frame"),
            C("fret", "回纹框", "Fret frame"), C("fan", "扇形·窗格框·中国结", "Fan in a lattice frame, a knot")
        };

        private static readonly GuideChoice[] kShapes =
        {
            C("rect", "矩形", "Rectangle"), C("round", "圆角", "Round corners"), C("arch", "拱顶", "Arched top"), C("oval", "椭圆", "Oval"), C("circle", "圆形", "Round")
        };

        private static readonly GuideChoice[] kNeonColours =
        {
            C("white", "冷白", "Cool white"), C("warm", "暖白", "Warm white"), C("red", "红色", "Red"), C("gold", "金黄", "Gold"), C("blue", "蓝色", "Blue"), C("pink", "粉色", "Pink"), C("green", "绿色", "Green")
        };

        private static readonly GuideChoice[] kMotifs =
        {
            C("shanshui", "山水（山、云、浪）", "Hills, clouds and waves"), C("sun", "红日", "Red sun"), C("moon", "明月祥云", "Moon and cloud"), C("clouds", "祥云", "Lucky clouds"),
            C("waves", "海浪", "Waves"), C("plum", "梅花", "Plum blossom"), C("none", "无图案", "None")
        };

        private static readonly GuideChoice[] kMotifs2 = Same(kMotifs);

        private static GuideChoice[] Same(GuideChoice[] choices)
        {
            var all = new List<GuideChoice> { C("same", "与第一个相同", "As the first") };
            all.AddRange(choices);
            return all.ToArray();
        }

        // ------------------------------------------------------------------ sizes (cm; see the class note)

        /// <summary>The banner's default width and height (the trade's 60 x 180) and its lower edge above the ground (cm).</summary>
        public const float BannerW = 60f, BannerH = 180f, BannerY = 300f;

        /// <summary>The light box's default width, height and depth (60 x 120 is a stock size; 10 to 20 cm deep in the photos), its lower edge (cm).</summary>
        public const float BoxW = 60f, BoxH = 120f, BoxD = 12f, BoxY = 300f;

        /// <summary>
        /// A banner's arms (m): slotted aluminium bars of 8 x 4 cm (the hem slides into the slot) whose length is twice the banner's width and 15 cm (they reach ArmPast past its outer edge), with end caps;
        /// and the hoops' height (10 to 15 cm).
        /// </summary>
        public const float ArmHeight = 0.08f, ArmDepth = 0.04f, ArmPast = 0.04f, HoopHeight = 0.12f;

        private static float F(GuideSpec s, string key, float fallback, float min, float max)
        {
            double v = s.Number(key, fallback);
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : (float)Math.Max(min, Math.Min(max, v));
        }

        public static GuideColour Colour(string name, GuideColour fallback)
        {
            switch (name)
            {
                case "red": return GuideColour.Red;
                case "dred": return GuideColour.DarkRed;
                case "gold": return GuideColour.Gold;
                case "yellow": return GuideColour.Yellow;
                case "blue": return GuideColour.Blue;
                case "navy": return GuideColour.Navy;
                case "green": return GuideColour.Green;
                case "orange": return GuideColour.Orange;
                case "white": return GuideColour.White;
                case "black": return GuideColour.Black;
                default: return fallback;
            }
        }

        private static SolidColour Hardware(GuideSpec s) =>
            s.Is("hw", "white") ? SolidColour.White : s.Is("hw", "black") ? SolidColour.Black : s.Is("hw", "gold") ? SolidColour.Gold : s.Is("hw", "red") ? SolidColour.Red : SolidColour.Steel;

        /// <summary>The fields of a print and of a picture: a style, two lines of text, three colours; the imported picture's key, size and fill (set by the panel's picture list).</summary>
        private static IEnumerable<GuideField> PrintFields(string title, string sub, string ground, string ink, string accent)
        {
            yield return GuideField.Choice("style", "样式", "Style", "vert", kStyles).In("画面", "Print");
            yield return GuideField.Text("title", "主标题", "Title", title, 12, s => !s.Is("style", "pic"));
            yield return GuideField.Text("sub", "副标题", "Subtitle", sub, 16, s => !s.Is("style", "pic"));
            yield return GuideField.Choice("bg", "底色", "Ground", ground, kColours);
            yield return GuideField.Choice("ink", "字色", "Letters", ink, kColours, s => !s.Is("style", "pic"));
            yield return GuideField.Choice("accent", "点缀色", "Accent", accent, kColours, s => !s.Is("style", "pic"));
            yield return GuideField.Choice("fit", "图片填充", "Picture", "cover", kFits, s => s.Is("style", "pic"));
            // As a custom sign's (0.12.30, 0.12.31): kept, the ground shows through the picture's clear parts; cut, the ad is only where the picture is opaque.
            yield return GuideField.Flag("cut", "按图片透明处裁掉", "Cut where the picture is clear", false, s => s.Is("style", "pic"));
            yield return GuideField.Text(PictureKey, "（内部）图片", "(internal) picture", "", 16, s => false);
            yield return GuideField.Digits("pw", "（内部）图片宽", "(internal) picture width", "", 5, s => false);
            yield return GuideField.Digits("ph", "（内部）图片高", "(internal) picture height", "", 5, s => false);
        }

        /// <summary>The field of the imported picture's key (the panel sets it, with pw and ph): a template that has it takes pictures.</summary>
        public const string PictureKey = "img";

        private static GuideField[] Fields(params IEnumerable<GuideField>[] groups)
        {
            var all = new List<GuideField>();
            foreach (IEnumerable<GuideField> g in groups)
                all.AddRange(g);
            return all.ToArray();
        }

        /// <summary>
        /// How it is mounted: one or a pair, the lower edge, the room to the post, the hardware's colour. beside (0.12.49, a light box that can be round the post): whether it is beside the post; the
        /// lower edge then comes first, so that the section keeps its heading when the pair and the room are hidden.
        /// </summary>
        private static IEnumerable<GuideField> Mount(float y, string gap = "3", Func<GuideSpec, bool> beside = null)
        {
            GuideField form = GuideField.Choice("form", "挂法", "Hung", "pair", kForms, beside);
            GuideField lower = GuideField.Digits("y", "下沿离地（cm）", "Lower edge (cm)", y.ToString(CultureInfo.InvariantCulture), 3);
            GuideField room = GuideField.Digits("gap", "离灯杆（cm）", "From the post (cm)", gap, 2, beside);
            GuideField hardware = GuideField.Choice("hw", "支架颜色", "Hardware", "silver", kHardware);
            return beside == null ? new[] { form.In("安装", "Mounting"), lower, room, hardware } : new[] { lower.In("安装", "Mounting"), form, room, hardware };
        }

        public static IEnumerable<GuideTemplate> All()
        {
            yield return T(BannerId, "道旗", "灯杆道旗", "Lamp-post banner", "dgdq", new[] { "道旗", "灯杆旗", "刀旗", "banner", "flag" }, Fields(new[]
            {
                GuideField.Digits("w", "宽（cm）", "Width (cm)", BannerW.ToString(CultureInfo.InvariantCulture), 3).In("尺寸", "Size"),
                GuideField.Digits("h", "高（cm）", "Height (cm)", BannerH.ToString(CultureInfo.InvariantCulture), 3)
            }, PrintFields("文明城市", "共建共享", "red", "gold", "gold"), Mount(BannerY)), Banner);
            yield return T(BoxId, "灯箱", "灯杆灯箱", "Lamp-post light box", "dgdx", new[] { "灯箱", "广告", "light box", "ad" }, Fields(new[]
            {
                GuideField.Choice("design", "款式", "Design", "arms", kDesigns).In("尺寸", "Size"),
                GuideField.Choice("shape", "外形", "Shape", "rect", kShapes, s => !s.Is("design", "fan") && !s.Is("design", "lattice")),
                GuideField.Digits("w", "宽（cm，圆形为直径）", "Width (cm; a round one's diameter)", BoxW.ToString(CultureInfo.InvariantCulture), 3),
                GuideField.Digits("h", "高（cm）", "Height (cm)", BoxH.ToString(CultureInfo.InvariantCulture), 3, s => !s.Is("design", "fan") && !s.Is("design", "lattice") && !s.Is("shape", "circle")),
                GuideField.Digits("d", "厚（cm）", "Depth (cm)", BoxD.ToString(CultureInfo.InvariantCulture), 2),
                GuideField.Choice("trim", "装饰颜色", "Ornaments' colour", "red", new[] { C("red", "中国红", "China red"), C("gold", "金色", "Gold") }, s => !s.Is("design", "arms") && !s.Is("design", "fret")),
                GuideField.Flag("wrap", "抱柱", "Round the post", false, s => s.Is("design", "arms") || s.Is("design", "ruyi")),
                GuideField.Flag("lit", "夜间点亮", "Lit at night", true)
            }, PrintFields("城市因你而美", "文明出行 礼让行人", "white", "red", "blue"), Mount(BoxY, "6", s => !(s.Flag("wrap") && (s.Is("design", "arms") || s.Is("design", "ruyi"))))), Box);
            yield return T(PanelId, "宣传牌", "灯杆宣传牌", "Lamp-post panel", "dgxcp", new[] { "宣传牌", "公益", "panel" }, Fields(new[]
            {
                GuideField.Digits("w", "宽（cm）", "Width (cm)", "50", 3).In("尺寸", "Size"),
                GuideField.Digits("h", "高（cm）", "Height (cm)", "80", 3)
            }, PrintFields("文明交通", "安全出行", "blue", "white", "yellow"), Mount(300f, "6")), Panel);
            yield return T(KnotId, "中国结", "灯杆中国结", "Chinese knot", "zgj", new[] { "中国结", "春节", "LED", "knot" }, new[]
            {
                GuideField.Digits("size", "高度（cm）", "Height (cm)", "150", 3).In("尺寸", "Size"),
                GuideField.Flag("fu", "中心福字", "Fu in the middle", true),
                GuideField.Flag("lit", "LED 夜间点亮", "LED lit at night", true),
                GuideField.Choice("form", "挂法", "Hung", "pair", kForms).In("安装", "Mounting"),
                GuideField.Digits("y", "下沿离地（cm）", "Lower edge (cm)", "320", 3),
                GuideField.Digits("gap", "离灯杆（cm）", "From the post (cm)", "15", 2),
                GuideField.Choice("hw", "支架颜色", "Hardware", "silver", kHardware)
            }, Knot);
            yield return T(FigureId, "造型灯", "灯杆 LED 造型灯", "Lamp-post LED figure", "zxd", new[] { "造型灯", "福字灯", "梅花", "如意", "祥云", "雪花", "LED" }, new[]
            {
                GuideField.Choice("motif", "造型", "Figure", "fu", new[] { C("fu", "福字灯", "Fu"), C("plum", "梅花", "Plum blossom"), C("cloud", "如意祥云", "Lucky cloud"), C("snow", "雪花", "Snowflake"), C("star", "五角星", "Star") }).In("尺寸", "Size"),
                GuideField.Digits("size", "大小（cm）", "Size (cm)", "80", 3),
                GuideField.Choice("colour", "颜色", "Colour", "red", new[] { C("red", "红色", "Red"), C("gold", "金色", "Gold"), C("white", "暖白", "Warm white"), C("blue", "冰蓝", "Ice blue") }),
                GuideField.Flag("lit", "LED 夜间点亮", "LED lit at night", true),
                GuideField.Choice("form", "挂法", "Hung", "pair", kForms).In("安装", "Mounting"),
                GuideField.Digits("y", "下沿离地（cm）", "Lower edge (cm)", "320", 3),
                GuideField.Digits("gap", "离灯杆（cm）", "From the post (cm)", "15", 2),
                GuideField.Choice("hw", "支架颜色", "Hardware", "silver", kHardware)
            }, Figure);
            yield return T(LanternId, "灯笼", "灯杆灯笼", "Lamp-post lanterns", "dgdl", new[] { "灯笼", "红灯笼", "宫灯", "春节", "lantern" }, new[]
            {
                GuideField.Choice("kind", "样式", "Kind", "round", new[] { C("round", "圆灯笼", "Round lantern"), C("palace", "六角宫灯", "Hexagonal palace lantern"), C("long", "长灯笼", "Long lantern") }).In("灯笼", "Lanterns"),
                GuideField.Digits("size", "直径（cm）", "Diameter (cm)", "60", 3),
                GuideField.Digits("count", "每串个数（1–3）", "On a string (1 to 3)", "1", 1),
                GuideField.Choice("colour", "颜色", "Colour", "red", new[] { C("red", "红色", "Red"), C("gold", "金黄", "Gold") }),
                GuideField.Flag("lit", "夜间点亮", "Lit at night", true),
                GuideField.Choice("form", "挂法", "Hung", "pair", kForms).In("安装", "Mounting"),
                GuideField.Digits("y", "下沿离地（cm）", "Lower edge (cm)", "300", 3),
                GuideField.Digits("gap", "离灯杆（cm）", "From the post (cm)", "20", 2),
                GuideField.Choice("hw", "支架颜色", "Hardware", "silver", kHardware)
            }, Lantern);
            yield return T(NeonId, "霓虹", "灯杆霓虹造型灯", "Lamp-post neon figure", "nhzxd", new[] { "霓虹", "霓虹灯", "造型灯", "图案灯", "灯笼", "宫灯", "夜景", "neon", "LED" }, new[]
            {
                GuideField.Choice("shape", "外形", "Shape", "lantern", new[] { C("lantern", "宫灯形", "Palace lantern"), C("round", "圆形", "Round"), C("haitang", "海棠窗形", "Begonia window"), C("arch", "拱形", "Arched") })
                    .In("造型", "Figure"),
                GuideField.Digits("w", "宽（cm）", "Width (cm)", "70", 3),
                GuideField.Digits("h", "灯身高（cm）", "Body height (cm)", "100", 3, s => !s.Is("shape", "round")),
                GuideField.Flag("tassel", "如意结和流苏", "Ruyi knot and tassel", true),
                GuideField.Choice("motif", "图案", "Pattern", "shanshui", kMotifs),
                GuideField.Text("title", "文字（竖排）", "Characters (one under another)", "山水之城", 6),
                GuideField.Choice("motif2", "另一个的图案", "The other's pattern", "same", kMotifs2, s => !s.Is("form", "one")),
                GuideField.Text("title2", "另一个的文字", "The other's characters", "", 6, s => !s.Is("form", "one")),
                GuideField.Choice("frame", "灯带颜色", "Neon colour", "white", kNeonColours).In("灯带", "Neon"),
                GuideField.Choice("ink", "文字颜色", "Characters' colour", "white", kNeonColours),
                GuideField.Choice("palette", "图案配色", "Pattern colours", "multi", new[] { C("multi", "彩色", "Many colours"), C("mono", "与灯带同色", "The neon's colour") }),
                GuideField.Digits("line", "灯带宽（mm）", "Strip width (mm)", "15", 2),
                GuideField.Flag("lit", "夜间点亮", "Lit at night", true),
                GuideField.Choice("form", "挂法", "Hung", "pair", kForms).In("安装", "Mounting"),
                GuideField.Digits("y", "下沿离地（cm）", "Lower edge (cm)", "300", 3),
                GuideField.Digits("gap", "离灯杆（cm）", "From the post (cm)", "12", 2),
                GuideField.Choice("hw", "支架颜色", "Hardware", "silver", kHardware)
            }, NeonFigure);
        }

        private static GuideTemplate T(string id, string code, string zh, string en, string pinyin, string[] keywords, GuideField[] fields, Func<GuideSpec, GuideBoard> layout)
        {
            var words = new List<string>(keywords) { "灯杆", "路灯", "广告", "lamp post" };
            return new GuideTemplate { Id = id, Code = code, Zh = zh, En = en, Group = Group, Pinyin = pinyin, Keywords = words.ToArray(), Standalone = false, Fields = fields, Layout = layout };
        }

        /// <summary>The kinds and the prints as presets of the templates: the usual themes of Chinese streets (no national flags, no real brands).</summary>
        public static IEnumerable<GuidePreset> Presets()
        {
            // Banners.
            yield return P("lamp.banner.civic", BannerId, "道旗", "道旗·文明城市", "Banner: civilised city", "wmcs", "title=文明城市;sub=共建共享;bg=red;ink=gold;accent=gold;style=vert", "文明城市");
            yield return P("lamp.banner.welcome", BannerId, "道旗", "道旗·城市欢迎您", "Banner: welcome", "cshyn", "title=欢迎您;sub=魅力之城;bg=blue;ink=white;accent=gold;style=band", "欢迎", "城市");
            yield return P("lamp.banner.spring", BannerId, "道旗", "道旗·新春快乐", "Banner: Happy New Year", "xckl", "title=新春快乐;sub=阖家幸福;bg=red;ink=gold;accent=gold;style=fest", "春节", "新年", "福");
            yield return P("lamp.banner.national", BannerId, "道旗", "道旗·国庆", "Banner: National Day", "gq", "title=喜迎国庆;sub=盛世华诞;bg=dred;ink=gold;accent=gold;style=fest", "国庆");
            yield return P("lamp.banner.midautumn", BannerId, "道旗", "道旗·中秋", "Banner: Mid-Autumn", "zq", "title=花好月圆;sub=中秋团圆;bg=navy;ink=gold;accent=gold;style=vert", "中秋", "团圆");
            yield return P("lamp.banner.waste", BannerId, "道旗", "道旗·垃圾分类", "Banner: sort the rubbish", "ljfl", "title=垃圾分类;sub=从我做起;bg=green;ink=white;accent=yellow;style=band", "垃圾分类", "环保");
            yield return P("lamp.banner.traffic", BannerId, "道旗", "道旗·文明出行", "Banner: courteous traffic", "wmcx", "title=文明出行;sub=礼让行人;bg=blue;ink=white;accent=yellow;style=vert", "交通", "礼让");
            yield return P("lamp.banner.sports", BannerId, "道旗", "道旗·城市马拉松", "Banner: city marathon", "mls", "title=城市马拉松;sub=奔跑吧;bg=orange;ink=white;accent=navy;style=band", "马拉松", "体育");
            yield return P("lamp.banner.tourism", BannerId, "道旗", "道旗·文化旅游节", "Banner: culture and tourism festival", "lyj", "title=文化旅游节;sub=山水相约;bg=green;ink=white;accent=gold;style=card", "旅游", "文化");
            yield return P("lamp.banner.shop", BannerId, "道旗", "道旗·商业广告（示例）", "Banner: shop ad (sample)", "sygg", "title=盛大开业;sub=全场八折;bg=yellow;ink=red;accent=red;style=card", "商业", "开业");
            yield return P("lamp.banner.pic", BannerId, "道旗", "道旗·导入图片", "Banner: your picture", "drtp", "style=pic;bg=white", "图片", "PNG");
            // Light boxes.
            yield return P("lamp.box.civic", BoxId, "灯箱", "灯箱·公益", "Light box: public message", "gydx", "title=城市因你而美;sub=文明出行 礼让行人;bg=white;ink=red;accent=blue;style=band", "公益");
            yield return P("lamp.box.city", BoxId, "灯箱", "灯箱·城市名片（拱顶）", "Light box: city card (arched)", "csmp", "shape=arch;title=魅力新城;sub=宜居宜业;bg=navy;ink=white;accent=gold;style=vert", "城市", "拱顶");
            yield return P("lamp.box.spring", BoxId, "灯箱", "灯箱·春节（椭圆）", "Light box: spring festival (oval)", "cjdx", "shape=oval;w=70;h=100;title=福;sub=新春大吉;bg=red;ink=gold;accent=gold;style=fest", "春节", "椭圆");
            yield return P("lamp.box.shop", BoxId, "灯箱", "灯箱·商业广告（示例）", "Light box: shop ad (sample)", "sydx", "shape=round;title=品质生活;sub=热线 400-000-0000;bg=white;ink=black;accent=orange;style=card", "商业");
            yield return P("lamp.box.pic", BoxId, "灯箱", "灯箱·导入图片", "Light box: your picture", "dxtp", "style=pic;bg=white", "图片", "PNG");
            // Light boxes of the designs (0.12.49, LampBoxes; the photos in REPORT.md 9).
            yield return P("lamp.box.ruyi", BoxId, "灯箱", "灯箱·如意云头（夜间红光）", "Light box: cloud crests (red at night)", "ryyt",
                "design=ruyi;shape=round;w=60;h=110;d=15;title=文明城市;sub=共建美好家园;bg=white;ink=red;accent=red;style=band", "如意", "云头", "中国风");
            yield return P("lamp.box.wrap", BoxId, "灯箱", "抱柱灯箱·如意云头", "Box round the post: cloud crest", "bzdx",
                "design=ruyi;wrap=1;shape=round;w=70;h=110;title=魅力新城;sub=宜居宜业;bg=navy;ink=white;accent=gold;style=vert", "抱柱", "如意");
            yield return P("lamp.box.wrapplain", BoxId, "灯箱", "抱柱灯箱·简约", "Box round the post: plain", "bzdxjy",
                "design=arms;wrap=1;shape=rect;w=60;h=120;title=绿色出行;sub=低碳生活 从我做起;bg=green;ink=white;accent=yellow;style=card", "抱柱");
            yield return P("lamp.box.knot", BoxId, "灯箱", "灯箱·圆形中国结（夜间红光）", "Light box: round, Chinese knot (red at night)", "yxzgj",
                "design=knot;shape=circle;w=70;d=12;title=诚信;sub=文明城市 你我共建;bg=red;ink=gold;accent=gold;style=wide", "中国结", "圆形");
            yield return P("lamp.box.lattice", BoxId, "灯箱", "灯箱·圆形窗格框", "Light box: round in a lattice frame", "ckyx",
                "design=lattice;w=70;d=10;gap=2;hw=red;title=和谐;sub=邻里守望 和睦相处;bg=white;ink=red;accent=red;style=wide", "窗格", "回纹", "圆形");
            yield return P("lamp.box.fret", BoxId, "灯箱", "灯箱·回纹框（公益）", "Light box: fret frame (public message)", "hwk",
                "design=fret;shape=rect;w=60;h=110;d=8;gap=18;hw=black;title=崇德向善;sub=明礼守法;bg=white;ink=black;accent=dred;style=vert", "回纹", "公益");
            yield return P("lamp.box.fan", BoxId, "灯箱", "扇形灯箱·中国结（红）", "Fan light box with a knot (red)", "sxdx",
                "design=fan;w=90;d=10;gap=4;hw=red;trim=red;title=公正;sub=社会和谐;bg=white;ink=red;accent=red;style=wide", "扇形", "中国结", "窗格");
            yield return P("lamp.box.fangold", BoxId, "灯箱", "扇形灯箱·中国结（金）", "Fan light box with a knot (gold)", "sxdxj",
                "design=fan;w=90;d=10;gap=4;hw=gold;trim=gold;title=富强;sub=国泰民安;bg=red;ink=gold;accent=gold;style=wide", "扇形", "中国结");
            // Panels.
            yield return P("lamp.panel.traffic", PanelId, "宣传牌", "宣传牌·文明交通", "Panel: safe traffic", "wmjt", "title=文明交通;sub=安全出行", "交通");
            yield return P("lamp.panel.community", PanelId, "宣传牌", "宣传牌·社区公益", "Panel: community", "sqgy", "title=邻里和谐;sub=共建美好家园;bg=green;ink=white;accent=yellow;style=card", "社区");
            yield return P("lamp.panel.pic", PanelId, "宣传牌", "宣传牌·导入图片", "Panel: your picture", "xcptp", "style=pic;bg=white", "图片", "PNG");
            // Decorations.
            yield return P("lamp.knot.pair", KnotId, "中国结", "中国结（一对）", "Chinese knots (a pair)", "zgjyd", "form=pair", "春节");
            yield return P("lamp.knot.one", KnotId, "中国结", "中国结（单侧）", "Chinese knot (one side)", "zgjdc", "form=one", "春节");
            yield return P("lamp.knot.plain", KnotId, "中国结", "中国结（无福字）", "Chinese knot (plain)", "zgjwfz", "fu=0;size=70", "春节");
            yield return P("lamp.figure.fu", FigureId, "造型灯", "福字灯", "LED fu character", "fzd", "motif=fu", "福", "春节");
            yield return P("lamp.figure.plum", FigureId, "造型灯", "梅花造型灯", "LED plum blossom", "mhd", "motif=plum;colour=red", "梅花");
            yield return P("lamp.figure.cloud", FigureId, "造型灯", "如意祥云灯", "LED lucky cloud", "xyd", "motif=cloud;colour=gold", "祥云", "如意");
            yield return P("lamp.figure.snow", FigureId, "造型灯", "雪花造型灯", "LED snowflake", "xhd", "motif=snow;colour=blue", "雪花", "冬季");
            yield return P("lamp.figure.star", FigureId, "造型灯", "星形造型灯", "LED star", "xxd", "motif=star;colour=gold;form=one", "星");
            yield return P("lamp.lantern.pair", LanternId, "灯笼", "红灯笼（一对）", "Red lanterns (a pair)", "hdlyd", "form=pair", "春节");
            yield return P("lamp.lantern.string", LanternId, "灯笼", "红灯笼（三连串）", "Red lanterns (strings of three)", "hdlsl", "count=3;size=45", "春节", "串");
            yield return P("lamp.lantern.palace", LanternId, "灯笼", "六角宫灯", "Palace lanterns", "lygd", "kind=palace;colour=red;size=55", "宫灯");
            yield return P("lamp.lantern.long", LanternId, "灯笼", "长灯笼（金黄）", "Long lanterns (gold)", "cdl", "kind=long;colour=gold;size=40;count=2", "长灯笼");
            // Neon figures: the first after the owner's photo (hills and waves on one side, a red sun on the other).
            yield return P("lamp.neon.shanshui", NeonId, "霓虹", "霓虹宫灯·山水（一对）", "Neon lanterns: hills and waves (a pair)", "nhgdss", "motif=shanshui;title=山水之城;motif2=sun;title2=宜居新城", "宫灯", "山水");
            yield return P("lamp.neon.spring", NeonId, "霓虹", "霓虹宫灯·新春", "Neon lanterns: New Year", "nhxc", "frame=red;ink=gold;motif=clouds;title=新春快乐;title2=万事如意", "春节", "新年");
            yield return P("lamp.neon.moon", NeonId, "霓虹", "霓虹海棠窗·中秋", "Neon begonia window: Mid-Autumn", "nhzq", "shape=haitang;w=80;h=110;frame=gold;ink=warm;motif=moon;title=花好月圆", "中秋", "月亮");
            yield return P("lamp.neon.plum", NeonId, "霓虹", "霓虹拱窗·梅花", "Neon arch: plum blossom", "nhmh", "shape=arch;frame=pink;motif=plum;title=梅香", "梅花");
            yield return P("lamp.neon.waves", NeonId, "霓虹", "霓虹宫灯·海浪", "Neon lanterns: waves", "nhhl", "frame=blue;motif=waves;title=文明城市", "海浪", "城市");
            yield return P("lamp.neon.fu", NeonId, "霓虹", "霓虹圆灯·福", "Neon round lanterns: fu", "nhfu", "shape=round;frame=red;ink=gold;motif=none;title=福", "福", "春节");
            yield return P("lamp.neon.warm", NeonId, "霓虹", "霓虹宫灯·暖白单色", "Neon lanterns: warm white", "nhnb", "frame=warm;ink=warm;palette=mono;title=城市之光", "暖白");
        }

        private static GuidePreset P(string id, string template, string code, string zh, string en, string pinyin, string overrides, params string[] keywords)
        {
            var words = new List<string>(keywords) { "灯杆", "路灯" };
            return new GuidePreset { Id = id, Template = template, Code = code, Zh = zh, En = en, Pinyin = pinyin, Overrides = overrides, Keywords = words.ToArray() };
        }

        // ------------------------------------------------------------------ the layouts

        /// <summary>
        /// A board of one part or of a pair (one each side of the post, both facing the way the board does), with how it hangs (PoleAd: the builder puts each part gap from the post's surface). The
        /// parts are laid out side by side for the picture, the second 2 gap right of the first. wrap (0.12.49): one part round the post.
        /// </summary>
        private static GuideBoard Hung(GuideSpec s, PoleAdKind kind, Func<int, GuidePart> make, float depth, bool twoSided, bool lit, float defaultY, bool wrap = false)
        {
            bool pair = !wrap && !s.Is("form", "one");
            float gap = F(s, "gap", 6f, 0f, 80f) / 100f;
            var board = new GuideBoard { H = 0.2f };
            GuidePart first = make(0);
            board.Parts.Add(first);
            if (pair)
            {
                GuidePart second = make(1);
                second.X = first.W + 2f * gap;
                board.Parts.Add(second);
            }

            SolidColour hardware = Hardware(s);
            board.PoleAd = new PoleAd
            {
                Kind = kind, Gap = gap, Depth = depth, TwoSided = twoSided, Lit = lit, Pair = pair, Hardware = hardware, Edge = hardware, Wrap = wrap,
                Clearance = F(s, "y", defaultY, 50f, 800f) / 100f
            };
            return board;
        }

        private static GuideBoard Banner(GuideSpec s)
        {
            float w = F(s, "w", BannerW, 30f, 120f) / 100f, h = F(s, "h", BannerH, 60f, 300f) / 100f;
            return Hung(s, PoleAdKind.Banner, _ => Printed(s, w, h, 0f, GuidePartShape.RoundRect), 0.004f, true, false, BannerY);
        }

        /// <summary>
        /// A box round the post (抱柱灯箱, dl-63: the post goes through holes in its top and bottom, the box closes round it in two halves) is at least this deep (m): each side of the post of the attached
        /// support (SupportLayouts.HostRadius, wider than any post of ours a lamp-post ad stands on) its 1.2 cm collar, 5 mm of room, a crest's 2.5 cm shell and 5 mm to the face; the old rule's cap
        /// is 0.3 m (CJJ 149-2010 3.2.3).
        /// </summary>
        public const float WrapDepth = 0.28f;

        /// <summary>
        /// A light box of a design (0.12.49, LampBoxes): plain on two arms (into its actual side, an oval's too), with crests of scrolls above and below, with a knot under it, round in a lattice frame,
        /// in a fret frame, or a fan; beside the post or (plain or with crests) round it.
        /// </summary>
        private static GuideBoard Box(GuideSpec s)
        {
            string design = s.Get("design");
            float w = F(s, "w", BoxW, 30f, 150f) / 100f, h = F(s, "h", BoxH, 30f, 250f) / 100f, d = F(s, "d", BoxD, 4f, 40f) / 100f;
            SolidColour trim = s.Is("trim", "gold") ? SolidColour.Gold : SolidColour.Red, glow = trim == SolidColour.Gold ? SolidColour.GlowGold : SolidColour.GlowRed;
            GuideColour trimInk = trim == SolidColour.Gold ? GuideColour.Gold : GuideColour.Red;
            if (design == "fan")
                return Fan(s, Math.Min(1.2f, Math.Max(0.5f, w)), d, trim, trimInk);
            bool wrap = s.Flag("wrap") && (design == "arms" || design == "ruyi");
            string shape = design == "lattice" ? "circle" : s.Get("shape");
            if (shape == "circle")
                h = w;
            if (wrap)
                d = Math.Max(d, WrapDepth);
            float least = Math.Min(w, h), m = 0.05f * least, radius = shape == "round" ? 0.08f * least : 0f;
            List<Point2> outline = null, inset = null;
            if (shape == "arch")
            {
                outline = Arch(w, h);
                inset = Arch(w - 2f * m, h - 2f * m);
            }
            else if (shape == "oval" || shape == "circle")
            {
                outline = Ellipse(w, h, 48);
                inset = Ellipse(w - 2f * m, h - 2f * m, 48);
            }

            // A round box's print in the square inside its frame; a framed box's frame in its hardware's colour (L7), a round one's in its ornaments' (dl-box-q4-t2, q1-t23).
            float side = shape == "circle" ? (w - 2f * m) * (float)Math.Sqrt(0.5) : 0f;
            float px = side > 0f ? 0.5f * (w - side) : m, pw = side > 0f ? side : w - 2f * m;
            float py = side > 0f ? 0.5f * (h - side) : m, ph = side > 0f ? side : h - 2f * m;
            GuideColour frameInk = design == "fret" ? Ink(Hardware(s)) : design == "knot" || design == "lattice" ? trimInk : GuideColour.Steel;
            GuideBoard board = Hung(s, PoleAdKind.Box, _ =>
            {
                GuidePart part = Printed(s, w, h, radius, GuidePartShape.RoundRect, outline, m, frameInk, inset, px, py, pw, ph);
                // An arched, oval or round box is cut to its shape (and a picture cut to its clear parts within it).
                if (outline != null && part.Cut == null)
                    part.Cut = new List<List<Point2>> { outline };
                return part;
            }, d, true, s.Flag("lit"), BoxY, wrap);
            PoleAd ad = board.PoleAd;
            List<Point2> edge = outline ?? GuideKit.RoundRect(w, h, radius);
            switch (design)
            {
                case "ruyi":
                    LampBoxes.Ruyi(ad, edge, w, h, d, trim, glow);
                    break;
                case "knot":
                    LampBoxes.KnotUnder(ad, edge, w, h, d, trim, glow);
                    break;
                case "lattice":
                    LampBoxes.Lattice(ad, w, d, ad.Gap, trim);
                    break;
                case "fret":
                    LampBoxes.Frets(ad, edge, w, h, d, ad.Hardware);
                    break;
            }

            if (wrap)
            {
                ad.WrapTop = LampBoxes.EdgeY(edge, 0f, true);
                ad.WrapBottom = LampBoxes.EdgeY(edge, 0f, false);
            }
            else if (design != "lattice" && design != "fret")
            {
                SideArms(ad, edge, h, d);
            }

            return board;
        }

        /// <summary>Two arms into the side towards the post at 0.3 of the height above and below the middle, their brackets over the edge (as deep as the box and 4 mm more each way).</summary>
        private static void SideArms(PoleAd ad, List<Point2> edge, float h, float depth)
        {
            foreach (float k in new[] { 0.3f, -0.3f })
                ad.Arms.Add(LampBoxes.ArmTo(edge, k * h, Math.Max(0.012f, depth * 0.5f + 0.004f)));
        }

        /// <summary>The fan (LampBoxes.FanDressing): its lit face in the frame, its print across it in the rectangle that fits.</summary>
        private static GuideBoard Fan(GuideSpec s, float wf, float d, SolidColour trim, GuideColour trimInk)
        {
            List<Point2> face = LampBoxes.FanFace(wf, out float faceW, out float faceH, out float middleY, out float printHalf, out float printLow, out float printHigh);
            GuideBoard board = Hung(s, PoleAdKind.Box, _ =>
            {
                GuidePart part = Printed(s, faceW, faceH, 0f, GuidePartShape.RoundRect, face, 0f, trimInk, null, 0.5f * faceW - printHalf, 0.5f * faceH - printHigh, 2f * printHalf,
                    printHigh - printLow);
                if (part.Cut == null)
                    part.Cut = new List<List<Point2>> { face };
                return part;
            }, d, true, s.Flag("lit"), BoxY);
            PoleAd ad = board.PoleAd;
            // The frame is round the face: the room set is from the frame to the post.
            ad.Gap += 0.5f * (wf - faceW);
            LampBoxes.FanDressing(ad, wf, faceW, faceH, middleY, d, trim, ad.Hardware);
            return board;
        }

        private static GuideBoard Panel(GuideSpec s)
        {
            float w = F(s, "w", 50f, 30f, 150f) / 100f, h = F(s, "h", 80f, 30f, 250f) / 100f;
            GuideBoard board = Hung(s, PoleAdKind.Panel, _ => Printed(s, w, h, 0.02f, GuidePartShape.RoundRect, 0.03f * Math.Min(w, h)), 0.02f, true, false, 280f);
            SideArms(board.PoleAd, GuideKit.RoundRect(w, h, 0.02f), h, 0.02f);
            return board;
        }

        /// <summary>The print's colour of a hardware's.</summary>
        private static GuideColour Ink(SolidColour hardware) =>
            hardware == SolidColour.White ? GuideColour.White : hardware == SolidColour.Black ? GuideColour.Black : hardware == SolidColour.Gold ? GuideColour.Gold : hardware == SolidColour.Red ? GuideColour.Red : GuideColour.Steel;

        // ------------------------------------------------------------------ the prints

        /// <summary>
        /// A part of w x h printed by the settings: the imported picture (style pic, or a picture there is), or one of the styles. frame: a band of steel round the edge (a light box's aluminium shows
        /// as the print's edge), 0 for none.
        /// </summary>
        public static GuidePart Printed(GuideSpec s, float w, float h, float radius, GuidePartShape shape, float frame = 0f, List<Point2> outline = null) =>
            Printed(s, w, h, radius, shape, outline, frame, GuideColour.Steel, null, frame, frame, w - 2f * frame, h - 2f * frame);

        /// <summary>
        /// As above with the frame's colour (0.12.49), the frame along the outline when inset is the outline that far in (the outline drawn in the frame's colour, the inset in the ground; else a
        /// rectangle's band), and the print in the rectangle x, y, pw, ph (from the part's top left: the square inside a round box, the rectangle inside a fan).
        /// </summary>
        private static GuidePart Printed(GuideSpec s, float w, float h, float radius, GuidePartShape shape, List<Point2> outline, float frame, GuideColour frameColour, List<Point2> inset,
            float x, float y, float pw, float ph)
        {
            GuideColour ground = Colour(s.Get("bg"), GuideColour.Red), ink = Colour(s.Get("ink"), GuideColour.Gold), accent = Colour(s.Get("accent"), GuideColour.Gold);
            var part = new GuidePart { W = w, H = h, Shape = shape, Radius = radius, Ground = ground, Liner = 0f, Border = 0f };
            string key = s.Get(PictureKey);
            if (s.Is("style", "pic"))
            {
                if (CustomSigns.IsKey(key))
                    AddPicture(part, s, key, outline);
                else
                    Placeholder(part, w, h);
                return part;
            }

            if (frame > 0f && inset != null)
            {
                part.Elements.Add(GuideKit.Poly(InPart(outline, w, h), frameColour));
                part.Elements.Add(GuideKit.Poly(InPart(inset, w, h), ground));
            }
            else if (frame > 0f)
            {
                part.Elements.Add(GuideKit.Rect(0f, 0f, w, h, frameColour));
                part.Elements.Add(GuideKit.Rect(frame, frame, w - 2f * frame, h - 2f * frame, ground));
            }

            string title = (s.Get("title") ?? "").Trim(), sub = (s.Get("sub") ?? "").Trim();
            switch (s.Get("style"))
            {
                case "band":
                    BandStyle(part, title, sub, x, y, pw, ph, ground, ink, accent);
                    break;
                case "fest":
                    FestStyle(part, title, sub, x, y, pw, ph, ground, ink, accent);
                    break;
                case "card":
                    CardStyle(part, title, sub, x, y, pw, ph, ground, ink, accent);
                    break;
                case "wide":
                    WideStyle(part, title, sub, x, y, pw, ph, ink, accent);
                    break;
                default:
                    VertStyle(part, title, sub, x, y, pw, ph, ground, ink, accent);
                    break;
            }

            return part;
        }

        /// <summary>An outline round the part's middle (y up) as the points of a polygon element (from the part's top left, y down).</summary>
        private static float[] InPart(List<Point2> outline, float w, float h)
        {
            var points = new float[outline.Count * 2];
            for (int i = 0; i < outline.Count; i++)
            {
                points[2 * i] = outline[i].X + w * 0.5f;
                points[2 * i + 1] = h * 0.5f - outline[i].Y;
            }

            return points;
        }

        /// <summary>
        /// The imported picture on the part as its fill says (as a custom sign's: CustomSigns.SourceOf crops it); the ground shows through its clear parts and in the margins, or with cut the part is
        /// cut to where it is opaque (CustomSigns.Cut, within outline: the part's own when null). The builder maps a cut part's back as its front, so that the picture fits its cut from behind too.
        /// </summary>
        private static void AddPicture(GuidePart part, GuideSpec s, string key, List<Point2> outline)
        {
            double pw = s.Number("pw", 0.0), ph = s.Number("ph", 0.0);
            float aspect = pw >= 1.0 && ph >= 1.0 ? (float)(ph / pw) : part.H / part.W;
            float bx = 0f, by = 0f, bw = part.W, bh = part.H;
            if (s.Is("fit", "contain"))
            {
                bw = Math.Min(part.W, part.H / aspect);
                bh = bw * aspect;
                bx = (part.W - bw) * 0.5f;
                by = (part.H - bh) * 0.5f;
            }

            var picture = new GuideElement { Kind = GuideElementKind.Image, X = bx, Y = by, W = bw, H = bh, Text = key, Crop = s.Is("fit", "cover") };
            part.Elements.Add(picture);
            CustomMask mask = s.Flag("cut") ? CustomSigns.Masks?.Invoke(key) : null;
            if (mask != null)
            {
                part.Cut = CustomSigns.Cut(part, picture, mask, aspect, outline);
                picture.Opaque = part.Cut != null;
            }
        }

        /// <summary>A part waiting for its picture: grey with a frame and "PNG" in it.</summary>
        private static void Placeholder(GuidePart part, float w, float h)
        {
            part.Ground = GuideColour.LightGray;
            float t = 0.03f * Math.Min(w, h);
            part.Elements.Add(GuideKit.Rect(t, t, w - 2f * t, h - 2f * t, GuideColour.White));
            float ch = Math.Min(0.25f * w, 0.12f * h);
            part.Elements.Add(GuideKit.Ink("PNG", w * 0.15f, h * 0.5f - ch * 0.5f, w * 0.7f, ch, GuideColour.Gray, GuideAlign.Centre));
        }

        /// <summary>Characters one under the other, centred on cx from y, each ch high with a gap; as many as fit in room (the rest are left out).</summary>
        private static float Column(GuidePart part, string text, float cx, float y, float ch, float room, GuideColour colour)
        {
            float gap = 0.12f * ch, used = 0f;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    used += 0.5f * ch;
                    continue;
                }

                if (used + ch > room + 1e-4f)
                    break;
                part.Elements.Add(GuideKit.Cells(c.ToString(), cx - ch * 0.5f, y + used, ch, colour, 0f));
                used += ch + gap;
            }

            return used;
        }

        /// <summary>One line centred in the width, as large as fits (at most ch high).</summary>
        private static float Line(GuidePart part, string text, float x, float y, float width, float ch, GuideColour colour)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            // Latin letters and figures (a telephone number) are drawn by their own width, the Chinese characters in cells.
            float h = LineHeight(text, width, ch);
            part.Elements.Add(IsLatin(text) ? GuideKit.Ink(text, x, y, width, h, colour, GuideAlign.Centre) : GuideKit.Cells(text, x, y, h, colour, 0.05f * h, GuideAlign.Centre, width));
            return h;
        }

        /// <summary>Tall characters (竖排): the title down the middle, as large as the width and the height let it, the subtitle in smaller ones at the foot, a band of the accent at the top and the foot.</summary>
        private static void VertStyle(GuidePart part, string title, string sub, float x, float y, float w, float h, GuideColour ground, GuideColour ink, GuideColour accent)
        {
            float band = 0.06f * h;
            part.Elements.Add(GuideKit.Rect(x, y, w, band, accent));
            part.Elements.Add(GuideKit.Rect(x, y + h - band, w, band, accent));
            // A cloud under the top band (its height is about 0.2 of the width), the title under it.
            float cloudTop = y + band + 0.02f * h, cloudH = 0.21f * w;
            Cloud(part, x + w * 0.5f, cloudTop, w * 0.5f, accent);
            float subRoom = sub.Length > 0 ? 0.30f * h : 0f;
            int n = Math.Max(1, CountInk(title));
            float start = cloudTop + cloudH + 0.03f * h;
            float room = y + h - band - subRoom - 0.02f * h - start;
            float ch = Math.Min(0.62f * w, room / (n * 1.12f));
            float top = start + Math.Max(0f, (room - n * ch * 1.12f) * 0.5f);
            Column(part, title, x + w * 0.5f, top, ch, room, ink);
            if (sub.Length > 0)
            {
                float sch = Math.Min(0.3f * w, (subRoom - 0.04f * h) / Math.Max(1, CountInk(sub)) / 1.12f);
                float sx = x + w * 0.5f, sy = y + h - band - subRoom + 0.02f * h;
                part.Elements.Add(GuideKit.Rect(sx - sch * 0.75f, sy - 0.01f * h, sch * 1.5f, subRoom - 0.02f * h, accent, sch * 0.2f));
                Column(part, sub, sx, sy + 0.01f * h, sch, subRoom - 0.04f * h, ground);
            }
        }

        /// <summary>A head of the accent colour with the title across it (in lines of up to four characters), the subtitle under it and a knot at the foot.</summary>
        private static void BandStyle(GuidePart part, string title, string sub, float x, float y, float w, float h, GuideColour ground, GuideColour ink, GuideColour accent)
        {
            List<string> lines = Wrap(title, 4);
            float lineH = Math.Min(0.8f * w / Math.Max(1, Longest(lines)), 0.5f * h / Math.Max(1, lines.Count) / 1.2f);
            float head = lines.Count * lineH * 1.2f + 0.10f * h;
            part.Elements.Add(GuideKit.Rect(x, y, w, head, accent));
            float ly = y + 0.05f * h;
            foreach (string l in lines)
            {
                Line(part, l, x + 0.06f * w, ly, 0.88f * w, lineH, ground == accent ? ink : ground);
                ly += lineH * 1.2f;
            }

            float sy = y + head + 0.05f * h;
            foreach (string l in Wrap(sub, 6))
                sy += Line(part, l, x + 0.08f * w, sy, 0.84f * w, 0.09f * h, ink) * 1.3f;
            float knot = Math.Min(0.5f * w, h - (sy - y) - 0.06f * h);
            if (knot > 0.1f * w)
                KnotIcon(part, x + w * 0.5f, y + h - 0.04f * h - knot, knot, accent);
        }

        /// <summary>Festive: a gold edge, a 福 in a diamond, the title down the middle, lanterns at the top corners.</summary>
        private static void FestStyle(GuidePart part, string title, string sub, float x, float y, float w, float h, GuideColour ground, GuideColour ink, GuideColour accent)
        {
            float e = 0.03f * Math.Min(w, h);
            part.Elements.Add(GuideKit.Rect(x + e, y + e, w - 2f * e, h - 2f * e, accent));
            part.Elements.Add(GuideKit.Rect(x + 2f * e, y + 2f * e, w - 4f * e, h - 4f * e, ground));
            float d = Math.Min(0.7f * w, 0.3f * h);
            float cx = x + w * 0.5f, dy = y + 4f * e;
            part.Elements.Add(GuideKit.Poly(new[] { cx, dy, cx + d * 0.5f, dy + d * 0.5f, cx, dy + d, cx - d * 0.5f, dy + d * 0.5f }, accent));
            float fu = d * 0.48f;
            part.Elements.Add(GuideKit.Cells("福", cx - fu * 0.5f, dy + d * 0.5f - fu * 0.5f, fu, ground, 0f));
            if (title != "福")
            {
                float top = dy + d + 0.04f * h, room = h - (top - y) - (sub.Length > 0 ? 0.2f * h : 0.06f * h);
                float ch = Math.Min(0.55f * w, room / Math.Max(1, CountInk(title)) / 1.12f);
                Column(part, title, cx, top, ch, room, ink);
            }

            if (sub.Length > 0)
                Line(part, sub, x + 0.1f * w, y + h - 0.16f * h, 0.8f * w, 0.08f * h, ink);
        }

        /// <summary>Plain: a light ground, a strip of the accent down the left, the title and the subtitle across, a line at the foot.</summary>
        private static void CardStyle(GuidePart part, string title, string sub, float x, float y, float w, float h, GuideColour ground, GuideColour ink, GuideColour accent)
        {
            float strip = 0.08f * w;
            part.Elements.Add(GuideKit.Rect(x, y, strip, h, accent));
            float tx = x + strip + 0.06f * w, tw = w - strip - 0.12f * w, ty = y + 0.12f * h;
            foreach (string l in Wrap(title, 4))
                ty += Line(part, l, tx, ty, tw, 0.16f * h, ink) * 1.25f;
            part.Elements.Add(GuideKit.Rect(tx + tw * 0.2f, ty + 0.02f * h, tw * 0.6f, 0.006f * h + 0.004f, accent));
            ty += 0.06f * h;
            foreach (string l in Wrap(sub, 6))
                ty += Line(part, l, tx, ty, tw, 0.07f * h, ink) * 1.3f;
            float dot = Math.Min(0.3f * w, 0.18f * h);
            part.Elements.Add(GuideKit.Oval(x + strip + (w - strip) * 0.5f - dot * 0.5f, y + h - 0.08f * h - dot, dot, dot, accent));
        }

        /// <summary>
        /// Across (横排, 0.12.49: the round and fan boxes of dl-box-q4-t2, q1-t23, q7-t2): the title in one line as large as the width lets it (at most 0.55 of the height), the subtitle in a line of
        /// 0.14 under a short band of the accent, the whole block in the middle.
        /// </summary>
        private static void WideStyle(GuidePart part, string title, string sub, float x, float y, float w, float h, GuideColour ink, GuideColour accent)
        {
            float width = 0.94f * w, th = LineHeight(title, width, 0.55f * h), sh = sub.Length > 0 ? LineHeight(sub, width, 0.14f * h) : 0f;
            float block = th + (sh > 0f ? 0.12f * h + sh : 0f), top = y + 0.5f * (h - block);
            Line(part, title, x + 0.03f * w, top, width, th, ink);
            if (sh <= 0f)
                return;
            part.Elements.Add(GuideKit.Rect(x + 0.35f * w, top + th + 0.05f * h, 0.3f * w, 0.012f * h + 0.003f, accent));
            Line(part, sub, x + 0.03f * w, top + th + 0.12f * h, width, sh, ink);
        }

        /// <summary>How high Line draws a text in the width, at most ch.</summary>
        private static float LineHeight(string text, float width, float ch)
        {
            if (string.IsNullOrEmpty(text))
                return 0f;
            return IsLatin(text) ? Math.Min(ch, width / Math.Max(0.1f, GuideKit.LatinWidth(text, 1f)) * 0.95f) : Math.Min(ch, width / Math.Max(1, text.Length) / 1.05f);
        }

        private static int CountInk(string text)
        {
            int n = 0;
            foreach (char c in text ?? "")
                n += char.IsWhiteSpace(c) ? 0 : 1;
            return n;
        }

        private static int Longest(List<string> lines)
        {
            int n = 1;
            foreach (string l in lines)
                n = Math.Max(n, l.Length);
            return n;
        }

        /// <summary>
        /// A text in lines of about n characters, broken at its spaces first; a longer word into lines of even length (五个字 as 3 + 2, not 4 + 1), and one of Latin letters and figures (a telephone
        /// number) never broken.
        /// </summary>
        public static List<string> Wrap(string text, int n)
        {
            var lines = new List<string>();
            foreach (string word in (text ?? "").Split(new[] { ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (word.Length <= n || IsLatin(word))
                {
                    lines.Add(word);
                    continue;
                }

                int count = (word.Length + n - 1) / n, at = 0;
                for (int i = 0; i < count; i++)
                {
                    int length = (word.Length - at + (count - i) - 1) / (count - i);
                    lines.Add(word.Substring(at, length));
                    at += length;
                }
            }

            return lines;
        }

        private static bool IsLatin(string word)
        {
            foreach (char c in word)
            {
                if (c > 0x2000)
                    return false;
            }

            return true;
        }

        /// <summary>A small lucky cloud (祥云) of three circles and a base, centred on cx with its top at y.</summary>
        private static void Cloud(GuidePart part, float cx, float y, float w, GuideColour colour)
        {
            float r = w * 0.22f;
            part.Elements.Add(GuideKit.Oval(cx - 1.6f * r, y + 0.5f * r, 1.4f * r, 1.4f * r, colour));
            part.Elements.Add(GuideKit.Oval(cx - 0.8f * r, y, 1.6f * r, 1.6f * r, colour));
            part.Elements.Add(GuideKit.Oval(cx + 0.3f * r, y + 0.5f * r, 1.4f * r, 1.4f * r, colour));
            part.Elements.Add(GuideKit.Rect(cx - 1.5f * r, y + 1.25f * r, 3.1f * r, 0.6f * r, colour, 0.3f * r));
        }

        /// <summary>A knot (中国结) as a picture: a diamond with a loop above and a tassel below, size high, its top at y.</summary>
        private static void KnotIcon(GuidePart part, float cx, float y, float size, GuideColour colour)
        {
            float d = size * 0.55f, top = y + size * 0.1f;
            part.Elements.Add(GuideKit.Oval(cx - size * 0.06f, y, size * 0.12f, size * 0.12f, colour));
            part.Elements.Add(GuideKit.Poly(new[] { cx, top, cx + d * 0.5f, top + d * 0.5f, cx, top + d, cx - d * 0.5f, top + d * 0.5f }, colour));
            float t0 = top + d, t1 = y + size;
            part.Elements.Add(GuideKit.Poly(new[] { cx - size * 0.04f, t0, cx + size * 0.04f, t0, cx + size * 0.12f, t1, cx - size * 0.12f, t1 }, colour));
        }

        // ------------------------------------------------------------------ outlines

        /// <summary>The outline of a box with an arched top (a half ellipse as high as a third of the width) as the viewer sees it, round its middle, anticlockwise.</summary>
        public static List<Point2> Arch(float w, float h)
        {
            var points = new List<Point2>();
            float rise = Math.Min(w / 3f, h * 0.4f), straightTop = h * 0.5f - rise;
            points.Add(new Point2(-w * 0.5f, -h * 0.5f));
            points.Add(new Point2(w * 0.5f, -h * 0.5f));
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                double a = Math.PI * i / steps;
                points.Add(new Point2((float)(w * 0.5 * Math.Cos(a)), straightTop + (float)(rise * Math.Sin(a))));
            }

            return points;
        }

        /// <summary>An ellipse of w x h round its middle, anticlockwise.</summary>
        public static List<Point2> Ellipse(float w, float h, int steps = 32)
        {
            var points = new List<Point2>(steps);
            for (int i = 0; i < steps; i++)
            {
                double a = 2.0 * Math.PI * i / steps;
                points.Add(new Point2((float)(w * 0.5 * Math.Cos(a)), (float)(h * 0.5 * Math.Sin(a))));
            }

            return points;
        }

        /// <summary>A regular star or flower: points tips, the outer radius ro and the inner ri, anticlockwise from the top.</summary>
        private static List<Point2> Star(int tips, float ro, float ri, float cy = 0f)
        {
            var points = new List<Point2>();
            for (int i = 0; i < 2 * tips; i++)
            {
                double a = Math.PI * 0.5 + Math.PI * i / tips;
                float r = i % 2 == 0 ? ro : ri;
                points.Add(new Point2((float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
            }

            return points;
        }

        // ------------------------------------------------------------------ decorations

        /// <summary>
        /// A Chinese knot (中国结): a red diamond of four loops with a gold edge and a 福 (or a plain knot), a loop above it and a red tassel below, the board cut to that outline; lit at night when asked
        /// (an LED knot: its face glows red).
        /// </summary>
        private static GuideBoard Knot(GuideSpec s)
        {
            // The trade's knots are about half as wide as they are tall (1.5 x 0.8, 2.0 x 1.0 m).
            float size = F(s, "size", 150f, 30f, 320f) / 100f, w = size * 0.55f;
            bool fu = s.Flag("fu");
            return Hung(s, PoleAdKind.Knot, _ =>
            {
                var part = new GuidePart { W = w, H = size, Shape = GuidePartShape.RoundRect, Radius = 0f, Ground = GuideColour.Red, Liner = 0f, Border = 0f };
                float d = w * 0.92f, cx = w * 0.5f, top = size * 0.07f;
                part.Elements.Add(GuideKit.Poly(new[] { cx, top, cx + d * 0.5f, top + d * 0.5f, cx, top + d, cx - d * 0.5f, top + d * 0.5f }, GuideColour.Gold));
                float k = 0.84f;
                part.Elements.Add(GuideKit.Poly(new[] { cx, top + d * (1f - k) * 0.5f, cx + d * k * 0.5f, top + d * 0.5f, cx, top + d * (1f + k) * 0.5f, cx - d * k * 0.5f, top + d * 0.5f }, GuideColour.Red));
                // The weave: lines parallel to the diamond's sides, from one side to the opposite one.
                float a = d * k * 0.5f, mid = top + d * 0.5f, bar = 0.004f + 0.012f * d;
                for (int i = 1; i < 4; i++)
                {
                    float f = i / 4f;
                    part.Elements.Add(GuideKit.Bar(cx - a + a * f, mid - a * f, cx + a * f, mid + a - a * f, bar, GuideColour.DarkRed));
                    part.Elements.Add(GuideKit.Bar(cx + a - a * f, mid - a * f, cx - a * f, mid + a - a * f, bar, GuideColour.DarkRed));
                }

                if (fu)
                {
                    float c = d * 0.42f;
                    part.Elements.Add(GuideKit.Oval(cx - c * 0.65f, top + d * 0.5f - c * 0.65f, c * 1.3f, c * 1.3f, GuideColour.Gold));
                    part.Elements.Add(GuideKit.Cells("福", cx - c * 0.5f, top + d * 0.5f - c * 0.5f, c, GuideColour.Red, 0f));
                }

                // The tassel and its gold collar.
                float t0 = top + d, collar = size * 0.05f;
                part.Elements.Add(GuideKit.Rect(cx - w * 0.08f, t0, w * 0.16f, collar, GuideColour.Gold));
                part.Cut = new List<List<Point2>> { KnotOutline(w, size, d, top) };
                return part;
            }, 0.03f, true, s.Flag("lit"), 320f);
        }

        /// <summary>The outline of the knot (as the viewer sees it, round the part's middle): the loop on top, the diamond, the collar and the tassel widening to its foot.</summary>
        private static List<Point2> KnotOutline(float w, float size, float d, float top)
        {
            float hw = w * 0.5f, hs = size * 0.5f;
            Point2 P(float x, float y) => new Point2(x - hw, hs - y);
            float cx = hw, mid = top + d * 0.5f, t0 = top + d, collar = size * 0.05f;
            var outline = new List<Point2>
            {
                P(cx, t0), P(cx + w * 0.08f, t0), P(cx + w * 0.08f, t0 + collar), P(cx + w * 0.22f, size), P(cx - w * 0.22f, size), P(cx - w * 0.08f, t0 + collar), P(cx - w * 0.08f, t0),
                P(cx - d * 0.5f, mid), P(cx - w * 0.06f, top), P(cx - w * 0.06f, 0.01f * size), P(cx + w * 0.06f, 0.01f * size), P(cx + w * 0.06f, top), P(cx + d * 0.5f, mid)
            };
            outline.Reverse();
            return Anticlockwise(outline);
        }

        /// <summary>The points in anticlockwise order (y up), as the builder's faces want them.</summary>
        private static List<Point2> Anticlockwise(List<Point2> points)
        {
            double area = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                Point2 a = points[i], b = points[(i + 1) % points.Count];
                area += a.X * b.Y - b.X * a.Y;
            }

            if (area < 0.0)
                points.Reverse();
            return points;
        }

        /// <summary>An LED figure: a flat figure cut to its outline (福 on a round plate, a plum blossom, a lucky cloud, a snowflake, a star), one colour that glows at night.</summary>
        private static GuideBoard Figure(GuideSpec s)
        {
            float size = F(s, "size", 80f, 30f, 200f) / 100f;
            string motif = s.Get("motif");
            GuideColour colour = s.Is("colour", "gold") ? GuideColour.Gold : s.Is("colour", "white") ? GuideColour.White : s.Is("colour", "blue") ? GuideColour.Blue : GuideColour.Red;
            return Hung(s, PoleAdKind.Knot, _ =>
            {
                var part = new GuidePart { W = size, H = size, Shape = GuidePartShape.RoundRect, Ground = colour, Liner = 0f, Border = 0f };
                float r = size * 0.5f;
                List<Point2> outline;
                switch (motif)
                {
                    case "plum":
                        outline = Flower(5, r, 0.55f * r);
                        part.Elements.Add(GuideKit.Oval(r - 0.18f * r, r - 0.18f * r, 0.36f * r, 0.36f * r, colour == GuideColour.Gold ? GuideColour.Red : GuideColour.Gold));
                        break;
                    case "cloud":
                        outline = CloudOutline(size);
                        break;
                    case "snow":
                        outline = Star(6, r, 0.32f * r);
                        part.Elements.Add(GuideKit.Oval(r - 0.15f * r, r - 0.15f * r, 0.3f * r, 0.3f * r, GuideColour.White));
                        break;
                    case "star":
                        outline = Star(5, r, 0.42f * r, -0.06f * r);
                        break;
                    default:
                        outline = Ellipse(size, size);
                        part.Elements.Add(GuideKit.Oval(0.08f * size, 0.08f * size, 0.84f * size, 0.84f * size, colour == GuideColour.Gold ? GuideColour.Red : GuideColour.Gold));
                        part.Elements.Add(GuideKit.Cells("福", 0.2f * size, 0.2f * size, 0.6f * size, colour, 0f));
                        break;
                }

                part.Cut = new List<List<Point2>> { Anticlockwise(outline) };
                return part;
            }, 0.03f, true, s.Flag("lit"), 320f);
        }

        /// <summary>A flower of petals round the middle: a petal is an arc out to r between two notches at ri.</summary>
        private static List<Point2> Flower(int petals, float r, float ri)
        {
            var points = new List<Point2>();
            const int perPetal = 10;
            for (int p = 0; p < petals; p++)
            {
                for (int i = 0; i < perPetal; i++)
                {
                    double t = (p + i / (double)perPetal) / petals, a = Math.PI * 0.5 + 2.0 * Math.PI * t;
                    double bulge = Math.Sin(Math.PI * i / perPetal);
                    float rr = ri + (r - ri) * (float)Math.Sqrt(bulge);
                    points.Add(new Point2((float)(rr * Math.Cos(a)), (float)(rr * Math.Sin(a))));
                }
            }

            return points;
        }

        /// <summary>A lucky cloud (如意祥云): three lobes over a flat base, size wide, round the middle.</summary>
        private static List<Point2> CloudOutline(float size)
        {
            var points = new List<Point2>();
            float h = size * 0.6f, b = -h * 0.5f;
            (float cx, float cy, float r, double a0, double a1)[] lobes =
            {
                (size * 0.30f, b + h * 0.35f, size * 0.2f, -Math.PI * 0.5, Math.PI * 0.6),
                (0f, b + h * 0.62f, size * 0.26f, Math.PI * 0.1, Math.PI * 0.9),
                (-size * 0.30f, b + h * 0.35f, size * 0.2f, Math.PI * 0.4, Math.PI * 1.5)
            };
            foreach ((float cx, float cy, float r, double a0, double a1) in lobes)
            {
                const int steps = 10;
                for (int i = 0; i <= steps; i++)
                {
                    double a = a0 + (a1 - a0) * i / steps;
                    points.Add(new Point2(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
                }
            }

            return points;
        }

        /// <summary>
        /// Lanterns (灯笼): a round, a hexagonal palace or a long lantern, one to three on a string, on a side of the post or a pair; the body is built (SignBuilder.PoleAds), the board's part only
        /// marks where it is (it has no face).
        /// </summary>
        private static GuideBoard Lantern(GuideSpec s)
        {
            float d = F(s, "size", 60f, 25f, 150f) / 100f;
            int count = (int)F(s, "count", 1f, 1f, 3f);
            string kind = s.Get("kind");
            float h1 = LanternHeight(kind, d);
            float total = count * h1 + (count - 1) * LanternLink * d;
            GuideBoard board = Hung(s, PoleAdKind.Lantern, _ => new GuidePart { W = d, H = total, Shape = GuidePartShape.RoundRect, Ground = GuideColour.Red, Liner = 0f, Border = 0f }, d, false, s.Flag("lit"), 300f);
            board.PoleAd.Lanterns = count;
            board.PoleAd.LanternKind = kind == "palace" ? 1 : kind == "long" ? 2 : 0;
            board.PoleAd.LanternGold = s.Is("colour", "gold");
            return board;
        }

        /// <summary>
        /// A neon figure (霓虹造型灯, Neon): one or a pair, the second with its own pattern and characters when they are set. The part is the figure's picture in the catalogue (no face is drawn for
        /// it); its strips are built (SignBuilder.PoleAds).
        /// </summary>
        private static GuideBoard NeonFigure(GuideSpec s)
        {
            float w = F(s, "w", 70f, 30f, 150f) / 100f, line = F(s, "line", 15f, 6f, 40f) / 1000f;
            string motif2 = s.Get("motif2"), title2 = (s.Get("title2") ?? "").Trim();
            var arts = new List<NeonArt>();
            GuideBoard board = Hung(s, PoleAdKind.Neon, i =>
            {
                var design = new Neon.Design
                {
                    Shape = s.Get("shape"), W = w, BodyH = F(s, "h", 100f, 30f, 250f) / 100f, Line = line, Tassel = s.Flag("tassel"), Mono = s.Is("palette", "mono"),
                    Motif = i == 1 && motif2.Length > 0 && motif2 != "same" ? motif2 : s.Get("motif"),
                    Title = i == 1 && title2.Length > 0 ? title2 : (s.Get("title") ?? "").Trim(),
                    Frame = Neon.ColourOf(s.Get("frame")), Ink = Neon.ColourOf(s.Get("ink"))
                };
                NeonArt art = Neon.Draw(design);
                arts.Add(art);
                return Neon.Picture(art, w, Neon.Height(design), line);
            }, Neon.Depth, true, s.Flag("lit"), 300f);
            board.PoleAd.Neon.AddRange(arts);
            board.PoleAd.NeonWidth = line;
            return board;
        }

        /// <summary>The cord between two lanterns of a string, as a part of the diameter.</summary>
        public const float LanternLink = 0.08f;

        /// <summary>
        /// The height of one lantern with its caps and tassel for its diameter: a string of three round ones is about 3.5 diameters (the trade's 0.8 x 2.82 m), of two 2.2 (1.0 x 2.2 m).
        /// </summary>
        public static float LanternHeight(string kind, float d) => kind == "long" ? 1.9f * d : kind == "palace" ? 1.3f * d : 1.07f * d;
    }
}
