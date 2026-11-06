using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    internal static class StandardComplianceTests
    {
        public static void RunAll()
        {
            Program.Run("GB table 8: warning liner, border and colour amendment", Warning);
            Program.Run("GB figures 33/60: actual angles, paint widths and text clearance", Zigzags);
            Program.Run("GB figures 26/27/85: complete parking symbols and clipped grid", ParkingAndGrid);
            Program.Run("GB figures 101/104/107: full-size rumble pieces", RumbleDimensions);
            Program.Run("GB figure 105: curved and split rumble runs retain taper and phase", RumbleRuns);
        }

        private static Vec3 P(float x, float z) => new Vec3(x, 0f, z);
        private static float Area(MeshBuilder mesh)
        {
            float area = 0f;
            for (int i = 0; i < mesh.Indices.Count; i += 3)
                area += MeshBuilder.Cross(mesh.Positions[mesh.Indices[i + 1]] - mesh.Positions[mesh.Indices[i]], mesh.Positions[mesh.Indices[i + 2]] - mesh.Positions[mesh.Indices[i]]).Length * 0.5f;
            return area;
        }

        private static void Warning()
        {
            float[] liners = { .010f, .008f, .006f, .004f }, borders = { .09f, .08f, .065f, .05f };
            for (int band = 0; band < 4; band++)
            {
                GuidePart p = SignStyles.Warning(band, null).Parts[0];
                float borderBottom = p.Elements[0].Points.Where((v, i) => i % 2 == 1).Max();
                float fieldBottom = p.Elements[1].Points.Where((v, i) => i % 2 == 1).Max();
                Program.Check(p.Ground == GuideColour.Yellow && p.Elements[0].Colour == GuideColour.Black && p.Elements[1].Colour == GuideColour.Yellow, "three nested colour regions");
                Program.Near(p.H - borderBottom, liners[band], 1e-5f, "table 8 liner measured at base");
                Program.Near(borderBottom - fieldBottom, borders[band], 1e-5f, "table 8 black border measured at base");
            }
            var choice = SignCatalog.Choices.First(c => c.Id == "gb.warn.37");
            Program.Check(SignInstances.Of(choice.Def, choice.Spec).Board.Parts[0].Ground == GuideColour.FluorescentFireRed, "amended fire-red colour class");
            GuideColours.Rgb(GuideColour.Red, out byte r, out byte g, out byte b);
            Program.Check(r == 214 && g == 28 && b == 36, "existing prohibition red unchanged");
        }

        private static void AngleAndWidth(MeshBuilder m, float width, string context)
        {
            Vec3 a = (m.Positions[0] + m.Positions[1]) * .5f, b = (m.Positions[2] + m.Positions[3]) * .5f, c = (m.Positions[4] + m.Positions[5]) * .5f;
            float angle = (float)(Math.Acos(Vec3.Dot((a - b).HeadingXZ(), (c - b).HeadingXZ())) * 180 / Math.PI);
            Program.Near(angle, 60f, 1e-3f, context + " actual interior angle");
            Vec3 normal = (b - a).HeadingXZ().RightXZ();
            Program.Near(Math.Abs(Vec3.Dot(m.Positions[3] - m.Positions[2], normal)), width, 1e-5f, context + " perpendicular width at mitre");
        }

        private static void Zigzags()
        {
            foreach (float height in new[] { .5f, 1f, 3f })
                AngleAndWidth(MarkingLines.Zigzag(20f, height).Mesh, .2f, "line 41");
            foreach (float depth in new[] { 2.5f, 3f, 5f })
            foreach (float requested in new[] { 12f, 20f, 30f, 60f })
            foreach (string word in new[] { "校车", "公交车", "快速公交" })
            foreach (bool bay in new[] { false, true })
            {
                List<MarkingShape> shapes = bay ? MarkingFigures.BusBay(BusBayForm.C, 25f, requested, 25f, depth, word) : MarkingFigures.KerbStop(requested, depth, word);
                AngleAndWidth(shapes[1].Mesh, .2f, "stop");
                shapes[1].Mesh.Bounds(out Vec3 min, out Vec3 max);
                Program.Check(min.X >= (bay ? .45f : .2f) - 1e-4f && max.X <= depth - .1f + 1e-4f, "zigzag mitres stay inside the stop");
                foreach (MarkingText text in shapes[1].Texts)
                foreach (MarkingShape paint in shapes)
                {
                    var intersection = new MeshBuilder();
                    MarkingFills.ClipConvex(intersection, MarkingFills.Rectangle(text.X - text.Width * .5f, text.Z - text.Length * .5f, text.X + text.Width * .5f, text.Z + text.Length * .5f), paint.Mesh);
                    Program.Near(Area(intersection), 0f, 1e-6f, "no paint inside any text box: " + word);
                }
            }
        }

        private static void ParkingAndGrid()
        {
            GuideTemplate disabled = MarkingTemplates.ById("mk.bay.disabled");
            MarkingPlan p = disabled.Marking(disabled.Parse(""));
            Program.Check(p.Shapes.Count == 4 && p.Shapes[2].Mesh.TriangleCount > 20 && p.Shapes[3].Mesh.TriangleCount > 20, "both disabled bays contain wheelchair geometry");
            foreach (float size in new[] { 1f, 2f, 6f })
            {
                GuideTemplate bicycle = MarkingTemplates.ById("mk.bay.bicycle");
                MarkingPlan b = bicycle.Marking(bicycle.Parse("width=" + size + ";len=" + size));
                b.Shapes[1].Mesh.Bounds(out Vec3 min, out Vec3 max);
                Program.Check(min.X > -size * .5f + .1f && max.X < size * .5f - .1f && min.Z > .1f && max.Z < size - .1f, "bicycle symbol clear of frame at " + size);
            }
            foreach (float width in new[] { 3f, 5f, 12f })
            foreach (float line in new[] { .4f, .45f })
            {
                var grid = MarkingFigures.SimpleGrid(width, 8f, line);
                grid.Mesh.Bounds(out Vec3 min, out Vec3 max);
                Program.Near(min.X, -width / 2f, 1e-5f, "grid left bound");
                Program.Near(max.X, width / 2f, 1e-5f, "grid right bound");
                Program.Near(min.Z, 0f, 1e-5f, "grid near bound");
                Program.Near(max.Z, 8f, 1e-5f, "grid far bound");
            }
        }

        private static void RumbleDimensions()
        {
            foreach (float road in new[] { 2f, 3f, 3.5f, 3.75f, 8f })
            foreach (bool toll in new[] { false, true })
            {
                MeshBuilder m = (toll ? MarkingWarnings.TollPlazaGroup(road, 2) : MarkingWarnings.TransverseRumbleGroup(road, 2)).Mesh;
                for (int i = 0; i < m.VertexCount; i += 4)
                {
                    var quad = m.Positions.Skip(i).Take(4).ToArray();
                    Program.Near(quad.Max(v => v.X) - quad.Min(v => v.X), toll ? .5f : 1f, 1e-5f, "every transverse piece has its full standard length");
                    Program.Near(quad.Max(v => v.Z) - quad.Min(v => v.Z), .45f, 1e-5f, "every transverse piece is 45 cm thick");
                }
            }
            foreach (bool right in new[] { false, true })
            {
                var m = MarkingWarnings.LongitudinalRumble(20f, false, right).Mesh;
                float x = right ? .05f : -.05f;
                for (int block = 0; block < 10; block++)
                {
                    var edge = m.Positions.Where(p => Math.Abs(p.X - x) < 1e-5f && p.Z >= block * 2f - 1e-5f && p.Z < block * 2f + 1.5f).ToArray();
                    Program.Near(edge.Max(p => p.Z) - edge.Min(p => p.Z), 1f, 1e-5f, "Fig. 104 parallel edge, not total bounds");
                }
                Program.Near(Area(m), 3f, 1e-4f, "ten 0.3 square metre blocks");
            }
        }

        private static void RumbleRuns()
        {
            MarkingShape whole = MarkingWarnings.LongitudinalRumble(40f, true);
            MarkingShape first = MarkingWarnings.LongitudinalRumble(15.5f, true, true, 0f, 40f);
            MarkingShape second = MarkingWarnings.LongitudinalRumble(24.5f, true, true, 15.5f, 40f);
            Program.Near(whole.Area, first.Area + second.Area, 1e-4f, "splitting through a block preserves total paint and taper");
            foreach (float station in new[] { 0f, 10f, 20f, 30f })
            {
                var one = MarkingWarnings.LongitudinalRumble(1.5f, true, true, station, 40f);
                one.Mesh.Bounds(out Vec3 min, out Vec3 max);
                Program.Near(max.X - min.X, .1f + .2f * station / 30f, 1e-5f, "global 30 m taper width at " + station);
            }
            var path = new List<Vec3> { P(0f, 0f), P(10f, 0f), P(20f, 2f) };
            var curve = MarkingWarnings.LongitudinalRumble(path, true, true, 0f, 0f);
            curve.Mesh.Bounds(out Vec3 cmin, out Vec3 cmax);
            Program.Check(cmax.X > 18f && cmax.Z < 2.1f, "follows actual curve instead of a straight +Z run");
            var template = MarkingTemplates.ById("mk.rumble.longitudinal");
            string spec = MarkingTemplates.WithPath(template, "", RunPath.Encode(path, out _));
            MarkingPlan placed = template.Marking(template.Parse(spec));
            placed.Shapes[0].Mesh.Bounds(out Vec3 placedMin, out Vec3 placedMax);
            Program.Check(placedMin.X < -1f && placedMax.Z > 18f, "catalogue uses the path-aware geometry in the encoded local frame");
        }
    }
}
