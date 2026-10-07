using System;
using System.Collections.Generic;
using Game.Domain;

namespace Game.Application
{
    public sealed class LevelLoadException : Exception
    {
        public LevelLoadException(string message) : base(message) { }
    }

    /// <summary>
    /// The play order and every level, parsed and validated ONCE at boot (GDD §6). The boot node
    /// (<c>LevelConfigNode</c>) fetches the raw text from <c>Content/Configs/LevelConfig/</c> — the levels, then the shared
    /// conveyor files they name (<c>Content/Configs/ConveyorConfig/</c>) — and hands it to <see cref="Populate"/>; after that
    /// <see cref="Get"/> is a plain lookup — no I/O, no parsing mid-game.
    /// A level that fails V1–V5 is REFUSED with every problem named, and a refused level fails the whole
    /// load: the runtime never plays a malformed level. V6 (solvability) is CI's job
    /// (ContentLevelsTests / LevelTool validate), not a boot cost.
    /// </summary>
    public sealed class LevelCatalog
    {
        /// <summary>The play-order file next to the levels.</summary>
        public const string IndexFile = "levels.index.json";

        private IReadOnlyList<string> _order = Array.Empty<string>();
        private List<LevelDefinition> _levels = new List<LevelDefinition>();
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);   // level id → its live text
        private ConveyorLibrary _conveyors = new ConveyorLibrary();
        private readonly Dictionary<string, string> _conveyorTexts = new Dictionary<string, string>(StringComparer.Ordinal);   // conveyor id → its live text

        public bool IsLoaded { get; private set; }
        public IReadOnlyList<string> Order => _order;
        public int Count => _levels.Count;

        /// <summary>Index wraps past the end, so "next" after the last level replays from the first.</summary>
        public int Normalize(int index) => Count == 0 ? 0 : ((index % Count) + Count) % Count;

        public LevelDefinition Get(int index)
        {
            if (!IsLoaded) throw new LevelLoadException("levels are not loaded yet (LevelsLoaded boot cap)");
            return _levels[Normalize(index)];
        }

        /// <summary>The level ids <c>levels.index.json</c> lists, in play order.</summary>
        public static IReadOnlyList<string> ParseOrder(string indexJson)
        {
            if (indexJson == null) throw new LevelLoadException(IndexFile + " not found");
            JsonValue root;
            try { root = JsonReader.Parse(indexJson); }
            catch (JsonParseException e) { throw new LevelLoadException(IndexFile + ": " + e.Message); }
            if (!root.TryGet("order", out var order) || order.Kind != JsonKind.Array)
                throw new LevelLoadException(IndexFile + ": 'order' array missing");
            var ids = new List<string>();
            foreach (var item in order.Items)
            {
                if (item.Kind != JsonKind.String) throw new LevelLoadException(IndexFile + ": 'order' entries must be strings");
                ids.Add(item.String);
            }
            if (ids.Count == 0) throw new LevelLoadException(IndexFile + " lists no levels");
            return ids;
        }

        /// <summary>
        /// Parse and validate every level in <paramref name="order"/>. All-or-nothing: on any problem it throws
        /// one <see cref="LevelLoadException"/> naming every broken level, and the catalog stays as it was.
        /// </summary>
        /// <param name="levelTexts">Level id → JSON text; a missing id is a "listed but not found" problem.</param>
        /// <param name="conveyorTexts">Conveyor id → JSON text, for (at least) every conveyor the levels name; a
        /// conveyor a level names but that is missing here is that level's problem.</param>
        public void Populate(IReadOnlyList<string> order, IReadOnlyDictionary<string, string> levelTexts,
            IReadOnlyDictionary<string, string> conveyorTexts)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (levelTexts == null) throw new ArgumentNullException(nameof(levelTexts));
            if (conveyorTexts == null) throw new ArgumentNullException(nameof(conveyorTexts));
            if (order.Count == 0) throw new LevelLoadException(IndexFile + " lists no levels");

            var problems = new List<string>();
            var conveyors = ParseConveyors(conveyorTexts, problems);
            var levels = new List<LevelDefinition>(order.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in order)
            {
                if (!seen.Add(id)) { problems.Add($"{id}: listed twice in {IndexFile}"); continue; }
                if (!levelTexts.TryGetValue(id, out var text) || text == null) { problems.Add($"{id}: listed in the index but not found"); continue; }
                var parsed = LevelJson.Parse(text, conveyors);
                if (!parsed.Ok) { problems.Add($"{id}: " + string.Join("; ", parsed.Errors)); continue; }
                var semantic = LevelValidator.Validate(parsed.Level);
                if (semantic.Count > 0) { problems.Add($"{id}: " + string.Join("; ", semantic)); continue; }
                if (parsed.Level.Id != id) { problems.Add($"{id}: file declares id '{parsed.Level.Id}'"); continue; }
                levels.Add(parsed.Level);
            }
            if (problems.Count > 0) throw new LevelLoadException(string.Join(" | ", problems));

