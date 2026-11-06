using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Boards on both faces of a gantry (0.12.22; the owner: "门架式应该允许两面都能放牌子，一般也不会放两个门架用于双向车道").</summary>
    internal static class GantryFaceTests
    {
        public static void RunAll()
        {
            Program.Run("gantry faces: the back is a row of its own", Rows);
            Program.Run("gantry faces: a board goes on the back where the front is taken", AttachBack);
            Program.Run("gantry faces: a board on the back is built facing the other way", Built);
        }

        private static SignDef Board(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f)
        };

        private static readonly SignDef Guide = Board("t.guide", 3f, 2f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(int member, bool back, int lane = 0, float x = 0f) => new AssemblyBoard { Instance = Inst(Guide), Member = member, Slot = member, Lane = lane, OffsetX = x, Back = back };

        private static SupportDef Gantry => SupportCatalog.ById(SupportCatalog.Gantry);

        private static void Rows()
        {
            // Two boards side by side on each face: each face is as wide as its own two, not as all four.
            var boards = new List<AssemblyBoard> { B(0, false), B(1, false), B(2, true), B(3, true) };
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(boards), Gantry, 20f);
            Program.Near(plan.Total.Width, 2f * Guide.Width + Assembly.SideGap, 1e-3f, "the layout is made for the wider face, not for all the boards");
            Program.Check(Assembly.Overlaps(plan.Placed, true).Count == 0, "a board on the back does not overlap one on the front at the same place");
            Placement front0 = plan.Placed.Find(p => p.Board.Member == 0), back0 = plan.Placed.Find(p => p.Board.Member == 2);
            Program.Check(!front0.Back && back0.Back, "the placements know their face");
            Program.Check(front0.Cx > plan.Placed.Find(p => p.Board.Member == 1).Cx, "the front's first board is at the front traffic's left (the larger X)");
            Program.Check(back0.Cx < plan.Placed.Find(p => p.Board.Member == 3).Cx, "the back's first board is at the other traffic's left (the smaller X)");

            // Two boards at one place on the same face do overlap.
            var clash = new List<AssemblyBoard> { B(0, true, Attach.LaneHand, 5f), B(1, true, Attach.LaneHand, 5.5f) };
            Program.Check(Assembly.Overlaps(Assembly.Plan(clash, Gantry, 20f).Placed, true).Count == 1, "two boards at one place on the back overlap");

            // On a cantilever there is no back: the flag is ignored.
            SupportDef arm = SupportCatalog.ById(SupportCatalog.Cantilever);
            AssemblyPlan onArm = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(0, false), B(1, true) }), arm, 10f);
            Program.Check(onArm.Placed.TrueForAll(p => !p.Back), "a cantilever's boards are all on its front");
            Program.Check(onArm.Total.Width > 2f * Guide.Width, "and side by side there");
        }

        private static void AttachBack()
        {
            // A gantry of 14 m with boards across the whole front: no room left there, room on the back.
            var boards = new List<AssemblyBoard>();
            for (int i = 0; i < 3; i++)
            {
                AttachResult r = Attach.Evaluate(new AttachRequest { Existing = boards, Support = Gantry, Reach = 14f, Board = Inst(Guide) });
                if (r.Ok)
                    boards = r.Boards;
            }

            AttachResult front = Attach.Evaluate(new AttachRequest { Existing = boards, Support = Gantry, Reach = 14f, Board = Inst(Guide) });
            AttachResult back = Attach.Evaluate(new AttachRequest { Existing = boards, Support = Gantry, Reach = 14f, Board = Inst(Guide), Back = true });
            Program.Check(front.Ok && front.Grew || !front.Ok, "the front is full: another board there makes the beam longer or does not go on");
            Program.Check(back.Ok && !back.Grew, $"on the back it goes on without a longer beam ({back.Reason}, span {back.Span:0.0} m)");
            AssemblyBoard added = back.Boards.Find(b => b.Member == back.NewMember);
            Program.Check(added != null && added.Back, "the new board is on the back");
            Program.Check(Math.Abs(back.SpaceBefore - front.SpaceBefore) > 1f, "the room said is the back's own");
            AttachResult onArm = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard>(), Support = SupportCatalog.ById(SupportCatalog.Cantilever), Reach = 8f, Board = Inst(Guide), Back = true });
            Program.Check(onArm.Ok && !onArm.Boards.Find(b => b.Member == onArm.NewMember).Back, "asked on a cantilever, the board goes on its front");
        }

        private static void Built()
        {
            var front = new List<AssemblyBoard> { B(0, false, Attach.LaneHand, 6f) };
            var back = new List<AssemblyBoard> { B(0, true, Attach.LaneHand, 6f) };
            SignParts f = SignBuilder.BuildAssembly(front, Gantry, 16f), b = SignBuilder.BuildAssembly(back, Gantry, 16f);
            static float FaceNormalZ(SignParts parts)
            {
                float z = 0f;
                foreach (Vec3 n in parts.Faces[0].Mesh.Normals)
                    z += n.Z;
                return z;
            }

            Program.Check(FaceNormalZ(f) > 0f && FaceNormalZ(b) < 0f, "the face of a front board looks to +Z, of a back board to -Z");
            f.Faces[0].Mesh.Bounds(out Vec3 fMin, out Vec3 fMax);
            b.Faces[0].Mesh.Bounds(out Vec3 bMin, out Vec3 bMax);
            Program.Check(fMin.Z > 0f && bMax.Z < 0f, $"in front of the beam and behind it: {fMin.Z:0.000} and {bMax.Z:0.000}");
            Program.Near((fMin.X + fMax.X) * 0.5f, (bMin.X + bMax.X) * 0.5f, 1e-3f, "at the same place along the beam");
            Program.Near(fMax.Y, bMax.Y, 1e-3f, "at the same height");
            Program.Near(f.Metal.VertexCount, b.Metal.VertexCount, 0.5f, "with the same channels and clamps");
            int backPicks = 0;
            foreach (PickShape p in b.Picks)
            {
                if (p.Name == "board" && Math.Abs(Math.Abs(p.Turn) - (float)Math.PI) < 1e-4f)
                    backPicks++;
            }

            Program.Check(backPicks == 1, "and it is picked turned with it");
        }
    }
}
