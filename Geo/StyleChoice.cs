using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The style profile in force and the paint the player has set since it was chosen (0.12.75, DESIGN-support-styles.md 2): pure, one held by the tool. A profile is chosen once for the boards placed next;
    /// what the item stores is the variant's hash and the paint (TsItem.SupportHash, Style), so a board already placed is never dressed again, and the stack planners (Assembly.Plan, Attach) that read a
    /// stored 0 as "the plain planner's choice" are not given the profile.
    /// </summary>
    public sealed class StyleState
    {
        private StyleProfile m_Chosen = StyleProfiles.Plain;

        /// <summary>Off (features.styleProfiles = off): the plain profile whatever was chosen, and the panel shows no row.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>The parts of the paint the player has set since a profile was chosen: they keep the player's value over the profile's and over a variant's own finish.</summary>
        public PaintTouch Touched { get; private set; }

        /// <summary>The profile the player chose (or the file says), whether or not the feature is on.</summary>
        public StyleProfile Chosen => m_Chosen;

        /// <summary>The profile the planner is given: the chosen one, plain when the feature is off.</summary>
        public StyleProfile Effective => Enabled ? m_Chosen : StyleProfiles.Plain;

        /// <summary>The paint of a profile as the style of the boards placed next.</summary>
        public static SupportStyle PaintOf(StyleProfile profile) => new SupportStyle { Finish = profile.Finish, BlackBack = profile.BlackBack, Root = profile.Root };

        /// <summary>Takes the profile of the id (an unknown or empty id is plain) and clears what the player had set: the caller makes the profile's paint the paint of the next boards.</summary>
        public StyleProfile Choose(string id)
        {
            m_Chosen = StyleProfiles.ById(id) ?? StyleProfiles.Plain;
            Touched = PaintTouch.None;
            return m_Chosen;
        }

        /// <summary>The player set a part of the paint (the arguments of PlacementTool.SetStyle: below 0 is "as it is").</summary>
        public void Touch(int finish, int blackBack, int root)
        {
            if (finish >= 0)
                Touched |= PaintTouch.Finish;
            if (blackBack >= 0)
                Touched |= PaintTouch.BlackBack;
            if (root >= 0)
                Touched |= PaintTouch.Root;
        }

        /// <summary>
        /// The support and paint for a board the planner chooses for. The plain profile (or the feature off) is the planner as it was before profiles, with the paint the player has (what the tool
        /// placed with until 0.12.74); another profile is SupportPlanner.AutoStyled with the paint the player has and the parts of it he set.
        /// </summary>
        public PlannedSupport Plan(SignDef sign, SupportContext context, SupportStyle paint, bool stack = false)
        {
            StyleProfile profile = Effective;
            if (profile != StyleProfiles.Plain)
                return SupportPlanner.AutoStyled(sign, context, profile, stack, paint, Touched);
            return Unstyled(sign, context, paint, stack);
        }

        /// <summary>The planner's own choice with the player's paint: what the plain profile has to equal.</summary>
        public static PlannedSupport Unstyled(SignDef sign, SupportContext context, SupportStyle paint, bool stack = false)
        {
            SupportDef plain = SupportPlanner.AutoFor(sign, context);
            if (stack && plain.Kind != SupportKind.Gantry)
                plain = SupportPlanner.Auto(sign, true);
            return new PlannedSupport { Support = plain, Plain = plain, Style = paint, Note = "" };
        }
    }

    /// <summary>
    /// The plain profile against the planner as it was, for the first placements of a session: AutoStyled(plain) with every part of the paint taken from the player must give the support the planner gave
    /// and the paint the player had. There must be no difference; one is logged. Bounded: nothing is compared after Limit placements.
    /// </summary>
    public sealed class PlainShadow
    {
        public const int Limit = 2000, KeepDifferences = 5;

        public int Checked { get; private set; }
        public int Different { get; private set; }
        public readonly List<string> First = new List<string>();

        /// <summary>Compares one placement; true when it was the same (or nothing is compared any more).</summary>
        public bool Check(SignDef sign, SupportContext context, SupportStyle paint, bool stack)
        {
            if (Checked >= Limit)
                return true;
            return Compare(sign, context, paint, stack, SupportPlanner.AutoStyled(sign, context, StyleProfiles.Plain, stack, paint, PaintTouch.Finish | PaintTouch.BlackBack | PaintTouch.Root));
        }

        /// <summary>The comparison itself, with the plain profile's answer given (the tests give it a wrong one).</summary>
        public bool Compare(SignDef sign, SupportContext context, SupportStyle paint, bool stack, PlannedSupport styled)
        {
            Checked++;
            PlannedSupport old = StyleState.Unstyled(sign, context, paint, stack);
            // The stored hash follows from the support and the plain support, so comparing the support and the hash covers both.
            if (old.Support.Id == styled.Support.Id && old.Style.Packed == styled.Style.Packed && old.StoredSupportHash == styled.StoredSupportHash)
                return true;
            Different++;
            if (First.Count < KeepDifferences)
                First.Add($"{sign.Id}: planner {old.Support.Id}/{old.Style.Packed}, plain profile {styled.Support.Id}/{styled.Style.Packed}");
            return false;
        }

        public string Summary => $"plain profile against the planner: {Checked} placement(s) compared, {Different} different{(First.Count > 0 ? " (" + string.Join("; ", First) + ")" : "")}";
    }

    /// <summary>How far a profile is backed, for the panel (the classes of DESIGN-support-styles.md 2: a standard, a photograph, a maker's listing, this mod's estimate).</summary>
    public static class StyleEvidence
    {
        /// <summary>The short code of a class the panel names in both languages (lang: UI.Evidence.*).</summary>
        public static string Code(Evidence evidence)
        {
            switch (evidence)
            {
                case Evidence.Standard: return "std";
                case Evidence.Photo: return "photo";
                case Evidence.Supplier: return "supplier";
                default: return "estimate";
            }
        }

        /// <summary>The class of the profile's paint; empty for the plain profile, which is the default and not a city's.</summary>
        public static string PaintOf(StyleProfile profile) => profile == StyleProfiles.Plain ? "" : Code(profile.PaintWhy.Class);

        /// <summary>The root paint's class, empty when the profile paints no root.</summary>
        public static string RootOf(StyleProfile profile) => profile.Root == RootPaint.None ? "" : Code(profile.RootWhy.Class);

        /// <summary>The weakest class among the profile's forms (the variants it takes), empty when it takes none.</summary>
        public static string FormsOf(StyleProfile profile)
        {
            if (profile.Forms.Length == 0)
                return "";
            Evidence weakest = Evidence.Standard;
            foreach (FormRule rule in profile.Forms)
            {
                if (rule.Why.Class > weakest)
                    weakest = rule.Why.Class;
            }

            return Code(weakest);
        }
    }
}
