using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The road paint of 0.12.40 (DetailKind.RoadPaint, ChunkBuilder.PaintUv): a whole coat with dirt in it (0.12.42), its texture coordinates the place in the world.</summary>
    internal static class PaintTests
    {
        public static void RunAll()
        {
            Program.Run("paint: a whole coat with dirt in it", Surface);
            Program.Run("paint: texture coordinates from the world, the same across markings and tiles", Coordinates);
        }

        private static void Surface()
        {
            DetailSurface s = DetailSurfaces.Make(DetailKind.RoadPaint);
            Program.Check(s != null && s.Alpha == null && !DetailSurfaces.HasHoles(DetailKind.RoadPaint), "the paint is whole (the owner: worn-through patches read as damage)");
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float v in s.Shade)
            {
                lo = Math.Min(lo, v);
                hi = Math.Max(hi, v);
            }

            Program.Check(hi <= 1f && lo > 0.4f && hi - lo > 0.1f, $"dirt in it, not a flat colour: shade {lo:0.00} to {hi:0.00}");
            Program.Check(Math.Abs(s.SizeU - 2f) < 1e-6f && Math.Abs(s.SizeV - 2f) < 1e-6f, "a tile of 2 m");
        }

        private static void Coordinates()
        {
            // Two strips of paint, one put at a tile's origin (100, 0, 200) and one at another's (164, 0, 200): a point of the world has the same coordinates in both.
            var a = new MeshBuilder();
            Vec3 up = new Vec3(0f, 1f, 0f);
            a.Vertex(new Vec3(63f, 0f, 1f), up);
            a.Vertex(new Vec3(64f, 0f, 1f), up);
            var b = new MeshBuilder();
            b.Vertex(new Vec3(-1f, 0f, 1f), up);
            b.Vertex(new Vec3(0f, 0f, 1f), up);
            ChunkBuilder.PaintUv(a, 0, new Vec3(100f, 0f, 200f));
            ChunkBuilder.PaintUv(b, 0, new Vec3(164f, 0f, 200f));
            Program.Near(a.U[1], b.U[1], 1e-4f, "the same point, the same u in two tiles");
            Program.Near(a.V[1], b.V[1], 1e-4f, "and the same v");
            Program.Near(a.U[1] - a.U[0], 0.5f, 1e-4f, "a metre is half a tile");
            // Only the vertices appended from first on are given theirs.
            var c = new MeshBuilder();
            c.Vertex(new Vec3(5f, 0f, 5f), up, 7f, 7f);
            c.Vertex(new Vec3(5f, 0f, 5f), up);
            ChunkBuilder.PaintUv(c, 1, Vec3.Zero);
            Program.Check(c.U[0] == 7f && Math.Abs(c.U[1] - 2.5f) < 1e-4f, "those before first are left as they were");
            // Paint on an upright face (a kerb line's): from the side, up the face.
            var d = new MeshBuilder();
            d.Vertex(new Vec3(0f, 0.2f, 3f), new Vec3(1f, 0f, 0f));
            d.Vertex(new Vec3(0f, 0.2f, 3f), new Vec3(0f, 0f, -1f));
            ChunkBuilder.PaintUv(d, 0, Vec3.Zero);
            Program.Check(Math.Abs(d.U[0] - 1.5f) < 1e-4f && Math.Abs(d.V[0] - 0.1f) < 1e-4f, "a face looking along x: along z and up");
            Program.Check(Math.Abs(d.U[1]) < 1e-4f && Math.Abs(d.V[1] - 0.1f) < 1e-4f, "a face looking along z: along x and up");
        }
    }
}
