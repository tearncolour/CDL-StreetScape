using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Preview
{
    /// <summary>
    /// Pictures of the automatic marking (AutoMarking) of made-up stretches, as the tool would place the items (no game): a road of two lanes each way in two segments bending at a node, ending at a
    /// signalled junction with a crosswalk, its approach's lanes going left-straight and straight-right; the same road starting at a stop sign; and a two-lane road with kerbs and parking. Each item's plan is
    /// put where the tool would put it and all are drawn from above (MarkingRaster). Usage: Preview.exe root out --auto-demos. Geometry only; not the game.
    /// </summary>
    internal static class AutoDemos
    {
        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);

        private static ChainPiece Piece(Bezier3 curve, float[] forward, float[] backward, float kmh, ChainJoint after, float laneFrom, float laneTo)
        {
            var piece = new ChainPiece { Curve = curve, Kmh = kmh, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo };
            piece.Forward.AddRange(forward);
            piece.Backward.AddRange(backward);
            return piece;
        }

        /// <summary>The items of a plan as the tool makes them, each with its plan, place and yaw.</summary>
        private static List<(MarkingPlan, Vec3, float)> Items(RoadChain chain, AutoPlan plan)
        {
            var items = new List<(MarkingPlan, Vec3, float)>();
            foreach (AutoRun run in plan.Runs)
            {
                GuideTemplate t = MarkingTemplates.ById(run.Id);
                GuideSpec spec = t.Parse(t.Default().ToString());
                foreach (KeyValuePair<string, string> kv in run.Settings)
                    spec = spec.With(kv.Key, kv.Value);
                List<Vec3> path = chain.Line(run.S0, run.S1, run.LateralAt, 0f);
                float total = 0f;
                for (int i = 1; i < path.Count; i++)
                    total += Vec3.DistanceXZ(path[i - 1], path[i]);
                foreach (RunItem item in MarkingRuns.Items(path))
                    items.Add((MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), item.Path, item.Start, total)), item.Position, item.Yaw));
            }

            foreach (AutoStamp stamp in plan.Stamps)
            {
                GuideTemplate t = MarkingTemplates.ById(stamp.Id);
                ChainPiece piece = chain.Pieces[chain.PieceAt(stamp.S)];
                GuideSpec given = t.Default();
                foreach (KeyValuePair<string, string> kv in stamp.Settings)
                    given = given.With(kv.Key, kv.Value);
                string text = MarkingTemplates.ResolveSize(t, given.ToString(), piece.Kmh, piece.HasSpeed);
                text = MarkingTemplates.WithFit(t, text, stamp.Span, stamp.Lanes);
                float yaw = EdgeSnap.YawOf(chain.HeadingAt(stamp.S)) + (stamp.WithChain ? 0f : (float)Math.PI);
                items.Add((MarkingTemplates.PlanOf(t, text), chain.PointAt(stamp.S, stamp.Lateral, 0f), yaw));
            }

            return items;
        }

        private static MarkingPlan Together(List<(MarkingPlan Plan, Vec3 At, float Yaw)> items, List<Vec3> kerbLeft, List<Vec3> kerbRight)
        {
            var plan = new MarkingPlan();
            foreach ((MarkingPlan p, Vec3 at, float yaw) in items)
            {
                if (p == null)
                    continue;
                foreach (MarkingPart part in p.Parts)
                {
                    var shape = new MarkingShape { Colour = part.Colour, Lift = part.Lift };
                    shape.Mesh.Append(part.Mesh, new Vec3(at.X, 0f, at.Z), yaw);
                    plan.Shapes.Add(shape);
                }
            }

            plan.Bake(0f);
            return plan;
        }

        private static List<Vec3> Kerb(RoadChain chain, float lateral)
        {
            var line = new List<Vec3>();
            for (float s = 0f; s <= chain.Length; s += 2f)
                line.Add(chain.PointAt(s, lateral, 0f));
            return line;
        }

        public static int Run(string output)
        {
            var tiles = new List<MarkingPlan>();
            float[] two = { 1.75f, 5.25f }, twoBack = { -1.75f, -5.25f }, one = { 1.75f }, oneBack = { -1.75f };

            // 1. Two lanes each way, 50 km/h: 70 m north, a node, then 60 m bending east; a signalled junction at the end, a crosswalk across its mouth.
            var a = Piece(Bezier3.Line(P(0f, 0f), P(0f, 70f)), two, twoBack, 50f, ChainJoint.Pass, 0f, 70f);
            var b = Piece(new Bezier3(P(0f, 70f), P(0f, 95f), P(8f, 120f), P(20f, 128f)), two, twoBack, 50f, ChainJoint.End, 0f, 0f);
            var probe = new RoadChain(new[] { Piece(b.Curve, two, twoBack, 50f, ChainJoint.End, float.NaN, float.NaN) });
            b.LaneTo = probe.Length - 3f;
            var chain = new RoadChain(new[] { a, b });
            var end = new ChainApproach { StopS = b.LanesS1, WithChain = true, Control = JunctionControl.Signals, CrosswalkAt = 0.3f };
            end.Lanes.Add(new ApproachLane { Lateral = 1.75f, Left = true, Straight = true });
            end.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            AutoOptions options = AutoOptions.Default;
            options.CrossLines = true;
            options.Preview = true;
            AutoPlan plan = AutoMarking.Plan(chain, new[] { end }, options);
            tiles.Add(Together(Items(chain, plan), Kerb(chain, -7.2f), Kerb(chain, 7.2f)));
            Console.WriteLine($"signalled: {plan.Runs.Count} line(s), {plan.Stamps.Count} mark(s); " + string.Join(" | ", plan.Runs) + " || " + string.Join(" | ", plan.Stamps) + (plan.Notes.Count > 0 ? " || notes: " + string.Join("; ", plan.Notes) : ""));

            // 2. The same road starting at a stop sign (its approach against the chain at the start), the game's crossing lines cleared.
            var c = Piece(Bezier3.Line(P(0f, 0f), P(0f, 110f)), two, twoBack, 50f, ChainJoint.End, 3f, 107f);
            var straight = new RoadChain(new[] { c });
            var start = new ChainApproach { StopS = c.LanesS0, WithChain = false, Control = JunctionControl.Stop };
            start.Lanes.Add(new ApproachLane { Lateral = -1.75f, Left = true, UTurn = true });
            start.Lanes.Add(new ApproachLane { Lateral = -5.25f, Straight = true, Right = true });
            var far = new ChainApproach { StopS = c.LanesS1, WithChain = true, Control = JunctionControl.Yield };
            far.Lanes.Add(new ApproachLane { Lateral = 1.75f, Straight = true });
            far.Lanes.Add(new ApproachLane { Lateral = 5.25f, Right = true });
            AutoPlan plan2 = AutoMarking.Plan(straight, new[] { start, far }, options);
            tiles.Add(Together(Items(straight, plan2), Kerb(straight, -7.2f), Kerb(straight, 7.2f)));
            Console.WriteLine($"stop and yield: {plan2.Runs.Count} line(s), {plan2.Stamps.Count} mark(s)" + (plan2.Notes.Count > 0 ? "; notes: " + string.Join("; ", plan2.Notes) : ""));

            // 3. One lane each way, 30 km/h, kerbs both sides, parking on the right; a signalled junction at the end.
            var d = Piece(Bezier3.Line(P(0f, 0f), P(0f, 80f)), one, oneBack, 30f, ChainJoint.End, 0f, 78f);
            d.FootwayLeft = d.FootwayRight = true;
            d.ParkingRight = true;
            var narrow = new RoadChain(new[] { d });
            var nEnd = new ChainApproach { StopS = 78f, WithChain = true, Control = JunctionControl.Signals };
            nEnd.Lanes.Add(new ApproachLane { Lateral = 1.75f, Left = true, Straight = true, Right = true });
            AutoPlan plan3 = AutoMarking.Plan(narrow, new[] { nEnd }, options);
            tiles.Add(Together(Items(narrow, plan3), Kerb(narrow, -3.6f), Kerb(narrow, 6f)));
            AutoOptions edges = options;
            edges.Edges = EdgeLines.All;
            AutoPlan plan4 = AutoMarking.Plan(narrow, new[] { nEnd }, edges);
            tiles.Add(Together(Items(narrow, plan4), Kerb(narrow, -3.6f), Kerb(narrow, 6f)));
            Console.WriteLine($"narrow: {plan3.Runs.Count} line(s), {plan3.Stamps.Count} mark(s); with all edge lines {plan4.Runs.Count}" + (plan3.Notes.Count > 0 ? "; notes: " + string.Join("; ", plan3.Notes) : ""));

            const int w = 420, h = 1000;
            var sheet = new byte[tiles.Count * w * h * 4];
            for (int t = 0; t < tiles.Count; t++)
            {
                byte[] raw = MarkingRaster.Render(tiles[t], w, h, out int rw, out int rh, 0.03f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet, (y * tiles.Count * w + t * w) * 4, rw * 4);
            }

            File.WriteAllBytes(Path.Combine(output, "auto-demos.png"), Png.Encode(sheet, tiles.Count * w, h, false));

            // A close view of a signalled approach: 45 m of road, two lanes each way, a crosswalk 0.3 m beyond the lanes' end.
            var e = Piece(Bezier3.Line(P(0f, 0f), P(0f, 45f)), two, twoBack, 50f, ChainJoint.End, 0f, 43f);
            var shortChain = new RoadChain(new[] { e });
            var shortEnd = new ChainApproach { StopS = 43f, WithChain = true, Control = JunctionControl.Signals, CrosswalkAt = 0.3f };
            shortEnd.Lanes.Add(new ApproachLane { Lateral = 1.75f, Left = true, UTurn = true });
            shortEnd.Lanes.Add(new ApproachLane { Lateral = 5.25f, Straight = true, Right = true });
            AutoOptions closeOptions = AutoOptions.Default;
            closeOptions.CrossLines = true;
            AutoPlan closePlan = AutoMarking.Plan(shortChain, new[] { shortEnd }, closeOptions);
            Console.WriteLine("close: " + string.Join(" | ", closePlan.Runs) + " || " + string.Join(" | ", closePlan.Stamps) + (closePlan.Notes.Count > 0 ? " || notes: " + string.Join("; ", closePlan.Notes) : ""));
            byte[] close = MarkingRaster.Render(Together(Items(shortChain, closePlan), null, null), 900, 900, out int cw, out int ch, 0.03f);
            File.WriteAllBytes(Path.Combine(output, "auto-demo-junction.png"), Png.Encode(close, cw, ch, false));

            // ---- the second sheet (0.12.2's second part)
            var more = new List<MarkingPlan>();
            float[] three = { 1.75f, 5.25f, 8.75f };
            // 5. A lane ends at a node at 140 m (three to two with the chain, 50 km/h): the edge line moves in over formula 1's length, the lane's line is the change line.
            var drop = new RoadChain(new[] { Piece(Bezier3.Line(P(0f, 0f), P(0f, 140f)), three, twoBack, 50f, ChainJoint.Pass, 0f, 140f), Piece(Bezier3.Line(P(0f, 140f), P(0f, 230f)), two, twoBack, 50f, ChainJoint.End, 0f, 90f) });
            AutoPlan dropPlan = AutoMarking.Plan(drop, null, options);
            more.Add(Together(Items(drop, dropPlan), null, null));
            Console.WriteLine("lane drop: " + string.Join(" | ", dropPlan.Runs) + (dropPlan.Notes.Count > 0 ? " || notes: " + string.Join("; ", dropPlan.Notes) : ""));

            // 6. A bus lane on the right with the chain, a cycle lane each side, kerbs; a crosswalk without signals at a node at 120 m.
            var busA = Piece(Bezier3.Line(P(0f, 0f), P(0f, 120f)), two, twoBack, 50f, ChainJoint.Pass, 0f, 120f);
            var busB = Piece(Bezier3.Line(P(0f, 120f), P(0f, 230f)), two, twoBack, 50f, ChainJoint.End, 0f, 110f);
            foreach (ChainPiece p in new[] { busA, busB })
            {
                p.BusForward.Add(5.25f);
                p.BikeForward.Add(8f);
                p.BikeBackward.Add(-8f);
                p.BikeWidth = 2f;
                p.FootwayLeft = p.FootwayRight = true;
            }

            var busChain = new RoadChain(new[] { busA, busB });
            busChain.Crossings.Add(new ChainCrossing(118f, 122f, false));
            AutoPlan busPlan = AutoMarking.Plan(busChain, null, options);
            more.Add(Together(Items(busChain, busPlan), null, null));
            Console.WriteLine($"bus and cycle lanes, a crosswalk: {busPlan.Runs.Count} line(s), {busPlan.Stamps.Count} mark(s); " + string.Join(" | ", busPlan.Stamps.FindAll(s => s.Id != "mk.diamond")));

            // 7. One lane each way over a crest (15 m high over 600 m) at 60 km/h: the centre line by the overtaking sight (6.1.3, 6.1.4); seen from above.
            var crest = new RoadChain(new[] { Piece(new Bezier3(P(0f, 0f), P(0f, 230f), P(0f, 370f), P(0f, 600f)), one, oneBack, 60f, ChainJoint.End, float.NaN, float.NaN) });
            crest.Pieces[0].Curve = new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(0f, 20f, 230f), new Vec3(0f, 20f, 370f), new Vec3(0f, 0f, 600f));
            crest = new RoadChain(crest.Pieces);
            AutoPlan crestPlan = AutoMarking.Plan(crest, null, options);
            // 600 m of road is under a pixel across at this size: the zones are printed, not drawn.
            Console.WriteLine("crest: " + string.Join(" | ", crestPlan.Runs.FindAll(r => r.Id.Contains("yellow") || r.Id == "mk.line.centre")));

            // 8. Two left-turn lanes through a junction: the guide lines (5.6), yellow on the left of the first, white between.
            var turns = new List<List<Vec3>>();
            foreach ((float r, float x) in new[] { (16f, 1.75f), (19.5f, 5.25f) })
            {
                var arc = new List<Vec3>();
                for (int i = 0; i <= 24; i++)
                {
                    double angle = Math.PI * 0.5 * i / 24;
                    arc.Add(P(x - r + r * (float)Math.Cos(angle), r * (float)Math.Sin(angle)));
                }

                turns.Add(arc);
            }

            var guideItems = new List<(MarkingPlan, Vec3, float)>();
            GuideTemplate jg = MarkingTemplates.ById("mk.line.junction");
            foreach (GuideLine line in JunctionGuide.Lines(turns, new List<float> { 3.5f, 3.5f }, true, 4f))
            {
                string spec = jg.Parse(jg.Default().ToString()).With("colour", line.Yellow ? "y" : "w").ToString();
                float total = WaitingArea.Length(line.Points);
                foreach (RunItem item in MarkingRuns.Items(line.Points))
                    guideItems.Add((MarkingTemplates.PlanOf(jg, MarkingTemplates.WithRun(jg, spec, item.Path, item.Start, total)), item.Position, item.Yaw));
            }

            // The approach's own lines below the junction, for scale.
            var approachChain = new RoadChain(new[] { Piece(Bezier3.Line(P(0f, -40f), P(0f, -2f)), two, twoBack, 50f, ChainJoint.End, 0f, 38f) });
            guideItems.AddRange(Items(approachChain, AutoMarking.Plan(approachChain, null, options)));
            more.Add(Together(guideItems, null, null));

            var sheet2 = new byte[more.Count * w * h * 4];
            for (int t = 0; t < more.Count; t++)
            {
                byte[] raw = MarkingRaster.Render(more[t], w, h, out int rw, out int rh, 0.03f);
                for (int y = 0; y < rh; y++)
                    Array.Copy(raw, y * rw * 4, sheet2, (y * more.Count * w + t * w) * 4, rw * 4);
            }

            File.WriteAllBytes(Path.Combine(output, "auto-demos-2.png"), Png.Encode(sheet2, more.Count * w, h, false));
            return 0;
        }
    }
}
