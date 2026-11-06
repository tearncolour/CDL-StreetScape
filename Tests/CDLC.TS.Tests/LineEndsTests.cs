using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.73 (the owner: "弯道墙停在路口前"): a wall drawn along a road may run on to the node at the two ends of the chain, as a choice (the setting "nd", default on), where it used to stop where the lanes
    /// end (10.4 m short of the node in the owner's log). The chain made up: pieces of 100 m along +Z with lanes from 15 m and to 85 m of their own curve. The reading of the roads (ECS) is not here.
    /// </summary>
    internal static class LineEndsTests
    {
        public static void RunAll()
        {
            Program.Run("line ends: a one-piece chain runs on to the piece's ends, or stops where the lanes end", OnePiece);
            Program.Run("line ends: only the chain's two ends move, not a plain node, a break or a junction in it", Inner);
            Program.Run("line ends: every mode of the spans, a reversed piece, a piece too short", Modes);
            Program.Run("line ends: the setting, its default, an old item's spec", Setting);
        }

        private static ChainPiece Piece(Vec3 from, Vec3 to, ChainJoint after, float laneFrom, float laneTo, bool reversed = false)
        {
            var piece = new ChainPiece { Curve = Bezier3.Line(from, to), Kmh = 80f, HasSpeed = true, After = after, LaneFrom = laneFrom, LaneTo = laneTo, HalfWidth = 7f, Reversed = reversed };
            piece.Forward.Add(1.75f);
            piece.Backward.Add(-1.75f);
            return piece;
        }

        private static RoadChain Two(ChainJoint between)
        {
            ChainPiece a = Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), between, 15f, 85f);
            a.JoinRight = true;
            a.JoinLeft = true;
            return new RoadChain(new[] { a, Piece(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 200f), ChainJoint.End, 15f, 85f) });
        }

        private static string Show(List<ChainSpan> spans) => string.Join(" ", spans.ConvertAll(s => $"{s.From:0.0}-{s.To:0.0}"));

        private static void OnePiece()
        {
            var chain = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.End, 15f, 85f) });
            Program.Check(Show(chain.Spans(6f, JunctionMode.Side)) == "15.0-85.0", "as it was: where the lanes end: " + Show(chain.Spans(6f, JunctionMode.Side)));
            Program.Check(Show(chain.Spans(6f, JunctionMode.Side, false, null, true)) == "0.0-100.0", "run on to the node: the whole piece: " + Show(chain.Spans(6f, JunctionMode.Side, false, null, true)));
            Program.Check(Show(chain.Spans(6f, JunctionMode.Side, false, null, false)) == "15.0-85.0", "toNodes off is the old call");
            ChainSpan on = chain.Spans(6f, JunctionMode.Side, false, null, true)[0];
            Program.Check(on.From == chain.Pieces[0].S0 && on.To == chain.Pieces[0].S1, "and no further than the piece's own S0 and S1");
        }

        private static void Inner()
        {
            // A plain node between two pieces: the line goes on through it as before, the ends move.
            RoadChain pass = Two(ChainJoint.Pass);
            Program.Check(Show(pass.Spans(6f, JunctionMode.Side)) == "15.0-185.0", "a plain node: one span, ends at the lanes: " + Show(pass.Spans(6f, JunctionMode.Side)));
            Program.Check(Show(pass.Spans(6f, JunctionMode.Side, false, null, true)) == "0.0-200.0", "run on: one span from the first start to the last end: " + Show(pass.Spans(6f, JunctionMode.Side, false, null, true)));

            // A break of the chain between two pieces: the ends of the pieces at the break stay at their lanes.
            RoadChain broken = Two(ChainJoint.End);
            Program.Check(Show(broken.Spans(6f, JunctionMode.Side)) == "15.0-85.0 115.0-185.0", "a break: two spans: " + Show(broken.Spans(6f, JunctionMode.Side)));
            Program.Check(Show(broken.Spans(6f, JunctionMode.Side, false, null, true)) == "0.0-85.0 115.0-200.0", "run on: the outer ends move, the ends at the break do not: " + Show(broken.Spans(6f, JunctionMode.Side, false, null, true)));

            // A junction the chain crosses (a road joins on the wall's side): cut where the lanes end and start, as before; the ends move.
            RoadChain junction = Two(ChainJoint.Junction);
            Program.Check(Show(junction.Spans(6f, JunctionMode.Cut)) == "15.0-85.0 115.0-185.0", "a junction, cut: " + Show(junction.Spans(6f, JunctionMode.Cut)));
            Program.Check(Show(junction.Spans(6f, JunctionMode.Cut, false, null, true)) == "0.0-85.0 115.0-200.0", "a junction, cut, run on: " + Show(junction.Spans(6f, JunctionMode.Cut, false, null, true)));
            Program.Check(Show(junction.Spans(6f, JunctionMode.Side)) == "15.0-85.0 115.0-185.0" && Show(junction.Spans(6f, JunctionMode.Side, false, null, true)) == "0.0-85.0 115.0-200.0",
                "the side mode at a junction with no known mouth is cut the same way: only the ends differ: " + Show(junction.Spans(6f, JunctionMode.Side, false, null, true)));
        }

        private static void Modes()
        {
            RoadChain pass = Two(ChainJoint.Pass);
            Program.Check(Show(pass.Spans(6f, JunctionMode.Through, false, null, true)) == "0.0-200.0" && Show(pass.Spans(6f, JunctionMode.Through)) == "15.0-185.0", "through: " + Show(pass.Spans(6f, JunctionMode.Through, false, null, true)));
            Program.Check(Show(pass.Spans(0f, JunctionMode.Side, true, null, true)) == "0.0-200.0" && Show(pass.Spans(0f, JunctionMode.Side, true)) == "15.0-185.0", "a median wall: " + Show(pass.Spans(0f, JunctionMode.Side, true, null, true)));
            Program.Check(Show(pass.Spans(6f, true, true)) == "0.0-200.0" && Show(pass.Spans(6f, true)) == "15.0-185.0", "a line of the markings' overload: " + Show(pass.Spans(6f, true, true)));
            Program.Check(Show(pass.Spans(-6f, JunctionMode.Side, false, s => 6.65f, true)) == "0.0-200.0", "a wall that follows the edge");

            // A reversed piece: the chain runs against the segment's own direction, S0 and S1 are still the chain's distances.
            var rev = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 100f), new Vec3(0f, 0f, 0f), ChainJoint.End, 15f, 85f, true) });
            ChainSpan before = rev.Spans(6f, JunctionMode.Side)[0], after = rev.Spans(6f, JunctionMode.Side, false, null, true)[0];
            Program.Check(before.From > rev.Pieces[0].S0 && before.To < rev.Pieces[0].S1 && after.From == rev.Pieces[0].S0 && after.To == rev.Pieces[0].S1, $"a reversed piece: {before.From:0.0}-{before.To:0.0} then {after.From:0.0}-{after.To:0.0}");

            // A piece whose lanes fill it has nothing to run on to: the same either way. A piece that is too short for a span stays none.
            var full = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f), ChainJoint.End, float.NaN, float.NaN) });
            Program.Check(Show(full.Spans(6f, JunctionMode.Side)) == Show(full.Spans(6f, JunctionMode.Side, false, null, true)), "lanes from end to end: no change");
            var tiny = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 1f), ChainJoint.End, float.NaN, float.NaN) });
            Program.Check(tiny.Spans(6f, JunctionMode.Side, false, null, true).Count == 1 && tiny.Spans(6f, JunctionMode.Side).Count == 1, "a piece as long as the least span keeps its span");
            var stub = new RoadChain(new[] { Piece(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 1.5f), ChainJoint.End, 0.2f, 1.3f) });
            Program.Check(stub.Spans(6f, JunctionMode.Side).Count == 1 && stub.Spans(6f, JunctionMode.Side, false, null, true).Count == 1, "lanes of 1.1 m in a piece of 1.5 m: a span either way");
            Program.Check(new RoadChain(new ChainPiece[0]).Spans(6f, JunctionMode.Side, false, null, true).Count == 0, "no pieces, no spans");
        }

        private static void Setting()
        {
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                if (t.Group == MarkingTemplates.BarrierGroup && MarkingTemplates.IsRun(t))
                {
                    GuideField field = t.FieldOf(BarrierAuto.EndsKey);
                    Program.Check(field != null && field.Default == "j" && field.DraftOnly, t.Id + ": has the ends setting (nd), run on by default, for the placing only");
                    Program.Check(BarrierAuto.EndsAtNodes(t.Default()) && BarrierAuto.EndsAtNodes(t.Parse("")) && !BarrierAuto.EndsAtNodes(t.Parse("nd=l")) && BarrierAuto.EndsAtNodes(t.Parse("nd=j")), t.Id + ": the setting says it");
                }
            }

            GuideTemplate wbeam = MarkingTemplates.ById(MarkingTemplates.WBeamId);
            // An item placed before the setting existed has a spec without the key: it parses to the default and its other settings are unchanged (the path is baked in it, so what it draws is the same).
            GuideSpec old = wbeam.Parse("jn=side;g=0;path=200:0:0|200:100:0;ph=0;tl=400");
            Program.Check(BarrierAuto.EndsAtNodes(old) && old.Get("path") == "200:0:0|200:100:0" && old.Get("ph") == "0" && old.Get("tl") == "400" && BarrierAuto.JunctionOf(old) == JunctionMode.Side, "an old item's spec keeps its path, phase and length");
            Program.Check(MarkingTemplates.ById(BarrierAuto.Id).FieldOf(BarrierAuto.EndsKey) == null, "the automatic brush has none: its stretches end as they did");
            Program.Check(MarkingTemplates.ById(MarkingTemplates.GateId).FieldOf(BarrierAuto.EndsKey) == null, "nor has what is put by one click");
        }
    }
}
