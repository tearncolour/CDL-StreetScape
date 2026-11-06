using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The relation of an item to its road and the follow of a road that was moved, turned or bent (0.12.74).</summary>
    internal static class RoadRelationTests
    {
        public static void RunAll()
        {
            Program.Run("follow: an item measured on a road is put back where it was on the same road", RoundTrip);
            Program.Run("follow: a road that is moved takes its items by the move, and one that is turned turns them", MovedAndTurned);
            Program.Run("follow: a road made longer takes its items in proportion, or keeps them from the nearer end", Lengths);
            Program.Run("follow: the side, the distance from the road and what is past an end are kept on a bent road", Bent);
            Program.Run("follow: the outcomes (unchanged, moved, detached, unmeasurable) and the roads that have no shape", Outcomes);
            Program.Run("follow: a line's points are mapped as the road is; a path is written again within a centimetre", Paths);
        }

        private static ArcTable Arc(Bezier3 curve) => new ArcTable(curve);

        /// <summary>A bend: from the origin to 60 m along +Z with its middle pulled sideways by bow.</summary>
        private static Bezier3 Road(float bow, float length = 60f) => new Bezier3(new Vec3(0f, 0f, 0f), new Vec3(bow, 1f, length / 3f), new Vec3(bow, 2f, length * 2f / 3f), new Vec3(0f, 3f, length));

        private static Bezier3 Moved(Bezier3 c, float dx, float dy, float dz)
        {
            var d = new Vec3(dx, dy, dz);
            return new Bezier3(c.A + d, c.B + d, c.C + d, c.D + d);
        }

        private static Vec3 Turn(Vec3 p, float angle)
        {
            float s = (float)Math.Sin(angle), c = (float)Math.Cos(angle);
            return new Vec3(p.X * c + p.Z * s, p.Y, -p.X * s + p.Z * c);
        }

        private static Bezier3 Turned(Bezier3 c, float angle) => new Bezier3(Turn(c.A, angle), Turn(c.B, angle), Turn(c.C, angle), Turn(c.D, angle));

        private static bool Near(Vec3 a, Vec3 b, float tolerance) => (a - b).Length <= tolerance;

        private static void RoundTrip()
        {
            // At the left and the right of a bend, in front of its start, past its end, high and low, turned this way and that.
            Vec3[] places = { new Vec3(3f, 0.5f, 20f), new Vec3(-4f, 2f, 45f), new Vec3(0.5f, 1f, 5f), new Vec3(2f, -1f, -4f), new Vec3(-2f, 3f, 66f), new Vec3(5f, 3f, 59f) };
            float[] yaws = { 0f, 1.2f, -2.5f, 3.1f, -0.4f, 0.3f };
            bool all = true, yawAll = true, valid = true;
            foreach (float bow in new[] { 0f, 8f, -15f })
            {
                ArcTable arc = Arc(Road(bow));
                for (int i = 0; i < places.Length; i++)
                {
                    valid &= RoadRelation.TryMeasure(arc, places[i], yaws[i], out RoadRelation r);
                    bool applied = r.TryApply(arc, FollowMode.Fraction, out Vec3 at, out float yaw);
                    all &= applied && Near(at, places[i], 0.001f);
                    yawAll &= applied && Math.Abs(RoadRelation.Wrap(yaw - yaws[i])) < 1e-4f;
                }
            }

            Program.Check(valid, "every item can be measured");
            Program.Check(all, "the place comes back within a millimetre, inside the road and past its ends");
            Program.Check(yawAll, "and the yaw");
            RoadRelation.TryMeasure(Arc(Road(8f)), new Vec3(3f, 0.5f, 20f), 0.5f, out RoadRelation measured);
            Program.Check(measured.IsValid && measured.Fraction > 0.2f && measured.Fraction < 0.45f && measured.Length > 59f, $"the share of the length is about a third of it: {measured.Fraction:0.000} of {measured.Length:0.0} m");
            Program.Check(!default(RoadRelation).IsValid, "the default relation is none");
        }

        private static void MovedAndTurned()
        {
            Bezier3 road = Road(8f);
            Vec3[] places = { new Vec3(3f, 0.5f, 20f), new Vec3(-4f, 2f, 45f), new Vec3(2f, -1f, -4f), new Vec3(-2f, 3f, 66f) };
            bool moved = true, movedYaw = true, turnedOk = true, turnedYaw = true;
            foreach (Vec3 place in places)
            {
                RoadRelation.TryMeasure(Arc(road), place, 0.7f, out RoadRelation r);
                r.TryApply(Arc(Moved(road, 120f, 4f, -35f)), FollowMode.Fraction, out Vec3 at, out float yaw);
                moved &= Near(at, place + new Vec3(120f, 4f, -35f), 0.002f);
                movedYaw &= Math.Abs(RoadRelation.Wrap(yaw - 0.7f)) < 1e-4f;
                // Turned 40 degrees about the origin, then moved.
                float angle = 0.7f;
                r.TryApply(Arc(Moved(Turned(road, angle), 10f, 0f, 10f)), FollowMode.Fraction, out Vec3 at2, out float yaw2);
                turnedOk &= Near(at2, Turn(place, angle) + new Vec3(10f, 0f, 10f), 0.003f);
                turnedYaw &= Math.Abs(RoadRelation.Wrap(yaw2 - (0.7f + angle))) < 1e-3f;
            }

            Program.Check(moved && movedYaw, "a road moved by (120, 4, -35) takes the item by the same, with its yaw");
            Program.Check(turnedOk && turnedYaw, "a road turned about the origin and moved takes the item round, and its yaw by the turn");
        }

        private static void Lengths()
        {
            Bezier3 line = Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 100f));
            RoadRelation.TryMeasure(Arc(line), new Vec3(2f, 0f, 30f), 0f, out RoadRelation near);
            RoadRelation.TryMeasure(Arc(line), new Vec3(2f, 0f, 80f), 0f, out RoadRelation far);
            ArcTable longer = Arc(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 150f)));
            near.TryApply(longer, FollowMode.Fraction, out Vec3 a, out _);
            far.TryApply(longer, FollowMode.Fraction, out Vec3 b, out _);
            Program.Check(Near(a, new Vec3(2f, 0f, 45f), 0.01f) && Near(b, new Vec3(2f, 0f, 120f), 0.01f), $"made 1.5 times longer, the items are at 1.5 times the distance: {a} {b}");
            near.TryApply(longer, FollowMode.NearestEnd, out Vec3 c, out _);
            far.TryApply(longer, FollowMode.NearestEnd, out Vec3 d, out _);
            Program.Check(Near(c, new Vec3(2f, 0f, 30f), 0.01f) && Near(d, new Vec3(2f, 0f, 130f), 0.01f), $"or by the nearer end: 30 m from the start, 20 m from the end: {c} {d}");
            ArcTable shorter = Arc(Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 40f)));
            far.TryApply(shorter, FollowMode.NearestEnd, out Vec3 e, out _);
            near.TryApply(shorter, FollowMode.NearestEnd, out Vec3 f, out _);
            Program.Check(Near(e, new Vec3(2f, 0f, 20f), 0.01f) && Near(f, new Vec3(2f, 0f, 30f), 0.01f), $"made shorter, an item that would be before the start of what is left is held at its end: {e} {f}");
            // At exactly the middle the share 0.5 takes the end's rule.
            RoadRelation.TryMeasure(Arc(line), new Vec3(0f, 0f, 50f), 0f, out RoadRelation middle);
            middle.TryApply(longer, FollowMode.NearestEnd, out Vec3 g, out _);
            Program.Check(Near(g, new Vec3(0f, 0f, 100f), 0.01f), $"the middle follows the far end: {g}");
        }

        private static void Bent()
        {
            Bezier3 straight = Road(0f), bent = Road(14f);
            var place = new Vec3(3.5f, 1f, 25f);
            RoadRelation.TryMeasure(Arc(straight), place, 0f, out RoadRelation r);
            Program.Check(r.Lateral > 3.4f && r.Lateral < 3.6f, $"an item on the right of the road has a positive lateral: {r.Lateral:0.00}");
            r.TryApply(Arc(bent), FollowMode.Fraction, out Vec3 at, out _);
            ArcTable now = Arc(bent);
            float t = bent.ClosestT(at);
            Vec3 on = bent.Point(t), right = bent.Tangent(t).RightXZ();
            Vec3 d = at - on;
            float lateral = d.X * right.X + d.Z * right.Z;
            Program.Check(Math.Abs(lateral - r.Lateral) < 0.01f && Vec3.DistanceXZ(on, at) < r.Lateral + 0.01f, $"on the bent road it is {lateral:0.00} m to the right of it still");
            Program.Check(Math.Abs(now.SAt(t) / now.Length - r.Fraction) < 0.002f, "and at the same share of its length");
            // Turned about, the right is still the right of the road's direction.
            r.TryApply(Arc(Turned(straight, 3.14159f)), FollowMode.Fraction, out Vec3 behind, out _);
            Vec3 expected = Turn(place, 3.14159f);
            Program.Check(Near(behind, expected, 0.01f), $"a road turned half way round has the item on its right still: {behind} against {expected}");
            // Past the end: the distance past it along the end's heading.
            RoadRelation.TryMeasure(Arc(straight), new Vec3(1f, 3f, 64f), 0f, out RoadRelation past);
            Program.Check(past.Along > 3.9f && past.Along < 4.1f, $"4 m past the end: {past.Along:0.00}");
            past.TryApply(Arc(Turned(straight, 1f)), FollowMode.Fraction, out Vec3 p2, out _);
            Program.Check(Near(p2, Turn(new Vec3(1f, 3f, 64f), 1f), 0.01f), "past the end of a turned road it is past the end along its new heading");
        }

        private static void Outcomes()
        {
            Bezier3 road = Road(8f);
            ArcTable arc = Arc(road);
            var place = new Vec3(3f, 0.5f, 20f);
            RoadRelation.TryMeasure(arc, place, 0.5f, out RoadRelation r);
            Program.Check(r.Follow(arc, FollowMode.Fraction, place, 0.5f, out _, out _) == FollowOutcome.Unchanged, "the road as it was: nothing to write");
            ArcTable shifted = Arc(Moved(road, 10f, 0f, 0f));
            FollowOutcome moved = r.Follow(shifted, FollowMode.Fraction, place, 0.5f, out Vec3 at, out float yaw);
            Program.Check(moved == FollowOutcome.Moved && Near(at, place + new Vec3(10f, 0f, 0f), 0.002f) && Math.Abs(yaw - 0.5f) < 1e-4f, "a road moved: the item is to be written at the new place");
            Program.Check(r.Follow(shifted, FollowMode.Fraction, place + new Vec3(0f, 0f, 1f), 0.5f, out Vec3 kept, out _) == FollowOutcome.Detached && Near(kept, place + new Vec3(0f, 0f, 1f), 1e-6f),
                "an item the player moved by a metre is not the relation's any more, and is not written");
            Program.Check(r.Follow(shifted, FollowMode.Fraction, place, 1.5f, out _, out _) == FollowOutcome.Detached, "nor one turned by the player");
            // An item on the road's own line, the road turned about it: the place is the same and only the yaw changes, which is still to be written.
            Vec3 pivot = road.Point(0.4f);
            RoadRelation.TryMeasure(arc, pivot, 0.5f, out RoadRelation onLine);
            ArcTable about = Arc(new Bezier3(pivot + Turn(road.A - pivot, 0.4f), pivot + Turn(road.B - pivot, 0.4f), pivot + Turn(road.C - pivot, 0.4f), pivot + Turn(road.D - pivot, 0.4f)));
            FollowOutcome yawOnly = onLine.Follow(about, FollowMode.Fraction, pivot, 0.5f, out Vec3 samePlace, out float newYaw);
            Program.Check(yawOnly == FollowOutcome.Moved && Near(samePlace, pivot, 0.05f) && Math.Abs(RoadRelation.Wrap(newYaw - 0.9f)) < 0.03f, $"a road turned about an item on its line keeps the place and turns the yaw: {yawOnly}, {(samePlace - pivot).Length * 1000f:0.0} mm, {RoadRelation.Wrap(newYaw - 0.5f):0.000} rad");
            // After a write the new place is the one remembered: the next follow of the same road has nothing to do.
            RoadRelation after = r.Placed(at, yaw);
            Program.Check(after.Follow(shifted, FollowMode.Fraction, at, yaw, out _, out _) == FollowOutcome.Unchanged, "after the write, the same road is unchanged");
            Program.Check(after.Follow(arc, FollowMode.Fraction, at, yaw, out Vec3 back, out _) == FollowOutcome.Moved && Near(back, place, 0.002f), "and the road put back (an undo) takes the item back to where it was");
            // Roads with no shape.
            var dot = new Vec3(5f, 0f, 5f);
            ArcTable none = Arc(new Bezier3(dot, dot, dot, dot));
            Program.Check(!RoadRelation.TryMeasure(none, place, 0f, out _), "a road of no length has no relation");
            Program.Check(r.Follow(none, FollowMode.Fraction, place, 0.5f, out _, out _) == FollowOutcome.Unmeasurable && !r.TryApply(none, FollowMode.Fraction, out _, out _), "and one that is made so takes nothing");
            Program.Check(!RoadRelation.TryMeasure(arc, new Vec3(float.NaN, 0f, 0f), 0f, out _) && !RoadRelation.TryMeasure(arc, place, float.NaN, out _) && !RoadRelation.TryMeasure(null, place, 0f, out _), "no relation of what is not a place or a road");
            var bad = new Vec3(float.NaN, 0f, 0f);
            Program.Check(!RoadRelation.TryMeasure(Arc(new Bezier3(Vec3.Zero, new Vec3(0f, float.NaN, 20f), new Vec3(0f, 0f, 40f), new Vec3(0f, 0f, 60f))), place, 0f, out _), "nor of a road whose height is not a number");
            Program.Check(!RoadRelation.TryMeasure(Arc(new Bezier3(bad, bad, bad, bad)), place, 0f, out _), "nor of a road that is not a number");
        }

        /// <summary>A path of 24 chords along a bend at 3 m to its right, as a barrier is laid.</summary>
        private static List<Vec3> Line(Bezier3 road, float lateral)
        {
            ArcTable arc = Arc(road);
            var points = new List<Vec3>();
            for (int i = 0; i <= 24; i++)
            {
                float t = arc.TAt(arc.Length * (0.1f + 0.8f * i / 24f));
                Vec3 right = road.Tangent(t).RightXZ();
                points.Add(road.Point(t) + right * lateral);
            }

            return points;
        }

        private static void Paths()
        {
            Bezier3 road = Road(10f);
            List<Vec3> line = Line(road, 3f);
            string path = RunPath.Encode(line, out float yaw);
            Program.Check(path.Length > 0, "a line of 25 points is a path");
            // The text keeps each chord's length in whole centimetres, and the lengths are not corrected as the turns and the grades are: a line comes back from its text within half a centimetre a chord.
            List<Vec3> back = MarkingPolygon.ToWorld(RunPath.Decode(path), line[0], yaw);
            float worst = 0f;
            for (int i = 0; i < line.Count; i++)
                worst = Math.Max(worst, (back[i] - line[i]).Length);
            float bound = 0.005f * (line.Count - 1) + 0.01f;
            Program.Check(back.Count == line.Count && worst < bound, $"Encode then Decode puts the line back within half a centimetre a chord: {worst * 1000f:0.0} mm of {bound * 1000f:0} mm");
            Program.Check(RunPath.Encode(back, out _) == path, "and Encode of what Decode gave is the same text: the rounding does not go on");

            // A road moved and turned: the path is the path's line moved and turned, to a fraction of a millimetre (the lengths of a path are whole centimetres already).
            Bezier3 now = Moved(Turned(road, 0.6f), 80f, 2f, -30f);
            Program.Check(PathFollow.TryRemap(path, line[0], yaw, Arc(road), Arc(now), FollowMode.Fraction, out string moved, out Vec3 position, out float turned), "a line is written again on the moved road");
            List<Vec3> placed = MarkingPolygon.ToWorld(RunPath.Decode(moved), position, turned);
            float off = 0f;
            for (int i = 0; i < back.Count; i++)
                off = Math.Max(off, (placed[i] - (Turn(back[i], 0.6f) + new Vec3(80f, 2f, -30f))).Length);
            Program.Check(placed.Count == back.Count && off < 0.01f, $"it is the line moved and turned, within a centimetre: {off * 1000f:0.0} mm");
            Program.Check(Math.Abs(RoadRelation.Wrap(turned - (yaw + 0.6f))) < 0.01f && Near(position, Turn(back[0], 0.6f) + new Vec3(80f, 2f, -30f), 0.002f), "its position and its yaw are the first point's and the first chord's");

            // A road bent: each point keeps its lateral and its share of the length. Measured on the mapped points themselves, before they are written as a path.
            Bezier3 other = Road(-12f);
            ArcTable oldArc = Arc(road), newArc = Arc(other);
            var mapped = new List<Vec3>();
            Program.Check(RoadRelation.TryMapPoints(oldArc, newArc, FollowMode.Fraction, back, mapped) && mapped.Count == back.Count, "the points of a line are mapped onto a bent road");
            float lateralWorst = 0f, shareWorst = 0f;
            for (int i = 0; i < back.Count; i++)
            {
                RoadRelation.TryMeasure(oldArc, back[i], 0f, out RoadRelation before);
                RoadRelation.TryMeasure(newArc, mapped[i], 0f, out RoadRelation after);
                lateralWorst = Math.Max(lateralWorst, Math.Abs(after.Lateral - before.Lateral));
                shareWorst = Math.Max(shareWorst, Math.Abs(after.Fraction - before.Fraction));
            }

            Program.Check(lateralWorst < 0.002f && shareWorst < 0.0002f, $"each keeps its lateral within {lateralWorst * 1000f:0.0} mm and its share of the length within {shareWorst:0.00000}");
            Program.Check(PathFollow.TryRemap(path, line[0], yaw, oldArc, newArc, FollowMode.Fraction, out string bentPath, out Vec3 bentAt, out float bentYaw) && RunPath.Decode(bentPath).Count == back.Count && Near(bentAt, mapped[0], 0.001f), "and it is written as a path from the mapped points");

            // A line along two roads: each point goes by the road it is nearest to.
            Bezier3 first = Road(6f, 60f), second = new Bezier3(first.D, first.D + new Vec3(0f, 0f, 20f), first.D + new Vec3(4f, 0f, 40f), first.D + new Vec3(4f, 0f, 60f));
            var both = new List<Vec3>();
            both.AddRange(Line(first, 3f));
            both.AddRange(Line(second, 3f));
            int firstCount = Line(first, 3f).Count;
            var roads = new List<RoadRelation.RoadPair>
            {
                new RoadRelation.RoadPair { Old = Arc(first), Now = Arc(Moved(first, 30f, 0f, 5f)) },
                new RoadRelation.RoadPair { Old = Arc(second), Now = Arc(Moved(second, -12f, 0f, 3f)) }
            };
            var shifted = new List<Vec3>();
            bool pairsOk = RoadRelation.TryMapPoints(roads, FollowMode.Fraction, both, shifted);
            float chainOff = 0f;
            for (int i = 0; pairsOk && i < both.Count; i++)
                chainOff = Math.Max(chainOff, (shifted[i] - (both[i] + (i < firstCount ? new Vec3(30f, 0f, 5f) : new Vec3(-12f, 0f, 3f)))).Length);
            Program.Check(pairsOk && chainOff < 0.003f, $"a line along two roads moved by different amounts: each point goes by its own road: {chainOff * 1000f:0.0} mm");
            roads[1] = new RoadRelation.RoadPair { Old = roads[1].Old, Now = Arc(new Bezier3(Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero)) };
            Program.Check(!RoadRelation.TryMapPoints(roads, FollowMode.Fraction, both, shifted) && shifted.Count == 0, "when one of the roads has no shape the whole map fails and nothing of it is left");

            Program.Check(!PathFollow.TryRemap("", line[0], yaw, oldArc, newArc, FollowMode.Fraction, out _, out _, out _), "an item with no path is not a line");
            var dot = new Vec3(1f, 0f, 1f);
            Program.Check(!PathFollow.TryRemap(path, line[0], yaw, oldArc, Arc(new Bezier3(dot, dot, dot, dot)), FollowMode.Fraction, out _, out _, out _), "nor is it written on a road with no shape");
        }
    }
}
