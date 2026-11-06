using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CDLC.TS.Geo.Signs;

namespace CDLC.TS.Geo
{
    /// <summary>One thing the map should mark: a support with its boards (or a lone board), where it stands and what it shows. See scratch/design/MINIMAP-REQUIREMENTS.md, section 2.</summary>
    public struct TsMarker
    {
        /// <summary>1 post (single, double or triple), 2 cantilever, 3 double cantilever, 4 gantry, 5 attached (a sign on a lamp post or a signal pole), 6 variable-message sign (whatever it hangs on).</summary>
        public int Kind;

        /// <summary>The base point of the support, in absolute world decimetres (x east, z north).</summary>
        public int XDm, ZDm;

        /// <summary>The way the board faces, degrees clockwise from north, 0 to 359.</summary>
        public int Bearing;

        /// <summary>Bit 0: a variable-message sign. Bit 1: its screen is on. Bits 2 and 3: the colour class of what it shows (0 green, 1 yellow, 2 red, 3 white or other).</summary>
        public int Flags;

        /// <summary>What a variable-message sign that is on shows, one line of at most 24 characters; "" for everything else.</summary>
        public string Text;
    }

    /// <summary>
    /// The markers of the signs for the map (CDLC.TS.TsApi's content), made from the item records: one marker for each support (the boards of a group are one marker), its kind, place, facing,
    /// and for a variable-message sign whether it is on, in which colour and with what words; and the query that picks the nearest of them as the JSON the map reads. Pure, so the format is
    /// tested by decoding what it writes.
    /// </summary>
    public static class TsMarkers
    {
        public const int ApiVersion = 1;

        public const int KindPost = 1, KindCantilever = 2, KindDoubleCantilever = 3, KindGantry = 4, KindAttached = 5, KindVariable = 6;

        public const int FlagVariable = 1, FlagOn = 2;

        /// <summary>The colour class is in bits 2 and 3 of the flags.</summary>
        public const int ClassShift = 2;

        public const int ClassGreen = 0, ClassYellow = 1, ClassRed = 2, ClassWhite = 3;

        /// <summary>The most markers one call returns, whatever is asked.</summary>
        public const int MaxMarkers = 64;

        /// <summary>The most characters of a variable sign's words.</summary>
        public const int MaxText = 24;

        /// <summary>The bit of a kind in the mask of a query: kind 1 is bit 0 (value 1), kind 6 is bit 5 (value 32). A mask of 0 is every kind.</summary>
        public static int MaskOf(int kind) => kind >= 1 && kind <= 30 ? 1 << (kind - 1) : 0;

        // ------------------------------------------------------------------ making the markers

        /// <summary>The markers of all the items: one for each support with boards (the members of a group are one), one for each board over a lane on a support of its own, one for each other board.</summary>
        public static List<TsMarker> Build(IEnumerable<ItemRecord> records)
        {
            var markers = new List<TsMarker>();
            var groups = new Dictionary<uint, List<ItemRecord>>();
            foreach (ItemRecord record in records)
            {
                // The paint on the road is not a sign: it is not on the map's list of signs.
                SignDef painted = SignCatalog.ByHash(record.TypeHash);
                if (painted != null && painted.IsMarking)
                    continue;
                if (record.Group != 0u)
                {
                    if (!groups.TryGetValue(record.Group, out List<ItemRecord> members))
                        groups[record.Group] = members = new List<ItemRecord>();
                    members.Add(record);
                    continue;
                }

                AddSupport(markers, new List<ItemRecord> { record });
            }

            foreach (List<ItemRecord> members in groups.Values)
            {
                members.Sort(ItemGroups.Compare);
                AddSupport(markers, members);
            }

            return markers;
        }

