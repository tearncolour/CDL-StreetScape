using System;
using System.Collections.Generic;
using CDLC.TS.Snap;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// Where a median railing comes down at a gap pedestrians cross or a junction (GB 50688-2011 (2019) 7.6.1-1: "在有行人穿行的断口处，应逐渐降低护栏高度，且不高于0.70m，降低后的长度不应小于停车视距";
    /// GA/T 1567-2019 4.2.6: at a plane junction, a crosswalk, a left-turn or U-turn opening it comes down by a slanting transition, the end not above 0.70 m for 10 m at least, the transition 15 m at
    /// least). At the ends asked for it is Low for Length metres of the whole run from the end, and from there it rises in a straight ramp of Ramp metres to its height. Until 0.12.106 the whole
    /// of Length (20 to 70 m, the stopping sight distance) was the slope and 0.70 m was reached at one point, the end: the owner: "不是很慢的降低，而是很快的降低".
    /// </summary>
    public struct GapTaper
    {
        public const float Low = 0.70f;

        public bool AtStart, AtEnd;

        /// <summary>How long the railing is Low at an end (m).</summary>
        public float Length;

        /// <summary>How long the ramp from Low up to its height is (m); none: it steps.</summary>
        public float Ramp;

        /// <summary>The height at s metres along the whole run (whole: its length) of a railing full high elsewhere.</summary>
        public float HeightAt(float s, float whole, float full)
        {
            if (!(Length > 0f || Ramp > 0f) || full <= Low)
                return full;
            float d = float.MaxValue;
            if (AtStart)
                d = Math.Min(d, s);
            if (AtEnd)
                d = Math.Min(d, whole - s);
            if (d <= Length)
                return Low;
            return d < Length + Ramp ? Low + (full - Low) * (d - Length) / Ramp : full;
        }
    }

    /// <summary>A thing of the game to be put at a place of a marking's frame (0.12.32): the plants of a planter. Kind names what is wanted; the game's object is chosen where it is placed.</summary>
    public sealed class PropAnchor
    {
        public Vec3 Local;
        public float Yaw;
        public string Kind = "";

        /// <summary>How much room it has across (m): a plant is chosen that fits.</summary>
        public float Room;
    }

    /// <summary>
    /// The city railings and barriers of 0.12.32 (the owner: "更多城市护栏/矮护栏/绿化护栏/中央护栏", "护栏都做，可摆放绿化植物用游戏物件"). What GB 50688-2011 (2019) sets is quoted by clause: the heights of
    /// pedestrian railings (7.5.2: 1.10 m, never under 0.90 m; 1.40 m by a cycle track on a bridge) and the gaps between their bars (0.11 m where one can fall), the median railing (7.6.1: 1.10 m, lowered at
    /// the gaps pedestrians cross), the spacing of separating posts (7.6.2: 1.3 to 1.5 m) and the kinds of crash barrier (7.2.3: concrete, W-beam, metal beam and post, combined). The Hong Kong type's sizes
    /// are a supplier's (posts 40 x 40 mm, frame 40 x 15 mm, eleven 16 mm bars to a 1.5 m panel); every other size is this mod's estimate of the usual products, and the templates say so. Pure.
    /// </summary>
    public static partial class Barriers
    {
        /// <summary>The line raised by dy(s) at each of its points (at: their distances along it).</summary>
        private static List<Vec3> Raised(IList<Vec3> line, IList<float> at, Func<float, float> dy)
        {
            var raised = new List<Vec3>(line.Count);
            for (int i = 0; i < line.Count; i++)
                raised.Add(line[i] + Up * dy(at[i]));
            return raised;
        }

        /// <summary>A rectangular bar (width across, height up) swept along the line, its bottom at y.</summary>
        private static void Bar(MeshBuilder mesh, IList<Vec3> line, float u, float y, float width, float height, bool first, bool last) =>
            Sweep(mesh, line, new List<(float u, float v)> { (u - width * 0.5f, y), (u + width * 0.5f, y), (u + width * 0.5f, y + height), (u - width * 0.5f, y + height) }, true, first, last);

        /// <summary>A round tube of radius r swept along the line at (u, y).</summary>
        private static void Rail(MeshBuilder mesh, IList<Vec3> line, float u, float y, float r, bool first, bool last) => Sweep(mesh, line, Ring(r, 8, u, y), true, first, last);

        private static bool NearStation(float s, float spacing, float phase, float within)
        {
            double k = (s + phase) / spacing;
            return Math.Abs(k - Math.Round(k)) * spacing < within;
        }

        /// <summary>
        /// The Hong Kong type railing (港式护栏, 深标Ⅰ型; suppliers call it 甲型, which Shenzhen's SJG 189-2025 lists apart): square posts 40 mm every 1.5 m panel, a frame of flat bars 40 x 15 mm at the top and the bottom, eleven round bars 16 mm to a panel (125 mm apart,
        /// 109 mm between them), its bottom 0.1 m over the ground; white reflective film on the posts every 6 m, on both faces in a median. Comes down at the gaps (taper).
        /// </summary>
        public static List<MarkingShape> HongKongFence(IList<Vec3> line, float height, SolidColour colour, GapTaper taper, float phase, float whole, bool first, bool last)
        {
            var frame = new MarkingShape { Solid = colour };
            var film = new MarkingShape { Solid = colour == SolidColour.White ? SolidColour.Red : SolidColour.White, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { frame, film };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.8f, Math.Min(1.4f, height));
            const float post = 0.04f, flat = 0.04f, flatThick = 0.015f, bar = 0.008f, panel = 1.5f, barGap = 0.125f, bottom = 0.10f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (!(whole > 0f))
                whole = phase + total;
            float H(float s) => taper.HeightAt(phase + s, whole, height);
            foreach (float s in Stations(total, panel, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                float h = H(s);
                Box(frame.Mesh, p, ahead, post, post, -Embed, h + 0.02f);
                if (NearStation(s, 6f, phase, 0.1f))
                    Box(film.Mesh, p, ahead, post + 0.004f, post + 0.004f, h - 0.12f, h - 0.02f);
            }

            Bar(frame.Mesh, line, 0f, bottom, flatThick, flat, first, last);
            Bar(frame.Mesh, Raised(line, at, s => H(s) - height), 0f, height - flat, flatThick, flat, first, last);
            foreach (float s in MarkingStuds.Stations(total, barGap, phase))
            {
                if (NearStation(s, panel, phase, 0.04f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(frame.Mesh, p + Up * (bottom + flat), p + Up * (H(s) - flat), bar, bar, 6, false, false);
            }

            return shapes;
        }

        /// <summary>
        /// A pedestrian railing (人行护栏, GB 50688 7.5.2): round posts 60 mm every 2 m, a round top rail 50 mm at the height, a bottom rail 40 mm at 0.1 m and upright bars 20 mm with 0.11 m between them (7.5.2-3), no rail
        /// a foot could stand on between (7.5.2-3: "不宜采用有蹬踏面的结构").
        /// </summary>
        public static List<MarkingShape> PedestrianRailing(IList<Vec3> line, float height, SolidColour colour, float phase, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour };
            var shapes = new List<MarkingShape> { metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.9f, Math.Min(1.4f, height));
            const float postR = 0.03f, topR = 0.025f, lowR = 0.02f, barR = 0.01f, low = 0.10f, spacing = 2f, barGap = 0.13f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(metal.Mesh, p + Up * -Embed, p + Up * (height + 0.02f), postR, postR, 10, false, true);
            }

            Rail(metal.Mesh, line, 0f, height, topR, first, last);
            Rail(metal.Mesh, line, 0f, low, lowR, first, last);
            foreach (float s in MarkingStuds.Stations(total, barGap, phase))
            {
                if (NearStation(s, spacing, phase, 0.05f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(metal.Mesh, p + Up * low, p + Up * height, barR, barR, 6, false, false);
            }

            return shapes;
        }

        /// <summary>
        /// A low separating railing between the motor and the non-motor lanes (机非隔离栏, GB 50688 7.6.1-2, 3; its height "根据需要确定", 7.6.2-1): round posts 60 mm every 2.5 m with a white reflective band
        /// at the top (7.6.2-3), two round rails 48 mm at a third and at the top.
        /// </summary>
        public static List<MarkingShape> LowRailing(IList<Vec3> line, float height, SolidColour colour, float phase, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour };
            var band = new MarkingShape { Solid = colour == SolidColour.White ? SolidColour.Red : SolidColour.White, Detail = DetailKind.Reflective };
            var shapes = new List<MarkingShape> { metal, band };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.5f, Math.Min(1.2f, height));
            const float postR = 0.03f, railR = 0.024f, spacing = 2.5f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, spacing, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(metal.Mesh, p + Up * -Embed, p + Up * (height - 0.12f), postR, postR, 10, false, false);
                Solids.Tube(band.Mesh, p + Up * (height - 0.12f), p + Up * (height - 0.04f), postR + 0.001f, postR + 0.001f, 10, false, false);
                Solids.Tube(metal.Mesh, p + Up * (height - 0.04f), p + Up * (height + 0.03f), postR, postR, 10, false, true);
            }

            Rail(metal.Mesh, line, 0f, height * 0.35f, railR, first, last);
            Rail(metal.Mesh, line, 0f, height - 0.02f, railR, first, last);
            return shapes;
        }

        /// <summary>
        /// A low fence round a lawn or a green belt (草坪护栏), 0.3 to 0.6 m: flat pickets 40 x 12 mm every 0.12 m between two rails on posts every 1.5 m, or hoops of 12 mm round bar 0.6 m wide that overlap by
        /// half. Sizes are the usual products' estimates.
        /// </summary>
        public static List<MarkingShape> LawnFence(IList<Vec3> line, float height, bool hoops, SolidColour colour, float phase, bool first, bool last)
        {
            var metal = new MarkingShape { Solid = colour };
            var shapes = new List<MarkingShape> { metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.25f, Math.Min(0.8f, height));
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            if (hoops)
            {
                const float width = 0.6f, r = 0.006f;
                const int steps = 6;
                foreach (float s in MarkingStuds.Stations(total, width * 0.5f, phase))
                {
                    if (s + width > total + 1e-3f)
                        break;
                    // A hoop from s to s + width: its legs go into the ground, its top a half ellipse.
                    var points = new List<Vec3>();
                    for (int k = 0; k <= steps; k++)
                    {
                        double a = Math.PI * k / steps;
                        float along = s + width * 0.5f * (float)(1.0 - Math.Cos(a));
                        float up = height - width * 0.5f + width * 0.5f * (float)Math.Sin(a);
                        points.Add(RoadMarkings.PointAt(line, at, along) + Up * Math.Max(0f, up));
                    }

                    Solids.Tube(metal.Mesh, RoadMarkings.PointAt(line, at, s) + Up * -Embed, points[0], r, r, 4, false, false);
                    for (int k = 0; k < steps; k++)
                        Solids.Tube(metal.Mesh, points[k], points[k + 1], r, r, 4, false, false);
                    Solids.Tube(metal.Mesh, points[steps], RoadMarkings.PointAt(line, at, s + width) + Up * -Embed, r, r, 4, false, false);
                }

                return shapes;
            }

            const float post = 0.04f, rail = 0.03f, railThick = 0.015f, picket = 0.04f, picketThick = 0.012f, spacing = 1.5f, picketGap = 0.12f;
            foreach (float s in Stations(total, spacing, phase, last))
                Box(metal.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), post, post, -Embed, height + 0.03f);
            Bar(metal.Mesh, line, 0f, height * 0.2f, railThick, rail, first, last);
            Bar(metal.Mesh, line, 0f, height * 0.8f - rail, railThick, rail, first, last);
            foreach (float s in MarkingStuds.Stations(total, picketGap, phase))
            {
                if (NearStation(s, spacing, phase, 0.04f))
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Vec3 ahead = HeadingAt(line, at, s);
                // A picket's face looks across the road: its width along the line, its thickness across.
                Box(metal.Mesh, p, ahead, picketThick, picket, 0.02f, height);
            }

            return shapes;
        }

        /// <summary>
        /// Separating posts (分隔柱, 弹性警示柱; GB 50688 7.6.2: 1.3 to 1.5 m apart, reflective where there is no lighting): flexible round posts 80 mm on a black base 200 mm across, two white reflective bands
        /// near the top. The height and the colour are the usual products'.
        /// </summary>
        public static List<MarkingShape> FlexPosts(IList<Vec3> line, float height, SolidColour colour, float spacing, float phase, bool last)
        {
            var body = new MarkingShape { Solid = colour, Detail = DetailKind.Plastic };
            var band = new MarkingShape { Solid = SolidColour.White, Detail = DetailKind.Reflective };
            var foot = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            var shapes = new List<MarkingShape> { body, band, foot };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.3f, Math.Min(1.2f, height));
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, Math.Max(0.5f, spacing), phase, last))
                FlexPost(body.Mesh, band.Mesh, foot.Mesh, RoadMarkings.PointAt(line, at, s), height);
            return shapes;
        }

        private static void FlexPost(MeshBuilder body, MeshBuilder band, MeshBuilder foot, Vec3 p, float height)
        {
            const float r = 0.04f;
            Solids.Tube(foot, p + Up * -FootSink, p + Up * 0.03f, 0.1f, 0.09f, 14, false, true);
            float[] cuts = { 0.03f, height - 0.18f, height - 0.12f, height - 0.09f, height - 0.03f, height };
            for (int i = 0; i + 1 < cuts.Length; i++)
                Solids.Tube(i % 2 == 1 ? band : body, p + Up * cuts[i], p + Up * cuts[i + 1], r, r, 12, false, i + 2 == cuts.Length);
        }

        /// <summary>A flexible post where a median railing ends at a pedestrians' gap (GB 50688 7.6.1-1: "断口处应设置分隔柱"), a metre beyond the end of the run (start: before its start).</summary>
        public static List<MarkingShape> GapPost(IList<Vec3> line, bool start)
        {
            var body = new MarkingShape { Solid = SolidColour.Yellow, Detail = DetailKind.Plastic };
            var band = new MarkingShape { Solid = SolidColour.White, Detail = DetailKind.Reflective };
            var foot = new MarkingShape { Solid = SolidColour.Black, Detail = DetailKind.Plastic };
            if (line != null && line.Count >= 2)
            {
                Vec3 end = start ? line[0] : line[line.Count - 1], away = start ? (line[0] - line[1]).HeadingXZ() : (line[line.Count - 1] - line[line.Count - 2]).HeadingXZ();
                FlexPost(body.Mesh, band.Mesh, foot.Mesh, end + away * 1f, 0.75f);
            }

            return new List<MarkingShape> { body, band, foot };
        }

        /// <summary>
        /// A railing with planter boxes on it (花箱护栏; GB 50688 7.5.2-3: a planter on a railing must be kept from falling): square posts 50 mm every 2 m and a top rail at the height, and on it between each two
        /// posts a box 1.6 m long, 0.3 m wide and 0.25 m deep in a steel cradle (two flat bars under it and its ends held by the posts), soil 3 cm under its rim. The plants are the game's (props, one or two
        /// a box). Sizes are the usual products' estimates.
        /// </summary>
        public static List<MarkingShape> PlanterRailing(IList<Vec3> line, float height, SolidColour railColour, SolidColour boxColour, float phase, bool last, List<PropAnchor> props)
        {
            var rail = new MarkingShape { Solid = railColour };
            var box = new MarkingShape { Solid = boxColour };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var shapes = new List<MarkingShape> { rail, box, soil };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.6f, Math.Min(1.2f, height));
            const float post = 0.05f, spacing = 2f, boxLength = 1.6f, boxWidth = 0.3f, boxDeep = 0.25f, wall = 0.02f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            List<float> posts = Stations(total, spacing, phase, last);
            foreach (float s in posts)
                Box(rail.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), post, post, -Embed, height + boxDeep);
            Bar(rail.Mesh, line, 0f, height - 0.04f, 0.04f, 0.04f, true, last);
            Bar(rail.Mesh, line, 0f, 0.12f, 0.04f, 0.04f, true, last);
            for (int i = 0; i + 1 < posts.Count; i++)
            {
                float mid = (posts[i] + posts[i + 1]) * 0.5f;
                if (posts[i + 1] - posts[i] < boxLength + post)
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, mid), ahead = HeadingAt(line, at, mid);
                foreach (float k in new[] { -1f, 1f })
                    Box(rail.Mesh, p + ahead.RightXZ() * (k * (boxWidth * 0.5f + 0.01f)), ahead, 0.02f, boxLength, height - 0.02f, height + 0.06f);
                OpenBox(box.Mesh, soil.Mesh, p, ahead, boxWidth, boxLength, height, height + boxDeep, wall, 0.03f);
                AddPlants(props, p + Up * (height + boxDeep - 0.03f), ahead, boxLength, boxWidth);
            }

            return shapes;
        }

        /// <summary>
        /// Planter boxes standing on the road or a median as a separator (花箱隔离墩): boxes length x width x height (the usual 1.5 x 0.5 x 0.6 m) with gap metres between them, soil 5 cm under the rim, the game's
        /// plants in them (props). Sizes are the usual products' estimates.
        /// </summary>
        public static List<MarkingShape> PlanterBoxes(IList<Vec3> line, float length, float width, float height, float gap, SolidColour colour, float phase, List<PropAnchor> props)
        {
            // Planter boxes are glass-fibre concrete or plastic: concrete's pores on a concrete box, a smooth moulding on the painted ones.
            var box = new MarkingShape { Solid = colour, Detail = colour == SolidColour.Concrete || colour == SolidColour.Wood ? DetailKind.None : DetailKind.Plastic };
            var soil = new MarkingShape { Solid = SolidColour.Soil };
            var shapes = new List<MarkingShape> { box, soil };
            if (line == null || line.Count < 2)
                return shapes;
            length = Math.Max(0.6f, Math.Min(3f, length));
            width = Math.Max(0.3f, Math.Min(1.2f, width));
            height = Math.Max(0.3f, Math.Min(1.0f, height));
            gap = Math.Max(0f, Math.Min(5f, gap));
            const float wall = 0.05f;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            float pitch = length + gap;
            // A box's middle every pitch metres along the whole run (from half a box in), so that the boxes of a run of pieces go on in step.
            foreach (float s in MarkingStuds.Stations(total, pitch, phase - length * 0.5f))
            {
                if (s - length * 0.5f < -1e-3f || s + length * 0.5f > total + 1e-3f)
                    continue;
                Vec3 p = RoadMarkings.PointAt(line, at, s), ahead = HeadingAt(line, at, s);
                OpenBox(box.Mesh, soil.Mesh, p, ahead, width, length, -FootSink, height, wall, 0.05f);
                AddPlants(props, p + Up * (height - 0.05f), ahead, length - 2f * wall, width - 2f * wall);
            }

            return shapes;
        }

        /// <summary>A box open at the top: four walls wall thick from bottom to top, and the soil inside them to deep under the rim.</summary>
        private static void OpenBox(MeshBuilder walls, MeshBuilder soil, Vec3 p, Vec3 ahead, float width, float length, float bottom, float top, float wall, float deep)
        {
            Vec3 right = ahead.RightXZ();
            foreach (float k in new[] { -1f, 1f })
            {
                Box(walls, p + right * (k * (width - wall) * 0.5f), ahead, wall, length, bottom, top);
                Box(walls, p + ahead * (k * (length - wall) * 0.5f), ahead, width - 2f * wall, wall, bottom, top);
            }

            Box(soil, p, ahead, width - 2f * wall, length - 2f * wall, bottom, top - deep);
        }

        /// <summary>The plants of a box: one in its middle, or two a quarter of it from either end when it is long (one only when single: the game's smallest bushes are over a metre across); their room is the box's width.</summary>
        private static void AddPlants(List<PropAnchor> props, Vec3 middle, Vec3 ahead, float length, float width, string kind = GreenPlants.Planter, bool single = false)
        {
            if (props == null)
                return;
            float yaw = (float)Math.Atan2(ahead.X, ahead.Z);
            if (single || length < 1.2f)
            {
                props.Add(new PropAnchor { Local = middle, Yaw = yaw, Kind = kind, Room = Math.Min(length, width) });
                return;
            }

            foreach (float k in new[] { -0.25f, 0.25f })
                props.Add(new PropAnchor { Local = middle + ahead * (k * length), Yaw = yaw, Kind = kind, Room = Math.Min(length * 0.5f, width) });
        }

        /// <summary>
        /// A combined barrier (组合式护栏, GB 50688 7.2.3: by a bridge or a road that wants it to be seen through): an upright concrete wall 0.4 m thick and wallHeight high with its top edges chamfered, steel
        /// posts 100 x 100 mm every 2 m on it and a rectangular steel beam 140 mm high on them to the height. Sizes are the usual drawings' estimates, not a clause.
        /// </summary>
        public static List<MarkingShape> CombinedBarrier(IList<Vec3> line, float height, float wallHeight, SolidColour metalColour, float phase, bool first, bool last)
        {
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var metal = new MarkingShape { Solid = metalColour };
            var shapes = new List<MarkingShape> { concrete, metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.9f, Math.Min(1.5f, height));
            wallHeight = Math.Max(0.4f, Math.Min(height - 0.25f, wallHeight));
            const float half = 0.2f, chamfer = 0.04f, post = 0.1f, beam = 0.14f, beamDeep = 0.1f;
            Sweep(concrete.Mesh, line, new List<(float u, float v)> { (-half, -Embed), (half, -Embed), (half, wallHeight - chamfer), (half - chamfer, wallHeight), (-half + chamfer, wallHeight), (-half, wallHeight - chamfer) }, true, first, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, 2f, phase, last))
                Box(metal.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), post, post, wallHeight, height - beam);
            Bar(metal.Mesh, line, 0f, height - beam, beamDeep, beam, first, last);
            return shapes;
        }

        /// <summary>
        /// A metal beam and post barrier (金属梁柱式护栏, GB 50688 7.2.3: by a steel bridge or where it must be light and seen through): a concrete kerb 0.3 m wide and 0.25 m high, steel posts 140 x 100 mm every 2 m
        /// and two or three rectangular beams 160 x 120 mm on their face toward the traffic (side: +1 the traffic is on the line's right), the top one at the height. Sizes are the usual drawings' estimates,
        /// not a clause.
        /// </summary>
        public static List<MarkingShape> BeamAndPost(IList<Vec3> line, int beams, float height, SolidColour colour, int side, float phase, bool first, bool last)
        {
            var concrete = new MarkingShape { Solid = SolidColour.Concrete };
            var metal = new MarkingShape { Solid = colour };
            var shapes = new List<MarkingShape> { concrete, metal };
            if (line == null || line.Count < 2)
                return shapes;
            height = Math.Max(0.9f, Math.Min(1.5f, height));
            beams = Math.Max(2, Math.Min(3, beams));
            const float kerb = 0.25f, beam = 0.16f, beamDeep = 0.12f;
            Bar(concrete.Mesh, line, 0f, -Embed, 0.3f, kerb + Embed, first, last);
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, 2f, phase, last))
                Box(metal.Mesh, RoadMarkings.PointAt(line, at, s), HeadingAt(line, at, s), 0.14f, 0.1f, kerb, height - 0.02f);
            float face = (side >= 0 ? 1f : -1f) * (0.07f + beamDeep * 0.5f);
            for (int i = 0; i < beams; i++)
            {
                float top = height - i * (height - kerb - 0.1f) / beams;
                Bar(metal.Mesh, line, face, top - beam, beamDeep, beam, first, last);
            }

            return shapes;
        }

        /// <summary>
        /// A movable barrier across a median opening (活动护栏, GB 50688 7.2.6: at every median opening of an expressway, as strong as the median's barrier and joined to it): a two-wave beam on both faces of
        /// posts that stand in sleeves in the ground (插拔式: the posts are pulled out to open it), the posts yellow, the sleeves' collars on the ground. Sizes are the usual products' estimates.
        /// </summary>
        public static List<MarkingShape> MovableBarrier(IList<Vec3> line, float phase, bool first, bool last)
        {
            List<MarkingShape> shapes = WBeam(line, false, 2f, 1, true, phase, first, last, BeamEnd.Round, BeamEnd.Round, 0f);
            var collars = new MarkingShape { Solid = SolidColour.Yellow };
            shapes.Add(collars);
            if (line == null || line.Count < 2)
                return shapes;
            var at = new List<float>();
            float total = RoadMarkings.Lengths(line, at);
            foreach (float s in Stations(total, 2f, phase, last))
            {
                Vec3 p = RoadMarkings.PointAt(line, at, s);
                Solids.Tube(collars.Mesh, p + Up * -FootSink, p + Up * 0.06f, 0.09f, 0.09f, 12, false, true);
                Solids.Tube(collars.Mesh, p + Up * 0.06f, p + Up * 0.25f, 0.06f, 0.06f, 10, false, false);
            }

            return shapes;
        }
    }
}
