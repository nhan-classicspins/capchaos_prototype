using System;
using System.Collections.Generic;
using UnityEngine;

namespace SheetSync
{
    // Where the sheet is and how it maps onto the project. One row per config Id; IdColumn holds the Id,
    // and every other column listed in Columns holds one config's full JSON for that Id.
    //
    //   Id | Level        | Conveyor
    //   1  | { "Id": 1 …} | { "Id": 1 … }
    //
    // The editor window (Window > Sheet Sync) writes each cell into AssetFolder/FileNameFormat; at runtime,
    // SheetConfigOverride hands each cell to whatever the game registered for that column.
    [CreateAssetMenu(fileName = "SheetSyncSettings", menuName = "Sheet Sync/Settings")]
    public class SheetSyncSettings : ScriptableObject
    {
        [Tooltip("Turns the runtime override off without clearing the URL. The editor window ignores it.")]
        public bool Enabled = true;

        [Tooltip("The sheet's CSV export URL. For a Google Sheet shared as 'anyone with the link can view':\n" +
                 "https://docs.google.com/spreadsheets/d/<sheet id>/gviz/tq?tqx=out:csv&gid=<tab gid>")]
        public string SheetUrl;

        [Tooltip("Network timeout for the runtime fetch, in seconds.")]
        public int TimeoutSeconds = 5;

        [Tooltip("Header of the column holding each row's integer Id.")]
        public string IdColumn = "Id";

        public List<SheetColumn> Columns = new List<SheetColumn>();

        public SheetColumn FindColumn(string columnName)
        {
            foreach (var column in Columns)
            {
                if (column != null && column.ColumnName == columnName)
                {
                    return column;
                }
            }

            return null;
        }
    }

    // One JSON column on the sheet, and where its configs live in the project.
    [Serializable]
    public class SheetColumn
    {
        [Tooltip("Header of the column on the sheet.")]
        public string ColumnName = "Level";

        [Tooltip("Project folder the bundled JSON files are in, e.g. Assets/Configs/Levels.")]
        public string AssetFolder = "Assets/Configs/Levels";

        [Tooltip("File name for an Id, as a string.Format pattern with the Id as {0}, e.g. lvl_{0:000}.json.")]
        public string FileNameFormat = "lvl_{0:000}.json";

        [Tooltip("Name of the field inside the JSON that must equal the row's Id. The sheet's row Id wins: " +
                 "a JSON whose field disagrees is corrected on sync. Leave empty to skip the check.")]
        public string JsonIdField = "Id";

        [Tooltip("Optional. Full name of the C# type each cell must deserialize into (e.g. MyGame.Level, or " +
                 "an assembly-qualified name), so a cell with the wrong shape is caught by the editor sync " +
                 "rather than by the game. Empty = only checked to be a JSON object.")]
        public string ValidateAsType;

        [Tooltip("Optional. Addressables group a NEW file is added to on sync (existing files keep theirs). " +
                 "Needs com.unity.addressables. Empty = new files aren't made addressable.")]
        public string AddressablesGroup;

        [Tooltip("Optional. Label put on a new file's Addressables entry, e.g. the label the game loads its " +
                 "configs by.")]
        public string AddressablesLabel;

        public string FileNameFor(int id)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, FileNameFormat, id);
        }

        public string AssetPathFor(int id)
        {
            return $"{AssetFolder.TrimEnd('/')}/{FileNameFor(id)}";
        }
    }
}
