using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The space-truss gantry (0.12.78; the owner's photo of an expressway's double-layer gantry): the portal gantry draws bit for bit as it did in 0.12.77 (an item that stores the portal's hash or none),
    /// the layout rule of the space truss at any span, the planner's choice by the road's span, and the built gantry (closed, finite, boards in front of the truss, no tube through a board).
    /// </summary>
    internal static class SpaceGantryTests
    {
        /// <summary>The fingerprints (GantryPrint, the scheme independent of the number of colour slots) of the portal gantry as 0.12.77 built it, made on commit 35cb654: three board sets over eleven spans in both levels of detail.</summary>
        private static readonly (string Name, ulong Print)[] s_Portal =
        {
            ("small 0/Full", 9958808011584129798UL),
            ("wide 0/Full", 14562194085547403043UL),
            ("two 0/Full", 10384841216243981416UL),
            ("small 0/Reduced", 5132613597993914283UL),
            ("wide 0/Reduced", 18167707624122605841UL),
            ("two 0/Reduced", 8179183793741985876UL),
            ("small 6/Full", 14354946724541630467UL),
            ("wide 6/Full", 4146866386567727595UL),
            ("two 6/Full", 8598043249451054846UL),
            ("small 6/Reduced", 9855341535559873987UL),
            ("wide 6/Reduced", 335725297900475066UL),
            ("two 6/Reduced", 14994418590315738725UL),
            ("small 10/Full", 2473476828229557195UL),
            ("wide 10/Full", 9072125991979926034UL),
            ("two 10/Full", 8598043249451054846UL),
            ("small 10/Reduced", 16811301096404388420UL),
            ("wide 10/Reduced", 10268963441729038966UL),
            ("two 10/Reduced", 14994418590315738725UL),
            ("small 14/Full", 9958808011584129798UL),
            ("wide 14/Full", 14562194085547403043UL),
            ("two 14/Full", 10384841216243981416UL),
            ("small 14/Reduced", 5132613597993914283UL),
            ("wide 14/Reduced", 18167707624122605841UL),
            ("two 14/Reduced", 8179183793741985876UL),
            ("small 16/Full", 10329532300951099023UL),
            ("wide 16/Full", 12646520335282744288UL),
            ("two 16/Full", 428025275776682085UL),
            ("small 16/Reduced", 10371827173218840730UL),
            ("wide 16/Reduced", 1591450725272808575UL),
            ("two 16/Reduced", 6419843127485351367UL),
            ("small 16.5/Full", 15899560753980703364UL),
            ("wide 16.5/Full", 1623349587048869524UL),
            ("two 16.5/Full", 8229813595340885877UL),
            ("small 16.5/Reduced", 1951496790183520205UL),
            ("wide 16.5/Reduced", 11706565883822607269UL),
            ("two 16.5/Reduced", 1010368480942569369UL),
            ("small 20/Full", 5608249609742454942UL),
            ("wide 20/Full", 6269669185885374821UL),
            ("two 20/Full", 7144003789335116788UL),
            ("small 20/Reduced", 5490727495620250204UL),
            ("wide 20/Reduced", 2185193672175813204UL),
            ("two 20/Reduced", 5087873845816890189UL),
            ("small 22/Full", 14771766211371138195UL),
            ("wide 22/Full", 12177597858225530881UL),
            ("two 22/Full", 18097564420812966648UL),
            ("small 22/Reduced", 8576981710017385436UL),
            ("wide 22/Reduced", 18295043437406160826UL),
            ("two 22/Reduced", 17593511048249045081UL),
            ("small 23/Full", 10181501556211478576UL),
            ("wide 23/Full", 4392693068664976677UL),
            ("two 23/Full", 4862833564430873241UL),
            ("small 23/Reduced", 6973629821014886363UL),
            ("wide 23/Reduced", 4987977449169352858UL),
            ("two 23/Reduced", 2777085200684568592UL),
            ("small 30/Full", 1273232331854934732UL),
            ("wide 30/Full", 11492698141169804487UL),
            ("two 30/Full", 11384058636664458366UL),
            ("small 30/Reduced", 15231418416452336453UL),
            ("wide 30/Reduced", 7463180055635264069UL),
            ("two 30/Reduced", 4387091221612043871UL),
            ("small 40/Full", 6747330198046359684UL),
            ("wide 40/Full", 4069929523281512048UL),
            ("two 40/Full", 10018410617733757761UL),
            ("small 40/Reduced", 1233228124727036139UL),
            ("wide 40/Reduced", 8312844478765769109UL),
            ("two 40/Reduced", 9495878294983756956UL),
        };

        public static void RunAll()
        {
            Program.Run("space gantry: the portal gantry is drawn bit for bit as in 0.12.77", PortalUnchanged);
            Program.Run("space gantry: the depth of the truss, its width and the legs by the span", Layout);
            Program.Run("space gantry: the planner's choice by the road's span", Choice);
            Program.Run("space gantry: the catalogue entry, its hash and its family", Catalogue);
            Program.Run("space gantry: built closed, finite and in front of the boards", Built);
            Program.Run("space gantry: the legs, the lacing and the feet", Legs);
        }

        public static void PrintGolden()
        {
            foreach ((string name, ulong print) in GantryPrint.Cases(SupportCatalog.ById(SupportCatalog.Gantry)))
                Console.WriteLine($"            (\"{name}\", {print}UL),");
        }

        private static SupportDef Space => SupportCatalog.ById(SupportCatalog.GantrySpace);

        private static SupportDef Portal => SupportCatalog.ById(SupportCatalog.Gantry);

        private static void PortalUnchanged()
        {
            var now = GantryPrint.Cases(Portal).ToDictionary(c => c.Name, c => c.Print);
            Program.Check(now.Count == s_Portal.Length, "the same cases: " + now.Count + " against " + s_Portal.Length);
            foreach ((string name, ulong print) in s_Portal)
                Program.Check(now.TryGetValue(name, out ulong built) && built == print, "the portal gantry '" + name + "' is as 0.12.77 drew it");
            // The space truss over 16 m shares the portal's truss only: it is a support of its own, on round legs.
            SignInstance sign = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null);
            Program.Check(GantryPrint.Of(SignBuilder.Build(sign, Space, 20f)) != GantryPrint.Of(SignBuilder.Build(sign, Portal, 20f)), "the space truss on round legs is not the portal's on H legs");
        }

        private static void Layout()
        {
            Program.Near(SupportLayouts.SpaceTrussDepth(SupportLayouts.MinSpan), 1.2f, 1e-6f, "1.2 m deep at the shortest span");
            Program.Near(SupportLayouts.SpaceTrussDepth(SupportLayouts.PlanarGantryMax), 1.6f, 1e-6f, "22MR601's 1.6 m at 16 m");
            Program.Near(SupportLayouts.SpaceTrussDepth(11f), 1.4f, 1e-6f, "halfway at 11 m");
            Program.Near(SupportLayouts.SpaceTrussDepth(3f), 1.2f, 1e-6f, "not shallower than 1.2 m");
            Program.Near(SupportLayouts.SpaceTrussDepth(40f), 1.6f, 1e-6f, "not deeper than 1.6 m");
            float last = SupportLayouts.SpaceTrussDepth(SupportLayouts.MinSpan);
            for (float span = SupportLayouts.MinSpan; span <= 40f; span += 0.05f)
            {
                float depth = SupportLayouts.SpaceTrussDepth(span);
                Program.Check(depth >= last - 1e-6f && depth - last < 0.01f, "the depth is continuous and does not fall: " + span);
                last = depth;
            }

            SignDef board = SignCatalog.ById("gb.prohib.01");
            foreach (float span in new[] { 6f, 8f, 10f, 12f, 14f, 16f, 18f, 22f, 25f, 40f })
            {
                SupportLayout l = SupportLayouts.Of(board, Space, span);
                Program.Near(l.ArmLength, span, 1e-4f, span + ": the span asked for");
                Program.Check(l.BeamWidth > 0f && l.ChordYs.Length == 2 && l.RoundLegs, span + ": two planes of chords on round legs");
                Program.Near(l.BeamDepth, SupportLayouts.SpaceTrussDepth(span), 1e-6f, span + ": the depth of the rule");
                Program.Near(l.BeamWidth, span > 22f ? 0.75f : 0.5f, 1e-6f, span + ": 0.5 m from front to back, 0.75 m over 22 m (22MR601)");
                Program.Near(l.PoleRadius, SupportLayouts.SpaceLegRadius, 1e-6f, span + ": the legs' radius");
                Program.Near(l.ChordYs[1] - l.ChordYs[0], l.BeamDepth, 1e-5f, span + ": the chords are a depth apart");
                Program.Check(l.PoleTop > l.ChordYs[1] + l.ChordRadius, span + ": the legs rise over the beam");
                Program.Check(l.ChordYs[0] - l.ChordRadius >= 5.5f - 1e-3f, span + ": the beam is over the clearance");
                Program.Near(l.BoardCentreX, span * 0.5f, 1e-4f, span + ": the boards over the middle");
                Program.Check(SupportKinds.RootPaintable(SupportKind.Gantry, l), span + ": the round legs' roots can be painted");
            }

            // The portal gantry's layout is what it was: planar to 16 m, a space truss on H legs over it.
            SupportLayout p12 = SupportLayouts.Of(board, Portal, 12f), p20 = SupportLayouts.Of(board, Portal, 20f);
            Program.Check(p12.BeamWidth == 0f && p12.ChordYs.Length == 3 && !p12.RoundLegs && p12.BeamDepth == 2.0f, "the portal gantry of 12 m is planar");
            Program.Check(p20.BeamWidth == 0.5f && p20.ChordYs.Length == 2 && !p20.RoundLegs && p20.BeamDepth == 1.6f && p20.PoleRadius == SupportLayouts.GantryLegHalf, "the portal gantry of 20 m is a space truss on H legs");
            // A wide board still makes its span.
            SignDef wide = SignCatalog.ById("guide.exit.advance");
            SupportLayout w = SupportLayouts.Of(wide, Space, 6f);
            Program.Check(w.ArmLength >= wide.Width + 2f * Assembly.GantryClear - 1e-3f, "a wide board makes the span it needs: " + w.ArmLength);
        }

        private static void Choice()
        {
            SignDef guide = SignCatalog.ById("guide.exit.advance");
            Program.Near(SupportLayouts.SpaceGantryFrom, 10f, 0f, "over 10 m");
            Program.Check(!SupportLayouts.SpaceGantryFor(10f) && SupportLayouts.SpaceGantryFor(10.01f) && !SupportLayouts.SpaceGantryFor(6f) && SupportLayouts.SpaceGantryFor(40f), "the rule: over 10 m, not 10 m itself");
            Program.Check(SupportLayouts.SpaceGantryFor(0f), "a span not known is the default 14 m: a space truss");
            foreach ((int lanes, bool exit) in new[] { (3, false), (4, false), (1, true), (2, true) })
            {
                Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { LanesSameDirection = lanes, ExitLeftOrWide = exit, Span = 14f }).Id == SupportCatalog.GantrySpace, "the road asks for a gantry and its span is 14 m: the space truss (" + lanes + ", " + exit + ")");
                Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { LanesSameDirection = lanes, ExitLeftOrWide = exit, Span = 10f }).Id == SupportCatalog.Gantry, "10 m: the portal gantry (" + lanes + ", " + exit + ")");
                Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { LanesSameDirection = lanes, ExitLeftOrWide = exit, Span = 9f }).Id == SupportCatalog.Gantry, "9 m: the portal gantry");
            }

            Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { LanesSameDirection = 3 }).Id == SupportCatalog.GantrySpace, "no span known: the space truss");
            // No gantry where the road asks for none, whatever the span.
            Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { LanesSameDirection = 2, Span = 30f }).Kind != SupportKind.Gantry, "two lanes: not a gantry");
            Program.Check(SupportPlanner.AutoFor(guide, new SupportContext { Span = 30f }).Id == SupportPlanner.Auto(guide).Id, "no lanes known: the board's own support");
            // The board's own planner (Auto) is not changed: a board that hangs over the road has the portal gantry, as items without a stored support have always been drawn.
            SignDef screen = SignCatalog.ById("gb.vms.lane");
            Program.Check(SupportPlanner.Auto(screen).Id == SupportCatalog.Gantry, "a lane screen's own support is the portal gantry");
            Program.Check(SupportPlanner.AutoFor(screen, new SupportContext { LanesSameDirection = 3, Span = 30f }).Id == SupportCatalog.Gantry, "and the road's span does not change it");
            // With the plain profile the same answer; what the item stores is the hash of the form the road asked for (the tool saves it: it differs from the board's own).
            PlannedSupport plain = StyleState.Unstyled(guide, new SupportContext { LanesSameDirection = 3, Span = 14f }, default);
            Program.Check(plain.Support.Id == SupportCatalog.GantrySpace && plain.Plain.Id == SupportCatalog.GantrySpace, "the plain profile follows the planner");
            Program.Check(SupportCatalog.ById(SupportCatalog.GantrySpace).Id != SupportPlanner.Auto(guide).Id, "so the tool saves the hash");
        }

        private static void Catalogue()
        {
            SupportDef space = Space;
            Program.Check(space != null && space.Kind == SupportKind.Gantry && space.SpaceTruss && space.Clearance == 5.5f && space.ArmLength == 14f && !space.Hidden, "the entry: a gantry of 5.5 m clearance and 14 m by default");
            Program.Check(space.Zh == "门架（空间桁架，前后双层）" && space.En == "Gantry, space truss", "its names");
            Program.Check(space.Family == SupportFamilies.Gantry && SupportCatalog.Listed.Contains(space), "it is a variant of the gantry family and listed");
            Program.Check(SupportCatalog.ByHash(space.Hash) == space && space.Hash == SignCatalog.HashOf("gantry.space") && space.Hash != Portal.Hash, "its hash is the id's and is its own");
            var seen = new HashSet<uint>();
            foreach (SupportDef def in SupportCatalog.All)
                Program.Check(seen.Add(def.Hash), "the hash of " + def.Id + " is its own");
            Program.Check(!Portal.SpaceTruss && Portal.Id == "gantry.portal" && Portal.Clearance == 5.5f && Portal.ArmLength == 14f, "the portal gantry is as it was");
        }

        private static SignParts Parts(SupportDef support, float span, SupportDetail detail, params string[] boards)
        {
            var list = new List<AssemblyBoard>();
            for (int i = 0; i < boards.Length; i++)
                list.Add(new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById(boards[i]), null), Slot = i, Member = i, Lane = i + 1, OffsetX = span * (i + 1) / (boards.Length + 1) });
            return SignBuilder.BuildAssembly(list, support, span, detail);
        }

        private static void Built()
        {
            foreach (float span in new[] { 6f, 9f, 10f, 14f, 16f, 20f, 24f, 40f })
            {
                foreach (SupportDetail detail in new[] { SupportDetail.Full, SupportDetail.Reduced })
                {
                    string what = "space truss " + span + " m " + detail;
                    SignParts parts = Parts(Space, span, detail, "gb.prohib.01");
                    string problem = MeshProblem(parts.Metal) ?? MeshProblem(parts.Concrete);
                    Program.Check(problem == null, what + ": " + problem);
                    SupportLayout l = parts.Layout;
                    parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                    Program.Check(fMin.Z >= l.ChordRadius - 1e-4f, what + ": the board is in front of the truss's front chords: " + fMin.Z + " from " + l.ChordRadius);
                    // No steel in front of the board's face, across its outline.
                    int front = 0;
                    for (int i = 0; i < parts.Metal.VertexCount; i++)
                    {
                        Vec3 p = parts.Metal.Positions[i];
                        if (p.X > fMin.X && p.X < fMax.X && p.Y > fMin.Y && p.Y < fMax.Y && p.Z > fMax.Z + 1e-4f)
                            front++;
                    }

                    Program.Check(front == 0, what + ": " + front + " steel vertices in front of the board");
                    // The posts stand on the ground and the beam is where the layout says.
                    parts.Metal.Bounds(out Vec3 mMin, out Vec3 mMax);
                    Program.Check(mMin.Y > -1f && mMax.Y >= l.PoleTop - 1e-3f && mMax.Y <= l.PoleTop + 0.05f, what + ": from the ground to the legs' tops: " + mMin.Y + " to " + mMax.Y);
                    Program.Check(mMin.Z <= -l.BeamWidth - l.PoleRadius + 1e-3f, what + ": the back legs: " + mMin.Z);
                }

                SignParts full = Parts(Space, span, SupportDetail.Full, "gb.prohib.01"), reduced = Parts(Space, span, SupportDetail.Reduced, "gb.prohib.01");
                Program.Check(reduced.VertexCount < full.VertexCount, "space truss " + span + " m: the reduced build is lighter: " + reduced.VertexCount + " against " + full.VertexCount);
            }

            // Boards of every size on it: the boards between the legs, their lower edges at the clearance.
            foreach (string id in new[] { "gb.prohib.01", "gb.warn.01", "guide.exit.advance" })
            {
                SignParts parts = Parts(Space, 14f, SupportDetail.Full, id);
                parts.Face.Bounds(out Vec3 fMin, out Vec3 fMax);
                SupportLayout l = parts.Layout;
                Program.Check(fMin.X > l.PoleX[0] + l.PoleRadius && fMax.X < l.PoleX[1] - l.PoleRadius, id + ": the board hangs between the legs");
                Program.Check(fMin.Y >= l.BottomClearance - 1e-3f, id + ": the board's lower edge is at the clearance: " + fMin.Y);
            }

            // Two boards, one on the back.
            SignParts two = SignBuilder.BuildAssembly(new List<AssemblyBoard>
            {
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), Lane = 1, OffsetX = 4f, Slot = 0, Member = 0 },
                new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), Lane = 2, OffsetX = 9f, Slot = 1, Member = 1, Back = true }
            }, Space, 14f);
            Program.Check(two.Faces.Count == 2 && MeshProblem(two.Metal) == null, "two boards, one on the back: " + MeshProblem(two.Metal));
        }

        private static void Legs()
        {
            SignParts parts = Parts(Space, 14f, SupportDetail.Full, "gb.prohib.01");
            SupportLayout l = parts.Layout;
            Program.Check(parts.Feet.Count == 2 && parts.Feet[0].X == 0f && Math.Abs(parts.Feet[1].X - 14f) < 1e-4f, "a foot at each end");
            foreach (PostFoot foot in parts.Feet)
            {
                Program.Check(foot.MetalTo > foot.MetalFrom && foot.TubeTo > foot.TubeFrom && foot.ConcreteTo > foot.ConcreteFrom, "the foot has its plates, its tubes and its pedestal tagged");
                Program.Near(foot.Z, -l.BeamWidth * 0.5f, 1e-5f, "a pedestal under both legs");
            }

            // Each leg is a round tube of the layout's radius, in the front and the back plane.
            foreach (float z in new[] { 0f, -l.BeamWidth })
            {
                float widest = 0f;
                for (int i = parts.Feet[0].TubeFrom; i < parts.Feet[0].TubeTo; i++)
                {
                    Vec3 p = parts.Metal.Positions[i];
                    if (Math.Abs(p.Z - z) < l.PoleRadius + 1e-3f && Math.Abs(p.X) < l.PoleRadius + 1e-3f)
                        widest = Math.Max(widest, (float)Math.Sqrt(p.X * p.X + (p.Z - z) * (p.Z - z)));
                }

                Program.Near(widest, l.PoleRadius, 1e-4f, "the leg at z = " + z + " is a tube of its radius");
                float lowest = float.MaxValue;
                for (int i = parts.Feet[0].TubeFrom; i < parts.Feet[0].TubeTo; i++)
                {
                    Vec3 p = parts.Metal.Positions[i];
                    if (Math.Abs(p.Z - z) < l.PoleRadius + 1e-3f && Math.Abs(p.X) < l.PoleRadius + 1e-3f)
                        lowest = Math.Min(lowest, p.Y);
                }

                Program.Near(lowest, l.Base.Thick + l.Base.LowerThick + 0.01f, 1e-4f, "the leg at z = " + z + " stands on its plate");
            }

            // Each leg is capped at its top (a disc of the cap's sixteen sides and its middle facing up), and stands on the plate of a round tube of its size, not an H leg's.
            foreach (float px in l.PoleX)
            {
                foreach (float z in new[] { 0f, -l.BeamWidth })
                {
                    int cap = 0;
                    for (int i = 0; i < parts.Metal.VertexCount; i++)
                    {
                        Vec3 p = parts.Metal.Positions[i];
                        if (Math.Abs(p.Y - (l.PoleTop + 0.005f)) < 1e-4f && parts.Metal.Normals[i].Y > 0.99f && Math.Abs(p.X - px) < l.PoleRadius + 0.01f && Math.Abs(p.Z - z) < l.PoleRadius + 0.01f)
                            cap++;
                    }

                    Program.Check(cap >= 16, "the leg at x = " + px + ", z = " + z + " has its cap: " + cap + " vertices facing up");
                }
            }

            BasePlate round = BasePlate.ArmPost(SupportLayouts.SpaceLegRadius), hLeg = BasePlate.GantryLeg();
            Program.Check(l.Base.HalfX == round.HalfX && l.Base.HalfZ == round.HalfZ && l.Base.BoltX.Length == round.BoltX.Length, "the plate under a round leg is the cantilever's for a tube of its size");
            Program.Check(l.Base.HalfX != hLeg.HalfX || l.Base.BoltX.Length != hLeg.BoltX.Length, "not the H leg's");
            Program.Check(2f * l.Base.HalfZ <= l.BeamWidth + 0.5f && l.Base.HalfZ + l.Base.HalfZ >= 0.2f, "and the two plates of a pair do not run into one another: " + l.Base.HalfZ + " across 0.5 m");
            // The lacing: tubes from the front leg to the back one, between the legs' faces, at each end.
            int lacing = 0;
            for (int i = 0; i < parts.Metal.VertexCount; i++)
            {
                Vec3 p = parts.Metal.Positions[i];
                if (Math.Abs(p.X) < 1e-4f && p.Z < -l.PoleRadius - 1e-3f && p.Z > -l.BeamWidth + l.PoleRadius + 1e-3f && p.Y > 0.5f && p.Y < l.PoleTop - 0.4f)
                    lacing++;
            }

            Program.Check(lacing > 0, "the lacing between the legs: " + lacing);
            // The root paint goes round each leg, at each end: a sleeve on every one of the four tubes, moving with its foot.
            var ring = new List<AssemblyBoard> { new AssemblyBoard { Instance = SignInstances.Of(SignCatalog.ById("gb.prohib.01"), null), Lane = 1, OffsetX = 7f } };
            SignParts painted = SignBuilder.BuildAssembly(ring, Space, 14f, SupportDetail.Full, new SupportStyle { Root = RootPaint.BlueRings });
            MeshBuilder blue = painted.Solids[(int)SolidColour.Blue];
            Program.Check(blue != null && blue.VertexCount > 0, "the blue rings are there");
            foreach (PostFoot foot in painted.Feet)
            {
                foreach (float z in new[] { 0f, -l.BeamWidth })
                {
                    bool found = false;
                    foreach ((SolidColour colour, int from, int to) in foot.Paint)
                    {
                        for (int i = from; i < to && !found; i++)
                        {
                            Vec3 p = painted.Solids[(int)colour].Positions[i];
                            found = Math.Abs(p.Z - z) < l.PoleRadius + 0.02f && Math.Abs(p.X - foot.X) < l.PoleRadius + 0.02f;
                        }
                    }

                    Program.Check(found, "paint round the leg at x = " + foot.X + ", z = " + z);
                }
            }

            // The splice flange of the portal's space truss stands through a board hung over mid-span; this gantry has none: a board over the middle has no steel in front of it.
            Program.Check(!HasSplice(parts, l), "no splice flange of 0.3 m across at mid-span");
            // The stack of small boards stands on the first leg only, as the portal's does.
            Program.Check(SupportKinds.PostBoards(SupportKind.Gantry) && l.PostTop > l.PostClearance, "the first leg takes small boards: room " + (l.PostTop - l.PostClearance));
        }

        private static bool HasSplice(SignParts parts, SupportLayout l)
        {
            for (int i = 0; i < parts.Metal.VertexCount; i++)
            {
                Vec3 p = parts.Metal.Positions[i];
                if (Math.Abs(p.X - l.ArmLength * 0.5f) < 0.03f && Math.Abs(p.Y - l.ChordYs[0]) > 0.12f && Math.Abs(p.Y - l.ChordYs[0]) < 0.16f && Math.Abs(p.Z) < 0.16f)
                    return true;
            }

            return false;
        }

        /// <summary>Why a mesh is not sound: a coordinate or a normal that is not finite, a normal that is not a unit, an index out of range, a triangle with no area; null when it is.</summary>
        internal static string MeshProblem(MeshBuilder mesh)
        {
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Vec3 p = mesh.Positions[i], n = mesh.Normals[i];
                if (!p.IsFinite || !n.IsFinite)
                    return "a vertex that is not finite: " + i;
                if (Math.Abs(n.Length - 1f) > 1e-3f)
                    return "a normal that is not a unit: " + n.Length;
            }

            if (mesh.Indices.Count % 3 != 0)
                return "indices not in threes";
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
                if (a < 0 || b < 0 || c < 0 || a >= mesh.VertexCount || b >= mesh.VertexCount || c >= mesh.VertexCount)
                    return "an index out of range";
                Vec3 area = MeshBuilder.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                if (area.Length < 1e-9f)
                    return "a triangle with no area at " + t / 3;
            }

            return null;
        }
    }
}