        private static void AddSupport(List<TsMarker> markers, List<ItemRecord> members)
        {
            ItemRecord lead = members[0];
            if (!lead.Position.IsFinite || float.IsNaN(lead.Yaw) || float.IsInfinity(lead.Yaw))
                return;

            // The complete support as it is drawn, including its actual board order and the post count of an automatic support.
            AssemblyPlan plan = ItemGroups.PlanOf(members, out _);
            if (plan == null || plan.Placed.Count == 0)
                return;
            int kind = KindOf(plan.Support.Kind);
            Placement principal = plan.Placed.Find(p => !Assembly.IsAuxiliary(p.Def)) ?? plan.Placed[0];
            int facingMember = principal.Board.Member;

            // One marker still represents the support. Its facing is the board its words describe: the first lit screen in actual board order, or the first screen if all are dark; for an ordinary
            // support it is the first main board as physically arranged (top of a stack, or first across the arm). The lead is only the anchor of the support, and may be an auxiliary board or another screen.
            bool variable = false, on = false;
            int firstScreen = -1;
            int colour = ClassWhite;
            string text = "";
            foreach (Placement place in plan.Placed)
            {
                ItemRecord member = members[place.Board.Member];
                SignDef def = SignCatalog.ByHash(member.TypeHash);
                if (def == null || def.Group != "variable" || def.Template == null)
                    continue;
                variable = true;
                if (firstScreen < 0)
                    firstScreen = place.Board.Member;
                if (on)
                    continue;
                if (Describe(def, member.Spec, out bool shown, out int cls, out string words) && shown)
                {
                    on = true;
                    colour = cls;
                    text = words;
                    facingMember = place.Board.Member;
                }
            }

            if (variable && !on)
                facingMember = firstScreen;
            markers.Add(new TsMarker
            {
                Kind = variable ? KindVariable : kind,
                XDm = Dm(lead.Position.X),
                ZDm = Dm(lead.Position.Z),
                Bearing = BoardFacing.Bearing(BoardFacing.Yaw(plan, lead.Yaw, facingMember)),
                Flags = variable ? FlagVariable | (on ? FlagOn | (colour << ClassShift) : 0) : 0,
                Text = variable && on ? Shorten(text) : ""
            });
        }

        private static int Dm(float metres) => (int)Math.Round(metres * 10.0, MidpointRounding.AwayFromZero);

        /// <summary>The kind of a support form.</summary>
        public static int KindOf(SupportKind kind)
        {
            switch (kind)
            {
                case SupportKind.Cantilever: return KindCantilever;
                case SupportKind.FCantilever: return KindCantilever;
                case SupportKind.DoubleCantilever: return KindDoubleCantilever;
                case SupportKind.Gantry: return KindGantry;
                case SupportKind.Attached: return KindAttached;
                case SupportKind.AttachedArm: return KindAttached;
                // On a wall: fixed to something that is not the mod's, as a lamp post is (MiniMap's kinds are fixed: no new one).
                case SupportKind.WallFlat: return KindAttached;
                case SupportKind.WallBracket: return KindAttached;
                case SupportKind.OverRoad: return KindAttached;
                default: return KindPost;
            }
        }

        /// <summary>
        /// What a variable-message sign with these settings shows: whether its screen is on (something is lit), the colour class of it and one line of words. False when the sign is not one that
        /// is described (a type of the group that this does not know). The text screen shows its lines (empty: off, 4.11.1), a lane screen its state, the speed screen its limit, the
        /// traffic-state screen its name and, as its colour, the worst state of the roads it shows now (red for a jam, yellow for slow, green for free, white when it has read nothing yet).
        /// </summary>
        public static bool Describe(SignDef def, string spec, out bool on, out int colourClass, out string text)
        {
            on = false;
            colourClass = ClassWhite;
            text = "";
            if (def == null || def.Template == null)
                return false;
            GuideSpec s = def.Template.Parse(spec);
            switch (def.Id)
            {
                case "gb.vms.text":
                {
                    var words = new List<string>();
                    foreach (string key in new[] { "l1", "l2", "l3", "l4" })
                    {
                        string line = s.Get(key).Trim();
                        if (line.Length > 0)
                            words.Add(line);
                    }

                    on = words.Count > 0;
                    text = string.Join(" ", words);
                    colourClass = ClassOf(VariableSigns.ClassColour(s.Get("cls")));
                    return true;
                }

                case "gb.vms.lane":
                case "gb.vms.lane.closed":
                {
                    string state = s.Get("state");
                    on = state != "off";
                    switch (state)
                    {
                        case "closed": colourClass = ClassRed; text = "车道关闭"; break;
                        case "left": colourClass = ClassYellow; text = "向左并道"; break;
                        case "right": colourClass = ClassYellow; text = "向右并道"; break;
                        case "off": colourClass = ClassWhite; text = ""; break;
                        default: colourClass = ClassGreen; text = "车道可通行"; break;
                    }

                    return true;
                }

                case "gb.vms.speed":
                {
                    on = true;
                    colourClass = ClassRed;
                    string value = s.Get("value").Trim();
                    text = value.Length > 0 ? "限速 " + value : "限速";
                    return true;
                }

                default:
                    if (def.Id == TrafficMap.TemplateId)
                    {
                        on = true;
                        text = "路网态势";
                        colourClass = ClassWhite;
                        if (int.TryParse(s.Get("id"), NumberStyles.None, CultureInfo.InvariantCulture, out int item))
                        {
                            TmNetwork network = TrafficMap.Get(item);
                            if (network != null && network.Known)
                            {
                                TrafficLevel worst = TrafficLevel.Unknown;
                                foreach (TmEdge edge in network.Edges)
                                {
                                    if ((int)edge.Level > (int)worst)
                                        worst = edge.Level;
                                }

                                colourClass = worst == TrafficLevel.Jam ? ClassRed : worst == TrafficLevel.Slow ? ClassYellow : worst == TrafficLevel.Free ? ClassGreen : ClassWhite;
                            }
                        }

                        return true;
                    }

                    return false;
            }
        }

