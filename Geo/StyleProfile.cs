using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>How far a rule of a style profile is backed (scratch/research-supports-urban/REPORT.md uses the same classes): a standard or a guide, a photograph, a maker's listing, or this mod's own estimate.</summary>
    public enum Evidence : byte
    {
        Standard,
        Photo,
        Supplier,
        Estimate
    }

    /// <summary>What backs a rule: its class and where (the report's source ids S1 to S15 with a clause or a photo file; free text for an estimate).</summary>
    public readonly struct Sourced
    {
        public readonly Evidence Class;
        public readonly string Source;

        public Sourced(Evidence evidence, string source)
        {
            Class = evidence;
            Source = source;
        }
    }

    /// <summary>The forms of support a profile may dress: what the planner chooses by the board and the road (SupportPlanner.Auto), and what the tool chooses by where the cursor is.</summary>
    public enum SupportForm : byte
    {
        SinglePost,
        DoublePost,
        TriplePost,
        Cantilever,
        DoubleCantilever,
        FCantilever,
        Gantry,

        /// <summary>A street or lane name sign (GB 17733 B.11, B.12) on a single post: the city's street-name plate post goes there (PlateHead.Fits).</summary>
        StreetName,
        Attached,
        AttachedArm,
        WallFlat,
        WallBracket,
        OverRoad
    }

    public static class SupportForms
    {
        /// <summary>The form of the plain support the planner chose; streetName: the board is a street or lane name sign, which a single post gives to a plate post.</summary>
        public static SupportForm Of(SupportDef plain, bool streetName)
        {
            switch (plain.Kind)
            {
                case SupportKind.SinglePole: return streetName ? SupportForm.StreetName : SupportForm.SinglePost;
                case SupportKind.DoublePole: return SupportForm.DoublePost;
                case SupportKind.TriplePole: return SupportForm.TriplePost;
                case SupportKind.Cantilever: return SupportForm.Cantilever;
                case SupportKind.DoubleCantilever: return SupportForm.DoubleCantilever;
                case SupportKind.FCantilever: return SupportForm.FCantilever;
                case SupportKind.Gantry: return SupportForm.Gantry;
                case SupportKind.Attached: return SupportForm.Attached;
                case SupportKind.AttachedArm: return SupportForm.AttachedArm;
                case SupportKind.WallFlat: return SupportForm.WallFlat;
                case SupportKind.OverRoad: return SupportForm.OverRoad;
                default: return SupportForm.WallBracket;
            }
        }
    }

    /// <summary>The section of the rails of a letter frame (DESIGN-text-lightbox.md 6.2). Read by the text light box, which does not exist yet: nothing uses it until then.</summary>
    public enum RailSection : byte
    {
        Square,
        Round,
        Flat
    }

    /// <summary>The supports a profile gives a form, the preferred first (the plain one is always the last resort and is not listed), and what backs the rule.</summary>
    public sealed class FormRule
    {
        public SupportForm Form;
        public string[] Variants;
        public Sourced Why;
    }

    /// <summary>
    /// A style (风格方案, DESIGN-support-styles.md): the paint of a city's supports and, for each form the planner may choose, the variant of the catalogue it takes (SupportPlanner.AutoStyled). It chooses among the
    /// variants that exist and never changes what a board needs (the form). A rule is here only where the research backs it; everything else is the plain variant, and the rule's evidence class says how far.
    /// </summary>
    public sealed class StyleProfile
    {
        public string Id, Zh, En;

        /// <summary>The paint: the steel's finish (a variant that has a finish of its own keeps it, as the editor does), whether the boards' backs are black, the paint at the posts' root.</summary>
        public SupportFinish Finish;
        public bool BlackBack;
        public RootPaint Root;
        public Sourced PaintWhy, RootWhy;

        public FormRule[] Forms = new FormRule[0];

        /// <summary>The rails of a letter frame (unused until the text light box): no source has them, so every profile has the design's own square tube.</summary>
        public RailSection Rail = RailSection.Square;
        public Sourced RailWhy = new Sourced(Evidence.Estimate, "DESIGN-text-lightbox.md 6.2: a 4 cm square tube; no standard or photo of a letter frame was read");

        public IReadOnlyList<string> VariantsOf(SupportForm form)
        {
            foreach (FormRule rule in Forms)
            {
                if (rule.Form == form)
                    return rule.Variants;
            }

            return NoVariants;
        }

        private static readonly string[] NoVariants = new string[0];
    }

    /// <summary>
    /// The profiles (scratch/research-supports-urban/REPORT.md; S1 to S15 are its sources). Only the cities the research covered, and in each only what it says: Beijing and Guangzhou have no variant of their
    /// own (Beijing's standard allows octagonal posts and grey, black or metal backs but states no preference), so they differ from the plain profile by paint alone, Beijing by none.
    /// </summary>
    public static class StyleProfiles
    {
        private static readonly Sourced s_NoRoot = new Sourced(Evidence.Estimate, "default: no paint at the root");

        /// <summary>No style: galvanised steel, round posts, the plain variant of every form: exactly what the planner gave before profiles.</summary>
        public static readonly StyleProfile Plain = new StyleProfile
        {
            Id = "plain", Zh = "通用（GB 5768.2）", En = "Plain (GB 5768.2)",
            Finish = SupportFinish.Galvanised,
            PaintWhy = new Sourced(Evidence.Estimate, "default: hot-dip galvanised and unpainted, as the mod drew its supports before 0.12.57"),
            RootWhy = s_NoRoot
        };

        public static readonly StyleProfile Shanghai = new StyleProfile
        {
            Id = "shanghai", Zh = "上海", En = "Shanghai",
            Finish = SupportFinish.Graphite,
            PaintWhy = new Sourced(Evidence.Standard, "S1 A.5.8-1, S2 5.3.7: RAL 9011 graphite black, matte; no rule on the backs of boards, so they are not black"),
            RootWhy = s_NoRoot,
            Forms = new[]
            {
                new FormRule
                {
                    Form = SupportForm.Cantilever, Variants = new[] { SupportCatalog.Integrated, SupportCatalog.OctaCantilever },
                    Why = new Sourced(Evidence.Standard, "S1 4.2.3-3, 4.2.5 (guide signs on the arms of the multi-function pole); S2 (single arms octagonal tapered); arms to 16 m, S1 A.4.2-3")
                },
                new FormRule
                {
                    Form = SupportForm.DoubleCantilever, Variants = new[] { SupportCatalog.OctaDoubleCantilever },
                    Why = new Sourced(Evidence.Supplier, "S12: the makers' octagonal T type; S1 A.4.2-3 has the arms octagonal")
                },
                new FormRule
                {
                    Form = SupportForm.StreetName, Variants = new[] { SupportCatalog.PlateShanghai },
                    Why = new Sourced(Evidence.Photo, "S14 full-c01 photos; S13 DB31/T 416-2008 as quoted in a search snippet, not read")
                }
            }
        };

        public static readonly StyleProfile Hangzhou = new StyleProfile
        {
            Id = "hangzhou", Zh = "杭州", En = "Hangzhou",
            Finish = SupportFinish.Graphite,
            BlackBack = true,
            PaintWhy = new Sourced(Evidence.Standard, "S3 5.9.1 (matte black sign poles), 5.7 (matte black backs of the boards)"),
            RootWhy = s_NoRoot,
            Forms = new[]
            {
                new FormRule { Form = SupportForm.Cantilever, Variants = new[] { SupportCatalog.Framed }, Why = new Sourced(Evidence.Standard, "S3 5.7: cantilever signs take the framed form") },
                new FormRule { Form = SupportForm.StreetName, Variants = new[] { SupportCatalog.PlateHangzhou }, Why = new Sourced(Evidence.Standard, "S3 app. B (plate 1120 x 360 on a black post)") }
            }
        };

        /// <summary>Beijing: nothing the research found changes a support's look (S4 4.3.9 lets the back be grey, black or metal; 4.6.3 allows octagonal posts; neither is preferred): the plain profile with the photos' galvanised steel named.</summary>
        public static readonly StyleProfile Beijing = new StyleProfile
        {
            Id = "beijing", Zh = "北京", En = "Beijing",
            Finish = SupportFinish.Galvanised,
            PaintWhy = new Sourced(Evidence.Photo, "S14 full-c04 photos: galvanised; S4 4.3.9: the back grey, black or metal, no preference"),
            RootWhy = s_NoRoot
        };

        public static readonly StyleProfile Shenzhen = new StyleProfile
        {
            Id = "shenzhen", Zh = "深圳", En = "Shenzhen",
            Finish = SupportFinish.LightGrey,
            PaintWhy = new Sourced(Evidence.Photo, "S14 full-c03 photos: light grey; S6 gives no colour rule"),
            RootWhy = s_NoRoot,
            Forms = new[]
            {
                new FormRule { Form = SupportForm.StreetName, Variants = new[] { SupportCatalog.PlateShenzhen }, Why = new Sourced(Evidence.Photo, "report card D4: photos full-c03 and a maker's listing (S15)") }
            }
        };

        public static readonly StyleProfile Xuzhou = new StyleProfile
        {
            Id = "xuzhou", Zh = "徐州 / 江苏", En = "Xuzhou / Jiangsu",
            Finish = SupportFinish.LightGrey,
            Root = RootPaint.BlueRings,
            PaintWhy = new Sourced(Evidence.Photo, "S14 full-i12 photos (Jiangsu, dark and light grey); S7 5.4.2: the plate post silver-grey powder"),
            RootWhy = new Sourced(Evidence.Estimate, "the roadside report's S7, the Qidong 2018 drawing 交施-07 (blue and white paint): one county's drawing taken for the profile"),
            Forms = new[]
            {
                new FormRule { Form = SupportForm.StreetName, Variants = new[] { SupportCatalog.PlateXuzhou }, Why = new Sourced(Evidence.Standard, "S7 DB3203/T 1080-2025 figs. 1 to 8") }
            }
        };

        public static readonly StyleProfile Guangzhou = new StyleProfile
        {
            Id = "guangzhou", Zh = "广州", En = "Guangzhou",
            Finish = SupportFinish.DarkGrey,
            PaintWhy = new Sourced(Evidence.Estimate, "S14 full-c15 photos: grey long-arm cantilevers, a mid-grey; the shade is an estimate"),
            RootWhy = s_NoRoot
        };

        /// <summary>Every profile, the plain one first (the order the panel would show).</summary>
        public static readonly StyleProfile[] All = { Plain, Shanghai, Hangzhou, Beijing, Shenzhen, Xuzhou, Guangzhou };

        public static StyleProfile ById(string id)
        {
            foreach (StyleProfile profile in All)
            {
                if (profile.Id == id)
                    return profile;
            }

            return null;
        }
    }
}
