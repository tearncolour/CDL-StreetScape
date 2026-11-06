using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Each board of a stack on one post turned on its own about the post (0.12.8, AssemblyBoard.Turn; the owner: "一个杆子上多个牌子情况下也应该支持能够调整每一个牌子的朝向"); and the post of a hung support,
    /// which takes small boards, easier to point at (the owner: "悬臂式支架的竖杆也应该能够挂牌子").
    /// </summary>
    internal static class BoardTurnTests
    {
        public static void RunAll()
        {
            Program.Run("board turn: a board turned on a single post", OnSinglePost);
            Program.Run("board turn: where a board turns and where it does not", WhereItTurns);
            Program.Run("board turn: on the post of a cantilever, and pointing at that post", OnHungPost);
            Program.Run("board turn: what is drawn again and what the line of help says", Keys);
            Program.Run("board turn: runtime facings agree with the actual standalone and grouped meshes", RuntimeFacings);
        }

        private static readonly float Quarter = (float)(Math.PI / 2);

        private static SignDef D(string id) => SignInstances.Of(SignCatalog.ById(id), null).Def;

        private static AssemblyBoard B(string id, int member, float turn = 0f, int lane = 0) =>
            new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(id), null), Member = member, Slot = member, Turn = turn, Lane = lane };

        private static SignParts Build(SupportDef support, params AssemblyBoard[] boards) => SignBuilder.BuildAssembly(boards, support, 0f);

        private static SupportDef S(string id) => SupportCatalog.ById(id);

        /// <summary>A point of the sign's frame turned about the upright line at X = axisX, z = 0 (the sense of a yaw: +Z goes to (sin, 0, cos)).</summary>
        private static Vec3 Turned(Vec3 p, float axisX, float turn)
        {
            float s = (float)Math.Sin(turn), c = (float)Math.Cos(turn), x = p.X - axisX;
            return new Vec3(axisX + x * c + p.Z * s, p.Y, -x * s + p.Z * c);
        }

        private static bool Near(Vec3 a, Vec3 b, float e) => Math.Abs(a.X - b.X) < e && Math.Abs(a.Y - b.Y) < e && Math.Abs(a.Z - b.Z) < e;

        private static FacePart FaceOf(SignParts parts, string id) => parts.Faces.Find(f => f.Instance.Def.Id == id);

        /// <summary>Whether the second mesh is the first with every vertex turned about the axis (positions and normals), and the triangles the same.</summary>
        private static bool AllTurned(MeshBuilder a, MeshBuilder b, float axisX, float turn)
        {
            if (a.VertexCount != b.VertexCount || a.Indices.Count != b.Indices.Count)
                return false;
            for (int i = 0; i < a.VertexCount; i++)
            {
                if (!Near(Turned(a.Positions[i], axisX, turn), b.Positions[i], 1e-4f) || !Near(Turned(a.Normals[i], 0f, turn), b.Normals[i], 1e-4f))
                    return false;
            }

            for (int i = 0; i < a.Indices.Count; i++)
            {
                if (a.Indices[i] != b.Indices[i])
                    return false;
            }

            return true;
        }

        private static bool Same(MeshBuilder a, MeshBuilder b) => AllTurned(a, b, 0f, 0f);

        /// <summary>The metal of b is that of a with some vertices turned about the axis and the rest where they were: how many were turned (-1 when a vertex is neither).</summary>
        private static int TurnedCount(MeshBuilder a, MeshBuilder b, float axisX, float turn)
        {
            if (a.VertexCount != b.VertexCount)
                return -1;
            int turned = 0;
            for (int i = 0; i < a.VertexCount; i++)
            {
                if (Near(a.Positions[i], b.Positions[i], 1e-4f) && Near(a.Normals[i], b.Normals[i], 1e-4f))
                    continue;
                if (!Near(Turned(a.Positions[i], axisX, turn), b.Positions[i], 1e-4f) || !Near(Turned(a.Normals[i], 0f, turn), b.Normals[i], 1e-4f))
                    return -1;
                turned++;
            }

            return turned;
        }

        /// <summary>The shape a ray in the sign's frame (the item at the origin, facing +Z) hits first; null for none.</summary>
        private static PickShape Hit(SignParts parts, Vec3 from, Vec3 to) => Picking.Pick(parts.Picks, Vec3.Zero, 0f, from, to, out _, out PickShape shape) ? shape : null;

        private static void OnSinglePost()
        {
            // A single post: a no-horns board above a warning triangle; the triangle turned a quarter (clockwise seen from above: it faces +X).
            SupportDef pole = S(SupportCatalog.SinglePole);
            SignParts flat = Build(pole, B("gb.prohib.34", 0), B("gb.warn.01", 1));
            SignParts turned = Build(pole, B("gb.prohib.34", 0), B("gb.warn.01", 1, Quarter));
            float axis = flat.Layout.PoleX[0];
            Program.Check(flat.Faces.Count == 2 && turned.Faces.Count == 2, "two boards on the post");
            Program.Check(AllTurned(FaceOf(flat, "gb.warn.01").Mesh, FaceOf(turned, "gb.warn.01").Mesh, axis, Quarter), "the triangle's face is turned a quarter about the post, its normals with it");
            Program.Check(Same(FaceOf(flat, "gb.prohib.34").Mesh, FaceOf(turned, "gb.prohib.34").Mesh), "the board above it is not");
            bool facesX = true;
            foreach (Vec3 n in FaceOf(turned, "gb.warn.01").Mesh.Normals)
                facesX &= n.X > 0.99f;
            Program.Check(facesX, "the turned face looks along +X");
            int moved = TurnedCount(flat.Metal, turned.Metal, axis, Quarter);
            Program.Check(moved > 50, "its back, edge, channels and clamps turn with it, nothing else moves: " + moved);
            Program.Check(Same(flat.Concrete, turned.Concrete), "the foot is where it was");
            Program.Check(turned.Min.Z < flat.Min.Z - 0.2f, "the whole's box takes the turned board in (it reaches behind the post now): " + flat.Min.Z + " -> " + turned.Min.Z);

            // Picking: the turned triangle is hit from +X and no longer from the front; the board above is still hit from the front.
            Placement warn = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B("gb.prohib.34", 0), B("gb.warn.01", 1) }), pole, 0f).Placed.Find(p => p.Board.Member == 1);
            float y = warn.Cy;
            PickShape fromSide = Hit(turned, new Vec3(6f, y, 0f), new Vec3(-6f, y, 0f));
            Program.Check(fromSide != null && fromSide.Name == "board" && fromSide.Member == 1, "the turned board is hit from the side it faces: " + (fromSide?.Name ?? "nothing"));
            PickShape fromFront = Hit(turned, new Vec3(0f, y, 6f), new Vec3(0f, y, -6f));
            Program.Check(fromFront == null || fromFront.Member != 1, "and not from the front, where it was");
            PickShape flatFront = Hit(flat, new Vec3(0f, y, 6f), new Vec3(0f, y, -6f));
            Program.Check(flatFront != null && flatFront.Member == 1, "(unturned it is hit from the front)");

            // Half a turn: back to back with the one above; a turn back to nothing is the unturned support.
            SignParts round = Build(pole, B("gb.prohib.34", 0), B("gb.warn.01", 1, (float)Math.PI));
            bool facesBack = true;
            foreach (Vec3 n in FaceOf(round, "gb.warn.01").Mesh.Normals)
                facesBack &= n.Z < -0.99f;
            Program.Check(facesBack, "half a turn faces it the other way");
            Program.Check(Same(flat.Metal, Build(pole, B("gb.prohib.34", 0), B("gb.warn.01", 1, 0f)).Metal), "no turn is the support as before");
            Program.Check(Same(FaceOf(turned, "gb.warn.01").Mesh, FaceOf(Build(pole, B("gb.prohib.34", 0), B("gb.warn.01", 1, Quarter - 2f * (float)Math.PI)), "gb.warn.01").Mesh), "a turn and the same less a whole turn are one");

            // In reduced detail the turn is the same.
            SignParts far = SignBuilder.BuildAssembly(new[] { B("gb.prohib.34", 0), B("gb.warn.01", 1, Quarter) }, pole, 0f, SupportDetail.Reduced);
            Program.Check(AllTurned(FaceOf(flat, "gb.warn.01").Mesh, FaceOf(far, "gb.warn.01").Mesh, axis, Quarter), "far away (reduced detail) the face is turned the same");
        }

        private static void WhereItTurns()
        {
            Program.Check(Assembly.TurnAxis(SupportKind.SinglePole, SupportLayouts.Of(D("gb.prohib.34"), S(SupportCatalog.SinglePole), 0f), B("gb.prohib.34", 0), out float x) && Math.Abs(x) < 1e-6f, "a single post: about the post");
            Program.Check(Assembly.TurnAxis(SupportKind.Attached, SupportLayouts.Of(D("gb.prohib.34"), S(SupportCatalog.AttachedPole), 0f), B("gb.prohib.34", 0), out _), "a lamp post or a signal pole");
            Program.Check(!Assembly.TurnAxis(SupportKind.Cantilever, default, B("gb.prohib.34", 0), out _) && Assembly.TurnAxis(SupportKind.Cantilever, default, B("gb.prohib.34", 0, 0f, Assembly.OnPost), out _),
                "a hung support: the boards on its post, not those on its arm");
            Program.Check(!Assembly.TurnAxis(SupportKind.DoublePole, default, B("gb.prohib.34", 0), out _) && !Assembly.TurnAxis(SupportKind.WallFlat, default, B("gb.prohib.34", 0), out _) && !Assembly.TurnAxis(SupportKind.WallBracket, default, B("gb.prohib.34", 0), out _),
                "not on two posts or a wall");

            // What does not turn is built as if it had no turn.
            foreach (string id in new[] { SupportCatalog.DoublePole, SupportCatalog.WallFlat, SupportCatalog.WallBracket })
            {
                SupportDef support = S(id);
                SignParts a = Build(support, B("gb.prohib.34", 0), B("gb.warn.01", 1)), b = Build(support, B("gb.prohib.34", 0), B("gb.warn.01", 1, Quarter));
                Program.Check(Same(a.Metal, b.Metal) && Same(FaceOf(a, "gb.warn.01").Mesh, FaceOf(b, "gb.warn.01").Mesh), id + ": the board faces as its support does");
            }

            SupportDef arm = S(SupportCatalog.Cantilever);
            SignParts onArm = Build(arm, B("gb.prohib.34", 0), B("gb.warn.01", 1)), onArmTurned = Build(arm, B("gb.prohib.34", 0), B("gb.warn.01", 1, Quarter));
            Program.Check(Same(onArm.Metal, onArmTurned.Metal) && Same(FaceOf(onArm, "gb.warn.01").Mesh, FaceOf(onArmTurned, "gb.warn.01").Mesh), "a board on an arm faces as the arm does");

            // On a lamp post (attached): the clamps go round the host, so they turn with the board about its middle.
            SupportDef host = S(SupportCatalog.AttachedPole);
            SignParts a1 = Build(host, B("gb.prohib.34", 0), B("gb.warn.01", 1)), a2 = Build(host, B("gb.prohib.34", 0), B("gb.warn.01", 1, -Quarter));
            Program.Check(AllTurned(FaceOf(a1, "gb.warn.01").Mesh, FaceOf(a2, "gb.warn.01").Mesh, a1.Layout.PoleX[0], -Quarter), "on a lamp post: the board turns about the host");
            Program.Check(TurnedCount(a1.Metal, a2.Metal, a1.Layout.PoleX[0], -Quarter) > 10, "its channels and clamps with it");
        }

        private static void OnHungPost()
        {
            // A cantilever: a board on its arm and a small one on its post, turned a quarter the other way (it faces -X, away from the road the arm is over).
            SupportDef arm = S(SupportCatalog.Cantilever);
            AssemblyBoard plate = B("gb.prohib.34", 0), small = B("gb.warn.01", 1, 0f, Assembly.OnPost), turnedSmall = B("gb.warn.01", 1, -Quarter, Assembly.OnPost);
            SignParts a = Build(arm, plate, small), b = Build(arm, plate, turnedSmall);
            Program.Check(AllTurned(FaceOf(a, "gb.warn.01").Mesh, FaceOf(b, "gb.warn.01").Mesh, 0f, -Quarter), "the board on the post turns about the post");
            Program.Check(Same(FaceOf(a, "gb.prohib.34").Mesh, FaceOf(b, "gb.prohib.34").Mesh), "the board on the arm does not");
            int moved = TurnedCount(a.Metal, b.Metal, 0f, -Quarter);
            Program.Check(moved > 20, "its channels, collars and plates turn with it: " + moved);

            // Pointing at the post: the room for small boards (from the foot to under the lowest arm's joint) is picked from a quarter of a metre beside the tube; above it, and further off, not.
            SupportLayout layout = a.Layout;
            float r = layout.PoleRadius;
            PickShape beside = Hit(a, new Vec3(-6f, 1.2f, r + 0.22f), new Vec3(6f, 1.2f, r + 0.22f));
            Program.Check(beside != null && beside.Name == "post", "a ray 22 cm beside the post picks it: " + (beside?.Name ?? "nothing"));
            Program.Check(Hit(a, new Vec3(-6f, 1.2f, r + 0.6f), new Vec3(6f, 1.2f, r + 0.6f)) == null, "60 cm beside it nothing");
            float high = layout.PostTop + 0.5f;
            PickShape above = Hit(a, new Vec3(-6f, high, r + 0.22f), new Vec3(6f, high, r + 0.22f));
            Program.Check(above == null || above.Name != "post" || above.Grow == 0f, "above the room for boards the post is not grown");

            // The board on the post is in front of the grown post: a ray at it picks the board, not the post.
            Placement onPost = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { plate, small }), arm, 0f).Placed.Find(p => p.Board.Member == 1);
            PickShape atBoard = Hit(a, new Vec3(0f, onPost.Cy, 6f), new Vec3(0f, onPost.Cy, -6f));
            Program.Check(atBoard != null && atBoard.Name == "board" && atBoard.Member == 1, "the board on the post stays what is picked in front of it: " + (atBoard?.Name ?? "nothing"));
            // A single post's own post is not grown (its boards are in front of it and the post is where it stands).
            bool grownOnPole = false;
            foreach (PickShape shape in Build(S(SupportCatalog.SinglePole), plate).Picks)
                grownOnPole |= shape.Grow > 0f;
            Program.Check(!grownOnPole, "a single post's pick is as it was");
        }

        private static void Keys()
        {
            // The renderer's records: a turn is a change of the tile (TileDiff) and of the mesh's key (two turns are two meshes, one turn is one).
            uint pole = SupportCatalog.ById(SupportCatalog.SinglePole).Hash;
            ItemRecord R(string id, int slot, float turn) => new ItemRecord(SignCatalog.ById(id).Hash, new Vec3(10f, 0f, 20f), 0.3f, pole, null, 0f, 77u, slot) { Turn = turn };
            var flat = new List<ItemRecord> { R("gb.prohib.34", 0, 0f), R("gb.warn.01", 1, 0f) };
            var turned = new List<ItemRecord> { R("gb.prohib.34", 0, 0f), R("gb.warn.01", 1, Quarter) };
            Program.Check(!TileDiff.Same(flat[1], turned[1]) && TileDiff.Same(turned[1], R("gb.warn.01", 1, Quarter)), "a turned board is a change of what is drawn");
            SignParts p0 = ChunkBuilder.PartsOfGroup(flat), p1 = ChunkBuilder.PartsOfGroup(turned), p2 = ChunkBuilder.PartsOfGroup(new List<ItemRecord> { R("gb.prohib.34", 0, 0f), R("gb.warn.01", 1, Quarter) });
            Program.Check(p0 != null && p1 != null && !ReferenceEquals(p0, p1) && ReferenceEquals(p1, p2), "two turns are two meshes, the same turn one");
            Program.Check(ItemGroups.Boards(turned)[1].Turn == Quarter, "the record's turn goes to its board");

            Program.Near(Assembly.WrapTurn(3f * Quarter), -Quarter, 1e-5f, "three quarters clockwise is a quarter the other way");
            Program.Near(Assembly.WrapTurn(float.NaN), 0f, 0f, "no number is no turn");
            Program.Check(Assembly.TurnKey(B("gb.warn.01", 0, Quarter)) == 900 && Assembly.TurnKey(B("gb.warn.01", 0, -Quarter)) == -900, "the key in tenths of a degree");

            // The line of help in edit mode says the keys when the board turns, in both languages; not when it does not.
            foreach (bool zh in new[] { true, false })
            {
                List<string[]> with = HintText.Keys(new HintInfo { Kind = "move", Count = 2, Turns = true }, zh), without = HintText.Keys(new HintInfo { Kind = "move", Count = 2 }, zh);
                Program.Check(with.Exists(k => k[0] == "[  ]") && with.Exists(k => k[0] == "\\"), (zh ? "zh" : "en") + ": the keys that turn it");
                Program.Check(!without.Exists(k => k[0] == "[  ]") && HintText.Of(new HintInfo { Kind = "move", Count = 2, Turns = true }, zh).StartsWith(HintText.Of(new HintInfo { Kind = "move", Count = 2 }, zh), StringComparison.Ordinal),
                    (zh ? "zh" : "en") + ": nothing about turning otherwise");
                List<string[]> post = HintText.Keys(new HintInfo { Kind = "move", Count = 2, Hung = true, Turns = true }, zh);
                Program.Check(!post.Exists(k => k[0] == "← →"), (zh ? "zh" : "en") + ": a board on the post of a hung support is not moved along the beam");
                string beam = HintText.Of(new HintInfo { Kind = "attach", Count = 2, Hung = true, Max = 4 }, zh);
                Program.Check(beam.Length < 40, (zh ? "zh" : "en") + ": joining a beam says its span and the room left in one short line: " + beam);
            }
        }

        private static void RuntimeFacings()
        {
            ItemRecord Record(string id, string support, float turn, uint group = 0u, int slot = 0, int lane = 0) =>
                new ItemRecord(SignCatalog.ById(id).Hash, new Vec3(10f, 0f, 20f), 0.3f, support == null ? 0u : S(support).Hash, null, 0f, group, slot, lane) { Turn = turn };
            void Faces(MeshBuilder face, float yaw, float expected, string why)
            {
                var world = new MeshBuilder();
                world.Append(face, Vec3.Zero, yaw);
                var direction = new Vec3((float)Math.Sin(expected), 0f, (float)Math.Cos(expected));
                bool right = world.VertexCount > 0;
                foreach (Vec3 normal in world.Normals)
                    right &= Vec3.Dot(normal, direction) > 0.9999f;
                Program.Check(right, why);
            }

            // The actual standalone parts and tile used to discard Turn even though the editor preview turned it. Test both export paths and the world-space normals, at both support details.
            ItemRecord single = Record("gb.guide8.01", SupportCatalog.SinglePole, Quarter);
            float singleYaw = BoardFacing.Yaw(single);
            Program.Near(singleYaw, single.Yaw + Quarter, 1e-6f, "a standalone single-post board includes its own turn");
            Faces(ChunkBuilder.PartsOfRecord(single).Faces[0].Mesh, single.Yaw, singleYaw, "standalone parts face the direction the API reports");
            foreach (TileTier tier in new[] { TileTier.Near, TileTier.Mid, TileTier.Far })
            {
                ChunkDetail detail = ChunkDetail.Of(tier);
                List<ChunkPart> tile = ChunkBuilder.BuildDetailed(new[] { single }, Vec3.Zero, detail, out int skipped);
                string material = FaceDetail.Material(SignInstances.Of(SignCatalog.ByHash(single.TypeHash), single.Spec).Key, detail.FaceDetail);
                ChunkPart face = tile.Find(p => p.Material == material);
                Program.Check(skipped == 0 && face != null, tier + ": standalone turned board is in the tile");
                if (face != null)
                    Faces(face.Mesh, 0f, singleYaw, tier + ": the actual tile has the reported world-space facing");
            }

            // The grouped renderer and the reader use the same support plan, not the queried board in isolation.
            ItemRecord top = Record("gb.prohib.34", SupportCatalog.SinglePole, -Quarter, 71u, 0);
            ItemRecord low = Record("gb.warn.01", SupportCatalog.SinglePole, Quarter, 71u, 1);
            var members = new List<ItemRecord> { top, low };
            float[] groupYaws = BoardFacing.Yaws(members);
            SignParts grouped = ChunkBuilder.PartsOfGroup(members);
            Faces(FaceOf(grouped, "gb.prohib.34").Mesh, top.Yaw, groupYaws[0], "the first board's mesh and facing agree");
            Faces(FaceOf(grouped, "gb.warn.01").Mesh, top.Yaw, groupYaws[1], "the independently turned second board's mesh and facing agree");
            Program.Near(groupYaws[0], top.Yaw - Quarter, 1e-6f, "the top board keeps its own negative turn");
            Program.Near(groupYaws[1], top.Yaw + Quarter, 1e-6f, "the lower board keeps its own positive turn");
            float[] reversed = BoardFacing.Yaws(new[] { low, top });
            Program.Near(reversed[0], groupYaws[1], 1e-6f, "input order does not change the lower board's facing");
            Program.Near(reversed[1], groupYaws[0], 1e-6f, "input order does not change the top board's facing");

            // An automatic single post becomes a multi-post support once another board widens the assembly. Its saved turn is retained but no longer applied, exactly as in its mesh.
            ItemRecord automatic = Record("gb.prohib.34", null, Quarter, 72u, 0);
            ItemRecord wide = Record("guide.exit.advance", null, 0f, 72u, 1);
            Program.Near(BoardFacing.Yaw(automatic), automatic.Yaw + Quarter, 1e-6f, "the small board alone can turn on its automatic single post");
            var grown = new List<ItemRecord> { automatic, wide };
            AssemblyPlan grownPlan = ItemGroups.PlanOf(grown, out _);
            Program.Check(grownPlan.Layout.PoleX.Length > 1, "the complete assembly actually needs multiple posts");
            float grownYaw = BoardFacing.Yaws(grown)[0];
            Program.Near(grownYaw, automatic.Yaw, 1e-6f, "the same saved turn is inactive on the multi-post assembly");
            Faces(FaceOf(ChunkBuilder.PartsOfGroup(grown), "gb.prohib.34").Mesh, automatic.Yaw, grownYaw, "the grown assembly's actual mesh agrees");

            foreach (string support in new[] { SupportCatalog.DoublePole, SupportCatalog.TriplePole, SupportCatalog.WallFlat, SupportCatalog.WallBracket, SupportCatalog.Cantilever, SupportCatalog.Gantry })
            {
                ItemRecord fixedBoard = Record("gb.prohib.34", support, Quarter);
                Program.Near(BoardFacing.Yaw(fixedBoard), fixedBoard.Yaw, 1e-6f, support + ": a saved turn on a fixed board is ignored");
                Faces(ChunkBuilder.PartsOfRecord(fixedBoard).Faces[0].Mesh, fixedBoard.Yaw, fixedBoard.Yaw, support + ": the fixed mesh agrees");
            }
            foreach (string support in new[] { SupportCatalog.Cantilever, SupportCatalog.DoubleCantilever, SupportCatalog.Gantry })
            {
                ItemRecord postBoard = Record("gb.prohib.34", support, Quarter, 0u, 0, Assembly.OnPost);
                Program.Near(BoardFacing.Yaw(postBoard), postBoard.Yaw + Quarter, 1e-6f, support + ": a board on its post turns");
                Faces(ChunkBuilder.PartsOfRecord(postBoard).Faces[0].Mesh, postBoard.Yaw, BoardFacing.Yaw(postBoard), support + ": the post board's actual mesh agrees");
            }

            // Both runtime readers resolve siblings from the indexed store, and replace a one-frame-old snapshot with the live screen being read.
            var store = new ItemStore<int>();
            store.Upsert(1, automatic);
            store.Upsert(2, wide);
            Program.Near(BoardFacing.Yaw(store, 1, automatic), grownYaw, 1e-6f, "the runtime lookup takes the complete automatic support into account");
            store.Upsert(3, top);
            store.Upsert(4, low);
            ItemRecord edited = low;
            edited.Turn = 0f;
            Program.Near(BoardFacing.Yaw(store, 4, edited), top.Yaw, 1e-6f, "the runtime lookup uses the current screen's edit over the stale stored turn");
            foreach (float turn in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                ItemRecord bad = single;
                bad.Turn = turn;
                Program.Near(BoardFacing.Yaw(bad), bad.Yaw, 1e-6f, "a non-finite saved turn is inactive: " + turn);
            }
        }
    }
}
