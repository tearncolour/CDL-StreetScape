using System;
using System.Collections.Generic;
using System.Text.Json;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The markers for the map (TsApi's content): the kind of each support, where it stands, what a variable sign shows, and the JSON a query writes, decoded and checked against the document the map is written from.</summary>
    internal static class TsMarkersTests
    {
        private static uint Hash(string id) => SignCatalog.HashOf(id);

        private static uint SupportHash(string id) => SupportCatalog.ById(id).Hash;

        private static ItemRecord Item(string sign, float x, float z, float yaw = 0f, uint support = 0u, string spec = null, uint group = 0u, int slot = 0, int lane = 0) =>
            new ItemRecord(Hash(sign), new Vec3(x, 0f, z), yaw, support, spec, 0f, group, slot, lane, 0f);

        private static string Spec(string id, params string[] keyValues)
        {
            GuideSpec spec = SignCatalog.ChoiceById(id).Def.Template.Default();
            for (int i = 0; i + 1 < keyValues.Length; i += 2)
                spec = spec.With(keyValues[i], keyValues[i + 1]);
            return spec.ToString();
        }

        private static TsMarker Only(params ItemRecord[] records)
        {
            List<TsMarker> markers = TsMarkers.Build(records);
            Program.Check(markers.Count == 1, "one marker for " + records.Length + " record(s): " + markers.Count);
            return markers.Count > 0 ? markers[0] : default;
        }

        public static void RunAll()
        {
            Program.Run("markers: the kind of the support of a board, of a group, of a screen", Kinds);
            Program.Run("markers: the place in decimetres, the bearing, what is left out", PlaceAndBearing);
            Program.Run("markers: the actual board described by a turned support's marker", TurnedFacings);
            Program.Run("markers: what a variable-message sign shows", Variables);
            Program.Run("markers: the traffic-state screen's colour follows its network", TrafficColour);
            Program.Run("markers: the query, the order, the mask, the cap", Querying);
            Program.Run("markers: the JSON decoded against the document", Json);
            Program.Run("markers: text is shortened and escaped", Text);
        }

        private static void Kinds()
        {
            Program.Check(TsMarkers.MaskOf(1) == 1 && TsMarkers.MaskOf(2) == 2 && TsMarkers.MaskOf(6) == 32 && TsMarkers.MaskOf(0) == 0 && TsMarkers.MaskOf(31) == 0, "mask bits: kind 1 is bit 0, kind 6 is bit 5");

            Program.Check(Only(Item("gb.prohib.09", 0, 0)).Kind == TsMarkers.KindPost, "a board on the planner's support (a post)");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.SinglePole))).Kind == TsMarkers.KindPost, "single post");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.DoublePole))).Kind == TsMarkers.KindPost, "double post");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.TriplePole))).Kind == TsMarkers.KindPost, "triple post");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.Cantilever))).Kind == TsMarkers.KindCantilever, "cantilever");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.DoubleCantilever))).Kind == TsMarkers.KindDoubleCantilever, "double cantilever");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.Gantry))).Kind == TsMarkers.KindGantry, "gantry");

            // A hash that is not a support in the catalogue (a pack that is gone) is the planner's choice, as it is drawn.
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, 12345u)).Kind == TsMarkers.KindPost, "an unknown support hash is the planner's");

            // The boards of one group are one support, one marker, the lead's place; the kind is the support's.
            uint gantry = SupportHash(SupportCatalog.Gantry);
            TsMarker group = Only(Item("gb.prohib.09", 10, 20, 0f, gantry, null, 77u, 0), Item("gb.prohib.07", 10, 20, 0f, gantry, null, 77u, 1), Item("gb.prohib.09", 10, 20, 0f, gantry, null, 77u, 2));
            Program.Check(group.Kind == TsMarkers.KindGantry && group.XDm == 100 && group.ZDm == 200, "three boards on a gantry are one gantry marker: " + group.Kind);
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.Cantilever), null, 5u, 0), Item("gb.prohib.07", 0, 0, 0f, SupportHash(SupportCatalog.Cantilever), null, 5u, 1)).Kind == TsMarkers.KindCantilever, "two boards on a cantilever");
            Program.Check(Only(Item("gb.prohib.09", 0, 0, 0f, SupportHash(SupportCatalog.DoubleCantilever), null, 6u, 0), Item("gb.prohib.07", 0, 0, 0f, SupportHash(SupportCatalog.DoubleCantilever), null, 6u, 1)).Kind == TsMarkers.KindDoubleCantilever, "two boards on a double cantilever");

            // Two groups are two markers; a group and a lone board are two.
            List<TsMarker> several = TsMarkers.Build(new[] { Item("gb.prohib.09", 0, 0, 0f, gantry, null, 1u), Item("gb.prohib.09", 0, 0, 0f, gantry, null, 1u, 1), Item("gb.prohib.09", 50, 0, 0f, gantry, null, 2u), Item("gb.prohib.09", 90, 0) });
            Program.Check(several.Count == 3, "two groups and a lone board are three markers: " + several.Count);

            // A board over a lane on a support of its own is drawn as an assembly of one: the kind is that assembly's (a lane screen: a gantry; and kind 6).
            TsMarker lane = Only(Item("gb.vms.lane", 0, 0, 0f, 0u, null, 0u, 0, 2));
            Program.Check(lane.Kind == TsMarkers.KindVariable, "a lane screen over a lane is a variable sign");
            TsMarker boardOverLane = Only(Item("gb.prohib.09", 0, 0, 0f, gantry, null, 0u, 0, 1));
            Program.Check(boardOverLane.Kind == TsMarkers.KindGantry, "a board over a lane on a gantry is a gantry");

            // A group with no board of a known type is not marked (as it is not drawn); one with a known board among unknown is.
            Program.Check(TsMarkers.Build(new[] { new ItemRecord(Hash("nope.not.a.sign"), new Vec3(0, 0, 0), 0f, 0u, null, 0f, 9u, 0, 0, 0f) }).Count == 0, "a group of an unknown type has no marker");
            Program.Check(TsMarkers.Build(new[] { new ItemRecord(Hash("nope.not.a.sign"), new Vec3(0, 0, 0), 0f, 0u, null, 0f, 0u, 0, 0, 0f) }).Count == 0, "a lone board of an unknown type has no marker");
            Program.Check(TsMarkers.Build(new ItemRecord[0]).Count == 0, "no records, no markers");
            Program.Check(TsMarkers.Build(new[] { Item("gb.prohib.09", 0, 0, 0f, gantry, null, 3u, 0), new ItemRecord(Hash("nope.not.a.sign"), new Vec3(0, 0, 0), 0f, gantry, null, 0f, 3u, 1, 0, 0f) }).Count == 1, "a known board among unknown ones keeps the marker");
        }

        private static void PlaceAndBearing()
        {
            // Absolute world decimetres, rounded away from zero at the half; x east, z north.
            TsMarker a = Only(Item("gb.prohib.09", 100.04f, -50.06f));
            Program.Check(a.XDm == 1000 && a.ZDm == -501, "100.04 m and -50.06 m are 1000 and -501 dm: " + a.XDm + "," + a.ZDm);
            TsMarker b = Only(Item("gb.prohib.09", -7000.26f, 6999.94f));
            Program.Check(b.XDm == -70003 && b.ZDm == 69999, "far from the origin: " + b.XDm + "," + b.ZDm);
            TsMarker c = Only(Item("gb.prohib.09", 0f, 0f));
            Program.Check(c.XDm == 0 && c.ZDm == 0, "the origin");

            // The bearing is the yaw in degrees clockwise from north: 0 faces +Z (north), a quarter turn faces +X (east).
            foreach ((float yaw, int bearing) in new[] { (0f, 0), ((float)(Math.PI / 2), 90), ((float)Math.PI, 180), ((float)(-Math.PI / 2), 270), ((float)(Math.PI * 1.5), 270), ((float)(Math.PI * 2 + 0.1), 6), (-0.1f, 354), ((float)(359.6 * Math.PI / 180), 0), ((float)(359.4 * Math.PI / 180), 359), (123456f, ((int)Math.Round(((123456.0 * 180.0 / Math.PI) % 360.0 + 360.0) % 360.0)) % 360) })
            {
                TsMarker m = Only(Item("gb.prohib.09", 0, 0, yaw));
                Program.Check(m.Bearing == bearing, "yaw " + yaw + " rad is bearing " + bearing + ": " + m.Bearing);
                Program.Check(m.Bearing >= 0 && m.Bearing <= 359, "within 0 to 359: " + m.Bearing);
            }

            // The facing is the same one the reader and the tool use: (sin yaw, cos yaw) is east and north components, and a bearing of 90 is east.
            float yawEast = (float)(Math.PI / 2);
            Program.Check(Math.Abs(Math.Sin(yawEast) - 1.0) < 1e-6 && Math.Abs(Math.Cos(yawEast)) < 1e-6 && Only(Item("gb.prohib.09", 0, 0, yawEast)).Bearing == 90, "yaw of a quarter turn faces east and is bearing 90");

            // What is not a place is left out, not thrown.
            Program.Check(TsMarkers.Build(new[] { Item("gb.prohib.09", float.NaN, 0f) }).Count == 0, "NaN x");
            Program.Check(TsMarkers.Build(new[] { Item("gb.prohib.09", 0f, float.PositiveInfinity) }).Count == 0, "infinite z");
            Program.Check(TsMarkers.Build(new[] { Item("gb.prohib.09", 0f, 0f, float.NaN) }).Count == 0, "NaN yaw");
            Program.Check(TsMarkers.Build(new[] { Item("gb.prohib.09", 0f, 0f, float.NegativeInfinity) }).Count == 0, "infinite yaw");

            // The group's marker is the lead's place (the first in the order of the group): the same whichever way the records come.
            uint gantry = SupportHash(SupportCatalog.Gantry);
            ItemRecord first = Item("gb.prohib.09", 10, 10, 0f, gantry, null, 4u, 0), second = Item("gb.prohib.07", 10, 10, 0f, gantry, null, 4u, 1);
            TsMarker one = Only(first, second), two = Only(second, first);
            Program.Check(one.Kind == two.Kind && one.XDm == two.XDm && one.ZDm == two.ZDm && one.Bearing == two.Bearing, "the order of the records does not matter");
        }

        private static void TurnedFacings()
        {
            float quarter = (float)Math.PI / 2f;
            uint pole = SupportHash(SupportCatalog.SinglePole);
            ItemRecord single = Item("gb.prohib.34", 10f, 20f, 0.3f, pole);
            single.Turn = quarter;
            TsMarker turned = Only(single);
            Program.Check(turned.Bearing == BoardFacing.Bearing(single.Yaw + quarter) && turned.XDm == 100 && turned.ZDm == 200, "a standalone turned board reports its face, with the support's anchor unchanged");

            // The slot lead need not be the main board at the top: the normal assembly order puts prohibition before guide signs. Its own turn is the marker's bearing.
            ItemRecord lead = Item("gb.guide8.01", 10f, 20f, 0.3f, pole, null, 88u, 0);
            lead.Turn = quarter;
            ItemRecord main = Item("gb.prohib.34", 10f, 20f, 0.3f, pole, null, 88u, 1);
            main.Turn = -quarter;
            foreach (ItemRecord[] order in new[] { new[] { lead, main }, new[] { main, lead } })
            {
                TsMarker marker = Only(order);
                Program.Check(marker.Bearing == BoardFacing.Bearing(lead.Yaw - quarter), "an ordinary grouped marker follows the actual first main board, independently of input and slot-lead order");
            }
            main.Manual = true;
            Program.Check(Only(lead, main).Bearing == BoardFacing.Bearing(lead.Yaw + quarter), "a manual board order changes the actual main board represented by the marker");

            // An auxiliary inserted before its owner is an anchor, not the main sign represented by the marker.
            SignDef auxiliary = null;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.Group == "auxiliary")
                {
                    auxiliary = def;
                    break;
                }
            }
            Program.Check(auxiliary != null, "an auxiliary board is available for the mixed-support case");
            if (auxiliary != null)
            {
                ItemRecord aux = Item(auxiliary.Id, 10f, 20f, 0.3f, pole, null, 88u, 0);
                aux.Turn = quarter;
                main.Manual = false;
                Program.Check(Only(aux, main).Bearing == BoardFacing.Bearing(main.Yaw - quarter), "a leading auxiliary does not supply a grouped marker's facing when a main board is present");
            }

            // A variable marker's facing and text must describe the same screen, even when another board or a dark screen leads the support.
            ItemRecord plain = Item("gb.prohib.09", 0f, 0f, 0.3f, pole, null, 89u, 0);
            plain.Turn = (float)Math.PI;
            ItemRecord off = Item("gb.vms.lane", 0f, 0f, 0.3f, pole, Spec("gb.vms.lane", "state", "off"), 89u, 1);
            off.Turn = -quarter;
            ItemRecord on = Item("gb.vms.lane.closed", 0f, 0f, 0.3f, pole, Spec("gb.vms.lane.closed", "state", "closed"), 89u, 2);
            on.Turn = quarter;
            TsMarker lit = Only(on, plain, off);
            Program.Check(lit.Kind == TsMarkers.KindVariable && lit.Text == "车道关闭" && (lit.Flags & TsMarkers.FlagOn) != 0 && lit.Bearing == BoardFacing.Bearing(on.Yaw + quarter),
                "the lit screen supplies both the words and the bearing, rather than the ordinary slot lead or the dark screen");
            on.Spec = Spec("gb.vms.lane.closed", "state", "off");
            TsMarker dark = Only(on, plain, off);
            Program.Check(dark.Kind == TsMarkers.KindVariable && dark.Flags == TsMarkers.FlagVariable && dark.Text == "" && dark.Bearing == BoardFacing.Bearing(off.Yaw - quarter),
                "when all screens are dark the first actual screen still supplies the variable marker's facing");

            // A saved Turn remains inactive on a fixed assembly; a default support must be planned for all its members before deciding this.
            ItemRecord fixedBoard = single;
            fixedBoard.SupportHash = SupportHash(SupportCatalog.DoublePole);
            Program.Check(Only(fixedBoard).Bearing == BoardFacing.Bearing(single.Yaw), "multiple posts ignore an old stored turn");
            lead.SupportHash = main.SupportHash = 0u;
            main = Item("guide.exit.advance", 10f, 20f, lead.Yaw, 0u, null, lead.Group, 1);
            Program.Check(Only(lead, main).Bearing == BoardFacing.Bearing(lead.Yaw), "an automatic support grown to several posts uses the complete layout, not the first board alone");
        }

        private static void Variables()
        {
            // The default text screen: two lines, a warning (yellow): on, class 1, the words of both lines.
            TsMarker text = Only(Item("gb.vms.text", 0, 0));
            Program.Check(text.Kind == TsMarkers.KindVariable, "a text screen is kind 6");
            Program.Check(text.Flags == (TsMarkers.FlagVariable | TsMarkers.FlagOn | (TsMarkers.ClassYellow << TsMarkers.ClassShift)) && text.Flags == 7, "on, yellow: " + text.Flags);
            Program.Check(text.Text == "前方事故 减速慢行", "the words of the two lines: " + text.Text);

            foreach ((string cls, int colourClass) in new[] { ("info", 0), ("warn", 1), ("red", 2), ("white", 3) })
            {
                TsMarker m = Only(Item("gb.vms.text", 0, 0, 0f, 0u, Spec("gb.vms.text", "cls", cls)));
                Program.Check(((m.Flags >> TsMarkers.ClassShift) & 3) == colourClass && (m.Flags & TsMarkers.FlagOn) != 0, cls + " is colour class " + colourClass + ": flags " + m.Flags);
            }

            // Empty lines: off (4.11.1), a variable sign with nothing shown, no words, class 0.
            TsMarker empty = Only(Item("gb.vms.text", 0, 0, 0f, 0u, Spec("gb.vms.text", "l1", "", "l2", "", "l3", "  ", "l4", "")));
            Program.Check(empty.Kind == TsMarkers.KindVariable && empty.Flags == TsMarkers.FlagVariable && empty.Text == "", "a screen with no words is off: flags " + empty.Flags);

            // Lines 3 and 4 are shown when set; blank lines in between are left out of the one line.
            TsMarker four = Only(Item("gb.vms.text", 0, 0, 0f, 0u, Spec("gb.vms.text", "l1", "甲", "l2", "", "l3", "乙", "l4", "丙")));
            Program.Check(four.Text == "甲 乙 丙", "three lines of four: " + four.Text);

            // Lane screens: the state is the colour and the words; off is off.
            foreach ((string state, int colourClass, string words) in new[] { ("pass", 0, "车道可通行"), ("closed", 2, "车道关闭"), ("left", 1, "向左并道"), ("right", 1, "向右并道") })
            {
                TsMarker m = Only(Item("gb.vms.lane", 0, 0, 0f, 0u, Spec("gb.vms.lane", "state", state)));
                Program.Check(m.Flags == (TsMarkers.FlagVariable | TsMarkers.FlagOn | (colourClass << TsMarkers.ClassShift)) && m.Text == words, state + ": flags " + m.Flags + " " + m.Text);
            }

            TsMarker off = Only(Item("gb.vms.lane", 0, 0, 0f, 0u, Spec("gb.vms.lane", "state", "off")));
            Program.Check(off.Flags == TsMarkers.FlagVariable && off.Text == "", "a lane screen that is off: flags " + off.Flags);
            TsMarker closedByDefault = Only(Item("gb.vms.lane.closed", 0, 0));
            Program.Check(closedByDefault.Kind == TsMarkers.KindVariable && (closedByDefault.Flags & TsMarkers.FlagOn) != 0 && ((closedByDefault.Flags >> TsMarkers.ClassShift) & 3) == TsMarkers.ClassRed && closedByDefault.Text == "车道关闭", "the lane-closed screen is red and says so: " + closedByDefault.Flags + " " + closedByDefault.Text);

            // The speed screen: red; the limit in the words, or only the word for the limit with no value.
            TsMarker speed = Only(Item("gb.vms.speed", 0, 0));
            Program.Check(speed.Flags == (TsMarkers.FlagVariable | TsMarkers.FlagOn | (TsMarkers.ClassRed << TsMarkers.ClassShift)) && speed.Text == "限速 60", "speed 60: " + speed.Flags + " " + speed.Text);
            TsMarker ring = Only(Item("gb.vms.speed", 0, 0, 0f, 0u, Spec("gb.vms.speed", "value", "")));
            Program.Check((ring.Flags & TsMarkers.FlagOn) != 0 && ring.Text == "限速", "a ring alone is on, and says only the word: " + ring.Text);

            // A gantry of lane screens (a group): kind 6; on when any is; the colour and words of the first that is on.
            uint gantry = SupportHash(SupportCatalog.Gantry);
            TsMarker lanes = Only(
                Item("gb.vms.lane", 0, 0, 0f, gantry, Spec("gb.vms.lane", "state", "off"), 8u, 0, 1),
                Item("gb.vms.lane", 0, 0, 0f, gantry, Spec("gb.vms.lane", "state", "closed"), 8u, 1, 2),
                Item("gb.vms.lane", 0, 0, 0f, gantry, Spec("gb.vms.lane", "state", "pass"), 8u, 2, 3));
            Program.Check(lanes.Kind == TsMarkers.KindVariable && (lanes.Flags & TsMarkers.FlagOn) != 0 && ((lanes.Flags >> TsMarkers.ClassShift) & 3) == TsMarkers.ClassRed && lanes.Text == "车道关闭", "the first screen that is on speaks for the gantry: " + lanes.Flags + " " + lanes.Text);
            TsMarker allOff = Only(
                Item("gb.vms.lane", 0, 0, 0f, gantry, Spec("gb.vms.lane", "state", "off"), 9u, 0, 1),
                Item("gb.vms.lane", 0, 0, 0f, gantry, Spec("gb.vms.lane", "state", "off"), 9u, 1, 2));
            Program.Check(allOff.Kind == TsMarkers.KindVariable && allOff.Flags == TsMarkers.FlagVariable, "a gantry of screens that are all off: " + allOff.Flags);

            // A variable screen among ordinary boards on one support: the support is a variable sign (kind 6 wins); a plain board alone has flags 0 and no words.
            TsMarker mixed = Only(Item("gb.prohib.09", 0, 0, 0f, gantry, null, 10u, 0), Item("gb.vms.speed", 0, 0, 0f, gantry, null, 10u, 1));
            Program.Check(mixed.Kind == TsMarkers.KindVariable && (mixed.Flags & TsMarkers.FlagOn) != 0 && mixed.Text == "限速 60", "a screen among boards: " + mixed.Kind + " " + mixed.Text);
            TsMarker plain = Only(Item("gb.prohib.09", 0, 0, 0f, gantry, null, 11u, 0), Item("gb.prohib.07", 0, 0, 0f, gantry, null, 11u, 1));
            Program.Check(plain.Flags == 0 && plain.Text == "", "ordinary boards: no flags, no words");

            // A garbled spec is the defaults, not a throw.
            TsMarker garbled = Only(Item("gb.vms.text", 0, 0, 0f, 0u, ";;==;l1"));
            Program.Check(garbled.Kind == TsMarkers.KindVariable, "a garbled spec still marks");
        }

        private static void TrafficColour()
        {
            int id = 731001;
            string spec(int number) => Spec(TrafficMap.TemplateId, "id", number.ToString(), "rev", "1");
            try
            {
                // Not read yet (no network for the item, or no item number): on, white, the name.
                TsMarker unread = Only(Item(TrafficMap.TemplateId, 0, 0, 0f, 0u, spec(id)));
                Program.Check(unread.Kind == TsMarkers.KindVariable && unread.Flags == (TsMarkers.FlagVariable | TsMarkers.FlagOn | (TsMarkers.ClassWhite << TsMarkers.ClassShift)) && unread.Text == "路网态势", "unread: on, white: " + unread.Flags);
                TsMarker noItem = Only(Item(TrafficMap.TemplateId, 0, 0));
                Program.Check(noItem.Flags == unread.Flags, "no item number is the same as unread");

                TmNetwork Net(params TrafficLevel[] levels)
                {
                    var net = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
                    foreach (TrafficLevel level in levels)
                        net.Edges.Add(new TmEdge { Level = level, Points = new float[] { 0, 0, 0, 100 } });
                    return net;
                }

                int Colour(TmNetwork net, int number)
                {
                    TrafficMap.Set(number, net, 1);
                    TsMarker m = Only(Item(TrafficMap.TemplateId, 0, 0, 0f, 0u, spec(number)));
                    Program.Check((m.Flags & TsMarkers.FlagOn) != 0 && (m.Flags & TsMarkers.FlagVariable) != 0 && m.Text == "路网态势", "on, variable, named");
                    return (m.Flags >> TsMarkers.ClassShift) & 3;
                }

                Program.Check(Colour(Net(TrafficLevel.Free, TrafficLevel.Free), id + 1) == TsMarkers.ClassGreen, "all free: green");
                Program.Check(Colour(Net(TrafficLevel.Free, TrafficLevel.Slow, TrafficLevel.Free), id + 2) == TsMarkers.ClassYellow, "one slow: yellow");
                Program.Check(Colour(Net(TrafficLevel.Slow, TrafficLevel.Jam, TrafficLevel.Free), id + 3) == TsMarkers.ClassRed, "one jam: red");
                Program.Check(Colour(Net(TrafficLevel.Unknown), id + 4) == TsMarkers.ClassWhite, "nothing known: white");
                Program.Check(Colour(new TmNetwork(), id + 5) == TsMarkers.ClassWhite, "a network that was not read: white");
                Program.Check(Colour(Net(), id + 6) == TsMarkers.ClassWhite, "a network with no roads: white");

                // The colour follows a new publication (the revision is the item's settings, so the markers are made again).
                TrafficMap.Set(id + 7, Net(TrafficLevel.Free), 1);
                Program.Check(((Only(Item(TrafficMap.TemplateId, 0, 0, 0f, 0u, spec(id + 7))).Flags >> TsMarkers.ClassShift) & 3) == TsMarkers.ClassGreen, "first free");
                TrafficMap.Set(id + 7, Net(TrafficLevel.Jam), 2);
                Program.Check(((Only(Item(TrafficMap.TemplateId, 0, 0, 0f, 0u, spec(id + 7))).Flags >> TsMarkers.ClassShift) & 3) == TsMarkers.ClassRed, "then jammed");
            }
            finally
            {
                TrafficMap.Clear();
            }
        }

        private static TsMarker M(int kind, int x, int z, int bearing = 0, int flags = 0, string text = "") => new TsMarker { Kind = kind, XDm = x, ZDm = z, Bearing = bearing, Flags = flags, Text = text };

        private static List<int[]> Decode(string json, out int version, out int revision, out List<string> texts)
        {
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                version = doc.RootElement.GetProperty("v").GetInt32();
                revision = doc.RootElement.GetProperty("rev").GetInt32();
                var rows = new List<int[]>();
                texts = new List<string>();
                foreach (JsonElement row in doc.RootElement.GetProperty("markers").EnumerateArray())
                {
                    Program.Check(row.GetArrayLength() == 6, "six values in a marker: " + row.GetArrayLength());
                    rows.Add(new[] { row[0].GetInt32(), row[1].GetInt32(), row[2].GetInt32(), row[3].GetInt32(), row[4].GetInt32() });
                    texts.Add(row[5].GetString());
                }

                return rows;
            }
        }

        private static void Querying()
        {
            // Absolute decimetres: a marker at (1000.0 m, -2000.0 m) is 10000 and -20000, whatever the centre of the query.
            var all = new List<TsMarker> { M(2, 10000, -20000, 90), M(1, 10300, -20000), M(4, 10000, -19000), M(1, 20000, -20000), M(6, 9000, -20000, 0, 3, "字") };
            List<int[]> rows = Decode(TsMarkers.Query(all, 5, 1000f, -2000f, 150f, 0, 10), out int v, out int rev, out List<string> texts);
            Program.Check(v == 1 && rev == 5, "version 1, revision 5");
            Program.Check(rows.Count == 4, "within 150 m of the centre: the four near ones, not the one 1000 m away: " + rows.Count);
            Program.Check(rows[0][0] == 2 && rows[0][1] == 10000 && rows[0][2] == -20000 && rows[0][3] == 90, "nearest first, the coordinates absolute: " + rows[0][0] + " " + rows[0][1] + " " + rows[0][2]);
            Program.Check(rows[1][0] == 1 && rows[1][1] == 10300 && rows[2][0] == 4 && rows[3][0] == 6, "then 30 m, then the two at 100 m with the lower kind first: " + string.Join(",", rows.ConvertAll(r => r[0].ToString())));

            // The edge of the radius is in; a hair beyond is out.
            Program.Check(Decode(TsMarkers.Query(new[] { M(1, 10300, -20000) }, 0, 1000f, -2000f, 30f, 0, 10), out _, out _, out _).Count == 1, "at exactly the radius");
            Program.Check(Decode(TsMarkers.Query(new[] { M(1, 10301, -20000) }, 0, 1000f, -2000f, 30f, 0, 10), out _, out _, out _).Count == 0, "10 cm beyond it");

            // The mask: kind 2 is bit 1; 0 is all; a mask with no kind in the list gives none; several bits.
            Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, 5000f, TsMarkers.MaskOf(2), 10), out _, out _, out _).Count == 1, "only the cantilever");
            Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, 5000f, TsMarkers.MaskOf(1) | TsMarkers.MaskOf(6), 10), out _, out _, out _).Count == 3, "posts and variable signs");
            Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, 5000f, TsMarkers.MaskOf(5), 10), out _, out _, out _).Count == 0, "attached: none yet");
            Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, 5000f, 0, 10), out _, out _, out _).Count == 5, "mask 0: all");

            // The cap: the count asked for, 64 at the most, 64 for 0 or less.
            var many = new List<TsMarker>();
            for (int i = 0; i < 100; i++)
                many.Add(M(1, i * 10, 0));
            Program.Check(Decode(TsMarkers.Query(many, 0, 0f, 0f, 5000f, 0, 3), out _, out _, out _).Count == 3, "three asked");
            Program.Check(Decode(TsMarkers.Query(many, 0, 0f, 0f, 5000f, 0, 1000), out _, out _, out _).Count == TsMarkers.MaxMarkers, "1000 asked: 64");
            Program.Check(Decode(TsMarkers.Query(many, 0, 0f, 0f, 5000f, 0, 0), out _, out _, out _).Count == TsMarkers.MaxMarkers, "0 asked: 64");
            Program.Check(Decode(TsMarkers.Query(many, 0, 0f, 0f, 5000f, 0, -5), out _, out _, out _).Count == TsMarkers.MaxMarkers, "-5 asked: 64");
            List<int[]> nearest = Decode(TsMarkers.Query(many, 0, 0f, 0f, 5000f, 0, 5), out _, out _, out _);
            Program.Check(nearest.Count == 5 && nearest[0][1] == 0 && nearest[4][1] == 40, "the cap keeps the nearest");

            // The order never depends on how the list was made: the same JSON from any permutation, including markers at the same distance.
            var tied = new List<TsMarker> { M(1, 100, 0), M(1, -100, 0), M(2, 0, 100), M(2, 0, -100), M(4, 0, 100), M(6, 100, 0), M(1, 50, 0) };
            string expected = TsMarkers.Query(tied, 1, 0f, 0f, 100f, 0, 64);
            var rng = new Random(11);
            for (int round = 0; round < 40; round++)
            {
                var shuffled = new List<TsMarker>(tied);
                for (int i = shuffled.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    TsMarker t = shuffled[i];
                    shuffled[i] = shuffled[j];
                    shuffled[j] = t;
                }

                Program.Check(TsMarkers.Query(shuffled, 1, 0f, 0f, 100f, 0, 64) == expected, "the same JSON from any order of the list");
            }

            // Nothing: an empty list, no list, a radius that is none, a place that is not one.
            Program.Check(TsMarkers.Query(new List<TsMarker>(), 0, 0f, 0f, 100f, 0, 10) == "{\"v\":1,\"rev\":0,\"markers\":[]}", "an empty list: " + TsMarkers.Query(new List<TsMarker>(), 0, 0f, 0f, 100f, 0, 10));
            Program.Check(TsMarkers.Query(null, 3, 0f, 0f, 100f, 0, 10) == "{\"v\":1,\"rev\":3,\"markers\":[]}", "no list");
            foreach (float radius in new[] { 0f, -5f, float.NaN })
                Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, radius, 0, 10), out _, out _, out _).Count == 0, "radius " + radius + " finds nothing");
            Program.Check(Decode(TsMarkers.Query(all, 0, float.NaN, 0f, 100f, 0, 10), out _, out _, out _).Count == 0, "NaN place finds nothing");
            Program.Check(Decode(TsMarkers.Query(all, 0, 0f, float.PositiveInfinity, 100f, 0, 10), out _, out _, out _).Count == 0, "infinite place finds nothing");
            Program.Check(Decode(TsMarkers.Query(all, 0, 1000f, -2000f, float.PositiveInfinity, 0, 10), out _, out _, out _).Count == 5, "an infinite radius is everything (still capped)");
        }

        private static void Json()
        {
            // What the document (MINIMAP-REQUIREMENTS.md, 2.1) promises, field by field, on markers made from real records.
            uint gantry = SupportHash(SupportCatalog.Gantry);
            List<TsMarker> built = TsMarkers.Build(new[]
            {
                Item("gb.prohib.09", 100f, 200f, (float)(Math.PI / 2)),
                Item("gb.prohib.09", 300f, -400f, 0f, SupportHash(SupportCatalog.Cantilever)),
                Item("gb.vms.speed", -50f, 60f, (float)Math.PI, gantry, Spec("gb.vms.speed", "value", "80")),
                Item("gb.vms.text", 5f, 5f, 0f, 0u, Spec("gb.vms.text", "l1", "前方\"事故\"", "l2", "a\\b", "cls", "red"))
            });
            Program.Check(built.Count == 4, "four markers");
            string json = TsMarkers.Query(built, 42, 0f, 0f, 1000f, 0, 64);
            List<int[]> rows = Decode(json, out int v, out int rev, out List<string> texts);
            Program.Check(v == 1 && rev == 42 && rows.Count == 4, "decoded: " + json);
            foreach (int[] row in rows)
            {
                Program.Check(row[0] >= 1 && row[0] <= 6, "kind 1 to 6: " + row[0]);
                Program.Check(row[3] >= 0 && row[3] <= 359, "bearing 0 to 359: " + row[3]);
                Program.Check(row[4] >= 0 && row[4] <= 15, "flags in 4 bits: " + row[4]);
                Program.Check(((row[4] & TsMarkers.FlagVariable) != 0) == (row[0] == TsMarkers.KindVariable), "bit 0 is set exactly for the variable signs");
                Program.Check((row[4] & TsMarkers.FlagOn) == 0 || (row[4] & TsMarkers.FlagVariable) != 0, "only a variable sign is on");
            }

            // The ordinary boards have flags 0 and no words ("the JSON stays small").
            int plain = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i][0] != TsMarkers.KindVariable)
                {
                    plain++;
                    Program.Check(rows[i][4] == 0 && texts[i] == "", "a plain sign carries nothing but its place");
                }
            }

            Program.Check(plain == 2, "two plain signs: " + plain);

            // The words survive the JSON, quotes and backslashes included.
            int at = texts.IndexOf("前方\"事故\" a\\b");
            Program.Check(at >= 0, "the quotes and the backslash are escaped and come back: " + string.Join("|", texts));
            Program.Check(texts.Contains("限速 80"), "the speed words");

            // The whole string is plain JSON: no raw line breaks, parseable by a strict parser.
            Program.Check(json.IndexOf('\n') < 0 && json.IndexOf('\r') < 0, "one line");

            // Numbers are written with no culture: a string that has no comma decimal point or group separator.
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                string german = TsMarkers.Query(new[] { M(1, -12345, 67890, 270, 0, "") }, 1234567, 0f, 0f, 10000f, 0, 5);
                Program.Check(german == "{\"v\":1,\"rev\":1234567,\"markers\":[[1,-12345,67890,270,0,\"\"]]}", "the same in another culture: " + german);
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        }

        private static void Text()
        {
            // Built from code points, not typed: some characters of this test (the line separators, a lone half of a pair) do not survive being written in a source file.
            string smile = char.ConvertFromUtf32(0x1F600);
            string lineSeparator = ((char)0x2028).ToString(), paragraphSeparator = ((char)0x2029).ToString();
            string loneHigh = ((char)0xD83D).ToString(), loneLow = ((char)0xDE00).ToString(), replacement = ((char)0xFFFD).ToString();
            string ctl1 = ((char)1).ToString(), ctl31 = ((char)31).ToString();

            // Shortened to 24 characters, a pair of a surrogate counted as one, never cut in the middle of one.
            string long40 = new string('x', 40).Replace("x", "字");
            Program.Check(TsMarkers.Shorten(long40) == long40.Substring(0, 24), "40 characters: 24");
            Program.Check(TsMarkers.Shorten(long40.Substring(0, 24)) == long40.Substring(0, 24) && TsMarkers.Shorten(long40.Substring(0, 23)) == long40.Substring(0, 23), "24 and 23 are whole");
            string pairs = string.Concat(System.Linq.Enumerable.Repeat(smile, 30));
            string shortPairs = TsMarkers.Shorten(pairs);
            Program.Check(shortPairs.Length == 48 && shortPairs == string.Concat(System.Linq.Enumerable.Repeat(smile, 24)), "30 pairs: 24 whole pairs: " + shortPairs.Length);
            string split = new string('a', 23) + smile + "b";
            string cut = TsMarkers.Shorten(split);
            Program.Check(cut == new string('a', 23) + smile, "the 24th character is a pair: kept whole: " + cut.Length);

            // A lone half of a pair becomes the replacement character: the JSON is always valid text.
            Program.Check(TsMarkers.Shorten("a" + loneHigh + "z") == "a" + replacement + "z" && TsMarkers.Shorten(loneLow) == replacement, "a lone surrogate");
            Program.Check(TsMarkers.Shorten("a\nb\tc\rd e f") == "a b c d e f", "line breaks and tabs are spaces");
            Program.Check(TsMarkers.Shorten("a" + lineSeparator + "b" + paragraphSeparator + "c") == "a b c", "the separators are spaces");
            Program.Check(TsMarkers.Shorten(null) == "" && TsMarkers.Shorten("") == "", "nothing");

            // Escaped in the JSON: quote, backslash, controls; and everything decodes back to what was given.
            string nasty = "q\"b\\c" + ctl1 + "d" + ctl31 + "e" + lineSeparator + "f" + paragraphSeparator + "g" + smile + "h/i";
            string json = TsMarkers.Query(new[] { M(6, 0, 0, 0, 3, nasty) }, 0, 0f, 0f, 10f, 0, 1);
            Decode(json, out _, out _, out List<string> texts);
            Program.Check(texts.Count == 1 && texts[0] == nasty, "the text comes back as it went in: " + json);
            Program.Check(json.IndexOf(lineSeparator, StringComparison.Ordinal) < 0 && json.IndexOf(paragraphSeparator, StringComparison.Ordinal) < 0 && json.IndexOf((char)1) < 0 && json.IndexOf((char)31) < 0, "no raw separators or controls in the JSON");

            // The text of a marker made from a screen is shortened before it is written: a screen of four long lines.
            string ten = "一二三四五六七八九十";
            TsMarker m = TsMarkers.Build(new[] { new ItemRecord(Hash("gb.vms.text"), new Vec3(0, 0, 0), 0f, 0u, Spec("gb.vms.text", "l1", ten, "l2", ten, "l3", ten, "l4", ten), 0f, 0u, 0, 0, 0f) })[0];
            Program.Check(m.Text.Length == TsMarkers.MaxText && m.Text.StartsWith(ten + " " + ten), "four lines of ten: the first 24 characters: " + m.Text);
        }
    }
}
