using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The over-road support hung under the structure (0.12.81; SupportDef.Hanger, attached.over.hung): the same kind as the one flat on the face, so no new kind; what it needs of a structure, the layout (the
    /// reach is the soffit's height, the lower edge follows from it), the built rods, plates and braces (every rod ends on a plate on the soffit's plane and starts on the tube, nothing floats), the lean about the tube,
    /// reduced detail, the item as it is saved, an older build, and that the support flat on the face is bit for bit what 0.12.80 built. GB 5768.2 draws no hung board: every size is this mod's estimate.
    /// The game's reads (the nets, the raycast) are the tool's and are not here.
    /// </summary>
    internal static class OverHungTests
    {
        public static void RunAll()
        {
            Program.Run("hung over road: the support in the catalogue, the face's kind, no new kind", Catalogue);
            Program.Run("hung over road: what the board needs of the structure", Fitting);
            Program.Run("hung over road: where the board stands under the structure", Spot);
            Program.Run("hung over road: the layout, the lower edge follows the soffit", Layout);
            Program.Run("hung over road: the rods start on the tube and end on plates on the soffit", Built);
            Program.Run("hung over road: the board leans about its tube, the hardware does not", Lean);
            Program.Run("hung over road: far tiles keep the plates and the braces", Reduced);
            Program.Run("hung over road: the thumbnail's stand-in is a slab the plates are sunk into", StandIn);
            Program.Run("hung over road: saved and read back, an older build meets the hash", Saved);
            Program.Run("hung over road: the support flat on the face is as 0.12.80 built it", FaceUnchanged);
            Program.Run("hung over road: the line of help", Hints);
            Program.Run("hung over road: what the tool decides, from the spot", Placing);
            Program.Run("hung over road: the figures are as documented", Constants);
            Program.Run("hung over road: a text light box hangs on the tube's carrier", TextBoard);
            Program.Run("hung over road: the tile shows the rods and the slab", Tile);
        }

        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Plate = Make("t.hung.plate", 2.4f, 1.2f), Tall = Make("t.hung.tall", 1.0f, 3.5f), Small = Make("t.hung.small", 0.6f, 0.6f), Wide = Make("t.hung.wide", 6.0f, 1.5f), Crowned = Make("t.hung.crowned", 1.6f, 1.0f), Triangle = Make("t.hung.triangle", 1.2f, 1.04f);

        static OverHungTests()
        {
            Crowned.ExtraTop = 0.4f;
            // A triangle is narrow where the tube passes (the middle of its height): the rods, inset from its bounding width, stand beyond its outline there.
            Triangle.Shape = ShapeKind.Triangle;
        }

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static SupportDef Hung => SupportCatalog.ById(SupportCatalog.OverRoadHung);

        private static SupportDef Face => SupportCatalog.ById(SupportCatalog.OverRoad);

        private static SignParts Build(SignDef def, float soffit, SupportDetail detail = SupportDetail.Full, float tiltDegrees = 0f) =>
            SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(def), Tilt = tiltDegrees * (float)(Math.PI / 180.0) } }, Hung, soffit, detail);

        private static OverStructure Bridge(float width = 12f) => new OverStructure
        {
            Curve = Bezier3.Line(new Vec3(50f, 7f, -40f), new Vec3(50f, 7f, 40f)), Width = width, Lowest = -1f, Highest = 0.6f, Id = 7
        };

        private static void Catalogue()
        {
            SupportDef hung = Hung, face = Face;
            Program.Check(hung != null && hung.Id == "attached.over.hung" && hung.Kind == SupportKind.OverRoad && hung.Hanger && face != null && !face.Hanger, "the hung support is the face's kind with the flag");
            Program.Check(SupportCatalog.ByHash(hung.Hash) == hung && hung.Hash != face.Hash && hung.Hash != 0u, "found again by its hash, which is not the face's");
            Program.Check((int)SupportKind.OverRoad == (int)SupportKind.AttachedArm + 1 && Array.IndexOf(Enum.GetValues(typeof(SupportKind)), SupportKind.OverRoad) == Enum.GetValues(typeof(SupportKind)).Length - 1, "no kind was appended: OverRoad is still the last (saved by number)");
            Program.Check(!hung.Hidden && System.Linq.Enumerable.Contains(SupportCatalog.Listed, hung), "listed in the panel");
            Program.Check(hung.Family == SupportFamilies.Attached && hung.Zh.Length > 0 && hung.En.Length > 0 && hung.Zh != face.Zh && hung.En != face.En, "in the attached family, named in both languages, not as the face's");
            Program.Check(SupportForms.Of(hung, false) == SupportForm.OverRoad, "the form of the style profiles is the over-road one");
            Program.Check(BoardTilt.Leans(hung) && !BoardTilt.Leans(face), "the hung board leans (the lean row and the preview), the board flat on a face does not");
            Program.Check(SupportKinds.OverRoad(hung.Kind) && !SupportKinds.Hung(hung.Kind) && !SupportKinds.Hosted(hung.Kind) && !SupportKinds.OnWall(hung.Kind) && !SupportKinds.OnArm(hung.Kind), "every rule of the kind holds for it: no post, no wall, no arm");
            Program.Check(!SupportKinds.RootPaintable(hung.Kind, new SupportLayout()) && !SignLine.CanStand(hung.Kind) && Assembly.MaxFor(hung.Kind) == 1, "no root paint, no line, one board");
            Program.Check(LightingPrint.SinceGolden.Contains(hung.Id), "outside the 0.12.79 golden fold, as every support appended since");
            var hashes = new HashSet<uint>();
            foreach (SupportDef def in SupportCatalog.All)
                Program.Check(hashes.Add(def.Hash), "the hash of " + def.Id + " is its own");
        }

        private static void Fitting()
        {
            Program.Near(OverRoad.HungNeeded(1.2f), OverRoad.MinFree + 1.2f + OverRoad.MinHang, 1e-5f, "a board needs the free height under it, its own height and the shortest rods");
            Program.Check(OverRoad.HungFits(OverRoad.HungNeeded(1.2f), 1.2f) && !OverRoad.HungFits(OverRoad.HungNeeded(1.2f) - 0.01f, 1.2f), "exactly at the limit");
            Program.Check(!OverRoad.HungFits(float.NaN, 1.2f) && !OverRoad.HungFits(3f, 1.2f), "no soffit, or a low one: refused");
            Program.Near(OverRoad.HungLowerEdge(8f, 1.2f), 8f - 1.2f - OverRoad.HangLength, 1e-5f, "under a high soffit the board hangs on rods of HangLength");
            Program.Near(OverRoad.HungLowerEdge(OverRoad.HungNeeded(1.2f), 1.2f), OverRoad.MinFree, 1e-5f, "under the lowest soffit it hangs at the free height, on the shortest rods");
            Program.Near(OverRoad.HungLowerEdge(5f, 1.2f), OverRoad.MinFree, 1e-5f, "under one that is lower it is held at the free height (the tool refuses it before)");

            // Whenever it fits: the board is at least MinFree up, the rods above it MinHang to HangLength long. Whenever it does not: they are shorter than MinHang.
            for (float soffit = 4f; soffit <= 12f; soffit += 0.1f)
            {
                foreach (float total in new[] { 0.3f, 0.6f, 1.2f, 1.9f, 3.5f })
                {
                    float lower = OverRoad.HungLowerEdge(soffit, total), rods = soffit - lower - total;
                    bool fits = OverRoad.HungFits(soffit, total);
                    Program.Check(lower >= OverRoad.MinFree - 1e-5f, $"soffit {soffit:0.0}, board {total}: never under the free height");
                    Program.Check(fits ? rods >= OverRoad.MinHang - 1e-3f && rods <= OverRoad.HangLength + 1e-3f : rods < OverRoad.MinHang, $"soffit {soffit:0.0}, board {total}: rods {rods:0.00} m, fits {fits}");
                }
            }

            Program.Check(OverRoad.HungDeepEnough(OverRoad.HungDepth) && !OverRoad.HungDeepEnough(OverRoad.HungDepth - 0.01f) && !OverRoad.HungDeepEnough(float.NaN), "a structure must reach back as far as the braces' plates and a margin");
            Program.Near(OverRoad.HungDepth, OverRoad.HangSetback + OverRoad.BraceRun + OverRoad.PlateHalf + 0.1f, 1e-6f, "the depth is the setback, the braces' run, a plate's half and 0.1 m");
        }

        private static void Spot()
        {
            int id = OverRoad.Find(new List<OverStructure> { Bridge() }, Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f)), 14f, 0f, 0.5f, out OverSpot spot);
            Program.Check(id == 7, "the bridge is found");
            Program.Near(spot.Depth, 12f, 1e-4f, "its depth behind the face is the structure's width");
            Vec3 origin = OverRoad.HungOrigin(spot, 0f);
            Program.Near(origin.Y, 0f, 1e-6f, "the origin is at the road's height");
            Program.Near(Vec3.Dot(origin - spot.Point, spot.Face), -OverRoad.HangSetback, 1e-4f, "it is HangSetback behind the face line, into the structure");
            Program.Near(Math.Abs(Vec3.Dot(origin - spot.Point, spot.Run)), 0f, 1e-4f, "and not along the structure");
            // A bridge that crosses at an angle: its face looks along X and Z both, and the origin moves in both.
            var skew = new OverStructure { Curve = Bezier3.Line(new Vec3(30f, 7f, -30f), new Vec3(70f, 7f, 30f)), Width = 12f, Lowest = -1f, Highest = 0.6f, Id = 11 };
            Program.Check(OverRoad.Find(new List<OverStructure> { skew }, Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f)), 14f, 0f, 0.5f, out OverSpot slant) == 11 && Math.Abs(slant.Face.X) > 0.3f && Math.Abs(slant.Face.Z) > 0.3f, "a skew bridge has a slanted face");
            Vec3 slantOrigin = OverRoad.HungOrigin(slant, 0f);
            Program.Near(slantOrigin.X - slant.Point.X, -slant.Face.X * OverRoad.HangSetback, 1e-5f, "on a skew bridge the origin is behind the face line in X");
            Program.Near(slantOrigin.Z - slant.Point.Z, -slant.Face.Z * OverRoad.HangSetback, 1e-5f, "and in Z");
            Program.Near(Vec3.DistanceXZ(slantOrigin, slant.Point), OverRoad.HangSetback, 1e-4f, "by HangSetback in all");
            Program.Check(Math.Abs(origin.X - 50f) < 6f, "it is within the structure's footprint");
            Vec3 faceOrigin = OverRoad.Origin(spot, 0f);
            Program.Check(Vec3.DistanceXZ(faceOrigin, spot.Point) < 1e-5f && Vec3.DistanceXZ(origin, faceOrigin) > OverRoad.HangSetback - 1e-4f, "the face support's origin is on the face line as before");
            Program.Near(OverRoad.Yaw(spot.Face), (float)Math.Atan2(spot.Face.X, spot.Face.Z), 1e-6f, "it faces the traffic as the face support does");
            Program.Check(OverRoad.Find(new List<OverStructure> { Bridge(1.0f) }, Bezier3.Line(new Vec3(0f, 0f, 0f), new Vec3(100f, 0f, 0f)), 14f, 0f, 0.5f, out OverSpot narrow) == 7 && !OverRoad.HungDeepEnough(narrow.Depth) && OverRoad.HungDeepEnough(spot.Depth), "a structure 1 m deep is found, and is too narrow to hang from; the 12 m one is not");
        }

        private static void Layout()
        {
            SupportLayout l = SupportLayouts.Of(Plate, Hung, 8f);
            Program.Check(l.Hanger && l.Kind == SupportKind.OverRoad, "the layout is a hung one");
            Program.Near(l.HangerTop, 8f, 1e-6f, "the reach is the soffit's height");
            Program.Near(l.BottomClearance, 8f - 1.2f - OverRoad.HangLength, 1e-5f, "the lower edge follows from it");
            Program.Near(l.BoardTop, l.BottomClearance + 1.2f, 1e-5f, "the board stands on it");
            Program.Near(l.ArmY, l.BoardTop - SupportLayouts.ArmInset(1.2f), 1e-5f, "the tube passes behind the board where a cantilever's arm does");
            Program.Check(l.ArmY > l.BottomClearance && l.ArmY < l.BoardTop && l.HangY == l.ArmY, "within the board's height");
            Program.Near(l.HangRadius, SupportLayouts.HangerTubeRadius(2.4f), 1e-6f, "the tube's size is by the board's width");
            Program.Near(l.BackZ, l.HangRadius + SupportLayouts.ClampRoom, 1e-6f, "the board's back is a clamp in front of the tube, whose axis is the origin");
            Program.Near(l.PoleTop, 8f, 1e-6f, "the top of the support is the soffit");
            Program.Check(l.PoleX.Length == 0 && l.PoleRadius == 0f && l.BoardCentreX == 0f && l.Base == null, "no post, no foot, no offset");
            Program.Near(l.InnerReach, 1.2f, 1e-6f, "the board's half width");

            Program.Near(SupportLayouts.Of(Plate, Hung, 0f).BottomClearance, OverRoad.MinFree, 1e-6f, "without a reach: the free height, with the rods' own length above the board");
            Program.Near(SupportLayouts.Of(Plate, Hung, 0f).HangerTop, OverRoad.MinFree + 1.2f + OverRoad.HangLength, 1e-5f, "so the default soffit is that high");
            Program.Near(SupportLayouts.Of(Plate, Hung, OverRoad.HungNeeded(1.2f)).BottomClearance, OverRoad.MinFree, 1e-5f, "the lowest soffit that fits has it at the free height");
            // What stands above the board counts: the rods are above all of it.
            SupportLayout crowned = SupportLayouts.Of(Crowned, Hung, 7f);
            Program.Near(crowned.BottomClearance, 7f - 1.0f - 0.4f - OverRoad.HangLength, 1e-5f, "a board with a crown (0.4 m) hangs that much lower");
            Program.Near(SupportLayouts.Of(Crowned, Hung, 0f).HangerTop, OverRoad.MinFree + 1.0f + 0.4f + OverRoad.HangLength, 1e-5f, "and the default soffit is that much higher");
            Program.Check(SupportLayouts.Of(Crowned, Hung, OverRoad.HungNeeded(1.4f)).BottomClearance == OverRoad.MinFree, "the lowest soffit that fits it is 0.4 m higher than for the board alone");
            Program.Near(SupportLayouts.Of(Plate, Hung, 1.0f).BottomClearance, 0.5f, 1e-6f, "a layout is never lower than half a metre");
            SupportLayout damaged = SupportLayouts.Of(Plate, Hung, 5f);
            Program.Check(!float.IsNaN(damaged.BottomClearance) && damaged.BottomClearance >= 0.5f && damaged.HangerTop - damaged.BoardTop >= OverRoad.MinHang - 1e-4f, "a reach the board does not fit under (a saved item) still makes a layout, with the shortest rods");
            Program.Near(SupportLayouts.Of(Plate, Hung, 8f, 7f).BottomClearance, 8f - 1.2f - OverRoad.HangLength, 1e-5f, "a clearance by hand is for a hung support: not this one");
            SupportLayout face = SupportLayouts.Of(Plate, Face, 4.9f);
            Program.Check(!face.Hanger && face.HangerXs == null && face.BottomClearance == 4.9f, "the face support's layout has none of it");

            float[] placedXs = SupportLayouts.HangerPositions(Plate.Width);
            Program.Check(l.HangerXs != null && l.HangerXs.Length == placedXs.Length && System.Linq.Enumerable.SequenceEqual(l.HangerXs, placedXs), "the layout's rods are at the places for the board's width");
            Program.Check(SupportLayouts.Of(Wide, Hung, 9f).HangerXs.Length == SupportLayouts.HangerPositions(Wide.Width).Length && SupportLayouts.Of(Wide, Hung, 9f).HangerXs.Length > l.HangerXs.Length, "and a wider board has more");

            // The rods: two at the least, one to about HangerPitch of the board's width, symmetric, inside the board.
            foreach (float width in new[] { 0.4f, 0.6f, 1.0f, 1.6f, 2.4f, 3.6f, 6.0f, 9.0f })
            {
                float[] xs = SupportLayouts.HangerPositions(width);
                string what = $"a board {width} m wide: {xs.Length} rods";
                Program.Check(xs.Length >= 2, what + ", two at the least");
                bool ordered = true, symmetric = true, pitch = true;
                for (int i = 1; i < xs.Length; i++)
                {
                    ordered &= xs[i] > xs[i - 1];
                    pitch &= xs[i] - xs[i - 1] <= SupportLayouts.HangerPitch + 1e-4f;
                }

                for (int i = 0; i < xs.Length; i++)
                    symmetric &= Math.Abs(xs[i] + xs[xs.Length - 1 - i]) < 1e-5f;
                Program.Check(ordered && symmetric && pitch, what + ", in order, symmetric, at most " + SupportLayouts.HangerPitch + " m apart");
                Program.Check(xs[xs.Length - 1] <= width * 0.5f - 0.1f + 1e-4f, what + ", inside the board by at least 0.1 m");
                if (xs.Length > 2)
                    Program.Check(xs[1] - xs[0] > SupportLayouts.HangerPitch * 0.5f, what + ", not crowded");
            }

            Program.Check(SupportLayouts.HangerTubeRadius(1.0f) < SupportLayouts.HangerTubeRadius(3.0f) && SupportLayouts.HangerTubeRadius(3.0f) < SupportLayouts.HangerTubeRadius(6.0f), "a wider board has a wider tube");
            Program.Check(SupportLayouts.HangerRodRadius(1.0f) < SupportLayouts.HangerRodRadius(3.0f) && SupportLayouts.HangerRodRadius(3.0f) < SupportLayouts.HangerRodRadius(6.0f), "and thicker rods");
        }

        private static List<Vec3> At(MeshBuilder mesh, float y, float tolerance = 1e-4f) => mesh.Positions.FindAll(p => Math.Abs(p.Y - y) <= tolerance);

        private static float Round(float v) => (float)Math.Round(v, 3);

        private static void Built()
        {
            foreach (SignDef def in new[] { Plate, Tall, Small, Wide, Crowned, Triangle })
            {
                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                {
                    float soffit = OverRoad.HungNeeded(def.Height + def.ExtraTop) + 1.1f;
                    SignParts parts = Build(def, soffit, detail);
                    SupportLayout l = parts.Layout;
                    string what = $"{def.Id} ({detail})";
                    Program.Check(parts.Concrete.VertexCount == 0 && parts.Picks.Count == 1 && parts.Picks[0].Name == "board", what + ": no foundation, only the board can be pointed at");
                    Program.Check(l.BottomClearance >= OverRoad.MinFree - 1e-4f, what + ": the free height under the board");
                    float rodR = SupportLayouts.HangerRodRadius(def.Width), braceR = rodR * 0.8f;

                    // The plates: every top face is the soffit's plane sunk PlateInto, and they are at the rods' x on the tube's plane and at the two outer rods' x BraceRun behind it, nothing else.
                    parts.Metal.Bounds(out _, out Vec3 mMax);
                    Program.Near(mMax.Y, soffit + SupportLayouts.PlateInto, 1e-4f, what + ": nothing of it is over the soffit's plane but the plates' sunk edge");
                    List<Vec3> tops = At(parts.Metal, soffit + SupportLayouts.PlateInto);
                    var corners = new HashSet<(float, float)>();
                    foreach (Vec3 p in tops)
                        corners.Add((Round(p.X), Round(p.Z)));
                    var want = new HashSet<(float, float)>();
                    float[] xs = l.HangerXs;
                    // Each rod and brace as (x, z of its upper end, radius, unit direction of its axis).
                    float endY = soffit - SupportLayouts.PlateThick * 0.5f, rise = endY - l.ArmY, braceLength = (float)Math.Sqrt(rise * rise + OverRoad.BraceRun * OverRoad.BraceRun);
                    var centres = new List<(float X, float Z, float R, Vec3 D)>();
                    foreach (float x in xs)
                        centres.Add((x, 0f, rodR, new Vec3(0f, 1f, 0f)));
                    foreach (float x in new[] { xs[0], xs[xs.Length - 1] })
                        centres.Add((x, -OverRoad.BraceRun, braceR, new Vec3(0f, rise / braceLength, -OverRoad.BraceRun / braceLength)));
                    foreach ((float x, float z, float _, Vec3 _) in centres)
                    {
                        foreach (float sx in new[] { -1f, 1f })
                        {
                            foreach (float sz in new[] { -1f, 1f })
                                want.Add((Round(x + sx * OverRoad.PlateHalf), Round(z + sz * OverRoad.PlateHalf)));
                        }
                    }

                    Program.Check(corners.SetEquals(want), what + ": the plates are one at each rod and one at each outer rod's brace: " + corners.Count + " corners for " + want.Count);
                    Program.Check(tops.Count >= 4 * centres.Count, what + ": each plate has its top face");

                    // Each rod and brace ends in its plate: a ring of its radius round its axis, square to it, at the end point (its plate's middle height), and the centre of its cap; and no other vertex is within
                    // half a millimetre of that height (the rings of the braces are slanted, so a few of theirs are).
                    bool OnRing(Vec3 p, (float X, float Z, float R, Vec3 D) c)
                    {
                        Vec3 d = p - new Vec3(c.X, endY, c.Z);
                        return Math.Abs(Vec3.Dot(d, c.D)) < 1e-4f && (Math.Abs(d.Length - c.R) < 1e-4f || d.Length < 1e-4f);
                    }

                    foreach ((float x, float z, float r, Vec3 dir) in centres)
                    {
                        var c = (x, z, r, dir);
                        int on = parts.Metal.Positions.FindAll(p => OnRing(p, c)).Count;
                        Program.Check(on >= 6, what + $": the rod at x {x:0.00}, z {z:0.00} ends inside its plate ({on} vertices of its end ring)");
                    }

                    Program.Check(At(parts.Metal, endY, 5e-4f).TrueForAll(p => centres.Exists(c => OnRing(p, c))), what + ": nothing else is at the rods' ends' height");

                    // The rods start on the tube's axis: a ring of the rod's radius round it at the tube's height, at each rod's x.
                    List<Vec3> feet = At(parts.Metal, l.ArmY);
                    foreach (float x in xs)
                        Program.Check(feet.FindAll(p => Math.Abs(Math.Sqrt((p.X - x) * (p.X - x) + p.Z * p.Z) - rodR) < 1e-4f).Count >= 6, what + $": the rod at x {x:0.00} starts on the tube's axis");

                    // The tube: its end rings are at the radius of the tube round its axis; it covers every rod and stays within the board's width.
                    float tubeMin = float.MaxValue, tubeMax = float.MinValue;
                    foreach (Vec3 p in parts.Metal.Positions)
                    {
                        float d = (float)Math.Sqrt((p.Y - l.ArmY) * (p.Y - l.ArmY) + p.Z * p.Z);
                        if (Math.Abs(d - l.HangRadius) < 1e-4f)
                        {
                            tubeMin = Math.Min(tubeMin, p.X);
                            tubeMax = Math.Max(tubeMax, p.X);
                        }
                    }

                    Program.Check(tubeMin <= xs[0] - rodR + 1e-4f && tubeMax >= xs[xs.Length - 1] + rodR - 1e-4f, what + $": the tube covers the outer rods: {tubeMin:0.000} to {tubeMax:0.000}");
                    Program.Check(tubeMin >= -def.Width * 0.5f && tubeMax <= def.Width * 0.5f, what + ": and stays within the board's width");

                    // Every clamp's backing (a 4 cm wide box from the tube's surface to the channel, centred on the channel) is on the tube: its centre is within the tube's length.
                    List<Vec3> backings = parts.Metal.Positions.FindAll(p => Math.Abs(Math.Abs(p.Y - l.ArmY) - 0.03f) < 1e-4f && p.Z > l.HangRadius * 0.4f && p.Z < l.BackZ - SupportLayouts.RailDepth + 1e-4f);
                    Program.Check(backings.Count >= 8, what + ": there are clamps' backings: " + backings.Count + " vertices");
                    float backMin = float.MaxValue, backMax = float.MinValue;
                    foreach (Vec3 p in backings)
                    {
                        backMin = Math.Min(backMin, p.X);
                        backMax = Math.Max(backMax, p.X);
                    }

                    Program.Check(backings.Count == 0 || backMin + 0.02f >= tubeMin - 1e-4f && backMax - 0.02f <= tubeMax + 1e-4f, what + $": the tube reaches every clamp: backings {backMin + 0.02f:0.000} to {backMax - 0.02f:0.000}, tube {tubeMin:0.000} to {tubeMax:0.000}");
                    Program.Check(l.HangerTop - (l.BoardTop + def.ExtraTop) >= OverRoad.MinHang - 1e-4f, what + ": the rods show above the board");

                    // The board is clamped to the tube: its channels' back faces are RailDepth behind its back, and a band or a backing meets the tube.
                    Program.Check(parts.Metal.Positions.Exists(p => Math.Abs(p.Z - (l.BackZ - SupportLayouts.RailDepth)) < 1e-5f), what + ": the board's channels are on its back");
                    int bands = parts.Metal.Positions.FindAll(p => Math.Abs(Math.Sqrt((p.Y - l.ArmY) * (p.Y - l.ArmY) + p.Z * p.Z) - (l.HangRadius + 0.005f)) < 1e-4f).Count;
                    Program.Check(detail == SupportDetail.Reduced || bands >= 8, what + ": the clamps' bands go round the tube: " + bands + " vertices");
                    Program.Check(parts.Metal.Positions.Exists(p => Math.Abs(p.Y - l.ArmY) < 0.031f && p.Z > l.HangRadius * 0.4f && p.Z < l.BackZ - SupportLayouts.RailDepth + 1e-4f), what + ": and a backing runs from the tube to the channel");

                    // The bolts of a plate: four, under it, in its corners (full detail only).
                    float low = soffit - SupportLayouts.PlateThick, bolt = OverRoad.PlateHalf - 0.025f;
                    List<Vec3> heads = At(parts.Metal, low - 0.011f);
                    if (detail == SupportDetail.Reduced)
                        Program.Check(heads.Count == 0, what + ": far tiles have no bolts");
                    else
                    {
                        foreach ((float x, float z, float _, Vec3 _) in centres)
                        {
                            int bolts = 0;
                            foreach (float sx in new[] { -1f, 1f })
                            {
                                foreach (float sz in new[] { -1f, 1f })
                                    bolts += heads.Exists(p => Math.Abs(p.X - (x + sx * bolt)) < 1e-4f && Math.Abs(p.Z - (z + sz * bolt)) < 1e-4f) ? 1 : 0;
                            }

                            Program.Check(bolts == 4, what + $": the plate at x {x:0.00}, z {z:0.00} has its four bolts: {bolts}");
                        }
                    }

                    // Closed above the board: the rods, braces, plates and bolts have no opening (what is open lower down is the clamps' bands, as on any arm).
                    List<Vec3> open = OverRoadTests.OpenCorners(parts.Metal).FindAll(p => p.Y > l.BoardTop + def.ExtraTop + 0.005f);
                    Program.Check(open.Count == 0, what + ": no open edge over the board: " + open.Count);

                    // Nothing goes through the face or in front of it: the hardware is behind the board, in the back half space but for the board's own back and rim.
                    parts.FaceBounds(out Vec3 fMin, out Vec3 fMax);
                    Program.Near(fMin.Z, SignBuilder.FacePlane(l, Inst(def)), 1e-5f, what + ": the face is one plane");
                    Program.Check(parts.Metal.Positions.FindAll(p => p.Y > l.BoardTop + 0.005f).TrueForAll(p => p.Z <= OverRoad.PlateHalf + 1e-4f), what + ": over the board nothing comes in front of the tube's plate");
                }
            }
        }

        private static void Lean()
        {
            float soffit = 9f;
            SignParts flat = Build(Plate, soffit), leaned = Build(Plate, soffit, SupportDetail.Full, 15f);
            flat.FaceBounds(out Vec3 f0, out Vec3 f1);
            leaned.FaceBounds(out Vec3 l0, out Vec3 l1);
            Program.Near(f1.Z - f0.Z, 0f, 1e-5f, "upright, the face is one plane");
            Program.Near(l1.Z - l0.Z, Plate.Height * (float)Math.Sin(15.0 * Math.PI / 180.0), 0.01f, "leaned 15 degrees its face's top is that much in front of its bottom");
            Program.Check(l1.Y < f1.Y && l0.Y > f0.Y - 1e-4f, "about the tube: the top comes down a little, the bottom comes up");
            // About the tube: the face's top is in front of where it was by what it is above the tube, its bottom behind by what it is under it.
            float above = SupportLayouts.ArmInset(Plate.Height), s15 = (float)Math.Sin(15.0 * Math.PI / 180.0);
            Program.Near(l1.Z - f1.Z, above * s15, 0.01f, "its top came forward by its height over the tube times the sine");
            Program.Near(f0.Z - l0.Z, (Plate.Height - above) * s15, 0.01f, "its bottom went back by its height under the tube times the sine");

            // The rods, plates and braces do not lean: what is over the board's untilted top is the same vertex for vertex.
            float line = flat.Layout.BoardTop + 0.01f;
            List<Vec3> a = flat.Metal.Positions.FindAll(p => p.Y > line), b = leaned.Metal.Positions.FindAll(p => p.Y > line);
            Program.Check(a.Count > 0 && a.Count == b.Count, "the hardware over the board is as many vertices when it leans: " + a.Count + " / " + b.Count);
            bool same = a.Count == b.Count;
            for (int i = 0; same && i < a.Count; i++)
                same = (a[i] - b[i]).Length < 1e-6f;
            Program.Check(same, "and the same ones");

            // A lean of 15 degrees keeps the free height: the lower edge comes up, not down.
            Program.Check(l0.Y >= leaned.Layout.BottomClearance - 1e-3f, "the lower edge of the leaned board is not under the layout's");
            Program.Check(SignBuilder.FacePlane(leaned.Layout, Inst(Plate)) > leaned.Layout.BackZ, "the face is in front of its back");
        }

        private static void Reduced()
        {
            SignParts full = Build(Plate, 8f), far = Build(Plate, 8f, SupportDetail.Reduced);
            Program.Check(far.Metal.VertexCount < full.Metal.VertexCount * 0.75f && far.Metal.VertexCount > 0, $"far tiles have fewer vertices of metal: {far.Metal.VertexCount} against {full.Metal.VertexCount}");
            var fullTops = new HashSet<(float, float)>();
            var farTops = new HashSet<(float, float)>();
            foreach (Vec3 p in At(full.Metal, 8f + SupportLayouts.PlateInto))
                fullTops.Add((Round(p.X), Round(p.Z)));
            foreach (Vec3 p in At(far.Metal, 8f + SupportLayouts.PlateInto))
                farTops.Add((Round(p.X), Round(p.Z)));
            Program.Check(farTops.Count > 0 && fullTops.SetEquals(farTops), "the plates, with the braces' two, are in the same places: " + farTops.Count + " corners");
            Program.Check(full.Layout.HangerTop == far.Layout.HangerTop && full.Layout.BottomClearance == far.Layout.BottomClearance && full.Layout.BackZ == far.Layout.BackZ, "the same layout");
            Program.Check(full.Picks.Count == far.Picks.Count, "the same places to point at");
        }

        private static void StandIn()
        {
            SignParts parts = Build(Plate, 8f);
            SignBuilder.AddOverStandIn(parts);
            SupportLayout l = parts.Layout;
            Program.Check(parts.Concrete.VertexCount > 0 && OverRoadTests.Open(parts.Concrete) == 0, "the stand-in is a closed body");
            parts.Concrete.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Near(lo.Y, l.HangerTop, 1e-5f, "its underside is the soffit the rods' plates are on");
            Program.Near(hi.Z, OverRoad.HangSetback, 1e-5f, "its edge is the face line, HangSetback in front of the tube's axis");
            Program.Check(lo.Z <= -(OverRoad.BraceRun + OverRoad.PlateHalf) && lo.X <= l.HangerXs[0] - OverRoad.PlateHalf && hi.X >= l.HangerXs[l.HangerXs.Length - 1] + OverRoad.PlateHalf, "it has every plate under it, the braces' behind the rods' too");
            parts.Metal.Bounds(out _, out Vec3 mMax);
            Program.Check(mMax.Y > lo.Y && mMax.Y < hi.Y, "the plates' sunk edge is inside it, not through it");

            // The one on a face is as it was: a beam whose face is the plane z = 0.
            SignParts face = SignBuilder.Build(Inst(Plate), Face, 4.9f);
            SignBuilder.AddOverStandIn(face);
            face.Concrete.Bounds(out Vec3 flo, out _);
            Program.Near(flo.Y, 4.9f - OverRoad.TopGap, 1e-4f, "the face's stand-in is still the beam around the board");
        }

        private static void Saved()
        {
            SupportDef hung = Hung;
            var record = new SaveRecord { ModVersion = "0.12.81", City = "c", SaveId = "s", SaveName = "n", CityName = "x", Time = 1L };
            record.Items.Add(new StoredItem { TypeHash = Plate.Hash, EdgeT = 0.4f, Position = new Vec3(44.4f, 0f, 3f), Yaw = OverRoad.Yaw(new Vec3(-1f, 0f, 0f)), SupportHash = hung.Hash, Spec = "", Reach = 7.3f, BaseReach = 7.3f, Anchor = -1, Flags = (byte)(BoardTilt.Default << 6) });
            var stream = new MemoryStream();
            record.Write(stream);
            stream.Position = 0;
            StoredItem back = SaveRecord.Read(stream).Items[0];
            Program.Check(back.SupportHash == hung.Hash && SupportCatalog.ByHash(back.SupportHash) == hung, "the support's hash is read back and is this support's");
            Program.Near(back.Reach, 7.3f, 0f, "the soffit's height is the reach");
            Program.Check(back.Flags >> 6 == BoardTilt.Default, "and the lean is kept in the flags as for any board that leans");

            // Drawn again from the record: the same board, the same rods.
            SignParts a = Build(Plate, 7.3f, SupportDetail.Full, BoardTilt.Default * BoardTilt.Step);
            SignParts b = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = Inst(Plate), Tilt = BoardTilt.Radians(back.Flags >> 6) } }, SupportCatalog.ByHash(back.SupportHash), back.Reach, SupportDetail.Full);
            Program.Check(a.Metal.VertexCount == b.Metal.VertexCount && a.Layout.BottomClearance == b.Layout.BottomClearance && a.Layout.HangerTop == b.Layout.HangerTop, "drawn again from the record it is the same support");

            // A build before this one has no such id: the hash is unknown to it, and it draws the board on the support the planner makes, keeping the hash (SupportCatalog.For).
            Program.Check(SupportCatalog.ByHash(0xABCD1234u) == null && SupportCatalog.For(Plate, 0xABCD1234u) == SupportPlanner.Auto(Plate), "an unknown hash falls back to the planner's support");
            Program.Check(SupportCatalog.For(Plate, hung.Hash) == hung, "this build draws it on its own support");
            Program.Check(hung.Hash == SignCatalog.HashOf("attached.over.hung"), "the hash is the id's, so it is the same in every build that has the id");
        }

        private static void FaceUnchanged()
        {
            string golden = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "overroad-face-0.12.80.txt");
            Program.Check(golden != null, "the golden file is found");
            if (golden == null)
                return;
            string[] want = File.ReadAllLines(golden, new System.Text.UTF8Encoding(false));
            List<string> now = OverRoadFacePrint.Lines();
            Program.Check(now.Count == want.Length && now.Count == 112, "the same builds: " + now.Count + " against " + want.Length);
            int differ = 0;
            for (int i = 0; i < Math.Min(now.Count, want.Length); i++)
            {
                if (now[i] == want[i])
                    continue;
                differ++;
                if (differ <= 5)
                    Program.Check(false, "not as 0.12.80 built it: " + now[i].Substring(0, Math.Min(40, now[i].Length)));
            }

            Program.Check(differ == 0, differ + " build(s) of the support flat on a face differ from 0.12.80's");
        }

        private static void Hints()
        {
            foreach (string kind in new[] { "over", "over-none", "over-low", "over-thin", "support-over" })
                Program.Check(Array.IndexOf(HintText.Kinds, kind) >= 0, kind + " is a known kind");
            var thin = new HintInfo { Kind = "over-thin", Left = 0.9f, Need = 1.2f, Hung = true, Bad = true };
            string zh = HintText.Of(thin, true), en = HintText.Of(thin, false);
            Program.Check(zh.Length > 0 && en.Length > 0 && zh != en && en.Contains("0.9") && en.Contains("1.2") && zh.Contains("0.9") && zh.Contains("1.2"), "the narrow bridge's refusal says what there is and what is needed, in both languages");
            Program.Check(zh.IndexOf("0.9", StringComparison.Ordinal) < zh.IndexOf("1.2", StringComparison.Ordinal) && en.IndexOf("0.9", StringComparison.Ordinal) < en.IndexOf("1.2", StringComparison.Ordinal), "what there is comes before what is needed");
            Program.Check(en.IndexOf("deep", StringComparison.Ordinal) > en.IndexOf("0.9", StringComparison.Ordinal) && zh.IndexOf("深", StringComparison.Ordinal) > zh.IndexOf("0.9", StringComparison.Ordinal), "and the 0.9 is the depth");
            Program.Check(HintText.Keys(thin, true).Exists(row => row[0].Contains("'")) && HintText.Keys(thin, false).Exists(row => row[0].Contains("'")), "the key to change the support is there, as for the low bridge");

            var over = new HintInfo { Kind = "over", Left = 6.2f, Need = 8f, Hung = true };
            var overFace = new HintInfo { Kind = "over", Left = 6.2f, Need = 8f };
            Program.Check(HintText.Of(over, false) != HintText.Of(overFace, false) && HintText.Of(over, true) != HintText.Of(overFace, true), "a hung board's line is not the face board's");
            Program.Check(HintText.Of(over, false).Contains("6.2") && HintText.Of(over, true).Contains("6.2"), "it names the lower edge's height");
            Program.Check(HintText.Of(over, false).Contains("hang") && HintText.Of(over, true).Contains("吊") && !HintText.Of(overFace, false).Contains("hang") && !HintText.Of(overFace, true).Contains("吊"), "the hung line says hang, the face line does not");
            var low = new HintInfo { Kind = "over-low", Left = 5.2f, Need = 6f, Hung = true };
            var lowFace = new HintInfo { Kind = "over-low", Left = 5.2f, Need = 6f };
            Program.Check(HintText.Of(low, false) != HintText.Of(lowFace, false) && HintText.Of(low, false).Contains("5.2") && HintText.Of(low, false).Contains("6.0"), "the low bridge's refusal says what there is and what is needed, not the face's words");
        }

        private static void Placing()
        {
            var spot = new OverSpot { Structure = 7, Point = new Vec3(44f, 6f, 3f), Run = new Vec3(0f, 0f, 1f), Face = new Vec3(-1f, 0f, 0f), Underside = 7.2f, FaceHeight = 1.6f, Depth = 12f };
            OverPlacement hung = OverRoad.Place(spot, 0.5f, 1.2f, 0f, true);
            Program.Check(hung.Refusal == OverRefusal.None, "a board 1.2 m high hangs under a soffit 7.2 m up and 12 m deep");
            Program.Near(hung.Reach, 7.2f, 1e-6f, "its reach is the soffit's height");
            Program.Near(hung.Lower, 7.2f - 1.2f - 0.6f, 1e-5f, "its lower edge follows from it");
            Program.Near(hung.Origin.X, 44f + 0.4f, 1e-5f, "the origin is 0.4 m in from the face line (the face looks to -X)");
            Program.Check(Math.Abs(hung.Origin.Y - 0.5f) < 1e-6f && Math.Abs(hung.Origin.Z - 3f) < 1e-6f, "at the road's height, on the face's line along the structure");
            OverPlacement face = OverRoad.Place(spot, 0.5f, 1.2f, 0f, false);
            Program.Check(face.Refusal == OverRefusal.None && Math.Abs(face.Reach - face.Lower) < 1e-6f && Math.Abs(face.Lower - (7.2f + OverRoad.TopGap)) < 1e-5f, "the face support's reach is its lower edge, TopGap over the soffit");
            Program.Near(face.Origin.X, 44f, 1e-5f, "and its origin is on the face line");

            spot.Underside = 6.0f;
            Program.Check(OverRoad.Place(spot, 0f, 1.2f, 0f, true).Refusal == OverRefusal.None, "6.0 m up is enough for 1.2 m: the lowest soffit that fits");
            spot.Underside = 5.9f;
            OverPlacement low = OverRoad.Place(spot, 0f, 1.2f, 0f, true);
            Program.Check(low.Refusal == OverRefusal.TooLow && Math.Abs(low.Left - 5.9f) < 1e-6f && Math.Abs(low.Need - 6.0f) < 1e-5f, "5.9 m is too low: the line says what there is and what is needed");
            spot.Underside = 6.0f;
            OverPlacement crowned = OverRoad.Place(spot, 0f, 1.2f, 0.2f, true);
            Program.Check(crowned.Refusal == OverRefusal.TooLow && Math.Abs(crowned.Left - 6.0f) < 1e-6f && Math.Abs(crowned.Need - 6.2f) < 1e-5f, "what stands above the board counts (a lamp-post ad's top): 6.2 m is needed, not 6.0");
            Program.Check(OverRoad.Place(spot, 0f, 1.2f, 0.2f, false).Refusal == OverRefusal.None, "and does not for the board on a face (as in 0.12.80)");

            spot.Underside = 8f;
            spot.Depth = 1.1f;
            OverPlacement thin = OverRoad.Place(spot, 0f, 1.2f, 0f, true);
            Program.Check(thin.Refusal == OverRefusal.TooThin && Math.Abs(thin.Left - 1.1f) < 1e-6f && Math.Abs(thin.Need - OverRoad.HungDepth) < 1e-6f, "a structure 1.1 m deep is too thin to hang from");
            Program.Check(OverRoad.Place(spot, 0f, 1.2f, 0f, false).Refusal == OverRefusal.None, "the board on a face does not need depth");
            spot.Underside = 5f;
            Program.Check(OverRoad.Place(spot, 0f, 1.2f, 0f, true).Refusal == OverRefusal.TooLow, "too low and too thin: the low one is said");
            spot.Depth = 12f;
            spot.Underside = 8f;
            spot.FaceHeight = 1.0f;
            Program.Check(OverRoad.Place(spot, 0f, 1.2f, 0f, false).Refusal == OverRefusal.TooLow && OverRoad.Place(spot, 0f, 1.2f, 0f, true).Refusal == OverRefusal.None, "a low face refuses the board flat on it, not the hung one");
        }

        private static void Constants()
        {
            Program.Check(OverRoad.HangSetback == 0.4f && OverRoad.MinHang == 0.3f && OverRoad.HangLength == 0.6f && OverRoad.BraceRun == 0.6f && OverRoad.PlateHalf == 0.09f, "the figures of the hung board are as documented (this mod's estimates)");
            Program.Check(SupportLayouts.HangerPitch == 1.5f && SupportLayouts.PlateThick == 0.012f && SupportLayouts.PlateInto == 0.004f && SignBuilder.OverStandInThick == 0.9f, "so are the rods' pitch, the plates' thickness and how far they are sunk, and the slab");
            Program.Near(OverRoad.HungDepth, 1.19f, 1e-5f, "the depth a hung board needs is 1.19 m");
            Program.Check(SupportLayouts.HangerTubeRadius(2f) == 0.032f && SupportLayouts.HangerTubeRadius(2.01f) == 0.045f && SupportLayouts.HangerTubeRadius(4f) == 0.045f && SupportLayouts.HangerTubeRadius(4.01f) == 0.057f, "the tube's sizes change at 2 m and 4 m");
            Program.Check(SupportLayouts.HangerRodRadius(2.5f) == 0.012f && SupportLayouts.HangerRodRadius(2.51f) == 0.016f && SupportLayouts.HangerRodRadius(5f) == 0.016f && SupportLayouts.HangerRodRadius(5.01f) == 0.020f, "the rods' sizes change at 2.5 m and 5 m");
            Program.Check(SupportLayouts.HangerPositions(1.0f).Length == 2 && SupportLayouts.HangerPositions(2.4f).Length == 3 && SupportLayouts.HangerPositions(3.6f).Length == 3 && SupportLayouts.HangerPositions(6.0f).Length == 5 && SupportLayouts.HangerPositions(9.0f).Length == 7, "rods by width: 1.0 m two, 2.4 m and 3.6 m three, 6.0 m five, 9.0 m seven");
            float[] six = SupportLayouts.HangerPositions(6.0f);
            Program.Near(six[0], -(3f - 0.35f), 1e-5f, "inset a tenth of the width, at most 0.35 m, from each end");
            Program.Near(SupportLayouts.HangerPositions(1.0f)[1], 0.4f, 1e-5f, "and at least 0.1 m: 1.0 m wide, the rods at 0.4 m");
        }

        private static void TextBoard()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            SignInstance text = SignInstances.Of(SignCatalog.ById(Geo.Signs.TextBoxes.TemplateId), Geo.Signs.TextBoxes.Template.Parse("").With("text", "文字灯箱").ToString());
            SignParts parts = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = text } }, Hung, 9f, SupportDetail.Full, default);
            SupportLayout l = parts.Layout;
            Program.Check(l.Hanger && parts.TextOwns == null, "the text's channels are the tube's, not a post's");
            FramePlan frame = TextPlans.Get(text.Board.Text.Job)?.Frame;
            Program.Check(frame != null && frame.Rails.Count > 0, "the text has a frame with rails");
            if (frame == null)
                return;
            FrameStyle style = FrameStyle.For(text.Board.Text.Job.LetterHeight);
            GuidePart main = text.Board.Main;
            CarrierPlan carrier = TextCarrier.Hung(frame, style, main.W, main.H, false);
            Program.Check(carrier.Bolts.Count > 0, "the carrier has bolts where the channels cross the rails");
            // Each bolt is a box on the rails' back: its front face is BoltThickness in front of the rails' back (backZ + the rail's size), at its place on the board.
            float plane = l.BackZ + Math.Max(0.006f, style.RailSize) + TextCarrier.BoltThickness;
            var found = new HashSet<(float, float)>();
            foreach (Vec3 p in parts.Metal.Positions)
            {
                if (Math.Abs(p.Z - plane) < 1e-5f)
                    found.Add((Round(p.X), Round(p.Y)));
            }

            var want = new HashSet<(float, float)>();
            Placement placed = Assembly.Plan(new List<AssemblyBoard> { new AssemblyBoard { Instance = text } }, Hung, 9f).Placed[0];
            foreach (CarrierBolt bolt in carrier.Bolts)
            {
                foreach (float sx in new[] { -1f, 1f })
                {
                    foreach (float sy in new[] { -1f, 1f })
                        want.Add((Round(placed.Cx - bolt.X + sx * TextCarrier.BoltHalf), Round(placed.Cy + bolt.Y + sy * TextCarrier.BoltHalf)));
                }
            }

            Program.Check(want.Count > 0 && found.SetEquals(want), "the bolts are built where the carrier puts them: " + found.Count + " corners for " + want.Count);
        }

        private static void Tile()
        {
            SupportLayout l = SupportLayouts.Of(Plate, Hung, 8f);
            float half = Thumbnails.OverFrame(l, 3.0f, 1.2f, out Vec3 at);
            float top = 8f + SignBuilder.OverStandInThick;
            Program.Check(at.Y - half <= l.BottomClearance && at.Y + half >= top, "the frame holds the board from its lower edge to the slab's top");
            Program.Near(at.Y, (l.BottomClearance + top) * 0.5f, 1e-5f, "centred between them");
            Program.Check(at.X == 0f && at.Z == 0f, "on the board's middle");
            Program.Near(half, 3.0f * 0.5f + 0.25f, 1e-5f, "with a quarter metre of room: a board 3 m wide decides it by its width (1.5 m, the slab's 1.35 m half height is less)");
            Program.Near(Thumbnails.OverFrame(l, 1.0f, 1.2f, out _), (top - l.BottomClearance) * 0.5f + 0.25f, 1e-5f, "a narrow board by the height from its lower edge to the slab's top");
            float wide = Thumbnails.OverFrame(l, 8.0f, 1.2f, out _);
            Program.Near(wide, 4.25f, 1e-5f, "a board 8 m wide decides it by its width");
            Program.Check(Thumbnails.OverFrame(l, 8.0f, 1.2f, out _) > half, "and so is shown wider than a narrow one");
            // The board on a face is framed close, as before: its own size and 0.9 m.
            SupportLayout face = SupportLayouts.Of(Plate, Face, 4.9f);
            Program.Near(Thumbnails.OverFrame(face, 3.0f, 1.2f, out Vec3 faceAt), 3.0f * 0.5f + 0.9f, 1e-6f, "the face support's tile is framed round the board, as in 0.12.80");
            Program.Check(faceAt.X == 0f && faceAt.Z == 0f && Math.Abs(faceAt.Y - face.BoardCentreY) < 1e-6f, "on the board's middle");
            Program.Near(Thumbnails.OverFrame(face, 1.0f, 3.5f, out _), 3.5f * 0.5f + 0.9f, 1e-6f, "by its larger side");
            byte[] picture = new byte[16 * 16 * 4];
            for (int i = 0; i < picture.Length; i++)
                picture[i] = 200;
            byte[] hungTile = Thumbnails.SupportRgba(Hung, picture, 16, 16), faceTile = Thumbnails.SupportRgba(Face, picture, 16, 16);
            Program.Check(hungTile.Length == Thumbnails.Size * Thumbnails.Size * 4 && !System.Linq.Enumerable.SequenceEqual(hungTile, faceTile), "the two tiles are tiles, and are not the same picture");
        }
    }
}
