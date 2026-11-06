using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The construction hoarding's default (the flat panel, no flowers) is what it was before the styles: a hash of every vertex, normal, texture coordinate and index of the plans of 72 settings (three
    /// colours, three heights, two post spacings over four lines and two pieces of a run), made on the commit before the styles (35cb654) and frozen here. Uses only what that commit had.
    /// </summary>
    internal static class HoardingRegressionTests
    {
        public static void RunAll() => Program.Run("hoarding: the default is the flat panel of 0.12.77, vertex for vertex", Frozen);

        private static Vec3 P(float x, float z, float y = 0f) => new Vec3(x, y, z);

        private static List<List<Vec3>> Lines() => new List<List<Vec3>>
        {
            new List<Vec3> { P(0f, 0f), P(0f, 10f) },
            new List<Vec3> { P(0f, 0f), P(0f, 5f), P(4f, 9f), P(9f, 9f) },
            new List<Vec3> { P(0f, 0f, 0f), P(0f, 6f, 0.4f), P(1f, 12f, 0.1f) },
            new List<Vec3> { P(0f, 0f), P(0f, 2.5f), P(0.3f, 4f), P(2f, 4.2f) }
        };

        private static MarkingPlan PlanOf(string settings, List<Vec3> line, float phase, float total)
        {
            GuideTemplate t = MarkingTemplates.ById(MarkingTemplates.HoardingId);
            string path = RunPath.Encode(line, out _);
            GuideSpec spec = t.Default();
            foreach (string pair in settings.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                spec = spec.With(pair.Substring(0, eq), pair.Substring(eq + 1));
            }

            return MarkingTemplates.PlanOf(t, MarkingTemplates.WithRun(t, spec.ToString(), path, phase, total));
        }

        /// <summary>FNV-1a over what a plan draws, in the order it holds it.</summary>
        internal static ulong Hash(MarkingPlan plan)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                for (int i = 0; i < 8; i++)
                {
                    h ^= (ulong)((v >> (8 * i)) & 0xFF);
                    h *= 1099511628211UL;
                }
            }

            void MixF(float f) => Mix(BitConverter.SingleToInt32Bits(f));
            Mix(plan.Parts.Count);
            foreach (MarkingPart part in plan.Parts)
            {
                Mix(part.Solid.HasValue ? (int)part.Solid.Value : -1);
                Mix((int)part.Detail);
                MeshBuilder m = part.Mesh;
                Mix(m.VertexCount);
                for (int i = 0; i < m.VertexCount; i++)
                {
                    MixF(m.Positions[i].X); MixF(m.Positions[i].Y); MixF(m.Positions[i].Z);
                    MixF(m.Normals[i].X); MixF(m.Normals[i].Y); MixF(m.Normals[i].Z);
                    MixF(m.U[i]); MixF(m.V[i]);
                }

                Mix(m.Indices.Count);
                foreach (int index in m.Indices)
                    Mix(index);
            }

            Mix(plan.Props.Count);
            return h;
        }

        /// <summary>The hashes of the 72 plans, from the commit before the styles (35cb654).</summary>
        private static readonly ulong[] s_Frozen =
        {
            16596485240849583638UL, 17526366284659939613UL, 8138861473510967086UL,
            15024422103107048730UL, 5348821510586757246UL, 13052397185395174287UL,
            3047971822930998704UL, 276441868724046418UL, 10813275759343370098UL,
            15876377756523774221UL, 13123779239295453066UL, 8195372091328452038UL,
            13889744929529471506UL, 243537782052469303UL, 11630900922635411664UL,
            2960115427849535078UL, 5235087346879844018UL, 5869771568348443529UL,
            1259667491423695210UL, 5243210963250892190UL, 4869521471944400082UL,
            8609046997269185379UL, 5896797062058297840UL, 2595104425602820574UL,
            13071103107551284396UL, 5602128201125145699UL, 17543357839858289748UL,
            9052443782422328600UL, 4015927808821348924UL, 14275201251408824825UL,
            9952092508052625190UL, 10202546942273290728UL, 14853805150265452096UL,
            6898486150108656143UL, 4158501801864858612UL, 17175364411846514164UL,
            9165791392756451768UL, 9114866498055517837UL, 9249764445510861678UL,
            3715574367947860612UL, 2582357227008222464UL, 18128144491222719947UL,
            6954483809792956060UL, 17873227401540154060UL, 15493941610043680056UL,
            12041682326021306281UL, 12436180329452414198UL, 5568700770683034044UL,
            14681936365201817085UL, 16410122824012235474UL, 7777489131439368241UL,
            10653009659236611673UL, 13387465015146154285UL, 614810316851706500UL,
            13337144356751738679UL, 2785030052993228969UL, 5053497556138005561UL,
            1132840347562442898UL, 15123273023528333413UL, 6625118678628337733UL,
            8583088730882444897UL, 6315896457108226028UL, 12265910381455585247UL,
            8917209460940253853UL, 14998956242470438841UL, 9682079709879421414UL,
            15304766128242414485UL, 11825662690038600149UL, 1259697268409778657UL,
            15321678065117078128UL, 2441952776095683535UL, 5558861254160104141UL
        };

        private static void Frozen()
        {
            var seen = new List<ulong>();
            foreach (string colour in new[] { "b", "w", "g" })
            {
                foreach (string height in new[] { "180", "200", "250" })
                {
                    foreach (string post in new[] { "2", "3" })
                    {
                        string settings = $"colour={colour};height={height};post={post}";
                        List<List<Vec3>> lines = Lines();
                        for (int l = 0; l < lines.Count; l++)
                        {
                            // Two lines of the four are a piece in the middle of a longer run (phase 1.7 of 30 m).
                            bool piece = l >= 2;
                            ulong hash = Hash(PlanOf(settings, lines[l], piece ? 1.7f : 0f, piece ? 30f : 0f));
                            seen.Add(hash);
                        }
                    }
                }
            }

            if (s_Frozen.Length == 0)
            {
                for (int i = 0; i < seen.Count; i++)
                    Console.WriteLine($"HOARDING-HASH {i} {seen[i]}UL");
                return;
            }

            Program.Check(seen.Count == s_Frozen.Length && seen.Count == 72, "the settings counted: " + seen.Count);
            for (int i = 0; i < seen.Count; i++)
                Program.Check(seen[i] == s_Frozen[i], "setting " + i + " draws what it drew before the styles");
        }
    }
}
