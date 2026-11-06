using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The things that stand on a toll plaza's road (0.12.26; the owner: "加入高速收费站路标等等…还有常见的路面摆件"): the lifting-arm gate (道闸、栏杆机), the toll booth (收费亭), the raised toll island
    /// (收费岛) and the rubber speed hump (减速带) that crosses the lanes before it. Each is put where the cursor is, as a stamp, in the item's frame: +Z along the traffic, +X to its right, the ground at
    /// y = 0. Decoration only: the game's traffic does not see them. The sizes are the usual products' and plazas', estimated, not a standard's clauses (the notes of the templates say so). Pure.
    /// </summary>
    public static class TollProps
    {
        /// <summary>How far a loose foot reaches under the ground (m), as Barriers' own: no gap under a thing on a crowned road.</summary>
        private const float Sink = Barriers.FootSink;

        private static MarkingShape Shape(SolidColour colour) => new MarkingShape { Solid = colour };

        /// <summary>The box x0 to x1, y0 to y1, z0 to z1.</summary>
        private static void Slab(MarkingShape shape, float x0, float x1, float y0, float y1, float z0, float z1) =>
            Solids.Box(shape.Mesh, new Vec3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f), new Vec3((x1 - x0) * 0.5f, (y1 - y0) * 0.5f, (z1 - z0) * 0.5f));

        /// <summary>
        /// A lifting-arm gate: a grey housing 360 x 420 mm and 1.2 m high on a concrete plinth, a label and a red lamp on it, and a round arm of 70 mm in red and white every 500 mm, on a hub at 0.95 m, to
        /// the side the arm reaches (right is +X of the item); down across the lane, or open, standing up. length: the arm's (m), 3 to 6.
        /// </summary>
        public static List<MarkingShape> Gate(float length, bool open, bool armRight)
        {
            MarkingShape gray = Shape(SolidColour.LightGray), steel = Shape(SolidColour.Steel), black = Shape(SolidColour.Black), red = Shape(SolidColour.Red), white = Shape(SolidColour.White), concrete = Shape(SolidColour.Concrete);
            var shapes = new List<MarkingShape> { gray, steel, black, red, white, concrete };
            length = Math.Max(3f, Math.Min(6f, length));
            float side = armRight ? 1f : -1f;
            Slab(concrete, -0.27f, 0.27f, -Sink, 0.12f, -0.30f, 0.30f);
            Slab(gray, -0.18f, 0.18f, 0.12f, 1.17f, -0.21f, 0.21f);
            Slab(steel, -0.20f, 0.20f, 1.17f, 1.21f, -0.23f, 0.23f);
            // The label and the lamp are on the two faces that look along the road.
            Slab(black, -0.09f, 0.09f, 0.62f, 0.88f, -0.215f, 0.215f);
            Slab(red, -0.03f, 0.03f, 1.00f, 1.06f, -0.218f, 0.218f);
            const float hubY = 0.95f, armR = 0.035f, piece = 0.5f, root = 0.24f;
            Solids.Tube(steel.Mesh, new Vec3(side * 0.20f, hubY, -0.09f), new Vec3(side * 0.20f, hubY, 0.09f), 0.05f, 0.05f, 12, true, true);
            int pieces = (int)Math.Ceiling(length / piece - 1e-4f);
            for (int i = 0; i < pieces; i++)
            {
                float a = i * piece, b = Math.Min(length, a + piece);
                MarkingShape colour = i % 2 == 0 ? red : white;
                Vec3 from = open ? new Vec3(side * root, hubY + a, 0f) : new Vec3(side * (root + a), hubY, 0f), to = open ? new Vec3(side * root, hubY + b, 0f) : new Vec3(side * (root + b), hubY, 0f);
                Solids.Tube(colour.Mesh, from, to, armR, armR, 8, false, i == pieces - 1);
            }

            return shapes;
        }

        /// <summary>
        /// A toll booth: a plinth 150 mm high, a grey lower wall to 1.0 m, windows round it to 2.2 m between white posts, a roof over it in the colour of the plaza (fascia) with a steel cap, and a
        /// door in the +X wall. 1.7 m across and length (m, 2.4 to 4.0) along the road, 2.4 m high.
        /// </summary>
        public static List<MarkingShape> Booth(float length, SolidColour fascia)
        {
            MarkingShape gray = Shape(SolidColour.LightGray), glass = Shape(SolidColour.Blue), white = Shape(SolidColour.White), steel = Shape(SolidColour.Steel), concrete = Shape(SolidColour.Concrete), roof = Shape(fascia);
            var shapes = new List<MarkingShape> { gray, glass, white, steel, concrete, roof };
            length = Math.Max(2.4f, Math.Min(4f, length));
            const float hw = 0.85f, wall = 1.0f, top = 2.2f;
            float hl = length * 0.5f;
            Slab(concrete, -hw - 0.10f, hw + 0.10f, -Sink, 0.15f, -hl - 0.10f, hl + 0.10f);
            Slab(gray, -hw, hw, 0.15f, wall, -hl, hl);
            Slab(glass, -hw + 0.04f, hw - 0.04f, wall, top, -hl + 0.04f, hl - 0.04f);
            // The posts: at the corners and half way along the long walls (and across the short ones), proud of the glass.
            foreach (float x in new[] { -hw, hw })
            {
                foreach (float z in new[] { -hl, 0f, hl })
                    Slab(white, x - 0.05f, x + 0.05f, wall, top, z - 0.05f, z + 0.05f);
            }

            foreach (float z in new[] { -hl, hl })
                Slab(white, -0.05f, 0.05f, wall, top, z - 0.05f, z + 0.05f);
            Slab(white, -hw, hw, wall - 0.04f, wall, -hl, hl);
            Slab(roof, -hw - 0.18f, hw + 0.18f, top, top + 0.16f, -hl - 0.18f, hl + 0.18f);
            Slab(steel, -hw - 0.12f, hw + 0.12f, top + 0.16f, top + 0.20f, -hl - 0.12f, hl + 0.12f);
            // The door and its handle in the wall on the lane side.
            Slab(steel, hw, hw + 0.012f, 0.20f, wall - 0.04f, -0.42f, 0.42f);
            Slab(white, hw + 0.012f, hw + 0.03f, 0.60f, 0.68f, 0.28f, 0.34f);
            return shapes;
        }

        /// <summary>
        /// A raised toll island: a concrete kerb section width (m, 1.6 to 3.5) across and 200 mm high, its top edges chamfered 80 mm, length (m, 4 to 60) long, with a yellow board with black bars on each
        /// end facing the traffic: yellow, with two upright black bars (the usual reflective end plate).
        /// </summary>
        public static List<MarkingShape> Island(float length, float width)
        {
            MarkingShape concrete = Shape(SolidColour.Concrete), yellow = Shape(SolidColour.Yellow), black = Shape(SolidColour.Black);
            var shapes = new List<MarkingShape> { concrete, yellow, black };
            length = Math.Max(4f, Math.Min(60f, length));
            width = Math.Max(1.6f, Math.Min(3.5f, width));
            float hw = width * 0.5f, hl = length * 0.5f;
            Barriers.Sweep(concrete.Mesh, new List<Vec3> { new Vec3(0f, 0f, -hl), new Vec3(0f, 0f, hl) },
                new List<(float u, float v)> { (-hw, -Sink), (hw, -Sink), (hw, 0.17f), (hw - 0.08f, 0.20f), (-hw + 0.08f, 0.20f), (-hw, 0.17f) }, true, true, true);
            // The end plates, a little proud of the concrete: yellow over the width less 120 mm each side, in five parts across, the second and the fourth black.
            float plate = (width - 0.24f) / 5f;
            foreach (float z in new[] { -hl, hl })
            {
                float dir = z < 0f ? -1f : 1f;
                Slab(yellow, -hw + 0.12f, hw - 0.12f, 0.03f, 0.19f, Math.Min(z, z + dir * 0.02f), Math.Max(z, z + dir * 0.02f));
                foreach (int i in new[] { 1, 3 })
                {
                    float x0 = -hw + 0.12f + plate * i;
                    Slab(black, x0, x0 + plate, 0.03f, 0.19f, Math.Min(z + dir * 0.02f, z + dir * 0.024f), Math.Max(z + dir * 0.02f, z + dir * 0.024f));
                }
            }

            return shapes;
        }

        /// <summary>
        /// A rubber speed hump (减速带) across the road: width (m) from -width/2 to width/2, rows of them 400 mm apart along the road, each 350 mm wide and 50 mm high with rounded shoulders, in yellow and
        /// black pieces 250 mm long across.
        /// </summary>
        public static List<MarkingShape> Hump(float width, int rows)
        {
            MarkingShape yellow = Shape(SolidColour.Yellow), black = Shape(SolidColour.Black);
            var shapes = new List<MarkingShape> { yellow, black };
            width = Math.Max(1f, Math.Min(16f, width));
            rows = Math.Max(1, Math.Min(3, rows));
            var profile = new List<(float u, float v)> { (-0.175f, -0.02f), (0.175f, -0.02f), (0.175f, 0.0f), (0.15f, 0.035f), (0.08f, 0.05f), (-0.08f, 0.05f), (-0.15f, 0.035f), (-0.175f, 0.0f) };
            int pieces = (int)Math.Ceiling(width / 0.25f - 1e-4f);
            for (int row = 0; row < rows; row++)
            {
                float z = (row - (rows - 1) * 0.5f) * 0.4f;
                for (int i = 0; i < pieces; i++)
                {
                    float x0 = -width * 0.5f + i * 0.25f, x1 = Math.Min(width * 0.5f, x0 + 0.25f);
                    Barriers.Sweep((i % 2 == 0 ? yellow : black).Mesh, new List<Vec3> { new Vec3(x0, 0f, z), new Vec3(x1, 0f, z) }, profile, true, true, true);
                }
            }

            return shapes;
        }
    }
}
