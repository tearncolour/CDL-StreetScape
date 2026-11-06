using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// When a tile's material is made (0.12.76): the owner's 0.12.75 log had one part of 460 vertices that took 40.7 ms because its material was made in it. The decisions are pure (MaterialGate,
    /// DetailStages) and so is the colour of the maps (DetailPixels), which a worker makes now.
    /// </summary>
    internal static class MaterialGateTests
    {
        public static void RunAll()
        {
            Program.Run("material gate: a material is made in an update of its own, a tile waits for one that is on its way", Gate);
            Program.Run("detail stages: the order, and a stage starts only inside the budget", Stages);
            Program.Run("detail pixels: the maps are the ones the loops of the main thread made, on a worker too", Pixels);
        }

        private static void Gate()
        {
            const double budget = 0.35;
            Program.Check(MaterialGate.Decide(MaterialNeed.Have, 100.0, 9, 3, budget, 0.0) == GateAction.Go, "a material that is there is never held up, whatever was spent");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 0.0, 0, 0, budget, 0.0) == GateAction.Go, "the first part of an update may make its material");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 0.1, 2, 0, budget, 0.0) == GateAction.Go, "after other parts, within half the budget (0.175 ms), a material may be made");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 0.2, 2, 0, budget, 0.0) == GateAction.Defer, "after other parts that used more than half of it, the material is for the next update");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 0.175, 2, 0, budget, 0.0) == GateAction.Go && MaterialGate.Decide(MaterialNeed.Make, 0.1751, 2, 0, budget, 0.0) == GateAction.Defer, "half the budget itself is still allowed");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 0.0, 0, 1, budget, 0.0) == GateAction.Defer, "one material an update, even when nothing else was done");
            Program.Check(MaterialGate.Decide(MaterialNeed.Make, 50.0, 0, 0, budget, 0.0) == GateAction.Go, "the first part is made whatever the clock says: an update that does nothing never finishes a tile");

            Program.Check(MaterialGate.Decide(MaterialNeed.Wait, 0.0, 0, 0, budget, 0.0) == GateAction.Wait, "a material that is on its way is waited for");
            Program.Check(MaterialGate.Decide(MaterialNeed.Wait, 0.0, 0, 0, budget, MaterialGate.WaitSeconds - 0.01) == GateAction.Wait, "still within the time it may take");
            Program.Check(MaterialGate.Decide(MaterialNeed.Wait, 0.0, 0, 0, budget, MaterialGate.WaitSeconds) == GateAction.Go, "after that the main thread makes it as it always did");
            Program.Check(MaterialGate.Decide(MaterialNeed.Wait, 0.3, 3, 0, budget, 9.0) == GateAction.Defer && MaterialGate.Decide(MaterialNeed.Wait, 0.0, 0, 1, budget, 9.0) == GateAction.Defer, "and then it is held to the same rule as any other material");

            Program.Check(MaterialGate.MayMake(0.0, 0, 0, double.MaxValue) && MaterialGate.MayMake(1e300, 1, 0, double.MaxValue), "an unbounded budget allows it");
            Program.Check(!MaterialGate.MayMake(0.0, 1, 0, -1.0) && MaterialGate.MayMake(0.0, 0, 0, -1.0), "a spent budget (the tests' -1) allows only the first part's");

            // The chain a tile goes through: three new materials, each in its own update, the parts that have theirs in between.
            var needs = new[] { MaterialNeed.Have, MaterialNeed.Make, MaterialNeed.Make, MaterialNeed.Have, MaterialNeed.Make };
            int updates = 0, next = 0;
            while (next < needs.Length && updates < 20)
            {
                updates++;
                int partsDone = 0, made = 0;
                while (next < needs.Length)
                {
                    GateAction action = MaterialGate.Decide(needs[next], 0.0, partsDone, made, budget, 0.0);
                    if (action != GateAction.Go)
                        break;
                    if (needs[next] == MaterialNeed.Make)
                        made++;
                    partsDone++;
                    next++;
                }
            }

            Program.Check(next == needs.Length && updates == 3, "five parts with three materials to make take three updates: " + updates);
        }

        private static void Stages()
        {
            foreach (bool holes in new[] { false, true })
            {
                foreach (bool maps in new[] { false, true })
                {
                    var order = new List<DetailStage>();
                    DetailStage stage = DetailStage.Prepare;
                    for (int i = 0; i < 12 && stage != DetailStage.Done; i++)
                    {
                        order.Add(stage);
                        stage = DetailStages.Next(stage, holes, maps);
                    }

                    var want = new List<DetailStage> { DetailStage.Prepare, DetailStage.Colour };
                    if (holes)
                        want.Add(DetailStage.Holes);
                    want.Add(DetailStage.Compress);
                    if (!maps)
                    {
                        want.Add(DetailStage.Normal);
                        want.Add(DetailStage.Mask);
                    }

                    want.Add(DetailStage.Material);
                    Program.Check(stage == DetailStage.Done && string.Join(",", order) == string.Join(",", want), $"holes {holes}, maps made {maps}: {string.Join(",", order)}");
                }
            }

            Program.Check(DetailStages.Next(DetailStage.Done, true, true) == DetailStage.Done, "Done stays Done");
            Program.Check(DetailStages.MayStart(0.0, 1.0) && DetailStages.MayStart(0.99, 1.0) && !DetailStages.MayStart(1.0, 1.0) && !DetailStages.MayStart(5.0, 1.0), "a stage starts only while there is budget left; the one running is not cut short");
            Program.Check(!DetailStages.MayStart(0.0, 0.0) && !DetailStages.MayStart(0.0, -1.0), "no budget, no stage");
        }

        private static byte Reference(float v)
        {
            int i = (int)Math.Round((double)(v * 255f));
            return (byte)Math.Max(0, Math.Min(255, i));
        }

        private static void Pixels()
        {
            Program.Check(DetailPixels.ToByte(0f) == 0 && DetailPixels.ToByte(1f) == 255 && DetailPixels.ToByte(-0.4f) == 0 && DetailPixels.ToByte(3f) == 255 && DetailPixels.ToByte(float.NaN) == 0, "the byte of a unit value clamps");
            Program.Check(DetailPixels.ToByte(0.5f / 255f) == 0 && DetailPixels.ToByte(1.5f / 255f) == 2 && DetailPixels.ToByte(2.5f / 255f) == 2, "and rounds half to even, as Mathf.RoundToInt does");

            foreach (DetailKind kind in new[] { DetailKind.Galvanised, DetailKind.WeldedMesh, DetailKind.TreeGrate })
            {
                DetailSurface s = DetailSurfaces.Make(kind);
                Program.Check(s != null, kind + " bakes");
                if (s == null)
                    continue;
                int n = s.Width * s.Height;
                DetailPixels.Maps(s, out byte[] normal, out byte[] mask);
                Program.Check(normal.Length == n * 4 && mask.Length == n * 4, kind + ": four bytes a texel");
                bool same = true;
                for (int k = 0; k < n && same; k++)
                {
                    same = normal[k * 4] == Reference(s.Normals[k * 3] * 0.5f + 0.5f) && normal[k * 4 + 1] == Reference(s.Normals[k * 3 + 1] * 0.5f + 0.5f)
                        && normal[k * 4 + 2] == Reference(s.Normals[k * 3 + 2] * 0.5f + 0.5f) && normal[k * 4 + 3] == 255
                        && mask[k * 4] == Reference(s.Metal[k]) && mask[k * 4 + 1] == Reference(s.Coat[k]) && mask[k * 4 + 2] == 0 && mask[k * 4 + 3] == Reference(s.Smooth[k]);
                }

                Program.Check(same, kind + ": the normal map and the mask map are as the loops had them");

                float red = 0.62f, green = 0.5f, blue = 0.31f;
                byte[] colour = DetailPixels.Colour(s, red, green, blue);
                bool colourSame = colour.Length == n * 4;
                for (int k = 0; k < n && colourSame; k++)
                {
                    colourSame = colour[k * 4] == Reference(red * s.Shade[k]) && colour[k * 4 + 1] == Reference(green * s.Shade[k]) && colour[k * 4 + 2] == Reference(blue * s.Shade[k])
                        && colour[k * 4 + 3] == (s.Alpha != null ? Reference(s.Alpha[k]) : (byte)255);
                }

                Program.Check(colourSame, kind + ": the colour map is the colour times the shade, with the holes in its alpha (" + (s.Alpha != null ? "has holes" : "solid") + ")");

                // Made on a worker the same: the pixels are a pure function of the surface.
                byte[] onWorker = Task.Run(() => DetailPixels.Colour(s, red, green, blue)).Result;
                Program.Check(onWorker.Length == colour.Length && BitConverter.ToString(onWorker, 0, 64) == BitConverter.ToString(colour, 0, 64) && SameBytes(onWorker, colour), kind + ": made on a worker the same");
            }
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }
    }
}
