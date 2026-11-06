using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>The meshes of one sign on one support, in the sign's frame: the first post stands on the origin (the cantilever's post too), +Z is the way the sign faces.</summary>
    public sealed class SignParts
    {
        /// <summary>Galvanised steel: posts, arms, flanges, bolts, rails, clamps, the back and the edge of the board.</summary>
        public readonly MeshBuilder Metal = new MeshBuilder();

        /// <summary>The concrete of the foundation, as much of it as shows above the ground.</summary>
        public readonly MeshBuilder Concrete = new MeshBuilder();

        /// <summary>The lit dots of the LED screens on the support, a mesh for each colour (indexed by LedColour; null when there is none): small quads in front of the screens' faces.</summary>
        public readonly MeshBuilder[] Leds = new MeshBuilder[6];

        /// <summary>The front of each board on the support, in the order they hang (the top or left one first), each with its own picture.</summary>
        public readonly List<FacePart> Faces = new List<FacePart>();

        /// <summary>Painted solids (0.12.23: the body of an electric box, its hood and handle), a mesh for each colour (indexed by SolidColour; null when there is none).</summary>
        public readonly MeshBuilder[] Solids = new MeshBuilder[SolidCount];

        /// <summary>The size of the arrays indexed by SolidColour.</summary>
        public const int SolidCount = 39;

        /// <summary>How the support is painted (0.12.57): its steel is drawn in MetalColour, the boards' backs apart when they are black on another steel (SupportStyle.BlackBacks).</summary>
        public SupportStyle Style;

        /// <summary>Built without something that was not ready (a text's plan, which a worker is making): shown now, not kept, and built again when it is (TextPlans.Epoch).</summary>
        public bool Provisional;

        /// <summary>The frame of a standing light box (0.12.76, StandPlanner) when the board on this support is one and the support is a post kind: BuildStandFrame makes the posts and braces from it; null otherwise.</summary>
        public StandPlan Stand;

        /// <summary>How many ties from above a text leaned on a wall was built with (TextTilt.WallTies): what the builder chose, for the tests.</summary>
        internal int WallTieCount;
        /// <summary>The text boards whose own channels are built (SignBuilder.AddTextCarrier): the support's generic ones are not, for those.</summary>
        internal HashSet<Placement> TextOwns;

        public SolidColour MetalColour => Style.MetalColour;

        internal MeshBuilder Solid(SolidColour colour) => Solids[(int)colour] ?? (Solids[(int)colour] = new MeshBuilder());

        /// <summary>The front of the first board (the only one on a support with one), with the picture of the sign.</summary>
        public MeshBuilder Face => Faces.Count > 0 ? Faces[0].Mesh : s_NoFace;

        private static readonly MeshBuilder s_NoFace = new MeshBuilder();

        /// <summary>Adds the face of a board; its triangles go to the mesh that is returned.</summary>
        internal MeshBuilder AddFace(SignInstance instance)
        {
            var part = new FacePart { Instance = instance, Mesh = new MeshBuilder() };
            Faces.Add(part);
            return part.Mesh;
        }

        /// <summary>The vertices of the lit dots of the LED screens (4 for each lit dot).</summary>
        public int LedVertexCount
        {
            get
            {
                int count = 0;
                foreach (MeshBuilder dots in Leds)
                    count += dots == null ? 0 : dots.VertexCount;
                return count;
            }
        }

        /// <summary>The box round all the faces (local frame); zero when there are none.</summary>
        public void FaceBounds(out Vec3 min, out Vec3 max)
        {
            min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (FacePart face in Faces)
            {
                if (face.Mesh.VertexCount == 0)
                    continue;
                face.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
                min = new Vec3(Math.Min(min.X, lo.X), Math.Min(min.Y, lo.Y), Math.Min(min.Z, lo.Z));
                max = new Vec3(Math.Max(max.X, hi.X), Math.Max(max.Y, hi.Y), Math.Max(max.Z, hi.Z));
            }

            if (min.X > max.X)
            {
                min = Vec3.Zero;
                max = Vec3.Zero;
            }
        }

        public SupportLayout Layout;

        /// <summary>The feet of the support's posts (0.12.5: PostGround stands them on the ground under each); none for a support on a wall or on a host.</summary>
        public readonly List<PostFoot> Feet = new List<PostFoot>();

        private int[] m_MetalTags, m_ConcreteTags;
        private int[][] m_SolidTags;

        /// <summary>Which vertices of the metal, the concrete and the coloured solids are which foot (PostGround.Tags), made the first time they are asked for. Safe from any thread.</summary>
        public void FootTags(out int[] metal, out int[] concrete, out int[][] solids)
        {
            lock (Feet)
            {
                if (m_MetalTags == null)
                    PostGround.Tags(this, out m_MetalTags, out m_ConcreteTags, out m_SolidTags);
                metal = m_MetalTags;
                concrete = m_ConcreteTags;
                solids = m_SolidTags;
            }
        }

        /// <summary>What can be pointed at (the board, each post, the foot, the arm), in the sign's frame; see Picking.</summary>
        public readonly List<PickShape> Picks = new List<PickShape>();

        /// <summary>The boards that turn on their post (0.12.8, Assembly.TurnAxis), while the support is built: what was built for each, turned about the post once all of it is there. Null when none turns.</summary>
        internal Dictionary<Placement, TurnedBoard> Turning;

        /// <summary>The box around everything (local frame).</summary>
        public Vec3 Min, Max;

        public int VertexCount
        {
            get
            {
                int count = Metal.VertexCount + Concrete.VertexCount + LedVertexCount;
                foreach (FacePart face in Faces)
                    count += face.Mesh.VertexCount;
                foreach (MeshBuilder solid in Solids)
                    count += solid == null ? 0 : solid.VertexCount;
                return count;
            }
        }
    }

    /// <summary>
    /// A board that turns on its post (0.12.8): the vertices of it in each mesh (its face, its back and edge, its channels and clamps, its LED dots) and its picks, which are turned about the post's
    /// axis (sign-frame X AxisX, z = 0, upright) by Turn when the support is built.
    /// </summary>
    internal sealed class TurnedBoard
    {
        /// <summary>The lean (when lean) then the turn of the vertices from from to to.</summary>
        private void Move(MeshBuilder mesh, int from, int to, bool lean)
        {
            float s = (float)Math.Sin(Turn), c = (float)Math.Cos(Turn), ls = lean ? (float)Math.Sin(Tilt) : 0f, lc = lean ? (float)Math.Cos(Tilt) : 1f;
            for (int i = from; i < to; i++)
            {
                Vec3 p = mesh.Positions[i], n = mesh.Normals[i];
                if (lean)
                {
                    float dy = p.Y - TiltY, dz = p.Z - TiltZ;
                    p = new Vec3(p.X, TiltY + dy * lc - dz * ls, TiltZ + dy * ls + dz * lc);
                    n = new Vec3(n.X, n.Y * lc - n.Z * ls, n.Y * ls + n.Z * lc);
                }

                float x = p.X - AxisX, z = p.Z - AxisZ;
                mesh.Positions[i] = new Vec3(AxisX + x * c + z * s, p.Y, AxisZ - x * s + z * c);
                mesh.Normals[i] = new Vec3(n.X * c + n.Z * s, n.Y, -n.X * s + n.Z * c);
            }
        }

        public float AxisX, AxisZ, Turn;

        /// <summary>
        /// 0.12.55: how far the board leans forward (radians, BoardTilt) about the line along X at TiltY, TiltZ (its lowest tube's height, its channels' back: HangOnTubes), before the turn. The
        /// Upright ranges (the clamps round the tube) are turned with the board but do not lean.
        /// </summary>
        public float Tilt, TiltY, TiltZ;

        public readonly List<(MeshBuilder Mesh, int From, int To)> Ranges = new List<(MeshBuilder Mesh, int From, int To)>();
        public readonly List<(MeshBuilder Mesh, int From, int To)> Upright = new List<(MeshBuilder Mesh, int From, int To)>();
        public int PickFrom, PickTo;

        /// <summary>The vertices of the mesh from the one at from to its end are this board's.</summary>
        public void Since(MeshBuilder mesh, int from)
        {
            if (mesh != null && mesh.VertexCount > from)
                Ranges.Add((mesh, from, mesh.VertexCount));
        }

        /// <summary>As Since, for what turns with the board but does not lean.</summary>
        public void SinceUpright(MeshBuilder mesh, int from)
        {
            if (mesh != null && mesh.VertexCount > from)
                Upright.Add((mesh, from, mesh.VertexCount));
        }

        public void Apply(List<PickShape> picks)
        {
            foreach ((MeshBuilder mesh, int from, int to) in Ranges)
                Move(mesh, from, to, true);
            foreach ((MeshBuilder mesh, int from, int to) in Upright)
                Move(mesh, from, to, false);

            for (int i = PickFrom; i < PickTo && i < picks.Count; i++)
            {
                picks[i].Turn = Turn;
                picks[i].AxisX = AxisX;
                picks[i].AxisZ = AxisZ;
                picks[i].Tilt = Tilt;
                picks[i].TiltY = TiltY;
                picks[i].TiltZ = TiltZ;
            }
        }
    }

    /// <summary>The front of one board: its mesh, textured with the picture of the instance (Instance.Key names that picture).</summary>
    public sealed class FacePart
    {
        public SignInstance Instance;
        public MeshBuilder Mesh;
    }

    /// <summary>How much of a support is built. Reduced is for tiles that are far away: no LED dots, tubes of six sides at most, and none of the hardware that cannot be seen from there (studs, nuts, ribs, the collars of the clamps, the web of a truss).</summary>
    public enum SupportDetail
    {
        Full,
        Reduced
    }

    public static partial class SignBuilder
    {
        /// <summary>Set on this thread while a support is built in reduced detail.</summary>
        [ThreadStatic]
        private static bool t_Reduced;

        private const int PoleSides = 12;

        /// <summary>One sign on its default support.</summary>
        public static SignParts Build(SignDef def) => Build(def, null);

        /// <summary>One sign (a guide board with its default settings) on a support (null: the sign's default).</summary>
        public static SignParts Build(SignDef def, SupportDef support) => Build(SignInstances.Of(def, null), support);

        /// <summary>One sign as it is drawn on a support (null: the planner's choice for the board).</summary>
        public static SignParts Build(SignInstance instance, SupportDef support) => Build(instance, support, 0f);

        /// <summary>As above, with the reach of the support: the span of a gantry or the arm of a cantilever (m); 0 is the default.</summary>
        public static SignParts Build(SignInstance instance, SupportDef support, float reach) => Build(instance, support, reach, SupportDetail.Full);

        public static SignParts Build(SignInstance instance, SupportDef support, float reach, SupportDetail detail) =>
            BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance, Slot = 0, Member = 0 } }, support, reach, detail);

        /// <summary>
        /// One support with up to four boards (Assembly): the boards are put in order (4.8.6), the support is laid out for what they make together (the widest of them and the height of the
        /// stack on posts; all of them side by side on an arm or a beam), and each board hangs where Assembly.Arrange puts it. The planner's support (null) is the one the whole makes.
        /// </summary>
        public static SignParts BuildAssembly(IEnumerable<AssemblyBoard> boards, SupportDef support, float reach) => BuildAssembly(boards, support, reach, SupportDetail.Full);

        /// <summary>As above, in the given detail: the faces, the picks and the places of everything are the same, so a sign drawn in reduced detail stands where the full one does.</summary>
        public static SignParts BuildAssembly(IEnumerable<AssemblyBoard> boards, SupportDef support, float reach, SupportDetail detail) => BuildAssembly(boards, support, reach, detail, default);

        /// <summary>As above, painted (0.12.57, SupportStyle).</summary>
        public static SignParts BuildAssembly(IEnumerable<AssemblyBoard> boards, SupportDef support, float reach, SupportDetail detail, SupportStyle style)
        {
            bool before = t_Reduced;
            int segmentsBefore = Solids.MaxSegments;
            t_Reduced = detail == SupportDetail.Reduced;
            Solids.MaxSegments = t_Reduced ? 6 : 0;
            try
            {
                return BuildAssemblyCore(boards, support, reach, style);
            }
            finally
            {
                t_Reduced = before;
                Solids.MaxSegments = segmentsBefore;
            }
        }

        private static SignParts BuildAssemblyCore(IEnumerable<AssemblyBoard> boards, SupportDef support, float reach, SupportStyle style)
        {
            List<AssemblyBoard> ordered = Assembly.Order(boards);
            AssemblyPlan plan = Assembly.Plan(ordered, support, reach);
            support = plan.Support;
            SupportLayout layout = plan.Layout;
            List<Placement> placed = plan.Placed;
            var parts = new SignParts { Layout = layout, Style = style };
            foreach (Placement placement in placed)
            {
                // A board in the stack on one post may be turned about it (0.12.8); on a hung support only those on its post.
                bool onOnePost = !SupportKinds.Hung(support.Kind) || placement.OnPost;
                if (Math.Abs(placement.Board.Turn) > 1e-4f && onOnePost && Assembly.TurnAxis(support.Kind, layout, placement.Board, out float axisX))
                {
                    parts.Turning = parts.Turning ?? new Dictionary<Placement, TurnedBoard>();
                    parts.Turning[placement] = new TurnedBoard { AxisX = axisX, Turn = Assembly.WrapTurn(placement.Board.Turn) };
                }
                else if (placement.Back)
                {
                    // On the back of a gantry's beam (0.12.22): a front board half round about its own middle, so that it faces the other way at the same place along the beam; on a space truss about the
                    // truss's middle, onto its back chords (0.12.53).
                    parts.Turning = parts.Turning ?? new Dictionary<Placement, TurnedBoard>();
                    parts.Turning[placement] = new TurnedBoard { AxisX = placement.Cx, AxisZ = -layout.BeamWidth * 0.5f, Turn = (float)Math.PI };
                }

                // A board on an arm or a beam leans forward (0.12.55, BoardTilt) about its lowest tube (HangOnTubes says where); a back board leans in its front frame and is turned with it.
                if (placement.Board.Tilt > 1e-4f && !placement.OnPost && BoardTilt.Leans(support))
                {
                    parts.Turning = parts.Turning ?? new Dictionary<Placement, TurnedBoard>();
                    if (!parts.Turning.TryGetValue(placement, out TurnedBoard turned))
                        parts.Turning[placement] = turned = new TurnedBoard();
                    turned.Tilt = placement.Board.Tilt;
                }
            }

            foreach (Placement placement in placed)
                AddBoard(parts, layout, placement);

            if (support.Kind == SupportKind.Gantry)
                BuildGantry(layout, parts, placed);
            else if (SupportKinds.Arm(support.Kind))
                BuildCantilever(layout, parts, placed, support.Kind == SupportKind.DoubleCantilever);
            else if (layout.Hanger)
                BuildOverHung(layout, parts, placed);
            else if (support.Kind == SupportKind.WallFlat || support.Kind == SupportKind.OverRoad)
                BuildWallFlat(layout, parts, placed);
            else if (support.Kind == SupportKind.WallBracket)
                BuildWallBracket(layout, parts, placed, support.Side < 0 ? -1f : 1f);
            else if (support.Kind == SupportKind.AttachedArm)
                BuildOnArm(layout, parts, placed);
            else if (parts.Stand != null)
                BuildStandFrame(layout, parts);
            else
                BuildPoles(layout, parts, placed, support.Kind == SupportKind.Attached);

            if (parts.Turning != null)
            {
                foreach (TurnedBoard turned in parts.Turning.Values)
                    turned.Apply(parts.Picks);
                parts.Turning = null;
            }

            var all = new List<MeshBuilder> { parts.Metal, parts.Concrete };
            foreach (FacePart face in parts.Faces)
                all.Add(face.Mesh);
            foreach (MeshBuilder solid in parts.Solids)
            {
                if (solid != null)
                    all.Add(solid);
            }
            parts.Min = new Vec3(float.MaxValue, float.MaxValue, float.MaxValue);
            parts.Max = new Vec3(float.MinValue, float.MinValue, float.MinValue);
            foreach (MeshBuilder mesh in all)
            {
                if (mesh.VertexCount == 0)
                    continue;
                mesh.Bounds(out Vec3 min, out Vec3 max);
                parts.Min = new Vec3(Math.Min(parts.Min.X, min.X), Math.Min(parts.Min.Y, min.Y), Math.Min(parts.Min.Z, min.Z));
                parts.Max = new Vec3(Math.Max(parts.Max.X, max.X), Math.Max(parts.Max.Y, max.Y), Math.Max(parts.Max.Z, max.Z));
            }

            return parts;
        }

        /// <summary>
        /// The plane of a board's face (z in the sign's frame). A plate (every board but an LED screen and an electric box) has the layout's front plane, a folded edge thick; an LED screen is a
        /// cabinet, its face Led.CabinetDepth in front of its back, an electric box's door its depth in front of it; the back is at the layout's back plane as that of any board (the channels and the
        /// clamps are fixed there).
        /// </summary>
        public static float FacePlane(SupportLayout layout, SignInstance instance) => FacePlane(layout.BackZ, instance);

        /// <summary>The plane of a board's face as it is placed: a board on the post of a hung support has its back at the layout's PostBackZ, the others at its BackZ.</summary>
        public static float FacePlaneOf(SupportLayout layout, Placement placement) => FacePlane(BackOf(layout, placement), placement.Board.Instance);

        /// <summary>The plane of the back of a board as it is placed.</summary>
        public static float BackOf(SupportLayout layout, Placement placement) => placement.OnPost ? layout.PostBackZ : layout.BackZ;

        private static float FacePlane(float backZ, SignInstance instance) =>
            instance?.Board?.Box != null ? backZ + instance.Board.Box.Depth
            : instance?.Board?.Text != null ? backZ + instance.Board.Text.Reach
            : instance != null && instance.IsLed ? backZ + Led.CabinetDepth(instance.Board.Main.W, instance.Board.Main.H) : backZ + SupportLayouts.BoardThickness;

        /// <summary>How far an electric box's rain hood reaches in front of its door, and past its sides, and how thick it is (m).</summary>
        public const float HoodReach = 0.035f, HoodSide = 0.012f, HoodThickness = 0.008f;

        /// <summary>A board's pick: a plate at the middle of its thickness; a cabinet (reach: what stands in front of its face) at its face, where the player points, its box from the back to that.</summary>
        private static PickShape BoardPick(List<Point2> outline, float centreX, float centreY, float width, float height, float backZ, int member, float faceZ, bool cabinet, float reach = 0f) => new PickShape
        {
            Name = "board",
            Member = member,
            Outline = outline.ToArray(),
            CentreX = centreX,
            CentreY = centreY,
            PlaneZ = cabinet ? faceZ : (backZ + faceZ) * 0.5f,
            Min = new Vec3(centreX - width * 0.5f, centreY - height * 0.5f, backZ),
            Max = new Vec3(centreX + width * 0.5f, centreY + height * 0.5f, cabinet ? faceZ + reach : faceZ)
        };

        /// <summary>
        /// The body of an electric box behind its door (cx, cy: the door's middle in the sign's frame): its back and sides in its colour (made by the caller), the rain hood over the door and the sides,
        /// louvers in the sides and the handle that stands out of the door. A far tile has the body and the hood, not the small parts.
        /// </summary>
        private static void AddBoxBody(SignParts parts, BoxBody box, GuidePart door, float cx, float cy, float backZ, float faceZ)
        {
            MeshBuilder body = parts.Solid(box.Colour);
            float halfW = door.W * 0.5f, top = cy + door.H * 0.5f;
            if (box.Hood)
                Solids.Box(body, new Vec3(cx, top + HoodThickness * 0.5f, (backZ + faceZ + HoodReach) * 0.5f), new Vec3(halfW + HoodSide, HoodThickness * 0.5f, (faceZ + HoodReach - backZ) * 0.5f));
            if (t_Reduced)
                return;

            MeshBuilder dark = parts.Solid(SolidColour.Black);
            if (box.SideLouvers)
            {
                // Slats in the lower part of each side, a little proud of it, as pressed louvers look from the road.
                float depth = faceZ - backZ, length = depth * 0.6f, pitch = Math.Min(0.025f, door.H * 0.05f);
                int count = Math.Max(3, Math.Min(8, (int)(door.H * 0.3f / pitch)));
                float y0 = cy - door.H * 0.5f + door.H * 0.12f;
                foreach (float side in new[] { -1f, 1f })
                {
                    for (int i = 0; i < count; i++)
                        Solids.Box(dark, new Vec3(cx + side * (halfW + 0.002f), y0 + i * pitch, (backZ + faceZ) * 0.5f), new Vec3(0.002f, pitch * 0.2f, length * 0.5f));
                }
            }

            if (box.HandleW > 0f)
            {
                // The door's frame is the viewer's: its x runs to the viewer's right, which is -X of the sign's frame.
                float hx = cx + halfW - (box.HandleX + box.HandleW * 0.5f), hy = top - (box.HandleY + box.HandleH * 0.5f);
                Solids.Box(dark, new Vec3(hx, hy, faceZ + 0.008f), new Vec3(box.HandleW * 0.5f, box.HandleH * 0.5f, 0.008f));
            }
        }

        /// <summary>
        /// The faces of one board on the support: its main board at the placement and any plates fixed to it (the exit number plate on top). All share one picture that covers the box round them; a part
        /// is placed by where its box is in the board's layout (x to the viewer's right, y down from the main board's top left corner), which in the sign's frame is -X and up.
        /// </summary>
        /// <summary>
        /// Where the dots of an LED screen come from (the grid of an instance), set by the game's renderer (which paints the picture the dots are read from) and by the tests. Null: no dots, only the
        /// picture of the screen's face.
        /// </summary>
        public static Func<SignInstance, LedGrid> LedGrids;

        /// <summary>The frame of an LED screen: four bars round the edge of the face, BezelWidth wide and standing BezelReach in front of it (the top and bottom bars the whole width, the sides between them).</summary>
        private static void AddBezel(MeshBuilder metal, float cx, float cy, float width, float height, float faceZ)
        {
            float w = Led.BezelWidth, reach = Led.BezelReach, zMid = faceZ + reach * 0.5f;
            foreach (float k in new[] { -1f, 1f })
            {
                Solids.Box(metal, new Vec3(cx, cy + k * (height * 0.5f - w * 0.5f), zMid), new Vec3(width * 0.5f, w * 0.5f, reach * 0.5f));
                Solids.Box(metal, new Vec3(cx + k * (width * 0.5f - w * 0.5f), cy, zMid), new Vec3(w * 0.5f, height * 0.5f - w, reach * 0.5f));
            }
        }

        /// <summary>The board that turns on its post, of what is being built (null: it faces as the support does).</summary>
        private static TurnedBoard TurnedOf(SignParts parts, Placement placement) => parts.Turning != null && parts.Turning.TryGetValue(placement, out TurnedBoard turned) ? turned : null;

        private static void AddBoard(SignParts parts, SupportLayout layout, Placement placement)
        {
            TurnedBoard turned = TurnedOf(parts, placement);
            if (turned == null)
            {
                AddBoardCore(parts, layout, placement);
                return;
            }

            int metal = parts.Metal.VertexCount, picks = parts.Picks.Count, faces = parts.Faces.Count;
            var leds = new int[parts.Leds.Length];
            for (int i = 0; i < leds.Length; i++)
                leds[i] = parts.Leds[i] == null ? 0 : parts.Leds[i].VertexCount;
            var solids = new int[parts.Solids.Length];
            for (int i = 0; i < solids.Length; i++)
                solids[i] = parts.Solids[i] == null ? 0 : parts.Solids[i].VertexCount;
            AddBoardCore(parts, layout, placement);
            turned.Since(parts.Metal, metal);
            for (int i = faces; i < parts.Faces.Count; i++)
                turned.Since(parts.Faces[i].Mesh, 0);
            for (int i = 0; i < leds.Length; i++)
                turned.Since(parts.Leds[i], leds[i]);
            for (int i = 0; i < solids.Length; i++)
                turned.Since(parts.Solids[i], solids[i]);
            turned.PickFrom = picks;
            turned.PickTo = parts.Picks.Count;
        }

        /// <summary>Where a board's back and edge are built: the steel, or black when the support's style says so (0.12.57).</summary>
        private static MeshBuilder BackOf(SignParts parts) => (parts.Style.BlackBacks || parts.Layout.Framed) && parts.MetalColour != SolidColour.Graphite ? parts.Solid(SolidColour.Graphite) : parts.Metal;

        private static void AddBoardCore(SignParts parts, SupportLayout layout, Placement placement)
        {
            SignInstance instance = placement.Board.Instance;
            if (instance.Board?.PoleAd != null)
            {
                AddPoleAd(parts, layout, placement);
                return;
            }

            if (instance.Board?.Stand != null)
            {
                AddStandBoard(parts, layout, placement);
                return;
            }

            if (instance.Board?.Text != null)
            {
                AddTextBoard(parts, layout, placement);
                return;
            }

            if (placement.OnHead)
            {
                AddPlateBoard(parts, layout, placement);
                return;
            }

            SignDef def = instance.Def;
            int member = placement.Board.Member;
            MeshBuilder face = parts.AddFace(instance);
            // A board on the post of a hung support stands further forward than the ones on the arm: in front of the post's foot.
            float backZ = BackOf(layout, placement), frontZ = backZ + SupportLayouts.BoardThickness;
            if (instance.Board == null)
            {
                List<Point2> outline = SignCatalog.Outline(def);
                Prims.PolygonFace(face, outline, frontZ, true, def.Width, def.Height, placement.Cy, placement.Cx);
                Prims.PolygonFace(BackOf(parts), outline, backZ, false, def.Width, def.Height, placement.Cy, placement.Cx);
                if (!t_Reduced)
                    Prims.Rim(BackOf(parts), outline, backZ, frontZ, placement.Cy, placement.Cx);
                parts.Picks.Add(BoardPick(outline, placement.Cx, placement.Cy, def.Width, def.Height, backZ, member, frontZ, false));
                return;
            }

            GuideBoard board = instance.Board;
            GuidePart main = board.Main;
            float boxW = instance.BoundsX1 - instance.BoundsX0, boxH = instance.BoundsY1 - instance.BoundsY0;
            float faceZ = FacePlane(backZ, instance);
            for (int i = 0; i < board.Parts.Count; i++)
            {
                GuidePart part = board.Parts[i];
                float cx = placement.Cx + (main.W * 0.5f - (part.X + part.W * 0.5f));
                float cy = placement.Cy + (main.H * 0.5f - (part.Y + part.H * 0.5f));
                List<Point2> outline = GuideKit.PartOutline(part);
                // The outline is as the viewer sees the face (x to the viewer's right, which is -X of the sign's frame): the face is mapped from it as it is, the back, the edge and the pick from its
                // mirror in the frame. (Until 0.12.2 they were built from the outline itself: the same for every outline but the pointed board of 路7 a, whose back then had its point at the other end.)
                List<Point2> inFrame = MirrorX(outline);
                float centreLayoutX = part.X + part.W * 0.5f, centreLayoutY = part.Y + part.H * 0.5f;
                float x0 = instance.BoundsX0, y0 = instance.BoundsY0;
                Func<float, float, (float u, float v)> uv = (px, py) => ((centreLayoutX + px - x0) / boxW, 1f - (centreLayoutY - py - y0) / boxH);
                // An electric box's back and sides are its painted body (and in a far tile too: they are most of what is seen of it); a plate's are its steel.
                BoxBody box = i == 0 ? board.Box : null;
                MeshBuilder back = box != null ? parts.Solid(box.Colour) : BackOf(parts);
                if (part.Cut != null)
                {
                    // A board cut to the opaque part of a player's picture (0.12.30): its face, back and edge follow the cut; the pick is its outline's.
                    Prims.LoopsFace(face, part.Cut, faceZ, true, cx, cy, uv);
                    Prims.LoopsFace(back, part.Cut, backZ, false, cx, cy, null);
                    if (!t_Reduced)
                        Prims.LoopsRim(back, part.Cut, backZ, faceZ, cx, cy);
                }
                else
                {
                    Prims.PolygonFaceMapped(face, outline, faceZ, true, cx, cy, uv);
                    Prims.PolygonFace(back, inFrame, backZ, false, part.W, part.H, cy, cx);
                    if (!t_Reduced || box != null)
                        Prims.Rim(back, inFrame, backZ, faceZ, cy, cx);
                }
                bool cabinet = i == 0 && instance.IsLed;
                parts.Picks.Add(BoardPick(inFrame, cx, cy, part.W, part.H, backZ, member, faceZ, cabinet || box != null, box != null ? (box.Hood ? HoodReach : 0.016f) : Led.BezelReach));
                if (box != null)
                    AddBoxBody(parts, box, part, cx, cy, backZ, faceZ);

                // An LED screen (variable-message sign) is a cabinet: the box above (its sides are the rim between the back and the face), a frame round the face that stands in front of it, and the
                // lit dots just in front of the face, inside the frame.
                if (cabinet)
                {
                    AddBezel(parts.Metal, cx, cy, part.W, part.H, faceZ);
                    if (LedGrids != null && !t_Reduced)
                    {
                        LedGrid grid = LedGrids(instance);
                        if (grid != null)
                            Led.AddDots(parts.Leds, grid, cx, cy, part.W, part.H, faceZ + Led.Lift);
                    }
                }

                if (i == 0)
                    AddLighting(parts, placement, backZ, faceZ);

                if (i > 0)
                {
                    // A plate fixed on top of the board: two short studs across the gap.
                    float bottom = cy - part.H * 0.5f, top = placement.Top;
                    if (bottom - top > 0.004f)
                    {
                        foreach (float k in new[] { -0.3f, 0.3f })
                            Solids.Box(parts.Metal, new Vec3(cx + k * part.W, (top + bottom) * 0.5f, backZ + SupportLayouts.BoardThickness * 0.5f), new Vec3(0.02f, (bottom - top) * 0.5f + 0.004f, SupportLayouts.BoardThickness * 0.5f));
                    }
                }
            }
        }

        // ------------------------------------------------------------------ single and double post

        private static void BuildPoles(SupportLayout layout, SignParts parts, List<Placement> placed, bool hosted)
        {
            float r = layout.PoleRadius, postTop = layout.PoleTop;

            // A hosted support (Attached) has no post of its own: the channels and the hoops round the host are all there is, and nothing of it can be pointed at but the boards.
            foreach (float px in hosted ? new float[0] : layout.PoleX)
            {
                if (layout.Head != null)
                {
                    BuildPlatePost(layout, parts, placed.Find(p => p.OnHead));
                    continue;
                }

                // The base (BasePlate.Post), the post, its cap.
                BasePlate b = layout.Base;
                float padX = b.HalfX + SupportLayouts.PedestalMargin, padZ = b.HalfZ + SupportLayouts.PedestalMargin;
                var foot = new PostFoot { X = px, Z = 0f, PadHalf = Math.Max(padX, padZ), PadBottom = PedestalBottom, MaxRaise = RaiseOf(layout), MetalFrom = parts.Metal.VertexCount, ConcreteFrom = parts.Concrete.VertexCount };
                float plateTop = AddBase(parts, b, px, 0f, r);
                foot.MetalTo = parts.Metal.VertexCount;
                foot.ConcreteTo = parts.Concrete.VertexCount;
                foot.Split = (plateTop + postTop) * 0.5f;
                foot.TubeFrom = parts.Metal.VertexCount;
                Solids.Tube(parts.Metal, new Vec3(px, plateTop, 0f), new Vec3(px, postTop, 0f), r, r, PoleSides, false, false);
                foot.TubeTo = parts.Metal.VertexCount;
                PaintRoot(parts, foot, r, r, plateTop, postTop, PoleSides, false, RoadSide.Left);
                PostCap(parts.Metal, px, 0f, postTop, r);
                parts.Feet.Add(foot);
                parts.Picks.Add(PostPick(px, 0f, r, plateTop, postTop + 0.005f));
                parts.Picks.Add(new PickShape { Name = "foot", Min = new Vec3(px - padX, PedestalBottom, -padZ), Max = new Vec3(px + padX, plateTop + 0.04f, padZ) });
            }

            // Channels on the back of each board (SupportLayouts.RailKFor), and a hoop where each meets a post. A board narrower than the posts' distance (an auxiliary board under a wide one) has
            // channels that reach the posts, like cross-arms.
            float minLength = layout.PoleX.Length > 1 ? layout.PoleX[layout.PoleX.Length - 1] - layout.PoleX[0] + 0.30f : 0.30f;
            foreach (Placement p in placed)
            {
                // A lamp-post ad has its own arms and hoops (AddPoleAd); a plate on a post's head has its head.
                if (p.Board.Instance.Board?.PoleAd != null || p.OnHead || (parts.TextOwns != null && parts.TextOwns.Contains(p)))
                    continue;
                int from = parts.Metal.VertexCount;
                foreach (float k in SupportLayouts.RailKFor(p.Def.Height))
                {
                    float y = p.Cy + k * p.Def.Height;
                    Rail(p.Def, layout, parts, p.Cx, p.Cy, y, minLength);
                    foreach (float px in layout.PoleX)
                        Hoop(parts, px, y, r, layout.BackZ - SupportLayouts.RailDepth);
                }

                // A board turned on its post takes its channels and clamps round with it.
                TurnedOf(parts, p)?.Since(parts.Metal, from);
            }
        }

        /// <summary>A stand-in for the host of an attached support (a post of the radius of SupportLayouts.HostRadius from the ground to a little above the top of the boards), for the thumbnail and the pictures: in the game the host is the game's own object.</summary>
        public static void AddHostStandIn(SignParts parts)
        {
            SupportLayout l = parts.Layout;
            if (l.Kind == SupportKind.AttachedArm)
            {
                // A signal's arm: a level tube through the clamps, half a metre past the board each side.
                float half = Math.Max(0.6f, (parts.Max.X - parts.Min.X) * 0.5f + 0.5f);
                Solids.Tube(parts.Metal, new Vec3(-half, l.HangY, 0f), new Vec3(half, l.HangY, 0f), l.HangRadius, l.HangRadius, PoleSides, true, true);
                return;
            }

            Solids.Tube(parts.Metal, new Vec3(0f, 0f, 0f), new Vec3(0f, l.PoleTop + 0.6f, 0f), l.PoleRadius, l.PoleRadius, PoleSides, true, true);
        }

        /// <summary>
        /// The boards on another mod's signal arm (0.12.51, SupportKind.AttachedArm): each on its channels, clamped round the arm that passes behind its upper part (HangOnTubes, the arm as thick as the
        /// item's reach says). The arm and its pole are the other mod's: nothing else is built.
        /// </summary>
        private static void BuildOnArm(SupportLayout layout, SignParts parts, List<Placement> placed)
        {
            foreach (Placement p in placed)
                HangOnTubes(layout, parts, p, new[] { layout.HangY }, x => layout.HangRadius);
        }

        /// <summary>How far a foot of the support may go up the ground (PostGround): its boards' lower edge (and those on its post) keep half a metre above it, and never more than PostGround.MaxRaise.</summary>
        private static float RaiseOf(SupportLayout layout)
        {
            float lowest = layout.BottomClearance;
            if (layout.PostClearance > 0f)
                lowest = Math.Min(lowest, layout.PostClearance);
            return Math.Max(0f, Math.Min(PostGround.MaxRaise, lowest - 0.5f));
        }

        /// <summary>A channel behind a board (its middle at cx, cy) at height y; returns its length. As long as the board allows (less 6 cm either side), at least minLength.</summary>
        private static float Rail(SignDef def, SupportLayout layout, SignParts parts, float cx, float cy, float y, float minLength) => Rail(def, layout.BackZ, parts, cx, cy, y, minLength);

        private static float Rail(SignDef def, float backZ, SignParts parts, float cx, float cy, float y, float minLength)
        {
            float chord = SignCatalog.ChordAt(def, y - cy);
            float length = Math.Max(chord - 0.12f, 0.20f);
            length = Math.Max(length, minLength);
            // A board that is narrower than the posts are apart is reached by a channel that is as long as it needs; one that is wide enough keeps inside it.
            if (minLength <= chord)
                length = Math.Min(length, Math.Max(chord - 0.04f, 0.20f));
            Solids.Box(parts.Metal, new Vec3(cx, y, backZ - SupportLayouts.RailDepth * 0.5f), new Vec3(length * 0.5f, SupportLayouts.RailWidth * 0.5f, SupportLayouts.RailDepth * 0.5f));
            return length;
        }

        /// <summary>
        /// The boards on the post of a hung support (0.12.2, Assembly.OnPost): the channels on the back of each, as on a single post, and a hoop round the post where each crosses it; on a space truss's
        /// H leg, the backing from its front flange to the channel.
        /// </summary>
        private static void PostBoardsOfHung(SupportLayout layout, SignParts parts, List<Placement> placed)
        {
            foreach (Placement p in placed)
            {
                if (!p.OnPost)
                    continue;
                int from = parts.Metal.VertexCount;
                PostBoardOfHung(layout, parts, p);
                TurnedOf(parts, p)?.Since(parts.Metal, from);
            }
        }

        /// <summary>
        /// The post of a hung support where its small boards go (0.12.8): from the foot to the top of the room for them under the lowest arm's or chord's joint (SupportLayout.PostTop), grown by PostPickGrow for the ray, so that
        /// pointing near the post, not only at its 0.3 m of tube, puts a board on it. Above that the post and the arm keep their own shapes.
        /// </summary>
        private static void AddPostBoardPick(SignParts parts, SupportLayout layout, float radius, float bottom)
        {
            if (layout.PostTop > bottom)
            {
                PickShape pick = PostPick(0f, 0f, radius, bottom, layout.PostTop);
                pick.Grow = PostPickGrow;
                parts.Picks.Add(pick);
            }
        }

        /// <summary>
        /// The pick box of a post (round, of radius r, or an H leg of that half width) at (x, z) from y0 to y1: 2 cm inside it on every side, so that with the pick tolerance (6 cm) it stays behind the
        /// middle of a board on it, whichever way the board is turned about it (its back 31 mm out, SupportLayouts.ClampRoom; its middle 46).
        /// </summary>
        private static PickShape PostPick(float x, float z, float r, float y0, float y1)
        {
            float h = Math.Max(0.005f, r - 0.02f);
            return new PickShape { Name = "post", Min = new Vec3(x - h, y0, z - h), Max = new Vec3(x + h, y1, z + h) };
        }

        /// <summary>How much further from the post of a hung support than from other parts a ray still picks it (m).</summary>
        public const float PostPickGrow = 0.25f;

        /// <summary>The channels and clamps of one board on the post of a hung support.</summary>
        private static void PostBoardOfHung(SupportLayout layout, SignParts parts, Placement p)
        {
            foreach (float k in SupportLayouts.RailKFor(p.Def.Height))
            {
                float y = p.Cy + k * p.Def.Height;
                Rail(p.Def, layout.PostBackZ, parts, p.Cx, p.Cy, y, 0.30f);
                float channelBack = layout.PostBackZ - SupportLayouts.RailDepth;
                if (layout.BeamWidth > 0f)
                    Solids.Box(parts.Metal, new Vec3(0f, y, (layout.PoleRadius + channelBack) * 0.5f), new Vec3(0.02f, 0.025f, (channelBack - layout.PoleRadius) * 0.5f + 0.002f));
                else
                    Hoop(parts, 0f, y, layout.PoleRadiusAt(y), channelBack, layout.PoleSides > 0 ? (float)Math.Cos(Math.PI / layout.PoleSides) : 1f, !layout.Integrated);
            }
        }

        // ------------------------------------------------------------------ on a wall

        /// <summary>
        /// Flat on a wall (0.12.2): each board's back on spacers, short tubes from the wall (the plane z = 0) to the board where its channels would be (two, three behind a tall board), two at each height
        /// (one behind a narrow place). Nothing else is built; nothing of it can be pointed at but the boards.
        /// </summary>
        private static void BuildWallFlat(SupportLayout layout, SignParts parts, List<Placement> placed)
        {
            foreach (Placement p in placed)
            {
                // A text board has its own spacers and frame (SignBuilder.AddTextRear).
                if (parts.TextOwns != null && parts.TextOwns.Contains(p))
                    continue;
                foreach (float k in SupportLayouts.RailKFor(p.Def.Height))
                {
                    float y = p.Cy + k * p.Def.Height;
                    float half = Math.Max(0f, Math.Min(HalfSpanAt(p, y - p.Cy) - 0.06f, 0.3f * p.Def.Width));
                    foreach (float sx in half > 0.05f ? new[] { -1f, 1f } : new[] { 0f })
                        Solids.Tube(parts.Metal, new Vec3(p.Cx + sx * half, y, 0f), new Vec3(p.Cx + sx * half, y, layout.BackZ + 0.002f), 0.012f, 0.012f, 8, false, false);
                }
            }
        }

        /// <summary>An outline turned left for right (x to -x), in the same order of turning.</summary>
        private static List<Point2> MirrorX(List<Point2> outline)
        {
            var mirrored = new List<Point2>(outline.Count);
            for (int i = outline.Count - 1; i >= 0; i--)
                mirrored.Add(new Point2(-outline[i].X, outline[i].Y));
            return mirrored;
        }

        /// <summary>
        /// How far the board reaches each way from its middle at a height dy above it (m, the nearer of the two sides): its outline's (a pointed board narrows toward its point), else its shape's.
        /// </summary>
        private static float HalfSpanAt(Placement p, float dy)
        {
            SignInstance instance = p.Board.Instance;
            if (instance.Board == null)
                return SignCatalog.ChordAt(p.Def, dy) * 0.5f;
            List<Point2> outline = GuideKit.PartOutline(instance.Board.Main);
            float best = float.MaxValue;
            foreach (float y in new[] { dy, -dy })
            {
                for (int i = 0; i < outline.Count; i++)
                {
                    Point2 a = outline[i], b = outline[(i + 1) % outline.Count];
                    if ((a.Y > y) == (b.Y > y))
                        continue;
                    float x = a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                    best = Math.Min(best, Math.Abs(x));
                }
            }

            return best == float.MaxValue ? p.Def.Width * 0.5f : best;
        }

        /// <summary>
        /// On a wall bracket (0.12.2): a plate on the wall (the plane x = 0) with four bolts, the arm out of it along the side's X, a tie from the wall above the arm to three quarters of it, a knee under
        /// its root, and the boards hanging under the arm, the top one on flat bars (two for a rectangle, one at the middle of any other shape, whose outline is highest there) and each one under it on a bar
        /// at the middle from the one above. All of it in the plane of the arm (z = 0), which is the boards' too.
        /// </summary>
        private static void BuildWallBracket(SupportLayout layout, SignParts parts, List<Placement> placed, float side)
        {
            float r = SupportLayouts.BracketArmRadius, length = layout.ArmLength, armY = layout.ArmY;
            float knee = SupportLayouts.BracketKnee, plate = 0.012f;
            float plateLo = armY - knee - 0.05f, plateHi = layout.PoleTop + 0.05f;
            Solids.Box(parts.Metal, new Vec3(side * plate * 0.5f, (plateLo + plateHi) * 0.5f, 0f), new Vec3(plate * 0.5f, (plateHi - plateLo) * 0.5f, 0.06f));
            if (!t_Reduced)
            {
                foreach (float y in new[] { plateLo + 0.03f, plateHi - 0.03f })
                {
                    foreach (float z in new[] { -0.035f, 0.035f })
                        Solids.Tube(parts.Metal, new Vec3(side * plate, y, z), new Vec3(side * (plate + 0.010f), y, z), 0.008f, 0.008f, 6, false, true);
                }
            }

            Solids.Tube(parts.Metal, new Vec3(side * plate, armY, 0f), new Vec3(side * length, armY, 0f), r, r, 12, false, true);
            Solids.Tube(parts.Metal, new Vec3(side * plate, layout.PoleTop, 0f), new Vec3(side * 0.75f * length, armY + r * 0.5f, 0f), 0.008f, 0.008f, 8, true, true);
            Solids.Tube(parts.Metal, new Vec3(side * plate, armY - knee, 0f), new Vec3(side * layout.KneeX, armY - r * 0.5f, 0f), 0.010f, 0.010f, 8, true, true);
            parts.Picks.Add(new PickShape { Name = "arm", Min = new Vec3(Math.Min(0f, side * length), armY - r, -r), Max = new Vec3(Math.Max(0f, side * length), armY + r, r) });
            parts.Picks.Add(new PickShape { Name = "plate", Min = new Vec3(Math.Min(0f, side * plate), plateLo, -0.06f), Max = new Vec3(Math.Max(0f, side * plate), plateHi, 0.06f) });

            float half = SupportLayouts.BoardThickness * 0.5f + 0.004f;
            for (int i = 0; i < placed.Count; i++)
            {
                Placement p = placed[i];
                bool rectangle = p.Def.Shape == ShapeKind.Rect || p.Def.Shape == ShapeKind.RoundRect;
                float from = i == 0 ? armY : placed[i - 1].Bottom + 0.04f, to = p.Top + p.Def.ExtraTop - 0.04f;
                // A text board has no plate for the bars to hold: they reach into its highest rail (0.12.76).
                float railTop = TextBarBottom(parts, p);
                if (!float.IsNaN(railTop))
                    to = Math.Min(to, railTop);
                float[] ks = i == 0 && rectangle ? new[] { -0.3f, 0.3f } : new[] { 0f };
                foreach (float k in ks)
                    Solids.Box(parts.Metal, new Vec3(p.Cx + k * p.Def.Width, (from + to) * 0.5f, 0f), new Vec3(0.0125f, (from - to) * 0.5f, half));
            }
        }

        /// <summary>How thick the slab is that stands for the structure over a hung board (m).</summary>
        public const float OverStandInThick = 0.9f;

        /// <summary>
        /// A stand-in for the structure over the road of an over-road support (0.12.81), for the thumbnail and the pictures (Fig. 8 a): a beam whose face is the plane z = 0 and which holds the board wholly
        /// within it (its soffit OverRoad.TopGap under the board's lower edge, its top OverRoad.TopGap over the board's top), under a deck that runs across; in the game the structure is the bridge's own.
        /// For a hung board (0.12.81) the structure is a slab over it: its underside is the plane the rods' plates are on, its edge OverRoad.HangSetback in front of the tube's axis, reaching back behind the braces.
        /// </summary>
        public static void AddOverStandIn(SignParts parts)
        {
            SupportLayout l = parts.Layout;
            if (l.Hanger)
            {
                float width = Math.Max(2.4f, Math.Abs(l.BoardCentreX) + l.InnerReach + 1.0f), depth = OverRoad.HungDepth + 1.6f;
                Solids.Box(parts.Concrete, new Vec3(0f, l.HangerTop + OverStandInThick * 0.5f, OverRoad.HangSetback - depth * 0.5f), new Vec3(width, OverStandInThick * 0.5f, depth * 0.5f));
                return;
            }

            float soffit = l.BottomClearance - OverRoad.TopGap, top = l.BoardTop + OverRoad.TopGap, half = Math.Max(2.4f, Math.Abs(l.BoardCentreX) + l.InnerReach + 1.0f);
            const float deck = 0.35f;
            Solids.Box(parts.Concrete, new Vec3(0f, (soffit + top) * 0.5f, -0.5f), new Vec3(half, (top - soffit) * 0.5f, 0.5f));
            Solids.Box(parts.Concrete, new Vec3(0f, top + deck * 0.5f, -0.9f), new Vec3(half, deck * 0.5f, 1.4f));
        }

        /// <summary>A stand-in for the wall of a wall support (a slab behind the board, or beside the bracket), for the thumbnail and the pictures: in the game the wall is the building's own.</summary>
        public static void AddWallStandIn(SignParts parts, SupportDef support)
        {
            SupportLayout l = parts.Layout;
            float top = l.PoleTop + 0.6f, half = Math.Max(1.2f, Math.Abs(l.BoardCentreX) + 1.0f);
            if (support.Kind == SupportKind.WallBracket)
            {
                float side = support.Side < 0 ? -1f : 1f;
                Solids.Box(parts.Concrete, new Vec3(-side * 0.15f, top * 0.5f, 0f), new Vec3(0.15f, top * 0.5f, 1.2f));
            }
            else
            {
                Solids.Box(parts.Concrete, new Vec3(0f, top * 0.5f, -0.15f), new Vec3(half, top * 0.5f, 0.15f));
            }
        }

        /// <summary>
        /// The hoop that holds a channel to a round post or pole at height y (22MR601 p.141 抱箍 50 × 5 with its backing 抱箍衬底; REPORT.md roadside 2.4): a band 5 mm thick round the post, the backing
        /// from the post's front to the channel (channelBack: the channel's back face) and the hoop's two ears as one plate across the post on the channel's back. The band is open, so that round a post
        /// that is not ours (an attached support) nothing shows where the game draws its own.
        /// </summary>
        private static void Hoop(SignParts parts, float px, float y, float r, float channelBack, float flat = 1f, bool band = true)
        {
            const float half = 0.025f, t = 0.005f;
            MeshBuilder metal = parts.Metal;
            // A polygon post (flat: its flats' distance over its corners'): the backing from its front flat.
            float root = Math.Max(r * 0.6f, r * flat - 0.008f);
            Solids.Box(metal, new Vec3(px, y, (root + channelBack) * 0.5f), new Vec3(0.02f, half, (channelBack - root) * 0.5f));
            // Without a band: a connector in the post's slot (the Shanghai pole: boards to 2 m² on slot, connector and slider, DG/TJ 08-2362 4.2.12-3).
            if (t_Reduced || !band)
                return;
            Solids.Tube(metal, new Vec3(px, y - half, 0f), new Vec3(px, y + half, 0f), r + t, r + t, PoleSides, false, false);
            Solids.Box(metal, new Vec3(px, y, channelBack - t * 0.5f), new Vec3(r + t + 0.045f, half, t * 0.5f));
        }
    }
}
