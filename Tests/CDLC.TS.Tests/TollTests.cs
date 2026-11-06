using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The toll plaza (0.12.26; the owner: "加入高速收费站路标等等…还有常见的路面摆件"): the tab, the boards, the notices' presets, and the things on the road (gate, booth, island, hump).</summary>
    internal static class TollTests
    {
        public static void RunAll()
        {
            Program.Run("toll: the tab lists the standard's entries, the barriers and its own boards", Series);
            Program.Run("toll: the boards are laid out with what they are set to", Boards);
            Program.Run("toll: the notices' presets", Presets);
            Program.Run("toll: the station's name hangs on a gantry", Support);
            Program.Run("toll: the lifting-arm gate", Gate);
            Program.Run("toll: the booth", Booth);
            Program.Run("toll: the island and the hump", IslandAndHump);
            Program.Run("toll: the things are stamps in the barriers' group", Stamps);
            Program.Run("series only: what belongs to a toll plaza or to the greening alone is not in the barrier tab (0.12.107)", SeriesOnly);
        }

        /// <summary>
        /// The owner, 7 Nov, with a picture of the barrier tab: "比如你完全属于收费站的东西就别进到栏杆分组里了". The gate, the booth, the island and the hump are the toll tab's alone (their kind stays the barriers',
        /// which the placing reads); the tree pit, the green belt, the viaduct planter and the flower bowl are the greening tab's alone. Everything else that is in a series stays in its kind's tab too.
        /// </summary>
        private static void SeriesOnly()
        {
            string[] toll = { MarkingTemplates.GateId, MarkingTemplates.BoothId, MarkingTemplates.TollIslandId, MarkingTemplates.HumpId };
            string[] green = { MarkingTemplates.TreePitId, MarkingTemplates.GreenBeltId, MarkingTemplates.ViaductPlanterId, MarkingTemplates.FlowerBowlId };
            foreach (string id in toll)
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && c.SeriesOnly && c.Series == TollSigns.Group && c.Group == MarkingTemplates.BarrierGroup, id + ": the toll tab's alone, still of the barriers' kind");
                Program.Check(TollSigns.Only.Contains(id) && TollSigns.Series.Contains(id) && SignSeries.Exclusive(id), id + ": in the set of what is the toll plaza's alone, and in its series");
            }

            foreach (string id in green)
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && c.SeriesOnly && c.Series == MarkingTemplates.GreenSeries && c.Group == MarkingTemplates.BarrierGroup, id + ": the greening tab's alone, still of the barriers' kind");
            }

            // What stays in both: the barriers a toll plaza uses and the greening with a rail, a box, a fence or a wall; the boards of the toll kind itself; the standard's toll entries.
            foreach (string id in new[] { MarkingTemplates.ConeId, MarkingTemplates.CrashDrumId, MarkingTemplates.WaterBarrierId, MarkingTemplates.BollardId, MarkingTemplates.PlanterRailingId, MarkingTemplates.PlanterBoxId,
                MarkingTemplates.LawnFenceId, MarkingTemplates.LivingWallId, TollSigns.StationId, TollSigns.LaneId, "mk.toll.plaza", "mk.toll.island", "gb.guide8.70", "bar.fence", "bar.wbeam" })
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && !c.SeriesOnly, id + ": listed in its kind's tab too");
            }

            // No entry but these eight: not one of the others went with them.
            var only = new List<string>();
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.SeriesOnly)
                    only.Add(c.Id);
            }

            Program.Check(only.Count == toll.Length + green.Length, "exactly " + (toll.Length + green.Length) + " entries are of a series alone: " + string.Join(", ", only));
            Program.Check(!SignSeries.Exclusive(null) && !SignSeries.Exclusive("") && !SignSeries.Exclusive("gb.prohib.01"), "nothing, nothing named and a plain sign: not of a series alone");
            // An entry of a series alone has the series (a tab to be in), and a series that is nothing else's has no such entry.
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.SeriesOnly)
                    Program.Check(c.Series.Length > 0, c.Id + ": of a series alone must have a series");
            }
        }

        private static void Series()
        {
            var listed = new HashSet<string>();
            foreach (SignChoice c in SignCatalog.Choices)
            {
                if (c.Series == TollSigns.Group)
                    listed.Add(c.Id);
            }

            foreach (string id in TollSigns.Series)
                Program.Check(listed.Contains(id), "the toll tab lists " + id);
            foreach (string id in new[] { TollSigns.StationId, TollSigns.LaneId, TollSigns.SignalId, TollSigns.NoticeId })
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && c.Group == TollSigns.Group && c.Series == TollSigns.Group && !c.Def.IsMarking, id + " is a board of the toll kind");
                Program.Check(c != null && c.Code == "非国标", id + " says it is not the standard's");
            }

            // An entry is in one series: the BRT tab keeps its own, and the standard's toll signs stay in their kinds.
            Program.Check(SignCatalog.ChoiceById("gb.guide8.70").Group == "guide" && SignCatalog.ChoiceById("gb.guide8.70").Series == TollSigns.Group, "路70 is a guide sign, listed in the toll tab too");
            Program.Check(SignCatalog.ChoiceById("mk.toll.plaza").Group == "marking" && SignCatalog.ChoiceById("mk.toll.plaza").Series == TollSigns.Group, "线69 stays a marking");
            Program.Check(SignCatalog.ChoiceById("bar.cone").Group == MarkingTemplates.BarrierGroup && SignCatalog.ChoiceById("bar.cone").Series == TollSigns.Group, "the cones are barriers, listed in the toll tab too");
            Program.Check(SignCatalog.ChoiceById("gb.mand.28").Series == BrtSigns.Group && SignCatalog.ChoiceById("brt.station").Series == BrtSigns.Group, "the BRT series is as it was");
            Program.Check(SignSeries.Of("gb.mand.28", "mandatory") == "brt" && SignSeries.Of("bar.gate", "barrier") == "toll" && SignSeries.Of("gb.prohib.01", "prohibition") == "", "the series of an entry");
            Program.Check(!BrtSigns.Series.Overlaps(TollSigns.Series), "no entry is in both series");
        }

        private static void Boards()
        {
            GuideTemplate station = SignCatalog.ById(TollSigns.StationId).Template;
            GuideBoard plain = station.Layout(station.Default()), blue = station.Layout(station.Default().With("ground", "blue")), english = station.Layout(station.Default().With("en", "EXAMPLE TOLL STATION"));
            Program.Check(plain.Main.Ground == GuideColour.Green && blue.Main.Ground == GuideColour.Blue && plain.Main.BorderColour == GuideColour.White, "green by default, blue when set, a white border");
            Program.Check(english.Main.H > plain.Main.H + 0.1f, "the English line makes the board higher");
            Program.Check(station.Layout(station.Default().With("name", "苏州工业园区收费站")).Main.W > plain.Main.W, "a longer name makes it wider");
            Program.Check(plain.Main.W > 3f * plain.Main.H, "a canopy board is long and low: " + plain.Main.W + " x " + plain.Main.H);
            Program.Check(station.Layout(station.Default().With("etc", "0")).Main.W < plain.Main.W, "without ETC it is narrower");

            GuideTemplate lane = SignCatalog.ById(TollSigns.LaneId).Template;
            var grounds = new Dictionary<string, GuideColour>();
            foreach (string kind in new[] { "etc", "mixed", "manual", "truck", "green" })
            {
                GuideBoard board = lane.Layout(lane.Default().With("kind", kind));
                grounds[kind] = board.Main.Ground;
                Program.Check(board.Main.W > 0f && board.Main.H > 0f && !float.IsNaN(board.Main.W), kind + ": a board");
            }

            Program.Check(grounds["etc"] == GuideColour.Blue && grounds["mixed"] == GuideColour.Blue && grounds["manual"] == GuideColour.Green && grounds["truck"] == GuideColour.Yellow && grounds["green"] == GuideColour.Green, "the ground of each kind");
            GuideBoard numbered = lane.Layout(lane.Default().With("no", "12")), bare = lane.Layout(lane.Default().With("no", ""));
            Program.Check(numbered.Main.W > bare.Main.W, "the lane number is on the board, and may be left out");
            Program.Check(lane.Layout(lane.Default().With("l1", "超限").With("l2", "检测")).Main.W > 0f && lane.Layout(lane.Default().With("l1", "").With("l2", "").With("no", "")).Main.W > 0f, "words of its own; nothing at all is still a board");
            Program.Check(lane.Layout(lane.Default().With("kind", "truck")).Main.BorderColour == GuideColour.Black && lane.Layout(lane.Default()).Main.BorderColour == GuideColour.White, "the yellow board has a black border");

            GuideTemplate signal = SignCatalog.ById(TollSigns.SignalId).Template;
            GuideBoard open = signal.Layout(signal.Default()), closed = signal.Layout(signal.Default().With("state", "closed"));
            Program.Check(open.Main.Ground == GuideColour.Black && open.Main.H > open.Main.W * 1.5f, "a tall black box with two lamps");
            Program.Check(Math.Abs(open.Main.H - closed.Main.H) < 1e-5f && !SameElements(open, closed), "the lit lamp changes, the box does not");

            GuideTemplate notice = SignCatalog.ById(TollSigns.NoticeId).Template;
            foreach ((string ground, GuideColour back) in new[] { ("yellow", GuideColour.Yellow), ("green", GuideColour.Green), ("blue", GuideColour.Blue), ("white", GuideColour.White) })
                Program.Check(notice.Layout(notice.Default().With("ground", ground)).Main.Ground == back, "the notice on " + ground);
            Program.Check(notice.Layout(notice.Default().With("l3", "请勿跟车")).Main.H > notice.Layout(notice.Default()).Main.H, "a third line makes it higher");
            foreach (string id in new[] { TollSigns.StationId, TollSigns.LaneId, TollSigns.SignalId, TollSigns.NoticeId })
            {
                GuideTemplate each = SignCatalog.ById(id).Template;
                float small = each.Layout(each.Default().With("speed", "30")).Main.H, large = each.Layout(each.Default().With("speed", "100")).Main.H;
                Program.Check(large > small, id + ": larger characters on a faster road");
            }
        }

        private static bool SameElements(GuideBoard a, GuideBoard b)
        {
            if (a.Main.Elements.Count != b.Main.Elements.Count)
                return false;
            for (int i = 0; i < a.Main.Elements.Count; i++)
            {
                if (a.Main.Elements[i].Colour != b.Main.Elements[i].Colour)
                    return false;
            }

            return true;
        }

        private static void Presets()
        {
            int found = 0;
            foreach (GuidePreset p in GuideTemplates.Presets)
            {
                if (p.Template != TollSigns.NoticeId)
                    continue;
                found++;
                SignChoice c = SignCatalog.ChoiceById(p.Id);
                Program.Check(c != null && c.Group == TollSigns.Group && c.Series == TollSigns.Group && c.Code == "非国标", p.Id + ": a toll notice");
                SignInstance instance = SignInstances.Of(c.Def, c.Spec);
                Program.Check(instance.Def.Width > 0.5f && instance.Def.Height > 0.3f, p.Id + ": a board of a size: " + instance.Def.Width + " x " + instance.Def.Height);
                Program.Check(c.Spec.Contains("l1=") && c.Keywords.Length >= 3, p.Id + ": its lines and its words for the search");
            }

            Program.Check(found == 6, "six presets: " + found);
            Program.Check(SignCatalog.ChoiceById("toll.p.weigh").Spec.Contains("称重") && SignCatalog.ChoiceById("toll.p.etc").Spec.Contains("ground=blue"), "the lines and the ground are the preset's");
        }

        private static void Support()
        {
            SignDef station = SignCatalog.ById(TollSigns.StationId);
            Program.Check(SupportPlanner.Auto(station).Kind == SupportKind.Gantry, "a station's name goes on a gantry by itself (its canopy)");
            Program.Check(SupportPlanner.Auto(SignCatalog.ById(TollSigns.NoticeId)).Kind != SupportKind.Gantry, "the other boards are as any board");
            SignParts parts = SignBuilder.Build(station, SupportCatalog.ById(SupportCatalog.Gantry));
            Program.Check(parts.Faces.Count == 1 && parts.Layout.ArmLength >= station.Width, "the gantry spans the board: " + parts.Layout.ArmLength + " for " + station.Width);
        }

        private static float Top(List<MarkingShape> shapes)
        {
            float top = float.MinValue;
            foreach (MarkingShape s in shapes)
            {
                if (s.Mesh.VertexCount == 0)
                    continue;
                s.Mesh.Bounds(out _, out Vec3 hi);
                top = Math.Max(top, hi.Y);
            }

            return top;
        }

        private static void Extents(List<MarkingShape> shapes, out Vec3 lo, out Vec3 hi)
        {
            lo = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            hi = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MarkingShape s in shapes)
            {
                if (s.Mesh.VertexCount == 0)
                    continue;
                s.Mesh.Bounds(out Vec3 a, out Vec3 b);
                lo = new Vec3(Math.Min(lo.X, a.X), Math.Min(lo.Y, a.Y), Math.Min(lo.Z, a.Z));
                hi = new Vec3(Math.Max(hi.X, b.X), Math.Max(hi.Y, b.Y), Math.Max(hi.Z, b.Z));
            }
        }

        private static bool Finite(List<MarkingShape> shapes)
        {
            foreach (MarkingShape s in shapes)
            {
                foreach (Vec3 p in s.Mesh.Positions)
                {
                    if (!p.IsFinite)
                        return false;
                }
            }

            return true;
        }

        private static void Gate()
        {
            List<MarkingShape> down = TollProps.Gate(4f, false, true), open = TollProps.Gate(4f, true, true), left = TollProps.Gate(4f, false, false);
            Extents(down, out Vec3 dLo, out Vec3 dHi);
            Extents(open, out Vec3 oLo, out Vec3 oHi);
            Extents(left, out Vec3 lLo, out Vec3 lHi);
            Program.Check(Finite(down) && Finite(open), "finite");
            Program.Check(dHi.X > 4f && dHi.X < 4.5f && dLo.X > -0.4f, "down: the arm reaches over the lane to the right, 4 m and the housing: " + dHi.X);
            Program.Check(dHi.Y < 1.3f && dLo.Y < 0f && dLo.Y >= -Barriers.FootSink - 1e-4f, "down: nothing higher than the housing, the foot just under the ground: " + dHi.Y);
            Program.Check(oHi.Y > 4f && oHi.X < 0.4f, "open: the arm stands up, 4 m over its hub: " + oHi.Y);
            Program.Near(lLo.X, -dHi.X, 1e-4f, "to the left it is the same the other way");
            Program.Near(lHi.X, -dLo.X, 1e-4f, "and the housing is in the middle");
            Program.Check(down.FindAll(s => s.Mesh.VertexCount > 0).Count >= 5, "in several materials: the housing, the cap, the label, the lamp, the arm in two colours");
            Program.Check(TollProps.Gate(3f, false, true) != null && Top(TollProps.Gate(99f, false, true)) > 0f && Extent(TollProps.Gate(99f, false, true)) < 6.5f && Extent(TollProps.Gate(0.1f, false, true)) > 3f, "the arm is kept to 3 to 6 m");
            foreach (MarkingShape s in down)
            {
                if (s.Solid == SolidColour.Red || s.Solid == SolidColour.White)
                    Program.Check(s.Mesh.VertexCount > 0, "the arm's red and white are both there: " + s.Solid);
            }
        }

        private static float Extent(List<MarkingShape> shapes)
        {
            Extents(shapes, out _, out Vec3 hi);
            return hi.X;
        }

        private static void Booth()
        {
            List<MarkingShape> booth = TollProps.Booth(3f, SolidColour.Blue);
            Extents(booth, out Vec3 lo, out Vec3 hi);
            Program.Check(Finite(booth), "finite");
            Program.Check(hi.Y > 2.3f && hi.Y < 2.6f && lo.Y < 0f && lo.Y >= -Barriers.FootSink - 1e-4f, "about 2.4 m high, on the ground: " + hi.Y);
            Program.Check(hi.X > 1.0f && hi.X < 1.2f && hi.Z > 1.6f && hi.Z < 1.8f && Math.Abs(lo.X + hi.X) < 0.05f && Math.Abs(lo.Z + hi.Z) < 1e-4f, "1.7 m across with the roof's overhang and 3 m along, round the item's origin");
            Extents(TollProps.Booth(4f, SolidColour.Green), out _, out Vec3 longer);
            Program.Check(longer.Z > hi.Z + 0.4f, "a longer one is longer along the road");
            Program.Check(TollProps.Booth(3f, SolidColour.Red).Find(s => s.Solid == SolidColour.Red).Mesh.VertexCount > 0 && TollProps.Booth(3f, SolidColour.Blue).Find(s => s.Solid == SolidColour.Blue).Mesh.VertexCount > 0, "the fascia is in the colour asked for");
            Program.Check(booth.Find(s => s.Solid == SolidColour.Blue).Mesh.VertexCount > 0 && booth.Find(s => s.Solid == SolidColour.White).Mesh.VertexCount > 0, "the glass and its posts");
        }

        private static void IslandAndHump()
        {
            List<MarkingShape> island = TollProps.Island(28f, 2.2f);
            Extents(island, out Vec3 lo, out Vec3 hi);
            Program.Check(Finite(island), "finite");
            Program.Check(Math.Abs(hi.Y - 0.20f) < 1e-3f && lo.Y < 0f && Math.Abs(hi.X - 1.1f) < 1e-3f, "200 mm high, 2.2 m wide: " + hi.Y + ", " + hi.X);
            Program.Check(hi.Z > 14f && hi.Z < 14.1f && lo.Z < -14f && lo.Z > -14.1f, "28 m long round the origin: " + lo.Z + " to " + hi.Z);
            Program.Check(island.Find(s => s.Solid == SolidColour.Concrete).Mesh.VertexCount > 0 && island.Find(s => s.Solid == SolidColour.Yellow).Mesh.VertexCount > 0 && island.Find(s => s.Solid == SolidColour.Black).Mesh.VertexCount > 0, "concrete, with yellow and black end plates");
            Extents(TollProps.Island(1f, 9f), out Vec3 sLo, out Vec3 sHi);
            Program.Check(sHi.Z < 2.1f && sHi.X < 1.8f, "kept to 4 m and 3.5 m at the least and the most: " + sHi.Z + ", " + sHi.X);

            List<MarkingShape> hump = TollProps.Hump(3.5f, 2);
            Extents(hump, out Vec3 hLo, out Vec3 hHi);
            Program.Check(Finite(hump), "the hump is finite");
            Program.Check(Math.Abs(hHi.X - 1.75f) < 1e-3f && Math.Abs(hLo.X + 1.75f) < 1e-3f, "3.5 m across, round the origin: " + hLo.X + " to " + hHi.X);
            Program.Check(Math.Abs(hHi.Y - 0.05f) < 1e-3f, "50 mm high: " + hHi.Y);
            Program.Check(hHi.Z > 0.35f && hHi.Z < 0.4f && hLo.Z < -0.35f, "two rows 400 mm apart: " + hLo.Z + " to " + hHi.Z);
            Extents(TollProps.Hump(3.5f, 1), out _, out Vec3 oneRow);
            Program.Check(oneRow.Z < 0.2f, "one row is 350 mm wide");
            Program.Check(hump.Find(s => s.Solid == SolidColour.Yellow).Mesh.VertexCount > 0 && hump.Find(s => s.Solid == SolidColour.Black).Mesh.VertexCount > 0, "yellow and black pieces");
        }

        private static float TopOf(MarkingPlan plan)
        {
            float top = float.MinValue;
            foreach (MarkingPart part in plan.Parts)
            {
                if (part.Mesh.VertexCount == 0)
                    continue;
                part.Mesh.Bounds(out _, out Vec3 hi);
                top = Math.Max(top, hi.Y);
            }

            return top;
        }

        private static void Stamps()
        {
            foreach (string id in new[] { MarkingTemplates.GateId, MarkingTemplates.BoothId, MarkingTemplates.TollIslandId, MarkingTemplates.HumpId })
            {
                SignDef def = SignCatalog.ById(id);
                Program.Check(def != null && def.IsMarking && def.Group == MarkingTemplates.BarrierGroup && !MarkingTemplates.IsRun(def.Template), id + ": a barrier's group, put by a click, not drawn along a road");
                Program.Check(def.Template.FieldOf(BarrierAuto.JunctionKey) == null, id + ": no setting for junctions (it is not along a road)");
                MarkingPlan plan = MarkingTemplates.PlanOf(def, "");
                Program.Check(plan != null && plan.HasSolids && plan.TriangleCount > 20, id + ": a body: " + plan?.TriangleCount);
                Program.Check(SignCatalog.ChoiceById(id).Code == "收费站", id + ": named for the toll plaza");
            }

            // A run barrier still has its junction setting, and the automatic brush too.
            Program.Check(MarkingTemplates.ById(MarkingTemplates.WBeamId).FieldOf(BarrierAuto.JunctionKey) != null && MarkingTemplates.ById(BarrierAuto.Id).FieldOf(BarrierAuto.JunctionKey) != null, "the runs and the brush keep the setting for junctions");

            // The settings change the body.
            GuideTemplate gate = MarkingTemplates.ById(MarkingTemplates.GateId);
            MarkingPlan down = MarkingTemplates.PlanOf(gate, gate.Default().With("state", "down").ToString()), up = MarkingTemplates.PlanOf(gate, gate.Default().With("state", "open").ToString());
            Program.Check(down.MaxX > up.MaxX + 2f && TopOf(up) > TopOf(down) + 2f, "the open gate stands up, the closed one lies across: " + down.MaxX + " / " + up.MaxX);
            MarkingPlan left = MarkingTemplates.PlanOf(gate, gate.Default().With("arm", "l").ToString());
            Program.Check(left.MinX < -down.MaxX + 0.1f && left.MaxX < 0.5f, "to the other side when asked");
            GuideTemplate island = MarkingTemplates.ById(MarkingTemplates.TollIslandId);
            Program.Check(MarkingTemplates.PlanOf(island, island.Default().With("length", "40").ToString()).MaxZ > MarkingTemplates.PlanOf(island, "").MaxZ + 5f, "the island's length is a setting");
            GuideTemplate hump = MarkingTemplates.ById(MarkingTemplates.HumpId);
            Program.Check(MarkingTemplates.PlanOf(hump, hump.Default().With("width", "7").ToString()).MaxX > MarkingTemplates.PlanOf(hump, "").MaxX + 1.5f, "and the hump's width");
        }
    }
}
