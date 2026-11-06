using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>Electric boxes on poles (0.12.23; the owner: "加入电箱，加入各种类型的电箱，能挂在立杆，其他杆子竖直部分和路灯杆上").</summary>
    internal static class CabinetTests
    {
        public static void RunAll()
        {
            Program.Run("electric boxes: the kinds in the catalogue", Kinds);
            Program.Run("electric boxes: the body behind the door, its colour and depth", Body);
            Program.Run("electric boxes: on their own post, under a sign, on a lamp post and on the post of an arm or a gantry", Mounting);
            Program.Run("electric boxes: turned, in a tile, and pointed at", Placed);
        }

        private static readonly string[] Ids = { "cab.distribution", "cab.streetlight", "cab.signal", "cab.camera", "cab.telecom", "cab.meter", "cab.steel", "cab.green", "cab.junction" };

        private static SignInstance Box(string id, string more = null)
        {
            SignChoice c = SignCatalog.ChoiceById(id);
            GuideTemplate t = c.Def.Template;
            string spec = more == null ? c.Spec : t.Parse(c.Spec).ToString() + ";" + more;
            return SignInstances.Of(c.Def, t.Parse(spec).ToString());
        }

        private static SupportDef Support(string id) => SupportCatalog.ById(id);

        private static SignParts Alone(SignInstance box, string support, SupportDetail detail = SupportDetail.Full) =>
            SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = box, Slot = 0, Member = 0 } }, Support(support), 0f, detail);

        private static void Kinds()
        {
            foreach (string id in Ids)
            {
                SignChoice c = SignCatalog.ChoiceById(id);
                Program.Check(c != null && c.Group == Cabinets.Group && !c.Def.IsMarking && c.Def.Template.Id == Cabinets.Id, id + " is an electric box in the catalogue");
                Program.Check(c != null && c.Code.Length > 0 && c.Zh.Length > 0 && c.En.Length > 0, id + " has its names");
            }

            Program.Check(SignCatalog.ChoiceById(Cabinets.Id) == null, "the template is listed by its kinds, not by itself");
            SignInstance signal = Box("cab.signal"), junction = Box("cab.junction");
            Program.Near(signal.Def.Width, 0.55f, 1e-4f, "the signal controller is 55 cm wide");
            Program.Near(signal.Def.Height, 0.75f, 1e-4f, "and 75 cm high");
            Program.Near(signal.Board.Box.Depth, 0.32f, 1e-4f, "and 32 cm deep");
            Program.Check(junction.Def.Width < 0.25f && !junction.Board.Box.Hood, "the junction box is small and has no hood");
            Program.Check(Box("cab.camera").Board.Box.Colour == SolidColour.White && Box("cab.steel").Board.Box.Colour == SolidColour.Steel && Box("cab.green").Board.Box.Colour == SolidColour.Green
                && Box("cab.distribution").Board.Box.Colour == SolidColour.LightGray, "white, stainless, green and light grey bodies");
            Program.Check(Box("cab.signal").Board.Box.SideLouvers && !Box("cab.camera").Board.Box.SideLouvers, "louvers in the sides where set");
            foreach (string id in Ids)
                Program.Check(!Attach.IsLarge(Box(id).Def) && SupportPlanner.FitsHost(Box(id).Def), id + " goes on a post or a lamp post");
            Program.Check(Box("cab.distribution", "w=500;h=900").Def.Width <= 1f && Box("cab.distribution", "h=900").Def.Height <= 1.4f, "no larger than 1 x 1.4 m whatever is set");
        }

        private static void Body()
        {
            foreach (string id in Ids)
            {
                SignInstance box = Box(id);
                SignParts parts = Alone(box, SupportCatalog.SinglePole);
                SupportLayout l = parts.Layout;
                float faceZ = SignBuilder.FacePlane(l, box);
                Program.Near(faceZ - l.BackZ, box.Board.Box.Depth, 1e-5f, id + ": the door is as far in front of the back as the box is deep");
                MeshBuilder body = parts.Solids[(int)box.Board.Box.Colour];
                Program.Check(body != null && body.VertexCount > 0, id + ": the body is in its colour");
                body.Bounds(out Vec3 lo, out Vec3 hi);
                float side = box.Board.Box.Hood ? SignBuilder.HoodSide : 0f;
                Program.Near(hi.X - lo.X, box.Def.Width + 2f * side, 2e-3f, id + ": as wide as the door (and the hood over its sides)");
                Program.Near(lo.Z, l.BackZ, 1e-4f, id + ": its back is where a board's back is (the channels are there)");
                Program.Near(hi.Z, faceZ + (box.Board.Box.Hood ? SignBuilder.HoodReach : 0f), 1e-4f, id + ": it reaches the door (and the hood in front of it)");
                Program.Check(hi.Y >= l.BoardTop - 1e-4f && lo.Y >= l.BottomClearance - 1e-4f, id + ": from the lower edge to the top (and the hood on it)");
                parts.Face.Bounds(out Vec3 flo, out Vec3 fhi);
                Program.Near(flo.Z, faceZ, 1e-5f, id + ": the door picture is on the front");
                Program.Check(parts.Solids[(int)SolidColour.Black] != null && parts.Solids[(int)SolidColour.Black].VertexCount > 0, id + ": the handle stands out of the door");

                SignParts far = Alone(box, SupportCatalog.SinglePole, SupportDetail.Reduced);
                Program.Check(far.Solids[(int)box.Board.Box.Colour] != null && far.Solids[(int)box.Board.Box.Colour].VertexCount > 0, id + ": a far tile has the body");
                Program.Check(far.Solids[(int)SolidColour.Black] == null || far.Solids[(int)SolidColour.Black].VertexCount == 0, id + ": and not the handle and the louvers");
            }

            // Without a hood the body is the box alone; the label and the number fit the door at any size.
            SignParts plain = Alone(Box("cab.distribution", "hood=0"), SupportCatalog.SinglePole);
            plain.Solids[(int)SolidColour.LightGray].Bounds(out Vec3 plo, out Vec3 phi);
            Program.Near(phi.X - plo.X, 0.40f, 2e-3f, "no hood: as wide as the door");
            foreach (string size in new[] { "w=15;h=15", "w=100;h=140", "w=15;h=140", "w=100;h=15" })
            {
                SignInstance box = Box("cab.signal", size + ";label=交通信号控制机房;label2=严禁攀爬;no=XH-0001-A;window=1;louver=both");
                GuidePart door = box.Board.Main;
                foreach (GuideElement e in door.Elements)
                    Program.Check(e.X >= -1e-4f && e.Right <= door.W + 1e-4f && e.Y >= -1e-4f && e.Bottom <= door.H + 1e-4f, $"{size}: a {e.Kind} '{e.Text}' is on the door");
            }
        }

        private static void Mounting()
        {
            // On its own post its lower edge is its own; under a sign it is the lowest of the stack.
            SignParts alone = Alone(Box("cab.camera"), SupportCatalog.SinglePole);
            Program.Near(alone.Layout.BottomClearance, 3f, 1e-4f, "alone its lower edge is set (3 m for the camera box)");
            SignInstance sign = SignInstances.Of(SignCatalog.ById("gb.prohib.30"), null), box = Box("cab.distribution");
            var stack = new List<AssemblyBoard> { new AssemblyBoard { Instance = box, Slot = 0, Member = 0 }, new AssemblyBoard { Instance = sign, Slot = 1, Member = 1 } };
            List<AssemblyBoard> ordered = Assembly.Order(stack);
            Program.Check(ordered[ordered.Count - 1].Instance == box, "in a stack the box is under the signs, whichever was put on first");
            AssemblyPlan plan = Assembly.Plan(ordered, Support(SupportCatalog.SinglePole), 0f);
            Placement boxAt = plan.Placed.Find(p => p.Board.Instance == box), signAt = plan.Placed.Find(p => p.Board.Instance == sign);
            Program.Check(boxAt.Top <= signAt.Bottom - Assembly.StackGap + 1e-4f, "and below the sign's lower edge");

            // Put on by pointing: on a sign's post, on a lamp post, on the post of an arm.
            var signOnly = new List<AssemblyBoard> { new AssemblyBoard { Instance = sign, Slot = 0, Member = 0 } };
            AttachResult onPost = Attach.Evaluate(new AttachRequest { Existing = signOnly, Support = Support(SupportCatalog.SinglePole), Board = box });
            Program.Check(onPost.Ok && !onPost.Hung, "a box joins a sign's post: " + onPost.Reason);
            AttachResult onLamp = Attach.Evaluate(new AttachRequest { Existing = new List<AssemblyBoard>(), Support = Support(SupportCatalog.AttachedPole), Board = box });
            Program.Check(onLamp.Ok, "a box goes on a lamp post or a signal pole: " + onLamp.Reason);
            SignInstance screen = SignInstances.Of(SignCatalog.ById("gb.vms.lane"), null);
            var arm = new List<AssemblyBoard> { new AssemblyBoard { Instance = screen, Slot = 0, Member = 0, Lane = 1, OffsetX = 5f } };
            foreach (string hung in new[] { SupportCatalog.Cantilever, SupportCatalog.Gantry })
            {
                AttachResult r = Attach.Evaluate(new AttachRequest { Existing = arm, Support = Support(hung), Reach = 10f, Board = box, OnPost = true });
                Program.Check(r.Ok && r.OnPost, $"a box goes on the post of a {hung}: {r.Reason}");
                SignParts built = SignBuilder.BuildAssembly(r.Boards, r.Support, 10f);
                Program.Check(built.Solids[(int)SolidColour.LightGray] != null && built.Solids[(int)SolidColour.LightGray].VertexCount > 0, hung + ": the box is built on the post");
            }

            // The clamps round a lamp post are there, and nothing of a post of its own.
            SignParts hosted = Alone(box, SupportCatalog.AttachedPole);
            Program.Check(hosted.Metal.VertexCount > 0 && hosted.Feet.Count == 0, "on a lamp post: the channels and clamps, no post or foot of its own");
        }

        private static void Placed()
        {
            SignInstance box = Box("cab.meter");
            SignParts straight = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = box } }, Support(SupportCatalog.SinglePole), 0f);
            SignParts turned = SignBuilder.BuildAssembly(new[] { new AssemblyBoard { Instance = box, Turn = (float)Math.PI / 2f } }, Support(SupportCatalog.SinglePole), 0f);
            straight.Solids[(int)SolidColour.LightGray].Bounds(out Vec3 slo, out Vec3 shi);
            turned.Solids[(int)SolidColour.LightGray].Bounds(out Vec3 tlo, out Vec3 thi);
            Program.Near(thi.X - tlo.X, shi.Z - slo.Z, 2e-3f, "turned a quarter on its post the body turns with the door (its depth across)");
            Program.Near(thi.Z - tlo.Z, shi.X - slo.X, 2e-3f, "and its width along");

            var record = new ItemRecord(SignCatalog.HashOf(Cabinets.Id), new Vec3(5f, 0f, 5f), 0f, SupportCatalog.ById(SupportCatalog.SinglePole).Hash, box.Spec);
            List<ChunkPart> tile = ChunkBuilder.Build(new[] { record }, Vec3.Zero, out int skipped);
            Program.Check(skipped == 0 && tile.Exists(p => p.Material == ChunkBuilder.SolidMaterial(SolidColour.LightGray)) && tile.Exists(p => p.Material.StartsWith(ChunkBuilder.FacePrefix, StringComparison.Ordinal)),
                "a tile has the box's body in its paint and its door as a face");

            PickShape pick = straight.Picks.Find(p => p.Name == "board");
            Program.Check(pick != null && Math.Abs(pick.PlaneZ - SignBuilder.FacePlane(straight.Layout, box)) < 1e-5f, "the box is pointed at on its door");
        }
    }
}
