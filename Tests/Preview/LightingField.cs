using System;
using System.Linq;
using System.Text;
using CDLC.TS.Geo;

namespace CDLC.TS.Preview
{
    /// <summary>The editor rows of a template as the panel's script takes them (k, l, t, v, ch, section), for the dock preview: Preview.exe root out --editor-rows id [en]: written to out/rows.<id>.json as UTF-8 (the console would put it in the system code page).</summary>
    internal static class EditorRows
    {
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public static int Run(string output, string id, bool zh)
        {
            SignChoice choice = SignCatalog.Choices.FirstOrDefault(c => c.Id == id);
            if (choice?.Def?.Template == null)
            {
                Console.WriteLine("FAIL no template " + id);
                return 1;
            }

            GuideTemplate template = choice.Def.Template;
            GuideSpec spec = template.Parse(choice.Spec ?? "");
            var rows = new StringBuilder("[");
            bool first = true;
            foreach (GuideField field in template.Fields)
            {
                if (field.Visible != null && !field.Visible(spec))
                    continue;
                string kind = field.Kind == GuideFieldKind.Choice ? "choice" : field.Kind == GuideFieldKind.Flag ? "flag" : "text";
                rows.Append(first ? "" : ",").Append("{\"k\":").Append(Q(field.Key)).Append(",\"l\":").Append(Q(zh ? field.Zh : field.En)).Append(",\"t\":").Append(Q(kind)).Append(",\"v\":").Append(Q(spec.Get(field.Key))).Append(",\"max\":").Append(field.MaxLength);
                string section = zh ? field.SectionZh : field.SectionEn;
                if (!string.IsNullOrEmpty(section))
                    rows.Append(",\"s\":").Append(Q(section));
                if (field.Kind == GuideFieldKind.Choice)
                    rows.Append(",\"ch\":[").Append(string.Join(",", field.Choices.Select(c => "{\"v\":" + Q(c.Value) + ",\"l\":" + Q(zh ? c.Zh : c.En) + "}"))).Append(']');
                rows.Append('}');
                first = false;
            }

            string file = System.IO.Path.Combine(output, "rows." + id + ".json");
            System.IO.File.WriteAllText(file, rows.Append(']').ToString(), new UTF8Encoding(false));
            Console.WriteLine(file);
            return 0;
        }
    }
}
