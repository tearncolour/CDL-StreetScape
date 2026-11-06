using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The support over the road (0.12.80; SupportKind.OverRoad, attached.over; GB 5768.2 4.8.12 d, Fig. 8 a): which structure over a road carries the board, how high its lower edge is, the layout and the built
    /// parts, the catalogue entry that an older build must not choke on, and the rules that keep it off the other supports. The game's reads (the nets, the raycast) are not here: they are the tool's.
    /// </summary>
    internal static class OverRoadTests
    {
        public static void RunAll()
        {
            Program.Run("over road: the support in the catalogue and its kind", Catalogue);
            Program.Run("over road: which structure carries the board", Finding);
            Program.Run("over road: the lower edge and the room under the structure", LowerEdge);
            Program.Run("over road: an edge of the game's nets is a structure or it is not", Describing);
            Program.Run("over road: the layout is flat on the face, as on a wall", Layout);
            Program.Run("over road: the built board is closed and nothing floats", Built);
            Program.Run("over road: not a standing support, not a wall, one board", Rules);
            Program.Run("over road: the line of help has text and keys in both languages", Hints);
            Program.Run("over road: saved and read back, an older build meets the hash", Saved);
            Program.Run("over road: no board on the older supports is changed by it", OlderSupports);
        }

        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Plate = Make("t.over.plate", 2.4f, 1.2f), Tall = Make("t.over.tall", 1.0f, 3.5f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        /// <summary>A road along +X on the ground (y 0), 14 m wide, and a bridge along +Z across it at x = 50 whose underside is 6 m up (centre line 7 m, cross-section from -1 to +0.6 m: a face 1.6 m high).</summary>
        private static Bezier3 Road => Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f));

        private static OverStructure Bridge(int id = 7, float x = 50f, float y = 7f, float lowest = -1f, float highest = 0.6f) => new OverStructure
        {
            Curve = Bezier3.Line(new Vec3(x, y, -40f), new Vec3(x, y, 40f)), Width = 12f, Lowest = lowest, Highest = highest, Id = id
        };

        private static void Catalogue()
        {
            SupportDef over = SupportCatalog.ById(SupportCatalog.OverRoad);
            Program.Check(over != null && over.Kind == SupportKind.OverRoad && over.Id == "attached.over", "the support is in the catalogue as attached.over");
            Program.Check(SupportCatalog.ByHash(over.Hash) == over, "found again by its hash");
            Program.Check((int)SupportKind.OverRoad == (int)SupportKind.AttachedArm + 1, "the kind is appended after the last one (saved by number)");
            Program.Check(Array.IndexOf(Enum.GetValues(typeof(SupportKind)), SupportKind.OverRoad) == Enum.GetValues(typeof(SupportKind)).Length - 1, "and it is the last of the kinds");
            Program.Check(!over.Hidden && System.Linq.Enumerable.Contains(SupportCatalog.Listed, over), "listed in the panel");
            Program.Check(over.Zh.Length > 0 && over.En.Length > 0, "named in both languages");
            Program.Check(SupportFamilies.Of(over) == SupportFamilies.Attached && over.Family == SupportFamilies.Attached, "in the attached family of the panel, not among the posts");

            // A build before this one meets the hash and does not know it: the board is drawn on the support the planner makes for it, and the hash is not rewritten (SupportCatalog.For).
            Program.Check(SupportCatalog.ByHash(0xABCD1234u) == null && SupportCatalog.For(Plate, 0xABCD1234u) == SupportPlanner.Auto(Plate), "an unknown hash falls back to the planner's support");
            var ids = new HashSet<uint>();
            foreach (SupportDef def in SupportCatalog.All)
                Program.Check(ids.Add(def.Hash), "the hash of " + def.Id + " is its own");
        }

        private static void Finding()
        {
            var list = new List<OverStructure> { Bridge() };
            int id = OverRoad.Find(list, Road, 14f, 0f, 0.5f, out OverSpot spot);
            Program.Check(id == 7 && spot.Structure == 7, "a bridge across the road over the point is found: " + id);
            Program.Near(spot.Underside, 6f, 1e-4f, "its underside is 6 m over the road (centre 7 m, lowest part 1 m under it)");
            Program.Near(spot.FaceHeight, 1.6f, 1e-4f, "and its face is 1.6 m high (from the lowest part to the highest)");
            Program.Near(spot.Run.LengthXZ, 1f, 1e-4f, "the structure's heading is a unit vector");
            Program.Near(Math.Abs(spot.Run.Z), 1f, 1e-3f, "it runs along Z, across the road");
            Program.Near(spot.Face.LengthXZ, 1f, 1e-4f, "the face's normal is a unit vector");
            Program.Check(Math.Abs(Vec3.Dot(spot.Face, spot.Run)) < 1e-4f, "the face is square to the structure's heading");
            // The road runs toward +X: its oncoming traffic comes from +X, so the face looks toward -X (against the road's heading), where the drivers who come along it are.
            Program.Check(spot.Face.X < -0.99f, "the face looks at the oncoming traffic: " + spot.Face);
            Program.Near(spot.Point.X, 50f - 6f, 1e-3f, "the point is on the face, half the structure's width (6 m) from its centre line");
            Program.Near(spot.Point.Y, 6f, 1e-3f, "at the structure's lowest height");

            // The other way along the road: the face turns.
            Bezier3 back = Bezier3.Line(new Vec3(100f, 0f, 0f), new Vec3(0f, 0f, 0f));
            Program.Check(OverRoad.Find(list, back, 14f, 0f, 0.5f, out OverSpot spotBack) == 7 && spotBack.Face.X > 0.99f, "on the road's other direction the face is the other one");

            Program.Check(OverRoad.Find(new List<OverStructure>(), Road, 14f, 0f, 0.5f, out _) == -1, "no structure: none");
            Program.Check(OverRoad.Find(new List<OverStructure> { Bridge(x: 90f) }, Road, 14f, 0f, 0.1f, out _) == -1, "a bridge 40 m along the road is not over the point");
            Program.Check(OverRoad.Find(new List<OverStructure> { Bridge(y: 3f) }, Road, 14f, 0f, 0.5f, out _) == -1, "an underside 2 m over the road (less than MinFree) is no structure to hang a board on");
            Program.Check(OverRoad.Find(new List<OverStructure> { Bridge(y: 30f) }, Road, 14f, 0f, 0.5f, out _) == -1, "one 29 m up is too far to read a board from");

            // A viaduct along the road is above it, with no face across the traffic.
            var along = new OverStructure { Curve = Bezier3.Line(new Vec3(-40f, 8f, 0f), new Vec3(140f, 8f, 0f)), Width = 12f, Lowest = -1f, Highest = 0.4f, Id = 9 };
            Program.Check(OverRoad.Find(new List<OverStructure> { along }, Road, 14f, 0f, 0.5f, out _) == -1, "a structure along the road is not one across it");
            var skew = new OverStructure { Curve = Bezier3.Line(new Vec3(30f, 7f, -30f), new Vec3(70f, 7f, 30f)), Width = 12f, Lowest = -1f, Highest = 0.4f, Id = 11 };
            Program.Check(OverRoad.Find(new List<OverStructure> { skew }, Road, 14f, 0f, 0.5f, out _) == 11, "a skew crossing of 56 degrees counts");
            var shallow = new OverStructure { Curve = Bezier3.Line(new Vec3(0f, 7f, -25f), new Vec3(100f, 7f, 25f)), Width = 12f, Lowest = -1f, Highest = 0.4f, Id = 12 };
            Program.Check(OverRoad.Find(new List<OverStructure> { shallow }, Road, 14f, 0f, 0.5f, out _) == -1, "a crossing of 27 degrees (under MinCrossDegrees) does not");

            // The lowest of two that cross at once is the one a driver meets first.
            int low = OverRoad.Find(new List<OverStructure> { Bridge(id: 1, y: 11f), Bridge(id: 2, y: 8f), Bridge(id: 3, y: 13f) }, Road, 14f, 0f, 0.5f, out OverSpot lowSpot);
            Program.Check(low == 2 && lowest(lowSpot), "of several the lowest underside carries it");

            // The road's own height is taken from the road: a road 20 m up under a bridge 27 m up has the same 6 m.
            Bezier3 raised = Bezier3.Line(new Vec3(0f, 20f, 0f), new Vec3(100f, 20f, 0f));
            Program.Near(OverRoad.Find(new List<OverStructure> { Bridge(y: 27f) }, raised, 14f, 20f, 0.5f, out OverSpot up) == 7 ? up.Underside : float.NaN, 6f, 1e-4f, "heights are measured from the road, not from the ground");
        }

        private static bool lowest(OverSpot spot) => Math.Abs(spot.Underside - 7f) < 1e-3f;

        private static void LowerEdge()
        {
            Program.Near(OverRoad.LowerEdge(6f, 1.6f, 1.2f), 6f + OverRoad.TopGap, 1e-5f, "the lower edge is the gap over the face's lowest part: the board is low in the face (Fig. 8 a)");
            Program.Check(OverRoad.LowerEdge(6f, 1.6f, 1.2f) >= OverRoad.MinFree, "so it leaves at least the free height under it");
            Program.Check(float.IsNaN(OverRoad.LowerEdge(4f, 2f, 1.2f)), "a face 4 m over the road is too low a structure: under MinFree");
            // The board stands wholly within the face: a face lower than the board and its two gaps refuses it, whatever else.
            Program.Check(OverRoad.Fits(6f, OverRoad.FaceNeeded(1.2f) + 1e-3f, 1.2f) && !OverRoad.Fits(6f, OverRoad.FaceNeeded(1.2f) - 1e-3f, 1.2f), "exactly at the limit: the board and a gap above and below");
            Program.Near(OverRoad.FaceNeeded(1.2f), 1.2f + 2f * OverRoad.TopGap, 1e-6f, "FaceNeeded is the board and its two gaps");
            Program.Check(float.IsNaN(OverRoad.LowerEdge(6f, 1.0f, 1.2f)), "a face lower than the board refuses it (its spacers would hang in the air)");
            Program.Check(float.IsNaN(OverRoad.LowerEdge(6f, float.NaN, 1.2f)), "no face height: refused");
            Program.Check(!OverRoad.Fits(6f, 3.5f, 3.6f) && OverRoad.Fits(6f, 4f, 3.6f), "a tall board needs a tall face");
            Program.Check(OverRoad.MinFree == 4.5f && OverRoad.TopGap > 0f && OverRoad.MaxUnderside > OverRoad.MinFree, "the constants are as documented");
        }

        private static void Describing()
        {
            Bezier3 curve = Bezier3.Line(new Vec3(0f, 7f, 0f), new Vec3(10f, 7f, 0f));
            Program.Check(OverRoad.Describe(true, false, 12f, -1f, 0.4f, 5, curve, out OverStructure s) && s.Id == 5 && s.Width == 12f && s.Lowest == -1f && s.Highest == 0.4f, "an elevated edge is a structure");
            Program.Check(!OverRoad.Describe(false, false, 12f, -1f, 0.4f, 5, curve, out _), "a road on the ground is not");
            Program.Check(!OverRoad.Describe(true, true, 12f, -1f, 0.4f, 5, curve, out _), "a tunnel is not");
            Program.Check(!OverRoad.Describe(true, false, 0f, -1f, 0.4f, 5, curve, out _) && !OverRoad.Describe(true, false, float.NaN, -1f, 0.4f, 5, curve, out _), "no width: none");
            Program.Check(!OverRoad.Describe(true, false, 12f, 0.4f, -1f, 5, curve, out _) && !OverRoad.Describe(true, false, 12f, 0.4f, 0.4f, 5, curve, out _), "a lowest part that is not below the highest: none");
            Program.Check(!OverRoad.Describe(true, false, 12f, float.NaN, 0.4f, 5, curve, out _), "not a number: none");
            Bezier3 broken = Bezier3.Line(new Vec3(float.NaN, 7f, 0f), new Vec3(10f, 7f, 0f));
            Program.Check(!OverRoad.Describe(true, false, 12f, -1f, 0.4f, 5, broken, out _), "a curve that is not a number: none");
        }

        private static void Layout()
        {
            SupportDef over = SupportCatalog.ById(SupportCatalog.OverRoad), flat = SupportCatalog.ById(SupportCatalog.WallFlat);
            SupportLayout l = SupportLayouts.Of(Plate, over, 4.9f);
            Program.Near(l.BottomClearance, 4.9f, 1e-6f, "the reach is the height of the lower edge");
            Program.Near(l.BoardTop, 4.9f + Plate.Height, 1e-5f, "the board stands on it");
            Program.Near(l.BackZ, SupportLayouts.WallGap, 1e-6f, "the back on spacers in front of the face");
            Program.Check(l.PoleX.Length == 0 && l.PoleRadius == 0f && l.BoardCentreX == 0f, "no post and no offset");
            SupportLayout w = SupportLayouts.Of(Plate, flat, 4.9f);
            Program.Check(l.BottomClearance == w.BottomClearance && l.BackZ == w.BackZ && l.BoardTop == w.BoardTop && l.PoleTop == w.PoleTop && l.InnerReach == w.InnerReach, "the same as a board flat on a wall");
            Program.Near(SupportLayouts.Of(Plate, over, 0f).BottomClearance, Plate.BottomClearance, 1e-6f, "reach 0: the sign's own clearance");
            Program.Near(SupportLayouts.Of(Plate, over, 4.9f, 7f).BottomClearance, 4.9f, 1e-6f, "a clearance by hand is for a hung support: not this one");
        }

        /// <summary>The corners of the edges used an odd number of times (a closed body has none): where a mesh is open.</summary>
        internal static List<Vec3> OpenCorners(MeshBuilder mesh)
        {
            var found = new List<Vec3>();
            var ids = new Dictionary<(long, long, long), int>();
            var at = new List<Vec3>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                {
                    ids[key] = id = ids.Count;
                    at.Add(p);
                }

                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = Id(mesh.Positions[mesh.Indices[t]]), b = Id(mesh.Positions[mesh.Indices[t + 1]]), c = Id(mesh.Positions[mesh.Indices[t + 2]]);
                if (a == b || b == c || a == c)
                    continue;
                foreach ((int p, int q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var e = p < q ? (p, q) : (q, p);
                    edges[e] = edges.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            }

            foreach (KeyValuePair<(int, int), int> edge in edges)
            {
                if (edge.Value % 2 == 0)
                    continue;
                found.Add(at[edge.Key.Item1]);
                found.Add(at[edge.Key.Item2]);
            }

            return found;
        }

        internal static int Open(MeshBuilder mesh)
        {
            var ids = new Dictionary<(long, long, long), int>();
            int Id(Vec3 p)
            {
                var key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
                if (!ids.TryGetValue(key, out int id))
                    ids[key] = id = ids.Count;
                return id;
            }

            var edges = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                int a = Id(mesh.Positions[mesh.Indices[t]]), b = Id(mesh.Positions[mesh.Indices[t + 1]]), c = Id(mesh.Positions[mesh.Indices[t + 2]]);
                if (a == b || b == c || a == c)
                    continue;
                foreach ((int p, int q) in new[] { (a, b), (b, c), (c, a) })
                {
                    var e = p < q ? (p, q) : (q, p);
                    edges[e] = edges.TryGetValue(e, out int n) ? n + 1 : 1;
                }
            }

            int open = 0;
            foreach (int n in edges.Values)
                open += n % 2;
            return open;
        }

        private static void Built()
        {
            SupportDef over = SupportCatalog.ById(SupportCatalog.OverRoad);
            foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
            {
                SignParts parts = SignBuilder.Build(Inst(Plate), over, 4.9f, detail);
                string what = " (" + detail + ")";
                Program.Check(parts.Metal.VertexCount > 0, "there are spacers" + what);
                Program.Check(parts.Concrete.VertexCount == 0, "no foundation" + what);
                Program.Check(parts.Picks.Count == 1 && parts.Picks[0].Name == "board", "only the board can be pointed at" + what);

                // parts.Metal holds the spacers and the board's own back and rim (z from BackZ to BackZ + BoardThickness, Full detail only). The spacers are the metal behind the board's back: tubes left open at both
                // ends (SignBuilder.BuildWallFlat: one end on the face, the other 2 mm inside the board's back; neither shows), so their openings are in those two planes and nowhere else.
                float back = parts.Layout.BackZ, backEnd = back + 0.002f;
                List<Vec3> open = OpenCorners(parts.Metal).FindAll(p => p.Z < back - 1e-4f);
                Program.Check(open.Count == 0 || open.TrueForAll(p => Math.Abs(p.Z) < 1e-4f), "a spacer is open only on the face (its other end is inside the board)" + what);
                List<Vec3> behind = parts.Metal.Positions.FindAll(p => p.Z <= backEnd + 1e-4f);
                Program.Check(behind.Count > 0 && behind.Exists(p => Math.Abs(p.Z) < 1e-5f) && behind.Exists(p => Math.Abs(p.Z - backEnd) < 1e-5f), "the spacers run from the face to just inside the board's back" + what);

                // The spacers go from the face (z = 0) to the board's back (BackZ), inside the board's width and between its edges: nothing floats, nothing passes through the board.
                parts.Metal.Bounds(out Vec3 lo, out Vec3 hi);
                Program.Near(lo.Z, 0f, 1e-5f, "the metal starts on the face" + what);
                Program.Check(lo.Y >= parts.Layout.BottomClearance - 1e-4f && hi.Y <= parts.Layout.BoardTop + 1e-4f, "between the board's lower and upper edge" + what);
                Program.Check(lo.X >= -Plate.Width * 0.5f && hi.X <= Plate.Width * 0.5f, "inside its width" + what);
            }

            SignParts wallParts = SignBuilder.Build(Inst(Plate), SupportCatalog.ById(SupportCatalog.WallFlat), 4.9f);
            SignParts overParts = SignBuilder.Build(Inst(Plate), over, 4.9f);
            Program.Check(wallParts.Metal.VertexCount == overParts.Metal.VertexCount, "as many spacers as a wall's");

            // The thumbnail's stand-in is a beam and a deck over the board, closed, and its soffit is just over the board's top (OverRoad.TopGap).
            SignParts shown = SignBuilder.Build(Inst(Plate), over, 4.9f);
            SignBuilder.AddOverStandIn(shown);
            Program.Check(shown.Concrete.VertexCount > 0 && Open(shown.Concrete) == 0, "the stand-in is a closed body");
            shown.Concrete.Bounds(out Vec3 clo, out _);
            Program.Near(clo.Y, shown.Layout.BottomClearance - OverRoad.TopGap, 1e-4f, "its soffit is the gap under the board's lower edge: the face holds the board");
            Program.Check(clo.Z <= -0.4f && shown.Concrete.Positions.TrueForAll(p => p.Z <= 0.5f), "behind the board's plane (z = 0 is the face)");

            // The thing the first version of this support got wrong and no test saw: every spacer ends on the structure. A spacer is a tube from z = 0 (the face) to the board's back, at the height of a rail
            // of the board; the face of the stand-in is the plane z = 0 between its soffit and its top, so each spacer's end on the face must be within that height.
            // The face of the stand-in is the beam's: the geometry at z between its front (z = 0) and the board's own plane, from the soffit to the top; the deck stands on it. Its highest point on the face is the
            // board's top and a gap over it, taken from the mesh and not from the product's own arithmetic (a face that stopped under the board's top left the board's top edge standing out of it).
            float faceTop = float.MinValue;
            foreach (Vec3 p in shown.Concrete.Positions)
            {
                if (Math.Abs(p.Z) < 1e-5f)
                    faceTop = Math.Max(faceTop, p.Y);
            }

            Program.Near(faceTop, shown.Layout.BoardTop + OverRoad.TopGap, 1e-4f, "the face of the stand-in reaches the board's top and a gap over it, so the board is wholly within it");
            float soffit = shown.Layout.BottomClearance - OverRoad.TopGap, topOfFace = shown.Layout.BoardTop + OverRoad.TopGap;
            List<Vec3> onFace = shown.Metal.Positions.FindAll(p => Math.Abs(p.Z) < 1e-5f);
            Program.Check(onFace.Count > 0 && onFace.TrueForAll(p => p.Y > soffit && p.Y < topOfFace), "every spacer ends on the structure's face, none in the air under or over it");
        }

        private static void Rules()
        {
            SupportDef over = SupportCatalog.ById(SupportCatalog.OverRoad);
            Program.Check(SupportKinds.OverRoad(SupportKind.OverRoad) && !SupportKinds.OverRoad(SupportKind.WallFlat), "the predicate is this kind's alone");
            Program.Check(!SupportKinds.OnWall(SupportKind.OverRoad), "it is no wall support: no building is its host");
            Program.Check(!SupportKinds.Hung(SupportKind.OverRoad) && !SupportKinds.Hosted(SupportKind.OverRoad) && !SupportKinds.OnArm(SupportKind.OverRoad), "not a beam, not a host's post, not an arm");
            Program.Check(!SupportKinds.RootPaintable(SupportKind.OverRoad, new SupportLayout()), "no post to paint at the root");
            Program.Check(!SignLine.CanStand(SupportKind.OverRoad), "it cannot stand at every spacing of a line");
            Program.Check(Assembly.MaxFor(SupportKind.OverRoad) == 1, "one board to an item");
            Program.Check(BatchRequest.NeedsHost(SupportKind.OverRoad), "the batch interface has no structure to give");
            Program.Check(TsMarkers.KindOf(SupportKind.OverRoad) == TsMarkers.KindAttached, "the minimap sees it as an attached support");
            Program.Check(SupportForms.Of(over, false) == SupportForm.OverRoad, "its own form for the style profiles");
            Program.Check(!Assembly.ByHand(SupportKind.OverRoad, new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(Plate), Member = 0, OffsetX = 3f } }), "a height by hand is not its way: the lower edge is the structure's");

            var board = new AssemblyBoard { Instance = Inst(Plate), Member = 0, Slot = 0 };
            AssemblyPlan plan = Assembly.Plan(new List<AssemblyBoard> { board }, over, 4.9f);
            Program.Check(plan.Support == over && !plan.Hung && plan.Placed.Count == 1, "the plan has the one board");
            Program.Near(plan.Placed[0].Bottom, 4.9f, 1e-4f, "at the reach");
            Program.Near(plan.Layout.PoleTop, plan.Layout.BoardTop + Plate.ExtraTop, 1e-5f, "nothing rises over it (a post would end behind the board)");
        }

        /// <summary>
        /// The item is saved as any item is (no new field: the hash of the support, the height of the lower edge as the reach, the road as the edge): what it needs is that the support's number is stable and
        /// that a record that names it reads back as written. A build that does not know the hash draws the board on the support the planner makes for it and keeps the hash (SupportCatalog.For).
        /// </summary>
        private static void Saved()
        {
            SupportDef over = SupportCatalog.ById(SupportCatalog.OverRoad);
            var record = new SaveRecord { ModVersion = "0.12.80", City = "c", SaveId = "s", SaveName = "n", CityName = "x", Time = 1L };
            record.Items.Add(new StoredItem { TypeHash = Plate.Hash, EdgeT = 0.4f, Position = new Vec3(44f, 0f, 3f), Yaw = OverRoad.Yaw(new Vec3(-1f, 0f, 0f)), SupportHash = over.Hash, Spec = "", Reach = 6.1f, BaseReach = 6.1f, Anchor = -1 });
            var stream = new System.IO.MemoryStream();
            record.Write(stream);
            stream.Position = 0;
            StoredItem back = SaveRecord.Read(stream).Items[0];
            Program.Check(back.SupportHash == over.Hash && SupportCatalog.ByHash(back.SupportHash) == over, "the support's hash is read back and is this support's");
            Program.Near(back.Reach, 6.1f, 0f, "the height of the lower edge is the reach");
            Program.Near(back.Yaw, (float)Math.Atan2(-1f, 0f), 1e-6f, "the board faces along the face's normal");
            Program.Check(SupportCatalog.ById("attached.over").Hash == over.Hash, "the hash is the id's, so it is the same in every build that has the id");

            // The layout is what the item is built from when it is drawn again: the reach (the lower edge) gives the same board and spacers.
            SignParts a = SignBuilder.Build(Inst(Plate), over, 6.1f), b = SignBuilder.Build(Inst(Plate), SupportCatalog.ByHash(back.SupportHash), back.Reach);
            Program.Check(a.Metal.VertexCount == b.Metal.VertexCount && a.Layout.BottomClearance == b.Layout.BottomClearance, "drawn again from the record it is the same board");
        }

        /// <summary>
        /// The golden table of 0.12.79 (lighting-0.12.79.txt) folds each board's builds on the supports that existed then. Adding a support must not move a board on the others: the table is compared with this
        /// support left out of the fold (LightingPrint.SinceGolden), and that is only honest if it is out of the fold and in the catalogue, and if the table's supports are all still there.
        /// </summary>
        private static void OlderSupports()
        {
            Program.Check(LightingPrint.SinceGolden.Contains(SupportCatalog.OverRoad), "the support over the road is outside the golden fold");
            Program.Check(SupportCatalog.ById(SupportCatalog.OverRoad) != null, "and it is in the catalogue, so its own tests above are about something");
            int older = 0;
            foreach (SupportDef def in SupportCatalog.All)
                older += LightingPrint.SinceGolden.Contains(def.Id) ? 0 : 1;
            Program.Check(older == 30, "the supports of 0.12.79 are all there: " + older + " of 30 (a support removed or renamed would change every board's number)");
            foreach (string id in LightingPrint.SinceGolden)
                Program.Check(SupportCatalog.ById(id) != null, "a support left out of the fold exists: " + id);
        }

        private static void Hints()
        {
            foreach (string kind in new[] { "over", "over-none", "over-low", "support-over" })
            {
                Program.Check(Array.IndexOf(HintText.Kinds, kind) >= 0, kind + " is a known kind");
                var h = new HintInfo { Kind = kind, Left = 4.9f, Need = 6f, Name = "x" };
                string zh = HintText.Of(h, true), en = HintText.Of(h, false);
                Program.Check(!string.IsNullOrEmpty(zh) && !string.IsNullOrEmpty(en) && zh != en, kind + " has text in both languages");
                Program.Check(HintText.Keys(h, true).Count > 0, kind + " has keys");
            }

            var low = new HintInfo { Kind = "over-low", Left = 5.2f, Need = 5.8f };
            Program.Check(HintText.Of(low, false).Contains("5.2") && HintText.Of(low, false).Contains("5.8"), "the refusal says what there is and what is needed");
        }
    }
}
