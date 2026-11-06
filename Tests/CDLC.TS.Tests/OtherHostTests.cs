using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.51 (the owner via the RIE session: "允许红绿灯竖杆和横杆上挂路牌"): boards on another mod's signal poles and arms (TsExtension.OtherHosts): a pole's radius, top and heads' heights
    /// (HostPick.FitHeight, Attach), an arm's choice and the place along it (HostPick.NearestArm, PlaceOnArm, WithoutTaken), the arm support's layout and parts, and the reach kept to the millimetre.
    /// </summary>
    internal static class OtherHostTests
    {
        public static void RunAll()
        {
            Program.Run("other hosts: a board's height on a pole with heads", Heights);
            Program.Run("other hosts: the arm near the cursor and the place along it", ArmPlace);
            Program.Run("other hosts: a board on a signal arm, its clamps round the arm", ArmBoard);
            Program.Run("other hosts: the pole's radius kept, to the millimetre", Radius);
        }

        private static void Heights()
        {
            var plain = new HostCandidate();
            Program.Near(HostPick.FitHeight(plain, 2.3f, 0.6f, 0.15f, 6f), 2.3f, 1e-5f, "a post of the game's: where asked");
            // A pedestrian pole of the extension: heads from 2.4 to 3.3 m and 3.5 to 4.1 m, its top 4.5 m.
            var pole = new HostCandidate { Radius = 0.065f, Highest = 4.5f, Busy = new[] { 2.4f, 3.3f, 3.5f, 4.1f } };
            Program.Near(HostPick.FitHeight(pole, 1.5f, 0.6f, 0.15f, 6f), 1.5f, 1e-5f, "below the heads: where asked");
            Program.Near(HostPick.FitHeight(pole, 2.3f, 0.6f, 0.15f, 6f), 1.8f, 1e-5f, "into the first head: down under it (its top at the head's foot)");
            Program.Check(float.IsNaN(HostPick.FitHeight(pole, 3.3f, 0.6f, 0.15f, 6f)) == false, "a board too tall for the gap between the heads goes elsewhere");
            Program.Near(HostPick.FitHeight(pole, 3.3f, 0.6f, 0.15f, 6f), 2.4f - 0.6f, 1e-5f, "the 0.2 m between the heads too small: the nearest free place, under the first head");
            Program.Near(HostPick.FitHeight(pole, 4.1f, 0.3f, 0.15f, 6f), 4.1f, 1e-5f, "a low board above the heads, under the top");
            Program.Near(HostPick.FitHeight(pole, 4.4f, 0.3f, 0.15f, 6f), 4.2f, 1e-5f, "its top no higher than the pole's highest");
            var full = new HostCandidate { Highest = 2f, Busy = new[] { 0.1f, 1.9f } };
            Program.Check(float.IsNaN(HostPick.FitHeight(full, 1f, 0.5f, 0.15f, 6f)), "no room: none");

            // Joining a stack on such a pole keeps out of the heads too (Attach.AtHeight).
            SignInstance small = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
            var request = new AttachRequest
            {
                Existing = new List<AssemblyBoard>(),
                Support = SupportCatalog.ById(SupportCatalog.AttachedPole),
                Board = small,
                Height = 2.3f,
                HostHighest = 4.5f,
                HostBusy = new[] { 2.4f, 3.3f }
            };
            AttachResult result = Attach.Evaluate(request);
            float h = small.Def.Height + small.Def.ExtraTop;
            Program.Check(result.Ok && result.Offset + h <= 2.4f + 1e-3f, $"a board asked at 2.3 m goes under the head: lower edge {result.Offset:0.00}, top {result.Offset + h:0.00}");
            request.Height = 3.6f;
            result = Attach.Evaluate(request);
            Program.Check(result.Ok && result.Offset >= 3.3f - 1e-3f && result.Offset + h <= 4.5f + 1e-3f, $"or over it, under the top: {result.Offset:0.00}");
        }

        /// <summary>An arm 8 m long from (0, 6.2, 0) toward +X, its heads facing -Z; free from 0.3 to 3.0 m and from 3.8 to 7.9 m.</summary>
        private static HostArm Arm() => new HostArm { X = 0f, Y = 6.2f, Z = 0f, DirX = 1f, DirZ = 0f, Length = 8f, Radius = 0.12f, TipRadius = 0.06f, FaceX = 0f, FaceZ = -1f, Free = new[] { 0.3f, 3.0f, 3.8f, 7.9f }, Id = 7 };

        private static void ArmPlace()
        {
            var arms = new List<HostArm> { Arm(), new HostArm { X = 0f, Y = 6.2f, Z = 10f, DirX = 0f, DirZ = 1f, Length = 6f, Radius = 0.1f, Id = 8 } };
            int pick = HostPick.NearestArm(arms, 5f, 1.2f, HostPick.ArmReach, out float along, out float distance);
            Program.Check(pick == 0 && Math.Abs(along - 5f) < 1e-4f && Math.Abs(distance - 1.2f) < 1e-4f, "the arm under the cursor, 5 m along it");
            Program.Check(HostPick.NearestArm(arms, 5f, 4f, HostPick.ArmReach, out _, out _) < 0, "4 m off any arm: none");
            Program.Check(HostPick.NearestArm(arms, -2f, 0.5f, HostPick.ArmReach, out along, out _) == 0 && along == 0f, "behind the root: the root");
            HostArm arm = Arm();
            Program.Near(HostPick.PlaceOnArm(arm, 5f, 2f), 5f, 1e-5f, "a 2 m board where the cursor is");
            Program.Near(HostPick.PlaceOnArm(arm, 7.5f, 2f), 6.9f, 1e-5f, "pushed in from the tip's end of its stretch");
            Program.Near(HostPick.PlaceOnArm(arm, 3.5f, 2f), 4.8f, 1e-5f, "at a head between the stretches: into the nearest that holds it (3.8 + 1, nearer than 3.0 - 1)");
            Program.Near(HostPick.PlaceOnArm(arm, 3.4f, 2f), 2f, 1e-5f, "as near both: the stretch toward the root");
            Program.Check(float.IsNaN(HostPick.PlaceOnArm(arm, 2f, 4.5f)), "a board wider than every stretch: none");
            Program.Near(HostPick.PlaceOnArm(new HostArm { Length = 5f }, 4.9f, 1f), 4.4f, 1e-5f, "an arm that lists no stretches: 0.1 m short of its tip");
            float[] left = HostPick.WithoutTaken(arm.Free, new List<(float, float)> { (4.4f, 6.6f) });
            Program.Check(left.Length == 6 && Math.Abs(left[2] - 3.8f) < 1e-5f && Math.Abs(left[3] - 4.4f) < 1e-5f && Math.Abs(left[4] - 6.6f) < 1e-5f, "a board already there takes its stretch: " + string.Join(",", left));
            arm.Free = left;
            Program.Near(HostPick.PlaceOnArm(arm, 5.5f, 1.2f), 7.2f, 1e-5f, "the next board beside it, toward the tip (6.6 + 0.6)");
            Program.Near(arm.RadiusAt(4f), 0.09f, 1e-5f, "the arm tapers from 0.12 to 0.06 m");
            Program.Near(new HostArm { Radius = 0.1f, Length = 4f }.RadiusAt(3f), 0.1f, 1e-6f, "no tip radius: as at the root");
        }

        private static void ArmBoard()
        {
            SupportDef arm = SupportCatalog.ById(SupportCatalog.AttachedArm);
            SignDef name = SignCatalog.ById("gb17733.street");
            SignInstance plate = name != null ? SignInstances.Of(name, null) : SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
            Program.Check(SupportPlanner.FitsArm(plate.Def), $"a road name plate goes on an arm ({plate.Def.Width:0.0} x {plate.Def.Height:0.0} m)");
            Program.Check(!SupportPlanner.FitsArm(SignCatalog.ById("lamp.banner.civic")) && !SupportPlanner.FitsArm(new SignDef { Width = 5f, Height = 1f }), "not a lamp-post ad, nor a board 5 m wide");
            Program.Check(Assembly.MaxFor(SupportKind.AttachedArm) == 1, "one board to an item on an arm");
            foreach (float radius in new[] { 0.06f, 0.12f })
            {
                SignParts parts = ChunkBuilder.PartsOf(plate, arm.Hash, radius);
                SupportLayout l = parts.Layout;
                string what = $"a road name plate on an arm of {radius} m";
                Program.Near(l.HangRadius, radius, 1e-6f, what + ": the clamps fit the arm's radius");
                Program.Near(l.HangY, 0f, 1e-6f, what + ": the origin on the arm's axis");
                Program.Near(l.BoardTop, SupportLayouts.ArmInset(plate.Def.Height), 1e-5f, what + ": the arm passes behind the board's upper part, as a cantilever's");
                parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                Program.Check(fMin.Z > radius, what + ": the face stands in front of the arm (+Z, the way the heads face): " + fMin.Z);
                Program.Check(!parts.Picks.Exists(p => p.Name == "post" || p.Name == "foot"), what + ": no post and no foot of ours");
                // The clamps: bands 5 mm thick round the arm, behind it and in front of it (their backings reach into the arm, which hides them, as on a cantilever).
                int behind = 0, before = 0;
                foreach (Vec3 p in parts.Metal.Positions)
                {
                    float r = (float)Math.Sqrt(p.Y * p.Y + p.Z * p.Z);
                    if (Math.Abs(r - (radius + 0.005f)) > 0.002f)
                        continue;
                    behind += p.Z < -radius * 0.5f ? 1 : 0;
                    before += p.Z > radius * 0.5f ? 1 : 0;
                }

                Program.Check(behind > 0 && before > 0, what + $": collars round the arm ({behind} vertices behind it, {before} in front)");
            }

            // The panel's picture shows the arm it would be on.
            SignParts shown = SignBuilder.Build(plate, arm);
            int from = shown.Metal.VertexCount;
            SignBuilder.AddHostStandIn(shown);
            float lo = float.MaxValue, hi = float.MinValue, off = 0f;
            for (int i = from; i < shown.Metal.VertexCount; i++)
            {
                Vec3 p = shown.Metal.Positions[i];
                lo = Math.Min(lo, p.X);
                hi = Math.Max(hi, p.X);
                off = Math.Max(off, (float)Math.Sqrt(p.Y * p.Y + p.Z * p.Z));
            }

            Program.Check(shown.Metal.VertexCount > from && hi - lo > plate.Def.Width && off <= shown.Layout.HangRadius + 1e-4f, "the picture's stand-in: a level arm through the clamps, past the board");
        }

        private static void Radius()
        {
            SupportDef attached = SupportCatalog.ById(SupportCatalog.AttachedPole), arm = SupportCatalog.ById(SupportCatalog.AttachedArm), gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            Program.Check(ChunkBuilder.ReachKey(attached, 0.123f, out float built) == 123 && Math.Abs(built - 0.123f) < 1e-6f, "a host's radius to the millimetre");
            Program.Check(ChunkBuilder.ReachKey(arm, 0.0864f, out built) == 86 && Math.Abs(built - 0.086f) < 1e-6f, "an arm's too");
            Program.Check(ChunkBuilder.ReachKey(gantry, 14.34f, out built) == 143 && Math.Abs(built - 14.3f) < 1e-4f, "a span to the tenth of a metre, as before");
            SignInstance small = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
            Program.Near(SupportLayouts.Of(small.Def, attached, 0.13f).PoleRadius, 0.13f, 1e-6f, "an attached board on a pole of 0.13 m");
            Program.Near(SupportLayouts.Of(small.Def, attached, 0f).PoleRadius, SupportLayouts.HostRadius, 1e-6f, "a post of the game's: the usual radius");
            Program.Near(SupportLayouts.Of(small.Def, attached, 5f).PoleRadius, SupportLayouts.HostRadius, 1e-6f, "a reach left from a cantilever is not a radius");
            SupportLayout l = SupportLayouts.Of(small.Def, attached, 0.13f);
            Program.Check(l.BackZ >= 0.13f + 0.02f, "the board stands clear in front of the wider pole: back at " + l.BackZ);
        }
    }
}
