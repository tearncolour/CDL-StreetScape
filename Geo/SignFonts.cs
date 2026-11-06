using System;
using System.IO;

namespace CDLC.TS.Geo
{
    /// <summary>
    /// The font files the boards are painted and the road words are made with (0.12.28; the owner: "将字体更换为交通标志专用字体和西文的Traffic字体…保留自主更换字体接口"). Two roles: the Chinese characters
    /// (Cjk) and the letters and figures (Latin). In each the player's file is taken when there is one (cjk.ttf or 中文.ttf, latin.ttf or 西文.ttf, .otf too, in ModsData/CDLC.TS/Fonts), else the mod's
    /// own (Fonts/ in the mod's folder: BundledCjk, and a latin file if there is one); a role with no file takes the other's (the traffic sign font has its own letters), and with neither the painter
    /// falls back to an installed Chinese family. Read once when the mod is loaded: a font put in the folder is used from the next start of the game. Pure.
    /// </summary>
    public static class SignFonts
    {
        /// <summary>The traffic sign font the mod carries (交通标志专用字体_GB18030, STJTBZ_GB18030 1.10).</summary>
        public const string BundledCjk = "STJTBZ_GB18030.ttf";

        /// <summary>The names a file of each role may have (without the extension), and the extensions read.</summary>
        public static readonly string[] CjkNames = { "cjk", "中文" }, LatinNames = { "latin", "西文" }, Extensions = { ".ttf", ".otf" };

        /// <summary>The file of the Chinese characters and of the letters and figures (null: none, the other's or an installed family).</summary>
        public static string Cjk { get; private set; }
        public static string Latin { get; private set; }

        /// <summary>Where each came from: "player", "mod" or "" (none).</summary>
        public static string CjkFrom { get; private set; } = "";
        public static string LatinFrom { get; private set; } = "";

        /// <summary>Changes whenever the files do (what was made with other fonts is made again).</summary>
        public static int Version { get; private set; }

        /// <summary>
        /// Who the fonts are, for the names of files made with them (the thumbnails): the files' names and sizes, so that a font put in its place makes them again. "" when no file is set.
        /// </summary>
        public static string Stamp { get; private set; } = "";

        /// <summary>Sets the files (null for none); the tests and the preview tools set them by hand.</summary>
        public static void Use(string cjk, string latin, string cjkFrom = "mod", string latinFrom = "mod")
        {
            Cjk = string.IsNullOrEmpty(cjk) ? null : cjk;
            Latin = string.IsNullOrEmpty(latin) ? null : latin;
            CjkFrom = Cjk == null ? "" : cjkFrom;
            LatinFrom = Latin == null ? "" : latinFrom;
            Stamp = StampOf(Cjk) + "|" + StampOf(Latin);
            Version++;
        }

        /// <summary>Chooses the files: in each role the player's (in playerFolder) before the mod's (in modFolder). Either folder may be null or missing.</summary>
        public static void Resolve(string playerFolder, string modFolder)
        {
            string cjk = Find(playerFolder, CjkNames), latin = Find(playerFolder, LatinNames);
            string cjkFrom = cjk != null ? "player" : "mod", latinFrom = latin != null ? "player" : "mod";
            if (cjk == null)
            {
                string bundled = modFolder == null ? null : Path.Combine(modFolder, BundledCjk);
                cjk = bundled != null && File.Exists(bundled) ? bundled : Find(modFolder, CjkNames);
            }

            if (latin == null)
                latin = Find(modFolder, LatinNames);
            Use(cjk, latin, cjkFrom, latinFrom);
        }

        /// <summary>The first file of one of the names with one of the extensions in a folder; null when there is none.</summary>
        public static string Find(string folder, string[] names)
        {
            if (string.IsNullOrEmpty(folder))
                return null;
            try
            {
                if (!Directory.Exists(folder))
                    return null;
                foreach (string name in names)
                {
                    foreach (string extension in Extensions)
                    {
                        string path = Path.Combine(folder, name + extension);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch (Exception)
            {
                // A folder that cannot be read has no font for us.
            }

            return null;
        }

        private static string StampOf(string path)
        {
            if (path == null)
                return "";
            try
            {
                return Path.GetFileName(path) + ":" + new FileInfo(path).Length;
            }
            catch (Exception)
            {
                return Path.GetFileName(path);
            }
        }

        /// <summary>A line for the log and the panel: which file each role takes and from where.</summary>
        public static string Describe(bool zh)
        {
            string Name(string path, string from) => path == null ? null : Path.GetFileName(path) + (zh ? (from == "player" ? "（玩家）" : "（随 mod）") : from == "player" ? " (the player's)" : " (the mod's)");
            string cjk = Name(Cjk, CjkFrom) ?? (zh ? "系统已安装的中文字体" : "an installed Chinese font");
            string latin = Name(Latin, LatinFrom) ?? (zh ? "同中文字体" : "the Chinese font's own");
            return zh ? $"中文 {cjk}，西文 {latin}" : $"Chinese {cjk}, letters and figures {latin}";
        }
    }
}
