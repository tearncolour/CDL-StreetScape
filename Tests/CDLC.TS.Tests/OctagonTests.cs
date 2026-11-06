using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 八角锥杆 (the owner's list): the single and the double cantilever with an octagonal tapered post and arms. The arms after Shanghai DG/TJ 08-2362-2021 table A.4.2-3 (corner to corner),
    /// their flanges after table A.4.2-4 and fig. A.4.3, the post after a maker's L-type pole (scratch/research-octagon/REPORT.md); flats to the front and on the arms' top and bottom.
    /// </summary>
    internal static class OctagonTests
    {
        public static void RunAll()
        {
            Program.Run("octagon: the catalogue and the tables", Tables);
            Program.Run("octagon: the tapered post, its flats, its base", Post);
            Program.Run("octagon: the tapered arms, their plates and clamps", Arms);
            Program.Run("octagon: the root paint on the flats", Paint);
        }

        private static SignParts Parts(string sign, string support, float reach, SupportStyle style = default)
        {
            var record = new ItemRecord(SignCatalog.ById(sign).Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(support).Hash, null, reach);
            record.Style = style.Packed;
            return ChunkBuilder.PartsOfGroup(new[] { record });
        }

        private static void Tables()
        {
            SupportDef single = SupportCatalog.ById(SupportCatalog.OctaCantilever), mirror = SupportCatalog.ById(SupportCatalog.OctaCantileverRight), twin = SupportCatalog.ById(SupportCatalog.OctaDoubleCantilever);
            Program.Check(single != null && mirror != null && twin != null && single.Octagonal && mirror.Octagonal && twin.Octagonal, "three octagonal supports");
            Program.Check(single.MirrorId == mirror.Id && mirror.MirrorId == single.Id && mirror.Hidden && !single.Hidden && !twin.Hidden && mirror.Side == -1, "the single's mirror hidden, the double listed");
            Program.Check(SupportCatalog.ByHash(single.Hash) == single && SupportCatalog.ByHash(twin.Hash) == twin, "found again by their hashes (what an item saves)");
            var rows = new (float Length, float Root)[] { (3f, 150f), (4f, 150f), (5f, 200f), (6f, 200f), (7f, 220f), (8f, 220f), (10f, 240f), (12f, 270f), (14f, 285f), (16f, 300f), (20f, 300f) };
            bool table = true;
            foreach ((float length, float root) in rows)
            {
                SupportLayouts.OctaArm(length, 1, out float r, out float t);
                table &= Math.Abs(r * 2000f - root) < 0.5f && Math.Abs(t * 2000f - 120f) < 0.5f;
            }

            Program.Check(table, "a single arm: table A.4.2-3, 150 to 300 at the root by its length, 120 at the tip");
            SupportLayouts.OctaArm(5f, 2, out float r2, out float t2);
            SupportLayouts.OctaArm(8f, 3, out float r3, out float t3);
            Program.Check(Math.Abs(r2 - 0.07f) < 1e-6f && r2 == t2 && Math.Abs(r3 - 0.08f) < 1e-6f && r3 == t3, "2F and 3F arms: octagons of 140 to 6 m, 160 beyond, not tapered");
            Program.Check(SupportLayouts.OctaFlangeHalf(10f, 1) == 0.215f && SupportLayouts.OctaFlangeHalf(12f, 1) == 0.235f && SupportLayouts.OctaFlangeHalf(6f, 3) == 0.175f, "法兰1 430 square to 10 m, 法兰2 470 beyond, 法兰3/4 Ø350 for parallel arms");
            SupportLayouts.OctaPost(6f, 1f, false, out float foot, out float top);
            SupportLayouts.OctaPost(12f, 1f, false, out float foot12, out float top12);
            SupportLayouts.OctaPost(6f, 1f, true, out float footTwin, out _);
            Program.Check(foot == 0.14f && top == 0.11f && foot12 == 0.165f && top12 == 0.14f && footTwin == 0.165f, "the post: 280 to 220 with an arm to 9 m, 330 to 280 else and on a double cantilever");
            BasePlate small = BasePlate.OctaPost(0.14f), large = BasePlate.OctaPost(0.165f);
            Program.Check(small.Round && small.HalfX == 0.25f && large.HalfX == 0.30f && small.BoltX.Length == 8 && Math.Abs(Math.Sqrt(small.BoltX[0] * small.BoltX[0] + small.BoltZ[0] * small.BoltZ[0]) - 0.205) < 1e-4
                && Math.Abs(Math.Sqrt(large.BoltX[3] * large.BoltX[3] + large.BoltZ[3] * large.BoltZ[3]) - 0.24) < 1e-4 && small.BoltRadius == 0.015f && large.BoltRadius == 0.021f,
                "the base: 法兰9 Ø500 8-M30 on Ø410 under 320, 法兰10 Ø600 8-M42 on Ø480");
        }

        /// <summary>The distinct directions (to 1°) of the normals of the post's tube (PostFoot.TubeFrom to TubeTo).</summary>
        private static List<Vec3> TubeNormals(SignParts parts)
        {
            var found = new List<Vec3>();
            PostFoot foot = parts.Feet[0];
            for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
            {
                Vec3 n = parts.Metal.Normals[i];
                if (!found.Exists(f => Vec3.Dot(f, n) > 0.9998f))
                    found.Add(n);
            }

            return found;
        }

        private static void Post()
        {
            SignParts parts = Parts("gb17733.street", SupportCatalog.OctaCantilever, 6f);
            SupportLayout l = parts.Layout;
            Program.Check(l.PoleSides == 8 && l.PoleRadius == 0.14f && l.PoleTopRadius == 0.11f && Math.Abs(l.PoleRadiusAt(l.PoleTop) - 0.11f) < 1e-6f, "an octagonal post 280 to 220");
            List<Vec3> normals = TubeNormals(parts);
            Program.Check(normals.Count == 8 && normals.TrueForAll(n => n.Y > 0f && n.Y < 0.01f) && normals.Exists(n => n.Z > 0.9999f) && normals.Exists(n => n.X > 0.9999f), $"eight flats leaning back a little with the taper, one to the front and one to the arm ({normals.Count})");
            bool corners = true;
            PostFoot foot = parts.Feet[0];
            for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
            {
                Vec3 p = parts.Metal.Positions[i];
                corners &= Math.Abs((float)Math.Sqrt(p.X * p.X + p.Z * p.Z) - l.PoleRadiusAt(p.Y)) < 1e-4f;
            }

            Program.Check(corners && foot.TubeTo > foot.TubeFrom, "its corners on the tapered radius, foot to top");
            parts.Metal.Bounds(out Vec3 lo, out Vec3 hi);
            float plate = 0f;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                if (p.Y < 0.06f)
                    plate = Math.Max(plate, (float)Math.Sqrt(p.X * p.X + p.Z * p.Z));
            }

            Program.Check(l.Base.Round && Math.Abs(plate - 0.25f) < 2e-3f, $"on a round plate Ø500 ({plate * 2f:0.000})");
            SignParts large = Parts("guide.exit.advance", SupportCatalog.OctaDoubleCantilever, 6f);
            Program.Check(large.Layout.PoleRadius == 0.165f && large.Layout.Base.HalfX == 0.30f && large.Layout.ArmYs.Length > 1, "a double cantilever: 330 on Ø600, parallel arms");
        }

        private static void Arms()
        {
            SignParts parts = Parts("gb17733.street", SupportCatalog.OctaCantilever, 6f);
            SupportLayout l = parts.Layout;
            Program.Check(l.ArmYs.Length == 1 && Math.Abs(l.ArmLength - 6f) < 1e-3f && l.ArmRootRadius == 0.10f && l.ArmTipRadius == 0.06f, $"one arm of 6 m, 200 to 120 ({l.ArmLength:0.00} m, {l.ArmRootRadius}, {l.ArmTipRadius})");
            float y = l.ArmY;
            // The arm's flats on top and in front where the taper puts them, from the plate to the tip (its vertices are at those two ends).
            float leave = l.PoleRadiusAt(y) * (float)Math.Cos(Math.PI / 8), middle = leave + SupportLayouts.JointStub, root = middle + SupportLayouts.JointPlate;
            float flat = (float)Math.Cos(Math.PI / 8);
            bool upRoot = false, upTip = false, frontRoot = false, frontTip = false;
            for (int i = 0; i < parts.Metal.VertexCount; i++)
            {
                Vec3 p = parts.Metal.Positions[i], n = parts.Metal.Normals[i];
                bool atRoot = Math.Abs(p.X - root) < 1e-4f, atTip = Math.Abs(p.X - l.ArmLength) < 1e-4f;
                float face = l.ArmRadiusAt(p.X) * flat;
                bool up = n.Y > 0.9999f && Math.Abs(p.Y - y - face) < 1e-4f, front = n.Z > 0.9999f && Math.Abs(p.Z - face) < 1e-4f;
                upRoot |= up && atRoot;
                upTip |= up && atTip;
                frontRoot |= front && atRoot;
                frontTip |= front && atTip;
            }

            Program.Check(upRoot && upTip && frontRoot && frontTip, $"the arm a tapered octagon, flats on top and in front at the plate ({l.ArmRadiusAt(root) * 2000f:0} corner to corner) and at the tip ({l.ArmTipRadius * 2000f:0})");
            // The square plates: 430 across, JointStub out from the post's flat.
            bool square = false;
            foreach (Vec3 p in parts.Metal.Positions)
                square |= Math.Abs(p.X - (middle + SupportLayouts.JointPlate)) < 1e-4f && Math.Abs(Math.Abs(p.Y - y) - 0.215f) < 1e-4f && Math.Abs(Math.Abs(p.Z) - 0.215f) < 1e-4f;
            Program.Check(square, "a square plate 430 × 430 at the stub's end");
            // The clamps round the arm where the board is: on its radius there.
            float clamp = float.NaN;
            float bx = l.BoardCentreX;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                float d = (float)Math.Sqrt((p.Y - y) * (p.Y - y) + p.Z * p.Z);
                if (Math.Abs(p.X - bx) < 0.6f && d > l.ArmRadiusAt(p.X) + 0.004f && d < l.ArmRadiusAt(p.X) + 0.006f)
                    clamp = d - l.ArmRadiusAt(p.X);
            }

            Program.Check(Math.Abs(clamp - 0.005f) < 1e-3f, $"the clamps 5 mm round the arm's taper where the board is ({clamp:0.0000})");
            SignParts mirrored = Parts("gb17733.street", SupportCatalog.OctaCantileverRight, 6f);
            mirrored.Metal.Bounds(out Vec3 mlo, out Vec3 mhi);
            Program.Check(mlo.X < -5.9f && mhi.X < 0.4f, "the mirror reaches to -X");
            SignParts three = Parts("guide.exit.advance", SupportCatalog.OctaCantilever, 6f);
            bool round = false;
            foreach (Vec3 p in three.Metal.Positions)
                round |= Math.Abs(Math.Sqrt((p.Y - three.Layout.ArmY) * (p.Y - three.Layout.ArmY) + p.Z * p.Z) - 0.175) < 1e-4 && p.X > 0.2f && p.X < 0.5f;
            Program.Check(three.Layout.ArmYs.Length > 1 && three.Layout.ArmRootRadius == 0.07f && round, "a large board: parallel arms of 140 on round plates Ø350");
        }

        private static void Paint()
        {
            SignParts parts = Parts("gb17733.street", SupportCatalog.OctaCantilever, 6f, new SupportStyle { Root = RootPaint.Marking });
            SupportLayout l = parts.Layout;
            bool outside = true;
            int count = 0;
            foreach (SolidColour colour in new[] { SolidColour.Yellow, SolidColour.Black })
            {
                MeshBuilder mesh = parts.Solids[(int)colour];
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    Vec3 p = mesh.Positions[i], n = mesh.Normals[i];
                    float facet = l.PoleRadiusAt(p.Y) * (float)Math.Cos(Math.PI / 8);
                    outside &= Math.Abs(p.X * n.X + p.Z * n.Z - facet - 0.002f) < 1e-4f;
                    count++;
                }
            }

            Program.Check(count > 100 && outside, "the stripes 2 mm outside the post's flats all the way up its taper");
        }
    }
}
