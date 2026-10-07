using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace SheetSync
{
    // Overrides configs the game has already loaded with what the sheet says right now, so a designer can
    // tune a live build without shipping a new one. The game registers, per sheet column, what to do with a
    // cell; SyncAsync fetches the sheet and runs each non-empty cell through it.
    //
    //   var sync = new SheetConfigOverride(settings);
    //   sync.Register<Level>("Level", id => levelRepository.GetOrAdd(id));
    //   await sync.SyncAsync();
    //
    // Never throws for a bad network or a bad cell: a fetch failure leaves every config as it was, and a
    // cell that won't parse leaves just that one config as it was - each logged as a warning.
    public class SheetConfigOverride
    {
        readonly SheetSyncSettings _settings;
        readonly Dictionary<string, Action<int, string>> _handlers = new Dictionary<string, Action<int, string>>();

        public SheetConfigOverride(SheetSyncSettings settings)
        {
            _settings = settings;
        }

        // Raw form: receives the row's Id and the cell's JSON text.
        public void Register(string column, Action<int, string> apply)
        {
            _handlers[column] = apply;
        }

        // Typed form: the cell is parsed as T and REPLACES every public field/property of the object
        // getOrAdd returns for that Id - a designer pastes the whole object, not just the fields to tweak.
        // The object itself is kept (copied into, not swapped), since other systems may already hold it.
        // The column's JsonIdField, if set, is forced to the row's Id afterwards.
        //
        // getOrAdd decides what an Id the game doesn't have yet means: return a new, registered object to
        // add it, or null to ignore that row.
        public void Register<T>(string column, Func<int, T> getOrAdd) where T : class
        {
            var idField = _settings != null ? _settings.FindColumn(column)?.JsonIdField : null;

            Register(column, (id, json) =>
            {
                var source = JsonConvert.DeserializeObject<T>(json);
                var target = getOrAdd(id);
                if (source == null || target == null)
                {
                    return;
                }

                CopyMembers(source, target);

                if (!string.IsNullOrEmpty(idField))
                {
                    TrySetId(target, idField, id);
                }
            });
        }

        // Fetches the sheet and applies it. False when it was skipped (disabled, no URL) or the fetch failed.
        public async Task<bool> SyncAsync()
        {
            if (_settings == null || !_settings.Enabled || string.IsNullOrWhiteSpace(_settings.SheetUrl))
            {
                return false;
            }

            string csv;
            try
            {
                csv = await SheetCsvFetcher.FetchAsync(_settings.SheetUrl, _settings.TimeoutSeconds);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SheetSync] Sheet fetch failed, bundled configs kept. {e.Message}");
                return false;
            }

            List<SheetRow> rows;
            try
            {
                rows = SheetRow.FromCsv(csv, _settings.IdColumn);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SheetSync] Sheet couldn't be parsed, bundled configs kept. {e.Message}");
                return false;
            }

            Apply(rows);
            return true;
        }

        // Runs every registered column of every row through its handler. Returns how many cells applied.
        public int Apply(IEnumerable<SheetRow> rows)
        {
            var applied = 0;
            foreach (var row in rows)
            {
                foreach (var pair in _handlers)
                {
                    if (!row.TryGetCell(pair.Key, out var json))
                    {
                        continue;
                    }

                    try
                    {
                        pair.Value(row.Id, json);
                        applied++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[SheetSync] {pair.Key} {row.Id}: couldn't apply the cell, kept as it was. {e.Message}");
                    }
                }
            }

            return applied;
        }

        // Every public instance field, and every public read/write property, from source onto target.
        static void CopyMembers<T>(T source, T target)
        {
            var type = typeof(T);

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                {
                    field.SetValue(target, field.GetValue(source));
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                    && property.GetSetMethod() != null)
                {
                    property.SetValue(target, property.GetValue(source));
                }
            }
        }

        static void TrySetId(object target, string memberName, int id)
        {
            var type = target.GetType();

            var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == typeof(int))
            {
                field.SetValue(target, id);
                return;
            }

            var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType == typeof(int))
            {
                property.SetValue(target, id);
            }
        }
    }
}
