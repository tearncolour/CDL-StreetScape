using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The mountings of 0.12.2: a sign on a building's wall, flat on it or on a bracket (SupportKind.WallFlat, WallBracket, WallMount), and small boards on the post of a cantilever, a double cantilever
    /// or a gantry (Assembly.OnPost). And the back of the pointed board of 路7 a, which the wall's checks found mirrored.
    /// </summary>
    internal static class WallAndPostTests
    {
        public static void RunAll()
        {
            Program.Run("walls: the supports", WallCatalogue);
            Program.Run("walls: the wall under the cursor (WallMount)", WallMath);
            Program.Run("walls: the layouts", WallLayouts);
            Program.Run("walls: nothing goes into the wall, either way a bracket faces", WallInWorld);
            Program.Run("walls: stacks on a wall", WallStacks);
            Program.Run("walls: the pointed board's back has its point where its face has it", PointedBack);
            Program.Run("post boards: on a cantilever's post", PostOfCantilever);
            Program.Run("post boards: the rules of putting one on", PostRules);
            Program.Run("post boards: a double cantilever and a gantry", PostOthers);
            Program.Run("post boards: the built post", PostBuilt);
        }

        private static SignDef Make(string id, string group, float w, float h, ShapeKind shape = ShapeKind.RoundRect) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = group, Shape = shape, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Small = Make("t.small", "prohibition", 0.8f, 0.8f, ShapeKind.Circle), Warn = Make("t.warn", "warning", 0.9f, 0.8f, ShapeKind.Triangle),
            Plate = Make("t.plate", "guide", 1.2f, 0.5f), Big = Make("t.big", "guide", 4.0f, 2.0f), Tall = Make("t.tall", "mandatory", 0.8f, 1.35f), Door = Make("t.door", "placename", 0.15f, 0.09f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static AssemblyBoard B(SignDef def, int member, int slot, int lane = 0, float x = 0f) => new AssemblyBoard { Instance = Inst(def), Member = member, Slot = slot, Lane = lane, OffsetX = x };

        private static AttachResult Eval(List<AssemblyBoard> existing, SignDef add, SupportDef support, float reach = 0f, bool onPost = false, float offset = float.NaN) =>
            Attach.Evaluate(new AttachRequest { Existing = existing, Support = support, Reach = reach, Board = Inst(add), OnPost = onPost, Offset = offset });

        private static SupportDef S(string id) => SupportCatalog.ById(id);

        private static Vec3 XOf(float yaw) => new Vec3((float)Math.Cos(yaw), 0f, -(float)Math.Sin(yaw));

        private static Vec3 ZOf(float yaw) => new Vec3((float)Math.Sin(yaw), 0f, (float)Math.Cos(yaw));

        private static bool Near(Vec3 a, Vec3 b, float e) => Math.Abs(a.X - b.X) < e && Math.Abs(a.Y - b.Y) < e && Math.Abs(a.Z - b.Z) < e;

        // ------------------------------------------------------------------ walls

        private static void WallCatalogue()
        {
            SupportDef flat = S(SupportCatalog.WallFlat), bracket = S(SupportCatalog.WallBracket), mirror = S(SupportCatalog.WallBracketLeft);
            Program.Check(flat != null && flat.Kind == SupportKind.WallFlat && bracket != null && bracket.Kind == SupportKind.WallBracket && mirror != null && mirror.Kind == SupportKind.WallBracket, "the wall supports exist");
            Program.Check(bracket.Side == 1 && mirror.Side == -1 && bracket.MirrorId == mirror.Id && mirror.MirrorId == bracket.Id, "the bracket and its mirror name each other, reaching to +X and -X");
            Program.Check(!flat.Hidden && !bracket.Hidden && mirror.Hidden, "the mirror is hidden");
            var listed = new List<SupportDef>(SupportCatalog.Offerable);
            Program.Check(listed.Contains(bracket) && listed.Contains(flat) && !listed.Contains(mirror), "the panel lists the bracket, not its mirror");
            Program.Check(SupportCatalog.Offerable.Count == SupportCatalog.All.Count - 7, "only the mirrors (the bracket's and the six cantilevers', 0.12.25, 0.12.26, 0.12.55 and three of 0.12.57) are not listed");
            Program.Check(ReferenceEquals(SupportCatalog.ListedOf(mirror), bracket) && ReferenceEquals(SupportCatalog.ListedOf(flat), flat) && SupportCatalog.ListedOf(null) == null, "a mirror is named as the listed one");
            Program.Check(SupportKinds.OnWall(SupportKind.WallFlat) && SupportKinds.OnWall(SupportKind.WallBracket) && !SupportKinds.OnWall(SupportKind.Attached) && !SupportKinds.Hosted(SupportKind.WallFlat), "the kinds");
            Program.Check(!SupportKinds.Hung(SupportKind.WallBracket) && !SupportKinds.Hung(SupportKind.WallFlat), "a wall's boards are stacked, not hung side by side");
            Program.Check(SupportPlanner.FitsWall(Big, SupportKind.WallFlat) && !SupportPlanner.FitsWall(Big, SupportKind.WallBracket) && SupportPlanner.FitsWall(Plate, SupportKind.WallBracket), "flat on a wall any board; a bracket the small ones");
            Program.Check(SupportPlanner.Auto(Plate).Kind == SupportKind.SinglePole && SupportPlanner.Auto(Big).Kind == SupportKind.DoublePole, "the planner never puts a board on a wall by itself");
            Program.Check(TsMarkers.KindOf(SupportKind.WallFlat) == TsMarkers.KindAttached && TsMarkers.KindOf(SupportKind.WallBracket) == TsMarkers.KindAttached, "MiniMap sees them as attached");
            foreach (string kind in new[] { "wall", "wall-none", "wall-large", "attach-post", "refuse-post-height", "refuse-bracket-height", "support-to-wall", "support-from-wall" })
            {
                foreach (bool zh in new[] { true, false })
                    Program.Check(HintText.Of(new HintInfo { Kind = kind, Name = "附着式", Left = 2.3f, Need = 2.3f, Count = 2 }, zh).Length > 5, kind + ": a line of help in " + (zh ? "Chinese" : "English"));
                Program.Check(Array.IndexOf(HintText.Kinds, kind) >= 0, kind + " is a known kind");
            }

            Program.Check(HintText.Keys(new HintInfo { Kind = "wall", Hung = true, Name = "附着式（墙面支架）" }, true).Exists(k => k[0] == "\\" && k[1].StartsWith("换朝向", StringComparison.Ordinal))
                && !HintText.Keys(new HintInfo { Kind = "wall", Hung = false }, true).Exists(k => k[1].Contains("换朝向")),
                "the key that turns a bracket's board is named for a bracket only");
        }

        private static void WallMath()
        {
            Vec3 ray = new Vec3(0.3f, -0.4f, -1f);
            Program.Check(WallMount.TryNormal(new Vec3(0f, 0f, 1f), ray, out Vec3 n) && Near(n, new Vec3(0f, 0f, 1f), 1e-6f), "a wall facing the viewer");
            Program.Check(WallMount.TryNormal(new Vec3(0f, 0f, -1f), ray, out n) && Near(n, new Vec3(0f, 0f, 1f), 1e-6f), "a normal that points away is turned toward the viewer");
            Program.Check(WallMount.TryNormal(new Vec3(0.6f, 0.3f, 0.8f), new Vec3(-1f, 0f, -1f), out n) && Math.Abs(n.Y) < 1e-6f && Math.Abs(n.Length - 1f) < 1e-5f && n.X > 0f && n.Z > 0f, "a leaning wall is made level");
            Program.Check(!WallMount.TryNormal(new Vec3(0f, 1f, 0f), ray, out _) && !WallMount.TryNormal(new Vec3(0.5f, 0.6f, 0f), ray, out _), "a roof or a slope is not a wall");
            Program.Check(!WallMount.TryNormal(Vec3.Zero, ray, out _) && !WallMount.TryNormal(new Vec3(float.NaN, 0f, 1f), ray, out _), "no normal is no wall");

            // The height: the board's middle at the cursor; near the standard height it is that; else to 10 cm; never too low.
            Program.Near(WallMount.LowerEdge(2.3f + 0.3f, 0.6f, 2.3f), 2.3f, 1e-6f, "at the standard height");
            Program.Near(WallMount.LowerEdge(2.3f + 0.3f + 0.5f, 0.6f, 2.3f), 2.3f, 1e-6f, "half a metre off: still the standard height");
            Program.Near(WallMount.LowerEdge(5.04f, 0.6f, 2.3f), 4.7f, 1e-5f, "higher up: where the cursor is, to 10 cm");
            Program.Near(WallMount.LowerEdge(0.1f, 0.6f, 2.3f), WallMount.MinHeight, 1e-6f, "never below the lowest");
            Program.Near(WallMount.LowerEdge(500f, 0.6f, 2.3f), WallMount.MaxHeight, 1e-6f, "never above the highest");
            Program.Near(WallMount.LowerEdge(float.NaN, 0.6f, 2.3f), 2.3f, 1e-6f, "no height: the standard one");

            // The facings, for walls every way round.
            for (int i = 0; i < 16; i++)
            {
                double a = i * Math.PI / 8.0 + 0.1;
                var normal = new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                float flat = WallMount.FlatYaw(normal);
                Program.Check(Near(ZOf(flat), normal, 1e-5f), "flat: the sign faces out of the wall " + i);
                Program.Check(Near(WallMount.NormalOf(SupportKind.WallFlat, flat, 1), normal, 1e-5f), "flat: the wall from the yaw " + i);
                foreach (int facing in new[] { 1, -1 })
                {
                    float yaw = WallMount.BracketYaw(normal, facing, out int side);
                    Program.Check(side == facing, "the bracket's side follows its facing");
                    Program.Check(Near(XOf(yaw) * side, normal, 1e-5f), "the arm goes out of the wall " + i + " " + facing);
                    Program.Check(Near(ZOf(yaw), WallMount.Along(normal) * facing, 1e-5f), "the board faces along the wall " + i + " " + facing);
                    Program.Check(Near(WallMount.NormalOf(SupportKind.WallBracket, yaw, side), normal, 1e-5f) && WallMount.FacingOf(yaw, side) == facing, "the wall and the facing from the yaw " + i + " " + facing);
                    // Changing the support keeps the wall: to flat and back.
                    float toFlat = WallMount.Convert(SupportKind.WallBracket, side, yaw, SupportKind.WallFlat, 1, out int flatSide);
                    Program.Check(flatSide == 1 && Near(ZOf(toFlat), normal, 1e-5f), "a bracket made flat faces out of the same wall");
                    float turned = WallMount.Convert(SupportKind.WallBracket, side, yaw, SupportKind.WallBracket, -facing, out int turnedSide);
                    Program.Check(turnedSide == -side && Near(XOf(turned) * turnedSide, normal, 1e-5f) && Near(ZOf(turned), WallMount.Along(normal) * -facing, 1e-5f), "a bracket turned round still goes out of the wall");
                }

                float fromFlat = WallMount.Convert(SupportKind.WallFlat, 1, flat, SupportKind.WallBracket, 1, out int bracketSide);
                Program.Check(bracketSide == 1 && Near(XOf(fromFlat), normal, 1e-5f), "a flat sign made a bracket goes out of the same wall");
            }

            var wallNormal = new Vec3(0f, 0f, 1f);
            Program.Check(WallMount.FacingToward(wallNormal, Vec3.Zero, new Vec3(-10f, 2f, 5f)) == 1 && WallMount.FacingToward(wallNormal, Vec3.Zero, new Vec3(10f, 2f, 5f)) == -1, "a bracket's board faces the viewer's side");
            Program.Check(Near(WallMount.Along(wallNormal), new Vec3(-1f, 0f, 0f), 1e-6f), "along the wall: the right of one who faces it (-X when the wall faces +Z, Unity's frame)");
            Program.Near(WallMount.Wrap(7f), 7f - 2f * (float)Math.PI, 1e-5f, "angles are wrapped");
        }

        private static void WallLayouts()
        {
            SupportDef flat = S(SupportCatalog.WallFlat), bracket = S(SupportCatalog.WallBracket), mirror = S(SupportCatalog.WallBracketLeft);
            SupportLayout f = SupportLayouts.Of(Plate, flat, 0f);
            Program.Near(f.BottomClearance, SupportCatalog.RoadsideClearance, 1e-6f, "flat: the sign's own clearance by default");
            Program.Near(SupportLayouts.Of(Plate, flat, 3.4f).BottomClearance, 3.4f, 1e-6f, "the reach is the height of the lower edge");
            Program.Near(f.BackZ, SupportLayouts.WallGap, 1e-6f, "flat: the back on the spacers");
            Program.Check(f.PoleX.Length == 0 && f.PoleRadius == 0f, "no post");
            SupportLayout b = SupportLayouts.Of(Plate, bracket, 2.6f), m = SupportLayouts.Of(Plate, mirror, 2.6f);
            Program.Near(b.BottomClearance, 2.6f, 1e-6f, "a bracket at its height");
            Program.Near(b.ArmLength, SupportLayouts.BracketWallGap + Plate.Width + SupportLayouts.BracketTip, 1e-5f, "the arm: the gap, the board and a little more");
            Program.Near(b.BoardCentreX, SupportLayouts.BracketWallGap + Plate.Width * 0.5f, 1e-5f, "the board out from the wall");
            Program.Near(m.BoardCentreX, -b.BoardCentreX, 1e-6f, "the mirror reaches the other way");
            Program.Near(b.ArmY, b.BoardTop + SupportLayouts.BracketHang + SupportLayouts.BracketArmRadius, 1e-5f, "the arm above the board");
            Program.Near(b.BackZ, -SupportLayouts.BoardThickness * 0.5f, 1e-6f, "the board in the plane of the arm");

            // The mirror is the bracket turned left for right.
            SignParts pb = SignBuilder.Build(Inst(Plate), bracket, 2.6f), pm = SignBuilder.Build(Inst(Plate), mirror, 2.6f);
            pb.Metal.Bounds(out Vec3 bMin, out Vec3 bMax);
            pm.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
            Program.Check(Math.Abs(bMin.X + mMax.X) < 1e-4f && Math.Abs(bMax.X + mMin.X) < 1e-4f && Math.Abs(bMin.Y - mMin.Y) < 1e-5f && Math.Abs(bMax.Y - mMax.Y) < 1e-5f, "the mirror's metal is the bracket's turned left for right");
            Program.Check(pb.Metal.VertexCount == pm.Metal.VertexCount, "as much of it");
            Program.Check(pb.Picks.Exists(p => p.Name == "arm") && pb.Picks.Exists(p => p.Name == "plate"), "a bracket's arm and plate can be pointed at");
            SignParts pf = SignBuilder.Build(Inst(Plate), flat, 2.6f);
            Program.Check(pf.Picks.TrueForAll(p => p.Name == "board"), "flat on a wall only the board can be pointed at");
            Program.Check(pf.Concrete.VertexCount == 0 && pb.Concrete.VertexCount == 0, "no foundation");

            // A bracket's board hangs on two bars when it is a rectangle, on one at its middle when it is round (its outline is highest there): a bar is a box from the arm's axis down into the board's top
            // edge, so its corners are at those two heights at those places.
            bool BarAt(SignDef def, float k)
            {
                SignParts parts = SignBuilder.Build(Inst(def), bracket, 2.6f);
                float cx = parts.Layout.BoardCentreX, x = cx + k * def.Width, top = parts.Layout.BoardTop, below = parts.Layout.ArmY - SupportLayouts.BracketArmRadius;
                bool down = parts.Metal.Positions.Exists(v => Math.Abs(v.Y - (top - 0.04f)) < 0.002f && Math.Abs(v.X - x) < 0.014f);
                bool up = parts.Metal.Positions.Exists(v => Math.Abs(v.Y - parts.Layout.ArmY) < 0.002f && Math.Abs(v.X - x) < 0.014f && Math.Abs(v.Z) < SupportLayouts.BoardThickness);
                return down && up && below > top;
            }

            SignDef square = Make("t.sq", "x", 0.8f, 0.8f);
            Program.Check(BarAt(square, -0.3f) && BarAt(square, 0.3f) && !BarAt(square, 0f), "a rectangle on two bars");
            Program.Check(BarAt(Small, 0f) && !BarAt(Small, 0.3f) && !BarAt(Small, -0.3f), "a round board on one, at its middle");
        }

        /// <summary>The parts of a sign placed at p with a yaw, in the world: every vertex of a mesh.</summary>
        private static IEnumerable<Vec3> World(MeshBuilder mesh, Vec3 p, float yaw)
        {
            Vec3 x = XOf(yaw), z = ZOf(yaw);
            foreach (Vec3 v in mesh.Positions)
                yield return p + x * v.X + new Vec3(0f, v.Y, 0f) + z * v.Z;
        }

        private static void WallInWorld()
        {
            var p = new Vec3(100f, 12f, -40f);
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4.0 + 0.3;
                var normal = new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                float flatYaw = WallMount.FlatYaw(normal);
                SignParts flat = SignBuilder.Build(Inst(Plate), S(SupportCatalog.WallFlat), 2.3f);
                float deepest = float.MaxValue, faceNear = float.MaxValue;
                foreach (Vec3 v in World(flat.Metal, p, flatYaw))
                    deepest = Math.Min(deepest, Vec3.Dot(v - p, normal));
                foreach (Vec3 v in World(flat.Face, p, flatYaw))
                    faceNear = Math.Min(faceNear, Vec3.Dot(v - p, normal));
                Program.Check(deepest >= -1e-4f && faceNear > SupportLayouts.WallGap, "flat " + i + ": nothing in the wall, the face in front of it: " + deepest + ", " + faceNear);

                foreach (int facing in new[] { 1, -1 })
                {
                    float yaw = WallMount.BracketYaw(normal, facing, out int side);
                    SupportDef support = side > 0 ? S(SupportCatalog.WallBracket) : S(SupportCatalog.WallBracketLeft);
                    SignParts parts = SignBuilder.Build(Inst(Plate), support, 2.3f);
                    float least = float.MaxValue, boardNear = float.MaxValue;
                    foreach (Vec3 v in World(parts.Metal, p, yaw))
                        least = Math.Min(least, Vec3.Dot(v - p, normal));
                    foreach (Vec3 v in World(parts.Face, p, yaw))
                        boardNear = Math.Min(boardNear, Vec3.Dot(v - p, normal));
                    Program.Check(least >= -1e-4f, "bracket " + i + " facing " + facing + ": nothing goes into the wall: " + least);
                    Program.Check(boardNear >= SupportLayouts.BracketWallGap - 1e-3f, "bracket " + i + " facing " + facing + ": the board clear of the wall: " + boardNear);
                    // The face is seen from along the wall, the way the bracket faces.
                    Program.Check(Near(ZOf(yaw), WallMount.Along(normal) * facing, 1e-5f), "bracket " + i + ": the face looks along the wall");
                }
            }
        }

        private static void WallStacks()
        {
            SupportDef flat = S(SupportCatalog.WallFlat), bracket = S(SupportCatalog.WallBracket);
            AttachResult bigOnWall = Eval(new List<AssemblyBoard> { B(Big, 0, 0) }, Big, flat);
            Program.Check(bigOnWall.Ok && !bigOnWall.Hung, "flat on a wall a large board goes on another: " + bigOnWall.Reason);
            Program.Check(bigOnWall.SpaceLeft > 0f && bigOnWall.SpaceLeft <= Attach.MaxWallStack, "the room on a wall is counted");
            AttachResult bigOnBracket = Eval(new List<AssemblyBoard> { B(Plate, 0, 0) }, Big, bracket);
            Program.Check(!bigOnBracket.Ok && bigOnBracket.Reason == AttachReason.Large, "a bracket takes small boards only");
            AttachResult two = Eval(new List<AssemblyBoard> { B(Plate, 0, 0) }, Plate, bracket);
            Program.Check(two.Ok && two.Count == 2, "two plates under one bracket");
            AttachResult tooTall = Eval(new List<AssemblyBoard> { B(Tall, 0, 0) }, Tall, bracket);
            Program.Check(!tooTall.Ok && tooTall.Reason == AttachReason.NoHeight, "a bracket's stack is 2 m at most: " + tooTall.Reason);
            Program.Check(Attach.MaxStackFor(SupportKind.WallBracket) == Attach.MaxBracketStack && Attach.MaxStackFor(SupportKind.SinglePole) == Attach.MaxPostStack, "the limits by kind");
            AttachResult four = Eval(new List<AssemblyBoard> { B(Door, 0, 0), B(Door, 1, 1), B(Door, 2, 2), B(Door, 3, 3) }, Door, flat);
            Program.Check(!four.Ok && four.Reason == AttachReason.Full, "four boards on a wall at most (4.8.2)");

            // A stack under a bracket: each board under the one above, the top one under the arm, the lowest at the height.
            List<AssemblyBoard> stack = Assembly.Order(new List<AssemblyBoard> { B(Small, 0, 0), B(Warn, 1, 1) });
            AssemblyPlan plan = Assembly.Plan(stack, bracket, 2.5f);
            Program.Near(plan.Placed[plan.Placed.Count - 1].Bottom, 2.5f, 1e-5f, "the lowest board at the height");
            Program.Check(plan.Placed[0].Top < plan.Layout.ArmY - SupportLayouts.BracketArmRadius, "the top board under the arm");
            Program.Check(plan.Placed.TrueForAll(pl => Math.Abs(pl.Cx - plan.Layout.BoardCentreX) < 1e-5f), "all of them out from the wall at the same place");
        }

        private static void PointedBack()
        {
            SignDef def = SignCatalog.ById("gb.guide8.07");
            GuideTemplate t = def.Template;
            foreach (string form in new[] { "r", "l" })
            {
                SignInstance instance = SignInstances.Of(def, t.Parse(t.Default().ToString()).With("form", form).With("name", "福宁路").ToString());
                foreach (string id in new[] { SupportCatalog.SinglePole, SupportCatalog.WallFlat })
                {
                    SignParts parts = SignBuilder.Build(instance, S(id));
                    parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                    float backMin = float.MaxValue, backMax = float.MinValue;
                    for (int i = 0; i < parts.Metal.VertexCount; i++)
                    {
                        if (Math.Abs(parts.Metal.Positions[i].Z - parts.Layout.BackZ) > 1e-5f || parts.Metal.Normals[i].Z > -0.999f)
                            continue;
                        backMin = Math.Min(backMin, parts.Metal.Positions[i].X);
                        backMax = Math.Max(backMax, parts.Metal.Positions[i].X);
                    }

                    Program.Check(Math.Abs(backMin - fMin.X) < 0.005f && Math.Abs(backMax - fMax.X) < 0.005f, $"路7 a {form} on {id}: the back is the face's outline: back {backMin}..{backMax}, face {fMin.X}..{fMax.X}");
                }
            }
        }

        // ------------------------------------------------------------------ boards on the post of a hung support

        private static void PostOfCantilever()
        {
            SupportDef arm = S(SupportCatalog.Cantilever);
            List<AssemblyBoard> without = Assembly.Order(new List<AssemblyBoard> { B(Plate, 0, 0) });
            List<AssemblyBoard> with = Assembly.Order(new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost) });
            AssemblyPlan a = Assembly.Plan(without, arm, 0f), b = Assembly.Plan(with, arm, 0f);
            Program.Check(b.Hung && b.Placed.Count == 2, "a cantilever with a board on its arm and one on its post");
            Placement onArm = b.Placed.Find(p => p.Board.Member == 0), onPost = b.Placed.Find(p => p.Board.Member == 1);
            Program.Check(!onArm.OnPost && onPost.OnPost, "which is where");
            Program.Near(onArm.Cx, a.Placed[0].Cx, 1e-5f, "the board on the arm does not move for the one on the post");
            Program.Near(b.Layout.ArmLength, a.Layout.ArmLength, 1e-5f, "nor does the arm");
            Program.Near(b.Layout.ArmY, a.Layout.ArmY, 1e-5f, "nor its height");
            Program.Near(onPost.Cx, 0f, 1e-6f, "the board on the post is centred on it");
            Program.Near(onPost.Bottom, SupportCatalog.RoadsideClearance, 1e-5f, "at the roadside clearance");
            Program.Check(onPost.Top <= b.Layout.PostTop + 1e-4f, "under the arm's joint");
            Program.Near(b.Layout.PostTop, b.Layout.ArmYs[b.Layout.ArmYs.Length - 1] - SupportLayouts.JointFlangeOf(b.Layout) - 0.10f, 1e-5f, "under the lowest arm's flange");
            Program.Near(SignBuilder.FacePlaneOf(b.Layout, onPost), b.Layout.PostBackZ + SupportLayouts.BoardThickness, 1e-6f, "its face in front of the post");
            Program.Check(b.Layout.PostBackZ > b.Layout.PoleRadius + SupportLayouts.PlateDepth, "its back clear of the post");
            Program.Near(Assembly.TotalDef(with, true).Width, Assembly.TotalDef(without, true).Width, 1e-5f, "the arm's boards are as wide as without it");
            Program.Check(Assembly.Overlaps(b.Placed, true).Count == 0, "they do not overlap");
            Program.Near(Assembly.RequiredReach(with, SupportKind.Cantilever), 0f, 1e-6f, "the post asks no reach");

            // Two on the post: in the standard's order, the prohibition sign above the warning sign.
            List<AssemblyBoard> two = Assembly.Order(new List<AssemblyBoard> { B(Plate, 0, 0), B(Warn, 1, 1, Assembly.OnPost), B(Small, 2, 2, Assembly.OnPost) });
            AssemblyPlan c = Assembly.Plan(two, arm, 0f);
            Placement warn = c.Placed.Find(p => p.Board.Member == 1), small = c.Placed.Find(p => p.Board.Member == 2);
            Program.Check(small.Bottom > warn.Top, "the prohibition sign above the warning sign");
            Program.Near(small.Bottom - warn.Top, Assembly.StackGap, 1e-5f, "a stack's gap between them");
            Program.Near(warn.Bottom, SupportCatalog.RoadsideClearance, 1e-5f, "the lower at the clearance");
            Program.Near(Assembly.PostStackHeight(two), small.Top - warn.Bottom, 1e-5f, "the stack's height");
            Program.Check(Assembly.Overlaps(c.Placed, true).Count == 0, "a stack on the post is not an overlap");

            // Only post boards: the arm is laid out for no board, and nothing is broken.
            AssemblyPlan lone = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Small, 0, 0, Assembly.OnPost) }), arm, 0f);
            Program.Check(lone.Layout.ArmLength >= arm.ArmLength - 1e-4f && !float.IsNaN(lone.Layout.ArmY) && lone.Placed[0].OnPost && lone.Layout.PostTop > lone.Placed[0].Top, "a board on the post alone");
            Program.Check(SignBuilder.BuildAssembly(lone.Placed.ConvertAll(p => p.Board), arm, 0f).Faces.Count == 1, "and it is built");

            // A support that has no such post stacks every board: the lane is not looked at.
            AssemblyPlan pole = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost) }), S(SupportCatalog.SinglePole), 0f);
            Program.Check(!pole.Hung && pole.Placed.TrueForAll(p => !p.OnPost), "on a single post the lane of the post is nothing");
        }

        private static void PostRules()
        {
            SupportDef arm = S(SupportCatalog.Cantilever);
            var existing = new List<AssemblyBoard> { B(Plate, 0, 0) };
            AttachResult ok = Eval(existing, Small, arm, 0f, true);
            Program.Check(ok.Ok && ok.OnPost && ok.Hung, "a small board goes on the post: " + ok.Reason);
            AssemblyBoard added = ok.Boards.Find(x => x.Member == ok.NewMember);
            Program.Check(added != null && added.Lane == Assembly.OnPost && added.OffsetX == 0f, "it is marked as on the post");
            Program.Check(ok.Count == 1 && ok.Index == 0, "the first on the post");
            AssemblyBoard kept = ok.Boards.Find(x => x.Member == 0);
            Program.Check(kept.Lane == 0, "the board on the arm is left as it was");
            Program.Near(ok.SpaceLeft, Attach.PostRoom(Assembly.Plan(ok.Boards, arm, 0f).Layout) - Small.Height, 1e-4f, "the room left under the arm");
            AttachResult large = Eval(existing, Big, arm, 0f, true);
            Program.Check(!large.Ok && large.Reason == AttachReason.Large && large.OnPost, "a large board does not go on the post");
            var stacked = new List<AssemblyBoard> { B(Plate, 0, 0), B(Tall, 1, 1, Assembly.OnPost), B(Tall, 2, 2, Assembly.OnPost) };
            AttachResult tall = Eval(stacked, Tall, arm, 0f, true);
            Program.Check(!tall.Ok && tall.Reason == AttachReason.NoHeight, "three tall boards do not fit under the arm of a small cantilever: " + tall.Reason + ", room " + tall.SpaceBefore);
            Program.Check(Attach.PostRoom(Assembly.Plan(Assembly.Order(stacked), arm, 0f).Layout) < Attach.MaxPostStack, "the arm's joint is lower than a post's stack");
            var full = new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost), B(Plate, 2, 2), B(Small, 3, 3, Assembly.OnPost) };
            Program.Check(Eval(full, Small, arm, 0f, true).Reason == AttachReason.Full, "four boards on a cantilever in all, the post's counted");

            // The boards on the post take no room on the arm: a board put on the arm goes where it would without them.
            AttachResult beside = Eval(new List<AssemblyBoard> { B(Plate, 0, 0) }, Plate, arm, 0f, false, 3f);
            AttachResult besidePost = Eval(new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost) }, Plate, arm, 0f, false, 3f);
            Program.Check(beside.Ok && besidePost.Ok && !besidePost.OnPost, "pointing at the arm puts it on the arm");
            Program.Near(besidePost.Offset, beside.Offset, 1e-4f, "at the same place as without the board on the post");
            Program.Check(besidePost.Boards.Find(x => x.Member == 1).Lane == Assembly.OnPost, "the board on the post stays there");
            // A support without a post for boards ignores the wish.
            AttachResult onPole = Eval(new List<AssemblyBoard> { B(Plate, 0, 0) }, Small, S(SupportCatalog.SinglePole), 0f, true);
            Program.Check(onPole.Ok && !onPole.OnPost && !onPole.Hung, "on a single post it is an ordinary stack");
            Program.Check(Attach.LanePost == Assembly.OnPost && Assembly.OnPost < Attach.LaneHand, "the lane byte of the post");
        }

        private static void PostOthers()
        {
            SupportDef twin = S(SupportCatalog.DoubleCantilever), gantry = S(SupportCatalog.Gantry);
            var arms = new List<AssemblyBoard> { B(Plate, 0, 0), B(Plate, 1, 1), B(Plate, 2, 2), B(Plate, 3, 3), B(Small, 4, 4, Assembly.OnPost) };
            int[] side = Assembly.Arms(Assembly.Order(arms));
            Program.Check(Array.IndexOf(side, 0) >= 0 && Array.FindAll(side, s => s != 0).Length == 4, "a double cantilever: the post's board on no arm");
            AssemblyPlan p2 = Assembly.Plan(Assembly.Order(arms), twin, 0f);
            Program.Check(p2.Placed.Find(p => p.Board.Member == 4).OnPost && Assembly.Overlaps(p2.Placed, true).Count == 0, "on the post of a double cantilever, overlapping nothing");
            AttachResult moreOnArm = Eval(arms, Plate, twin);
            Program.Check(moreOnArm.Ok, "an arm still takes its four: the post's board is not on one");

            AssemblyPlan g = Assembly.Plan(Assembly.Order(new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost) }), gantry, 16f);
            Placement post = g.Placed.Find(p => p.Board.Member == 1);
            Program.Check(post.OnPost && Math.Abs(post.Cx) < 1e-6f, "a gantry's board on its first post");
            Program.Near(g.Layout.PostTop, g.Layout.HangY - SupportLayouts.JointFlangeOf(g.Layout) - 0.10f, 1e-5f, "under the flange of the gantry's lowest chord");
            Program.Check(post.Top <= g.Layout.PostTop && g.Layout.PostBackZ > g.Layout.PoleRadius, "under it, in front of the post");
            Program.Check(Eval(new List<AssemblyBoard> { B(Plate, 0, 0) }, Small, gantry, 16f, true).OnPost, "and it is put there by pointing at the post");
        }

        private static void PostBuilt()
        {
            SupportDef arm = S(SupportCatalog.Cantilever);
            var boards = new List<AssemblyBoard> { B(Plate, 0, 0), B(Small, 1, 1, Assembly.OnPost) };
            SignParts withPost = SignBuilder.BuildAssembly(boards, arm, 0f), plain = SignBuilder.BuildAssembly(new List<AssemblyBoard> { B(Plate, 0, 0) }, arm, 0f);
            Program.Check(withPost.Faces.Count == 2, "two faces");
            Program.Check(withPost.Metal.VertexCount > plain.Metal.VertexCount + 40, "the post board has its channels and clamps");
            Placement onPost = Assembly.Plan(Assembly.Order(boards), arm, 0f).Placed.Find(p => p.OnPost);
            // The hoops' backings (22MR601 p.141 抱箍衬底), 4 cm wide, reach from inside the post to the channel on the board's back.
            float channel = withPost.Layout.PostBackZ - SupportLayouts.RailDepth, r = withPost.Layout.PoleRadius;
            bool front = false, inPost = false;
            foreach (Vec3 v in withPost.Metal.Positions)
            {
                if (v.Y < onPost.Bottom || v.Y > onPost.Top || Math.Abs(Math.Abs(v.X) - 0.02f) > 1e-3f)
                    continue;
                front |= Math.Abs(v.Z - channel) < 1e-3f;
                inPost |= v.Z < r && v.Z > r - 0.02f;
            }

            Program.Check(front && inPost, "the hoops' backings go from inside the post to the channel");
            FacePart face = withPost.Faces.Find(fp => fp.Instance.Def == Small);
            face.Mesh.Bounds(out Vec3 fMin, out Vec3 fMax);
            Program.Near(fMin.Z, withPost.Layout.PostBackZ + SupportLayouts.BoardThickness, 1e-5f, "the post board's face in its plane");
            Program.Check(fMin.X < 0f && fMax.X > 0f && fMin.Y >= SupportCatalog.RoadsideClearance - 1e-4f, "in front of the post, above the clearance");
            PickShape pick = withPost.Picks.Find(ps => ps.Name == "board" && ps.Member == 1);
            Program.Check(pick != null && Math.Abs(pick.Min.Z - withPost.Layout.PostBackZ) < 1e-5f, "it is pointed at where it is");
            Program.Check(SignBuilder.BuildAssembly(boards, arm, 0f, SupportDetail.Reduced).Faces.Count == 2, "and in reduced detail");
        }
    }
}
