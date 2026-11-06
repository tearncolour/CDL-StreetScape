using System;
using System.Collections.Generic;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// 0.12.57 (the owner: "涂装应该支持根部不同颜色油漆"): the paint on a post's root. 立面标记 (GB 51038-2015 14.6.2): yellow and black stripes at 45°, 15 cm wide and apart, from the base to 2.5 m,
    /// sloping down toward the carriageway; the blue root with rings of the Qidong drawing 交施-07: blue to 1.0 m, white and blue rings of 0.1 m to 1.6 m. A sleeve round the post, outside it and
    /// inside its clamps, kept in the style byte with the finish, moved with the foot on the ground. The colours are read as seen from the front, so the triangles' layout does not matter.
    /// </summary>
    internal static class RootPaintTests
    {
        public static void RunAll()
        {
            Program.Run("root paint: kept in the style byte with the finish and the backs", Packing);
            Program.Run("root paint: 立面标记 stripes round the post to 2.5 m, sloping down to the carriageway", Marking);
            Program.Run("root paint: the blue root and its rings", Rings);
            Program.Run("root paint: moved with the foot on the ground", Ground);
        }

        private static readonly SolidColour[] Paints = { SolidColour.Yellow, SolidColour.Black, SolidColour.Blue, SolidColour.White };

        private static ItemRecord Item(string id, SupportStyle style, string support = null)
        {
            uint hash = support == null ? 0u : SupportCatalog.ById(support).Hash;
            var record = new ItemRecord(SignCatalog.ById(id).Hash, new Vec3(0f, 0f, 0f), 0f, hash);
            record.Style = style.Packed;
            return record;
        }

        private static void Packing()
        {
            bool all = true;
            for (int finish = 0; finish < 4; finish++)
            {
                for (int root = 0; root < 3; root++)
                {
                    foreach (bool back in new[] { false, true })
                    {
                        var style = new SupportStyle { Finish = (SupportFinish)finish, BlackBack = back, Root = (RootPaint)root };
                        SupportStyle again = SupportStyle.Of(style.Packed);
                        all &= again.Finish == style.Finish && again.BlackBack == back && again.Root == style.Root;
                    }
                }
            }

            Program.Check(all, "every finish, back and root paint read back from its byte");
            bool old = true;
            for (int b = 0; b < 8; b++)
                old &= SupportStyle.Of((byte)b).Root == RootPaint.None;
            Program.Check(old, "a byte with only the finish and the backs (bits 3 and 4 clear): no root paint");
            SignParts plain = ChunkBuilder.PartsOfRecord(Item("gb.stop", default));
            Program.Check(plain.Solids[(int)SolidColour.Yellow] == null && plain.Solids[(int)SolidColour.Blue] == null && plain.Feet.TrueForAll(f => f.Paint.Count == 0), "no root paint: no sleeve");
        }

        /// <summary>The paint seen from the front (+Z, the traffic's side) at (x, y): the nearest painted triangle there with its z ahead of the post's axis; null where none is.</summary>
        private static SolidColour? FrontColour(SignParts parts, float x, float y)
        {
            SolidColour? seen = null;
            float nearest = 0f;
            foreach (SolidColour colour in Paints)
            {
                MeshBuilder mesh = parts.Solids[(int)colour];
                if (mesh == null)
                    continue;
                for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
                {
                    Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                    float d = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
                    if (Math.Abs(d) < 1e-12f)
                        continue;
                    float wa = ((b.Y - c.Y) * (x - c.X) + (c.X - b.X) * (y - c.Y)) / d, wb = ((c.Y - a.Y) * (x - c.X) + (a.X - c.X) * (y - c.Y)) / d, wc = 1f - wa - wb;
                    if (wa < 0f || wb < 0f || wc < 0f)
                        continue;
                    float z = wa * a.Z + wb * b.Z + wc * c.Z;
                    if (z > nearest)
                    {
                        nearest = z;
                        seen = colour;
                    }
                }
            }

            return seen;
        }

        /// <summary>The heights (every 1 mm from y0 to y1) where the paint seen from the front at x changes, with the colour above each change.</summary>
        private static List<(float Y, SolidColour? Above)> Changes(SignParts parts, float x, float y0, float y1)
        {
            var changes = new List<(float Y, SolidColour? Above)>();
            SolidColour? last = FrontColour(parts, x, y0);
            for (float y = y0 + 0.001f; y <= y1; y += 0.001f)
            {
                SolidColour? now = FrontColour(parts, x, y);
                if (now != last)
                    changes.Add((y, now));
                last = now;
            }

            return changes;
        }

        /// <summary>How far down the stripes go from x0 to x1 on the front (positive: lower at x1), from their changes of colour there between 0.5 and 2.0 m.</summary>
        private static float Drop(SignParts parts, float x0, float x1, out List<(float Y, SolidColour? Above)> at0)
        {
            at0 = Changes(parts, x0, 0.5f, 2.0f);
            List<(float Y, SolidColour? Above)> at1 = Changes(parts, x1, 0.4f, 2.1f);
            (float Y, SolidColour? Above) first = at0.Count > 0 ? at0[0] : (0f, null);
            float best = float.NaN;
            foreach ((float y, SolidColour? above) in at1)
            {
                if (above == first.Above && (float.IsNaN(best) || Math.Abs(first.Y - y) < Math.Abs(best)))
                    best = first.Y - y;
            }

            return best;
        }

        private static void Marking()
        {
            var marking = new SupportStyle { Root = RootPaint.Marking };
            SignParts parts = ChunkBuilder.PartsOfRecord(Item("gb.stop", marking));
            MeshBuilder yellow = parts.Solids[(int)SolidColour.Yellow], black = parts.Solids[(int)SolidColour.Black];
            Program.Check(yellow != null && black != null && yellow.VertexCount > 0 && black.VertexCount > 0, "yellow and black solids");
            if (yellow == null || black == null)
                return;
            float r = parts.Layout.PoleRadius, px = parts.Layout.PoleX[0];
            yellow.Bounds(out Vec3 ylo, out Vec3 yhi);
            black.Bounds(out Vec3 blo, out Vec3 bhi);
            float lo = Math.Min(ylo.Y, blo.Y), hi = Math.Max(yhi.Y, bhi.Y);
            Program.Check(lo > 0.01f && lo < 0.1f && Math.Abs(hi - SignBuilder.MarkingTop) < 1e-3f, $"from the base plate's top to 2.5 m: {lo:0.000} .. {hi:0.000}");
            // Each vertex on a facet parallel to the post's (12 sides, a vertex at the front) 2 mm further out: outside the post, inside its clamps (5 mm).
            bool round = true;
            foreach (MeshBuilder mesh in new[] { yellow, black })
            {
                foreach (Vec3 p in mesh.Positions)
                {
                    double angle = Math.Atan2(p.X - px, p.Z), step = 2.0 * Math.PI / 12;
                    double centre = -Math.PI + (Math.Min(11, Math.Floor((angle + Math.PI) / step)) + 0.5) * step;
                    float out_ = (float)((p.X - px) * Math.Sin(centre) + p.Z * Math.Cos(centre)), facet = r * (float)Math.Cos(Math.PI / 12);
                    round &= Math.Abs(out_ - facet - 0.002f * (float)Math.Cos(Math.PI / 12)) < 1e-4f;
                }
            }

            Program.Check(round, "the sleeve's facets 2 mm outside the post's, inside its clamps (5 mm)");
            float ya = MeshArea(yellow), ba = MeshArea(black), sleeve = 12f * 2f * (r + 0.002f) * (float)Math.Sin(Math.PI / 12) * (hi - lo);
            Program.Check(Math.Abs(ya + ba - sleeve) < sleeve * 1e-3f && Math.Abs(ya - ba) < sleeve * 0.1f, $"the sleeve whole, half yellow and half black: {ya:0.000} + {ba:0.000} m² of {sleeve:0.000}");

            // Up a line on the front each colour is 0.15 m × √2 tall (15 cm across stripes at 45°), yellow and black in turn.
            List<(float Y, SolidColour? Above)> changes = Changes(parts, px + 0.3f * r, 0.5f, 2.0f);
            bool pitch = changes.Count >= 6;
            for (int i = 1; i < changes.Count; i++)
                pitch &= Math.Abs(changes[i].Y - changes[i - 1].Y - SignBuilder.MarkingStripe * (float)Math.Sqrt(2.0)) < 0.003f && changes[i].Above != changes[i - 1].Above;
            Program.Check(pitch && changes.TrueForAll(c => c.Above == SolidColour.Yellow || c.Above == SolidColour.Black), $"stripes 15 cm across, yellow and black in turn: {string.Join(", ", changes.ConvertAll(c => c.Y.ToString("0.000") + c.Above))}");

            // 45° down toward +X (the driver's left: the carriageway beside a post on the right of the road): lower by the way along the facet (the front's facets, from its vertex at +Z, 15° off it).
            float along = 0.25f * r / (float)Math.Cos(Math.PI / 12);
            float right = Drop(parts, px + 0.05f * r, px + 0.3f * r, out _), left = Drop(parts, px - 0.3f * r, px - 0.05f * r, out _);
            Program.Check(Math.Abs(right - along) < 0.003f && Math.Abs(left - along) < 0.003f, $"down toward +X on both halves of the front, at 45°: {right:0.0000} and {left:0.0000} for {along:0.0000}");

            SignParts mirrored = ChunkBuilder.PartsOfGroup(new[] { Item("gb.stop", marking, SupportCatalog.CantileverRight) });
            float rm = mirrored.Layout.PoleRadius, mirroredDrop = Drop(mirrored, 0.05f * rm, 0.3f * rm, out _);
            Program.Check(Math.Abs(mirroredDrop + 0.25f * rm / (float)Math.Cos(Math.PI / 16)) < 0.003f, $"a cantilever reaching to -X: down toward -X ({mirroredDrop:0.0000})");
            SignParts twin = ChunkBuilder.PartsOfGroup(new[] { Item("gb.stop", marking, SupportCatalog.DoubleCantilever) });
            float rt = twin.Layout.PoleRadius, outRight = Drop(twin, 0.05f * rt, 0.3f * rt, out _), outLeft = Drop(twin, -0.05f * rt, -0.3f * rt, out _);
            Program.Check(outRight > 0.003f && outLeft > 0.003f, $"a double cantilever (carriageways either side): down to either side from the front's middle ({outRight:0.0000}, {outLeft:0.0000})");
        }

        private static float MeshArea(MeshBuilder mesh)
        {
            float area = 0f;
            for (int t = 0; t + 2 < mesh.Indices.Count; t += 3)
            {
                Vec3 a = mesh.Positions[mesh.Indices[t]], b = mesh.Positions[mesh.Indices[t + 1]], c = mesh.Positions[mesh.Indices[t + 2]];
                area += MeshBuilder.Cross(b - a, c - a).Length * 0.5f;
            }

            return area;
        }

        private static void Rings()
        {
            SignParts parts = ChunkBuilder.PartsOfRecord(Item("gb.stop", new SupportStyle { Root = RootPaint.BlueRings, Finish = SupportFinish.LightGrey }));
            Program.Check(parts.Solids[(int)SolidColour.Blue] != null && parts.Solids[(int)SolidColour.White] != null, "blue and white solids");
            float px = parts.Layout.PoleX[0];
            List<(float Y, SolidColour? Above)> changes = Changes(parts, px, 0.2f, 1.8f);
            string seen = string.Join(", ", changes.ConvertAll(c => c.Y.ToString("0.000") + " " + (c.Above?.ToString() ?? "steel")));
            bool rings = changes.Count == 7;
            for (int i = 0; rings && i < 7; i++)
                rings &= Math.Abs(changes[i].Y - (1.0f + 0.1f * i)) < 0.0015f && changes[i].Above == (i == 6 ? (SolidColour?)null : i % 2 == 0 ? SolidColour.White : SolidColour.Blue);
            Program.Check(rings && FrontColour(parts, px, 0.5f) == SolidColour.Blue, $"blue to 1.0 m, then white and blue rings of 0.1 m to 1.6 m, the steel above: {seen}");
            Program.Check(parts.MetalColour == SolidColour.PoleLightGrey, "the post above in its finish");
            // Level all the way round (0.12.58: 0.12.57 cut them at 45° like the 立面标记's stripes): the heights of the changes are the same across the front, on every post and on a faceted one.
            foreach ((string sign, string support) in new[] { ("gb.stop", "pole.single"), ("gb17733.street", "cantilever.single"), ("gb17733.street", "cantilever.octa"), ("gb17733.street", "integrated.sh"), ("gb17733.street", "plate.sz"), ("gb17733.street", "plate.hz") })
            {
                var record = new ItemRecord(SignCatalog.ById(sign).Hash, new Vec3(0f, 0f, 0f), 0f, SupportCatalog.ById(support).Hash, null, 6f);
                record.Style = new SupportStyle { Root = RootPaint.BlueRings }.Packed;
                SignParts built = ChunkBuilder.PartsOfGroup(new[] { record });
                float x = built.Layout.PoleX[0], r = built.Layout.PoleRadius;
                bool level = true;
                // Places across the post off the edges between its flats (a 24-, 16-, 12- or 8-sided post has them at sines of 7.5°, 22.5°... of its radius).
                foreach (float k in new[] { -0.83f, -0.5f, 0.07f, 0.46f, 0.84f })
                {
                    List<(float Y, SolidColour? Above)> across = Changes(built, x + k * r, 0.5f, 1.8f);
                    level &= across.Count == 7 && across.TrueForAll(c => Math.Abs(((c.Y - 1.0f) / 0.1f) - Math.Round((c.Y - 1.0f) / 0.1f)) < 0.04);
                }

                Program.Check(level, $"{support}: the rings level across the post (every 0.1 m from 1.0 m at five places across it)");
            }
        }

        private static void Ground()
        {
            ItemRecord flat = Item("gb.stop", new SupportStyle { Root = RootPaint.Marking }), raised = flat;
            ChunkBuilder.PartsOfRecord(flat).FootTags(out _, out _, out int[][] solids);
            Program.Check(solids[(int)SolidColour.Yellow] != null && Array.TrueForAll(solids[(int)SolidColour.Yellow], t => t == 0) && Array.TrueForAll(solids[(int)SolidColour.Black], t => t == 0), "every vertex of the paint is the foot's");
            raised.Ground = GroundProfile.Sample((x, z) => 0.2f, -3f, 3f);
            float Lowest(ItemRecord item, bool highest)
            {
                float low = highest ? float.MinValue : float.MaxValue;
                foreach (ChunkPart part in ChunkBuilder.Build(new[] { item }, Vec3.Zero, out _))
                {
                    if (part.Material != ChunkBuilder.SolidMaterial(SolidColour.Yellow) && part.Material != ChunkBuilder.SolidMaterial(SolidColour.Black))
                        continue;
                    foreach (Vec3 p in part.Mesh.Positions)
                        low = highest ? Math.Max(low, p.Y) : Math.Min(low, p.Y);
                }

                return low;
            }

            float a = Lowest(flat, false), b = Lowest(raised, false);
            Program.Check(Math.Abs(b - a - 0.2f) < 1e-3f, $"the paint up with the foot on ground 0.2 m higher: {a:0.000} -> {b:0.000}");
            float postTop = ChunkBuilder.PartsOfRecord(flat).Layout.PoleTop;
            ItemRecord high = flat;
            high.Ground = GroundProfile.Sample((x, z) => 1.4f, -3f, 3f);
            float flatTop = Lowest(flat, true), highTop = Lowest(high, true);
            Program.Check(Math.Abs(flatTop - SignBuilder.MarkingTop) < 1e-3f && highTop <= postTop - 0.01f + 1e-4f && highTop > SignBuilder.MarkingTop, $"on a foot raised 1.4 m, no higher than 1 cm under the post's top ({postTop:0.000}): {flatTop:0.000} -> {highTop:0.000}");
        }
    }
}
