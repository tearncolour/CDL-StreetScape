using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>How an item's place is found again on a road that was changed.</summary>
    public enum FollowMode
    {
        /// <summary>The same share of the road's length from its start: a road that is made longer or shorter takes its items with it in proportion, and none jumps.</summary>
        Fraction,

        /// <summary>The same distance from the nearer end of the road: an item near a junction stays as far from it. An item at the middle of a road that changes length jumps by the change.</summary>
        NearestEnd
    }

    /// <summary>What a follow found to do with an item (RoadRelation.Follow).</summary>
    public enum FollowOutcome
    {
        /// <summary>The road has the shape it was measured on, as far as the item can tell: nothing to write.</summary>
        Unchanged,

        /// <summary>The item is to be written at the place and yaw given.</summary>
        Moved,

        /// <summary>The item is not where the relation put it: somebody moved it (the player, an undo). The relation is of no use; it is measured again where the item is.</summary>
        Detached,

        /// <summary>The road has no usable shape now (shorter than RoadRelation.MinLength, not finite): the item stays where it is.</summary>
        Unmeasurable
    }

    /// <summary>
    /// Where an item stands on a road (0.12.74; the owner: "用MoveIt移动道路之后应该能够自动跟随道路变化"): measured once against the curve of its edge when it is linked, and kept as it is, so that when the
    /// road's curve changes (Move It and the game's own modify write the new Curve in place) the item's place and yaw are made again from the new curve with the same relation. The relation is not
    /// refreshed from each new curve: a road dragged through a short length and back, or undone, takes its items back exactly, which a step by step refresh would not (an item clamped at an end
    /// stays there). Pure: no Unity and no game types; the curve is the game's Curve.m_Bezier, y up.
    /// </summary>
    public struct RoadRelation
    {
        /// <summary>The shortest road (m, in the ground plane) that has a relation: below it the place along it is not told from the end.</summary>
        public const float MinLength = 0.5f;

        /// <summary>How far (m) an item may be from where the relation put it for the relation to hold: Move It's own writes are exact, and the player's smallest move is more than this.</summary>
        public const float PlacedTolerance = 0.05f;

        /// <summary>A change smaller than this (m) is not written: the road's curve was updated for another reason (a lane, a signal) and has its shape.</summary>
        public const float WriteTolerance = 0.001f;

        /// <summary>The share of the road's length from its start to the nearest point of the curve.</summary>
        public float Fraction;

        /// <summary>The road's length (m, ground plane) when it was measured.</summary>
        public float Length;

        /// <summary>Metres along the road's direction from that point: about 0 inside the road, the distance past an end for an item in a junction's mouth.</summary>
        public float Along;

        /// <summary>Metres to the right of the road's direction (the driver's right) from that point.</summary>
        public float Lateral;

        /// <summary>Metres above the centre line at that point.</summary>
        public float Height;

        /// <summary>The item's yaw less the road's heading there (radians, wrapped).</summary>
        public float Yaw;

        /// <summary>Where the relation put the item last (or where it was when measured) and its yaw: an item that is elsewhere has been moved by someone else.</summary>
        public Vec3 PlacedAt;

        public float PlacedYaw;

        /// <summary>Measured on a road: false for the default value.</summary>
        public bool IsValid => Length >= MinLength;

        /// <summary>The relation of an item at a place with a yaw to the road whose curve (and arc table) is given; false when the road has no usable shape or the item's place is not finite.</summary>
        public static bool TryMeasure(ArcTable arc, Vec3 position, float yaw, out RoadRelation relation)
        {
            relation = default;
            if (arc == null || !position.IsFinite || float.IsNaN(yaw) || float.IsInfinity(yaw) || !Usable(arc))
                return false;
            Bezier3 curve = arc.Curve;
            float t = curve.ClosestT(position);
            Vec3 on = curve.Point(t), heading = curve.Tangent(t).HeadingXZ(), right = heading.RightXZ();
            Vec3 d = position - on;
            relation = new RoadRelation
            {
                Fraction = arc.SAt(t) / arc.Length,
                Length = arc.Length,
                Along = Vec3.Dot(new Vec3(d.X, 0f, d.Z), heading),
                Lateral = Vec3.Dot(new Vec3(d.X, 0f, d.Z), right),
                Height = d.Y,
                Yaw = Wrap(yaw - HeadingYaw(heading)),
                PlacedAt = position,
                PlacedYaw = yaw
            };
            return true;
        }

        /// <summary>Where the relation puts the item on the road of this curve; false when the road has no usable shape or the relation is not one.</summary>
        public bool TryApply(ArcTable arc, FollowMode mode, out Vec3 position, out float yaw)
        {
            position = default;
            yaw = 0f;
            if (!IsValid || arc == null || !Usable(arc))
                return false;
            float s = Fraction * Length;
            if (mode == FollowMode.NearestEnd)
                s = Fraction < 0.5f ? s : arc.Length - (Length - s);
            else
                s = Fraction * arc.Length;
            float t = arc.TAt(s);
            Bezier3 curve = arc.Curve;
            Vec3 on = curve.Point(t), heading = curve.Tangent(t).HeadingXZ(), right = heading.RightXZ();
            position = on + heading * Along + right * Lateral + new Vec3(0f, Height, 0f);
            yaw = Wrap(Yaw + HeadingYaw(heading));
            return position.IsFinite;
        }

        /// <summary>
        /// What to do with an item that has this relation when its road's curve is now arc's. The item is where it was put (position, yaw: the relation's PlacedAt and PlacedYaw) or it is not (Detached);
        /// the road has a shape (else Unmeasurable); the place the relation gives is the item's already (Unchanged) or not (Moved, with the place and the yaw).
        /// </summary>
        public FollowOutcome Follow(ArcTable arc, FollowMode mode, Vec3 position, float yaw, out Vec3 newPosition, out float newYaw)
        {
            newPosition = position;
            newYaw = yaw;
            if (!IsValid)
                return FollowOutcome.Detached;
            if ((position - PlacedAt).Length > PlacedTolerance || Math.Abs(Wrap(yaw - PlacedYaw)) > 1e-3f)
                return FollowOutcome.Detached;
            if (!TryApply(arc, mode, out Vec3 at, out float turned))
                return FollowOutcome.Unmeasurable;
            if ((at - position).Length <= WriteTolerance && Math.Abs(Wrap(turned - yaw)) <= 1e-4f)
                return FollowOutcome.Unchanged;
            newPosition = at;
            newYaw = turned;
            return FollowOutcome.Moved;
        }

        /// <summary>One road of a line's chain: its curve when the line's points were measured, and its curve now.</summary>
        public struct RoadPair
        {
            public ArcTable Old, Now;
        }

        /// <summary>
        /// The points of a line mapped from the old curves of the roads it runs along to their new ones: each point is measured against the road whose old curve is nearest to it, and that road's new curve is the one it goes to, whatever the others have become (its share of the
        /// length, its lateral, its height) and put at the same place on that road's new curve, all or nothing (false and mapped empty when a point has no road with a usable shape). For roads that were
        /// only moved or turned the map is the move or the turn; for a road that was bent it keeps each point's lateral and share of the length. A point past a road's end keeps its distance past it
        /// along the end's heading, so a line that goes on over a node is mapped well while the two roads of the node keep meeting (the game moves both when a node is moved).
        /// </summary>
        public static bool TryMapPoints(IList<RoadPair> roads, FollowMode mode, IList<Vec3> points, List<Vec3> mapped)
        {
            mapped.Clear();
            var distance = new float[roads.Count];
            var roadY = new float[roads.Count];
            var candidate = new int[roads.Count];
            foreach (Vec3 point in points)
            {
                int found = 0;
                for (int i = 0; i < roads.Count; i++)
                {
                    if (roads[i].Old == null || !Usable(roads[i].Old) || !point.IsFinite)
                        continue;
                    Bezier3 curve = roads[i].Old.Curve;
                    Vec3 on = curve.Point(curve.ClosestT(point, 8));
                    candidate[found] = i;
                    distance[found] = Vec3.DistanceXZ(on, point);
                    roadY[found] = on.Y;
                    found++;
                }

                int best = found == 0 ? -1 : candidate[RoadLevels.Pick(distance, roadY, found, point.Y)];

                if (best < 0 || !TryMeasure(roads[best].Old, point, 0f, out RoadRelation relation) || !relation.TryApply(roads[best].Now, mode, out Vec3 at, out _))
                {
                    mapped.Clear();
                    return false;
                }

                mapped.Add(at);
            }

            return true;
        }

        /// <summary>The points of a line on one road: as above, for the one road.</summary>
        public static bool TryMapPoints(ArcTable old, ArcTable now, FollowMode mode, IList<Vec3> points, List<Vec3> mapped) =>
            TryMapPoints(new[] { new RoadPair { Old = old, Now = now } }, mode, points, mapped);

        /// <summary>The relation as it is after the item was written at a place: the same road relation, the new place remembered.</summary>
        public RoadRelation Placed(Vec3 at, float yaw)
        {
            RoadRelation moved = this;
            moved.PlacedAt = at;
            moved.PlacedYaw = yaw;
            return moved;
        }

        private static bool Usable(ArcTable arc) => arc.Length >= MinLength && arc.Curve.A.IsFinite && arc.Curve.B.IsFinite && arc.Curve.C.IsFinite && arc.Curve.D.IsFinite;

        /// <summary>The yaw of a ground-plane heading (0 faces +Z, as everywhere in the mod).</summary>
        internal static float HeadingYaw(Vec3 heading) => (float)Math.Atan2(heading.X, heading.Z);

        internal static float Wrap(float angle)
        {
            const float twoPi = (float)(2.0 * Math.PI);
            angle %= twoPi;
            if (angle > (float)Math.PI)
                angle -= twoPi;
            else if (angle < -(float)Math.PI)
                angle += twoPi;
            return angle;
        }
    }
}
