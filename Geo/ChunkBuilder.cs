using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>One placed item as the renderer needs it.</summary>
    public struct ItemRecord
    {
        public uint TypeHash;
        public Vec3 Position;
        public float Yaw;

        /// <summary>The hash of the support style; 0 is the sign's default.</summary>
        public uint SupportHash;

        /// <summary>The written spec of a guide board (GuideSpec); null or empty for a classic sign, or a board that keeps the template's defaults.</summary>
        public string Spec;

        /// <summary>The span of a gantry or the arm of a cantilever (m); 0 is the support's default. On a wall the height of the lower edge (TsItem.Reach).</summary>
        public float Reach;

        /// <summary>Items with the same non-zero group are the boards of one support, in the order of their slots; 0 is a sign on a support of its own.</summary>
        public uint Group;
        public int Slot;

        /// <summary>The lane the board is over (counted from the driver's left; 0: side by side with the others; Assembly.OnPost: on the post) and, with a lane, the sign-frame X of its middle (m from the first post).</summary>
        public int Lane;
        public float OffsetX;

        /// <summary>How far the board is turned on its post from the way its support faces (radians; TsItem.Turn; 0.12.8).</summary>
        public float Turn;

        /// <summary>On the back of a gantry's beam (TsItem.FlagBack, 0.12.22).</summary>
        public bool Back;

        /// <summary>How far the board leans forward on an arm or a beam (radians, 0.12.55: BoardTilt, TsItem.TiltSteps).</summary>
        public float Tilt;

        /// <summary>How far the lower edge of a board on an arm or a beam is above the support's clearance (m; TsItem.Rise, 0.12.73).</summary>
        public float Rise;

        /// <summary>The height of the lower edge of the boards of a hung support as the player set it (m; TsItem.Clearance, 0.12.78); 0: the support's own.</summary>
        public float Clearance;

        /// <summary>How the support is painted (0.12.57: SupportStyle.Packed, TsItem.Style); the lead's counts for a support of several boards.</summary>
        public byte Style;

        /// <summary>The boards of this item's support are in the player's order (TsItem.FlagManual).</summary>
        public bool Manual;

        /// <summary>A marking the automatic marking placed (TsItem.FlagAuto): marking the stretch again replaces it.</summary>
        public bool Auto;

        /// <summary>Not drawn: the tool is showing the support with a sign put on it instead (the preview), and this one is hidden while it does. Not saved.</summary>
        public bool Hidden;

        /// <summary>An area marking's fit to the road under it (0.12.4: AreaFitSystem works it out from the roads); null: drawn as its outline says. Not saved.</summary>
        public AreaFit Fit;

        /// <summary>The ground along the line of the support's posts (0.12.5, PostGround: each post stands on it); null: the posts stand at the item's height. Not saved.</summary>
        public GroundProfile Ground;

        /// <summary>A marking laid on the ground as it is now (0.12.32, MarkingGround: AreaFitSystem works it out from the roads); null: drawn at the heights it was placed with. Not saved.</summary>
        public MarkingGround.Laid Laid;

        public ItemRecord(uint typeHash, Vec3 position, float yaw, uint supportHash = 0u, string spec = null, float reach = 0f, uint group = 0u, int slot = 0, int lane = 0, float offsetX = 0f)
        {
            Manual = false;
            Auto = false;
            Turn = 0f;
            Back = false;
            Tilt = 0f;
            Rise = 0f;
            Clearance = 0f;
            Style = 0;
            Hidden = false;
            Fit = null;
            Ground = null;
            Laid = null;
            Lane = lane;
            OffsetX = offsetX;
            TypeHash = typeHash;
            Position = position;
            Yaw = yaw;
            SupportHash = supportHash;
            Spec = spec;
            Reach = reach;
            Group = group;
            Slot = slot;
        }
    }

    public readonly struct TileKey : IEquatable<TileKey>
    {
        public readonly int X, Z;

        public TileKey(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(TileKey other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is TileKey other && Equals(other);
        public override int GetHashCode() => X * 73856093 ^ Z * 19349663;
        public override string ToString() => $"{X},{Z}";
    }

    public static class Tiles
    {
        /// <summary>The edge of a chunk of the map (m). Items are drawn in one mesh per tile and material.</summary>
        public const float Size = 192f;

        public static TileKey Of(float x, float z) => new TileKey((int)Math.Floor(x / Size), (int)Math.Floor(z / Size));

        /// <summary>The middle of a tile on the ground plane: the origin of its meshes, which keeps the vertex values small.</summary>
        public static Vec3 Centre(TileKey key) => new Vec3((key.X + 0.5f) * Size, 0f, (key.Z + 0.5f) * Size);
    }

    /// <summary>A mesh of one material for one tile, in the space of the tile's centre.</summary>
    public sealed class ChunkPart
    {
        public string Material;
        public MeshBuilder Mesh;
    }

    /// <summary>How much of a tile is built: the detail of the faces' pictures (FaceDetail) and of the supports.</summary>
    public struct ChunkDetail
    {
        public int FaceDetail;
        public SupportDetail Support;

        /// <summary>The level of the noise walls' geometry (BarrierLod); 0 is all of it.</summary>
        public int Barrier;

        /// <summary>Only the items that are seen from far (LongReach) are built: the horizon tier.</summary>
        public bool LongReachOnly;

        public static ChunkDetail Full => default;

        public static ChunkDetail Of(TileTier tier) =>
            new ChunkDetail { FaceDetail = Geo.FaceDetail.OfTier(tier), Support = Geo.FaceDetail.SupportOf(tier), Barrier = BarrierLod.Of(tier), LongReachOnly = tier == TileTier.Horizon };
    }

    public static class ChunkBuilder
    {
        public const string MetalMaterial = "metal";
        public const string ConcreteMaterial = "concrete";
        public const string FacePrefix = "face:";

        /// <summary>The material of a marking's paint: "marking:0" white, "marking:1" yellow, "marking:2" red, "marking:3" black (MarkingColour as a number).</summary>
        public const string MarkingPrefix = "marking:";

        /// <summary>How far above the road's surface the paint lies (m): enough that it is not lost in the road's depth, little enough that it cannot be seen to float.</summary>
        public const float MarkingRaise = 0.03f;

        public static string MarkingMaterial(MarkingColour colour) => MarkingPrefix + ((int)colour).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The colour a paint material stands for (white for a name that is not one).</summary>
        public static MarkingColour MarkingColourOf(string material)
        {
            if (material != null && material.StartsWith(MarkingPrefix, StringComparison.Ordinal) && int.TryParse(material.Substring(MarkingPrefix.Length), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n)
                && Enum.IsDefined(typeof(MarkingColour), n))
                return (MarkingColour)n;
            return MarkingColour.White;
        }

        public static bool IsMarkingMaterial(string material) => material.StartsWith(MarkingPrefix, StringComparison.Ordinal);

        /// <summary>The material of a painted solid of a road barrier: "solid:2" white ... (SolidColour as a number); steel and concrete are the signs' own.</summary>
        public const string SolidPrefix = "solid:";

        public static string SolidMaterial(SolidColour colour) => colour == SolidColour.Metal ? MetalMaterial : colour == SolidColour.Concrete ? ConcreteMaterial
            : SolidPrefix + ((int)colour).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The colour a painted solid's material stands for (white for a name that is not one).</summary>
        public static SolidColour SolidColourOf(string material)
        {
            if (material != null && material.StartsWith(SolidPrefix, StringComparison.Ordinal) && int.TryParse(material.Substring(SolidPrefix.Length), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n)
                && Enum.IsDefined(typeof(SolidColour), n))
                return (SolidColour)n;
            return SolidColour.White;
        }

        /// <summary>The material a part of a marking is drawn in: its paint, or its solid's.</summary>
        public static string PartMaterial(MarkingPart part) =>
            part.Solid.HasValue ? (part.Detail != DetailKind.None ? DetailMaterial(part.Detail, part.Solid.Value) : SolidMaterial(part.Solid.Value)) : MarkingMaterial(part.Colour);

        /// <summary>The material of a solid with baked detail (0.12.32): "detail:kind:colour", both as numbers.</summary>
        public const string DetailPrefix = "detail:";

        public static string DetailMaterial(DetailKind kind, SolidColour colour) =>
            DetailPrefix + ((int)kind).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + ((int)colour).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The kind and the colour of a detail material's name; false for a name that is not one.</summary>
        public static bool DetailOf(string material, out DetailKind kind, out SolidColour colour)
        {
            kind = DetailKind.None;
            colour = SolidColour.White;
            if (material == null || !material.StartsWith(DetailPrefix, StringComparison.Ordinal))
                return false;
            string[] parts = material.Substring(DetailPrefix.Length).Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int k)
                || !int.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int c)
                || !Enum.IsDefined(typeof(DetailKind), k) || k == 0 || !Enum.IsDefined(typeof(SolidColour), c))
                return false;
            kind = (DetailKind)k;
            colour = (SolidColour)c;
            return true;
        }

        /// <summary>The material of the lit dots of one colour (LedColour as a number): "led:1" red ... "led:5" blue.</summary>
        public const string LedPrefix = "led:";

        /// <summary>Unity's 16-bit indices: a mesh is cut before it passes this.</summary>
        public const int MaxVertices = 60000;

        /// <summary>Guide boards are many (every spec is one): the meshes of those not used for a whole generation are dropped (TwoGenCache), so at most twice this many are kept.</summary>
        private const int kMaxCachedParts = 500;

        private static readonly TwoGenCache<(string, uint, int, int), SignParts> s_Cache = new TwoGenCache<(string, uint, int, int), SignParts>(kMaxCachedParts);
        private static readonly TwoGenCache<string, SignParts> s_GroupCache = new TwoGenCache<string, SignParts>(kMaxCachedParts);

        /// <summary>The parts with more vertices than this (a text light box's letters, 0.12.75) are kept in a few places apart, so that a hundred of them are not held at once (a sign has hundreds of vertices, a text tens of thousands).</summary>
        private const int kHeavyVertices = 20000, kMaxHeavyParts = 6;

        private static readonly TwoGenCache<(string, uint, int, int), SignParts> s_HeavyCache = new TwoGenCache<(string, uint, int, int), SignParts>(kMaxHeavyParts);
        private static readonly TwoGenCache<string, SignParts> s_HeavyGroupCache = new TwoGenCache<string, SignParts>(kMaxHeavyParts);

        /// <summary>Forgets every mesh that is kept: a city is left (the memory is given back), or a test wants to start from nothing.</summary>
        public static void ClearCaches()
        {
            lock (s_Cache)
            {
                s_Cache.Clear();
                s_GroupCache.Clear();
                s_HeavyCache.Clear();
                s_HeavyGroupCache.Clear();
            }

            SignInstances.ClearCaches();
        }

        /// <summary>The meshes of a sign on a support (the sign's default for hash 0), built once and kept.</summary>
        public static SignParts PartsOf(SignDef def, uint supportHash) => PartsOf(def, null, supportHash);

        /// <summary>The meshes of a sign with its settings on a support, built once and kept.</summary>
        public static SignParts PartsOf(SignDef def, string spec, uint supportHash) => PartsOf(SignInstances.Of(def, spec), supportHash);

        public static SignParts PartsOf(SignInstance instance, uint supportHash) => PartsOf(instance, supportHash, 0f);

        /// <summary>The meshes of a sign on a support with its reach (the span of a gantry, the arm of a cantilever; 0 is the default). The reach is kept to a tenth of a metre.</summary>
        public static SignParts PartsOf(SignInstance instance, uint supportHash, float reach) => PartsOf(instance, supportHash, reach, SupportDetail.Full);

        /// <summary>
        /// The reach as a key of the parts' cache and as it is built: a tenth of a metre (a span, an arm), a millimetre on a hosted support (0.12.51: the radius of another mod's pole or arm, which the
        /// clamps fit).
        /// </summary>
        public static int ReachKey(SupportDef support, float reach, out float built)
        {
            float unit = support != null && SupportKinds.Hosted(support.Kind) ? 0.001f : 0.1f;
            int key = (int)Math.Round(Math.Max(0f, reach) / unit);
            built = key * unit;
            return key;
        }

        /// <summary>As above, in the given detail (a tile that is far away is built from the reduced one). Safe from any thread.</summary>
        public static SignParts PartsOf(SignInstance instance, uint supportHash, float reach, SupportDetail detail) => PartsOf(instance, supportHash, reach, detail, 0);

        /// <summary>As above, painted (0.12.57: SupportStyle.Packed).</summary>
        public static SignParts PartsOf(SignInstance instance, uint supportHash, float reach, SupportDetail detail, byte style)
        {
            SupportDef support = SupportCatalog.For(instance.Def, supportHash);
            int tenths = ReachKey(support, reach, out float built);
            var key = (instance.Key, support.Hash, tenths, (int)detail * 256 + style);
            lock (s_Cache)
            {
                if (s_Cache.TryGet(key, out SignParts hit) || s_HeavyCache.TryGet(key, out hit))
                    return hit;
            }

            // (built outside the lock) Building an LED screen paints its picture (a tenth of a second), and the main thread asks for the parts of a sign every frame in the tool's modes; it must not wait for a
            // worker's build. Two threads that want the same one at the same moment both build it, and the first to finish is the one that is kept.
            SignParts parts = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, support, built, detail, SupportStyle.Of(style));
            if (parts.Provisional)
                return parts;
            lock (s_Cache)
            {
                if (s_Cache.TryGet(key, out SignParts other) || s_HeavyCache.TryGet(key, out other))
                    return other;
                if (parts.VertexCount > kHeavyVertices)
                    s_HeavyCache.Set(key, parts);
                else
                    s_Cache.Set(key, parts);
                return parts;
            }
        }

        /// <summary>
        /// The meshes of the boards of one support: the members in the order of ItemGroups.Compare, the first of them the lead whose support and reach count. Null when none of them is of a type
        /// that is in the catalogue. What a hit on a board tells (PickShape.Member) is the member's place in the list.
        /// </summary>
        public static SignParts PartsOfGroup(IReadOnlyList<ItemRecord> members) => PartsOfGroup(members, SupportDetail.Full);

        public static SignParts PartsOfGroup(IReadOnlyList<ItemRecord> members, SupportDetail detail)
        {
            List<AssemblyBoard> boards = ItemGroups.Boards(members);
            if (boards.Count == 0)
                return null;
            ItemRecord lead = members[0];
            SupportDef support = SupportCatalog.ByHash(lead.SupportHash);
            int tenths = ReachKey(support, lead.Reach, out float built);
            var key = new System.Text.StringBuilder();
            foreach (AssemblyBoard board in boards)
                key.Append(board.Member).Append(':').Append(board.Slot).Append(':').Append(board.Lane).Append('@').Append((int)Math.Round(board.OffsetX * 100f)).Append(board.Manual ? 'M' : 'a').Append(board.Back ? 'B' : 'F').Append('^').Append(Assembly.TurnKey(board)).Append('/').Append(Assembly.TiltKey(board)).Append('r').Append(Assembly.RiseKey(board)).Append('c').Append(Assembly.ClearanceKey(board)).Append(':').Append(board.Instance.Key).Append('&');
            key.Append('|').Append(support == null ? 0u : support.Hash).Append('|').Append(tenths).Append('|').Append((int)detail).Append('|').Append(lead.Style);
            string text = key.ToString();
            lock (s_Cache)
            {
                if (s_GroupCache.TryGet(text, out SignParts hit) || s_HeavyGroupCache.TryGet(text, out hit))
                    return hit;
            }

            // Built outside the lock, as PartsOf does.
            SignParts parts = SignBuilder.BuildAssembly(boards, support, built, detail, SupportStyle.Of(lead.Style));
            if (parts.Provisional)
                return parts;
            lock (s_Cache)
            {
                if (s_GroupCache.TryGet(text, out SignParts other) || s_HeavyGroupCache.TryGet(text, out other))
                    return other;
                if (parts.VertexCount > kHeavyVertices)
                    s_HeavyGroupCache.Set(text, parts);
                else
                    s_GroupCache.Set(text, parts);
                return parts;
            }
        }

        /// <summary>A board alone on its support whose placement the single-board cache does not carry: over a lane, turned, on a gantry's back, at a height by hand (or moved down on a signal's arm), leaning, raised on an arm or a beam.</summary>
        private static bool OwnAssembly(in ItemRecord item) => item.Lane > 0 || item.Turn != 0f || item.Back || item.OffsetX != 0f || item.Tilt != 0f || item.Rise != 0f || item.Clearance != 0f;

        /// <summary>The meshes of one item as it stands: a board over a lane or turned on its post uses a complete one-board assembly.</summary>
        public static SignParts PartsOfRecord(ItemRecord record)
        {
            SignDef marked = SignCatalog.ByHash(record.TypeHash);
            if (marked != null && marked.IsMarking)
                return MarkingParts(marked, record);
            if (OwnAssembly(record))
                return PartsOfGroup(new[] { record });
            SignDef def = SignCatalog.ByHash(record.TypeHash);
            return def == null ? null : PartsOf(SignInstances.Of(def, record.Spec), record.SupportHash, record.Reach, SupportDetail.Full, record.Style);
        }

        /// <summary>The meshes of the items of one tile. Items of a type that is not in the catalogue (a pack that is gone) are skipped and counted.</summary>
        public static List<ChunkPart> Build(IEnumerable<ItemRecord> items, Vec3 origin, out int skipped, int maxVertices = MaxVertices) =>
            BuildDetailed(items, origin, ChunkDetail.Full, out skipped, maxVertices);

        /// <summary>
        /// As Build, at a detail: the faces' materials are named for it (FaceDetail.Material), and in reduced detail the supports are the reduced ones and there are no LED dots. The tile is the same
        /// signs in the same places. Safe from any thread (a tile is built on a worker).
        /// </summary>
        public static List<ChunkPart> BuildDetailed(IEnumerable<ItemRecord> items, Vec3 origin, ChunkDetail detail, out int skipped, int maxVertices = MaxVertices)
        {
            skipped = 0;
            var builders = new Dictionary<string, List<MeshBuilder>>();
            var groups = new Dictionary<uint, List<ItemRecord>>();
            var lonely = new List<ItemRecord>();
            foreach (ItemRecord item in items)
            {
                // Hidden while the tool shows the support with a sign on it instead: not drawn, and not skipped (nothing is wrong with it). At the horizon only what is seen from far is built.
                if (item.Hidden || (detail.LongReachOnly && !LongReach.Is(item.TypeHash)))
                    continue;

                // A road marking is paint, not a board: it has no support, no group and no lane.
                SignDef marking = SignCatalog.ByHash(item.TypeHash);
                if (marking != null && marking.IsMarking)
                {
                    if (!AddMarking(builders, item, marking, origin, maxVertices, detail.Barrier))
                        skipped++;
                    continue;
                }

                if (item.Group != 0u)
                {
                    if (!groups.TryGetValue(item.Group, out List<ItemRecord> members))
                        groups[item.Group] = members = new List<ItemRecord>();
                    members.Add(item);
                    continue;
                }

                if (OwnAssembly(item))
                {
                    // A board over a lane, turned on its post, on a gantry's back, at a height of its own or leaning (0.12.56) needs the complete one-board assembly too: the ordinary single-board cache
                    // does not carry its placement.
                    lonely.Add(item);
                    continue;
                }

                SignDef def = SignCatalog.ByHash(item.TypeHash);
                if (def == null || !item.Position.IsFinite || float.IsNaN(item.Yaw) || float.IsInfinity(item.Yaw))
                {
                    skipped++;
                    continue;
                }

                SignInstance instance = SignInstances.Of(def, item.Spec);
                SignParts parts = PartsOf(instance, item.SupportHash, item.Reach, detail.Support, item.Style);
                Vec3 at = item.Position - origin;
                AppendSupport(builders, parts, item.Ground, at, item.Yaw, maxVertices);
                foreach (FacePart face in parts.Faces)
                    Target(builders, FaceDetail.Material(face.Instance.Key, detail.FaceDetail), face.Mesh.VertexCount, maxVertices).Append(face.Mesh, at, item.Yaw);
                AddLeds(builders, parts, at, item.Yaw, maxVertices);
            }

            // The boards of one support are drawn as one: the support carries all of them.
            foreach (KeyValuePair<uint, List<ItemRecord>> pair in groups)
            {
                pair.Value.Sort(ItemGroups.Compare);
                if (!AddMembers(pair.Value, builders, origin, maxVertices, detail))
                    skipped += pair.Value.Count;
            }

            foreach (ItemRecord one in lonely)
            {
                if (!AddMembers(new List<ItemRecord> { one }, builders, origin, maxVertices, detail))
                    skipped++;
            }

            var result = new List<ChunkPart>();
            foreach (KeyValuePair<string, List<MeshBuilder>> pair in builders)
            {
                foreach (MeshBuilder mesh in pair.Value)
                    result.Add(new ChunkPart { Material = pair.Key, Mesh = mesh });
            }

            return result;
        }

        /// <summary>Adds the meshes of one support with its boards (the members in the order of ItemGroups.Compare, the first the lead); false when it cannot be drawn (the lead is not a place, or no board is of a known type).</summary>
        private static bool AddMembers(List<ItemRecord> members, Dictionary<string, List<MeshBuilder>> builders, Vec3 origin, int maxVertices, ChunkDetail detail)
        {
            ItemRecord lead = members[0];
            SignParts parts = lead.Position.IsFinite && !float.IsNaN(lead.Yaw) && !float.IsInfinity(lead.Yaw) ? PartsOfGroup(members, detail.Support) : null;
            if (parts == null)
                return false;

            Vec3 at = lead.Position - origin;
            AppendSupport(builders, parts, lead.Ground, at, lead.Yaw, maxVertices);
            foreach (FacePart face in parts.Faces)
                Target(builders, FaceDetail.Material(face.Instance.Key, detail.FaceDetail), face.Mesh.VertexCount, maxVertices).Append(face.Mesh, at, lead.Yaw);
            AddLeds(builders, parts, at, lead.Yaw, maxVertices);
            return true;
        }

        /// <summary>
        /// The metal, the concrete and the painted solids of a support (an electric box's body, black backs, the paint on the posts' root) into the tile, its posts stood on the ground of the profile
        /// (PostGround) when there is one: PostGround.Append copies a mesh as it is when nothing of it moves.
        /// </summary>
        private static void AppendSupport(Dictionary<string, List<MeshBuilder>> builders, SignParts parts, GroundProfile ground, Vec3 at, float yaw, int maxVertices)
        {
            float[] top = null, bottom = null;
            int[] metalTags = null, concreteTags = null;
            int[][] solidTags = null;
            if (ground != null && parts.Feet.Count > 0)
            {
                PostGround.Offsets(parts, ground, out top, out bottom);
                if (parts.Stand != null)
                    Signs.StandBoxes.NoteFeet(parts.Stand, top);
                if (PostGround.Moves(top, bottom))
                    parts.FootTags(out metalTags, out concreteTags, out solidTags);
            }

            PostGround.Append(Target(builders, SolidMaterial(parts.MetalColour), parts.Metal.VertexCount, maxVertices), parts.Metal, metalTags, top, bottom, at, yaw);
            if (parts.Concrete.VertexCount > 0)
                PostGround.Append(Target(builders, ConcreteMaterial, parts.Concrete.VertexCount, maxVertices), parts.Concrete, concreteTags, top, bottom, at, yaw);
            for (int colour = 0; colour < parts.Solids.Length; colour++)
            {
                MeshBuilder solid = parts.Solids[colour];
                if (solid != null && solid.VertexCount > 0)
                    PostGround.Append(Target(builders, SolidMaterial((SolidColour)colour), solid.VertexCount, maxVertices), solid, solidTags?[colour], top, bottom, at, yaw, parts.Feet);
            }
        }

        /// <summary>The paint of a marking into the tile: a mesh for each colour, lifted by MarkingRaise above the item's height (laid on the ground when it is, Laid). False when it has no position or no paint.</summary>
        private static bool AddMarking(Dictionary<string, List<MeshBuilder>> builders, ItemRecord item, SignDef def, Vec3 origin, int maxVertices, int level)
        {
            if (!item.Position.IsFinite || float.IsNaN(item.Yaw) || float.IsInfinity(item.Yaw))
                return false;
            MarkingPlan plan = MarkingTemplates.PlanOf(def, item.Laid != null ? item.Laid.Spec : item.Spec, level);
            if (plan == null)
                return false;
            // An area that clears the game's markings has an outline and no paint, and a stretch's record (0.12.44) only keeps settings: nothing to draw, and nothing wrong.
            if (plan.Parts.Count == 0)
                return plan.Outlines.Count > 0 || def.Id == AutoMarking.Id;
            Vec3 placed = item.Position - origin + new Vec3(0f, item.Laid != null ? item.Laid.Dy : 0f, 0f);
            Vec3 at = placed + new Vec3(0f, MarkingRaise, 0f);
            foreach (MarkingPart part in plan.Parts)
            {
                // A barrier's solid stands on the road; an area fitted to the road has its paint cut to the surface that can be painted and laid on it (AreaFit).
                if (part.Solid.HasValue)
                {
                    if (part.Mesh.VertexCount > 0)
                        Target(builders, PartMaterial(part), part.Mesh.VertexCount, maxVertices).Append(part.Mesh, placed, item.Yaw);
                    continue;
                }

                MeshBuilder mesh = item.Fit != null ? item.Fit.DrapedOf(part.Mesh) : item.Laid?.Cut != null ? item.Laid.Cut.DrapedNearOf(part.Mesh, out _) : part.Mesh;
                if (mesh.VertexCount == 0)
                    continue;
                MeshBuilder target = Target(builders, MarkingMaterial(part.Colour), mesh.VertexCount, maxVertices);
                int first = target.VertexCount;
                target.Append(mesh, at, item.Yaw);
                PaintUv(target, first, origin);
            }

            return true;
        }

        /// <summary>
        /// The texture coordinates of paint from the vertex first on (0.12.40, DetailKind.RoadPaint): the place in the world (origin: the tile's) in tiles of the paint's surface, seen from above, or
        /// from the side for paint on an upright face (a kerb's), so that the paint's stones, dirt and wear go on across the markings and the tiles of the map.
        /// </summary>
        public static void PaintUv(MeshBuilder mesh, int first, Vec3 origin)
        {
            (float su, float sv) = DetailSurfaces.TileOf(DetailKind.RoadPaint);
            for (int i = first; i < mesh.VertexCount; i++)
            {
                Vec3 p = mesh.Positions[i] + origin, n = mesh.Normals[i];
                bool flat = Math.Abs(n.Y) >= 0.5f;
                mesh.U[i] = (flat || Math.Abs(n.Z) >= Math.Abs(n.X) ? p.X : p.Z) / su;
                mesh.V[i] = (flat ? p.Z : p.Y) / sv;
            }
        }

        /// <summary>What can be pointed at on a marking: boxes over the cells of its paint (so that the empty road inside a long curved run is not picked), in the item's frame.</summary>
        internal static SignParts MarkingParts(SignDef def, ItemRecord record)
        {
            MarkingPlan plan = MarkingTemplates.PlanOf(def, record.Spec);
            return plan == null ? new SignParts() : plan.CachedPickParts();
        }

        /// <summary>The lit dots of the LED screens of a support, a part for each colour.</summary>
        private static void AddLeds(Dictionary<string, List<MeshBuilder>> builders, SignParts parts, Vec3 at, float yaw, int maxVertices)
        {
            for (int colour = 1; colour < parts.Leds.Length; colour++)
            {
                MeshBuilder dots = parts.Leds[colour];
                if (dots != null && dots.VertexCount > 0)
                    Target(builders, LedPrefix + colour, dots.VertexCount, maxVertices).Append(dots, at, yaw);
            }
        }

        private static MeshBuilder Target(Dictionary<string, List<MeshBuilder>> builders, string material, int add, int maxVertices)
        {
            if (!builders.TryGetValue(material, out List<MeshBuilder> list))
                builders[material] = list = new List<MeshBuilder>();
            if (list.Count == 0 || list[list.Count - 1].VertexCount + add > maxVertices)
                list.Add(new MeshBuilder());
            return list[list.Count - 1];
        }
    }
}
