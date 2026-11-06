using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// Electric boxes on poles (0.12.23; the owner: "加入电箱，加入各种类型的电箱，能挂在立杆，其他杆子竖直部分和路灯杆上"): the distribution, street-lighting, signal-controller, camera, telecom and meter boxes
    /// Chinese roads have on their posts. Not signs and not in GB 5768: their sizes, colours and labels are the usual ones of pole-mounted boxes (抱杆箱), this mod's choice. A box is a board whose
    /// picture is its door (BoxBody: the body behind it is painted solids), so it goes wherever a board goes: on a post of its own, in the stack of a sign's post, on the post or the beam of a cantilever
    /// or a gantry, on a wall, and on the game's street-lamp posts and signal poles; it is fixed by the same channels and clamps.
    /// </summary>
    public static class Cabinets
    {
        public const string Group = "cabinet", Id = "cab.box";

        private static readonly GuideChoice[] kBodies =
        {
            new GuideChoice("grey", "浅灰", "Light grey"), new GuideChoice("white", "白色", "White"), new GuideChoice("steel", "不锈钢", "Stainless steel"), new GuideChoice("green", "墨绿", "Dark green")
        };

        private static readonly GuideChoice[] kLouvers =
        {
            new GuideChoice("none", "无", "None"), new GuideChoice("door", "门下部", "Lower door"), new GuideChoice("sides", "两侧", "Both sides"), new GuideChoice("both", "门下部与两侧", "Door and sides")
        };

        private static readonly GuideChoice[] kLabelStyles =
        {
            new GuideChoice("red", "红字", "Red letters"), new GuideChoice("plate", "白底黑字铭牌", "Black on a white plate"), new GuideChoice("black", "黑字", "Black letters")
        };

        private static readonly GuideChoice[] kHinges = { new GuideChoice("l", "左侧（把手在右）", "Left (handle right)"), new GuideChoice("r", "右侧（把手在左）", "Right (handle left)") };

        private static float F(GuideSpec s, string key, float fallback, float min, float max)
        {
            double v = s.Number(key, fallback);
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : (float)Math.Max(min, Math.Min(max, v));
        }

        public static IEnumerable<GuideTemplate> All()
        {
            yield return new GuideTemplate
            {
                Id = Id,
                Code = "电箱",
                Zh = "电箱（自定义）",
                En = "Electric box (custom)",
                Group = Group,
                Pinyin = "dx",
                Keywords = new[] { "电箱", "配电箱", "机箱", "设备箱", "抱杆箱", "electric box", "cabinet" },
                // The kinds are its presets (Presets); the template itself is not listed.
                Standalone = false,
                Fields = new[]
                {
                    GuideField.Digits("w", "宽（cm）", "Width (cm)", "40", 3),
                    GuideField.Digits("h", "高（cm）", "Height (cm)", "50", 3),
                    GuideField.Digits("d", "厚（cm）", "Depth (cm)", "18", 2),
                    GuideField.Choice("body", "箱体", "Body", "grey", kBodies),
                    GuideField.Flag("hood", "防雨檐", "Rain hood", true),
                    GuideField.Choice("louver", "百叶", "Louvers", "none", kLouvers),
                    GuideField.Flag("window", "观察窗（电表）", "Window (meters)", false),
                    GuideField.Flag("sticker", "当心触电标识", "Electric shock warning", true),
                    GuideField.Text("label", "标签", "Label", "配电箱", 10),
                    GuideField.Text("label2", "标签第二行（可空）", "Label line 2 (may be empty)", "", 10),
                    GuideField.Choice("lstyle", "标签样式", "Label style", "red", kLabelStyles),
                    GuideField.Text("no", "编号（可空）", "Number (may be empty)", "", 10),
                    GuideField.Choice("hinge", "门轴", "Hinges", "l", kHinges),
                    GuideField.Digits("y", "单独安装时下沿高（m）", "Lower edge on its own (m)", "2.5", 3)
                },
                Layout = Layout
            };
        }

        /// <summary>The kinds of box, as presets of the one template: the usual sizes and looks.</summary>
        public static IEnumerable<GuidePreset> Presets()
        {
            yield return P("cab.distribution", "配电", "抱杆配电箱", "Pole-mounted distribution box", "bgpdx", "label=配电箱", "配电箱", "配电", "distribution");
            yield return P("cab.streetlight", "路灯", "路灯控制箱", "Street-lighting control box", "ldkzx", "w=30;h=40;d=16;label=路灯控制箱;lstyle=plate;no=LD-01", "路灯", "控制箱", "street light");
            yield return P("cab.signal", "信号", "交通信号控制机（抱杆式）", "Traffic signal controller (pole-mounted)", "jtxhkzj", "w=55;h=75;d=32;louver=sides;label=交通信号控制机;lstyle=plate;y=2", "信号机", "信号控制", "signal controller");
            yield return P("cab.camera", "监控", "监控设备箱", "Camera equipment box", "jksbx", "w=50;h=60;d=25;body=white;louver=door;label=监控设备箱;lstyle=black;y=3", "监控", "电子警察", "摄像头", "camera");
            yield return P("cab.telecom", "通信", "通信接线箱", "Telecom junction box", "txjxx", "w=30;h=40;d=15;hood=0;sticker=0;label=通信设施;label2=严禁破坏", "通信", "光缆", "接线", "telecom");
            yield return P("cab.meter", "电表", "电表箱", "Meter box", "dbx", "w=40;h=55;d=18;window=1;label=电表箱;lstyle=plate", "电表", "计量", "meter");
            yield return P("cab.steel", "不锈钢", "不锈钢防水箱", "Stainless steel box", "bxgfsx", "d=20;body=steel;label=", "不锈钢", "防水", "stainless");
            yield return P("cab.green", "配电", "墨绿配电箱", "Dark green distribution box", "mlpdx", "w=50;h=60;d=22;body=green;lstyle=plate", "墨绿", "配电箱", "green");
            yield return P("cab.junction", "接线", "接线盒", "Junction box", "jxh", "w=20;h=25;d=12;hood=0;sticker=0;label=", "接线盒", "junction box");
        }

        private static GuidePreset P(string id, string code, string zh, string en, string pinyin, string overrides, params string[] keywords)
        {
            var words = new List<string>(keywords) { "电箱", "electric box" };
            return new GuidePreset { Id = id, Template = Id, Code = code, Zh = zh, En = en, Pinyin = pinyin, Overrides = overrides, Keywords = words.ToArray() };
        }

        /// <summary>
        /// The door of a box as a board (the part's frame: metres from its top left corner, y down): the box's colour, the gap round the door, the hinges on one side and the handle with its lock on the
        /// other; down the middle of what is left the window, the yellow shock-warning triangle, the label (on a white plate or in letters) and the number, all made smaller together when the door is
        /// small; louvers in the lower part of the door. The body (BoxBody) is as deep as set, of the same colour.
        /// </summary>
        private static GuideBoard Layout(GuideSpec s)
        {
            float w = F(s, "w", 40f, 15f, 100f) / 100f, h = F(s, "h", 50f, 15f, 140f) / 100f, d = F(s, "d", 18f, 8f, 50f) / 100f;
            string body = s.Get("body");
            GuideColour ground = body == "white" ? GuideColour.White : body == "steel" ? GuideColour.Steel : body == "green" ? GuideColour.Green : GuideColour.LightGray;
            SolidColour solid = body == "white" ? SolidColour.White : body == "steel" ? SolidColour.Steel : body == "green" ? SolidColour.Green : SolidColour.LightGray;
            GuideColour line = body == "green" ? GuideColour.Black : GuideColour.Gray;
            var door = new GuidePart { W = w, H = h, Ground = ground, Liner = 0f, Border = 0f, Radius = 0.006f };

            // The gap round the door and its hinges.
            float m = Math.Max(0.012f, Math.Min(0.04f, 0.06f * Math.Min(w, h))), t = Math.Max(0.003f, 0.006f * Math.Min(w, h));
            door.Elements.Add(GuideKit.Rect(m, m, w - 2f * m, t, line));
            door.Elements.Add(GuideKit.Rect(m, h - m - t, w - 2f * m, t, line));
            door.Elements.Add(GuideKit.Rect(m, m, t, h - 2f * m, line));
            door.Elements.Add(GuideKit.Rect(w - m - t, m, t, h - 2f * m, line));
            bool handleLeft = s.Is("hinge", "r");
            float hingeH = Math.Max(0.02f, Math.Min(0.05f, 0.08f * h)), hingeX = handleLeft ? w - m - 0.008f : m - 0.002f;
            foreach (float at in new[] { 0.15f, 0.85f })
                door.Elements.Add(GuideKit.Rect(hingeX, m + at * (h - 2f * m) - hingeH * 0.5f, 0.01f, hingeH, line));

            // The handle, with its lock above it; the 3D handle (BoxBody) stands out of the door there.
            float handleW = Math.Max(0.012f, Math.Min(0.018f, 0.045f * w)), handleH = Math.Max(0.05f, Math.Min(0.14f, 0.22f * h));
            float handleX = handleLeft ? m + 0.018f : w - m - 0.018f - handleW, handleY = h * 0.5f - handleH * 0.5f;
            door.Elements.Add(GuideKit.Rect(handleX, handleY, handleW, handleH, GuideColour.Black, handleW * 0.4f));
            float lockD = Math.Min(0.016f, handleW);
            door.Elements.Add(GuideKit.Oval(handleX + handleW * 0.5f - lockD * 0.5f, handleY - lockD - 0.008f, lockD, lockD, GuideColour.Gray));

            // The column between the hinges' gap and the handle.
            float left = handleLeft ? handleX + handleW + 0.015f : m + t + 0.015f, right = handleLeft ? w - m - t - 0.015f : handleX - 0.015f;
            float column = Math.Max(0.02f, right - left), middle = (left + right) * 0.5f;
            float gap = Math.Max(0.008f, 0.025f * h), top = m + t + gap, bottom = h - m - t - gap;

            // Louvers in the lower part of the door.
            string louver = s.Get("louver");
            if (louver == "door" || louver == "both")
            {
                float slat = 0.007f, pitch = 0.018f, height = Math.Max(0.04f, Math.Min(0.16f, 0.2f * h));
                int count = Math.Max(3, Math.Min(9, (int)(height / pitch)));
                float lw = column * 0.75f, y0 = bottom - count * pitch;
                for (int i = 0; i < count; i++)
                    door.Elements.Add(GuideKit.Rect(middle - lw * 0.5f, y0 + i * pitch + (pitch - slat) * 0.5f, lw, slat, line));
                bottom = y0 - gap;
            }

            // What goes down the column, at its own size; all of it smaller when it does not fit.
            string label = (s.Get("label") ?? "").Trim(), label2 = (s.Get("label2") ?? "").Trim(), number = (s.Get("no") ?? "").Trim();
            bool window = s.Flag("window"), sticker = s.Flag("sticker");
            float windowH = Math.Max(0.06f, Math.Min(0.22f, 0.28f * h)), stickerSide = Math.Max(0.05f, Math.Min(0.16f, 0.3f * Math.Min(column, h)));
            float ch = Math.Max(0.02f, Math.Min(0.05f, 0.065f * h));
            var lines = new List<string>();
            foreach (string l in new[] { label, label2 })
            {
                if (l.Length > 0)
                    lines.Add(l);
            }

            float labelH = lines.Count == 0 ? 0f : lines.Count * ch + (lines.Count - 1) * 0.35f * ch + 0.6f * ch;
            float numberH = number.Length == 0 ? 0f : 0.75f * ch + 0.3f * ch;
            float[] sizes = { window ? windowH : 0f, sticker ? stickerSide * 0.866f : 0f, labelH, numberH };
            float natural = 0f;
            int present = 0;
            foreach (float size in sizes)
            {
                natural += size;
                present += size > 0f ? 1 : 0;
            }

            float room = bottom - top - Math.Max(0, present - 1) * gap;
            float k = natural > 0f && room > 0f ? Math.Min(1f, room / natural) : 1f;
            float y = top + Math.Max(0f, room - natural * k) * 0.3f;
            if (window)
            {
                float ww = column * 0.85f, wh = windowH * k;
                door.Elements.Add(GuideKit.Rect(middle - ww * 0.5f, y, ww, wh, line));
                door.Elements.Add(GuideKit.Rect(middle - ww * 0.5f + 0.006f, y + 0.006f, Math.Max(0.002f, ww - 0.012f), Math.Max(0.002f, wh - 0.012f), GuideColour.Black));
                y += wh + gap;
            }

            if (sticker)
            {
                AddShockWarning(door, middle, y, Math.Min(stickerSide * k, column));
                y += Math.Min(stickerSide * k, column) * 0.866f + gap;
            }

            float textH = ch * k;
            GuideColour letters = s.Is("lstyle", "red") ? GuideColour.Red : GuideColour.Black;
            if (lines.Count > 0)
                y = AddText(door, lines, textH, letters, s.Is("lstyle", "plate"), middle, y, column) + gap;
            if (number.Length > 0)
                AddText(door, new List<string> { number }, textH * 0.75f, GuideColour.Black, true, middle, y, column);

            var board = new GuideBoard
            {
                H = textH,
                Box = new BoxBody
                {
                    Depth = d,
                    Colour = solid,
                    Hood = s.Flag("hood"),
                    SideLouvers = louver == "sides" || louver == "both",
                    HandleX = handleX,
                    HandleY = handleY,
                    HandleW = handleW,
                    HandleH = handleH,
                    Clearance = F(s, "y", 2.5f, 0.5f, 6f)
                }
            };
            board.Parts.Add(door);
            return board;
        }

        /// <summary>The shock-warning triangle (yellow, a black edge and a black lightning bolt) of side side, its apex at (cx, y).</summary>
        private static void AddShockWarning(GuidePart door, float cx, float y, float side)
        {
            float h = side * 0.866f, cy = y + h * 2f / 3f;
            float[] Triangle(float k) => new[] { cx, cy - k * h * 2f / 3f, cx + k * side * 0.5f, cy + k * h / 3f, cx - k * side * 0.5f, cy + k * h / 3f };
            door.Elements.Add(GuideKit.Poly(Triangle(1f), GuideColour.Black));
            door.Elements.Add(GuideKit.Poly(Triangle(0.8f), GuideColour.Yellow));
            float[] bolt = { 0.05f, -0.30f, -0.10f, 0.02f, 0.01f, 0.02f, -0.06f, 0.24f, 0.11f, -0.06f, 0f, -0.06f };
            var points = new float[bolt.Length];
            for (int i = 0; i < bolt.Length; i += 2)
            {
                points[i] = cx + bolt[i] * h;
                points[i + 1] = cy + bolt[i + 1] * h;
            }

            door.Elements.Add(GuideKit.Poly(points, GuideColour.Black));
        }

        /// <summary>Lines of text centred on cx from y down, on a white plate when asked, made smaller to fit the column; the y under it.</summary>
        private static float AddText(GuidePart door, List<string> lines, float ch, GuideColour colour, bool plate, float cx, float y, float column)
        {
            GuideBlock block = AuxiliarySigns.Lines(lines, ch, colour, 0.15f, 0.35f);
            float pad = plate ? 0.3f * ch : 0f, widest = column - 2f * pad;
            if (block.W > widest && widest > 0f)
            {
                ch *= widest / block.W;
                pad = plate ? 0.3f * ch : 0f;
                block = AuxiliarySigns.Lines(lines, ch, colour, 0.15f, 0.35f);
            }

            if (plate)
                door.Elements.Add(GuideKit.Rect(cx - block.W * 0.5f - pad, y, block.W + 2f * pad, block.H + 2f * pad, GuideColour.White, pad * 0.5f));
            door.Add(block, cx - block.W * 0.5f, y + pad);
            return y + block.H + 2f * pad;
        }
    }
}
