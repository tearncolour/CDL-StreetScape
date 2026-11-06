using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The foot of one post of a support as SignBuilder made it (0.12.5): where the post stands in the sign's frame, how large its pedestal is, and which vertices of the support's meshes are the foot (the pad,
    /// the flange, the studs, the ribs: they move with the ground) and which are the post's tube (its lower ring moves with the foot, its upper end stays: the post grows or shrinks).
    /// </summary>
    public sealed class PostFoot
    {
        public float X, Z, PadHalf;

        /// <summary>The height of the pad's lower face (it reaches down to the lowest ground under the pad) and of the line below which the tube's vertices are its lower end (sign's frame).</summary>
        public float PadBottom, Split;

        /// <summary>How far up (m) the foot may go: the boards' lower edge must stay clear of the ground.</summary>
        public float MaxRaise;

        /// <summary>The ranges of the vertices of the foot (metal and concrete) and of the post's tube (metal).</summary>
        public int MetalFrom, MetalTo, TubeFrom, TubeTo, ConcreteFrom, ConcreteTo;

        /// <summary>
        /// The paint on the post's root (0.12.57, SignBuilder.PaintRoot) and the Shanghai pole's door seam: ranges of the vertices of coloured solids, all of them moving with the foot but no higher than
        /// PaintTop (1 cm under the post's top, which stays where it is).
        /// </summary>
        public readonly List<(SolidColour Colour, int From, int To)> Paint = new List<(SolidColour Colour, int From, int To)>();

        public float PaintTop = float.MaxValue;

        /// <summary>
        /// The post's tube where it is made of coloured solids (0.12.78, a height-limit frame's banded columns): ranges of their vertices that move as the metal tube's do, those below Split with the foot,
        /// those above where they are.
        /// </summary>
        public readonly List<(SolidColour Colour, int From, int To)> TubeSolids = new List<(SolidColour Colour, int From, int To)>();
    }

    /// <summary>
    /// The ground along the line of a support's posts (0.12.5): heights relative to the item's own (the road's surface where it was placed), along its local X (the line across the road the posts stand on)
    /// every Step metres, in three rows (local Z = -RowZ, 0, +RowZ: the pad's front and back). NaN where the ground is not known or is not to be followed (a foot beside a bridge over a valley).
    /// Made on the main thread from the roads' surfaces and the terrain (AreaFitSystem), read by the tile builders. Pure.
    /// </summary>
    public sealed class GroundProfile
    {
        public const float Step = 0.5f, RowZ = 0.8f;

        private readonly float m_X0;
        private readonly float[][] m_Rows;

        private static int s_Versions;

        /// <summary>Different for every profile made.</summary>
        public readonly int Version = ++s_Versions;

        /// <summary>What it was made from, for the log.</summary>
        public string Note = "";

        public float X0 => m_X0;

        public int Count => m_Rows[1].Length;

        private GroundProfile(float x0, int count)
        {
            m_X0 = x0;
            m_Rows = new[] { new float[count], new float[count], new float[count] };
        }

        /// <summary>A profile from x0 to x1 (local X) from a function of the ground's height at a place of the item's frame (NaN: not known).</summary>
        public static GroundProfile Sample(Func<float, float, float> ground, float x0, float x1)
        {
            int count = Math.Max(2, Math.Min(400, (int)Math.Ceiling((x1 - x0) / Step) + 1));
            var profile = new GroundProfile(x0, count);
            for (int row = 0; row < 3; row++)
            {
                float z = (row - 1) * RowZ;
                for (int i = 0; i < count; i++)
                {
                    float h = ground == null ? float.NaN : ground(x0 + i * Step, z);
                    profile.m_Rows[row][i] = float.IsInfinity(h) ? float.NaN : h;
                }
            }

            return profile;
        }

        /// <summary>The ground's height at a place of the item's frame: along X between the samples, across Z between the rows (outside the rows the nearest); NaN where a sample it needs is not known.</summary>
        public float At(float x, float z)
        {
            float f = (x - m_X0) / Step;
            if (float.IsNaN(f) || f < -0.5f || f > Count - 0.5f)
                return float.NaN;
            int i = Math.Max(0, Math.Min(Count - 2, (int)Math.Floor(f)));
            float t = Math.Max(0f, Math.Min(1f, f - i));
            float r = Math.Max(-1f, Math.Min(1f, z / RowZ));
            int row = r < 0f ? 0 : 2;
            float w = Math.Abs(r);
            float centre = Lerp(m_Rows[1][i], m_Rows[1][i + 1], t), side = Lerp(m_Rows[row][i], m_Rows[row][i + 1], t);
            if (w < 1e-6f)
                return centre;
            return centre + (side - centre) * w;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    /// <summary>
    /// Stands the posts of a support on the ground under each of them (0.12.5; the owner: "立柱自动接地"): a post stays plumb, its foot goes to the ground (down a slope beside the road, up onto a footway or a
    /// verge) and the post grows or shrinks to it; the boards and the beam stay where they were, at their clearance above the road (GB 5768.2 4.8.11 measures it from the road). The support's meshes are the
    /// ones built and kept for every sign of its kind; only their copy into a tile is moved (Append), foot by foot. Pure.
    /// </summary>
    public static class PostGround
    {
        /// <summary>The deepest a foot goes down (m): the ground further below is a valley under a bridge, not the ground the post stands on.</summary>
        public const float MaxDrop = 5f;

        /// <summary>The highest a foot goes up (m), whatever the boards' clearance.</summary>
        public const float MaxRaise = 1.5f;

        /// <summary>The highest a surface or the terrain may be above the item (m) to be the ground a post stands on: higher is a deck over it or the side of a cutting, not its ground.</summary>
        public const float MaxGroundAbove = 3f;

        /// <summary>
        /// The ground under a foot (relative to the item's height): the road's surface there when there is one within reach (a footway, a median, the deck the sign is on); else, unless the sign stands on an
        /// elevated road (then a foot off its deck is in the air over the ground below, and stays as it is), the terrain within reach. NaN: not known, the foot stays.
        /// </summary>
        public static float GroundOf(bool onSurface, float surfaceY, bool originElevated, float terrainY)
        {
            if (onSurface && !float.IsNaN(surfaceY) && surfaceY >= -MaxDrop && surfaceY <= MaxGroundAbove)
                return surfaceY;
            if (originElevated || float.IsNaN(terrainY) || float.IsInfinity(terrainY))
                return float.NaN;
            return terrainY >= -MaxDrop && terrainY <= MaxGroundAbove ? terrainY : float.NaN;
        }

        /// <summary>
        /// The tags of the vertices of a support's metal, concrete and coloured solids (null for a colour with no paint on a post's root): -1 not a foot; 2k the foot k (moves with the ground at its post);
        /// 2k + 1 the bottom of the pad of foot k (to the lowest ground under it).
        /// </summary>
        public static void Tags(SignParts parts, out int[] metal, out int[] concrete, out int[][] solids)
        {
            var made = new int[parts.Solids.Length][];
            solids = made;
            int[] TagsOf(SolidColour colour)
            {
                int[] tags = made[(int)colour];
                if (tags == null)
                {
                    made[(int)colour] = tags = new int[parts.Solids[(int)colour].VertexCount];
                    for (int i = 0; i < tags.Length; i++)
                        tags[i] = -1;
                }

                return tags;
            }

            for (int k = 0; k < parts.Feet.Count; k++)
            {
                foreach ((SolidColour colour, int from, int to) in parts.Feet[k].Paint)
                {
                    int[] tags = TagsOf(colour);
                    for (int i = from; i < to; i++)
                        tags[i] = 2 * k;
                }

                foreach ((SolidColour colour, int from, int to) in parts.Feet[k].TubeSolids)
                {
                    int[] tags = TagsOf(colour);
                    MeshBuilder mesh = parts.Solids[(int)colour];
                    for (int i = from; i < to; i++)
                    {
                        if (mesh.Positions[i].Y < parts.Feet[k].Split)
                            tags[i] = 2 * k;
                    }
                }
            }

            metal = new int[parts.Metal.VertexCount];
            concrete = new int[parts.Concrete.VertexCount];
            for (int i = 0; i < metal.Length; i++)
                metal[i] = -1;
            for (int i = 0; i < concrete.Length; i++)
                concrete[i] = -1;
            for (int k = 0; k < parts.Feet.Count; k++)
            {
                PostFoot foot = parts.Feet[k];
                for (int i = Math.Max(0, foot.MetalFrom); i < Math.Min(metal.Length, foot.MetalTo); i++)
                    metal[i] = 2 * k;
                for (int i = Math.Max(0, foot.TubeFrom); i < Math.Min(metal.Length, foot.TubeTo); i++)
                {
                    if (parts.Metal.Positions[i].Y < foot.Split)
                        metal[i] = 2 * k;
                }

                float middle = foot.PadBottom * 0.5f;
                for (int i = Math.Max(0, foot.ConcreteFrom); i < Math.Min(concrete.Length, foot.ConcreteTo); i++)
                    concrete[i] = parts.Concrete.Positions[i].Y < middle ? 2 * k + 1 : 2 * k;
            }
        }

        /// <summary>
        /// How far each foot moves (top: the foot and the tube's lower end; bottom: the pad's lower face) on a profile: to the ground at the post, no further down than MaxDrop and no further up than the foot's
        /// MaxRaise; the pad's lower face to the lowest ground under the pad as well. A foot whose ground is not known does not move.
        /// </summary>
        public static void Offsets(SignParts parts, GroundProfile profile, out float[] top, out float[] bottom)
        {
            int n = parts.Feet.Count;
            top = new float[n];
            bottom = new float[n];
            if (profile == null)
                return;
            for (int k = 0; k < n; k++)
            {
                PostFoot foot = parts.Feet[k];
                float g = profile.At(foot.X, foot.Z);
                if (float.IsNaN(g))
                    continue;
                g = Math.Max(-MaxDrop, Math.Min(g, Math.Min(MaxRaise, foot.MaxRaise)));
                float low = g;
                foreach (float dx in new[] { -1f, 1f })
                {
                    foreach (float dz in new[] { -1f, 0f, 1f })
                    {
                        float h = profile.At(foot.X + dx * foot.PadHalf, foot.Z + dz * foot.PadHalf);
                        if (!float.IsNaN(h))
                            low = Math.Min(low, Math.Max(-MaxDrop - 1f, h));
                    }
                }

                top[k] = g;
                bottom[k] = low;
            }
        }

        /// <summary>
        /// A support's mesh copied into a target (as MeshBuilder.Append: at, turned by yaw) with each foot's vertices moved up or down by its offsets; with feet, a foot's vertices no higher than its
        /// PaintTop (the paint on its post's root).
        /// </summary>
        public static void Append(MeshBuilder target, MeshBuilder source, int[] tags, float[] top, float[] bottom, Vec3 at, float yaw, List<PostFoot> feet = null)
        {
            if (tags == null || tags.Length != source.VertexCount || top == null || top.Length == 0)
            {
                target.Append(source, at, yaw);
                return;
            }

            float s = (float)Math.Sin(yaw), c = (float)Math.Cos(yaw);
            int first = target.Positions.Count;
            for (int i = 0; i < source.Positions.Count; i++)
            {
                Vec3 p = source.Positions[i];
                Vec3 n = source.Normals[i];
                int tag = tags[i];
                float dy = tag < 0 ? 0f : (tag & 1) == 0 ? top[tag >> 1] : bottom[tag >> 1];
                float y = feet != null && tag >= 0 && (tag & 1) == 0 ? Math.Min(p.Y + dy, feet[tag >> 1].PaintTop) : p.Y + dy;
                target.Positions.Add(new Vec3(p.X * c + p.Z * s + at.X, y + at.Y, -p.X * s + p.Z * c + at.Z));
                target.Normals.Add(new Vec3(n.X * c + n.Z * s, n.Y, -n.X * s + n.Z * c));
                target.U.Add(source.U[i]);
                target.V.Add(source.V[i]);
            }

            for (int i = 0; i < source.Indices.Count; i++)
                target.Indices.Add(source.Indices[i] + first);
        }

        /// <summary>
        /// Whether two sets of offsets are the same to what the eye can see (within a millimetre, the threshold of Moves): the meshes made from them are then the same, and a preview that stands on the
        /// first need not be made again for the second. The first set is the one the shown meshes were made from, so a run of small changes never adds up to a visible one.
        /// </summary>
        public static bool Same(float[] topA, float[] bottomA, float[] topB, float[] bottomB)
        {
            if (topA == null || topA.Length != topB.Length)
                return false;
            for (int k = 0; k < topA.Length; k++)
            {
                if (Math.Abs(topA[k] - topB[k]) > 1e-3f || Math.Abs(bottomA[k] - bottomB[k]) > 1e-3f)
                    return false;
            }

            return true;
        }

        /// <summary>Whether a profile moves any foot of the support (all offsets zero: the plain copy will do).</summary>
        public static bool Moves(float[] top, float[] bottom)
        {
            for (int k = 0; k < top.Length; k++)
            {
                if (Math.Abs(top[k]) > 1e-3f || Math.Abs(bottom[k]) > 1e-3f)
                    return true;
            }

            return false;
        }
    }
}
