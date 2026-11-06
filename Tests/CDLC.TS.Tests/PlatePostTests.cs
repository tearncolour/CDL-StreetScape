using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 路名牌立柱 (the owner: "路名牌立柱（上海、徐州、杭州、深圳四种样式）"): the plate on a head at the post's top, its plane through the post's axis, printed on both faces; the heads, posts and
    /// heights of Xuzhou DB3203/T 1080-2025, Shanghai DB31/T 416 (as quoted), the Hangzhou 2015 guide fig. B.1 and a Shenzhen maker; boards under the head (figs. B.2, B.3).
    /// </summary>
    internal static class PlatePostTests
    {
        public static void RunAll()
        {
            Program.Run("plate post: the catalogue and the heads", Heads);
            Program.Run("plate post: the plate on its head, both faces reading", Plate);
            Program.Run("plate post: boards under the head", Stack);
        }

        private static SignInstance Plate(string spec) => SignInstances.Of(SignCatalog.ById("gb17733.street"), spec);

        private static void Heads()
        {
            var ids = new[] { (SupportCatalog.PlateShanghai, PlateStyle.Shanghai, SupportFinish.LightGrey), (SupportCatalog.PlateXuzhou, PlateStyle.Xuzhou, SupportFinish.LightGrey),
                (SupportCatalog.PlateHangzhou, PlateStyle.Hangzhou, SupportFinish.Graphite), (SupportCatalog.PlateShenzhen, PlateStyle.Shenzhen, SupportFinish.LightGrey) };
            bool all = true;
            foreach ((string id, PlateStyle style, SupportFinish finish) in ids)
            {
                SupportDef def = SupportCatalog.ById(id);
                all &= def != null && !def.Hidden && def.Kind == SupportKind.SinglePole && def.Plate == style && def.DefaultFinish == finish;
            }

            Program.Check(all, "four plate posts listed, each of its city, in its paint");
            Program.Check(PlateHead.Large(1.5f, 0.45f) && !PlateHead.Large(1.2f, 0.36f), "A for 1500 × 450, B for 1200 × 360 (DB3203/T 1080 5.1.2)");
            PlateHead a = PlateHead.Of(PlateStyle.Xuzhou, true), b = PlateHead.Of(PlateStyle.Xuzhou, false);
            Program.Check(a.Height == 0.25f && a.TopA == 0.20f && a.TopB == 0.10f && a.SecondA == 0.125f && a.SecondB == 0.08f && a.PostRadius == 0.0445f && a.Clearance == 2.5f,
                "Xuzhou A: a 400 × 200 tray over 250 × 160, 250 in all, on φ89, the plate at 2.5 m");
            Program.Check(b.Height == 0.20f && b.TopA == 0.165f && b.TopB == 0.085f && b.SecondA == 0.115f && b.SecondB == 0.07f && b.PostRadius == 0.038f && b.Clearance == 2.5f,
                "Xuzhou B: 330 × 170 over 230 × 140, 200 in all, on φ76");
            Program.Check(Math.Abs(a.TopThick + a.Neck + a.SecondThick + 0.19f - a.Height) < 1e-6f && Math.Abs(b.TopThick + b.Neck + b.SecondThick + 0.14f - b.Height) < 1e-6f, "trays 20, neck 20, second 20 and a cone of 190 or 140");
            Program.Check(PlateHead.Of(PlateStyle.Shanghai, false).Clearance == 2.14f && PlateHead.Of(PlateStyle.Shanghai, true).Clearance == 2.5f, "Shanghai: a B plate at 2140, an A plate at 2500");
            PlateHead hz = PlateHead.Of(PlateStyle.Hangzhou, false), sz = PlateHead.Of(PlateStyle.Shenzhen, false);
            Program.Check(hz.Height == 0.24f && hz.TopA == 0.165f && hz.PostRadius == 0.038f && hz.Clearance == 2.0f, "Hangzhou: a 330 tray 240 tall on Ø76, the plate at 2 m");
            Program.Check(sz.Flare && sz.Flutes == 24 && sz.PlateDepth == 0.008f && sz.Clearance == 2.0f, "Shenzhen: a flare on a fluted post, an 8 mm plate at 2 m");
        }

        private static void Plate()
        {
            SupportDef support = SupportCatalog.ById(SupportCatalog.PlateShanghai);
            SignParts parts = SignBuilder.Build(Plate("len=1500;wid=450"), support);
            SupportLayout l = parts.Layout;
            PlateHead head = l.Head;
            parts.Face.Bounds(out Vec3 lo, out Vec3 hi);
            Program.Check(Math.Abs(lo.Y - 2.5f) < 1e-3f && Math.Abs(lo.Z + head.PlateDepth * 0.5f) < 1e-5f && Math.Abs(hi.Z - head.PlateDepth * 0.5f) < 1e-5f && Math.Abs(lo.X + hi.X) < 1e-3f,
                $"the plate's lower edge at 2.5 m, round the post's axis, {head.PlateDepth * 1000f:0.0} thick");
            Program.Check(Math.Abs(l.PoleTop - (2.5f - head.Height + 0.03f)) < 1e-4f && l.PoleRadius == 0.0445f, "the φ89 post ends 3 cm up the cone");
            // The tray under the plate: 400 × 200 at its top.
            float trayX = 0f, trayZ = 0f, lowest = float.MaxValue;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                if (Math.Abs(p.Y - 2.5f) < 1e-4f && Math.Abs(p.Z) < 0.2f)
                {
                    trayX = Math.Max(trayX, Math.Abs(p.X) < 0.3f ? Math.Abs(p.X) : 0f);
                    trayZ = Math.Max(trayZ, Math.Abs(p.Z));
                }

                lowest = Math.Min(lowest, p.Y);
            }

            Program.Check(Math.Abs(trayX - 0.2f) < 1e-3f && Math.Abs(trayZ - 0.1f) < 1e-3f, $"the oval tray 400 × 200 under the plate ({trayX * 2000f:0} × {trayZ * 2000f:0})");
            Program.Check(lowest < -0.29f && parts.Concrete.VertexCount == 0, "the post from under the paving, its flange buried");
            // Both faces read the right way: the front's U grows to the viewer's right (-X), the back's to +X (the viewer's right from behind).
            MeshBuilder face = parts.Face;
            int fl = -1, fr = -1, bl = -1, br = -1;
            for (int i = 0; i < face.VertexCount; i++)
            {
                bool front = face.Normals[i].Z > 0.999f, back = face.Normals[i].Z < -0.999f;
                if (front && (fl < 0 || face.Positions[i].X > face.Positions[fl].X)) fl = i;
                if (front && (fr < 0 || face.Positions[i].X < face.Positions[fr].X)) fr = i;
                if (back && (bl < 0 || face.Positions[i].X < face.Positions[bl].X)) bl = i;
                if (back && (br < 0 || face.Positions[i].X > face.Positions[br].X)) br = i;
            }

            Program.Check(fl >= 0 && bl >= 0 && face.U[fr] > face.U[fl] + 0.9f && face.U[br] > face.U[bl] + 0.9f, "the name reads from the front and from behind");
            SignParts sz = SignBuilder.Build(Plate("len=1200;wid=360"), SupportCatalog.ById(SupportCatalog.PlateShenzhen));
            PostFoot foot = sz.Feet[0];
            var normals = new List<Vec3>();
            for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
            {
                if (!normals.Exists(n => Vec3.Dot(n, sz.Metal.Normals[i]) > 0.9998f))
                    normals.Add(sz.Metal.Normals[i]);
            }

            Program.Check(normals.Count == 24, $"Shenzhen's fluted post: 24 flats ({normals.Count})");
            SignParts stop = SignBuilder.Build(SignInstances.Of(SignCatalog.ById("gb.stop"), null), support);
            Program.Check(stop.Layout.Head == null && stop.Layout.PoleTop > stop.Layout.BoardCentreY && stop.Layout.BackZ > stop.Layout.PoleRadius, "a sign that is no plate: a plain post behind it (the head is for plates)");
        }

        private static void Stack()
        {
            SupportDef support = SupportCatalog.ById(SupportCatalog.PlateHangzhou);
            var ordered = new List<AssemblyBoard>
            {
                new AssemblyBoard { Instance = Plate("len=1120;wid=360;run=ns"), Member = 0, Slot = 0 },
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), Member = 1, Slot = 1 }
            };
            AssemblyPlan plan = Assembly.Plan(Assembly.Order(ordered), support, 0f);
            Placement plate = plan.Placed.Find(p => p.OnHead), under = plan.Placed.Find(p => !p.OnHead);
            Program.Check(plate != null && plate.Board.Instance.Def.Id == "gb17733.street", "the plate on the head though a prohibition comes first in the order (4.8.6)");
            Program.Check(Math.Abs(under.Bottom - Assembly.PlateStackBottom) < 1e-4f && Math.Abs(plate.Bottom - under.Top - under.Def.ExtraTop - plan.Layout.Head.GapUnder) < 1e-4f,
                $"the lowest board at 1.9 m, the head's room and 5 cm over it (fig. B.2): {under.Bottom:0.000}, {under.Top:0.000}, {plate.Bottom:0.000}");
            Program.Check(Math.Abs(plan.Layout.PoleTop - (plate.Bottom - plan.Layout.Head.Height + 0.03f)) < 1e-4f, "the post up to the head");
            SignParts parts = SignBuilder.BuildAssembly(ordered, support, 0f);
            Program.Check(parts.Faces.Count == 2 && parts.Metal.VertexCount > 0, "both boards drawn");
        }
    }
}
