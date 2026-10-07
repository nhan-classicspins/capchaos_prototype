using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
#if SHEETSYNC_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

namespace SheetSync.Editor
{
    // Pulls every column listed in a SheetSyncSettings from its sheet and writes the cells into the
    // project's bundled JSON files, so the sheet's values ship in the build instead of only arriving over
    // the network.
    //
    // Two steps on purpose: Fetch & Preview only reads, and lists per Id and column what would change;
    // nothing is written until Apply. Cells are compared as JSON, not as text, so one that only differs in
    // whitespace reads as unchanged. What gets written is the cell's own text (line endings normalised), so
    // a sheet formatted like the files keeps the git diff down to the real change.
    //
    // A cell for an Id with no file yet creates one - added to the column's Addressables group and label,
    // when it has them, so a game that loads its configs by label sees it. A file whose Id isn't on the
    // sheet is reported and left alone; this never deletes anything.
    public class SheetSyncWindow : EditorWindow
    {
        const int TimeoutSeconds = 30;

        enum ChangeKind
        {
            Unchanged,
            Changed,
            New,
            Invalid,
            Empty,
        }

        class Entry
        {
            public int Id;
            public SheetColumn Column;
            public string Path;
            public ChangeKind Change;
            public string Detail;
            public string ContentToWrite;
        }

        SheetSyncSettings _settings;
        bool _isFetching;
        string _status;
        readonly List<Entry> _entries = new List<Entry>();
        readonly List<string> _notOnSheet = new List<string>();
        bool _showUnchanged;
        Vector2 _scroll;

        [MenuItem("Window/Sheet Sync")]
        static void Open()
        {
            var window = GetWindow<SheetSyncWindow>("Sheet Sync");
            window.minSize = new Vector2(560f, 360f);
            window.Show();
        }

        void OnEnable()
        {
            if (_settings == null)
            {
                var guid = AssetDatabase.FindAssets("t:" + nameof(SheetSyncSettings)).FirstOrDefault();
                if (guid != null)
                {
                    _settings = AssetDatabase.LoadAssetAtPath<SheetSyncSettings>(AssetDatabase.GUIDToAssetPath(guid));
                }
            }
        }

        void OnGUI()
        {
            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _settings = (SheetSyncSettings)EditorGUILayout.ObjectField("Settings", _settings, typeof(SheetSyncSettings), false);
                if (check.changed)
                {
                    _entries.Clear();
                    _notOnSheet.Clear();
                    _status = null;
                }
            }

            if (_settings == null)
            {
                EditorGUILayout.HelpBox("Create one with Assets > Create > Sheet Sync > Settings, set its sheet URL " +
                                        "and columns, and pick it here.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(_settings.SheetUrl) ? "(no sheet URL)" : _settings.SheetUrl,
                EditorStyles.miniLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_isFetching || string.IsNullOrWhiteSpace(_settings.SheetUrl)))
                {
                    if (GUILayout.Button(_isFetching ? "Fetching..." : "Fetch & Preview", GUILayout.Width(140f)))
                    {
                        FetchAndPreview();
                    }
                }

                var pending = _entries.Count(e => e.Change == ChangeKind.Changed || e.Change == ChangeKind.New);
                using (new EditorGUI.DisabledScope(_isFetching || pending == 0))
                {
                    if (GUILayout.Button($"Apply ({pending})", GUILayout.Width(100f)))
                    {
                        Apply();
                    }
                }
            }

            if (!string.IsNullOrEmpty(_status))
            {
                EditorGUILayout.HelpBox(_status, MessageType.Info);
            }

            if (_entries.Count == 0 && _notOnSheet.Count == 0)
            {
                return;
            }

