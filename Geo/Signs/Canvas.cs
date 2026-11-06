using System;
using System.Collections.Generic;

namespace CDLC.TS.Geo.Signs
{
    /// <summary>
    /// A drawing area on a part of a board, in units of 0 to 100 in each direction (x to the right, y down), so that a pictogram or a layout is written once and is the same at every size of
    /// the sign. The box is the part of the sign the standard gives to the figure (inside the red ring of a prohibition sign, inside the black edge of a warning sign); what is drawn may
    /// reach outside it a little (units below 0 or above 100) when the figure of the standard does. Everything added goes to the part's elements, in order: later things are drawn over earlier ones.
    /// </summary>
    public sealed class Canvas
    {
        public readonly GuidePart Part;

        /// <summary>The box in metres, in the part's frame (from its top left corner, y down).</summary>
        public readonly float X, Y, W, H;

        public Canvas(GuidePart part, float x, float y, float w, float h)
        {
            Part = part;
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        /// <summary>Units to metres, along x and along y.</summary>
        public float Mx(float u) => u * W / 100f;

        public float My(float v) => v * H / 100f;

        private float Px(float u) => X + u * W / 100f;

        private float Py(float v) => Y + v * H / 100f;

        public Canvas Add(GuideElement element)
        {
            element.Art = true;
            Part.Elements.Add(element);
            return this;
        }

        /// <summary>A pictogram written in the 100 x 100 box, put into the area (ux, uy, uw, uh) of the canvas (the whole canvas by default).</summary>
        public Canvas Pic(string path, GuideColour colour, float ux = 0f, float uy = 0f, float uw = 100f, float uh = 100f) =>
            Add(GuideKit.Picture(path, Px(ux), Py(uy), uw * W / 100f, uh * H / 100f, colour));

        /// <summary>A pictogram scaled evenly so that its own bounds fit the area, and centred in it.</summary>
        public Canvas Fit(string path, GuideColour colour, float ux = 0f, float uy = 0f, float uw = 100f, float uh = 100f) =>
            Add(GuideKit.FitPicture(path, Px(ux), Py(uy), uw * W / 100f, uh * H / 100f, colour));

        /// <summary>A polygon of x, y pairs in units.</summary>
        public Canvas Poly(GuideColour colour, params float[] units)
        {
            var m = new float[units.Length];
            for (int i = 0; i + 1 < units.Length; i += 2)
            {
                m[i] = Px(units[i]);
                m[i + 1] = Py(units[i + 1]);
            }

            return Add(GuideKit.Poly(m, colour));
        }

        public Canvas Rect(GuideColour colour, float ux, float uy, float uw, float uh, float radiusUnits = 0f) =>
            Add(GuideKit.Rect(Px(ux), Py(uy), uw * W / 100f, uh * H / 100f, colour, radiusUnits * W / 100f));

        public Canvas Oval(GuideColour colour, float ux, float uy, float uw, float uh) =>
            Add(GuideKit.Oval(Px(ux), Py(uy), uw * W / 100f, uh * H / 100f, colour));

        /// <summary>A straight bar between two points (units), with a thickness (units of the canvas width).</summary>
        public Canvas Bar(GuideColour colour, float ux0, float uy0, float ux1, float uy1, float thicknessUnits) =>
            Add(GuideKit.Bar(Px(ux0), Py(uy0), Px(ux1), Py(uy1), thicknessUnits * W / 100f, colour));

        /// <summary>Letters and digits: the ink as high as the box (the area uh), squeezed if wider than uw.</summary>
        public Canvas Ink(string text, GuideColour colour, float ux, float uy, float uw, float uh, GuideAlign align = GuideAlign.Centre, string inkRef = null) =>
            Add(GuideKit.Ink(text, Px(ux), Py(uy), uw * W / 100f, uh * H / 100f, colour, align, inkRef));

        /// <summary>Chinese characters, one to a square cell of side ucell (units of the canvas height), ugap between cells; the run is aligned in the width uw (0: its own width).</summary>
        public Canvas Cells(string text, GuideColour colour, float ux, float uy, float ucell, float ugap = 0f, GuideAlign align = GuideAlign.Left, float uw = 0f)
        {
            float h = ucell * H / 100f, gap = ugap * H / 100f;
            return Add(GuideKit.Cells(text, Px(ux), Py(uy), h, colour, gap, align, uw * W / 100f));
        }

        /// <summary>One of the standard's six arrows (GB 5768.2 Fig. 204) in a square of side usize (units of the canvas width).</summary>
        public Canvas Arrow(GuideArrow direction, GuideColour colour, float ux, float uy, float usize) =>
            Add(GuideKit.Poly(GuideKit.ArrowPolygon(direction, Px(ux), Py(uy), usize * W / 100f), colour));
    }
}
