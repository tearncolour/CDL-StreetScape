namespace CDLC.TS.Geo
{
    /// <summary>
    /// A click of the tool read with and without a modifier key. The game's Apply action carries a prohibition on Shift, Ctrl and Alt (Game.Input.ProhibitionModifierProcessor: a binding that does not
    /// declare a modifier gives nothing while the key is down), so a click with one of them held never reached the tool: the owner's 0.12.77 log had no click at all for the stretch delete (Shift and a
    /// click) and the tool's other modified clicks. The mouse button is read directly then, as the keys always were.
    /// </summary>
    public static class ClickRead
    {
        /// <summary>The left button went down with a modifier held and the game's action did not say so: the click is the tool's.</summary>
        public static bool Direct(bool applyPressed, bool leftPressed, bool modifierHeld) => !applyPressed && leftPressed && modifierHeld;
    }
}
