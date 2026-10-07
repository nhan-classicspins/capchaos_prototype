using System.Collections.Generic;
using System.Text;

namespace SheetSync
{
    // Minimal RFC4180 CSV parser: handles quoted fields, doubled-quote escaping, and embedded
    // newlines/commas inside quoted fields (needed because the sheet's cells hold pretty-printed JSON).
    public static class CsvTableParser
    {
        // First row is treated as the header. Returns one dictionary per data row, keyed by header name.
        public static List<Dictionary<string, string>> Parse(string csv)
        {
            var rows = ParseRows(csv);
            var result = new List<Dictionary<string, string>>();
            if (rows.Count == 0)
            {
                return result;
            }

            var header = rows[0];
            for (var r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                var dict = new Dictionary<string, string>();
                for (var c = 0; c < header.Count; c++)
                {
                    dict[header[c]] = c < row.Count ? row[c] : string.Empty;
                }
                result.Add(dict);
            }
            return result;
        }

        static List<List<string>> ParseRows(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;
            var i = 0;
            var length = csv.Length;

            while (i < length)
            {
                var c = csv[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < length && csv[i + 1] == '"')
                        {
                            field.Append('"');
                            i += 2;
                            continue;
                        }
                        inQuotes = false;
                        i++;
                        continue;
                    }
                    field.Append(c);
                    i++;
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        i++;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        i++;
                        break;
                    case '\r':
                        i++;
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        i++;
                        break;
                    default:
                        field.Append(c);
                        i++;
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}
