using System.Globalization;
using CDLC.TS.Geo;
using CDLC.TS.Geo.Signs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CDLC.TS
{
    /// <summary>
    /// The tilt of a standing light box by keys (0.12.76; the owner has no numpad and asked for keys and a slider): the = key leans it back by one more degree, the - key one less (Shift: five), held they
    /// repeat as the arrow keys do, and past upright the - key goes on into the depression angle (0.12.80: the letters look down); for the box about to be placed and for one that stands and is being edited.
    /// The slider in the editor sets the same field (GuideField.Slider).
    /// </summary>
    internal sealed partial class PlacementTool
    {
        /// <summary>Whether the keys of the tilt apply now (the hint lists them): a standing light box is chosen, or is the one being edited.</summary>
        private static bool s_StandTilt;

        private void ReadStandKeys(Keyboard keyboard)
        {
            s_StandTilt = StandSpecNow(out GuideSpec spec, out bool text);
            if (!s_StandTilt)
                return;
            int presses = ArrowPress(keyboard, Key.Equals) - ArrowPress(keyboard, Key.Minus);
            if (presses == 0)
                return;
            float max = text ? TextTilt.MaxDeg : StandBoxes.MaxTilt;
            float now = Mathf.Clamp(text ? TextTilt.Typed(spec) : StandBoxes.TypedTilt(spec), -max, max);
            float next = TextTilt.Step(now, presses, m_Shift, max);
            if (next != now)
                EditField(EditorTarget, "tilt", next.ToString("0", CultureInfo.InvariantCulture));
        }

        /// <summary>The settings of the standing light box or the text light box (the two that lean) the tool works on, the draft being placed or the board being edited; false when it is neither.</summary>
        private bool StandSpecNow(out GuideSpec spec, out bool text)
        {
            spec = null;
            text = false;
            if (!IsActive)
                return false;
            if (Mode == 2 && TryReadEdit(out TsItem item))
            {
                SignDef def = SignCatalog.ByHash(item.TypeHash);
                if (!Leans(def, out text))
                    return false;
                spec = def.Template.Parse(item.SpecText);
                return true;
            }

            if (Mode != 0)
                return false;
            SignChoice choice = SignCatalog.ChoiceById(SelectedId);
            if (!Leans(choice?.Def, out text))
                return false;
            spec = choice.Def.Template.Parse(Draft);
            return true;
        }

        private static bool Leans(SignDef def, out bool text)
        {
            text = def?.Template != null && def.Template.Id == TextBoxes.TemplateId;
            return text || (def?.Template != null && def.Template.Id == StandBoxes.TemplateId);
        }
    }
}
