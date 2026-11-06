using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 上海型综合杆 (the owner: "上海型综合杆（多杆合一，石墨黑十二棱主杆，有完整标准尺寸）"): the main pole by DG/TJ 08-2362-2021 table A.4.2-1 against its arms' moments (table A.4.2-3), twelve
    /// sides, its top over the upper arm (figs. A.2.3-2, -5), its flange under the paving; the Ø160 upper pole to 12.5 m with the lamp at 12 m (2018 guide appendix B); the door from 0.5 m, the slots
    /// from 2.5 m; graphite by default.
    /// </summary>
    internal static class IntegratedTests
    {
        public static void RunAll()
        {
            Program.Run("integrated: the catalogue and the main pole's table", Tables);
            Program.Run("integrated: the main pole, buried, twelve-sided, to over its arm", MainPole);
            Program.Run("integrated: the upper pole, the lamp, the door and the slots", Fittings);
        }

        private static SignParts Parts(string sign, string support, float reach)
        {
            var record = new ItemRecord(SignCatalog.ById(sign).Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(support).Hash, null, reach);
            return ChunkBuilder.PartsOfGroup(new[] { record });
        }

        private static void Tables()
        {
            SupportDef pole = SupportCatalog.ById(SupportCatalog.Integrated), mirror = SupportCatalog.ById(SupportCatalog.IntegratedRight);
            Program.Check(pole != null && mirror != null && pole.Integrated && mirror.Integrated && !pole.Hidden && mirror.Hidden && pole.MirrorId == mirror.Id && mirror.Side == -1, "listed, with a hidden mirror");
            Program.Check(pole.DefaultFinish == SupportFinish.Graphite && mirror.DefaultFinish == SupportFinish.Graphite && SupportCatalog.ById(SupportCatalog.Cantilever).DefaultFinish == null, "graphite (RAL 9011) when chosen; the others keep the finish");
            var cases = new (float Length, int Arms, float Foot, float Top)[] { (6f, 1, 0.14f, 0.12f), (12f, 1, 0.14f, 0.12f), (14f, 1, 0.15f, 0.12f), (16f, 1, 0.16f, 0.14f), (6f, 3, 0.14f, 0.12f), (8f, 3, 0.16f, 0.14f), (8f, 2, 0.14f, 0.12f) };
            bool table = true;
            foreach ((float length, int arms, float foot, float top) in cases)
            {
                SupportLayouts.MainPole(length, arms, out float f, out float t);
                table &= f == foot && t == top;
            }

            Program.Check(table, "主杆5 (280-240, 100 kN·m), 主杆6 (300-240, 110), 主杆7 (320-280, 200) by the arms' rated moments together");
            Program.Check(SupportLayouts.MainPoleAbove(1) == 0.235f && SupportLayouts.MainPoleAbove(3) == 0.22f, "the main pole's top 235 over a single arm, 220 over parallel ones");
        }

        private static void MainPole()
        {
            SignParts parts = Parts("gb17733.street", SupportCatalog.Integrated, 6f);
            SupportLayout l = parts.Layout;
            Program.Check(l.Integrated && l.OctaArms && l.PoleSides == 12 && l.PoleRadius == 0.14f && l.PoleTopRadius == 0.12f, "a twelve-sided main pole 280 to 240, 八角锥杆's arms");
            Program.Check(Math.Abs(l.PoleTop - l.ArmY - 0.235f) < 1e-5f, $"its top 235 over the arm ({l.PoleTop:0.000} / {l.ArmY:0.000})");
            PostFoot foot = parts.Feet[0];
            var normals = new List<Vec3>();
            float low = float.MaxValue;
            for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
            {
                Vec3 n = parts.Metal.Normals[i];
                low = Math.Min(low, parts.Metal.Positions[i].Y);
                if (!normals.Exists(f => Vec3.Dot(f, n) > 0.9998f))
                    normals.Add(n);
            }

            Program.Check(normals.Count == 12 && normals.Exists(n => n.Z > 0.9999f), $"twelve flats, one to the front ({normals.Count})");
            Program.Check(low < -0.29f && parts.Concrete.VertexCount == 0, "from under the paving, no foundation drawn (the flange is buried)");
            // Nothing round the foot wider than the pole: no base plate, no ribs, no bolts (the door's 5 mm aside).
            bool bare = true;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                if (p.Y < 0.45f)
                    bare &= Math.Sqrt(p.X * p.X + p.Z * p.Z) <= l.PoleRadiusAt(p.Y) + 1e-4f;
            }

            Program.Check(bare, "a bare pole at the foot");
            SignParts three = Parts("guide.exit.advance", SupportCatalog.Integrated, 6f);
            Program.Check(three.Layout.ArmYs.Length > 1 && Math.Abs(three.Layout.PoleTop - three.Layout.ArmYs[0] - 0.22f) < 1e-5f, "parallel arms: the top 220 over the upper one");
        }

        private static void Fittings()
        {
            SignParts parts = Parts("gb17733.street", SupportCatalog.Integrated, 6f);
            SupportLayout l = parts.Layout;
            parts.Metal.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Check(Math.Abs(hi.Y - (SignBuilder.IntegratedHeight + 0.012f)) < 1e-4f, $"the upper pole and its flange to 12.5 m ({hi.Y:0.000})");
            bool upper = true;
            int seen = 0;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                float d = (float)Math.Sqrt(p.X * p.X + p.Z * p.Z);
                if (p.Y > l.PoleTop + 0.2f && p.Y < 11.0f && d < 0.2f)
                {
                    upper &= Math.Abs(d - SignBuilder.UpperRadius) < 1e-4f;
                    seen++;
                }
            }

            Program.Check(upper && seen > 0, "Ø160 between the collar and the lamp's clamp");
            MeshBuilder lens = parts.Solids[(int)SolidColour.LightGray];
            lens.Bounds(out Vec3 llo, out Vec3 lhi);
            Program.Check(lens.VertexCount > 0 && llo.X > SignBuilder.LampReach && Math.Abs((llo.Y + lhi.Y) * 0.5f - (SignBuilder.LampHeight - 0.052f)) < 1e-3f, $"the lamp's head over the road at 12 m ({llo.X:0.00}..{lhi.X:0.00}, {llo.Y:0.000})");
            SignParts mirrored = Parts("gb17733.street", SupportCatalog.IntegratedRight, 6f);
            mirrored.Solids[(int)SolidColour.LightGray].Bounds(out Vec3 mlo, out _);
            Program.Check(mlo.X < -SignBuilder.LampReach, "the mirror's lamp to -X with its arm");

            // The door's seam on the back flat from 0.5 to 1.3 m; the slots on the four faces from 2.5 m.
            MeshBuilder dark = parts.Solids[(int)SolidColour.Black];
            bool door = false;
            var faces = new HashSet<int>();
            foreach (Vec3 p in dark.Positions)
            {
                door |= p.Z < -0.1f && Math.Abs(p.X) < 0.04f && p.Y > 0.49f && p.Y < 0.51f;
                if (p.Y > SignBuilder.SlotFrom - 1e-3f && p.Y < SignBuilder.SlotFrom + 1e-3f)
                    faces.Add((int)Math.Round(Math.Atan2(p.X, p.Z) / (Math.PI / 2)) & 3);
            }

            Program.Check(door && parts.Feet[0].Paint.Exists(r => r.Colour == SolidColour.Black), "the door's seam on the back, moving with the foot");
            Program.Check(faces.Count == 4, $"slots on four faces from 2.5 m ({faces.Count})");
            var record = new ItemRecord(SignCatalog.ById("gb17733.street").Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(SupportCatalog.Integrated).Hash, null, 6f);
            float Seam(ItemRecord item)
            {
                float low = float.MaxValue;
                foreach (ChunkPart part in ChunkBuilder.Build(new[] { item }, Vec3.Zero, out _))
                {
                    if (part.Material != ChunkBuilder.SolidMaterial(SolidColour.Black))
                        continue;
                    foreach (Vec3 p in part.Mesh.Positions)
                        low = Math.Min(low, p.Y);
                }

                return low;
            }

            float flat = Seam(record);
            record.Ground = GroundProfile.Sample((x, z) => 0.2f, -3f, 3f);
            Program.Check(Math.Abs(Seam(record) - flat - 0.2f) < 1e-3f, "on ground 0.2 m higher the door 0.2 m higher");
        }
    }
}
