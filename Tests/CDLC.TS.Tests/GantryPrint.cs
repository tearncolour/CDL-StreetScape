using System;
using System.Collections.Generic;
using System.Reflection;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// A fingerprint of a built support (its layout, every mesh buffer, the picks and the feet) to the last bit of each float: a gantry that is drawn "as before" is the same fingerprint.
    /// The layout is hashed by the names of the fields it had in 0.12.77, so a field added later does not change it.
    /// </summary>
    internal static class GantryPrint
    {
        private static readonly string[] s_LayoutFields =
        {
            "ArmLength", "ArmY", "PoleRadius", "BeamDepth", "BeamWidth", "ChordRadius", "ChordYs", "PoleX", "BoardCentreX", "BoardCentreY", "BoardTop", "BottomClearance", "PoleTop", "HangY", "HangRadius",
            "BackZ", "InnerReach", "PostClearance", "PostBackZ", "PostTop", "RailK"
        };

        private sealed class Hash
        {
            public ulong Value = 14695981039346656037UL;

            public void Add(int v)
            {
                unchecked
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Value ^= (byte)(v >> (8 * i));
                        Value *= 1099511628211UL;
                    }
                }
            }

            public void Add(float v) => Add(BitConverter.SingleToInt32Bits(v));

            public void Add(string s)
            {
                Add(s == null ? -1 : s.Length);
                if (s != null)
                {
                    foreach (char c in s)
                        Add((int)c);
                }
            }

            public void Add(Vec3 v)
            {
                Add(v.X);
                Add(v.Y);
                Add(v.Z);
            }
        }

        public static ulong Of(SignParts parts)
        {
            var h = new Hash();
            object boxed = parts.Layout;
            foreach (string name in s_LayoutFields)
            {
                FieldInfo field = typeof(SupportLayout).GetField(name);
                object value = field.GetValue(boxed);
                h.Add(name);
                if (value is float f)
                    h.Add(f);
                else if (value is float[] array)
                {
                    h.Add(array == null ? -1 : array.Length);
                    if (array != null)
                    {
                        foreach (float x in array)
                            h.Add(x);
                    }
                }
            }

            h.Add(parts.Layout.Base.HalfX);
            h.Add(parts.Layout.Base.HalfZ);
            h.Add(parts.Layout.Base.Thick);
            AddMesh(h, parts.Metal);
            AddMesh(h, parts.Concrete);
            // The coloured solids by their slot, the empty ones left out: a build with more colour slots (0.12.78 added three) is the same print.
            for (int slot = 0; slot < parts.Solids.Length; slot++)
            {
                if (parts.Solids[slot] == null)
                    continue;
                h.Add(slot);
                AddMesh(h, parts.Solids[slot]);
            }
            foreach (FacePart face in parts.Faces)
                AddMesh(h, face.Mesh);
            foreach (PickShape pick in parts.Picks)
            {
                h.Add(pick.Name);
                h.Add(pick.Min);
                h.Add(pick.Max);
            }

            foreach (PostFoot foot in parts.Feet)
            {
                h.Add(foot.X);
                h.Add(foot.Z);
                h.Add(foot.PadHalf);
                h.Add(foot.MetalFrom);
                h.Add(foot.MetalTo);
                h.Add(foot.TubeFrom);
                h.Add(foot.TubeTo);
                h.Add(foot.ConcreteFrom);
                h.Add(foot.ConcreteTo);
                h.Add(foot.Split);
                h.Add(foot.Paint.Count);
            }

            h.Add(parts.Min);
            h.Add(parts.Max);
            return h.Value;
        }

        private static void AddMesh(Hash h, MeshBuilder mesh)
        {
            if (mesh == null)
            {
                h.Add(-1);
                return;
            }

            h.Add(mesh.VertexCount);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                h.Add(mesh.Positions[i]);
                h.Add(mesh.Normals[i]);
                h.Add(mesh.U[i]);
                h.Add(mesh.V[i]);
            }

            h.Add(mesh.Indices.Count);
            foreach (int index in mesh.Indices)
                h.Add(index);
        }

        /// <summary>The cases the regression is made of: a span (0 is the support's own), the level of detail and the boards.</summary>
        public static IEnumerable<(string Name, ulong Print)> Cases(SupportDef support)
        {
            SignInstance small = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
            SignInstance wide = SignInstances.Of(SignCatalog.ById("guide.exit.advance"), GuideTemplates.SpecOf(GuideTemplates.PresetById("gb.exit.adv.2km")).ToString());
            foreach (float span in new[] { 0f, 6f, 10f, 14f, 16f, 16.5f, 20f, 22f, 23f, 30f, 40f })
            {
                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                {
                    string at = $"{span:0.#}/{detail}";
                    yield return ("small " + at, Of(SignBuilder.Build(small, support, span, detail)));
                    yield return ("wide " + at, Of(SignBuilder.Build(wide, support, span, detail)));
                    var both = new List<AssemblyBoard>
                    {
                        new AssemblyBoard { Instance = small, Lane = 1, OffsetX = 3f, Slot = 0, Member = 0 },
                        new AssemblyBoard { Instance = wide, Lane = 2, OffsetX = 8f, Slot = 1, Member = 1, Back = true }
                    };
                    yield return ("two " + at, Of(SignBuilder.BuildAssembly(both, support, span, detail)));
                }
            }
        }
    }
}
