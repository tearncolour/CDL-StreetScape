using System;
using System.Collections.Generic;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>How congested a stretch of road is, for the colour of its line on a traffic-state screen (GB 5768.2 Table 10, traffic state: red, yellow, green).</summary>
    public enum TrafficLevel : byte
    {
        Unknown = 0,
        Free = 1,
        Slow = 2,
        Jam = 3
    }

    /// <summary>One stretch of road of a traffic-state screen: the points of its centre line, the way the traffic runs (away from the sign), how congested that way is, and the road's name.</summary>
    public sealed class TmEdge
    {
        /// <summary>x, z pairs in the world (metres; +X east, +Z north), in the order of travel.</summary>
        public float[] Points = new float[0];

        public TrafficLevel Level = TrafficLevel.Unknown;

        /// <summary>The road's name as the game shows it; "" when it has none.</summary>
        public string Name = "";

        /// <summary>The road the stretch belongs to (the game's aggregate of the road's edges), 0 when not known: the stretches of one road are named once.</summary>
        public int Road;

        /// <summary>How far along the roads from the sign the stretch starts (m): 0 for the stretch the sign stands on (0.8.0).</summary>
        public float Distance;

        /// <summary>The stretch's length (m).</summary>
        public float Length;

        /// <summary>A traffic accident on the stretch's road edge (the game's AccidentSite with TrafficAccident): TmAccident.None, Active, or Secured (the police are there).</summary>
        public byte Accident;
    }

    /// <summary>The values of TmEdge.Accident.</summary>
    public static class TmAccident
    {
        public const byte None = 0, Active = 1, Secured = 2;
    }

    /// <summary>
    /// What a traffic-state screen shows: the roads that traffic from the sign can take (the stretches ahead, the roads that leave the junctions on the way) with the congestion of each in the
    /// direction it would be driven. Pure data: the game's reader makes it from the road network, the layout draws it, the tests make it by hand.
    /// </summary>
    public sealed class TmNetwork
    {
        /// <summary>The sign, in the world; and the way the traffic that sees it is going (a unit vector in the ground plane): the top of the screen.</summary>
        public float OriginX, OriginZ, HeadX, HeadZ = 1f;

        public readonly List<TmEdge> Edges = new List<TmEdge>();

        /// <summary>False when nothing was read (the sign is not on a road, or the road is gone): the screen shows the sign's mark only.</summary>
        public bool Known;
    }

    /// <summary>The options of the layout of a traffic-state screen.</summary>
    public struct TmOptions
    {
        public float Width, Height;

        /// <summary>The height of the characters of the names (m).</summary>
        public float CharHeight;

        public bool Names, Direction;
    }

    /// <summary>
    /// The traffic-state screen (a variable-message sign that shows the congestion of the roads ahead as a small map, as the screens over Chinese city roads do): the layout from a network, the
    /// colours of the congestion, and the live networks the game's reader publishes. Pure, so that all of it is checked offline.
    /// </summary>
    public static class TrafficMap
    {
        public const string TemplateId = "gb.vms.traffic";

        // ------------------------------------------------------------------ the congestion

        /// <summary>
        /// The speed ratio of a lane (1 free, 0 stopped) from the game's flow offset: CarLane.m_FlowOffset is 256 minus 256 times the ratio of the distance driven to the time taken (the game's
        /// TrafficFlowSystem), kept between 0 and 255: 0 is free flow and 255 a jam.
        /// </summary>
        public static float RatioOfOffset(float flowOffset) => Math.Max(0f, Math.Min(1f, 1f - flowOffset / 256f));

        /// <summary>The level of a speed ratio: Free at or above slowBelow, Jam below jamBelow, Slow between. PROVISIONAL limits (the defaults of the screen): what the game's ratio is on a healthy road has not been read yet.</summary>
        public static TrafficLevel LevelOf(float ratio, float slowBelow, float jamBelow) =>
            ratio >= slowBelow ? TrafficLevel.Free : ratio < jamBelow ? TrafficLevel.Jam : TrafficLevel.Slow;

        /// <summary>
        /// The level with a margin, so that a ratio near a limit does not make the colour flicker: the colour stays what it was until the ratio is a margin past the limit. previous is Unknown the first time.
        /// </summary>
        public static TrafficLevel LevelWithMargin(TrafficLevel previous, float ratio, float slowBelow, float jamBelow, float margin = 0.04f)
        {
            TrafficLevel now = LevelOf(ratio, slowBelow, jamBelow);
            if (previous == TrafficLevel.Unknown || previous == now)
                return now;
            // Moving to a better level needs the ratio a margin above the limit it crosses; to a worse one a margin below it.
            switch (previous)
            {
                case TrafficLevel.Free:
                    return ratio < slowBelow - margin ? LevelOf(ratio, slowBelow, jamBelow) : TrafficLevel.Free;
                case TrafficLevel.Jam:
                    return ratio >= jamBelow + margin ? LevelOf(ratio, slowBelow, jamBelow) : TrafficLevel.Jam;
                default:
                    if (now == TrafficLevel.Free)
                        return ratio >= slowBelow + margin ? TrafficLevel.Free : TrafficLevel.Slow;
                    return ratio < jamBelow - margin ? TrafficLevel.Jam : TrafficLevel.Slow;
            }
        }

        /// <summary>The colour a level is drawn in (Table 10: traffic state red, yellow, green).</summary>
        public static GuideColour ColourOf(TrafficLevel level)
        {
            switch (level)
            {
                case TrafficLevel.Jam: return GuideColour.Red;
                case TrafficLevel.Slow: return GuideColour.Yellow;
                default: return GuideColour.Green;
            }
        }

        // ------------------------------------------------------------------ the live networks

        private sealed class Live
        {
            public TmNetwork Network;
            public int Revision;

            /// <summary>A network that is ready but not yet shown: its picture is being made on another thread (0.6.0), and it is shown (Publish) when that is done.</summary>
            public TmNetwork Staged;
            public int StagedRevision;
        }

        private static readonly Dictionary<int, Live> s_Live = new Dictionary<int, Live>();
        private static int s_Changes;

        /// <summary>Raised by one whenever any live network is set or removed (the renderer looks at it every frame and reads the items again when it moved).</summary>
        public static int Changes => s_Changes;

        /// <summary>Publishes what a screen shows now. The revision names this content: a screen is laid out again when it differs from the one it was laid out for.</summary>
        public static void Set(int id, TmNetwork network, int revision)
        {
            lock (s_Live)
            {
                if (s_Live.TryGetValue(id, out Live known) && known.Revision == revision)
                    return;
                s_Live[id] = new Live { Network = network, Revision = revision };
                s_Changes++;
            }
        }

        public static TmNetwork Get(int id)
        {
            lock (s_Live)
                return s_Live.TryGetValue(id, out Live live) ? live.Network : null;
        }

        /// <summary>
        /// The network a screen of this revision shows: the published one when its revision is that, the staged one when that is (so that a picture can be made for a revision before it is shown), else
        /// the published one (a screen of a revision that is neither is drawn from what is there).
        /// </summary>
        public static TmNetwork Get(int id, int revision)
        {
            lock (s_Live)
            {
                if (!s_Live.TryGetValue(id, out Live live))
                    return null;
                if (live.Staged != null && live.StagedRevision == revision && live.Revision != revision)
                    return live.Staged;
                return live.Network;
            }
        }

        /// <summary>Keeps a network for a revision without showing it: nothing that is drawn changes (Changes is not raised) until Publish. A staged revision replaces an earlier staged one.</summary>
        public static void Stage(int id, TmNetwork network, int revision)
        {
            lock (s_Live)
            {
                if (!s_Live.TryGetValue(id, out Live live))
                    s_Live[id] = live = new Live();
                live.Staged = network;
                live.StagedRevision = revision;
            }
        }

        /// <summary>Shows the staged network: it becomes the published one with its revision, and Changes is raised. False when nothing is staged.</summary>
        public static bool Publish(int id)
        {
            lock (s_Live)
            {
                if (!s_Live.TryGetValue(id, out Live live) || live.Staged == null)
                    return false;
                live.Network = live.Staged;
                live.Revision = live.StagedRevision;
                live.Staged = null;
                s_Changes++;
                return true;
            }
        }

        public static int RevisionOf(int id)
        {
            lock (s_Live)
                return s_Live.TryGetValue(id, out Live live) ? live.Revision : 0;
        }

        /// <summary>Forgets what a screen showed (its item is gone). Not a change of anything that is drawn.</summary>
        public static void Remove(int id)
        {
            lock (s_Live)
                s_Live.Remove(id);
        }

        public static void Clear()
        {
            lock (s_Live)
            {
                if (s_Live.Count > 0)
                    s_Changes++;
                s_Live.Clear();
            }
        }

        // ------------------------------------------------------------------ what the renderer and the reader need of an item

        /// <summary>Whether a placed item (by its saved type hash) is a traffic-state screen: the ones the game's reader publishes a network for.</summary>
        public static bool IsTrafficScreen(uint typeHash) => typeHash == s_Hash;

        private static readonly uint s_Hash = SignCatalog.HashOf(TemplateId);

        /// <summary>
        /// The settings of an item as the renderer draws it: the item's own settings with the number the reader publishes under and the revision of what it published. Both are only for the renderer
        /// (the template's two hidden fields) and are never saved: the item keeps its own settings. Without the extension that reads the game (LiveContent) there is no reader: the settings stay as
        /// they are and the screen shows its default picture.
        /// </summary>
        public static string Decorate(string spec, int id, int revision)
        {
            GuideTemplate template = SignCatalog.ChoiceById(TemplateId)?.Def.Template;
            if (template == null || !LiveContent.On)
                return spec;
            return GuideSpec.Parse(template, spec).With("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture)).With("rev", revision.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToString();
        }

        /// <summary>What an item's settings ask of the reader: how many junctions ahead, how far, and the speed ratios under which a road is slow and jammed.</summary>
        public struct Settings
        {
            public int Junctions;
            public float Range, SlowBelow, JamBelow;
        }

        /// <summary>The reader's settings from the written settings of an item (empty: the defaults). The limits are kept sane: slow between 20 and 100 per cent, jam at least 5 points under it.</summary>
        public static Settings SettingsOf(string spec)
        {
            var settings = new Settings { Junctions = 3, Range = 800f, SlowBelow = 0.7f, JamBelow = 0.4f };
            GuideTemplate template = SignCatalog.ChoiceById(TemplateId)?.Def.Template;
            if (template == null)
                return settings;
            GuideSpec s = GuideSpec.Parse(template, spec);
            settings.Junctions = (int)Math.Max(1, Math.Min(8, s.Number("jn", 3)));
            settings.Range = (float)Math.Max(100, Math.Min(4000, s.Number("range", 800)));
            float slow = (float)s.Number("slow", 70) / 100f, jam = (float)s.Number("jam", 40) / 100f;
            settings.SlowBelow = Math.Max(0.2f, Math.Min(1f, slow));
            settings.JamBelow = Math.Max(0.05f, Math.Min(settings.SlowBelow - 0.05f, jam));
            return settings;
        }

        /// <summary>
        /// A number that names what a network shows: the same for two networks that draw the same (the structure, the names, the levels, the points to a metre), another when anything that is drawn
        /// differs. The reader raises a screen's revision only when it changes, so that a tile is built again only when its picture is another.
        /// </summary>
        public static int Signature(TmNetwork network)
        {
            unchecked
            {
                uint h = 2166136261u;
                void Mix(uint v)
                {
                    h ^= v;
                    h *= 16777619u;
                }

                if (network == null || !network.Known)
                    return (int)h;
                Mix(1u);
                Mix((uint)Math.Round(network.HeadX * 100f));
                Mix((uint)Math.Round(network.HeadZ * 100f));
                foreach (TmEdge edge in network.Edges)
                {
                    Mix((uint)edge.Level);
                    Mix((uint)edge.Road);
                    foreach (char c in edge.Name ?? "")
                        Mix(c);
                    if (edge.Points != null)
                    {
                        foreach (float p in edge.Points)
                            Mix((uint)(int)Math.Round(p));
                    }

                    Mix(0xFFFFu);
                }

                return (int)h;
            }
        }

        // ------------------------------------------------------------------ the demonstration network

        /// <summary>
        /// A made-up network for the panel's picture and the placement preview (a main road going up from the sign with three crossings, one of them jammed on its east side, one stretch of the main
        /// road slow, a road that joins two of the crossings on the east): what the screen looks like before it has read a road.
        /// </summary>
        public static TmNetwork Demo()
        {
            var net = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            TmEdge Line(string name, int road, TrafficLevel level, params float[] xz) => new TmEdge { Name = name, Road = road, Level = level, Points = xz };
            net.Edges.Add(Line("东海西路", 1, TrafficLevel.Free, 0, 0, 0, 300));
            net.Edges.Add(Line("东海西路", 1, TrafficLevel.Slow, 0, 300, 0, 620));
            net.Edges.Add(Line("东海西路", 1, TrafficLevel.Free, 0, 620, 0, 900));
            net.Edges.Add(Line("延安三路", 2, TrafficLevel.Free, 0, 300, -420, 300));
            net.Edges.Add(Line("延安三路", 2, TrafficLevel.Jam, 0, 300, 420, 300));
            net.Edges.Add(Line("山东路", 3, TrafficLevel.Free, 0, 620, -380, 620));
            net.Edges.Add(Line("山东路", 3, TrafficLevel.Free, 0, 620, 380, 620));
            net.Edges.Add(Line("太平角四路", 4, TrafficLevel.Free, 0, 900, -340, 900));
            net.Edges.Add(Line("太平角四路", 4, TrafficLevel.Free, 0, 900, 340, 900));
            net.Edges.Add(Line("胶宁高架", 5, TrafficLevel.Jam, 380, 620, 380, 900));
            return net;
        }

        // ------------------------------------------------------------------ the compass

        /// <summary>The compass word of the direction the traffic goes (the nearest of north, east, south, west; the world's +Z is north and +X east).</summary>
        public static string CompassOf(float headX, float headZ)
        {
            double degrees = Math.Atan2(headX, headZ) * 180.0 / Math.PI;
            if (degrees < 0.0)
                degrees += 360.0;
            int quarter = (int)Math.Floor((degrees + 45.0) / 90.0) % 4;
            return quarter == 0 ? "北" : quarter == 1 ? "东" : quarter == 2 ? "南" : "西";
        }

        // ------------------------------------------------------------------ the layout

        private struct Seg
        {
            public float X0, Y0, X1, Y1;
        }

        private struct Box
        {
            public float X0, Y0, X1, Y1;
        }

        /// <summary>
        /// The picture of the network on a screen of the given size: the traffic's way up, the sign's red triangle at the foot, the roads as lines (green, yellow, red: the worse over the better where
        /// they cross), the roads' names in white beside their lines where there is room (never over a line or another name), the compass word of the way the traffic goes at the top right.
        /// Returns the part's elements in the part's own frame (x to the right, y down, metres).
        /// </summary>
        public static List<GuideElement> Layout(TmNetwork network, TmOptions options)
        {
            var elements = new List<GuideElement>();
            float W = options.Width, H = options.Height, ch = Math.Max(0.02f, options.CharHeight);
            float margin = 0.6f * ch, lineWidth = Math.Max(0.02f, 0.035f * H);
            float markHeight = 1.3f * ch;

            // The network in the screen's frame, in metres of the world: u to the right of the way the traffic goes, v ahead of the sign.
            float hx = network != null ? network.HeadX : 0f, hz = network != null ? network.HeadZ : 1f;
            float hl = (float)Math.Sqrt(hx * hx + hz * hz);
            if (hl < 1e-6f)
            {
                hx = 0f;
                hz = 1f;
            }
            else
            {
                hx /= hl;
                hz /= hl;
            }

            var lines = new List<(float[] uv, TrafficLevel level, string name, int road)>();
            float minU = 0f, maxU = 0f, maxV = 0f;
            if (network != null && network.Known)
            {
                foreach (TmEdge edge in network.Edges)
                {
                    if (edge.Points == null || edge.Points.Length < 4 || edge.Level == TrafficLevel.Unknown)
                        continue;
                    var uv = new float[edge.Points.Length];
                    for (int i = 0; i + 1 < edge.Points.Length; i += 2)
                    {
                        float dx = edge.Points[i] - network.OriginX, dz = edge.Points[i + 1] - network.OriginZ;
                        uv[i] = dx * hz - dz * hx;
                        uv[i + 1] = Math.Max(0f, dx * hx + dz * hz);
                        minU = Math.Min(minU, uv[i]);
                        maxU = Math.Max(maxU, uv[i]);
                        maxV = Math.Max(maxV, uv[i + 1]);
                    }

                    lines.Add((uv, edge.Level, edge.Name ?? "", edge.Road));
                }
            }

            // The scale: the whole network in the room the screen has, never closer than 200 m across so that a short stretch is not blown up; the sign's mark at the foot.
            float availW = Math.Max(0.1f, W - 2f * margin), availH = Math.Max(0.1f, H - 2f * margin - markHeight);
            float spanU = Math.Max(200f, maxU - minU), spanV = Math.Max(200f, maxV);
            float scale = Math.Min(availW / spanU, availH / spanV);
            float centreU = (minU + maxU) * 0.5f;
            float footY = H - margin - markHeight * 0.5f, centreX = W * 0.5f;
            float X(float u) => centreX + (u - centreU) * scale;
            float Y(float v) => footY - v * scale;

            // The lines, the better first so that the worse is on top.
            var segments = new List<Seg>();
            foreach (TrafficLevel level in new[] { TrafficLevel.Free, TrafficLevel.Slow, TrafficLevel.Jam })
            {
                GuideColour colour = ColourOf(level);
                foreach ((float[] uv, TrafficLevel lv, string name, int road) in lines)
                {
                    if (lv != level)
                        continue;
                    for (int i = 0; i + 3 < uv.Length; i += 2)
                    {
                        float x0 = X(uv[i]), y0 = Y(uv[i + 1]), x1 = X(uv[i + 2]), y1 = Y(uv[i + 3]);
                        float dx = x1 - x0, dy = y1 - y0, length = (float)Math.Sqrt(dx * dx + dy * dy);
                        segments.Add(new Seg { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });
                        if (length < 1e-5f)
                            continue;
                        float nx = -dy / length * lineWidth * 0.5f, ny = dx / length * lineWidth * 0.5f;
                        elements.Add(GuideKit.Poly(new[] { x0 + nx, y0 + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, x0 - nx, y0 - ny }, colour));
                    }

                    // Round ends and bends: a disc where a stretch starts, ends or turns.
                    for (int i = 0; i + 1 < uv.Length; i += 2)
                    {
                        bool end = i == 0 || i + 2 >= uv.Length;
                        if (!end && Math.Abs(Turn(uv, i)) < 0.17f)
                            continue;
                        elements.Add(GuideKit.Oval(X(uv[i]) - lineWidth * 0.5f, Y(uv[i + 1]) - lineWidth * 0.5f, lineWidth, lineWidth, colour));
                    }
                }
            }

            // The names, once for each road, beside the longest stretch of it that is long enough, clear of the lines and of one another.
            var taken = new List<Box>();
            if (options.Names)
            {
                var roads = new Dictionary<string, List<(float[] uv, float length)>>();
                foreach ((float[] uv, TrafficLevel lv, string name, int road) in lines)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    string key = road != 0 ? "r" + road + ":" + name : "n:" + name;
                    if (!roads.TryGetValue(key, out List<(float[], float)> list))
                        roads[key] = list = new List<(float[], float)>();
                    list.Add((uv, ScreenLength(uv, scale)));
                }

                var order = new List<KeyValuePair<string, List<(float[] uv, float length)>>>(roads);
                order.Sort((a, b) => TotalLength(b.Value).CompareTo(TotalLength(a.Value)));
                foreach (KeyValuePair<string, List<(float[] uv, float length)>> road in order)
                {
                    string name = road.Key.Substring(road.Key.IndexOf(':') + 1);
                    var stretches = new List<(float[] uv, float length)>(road.Value);
                    stretches.Sort((a, b) => b.length.CompareTo(a.length));
                    bool placed = false;

                    // The size of the characters first, the room beside the road second: a name that does not fit at full size is tried at four fifths (13 dots on a 12.5 mm screen), no smaller.
                    foreach (float shrink in new[] { 1f, 0.8f })
                    {
                        float size = ch * shrink;
                        GuideBlock text = RoadGuideSigns.Line(name, size, GuideColour.White, 0.1f * size);
                        if (text.W <= 0f)
                            break;
                        foreach ((float[] uv, float length) in stretches)
                        {
                            if (placed)
                                break;
                            foreach (float at in new[] { 0.5f, 0.3f, 0.7f, 0.15f, 0.85f, 0.4f, 0.6f, 0.05f, 0.95f })
                            {
                                if (placed)
                                    break;
                                PointAt(uv, at, X, Y, out float px, out float py, out float tx, out float ty);
                                // Beside the road on either side, centred on the point, or slid along the road by half the name's width (a name longer than the stretch stands beside it, past its end).
                                foreach ((float side, float slide) in new[] { (1f, 0f), (-1f, 0f), (1f, 0.5f), (-1f, 0.5f), (1f, -0.5f), (-1f, -0.5f) })
                                {
                                    float nx = -ty * side, ny = tx * side;
                                    float half = Math.Abs(nx) * text.W * 0.5f + Math.Abs(ny) * size * 0.5f;
                                    float along = slide * (Math.Abs(tx) * text.W + Math.Abs(ty) * size);
                                    float cx = px + tx * along + nx * (lineWidth * 0.5f + 0.25f * size + half), cy = py + ty * along + ny * (lineWidth * 0.5f + 0.25f * size + half);
                                    var box = new Box { X0 = cx - text.W * 0.5f, Y0 = cy - size * 0.5f, X1 = cx + text.W * 0.5f, Y1 = cy + size * 0.5f };
                                    if (box.X0 < margin * 0.5f || box.X1 > W - margin * 0.5f || box.Y0 < margin * 0.5f || box.Y1 > footY - markHeight * 0.4f)
                                        continue;
                                    if (Overlaps(box, taken, 0.15f * size) || Touches(box, segments, lineWidth * 0.5f + 0.1f * size))
                                        continue;
                                    taken.Add(box);
                                    foreach (GuideElement e in text.Elements)
                                        elements.Add(e.Moved(box.X0, box.Y0));
                                    placed = true;
                                    break;
                                }
                            }
                        }

                        if (placed)
                            break;
                    }
                }
            }

            // The sign: a red triangle at the foot, pointing the way the traffic goes.
            float tri = 0.55f * ch;
            float mx = X(0f), my = footY;
            elements.Add(GuideKit.Poly(new[] { mx, my - tri, mx + tri * 0.9f, my + tri * 0.7f, mx - tri * 0.9f, my + tri * 0.7f }, GuideColour.Red));

            // The way the traffic goes, in words, at the top right.
            if (options.Direction)
            {
                string compass = CompassOf(hx, hz);
                float cw = ch;
                elements.Add(GuideKit.Cells(compass, W - margin - cw, margin, ch, GuideColour.White, 0f));
            }

            return elements;
        }

        private static float TotalLength(List<(float[] uv, float length)> stretches)
        {
            float total = 0f;
            foreach ((float[] uv, float length) in stretches)
                total += length;
            return total;
        }

        private static float ScreenLength(float[] uv, float scale)
        {
            float total = 0f;
            for (int i = 0; i + 3 < uv.Length; i += 2)
            {
                float dx = uv[i + 2] - uv[i], dy = uv[i + 3] - uv[i + 1];
                total += (float)Math.Sqrt(dx * dx + dy * dy);
            }

            return total * scale;
        }

        /// <summary>The turn (radians, signed) of a polyline at its point i (a pair index into uv), 0 at the ends.</summary>
        private static float Turn(float[] uv, int i)
        {
            if (i < 2 || i + 3 >= uv.Length)
                return 0f;
            float ax = uv[i] - uv[i - 2], ay = uv[i + 1] - uv[i - 1], bx = uv[i + 2] - uv[i], by = uv[i + 3] - uv[i + 1];
            return (float)Math.Atan2(ax * by - ay * bx, ax * bx + ay * by);
        }

        /// <summary>The point at the fraction at of a polyline's length, in screen metres, and the unit direction there.</summary>
        private static void PointAt(float[] uv, float at, Func<float, float> x, Func<float, float> y, out float px, out float py, out float tx, out float ty)
        {
            float total = 0f;
            for (int i = 0; i + 3 < uv.Length; i += 2)
                total += Dist(uv, i);
            float target = total * at, run = 0f;
            px = x(uv[0]);
            py = y(uv[1]);
            tx = 0f;
            ty = -1f;
            for (int i = 0; i + 3 < uv.Length; i += 2)
            {
                float length = Dist(uv, i);
                if (length < 1e-6f)
                    continue;
                float x0 = x(uv[i]), y0 = y(uv[i + 1]), x1 = x(uv[i + 2]), y1 = y(uv[i + 3]);
                float sx = x1 - x0, sy = y1 - y0, sl = (float)Math.Sqrt(sx * sx + sy * sy);
                if (sl > 1e-9f)
                {
                    tx = sx / sl;
                    ty = sy / sl;
                }

                if (run + length >= target || i + 5 >= uv.Length)
                {
                    float f = Math.Max(0f, Math.Min(1f, (target - run) / length));
                    px = x0 + (x1 - x0) * f;
                    py = y0 + (y1 - y0) * f;
                    return;
                }

                run += length;
            }
        }

        private static float Dist(float[] uv, int i)
        {
            float dx = uv[i + 2] - uv[i], dy = uv[i + 3] - uv[i + 1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool Overlaps(Box box, List<Box> others, float gap)
        {
            foreach (Box o in others)
            {
                if (box.X0 < o.X1 + gap && box.X1 > o.X0 - gap && box.Y0 < o.Y1 + gap && box.Y1 > o.Y0 - gap)
                    return true;
            }

            return false;
        }

        /// <summary>Whether any line (a segment with the given clearance round it) touches the box: found by walking each segment in small steps.</summary>
        private static bool Touches(Box box, List<Seg> segments, float clearance)
        {
            float step = Math.Max(0.005f, clearance * 0.5f);
            foreach (Seg s in segments)
            {
                float dx = s.X1 - s.X0, dy = s.Y1 - s.Y0, length = (float)Math.Sqrt(dx * dx + dy * dy);
                int n = Math.Max(1, (int)Math.Ceiling(length / step));
                for (int i = 0; i <= n; i++)
                {
                    float x = s.X0 + dx * i / n, y = s.Y0 + dy * i / n;
                    if (x > box.X0 - clearance && x < box.X1 + clearance && y > box.Y0 - clearance && y < box.Y1 + clearance)
                        return true;
                }
            }

            return false;
        }
    }
}
