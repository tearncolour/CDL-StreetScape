using System;
using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The style profiles and the planner that takes them (DESIGN-support-styles.md): the plain profile is the planner as it was, every profile names supports that exist, the paint and the variants are what the research backs, a board a variant cannot hold goes to the next.</summary>
    internal static class StyleProfileTests
    {
        public static void RunAll()
        {
            Program.Run("styles: the plain profile gives the planner's own answer for every sign, context and stack", PlainIsTheOldPlanner);
            Program.Run("styles: every profile gives an existing, listed support of the form's family, deterministically", EveryProfile);
            Program.Run("styles: the profiles are what the research says (paint, variants, evidence)", TheTable);
            Program.Run("styles: a board a variant cannot hold goes to the next variant, then to the plain one, with a note", FallBack);
            Program.Run("styles: what an item stores is what the panel stores for the same choice", Stored);
            Program.Run("styles: the profile's paint is a default: what the player set since choosing it stays, part by part", PlayerPaint);
        }

        private static readonly SupportContext[] Contexts =
        {
            new SupportContext { LanesSameDirection = 0 }, new SupportContext { LanesSameDirection = 1 }, new SupportContext { LanesSameDirection = 2 }, new SupportContext { LanesSameDirection = 3 },
            new SupportContext { LanesSameDirection = 0, ExitLeftOrWide = true }, new SupportContext { LanesSameDirection = 1, ExitLeftOrWide = true },
            new SupportContext { LanesSameDirection = 2, ExitLeftOrWide = true }, new SupportContext { LanesSameDirection = 3, ExitLeftOrWide = true }
        };

        /// <summary>What the planner gave before profiles, from the old methods alone: AutoFor for a board, Auto(sign, true) for a stack unless the road made a gantry of it.</summary>
        private static SupportDef Old(SignDef sign, SupportContext context, bool stack)
        {
            SupportDef byContext = SupportPlanner.AutoFor(sign, context);
            return stack && byContext.Kind != SupportKind.Gantry ? SupportPlanner.Auto(sign, true) : byContext;
        }

        private static void PlainIsTheOldPlanner()
        {
            int cases = 0, wrong = 0, noted = 0, painted = 0, stored = 0;
            string first = "";
            foreach (SignDef sign in SignCatalog.All)
            {
                foreach (SupportContext context in Contexts)
                {
                    foreach (bool stack in new[] { false, true })
                    {
                        PlannedSupport planned = SupportPlanner.AutoStyled(sign, context, StyleProfiles.Plain, stack);
                        cases++;
                        if (planned.Support.Id != Old(sign, context, stack).Id)
                        {
                            wrong++;
                            first = first.Length > 0 ? first : sign.Id;
                        }

                        noted += planned.Note.Length > 0 ? 1 : 0;
                        painted += planned.StoredStyle != 0 ? 1 : 0;
                        stored += planned.StoredSupportHash != 0u ? 1 : 0;
                    }
                }
            }

            Program.Check(cases == SignCatalog.All.Count * Contexts.Length * 2 && cases > 1000, "every sign, context and stack was run: " + cases);
            Program.Check(wrong == 0, $"the plain profile gives the old planner's support for all {cases} cases ({wrong} differ, the first: {first})");
            Program.Check(noted == 0 && painted == 0 && stored == 0, $"and changes nothing: no note ({noted}), the paint byte 0 ({painted} differ), the stored hash 0 ({stored} differ)");
            // Old Auto and AutoFor are called by the tool as before: a few known answers.
            Program.Check(SupportPlanner.Auto(SignCatalog.ById("gb.vms.traffic")).Id == SupportCatalog.Cantilever, "the traffic-state screen's support is still the cantilever");
        }

        private static string FamilyOfForm(SupportForm form)
        {
            switch (form)
            {
                case SupportForm.StreetName: return SupportFamilies.Plate;
                case SupportForm.Cantilever:
                case SupportForm.DoubleCantilever:
                case SupportForm.FCantilever: return SupportFamilies.Cantilever;
                case SupportForm.Gantry: return SupportFamilies.Gantry;
                case SupportForm.Attached:
                case SupportForm.AttachedArm:
                case SupportForm.WallFlat:
                case SupportForm.WallBracket: return SupportFamilies.Attached;
                default: return SupportFamilies.Post;
            }
        }

        private static void EveryProfile()
        {
            Program.Check(StyleProfiles.All.Length == 7 && StyleProfiles.All[0] == StyleProfiles.Plain, "seven profiles, the plain one first");
            var ids = new HashSet<string>();
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                Program.Check(ids.Add(profile.Id) && StyleProfiles.ById(profile.Id) == profile, profile.Id + ": a unique id that finds it");
                Program.Check(!string.IsNullOrEmpty(profile.Zh) && !string.IsNullOrEmpty(profile.En), profile.Id + ": named in both languages");
                Program.Check(!string.IsNullOrEmpty(profile.PaintWhy.Source) && !string.IsNullOrEmpty(profile.RootWhy.Source) && !string.IsNullOrEmpty(profile.RailWhy.Source), profile.Id + ": the paint, the root and the rails say what backs them");
                var forms = new HashSet<SupportForm>();
                foreach (FormRule rule in profile.Forms)
                {
                    Program.Check(forms.Add(rule.Form) && rule.Variants.Length > 0 && !string.IsNullOrEmpty(rule.Why.Source), $"{profile.Id}/{rule.Form}: one rule for the form, with variants and a source");
                    foreach (string id in rule.Variants)
                    {
                        SupportDef variant = SupportCatalog.ById(id);
                        Program.Check(variant != null && !variant.Hidden, $"{profile.Id}/{rule.Form}: {id} is a listed support");
                        if (variant == null)
                            continue;
                        Program.Check(SupportFamilies.Of(variant) == FamilyOfForm(rule.Form), $"{profile.Id}/{rule.Form}: {id} is of the form's family");
                        Program.Check(SupportForms.Of(variant, rule.Form == SupportForm.StreetName) == rule.Form, $"{profile.Id}/{rule.Form}: {id} is of the form's kind");
                        Program.Check(SupportCatalog.ByHash(variant.Hash) == variant, $"{id}: its hash finds it (an item that stores it is drawn with it)");
                    }
                }
            }

            int cases = 0;
            var changedBy = new Dictionary<string, HashSet<string>>();
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                changedBy[profile.Id] = new HashSet<string>();
                foreach (SignDef sign in SignCatalog.All)
                {
                    foreach (SupportContext context in Contexts)
                    {
                        foreach (bool stack in new[] { false, true })
                        {
                            PlannedSupport planned = SupportPlanner.AutoStyled(sign, context, profile, stack);
                            cases++;
                            SupportDef plain = Old(sign, context, stack);
                            if (planned.Plain.Id != plain.Id)
                                Program.Check(false, $"{profile.Id}/{sign.Id}: the form is the plain planner's");
                            if (SupportCatalog.ByHash(planned.Support.Hash) != planned.Support || planned.Support.Hidden)
                                Program.Check(false, $"{profile.Id}/{sign.Id}: {planned.Support.Id} is a listed support its hash finds");
                            bool plate = PlateHead.FitsDef(sign) && plain.Kind == SupportKind.SinglePole && profile.VariantsOf(SupportForm.StreetName).Count > 0;
                            string family = plate ? SupportFamilies.Plate : SupportFamilies.Of(plain);
                            if (planned.Support.Id != plain.Id && SupportFamilies.Of(planned.Support) != family)
                                Program.Check(false, $"{profile.Id}/{sign.Id}: {planned.Support.Id} is of the family of the form");
                            SupportFinish finish = planned.Support.DefaultFinish ?? profile.Finish;
                            if (planned.Style.Finish != finish || planned.Style.BlackBack != profile.BlackBack || planned.Style.Root != profile.Root)
                                Program.Check(false, $"{profile.Id}/{sign.Id}: the paint is the profile's, the variant's own finish over it");
                            PlannedSupport again = SupportPlanner.AutoStyled(sign, context, profile, stack);
                            if (again.Support.Id != planned.Support.Id || again.StoredStyle != planned.StoredStyle || again.Note != planned.Note)
                                Program.Check(false, $"{profile.Id}/{sign.Id}: deterministic");
                            if (planned.Support.Id != plain.Id)
                            {
                                changedBy[profile.Id].Add(sign.Id);
                            }
                        }
                    }
                }
            }

            Program.Check(cases == StyleProfiles.All.Length * SignCatalog.All.Count * Contexts.Length * 2, "every profile, sign, context and stack was run: " + cases);
            Program.Check(changedBy["plain"].Count == 0 && changedBy["beijing"].Count == 0 && changedBy["guangzhou"].Count == 0, "the plain, Beijing and Guangzhou profiles change no support, only paint");
            Program.Check(changedBy["shanghai"].Count > 0 && changedBy["hangzhou"].Count > 0 && changedBy["shenzhen"].Count > 0 && changedBy["xuzhou"].Count > 0, "the four profiles with variants change some sign's support");
            // For the report: how many signs change under each profile.
            foreach (KeyValuePair<string, HashSet<string>> pair in changedBy)
                Console.WriteLine($"  styles: {pair.Key}: {pair.Value.Count} sign(s) of {SignCatalog.All.Count} change their support (of {Contexts.Length * 2} contexts each; {string.Join(", ", pair.Value)})");
        }

        private static SignDef Wide(string id, string group, float width) => new SignDef { Id = id, Group = group, Width = width, Height = 2f, Shape = ShapeKind.Rect };

        private static void TheTable()
        {
            // The report's content, written out again (REPORT.md cards A to D and the paint of card B): a change of a profile's data must change a line here.
            void Paint(StyleProfile p, SupportFinish finish, bool blackBack, RootPaint root, Evidence paintClass, Evidence rootClass)
            {
                Program.Check(p.Finish == finish && p.BlackBack == blackBack && p.Root == root, $"{p.Id}: finish {finish}, black backs {blackBack}, root {root}");
                Program.Check(p.PaintWhy.Class == paintClass && p.RootWhy.Class == rootClass, $"{p.Id}: the paint is backed as {paintClass}, the root as {rootClass}");
            }

            void Rule(StyleProfile p, SupportForm form, Evidence evidence, params string[] variants)
            {
                FormRule rule = Array.Find(p.Forms, r => r.Form == form);
                Program.Check(rule != null && rule.Why.Class == evidence && string.Join(",", rule.Variants) == string.Join(",", variants), $"{p.Id}/{form}: {string.Join(", ", variants)} ({evidence})");
            }

            StyleProfile plain = StyleProfiles.Plain, shanghai = StyleProfiles.Shanghai, hangzhou = StyleProfiles.Hangzhou, beijing = StyleProfiles.Beijing, shenzhen = StyleProfiles.Shenzhen, xuzhou = StyleProfiles.Xuzhou, guangzhou = StyleProfiles.Guangzhou;
            Paint(plain, SupportFinish.Galvanised, false, RootPaint.None, Evidence.Estimate, Evidence.Estimate);
            Paint(shanghai, SupportFinish.Graphite, false, RootPaint.None, Evidence.Standard, Evidence.Estimate);
            Paint(hangzhou, SupportFinish.Graphite, true, RootPaint.None, Evidence.Standard, Evidence.Estimate);
            Paint(beijing, SupportFinish.Galvanised, false, RootPaint.None, Evidence.Photo, Evidence.Estimate);
            Paint(shenzhen, SupportFinish.LightGrey, false, RootPaint.None, Evidence.Photo, Evidence.Estimate);
            Paint(xuzhou, SupportFinish.LightGrey, false, RootPaint.BlueRings, Evidence.Photo, Evidence.Estimate);
            Paint(guangzhou, SupportFinish.DarkGrey, false, RootPaint.None, Evidence.Estimate, Evidence.Estimate);
            Program.Check(plain.Forms.Length == 0 && beijing.Forms.Length == 0 && guangzhou.Forms.Length == 0, "the plain, Beijing and Guangzhou profiles have no variant: the research gives none");
            Rule(shanghai, SupportForm.Cantilever, Evidence.Standard, SupportCatalog.Integrated, SupportCatalog.OctaCantilever);
            Rule(shanghai, SupportForm.DoubleCantilever, Evidence.Supplier, SupportCatalog.OctaDoubleCantilever);
            Rule(shanghai, SupportForm.StreetName, Evidence.Photo, SupportCatalog.PlateShanghai);
            Rule(hangzhou, SupportForm.Cantilever, Evidence.Standard, SupportCatalog.Framed);
            Rule(hangzhou, SupportForm.StreetName, Evidence.Standard, SupportCatalog.PlateHangzhou);
            Rule(shenzhen, SupportForm.StreetName, Evidence.Photo, SupportCatalog.PlateShenzhen);
            Rule(xuzhou, SupportForm.StreetName, Evidence.Standard, SupportCatalog.PlateXuzhou);
            Program.Check(shanghai.Forms.Length == 3 && hangzhou.Forms.Length == 2 && shenzhen.Forms.Length == 1 && xuzhou.Forms.Length == 1, "and no rule besides");
            foreach (StyleProfile p in StyleProfiles.All)
                Program.Check(p.Rail == RailSection.Square && p.RailWhy.Class == Evidence.Estimate, p.Id + ": the rails are the design's own square tube, an estimate: no source has a letter frame");

            // What the table does to boards.
            SignDef street = SignCatalog.ById("gb17733.street"), lane = SignCatalog.ById("gb17733.lane"), screen = SignCatalog.ById("gb.vms.traffic"), speed = SignCatalog.ById("gb.prohib.36");
            SupportContext none = default;
            Program.Check(SupportPlanner.AutoStyled(street, none, shanghai).Support.Id == SupportCatalog.PlateShanghai && SupportPlanner.AutoStyled(lane, none, xuzhou).Support.Id == SupportCatalog.PlateXuzhou, "a street or lane sign gets the city's plate post");
            Program.Check(SupportPlanner.AutoStyled(street, none, beijing).Support.Id == SupportCatalog.SinglePole && SupportPlanner.AutoStyled(speed, none, shanghai).Support.Id == SupportCatalog.SinglePole, "a city with no plate post, and a board that is no street sign, keep the single post");
            Program.Check(SupportPlanner.AutoStyled(screen, none, hangzhou).Support.Id == SupportCatalog.Framed && SupportPlanner.AutoStyled(screen, none, shanghai).Support.Id == SupportCatalog.Integrated, "the traffic-state screen's cantilever is Hangzhou's framed one, Shanghai's multi-function pole");
            Program.Check(SupportPlanner.AutoStyled(street, none, shanghai).Style.Finish == SupportFinish.LightGrey && SupportPlanner.AutoStyled(speed, none, shanghai).Style.Finish == SupportFinish.Graphite, "Shanghai's plate post keeps its own silver-grey, the other supports the graphite");
            Program.Check(SupportPlanner.AutoStyled(speed, none, hangzhou).Style.BlackBack && !SupportPlanner.AutoStyled(speed, none, shanghai).Style.BlackBack, "Hangzhou's boards have black backs, Shanghai's do not");
            Program.Check(SupportPlanner.AutoStyled(speed, none, xuzhou).Style.Root == RootPaint.BlueRings, "Xuzhou's posts have the blue root rings");
            // A stack of boards on one post: any street sign among them gives the plate post (the caller says so), and the form is the stack's.
            SignDef wideStack = Wide("stack", "guide", 1.5f);
            Program.Check(SupportPlanner.Resolve(SupportPlanner.Auto(wideStack, true), true, wideStack, shanghai).Support.Id == SupportCatalog.PlateShanghai, "a stack with a street sign among its boards is on the plate post");
            Program.Check(SupportPlanner.Resolve(SupportPlanner.Auto(Wide("wide", "guide", 3f)), true, Wide("wide", "guide", 3f), shanghai).Support.Id == SupportCatalog.DoublePole, "a stack too wide for one post is two posts, whatever the street sign says");
        }

        private static void FallBack()
        {
            SupportContext none = default;
            // The traffic-state screen at several widths: the arm of the multi-function pole is listed to 16 m, the framed one has no such limit.
            SignDef Screen(float width) => new SignDef { Id = "gb.vms.traffic", Group = "variable", Width = width, Height = 2f, Shape = ShapeKind.Rect };
            Program.Check(SupportPlanner.AutoStyled(Screen(6f), none, StyleProfiles.Shanghai).Note.Length == 0 && SupportPlanner.AutoStyled(Screen(6f), none, StyleProfiles.Shanghai).Support.Id == SupportCatalog.Integrated, "a 6 m screen: the multi-function pole, no note");
            // The longest board whose arm is 16 m: 1.0 + w - 0.05 = 16 gives w = 15.05.
            Program.Check(SupportPlanner.AutoStyled(Screen(15.05f), none, StyleProfiles.Shanghai).Support.Id == SupportCatalog.Integrated, "a board whose arm is exactly 16 m is held");
            PlannedSupport over = SupportPlanner.AutoStyled(Screen(15.2f), none, StyleProfiles.Shanghai);
            Program.Check(over.Support.Id == SupportCatalog.Cantilever && over.Plain.Id == SupportCatalog.Cantilever && over.Note.Contains(SupportCatalog.Integrated) && over.Note.Contains(SupportCatalog.OctaCantilever) && over.Note.Contains("took " + SupportCatalog.Cantilever),
                "a board whose arm would be over 16 m: neither octagonal variant, the plain cantilever, and the note says each: " + over.Note);
            Program.Check(over.StoredSupportHash == 0u && over.Style.Finish == SupportFinish.Graphite, "the plain support it falls to is the planner's own (stored as 0) in the profile's paint");
            PlannedSupport framed = SupportPlanner.AutoStyled(Screen(25f), none, StyleProfiles.Hangzhou);
            Program.Check(framed.Support.Id == SupportCatalog.Framed && framed.Note.Length == 0, "Hangzhou's framed cantilever has no 16 m limit: a 25 m board keeps it");

            // The next variant before the plain one: a profile made for the test (the octagonal first, the framed second).
            var chain = new StyleProfile { Id = "test", Zh = "t", En = "t", Forms = new[] { new FormRule { Form = SupportForm.Cantilever, Variants = new[] { SupportCatalog.OctaCantilever, SupportCatalog.Framed }, Why = new Sourced(Evidence.Estimate, "test") } } };
            PlannedSupport second = SupportPlanner.AutoStyled(Screen(20f), none, chain);
            Program.Check(second.Support.Id == SupportCatalog.Framed && second.Note.Contains(SupportCatalog.OctaCantilever) && second.Note.EndsWith("took " + SupportCatalog.Framed), "a board the first cannot hold goes to the second variant, with a note: " + second.Note);
            Program.Check(SupportPlanner.AutoStyled(Screen(6f), none, chain).Support.Id == SupportCatalog.OctaCantilever && SupportPlanner.AutoStyled(Screen(6f), none, chain).Note.Length == 0, "a board the first holds takes it, with no note");

            // The double cantilever, which the tool chooses by context and the planner never does: the same limit.
            SupportDef doublePlain = SupportCatalog.ById(SupportCatalog.DoubleCantilever);
            Program.Check(SupportPlanner.Resolve(doublePlain, false, Screen(6f), StyleProfiles.Shanghai).Support.Id == SupportCatalog.OctaDoubleCantilever, "Shanghai's double cantilever is the octagonal one");
            PlannedSupport doubleOver = SupportPlanner.Resolve(doublePlain, false, Screen(20f), StyleProfiles.Shanghai);
            Program.Check(doubleOver.Support.Id == SupportCatalog.DoubleCantilever && doubleOver.Note.Length > 0, "and the plain double cantilever for a board it cannot hold: " + doubleOver.Note);
            // A form the profile has no rule for is the plain support with the profile's paint and no note.
            PlannedSupport gantry = SupportPlanner.Resolve(SupportCatalog.ById(SupportCatalog.Gantry), false, Screen(8f), StyleProfiles.Shanghai);
            Program.Check(gantry.Support.Id == SupportCatalog.Gantry && gantry.Note.Length == 0 && gantry.Style.Finish == SupportFinish.Graphite, "a gantry in Shanghai: the plain gantry, graphite, no note");
        }

        /// <summary>What PlacementTool does for a support the panel chose (ChooseSupport: SupportHashOf(id), and a support with a finish of its own sets the finish, the backs and the root staying as the paint was set).</summary>
        private static (uint Hash, byte Style) Panel(SupportStyle paint, string id)
        {
            SupportDef next = SupportCatalog.ById(id);
            SupportStyle style = paint;
            if (next?.DefaultFinish is SupportFinish finish)
                style.Finish = finish;
            return (next?.Hash ?? 0u, style.Packed);
        }

        private static void PlayerPaint()
        {
            SignDef speed = SignCatalog.ById("gb.prohib.36"), street = SignCatalog.ById("gb17733.street");
            SupportContext none = default;
            StyleProfile hangzhou = StyleProfiles.Hangzhou, shanghai = StyleProfiles.Shanghai;
            var player = new SupportStyle { Finish = SupportFinish.LightGrey, BlackBack = false, Root = RootPaint.Marking };

            // Nothing touched: the profile's paint, as before (the parameters have defaults).
            SupportStyle plainHangzhou = SupportPlanner.AutoStyled(speed, none, hangzhou).Style;
            Program.Check(SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.None).Style.Packed == plainHangzhou.Packed && plainHangzhou.BlackBack, "nothing touched: the profile's paint");

            // Each part alone: the player's value for that part, the profile's for the others.
            SupportStyle onlyFinish = SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.Finish).Style;
            Program.Check(onlyFinish.Finish == SupportFinish.LightGrey && onlyFinish.BlackBack == plainHangzhou.BlackBack && onlyFinish.Root == plainHangzhou.Root, "only the finish touched: the finish is the player's, the backs and the root the profile's");
            SupportStyle onlyBack = SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.BlackBack).Style;
            Program.Check(!onlyBack.BlackBack && onlyBack.Finish == plainHangzhou.Finish && onlyBack.Root == plainHangzhou.Root, "only the backs touched: the player's backs (off), the rest the profile's");
            SupportStyle onlyRoot = SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.Root).Style;
            Program.Check(onlyRoot.Root == RootPaint.Marking && onlyRoot.Finish == plainHangzhou.Finish && onlyRoot.BlackBack == plainHangzhou.BlackBack, "only the root touched: the player's root, the rest the profile's");
            SupportStyle all = SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root).Style;
            Program.Check(all.Packed == player.Packed, "everything touched: the player's paint whole");

            // The player's finish beats a variant's own finish too (the Shanghai plate post is silver-grey by default), and the support is still the profile's variant.
            PlannedSupport plate = SupportPlanner.AutoStyled(street, none, shanghai, false, new SupportStyle { Finish = SupportFinish.DarkGrey }, PaintTouch.Finish);
            Program.Check(plate.Support.Id == SupportCatalog.PlateShanghai && plate.Style.Finish == SupportFinish.DarkGrey && SupportPlanner.AutoStyled(street, none, shanghai).Style.Finish == SupportFinish.LightGrey, "the player's finish beats the variant's own, and the variant is still the profile's");

            // What the touch does not do: it does not change the support the profile picks, and the stored bytes follow the resolved paint.
            Program.Check(SupportPlanner.AutoStyled(street, none, shanghai, false, player, PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root).Support.Id == SupportPlanner.AutoStyled(street, none, shanghai).Support.Id, "touching the paint does not change the support");
            Program.Check(SupportPlanner.AutoStyled(speed, none, hangzhou, false, player, PaintTouch.Finish).StoredStyle == onlyFinish.Packed, "the stored byte is the resolved paint");
        }

        private static void Stored()
        {
            int cases = 0, wrong = 0, unknown = 0, explicitHash = 0;
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                var paint = new SupportStyle { Finish = profile.Finish, BlackBack = profile.BlackBack, Root = profile.Root };
                foreach (SignDef sign in SignCatalog.All)
                {
                    foreach (bool stack in new[] { false, true })
                    {
                        PlannedSupport planned = SupportPlanner.AutoStyled(sign, new SupportContext { LanesSameDirection = 2 }, profile, stack);
                        // The panel: the planner's own choice when the profile changed nothing (no id), else the variant's id.
                        (uint hash, byte style) = Panel(paint, planned.Support.Id == planned.Plain.Id ? null : planned.Support.Id);
                        cases++;
                        wrong += planned.StoredSupportHash != hash || planned.StoredStyle != style ? 1 : 0;
                        unknown += planned.StoredSupportHash != 0u && SupportCatalog.ByHash(planned.StoredSupportHash) == null ? 1 : 0;
                        explicitHash += planned.StoredSupportHash != 0u ? 1 : 0;
                    }
                }
            }

            Program.Check(cases > 1000 && wrong == 0, $"the hash and the paint byte an item stores are the panel's for the same choice ({wrong} of {cases} differ)");
            Program.Check(unknown == 0 && explicitHash > 0, $"every stored hash is a support of the catalogue, which a mod of 0.12.66 to 0.12.72 has ({unknown} unknown; {explicitHash} are explicit)");
            // The plain variant is stored as 0, the planner's choice: it goes on following the board when it is edited; a variant is stored by its hash.
            PlannedSupport plate = SupportPlanner.AutoStyled(SignCatalog.ById("gb17733.street"), default, StyleProfiles.Shanghai);
            Program.Check(plate.StoredSupportHash == SupportCatalog.ById(SupportCatalog.PlateShanghai).Hash && plate.StoredStyle == new SupportStyle { Finish = SupportFinish.LightGrey }.Packed, "the Shanghai plate post is stored by its hash, silver-grey");
            PlannedSupport post = SupportPlanner.AutoStyled(SignCatalog.ById("gb.prohib.36"), default, StyleProfiles.Hangzhou);
            Program.Check(post.StoredSupportHash == 0u && post.StoredStyle == new SupportStyle { Finish = SupportFinish.Graphite, BlackBack = true }.Packed, "a plain post in Hangzhou is stored as 0 with graphite and black backs");
        }
    }
}