            _order = new List<string>(order);
            _levels = levels;
            _conveyors = conveyors;
            _conveyorTexts.Clear();
            foreach (var pair in conveyorTexts) if (conveyors.TryGet(pair.Key, out _)) _conveyorTexts[pair.Key] = pair.Value;
            _texts.Clear();
            foreach (string id in order) _texts[id] = levelTexts[id];
            IsLoaded = true;
        }

        /// <summary>
        /// Resync from the level config sheet: newer conveyor texts (conveyor id → JSON) first, then newer level texts
        /// (level id → JSON). Unlike <see cref="Populate"/> it is per item, and a refused item keeps what it had — a bad
        /// cell never breaks the game. The play order never changes.
        /// <list type="bullet">
        /// <item>A conveyor must parse, carry its own id and pass V8/V9 — and EVERY level that runs on it must still pass
        /// V1–V9 on the new layout; otherwise it is refused, naming the levels it would break. A conveyor no level runs
        /// on yet is added, so a synced level may name it.</item>
        /// <item>A level is parsed against the conveyors as they are now (synced ones included) and checked V1–V9 like a
        /// bundled one; a text that fails, names another id, or is for a level the index does not list is refused.</item>
        /// </list>
        /// </summary>
        public LevelSyncReport Override(IReadOnlyDictionary<string, string> levelTexts,
            IReadOnlyDictionary<string, string> conveyorTexts = null)
        {
            if (levelTexts == null) throw new ArgumentNullException(nameof(levelTexts));
            if (!IsLoaded) throw new LevelLoadException("levels are not loaded yet (LevelsLoaded boot cap)");
            var changedConveyors = new List<string>();
            var changed = new List<string>();
            var problems = new List<string>();
            int unchanged = 0;
            if (conveyorTexts != null)
                foreach (var pair in conveyorTexts)
                {
                    var outcome = OverrideConveyor(pair.Key, pair.Value, problems);
                    if (outcome == true) changedConveyors.Add(pair.Key);
                    else if (outcome == null) unchanged++;
                }
            foreach (var pair in levelTexts)
            {
                string id = pair.Key;
                int index = IndexOf(id);
                if (index < 0) { problems.Add($"{id}: not in {IndexFile} — not added"); continue; }
                if (SameText(_texts[id], pair.Value)) { unchanged++; continue; }
                if (!TryParseLevel(id, pair.Value, _conveyors, problems, out var level)) continue;
                _levels[index] = level;
                _texts[id] = pair.Value;
                changed.Add(id);
            }
            return new LevelSyncReport(changed, unchanged, problems, changedConveyors);
        }

        /// <summary>One conveyor of a resync: true = taken, false = refused (its problem added), null = same text.</summary>
        private bool? OverrideConveyor(string id, string text, List<string> problems)
        {
            string file = ConveyorJson.FileOf(id);
            if (_conveyorTexts.TryGetValue(id, out var current) && SameText(current, text)) return null;
            var parsed = ConveyorJson.Parse(text);
            if (!parsed.Ok) { problems.Add($"{file}: " + string.Join("; ", parsed.Errors)); return false; }
            if (parsed.Conveyor.Id != id) { problems.Add($"{file}: the text declares id '{parsed.Conveyor.Id}'"); return false; }
            var layout = ConveyorValidator.Validate(parsed.Conveyor);
            if (layout.Count > 0) { problems.Add($"{file}: " + string.Join("; ", layout)); return false; }

            var library = new ConveyorLibrary();
            foreach (var c in _conveyors.All) if (c.Id != id) library.Add(c);
            library.Add(parsed.Conveyor);
            // every level on this layout must still hold on the new one — all of them, or the conveyor is refused
            var relaid = new List<(int Index, LevelDefinition Level)>();
            var broken = new List<string>();
            for (int i = 0; i < _levels.Count; i++)
            {
                if (_levels[i].Conveyor.Id != id) continue;
                if (TryParseLevel(_order[i], _texts[_order[i]], library, broken, out var level)) relaid.Add((i, level));
            }
            if (broken.Count > 0)
            {
                problems.Add($"{file}: refused — it would break " + string.Join(" | ", broken));
                return false;
            }
            _conveyors = library;
            _conveyorTexts[id] = text;
            foreach (var (index, level) in relaid) _levels[index] = level;
            return true;
        }

        /// <summary>Parse and check level <paramref name="id"/>'s text (V1–V9) against <paramref name="conveyors"/>.</summary>
        private static bool TryParseLevel(string id, string text, ConveyorLibrary conveyors, List<string> problems, out LevelDefinition level)
        {
            level = null;
            var parsed = LevelJson.Parse(text, conveyors);
            if (!parsed.Ok) { problems.Add($"{id}: " + string.Join("; ", parsed.Errors)); return false; }
            var semantic = LevelValidator.Validate(parsed.Level);
            if (semantic.Count > 0) { problems.Add($"{id}: " + string.Join("; ", semantic)); return false; }
            if (parsed.Level.Id != id) { problems.Add($"{id}: the text declares id '{parsed.Level.Id}'"); return false; }
            level = parsed.Level;
            return true;
        }

        private static bool SameText(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

        private int IndexOf(string id)
        {
            for (int i = 0; i < _order.Count; i++) if (string.Equals(_order[i], id, StringComparison.Ordinal)) return i;
            return -1;
        }

        // line endings and the trailing newline never count as a change
        private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

        /// <summary>Every conveyor text → the library the levels are parsed against. A broken conveyor is reported
        /// once, by file, and left out — the levels naming it then fail on their own <c>$.conveyor</c>.</summary>
        private static ConveyorLibrary ParseConveyors(IReadOnlyDictionary<string, string> texts, List<string> problems)
        {
            var library = new ConveyorLibrary();
            foreach (var pair in texts)
            {
                string file = ConveyorJson.FileOf(pair.Key);
                var parsed = ConveyorJson.Parse(pair.Value);
                if (!parsed.Ok) { problems.Add($"{file}: " + string.Join("; ", parsed.Errors)); continue; }
                if (parsed.Conveyor.Id != pair.Key) { problems.Add($"{file}: file declares id '{parsed.Conveyor.Id}'"); continue; }
                library.Add(parsed.Conveyor);
            }
            return library;
        }
    }
}
