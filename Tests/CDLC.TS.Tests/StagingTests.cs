using System;
using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>A traffic-state screen's picture is made before it is shown (0.6.0): the network of a revision is kept staged while the picture is made on another thread, and is shown when it is done.</summary>
    internal static class StagingTests
    {
        public static void RunAll()
        {
            Program.Run("staging: a staged network is read by its own revision and shown only when published", Staged);
            Program.Run("staging: a screen is read at three samples to a dot, the text screens at six", Samples);
        }

        private static TmNetwork Network(TrafficLevel level)
        {
            var net = new TmNetwork { Known = true, HeadX = 0f, HeadZ = 1f };
            net.Edges.Add(new TmEdge { Level = level, Name = "", Road = 1, Points = new float[] { 0, 0, 0, 500 } });
            return net;
        }

        private static void Staged()
        {
            const int id = 765432;
            try
            {
                TmNetwork first = Network(TrafficLevel.Free), second = Network(TrafficLevel.Jam), third = Network(TrafficLevel.Slow);
                TrafficMap.Set(id, first, 1);
                int changes = TrafficMap.Changes;

                // Staging shows nothing: the published network, its revision and the change counter are as they were.
                TrafficMap.Stage(id, second, 2);
                Program.Check(TrafficMap.Changes == changes, "staging does not raise Changes (the tiles are not rebuilt for a picture that is not shown)");
                Program.Check(TrafficMap.RevisionOf(id) == 1 && ReferenceEquals(TrafficMap.Get(id), first), "what is published is still the first revision");

                // The picture of revision 2 is made from the staged network, the picture of revision 1 from the published one, any other revision from what is published.
                Program.Check(ReferenceEquals(TrafficMap.Get(id, 2), second), "revision 2 reads the staged network");
                Program.Check(ReferenceEquals(TrafficMap.Get(id, 1), first), "revision 1 reads the published network");
                Program.Check(ReferenceEquals(TrafficMap.Get(id, 7), first), "a revision that is neither reads what is published");

                // A later staging replaces the earlier one.
                TrafficMap.Stage(id, third, 3);
                Program.Check(ReferenceEquals(TrafficMap.Get(id, 3), third) && ReferenceEquals(TrafficMap.Get(id, 2), first), "a staged revision replaces the earlier staged one: revision 2 is gone");

                // Publishing shows it, once.
                Program.Check(TrafficMap.Publish(id), "publish says it showed something");
                Program.Check(TrafficMap.Changes == changes + 1, "publishing raises Changes once: " + (TrafficMap.Changes - changes));
                Program.Check(TrafficMap.RevisionOf(id) == 3 && ReferenceEquals(TrafficMap.Get(id), third) && ReferenceEquals(TrafficMap.Get(id, 3), third), "the staged network is the published one now, with its revision");
                Program.Check(!TrafficMap.Publish(id) && TrafficMap.Changes == changes + 1, "nothing is staged any more: a second publish shows nothing and changes nothing");

                // A screen that was never published can be staged and published (the first reading).
                const int fresh = 765433;
                try
                {
                    Program.Check(TrafficMap.Get(fresh, 1) == null && TrafficMap.RevisionOf(fresh) == 0, "a screen with nothing has no network");
                    TrafficMap.Stage(fresh, first, 1);
                    Program.Check(TrafficMap.RevisionOf(fresh) == 0 && TrafficMap.Get(fresh) == null, "staged, it is still not shown: no published network, revision 0");
                    Program.Check(ReferenceEquals(TrafficMap.Get(fresh, 1), first), "revision 1 reads the staged network");
                    Program.Check(TrafficMap.Publish(fresh) && TrafficMap.RevisionOf(fresh) == 1 && ReferenceEquals(TrafficMap.Get(fresh), first), "published");
                }
                finally
                {
                    TrafficMap.Remove(fresh);
                }

                // The picture is the one of the revision it was made for: an instance made while revision 2 is only staged is the picture of that network, and stays after it is published.
                GuideTemplate template = SignCatalog.ChoiceById(TrafficMap.TemplateId).Def.Template;
                SignDef def = SignCatalog.ChoiceById(TrafficMap.TemplateId).Def;
                GuideSpec defaults = template.Default();
                const int other = 765434;
                try
                {
                    TrafficMap.Set(other, Network(TrafficLevel.Free), 1);
                    TrafficMap.Stage(other, Network(TrafficLevel.Jam), 2);
                    SignInstance beforeShown = SignInstances.Of(def, TrafficMap.Decorate(defaults.ToString(), other, 2));
                    TrafficMap.Publish(other);
                    SignInstance afterShown = SignInstances.Of(def, TrafficMap.Decorate(defaults.ToString(), other, 2));
                    Program.Check(ReferenceEquals(beforeShown, afterShown), "the instance made for the staged revision is the one the screen gets when it is published (nothing is made twice)");
                    int red = 0, green = 0;
                    foreach (GuideElement e in afterShown.Board.Main.Elements)
                    {
                        red += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Red && e.Points.Length == 8 ? 1 : 0;
                        green += e.Kind == GuideElementKind.Polygon && e.Colour == GuideColour.Green && e.Points.Length == 8 ? 1 : 0;
                    }

                    Program.Check(red == 1 && green == 0, "revision 2 is the jammed road, not the free one that was published when it was made: " + red + " red, " + green + " green");
                }
                finally
                {
                    TrafficMap.Remove(other);
                }
            }
            finally
            {
                TrafficMap.Remove(id);
            }
        }

        private static void Samples()
        {
            SignDef def = SignCatalog.ChoiceById(TrafficMap.TemplateId).Def;
            SignInstance traffic = SignInstances.Of(def, null);
            Program.Check(traffic.Board.LedSamples == CDLC.TS.Geo.Signs.VariableSigns.TrafficSamplesPerDot && CDLC.TS.Geo.Signs.VariableSigns.TrafficSamplesPerDot == 3, "the traffic-state screen is read at three samples to a dot: " + traffic.Board.LedSamples);

            SignDef text = SignCatalog.ChoiceById("gb.vms.text")?.Def;
            if (text != null)
                Program.Check(SignInstances.Of(text, null).Board.LedSamples == 0, "a text screen keeps the default (the size of its picture is the default six)");

            // The size of the picture the dots are read from: nine times less than at six for the same dots (a third each way, the long side capped the same).
            Led.Dimensions(traffic.Board, def.Width, def.Height, out int dotsX, out int dotsY);
            Led.SampleSize(dotsX, dotsY, out int w6, out int h6);
            Led.SampleSize(dotsX, dotsY, out int w3, out int h3, 3);
            Led.SampleSize(dotsX, dotsY, out int wDefault, out int hDefault, 0);
            Program.Check(wDefault == w6 && hDefault == h6, "0 is the default");
            Program.Check((long)w3 * h3 * 3 < (long)w6 * h6, "three samples is a much smaller picture than six: " + w3 + "x" + h3 + " against " + w6 + "x" + h6);
            Program.Check(w3 >= Math.Min(dotsX * 3, Led.MaxSampleSide) - 1, "at least three pixels to a dot across unless capped: " + w3 + " for " + dotsX + " dots");
        }
    }
}
