using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The paint of one colour of a marking, with its words made into triangles and the grade of the road put on: what a tile draws.</summary>
    public sealed class MarkingPart
    {
        public MarkingColour Colour;
        public MeshBuilder Mesh;

        /// <summary>The layer of the paint over the road (m): paint of one colour in two layers is two parts.</summary>
        public float Lift;

        /// <summary>The paint is on an upright surface (MarkingShape.Upright): x across, y up, at z = 0 and facing -Z; its layers are in front of one another.</summary>
        public bool Upright;

        /// <summary>A solid part of a road barrier (MarkingShape.Solid); null for paint.</summary>
        public SolidColour? Solid;

        /// <summary>The baked detail of a solid's surface (0.12.32): its material is the detail material of the kind and the colour.</summary>
        public DetailKind Detail;
    }

    /// <summary>
    /// A marking as it is drawn: the shapes the template made (the paint and the boxes of the words, in the marking's own frame, flat), baked into one mesh for each colour (the words from the font by
    /// MarkingLetters, the whole tilted by the grade of the road: height added along the way the traffic runs) and the box round it for picking. The frame is the item's: +Z the way the traffic runs,
    /// +X to the right of the driver, height 0 the road surface at the item's position. Pure.
    /// </summary>
    public sealed class MarkingPlan
    {
        /// <summary>What the template made, before the words become triangles and before the grade.</summary>
        public readonly List<MarkingShape> Shapes = new List<MarkingShape>();

        /// <summary>The baked paint: at most one part for each colour.</summary>
        public readonly List<MarkingPart> Parts = new List<MarkingPart>();

        /// <summary>
        /// Outlines that are not paint (in the marking's frame, flat): the area of a clearing of the game's markings (VanillaClear). They are never drawn on the road; the tool draws them as lines, the picture
        /// of the marking shows them, and they can be pointed at (low, under any paint).
        /// </summary>
        public readonly List<List<Vec3>> Outlines = new List<List<Vec3>>();

        /// <summary>The top of the boxes to point at an outline by (m over the road): under those of paint (0.3 m), so that a marking painted inside the area is picked before it.</summary>
        public const float OutlineTop = 0.02f;

        /// <summary>The game's things to be put on the marking (0.12.32: the plants of a planter), in the marking's frame as the template made them.</summary>
        public readonly List<PropAnchor> Props = new List<PropAnchor>();

        /// <summary>Props laid on the grade or the ground as the paint and the solids are (Bake).</summary>
        public readonly List<PropAnchor> BakedProps = new List<PropAnchor>();

        /// <summary>The detail a solid of a colour has when its shape names none: concrete and galvanised steel their own, wood its grain, soil its clods, bricks their courses, the painted colours a powder coat; glass none (it is the game's glass, 0.12.49).</summary>
        public static DetailKind DetailOf(SolidColour colour)
        {
            switch (colour)
            {
                case SolidColour.Concrete: return DetailKind.Concrete;
                case SolidColour.Metal:
                case SolidColour.Steel: return DetailKind.Galvanised;
                case SolidColour.Wood: return DetailKind.Wood;
                case SolidColour.Soil: return DetailKind.Soil;
                case SolidColour.Granite: return DetailKind.KerbStone;
                case SolidColour.Brick:
                case SolidColour.GreyBrick: return DetailKind.Brick;
                case SolidColour.Glass:
                case SolidColour.GlassBlue:
                case SolidColour.GlassGreen: return DetailKind.None;
                default: return DetailKind.Painted;
            }
        }

        /// <summary>Whether the plan has solid parts (a road barrier), which are drawn and pictured as bodies, not as paint.</summary>
        public bool HasSolids => Parts.Exists(p => p.Solid.HasValue);

        /// <summary>The box round all the paint and the outlines in the marking's frame (the grade not included); zero when there is none.</summary>
        public float MinX, MaxX, MinZ, MaxZ;

        /// <summary>The line the marking is bent along (0.12.49, MarkingBend), null for a straight one; and the box round its paint before it was bent, over which the ground under it is read.</summary>
        public MarkingBend Bend;

        public float StraightMinX, StraightMaxX, StraightMinZ, StraightMaxZ;

        /// <summary>
        /// Boxes to point at, one for each cell of the given size (m) that holds paint, so that the empty road in the bend of a long run is not picked; if that is more than the limit the cells are
        /// made larger until it is not. In the marking's frame, from just under the road to 0.3 m above it.
        /// </summary>
        public List<PickShape> PickShapes(float cell, int limit)
        {
            var shapes = new List<PickShape>();
            if (!(cell > 0f))
                return shapes;
            // The paint, or with none the areas of the outlines (filled), and how high the boxes reach.
            var meshes = new List<MeshBuilder>();
            float top = 0.3f;
            foreach (MarkingPart part in Parts)
                meshes.Add(part.Mesh);
            if (meshes.Count == 0 && Outlines.Count > 0)
            {
                var area = new MeshBuilder();
                foreach (List<Vec3> outline in Outlines)
                    Polygon.Fill(area, outline);
                meshes.Add(area);
                top = OutlineTop;
            }

            if (meshes.Count == 0)
                return shapes;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var boxes = new Dictionary<(int, int), Vec3[]>();
                foreach (MeshBuilder mesh in meshes)
                {
                    for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
                    {
                        // The box of the triangle, cut at the cells it reaches into: each cell holds the part of the paint that is in it.
                        Vec3 p0 = mesh.Positions[mesh.Indices[i]], p1 = mesh.Positions[mesh.Indices[i + 1]], p2 = mesh.Positions[mesh.Indices[i + 2]];
                        float minX = Math.Min(p0.X, Math.Min(p1.X, p2.X)), maxX = Math.Max(p0.X, Math.Max(p1.X, p2.X));
                        float minZ = Math.Min(p0.Z, Math.Min(p1.Z, p2.Z)), maxZ = Math.Max(p0.Z, Math.Max(p1.Z, p2.Z));
                        float minY = Math.Min(p0.Y, Math.Min(p1.Y, p2.Y)), maxY = Math.Max(p0.Y, Math.Max(p1.Y, p2.Y));
                        int cx0 = (int)Math.Floor(minX / cell), cx1 = (int)Math.Floor(maxX / cell), cz0 = (int)Math.Floor(minZ / cell), cz1 = (int)Math.Floor(maxZ / cell);
                        if ((long)(cx1 - cx0 + 1) * (cz1 - cz0 + 1) > 4096)
                        {
                            cx1 = cx0;
                            cz1 = cz0;
                        }

                        for (int cx = cx0; cx <= cx1; cx++)
                        {
                            for (int cz = cz0; cz <= cz1; cz++)
                            {
                                float lowX = Math.Max(minX, cx * cell), highX = Math.Min(maxX, (cx + 1) * cell);
                                float lowZ = Math.Max(minZ, cz * cell), highZ = Math.Min(maxZ, (cz + 1) * cell);
                                if (cx0 == cx1 && cz0 == cz1)
                                {
                                    lowX = minX;
                                    highX = maxX;
                                    lowZ = minZ;
                                    highZ = maxZ;
                                }

                                if (highX < lowX || highZ < lowZ)
                                    continue;
                                var key = (cx, cz);
                                if (!boxes.TryGetValue(key, out Vec3[] box))
                                {
                                    boxes[key] = new[] { new Vec3(lowX, minY, lowZ), new Vec3(highX, maxY, highZ) };
                                }
                                else
                                {
                                    box[0] = new Vec3(Math.Min(box[0].X, lowX), Math.Min(box[0].Y, minY), Math.Min(box[0].Z, lowZ));
                                    box[1] = new Vec3(Math.Max(box[1].X, highX), Math.Max(box[1].Y, maxY), Math.Max(box[1].Z, highZ));
                                }
                            }
                        }
                    }
                }

                if (boxes.Count <= limit)
                {
                    foreach (Vec3[] box in boxes.Values)
                        shapes.Add(new PickShape { Name = "marking", Min = new Vec3(box[0].X - 0.1f, Math.Min(box[0].Y, 0f) - 0.1f, box[0].Z - 0.1f), Max = new Vec3(box[1].X + 0.1f, Math.Max(box[1].Y, 0f) + top, box[1].Z + 0.1f) });
                    return shapes;
                }

                cell *= 2f;
            }

            // Whatever is left: one box round it all.
            shapes.Add(new PickShape { Name = "marking", Min = new Vec3(MinX, -0.1f, MinZ), Max = new Vec3(MaxX, top, MaxZ) });
            return shapes;
        }

        /// <summary>The vertices of the paint (all colours): what the plan weighs in the cache.</summary>
        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (MarkingPart p in Parts)
                    n += p.Mesh.VertexCount;
                foreach (List<Vec3> outline in Outlines)
                    n += outline.Count;
                return n;
            }
        }

        private SignParts m_PickParts;

        /// <summary>
        /// What can be pointed at on the marking (the boxes over the cells of its paint, 2 m cells, at most 120 boxes, and its bounds), made once: the pointing of the tool asks for it for every marking near the
        /// cursor at every frame the mouse moves. Shared: the caller only reads it.
        /// </summary>
        public SignParts CachedPickParts()
        {
            SignParts parts = m_PickParts;
            if (parts != null)
                return parts;
            lock (Parts)
            {
                if (m_PickParts != null)
                    return m_PickParts;
                parts = new SignParts();
                foreach (PickShape shape in PickShapes(2f, 120))
                    parts.Picks.Add(shape);
                parts.Min = new Vec3(MinX, 0f, MinZ);
                parts.Max = new Vec3(MaxX, Parts.Count == 0 && Outlines.Count > 0 ? OutlineTop : 0.3f, MaxZ);
                m_PickParts = parts;
                return parts;
            }
        }

        /// <summary>The triangles of the paint (all colours).</summary>
        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (MarkingPart p in Parts)
                    n += p.Mesh.TriangleCount;
                return n;
            }
        }

        /// <summary>
        /// Makes the parts: the shapes' meshes and the words of each colour into one mesh, lifted by the grade (rise over run along +Z) or, when the item has one, laid on the heights of the ground under it
        /// (0.12.30, GroundGrid: each vertex by the ground where it is, the paint on an upright surface too, as the thing it is on). A grade beyond 30 % is not a road, and is left out. With a bend (0.12.49)
        /// the flat paint is cut (MarkingBend.Cut) and bent along its line (MarkingBend.Map) after it has taken its heights where it was straight (the ground is read over the straight box: MarkingGround.GridUnder).
        /// With surfaceNormals (0.12.83) the paint's normals follow the heights it ended with (SurfaceNormals.Fit), after the bend.
        /// </summary>
        public void Bake(float grade, GroundGrid ground = null, MarkingBend bend = null, bool surfaceNormals = false)
        {
            Bend = bend;
            Parts.Clear();
            if (float.IsNaN(grade) || float.IsInfinity(grade) || Math.Abs(grade) > 0.3f)
                grade = 0f;
            MinX = MinZ = float.MaxValue;
            MaxX = MaxZ = float.MinValue;
            foreach (MarkingShape shape in Shapes)
            {
                MarkingPart part = null;
                DetailKind detail = !shape.Solid.HasValue ? DetailKind.None : shape.Detail != DetailKind.None ? shape.Detail : DetailOf(shape.Solid.Value);
                foreach (MarkingPart existing in Parts)
                {
                    if (existing.Colour == shape.Colour && Math.Abs(existing.Lift - shape.Lift) < 1e-6f && existing.Upright == shape.Upright && existing.Solid == shape.Solid && existing.Detail == detail)
                        part = existing;
                }

                if (part == null)
                {
                    part = new MarkingPart { Colour = shape.Colour, Mesh = new MeshBuilder(), Lift = shape.Lift, Upright = shape.Upright, Solid = shape.Solid, Detail = detail };
                    Parts.Add(part);
                }

                part.Mesh.Append(shape.Mesh, Vec3.Zero, 0f);
                if (shape.Texts.Count > 0)
                    MarkingLetters.AddTexts(part.Mesh, shape.Texts);
            }

            StraightMinX = StraightMinZ = float.MaxValue;
            StraightMaxX = StraightMaxZ = float.MinValue;
            foreach (MarkingPart part in Parts)
            {
                if (part.Detail != DetailKind.None)
                {
                    DetailUv.Ensure(part.Mesh);
                    DetailSurfaces.ToTile(part.Mesh, part.Detail);
                }

                bool bent = bend != null && !part.Upright;
                if (bent)
                {
                    foreach (Vec3 p in part.Mesh.Positions)
                    {
                        StraightMinX = Math.Min(StraightMinX, p.X);
                        StraightMaxX = Math.Max(StraightMaxX, p.X);
                        StraightMinZ = Math.Min(StraightMinZ, p.Z);
                        StraightMaxZ = Math.Max(StraightMaxZ, p.Z);
                    }

                    MarkingBend.Cut(part.Mesh, bend.CutsWithin(StraightMinZ, StraightMaxZ));
                }
                else
                {
                    foreach (Vec3 p in part.Mesh.Positions)
                    {
                        MinX = Math.Min(MinX, p.X);
                        MaxX = Math.Max(MaxX, p.X);
                        MinZ = Math.Min(MinZ, p.Z);
                        MaxZ = Math.Max(MaxZ, p.Z);
                    }
                }

                if (ground != null)
                {
                    for (int i = 0; i < part.Mesh.Positions.Count; i++)
                    {
                        Vec3 p = part.Mesh.Positions[i];
                        float h = ground.At(p.X, p.Z);
                        part.Mesh.Positions[i] = part.Upright ? new Vec3(p.X, p.Y + h, p.Z - part.Lift) : new Vec3(p.X, p.Y + h + part.Lift, p.Z);
                    }
                }
                else if (grade != 0f || part.Lift != 0f)
                {
                    for (int i = 0; i < part.Mesh.Positions.Count; i++)
                    {
                        Vec3 p = part.Mesh.Positions[i];
                        // A layer of paint on the road is raised; a layer on an upright surface is brought toward the viewer (-Z).
                        part.Mesh.Positions[i] = part.Upright ? new Vec3(p.X, p.Y, p.Z - part.Lift) : new Vec3(p.X, p.Y + p.Z * grade + part.Lift, p.Z);
                    }
                }

                if (bent)
                {
                    for (int i = 0; i < part.Mesh.Positions.Count; i++)
                    {
                        Vec3 p = part.Mesh.Positions[i];
                        Vec3 m = bend.Map(p.X, p.Z);
                        part.Mesh.Positions[i] = new Vec3(m.X, p.Y, m.Z);
                        MinX = Math.Min(MinX, m.X);
                        MaxX = Math.Max(MaxX, m.X);
                        MinZ = Math.Min(MinZ, m.Z);
                        MaxZ = Math.Max(MaxZ, m.Z);
                    }
                }

                // A solid's top faces up too, but it stands on the ground and is not laid on it; a sheet on an upright surface has no triangle that faces up.
                if (surfaceNormals && !part.Solid.HasValue)
                    SurfaceNormals.Fit(part.Mesh);
            }

            foreach (List<Vec3> outline in Outlines)
            {
                foreach (Vec3 p in outline)
                {
                    MinX = Math.Min(MinX, p.X);
                    MaxX = Math.Max(MaxX, p.X);
                    MinZ = Math.Min(MinZ, p.Z);
                    MaxZ = Math.Max(MaxZ, p.Z);
                }
            }

            if (MinX > MaxX)
                MinX = MaxX = MinZ = MaxZ = 0f;
            BakedProps.Clear();
            foreach (PropAnchor prop in Props)
            {
                Vec3 p = prop.Local;
                float h = ground != null ? ground.At(p.X, p.Z) : p.Z * grade;
                BakedProps.Add(new PropAnchor { Local = new Vec3(p.X, p.Y + h, p.Z), Yaw = prop.Yaw, Kind = prop.Kind, Room = prop.Room });
            }
        }
    }
}
