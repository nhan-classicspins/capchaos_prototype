using System.Collections.Generic;
using System.Globalization;

namespace SheetSync
{
    // One sheet row: its Id and every cell by column header.
    public class SheetRow
    {
        public SheetRow(int id, IReadOnlyDictionary<string, string> cells)
        {
            Id = id;
            Cells = cells;
        }

        public int Id { get; }
        public IReadOnlyDictionary<string, string> Cells { get; }

        // The cell's text, or false when the column is missing or the cell is blank.
        public bool TryGetCell(string column, out string value)
        {
            if (Cells.TryGetValue(column, out value) && !string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = null;
            return false;
        }

        // Every row with a parseable integer in idColumn, in sheet order. Rows without one (blank lines,
        // notes) are skipped.
        public static List<SheetRow> FromCsv(string csv, string idColumn)
        {
            var rows = new List<SheetRow>();
            foreach (var cells in CsvTableParser.Parse(csv))
            {
                if (cells.TryGetValue(idColumn, out var idText)
                    && int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    rows.Add(new SheetRow(id, cells));
                }
            }

            return rows;
        }
    }
}
