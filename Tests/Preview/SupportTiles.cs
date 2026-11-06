using System;
using System.Collections.Generic;
using System.IO;
using CDLC.TS.Geo;
using CDLC.TS.Render;

namespace CDLC.TS.Preview
{
    /// <summary>The panel's tiles of the supports named after --support-tiles (ids), written as thumb.&lt;file&gt; in the output folder: a look at a few without making all of them.</summary>
    internal static class SupportTiles
    {
        public static int Run(string output, IEnumerable<string> ids)
        {
            foreach (string id in ids)
            {
                SupportDef support = SupportCatalog.ById(id);
                if (support == null)
                {
                    Console.WriteLine("FAIL no such support: " + id);
                    return 1;
                }

                SignInstance carrier = Thumbnails.CarrierFor(support);
                byte[] picture = LedScreens.PaintFace(carrier, out int w, out int h);
                string file = "thumb." + Thumbnails.FileName("p", support.Id);
                File.WriteAllBytes(Path.Combine(output, file), Thumbnails.SupportPng(support, picture, w, h));
                Console.WriteLine(file);
            }

            return 0;
        }
    }
}
