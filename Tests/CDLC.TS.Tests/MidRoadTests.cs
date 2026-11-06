using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>A sign on posts in the middle of the road (0.12.80; the owner: "这种路牌应该能摆在路中间"): where across the road the post stands, which traffic the board faces, which supports take it, and what the keys and the line of help say.</summary>
    internal static class MidRoadTests
    {
        public static void RunAll()
        {
            Program.Run("mid road: only the posts take it", Supports);
            Program.Run("mid road: the supports the panel's button is offered for", Offered);
            Program.Run("mid road: the cursor over the carriageway, and beyond it", Inside);
            Program.Run("mid road: the lateral of the post", Lateral);
            Program.Run("mid road: the traffic the board faces", Traffic);
            Program.Run("mid road: the keys and the line of help", Hints);
        }

        // A two-way road of four lanes as the game reads it: the lanes of the traffic along the road's direction on the right (positive), the others on the left.
        private static readonly float[] Forward = { 1.75f, 5.25f }, Backward = { -1.75f, -5.25f };

        private static void Supports()
        {
            Program.Check(MidRoad.Takes(SupportKind.SinglePole) && MidRoad.Takes(SupportKind.DoublePole) && MidRoad.Takes(SupportKind.TriplePole), "one, two and three posts");
            foreach (SupportKind kind in new[] { SupportKind.Cantilever, SupportKind.Gantry, SupportKind.DoubleCantilever, SupportKind.Attached, SupportKind.WallFlat, SupportKind.WallBracket })
                Program.Check(!MidRoad.Takes(kind), kind + ": an arm, a beam, a wall or another mod's pole is not put across a road by hand");
            foreach (SupportKind kind in Enum.GetValues(typeof(SupportKind)))
                Program.Check(MidRoad.Takes(kind) == KerbMargin.Applies(kind), kind + ": the posts the kerb distance moves are the posts this puts in the road");
        }

        // The support page's button (0.12.84) is shown for the supports the panel lists with the flag "mid" (TsUiSystem.SupportsJson: MidRoad.Takes of the kind): the posts and the street-name plate posts, no arm, no gantry.
        private static void Offered()
        {
            string[] expected = { "pole.single", "pole.double", "pole.triple", "plate.sh", "plate.xz", "plate.hz", "plate.sz" };
            string[] offered = SupportCatalog.Offerable.Where(def => MidRoad.Takes(def.Kind)).Select(def => def.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Program.Check(offered.SequenceEqual(expected.OrderBy(id => id, StringComparer.Ordinal)), "the listed supports the button is offered for: " + string.Join(", ", offered));
            Program.Check(SupportCatalog.Offerable.Where(def => def.Family == "gantry" || def.Family == "cantilever").All(def => !MidRoad.Takes(def.Kind)), "no gantry or cantilever is offered it");
            Program.Check(SupportCatalog.Offerable.Where(def => SupportKinds.OnWall(def.Kind) || SupportKinds.Hosted(def.Kind)).All(def => !MidRoad.Takes(def.Kind)), "no wall support and no other mod's pole is offered it");
        }

        private static void Inside()
        {
            Program.Check(MidRoad.Inside(0f, true, -7.5f, 7.5f, 12f), "the middle of a carriageway of 15 m");
            Program.Check(MidRoad.Inside(7.7f, true, -7.5f, 7.5f, 12f) && MidRoad.Inside(-7.7f, true, -7.5f, 7.5f, 12f), "within 0.3 m beyond its edges still counts: the cursor's own error");
            Program.Check(!MidRoad.Inside(7.9f, true, -7.5f, 7.5f, 12f) && !MidRoad.Inside(-7.9f, true, -7.5f, 7.5f, 12f), "on the footway the post is at the road's side as before");
            Program.Check(MidRoad.Inside(11f, false, 0f, 0f, 12f) && !MidRoad.Inside(12.4f, false, 0f, 0f, 12f), "no carriageway read: the road's half width, the game's");
            Program.Check(!MidRoad.Inside(float.NaN, true, -7.5f, 7.5f, 12f), "not a number is not over the road");
            Program.Check(!MidRoad.Inside(0f, true, -0.2f, 0.2f, 12f), "a carriageway narrower than the gaps either side has no room for a post");
        }

        private static void Lateral()
        {
            Program.Check(MidRoad.Lateral(3.3f, true, -7.5f, 7.5f, 12f) == 3.3f, "the cursor's place across the road");
            Program.Check(MidRoad.Lateral(-3.3f, true, -7.5f, 7.5f, 12f) == -3.3f, "on the left as on the right");
            Program.Check(Math.Abs(MidRoad.Lateral(7.7f, true, -7.5f, 7.5f, 12f) - 7.2f) < 1e-5f, "kept 0.3 m in from the edge");
            Program.Check(Math.Abs(MidRoad.Lateral(-9f, true, -7.5f, 7.5f, 12f) + 7.2f) < 1e-5f, "and from the left edge");
            Program.Check(MidRoad.Lateral(0.3f, true, -7.5f, 7.5f, 12f) == 0f && MidRoad.Lateral(-0.35f, true, -7.5f, 7.5f, 12f) == 0f, "the centre line draws it in within 0.35 m");
            Program.Check(MidRoad.Lateral(0.36f, true, -7.5f, 7.5f, 12f) == 0.36f, "and lets go beyond it");
            Program.Check(Math.Abs(MidRoad.Lateral(0f, true, 0.02f, 7.5f, 12f) - 0.32f) < 1e-5f, "the centre line is not on a carriageway that starts 2 cm right of it: no magnet, the place kept in from the edge (0.05 would not tell: 0.05f + 0.3f is 0.35000002, past the magnet either way)");
            Program.Check(MidRoad.Lateral(0.1f, true, 1f, 7.5f, 12f) == 1.3f, "no centre line on the carriageway (it starts at 1 m): no magnet, kept in from the edge");
            Program.Check(MidRoad.Lateral(2f, false, 0f, 0f, 12f) == 2f && Math.Abs(MidRoad.Lateral(40f, false, 0f, 0f, 12f) - 11.7f) < 1e-5f, "no carriageway read: by the road's half width");
            Program.Check(Math.Abs(MidRoad.Lateral(2f, true, 1f, 1.4f, 12f) - 1.2f) < 1e-5f, "a carriageway too narrow to keep in from (Inside refuses it, so this only guards the arithmetic): its middle");
        }

        private static void Traffic()
        {
            Program.Check(MidRoad.Traffic(2.5f, Forward, Backward) == 1, "right of the centre line, by a forward lane: the forward traffic");
            Program.Check(MidRoad.Traffic(-2.5f, Forward, Backward) == -1, "left of it, by a backward lane: the other traffic");
            Program.Check(MidRoad.Traffic(-0.3f, Forward, Backward) == -1, "a hair left of the centre line the backward lane is nearer (the strip rule took the right-hand lanes first and said forward)");
            Program.Check(MidRoad.Traffic(0.3f, Forward, Backward) == 1, "a hair right of it, forward");
            Program.Check(MidRoad.Traffic(0f, Forward, Backward) == 1, "on the centre line the lanes are equally near: the post's side, which is the right");
            Program.Check(MidRoad.Traffic(-0.0f, Forward, Backward) == 1, "negative zero is the centre line too");
            Program.Check(MidRoad.Traffic(7.2f, Forward, Backward) == 1 && MidRoad.Traffic(-7.2f, Forward, Backward) == -1, "beyond the outer lanes: that lane's traffic");
            Program.Check(MidRoad.Traffic(3.5f, Forward, Backward) == 1, "between two lanes of one traffic");
            Program.Check(MidRoad.Traffic(-1f, new[] { 1f }, new[] { -3f }) == -1, "two lanes as near as each other, the post left of the centre line: the traffic on its side, the backward");
            Program.Check(MidRoad.Traffic(1f, new[] { 3f }, new[] { -1f }) == 1, "and right of it, the forward");
            Program.Check(MidRoad.Traffic(1f, null, null) == 1 && MidRoad.Traffic(-1f, null, null) == -1, "no lane read: the side of the post");
            Program.Check(MidRoad.Traffic(-1f, new float[0], new float[0]) == -1, "empty lists the same");
            Program.Check(MidRoad.Traffic(-3f, new[] { 1.75f }, null) == 1, "only one traffic read: that traffic, whichever side the post is");
            Program.Check(MidRoad.Traffic(2f, new[] { float.NaN, 1.75f }, new[] { float.PositiveInfinity }) == 1, "a lane that is not a number is not one");
        }

        private static void Hints()
        {
            HintInfo place = new HintInfo { Kind = "place" };
            List<string[]> off = HintText.Keys(place, true), offEn = HintText.Keys(place, false);
            Program.Check(!off.Any(k => k[0] == "/") && !offEn.Any(k => k[0] == "/"), "no / key is listed where the support is no post, and by default nothing changes");
            HintInfo post = new HintInfo { Kind = "place", MidKey = true };
            List<string[]> keys = HintText.Keys(post, true), keysEn = HintText.Keys(post, false);
            Program.Check(keys.Count == off.Count + 1 && keysEn.Count == offEn.Count + 1, "a post adds one key");
            Program.Check(keys.Any(k => k[0] == "/" && k[1] == "路中") && keysEn.Any(k => k[0] == "/" && k[1] == "in the road"), "the / key says what it does, in both languages: " + string.Join("|", keys.Select(k => k[0])));
            Program.Check(HintText.Keys(new HintInfo { Kind = "place", MidKey = true, MidOn = true }, false).Any(k => k[0] == "/" && k[1].Contains("roadside")), "with the mode on it says how to leave it");
            Program.Check(!HintText.Keys(new HintInfo { Kind = "edit", MidKey = true }, true).Any(k => k[0] == "/"), "only the placing mode has it");
            foreach (string kind in new[] { "attach", "host", "wall" })
                Program.Check(!HintText.Keys(new HintInfo { Kind = kind, MidKey = true }, true).Any(k => k[0] == "/"), kind + ": joining a support, a host or a wall has no middle of the road");
            // The line of help.
            string plain = HintText.Of(place, true), plainEn = HintText.Of(place, false);
            Program.Check(plain == "单击放置。" && plainEn == "Click to place.", "by default the line is what it was: " + plain + " / " + plainEn);
            Program.Check(HintText.Of(new HintInfo { Kind = "place", MidKey = true }, true) == plain, "a post with the mode off: the same line");
            Program.Check(HintText.Of(new HintInfo { Kind = "place", MidKey = true, MidOn = true, MidHere = true }, true) == "路中：单击放置。", "the mode on: it says so: " + HintText.Of(new HintInfo { Kind = "place", MidKey = true, MidOn = true }, true));
            Program.Check(HintText.Of(new HintInfo { Kind = "place", MidKey = true, MidOn = true, MidHere = true }, false) == "In the road: Click to place.", "and in English");
            // The mode is on and the cursor is not over the carriageway: the post would stand at the road's side, and the line says what to do instead of claiming the road.
            string away = HintText.Of(new HintInfo { Kind = "place", MidKey = true, MidOn = true }, true), awayEn = HintText.Of(new HintInfo { Kind = "place", MidKey = true, MidOn = true }, false);
            Program.Check(away == "路中：光标移到车行道上。单击放置。" && awayEn == "In the road: put the cursor on the lanes. Click to place.", "the mode on, the cursor off the lanes: it says where to put it: " + away + " / " + awayEn);
            Program.Check(away.Length < 28 && awayEn.Length < 64, "within the state line's budget");
            Program.Check(!new HintInfo { Kind = "place", MidKey = true, MidOn = true }.Equals(new HintInfo { Kind = "place", MidKey = true, MidOn = true, MidHere = true }), "the panel's line is made again when the post comes into the road or leaves it");
            string refused = HintText.Of(new HintInfo { Kind = "place", MidOn = true }, true), refusedEn = HintText.Of(new HintInfo { Kind = "place", MidOn = true }, false);
            Program.Check(refused.Contains("立柱") && refused.EndsWith("单击放置。") && refusedEn.Contains("posts only") && refusedEn.EndsWith("Click to place."), "the mode on a support that is no post: refused in a short line: " + refused);
            Program.Check(refused.Length < 40 && refusedEn.Length < 110, "short: the owner dislikes standing text");
            Program.Check(!new HintInfo { Kind = "place", MidKey = true }.Equals(new HintInfo { Kind = "place" }) && !new HintInfo { Kind = "place", MidOn = true }.Equals(new HintInfo { Kind = "place" }), "the panel's line is made again when the mode changes (Equals)");
            Program.Check(ShortcutPolicy.ToolKeys.Any(k => k.Path.EndsWith("/slash", StringComparison.OrdinalIgnoreCase)), "the / key is a tool key: the game's own binding of it is held back while placing");
        }
    }
}
