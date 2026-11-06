using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Bench
{
    /// <summary>How many vertices each kind of support costs, with a small sign and a big one: where the geometry of a tile comes from.</summary>
    internal static class Parts
    {
        public static void Report()
        {
            foreach (string id in new[] { "gb.prohib.09", "gb.warn.01", "guide.exit.advance", "gb.guide8.01" })
            {
                SignDef def = SignCatalog.ById(id);
                if (def == null)
                    continue;
                foreach (SupportDef support in SupportCatalog.All)
                {
                    SignParts parts = SignBuilder.Build(SignInstances.Of(def, null), support, 0f);
                    SignParts reduced = SignBuilder.Build(SignInstances.Of(def, null), support, 0f, SupportDetail.Reduced);
                    Console.WriteLine($"  {id,-20} on {support.Id,-18}: metal {parts.Metal.VertexCount,6} -> reduced {reduced.Metal.VertexCount,5}, concrete {parts.Concrete.VertexCount,5}, faces {FaceVertices(parts),5}");
                }
            }
        }

        private static int FaceVertices(SignParts parts)
        {
            int n = 0;
            foreach (FacePart face in parts.Faces)
                n += face.Mesh.VertexCount;
            return n;
        }
    }
}
