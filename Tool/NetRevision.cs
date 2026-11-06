namespace CDLC.TS
{
    /// <summary>
    /// A number that goes up when the road network changes (an edge, a node or a road's name is made, updated or deleted, or a city is loaded): what was read from the network is kept as long as it stands
    /// still. Set by ItemRoadSystem from the game's own Updated, Created and Deleted marks, so that nothing is polled. Main thread.
    /// </summary>
    internal static class NetRevision
    {
        internal static int Value { get; private set; }

        internal static void Bump() => Value++;
    }
}
