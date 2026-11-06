using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The posts stood on the ground under each of them (0.12.5; the owner: "立柱自动接地"): every support on posts knows its feet, a foot moves to the ground of its profile (down a slope, up onto a footway) and
    /// its post grows or shrinks, the boards and the beam stay; the limits (no deeper than 5 m, no higher than 1.5 m or half a metre under the boards); what is the ground (a surface, else the terrain, not
    /// beside a bridge); the surfaces a post stands on.
    /// </summary>
    internal static class PostGroundTests
    {
        public static void RunAll()
        {
            Program.Run("post ground: every support on posts knows its feet", Feet);
            Program.Run("post ground: a foot goes down a slope and up a footway, the post grows to it", Moves);
            Program.Run("post ground: the limits, and a foot whose ground is not known", Limits);
            Program.Run("post ground: offsets that are the same to the eye need no new preview", SameOffsets);
            Program.Run("post ground: what the ground under a foot is", GroundRule);
            Program.Run("post ground: the profile, and the surfaces a post stands on", ProfileAndSurfaces);
        }

        private static SignDef Make(string id, float w, float h) => new SignDef
        {
            Id = id, Code = id, Zh = id, En = id, Group = "guide", Shape = ShapeKind.RoundRect, CornerRadius = 0.05f, Width = w, Height = h, BottomClearance = SupportCatalog.RoadsideClearance,
            TextureWidth = (int)(w * 200f), TextureHeight = (int)(h * 200f), Keywords = new string[0]
        };

        private static readonly SignDef Plate = Make("pg.plate", 1.2f, 0.8f), Wide = Make("pg.wide", 4.0f, 2.0f);

        private static SignInstance Inst(SignDef def) => new SignInstance { Def = def, Key = def.Id, Spec = "", TexW = def.TextureWidth, TexH = def.TextureHeight };

        private static SignParts Parts(SignDef def, string support, float reach = 0f) => SignBuilder.Build(Inst(def), SupportCatalog.ById(support), reach);

        /// <summary>A profile whose ground is the given function of local X (the same in the three rows unless across is given).</summary>
        private static GroundProfile Profile(Func<float, float> ground, Func<float, float, float> across = null) =>
            GroundProfile.Sample((x, z) => across != null ? across(x, z) : ground(x), -12f, 30f);

        private static MeshBuilder Grounded(MeshBuilder source, int[] tags, float[] top, float[] bottom)
        {
            var mesh = new MeshBuilder();
            PostGround.Append(mesh, source, tags, top, bottom, Vec3.Zero, 0f);
            return mesh;
        }

        private static void Feet()
        {
            SignParts single = Parts(Plate, SupportCatalog.SinglePole), twin = Parts(Wide, SupportCatalog.DoublePole), triple = Parts(Wide, SupportCatalog.TriplePole);
            SignParts arm = Parts(Wide, SupportCatalog.Cantilever, 6f), twinArm = Parts(Wide, SupportCatalog.DoubleCantilever, 5f), gantry = Parts(Wide, SupportCatalog.Gantry, 18f);
            Program.Check(single.Feet.Count == 1 && twin.Feet.Count == 2 && triple.Feet.Count == 3, $"a foot under each post: {single.Feet.Count}, {twin.Feet.Count}, {triple.Feet.Count}");
            Program.Check(arm.Feet.Count == 1 && twinArm.Feet.Count == 1 && gantry.Feet.Count == 2, $"a cantilever's one, a gantry's two: {arm.Feet.Count}, {twinArm.Feet.Count}, {gantry.Feet.Count}");
            Program.Near(gantry.Feet[1].X - gantry.Feet[0].X, 18f, 0.5f, "a gantry's feet a span apart");
            Program.Check(Parts(Plate, SupportCatalog.WallFlat, 2.5f).Feet.Count == 0 && Parts(Plate, SupportCatalog.WallBracket, 2.5f).Feet.Count == 0 && Parts(Plate, SupportCatalog.AttachedPole).Feet.Count == 0,
                "a sign on a wall or on a host has none");
            foreach (SignParts parts in new[] { single, twin, arm, gantry })
            {
                parts.FootTags(out int[] metal, out int[] concrete, out _);
                int footMetal = 0, tubeLow = 0, footConcrete = 0, padBottom = 0;
                for (int i = 0; i < metal.Length; i++)
                {
                    if (metal[i] >= 0)
                    {
                        footMetal++;
                        Program.Check(parts.Metal.Positions[i].Y < parts.Layout.BottomClearance, "nothing of a board or an arm is a foot");
                    }
                }

                for (int i = 0; i < concrete.Length; i++)
                {
                    if (concrete[i] >= 0)
                        footConcrete++;
                    if (concrete[i] >= 0 && (concrete[i] & 1) == 1)
                        padBottom++;
                }

                foreach (PostFoot foot in parts.Feet)
                {
                    for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
                    {
                        if (metal[i] >= 0)
                            tubeLow++;
                    }
                }

                Program.Check(footMetal > 0 && tubeLow > 0 && footConcrete == parts.Concrete.VertexCount && padBottom > 0 && padBottom < footConcrete, $"the feet's metal, the tubes' lower ends, the pads and their lower faces are tagged ({footMetal}, {tubeLow}, {footConcrete}, {padBottom})");
            }
        }

        private static void Moves()
        {
            // Two posts of a wide board, the ground falling 0.3 m a metre across the road: each foot goes down to its own ground.
            SignParts twin = Parts(Wide, SupportCatalog.DoublePole);
            GroundProfile slope = Profile(x => -0.3f * x - 0.2f);
            PostGround.Offsets(twin, slope, out float[] top, out float[] bottom);
            for (int k = 0; k < twin.Feet.Count; k++)
                Program.Near(top[k], -0.3f * twin.Feet[k].X - 0.2f, 1e-3f, $"foot {k} on its ground");
            Program.Check(top[0] != top[1], "the two feet go to different heights: " + top[0] + " " + top[1]);
            Program.Check(bottom[0] <= top[0] - 0.29f * twin.Feet[0].PadHalf && bottom[1] <= top[1] - 0.29f * twin.Feet[1].PadHalf, "the pad's lower face goes to the lowest ground under the pad");
            twin.FootTags(out int[] metalTags, out int[] concreteTags, out _);
            MeshBuilder metal = Grounded(twin.Metal, metalTags, top, bottom), concrete = Grounded(twin.Concrete, concreteTags, top, bottom);
            Program.Check(metal.VertexCount == twin.Metal.VertexCount && concrete.VertexCount == twin.Concrete.VertexCount && metal.Indices.Count == twin.Metal.Indices.Count, "the same mesh, its vertices moved");
            for (int k = 0; k < twin.Feet.Count; k++)
            {
                PostFoot foot = twin.Feet[k];
                float lowest = float.MaxValue, lowestBefore = float.MaxValue;
                for (int i = foot.TubeFrom; i < foot.TubeTo; i++)
                {
                    lowest = Math.Min(lowest, metal.Positions[i].Y);
                    lowestBefore = Math.Min(lowestBefore, twin.Metal.Positions[i].Y);
                    Program.Check(Math.Abs(metal.Positions[i].X - twin.Metal.Positions[i].X) < 1e-5f && Math.Abs(metal.Positions[i].Z - twin.Metal.Positions[i].Z) < 1e-5f, "the post stays plumb");
                }

                Program.Near(lowest, lowestBefore + top[k], 1e-4f, $"post {k}'s lower end is on the ground");
                float padTop = float.MinValue;
                for (int i = foot.ConcreteFrom; i < foot.ConcreteTo; i++)
                    padTop = Math.Max(padTop, concrete.Positions[i].Y);
                Program.Near(padTop, 0.01f + top[k], 1e-4f, $"its pedestal is level with its ground, 1 cm over it");
            }

            // Everything above the feet stays: the boards' faces are not touched, nor the top of the posts.
            float topBefore = float.MinValue, topAfter = float.MinValue;
            for (int i = 0; i < metal.VertexCount; i++)
            {
                topBefore = Math.Max(topBefore, twin.Metal.Positions[i].Y);
                topAfter = Math.Max(topAfter, metal.Positions[i].Y);
            }

            Program.Near(topAfter, topBefore, 1e-5f, "the posts' tops and the boards stay at their clearance above the road");
            // Up onto a footway 15 cm high: the foot goes up; the post is 15 cm shorter.
            SignParts single = Parts(Plate, SupportCatalog.SinglePole);
            PostGround.Offsets(single, Profile(x => 0.15f), out float[] up, out float[] upBottom);
            Program.Near(up[0], 0.15f, 1e-4f, "onto the footway");
            // A gantry over a road whose far side is 0.8 m lower: the far post is longer.
            SignParts gantry = Parts(Wide, SupportCatalog.Gantry, 18f);
            PostGround.Offsets(gantry, Profile(x => x > 9f ? -0.8f : 0f), out float[] gt, out _);
            Program.Check(Math.Abs(gt[0]) < 1e-4f && Math.Abs(gt[1] + 0.8f) < 1e-4f, "a gantry's far post goes down 0.8 m: " + gt[0] + " " + gt[1]);
            Program.Check(!PostGround.Moves(new[] { 0f, 0f }, new[] { 0f, 0f }) && PostGround.Moves(gt, new[] { 0f, -0.8f }), "nothing moves on level ground: the plain copy");
        }

        private static void Limits()
        {
            SignParts single = Parts(Plate, SupportCatalog.SinglePole);
            PostGround.Offsets(single, Profile(x => -8f), out float[] deep, out _);
            Program.Near(deep[0], -PostGround.MaxDrop, 1e-4f, "no deeper than 5 m");
            PostGround.Offsets(single, Profile(x => 2.5f), out float[] high, out _);
            Program.Check(high[0] <= PostGround.MaxRaise + 1e-4f && high[0] <= single.Layout.BottomClearance - 0.5f + 1e-4f, "no higher than 1.5 m, and half a metre under the board: " + high[0]);
            PostGround.Offsets(single, Profile(x => float.NaN), out float[] none, out float[] noneBottom);
            Program.Check(none[0] == 0f && noneBottom[0] == 0f, "a foot whose ground is not known stays");
            PostGround.Offsets(single, null, out float[] nothing, out _);
            Program.Check(nothing.Length == 1 && nothing[0] == 0f, "no profile, nothing moves");
            // A board low on its post (a door plate on a short post): the foot goes up no more than half a metre under it.
            SignParts arm = Parts(Wide, SupportCatalog.Cantilever, 6f);
            PostGround.Offsets(arm, Profile(x => 1.2f), out float[] armUp, out _);
            Program.Near(armUp[0], 1.2f, 1e-4f, "a cantilever's foot up a verge 1.2 m");
        }

        // 4 Nov (the owner: "做预览重建的优化"): the preview was made again at every half metre the cursor moved; it is kept while the offsets it stands on are the same.
        private static void SameOffsets()
        {
            float[] top = { 0.15f, -0.4f }, bottom = { 0.1f, -0.5f };
            Program.Check(PostGround.Same(top, bottom, new[] { 0.15f, -0.4f }, new[] { 0.1f, -0.5f }), "the same offsets are the same");
            Program.Check(PostGround.Same(top, bottom, new[] { 0.1505f, -0.3995f }, new[] { 0.1004f, -0.5004f }), "half a millimetre either way is the same");
            Program.Check(!PostGround.Same(top, bottom, new[] { 0.152f, -0.4f }, bottom), "two millimetres of the foot is not");
            Program.Check(!PostGround.Same(top, bottom, top, new[] { 0.1f, -0.498f }), "two millimetres of the pad's lower face is not");
            Program.Check(!PostGround.Same(top, bottom, new[] { 0.15f }, new[] { 0.1f }), "another number of feet is not (another support)");
            Program.Check(!PostGround.Same(null, null, top, bottom), "no offsets shown yet: nothing is the same");
            Program.Check(PostGround.Same(new float[0], new float[0], new float[0], new float[0]), "a support with no feet has nothing that differs");
            // A run of small steps is judged against what is shown, not against the step before: 0.6 mm a step does not add up unseen.
            float shown = 0f, widest = 0f;
            int kept = 0;
            for (int step = 1; step <= 10; step++)
            {
                float now = step * 0.0006f;
                if (PostGround.Same(new[] { shown }, new[] { shown }, new[] { now }, new[] { now }))
                    kept++;
                else
                    shown = now;
                widest = Math.Max(widest, Math.Abs(shown - now));
            }

            Program.Check(widest <= 1e-3f + 1e-6f && kept == 5 && Math.Abs(shown - 0.006f) < 1e-6f, "ten steps of 0.6 mm: every other one is kept and the shown offset is never a millimetre off (widest " + widest + ", kept " + kept + ", shown " + shown + ")");
        }

        private static void GroundRule()
        {
            Program.Near(PostGround.GroundOf(true, 0.18f, false, -0.4f), 0.18f, 1e-6f, "a footway under the foot: its surface, not the terrain under it");
            Program.Near(PostGround.GroundOf(false, float.NaN, false, -1.7f), -1.7f, 1e-6f, "no road there: the terrain");
            Program.Check(float.IsNaN(PostGround.GroundOf(false, float.NaN, true, -9f)), "beside an elevated road: the valley below is not its ground");
            Program.Check(float.IsNaN(PostGround.GroundOf(false, float.NaN, false, -7f)), "deeper than 5 m: not followed");
            Program.Check(float.IsNaN(PostGround.GroundOf(false, float.NaN, false, 4f)), "the side of a cutting 4 m above: not followed");
            Program.Near(PostGround.GroundOf(true, 7.5f, false, -0.3f), -0.3f, 1e-6f, "a bridge 7.5 m over the foot: the terrain under it");
            Program.Near(PostGround.GroundOf(true, -0.4f, true, -9f), -0.4f, 1e-6f, "on the bridge's own footway: its surface");
        }

        private static void ProfileAndSurfaces()
        {
            GroundProfile p = Profile(x => 0.1f * x, (x, z) => 0.1f * x + 0.5f * z);
            Program.Near(p.At(3.25f, 0f), 0.325f, 1e-4f, "between the samples along X");
            Program.Near(p.At(2f, GroundProfile.RowZ), 0.2f + 0.4f, 1e-4f, "a row in front");
            Program.Near(p.At(2f, GroundProfile.RowZ * 0.5f), 0.2f + 0.2f, 1e-4f, "between the rows");
            Program.Check(float.IsNaN(p.At(-20f, 0f)) && float.IsNaN(p.At(40f, 0f)), "outside the profile: not known");
            GroundProfile holes = GroundProfile.Sample((x, z) => x > 4f && x < 6f ? float.NaN : 0f, -12f, 12f);
            Program.Check(float.IsNaN(holes.At(5f, 0f)) && !float.IsNaN(holes.At(2f, 0f)), "a place not known stays not known");

            // A city road: two footways 0.2 m up, a raised median that stops traffic 0.15 m up, a barrier 0.9 m up, the carriageway.
            var pieces = new List<SectionPiece>
            {
                new SectionPiece { X = -9f, Width = 3f, Top = 0.2f, Surface = true, Name = "footway" },
                new SectionPiece { X = -4.5f, Width = 6f, Top = 0f, Surface = true, RoadLanes = true, Name = "lanes" },
                new SectionPiece { X = 0f, Width = 3f, Top = 0.15f, Surface = true, BlocksTraffic = true, Name = "median" },
                new SectionPiece { X = 4.5f, Width = 6f, Top = 0f, Surface = true, RoadLanes = true, Name = "lanes" },
                new SectionPiece { X = 7.75f, Width = 0.5f, Top = 0.9f, Surface = true, BlocksTraffic = true, Name = "barrier" },
                new SectionPiece { X = 9.25f, Width = 2.5f, Top = 0.2f, Surface = true, Name = "footway" }
            };
            var log = new List<string>();
            List<PaintableStretch> stand = SurfaceSection.Standable(pieces, 21f, log);
            Program.Check(stand.Count == 5 && Math.Abs(stand[0].Y - 0.2f) < 1e-4f && Math.Abs(stand[2].Y - 0.15f) < 1e-4f && Math.Abs(stand[1].Y) < 1e-4f, "posts stand on the two footways, the two carriageways and the median, each at its own height: " + stand.Count);
            Program.Check(log[4].Contains("barrier or a wall") && log[2].EndsWith("stand"), "not on the barrier: " + string.Join(" | ", log));
            Program.Check(SurfaceSection.Paintable(pieces, 21f).Count == 2, "while paint goes on the lanes only");
        }
    }
}
