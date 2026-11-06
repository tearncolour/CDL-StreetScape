using System;
using System.Collections.Generic;
using System.Threading;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The supports of far tiles (0.7.0): a sign 400 m away does not need its studs and nuts, the ribs of its foot, the collars of its clamps or the web of its truss. 93 % of the vertices of a
    /// tile were such metal. The reduced build must stand the same sign in the same place (faces, picks, layout) and be a fraction of the vertices.
    /// </summary>
    internal static class ReducedDetailTests
    {
        public static void RunAll()
        {
            Program.Run("reduced detail: the same sign in the same place with far fewer vertices, on every support", Same);
            Program.Run("reduced detail: no LED dots, and the full build is not changed by it", Leds);
            Program.Run("reduced detail: the setting belongs to the thread that builds", Threads);
        }

        private static SignDef Def(string id)
        {
            SignDef def = SignCatalog.ById(id);
            if (def == null)
                throw new InvalidOperationException("no sign " + id);
            return def;
        }

        private static int Faces(SignParts parts)
        {
            int n = 0;
            foreach (FacePart face in parts.Faces)
                n += face.Mesh.VertexCount;
            return n;
        }

        private static void Same()
        {
            long full = 0, reduced = 0;
            foreach (string id in new[] { "gb.prohib.09", "gb.warn.01", "guide.exit.advance", "gb.guide8.01", "gb.guide9.47b" })
            {
                SignInstance instance = SignInstances.Of(Def(id), null);
                foreach (SupportDef support in SupportCatalog.All)
                {
                    string what = id + " on " + support.Id;
                    SignParts a = SignBuilder.Build(instance, support, 0f, SupportDetail.Full), b = SignBuilder.Build(instance, support, 0f, SupportDetail.Reduced);
                    SignParts again = SignBuilder.Build(instance, support, 0f);
                    Program.Check(again.Metal.VertexCount == a.Metal.VertexCount && again.Concrete.VertexCount == a.Concrete.VertexCount, what + ": the default build is the full one");

                    // The faces, the layout and the places that can be pointed at are the same.
                    Program.Check(Faces(a) == Faces(b) && a.Faces.Count == b.Faces.Count, what + ": the same faces");
                    Program.Check(a.Layout.BackZ == b.Layout.BackZ && a.Layout.BottomClearance == b.Layout.BottomClearance && a.Layout.PoleTop == b.Layout.PoleTop && a.Layout.ArmLength == b.Layout.ArmLength, what + ": the same layout");
                    Program.Check(a.Picks.Count == b.Picks.Count, what + ": the same number of places to point at: " + a.Picks.Count + " / " + b.Picks.Count);
                    for (int i = 0; i < Math.Min(a.Picks.Count, b.Picks.Count); i++)
                        Program.Check(a.Picks[i].Name == b.Picks[i].Name && a.Picks[i].Min.X == b.Picks[i].Min.X && a.Picks[i].Max.Y == b.Picks[i].Max.Y, what + ": pick " + i + " is the same");

                    // Nothing sticks out beyond the full build, and little of the size is lost.
                    Program.Check(b.Min.X >= a.Min.X - 1e-3f && b.Min.Y >= a.Min.Y - 1e-3f && b.Min.Z >= a.Min.Z - 1e-3f && b.Max.X <= a.Max.X + 1e-3f && b.Max.Y <= a.Max.Y + 1e-3f && b.Max.Z <= a.Max.Z + 1e-3f, what + ": inside the box of the full build");
                    // The height to within the collars; sideways to within the ribs of a foot (up to 0.5 m on a gantry's post, which is what reduced detail leaves out).
                    Program.Check(b.Max.Y >= a.Max.Y - 0.12f && b.Max.X >= a.Max.X - 0.5f && b.Min.X <= a.Min.X + 0.5f && b.Max.Z >= a.Max.Z - 0.5f && b.Min.Z <= a.Min.Z + 0.5f, what + $": the size is the same to within the hardware: Max.Y {a.Max.Y:0.000}/{b.Max.Y:0.000}, Max.X {a.Max.X:0.000}/{b.Max.X:0.000}, Min.X {a.Min.X:0.000}/{b.Min.X:0.000}");

                    // Far fewer vertices of metal; the concrete and the faces are what they were.
                    Program.Check(b.Metal.VertexCount < a.Metal.VertexCount * 0.75f, what + ": the metal is lighter: " + b.Metal.VertexCount + " against " + a.Metal.VertexCount);
                    Program.Check(b.Metal.VertexCount > 0 && b.Concrete.VertexCount == a.Concrete.VertexCount, what + ": there is metal, and the same concrete");
                    full += a.Metal.VertexCount;
                    reduced += b.Metal.VertexCount;

                    // Well formed: indices inside the mesh, and every vertex finite.
                    Program.Check(WellFormed(b.Metal), what + ": the reduced metal is a well formed mesh");
                }
            }

            double share = (double)reduced / full;
            Console.WriteLine($"  reduced detail: {reduced} vertices of metal against {full} ({share * 100:0} %)");
            Program.Check(share < 0.55, "over these signs and supports the reduced metal is under 55 % of the full: " + (share * 100).ToString("0") + " %");
        }

        private static bool WellFormed(MeshBuilder mesh)
        {
            if (mesh.Indices.Count % 3 != 0 || mesh.Positions.Count != mesh.Normals.Count)
                return false;
            foreach (int index in mesh.Indices)
            {
                if (index < 0 || index >= mesh.VertexCount)
                    return false;
            }

            foreach (Vec3 p in mesh.Positions)
            {
                if (!p.IsFinite)
                    return false;
            }

            return true;
        }

        private static LedGrid Grid()
        {
            var grid = new LedGrid(6, 4);
            grid.Dots[0] = LedColour.Red;
            grid.Dots[10] = LedColour.Green;
            grid.Dots[23] = LedColour.White;
            return grid;
        }

        private static void Leds()
        {
            Func<SignInstance, LedGrid> keep = SignBuilder.LedGrids;
            try
            {
                int asked = 0;
                SignBuilder.LedGrids = i => { asked++; return Grid(); };
                SignInstance screen = SignInstances.Of(Def("gb.vms.lane"), null);
                SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);

                SignParts full = SignBuilder.Build(screen, gantry, 0f, SupportDetail.Full);
                Program.Check(asked == 1 && full.LedVertexCount == 3 * 4, "the full build asks for the dots and has them: " + asked + " asked, " + full.LedVertexCount + " vertices");

                asked = 0;
                SignParts reduced = SignBuilder.Build(screen, gantry, 0f, SupportDetail.Reduced);
                Program.Check(asked == 0, "the reduced build does not even ask for the dots (reading them is the expensive part): " + asked);
                Program.Check(reduced.LedVertexCount == 0 && reduced.Faces.Count == 1, "no dots, and the face is there");

                // The full build after a reduced one on the same thread is the full build (nothing was left set).
                SignParts after = SignBuilder.Build(screen, gantry, 0f, SupportDetail.Full);
                Program.Check(after.Metal.VertexCount == full.Metal.VertexCount && after.LedVertexCount == full.LedVertexCount, "a full build after a reduced one is the full build");
                Program.Check(Solids.MaxSegments == 0, "and the limit on the sides of a tube is back to none: " + Solids.MaxSegments);

                // An exception in the build leaves nothing set either.
                try
                {
                    SignBuilder.LedGrids = i => throw new InvalidOperationException("no dots today");
                    SignBuilder.Build(screen, gantry, 0f, SupportDetail.Full);
                }
                catch (InvalidOperationException)
                {
                }

                SignBuilder.LedGrids = null;
                try
                {
                    SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = null, Slot = 0, Member = 0 } }, gantry, 0f, SupportDetail.Reduced);
                }
                catch (Exception)
                {
                }

                Program.Check(Solids.MaxSegments == 0, "a build that threw leaves the limit at none: " + Solids.MaxSegments);
            }
            finally
            {
                SignBuilder.LedGrids = keep;
            }
        }

        private static void Threads()
        {
            // Reduced and full builds on several threads at once give what each gives alone: the setting is the thread's own.
            SignInstance instance = SignInstances.Of(Def("guide.exit.advance"), null);
            SupportDef gantry = SupportCatalog.ById(SupportCatalog.Gantry);
            int fullAlone = SignBuilder.Build(instance, gantry, 0f, SupportDetail.Full).Metal.VertexCount;
            int reducedAlone = SignBuilder.Build(instance, gantry, 0f, SupportDetail.Reduced).Metal.VertexCount;
            Program.Check(reducedAlone < fullAlone, "reduced is lighter alone");

            int wrong = 0;
            var threads = new List<Thread>();
            for (int t = 0; t < 8; t++)
            {
                bool reduced = t % 2 == 0;
                var thread = new Thread(() =>
                {
                    for (int i = 0; i < 40; i++)
                    {
                        int count = SignBuilder.Build(instance, gantry, 0f, reduced ? SupportDetail.Reduced : SupportDetail.Full).Metal.VertexCount;
                        if (count != (reduced ? reducedAlone : fullAlone))
                            Interlocked.Increment(ref wrong);
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads)
                thread.Join();
            Program.Check(wrong == 0, wrong + " build(s) on a thread came out as the other detail");
        }
    }
}
