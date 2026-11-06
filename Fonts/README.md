# Fonts

The mod carries the traffic-sign font 交通标志专用字体_GB18030 (`STJTBZ_GB18030.ttf`, version 1.10 of 15 November 2013) in its Paradox Mods package. **It is not in this repository**: the font has no
licence file and names the Ministry of Transport of the PRC as its copyright holder, so a copy is not published here under the AGPL.

To build the mod or to run the tests that read the sign font (`FontLetterTests`, `TextLayoutTests`, `TextFontTests`, the bench in `Tests/Bench`), put your own copy of the file in this folder under
that name. The build copies everything in this folder to the mod's `Fonts` folder; without a font the mod falls back to a font of the player's (`ModsData/CDLC.TS/Fonts`) and then to the game's own.

`Tests/Fonts` holds Noto Sans SC Bold, which is under the SIL Open Font License (see `Tests/Fonts/OFL.txt`).
