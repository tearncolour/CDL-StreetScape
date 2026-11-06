using CDLC.TS.Geo;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// The screens without the Realistic Information Extension (0.12.29: the reading of the game is its own mod): a traffic-state screen shows its default picture and an automatic screen what was
    /// set by hand. The other tests run as with the extension (Program sets LiveContent.On); this one turns it off and back.
    /// </summary>
    internal static class LiveContentTests
    {
        public static void RunAll()
        {
            Program.Run("live content: without the extension the screens show their own content", WithoutExtension);
        }

        private static void WithoutExtension()
        {
            SignDef text = SignCatalog.ById("gb.vms.text"), speed = SignCatalog.ById("gb.vms.speed"), traffic = SignCatalog.ById(TrafficMap.TemplateId);
            string autoText = text.Template.Default().With("src", "auto").With("l1", "自己的话").ToString();
            string autoSpeed = speed.Template.Default().With("src", "auto").With("value", "80").ToString();
            Program.Check(LiveContent.On && VmsAuto.IsAutoScreen(text.Hash, autoText), "with the extension an automatic screen is one");
            LiveContent.On = false;
            try
            {
                Program.Check(!VmsAuto.IsAutoScreen(text.Hash, autoText) && !VmsAuto.IsAutoScreen(speed.Hash, autoSpeed), "without it no screen is automatic");
                GuideSpec spec = text.Template.Parse(autoText);
                Program.Check(text.Template.FieldOf("l1").Visible(spec) && !text.Template.FieldOf("src").Visible(spec), "its own lines are asked for, the content's source is not");
                Program.Check(speed.Template.FieldOf("value").Visible(speed.Template.Parse(autoSpeed)), "a speed screen's value is asked for");
                string shown = TrafficMap.Decorate(traffic.Template.Default().ToString(), 42, 3);
                Program.Check(traffic.Template.Parse(shown).Get("id") == "" && traffic.Template.Parse(shown).Get("rev") == "", "a traffic-state screen gets no live item: it shows its default picture");
                SignInstance demo = SignInstances.Of(traffic, shown);
                Program.Check(demo.IsLed && demo.Board.Main.Elements.Count > 0, "and that picture has the made-up roads on it");
            }
            finally
            {
                LiveContent.On = true;
            }

            Program.Check(traffic.Template.Parse(TrafficMap.Decorate(traffic.Template.Default().ToString(), 42, 3)).Get("id") == "42", "with it again, the screen is the reader's item");
        }
    }
}
