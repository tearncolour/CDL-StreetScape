using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>The band of a board by the road it stands at (0.6.0): a highway by its limit as Table 3 has it, a city street never above the 40 to 70 km/h band.</summary>
    internal static class CityBandTests
    {
        public static void RunAll()
        {
            Program.Run("city band: the band of a road by its limit and its rules", Table);
            Program.Run("city band: ResolveSpeed keeps what it did and takes the road's rules", Resolve);
        }

        private static void Table()
        {
            // A highway: Table 3 as it was (the four bands by the limit).
            foreach ((float kmh, string band) in new[] { (130f, "100"), (120f, "100"), (100f, "100"), (99f, "80"), (80f, "80"), (71f, "80"), (70f, "60"), (60f, "60"), (40f, "60"), (39f, "30"), (20f, "30"), (0f, "30") })
                Program.Check(GuideTemplates.BandForRoad(kmh, true) == band && GuideTemplates.BandForSpeed(kmh) == band, kmh + " km/h on a highway is band " + band + ": " + GuideTemplates.BandForRoad(kmh, true));

            // A city street: the same up to 70 km/h, and the 40 to 70 band above it.
            foreach ((float kmh, string band) in new[] { (130f, "60"), (120f, "60"), (100f, "60"), (99f, "60"), (80f, "60"), (71f, "60"), (70f, "60"), (60f, "60"), (50f, "60"), (40f, "60"), (39f, "30"), (30f, "30"), (0f, "30") })
                Program.Check(GuideTemplates.BandForRoad(kmh, false) == band, kmh + " km/h on a city street is band " + band + ": " + GuideTemplates.BandForRoad(kmh, false));

            // A city street is never taller in the characters than the same speed on a highway, and never a band more than 60.
            for (int kmh = 0; kmh <= 140; kmh++)
            {
                float city = GuideTemplates.CharHeightOf(GuideTemplates.BandForRoad(kmh, false)), highway = GuideTemplates.CharHeightOf(GuideTemplates.BandForRoad(kmh, true));
                Program.Check(city <= highway + 1e-6f && city <= 0.40f + 1e-6f, "at " + kmh + " km/h a city street's characters are at most 40 cm and not above a highway's: " + city + " / " + highway);
            }

            // The character height of the bands themselves is what Table 3 says (the mid values the mod uses): 65, 55, 40, 30 cm.
            Program.Near(GuideTemplates.CharHeightOf("100"), 0.65f, 1e-6f, "band 100: 65 cm");
            Program.Near(GuideTemplates.CharHeightOf("80"), 0.55f, 1e-6f, "band 80: 55 cm");
            Program.Near(GuideTemplates.CharHeightOf("60"), 0.40f, 1e-6f, "band 60: 40 cm");
            Program.Near(GuideTemplates.CharHeightOf("30"), 0.30f, 1e-6f, "band 30: 30 cm");
        }

        private static void Resolve()
        {
            SignDef board = SignCatalog.ChoiceById("gb.guide8.01").Def;
            SignDef exit = SignCatalog.ById("guide.exit.advance");
            string draft = board.Template.Default().With("speed", "auto").ToString();
            GuideSpec Parsed(SignDef def, string text) => def.Template.Parse(text);

            // A city street of 80 km/h: the 60 band; the same limit on a highway: the 80 band; the call before 0.6.0 (no rules given) is the highway's.
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 80f, true, false)).Is("speed", "60"), "80 km/h, city street: band 60");
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 80f, true, true)).Is("speed", "80"), "80 km/h, highway: band 80");
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 80f, true)).Is("speed", "80"), "80 km/h, rules not given: band 80 (the old call)");
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 100f, true, false)).Is("speed", "60"), "100 km/h, city street: band 60");
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 100f, true, true)).Is("speed", "100"), "100 km/h, highway: band 100");
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 30f, true, false)).Is("speed", "30"), "30 km/h, city street: band 30");

            // No road: band 60 whatever the rules say; a band chosen by hand is kept; a sign without the setting is untouched.
            Program.Check(Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 0f, false, false)).Is("speed", "60") && Parsed(board, GuideTemplates.ResolveSpeed(board, draft, 0f, false, true)).Is("speed", "60"), "no road: band 60");
            string fixedSpec = board.Template.Default().With("speed", "100").ToString();
            Program.Check(GuideTemplates.ResolveSpeed(board, fixedSpec, 30f, true, false) == fixedSpec && GuideTemplates.ResolveSpeed(board, fixedSpec, 130f, true, true) == fixedSpec, "a band chosen by hand is kept");
            SignDef countdown = SignCatalog.ById("guide.countdown");
            Program.Check(GuideTemplates.ResolveSpeed(countdown, "", 30f, true, false) == "", "a sign without the setting is untouched");

            // The label of the automatic choice says what it does now.
            GuideField speed = board.Template.FieldOf("speed");
            Program.Check(speed != null && speed.Choices[0].Value == "auto" && speed.Choices[0].Zh.Contains("城市") && speed.Choices[0].En.Contains("city street"), "the automatic choice says that a city street is kept to the 40–70 band");
            Program.Check(Parsed(exit, GuideTemplates.ResolveSpeed(exit, exit.Template.Default().With("speed", "auto").ToString(), 100f, true, false)).Is("speed", "60"), "the same on an exit board of a template that has the setting");
        }
    }
}
