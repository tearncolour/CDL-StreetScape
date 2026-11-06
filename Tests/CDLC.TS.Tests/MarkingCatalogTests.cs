using System;
using System.Collections.Generic;
using System.Text;
using CDLC.TS.Geo;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The marking templates (MarkingTemplates): the catalogue, every setting of every marking, the plans they make, the grade and the path.</summary>
    internal static class MarkingCatalogTests
    {
        public static void RunAll()
        {
            Program.Run("marking catalogue: ids, hashes, names, codes and the group", Catalogue);
            Program.Run("marking catalogue: every marking builds sound paint at its defaults", Defaults);
            Program.Run("marking catalogue: every choice and flag of every marking builds", EveryOption);
            Program.Run("marking catalogue: typed numbers are held to the standard's ranges", Numbers);
            Program.Run("marking catalogue: the grade, the path, the size and the words", GradePathWords);
        }

        private static void Sound(MarkingPlan plan, string what)
        {
            // The area that clears the game's markings is the one with no paint: it has an outline instead.
            if (what == VanillaClear.Id || what.StartsWith(VanillaClear.Id + " ", StringComparison.Ordinal))
            {
                Program.Check(plan != null && plan.Parts.Count == 0 && plan.Outlines.Count == 1 && plan.Outlines[0].Count >= 3, what + ": no paint, an outline");
                return;
            }

            // A stretch's record (0.12.44) keeps the settings it was marked with and draws nothing.
            if (what == AutoMarking.Id + " " + AutoMarking.RecordKey + "=1")
            {
                Program.Check(plan != null && plan.Parts.Count == 0 && plan.Outlines.Count == 0, what + ": nothing");
                return;
            }

            Program.Check(plan != null && plan.Parts.Count > 0 && plan.TriangleCount > 0, what + ": has paint");
            if (plan == null)
                return;
            foreach (MarkingPart part in plan.Parts)
            {
                Program.Check(part.Mesh.VertexCount < 60000, what + ": under the vertex limit of a mesh (" + part.Mesh.VertexCount + ")");
                Program.Check(part.Mesh.Indices.Count % 3 == 0, what + ": whole triangles");
                foreach (Vec3 p in part.Mesh.Positions)
                    Program.Check(p.IsFinite && Math.Abs(p.X) < 500f && Math.Abs(p.Z) < 500f && Math.Abs(p.Y) < 200f, what + ": finite and inside 500 m: " + p);
                for (int i = 0; i < part.Mesh.Indices.Count; i++)
                    Program.Check(part.Mesh.Indices[i] >= 0 && part.Mesh.Indices[i] < part.Mesh.VertexCount, what + ": index in range");
            }
        }

        /// <summary>The markings that stand up from the road (a kerb's face, a marker, a post): their sides face along the road, and none faces down.</summary>
        private static bool Solid(string id) => id.StartsWith("mk.kerb.", StringComparison.Ordinal) || id == "mk.stud" || id == "mk.delineator";

        /// <summary>
        /// Paint on the road: every triangle faces up when the road is flat (the grade tilts the normals' sense by a hair only). Paint on something that stands up from it (Solid): none faces down. The elevation
        /// marking's paint is an upright sheet: every triangle faces the oncoming traffic (-Z).
        /// </summary>
        private static void FacingUp(MarkingPlan plan, string what)
        {
            string id = what.Split(' ', ':')[0];
            foreach (MarkingPart part in plan.Parts)
            {
                for (int i = 0; i + 2 < part.Mesh.Indices.Count; i += 3)
                {
                    Vec3 a = part.Mesh.Positions[part.Mesh.Indices[i]], b = part.Mesh.Positions[part.Mesh.Indices[i + 1]], c = part.Mesh.Positions[part.Mesh.Indices[i + 2]];
                    Vec3 n = MeshBuilder.Cross(b - a, c - a);
                    // A barrier's solids face every way (MarkingShape.Solid; BarrierTests checks their winding).
                    if (part.Solid.HasValue)
                        continue;
                    if (part.Upright)
                        Program.Check(n.Z < 0f, what + ": triangle " + i / 3 + " faces the traffic");
                    else if (Solid(id))
                        Program.Check(n.Y > -1e-9f, what + ": triangle " + i / 3 + " does not face down");
                    else
                        Program.Check(n.Y > 0f, what + ": triangle " + i / 3 + " faces up");
                }
            }
        }

        private static void Catalogue()
        {
            IReadOnlyList<GuideTemplate> all = MarkingTemplates.All;
            Program.Check(all.Count >= 60, "at least sixty markings: " + all.Count);
            var ids = new HashSet<string>();
            var hashes = new HashSet<uint>();
            foreach (GuideTemplate t in all)
            {
                Program.Check(ids.Add(t.Id), t.Id + ": the id is new");
                Program.Check(hashes.Add(t.Hash), t.Id + ": the hash is new");
                bool barrier = t.Group == MarkingTemplates.BarrierGroup;
                Program.Check(t.Id.StartsWith(barrier ? "bar." : "mk.", StringComparison.Ordinal), t.Id + ": ids start with mk. (a barrier's with bar.)");
                Program.Check((t.Group == "marking" || barrier) && t.IsMarking && t.Standalone, t.Id + ": group marking or barrier, standalone");
                Program.Check(!string.IsNullOrWhiteSpace(t.Zh) && !string.IsNullOrWhiteSpace(t.En) && !string.IsNullOrWhiteSpace(t.Code) && t.Keywords.Length > 0, t.Id + ": has names, a code and words to search by");
                Program.Check(t.FieldOf("g") != null, t.Id + ": has the grade");
                Program.Check(t.FieldOf("g").Visible != null && !t.FieldOf("g").Visible(t.Default()), t.Id + ": the grade is not shown in the editor");
                if (t.FieldOf(RunPath.Key) != null)
                    Program.Check(!t.FieldOf(RunPath.Key).Visible(t.Default()), t.Id + ": the path is not shown in the editor");
                Program.Check(t.Pinyin.Length > 0, t.Id + ": has pinyin initials");
            }

            // They are in the catalogue of signs, with their own group, and nothing else's hash is theirs.
            int markings = 0, signs = 0;
            foreach (SignDef def in SignCatalog.All)
            {
                if (def.IsMarking)
                    markings++;
                else
                    signs++;
            }

            Program.Check(markings == all.Count, "every marking is a SignDef of the catalogue: " + markings);
            Program.Check(signs >= 250, "the signs are still there (templates; the panel lists more through presets): " + signs);
            foreach (GuideTemplate t in all)
            {
                SignChoice choice = SignCatalog.ChoiceById(t.Id);
                Program.Check(choice != null && choice.Group == t.Group && choice.Def.IsMarking, t.Id + ": the panel lists it under its group");
                Program.Check(SignCatalog.ByHash(t.Hash) == choice.Def, t.Id + ": the hash of an item finds it");
            }

            // The check of signs: no sign shares a hash with a marking (the hash is what an item saves).
            var signHashes = new HashSet<uint>();
            foreach (SignDef def in SignCatalog.All)
            {
                if (!def.IsMarking)
                    signHashes.Add(def.Hash);
            }

            foreach (GuideTemplate t in all)
                Program.Check(!signHashes.Contains(t.Hash), t.Id + ": no sign has its hash");

            // The standard's numbers 线1 to 线72 are covered: every number named in a code, plus the figures that have no number.
            var covered = new HashSet<int>();
            foreach (GuideTemplate t in all)
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(t.Code, "\\d+"))
                    covered.Add(int.Parse(m.Value));
            }

            // Not yet made: the markings that are letters and symbols (线34 to 线40), the kerb faces (线48, 线50) and the elevation (线72).
            var missing = new List<int>();
            for (int n = 1; n <= 72; n++)
            {
                if (!covered.Contains(n))
                    missing.Add(n);
            }

            Program.Check(missing.Count <= 14, "the lines not made yet are few and known: " + string.Join(",", missing));
            foreach (int n in new[] { 34, 35, 36, 37, 38, 39, 40, 48, 50, 72 })
                Program.Check(missing.Contains(n) || covered.Contains(n), "线" + n + " is either made or on the list");
        }

        private static void Defaults()
        {
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                GuideSpec spec = t.Default();
                MarkingPlan plan = MarkingTemplates.PlanOf(t, spec.ToString());
                Sound(plan, t.Id);
                if (plan != null)
                {
                    FacingUp(plan, t.Id);
                    Program.Check(plan.MaxX >= plan.MinX && plan.MaxZ >= plan.MinZ, t.Id + ": a box");
                }

                // The automatic marking is a brush, never an item: its settings are not saved, and may be longer. An item's text is 512 bytes with its path (32 pieces of about ten characters, 0.12.73: the setting of the line's ends took 5).
                Program.Check(Encoding.UTF8.GetByteCount(spec.ToString()) < (t.Id == AutoMarking.Id ? 300 : 128), t.Id + ": the default settings are short: " + Encoding.UTF8.GetByteCount(spec.ToString()));
                // The same settings again are the same plan (cached).
                Program.Check(ReferenceEquals(plan, MarkingTemplates.PlanOf(t, spec.ToString())), t.Id + ": the plan is kept");
                // The settings parse back to themselves.
                Program.Check(t.Parse(spec.ToString()).ToString() == spec.ToString(), t.Id + ": the settings are stable");
            }

            Program.Check(MarkingTemplates.PlanOf((GuideTemplate)null, "") == null && MarkingTemplates.PlanOf((SignDef)null, "") == null, "no template, no plan");
            Program.Check(MarkingTemplates.PlanOf(GuideTemplates.ById("guide.exit.advance"), "") == null, "a board has no marking plan");
        }

        private static void EveryOption()
        {
            int tried = 0;
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                foreach (GuideField field in t.Fields)
                {
                    IEnumerable<string> values = field.Kind == GuideFieldKind.Choice ? ValuesOf(field) : field.Kind == GuideFieldKind.Flag ? new[] { "0", "1" } : new string[0];
                    foreach (string value in values)
                    {
                        GuideSpec spec = t.Default().With(field.Key, value);
                        MarkingPlan plan = MarkingTemplates.PlanOf(t, spec.ToString());
                        Sound(plan, t.Id + " " + field.Key + "=" + value);
                        tried++;
                    }
                }
            }

            Program.Check(tried > 150, "many options were tried: " + tried);
        }

        private static IEnumerable<string> ValuesOf(GuideField field)
        {
            foreach (GuideChoice c in field.Choices)
                yield return c.Value;
        }

        private static void Numbers()
        {
            // A number typed to the extreme (a text field of digits) is held to the range the template allows: the paint stays inside 500 m and has paint.
            foreach (GuideTemplate t in MarkingTemplates.All)
            {
                foreach (GuideField field in t.Fields)
                {
                    if (field.Kind != GuideFieldKind.Text || !field.DigitsOnly)
                        continue;
                    foreach (string value in new[] { "0", "0.0001", "99999", "9", "" })
                    {
                        GuideSpec spec = t.Default().With(field.Key, value);
                        MarkingPlan plan = MarkingTemplates.PlanOf(t, spec.ToString());
                        Sound(plan, t.Id + " " + field.Key + "=" + value);
                    }
                }
            }

            // The stop line's length, its width and the crosswalk's stripe are what the standard names.
            GuideTemplate stop = MarkingTemplates.ById("mk.stop");
            MarkingPlan p = MarkingTemplates.PlanOf(stop, stop.Default().With("span", "9").With("width", "40").ToString());
            Program.Near(p.MaxX - p.MinX, 9f, 1e-3f, "the stop line is as long as asked");
            Program.Near(p.MaxZ - p.MinZ, 0.4f, 1e-4f, "and 40 cm wide");
            MarkingPlan tooShort = MarkingTemplates.PlanOf(stop, stop.Default().With("span", "0").ToString());
            Program.Near(tooShort.MaxX - tooShort.MinX, 0.5f, 1e-3f, "a length of nothing is the least (0.5 m)");
            MarkingPlan tooLong = MarkingTemplates.PlanOf(stop, stop.Default().With("span", "99999").ToString());
            Program.Near(tooLong.MaxX - tooLong.MinX, 60f, 1e-2f, "and the longest is 60 m");

            GuideTemplate cross = MarkingTemplates.ById("mk.crosswalk");
            MarkingPlan c = MarkingTemplates.PlanOf(cross, cross.Default().With("len", "1").ToString());
            Program.Near(c.MaxZ - c.MinZ, 2f, 1e-3f, "a crosswalk is 2 m wide at the least (5.8.3)");
            GuideTemplate bay = MarkingTemplates.ById("mk.busbay");
            MarkingPlan b = MarkingTemplates.PlanOf(bay, bay.Default().With("taper", "5").ToString());
            Program.Check(b.MaxZ - b.MinZ >= MarkingFigures.BayTaperMin * 2f + 12f - 1f, "the tapers of a bus bay are 25 m at the least (5.11.4): " + (b.MaxZ - b.MinZ));
        }

        private static void GradePathWords()
        {
            // The grade: 10 % lifts the far end of a 12 m crosswalk by 10 % of its length; zero changes nothing; a grade that is not a road is dropped.
            GuideTemplate cross = MarkingTemplates.ById("mk.crosswalk");
            string flat = cross.Default().ToString();
            MarkingPlan f = MarkingTemplates.PlanOf(cross, flat);
            MarkingPlan up = MarkingTemplates.PlanOf(cross, MarkingTemplates.WithGrade(cross, flat, 0.1f));
            float flatTop = float.MinValue, upTop = float.MinValue, flatBottom = float.MaxValue, upBottom = float.MaxValue;
            foreach (MarkingPart part in f.Parts)
                foreach (Vec3 v in part.Mesh.Positions) { flatTop = Math.Max(flatTop, v.Y); flatBottom = Math.Min(flatBottom, v.Y); }
            foreach (MarkingPart part in up.Parts)
                foreach (Vec3 v in part.Mesh.Positions) { upTop = Math.Max(upTop, v.Y); upBottom = Math.Min(upBottom, v.Y); }
            Program.Near(flatTop, 0f, 1e-6f, "flat: height 0");
            Program.Near(upTop, 0.1f * f.MaxZ, 1e-4f, "10 %: the far edge is 10 % of its distance higher: " + upTop);
            Program.Near(upBottom, 0.1f * f.MinZ, 1e-4f, "and the near edge 10 % of its (negative) distance lower: " + upBottom);
            MarkingPlan silly = MarkingTemplates.PlanOf(cross, MarkingTemplates.WithGrade(cross, flat, 5f));
            float sillyTop = float.MinValue;
            foreach (MarkingPart part in silly.Parts)
                foreach (Vec3 v in part.Mesh.Positions) sillyTop = Math.Max(sillyTop, v.Y);
            Program.Near(sillyTop, 0f, 1e-6f, "a grade over 30 % is not a road: dropped");
            Program.Check(MarkingTemplates.WithGrade(cross, flat, 0.0512345f).Contains("g=0.0512"), "the grade is written to four places");

            // The path: a straight run of 20 m and the same run bent to the right: the paint follows.
            GuideTemplate lane = MarkingTemplates.ById("mk.line.lane");
            string straightPath = "400:0:0|400:0:0|400:0:0|400:0:0|400:0:0";
            string bentPath = "400:0:0|400:1500:0|400:1500:0|400:1500:0|400:1500:0";
            MarkingPlan straight = MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), straightPath));
            MarkingPlan bent = MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), bentPath));
            Program.Near(straight.MaxX - straight.MinX, 0.15f, 1e-3f, "a straight run stays 15 cm wide across");
            Program.Check(bent.MaxX - bent.MinX > 3f, "a bent run swings away: " + (bent.MaxX - bent.MinX));
            Program.Check(bent.MaxX > bent.MinX && bent.MaxX > 3f, "toward +X (the right) when the turns are positive");
            MarkingPlan none = MarkingTemplates.PlanOf(lane, lane.Default().ToString());
            Program.Near(none.MaxZ, 6f, 0.05f, "no path: the preview's straight 12 m, of which 6-9 paints the first 6");
            MarkingPlan solidNone = MarkingTemplates.PlanOf(MarkingTemplates.ById("mk.line.whitesolid"), MarkingTemplates.ById("mk.line.whitesolid").Default().ToString());
            Program.Near(solidNone.MaxZ, 12f, 0.05f, "a solid line with no path is the straight 12 m");
            Program.Check(MarkingTemplates.IsRun(lane) && !MarkingTemplates.IsRun(MarkingTemplates.ById("mk.stop")), "runs and stamps are told apart");
            // A path that does not fit what the settings keep is cut where it fits: the spec stays under 480 bytes.
            var pieces = new List<string>();
            for (int i = 0; i < 32; i++)
                pieces.Add("350:" + (i % 2 == 0 ? "-1234" : "1234") + ":-12");
            string full = MarkingTemplates.WithPath(lane, lane.Default().ToString(), string.Join("|", pieces));
            Program.Check(Encoding.UTF8.GetByteCount(full) <= GuideSpec.MaxBytes, "a full path fits the settings: " + Encoding.UTF8.GetByteCount(full));
            Program.Check(MarkingTemplates.PlanOf(lane, full).TriangleCount > 0, "and is drawn");
            // Damaged paths are drawn as the straight run.
            Program.Check(MarkingTemplates.PlanOf(lane, MarkingTemplates.WithPath(lane, lane.Default().ToString(), "junk|||")).TriangleCount > 0, "a damaged path still draws");

            // The size of an arrow: auto becomes the band of the road's speed; an explicit one is kept; a marking with no size is returned as it is.
            GuideTemplate arrow = MarkingTemplates.ById("mk.arrow.straight");
            string auto = arrow.Default().ToString();
            Program.Check(arrow.Parse(auto).Is("size", "auto"), "the arrow's size is auto by default");
            Program.Check(arrow.Parse(MarkingTemplates.ResolveSize(arrow, auto, 30f, true)).Is("size", "30"), "30 km/h: Fig. 42");
            Program.Check(arrow.Parse(MarkingTemplates.ResolveSize(arrow, auto, 60f, true)).Is("size", "31"), "60 km/h: Fig. 43");
            Program.Check(arrow.Parse(MarkingTemplates.ResolveSize(arrow, auto, 120f, true)).Is("size", "32"), "120 km/h: Fig. 44");
            Program.Check(arrow.Parse(MarkingTemplates.ResolveSize(arrow, auto, 0f, false)).Is("size", "31"), "no speed known: Fig. 43");
            Program.Check(arrow.Parse(MarkingTemplates.ResolveSize(arrow, arrow.Default().With("size", "33").ToString(), 30f, true)).Is("size", "33"), "a size chosen by hand is kept");
            Program.Check(MarkingTemplates.ResolveSize(MarkingTemplates.ById("mk.stop"), MarkingTemplates.ById("mk.stop").Default().ToString(), 30f, true) == MarkingTemplates.ById("mk.stop").Default().ToString(), "a marking with no size is unchanged");
            Program.Near(MarkingTemplates.PlanOf(arrow, arrow.Default().With("size", "30").ToString()).MaxZ, 3f, 1e-3f, "the 3 m arrow is 3 m long");
            Program.Near(MarkingTemplates.PlanOf(arrow, arrow.Default().With("size", "32").ToString()).MaxZ, 9f, 1e-3f, "the 9 m arrow is 9 m long");

            // Words: with the font the stop-and-give-way line has the word 停 in the paint; without, only the lines. The plans are not mixed up (the font's version is in the key).
            GuideTemplate give = MarkingTemplates.ById("mk.giveway.stop");
            string withWord = give.Default().ToString();
            MarkingLetters.Font = FontLetterTests.Font;
            int with = MarkingTemplates.PlanOf(give, withWord).TriangleCount;
            Program.Check(FontLetterTests.Font != null && with > 40, "with the font the word is in the paint: " + with);
            string noWord = give.Default().With("word", "0").ToString();
            int without = MarkingTemplates.PlanOf(give, noWord).TriangleCount;
            Program.Check(without == 4, "with the word switched off only the two lines: " + without);
            MarkingLetters.Font = null;
            int noFont = MarkingTemplates.PlanOf(give, withWord).TriangleCount;
            Program.Check(noFont == 4, "without a font the lines only: " + noFont);
            MarkingLetters.Font = FontLetterTests.Font;
            Program.Check(MarkingTemplates.PlanOf(give, withWord).TriangleCount == with, "and the font back: the word again");
        }
    }
}
