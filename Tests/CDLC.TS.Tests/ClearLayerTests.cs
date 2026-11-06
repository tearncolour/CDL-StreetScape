using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    internal static class ClearLayerTests
    {
        public static void RunAll()
        {
            Program.Run("clear layers: bridge, ground and tunnel are distinct", SeparateDecks);
            Program.Run("clear layers: a sloping and crested road retains its surface after saving", SavedSurfaces);
            Program.Run("clear layers: capacity, edits and corrupt height profiles are safe", CapacityAndDamage);
            Program.Run("clear layers: legacy road slope and automatic replacement stay on their deck", LegacyAndReplacement);
        }

        private static List<Vec3> Rect(float y) => new List<Vec3> { new Vec3(-5f, y, -10f), new Vec3(5f, y, -10f), new Vec3(5f, y, 10f), new Vec3(-5f, y, 10f) };

        private static ClearRegion Saved(IList<Vec3> world, Vec3 origin, float yaw, bool strip = false)
        {
            GuideTemplate template = SignCatalog.ById(VanillaClear.Id).Template;
            string text = ClearElevation.WithOutline(template.Default().With(VanillaClear.KeepKey, "all"), world, origin, yaw, strip);
            Program.Check(text != null, "the complete polygon and height profile fit the saved spec");
            return text == null ? null : ClearRegion.Of(new ItemRecord(template.Hash, origin, yaw, 0u, text));
        }

        private static void SeparateDecks()
        {
            ClearRegion ground = Saved(Rect(0f), Vec3.Zero, 0f), bridge = Saved(Rect(12f), new Vec3(0f, 12f, 0f), 0f), tunnel = Saved(Rect(-8f), new Vec3(0f, -8f, 0f), 0f);
            foreach (ClearRegion region in new[] { ground, bridge, tunnel })
            {
                float own = region.Outline[0].Y;
                foreach (float y in new[] { 0f, 12f, -8f })
                {
                    Program.Check(region.Covers(new VanillaMarking { X = 0f, Y = y, Z = 0f, Owner = MarkingOwner.Edge }) == (y == own), "only the matching deck's lane is hidden");
                    Program.Check(region.Contains(new Vec3(0f, y, 0f)) == (y == own), "automatic replacement uses the same surface rule");
                }
            }
            Program.Check(!ground.SameAs(bridge), "a height-only change invalidates the area's cache");
            Program.Check(VanillaClear.Changes(new Dictionary<int, ClearRegion> { { 1, ground } }, new Dictionary<int, ClearRegion> { { 1, bridge } }).Count == 2, "both old and new height-only footprints are reconsidered");
            Program.Check(!ground.Contains(new Vec3(0f, float.NaN, 0f)), "a non-finite surface is never cleared");
        }

        private static void SavedSurfaces()
        {
            Vec3 origin = new Vec3(130f, 30f, -45f);
            float yaw = 0.73f;
            var local = new List<Vec3> { new Vec3(-5f, -10f, -50f), new Vec3(-5f, 0f, 0f), new Vec3(-5f, 10f, 50f), new Vec3(5f, 10f, 50f), new Vec3(5f, 0f, 0f), new Vec3(5f, -10f, -50f) };
            ClearRegion slope = Saved(MarkingPolygon.ToWorld(local, origin, yaw), origin, yaw, true);
            foreach (float z in new[] { -40f, -20f, 0f, 20f, 40f })
            {
                Vec3 p = MarkingPolygon.ToWorld(new[] { new Vec3(0f, z * 0.2f, z) }, origin, yaw)[0];
                Program.Check(slope.Contains(p) && !slope.Contains(p + new Vec3(0f, -6f, 0f)), "a long slope keeps its own deck at each cross-section");
            }

            var crest = new List<Vec3> { new Vec3(-4f, 0f, -20f), new Vec3(-4f, 6f, 0f), new Vec3(-4f, 0f, 20f), new Vec3(4f, 0f, 20f), new Vec3(4f, 6f, 0f), new Vec3(4f, 0f, -20f) };
            ClearRegion curved = Saved(crest, Vec3.Zero, 0f, true);
            Program.Check(!curved.SameAs(new ClearRegion(curved.Outline, curved.Scope, curved.KeepCrossings)), "a change of surface interpolation also invalidates the clearing cache");
            Program.Check(curved.Contains(new Vec3(0f, 6f, 0f)) && !curved.Contains(Vec3.Zero), "strip triangulation preserves a crest with collinear XZ boundary samples");
            Program.Check(curved.Contains(new Vec3(0f, 3f, 10f)) && !curved.Contains(new Vec3(0f, -3f, 10f)), "the crest's descending half is continuous");
            crest.Reverse();
            Program.Check(Saved(crest, Vec3.Zero, 0f, true).Contains(new Vec3(0f, 6f, 0f)), "opposite outline winding retains the road's cross-sections");
        }

        private static void CapacityAndDamage()
        {
            GuideTemplate template = SignCatalog.ById(VanillaClear.Id).Template;
            var circle = new List<Vec3>();
            for (int i = 0; i < 32; i++)
            {
                double angle = i * Math.PI * 2.0 / 32;
                circle.Add(new Vec3(233f * (float)Math.Cos(angle), 0f, 233f * (float)Math.Sin(angle)));
            }
            string written = ClearElevation.WithOutline(template.Default().With(VanillaClear.KeepKey, "all"), circle, Vec3.Zero, 0f);
            Program.Check(written != null && Encoding.UTF8.GetByteCount(written) <= GuideSpec.MaxBytes - 2, "reserve the maximum editable field growth in a dense saved polygon");
            GuideSpec before = template.Parse(written), after = before.With(VanillaClear.KeepKey, "lines");
            Program.Check(before.Get(MarkingPolygon.Key) == after.Get(MarkingPolygon.Key) && before.Get(ClearElevation.Key) == after.Get(ClearElevation.Key), "editing which lines are kept cannot truncate a hidden polygon or change a corner");
            Program.Check(ClearElevation.Decode(before).Count == MarkingPolygon.Decode(before.Get(MarkingPolygon.Key)).Count, "simplification keeps corner heights paired with their exact polygon");
            foreach (string bad in new[] { "h!bad", "hAAAA", "x123", "sAAAA" })
            {
                string damaged = before.With(ClearElevation.Key, bad).ToString();
                Program.Check(ClearRegion.Of(new ItemRecord(template.Hash, Vec3.Zero, 0f, 0u, damaged)) == null, "damaged height data cannot clear any deck: " + bad);
            }
            var high = new List<Vec3> { new Vec3(0f, 0f, 0f), new Vec3(10f, 400f, 0f), new Vec3(0f, 0f, 10f) };
            Program.Check(ClearElevation.WithOutline(template.Default(), high, Vec3.Zero, 0f) == null, "unrepresentable heights fail without wrapping to another deck");
        }

        private static void LegacyAndReplacement()
        {
            GuideTemplate template = SignCatalog.ById(VanillaClear.Id).Template;
            string old = template.Default().With(MarkingPolygon.Key, MarkingPolygon.Encode(Rect(0f))).With(ClearElevation.Key, "").ToString();
            ClearRegion legacy = ClearRegion.Of(new ItemRecord(template.Hash, new Vec3(0f, 12f, 0f), 0f, 0u, old));
            Program.Check(legacy.Contains(new Vec3(0f, 12f, 0f)) && !legacy.Contains(Vec3.Zero), "a legacy area without heights stays on its saved deck");
            Bezier3 road = Bezier3.Line(new Vec3(0f, 2f, -20f), new Vec3(0f, 22f, 20f));
            ClearRegion recovered = legacy.AlongLegacyRoad(road, new Vec3(0f, 12f, 0f));
            Program.Check(recovered.Contains(new Vec3(0f, 16f, 8f)) && !recovered.Contains(new Vec3(0f, 4f, 8f)), "a surviving legacy road hint restores its slope without selecting the lower road");
            Program.Check(VanillaClear.NearSameSurface(new Vec3(0.5f, 12.1f, 0f), new Vec3(0f, 12f, 0f), 1.5f), "replacing an automatic item accepts small paint and lane offsets");
            Program.Check(!VanillaClear.NearSameSurface(new Vec3(0.5f, 0f, 0f), new Vec3(0f, 12f, 0f), 1.5f), "nearby junction items on another deck are retained");
        }
    }
}
