using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CDLC.TS.Geo
{
    /// <summary>The nearest guide board ahead of a vehicle that faces it: what a map shows in one line ("ahead: exit advance 500 m").</summary>
    public sealed class TsAhead
    {
        /// <summary>The board's code in the standard ("路42") and its name ("出口预告"); the words on it, in reading order; how far ahead it stands (m, rounded); where; the way it faces.</summary>
        public string Code = "", Name = "", Text = "";

        public int Distance;
        public int XDm, ZDm, Bearing;
    }

    /// <summary>
    /// The guide board ahead (CDLC.TS 0.9.0, TsApi.AheadJson, the "later addition" of MINIMAP-REQUIREMENTS.md section 5): of the guide boards (GB 5768.2 chapters 8 and 9, the group "guide") the one
    /// that a vehicle at a place, heading some way, would see next: it faces the vehicle's way (the board faces the traffic that looks at it, so the vehicle runs against the board's facing, within
    /// 45 degrees), stands ahead of it within a distance, and not more than 30 m to the side. Of several, the nearest ahead. Pure, so that it is checked offline.
    /// </summary>
    public static class TsAheadRule
    {
        /// <summary>The farthest ahead a board is looked for, whatever is asked (m), and how far to the side one may stand (m).</summary>
        public const float MaxAhead = 1500f, MaxSide = 30f;

        /// <summary>How far from the vehicle's way the board's facing may be (degrees): a board is not read from the side.</summary>
        public const float FacingDegrees = 45f;

        /// <summary>The most characters of the words (a map line is short).</summary>
        public const int MaxText = 40;

        /// <summary>The guide board ahead, or null. headingDeg is clockwise from north, as the markers' bearing; maxDist is kept to MaxAhead. Items of a type that is not a guide board are ignored.</summary>
        public static TsAhead Find(IEnumerable<ItemRecord> records, float x, float z, float headingDeg, float maxDist)
        {
            if (records == null || !IsFinite(x) || !IsFinite(z) || !IsFinite(headingDeg) || !(maxDist > 0f))
                return null;
            maxDist = Math.Min(maxDist, MaxAhead);
            double heading = headingDeg * Math.PI / 180.0;
            double dx = Math.Sin(heading), dz = Math.Cos(heading);
            double cosLimit = Math.Cos(FacingDegrees * Math.PI / 180.0);

            ItemRecord best = default;
            float bestYaw = 0f;
            double bestAhead = double.MaxValue;
            bool found = false;
            var items = new List<ItemRecord>(records);
            float[] facings = BoardFacing.Yaws(items);
            for (int i = 0; i < items.Count; i++)
            {
                ItemRecord record = items[i];
                if (!record.Position.IsFinite || float.IsNaN(record.Yaw) || float.IsInfinity(record.Yaw))
                    continue;
                SignDef def = SignCatalog.ByHash(record.TypeHash);
                if (def == null || def.Group != "guide")
                    continue;

                // The traffic that sees a board runs against the way the board faces.
                float yaw = facings[i];
                if (!IsFinite(yaw))
                    continue;
                double toX = -Math.Sin(yaw), toZ = -Math.Cos(yaw);
                if (dx * toX + dz * toZ < cosLimit)
                    continue;

                double vx = record.Position.X - x, vz = record.Position.Z - z;
                double ahead = vx * dx + vz * dz, side = vx * dz - vz * dx;
                if (ahead <= 0.0 || ahead > maxDist || Math.Abs(side) > MaxSide)
                    continue;

                // The nearest ahead; boards of one support stand at one place: the one that hangs highest (the lowest slot) speaks.
                bool nearer = ahead < bestAhead - 0.01 || (Math.Abs(ahead - bestAhead) <= 0.01 && found && (record.Slot < best.Slot || (record.Slot == best.Slot && record.TypeHash < best.TypeHash)));
                if (!found || nearer)
                {
                    best = record;
                    bestYaw = yaw;
                    bestAhead = ahead;
                    found = true;
                }
            }

            if (!found)
                return null;
            SignDef chosen = SignCatalog.ByHash(best.TypeHash);
            return new TsAhead
            {
                Code = chosen.Code ?? "",
                Name = chosen.Zh ?? "",
                Text = WordsOf(SignInstances.Of(chosen, best.Spec)),
                Distance = (int)Math.Round(bestAhead, MidpointRounding.AwayFromZero),
                XDm = (int)Math.Round(best.Position.X * 10.0, MidpointRounding.AwayFromZero),
                ZDm = (int)Math.Round(best.Position.Z * 10.0, MidpointRounding.AwayFromZero),
                Bearing = BoardFacing.Bearing(bestYaw)
            };
        }

        private static bool IsFinite(float v) => !(float.IsNaN(v) || float.IsInfinity(v));

        /// <summary>
        /// The words on a board, in reading order: the text elements of its parts (the main board first), a part's from the top to the bottom and, on one line, from the left to the right, each
        /// word once in a row, joined by a space, at most MaxText characters. A board with no text gives "".
        /// </summary>
        public static string WordsOf(SignInstance instance)
        {
            if (instance == null || instance.Board == null)
                return "";
            var words = new List<string>();
            foreach (GuidePart part in instance.Board.Parts)
            {
                var texts = new List<GuideElement>();
                foreach (GuideElement element in part.Elements)
                {
                    if (element.Kind == GuideElementKind.Text && element.Mode != GuideTextMode.Line && !string.IsNullOrWhiteSpace(element.Text))
                        texts.Add(element);
                }

                texts.Sort((a, b) =>
                {
                    int c = Math.Round(a.Y / 0.05f).CompareTo(Math.Round(b.Y / 0.05f));
                    return c != 0 ? c : a.X.CompareTo(b.X);
                });
                foreach (GuideElement element in texts)
                {
                    string word = element.Text.Trim();
                    if (words.Count == 0 || words[words.Count - 1] != word)
                        words.Add(word);
                }
            }

            return TsMarkers.Shorten(string.Join(" ", words)).Trim();
        }

        // ------------------------------------------------------------------ the JSON

        /// <summary>
        /// {"v":1,"rev":n,"ahead":null} or {"v":1,"rev":n,"ahead":{"code":"路42","name":"出口预告","text":"G15 汕头 深圳 500m","d":312,"x":-26191,"z":24774,"b":184}}: d in metres, x and z in
        /// absolute world decimetres (as the markers), b the way the board faces (degrees clockwise from north).
        /// </summary>
        public static string Json(TsAhead ahead, int revision)
        {
            var sb = new StringBuilder(160);
            sb.Append("{\"v\":").Append(TsMarkers.ApiVersion.ToString(CultureInfo.InvariantCulture)).Append(",\"rev\":").Append(revision.ToString(CultureInfo.InvariantCulture)).Append(",\"ahead\":");
            if (ahead == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("{\"code\":");
                TsMarkers.AppendJsonString(sb, ahead.Code);
                sb.Append(",\"name\":");
                TsMarkers.AppendJsonString(sb, ahead.Name);
                sb.Append(",\"text\":");
                TsMarkers.AppendJsonString(sb, ahead.Text);
                sb.Append(",\"d\":").Append(ahead.Distance.ToString(CultureInfo.InvariantCulture)).Append(",\"x\":").Append(ahead.XDm.ToString(CultureInfo.InvariantCulture))
                    .Append(",\"z\":").Append(ahead.ZDm.ToString(CultureInfo.InvariantCulture)).Append(",\"b\":").Append(ahead.Bearing.ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            return sb.Append('}').ToString();
        }
    }
}