            _showUnchanged = EditorGUILayout.ToggleLeft("Show unchanged", _showUnchanged);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var entry in _entries)
            {
                if (!_showUnchanged && (entry.Change == ChangeKind.Unchanged || entry.Change == ChangeKind.Empty))
                {
                    continue;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{entry.Column.ColumnName} {entry.Id}", GUILayout.Width(140f));
                    EditorGUILayout.LabelField(entry.Change.ToString(), GUILayout.Width(80f));
                    EditorGUILayout.LabelField(entry.Detail ?? string.Empty);
                }
            }

            foreach (var path in _notOnSheet)
            {
                EditorGUILayout.LabelField($"Not on sheet, left alone: {path}");
            }

            EditorGUILayout.EndScrollView();
        }

        async void FetchAndPreview()
        {
            _isFetching = true;
            _status = "Fetching sheet...";
            _entries.Clear();
            _notOnSheet.Clear();
            Repaint();

            try
            {
                string csv;
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                {
                    csv = await client.GetStringAsync(_settings.SheetUrl);
                }

                BuildPreview(SheetRow.FromCsv(csv, _settings.IdColumn));
            }
            catch (Exception e)
            {
                _status = $"Fetch failed: {e.Message}";
                Debug.LogError($"[SheetSync] {_status}");
            }
            finally
            {
                _isFetching = false;
                Repaint();
            }
        }

        void BuildPreview(List<SheetRow> rows)
        {
            var columns = _settings.Columns.Where(c => c != null && !string.IsNullOrWhiteSpace(c.ColumnName)).ToList();
            var sheetIds = new HashSet<int>(rows.Select(r => r.Id));

            foreach (var row in rows)
            {
                foreach (var column in columns)
                {
                    _entries.Add(Compare(row, column));
                }
            }

            _entries.Sort((a, b) => a.Id != b.Id
                ? a.Id.CompareTo(b.Id)
                : string.CompareOrdinal(a.Column.ColumnName, b.Column.ColumnName));

            foreach (var column in columns)
            {
                CollectNotOnSheet(column, sheetIds);
            }

            int Count(ChangeKind kind) => _entries.Count(e => e.Change == kind);
            _status = $"{rows.Count} rows x {columns.Count} columns - {Count(ChangeKind.Changed)} changed, " +
                      $"{Count(ChangeKind.New)} new, {Count(ChangeKind.Unchanged)} unchanged, {Count(ChangeKind.Invalid)} invalid" +
                      (_notOnSheet.Count > 0 ? $", {_notOnSheet.Count} file(s) not on the sheet." : ".");
        }

        static Entry Compare(SheetRow row, SheetColumn column)
        {
            var entry = new Entry
            {
                Id = row.Id,
                Column = column,
                Path = column.AssetPathFor(row.Id),
            };

            if (!row.TryGetCell(column.ColumnName, out var cell))
            {
                entry.Change = ChangeKind.Empty;
                entry.Detail = "empty cell - file kept";
                return entry;
            }

            JObject sheetJson;
            try
            {
                sheetJson = JObject.Parse(cell);

                // Parsed into the real type too, when one is configured, so a cell that is valid JSON but
                // the wrong shape is caught here instead of when the game loads it.
                var validateType = ResolveType(column.ValidateAsType);
                if (validateType != null)
                {
                    JsonConvert.DeserializeObject(cell, validateType);
                }
                else if (!string.IsNullOrWhiteSpace(column.ValidateAsType))
                {
                    entry.Detail = $"type '{column.ValidateAsType}' not found - only checked as JSON. ";
                }
            }
            catch (Exception e)
            {
                entry.Change = ChangeKind.Invalid;
                entry.Detail = $"not valid: {e.Message}";
                return entry;
            }

            // The row's Id wins: the runtime override forces it onto the object too, so the file and the
            // sheet must agree on which config this is.
            var content = cell.Replace("\r\n", "\n").Trim();
            if (!string.IsNullOrEmpty(column.JsonIdField) && sheetJson.Value<int?>(column.JsonIdField) != row.Id)
            {
                sheetJson[column.JsonIdField] = row.Id;
                content = sheetJson.ToString(Formatting.Indented).Replace("\r\n", "\n");
                entry.Detail += $"{column.JsonIdField} in the JSON didn't match the row - fixed to the row's. ";
            }

            entry.ContentToWrite = content;

            if (!File.Exists(entry.Path))
            {
                entry.Change = ChangeKind.New;
                entry.Detail += "no file yet";
                return entry;
            }

            JObject fileJson;
            try
            {
                fileJson = JObject.Parse(File.ReadAllText(entry.Path));
            }
            catch (Exception)
            {
                entry.Change = ChangeKind.Changed;
                entry.Detail += "existing file isn't valid JSON";
                return entry;
            }

            if (JToken.DeepEquals(sheetJson, fileJson))
            {
                entry.Change = ChangeKind.Unchanged;
                return entry;
            }

            entry.Change = ChangeKind.Changed;
            entry.Detail += string.Join(", ", DifferingKeys(sheetJson, fileJson));
            return entry;
        }

        // A full name ("MyGame.Level") is looked up in every loaded assembly; an assembly-qualified one
        // directly.
        static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        static IEnumerable<string> DifferingKeys(JObject a, JObject b)
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var property in a.Properties())
            {
                keys.Add(property.Name);
            }

            foreach (var property in b.Properties())
            {
                keys.Add(property.Name);
            }

            return keys.Where(key => !JToken.DeepEquals(a[key], b[key]));
        }

        // Files in the column's folder whose name fits its pattern for some Id the sheet doesn't have.
        // Matched by formatting every candidate's Id back through the pattern, so any FileNameFormat works
        // without having to be parsed in reverse.
        void CollectNotOnSheet(SheetColumn column, HashSet<int> sheetIds)
        {
            if (!Directory.Exists(column.AssetFolder))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(column.AssetFolder))
            {
                var name = Path.GetFileName(path);
                var digits = new string(name.Where(char.IsDigit).ToArray());
                if (digits.Length == 0
                    || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                    || column.FileNameFor(id) != name)
                {
                    continue;
                }

                if (!sheetIds.Contains(id))
                {
                    _notOnSheet.Add(path.Replace('\\', '/'));
                }
            }
        }

        void Apply()
        {
            var toWrite = _entries.Where(e => e.Change == ChangeKind.Changed || e.Change == ChangeKind.New).ToList();
            if (toWrite.Count == 0)
            {
                return;
            }

            var newCount = toWrite.Count(e => e.Change == ChangeKind.New);
            if (!EditorUtility.DisplayDialog(
                    "Sync from sheet",
                    $"Overwrite {toWrite.Count - newCount} file(s) and create {newCount} new one(s) from the sheet?",
                    "Apply",
                    "Cancel"))
            {
                return;
            }

            var created = new List<Entry>();
            foreach (var entry in toWrite)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.Path) ?? entry.Column.AssetFolder);
                File.WriteAllText(entry.Path, entry.ContentToWrite);
                if (entry.Change == ChangeKind.New)
                {
                    created.Add(entry);
                }
            }

            AssetDatabase.Refresh();

            var registered = 0;
            foreach (var entry in created)
            {
                if (RegisterAddressable(entry))
                {
                    registered++;
                }
            }

            if (registered > 0)
            {
                AssetDatabase.SaveAssets();
            }

            foreach (var entry in toWrite)
            {
                entry.Change = ChangeKind.Unchanged;
                entry.Detail = "written";
            }

            _status = $"Wrote {toWrite.Count} file(s) ({created.Count} new).";
            Debug.Log($"[SheetSync] {_status} " + string.Join(", ", toWrite.Select(e => Path.GetFileName(e.Path))));
            Repaint();
        }

        // New files only - an existing one already has its entry. Addressed by file name without the
        // extension, like a hand-added one would be.
        static bool RegisterAddressable(Entry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.Column.AddressablesGroup))
            {
                return false;
            }

#if SHEETSYNC_ADDRESSABLES
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError($"[SheetSync] No Addressables settings - {entry.Path} was written but isn't addressable.");
                return false;
            }

            var group = settings.FindGroup(entry.Column.AddressablesGroup);
            if (group == null)
            {
                Debug.LogError($"[SheetSync] Addressables group '{entry.Column.AddressablesGroup}' not found - {entry.Path} was written but isn't addressable.");
                return false;
            }

            var guid = AssetDatabase.AssetPathToGUID(entry.Path);
            var addressableEntry = settings.CreateOrMoveEntry(guid, group, false, false);
            addressableEntry.address = Path.GetFileNameWithoutExtension(entry.Path);
            if (!string.IsNullOrWhiteSpace(entry.Column.AddressablesLabel))
            {
                addressableEntry.SetLabel(entry.Column.AddressablesLabel, true, true, false);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, addressableEntry, true);
            return true;
#else
            Debug.LogWarning($"[SheetSync] {entry.Column.ColumnName} has an Addressables group set, but com.unity.addressables " +
                             $"isn't installed - {entry.Path} was written but isn't addressable.");
            return false;
#endif
        }
    }
}
