using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The built meshes of every board of the catalogue on every support, in full and in reduced detail, as one number per board: for each of its builds the fingerprint of every mesh buffer to the last
    /// bit of each float, the picks and the bounds (the layout is left out: it is the support's, which the lighting does not touch, and a wall or an attached support has no base to hash), the builds
    /// folded in the order of SupportCatalog.All (full, then reduced, of each). The golden file was made by the code of 0.12.79, the commit this branch started from, in a worktree of it; a board with
    /// no lighting must still make the same number.
    /// </summary>
    internal static class LightingPrint
    {
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

        /// <summary>The number of a built support: its steel, concrete, coloured solids (by slot, the empty ones left out), faces, LED dots, picks and bounds.</summary>
        public static ulong Of(SignParts parts)
        {
            var h = new Hash();
            AddMesh(h, parts.Metal);
            AddMesh(h, parts.Concrete);
            for (int slot = 0; slot < parts.Solids.Length; slot++)
            {
                if (parts.Solids[slot] == null)
                    continue;
                h.Add(slot);
                AddMesh(h, parts.Solids[slot]);
            }

            foreach (FacePart face in parts.Faces)
                AddMesh(h, face.Mesh);
            foreach (MeshBuilder dots in parts.Leds)
            {
                if (dots != null)
                    AddMesh(h, dots);
            }

            foreach (PickShape pick in parts.Picks)
            {
                h.Add(pick.Name);
                h.Add(pick.Min);
                h.Add(pick.Max);
            }

            h.Add(parts.Min);
            h.Add(parts.Max);
            return h.Value;
        }

        /// <summary>The 64-bit FNV-1a of a text, as 16 hex digits.</summary>
        private static string Fold(string text)
        {
            ulong value = 14695981039346656037UL;
            foreach (byte b in Encoding.UTF8.GetBytes(text))
            {
                value ^= b;
                unchecked { value *= 1099511628211UL; }
            }

            return value.ToString("x16");
        }

        /// <summary>
        /// The supports appended after the golden table was made (0.12.79): left out of the fold, since a board's number would change with the count of supports and not with the board. Each has tests of its own;
        /// a board on the older supports must still come out as it did.
        /// </summary>
        internal static readonly HashSet<string> SinceGolden = new HashSet<string> { SupportCatalog.OverRoad, SupportCatalog.OverRoadHung };

        /// <summary>The number of one board: its builds on every support in full and reduced detail, "support F or R number" joined by ';' and folded.</summary>
        /// <summary>The boards whose far (reduced) builds were redrawn on purpose after 0.12.79 (0.12.105: a text light box's letters, not one bar a line): their golden is of the full detail only (FullOnly).</summary>
        internal static readonly HashSet<string> FarRedrawn = new HashSet<string> { "custom.text", "custom.stand" };

        public static string Board(SignInstance instance) => Board(instance, true);

        /// <summary>The number of a board on every support: in both details, or (reduced false) in the full detail alone.</summary>
        public static string Board(SignInstance instance, bool reduced)
        {
            var parts = new List<string>();
            foreach (SupportDef support in SupportCatalog.All)
            {
                if (SinceGolden.Contains(support.Id))
                    continue;
                foreach (SupportDetail detail in reduced ? new[] { SupportDetail.Full, SupportDetail.Reduced } : new[] { SupportDetail.Full })
                {
                    string print;
                    try
                    {
                        SignParts built = SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, support, 0f, detail);
                        print = Of(built).ToString("x16");
                    }
                    catch (Exception ex)
                    {
                        // A board a support refuses is a build too: it must refuse the same way.
                        print = "!" + ex.GetType().Name;
                    }

                    parts.Add(support.Id + (detail == SupportDetail.Full ? "F" : "R") + print);
                }
            }

            return Fold(string.Join(";", parts));
        }

        /// <summary>
        /// The fonts as the mod chooses them, the same for the golden file and for the check: the boards that draw characters (the text boxes, the neon lamps) are built from glyphs, so what they come to
        /// depends on which fonts are loaded, and the tests that run before this one change that. False when the bundled font is not found.
        /// </summary>
        public static bool Fonts()
        {
            string file = FontLetterTests.RepoFile("Fonts", SignFonts.BundledCjk);
            if (file == null)
                return false;
            SignFonts.Resolve(null, Path.GetDirectoryName(file));
            TextFonts.Use(TextFonts.DefaultId, new FontOutlines(File.ReadAllBytes(file)));
            return true;
        }

        /// <summary>One line for each board of the catalogue that is not a marking: "id|spec|number", the boards at the spec the panel starts them from.</summary>
        /// <summary>The lines of the boards in FarRedrawn, each with the number of its full-detail builds alone (the golden of those: lighting-full-0.12.79.txt).</summary>
        public static List<string> FullLines()
        {
            var lines = new List<string>();
            if (!Fonts())
                return lines;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Def == null || choice.Def.IsMarking || !FarRedrawn.Contains(choice.Id))
                    continue;
                lines.Add(choice.Id + "|" + (choice.Spec ?? "") + "|" + Board(SignInstances.Of(choice.Def, choice.Spec ?? ""), false));
            }

            return lines;
        }

        public static List<string> Lines()
        {
            var lines = new List<string>();
            if (!Fonts())
                return lines;
            foreach (SignChoice choice in SignCatalog.Choices)
            {
                if (choice.Def == null || choice.Def.IsMarking)
                    continue;
                lines.Add(choice.Id + "|" + (choice.Spec ?? "") + "|" + Board(SignInstances.Of(choice.Def, choice.Spec ?? "")));
            }

            return lines;
        }

        public static int Write(string file)
        {
            List<string> lines = Lines();
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            Console.WriteLine(lines.Count + " boards written to " + file);
            return 0;
        }
    }
}
