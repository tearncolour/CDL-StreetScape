using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>Whose markings an area clears: those of the road segments in it, those of the junctions in it, or both.</summary>
    public enum ClearScope
    {
        Edges = 0,
        Nodes = 1,
        All = 2
    }

    /// <summary>What a marking of the game belongs to: a road segment (an edge), a junction or the node between two segments, or something else (a building's lot: never cleared).</summary>
    public enum MarkingOwner
    {
        Other = 0,
        Edge = 1,
        Node = 2
    }

    /// <summary>A marking of the game as the clearing sees it: the middle of its curve (world X, Z), what owns it, and whether it is a line across the traffic (a stop or give-way line, a crosswalk).</summary>
    public struct VanillaMarking
    {
        public float X, Y, Z;
        public MarkingOwner Owner;
        public bool Crossing;
    }

    /// <summary>A box on the ground (world X, Z) where the markings are to be looked at again.</summary>
    public struct ClearBox
    {
        public float MinX, MinZ, MaxX, MaxZ;

        public ClearBox(float minX, float minZ, float maxX, float maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }
    }

    /// <summary>
    /// One area where the game's own road markings are not drawn (an item of mk.clear): its outline in the world, whose markings it takes and whether it leaves the lines across the traffic. A marking is in
    /// the area when the middle of its curve is: the game's markings are pieces that run along one lane or across the road, and a piece is cleared whole or not at all. Pure.
    /// </summary>
    public sealed class ClearRegion
    {
        public readonly List<Vec3> Outline;
        public readonly ClearScope Scope;
        public readonly bool KeepCrossings;
        public readonly bool StripSurface;
        public readonly float MinX, MaxX, MinZ, MaxZ;

        /// <summary>The region is an area marking's (OfArea), not an mk.clear area: the tool does not draw it as a clearing (0.12.14: islands showed a red clearing outline).</summary>
        public bool FromArea { get; private set; }

        public ClearRegion(IList<Vec3> outline, ClearScope scope, bool keepCrossings, bool stripSurface = false)
        {
            Outline = outline == null ? new List<Vec3>() : new List<Vec3>(outline);
            Scope = scope;
            KeepCrossings = keepCrossings;
            StripSurface = stripSurface && Outline.Count >= 4 && Outline.Count % 2 == 0;
            MinX = MinZ = float.MaxValue;
            MaxX = MaxZ = float.MinValue;
            foreach (Vec3 p in Outline)
            {
                MinX = Math.Min(MinX, p.X);
                MaxX = Math.Max(MaxX, p.X);
                MinZ = Math.Min(MinZ, p.Z);
                MaxZ = Math.Max(MaxZ, p.Z);
            }
        }

        public ClearBox Box => new ClearBox(MinX, MinZ, MaxX, MaxZ);

        private List<Vec3> m_Drawn;
        private List<Vec3[]> m_Surface;
        private Bezier3? m_LegacyCurve;
        private float m_LegacyOffset;

        /// <summary>Old areas saved no corner heights. A surviving road hint can recover its slope without rewriting the save or choosing another deck.</summary>
        public ClearRegion AlongLegacyRoad(Bezier3 curve, Vec3 origin)
        {
            if (!origin.IsFinite)
                return this;
            float offset = origin.Y - curve.Point(curve.ClosestT(origin)).Y;
            var outline = new List<Vec3>();
            foreach (Vec3 p in Outline)
                outline.Add(new Vec3(p.X, curve.Point(curve.ClosestT(p)).Y + offset, p.Z));
            return new ClearRegion(outline, Scope, KeepCrossings) { m_LegacyCurve = curve, m_LegacyOffset = offset };
        }

        /// <summary>Road mesh/paint offsets are small; a deck several metres above or below must never share a clearing.</summary>
        public const float HeightTolerance = 1.5f;

        /// <summary>The outline as the tool's overlay draws it (OverlayLine), made the first time it is drawn.</summary>
        public List<Vec3> Drawn => m_Drawn ?? (m_Drawn = OverlayLine.Simplify(Outline));

        /// <summary>Whether a point of the ground is inside the outline (even-odd; an outline of fewer than three corners has no inside).</summary>
        public bool Contains(float x, float z)
        {
            if (Outline.Count < 3 || x < MinX || x > MaxX || z < MinZ || z > MaxZ)
                return false;
            bool inside = false;
            for (int i = 0, j = Outline.Count - 1; i < Outline.Count; j = i++)
            {
                Vec3 a = Outline[i], b = Outline[j];
                if ((a.Z > z) != (b.Z > z) && x < (b.X - a.X) * (z - a.Z) / (b.Z - a.Z) + a.X)
                    inside = !inside;
            }

            return inside;
        }

        /// <summary>A point inside this clearing on the same road surface, including sloping and crowned outlines.</summary>
        public bool Contains(Vec3 point) => point.IsFinite && Contains(point.X, point.Z) &&
            TryHeight(point.X, point.Z, out float height) && Math.Abs(point.Y - height) <= HeightTolerance;

        private bool TryHeight(float x, float z, out float height)
        {
            if (m_LegacyCurve.HasValue)
            {
                Bezier3 curve = m_LegacyCurve.Value;
                height = curve.Point(curve.ClosestT(new Vec3(x, 0f, z))).Y + m_LegacyOffset;
                return !float.IsNaN(height) && !float.IsInfinity(height);
            }
            if (m_Surface == null)
            {
                m_Surface = new List<Vec3[]>();
                if (StripSurface)
                {
                    // A road's two sides contain matching cross-sections, in opposite order around the outline.
                    int half = Outline.Count / 2;
                    for (int i = 0; i + 1 < half; i++)
                    {
                        Vec3 a = Outline[i], b = Outline[Outline.Count - 1 - i], c = Outline[Outline.Count - 2 - i], d = Outline[i + 1];
                        m_Surface.Add(new[] { a, b, c });
                        m_Surface.Add(new[] { a, c, d });
                    }
                }
                else
                {
                    var mesh = new MeshBuilder();
                    Polygon.Fill(mesh, Outline);
                    for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                        m_Surface.Add(new[] { mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 1]], mesh.Positions[mesh.Indices[i + 2]] });

                    // Flat polygon triangulation may omit collinear boundary samples. Split those edges to retain a road's vertical crest/dip.
                    foreach (Vec3 p in Outline)
                    {
                        for (int i = m_Surface.Count - 1; i >= 0; i--)
                        {
                            Vec3[] tri = m_Surface[i];
                            for (int edge = 0; edge < 3; edge++)
                            {
                                Vec3 a = tri[edge], b = tri[(edge + 1) % 3], c = tri[(edge + 2) % 3];
                                float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
                                if (length2 < 1e-8f)
                                    continue;
                                float t = ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / length2;
                                if (t <= 1e-5f || t >= 1f - 1e-5f || Math.Abs(Polygon.Cross(a, b, p)) > 1e-4f)
                                    continue;
                                m_Surface[i] = new[] { a, p, c };
                                m_Surface.Add(new[] { p, b, c });
                                break;
                            }
                        }
                    }
                }
            }

            var at = new Vec3(x, 0f, z);
            foreach (Vec3[] tri in m_Surface)
            {
                float area = Polygon.Cross(tri[0], tri[1], tri[2]);
                if (Math.Abs(area) < 1e-8f)
                    continue;
                float a = Polygon.Cross(tri[1], tri[2], at) / area, b = Polygon.Cross(tri[2], tri[0], at) / area;
                float c = 1f - a - b;
                if (a >= -1e-5f && b >= -1e-5f && c >= -1e-5f)
                {
                    height = a * tri[0].Y + b * tri[1].Y + c * tri[2].Y;
                    return true;
                }
            }
            height = 0f;
            return false;
        }

        /// <summary>Whether the area takes this marking: its owner is of the area's scope, it is not a line across the traffic the area keeps, and its middle is inside.</summary>
        public bool Covers(in VanillaMarking marking)
        {
            if (marking.Owner == MarkingOwner.Other)
                return false;
            if (Scope == ClearScope.Edges && marking.Owner != MarkingOwner.Edge)
                return false;
            if (Scope == ClearScope.Nodes && marking.Owner != MarkingOwner.Node)
                return false;
            if (KeepCrossings && marking.Crossing)
                return false;
            return Contains(new Vec3(marking.X, marking.Y, marking.Z));
        }

        /// <summary>The same area: the same corners (to a millimetre), scope and keeping.</summary>
        public bool SameAs(ClearRegion other)
        {
            if (other == null || other.Scope != Scope || other.KeepCrossings != KeepCrossings || other.StripSurface != StripSurface || other.Outline.Count != Outline.Count ||
                !Nullable.Equals(m_LegacyCurve, other.m_LegacyCurve) || Math.Abs(m_LegacyOffset - other.m_LegacyOffset) > 1e-3f)
                return false;
            for (int i = 0; i < Outline.Count; i++)
            {
                if (Math.Abs(other.Outline[i].X - Outline[i].X) > 1e-3f || Math.Abs(other.Outline[i].Y - Outline[i].Y) > 1e-3f || Math.Abs(other.Outline[i].Z - Outline[i].Z) > 1e-3f)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// The game's markings under an area marking (an island, a box grid, a gore: AreaFitRules.IsAreaHash) are not drawn (0.12.12; the owner's island at a fork lay over the game's own hatched gore, the two
        /// patterns crossed). Its outline (the drawn polygon; else the box of its paint) of every owner, keeping the lines across the traffic. Null for an item of another kind or with no paint.
        /// </summary>
        public static ClearRegion OfArea(ItemRecord record)
        {
            if (!AreaFitRules.IsAreaHash(record.TypeHash) || !record.Position.IsFinite || float.IsNaN(record.Yaw) || float.IsInfinity(record.Yaw))
                return null;
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            if (def?.Template == null)
                return null;
            List<Vec3> local = MarkingPolygon.Decode(def.Template.Parse(record.Spec).Get(MarkingPolygon.Key));
            if (local.Count < 3)
            {
                MarkingPlan plan = MarkingTemplates.PlanOf(def, record.Spec);
                if (plan == null || !(plan.MaxX > plan.MinX) || !(plan.MaxZ > plan.MinZ))
                    return null;
                local = new List<Vec3> { new Vec3(plan.MinX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MinZ), new Vec3(plan.MaxX, 0f, plan.MaxZ), new Vec3(plan.MinX, 0f, plan.MaxZ) };
            }

            return new ClearRegion(MarkingPolygon.ToWorld(local, record.Position, record.Yaw), ClearScope.All, true) { FromArea = true };
        }

        /// <summary>The area of an item of mk.clear: its outline (in the item's frame in its settings) put in the world; null for an item of another kind or one whose outline cannot be read.</summary>
        public static ClearRegion Of(ItemRecord record)
        {
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            if (def == null || def.Id != VanillaClear.Id || def.Template == null || !record.Position.IsFinite || float.IsNaN(record.Yaw) || float.IsInfinity(record.Yaw))
                return null;
            GuideSpec spec = def.Template.Parse(record.Spec);
            List<Vec3> local = ClearElevation.Decode(spec);
            if (local.Count < 3)
                return null;
            ClearScope scope = spec.Is(VanillaClear.ScopeKey, "n") ? ClearScope.Nodes : spec.Is(VanillaClear.ScopeKey, "a") ? ClearScope.All : ClearScope.Edges;
            return new ClearRegion(MarkingPolygon.ToWorld(local, record.Position, record.Yaw), scope, !spec.Is(VanillaClear.KeepKey, "all"), ClearElevation.IsStrip(spec));
        }
    }

    /// <summary>
    /// Clearing the game's own road markings where the player wants to draw them (mk.clear, 0.12.0). The game's markings are lanes of their own (Game.Net.SecondaryLane, made from the road's car lanes by
    /// SecondaryLaneSystem); one that carries Game.Common.Overridden is not drawn (PreCullingSystem gives it only the debug mask) and is left alone by every system of the game that is not about fences. What is
    /// pure is here: which markings an area takes, the outline of a road segment or a junction, and where two sets of areas differ. The game side is ClearMarkingsSystem.
    /// </summary>
    public static class VanillaClear
    {
        public const string Id = "mk.clear";

        /// <summary>The keys of the settings: how the area is chosen (e a segment, r a whole road, n a junction, a drawn), which markings (lines: keep the lines across the traffic; all), redraw the standard's lines, whose markings (e, n, a).</summary>
        public const string ModeKey = "mode", KeepKey = "keep", RedrawKey = "redraw", ScopeKey = "scope";

        /// <summary>How far (m) an outline read from the road is grown outward: the edge line of the game lies a few centimetres inside the road's side.</summary>
        public const float Margin = 0.5f;

        /// <summary>Replacement of automatic junction items also respects the surface, even when two roads have the same plan view.</summary>
        public static bool NearSameSurface(Vec3 a, Vec3 b, float radius) => a.IsFinite && b.IsFinite &&
            Vec3.DistanceXZ(a, b) <= radius && Math.Abs(a.Y - b.Y) <= ClearRegion.HeightTolerance;

        /// <summary>
        /// Whether a node is cleared with the road whose segments meet there: two of the road's segments meet at it and at most one other (a ramp that leaves or joins, a side street), not where the road
        /// crosses another (0.12.1: the owner's interchange, where a ramp joins the road, kept its lines when the road was cleared).
        /// </summary>
        public static bool NodeOfRoad(int roadSegments, int otherSegments) => roadSegments == 2 && otherSegments <= 1;

        /// <summary>Whether any of the areas takes the marking.</summary>
        public static bool Covered(IList<ClearRegion> regions, in VanillaMarking marking)
        {
            if (regions == null)
                return false;
            foreach (ClearRegion region in regions)
            {
                if (region != null && region.Covers(marking))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Words in the name of a lane prefab of the game that say it is a line across the traffic (a crosswalk, a stop or give-way line). Not read from the game's data: the names of the prefabs are written to
        /// the log the first time a city's markings are looked at, so that this can be checked; a marking that the game puts across the lanes (SecondaryNetLane with the crossing flag) is known as one anyway.
        /// </summary>
        public static bool IsCrossingName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            string n = name.ToLowerInvariant();
            return n.Contains("crosswalk") || n.Contains("crossing") || n.Contains("zebra") || n.Contains("stop") || n.Contains("yield") || n.Contains("give way") || n.Contains("giveway");
        }

        /// <summary>
        /// A simple outline grown outward by margin (m): each corner moved out along the bisector of its two sides' outward normals, by margin over the cosine of half the turn (at most three times the margin, so
        /// that a sharp corner does not make a spike). Either winding; an outline of fewer than three corners, or with no area, is returned as it is.
        /// </summary>
        public static List<Vec3> Grow(IList<Vec3> outline, float margin)
        {
            var result = new List<Vec3>(outline ?? new List<Vec3>());
            int n = result.Count;
            float area = n >= 3 ? Polygon.SignedArea2(result) : 0f;
            if (n < 3 || Math.Abs(area) < 1e-6f || !(margin > 0f))
                return result;
            // Counter-clockwise (positive area): the outward normal of a side a->b is its right, (dz, -dx).
            float sign = area > 0f ? 1f : -1f;
            var grown = new List<Vec3>(n);
            for (int i = 0; i < n; i++)
            {
                Vec3 prev = result[(i + n - 1) % n], here = result[i], next = result[(i + 1) % n];
                Vec3 n1 = OutwardNormal(prev, here, sign), n2 = OutwardNormal(here, next, sign);
                float bx = n1.X + n2.X, bz = n1.Z + n2.Z;
                float len = (float)Math.Sqrt(bx * bx + bz * bz);
                if (len < 1e-6f)
                {
                    grown.Add(new Vec3(here.X + n1.X * margin, here.Y, here.Z + n1.Z * margin));
                    continue;
                }

                bx /= len;
                bz /= len;
                float cosHalf = bx * n1.X + bz * n1.Z;
                float distance = Math.Min(margin * 3f, margin / Math.Max(1e-3f, cosHalf));
                grown.Add(new Vec3(here.X + bx * distance, here.Y, here.Z + bz * distance));
            }

            return grown;
        }

        private static Vec3 OutwardNormal(Vec3 a, Vec3 b, float sign)
        {
            float dx = b.X - a.X, dz = b.Z - a.Z, len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6f)
                return Vec3.Zero;
            return new Vec3(dz / len * sign, 0f, -dx / len * sign);
        }

        /// <summary>The outline of a road segment from its two sides (both from one end of it to the other): the left side, then the right side back, grown by margin. Empty when either side has fewer than two points.</summary>
        public static List<Vec3> SideOutline(IList<Vec3> left, IList<Vec3> right, float margin)
        {
            if (left == null || right == null || left.Count < 2 || right.Count < 2)
                return new List<Vec3>();
            var outline = new List<Vec3>(left);
            for (int i = right.Count - 1; i >= 0; i--)
                outline.Add(right[i]);
            return Grow(outline, margin);
        }

        /// <summary>
        /// The convex hull of points on the ground (counter-clockwise), with at most maxCorners corners (the corners that add the least are left out first, which only cuts a little off) and grown by margin: the
        /// outline of a junction from the points of its surface. Empty for fewer than three points that are not in a line.
        /// </summary>
        public static List<Vec3> Hull(IList<Vec3> points, int maxCorners, float margin)
        {
            var sorted = new List<Vec3>();
            if (points != null)
            {
                foreach (Vec3 p in points)
                {
                    if (p.IsFinite)
                        sorted.Add(p);
                }
            }

            if (sorted.Count < 3)
                return new List<Vec3>();
            sorted.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
            var hull = new List<Vec3>();
            // Andrew's monotone chain: the lower chain, then the upper.
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int k = 0; k < sorted.Count; k++)
                {
                    Vec3 p = pass == 0 ? sorted[k] : sorted[sorted.Count - 1 - k];
                    while (hull.Count >= start + 2 && Polygon.Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 1e-9f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
            }

            if (hull.Count < 3 || Math.Abs(Polygon.SignedArea2(hull)) < 1e-4f)
                return new List<Vec3>();
            if (hull.Count > Math.Max(3, maxCorners))
                hull = MarkingPolygon.Simplify(hull, Math.Max(3, maxCorners));
            return Grow(hull, margin);
        }

        /// <summary>
        /// Where two sets of areas (by the item each belongs to) differ: the box of each area that is new or gone, and both boxes of one that changed. The markings in these boxes are the only ones whose clearing
        /// can have changed.
        /// </summary>
        public static List<ClearBox> Changes<TKey>(IDictionary<TKey, ClearRegion> before, IDictionary<TKey, ClearRegion> after)
        {
            var boxes = new List<ClearBox>();
            if (before != null)
            {
                foreach (KeyValuePair<TKey, ClearRegion> pair in before)
                {
                    if (pair.Value == null)
                        continue;
                    if (after == null || !after.TryGetValue(pair.Key, out ClearRegion now) || now == null || !now.SameAs(pair.Value))
                        boxes.Add(pair.Value.Box);
                }
            }

            if (after != null)
            {
                foreach (KeyValuePair<TKey, ClearRegion> pair in after)
                {
                    if (pair.Value == null)
                        continue;
                    if (before == null || !before.TryGetValue(pair.Key, out ClearRegion was) || was == null || !was.SameAs(pair.Value))
                        boxes.Add(pair.Value.Box);
                }
            }

            return boxes;
        }
    }
}