        private static int ClassOf(GuideColour colour)
        {
            switch (colour)
            {
                case GuideColour.Green: return ClassGreen;
                case GuideColour.Yellow: return ClassYellow;
                case GuideColour.Red: return ClassRed;
                default: return ClassWhite;
            }
        }

        /// <summary>At most MaxText characters (a character pair of a surrogate is one), control characters made spaces.</summary>
        public static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var sb = new StringBuilder();
            int count = 0;
            for (int i = 0; i < text.Length && count < MaxText; i++)
            {
                char c = text[i];
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c).Append(text[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append('\uFFFD');
                }
                else
                {
                    sb.Append(c < ' ' || c == '\u2028' || c == '\u2029' ? ' ' : c);
                }

                count++;
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ the query and the JSON

        /// <summary>
        /// The markers within radius metres of (x, z), the nearest first (ties by kind, then place, so the order never depends on how the list was made), at most maxCount (0 or less, or more than
        /// MaxMarkers: MaxMarkers), of the kinds in the mask (0: all), as the JSON {"v":1,"rev":n,"markers":[[kind,xDm,zDm,bearing,flags,"text"],...]}.
        /// </summary>
        public static string Query(IReadOnlyList<TsMarker> all, int revision, float x, float z, float radius, int kindMask, int maxCount)
        {
            int limit = maxCount <= 0 || maxCount > MaxMarkers ? MaxMarkers : maxCount;
            var picked = new List<KeyValuePair<double, TsMarker>>();
            if (all != null && radius > 0f && !float.IsNaN(radius) && !float.IsNaN(x) && !float.IsNaN(z) && !float.IsInfinity(x) && !float.IsInfinity(z))
            {
                double r2 = (double)radius * radius, cx = x * 10.0, cz = z * 10.0;
                foreach (TsMarker marker in all)
                {
                    if (kindMask != 0 && (kindMask & MaskOf(marker.Kind)) == 0)
                        continue;
                    double dx = (marker.XDm - cx) / 10.0, dz = (marker.ZDm - cz) / 10.0, d2 = dx * dx + dz * dz;
                    if (d2 <= r2)
                        picked.Add(new KeyValuePair<double, TsMarker>(d2, marker));
                }
            }

            picked.Sort((a, b) =>
            {
                int c = a.Key.CompareTo(b.Key);
                if (c != 0)
                    return c;
                c = a.Value.Kind.CompareTo(b.Value.Kind);
                if (c != 0)
                    return c;
                c = a.Value.XDm.CompareTo(b.Value.XDm);
                return c != 0 ? c : a.Value.ZDm.CompareTo(b.Value.ZDm);
            });

            var sb = new StringBuilder(64 + Math.Min(picked.Count, limit) * 40);
            sb.Append("{\"v\":").Append(ApiVersion.ToString(CultureInfo.InvariantCulture)).Append(",\"rev\":").Append(revision.ToString(CultureInfo.InvariantCulture)).Append(",\"markers\":[");
            for (int i = 0; i < picked.Count && i < limit; i++)
            {
                TsMarker m = picked[i].Value;
                if (i > 0)
                    sb.Append(',');
                sb.Append('[').Append(m.Kind.ToString(CultureInfo.InvariantCulture)).Append(',').Append(m.XDm.ToString(CultureInfo.InvariantCulture)).Append(',').Append(m.ZDm.ToString(CultureInfo.InvariantCulture))
                    .Append(',').Append(m.Bearing.ToString(CultureInfo.InvariantCulture)).Append(',').Append(m.Flags.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendString(sb, m.Text);
                sb.Append(']');
            }

            return sb.Append("]}").ToString();
        }

        /// <summary>A JSON string with the escapes a reader needs (quotes, backslashes, control characters and the two line separators).</summary>
        public static void AppendJsonString(StringBuilder sb, string text) => AppendString(sb, text);

        private static void AppendString(StringBuilder sb, string text)
        {
            sb.Append('"');
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }
    }
}
