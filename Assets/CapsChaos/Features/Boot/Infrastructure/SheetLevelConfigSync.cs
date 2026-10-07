using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using SheetSync;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;
using Game.Domain;
using Game.Presentation;

namespace Game.Infrastructure
{
    /// <summary>
    /// <see cref="ILevelConfigSync"/> over the level config spreadsheet, through the <c>com.cardfactory.sheetsync</c>
    /// package — two tabs, each described by its own <see cref="SheetSyncSettings"/> asset:
    /// <list type="bullet">
    /// <item><b>Levels</b> (addressable <see cref="LevelSettingsAddress"/>): column <see cref="LevelColumn"/> holds one
    /// level's whole JSON per row; the row's integer Id names the level through the column's file name format
    /// (<c>level_{0:0000}.json</c> → Id 18 is <c>level_0018</c>) — the mapping the package's editor window writes the
    /// bundled files with.</item>
    /// <item><b>Conveyors</b> (addressable <see cref="ConveyorSettingsAddress"/>): column <see cref="ConveyorColumn"/>
    /// holds one conveyor's whole JSON per row; the Id cell IS the conveyor id (<c>bean_42</c>) — a name, so this tab is
    /// read with the package's CSV fetcher and parser directly (its row reader only takes integer ids).</item>
    /// </list>
    /// Both tabs are fetched together and handed to <see cref="LevelCatalog.Override"/>, conveyors first, which checks
    /// every cell like a bundled file. Blank cells are skipped.
    /// </summary>
    /// <remarks>Root singleton: the boot node runs it once as the game starts, Main's Sync Config button whenever
    /// asked. One sync at a time — a second request while one runs is reported as skipped. Never throws (but for
    /// cancellation): a missing asset, a tab turned off, no URL or a failed fetch skips that tab and keeps what it
    /// covers as it was; only when neither tab could be read is the whole sync reported as skipped.</remarks>
    public sealed class SheetLevelConfigSync : ILevelConfigSync
    {
        /// <summary>The Addressables address of the levels tab's SheetSyncSettings.</summary>
        public const string LevelSettingsAddress = "SheetSyncSettings";
        /// <summary>The Addressables address of the conveyors tab's SheetSyncSettings.</summary>
        public const string ConveyorSettingsAddress = "SheetSyncSettingsConveyors";
        /// <summary>The sheet columns holding the JSON.</summary>
        public const string LevelColumn = "Level", ConveyorColumn = "Conveyor";

        private readonly IAssetService _assets;
        private readonly LevelCatalog _catalog;
        private bool _busy;

        public SheetLevelConfigSync(IAssetService assets, LevelCatalog catalog)
        {
            _assets = assets;
            _catalog = catalog;
        }

