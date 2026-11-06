using System;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 杭州框架式悬挂悬臂 (the owner's list): the board in a black frame hung by two rods from the upper arm, which passes it and the post; a lower arm to the frame at its middle; black back,
    /// no lean, matte black poles (Hangzhou 2015 guide 5.7, 5.9.1, fig. A.1; photo c9).
    /// </summary>
    internal static class FramedTests
    {
        public static void RunAll()
        {
            Program.Run("framed: the catalogue, black, plumb", Catalogue);
            Program.Run("framed: the arms, the hangers and the frame", Build);
        }

        private static SignParts Parts(string support, float reach, SupportStyle style = default, float tilt = 0f)
        {
            var record = new ItemRecord(SignCatalog.ById("gb17733.street").Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(support).Hash, null, reach);
            record.Style = style.Packed;
            record.Tilt = tilt;
            return ChunkBuilder.PartsOfGroup(new[] { record });
        }

        private static void Catalogue()
        {
            SupportDef framed = SupportCatalog.ById(SupportCatalog.Framed), mirror = SupportCatalog.ById(SupportCatalog.FramedRight);
            Program.Check(framed != null && mirror != null && framed.Framed && mirror.Framed && mirror.Hidden && !framed.Hidden && mirror.Side == -1 && framed.MirrorId == mirror.Id, "listed with a hidden mirror");
            Program.Check(framed.DefaultFinish == SupportFinish.Graphite, "matte black when chosen (5.9.1)");
            Program.Check(!BoardTilt.Leans(framed) && BoardTilt.Leans(SupportCatalog.ById(SupportCatalog.Cantilever)) && !BoardTilt.Leans((SupportDef)null), "its boards hang plumb; a plain cantilever's lean");
            SignParts leaning = Parts(SupportCatalog.Framed, 7f, default, BoardTilt.Radians(2));
            Program.Check(Math.Abs(leaning.Faces[0].Mesh.Normals[0].Y) < 1e-4f, "a lean on the record is not drawn");
            SignParts galvanised = Parts(SupportCatalog.Framed, 7f);
            MeshBuilder black = galvanised.Solids[(int)SolidColour.Graphite];
            black.Bounds(out Vec3 lo, out Vec3 hi);
            galvanised.Faces[0].Mesh.Bounds(out Vec3 flo, out Vec3 fhi);
            Program.Check(black.VertexCount > 0 && lo.Z < galvanised.Layout.BackZ + 1e-4f && lo.X < flo.X && hi.X > fhi.X, "the back and the frame black on galvanised steel (5.7)");
        }

        private static void Build()
        {
            SignParts parts = Parts(SupportCatalog.Framed, 7f);
            SupportLayout l = parts.Layout;
            parts.Faces[0].Mesh.Bounds(out Vec3 flo, out Vec3 fhi);
            float w = fhi.X - flo.X;
            Program.Check(l.Framed && l.ArmYs.Length == 1 && Math.Abs(l.ArmLength - 7f) < 1e-3f, "one upper arm of 7 m");
            Program.Check(Math.Abs(l.ArmY - (fhi.Y + SupportLayouts.FrameFace + SupportLayouts.FrameHang)) < 2e-3f, $"the upper arm 0.3 m over the frame ({l.ArmY:0.000} / {fhi.Y:0.000})");
            Program.Check(Math.Abs(l.ArmLength - SupportLayouts.FrameTipPast * w - fhi.X) < 2e-3f, $"it passes the board's outer edge by {SupportLayouts.FrameTipPast} of its width");
            Program.Check(Math.Abs(l.LowerArmY - (flo.Y + fhi.Y) * 0.5f) < 1e-3f, "the lower arm at the board's middle");
            Program.Check(Math.Abs(fhi.Z - (SupportLayouts.FrameDepth * 0.5f - 0.01f)) < 1e-4f, "the board's face inside its frame, round the arms' plane");
            // The upper arm from 0.09 of its reach behind the post to its tip; the lower one from 0.07 behind to the frame.
            float back = float.MaxValue, lowerFront = float.MinValue, lowerBack = float.MaxValue;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                if (Math.Abs(p.Y - l.ArmY) < 0.06f && Math.Abs(p.Z) < 0.06f)
                    back = Math.Min(back, p.X);
                if (Math.Abs(p.Y - l.LowerArmY) < 0.05f && Math.Abs(p.Z) < 0.05f)
                {
                    lowerFront = Math.Max(lowerFront, p.X);
                    lowerBack = Math.Min(lowerBack, p.X);
                }
            }

            Program.Check(Math.Abs(back + SupportLayouts.FrameBeyondPole * l.ArmLength) < 0.05f, $"the upper arm 0.09 of its reach past the post ({back:0.000})");
            Program.Check(Math.Abs(lowerBack + SupportLayouts.LowerBeyondPole * l.ArmLength) < 0.05f && lowerFront > flo.X - SupportLayouts.FrameFace - 0.02f && lowerFront < flo.X + 0.02f, $"the lower arm from past the post to the frame ({lowerBack:0.000} .. {lowerFront:0.000}, frame at {flo.X - SupportLayouts.FrameFace:0.000})");
            // Two hangers between the frame's top and the arm, in from each edge.
            int hangers = 0;
            foreach (float x in new[] { fhi.X - SupportLayouts.HangerInOuter * w, flo.X + SupportLayouts.HangerInInner * w })
            {
                bool found = false;
                foreach (Vec3 p in parts.Metal.Positions)
                    found |= Math.Abs(p.X - x) < SupportLayouts.HangerRadius + 1e-4f && Math.Abs(p.Y - (l.ArmY - l.ArmRootRadius * 0.5f)) < 1e-3f && Math.Abs(p.Z) < SupportLayouts.HangerRadius + 1e-4f;
                hangers += found ? 1 : 0;
            }

            Program.Check(hangers == 2, $"two hangers ({hangers})");
            Program.Check(l.PoleTop > l.ArmY + 0.4f, "the post over the upper arm");
            SignParts mirrored = Parts(SupportCatalog.FramedRight, 7f);
            mirrored.Faces[0].Mesh.Bounds(out Vec3 mlo, out _);
            Program.Check(mlo.X < -5f, "the mirror reaches to -X");
        }
    }
}
