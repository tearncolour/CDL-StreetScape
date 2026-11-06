using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo
{
    public static partial class Bilingual
    {
        private static Row R(string id, params string[] keys) => new Row { Id = id, Keys = keys };

        private sealed class Row
        {
            public string Id;
            public string[] Keys;
            public bool Capitals;
            public Func<GuideSpec, bool> Available;
        }

        /// <summary>
        /// The templates that have an English line, by id: the keys of the spec fields whose Chinese text it goes under (none: only the nouns of Table A.1 the template writes itself), whether it is set in
        /// capitals, and the forms of the board that have room for it. A template is added here when its layout takes the blocks of RoadGuide.Line, Auxiliary.Line or GuideKit.PlaceName at their own
        /// height (DESIGN-bilingual.md, "Opting in"); the test of the overlaps (BilingualTests) is the gate. Not here, and why: 路7 (a board pointed at one end, of a fixed height), 路27 and the direction
        /// marks (one character in a square of the standard's size), 路12 and the other pictograms, the exit number plates (their letters are the number's), 路54 (its characters are 0.7 h cells written by hand), the
        /// expressway entrance, fork and hub boards 路29 to 路37 and 路47 to 路51 (the places of a row are drawn as one text, "汕头 深圳", or by hand; or the board has a fixed size): each needs its own work.
        /// </summary>
        private static readonly Row[] kOptIns =
        {
            R("gb.guide8.01", "to1", "to2", "tl1", "tl2", "tr1", "tr2"),
            R("gb.guide8.02", "tnw1", "tnw2", "tne1", "tne2", "tw1", "tw2", "te1", "te2"),
            R("gb.guide8.203c", "to1", "to2", "tl1", "tl2", "tr1", "tr2"),
            R("gb.guide8.03", "to1", "to2", "tl1", "tl2", "tr1", "tr2"),
            R("gb.guide8.04", "to1", "to2", "to3", "to4", "to5", "to6"),
            R("gb.guide8.04b", "to1", "to2", "to3", "to4", "to5", "to6"),
            new Row { Id = "gb.guide8.06", Keys = new[] { "name" }, Available = s => !s.Is("layout", "v") },
            R("gb.guide8.08", "to1", "to2", "to3"),
            R("gb.guide8.09", "name"),
            R("gb.guide8.10", "name"),
            R("gb.guide8.11", "name"),
            new Row { Id = "guide.exit.advance", Keys = new[] { "to1", "to2" }, Capitals = true },
            new Row { Id = "guide.exit.direction", Keys = new[] { "to1", "to2" }, Capitals = true },
            new Row { Id = "guide.exit.sign", Keys = new string[0], Capitals = true },
            R("gb.guide9.38", "to1", "to2", "to3"),
            R("gb.guide9.38b", "to1", "to2", "to3"),
            R("gb.guide9.39", "to1", "to2", "to3"),
            R("gb.aux.text", "text"),
            R("gb.aux.02", "text"),
            R("gb.aux.11", "l1", "l2")
        };

        /// <summary>The ids of the table.</summary>
        public static IEnumerable<string> OptInIds
        {
            get
            {
                foreach (Row row in kOptIns)
                    yield return row.Id;
            }
        }

        /// <summary>The rows of the table that could not be applied, with the reason (a key the template does not have): the catalogue is made without them, and a test fails.</summary>
        public static readonly List<string> Failed = new List<string>();

        /// <summary>Gives the templates of the table their English line (once, as the catalogue is made); a template the table names that the list does not have is left out.</summary>
        public static void OptIn(IEnumerable<GuideTemplate> templates)
        {
            var byId = new Dictionary<string, GuideTemplate>();
            foreach (GuideTemplate template in templates)
                byId[template.Id] = template;
            foreach (Row row in kOptIns)
            {
                if (!byId.TryGetValue(row.Id, out GuideTemplate template))
                    continue;
                try
                {
                    Add(template, row.Capitals, row.Available, row.Keys);
                }
                catch (ArgumentException ex)
                {
                    Failed.Add(ex.Message);
                }
            }
        }
    }
}
