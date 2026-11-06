using System;
using System.Collections.Generic;
using System.Linq;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using CDLC.TS.Snap;

namespace CDLC.TS.Tests
{
    /// <summary>
    /// Where the text light box's two builds of 0.12.76 meet (the merge of the fonts and styles with the supports): a text set on an arc by its own style is not bent to a curved wall as well, and the rear
    /// frame of a style with an effect is made with that style's studs (a hollow letter's are half the size) on a post, on a wall and on a bracket.
    /// </summary>
    internal static class TextJoinTests
    {
        public static void RunAll()
        {
            FontOutlines font = TextLayoutTests.BundledFont();
            if (font == null)
                return;
            TextFonts.Use(TextFonts.DefaultId, font);
            Program.Run("text join: a text on its own arc is not bent to a curved wall too", OwnArcOnWall);
            Program.Run("text join: the studs of a style with an effect are the style's", StudsOfStyle);
        }

        private static SignInstance Instance(string extra) => SignInstances.Of(SignCatalog.ById(TextBoxes.TemplateId), TextBoxes.Template.Parse(extra.TrimStart(';')).With("text", "文字灯箱").ToString());

        private static SignParts Build(SignInstance instance, string support) =>
            SignBuilder.BuildAssembly(new List<AssemblyBoard> { new AssemblyBoard { Instance = instance } }, SupportCatalog.ById(support), 0f, SupportDetail.Full, default);

        private static void OwnArcOnWall()
        {
            TextPlans.Clear();
            SignInstance onFlat = Instance(";h=0.4;curve=arch;sweep=120"), onCurved = Instance(";h=0.4;curve=arch;sweep=120;arc=12");
            SignParts a = Build(onFlat, SupportCatalog.WallFlat), b = Build(onCurved, SupportCatalog.WallFlat);
            MeshBuilder ga = a.Solids[(int)onFlat.Board.Text.Glow], gb = b.Solids[(int)onCurved.Board.Text.Glow];
            Program.Check(ga.VertexCount > 0 && ga.VertexCount == gb.VertexCount, "the same letters: " + ga.VertexCount + " and " + gb.VertexCount);
            float worst = 0f;
            for (int i = 0; i < ga.VertexCount; i++)
                worst = Math.Max(worst, (ga.Positions[i] - gb.Positions[i]).Length);
            Program.Check(worst < 1e-4f, "a text with a sweep keeps its own arc whatever the wall's radius: the letters differ by " + worst);

            // And a straight text on that curved wall is still bent (the other fork's behaviour is not lost).
            SignInstance straightFlat = Instance(";h=0.4"), straightCurved = Instance(";h=0.4;arc=12");
            MeshBuilder sf = Build(straightFlat, SupportCatalog.WallFlat).Solids[(int)straightFlat.Board.Text.Glow], sc = Build(straightCurved, SupportCatalog.WallFlat).Solids[(int)straightCurved.Board.Text.Glow];
            float zFlat = sf.Positions.Max(p => p.Z) - sf.Positions.Min(p => p.Z), zCurved = sc.Positions.Max(p => p.Z) - sc.Positions.Min(p => p.Z);
            Program.Check(zCurved > zFlat + 0.015f, "a straight text follows a curved wall: z spread " + zCurved + " against " + zFlat);
        }

        private static void StudsOfStyle()
        {
            TextPlans.Clear();
            foreach (string support in new[] { SupportCatalog.WallFlat, SupportCatalog.DoublePole })
            {
                SignInstance plain = Instance(";h=0.5"), hollow = Instance(";h=0.5;style=" + TextStyles.Outline);
                SignParts plainParts = Build(plain, support), hollowParts = Build(hollow, support);
                FramePlan frame = TextPlans.Peek(hollow.Board.Text.Job).Frame;
                Program.Check(frame != null && frame.Studs.Count > 0, support + ": the hollow text has studs");
                float studPlain = TextStyles.FrameFor(0.5f, plain.Board.Text.Job.Art).StudRadius, studHollow = TextStyles.FrameFor(0.5f, hollow.Board.Text.Job.Art).StudRadius;
                Program.Check(studHollow < studPlain - 1e-4f, support + ": a hollow letter's studs are thinner than a plain letter's: " + studHollow + " against " + studPlain);
                Program.Check(Reach(hollowParts, hollow, support) < Reach(plainParts, plain, support) - 1e-4f, support + ": the built studs are thinner too: " + Reach(hollowParts, hollow, support) + " against " + Reach(plainParts, plain, support));
            }
        }

        /// <summary>How far the metal's vertices on the first stud's axis plane are from it: the stud's radius as built (the largest distance of a vertex at the stud's own z from the axis, within 3 cm of it).</summary>
        private static float Reach(SignParts parts, SignInstance instance, string support)
        {
            FramePlan frame = TextPlans.Peek(instance.Board.Text.Job).Frame;
            float cx = parts.Picks.Find(p => p.Name == "board").CentreX, cy = parts.Picks.Find(p => p.Name == "board").CentreY;
            FrameStud stud = frame.Studs[0];
            float ax = cx - stud.At.X, ay = cy + stud.At.Y, best = 0f;
            foreach (Vec3 p in parts.Metal.Positions)
            {
                float d = (float)Math.Sqrt((p.X - ax) * (p.X - ax) + (p.Y - ay) * (p.Y - ay));
                if (d < 0.03f && d > best && Math.Abs(p.Z - parts.Layout.BackZ) < 1e-4f)
                    best = d;
            }

            return best;
        }
    }
}
