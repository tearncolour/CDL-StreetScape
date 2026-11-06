using System;
using System.Collections.Generic;
using System.IO;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The fonts a text light box can be set in (0.12.75, DESIGN-text-lightbox.md 3): the one the boards use (SignFonts.Cjk: the player's cjk file or the bundled traffic sign font) and every .ttf and .otf
    /// in the player's fonts folder (ModsData/CDLC.TS/Fonts), chosen by the file's name, which is what a text's settings keep. A font a text names that is not there (another PC's) is not a choice
    /// (GuideField.Sanitize), so the text is set in the default one. A font is read from its file the first time a text asks (8 to 9 MB for a Chinese one) and at most MaxLoaded are held; they go with the
    /// city (Release). Safe from any thread; the first reading is warmed on a worker (Warm), so that the main thread does not wait for it.
    /// </summary>
    public static partial class TextFonts
    {
        /// <summary>The value of the default font in a text's settings, and its id.</summary>
        public const string DefaultChoice = "bundled";

        public const int DefaultId = 1;

        /// <summary>The most fonts held in memory at once (the default font is one of them while a text uses it).</summary>
        public const int MaxLoaded = 3;

        private static readonly object s_Lock = new object();
        private static string s_Folder;
        private static List<string> s_Files = new List<string>();
        private static readonly Dictionary<int, FontOutlines> s_Loaded = new Dictionary<int, FontOutlines>();
        private static readonly List<int> s_Order = new List<int>();
        private static readonly HashSet<int> s_Failed = new HashSet<int>();

        /// <summary>The player's fonts folder (null: none).</summary>
        public static string Folder
        {
            get { lock (s_Lock) return s_Folder; }
        }

        /// <summary>Changes whenever the list of files or the fonts do: what was made with another font is made again.</summary>
        public static int Version { get; private set; }

        /// <summary>The player's fonts folder, scanned for the files a text can be set in; the default font is read on a worker.</summary>
        public static void Begin(string playerFolder)
        {
            lock (s_Lock)
            {
                s_Folder = playerFolder;
                s_Files = Scan(playerFolder);
                Version++;
            }

            Warm();
        }

        /// <summary>Reads the default font on a worker so that the first text does not wait for it (nothing happens when it is read or there is none).</summary>
        public static void Warm() => System.Threading.Tasks.Task.Run(() => Get(DefaultId));

        /// <summary>The file names a text can be set in, in the order they are listed: sorted, without the default font's own file.</summary>
        public static IReadOnlyList<string> Files
        {
            get { lock (s_Lock) return s_Files; }
        }

        /// <summary>The file names of a folder that are fonts this reader takes (.ttf and .otf), sorted; empty for a folder that is not there or cannot be read.</summary>
        public static List<string> Scan(string folder)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(folder))
                return found;
            try
            {
                if (!Directory.Exists(folder))
                    return found;
                foreach (string path in Directory.GetFiles(folder))
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (Array.IndexOf(Extensions, extension) >= 0)
                        found.Add(Path.GetFileName(path));
                }
            }
            catch (Exception)
            {
                // A folder that cannot be read has no fonts for us.
            }

            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        /// <summary>The choices of the font field: the default first, then each file by its name (the same text in both languages).</summary>
        public static GuideChoice[] Choices()
        {
            var choices = new List<GuideChoice> { new GuideChoice(DefaultChoice, "交通标志专用字体（默认）", "Traffic sign font (default)") };
            foreach (string name in Files)
                choices.Add(new GuideChoice(name, name, name));
            choices.AddRange(SystemChoices());
            return choices.ToArray();
        }

        /// <summary>The id of a font by the value a text keeps: the default's, or one made from the file's name (never the default's).</summary>
        public static int IdOf(string choice)
        {
            if (string.IsNullOrEmpty(choice) || choice == DefaultChoice)
                return DefaultId;
            int id;
            unchecked
            {
                int hash = (int)2166136261;
                foreach (char c in choice)
                    hash = (hash ^ c) * 16777619;
                id = hash == DefaultId || hash == 0 ? hash + 2 : hash;
            }

            if (choice.StartsWith(SystemPrefix, StringComparison.Ordinal))
                RememberSystem(choice, id);
            return id;
        }

        /// <summary>The file a font id stands for: the default font's file, a system font's, or a file of the folder by its name (a name that goes anywhere else is none); the face of a collection.</summary>
        private static string PathOf(int id, out int face)
        {
            face = 0;
            if (id == DefaultId)
                return SignFonts.Cjk;
            if (SystemSource(id, out Source source))
            {
                face = source.Face;
                return source.Path;
            }

            foreach (string name in Files)
            {
                if (IdOf(name) == id && name == Path.GetFileName(name) && s_Folder != null)
                    return Path.Combine(s_Folder, name);
            }

            return null;
        }

        /// <summary>The font of an id, read the first time; null when there is none or it cannot be read (not tried again until Release). Safe from any thread.</summary>
        public static FontOutlines Get(int id)
        {
            lock (s_Lock)
            {
                if (s_Loaded.TryGetValue(id, out FontOutlines held))
                {
                    s_Order.Remove(id);
                    s_Order.Add(id);
                    return held;
                }

                if (s_Failed.Contains(id))
                    return null;
                string path = PathOf(id, out int face);
                FontOutlines font = null;
                try
                {
                    if (path != null && File.Exists(path))
                    {
                        font = new FontOutlines(File.ReadAllBytes(path), face);
                        if (!font.Ok)
                            font = null;
                    }
                }
                catch (Exception)
                {
                    font = null;
                }

                if (font == null)
                {
                    s_Failed.Add(id);
                    return null;
                }

                while (s_Loaded.Count >= MaxLoaded && s_Order.Count > 0)
                {
                    s_Loaded.Remove(s_Order[0]);
                    s_Order.RemoveAt(0);
                }

                s_Loaded[id] = font;
                s_Order.Add(id);
                Version++;
                return font;
            }
        }

        /// <summary>Hands a font over for the tests and the preview tools (the id's file is not read).</summary>
        public static void Use(int id, FontOutlines font)
        {
            lock (s_Lock)
            {
                s_Failed.Remove(id);
                s_Loaded[id] = font;
                s_Order.Remove(id);
                s_Order.Add(id);
                Version++;
            }
        }

        /// <summary>Lets the fonts go (a city is left): they are read again when the next text wants one.</summary>
        public static void Release()
        {
            lock (s_Lock)
            {
                s_Loaded.Clear();
                s_Order.Clear();
                s_Failed.Clear();
                Version++;
            }
        }
    }
}
