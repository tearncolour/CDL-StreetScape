using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One piece of structure over a road, as the reader of the game's nets gives it: the centre line of a segment that crosses the road above it, and the height range of its cross-section.</summary>
    public struct OverStructure
    {
        public Bezier3 Curve;

        /// <summary>The cross-section's width (m) and the height of its lowest and highest part above the centre line (m, from the composition's HeightRange).</summary>
        public float Width, Lowest, Highest;

        /// <summary>The id the caller keeps for it (an entity's index): only compared.</summary>
        public int Id;
    }

    /// <summary>Where a board hangs under a structure over the road: the point on the structure's side, the way the structure runs there, and the road's heading beneath it.</summary>
    public struct OverSpot
    {
        public int Structure;

        /// <summary>The point on the structure's underside edge that is nearest to the cursor's road, at its lowest height.</summary>
        public Vec3 Point;

        /// <summary>The unit heading (ground plane) of the structure there, and the unit normal of its face that looks at the approaching traffic: the board's back is on this face.</summary>
        public Vec3 Run, Face;

        /// <summary>The height of the structure's underside above the road's surface where the board hangs (m), and the height of the face above it: the lowest part to the highest of its cross-section.</summary>
        public float Underside, FaceHeight;

        /// <summary>How far the structure's underside reaches back from the face along the face's normal (m): the structure's width. A hung board's rods and braces need it under them (OverRoad.HungDepth).</summary>
        public float Depth;
    }

    /// <summary>Why a board is not put on a structure (OverRoad.Place): the face or the soffit is too low for it, or a hung board has too little structure behind its face.</summary>
    public enum OverRefusal
    {
        None,
        TooLow,
        TooThin
    }

    /// <summary>
    /// What putting a board on a structure comes to (OverRoad.Place): a refusal and the two numbers its line says (what there is, what is needed), or the item's reach, the height of the board's lower edge
    /// over the road and where its origin stands.
    /// </summary>
    public struct OverPlacement
    {
        public OverRefusal Refusal;
        public float Left, Need;
        public float Reach, Lower;
        public Vec3 Origin;
    }

    /// <summary>
    /// A board on the underside of a structure that crosses over the road (GB 5768.2 4.8.12 d, Fig. 8 a: 路上方附着式; 0.12.81): an overpass, a bridge or an elevated road whose side face carries the board
    /// above the lanes it is for. What the standard gives: Fig. 8 a shows two rectangular boards flat on the face of a beam that runs across the picture, the road below, a hatched deck above; Fig. 4 c
    /// says an overhead board faces the traffic with 0 or 15 degrees of tilt (4.8.9); 4.8.15 d asks a retroreflective sheeting of a higher grade for it. In Fig. 8 a the boards stand wholly within the face,
    /// with the structure's wall above and beside them (they do not hang under it): so the board is put on the face between its lowest and highest part, and a face too low for it refuses the board (the
    /// spacers behind it would hang in the air). What the standard does not give: the height of the lower edge (4.8.11 is for roadside boards; the clearance of a road is the road's own rule), and how the
    /// board is held (the figure draws no fixing): it stands on spacers as a board flat on a wall does.
    /// Pure: the game's nets are read by the tool (PlacementTool.OverRoad.cs) into OverStructure; nothing here knows an entity.
    /// </summary>
    public static class OverRoad
    {
        /// <summary>The greatest height of a structure's underside over the road that still takes a board (m): above it the board would be too far from the traffic to read (a flyover over a flyover).</summary>
        public const float MaxUnderside = 12f;

        /// <summary>The least free height under the board's lower edge (m): the 4.5 m the height-limit frame keeps (the least a lorry needs), this mod's figure, not a clause.</summary>
        public const float MinFree = 4.5f;

        /// <summary>How far the board's edges stay from the face's lowest and highest part (m): the board is within the face, not on its edge.</summary>
        public const float TopGap = 0.1f;

        /// <summary>How far a structure may be from the road's centre line in the ground plane and still be over it (m beyond the road's half width).</summary>
        public const float Margin = 1f;

        /// <summary>The least angle (degrees) a structure must cross the road at to be taken as over it: a segment that runs along the road is a viaduct above it, which has no face across the traffic.</summary>
        public const float MinCrossDegrees = 35f;

        /// <summary>
        /// The structure of an edge's numbers, for the reader of the game's nets (Tool/OverRoadReader.cs): an elevated edge that is not a tunnel (the composition's General flags), a width and a height range that are
        /// numbers, and a lowest part that is below the highest (an edge without an underside is none). False for a road on the ground, a tunnel, or numbers that are not.
        /// </summary>
        public static bool Describe(bool elevated, bool tunnel, float width, float lowest, float highest, int id, Bezier3 curve, out OverStructure structure)
        {
            structure = default;
            if (!elevated || tunnel || float.IsNaN(width) || float.IsNaN(lowest) || float.IsNaN(highest) || !(width > 0f) || !(lowest < highest) || !curve.A.IsFinite || !curve.D.IsFinite)
                return false;
            structure = new OverStructure { Curve = curve, Width = width, Lowest = lowest, Highest = highest, Id = id };
            return true;
        }

        /// <summary>
        /// The structure over the road at a point of it, if any: the structure whose centre line passes over the road's centre line (within Margin of its half width) at an angle of at least
        /// MinCrossDegrees, whose underside is within MinFree to MaxUnderside above the road there. The lowest of them when several cross at once (the one a driver meets first). Id -1 when none.
        /// </summary>
        public static int Find(IList<OverStructure> structures, Bezier3 road, float roadWidth, float roadY, float at, out OverSpot spot)
        {
            spot = default;
            int best = -1;
            float lowest = float.MaxValue;
            Vec3 roadPoint = road.Point(at);
            Vec3 roadRun = road.Tangent(at).HeadingXZ();
            for (int i = 0; i < structures.Count; i++)
            {
                OverStructure s = structures[i];
                if (!Crossing(s, roadPoint, roadRun, roadWidth * 0.5f + Margin, out float t, out Vec3 run))
                    continue;
                Vec3 centre = s.Curve.Point(t);
                float underside = centre.Y + s.Lowest - roadY;
                if (underside < MinFree || underside > MaxUnderside || underside >= lowest)
                    continue;
                lowest = underside;
                best = i;
                spot = new OverSpot { Structure = s.Id, Point = new Vec3(centre.X, centre.Y + s.Lowest, centre.Z), Run = run, Underside = underside, FaceHeight = s.Highest - s.Lowest };
            }

            if (best < 0)
                return -1;
            // The face that looks at the traffic: the structure's side toward the oncoming traffic (against the road's heading, the board is read by those who come along it).
            Vec3 face = new Vec3(-spot.Run.Z, 0f, spot.Run.X);
            if (Vec3.Dot(face, roadRun) > 0f)
                face = face * -1f;
            spot.Face = face;
            spot.Depth = structures[best].Width;
            // The point moves out to the face: half the structure's width from its centre line.
            spot.Point = spot.Point + face * (structures[best].Width * 0.5f);
            return spot.Structure;
        }

        /// <summary>Whether the structure crosses over the road's line near roadPoint: the nearest point of its curve to the road's point is within reach, the two lines cross at MinCrossDegrees or more; t and run there.</summary>
        private static bool Crossing(OverStructure s, Vec3 roadPoint, Vec3 roadRun, float reach, out float t, out Vec3 run)
        {
            t = s.Curve.ClosestT(roadPoint, 16);
            Vec3 p = s.Curve.Point(t);
            run = s.Curve.Tangent(t).HeadingXZ();
            if (Vec3.DistanceXZ(p, roadPoint) > s.Width * 0.5f + reach)
                return false;
            float cross = Math.Abs(roadRun.X * run.Z - roadRun.Z * run.X);
            return cross >= (float)Math.Sin(MinCrossDegrees * Math.PI / 180.0);
        }

        /// <summary>
        /// The height of the board's lower edge above the road's surface (m): wholly within the face, low in it (Fig. 8 a), TopGap above its lowest part. NaN when the face is lower than the board and its
        /// two gaps (the board would stand out below the structure, and its spacers hang in the air), or when there is not MinFree under it (Find keeps to that already).
        /// </summary>
        public static float LowerEdge(float underside, float faceHeight, float boardHeight)
        {
            if (!(faceHeight >= boardHeight + 2f * TopGap) || underside < MinFree)
                return float.NaN;
            return underside + TopGap;
        }

        /// <summary>The face a board needs (m): its height and a gap above and below.</summary>
        public static float FaceNeeded(float boardHeight) => boardHeight + 2f * TopGap;

        /// <summary>Whether a board of this height stands wholly within this face, with MinFree under it.</summary>
        public static bool Fits(float underside, float faceHeight, float boardHeight) => !float.IsNaN(LowerEdge(underside, faceHeight, boardHeight));

        /// <summary>
        /// The yaw of a board on the structure's face (the sign's +Z along the face's normal): it faces the traffic. The tilt of 4.8.9 (0 or 15 degrees, nodding down toward the traffic) is not in the yaw:
        /// the board stands upright, as a board on a wall does.
        /// </summary>
        public static float Yaw(Vec3 face) => (float)Math.Atan2(face.X, face.Z);

        /// <summary>The place a board's origin takes on the structure's face: below the point (the soffit's edge on the face) and down to the road's height, as a wall board's origin is the wall's foot.</summary>
        public static Vec3 Origin(OverSpot spot, float roadY) => new Vec3(spot.Point.X, roadY, spot.Point.Z);

        // ------------------------------------------------------------------ hung on rods (0.12.81)
        // GB 5768.2 gives a board over the road only its facing and its lean (4.8.9, Fig. 4 c: 0 or 15 degrees) and Fig. 8 a, which draws boards flat on a beam's face. A board hung under a structure is not drawn
        // anywhere in it: every figure below is this mod's estimate of how overhead boards are carried (rods from plates on the soffit to a tube clamped behind the board, two sway braces back to the soffit).

        /// <summary>How far behind the structure's face line the hung board's tube axis is (m): the board hangs under the soffit, not on its edge, and the rods' plates have soffit above them.</summary>
        public const float HangSetback = 0.4f;

        /// <summary>The shortest the rods are above the board's top (m): under it the board's top edge is on the soffit and there is nothing to see hanging.</summary>
        public const float MinHang = 0.3f;

        /// <summary>The length of the rods above the board's top when the lower edge is not held down by the free height (m).</summary>
        public const float HangLength = 0.6f;

        /// <summary>How far behind the rods the sway braces reach the soffit (m) and the half width of a plate on it: the soffit must have them under it (HungDepth).</summary>
        public const float BraceRun = 0.6f, PlateHalf = 0.09f;

        /// <summary>The soffit's room behind its face line that a hung board needs (m): the setback, the braces' run, their plates and a margin of 0.1 m.</summary>
        public const float HungDepth = HangSetback + BraceRun + PlateHalf + 0.1f;

        /// <summary>
        /// The height of a hung board's lower edge over the road (m): the rods' length under the soffit (HangLength) below its top, but never lower than MinFree. total: the board's height with what stands above it.
        /// </summary>
        public static float HungLowerEdge(float soffit, float total) => Math.Max(MinFree, soffit - total - HangLength);

        /// <summary>The soffit height a hung board of this height needs (m): the free height under it, its height and the shortest rods.</summary>
        public static float HungNeeded(float total) => MinFree + total + MinHang;

        /// <summary>Whether a board this high hangs under a soffit at this height with at least MinHang of rod above it and MinFree under it. A soffit that is not a number: no.</summary>
        public static bool HungFits(float soffit, float total) => soffit >= HungNeeded(total);

        /// <summary>Whether the structure reaches far enough back from its face for the hung board's rods and braces to stand under it.</summary>
        public static bool HungDeepEnough(float depth) => depth >= HungDepth;

        /// <summary>
        /// A board on the spot Find found, flat on the structure's face or (hung) under it: the refusal with its numbers, or the item's reach (the face's lower edge, or the soffit's height for a hung board), the lower
        /// edge and the origin. boardHeight and extraTop are the sign's; the face support has no use for extraTop (as before the hung version).
        /// </summary>
        public static OverPlacement Place(OverSpot spot, float roadY, float boardHeight, float extraTop, bool hung)
        {
            var plan = new OverPlacement();
            if (hung)
            {
                float total = boardHeight + extraTop;
                plan.Lower = HungLowerEdge(spot.Underside, total);
                plan.Reach = spot.Underside;
                plan.Origin = HungOrigin(spot, roadY);
                if (!HungFits(spot.Underside, total))
                {
                    plan.Refusal = OverRefusal.TooLow;
                    plan.Left = spot.Underside;
                    plan.Need = HungNeeded(total);
                }
                else if (!HungDeepEnough(spot.Depth))
                {
                    plan.Refusal = OverRefusal.TooThin;
                    plan.Left = spot.Depth;
                    plan.Need = HungDepth;
                }

                return plan;
            }

            plan.Lower = plan.Reach = LowerEdge(spot.Underside, spot.FaceHeight, boardHeight);
            plan.Origin = Origin(spot, roadY);
            if (float.IsNaN(plan.Lower))
            {
                plan.Refusal = OverRefusal.TooLow;
                plan.Left = spot.FaceHeight;
                plan.Need = FaceNeeded(boardHeight);
            }

            return plan;
        }

        /// <summary>The place a hung board's origin takes: the road's height, HangSetback in from the face line, so that the sign's z = 0 is the axis of the tube its channels are clamped to.</summary>
        public static Vec3 HungOrigin(OverSpot spot, float roadY) => new Vec3(spot.Point.X - spot.Face.X * HangSetback, roadY, spot.Point.Z - spot.Face.Z * HangSetback);
    }
}
