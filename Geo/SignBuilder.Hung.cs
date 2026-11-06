using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The bases, joints and overhead supports of 0.12.53, after 22MR601 交通标志支撑结构设计图 and the photos in scratch/research-supports-overhead (the owner: "不要自己猜，优化现有立柱结构，和实物一致"):
    /// straight posts on base plates over a pedestal level with the ground, arms and chords on flanged stubs, no braces, boards on a grid of slide channels with a clamp at every crossing.
    /// </summary>
    public static partial class SignBuilder
    {
        /// <summary>The top of a foundation's pedestal (1 cm over the ground it is level with) and how deep it is drawn (as far as a foot that goes down the ground may show of it).</summary>
        private const float PedestalTop = 0.01f, PedestalBottom = -0.30f;

        /// <summary>
        /// A post's base (BasePlate) at (x, z), the post round of radius postR: the pedestal (unless pedestal is false: one is drawn for several plates), the positioning plate and the flange on it, the
        /// anchor bolts BoltAbove over the flange with two nuts, and the ribs. Returns the flange's top, where the post starts.
        /// </summary>
        private static float AddBase(SignParts parts, BasePlate b, float x, float z, float postR, bool pedestal = true)
        {
            if (pedestal)
                AddPedestal(parts, x, z, b.HalfX + SupportLayouts.PedestalMargin, b.HalfZ + SupportLayouts.PedestalMargin);
            float lower = PedestalTop + b.LowerThick, top = lower + b.Thick;
            if (b.Round)
            {
                Solids.Tube(parts.Metal, new Vec3(x, PedestalTop, z), new Vec3(x, lower, z), b.HalfX, b.HalfX, 32, true, true);
                Solids.Tube(parts.Metal, new Vec3(x, lower, z), new Vec3(x, top, z), b.HalfX - 0.001f, b.HalfX - 0.001f, 32, true, true);
            }
            else
            {
                Solids.Box(parts.Metal, new Vec3(x, PedestalTop + b.LowerThick * 0.5f, z), new Vec3(b.HalfX, b.LowerThick * 0.5f, b.HalfZ));
                Solids.Box(parts.Metal, new Vec3(x, lower + b.Thick * 0.5f, z), new Vec3(b.HalfX - 0.001f, b.Thick * 0.5f, b.HalfZ - 0.001f));
            }
            if (t_Reduced)
                return top;
            for (int i = 0; i < b.BoltX.Length; i++)
            {
                var at = new Vec3(x + b.BoltX[i], top, z + b.BoltZ[i]);
                Solids.Tube(parts.Metal, at, at + new Vec3(0f, b.BoltAbove, 0f), b.BoltRadius, b.BoltRadius, 6, false, true);
                // Two nuts, each 0.8 of the bolt's diameter high, 1.6 of its radius across the corners' circle.
                Solids.Tube(parts.Metal, at, at + new Vec3(0f, 3.2f * b.BoltRadius, 0f), 1.6f * b.BoltRadius, 1.6f * b.BoltRadius, 6, false, true);
            }

            var foot = new Vec3(x, top, z);
            if (b.RibAngles != null)
            {
                foreach (float angle in b.RibAngles)
                {
                    float a = angle * (float)Math.PI / 180f;
                    var radial = new Vec3((float)Math.Cos(a), 0f, (float)Math.Sin(a));
                    RibPlate(parts.Metal, foot + radial * (postR - 0.004f), radial, b.RibOut + 0.004f, b, 1f);
                }
            }
            else
            {
                // An H leg: two ribs off each face of its web (±X) and off each flange's outer face (±Z).
                foreach (float s in new[] { -1f, 1f })
                {
                    foreach (float side in new[] { -0.05f, 0.05f })
                    {
                        RibPlate(parts.Metal, foot + new Vec3(s * HWeb, 0f, side), new Vec3(s, 0f, 0f), b.RibOut, b, 1f);
                        RibPlate(parts.Metal, foot + new Vec3(side, 0f, s * SupportLayouts.GantryLegHalf), new Vec3(0f, 0f, s), b.RibOut, b, 1f);
                    }
                }
            }

            return top;
        }

        private static void AddPedestal(SignParts parts, float x, float z, float halfX, float halfZ) =>
            Solids.Box(parts.Concrete, new Vec3(x, (PedestalTop + PedestalBottom) * 0.5f, z), new Vec3(halfX, (PedestalTop - PedestalBottom) * 0.5f, halfZ));

        /// <summary>
        /// A rib standing on the plate (foot on its top) from a post outward along out: length long, the plate's RibHigh tall, its inner edge upright, RibTop wide at the top and RibEnd high at the
        /// outer end (22MR601's ribs), scale times its thickness.
        /// </summary>
        private static void RibPlate(MeshBuilder metal, Vec3 foot, Vec3 outward, float length, BasePlate b, float scale)
        {
            var up = new Vec3(0f, b.RibHigh, 0f);
            Vec3 side = new Vec3(-outward.Z, 0f, outward.X) * (b.RibThick * 0.5f * scale);
            Solids.Slab(metal, new[] { foot, foot + outward * length, foot + outward * length + up * (b.RibEnd / b.RibHigh), foot + outward * b.RibTop + up, foot + up }, side);
        }

        /// <summary>A post's cap (22MR601 立柱盖帽: a 5 mm plate with a 30 mm rim, its radius 5.5 mm over the tube's), its top 5 mm over the post's end.</summary>
        private static void PostCap(MeshBuilder metal, float x, float z, float postTop, float r) =>
            Solids.Tube(metal, new Vec3(x, postTop - 0.025f, z), new Vec3(x, postTop + 0.005f, z), r + 0.0055f, r + 0.0055f, 16, true, true);

        /// <summary>
        /// The joint of a tube to a post at px (radius postR) along dir (+1 or -1 on X) at height y (SupportLayouts.JointFlangeOf; 22MR601 横梁与立柱连接图): a stub of the tube from inside the post to a
        /// pair of flanges JointStub out from the post's face, bolted together (bolts of boltR, 25 mm in from the rims), and ribs on four sides of the stub and of the tube beyond the flanges (ribLength
        /// long, tapering to 30 mm). Returns where the tube goes on from, at the outer flange's face.
        /// </summary>
        private static float FlangeJoint(MeshBuilder metal, float px, float y, float z, float dir, float postR, float tubeR, float flangeR, int bolts, float boltR, float ribLength, float ribThick) =>
            FlangeJoint(metal, new Vec3(px, y, z), new Vec3(dir, 0f, 0f), postR, tubeR, flangeR, bolts, boltR, ribLength, ribThick).X;

        /// <summary>
        /// As above along d, a way in the plane of the post's axis and X (the cranked arm's 18° up: 0.12.55), from root on the post's axis; returns the middle of the outer flange's face. Each corner of
        /// the stub's ribs starts on the post's surface; the side ribs reach no further out than the post they stand on.
        /// </summary>
        private static Vec3 FlangeJoint(MeshBuilder metal, Vec3 root, Vec3 d, float postR, float tubeR, float flangeR, int bolts, float boltR, float ribLength, float ribThick)
        {
            float dx = Math.Abs(d.X), sign = d.X < 0f ? -1f : 1f;
            float leave = postR / dx, middle = leave + SupportLayouts.JointStub;
            float inner = middle - SupportLayouts.JointPlate, outer = middle + SupportLayouts.JointPlate;
            Vec3 At(float t) => root + d * t;
            Solids.Tube(metal, At(leave * 0.5f), At(inner), tubeR, tubeR, 12, false, false);
            Solids.Tube(metal, At(inner), At(outer), flangeR, flangeR, 16, true, true);
            if (t_Reduced)
                return At(outer);
            var up = new Vec3(-d.Y * sign, d.X * sign, 0f);
            var side = new Vec3(0f, 0f, 1f);
            foreach (Vec3 radial in new[] { up, up * -1f, side, side * -1f })
            {
                Vec3 thick = MeshBuilder.Cross(d, radial) * (ribThick * 0.5f);
                // On the post's surface at a height h along radial: where the corner's distance from the axis across is the post's radius.
                Vec3 OnPost(float h)
                {
                    float across = radial.Z * h, t = (sign * (float)Math.Sqrt(Math.Max(0f, postR * postR - across * across)) - radial.X * h) / d.X;
                    return At(Math.Max(0f, t) - 0.004f) + radial * h;
                }

                // The stub's rib: from the post's face to the flange, from the stub to the flange's rim.
                float lo = tubeR - 0.003f, hi = radial.Z != 0f ? Math.Min(flangeR - 0.005f, postR - 0.003f) : flangeR - 0.005f;
                Solids.Slab(metal, new[] { OnPost(lo), At(inner) + radial * lo, At(inner) + radial * hi, OnPost(hi) }, thick);
                // The tube's rib beyond the flanges: as high as the flange at it, 30 mm at its end.
                Solids.Slab(metal, new[] { At(outer) + radial * lo, At(outer + ribLength) + radial * lo, At(outer + ribLength) + radial * (tubeR + 0.03f), At(outer) + radial * (flangeR - 0.005f) }, thick);
            }

            // The saddle plates of the larger joints (p.80, 横梁加劲肋 ⑩: 560 × 121 × 8 for φ273, cut to the post), over and under the stub's ribs: as wide as twice the post, from 0.4 of its radius
            // (where their straight inner edge leaves the post) to 4 cm beyond its face. Level joints only (the cranked arm's is on φ219, which has none).
            if (flangeR > 0.16f && postR > 0.12f && Math.Abs(d.Y) < 1e-3f)
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    float x0 = root.X + sign * 0.4f * postR, x1 = root.X + sign * (postR + 0.04f);
                    Solids.Box(metal, new Vec3((x0 + x1) * 0.5f, root.Y + s * (flangeR + 0.004f), root.Z), new Vec3(Math.Abs(x1 - x0) * 0.5f, 0.004f, 1.025f * postR));
                }
            }

            float ring = flangeR - 0.025f;
            for (int i = 0; i < bolts; i++)
            {
                float a = (float)(Math.PI * (2 * i + 1) / bolts);
                Vec3 off = up * (ring * (float)Math.Cos(a)) + side * (ring * (float)Math.Sin(a));
                Solids.Tube(metal, At(inner - 0.02f) + off, At(outer + 0.02f) + off, 1.6f * boltR, 1.6f * boltR, 6, true, true);
            }

            return At(outer);
        }

        /// <summary>The bolts of a cantilever's arm joint (22MR601 p.78: 4-φ22 on the φ300 flange; p.80: 8 on φ350), M20, and its arm ribs' length (p.78: 137; p.80: 200).</summary>
        private static void ArmJointOf(float flangeR, out int bolts, out float ribLength)
        {
            bool small = flangeR <= 0.15f;
            bolts = small ? 4 : 8;
            ribLength = small ? 0.137f : 0.2f;
        }

        private const float JointBolt = 0.01f, JointRib = 0.008f;

        // ------------------------------------------------------------------ cantilever

        /// <summary>
        /// 悬臂 (SupportLayouts.Of): the post straight on its base and capped; each arm (one to three, two sides on a double cantilever) on a flanged stub, straight to its closed end; the boards on the
        /// arms that pass behind them; the small ones on the post below the lowest arm's joint. 八角锥杆 (0.12.57, PoleSides 8): the post and the arms octagons tapering to their tops and tips, the
        /// arms on square or round plates (OctaJoint). 上海型综合杆 (0.12.57, Integrated): the main pole of twelve sides from under the paving (its flange buried), the same arms, the door with the
        /// foot and the fittings above (IntegratedFittings).
        /// </summary>
        private static void BuildCantilever(SupportLayout layout, SignParts parts, List<Placement> placed, bool twin)
        {
            float r = layout.PoleRadius, armR = layout.ArmRootRadius;
            bool faceted = layout.PoleSides > 0, integrated = layout.Integrated;
            float flat = faceted ? (float)Math.Cos(Math.PI / layout.PoleSides) : 1f;
            BasePlate b = layout.Base;
            var foot = new PostFoot { X = 0f, Z = 0f, PadHalf = integrated ? r : Math.Max(b.HalfX, b.HalfZ) + SupportLayouts.PedestalMargin, PadBottom = PedestalBottom, MaxRaise = RaiseOf(layout), MetalFrom = parts.Metal.VertexCount, ConcreteFrom = parts.Concrete.VertexCount };
            float plateTop;
            if (integrated)
            {
                plateTop = 0f;
                IntegratedDoor(parts, layout, foot);
            }
            else
            {
                plateTop = AddBase(parts, b, 0f, 0f, faceted ? layout.PoleRadiusAt(0.05f) * flat : r);
            }

            foot.MetalTo = parts.Metal.VertexCount;
            foot.ConcreteTo = parts.Concrete.VertexCount;
            float padX = integrated ? r : b.HalfX + SupportLayouts.PedestalMargin, padZ = integrated ? r : b.HalfZ + SupportLayouts.PedestalMargin;
            parts.Picks.Add(new PickShape { Name = "foot", Min = new Vec3(-padX, PedestalBottom, -padZ), Max = new Vec3(padX, plateTop + 0.04f, padZ) });
            parts.Picks.Add(PostPick(0f, 0f, r, plateTop, layout.PoleTop + 0.005f));
            AddPostBoardPick(parts, layout, r, plateTop);
            // Thinner than the arm by 2 cm in z, so that with the pick tolerance its front stays behind the middle of the boards' plane.
            float armHalf = Math.Max(0.03f, armR - 0.02f);
            float[] sides = twin ? new[] { 1f, -1f } : new[] { layout.ArmSide };
            foreach (float armY in layout.ArmYs)
            {
                foreach (float side in sides)
                    parts.Picks.Add(new PickShape { Name = "arm", Min = new Vec3(Math.Min(0f, side * layout.ArmLength), armY - layout.CrankRise - armR, -armHalf), Max = new Vec3(Math.Max(0f, side * layout.ArmLength), armY + armR, armHalf) });
            }

            foot.TubeFrom = parts.Metal.VertexCount;
            foot.Split = (plateTop + layout.PoleTop) * 0.5f;
            float topR = layout.PoleRadiusAt(layout.PoleTop), tubeFrom = integrated ? PedestalBottom : plateTop;
            if (faceted)
                Solids.Faceted(parts.Metal, new Vec3(0f, tubeFrom, 0f), new Vec3(0f, layout.PoleTop, 0f), layout.PoleRadiusAt(tubeFrom), topR, layout.PoleSides, false, false);
            else
                Solids.Tube(parts.Metal, new Vec3(0f, plateTop, 0f), new Vec3(0f, layout.PoleTop, 0f), r, r, 16, false, false);
            foot.TubeTo = parts.Metal.VertexCount;
            PaintRoot(parts, foot, r, topR, plateTop, layout.PoleTop, faceted ? layout.PoleSides : 16, faceted, twin ? RoadSide.Both : layout.ArmSide > 0f ? RoadSide.Left : RoadSide.Right);
            parts.Feet.Add(foot);
            // 八角锥杆's cap: an octagonal plate with a rim as the round post's (Hangzhou's 3 mm plate, 22MR601's 5 mm one with a 30 mm rim). The Shanghai pole goes on above (its collar).
            if (integrated)
                IntegratedFittings(parts, layout, layout.ArmSide);
            else if (faceted)
                Solids.Faceted(parts.Metal, new Vec3(0f, layout.PoleTop - 0.025f, 0f), new Vec3(0f, layout.PoleTop + 0.005f, 0f), topR + 0.0055f / flat, topR + 0.0055f / flat, layout.PoleSides, true, true);
            else
                PostCap(parts.Metal, 0f, 0f, layout.PoleTop, r);

            if (layout.Framed)
            {
                FramedArms(parts, layout, placed);
                PostBoardsOfHung(layout, parts, placed);
                return;
            }

            float flangeR = SupportLayouts.JointFlangeOf(layout);
            ArmJointOf(flangeR, out int bolts, out float ribLength);
            foreach (float armY in layout.ArmYs)
            {
                foreach (float side in sides)
                {
                    if (layout.OctaArms)
                    {
                        float from = OctaJoint(parts.Metal, layout, armY, side);
                        Solids.Faceted(parts.Metal, new Vec3(from, armY, 0f), new Vec3(side * layout.ArmLength, armY, 0f), layout.ArmRadiusAt(from), layout.ArmTipRadius, 8, false, true);
                        continue;
                    }

                    if (layout.CrankRise > 0f)
                    {
                        // The cranked arm (22MR601 p.78): from its joint on the post rising CrankAngle to the bend, then level; the two straight lengths overlap a little at the bend.
                        var d = new Vec3(side * (float)Math.Cos(SupportLayouts.CrankAngle), (float)Math.Sin(SupportLayouts.CrankAngle), 0f);
                        Vec3 from = FlangeJoint(parts.Metal, new Vec3(0f, armY - layout.CrankRise, 0f), d, r, armR, flangeR, bolts, JointBolt, ribLength, JointRib);
                        var bend = new Vec3(side * layout.CrankRun, armY, 0f);
                        float over = armR * (float)Math.Tan(SupportLayouts.CrankAngle * 0.5f);
                        Solids.Tube(parts.Metal, from, bend + d * over, armR, armR, 12, false, false);
                        Solids.Tube(parts.Metal, bend - new Vec3(side * over, 0f, 0f), new Vec3(side * layout.ArmLength, armY, 0f), armR, armR, 12, false, true);
                        continue;
                    }

                    float level = FlangeJoint(parts.Metal, 0f, armY, 0f, side, r, armR, flangeR, bolts, JointBolt, ribLength, JointRib);
                    Solids.Tube(parts.Metal, new Vec3(level, armY, 0f), new Vec3(side * layout.ArmLength, armY, 0f), armR, armR, 12, false, true);
                }
            }

            foreach (Placement p in placed)
            {
                if (!p.OnPost)
                    HangOnTubes(layout, parts, p, layout.ArmYs, x => layout.ArmRadiusAt(x));
            }

            PostBoardsOfHung(layout, parts, placed);
        }

        /// <summary>
        /// 八角锥杆's arm joint at height y on the side given (DG/TJ 08-2362-2021 fig. A.4.3; SupportLayouts.OctaFlangeHalf), the Shanghai pole's as well: a stub of the arm's octagon from inside the
        /// post to a pair of plates SupportLayouts.JointStub out from the post's flat, JointPlate thick each (22MR601's; the Shanghai drawings give no thickness): a single arm's square, 6 bolts at its corners and the middles of
        /// its sides fore and aft (法兰1: M24 on a 350 square; 法兰2: M30 on 380); parallel arms' round, 8 bolts on Ø290 (法兰3: M16; 法兰4, 8 m: M20). No ribs (none drawn). Returns where the arm goes
        /// on from (X), at the outer plate's face.
        /// </summary>
        private static float OctaJoint(MeshBuilder metal, SupportLayout layout, float y, float side)
        {
            int arms = layout.ArmYs.Length;
            float half = SupportLayouts.OctaFlangeHalf(layout.ArmLength, arms);
            float leave = layout.PoleRadiusAt(y) * (float)Math.Cos(Math.PI / layout.PoleSides), middle = leave + SupportLayouts.JointStub;
            float inner = middle - SupportLayouts.JointPlate, outer = middle + SupportLayouts.JointPlate;
            Solids.Faceted(metal, new Vec3(side * leave * 0.5f, y, 0f), new Vec3(side * inner, y, 0f), layout.ArmRootRadius, layout.ArmRootRadius, 8, false, false);
            if (arms == 1)
                Solids.Box(metal, new Vec3(side * middle, y, 0f), new Vec3(SupportLayouts.JointPlate, half, half));
            else
                Solids.Tube(metal, new Vec3(side * inner, y, 0f), new Vec3(side * outer, y, 0f), half, half, 24, true, true);
            if (t_Reduced)
                return side * outer;
            var bolts = new List<(float Y, float Z)>();
            float boltR;
            if (arms == 1)
            {
                bool large = layout.ArmLength > 10f;
                float e = large ? 0.19f : 0.175f;
                boltR = large ? 0.015f : 0.012f;
                foreach (float s in new[] { -1f, 1f })
                {
                    bolts.Add((s * e, -e));
                    bolts.Add((s * e, e));
                    bolts.Add((0f, s * e));
                }
            }
            else
            {
                boltR = layout.ArmLength > 6f ? 0.010f : 0.008f;
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * (2 * i + 1) / 8;
                    bolts.Add((0.145f * (float)Math.Cos(a), 0.145f * (float)Math.Sin(a)));
                }
            }

            foreach ((float by, float bz) in bolts)
                Solids.Tube(metal, new Vec3(side * (inner - 0.02f), y + by, bz), new Vec3(side * (outer + 0.02f), y + by, bz), 1.6f * boltR, 1.6f * boltR, 6, true, true);
            return side * outer;
        }

        // ------------------------------------------------------------------ gantry

        /// <summary>The half thickness of an H leg's web and the thickness of its flanges (HW200 × 200 × 8 × 12).</summary>
        private const float HWeb = 0.004f, HFlange = 0.012f;

        /// <summary>The radius of the web members of a gantry's truss (22MR601 pp.94, 102: φ60) and of the lacing between a space truss's legs (φ60 × 5).</summary>
        private const float WebRadius = 0.03f;

        /// <summary>门架 (SupportLayouts.Of): a planar truss on round posts, or a space truss on twin H legs.</summary>
        private static void BuildGantry(SupportLayout layout, SignParts parts, List<Placement> placed)
        {
            if (layout.HeightLimit)
                BuildLimitFrame(layout, parts);
            else if (layout.BeamWidth > 0f)
                BuildSpaceGantry(layout, parts);
            else
                BuildPlanarGantry(layout, parts);

            // The truss is as thick (front to back) as its front chords are, a little less: with the pick tolerance (6 cm) its front stays behind the middle of the boards' plane.
            float chordR = layout.ChordRadius, pickHalf = Math.Max(0.03f, chordR - 0.02f);
            float[] ys = layout.ChordYs;
            parts.Picks.Add(new PickShape { Name = "arm", Min = new Vec3(0f, ys[0] - chordR, -layout.BeamWidth - pickHalf), Max = new Vec3(layout.ArmLength, ys[ys.Length - 1] + chordR, pickHalf) });

            // Each board in front of the truss on channels clamped to its front chords (a board on the back, its channels and clamps on the back chords: SignBuilder turns it); the ones on the first
            // post are stacked there.
            foreach (Placement p in placed)
            {
                if (p.OnPost)
                    continue;
                HangOnTubes(layout, parts, p, ys, x => chordR);
            }

            PostBoardsOfHung(layout, parts, placed);
        }

        /// <summary>
        /// The 10 and 14 m gantry (22MR601 pp.94-101): straight φ377 posts on their bases and capped; three chords φ140 bolted to stubs on the posts; verticals φ60 at nodes 0.7 m in from the posts' axes
        /// and every bay (an even number of about 0.95 m: p.94's 860 for 10 m, 1050 for 14 m), and in each panel a diagonal, those of a bay meeting on the middle chord at every other node.
        /// </summary>
        private static void BuildPlanarGantry(SupportLayout layout, SignParts parts)
        {
            float span = layout.ArmLength, r = layout.PoleRadius, chordR = layout.ChordRadius;
            foreach (float px in layout.PoleX)
            {
                float plateTop = GantryFoot(layout, parts, px, 0f, layout.Base.HalfX + SupportLayouts.PedestalMargin, layout.Base.HalfZ + SupportLayouts.PedestalMargin, out PostFoot foot);
                foot.TubeFrom = parts.Metal.VertexCount;
                Solids.Tube(parts.Metal, new Vec3(px, plateTop, 0f), new Vec3(px, layout.PoleTop, 0f), r, r, 16, false, false);
                foot.TubeTo = parts.Metal.VertexCount;
                PaintRoot(parts, foot, r, r, plateTop, layout.PoleTop, 16, false, px <= 0f ? RoadSide.Left : RoadSide.Right);
                PostCap(parts.Metal, px, 0f, layout.PoleTop, r);
                parts.Picks.Add(PostPick(px, 0f, r, plateTop, layout.PoleTop + 0.005f));
                if (px == 0f)
                    AddPostBoardPick(parts, layout, r, plateTop);
            }

            float flangeR = SupportLayouts.JointFlangeOf(layout);
            foreach (float y in layout.ChordYs)
            {
                float a = FlangeJoint(parts.Metal, 0f, y, 0f, 1f, r, chordR, flangeR, 8, JointBolt, 0.15f, 0.012f);
                float c = FlangeJoint(parts.Metal, span, y, 0f, -1f, r, chordR, flangeR, 8, JointBolt, 0.15f, 0.012f);
                Solids.Tube(parts.Metal, new Vec3(a, y, 0f), new Vec3(c, y, 0f), chordR, chordR, 10, false, false);
            }

            Nodes(0.7f, span - 0.7f, 0.95f, out float n0, out float dx, out int bays);
            float yLow = layout.ChordYs[0], yMid = layout.ChordYs[1], yTop = layout.ChordYs[2];
            for (int i = 0; i <= (t_Reduced ? -1 : bays); i++)
            {
                float x = n0 + dx * i;
                Solids.Tube(parts.Metal, new Vec3(x, yLow, 0f), new Vec3(x, yTop, 0f), WebRadius, WebRadius, 6, false, false);
            }

            // In reduced detail the diagonals of every other bay: the truss still reads as one.
            for (int i = 0; i < bays; i += t_Reduced ? 2 : 1)
            {
                float xa = n0 + dx * i, xb = xa + dx;
                bool even = i % 2 == 0;
                Web(parts.Metal, new Vec3(xa, even ? yMid : yTop, 0f), new Vec3(xb, even ? yTop : yMid, 0f));
                Web(parts.Metal, new Vec3(xa, even ? yMid : yLow, 0f), new Vec3(xb, even ? yLow : yMid, 0f));
            }
        }

        /// <summary>
        /// The 18 and 26 m gantry (22MR601 pp.102-107): at each end two HW200 legs, one under the front chords and one under the back, on their bases over one pedestal and laced together (φ60 in
        /// 0.8 m panels, 0.5 m from either end: p.102's side view), capped; four chords bolted to plates on the legs and spliced at mid-span; verticals and diagonals on the front and the back face
        /// (the diagonals falling toward mid-span), a zigzag in the top and the bottom plane, and a diagonal across the truss at its ends and its middle (p.102's D-D).
        /// </summary>
        private static void BuildSpaceGantry(SupportLayout layout, SignParts parts)
        {
            float span = layout.ArmLength, w = layout.BeamWidth, chordR = layout.ChordRadius, leg = layout.RoundLegs ? layout.PoleRadius : SupportLayouts.GantryLegHalf;
            float yLow = layout.ChordYs[0], yTop = layout.ChordYs[1];
            float[] planes = { 0f, -w };
            foreach (float px in layout.PoleX)
            {
                float padX = layout.Base.HalfX + SupportLayouts.PedestalMargin, padZ = (w + 2f * layout.Base.HalfZ) * 0.5f + SupportLayouts.PedestalMargin;
                float plateTop = GantryFoot(layout, parts, px, -w * 0.5f, padX, padZ, out PostFoot foot, planes);
                foot.TubeFrom = parts.Metal.VertexCount;
                float mid = (plateTop + layout.PoleTop) * 0.5f, half = (layout.PoleTop - plateTop) * 0.5f;
                foreach (float z in planes)
                {
                    if (layout.RoundLegs)
                    {
                        Solids.Tube(parts.Metal, new Vec3(px, plateTop, z), new Vec3(px, layout.PoleTop, z), leg, leg, 16, false, false);
                        continue;
                    }

                    Solids.Box(parts.Metal, new Vec3(px, mid, z - leg + HFlange * 0.5f), new Vec3(leg, half, HFlange * 0.5f));
                    Solids.Box(parts.Metal, new Vec3(px, mid, z + leg - HFlange * 0.5f), new Vec3(leg, half, HFlange * 0.5f));
                    Solids.Box(parts.Metal, new Vec3(px, mid, z), new Vec3(HWeb, half, leg - HFlange));
                }

                foot.TubeTo = parts.Metal.VertexCount;
                foreach (float z in planes)
                {
                    if (layout.RoundLegs)
                        PostCap(parts.Metal, px, z, layout.PoleTop, leg);
                    else
                        Solids.Box(parts.Metal, new Vec3(px, layout.PoleTop + 0.005f, z), new Vec3(leg + 0.005f, 0.005f, leg + 0.005f));
                }

                // The paint on the round legs' roots: each leg's sleeve is round its own axis (the foot's z is the pedestal's middle).
                if (layout.RoundLegs)
                {
                    float footZ = foot.Z;
                    foreach (float z in planes)
                    {
                        foot.Z = z;
                        PaintRoot(parts, foot, leg, leg, plateTop, layout.PoleTop, 16, false, px <= 0f ? RoadSide.Left : RoadSide.Right);
                    }

                    foot.Z = footZ;
                }

                // The lacing between the legs, from the back flange of the front leg to the front flange of the back one.
                float zf = -leg, zb = -w + leg, y0 = plateTop + 0.5f, y1 = layout.PoleTop - 0.5f;
                int panels = Math.Max(1, (int)Math.Round((y1 - y0) / 0.8f));
                float dy = (y1 - y0) / panels;
                Web(parts.Metal, new Vec3(px, y0, zf), new Vec3(px, y0, zb));
                Web(parts.Metal, new Vec3(px, y1, zf), new Vec3(px, y1, zb));
                for (int i = 0; i < panels; i += t_Reduced ? 2 : 1)
                    Web(parts.Metal, new Vec3(px, y0 + dy * i, i % 2 == 0 ? zf : zb), new Vec3(px, y0 + dy * (i + 1), i % 2 == 0 ? zb : zf));
                PickShape legs = PostPick(px, 0f, leg, plateTop, layout.PoleTop + 0.01f);
                legs.Min.Z -= w;
                parts.Picks.Add(legs);
                if (px == 0f)
                    AddPostBoardPick(parts, layout, leg, plateTop);
            }

            float flangeR = SupportLayouts.JointFlangeOf(layout);
            foreach (float z in planes)
            {
                foreach (float y in layout.ChordYs)
                {
                    float a = FlangeJoint(parts.Metal, 0f, y, z, 1f, leg, chordR, flangeR, 8, JointBolt, 0.12f, 0.012f);
                    float c = FlangeJoint(parts.Metal, span, y, z, -1f, leg, chordR, flangeR, 8, JointBolt, 0.12f, 0.012f);
                    Solids.Tube(parts.Metal, new Vec3(a, y, z), new Vec3(c, y, z), chordR, chordR, 10, false, false);
                    // The splice at mid-span: a pair of flanges φ300 × 20 (p.102, 横梁与横梁连接). Not on the round-legged gantry: its flange stands through a board hung over the middle.
                    if (!layout.RoundLegs)
                        Solids.Tube(parts.Metal, new Vec3(span * 0.5f - SupportLayouts.JointPlate, y, z), new Vec3(span * 0.5f + SupportLayouts.JointPlate, y, z), 0.15f, 0.15f, 16, true, true);
                }
            }

            Nodes(0.6f, span - 0.6f, 1.0f, out float n0, out float dx, out int bays);
            for (int i = 0; i <= bays; i++)
            {
                float x = n0 + dx * i;
                bool frame = i == 0 || i == bays || i == bays / 2;
                foreach (float z in planes)
                {
                    if (!t_Reduced || frame)
                        Web(parts.Metal, new Vec3(x, yLow, z), new Vec3(x, yTop, z));
                }

                if (frame)
                {
                    Web(parts.Metal, new Vec3(x, yLow, 0f), new Vec3(x, yLow, -w));
                    Web(parts.Metal, new Vec3(x, yTop, 0f), new Vec3(x, yTop, -w));
                    Web(parts.Metal, new Vec3(x, yLow, 0f), new Vec3(x, yTop, -w));
                }
            }

            for (int i = 0; i < bays; i += t_Reduced ? 2 : 1)
            {
                float xa = n0 + dx * i, xb = xa + dx;
                bool left = xb <= span * 0.5f + 1e-3f;
                foreach (float z in planes)
                    Web(parts.Metal, new Vec3(xa, left ? yTop : yLow, z), new Vec3(xb, left ? yLow : yTop, z));
                if (t_Reduced)
                    continue;
                bool even = i % 2 == 0;
                Web(parts.Metal, new Vec3(xa, yTop, even ? 0f : -w), new Vec3(xb, yTop, even ? -w : 0f));
                Web(parts.Metal, new Vec3(xa, yLow, even ? 0f : -w), new Vec3(xb, yLow, even ? -w : 0f));
            }
        }

        /// <summary>The nodes of a truss from n0 to n1: an even number of bays of about bay metres.</summary>
        private static void Nodes(float n0, float n1, float bay, out float first, out float step, out int bays)
        {
            bays = Math.Max(2, (int)Math.Round((n1 - n0) / bay));
            if (bays % 2 == 1)
                bays++;
            first = n0;
            step = (n1 - n0) / bays;
        }

        private static void Web(MeshBuilder metal, Vec3 a, Vec3 b) => Solids.Tube(metal, a, b, WebRadius, WebRadius, 6, false, false);

        /// <summary>A gantry post's foot at (px, z): one pedestal (padX by padZ half) under the base of each leg at the planes' z (one round post: z itself); its foot and pick. Returns the plates' top.</summary>
        private static float GantryFoot(SupportLayout layout, SignParts parts, float px, float z, float padX, float padZ, out PostFoot foot, float[] planes = null)
        {
            foot = new PostFoot { X = px, Z = z, PadHalf = Math.Max(padX, padZ), PadBottom = PedestalBottom, MaxRaise = RaiseOf(layout), MetalFrom = parts.Metal.VertexCount, ConcreteFrom = parts.Concrete.VertexCount };
            AddPedestal(parts, px, z, padX, padZ);
            float top = 0f;
            foreach (float pz in planes ?? new[] { z })
                top = AddBase(parts, layout.Base, px, pz, layout.PoleRadius, false);
            foot.MetalTo = parts.Metal.VertexCount;
            foot.ConcreteTo = parts.Concrete.VertexCount;
            foot.Split = (top + layout.PoleTop) * 0.5f;
            parts.Feet.Add(foot);
            parts.Picks.Add(new PickShape { Name = "foot", Min = new Vec3(px - padX, PedestalBottom, z - padZ), Max = new Vec3(px + padX, top + 0.04f, z + padZ) });
            return top;
        }

        // ------------------------------------------------------------------ the boards on an arm or a beam

        /// <summary>The most a board's vertical channels on an arm or a beam are apart (m): the board grid of S8 (400 vertical, 500 horizontal) and S6's 350 (REPORT.md 2.7).</summary>
        private const float ChannelPitch = 0.4f;

        /// <summary>
        /// A board on an arm or a beam (22MR601 p.141, 交通标志板构造示意图: the board's back on a grid of aluminium slide channels 68 × 19, riveted on; a clamp 抱箍 with its backing at every channel
        /// and tube that cross, M12 square-head bolts in the channel): vertical channels at most ChannelPitch apart across the board where its outline has room (the narrowest place a tube crosses),
        /// horizontal ones at its top, middle and bottom (two on a board under a metre), and a clamp, a band round the tube and the backing to the channel, where each vertical channel crosses a tube
        /// behind the board. A channel that no tube crosses reaches up to the nearest one above the board. Reduced detail: two or three channels and their clamps, no horizontal ones. Returns the X of the vertical channels.
        /// </summary>
        private static float[] HangOnTubes(SupportLayout layout, SignParts parts, Placement p, float[] tubeYs, Func<float, float> tubeRadiusAt)
        {
            float width = p.Def.Width;
            var crossing = new List<float>();
            float narrowest = width;
            foreach (float y in tubeYs)
            {
                if (y < p.Bottom - 0.02f || y > p.Top + 0.02f)
                    continue;
                crossing.Add(y);
                narrowest = Math.Min(narrowest, SignCatalog.ChordAt(p.Def, Math.Min(p.Top, Math.Max(p.Bottom, y)) - p.Cy));
            }

            // The tubes the channels are clamped to: those that cross the board, else the nearest above it (or the highest when all are below).
            var used = new List<float>(crossing);
            if (used.Count == 0)
            {
                float best = float.NaN;
                foreach (float y in tubeYs)
                {
                    if (y >= p.Top - 0.02f && (float.IsNaN(best) || y < best))
                        best = y;
                }

                if (float.IsNaN(best))
                {
                    foreach (float y in tubeYs)
                        best = float.IsNaN(best) || y > best ? y : best;
                }

                used.Add(best);
            }

            float lowT = float.MaxValue, highT = float.MinValue;
            foreach (float y in used)
            {
                lowT = Math.Min(lowT, y);
                highT = Math.Max(highT, y);
            }

            // The outer channels' middles: 6 cm in from the outline where the tubes cross, within the board; a board too narrow for two channels side by side has one at its middle.
            float half = Math.Min(width * 0.5f - SupportLayouts.RailWidth * 0.5f, Math.Max(0f, (narrowest - 0.12f) * 0.5f));
            int channels = half < SupportLayouts.RailWidth ? 1 : t_Reduced ? (width > 3f ? 3 : 2) : Math.Max(2, (int)Math.Ceiling(2f * half / ChannelPitch - 1e-3f) + 1);
            float depth = SupportLayouts.RailDepth, channelZ = layout.BackZ - depth * 0.5f;
            // A board that leans (0.12.55) turns about its lowest tube at its channels' back: higher up its channels are further from the tubes.
            TurnedBoard turned = TurnedOf(parts, p);
            float lean = 0f;
            if (turned != null && turned.Tilt > 0f)
            {
                turned.TiltY = lowT;
                turned.TiltZ = layout.BackZ - depth;
                lean = (float)Math.Tan(turned.Tilt);
            }

            int channelsFrom = parts.Metal.VertexCount;
            if (!t_Reduced)
            {
                // The horizontal channels a little shallower than the vertical ones, so that where they cross the backs are not in one plane.
                foreach (float k in p.Def.Height < 1f ? new[] { -0.3f, 0.3f } : new[] { -0.38f, 0f, 0.38f })
                {
                    float y = p.Cy + k * p.Def.Height;
                    float length = SignCatalog.ChordAt(p.Def, y - p.Cy) - 0.12f;
                    if (length > 0.2f)
                        Solids.Box(parts.Metal, new Vec3(p.Cx, y, layout.BackZ - depth * 0.35f), new Vec3(length * 0.5f, SupportLayouts.RailWidth * 0.5f, depth * 0.35f));
                }
            }

            var xs = new float[channels];
            for (int i = 0; i < channels; i++)
            {
                float x = xs[i] = channels == 1 ? p.Cx : p.Cx - half + 2f * half * i / (channels - 1);
                // The channel runs between the clamps, and lower on the board where the outline allows; where a clamp is level with the board's edge it stops at the edge.
                float bottom = Math.Min(ChannelBottom(p, x - p.Cx), Math.Max(lowT - 0.045f, p.Bottom + 0.01f));
                float top = highT > p.Top ? highT + 0.045f : Math.Min(highT + 0.045f, p.Top - 0.01f);
                if (lowT > p.Top)
                    bottom = Math.Min(bottom, p.Top - 0.02f);
                // A board held from below, over its tube (a board moved up on a signal's arm, 0.12.57): the channel runs down to the clamp.
                if (highT < p.Bottom)
                {
                    bottom = Math.Min(bottom, lowT - 0.045f);
                    top = Math.Max(top, p.Bottom + 0.02f);
                }
                if (top > bottom + 0.02f)
                    Solids.Box(parts.Metal, new Vec3(x, (top + bottom) * 0.5f, channelZ), new Vec3(SupportLayouts.RailWidth * 0.5f, (top - bottom) * 0.5f, depth * 0.5f));
            }

            turned?.Since(parts.Metal, channelsFrom);

            // The clamps: a band 50 × 5 round the tube (p.141: 抱箍 50 × 5) and the backing from it to the channel, which a lean takes further out at the higher tubes.
            int clampsFrom = parts.Metal.VertexCount;
            foreach (float x in xs)
            {
                foreach (float y in used)
                {
                    float r = tubeRadiusAt(x);
                    if (!t_Reduced)
                        Solids.Tube(parts.Metal, new Vec3(x - 0.025f, y, 0f), new Vec3(x + 0.025f, y, 0f), r + 0.005f, r + 0.005f, 10, false, false);
                    float plateFront = layout.BackZ - depth + (y - lowT) * lean, plateBack = r * 0.5f;
                    Solids.Box(parts.Metal, new Vec3(x, y, (plateFront + plateBack) * 0.5f), new Vec3(0.02f, 0.03f, (plateFront - plateBack) * 0.5f + 0.002f));
                }
            }

            turned?.SinceUpright(parts.Metal, clampsFrom);
            return xs;
        }

        /// <summary>The lowest point of a channel dx from the board's middle: where the board's outline still has room for it (a triangle narrows downward), and at least a tenth of the height above the lower edge.</summary>
        private static float ChannelBottom(Placement p, float dx)
        {
            float need = Math.Abs(dx) * 2f + 0.10f;
            float bottom = p.Cy - p.Def.Height * 0.5f + p.Def.Height * 0.10f;
            const int steps = 40;
            float last = p.Top - p.Def.Height * 0.02f;
            for (int i = 1; i <= steps; i++)
            {
                float y = p.Top - p.Def.Height * i / steps;
                if (SignCatalog.ChordAt(p.Def, y - p.Cy) < need)
                    break;
                last = y;
            }

            return Math.Max(bottom, Math.Min(last, p.Top - p.Def.Height * 0.10f));
        }
    }
}
