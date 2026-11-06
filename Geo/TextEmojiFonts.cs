using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The machine's symbol and emoji fonts for the text light box (0.12.78; the owner: "支持Emoji"): found in the system fonts folders by their file names (TextChain.EmojiFiles), read on a worker the first
    /// time (Segoe UI Emoji is 13 MB), and handed to TextFaces when they are ready. Until then a cluster that the text's fonts lack is a blank, as before; the editor and the tiles ask again when the fonts arrive
    /// (TextFonts.Changed: the same signal a rescan of the font list gives). No font file is shipped or copied. Off with features.systemFonts.
    /// </summary>
    public static class TextEmojiFonts
    {
        private static int s_State;
        private static readonly object s_Lock = new object();

        /// <summary>Where the log lines go (the mod's log); null: nowhere.</summary>
        public static Action<string> Log;

        /// <summary>The fonts found and read, for the log and the tests: the file names.</summary>
        public static IReadOnlyList<string> Loaded { get; private set; } = new string[0];

        /// <summary>Looks for the fonts in the folders and reads them on a worker, once for the run of the game (about 16 MB of font data stays in memory: the fonts are not those of a city).</summary>
        public static void Begin(IEnumerable<string> folders)
        {
            if (Interlocked.CompareExchange(ref s_State, 1, 0) != 0)
                return;
            var list = new List<string>(folders ?? new string[0]);
            Task.Run(() => Read(list));
        }

        private static void Read(List<string> folders)
        {
            var fonts = new List<FontOutlines>();
            var names = new List<string>();
            try
            {
                Dictionary<string, string> files = TextFonts.ListSystemFiles(folders);
                foreach (string file in TextChain.EmojiFiles)
                {
                    if (!files.TryGetValue(file, out string path))
                        continue;
                    try
                    {
                        var font = new FontOutlines(File.ReadAllBytes(path), 0);
                        if (font.Ok)
                        {
                            fonts.Add(font);
                            names.Add(file);
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        Log?.Invoke("Emoji fonts: " + file + " could not be read (" + ex.Message + ").");
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke("Emoji fonts: not read (" + ex.Message + ").");
            }

            lock (s_Lock)
            {
                TextFaces.UseEmojiFonts(fonts);
                Loaded = names;
            }

            Log?.Invoke(names.Count == 0 ? "Emoji fonts: none of " + string.Join(", ", TextChain.EmojiFiles) + " is in the system's fonts: an emoji is a blank." : "Emoji fonts: " + string.Join(", ", names) + " (colour layers where the font has them).");
            Interlocked.Exchange(ref s_State, 2);
            TextFonts.Changed?.Invoke();
        }
    }
}
