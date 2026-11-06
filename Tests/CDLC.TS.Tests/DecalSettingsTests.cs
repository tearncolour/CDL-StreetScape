using System.Collections.Generic;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The decal settings of the materials (Geo/DecalSettings.cs): the mask arithmetic, the policy and its keyword rule, the experiment file's parser, the audit's caps and the census. The game's shaders
    /// and decals are not here: what ValidateMaterial and the keyword do to a real material is for the game's log (Materials: decal audit ...).
    /// </summary>
    internal static class DecalSettingsTests
    {
        public static void RunAll()
        {
            Program.Run("decal settings: layer masks, names, numbers and the float bits", Masks);
            Program.Run("decal settings: the policy, the layer of the objects and the keyword rule", Policy);
            Program.Run("decal settings: the experiment file", Experiment);
            Program.Run("decal settings: the renderers' decal layers (HDRP/Lit takes them from the rendering layer mask)", RenderingDecals);
            Program.Run("decal settings: the audit's lines and caps, the census", AuditAndCensus);
        }

        private static void RenderingDecals()
        {
            // The decal layers are bits 8 to 15, the light layers bits 0 to 7, and the rest stays set as in the game's own batches (BatchManagerSystem: renderingLayerMask uint.MaxValue).
            Program.Check(DecalMasks.RenderingLayerFor(0) == 0xFFFF00FFu, "no decal layer: " + DecalMasks.RenderingLayerFor(0).ToString("x"));
            Program.Check(DecalMasks.RenderingLayerFor(DecalMasks.Vehicles) == 0xFFFF08FFu, "Vehicles is the bit 11 (decal layer 3 is the bit 8 + 3)");
            Program.Check(DecalMasks.RenderingLayerFor(DecalMasks.Terrain) == 0xFFFF01FFu && DecalMasks.RenderingLayerFor(DecalMasks.Buildings | DecalMasks.Other) == 0xFFFF24FFu, "Terrain is the first decal layer, two layers add");
            Program.Check(DecalMasks.RenderingLayerFor(0xFF) == uint.MaxValue && DecalMasks.RenderingLayerFor(0x1FF) == uint.MaxValue, "all eight; a ninth bit lands in the bits that are set already");
            for (int mask = 0; mask < 256; mask += 5)
            {
                uint layer = DecalMasks.RenderingLayerFor(mask);
                Program.Check((layer & 0xFFu) == 0xFFu && (layer >> 16) == 0xFFFFu && ((layer >> 8) & 0xFFu) == (uint)mask, "the light layers and the high bits stay set, the decal layers are exactly the mask: " + mask);
            }

            // The default stays what 0.12.81 had until the owner's tests say which bits the game's decals read.
            Program.Check(DecalPolicy.ObjectRenderingDecals == 0xFF && DecalPolicy.ObjectRenderingLayer == uint.MaxValue, "the renderers of this mod have every bit set by default");

            Program.Check(DecalMasks.DescribeRendering(0x8ff) == "0x8ff (decal layers Vehicles (0x8))" && DecalMasks.DescribeRendering(0xFFFF00FFu) == "0xffff00ff (decal layers none (0x0))", "described for the log");

            var problems = new List<string>();
            Program.Check(MaterialExperiment.Parse("renderingDecals = none", problems).RenderingLayer == 0xFFFF00FFu && problems.Count == 0, "renderingDecals none is the mask without decal layers");
            Program.Check(MaterialExperiment.Parse("renderingDecals = Vehicles", null).RenderingLayer == 0xFFFF08FFu && MaterialExperiment.Parse("renderingDecals = Terrain|Roads|Buildings|Vehicles|Creatures|Other", null).RenderingLayer == 0xFFFF3FFFu, "by names");
            Program.Check(MaterialExperiment.Parse("renderingDecals = 0xff", null).RenderingLayer == uint.MaxValue, "a number up to 0xff");
            problems.Clear();
            MaterialExperiment tooBig = MaterialExperiment.Parse("renderingDecals = 0x100", problems);
            Program.Check(!tooBig.RenderingLayer.HasValue && problems.Count == 1 && problems[0].StartsWith("line 1:") && problems[0].Contains("renderingDecals"), "a bit above the eighth decal layer is a wrong line: " + string.Join("; ", problems));
            problems.Clear();
            Program.Check(!MaterialExperiment.Parse("renderingDecals = Vehicle", problems).RenderingLayer.HasValue && problems.Count == 1, "an unknown layer name is a wrong line");

            Program.Check(MaterialExperiment.Parse("renderingLayer = 0x8ff\nrenderingDecals = none", null).RenderingLayer == 0xFFFF00FFu && MaterialExperiment.Parse("renderingDecals = none\nrenderingLayer = 0x8ff", null).RenderingLayer == 0x8FFu, "the last of the two lines wins");
            Program.Check(MaterialExperiment.Parse("renderingDecals = none", null).Equals(MaterialExperiment.Parse("renderingLayer = 0xffff00ff", null)) && !MaterialExperiment.Parse("renderingDecals = none", null).Equals(MaterialExperiment.Parse("renderingDecals = Vehicles", null)), "equal by the mask, not by the key");
            Program.Check(MaterialExperiment.Parse("renderingDecals = none", null).Describe() == "renderingLayer 0xffff00ff (decal layers none (0x0))", "described: " + MaterialExperiment.Parse("renderingDecals = none", null).Describe());
        }

        private static void Masks()
        {
            Program.Check(DecalMasks.TryParse("Vehicles", out int m) && m == 8, "a layer by name");
            Program.Check(DecalMasks.TryParse("buildings|OTHER", out m) && m == 0x24, "names joined, any case: " + m);
            Program.Check(DecalMasks.TryParse("Terrain + Roads, Buildings", out m) && m == 7, "joined by + and a comma too: " + m);
            Program.Check(DecalMasks.TryParse("0x40", out m) && m == 0x40, "a bit that is no layer of the game, in hex");
            Program.Check(DecalMasks.TryParse("8", out m) && m == 8 && DecalMasks.TryParse("none", out m) && m == 0 && DecalMasks.TryParse("0", out m) && m == 0, "a decimal number and none");
            Program.Check(DecalMasks.TryParse("all", out m) && m == 0x3f, "all is the six layers of the game: " + m);
            Program.Check(DecalMasks.TryParse("Vehicles|0x40", out m) && m == 0x48, "a name and a number: " + m);
            Program.Check(!DecalMasks.TryParse("", out _) && !DecalMasks.TryParse("Vehicle", out _) && !DecalMasks.TryParse("Vehicles|", out _) && !DecalMasks.TryParse("0xzz", out _) && !DecalMasks.TryParse(null, out _), "what is no mask");

            Program.Check(DecalMasks.Describe(0x24) == "Buildings|Other (0x24)", "described: " + DecalMasks.Describe(0x24));
            Program.Check(DecalMasks.Describe(0) == "none (0x0)" && DecalMasks.Describe(8) == "Vehicles (0x8)", "none and one layer");
            Program.Check(DecalMasks.Describe(0x40) == "0x40", "a bit that is no layer says so by being a number: " + DecalMasks.Describe(0x40));
            Program.Check(DecalMasks.Describe(0x44) == "Buildings|0x40 (0x44)", "a layer and a spare bit: " + DecalMasks.Describe(0x44));
            Program.Check(DecalMasks.Describe(0x3f800000).Contains("float"), "what a mod stored as a float is told so: " + DecalMasks.Describe(0x3f800000));

            Program.Check(DecalMasks.ToFloat(0x20) != 32f && DecalMasks.FromFloat(DecalMasks.ToFloat(0x20)) == 0x20, "0x20 is not 32.0: the float holds the bits");
            Program.Check(DecalMasks.FromFloat(1.0f) == 0x3f800000, "the float 1.0 is the bits 0x3f800000 (the odd masks the census found in a mod's road decals)");
            foreach (int bits in new[] { 0, 1, 8, 0x20, 0x3f, 0x3f800000, -1 })
                Program.Check(DecalMasks.FromFloat(DecalMasks.ToFloat(bits)) == bits || (bits == -1), "round trip of " + bits);
        }

        private static void Policy()
        {
            // The materials with the property are on a layer that no decal of the game lists by default (a prefab's DecalProperties: Terrain|Roads|Buildings; a surface's RenderedArea: Terrain).
            Program.Check(DecalPolicy.ObjectMask == DecalMasks.Vehicles, "the objects' layer is Vehicles");
            Program.Check((DecalPolicy.ObjectMask & DecalMasks.Other) == 0 && (DecalPolicy.ObjectMask & DecalMasks.Buildings) == 0 && (DecalPolicy.ObjectMask & (DecalMasks.Terrain | DecalMasks.Roads)) == 0,
                "none of the layers a building, ground or road decal lists");
            Program.Check(DecalPolicy.SupportDecals == 0f, "no decals are taken");

            Program.Check(DecalPolicy.DisableKeywordWanted(true, 0f, null) == true, "a material that supports no decals has the keyword on");
            Program.Check(DecalPolicy.DisableKeywordWanted(true, 1f, null) == false, "one that supports them has it off");
            Program.Check(DecalPolicy.DisableKeywordWanted(false, 0f, null) == null, "a material with no _SupportDecals is left alone");
            Program.Check(DecalPolicy.DisableKeywordWanted(true, 0f, false) == false && DecalPolicy.DisableKeywordWanted(false, 0f, true) == true, "the experiment's disableDecals overrides, even for a material with no property");
        }

        private static void Experiment()
        {
            var problems = new List<string>();
            MaterialExperiment e = MaterialExperiment.Parse("# a test\r\ndecalMask = Vehicles|0x40   # the spare bit too\r\n\r\nSUPPORTDECALS=0\ndisableDecals = on\nqueue = 2000\nrenderingLayer = 0x100\n", problems);
            Program.Check(problems.Count == 0, "a clean file has no problems: " + string.Join("; ", problems));
            Program.Check(e.DecalMask == 0x48 && e.SupportDecals == 0f && e.DisableDecals == true && e.Queue == 2000 && e.RenderingLayer == 0x100u, "every key read, comments, CRLF and case ignored");
            Program.Check(!e.IsEmpty && e.Describe().Contains("decalMask Vehicles|0x40 (0x48)") && e.Describe().Contains("queue 2000") && e.Describe().Contains("renderingLayer 0x100"), "described: " + e.Describe());

            MaterialExperiment none = MaterialExperiment.Parse("# nothing\n\n", problems);
            Program.Check(none.IsEmpty && none.Equals(MaterialExperiment.None) && none.Describe().Contains("defaults") && MaterialExperiment.Parse(null, null).IsEmpty, "an empty or missing file is the defaults");

            problems.Clear();
            MaterialExperiment bad = MaterialExperiment.Parse("decalMask = Vehicle\nqueue = 99\nqueue = abc\nsupportDecals = 2\ndisableDecals = maybe\nrenderingLayer = x\nspeed = 3\njust text\nqueue = 2300\n", problems);
            Program.Check(problems.Count == 8, "each wrong line is named, 8 of them: " + problems.Count + " (" + string.Join("; ", problems) + ")");
            Program.Check(problems.Exists(p => p.StartsWith("line 1:")) && problems.Exists(p => p.StartsWith("line 8: no key")) && problems.Exists(p => p.Contains("unknown key 'speed'")), "with its line number and why");
            Program.Check(bad.Queue == 2300 && !bad.DecalMask.HasValue && !bad.SupportDecals.HasValue && !bad.DisableDecals.HasValue && !bad.RenderingLayer.HasValue, "the lines that are right still count, the wrong ones leave nothing");

            Program.Check(MaterialExperiment.Parse("queue = 2000\nqueue = 2300", null).Queue == 2300, "a key given twice takes its last line");
            Program.Check(MaterialExperiment.Parse("renderingLayer = all", null).RenderingLayer == uint.MaxValue && MaterialExperiment.Parse("renderingLayer = none", null).RenderingLayer == 0u, "all and none for the rendering layer");
            Program.Check(MaterialExperiment.Parse("queue = 1000", null).Queue == 1000 && MaterialExperiment.Parse("queue = 5000", null).Queue == 5000 && !MaterialExperiment.Parse("queue = 5001", null).Queue.HasValue, "the queue's range is 1000 to 5000");
            Program.Check(MaterialExperiment.Parse("supportDecals = on", null).SupportDecals == 1f && MaterialExperiment.Parse("supportDecals = off", null).SupportDecals == 0f, "supportDecals takes on and off as 1 and 0");

            Program.Check(MaterialExperiment.Parse("decalMask = 8", null).Equals(MaterialExperiment.Parse("decalMask = Vehicles", null)) && !MaterialExperiment.Parse("decalMask = 8", null).Equals(MaterialExperiment.Parse("decalMask = 0x20", null)), "equal by value");
            Program.Check(!MaterialExperiment.Parse("queue = 2000", null).Equals(MaterialExperiment.Parse("queue = 2001", null)) && !MaterialExperiment.Parse("disableDecals = on", null).Equals(MaterialExperiment.Parse("disableDecals = off", null)), "each key counts in the comparison");
            Program.Check(!MaterialExperiment.Parse("renderingLayer = 0x100", null).Equals(MaterialExperiment.Parse("renderingLayer = 0x200", null)) && !MaterialExperiment.Parse("supportDecals = 1", null).Equals(MaterialExperiment.Parse("supportDecals = 0", null)), "the rendering layer and the support count in the comparison too");
        }

        private static DecalSnapshot Snap(string shader, int queue, float support, int mask, bool keyword) =>
            new DecalSnapshot { Shader = shader, Queue = queue, HasSupport = true, Support = support, HasMask = true, Mask = mask, KeywordOn = keyword, Keywords = "_DISABLE_DECALS" };

        private static void AuditAndCensus()
        {
            var audit = new DecalAudit();
            DecalSnapshot a = Snap("BH/SG_DefaultShader", 2225, 0f, 8, true);
            string first = audit.Note("detail", a);
            Program.Check(first != null && first.Contains("detail") && first.Contains("BH/SG_DefaultShader") && first.Contains("queue 2225") && first.Contains("_SupportDecals 0") && first.Contains("Vehicles (0x8)") && first.Contains("_DISABLE_DECALS on"), "the first state of a family is a line: " + first);
            Program.Check(audit.Note("detail", a) == null && audit.Note("detail", a) == null, "the same state again is counted, not said");
            string changed = audit.Note("detail", Snap("BH/SG_DefaultShader", 2225, 0f, 8, false));
            Program.Check(changed != null && changed.Contains("_DISABLE_DECALS off"), "a different state (the keyword off) is a line of its own: " + changed);
            Program.Check(audit.Note("detail", Snap("BH/SG_DefaultShader", 2000, 0f, 8, true)) != null && audit.Note("detail", Snap("BH/SG_DefaultShader", 2100, 0f, 8, true)) == null, "three states of a family are said, the fourth is only counted");
            Program.Check(audit.Note("face", a) != null, "another family has its own lines");

            var many = new DecalAudit();
            int said = 0;
            for (int i = 0; i < 40; i++)
                said += many.Note("family" + i, Snap("HDRP/Lit", 2225, 0f, 0, true)) != null ? 1 : 0;
            Program.Check(said == 24, "at most 24 lines in all: " + said);
            Program.Check(many.Families == 40, "every family is still counted");

            string summary = audit.Summary();
            Program.Check(summary.Contains("detail 6 made in 4 states") && summary.Contains("face 1 made in 1 state"), "the summary counts them: " + summary);
            Program.Check(new DecalAudit().Summary().Contains("none made"), "an empty audit says none made");
            audit.Clear();
            Program.Check(audit.Families == 0 && audit.Note("detail", a) != null, "cleared, a family says its first state again");

            Program.Check(Snap("s", 1, 0f, 8, true).Key != Snap("s", 1, 0f, 0x20, true).Key && Snap("s", 1, 0f, 8, true).Key != Snap("s", 1, 1f, 8, true).Key, "the key tells the mask and the support apart");
            var noProperty = new DecalSnapshot { Shader = "HDRP/Lit", Queue = 2225, KeywordOn = true, Keywords = "" };
            Program.Check(noProperty.ToString().Contains("_SupportDecals none") && noProperty.ToString().Contains("no property"), "a material with no decal property says so: " + noProperty);

            var census = new List<DecalCensus.Item>();
            for (int i = 0; i < 224; i++)
                census.Add(new DecalCensus.Item { Mask = 0x24 });
            for (int i = 0; i < 10; i++)
                census.Add(new DecalCensus.Item { Mask = 1 });
            census.Add(new DecalCensus.Item { Mask = 0x3f800000 });
            census.Add(new DecalCensus.Item { Mask = 0x48 });
            census.Add(new DecalCensus.Item { Mask = null });
            string line = DecalCensus.Format(census);
            Program.Check(line.Contains("237 material(s)") && line.IndexOf("Buildings|Other (0x24) x224") < line.IndexOf("Terrain (0x1) x10"), "the census counts and puts the commonest first: " + line);
            Program.Check(line.Contains("listing Other 224") && line.Contains("Vehicles 1") && line.Contains("0x40 1") && line.Contains("no mask property 1"), "and says who lists Other, Vehicles and the spare bit: " + line);
            var spread = new List<DecalCensus.Item>();
            for (int i = 0; i < 12; i++)
                spread.Add(new DecalCensus.Item { Mask = 1 << i });
            Program.Check(DecalCensus.Format(spread).Contains("and 4 more mask(s)"), "more than 8 masks are cut and counted");
        }
    }
}
