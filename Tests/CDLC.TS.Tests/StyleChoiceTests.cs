using System;
using System.IO;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The style profile in the tool (StyleState, PlainShadow, StyleEvidence) and in the preferences file: the pure half of 0.12.75's step E; the planner itself is StyleProfileTests'.</summary>
    internal static class StyleChoiceTests
    {
        public static void RunAll()
        {
            Program.Run("style choice: choosing a profile sets the paint and lets go of the player's; touching marks parts", Choosing);
            Program.Run("style choice: the plain profile, and the feature off, are the planner as it was with the paint the player has", PlainIsUntouched);
            Program.Run("style choice: another profile gives its variant and its paint, the player's parts over both", AnotherProfile);
            Program.Run("style choice: the plain shadow finds no difference over every sign, context, stack and paint, and is bounded", Shadow);
            Program.Run("style choice: a profile never dresses what is stored: the hash is the variant's or 0", Stored);
            Program.Run("style choice: how far each profile is backed, for the panel", Backing);
            Program.Run("style choice: the profile and the feature in the preferences file", FileState);
        }

        private static readonly SupportContext[] Contexts =
        {
            default, new SupportContext { LanesSameDirection = 3 }, new SupportContext { LanesSameDirection = 1, ExitLeftOrWide = true }, new SupportContext { LanesSameDirection = 2 }
        };

        private static SignDef Sign(string id) => SignCatalog.ById(id);

        private static void Choosing()
        {
            var state = new StyleState();
            Program.Check(state.Chosen == StyleProfiles.Plain && state.Effective == StyleProfiles.Plain && state.Enabled && state.Touched == PaintTouch.None, "a new state is plain, on, and nothing touched");
            state.Touch(1, -1, -1);
            Program.Check(state.Touched == PaintTouch.Finish, "the finish buttons mark the finish");
            state.Touch(-1, 0, -1);
            Program.Check(state.Touched == (PaintTouch.Finish | PaintTouch.BlackBack), "the backs' button marks the backs (off is a choice too)");
            state.Touch(-1, -1, 2);
            Program.Check(state.Touched == (PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root), "the root marks the root");
            state.Touch(-1, -1, -1);
            Program.Check(state.Touched == (PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root), "an argument that is 'as it is' marks nothing and unmarks nothing");

            // Galvanised (0) and no root paint (0) are choices as much as the others: the player who sets them is not given the profile's paint back.
            state.Choose("plain");
            state.Touch(0, -1, -1);
            Program.Check(state.Touched == PaintTouch.Finish, "the finish galvanised (0) marks the finish");
            state.Choose("plain");
            state.Touch(-1, -1, 0);
            Program.Check(state.Touched == PaintTouch.Root, "no root paint (0) marks the root");

            StyleProfile chosen = state.Choose("hangzhou");
            Program.Check(chosen == StyleProfiles.Hangzhou && state.Chosen == StyleProfiles.Hangzhou && state.Touched == PaintTouch.None, "choosing a profile lets go of what the player had set");
            SupportStyle paint = StyleState.PaintOf(StyleProfiles.Hangzhou);
            Program.Check(paint.Finish == SupportFinish.Graphite && paint.BlackBack && paint.Root == RootPaint.None, "its paint is the profile's");
            paint = StyleState.PaintOf(StyleProfiles.Xuzhou);
            Program.Check(paint.Finish == SupportFinish.LightGrey && !paint.BlackBack && paint.Root == RootPaint.BlueRings, "another profile's paint, with its root rings");
            foreach (StyleProfile profile in StyleProfiles.All)
            {
                SupportStyle each = StyleState.PaintOf(profile);
                Program.Check(each.Finish == profile.Finish && each.BlackBack == profile.BlackBack && each.Root == profile.Root, "the paint of " + profile.Id + " is its three parts");
            }

            state.Touch(0, -1, -1);
            Program.Check(state.Choose("hangzhou") == StyleProfiles.Hangzhou && state.Touched == PaintTouch.None, "choosing the same profile again is a way back to its paint");
            Program.Check(state.Choose("nowhere") == StyleProfiles.Plain && state.Choose("") == StyleProfiles.Plain && state.Choose(null) == StyleProfiles.Plain, "an id this mod does not have is plain");
            state.Choose("shanghai");
            state.Enabled = false;
            Program.Check(state.Chosen == StyleProfiles.Shanghai && state.Effective == StyleProfiles.Plain, "off: the choice is kept, the planner is given plain");
            state.Enabled = true;
            Program.Check(state.Effective == StyleProfiles.Shanghai, "on again: the choice is back");
        }

        private static void PlainIsUntouched()
        {
            var plain = new StyleState();
            var off = new StyleState { Enabled = false };
            off.Choose("shanghai");
            SupportStyle leftover = new SupportStyle { Finish = SupportFinish.Graphite, BlackBack = true, Root = RootPaint.Marking };
            int cases = 0, support = 0, style = 0, stored = 0;
            foreach (SignDef sign in SignCatalog.All)
            {
                foreach (SupportContext context in Contexts)
                {
                    foreach (bool stack in new[] { false, true })
                    {
                        foreach (SupportStyle paint in new[] { default(SupportStyle), leftover })
                        {
                            SupportDef was = SupportPlanner.AutoFor(sign, context);
                            if (stack && was.Kind != SupportKind.Gantry)
                                was = SupportPlanner.Auto(sign, true);
                            foreach (StyleState state in new[] { plain, off })
                            {
                                PlannedSupport planned = state.Plan(sign, context, paint, stack);
                                cases++;
                                support += planned.Support.Id != was.Id || planned.Plain.Id != was.Id ? 1 : 0;
                                style += planned.Style.Packed != paint.Packed ? 1 : 0;
                                stored += planned.StoredSupportHash != 0u ? 1 : 0;
                            }
                        }
                    }
                }
            }

            Program.Check(cases > 4000, "every sign, context, stack, paint and state was run: " + cases);
            Program.Check(support == 0, $"plain and off give the planner's own support ({support} differ)");
            Program.Check(style == 0, $"and the paint the player has, whatever it was ({style} differ)");
            Program.Check(stored == 0, $"and store nothing of the support ({stored} differ)");
        }

        private static void AnotherProfile()
        {
            SignDef street = Sign("gb17733.street"), speed = Sign("gb.prohib.36");
            var state = new StyleState();
            state.Choose("shanghai");
            SupportStyle paint = StyleState.PaintOf(StyleProfiles.Shanghai);
            PlannedSupport plate = state.Plan(street, default, paint);
            Program.Check(plate.Support.Id == SupportCatalog.PlateShanghai && plate.Plain.Id == SupportCatalog.SinglePole, "a street-name board in Shanghai's profile gets Shanghai's plate post");
            Program.Check(plate.Style.Finish == SupportFinish.LightGrey, "with the plate post's own finish over the profile's graphite (the variant's finish is a default too)");
            PlannedSupport single = state.Plan(speed, default, paint);
            Program.Check(single.Support.Id == SupportCatalog.SinglePole && single.Style.Finish == SupportFinish.Graphite, "a small board has no variant in that profile: the plain post in the profile's paint");

            state.Touch(2, -1, -1);
            paint.Finish = SupportFinish.DarkGrey;
            PlannedSupport touched = state.Plan(street, default, paint);
            Program.Check(touched.Support.Id == SupportCatalog.PlateShanghai && touched.Style.Finish == SupportFinish.DarkGrey, "a finish the player set is over the variant's own, and the support is still the variant");
            Program.Check(touched.Style.BlackBack == StyleProfiles.Shanghai.BlackBack && touched.Style.Root == StyleProfiles.Shanghai.Root, "the parts he did not set are the profile's");

            state.Choose("hangzhou");
            PlannedSupport black = state.Plan(speed, default, StyleState.PaintOf(StyleProfiles.Hangzhou));
            Program.Check(black.Style.BlackBack && black.Style.Finish == SupportFinish.Graphite, "Hangzhou: black backs, matte black poles");
            state.Choose("plain");
            Program.Check(state.Plan(speed, default, default).Style.Packed == 0 && state.Plan(street, default, default).Support.Id == SupportCatalog.SinglePole, "back to plain: nothing of the profile is left");
        }

        private static void Shadow()
        {
            var shadow = new PlainShadow();
            int checkedCases = 0;
            var paints = new[] { default(SupportStyle), new SupportStyle { Finish = SupportFinish.DarkGrey, BlackBack = true, Root = RootPaint.BlueRings }, new SupportStyle { Finish = SupportFinish.LightGrey, Root = RootPaint.Marking } };
            foreach (SignDef sign in SignCatalog.All)
            {
                foreach (SupportContext context in Contexts)
                {
                    foreach (bool stack in new[] { false, true })
                    {
                        foreach (SupportStyle paint in paints)
                        {
                            if (shadow.Checked >= PlainShadow.Limit)
                                break;
                            Program.Check(shadow.Check(sign, context, paint, stack), "the plain profile equals the planner: " + sign.Id);
                            checkedCases++;
                        }
                    }
                }
            }

            Program.Check(shadow.Different == 0 && shadow.First.Count == 0, "no difference was found over " + shadow.Checked + " placements");
            Program.Check(shadow.Checked == PlainShadow.Limit && checkedCases >= PlainShadow.Limit, "it stops at its limit: " + shadow.Checked);
            Program.Check(shadow.Check(Sign("gb.prohib.36"), default, default, false) && shadow.Checked == PlainShadow.Limit, "and compares nothing after it");
            Program.Check(shadow.Summary.Contains("0 different"), "the summary says it: " + shadow.Summary);

            // What it does when the plain profile is not the planner (the answer is made wrong on purpose).
            var caught = new PlainShadow();
            SignDef street = Sign("gb17733.street"), speed = Sign("gb.prohib.36");
            SupportStyle own = new SupportStyle { Finish = SupportFinish.LightGrey };
            PlannedSupport good = SupportPlanner.AutoStyled(speed, default, StyleProfiles.Plain, false, own, PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root);
            Program.Check(caught.Compare(speed, default, own, false, good) && caught.Different == 0 && caught.Checked == 1, "the right answer is the same");
            PlannedSupport otherSupport = SupportPlanner.AutoStyled(street, default, StyleProfiles.Shanghai, false, own, PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root);
            Program.Check(!caught.Compare(street, default, own, false, otherSupport) && caught.Different == 1 && caught.First.Count == 1 && caught.First[0].Contains("gb17733.street"), "another support is a difference, and says which board");
            PlannedSupport otherPaint = good;
            otherPaint.Style = new SupportStyle { Finish = SupportFinish.Graphite };
            Program.Check(!caught.Compare(speed, default, own, false, otherPaint) && caught.Different == 2, "another paint is a difference");
            PlannedSupport otherHash = good;
            otherHash.Plain = SupportCatalog.ById(SupportCatalog.DoublePole);
            Program.Check(!caught.Compare(speed, default, own, false, otherHash) && caught.Different == 3, "another plain support (so another stored hash) is a difference");
            for (int i = 0; i < 20; i++)
                caught.Compare(street, default, own, false, otherSupport);
            Program.Check(caught.First.Count == PlainShadow.KeepDifferences && caught.Different == 23 && caught.Summary.Contains("23 different"), "only the first few differences are kept, all are counted");
        }

        private static void Stored()
        {
            SignDef street = Sign("gb17733.street"), speed = Sign("gb.prohib.36");
            PlannedSupport plate = SupportPlanner.AutoStyled(street, default, StyleProfiles.Shanghai);
            PlannedSupport post = SupportPlanner.AutoStyled(speed, default, StyleProfiles.Shanghai);
            Program.Check(plate.StoredSupportHash == SupportCatalog.ById(SupportCatalog.PlateShanghai).Hash, "a variant is stored by its hash, so that an older mod draws the same support from the catalogue");
            Program.Check(post.StoredSupportHash == 0u, "a board that took no variant is stored as the planner's choice: a profile chosen later does not reach it");
            Program.Check(SupportCatalog.ByHash(plate.StoredSupportHash) != null, "the stored hash is a support of the catalogue");
            // What the stack planners read for a stored 0 is the plain planner: they are not given the profile.
            Program.Check(SupportPlanner.Auto(street).Id == SupportCatalog.SinglePole, "the plain planner's answer for the board that took the plate post");
        }

        private static void Backing()
        {
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Plain) == "" && StyleEvidence.RootOf(StyleProfiles.Plain) == "" && StyleEvidence.FormsOf(StyleProfiles.Plain) == "", "the plain profile claims no evidence: it is the default");
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Shanghai) == "std" && StyleEvidence.FormsOf(StyleProfiles.Shanghai) == "supplier", "Shanghai: the paint is a standard, the forms are as weak as the makers' listing");
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Hangzhou) == "std" && StyleEvidence.FormsOf(StyleProfiles.Hangzhou) == "std", "Hangzhou: standard throughout");
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Beijing) == "photo" && StyleEvidence.FormsOf(StyleProfiles.Beijing) == "", "Beijing: photos, and no form of its own");
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Shenzhen) == "photo" && StyleEvidence.FormsOf(StyleProfiles.Shenzhen) == "photo", "Shenzhen: photos");
            Program.Check(StyleEvidence.RootOf(StyleProfiles.Xuzhou) == "estimate" && StyleEvidence.FormsOf(StyleProfiles.Xuzhou) == "std", "Xuzhou: the plate post a standard, the blue root one county's drawing");
            Program.Check(StyleEvidence.PaintOf(StyleProfiles.Guangzhou) == "estimate" && StyleEvidence.RootOf(StyleProfiles.Guangzhou) == "", "Guangzhou: the grey is an estimate, no root");
            Program.Check(StyleEvidence.Code(Evidence.Standard) == "std" && StyleEvidence.Code(Evidence.Photo) == "photo" && StyleEvidence.Code(Evidence.Supplier) == "supplier" && StyleEvidence.Code(Evidence.Estimate) == "estimate", "the four codes");
        }

        private static void FileState()
        {
            Program.Check(new Preferences().StyleProfiles == FeatureState.On, "the profiles are on by default");
            foreach (string state in new[] { "off", "on", "shadow", "log", "maybe" })
            {
                FeatureState read = Preferences.Parse("{\"features\":{\"styleProfiles\":\"" + state + "\"}}", out _).StyleProfiles;
                Program.Check(read == (state == "off" ? FeatureState.Off : FeatureState.On), "the feature reads '" + state + "' as " + read);
            }

            Program.Check(Preferences.Parse("{\"features\":{\"styleProfiles\":3}}", out _).StyleProfiles == FeatureState.On, "a state that is not text is on");

            string dir = Path.Combine(AppContext.BaseDirectory, "tmp", "ts-profiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, "preferences.json");
                var made = new Preferences();
                Program.Check(made.Save(path), "the defaults are written");
                Preferences read = Preferences.Load(path, out PreferencesRead how, out _);
                Program.Check(how == PreferencesRead.Read && read.Profile == "plain" && read.StyleProfiles == FeatureState.On, "and read back");
                read.Profile = "xuzhou";
                Program.Check(read.Changed && read.Save(path), "a chosen profile is written");
                Preferences again = Preferences.Load(path, out _, out _);
                Program.Check(again.Profile == "xuzhou" && again.RoadFollow == read.RoadFollow && again.AreaFitWorker == read.AreaFitWorker && again.PictureCut == read.PictureCut && again.PlaceShortcuts == read.PlaceShortcuts, "and comes back with the other states as they were");
                Program.Check(new StyleState().Choose(again.Profile) == StyleProfiles.Xuzhou, "the id the file holds is a profile");

                // A file that names a profile this mod does not have: the planner is plain, and the file is not rewritten until the player chooses.
                File.WriteAllText(path, "{\"profile\":\"klingon\",\"features\":{\"styleProfiles\":\"on\",\"future\":\"x\"}}");
                Preferences strange = Preferences.Load(path, out PreferencesRead strangeHow, out _);
                Program.Check(strangeHow == PreferencesRead.Read && strange.Profile == "klingon" && new StyleState().Choose(strange.Profile) == StyleProfiles.Plain && !strange.Changed, "an unknown profile id is plain and the file is as it was");
                strange.Profile = "shanghai";
                Program.Check(strange.Save(path) && Preferences.Load(path, out _, out _).Profile == "shanghai" && File.ReadAllText(path).Contains("\"future\": \"x\""), "choosing one writes it and keeps what the file had besides");

                // A damaged file is not written over by a choice.
                File.WriteAllText(path, "{ broken");
                Preferences damaged = Preferences.Load(path, out PreferencesRead damagedHow, out _);
                damaged.Profile = "hangzhou";
                Program.Check(damagedHow == PreferencesRead.Damaged && !damaged.Save(path) && File.ReadAllText(path) == "{ broken", "a damaged file is left as it is");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
