using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("CDLC.TS")]
[assembly: AssemblyDescription("CSL Driving Life : StreetScape - placeable, self-snapping road signs, markings and street furniture")]
[assembly: AssemblyProduct("CDLC.TS")]
[assembly: ComVisible(false)]
// The Realistic Information Extension (0.12.29) reads the game for this mod and is built against its internals (TsExtension).
[assembly: InternalsVisibleTo("CDLC.TS.RIE")]
// AutoRoadLiner (CDLC.TS.ARL, 0.12.93) joins through TsExtension.Register and is built against the same internals.
[assembly: InternalsVisibleTo("CDLC.TS.ARL")]
[assembly: AssemblyVersion("0.12.51.0")]
[assembly: AssemblyFileVersion("0.12.51.0")]
