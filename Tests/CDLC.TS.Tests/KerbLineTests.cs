using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A kerb line (线48, 线50) after a save and a load (the owner, 3 Nov: "路缘标线跑到路缘石上边来了"). Its path is the foot of the kerb's face and MarkingGround.AtFoot reads the carriageway in front of the face; the
    /// strip in front of the face belongs to the footway piece, which SurfaceReader.Pieces splits at the kerb only when the game's geometry of the piece is loaded (otherwise the piece is one surface at the
    /// footway's top, and the read is provisional). The log of the owner's two sessions of 3 Nov shows two pieces that stayed in that state for the whole session, and the lines laid from such a read.
    /// </summary>
    internal static class KerbLineTests
    {
        public static void RunAll()
        {
            Program.Run("kerb line: a read that takes the footway as one surface lays the foot on the kerb (the report)", OneSurfaceReproduces);
            Program.Run("kerb line: the side flipped lays the foot on the kerb too, and the side is in the saved settings", SideFlipped);
            Program.Run("kerb line: a lay from a provisional read keeps what the line had", SettleKeeps);
            Program.Run("kerb line: held, the line is drawn at the heights it was placed with, as a lay from a complete read draws it", HeldDrawsAtTheFoot);
            Program.Run("kerb line: a line an older version placed on the kerb's top is laid down to the foot by a complete read", OlderOnTheKerb);
            Program.Run("kerb line: the heights of a laid path, for the log", PathHeights);
            Program.Run("kerb line: a lay from a complete read has not moved a bit", CompleteUnchanged);
        }

        /// <summary>The footway on the kerb's side of the face (x = 100), 20 cm over the carriageway: what a read gives when the piece is split at its kerb.</summary>
        private static Func<List<Vec3>, List<Vec3>> Split(float sign) => MarkingGroundTests.On((x, z) => (x - 100f) * sign >= 0f ? 10.2f : 10f);

        /// <summary>What a read gives when the footway piece is taken as one surface (SurfaceReader.Pieces with the piece's geometry not loaded): the strip in front of the face has the footway's height.</summary>
        private static Func<List<Vec3>, List<Vec3>> OneSurface() => MarkingGroundTests.On((x, z) => 10.2f);

        private static void OneSurfaceReproduces()
        {
            SignDef def = MarkingGroundTests.Def("mk.kerb.nopark");
            var position = new Vec3(100f, 10f, 200f);
            foreach (string side in new[] { "r", "l" })
            {
                float sign = side == "r" ? 1f : -1f;
                string spec = MarkingGroundTests.Run(def, 20f, z => 10f, "side=" + side + ";height=20");
                MarkingGround.Laid complete = MarkingGround.Lay(def, spec, position, 0f, Split(sign));
                Program.Near(complete.Dy, 0f, 0.002f, side + ": split at the kerb, the foot is on the carriageway");
                MarkingGround.Laid one = MarkingGround.Lay(def, spec, position, 0f, OneSurface());
                Program.Near(one.Dy, 0.2f, 0.002f, side + ": the piece as one surface, the foot is the kerb's top: the line stands on the kerb");
                MarkingGroundTests.Heights(def, one, position, false, out float lo, out _, out _);
                Program.Near(lo, 10.2f, 0.01f, side + ": and so does its paint (lowest)");
            }
        }

        private static void SideFlipped()
        {
            SignDef def = MarkingGroundTests.Def("mk.kerb.nopark");
            var position = new Vec3(100f, 10f, 200f);
            string spec = MarkingGroundTests.Run(def, 20f, z => 10f, "side=r;height=20");
            // The side is part of the settings that are saved with the item, and is the one that was put.
            Program.Check(def.Template.Parse(spec).Is("side", "r") && def.Template.Parse(def.Template.Parse(spec).ToString()).Is("side", "r"), "the side is in the settings and survives writing and reading them");
            MarkingGround.Laid right = MarkingGround.Lay(def, spec, position, 0f, Split(1f));
            Program.Near(right.Dy, 0f, 0.002f, "the kerb on the right, read on the right: the carriageway");
            // The read in front of the face is on the other side of it when the side is the wrong one.
            MarkingGround.Laid wrong = MarkingGround.Lay(def, def.Template.Parse(spec).With("side", "l").ToString(), position, 0f, Split(1f));
            Program.Near(wrong.Dy, 0.2f, 0.002f, "a side that is not the kerb's reads the footway: the kerb's top");
        }

        private static void SettleKeeps()
        {
            SignDef kerb = MarkingGroundTests.Def("mk.kerb.nopark");
            SignDef edge = MarkingGroundTests.Def("mk.line.edge");
            var position = new Vec3(100f, 10f, 200f);
            string spec = MarkingGroundTests.Run(kerb, 20f, z => 10f, "side=r;height=20");
            MarkingGround.Laid made = MarkingGround.Lay(kerb, spec, position, 0f, OneSurface());
            MarkingGround.Laid before = MarkingGround.Lay(kerb, spec, position, 0f, Split(1f));
            Program.Check(MarkingGround.KeepsPlaced(kerb) && MarkingGround.KeepsPlaced(MarkingGroundTests.Def("mk.kerb.nolong")), "both kerb lines keep what they had");
            Program.Check(!MarkingGround.KeepsPlaced(edge) && !MarkingGround.KeepsPlaced(MarkingGroundTests.Def("mk.kerbstop")) && !MarkingGround.KeepsPlaced(null), "nothing else does: an edge line, a bus stop, no marking");
            Program.Check(MarkingGround.Settle(kerb, made, null, true) == null, "a provisional lay of a kerb line never laid: it is drawn as placed");
            Program.Check(ReferenceEquals(MarkingGround.Settle(kerb, made, before, true), before), "a provisional lay of a kerb line laid before: what it had");
            Program.Check(ReferenceEquals(MarkingGround.Settle(kerb, made, before, false), made), "a lay from a complete read is taken");
            Program.Check(ReferenceEquals(MarkingGround.Settle(kerb, made, null, false), made), "a first lay from a complete read is taken");
            MarkingGround.Laid line = MarkingGround.Lay(edge, MarkingGroundTests.Run(edge, 20f, z => 10f), position, 0f, OneSurface());
            Program.Check(ReferenceEquals(MarkingGround.Settle(edge, line, null, true), line), "another line is laid as before, provisional or not");
            Program.Check(MarkingGround.Settle(kerb, null, before, true) == null && ReferenceEquals(MarkingGround.Settle(null, line, null, true), line), "no lay is no lay, and an unknown marking is the lay");
        }

        private static void HeldDrawsAtTheFoot()
        {
            SignDef def = MarkingGroundTests.Def("mk.kerb.nopark");
            var position = new Vec3(100f, 10f, 200f);
            string spec = MarkingGroundTests.Run(def, 20f, z => 10f, "side=r;height=20");
            MarkingGround.Laid made = MarkingGround.Lay(def, spec, position, 0f, OneSurface());
            MarkingGround.Laid held = MarkingGround.Settle(def, made, null, true);
            float Lowest(MarkingGround.Laid laid)
            {
                var record = new ItemRecord(def.Hash, position, 0f, 0u, spec) { Laid = laid };
                List<ChunkPart> parts = ChunkBuilder.Build(new[] { record }, new Vec3(60f, 0f, 120f), out int skipped);
                Program.Check(skipped == 0 && parts.Count > 0, "built");
                float lo = float.MaxValue;
                foreach (ChunkPart part in parts)
                {
                    foreach (Vec3 p in part.Mesh.Positions)
                        lo = Math.Min(lo, p.Y);
                }

                return lo;
            }

            float complete = Lowest(MarkingGround.Lay(def, spec, position, 0f, Split(1f)));
            Program.Near(Lowest(held), complete, 0.001f, "held, the paint starts where a lay from a complete read starts it: on the carriageway");
            Program.Check(Lowest(made) > complete + 0.19f, "the provisional lay itself starts it on the kerb: " + Lowest(made) + " against " + complete);
        }

        private static void OlderOnTheKerb()
        {
            SignDef def = MarkingGroundTests.Def("mk.kerb.nolong");
            // Before 0.12.35 the line's path was on the footway: its origin 20 cm over the carriageway.
            var position = new Vec3(100f, 10.2f, 200f);
            string spec = MarkingGroundTests.Run(def, 20f, z => 10.2f, "side=r;height=20");
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, Split(1f));
            Program.Near(laid.Dy, -0.2f, 0.002f, "its foot goes down to the carriageway");
            Program.Near(laid.Worst, 0.2f, 0.002f, "and it had been 20 cm over it");
        }

        private static void PathHeights()
        {
            SignDef def = MarkingGroundTests.Def("mk.kerb.nopark");
            var position = new Vec3(100f, 10f, 200f);
            string spec = MarkingGroundTests.Run(def, 20f, z => 10f + 0.05f * z, "side=r;height=20");
            MarkingGround.Laid laid = MarkingGround.Lay(def, spec, position, 0f, MarkingGroundTests.On((x, z) => 10f + 0.05f * (z - 200f) + ((x - 100f) >= 0f ? 0.2f : 0f)));
            MarkingGround.PathHeights(def.Template, laid.Spec, position.Y + laid.Dy, out float lo, out float hi);
            Program.Near(lo, 10f, 0.01f, "the path starts at the foot");
            Program.Near(hi, 11f, 0.02f, "and climbs the road's 5 % over 20 m: " + hi);
        }

        private static void CompleteUnchanged()
        {
            string golden = FontLetterTests.RepoFile("Tests", "CDLC.TS.Tests", "Golden", "kerbline-0.12.83.txt");
            Program.Check(System.IO.File.Exists(golden), "the golden file is there: " + golden);
            if (!System.IO.File.Exists(golden))
                return;
            string[] want = System.IO.File.ReadAllLines(golden, new System.Text.UTF8Encoding(false));
            List<string> have = KerbLinePrint.Lines();
            Program.Check(want.Length == have.Count && want.Length >= 48, $"{have.Count} lays against {want.Length} in the golden");
            int differ = 0;
            for (int i = 0; i < Math.Min(want.Length, have.Count); i++)
            {
                if (want[i] != have[i] && differ++ < 5)
                    Program.Check(false, "differs from 0.12.83: " + want[i] + " / " + have[i]);
            }

            Program.Check(differ == 0, "no kerb line over a complete read has moved a bit: " + differ + " differ");
        }
    }
}
