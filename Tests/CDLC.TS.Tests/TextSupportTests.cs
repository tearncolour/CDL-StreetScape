using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>The supports of the text light box (0.12.76): a gantry for a long text, and what holds the rear frame to the support (TextSupports, TextCarrier, SignBuilder.AddTextCarrier).</summary>
    internal static class TextSupportTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("text supports: a long text goes on a gantry", Choice);
            Program.Run("text supports: the editor says it once", Hints);
            Program.Run("text carrier: where the vertical channels of an arm or a beam are", HungChannels);
            Program.Run("text carrier: channels behind the rails on a post, and bolts clear of the studs", PostPlan);
            Program.Run("text carrier: bolts where the rails lie over the channels under a beam", HungPlan);
            Program.Run("text carrier: built on a post, the channels are the frame's own", BuiltOnPost);
            Program.Run("text carrier: built on an arm and a gantry, the bolts are on the rails", BuiltHung);
            Program.Run("text carrier: every support takes a long and a short text", EverySupport);
        }

        /// <summary>A typed backslash and an n: a line break in a text field.</summary>
        private static readonly string NL = new string(new[] { (char)92, 'n' });

        private static GuideSpec Spec(string text, string extra = "") => TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", text);

        private static SignInstance Instance(string text, string extra = "") => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), Spec(text, extra).ToString());

        private static SignParts Build(SignInstance instance, string support = null)
        {
            SupportDef def = support != null ? SupportCatalog.ById(support) : SupportPlanner.Auto(instance.Def);
            return SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, def, 0f, SupportDetail.Full, default);
        }

        private static FramePlan Frame(SignInstance instance)
        {
            TextPlanResult plan = TextPlans.Get(instance.Board.Text.Job);
            return plan?.Frame;
        }

        private static void Choice()
        {
            bool was = TextSupports.Enabled;
            try
            {
                TextSupports.Enabled = true;
                Program.Check(TextSupports.Choose(5f, out string none) == TextSupportReason.Standard && none == null, "five metres: the planner's rules");
                Program.Check(TextSupports.Choose(TextSupports.GantryMinWidth, out _) == TextSupportReason.Standard, "exactly six metres is still two posts' (the planner's rule is 'up to')");
                Program.Check(TextSupports.Choose(6.01f, out string gantry) == TextSupportReason.LongOnGantry && gantry == SupportCatalog.Gantry, "over six metres: a gantry");
                Program.Check(TextSupports.Choose(SupportLayouts.MaxSpan, out _) == TextSupportReason.LongOnGantry, "as long as the longest gantry: a gantry");
                Program.Check(TextSupports.Choose(SupportLayouts.MaxSpan + 0.5f, out string over) == TextSupportReason.TooLongForGantry && over == SupportCatalog.Gantry, "longer: still a gantry, and the reason says it overhangs");
                Program.Check(TextSupports.Choose(float.NaN, out _) == TextSupportReason.Standard && TextSupports.Choose(0f, out _) == TextSupportReason.Standard && TextSupports.Choose(-3f, out _) == TextSupportReason.Standard, "no number: the planner's rules");

                SignInstance shortText = Instance("路牌");
                SignInstance longText = Instance("文字灯箱文字灯箱", ";h=1");
                Program.Check(longText.Def.Width > 6f, "the long text is over six metres: " + longText.Def.Width);
                Program.Check(SupportPlanner.Auto(shortText.Def).Kind != SupportKind.Gantry, "a short text is not on a gantry: " + SupportPlanner.Auto(shortText.Def).Id);
                Program.Check(SupportPlanner.Auto(longText.Def).Id == SupportCatalog.Gantry, "a long text is on the gantry: " + SupportPlanner.Auto(longText.Def).Id);
                Program.Check(TextSupports.Auto(longText.Def)?.Id == SupportCatalog.Gantry && TextSupports.Auto(shortText.Def) == null, "the text's own rule says so, and says nothing for a short one");

                // A board that is not a text keeps the planner's rules, whatever its width.
                foreach (string id in new[] { "gb.prohib.36", "gb.guide8.01" })
                {
                    SignDef other = SignCatalog.ById(id);
                    Program.Check(other != null && TextSupports.Auto(other) == null && !TextSupports.IsText(other), id + " is not a text board");
                }

                // The player's pick is the player's: the planner is only the automatic choice, and the support of a pick is built as asked.
                SignParts picked = Build(longText, SupportCatalog.DoublePole);
                Program.Check(picked.Layout.Kind == SupportKind.DoublePole, "a hand-picked double post stays");

                TextSupports.Enabled = false;
                Program.Check(TextSupports.Choose(20f, out _) == TextSupportReason.Standard && TextSupports.Auto(longText.Def) == null, "off: 0.12.75's rules");
                Program.Check(SupportPlanner.Auto(longText.Def).Kind != SupportKind.Gantry, "off: the planner's rules put it on posts: " + SupportPlanner.Auto(longText.Def).Id);
            }
            finally
            {
                TextSupports.Enabled = was;
            }
        }

        private static void Hints()
        {
            Program.Check(TextSupports.Hint(5f, true) == null && TextSupports.Hint(6f, false) == null, "nothing for a text that fits two posts");
            string zh = TextSupports.Hint(8f, true), en = TextSupports.Hint(8f, false);
            Program.Check(zh != null && zh.Contains("门架") && en != null && en.Contains("gantry"), "a long text: one line in each language: " + zh + " / " + en);
            Program.Check(zh.Length < 40 && en.Length < 80, "and a short one");
            Program.Check(TextSupports.Hint(SupportLayouts.MaxSpan + 1f, false).Contains("exceeds the longest gantry"), "a text longer than any gantry says it hangs over");

            string longSpec = Spec("文字灯箱文字灯箱", ";h=1").ToString();
            Program.Check(TextBoxes.Hints(longSpec, true).Any(l => l.Contains("门架")) && TextBoxes.Hints(longSpec, false).Any(l => l.Contains("gantry")), "the editor's hints carry it for a long text");
            Program.Check(!TextBoxes.Hints(Spec("路牌").ToString(), true).Any(l => l.Contains("门架")), "and not for a short one");
        }

        private static void HungChannels()
        {
            Program.Check(TextCarrier.HungChannelXs(0.1f, false).Length == 1 && TextCarrier.HungChannelXs(0.1f, false)[0] == 0f, "a narrow board has one channel at its middle");
            foreach (float width in new[] { 0.5f, 1.2f, 3f, 6.5f, 12f })
            {
                float[] xs = TextCarrier.HungChannelXs(width, false);
                Program.Check(xs.Length >= 2, width + ": at least two channels");
                Program.Check(xs[0] < 0f && xs[xs.Length - 1] > 0f && Math.Abs(xs[0] + xs[xs.Length - 1]) < 1e-5f, width + ": symmetric about the middle");
                Program.Check(xs[xs.Length - 1] < width * 0.5f - 0.05f, width + ": inside the board by 6 cm");
                if (width >= 0.5f)
                    Program.Near(xs[xs.Length - 1], width * 0.5f - 0.06f, 1e-4f, width + ": the outer channels are 6 cm in from the outline");
                for (int i = 1; i < xs.Length; i++)
                    Program.Check(xs[i] - xs[i - 1] <= TextCarrier.ChannelPitch + 1e-4f, width + ": no further apart than the pitch");
            }

            Program.Check(TextCarrier.HungChannelXs(3f, true).Length == 2 && TextCarrier.HungChannelXs(3.5f, true).Length == 3, "reduced detail: two, three over three metres");
        }

        private static List<float> PoleView(SignParts parts, float cx) => parts.Layout.PoleX.Select(px => cx - px).ToList();

        private static void PostPlan()
        {
            SignInstance one = Instance("文字灯箱", ";h=0.4");
            FramePlan frame = Frame(one);
            FrameStyle style = FrameStyle.For(0.4f);
            List<float> levels = TextCarrier.PostLevels(frame);
            Program.Check(levels.Count == 2 && levels[0] < levels[1], "a line of text: a channel behind its low rail and its high rail: " + levels.Count);
            float low = frame.Rails.Min(r => (r.A.Y + r.B.Y) * 0.5f), high = frame.Rails.Max(r => (r.A.Y + r.B.Y) * 0.5f);
            Program.Near(levels[0], low, 1e-5f, "the lowest rail's height");
            Program.Near(levels[1], high, 1e-5f, "the highest rail's height");

            SignInstance tall = Instance("文字灯箱\\n文字灯箱\\n文字灯箱", ";h=0.6");
            List<float> three = TextCarrier.PostLevels(Frame(tall));
            Program.Check(three.Count == 3 && three[0] < three[1] && three[1] < three[2], "a tall text has a channel between: " + three.Count);
            Program.Check(TextCarrier.PostLevels(null).Count == 0 && TextCarrier.PostLevels(new FramePlan()).Count == 0, "no frame, no levels");

            // A tall frame whose only rail near the middle is one at the low edge: no middle channel (a channel at the edge would be a second one there).
            var odd = new FramePlan();
            foreach (float y in new[] { 0f, 0.02f, 3f })
                odd.Rails.Add(new FrameSegment { A = new Point2(-1f, y), B = new Point2(1f, y) });
            List<float> oddLevels = TextCarrier.PostLevels(odd);
            Program.Check(oddLevels.Count == 2 && oddLevels[0] == 0f && oddLevels[1] == 3f, "a rail 2 cm over the lowest is not the middle: " + string.Join(",", oddLevels));
            var spread = new FramePlan();
            foreach (float y in new[] { 0f, 1.4f, 1.9f, 3f })
                spread.Rails.Add(new FrameSegment { A = new Point2(-1f, y), B = new Point2(1f, y) });
            List<float> spreadLevels = TextCarrier.PostLevels(spread);
            Program.Check(spreadLevels.Count == 3 && spreadLevels[1] == 1.4f, "the middle one is the rail nearest the middle: " + string.Join(",", spreadLevels));
            var shortFrame = new FramePlan();
            shortFrame.Rails.Add(new FrameSegment { A = new Point2(-1f, 0f), B = new Point2(1f, 0f) });
            Program.Check(TextCarrier.PostLevels(shortFrame).Count == 1, "one rail: one channel");

            // A bolt that has no place free of studs is dropped and counted: a short rail with a stud every centimetre round it.
            var packed = new FramePlan();
            packed.Rails.Add(new FrameSegment { A = new Point2(-0.1f, 0f), B = new Point2(0.1f, 0f) });
            for (int ix = -25; ix <= 25; ix++)
            {
                for (int iy = -8; iy <= 8; iy++)
                    packed.Studs.Add(new FrameStud { At = new Point2(ix * 0.01f, iy * 0.01f) });
            }

            CarrierPlan blocked = TextCarrier.Post(packed, FrameStyle.For(0.4f), new List<float> { 0f });
            Program.Check(blocked.Bolts.Count == 0 && blocked.DroppedBolts >= 3, "no bolt fits among the studs: " + blocked.Bolts.Count + " bolts, " + blocked.DroppedBolts + " dropped");

            CarrierPlan plan = TextCarrier.Post(frame, style, new List<float> { 0f });
            Program.Check(plan.Channels.Count == 2 && plan.Channels.All(c => !c.Vertical), "two horizontal channels");
            foreach (CarrierChannel channel in plan.Channels)
            {
                float railFrom = frame.Rails.Where(r => Math.Abs((r.A.Y + r.B.Y) * 0.5f - channel.Level) < 0.02f).Min(r => Math.Min(r.A.X, r.B.X));
                float railTo = frame.Rails.Where(r => Math.Abs((r.A.Y + r.B.Y) * 0.5f - channel.Level) < 0.02f).Max(r => Math.Max(r.A.X, r.B.X));
                Program.Check(channel.From <= railFrom + 1e-5f && channel.To >= railTo - 1e-5f, "the channel runs the length of the rails behind it");
                Program.Check(channel.From <= -TextCarrier.PostReach + 1e-5f && channel.To >= TextCarrier.PostReach - 1e-5f, "and reaches the post at the middle by PostReach");
            }

            // Two posts far apart: the channel reaches both.
            CarrierPlan twoPosts = TextCarrier.Post(frame, style, new List<float> { -3f, 3f });
            Program.Check(twoPosts.Channels.All(c => c.From <= -3f - TextCarrier.PostReach + 1e-5f && c.To >= 3f + TextCarrier.PostReach - 1e-5f), "two posts: the channel reaches both");

            // The bolts: on a rail, on a channel, clear of every stud, none on top of another, at the posts.
            float clear = TextCarrier.BoltHalf * 1.4143f + Math.Max(0.0015f, style.StudRadius);
            Program.Check(plan.Bolts.Count >= 2, "bolts: " + plan.Bolts.Count);
            foreach (CarrierBolt bolt in plan.Bolts)
            {
                Program.Check(frame.Rails.Any(r => Math.Abs((r.A.Y + r.B.Y) * 0.5f - bolt.Y) < 0.045f && bolt.X >= Math.Min(r.A.X, r.B.X) - 1e-4f && bolt.X <= Math.Max(r.A.X, r.B.X) + 1e-4f), "a bolt is on a rail");
                Program.Check(plan.Channels.Any(c => Math.Abs(c.Level - bolt.Y) < 0.045f && bolt.X >= c.From - 1e-4f && bolt.X <= c.To + 1e-4f), "and on a channel");
                Program.Check(frame.Studs.All(s => (s.At.X - bolt.X) * (s.At.X - bolt.X) + (s.At.Y - bolt.Y) * (s.At.Y - bolt.Y) >= clear * clear - 1e-7f), "and clear of every stud");
            }

            for (int i = 0; i < plan.Bolts.Count; i++)
            {
                for (int j = i + 1; j < plan.Bolts.Count; j++)
                    Program.Check(Math.Abs(plan.Bolts[i].X - plan.Bolts[j].X) >= 0.02f || Math.Abs(plan.Bolts[i].Y - plan.Bolts[j].Y) >= 0.02f, "no two bolts on top of each other");
            }

            // A long rail has a bolt at least every BoltPitch.
            SignInstance wide = Instance("文字灯箱文字灯箱", ";h=0.5");
            FramePlan wideFrame = Frame(wide);
            CarrierPlan widePlan = TextCarrier.Post(wideFrame, FrameStyle.For(0.5f), new List<float> { -3f, 3f });
            foreach (CarrierChannel channel in widePlan.Channels)
            {
                List<float> xs = widePlan.Bolts.Where(b => Math.Abs(b.Y - channel.Level) < 0.045f).Select(b => b.X).OrderBy(x => x).ToList();
                float gap = 0f;
                for (int i = 1; i < xs.Count; i++)
                    gap = Math.Max(gap, xs[i] - xs[i - 1]);
                Program.Check(xs.Count >= 3 && gap <= TextCarrier.BoltPitch + 0.1f + 2f * 0.04f, "a long channel: bolts " + xs.Count + ", the longest gap " + gap.ToString("0.00"));
            }

            Program.Check(TextCarrier.Post(new FramePlan(), style, null).Channels.Count == 0 && TextCarrier.Post(null, style, null).Bolts.Count == 0, "no frame: nothing");
            CarrierPlan again = TextCarrier.Post(frame, style, new List<float> { 0f });
            Program.Check(again.Bolts.Count == plan.Bolts.Count && again.Bolts.Zip(plan.Bolts, (a, b) => a.X == b.X && a.Y == b.Y).All(x => x), "the same twice");
        }

        private static void HungPlan()
        {
            SignInstance board = Instance("文字灯箱文字", ";h=0.6");
            FramePlan frame = Frame(board);
            GuidePart main = board.Board.Main;
            CarrierPlan plan = TextCarrier.Hung(frame, FrameStyle.For(0.6f), main.W, main.H, false);
            float[] xs = TextCarrier.HungChannelXs(main.W, false);
            Program.Check(plan.Channels.Count == xs.Length && plan.Channels.All(c => c.Vertical), "a vertical channel for each of the support's");
            Program.Check(plan.Crossings >= 4, "the rails lie over several channels: " + plan.Crossings);
            Program.Check(plan.Bolts.Count + plan.DroppedBolts == plan.Crossings, "a bolt for every crossing, or one dropped for a stud: " + plan.Bolts.Count + " + " + plan.DroppedBolts + " of " + plan.Crossings);
            float clear = TextCarrier.BoltHalf * 1.4143f + Math.Max(0.0015f, FrameStyle.For(0.6f).StudRadius);
            foreach (CarrierBolt bolt in plan.Bolts)
            {
                Program.Check(plan.Channels.Any(c => Math.Abs(c.Level - bolt.X) <= 0.021f), "a bolt is by a channel's x");
                Program.Check(frame.Rails.Any(r => Math.Abs((r.A.Y + r.B.Y) * 0.5f - bolt.Y) < 1e-4f && bolt.X >= Math.Min(r.A.X, r.B.X) - 0.021f && bolt.X <= Math.Max(r.A.X, r.B.X) + 0.021f), "and on a rail");
                Program.Check(frame.Studs.All(s => (s.At.X - bolt.X) * (s.At.X - bolt.X) + (s.At.Y - bolt.Y) * (s.At.Y - bolt.Y) >= clear * clear - 1e-7f), "and clear of every stud");
            }

            // Reduced detail: three channels at the most, so fewer crossings.
            CarrierPlan reduced = TextCarrier.Hung(frame, FrameStyle.For(0.6f), main.W, main.H, true);
            Program.Check(reduced.Channels.Count <= 3 && reduced.Crossings <= plan.Crossings, "reduced detail: fewer channels");
            // A rail too short to reach any channel is counted.
            var small = new FramePlan();
            small.Rails.Add(new FrameSegment { A = new Point2(0.05f, 0f), B = new Point2(0.1f, 0f) });
            CarrierPlan none = TextCarrier.Hung(small, FrameStyle.For(0.4f), 1.2f, 0.5f, false);
            Program.Check(none.UnheldRails == 1 && none.Bolts.Count == 0, "a rail between channels is held by nothing here: " + none.UnheldRails);
            Program.Check(TextCarrier.Hung(null, FrameStyle.For(0.4f), 1f, 1f, false).Channels.Count == 0, "no frame: nothing");
        }

        private static IEnumerable<float> Ys(MeshBuilder mesh, float z, float tolerance = 1e-4f) => mesh.Positions.Where(p => Math.Abs(p.Z - z) < tolerance).Select(p => p.Y);

        private static void BuiltOnPost()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱", ";h=0.4");
            SignParts parts = Build(instance, SupportCatalog.SinglePole);
            Program.Check(!parts.Provisional, "the plan was there");
            TextBoard text = instance.Board.Text;
            FramePlan frame = TextPlans.Peek(text.Job).Frame;
            float backZ = parts.Layout.BackZ, back = backZ - SupportLayouts.RailDepth;
            float cy = parts.Picks.Find(p => p.Name == "board").CentreY;
            List<float> levels = TextCarrier.PostLevels(frame);
            foreach (float level in levels)
            {
                float y = cy + level;
                bool channel = Ys(parts.Metal, back).Any(v => Math.Abs(v - (y - SupportLayouts.RailWidth * 0.5f)) < 1e-4f) && Ys(parts.Metal, back).Any(v => Math.Abs(v - (y + SupportLayouts.RailWidth * 0.5f)) < 1e-4f);
                Program.Check(channel, "a channel behind the rail at " + level.ToString("0.000"));
            }

            // The generic channels of the support (by the box's height) are not there: their levels have no channel of their own.
            float height = instance.Def.Height;
            int generic = 0;
            foreach (float k in SupportLayouts.RailKFor(height))
            {
                float y = cy + k * height;
                if (levels.All(l => Math.Abs(cy + l - y) > 0.03f) && Ys(parts.Metal, back).Any(v => Math.Abs(v - (y + SupportLayouts.RailWidth * 0.5f)) < 1e-4f))
                    generic++;
            }

            Program.Check(generic == 0, "the generic channels are not built for the text: " + generic);

            // Three lines on one post: the support's own channels would be at other heights than the frame's, so the number of channels says whose they are.
            SignInstance threeLines = Instance("文字灯箱\\n文字灯箱\\n文字灯箱", ";h=0.6");
            SignParts three = Build(threeLines, SupportCatalog.SinglePole);
            FramePlan threeFrame = TextPlans.Peek(threeLines.Board.Text.Job).Frame;
            List<float> threeLevels = TextCarrier.PostLevels(threeFrame);
            float threeBack = three.Layout.BackZ - SupportLayouts.RailDepth;
            List<float> heights = Ys(three.Metal, threeBack).OrderBy(v => v).ToList();
            int clusters = 0;
            for (int i = 0; i < heights.Count; i++)
            {
                if (i == 0 || heights[i] - heights[i - 1] > 0.1f)
                    clusters++;
            }

            Program.Check(clusters == threeLevels.Count, "three lines: as many channels as the frame has levels, the support's own are not built: " + clusters + " against " + threeLevels.Count);

            // Every bolt of the plan has its head where the plan says, as the viewer sees it (x mirrored about the board's middle), on a text that is not symmetric.
            SignInstance lopsided = Instance("Ljjj", ";h=2");
            SignParts lopsidedParts = Build(lopsided, SupportCatalog.SinglePole);
            PickShape lopsidedBoard = lopsidedParts.Picks.Find(p => p.Name == "board");
            FramePlan lopsidedFrame = TextPlans.Peek(lopsided.Board.Text.Job).Frame;
            CarrierPlan lopsidedPlan = TextCarrier.Post(lopsidedFrame, FrameStyle.For(2f), lopsidedParts.Layout.PoleX.Select(px => lopsidedBoard.CentreX - px).ToList());
            float lopsidedHead = lopsidedParts.Layout.BackZ + Math.Max(0.006f, FrameStyle.For(2f).RailSize) + TextCarrier.BoltThickness;
            float lopsidedSum = lopsidedPlan.Bolts.Sum(b => b.X);
            Program.Check(lopsidedPlan.Bolts.Count >= 4 && Math.Abs(lopsidedSum) > 0.15f, "the bolts of a text whose ink is off the middle are not symmetric: the sum of their x is " + lopsidedSum);
            foreach (CarrierBolt bolt in lopsidedPlan.Bolts)
                Program.Check(lopsidedParts.Metal.Positions.Any(p => Math.Abs(p.Z - lopsidedHead) < 1e-5f && Math.Abs(p.X - (lopsidedBoard.CentreX - bolt.X)) <= TextCarrier.BoltHalf + 1e-4f && Math.Abs(p.Y - (lopsidedBoard.CentreY + bolt.Y)) <= TextCarrier.BoltHalf + 1e-4f), "a bolt head at " + bolt.X.ToString("0.00") + ", " + bolt.Y.ToString("0.00") + " as the viewer sees it");

            // Left-aligned lines of unequal length: the channel is exactly as long as the rails behind it (and the post's reach), not mirrored.
            SignInstance ragged = Instance("文字灯箱文字\\n字", ";h=0.5;align=left");
            SignParts raggedParts = Build(ragged, SupportCatalog.SinglePole);
            FramePlan raggedFrame = TextPlans.Peek(ragged.Board.Text.Job).Frame;
            PickShape raggedBoard = raggedParts.Picks.Find(p => p.Name == "board");
            float raggedBack = raggedParts.Layout.BackZ - SupportLayouts.RailDepth;
            CarrierPlan raggedPlan = TextCarrier.Post(raggedFrame, FrameStyle.For(0.5f), raggedParts.Layout.PoleX.Select(px => raggedBoard.CentreX - px).ToList());
            foreach (CarrierChannel channel in raggedPlan.Channels)
            {
                float yTop = raggedBoard.CentreY + channel.Level + SupportLayouts.RailWidth * 0.5f;
                List<float> xs = raggedParts.Metal.Positions.Where(p => Math.Abs(p.Z - raggedBack) < 1e-4f && Math.Abs(p.Y - yTop) < 1e-4f).Select(p => p.X).ToList();
                Program.Check(xs.Count > 0 && Math.Abs(xs.Min() - (raggedBoard.CentreX - channel.To)) < 1e-3f && Math.Abs(xs.Max() - (raggedBoard.CentreX - channel.From)) < 1e-3f, "the channel at " + channel.Level.ToString("0.00") + " is as long as the rails behind it");
            }

            // A hoop at each post on each channel: the backing plate reaches the channel's back plane at the post's x.
            foreach (float px in three.Layout.PoleX)
            {
                foreach (float level in threeLevels)
                {
                    float y = three.Picks.Find(p => p.Name == "board").CentreY + level;
                    Program.Check(three.Metal.Positions.Any(p => Math.Abs(p.Z - threeBack) < 1e-4f && Math.Abs(p.X - px) <= 0.021f && Math.Abs(p.Y - (y + 0.025f)) < 1e-4f), "a hoop at the post on the channel at " + level.ToString("0.00"));
                }
            }

            // The channels touch the rails: the rails' back is the plane of the channels' front.
            Program.Check(parts.Metal.Positions.Any(p => Math.Abs(p.Z - backZ) < 1e-5f), "the channels' front is the rails' back plane");

            // Bolts: heads in front of the rails.
            FrameStyle style = FrameStyle.For(text.Job.LetterHeight);
            float head = backZ + Math.Max(0.006f, style.RailSize) + TextCarrier.BoltThickness;
            Program.Check(parts.Metal.Positions.Any(p => Math.Abs(p.Z - head) < 1e-5f), "bolt heads stand in front of the rails");
            Program.Check(head < backZ + text.Stand, "and behind the letters' backs: " + head + " < " + (backZ + text.Stand));

            // Off: the generic channels, no bolts.
            bool was = TextSupports.Enabled;
            try
            {
                TextSupports.Enabled = false;
                SignParts off = Build(instance, SupportCatalog.SinglePole);
                Program.Check(!off.Metal.Positions.Any(p => Math.Abs(p.Z - head) < 1e-5f), "off: no bolts");
                Program.Check(off.Metal.VertexCount != parts.Metal.VertexCount, "off: the support's own channels instead");
            }
            finally
            {
                TextSupports.Enabled = was;
            }

            // Two posts: a hoop at each post on each channel.
            SignInstance wide = Instance("文字灯箱文字", ";h=0.4");
            SignParts doublePost = Build(wide, SupportCatalog.DoublePole);
            Program.Check(doublePost.Layout.PoleX.Length == 2 && doublePost.Metal.VertexCount > parts.Metal.VertexCount, "two posts built");
        }

        private static void BuiltHung()
        {
            TextPlans.Clear();
            SignInstance instance = Instance("文字灯箱文字", ";h=0.5");
            TextBoard text = instance.Board.Text;
            foreach (string id in new[] { SupportCatalog.Cantilever, SupportCatalog.Gantry, SupportCatalog.DoubleCantilever, SupportCatalog.FCantilever })
            {
                SignParts parts = Build(instance, id);
                FramePlan frame = TextPlans.Peek(text.Job).Frame;
                FrameStyle style = FrameStyle.For(text.Job.LetterHeight);
                float head = parts.Layout.BackZ + Math.Max(0.006f, style.RailSize) + TextCarrier.BoltThickness;
                int heads = parts.Metal.Positions.Count(p => Math.Abs(p.Z - head) < 1e-5f);
                Program.Check(heads >= 8 * 4, id + ": bolt heads in front of the rails: " + heads + " vertices");
                Program.Check(parts.Metal.Positions.All(p => !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z)), id + ": finite");

                // Under an arm or a beam there are no horizontal channels at the frame's levels (those are a post's: the board is held by the vertical ones).
                float railPlane = parts.Layout.BackZ - SupportLayouts.RailDepth;
                float boardY = parts.Picks.Find(p => p.Name == "board").CentreY;
                foreach (float level in TextCarrier.PostLevels(frame))
                    Program.Check(!parts.Metal.Positions.Any(p => Math.Abs(p.Z - railPlane) < 1e-4f && Math.Abs(p.Y - (boardY + level + SupportLayouts.RailWidth * 0.5f)) < 1e-4f), id + ": no post-style channel at " + level.ToString("0.00"));

                // The support's vertical channels are where HungChannelXs says: a vertical channel's edges are RailWidth apart at the plane of their backs.
                PickShape board = parts.Picks.Find(p => p.Name == "board");
                float channelBack = parts.Layout.BackZ - SupportLayouts.RailDepth;
                foreach (float x in TextCarrier.HungChannelXs(instance.Def.Width, false))
                {
                    float centre = board.CentreX + x;
                    bool left = parts.Metal.Positions.Any(p => Math.Abs(p.Z - channelBack) < 1e-4f && Math.Abs(p.X - (centre - SupportLayouts.RailWidth * 0.5f)) < 1e-4f);
                    bool right = parts.Metal.Positions.Any(p => Math.Abs(p.Z - channelBack) < 1e-4f && Math.Abs(p.X - (centre + SupportLayouts.RailWidth * 0.5f)) < 1e-4f);
                    Program.Check(left && right, id + ": a vertical channel at " + x.ToString("0.00") + " from the board's middle");
                }
            }

            SignInstance big = Instance("文字灯箱文字灯箱", ";h=1");
            SignParts gantry = Build(big);
            Program.Check(gantry.Layout.Kind == SupportKind.Gantry, "a long text built as the planner chose it: " + gantry.Layout.Kind);
            Program.Check(gantry.Solids[(int)big.Board.Text.Glow] != null && gantry.Solids[(int)big.Board.Text.Glow].VertexCount > 400, "with its letters: " + gantry.Solids[(int)big.Board.Text.Glow]?.VertexCount + " provisional " + gantry.Provisional + " width " + big.Def.Width);
            Program.Check(gantry.Metal.VertexCount > 2000, "and a gantry and a frame: " + gantry.Metal.VertexCount);
        }

        private static void EverySupport()
        {
            TextPlans.Clear();
            foreach (SignInstance instance in new[] { Instance("路牌"), Instance("文字灯箱文字灯箱", ";h=1"), Instance("A\\nB\\nC", ";h=0.8") })
            {
                foreach (SupportDef support in SupportCatalog.All)
                {
                    SignParts parts;
                    try
                    {
                        parts = Build(instance, support.Id);
                    }
                    catch (Exception ex)
                    {
                        Program.Check(false, support.Id + ": built: " + ex.GetType().Name + ": " + ex.Message);
                        continue;
                    }

                    bool finite = true;
                    foreach (MeshBuilder mesh in new[] { parts.Metal, parts.Concrete })
                    {
                        foreach (Vec3 p in mesh.Positions)
                            finite &= !float.IsNaN(p.X + p.Y + p.Z) && !float.IsInfinity(p.X + p.Y + p.Z);
                    }

                    Program.Check(finite, support.Id + ": every coordinate is a number");
                    Program.Check(parts.Solids[(int)instance.Board.Text.Glow]?.VertexCount > 0, support.Id + ": the letters are there");
                }
            }
        }
    }
}
