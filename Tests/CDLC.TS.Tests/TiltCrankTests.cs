using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.55 (the owner: "车行道上方牌子的前倾（规范 0–15°），以及单悬臂横梁的 18° 折弯都做"): the boards on an arm or a beam lean forward (BoardTilt; JTG D82-2009 2.3.5-4) about their lowest tube, their
    /// clamps' backings as long as the lean asks, the picks leaning with them, the boards on a post upright; and the cranked single cantilever of 22MR601 p.78.
    /// </summary>
    internal static class TiltCrankTests
    {
        public static void RunAll()
        {
            Program.Run("lean: the steps", Steps);
            Program.Run("lean: a board on an arm leans forward about its lowest tube", OnArm);
            Program.Run("lean: on a gantry's front and back, and none on the post", OnGantry);
            Program.Run("crank: the cranked single cantilever of 22MR601 p.78", Crank);
            Program.Run("lean: drawn on a board alone, and a changed lean is another mesh (0.12.56)", Drawn);
        }

        private static AssemblyBoard B(string id, int member, float tilt = 0f, int lane = 0, bool back = false) =>
            new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(id), null), Member = member, Slot = member, Lane = lane, Tilt = tilt, Back = back };

        private static void Steps()
        {
            Program.Near(BoardTilt.Radians(1), (float)(5.0 * Math.PI / 180.0), 1e-6f, "a step is 5°");
            Program.Near(BoardTilt.Radians(9), BoardTilt.Radians(BoardTilt.Max), 1e-6f, "15° at the most (JTG D82 2.3.5: 0° to 15°)");
            for (int s = 0; s <= BoardTilt.Max; s++)
                Program.Check(BoardTilt.StepsOf(BoardTilt.Radians(s)) == s, "the steps back from their radians: " + s);
            Program.Check(BoardTilt.Leans(SupportKind.Cantilever) && BoardTilt.Leans(SupportKind.Gantry) && BoardTilt.Leans(SupportKind.FCantilever) && BoardTilt.Leans(SupportKind.AttachedArm), "the boards over the carriageway lean (车行道上方附着式 too)");
            Program.Check(!BoardTilt.Leans(SupportKind.SinglePole) && !BoardTilt.Leans(SupportKind.Attached) && !BoardTilt.Leans(SupportKind.WallFlat), "those by the road do not");
        }

        private static void OnArm()
        {
            SupportDef arm = SupportCatalog.ById(SupportCatalog.Cantilever);
            float lean = BoardTilt.Radians(2);
            SignParts upright = SignBuilder.BuildAssembly(new[] { B("gb17733.street", 0) }, arm, 0f), leaning = SignBuilder.BuildAssembly(new[] { B("gb17733.street", 0, lean) }, arm, 0f);
            FacePart face = leaning.Faces[0];
            bool down = true;
            foreach (Vec3 n in face.Mesh.Normals)
                down &= Math.Abs(n.Y + (float)Math.Sin(lean)) < 1e-3f && Math.Abs(n.Z - (float)Math.Cos(lean)) < 1e-3f;
            Program.Check(down, "the face looks 10° down, toward the drivers");
            face.Mesh.Bounds(out Vec3 lo, out Vec3 hi);
            upright.Faces[0].Mesh.Bounds(out Vec3 ulo, out Vec3 uhi);
            float height = uhi.Y - ulo.Y;
            Program.Check(hi.Z - lo.Z > height * (float)Math.Sin(lean) * 0.95f, $"its top toward the traffic: {hi.Z - lo.Z:0.000} m deep for a board {height:0.00} m high");
            Program.Near(hi.Y - lo.Y, height * (float)Math.Cos(lean), 2e-3f, "shorter seen from the side by the cosine");
            float topZ = float.NaN, bottomZ = float.NaN;
            foreach (Vec3 p in face.Mesh.Positions)
            {
                if (Math.Abs(p.Y - hi.Y) < 1e-3f)
                    topZ = p.Z;
                if (Math.Abs(p.Y - lo.Y) < 1e-3f)
                    bottomZ = p.Z;
            }

            Program.Check(topZ > bottomZ + 0.05f, $"the top edge is forward of the lower one (+Z, toward the drivers): top {topZ:0.000}, bottom {bottomZ:0.000}");

            // About its arm: at the arm's height the board is where it was; the backing reaches the channel there.
            SupportLayout l = leaning.Layout;
            float frontAtArm = float.MinValue;
            foreach (Vec3 p in face.Mesh.Positions)
            {
                if (Math.Abs(p.Y - l.ArmY) < 0.02f)
                    frontAtArm = Math.Max(frontAtArm, p.Z);
            }

            Program.Check(frontAtArm > l.FrontZ - 0.01f && frontAtArm < l.FrontZ + 0.02f, $"the face at the arm's height stays where it was ({frontAtArm:0.000} vs {l.FrontZ:0.000})");
            Program.Check(lo.Y >= l.BottomClearance - 0.05f, "and no lower than the clearance: " + lo.Y);

            // The ray at the leaning face's middle finds the board; nothing of the support is drawn in front of the face.
            Vec3 middle = (lo + hi) * 0.5f;
            bool hit = Picking.Pick(leaning.Picks, Vec3.Zero, 0f, middle + new Vec3(0f, 0f, 6f), middle - new Vec3(0f, 0f, 6f), out _, out PickShape shape);
            Program.Check(hit && shape.Name == "board", "the board is picked where it leans: " + (shape?.Name ?? "nothing"));
            float top = hi.Y - 0.05f;
            Vec3 high = new Vec3(middle.X, top, 0f);
            float zAtTop = float.MinValue;
            foreach (Vec3 p in face.Mesh.Positions)
            {
                if (Math.Abs(p.Y - top) < 0.05f)
                    zAtTop = Math.Max(zAtTop, p.Z);
            }

            Program.Check(Picking.Pick(leaning.Picks, Vec3.Zero, 0f, new Vec3(high.X, top, zAtTop + 0.5f), new Vec3(high.X, top, zAtTop - 0.5f), out _, out shape) && shape.Name == "board",
                "and at its top, which stands out by the lean");

            // A tall board on one arm swings its lower part back and up: just above where its lower edge was nothing is picked, a little higher it is.
            var tall = new SignDef { Id = "t.tall", Code = "t", Zh = "t", En = "t", Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = 1f, Height = 2.2f, BottomClearance = 2.3f, TextureWidth = 200, TextureHeight = 440, Keywords = new string[0] };
            var board = new AssemblyBoard { Instance = new SignInstance { Def = tall, Key = tall.Id, Spec = "", TexW = 200, TexH = 440 }, Member = 0, Slot = 0, Tilt = BoardTilt.Radians(3) };
            SignParts swung = SignBuilder.BuildAssembly(new[] { board }, arm, 0f);
            SupportLayout t = swung.Layout;
            Program.Check(t.ArmYs.Length == 1 && t.ArmY > t.BottomClearance + 1.9f, "one arm near the tall board's top");
            float x = t.BoardCentreX;
            bool Board(float y) => Picking.Pick(swung.Picks, Vec3.Zero, 0f, new Vec3(x, y, 6f), new Vec3(x, y, -6f), out _, out PickShape s) && s.Name == "board";
            // (a board is picked within Picking.Tolerance of its outline: 4 cm under the old edge is within it upright, and 13 cm under the risen one)
            Program.Check(!Board(t.BottomClearance - 0.04f), "4 cm under where its lower edge was: not the board (its edge has risen by the lean)");
            Program.Check(Board(t.BottomClearance + 0.12f), "12 cm over it: the board");
        }

        private static void OnGantry()
        {
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            float lean = BoardTilt.Radians(3);
            var boards = new List<AssemblyBoard> { B("gb17733.street", 0, lean), B("gb17733.street", 1, lean, 0, true), B("gb.prohib.01", 2, lean, Assembly.OnPost) };
            SignParts parts = SignBuilder.BuildAssembly(boards, gantry, 20f);
            Program.Check(parts.Layout.BeamWidth > 0f, "a span of 20 m: the space truss");
            var normals = parts.Faces.ConvertAll(f => f.Mesh.Normals[0]);
            Program.Check(normals.Count == 3 && normals.Exists(n => n.Z > 0.9f && Math.Abs(n.Y + (float)Math.Sin(lean)) < 1e-3f), "the front board looks forward and 15° down: " + string.Join(" ", normals));
            Program.Check(normals.Exists(n => n.Z < -0.9f && Math.Abs(n.Y + (float)Math.Sin(lean)) < 1e-3f), "the back one the other way, down too");
            Program.Check(normals.Exists(n => n.Z > 0.99f && Math.Abs(n.Y) < 1e-4f), "the board on the post stands upright");

            // A board taller than the truss is clamped to both chords: at the upper one the backings reach the leaning channels, further out by the lean over the truss's depth.
            var tall = new SignDef { Id = "t.tall", Code = "t", Zh = "t", En = "t", Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = 3f, Height = 2.2f, BottomClearance = 2.3f, TextureWidth = 600, TextureHeight = 440, Keywords = new string[0] };
            var board = new AssemblyBoard { Instance = new SignInstance { Def = tall, Key = tall.Id, Spec = "", TexW = 600, TexH = 440 }, Member = 0, Slot = 0, Tilt = lean };
            SignParts leaning = SignBuilder.BuildAssembly(new[] { board }, gantry, 20f);
            SupportLayout g = leaning.Layout;
            float lower = g.ChordYs[0], upper = g.ChordYs[1];
            float channelBack = g.BackZ - SupportLayouts.RailDepth + (upper - lower) * (float)Math.Tan(lean);
            int ends = 0;
            foreach (Vec3 v in leaning.Metal.Positions)
                ends += Math.Abs(v.Y - upper) < 0.035f && Math.Abs(v.Z - (channelBack + 0.002f)) < 0.004f ? 1 : 0;
            Program.Check(ends >= 8, $"the backings at the upper chord reach the leaning channels {channelBack:0.000} m out: {ends} corners there");
        }

        /// <summary>0.12.56: the lean is drawn on a board alone on its support (it was not: the single-board cache does not carry it), and the group cache takes a changed lean for another mesh.</summary>
        private static void Drawn()
        {
            SignDef plate = SignCatalog.ById("gb17733.street");
            uint arm = SupportCatalog.ById(SupportCatalog.Cantilever).Hash;
            var upright = new ItemRecord(plate.Hash, new Vec3(0f, 0f, 0f), 0f, arm);
            var leaning = upright;
            leaning.Tilt = BoardTilt.Radians(2);
            Vec3 a = ChunkBuilder.PartsOfRecord(upright).Faces[0].Mesh.Normals[0], b = ChunkBuilder.PartsOfRecord(leaning).Faces[0].Mesh.Normals[0];
            Program.Check(Math.Abs(a.Y) < 1e-4f && b.Y < -0.15f, $"a board alone leans when its record says so: {a} / {b}");
            List<ChunkPart> tile = ChunkBuilder.Build(new[] { leaning }, Vec3.Zero, out int skipped);
            bool down = false;
            foreach (ChunkPart part in tile)
            {
                if (part.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal))
                    down |= part.Mesh.Normals.Exists(n => n.Y < -0.15f);
            }

            Program.Check(skipped == 0 && down, "and in a tile");
            var five = leaning;
            five.Tilt = BoardTilt.Radians(1);
            five.Group = leaning.Group = 9u;
            Vec3 g10 = ChunkBuilder.PartsOfGroup(new[] { leaning }).Faces[0].Mesh.Normals[0], g5 = ChunkBuilder.PartsOfGroup(new[] { five }).Faces[0].Mesh.Normals[0];
            Program.Check(Math.Abs(g10.Y - g5.Y) > 0.05f, $"a group's lean changed: another mesh ({g10.Y:0.000} / {g5.Y:0.000})");
        }

        private static void Crank()
        {
            SupportDef crank = SupportCatalog.ById(SupportCatalog.CrankCantilever), straight = SupportCatalog.ById(SupportCatalog.Cantilever);
            Program.Check(crank != null && crank.Crank && crank.Kind == SupportKind.Cantilever && SupportCatalog.ById(crank.MirrorId).Crank && SupportCatalog.ById(crank.MirrorId).Hidden, "the cranked cantilever and its hidden mirror");
            SignDef plate = SignCatalog.ById("gb17733.street");
            SupportLayout l = SupportLayouts.Of(plate, crank), s = SupportLayouts.Of(plate, straight);
            Program.Near(l.CrankRise, 1.58f * (float)Math.Sin(18.0 * Math.PI / 180.0), 1e-4f, "the root 1.58 m × sin 18° under the level arm (p.78)");
            Program.Near(l.CrankRun, 1.58f * (float)Math.Cos(18.0 * Math.PI / 180.0), 1e-4f, "the bend 1.58 m × cos 18° out");
            Program.Near(l.ArmY, s.ArmY, 1e-5f, "the level arm where the straight one is");
            Program.Near(l.PoleTop, l.ArmY - l.CrankRise + SupportLayouts.ArmPostAbove, 1e-5f, "the post 0.3 m over the root (p.78)");
            Program.Check(l.BoardFrom >= l.CrankRun + SupportLayouts.CrankBoardGap - 1e-4f && s.CrankRise == 0f, "the board on the level part");
            Program.Check(l.PostTop < l.ArmY - l.CrankRise, "the small boards on the post under the root");

            SignParts parts = SignBuilder.Build(SignInstances.Of(plate, null), crank);
            float rootY = l.ArmY - l.CrankRise, r = l.ArmRootRadius;
            int onSlant = 0, beside = 0;
            var d = new Vec3((float)Math.Cos(SupportLayouts.CrankAngle), (float)Math.Sin(SupportLayouts.CrankAngle), 0f);
            foreach (Vec3 v in parts.Metal.Positions)
            {
                // Distance from the slanted line between the joint and the bend.
                Vec3 rel = v - new Vec3(0f, rootY, 0f);
                float t = rel.X * d.X + rel.Y * d.Y;
                if (t < 0.25f || t > 1.75f)
                    continue;
                Vec3 off = rel - d * t;
                if (Math.Abs(off.Length - r) < 0.002f)
                    onSlant++;
                else if (off.Length < 0.5f)
                    beside++;
            }

            Program.Check(onSlant >= 26, "a tube rises along the 18° line from the joint to the bend (its two rings): " + onSlant);
            parts.Metal.Bounds(out Vec3 mLo, out Vec3 mHi);
            Program.Check(mHi.X >= l.ArmLength - 1e-3f && mHi.X <= l.ArmLength + Assembly.ArmTipInside + 0.01f, "the level arm to its end, behind the board: " + mHi.X + " for " + l.ArmLength);

            // The joint's ribs stand on the post: every vertex of the joint near the post is on or in it.
            bool clear = true;
            foreach (Vec3 v in parts.Metal.Positions)
            {
                if (v.Y < rootY - 0.3f || v.Y > rootY + 0.3f || v.X < 0f || v.X > l.PoleRadius + 0.05f)
                    continue;
                float across = (float)Math.Sqrt(v.X * v.X + v.Z * v.Z);
                clear &= across <= l.PoleRadius + 0.05f;
            }

            Program.Check(clear, "the joint's ribs start at the post");
            // A board that needs two arms has them straight (the crank is 1F's).
            var big = new SignDef { Id = "t.big", Width = 2.5f, Height = 1.6f, Shape = ShapeKind.Rect, BottomClearance = 2.3f };
            SupportLayout two = SupportLayouts.Of(big, crank);
            Program.Check(two.ArmYs.Length == 2 && two.CrankRise == 0f, "4 m² on the cranked support: two straight arms (2F)");
        }
    }
}