        public async UniTask<LevelSyncReport> SyncAsync(CancellationToken ct)
        {
            if (!_catalog.IsLoaded) return Skip("the bundled levels are not loaded yet");
            if (_busy) return Skip("a sync is already running");
            _busy = true;
            try
            {
                var (levels, conveyors) = await UniTask.WhenAll(
                    ReadTabAsync(LevelSettingsAddress, LevelColumn, numericIds: true, ct),
                    ReadTabAsync(ConveyorSettingsAddress, ConveyorColumn, numericIds: false, ct));
                if (levels.Cells == null && conveyors.Cells == null)
                    return Skip($"levels tab: {levels.Skipped}; conveyors tab: {conveyors.Skipped}");

                var unread = new List<string>();
                if (levels.Cells == null) unread.Add("levels tab not read: " + levels.Skipped);
                if (conveyors.Cells == null) unread.Add("conveyors tab not read: " + conveyors.Skipped);
                var report = _catalog.Override(levels.Cells ?? new Dictionary<string, string>(), conveyors.Cells).With(unread);

                Debug.Log($"[LevelSync] sheet: {levels.Cells?.Count ?? 0} level(s), {conveyors.Cells?.Count ?? 0} conveyor(s) — {report}" +
                          (report.ChangedConveyors.Count > 0 ? " — conveyors " + string.Join(", ", report.ChangedConveyors) : "") +
                          (report.Changed.Count > 0 ? " — levels " + string.Join(", ", report.Changed) : ""));
                foreach (var problem in report.Problems) Debug.LogWarning("[LevelSync] " + problem);
                return report;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { return Skip("failed: " + e.Message); }
            finally { _busy = false; }
        }

        /// <summary>
        /// One tab: id → the JSON in <paramref name="column"/>, for every row with a usable id and a non-blank cell
        /// (<paramref name="numericIds"/>: the Id is the row number of a level, mapped through the column's file name
        /// format; else it is used as it is). Cells null = the tab was not read, and Skipped says why.
        /// </summary>
        private async UniTask<(Dictionary<string, string> Cells, string Skipped)> ReadTabAsync(string address, string column,
            bool numericIds, CancellationToken ct)
        {
            var settings = await _assets.TryLoadAsync(new AssetKey<SheetSyncSettings>(address), ct);
            if (settings == null) return (null, $"no addressable '{address}' asset");
            try
            {
                if (!settings.Enabled) return (null, "turned off in " + settings.name);
                if (string.IsNullOrWhiteSpace(settings.SheetUrl)) return (null, "no Sheet Url in " + settings.name);
                var columnSettings = settings.FindColumn(column);
                if (columnSettings == null) return (null, $"no '{column}' column in {settings.name}");

                string csv;
                try
                {
                    // a pasted browser link (…/edit) would fetch an HTML page: always ask for the CSV export
                    csv = await SheetCsvFetcher.FetchAsync(CsvUrlOf(settings.SheetUrl), settings.TimeoutSeconds)
                        .AsUniTask().AttachExternalCancellation(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { return (null, "fetch failed: " + e.Message); }

                var cells = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var row in CsvTableParser.Parse(csv))
                {
                    if (!row.TryGetValue(settings.IdColumn, out var idText) || string.IsNullOrWhiteSpace(idText)) continue;
                    if (!row.TryGetValue(column, out var json) || string.IsNullOrWhiteSpace(json)) continue;
                    idText = idText.Trim();
                    if (numericIds)
                    {
                        if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) continue;   // notes, blank lines
                        idText = Path.GetFileNameWithoutExtension(columnSettings.FileNameFor(n));
                    }
                    else if (!ConveyorDefinition.IsConveyorId(idText)) continue;
                    cells[idText] = json;
                }
                if (cells.Count == 0 && !csv.Contains(column))
                    return (null, $"the sheet has no '{column}' column — is {settings.name}'s Sheet Url the right tab?");
                return (cells, null);
            }
            finally { _assets.Release(settings); }
        }

        /// <summary>
        /// The CSV export of a Google Sheet link: a browser link (<c>…/spreadsheets/d/&lt;id&gt;/edit?usp=sharing#gid=N</c>)
        /// becomes <c>…/spreadsheets/d/&lt;id&gt;/gviz/tq?tqx=out:csv&amp;gid=N</c> (no gid = the first tab). Any other URL —
        /// already a CSV export, or not a Google Sheet — is used as it is.
        /// </summary>
        public static string CsvUrlOf(string url)
        {
            const string marker = "/spreadsheets/d/";
            if (string.IsNullOrWhiteSpace(url)) return url;
            url = url.Trim();
            int at = url.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0 || url.Contains("tqx=out:csv") || url.Contains("format=csv")) return url;
            int idStart = at + marker.Length;
            int idEnd = url.IndexOfAny(new[] { '/', '?', '#' }, idStart);
            string id = idEnd < 0 ? url.Substring(idStart) : url.Substring(idStart, idEnd - idStart);
            string gid = null;
            int g = url.IndexOf("gid=", StringComparison.Ordinal);
            if (g >= 0)
            {
                int end = g + 4;
                while (end < url.Length && char.IsDigit(url[end])) end++;
                gid = url.Substring(g + 4, end - g - 4);
            }
            return url.Substring(0, at) + marker + id + "/gviz/tq?tqx=out:csv" + (string.IsNullOrEmpty(gid) ? "" : "&gid=" + gid);
        }

        private static LevelSyncReport Skip(string reason)
        {
            Debug.Log("[LevelSync] skipped: " + reason);
            return LevelSyncReport.NotRun(reason);
        }
    }
}
