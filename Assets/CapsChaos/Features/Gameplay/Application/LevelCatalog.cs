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
    /// (<c>LevelConfigNode</c>) fetches the raw text from <c>Content/LevelConfig/</c> and hands it to
    /// <see cref="Populate"/>; after that <see cref="Get"/> is a plain lookup — no I/O, no parsing mid-game.
    /// A level that fails V1–V5 is REFUSED with every problem named, and a refused level fails the whole
    /// load: the runtime never plays a malformed level. V6 (solvability) is CI's job
    /// (ContentLevelsTests / LevelTool validate), not a boot cost.
    /// </summary>
    public sealed class LevelCatalog
    {
        /// <summary>The play-order file next to the levels.</summary>
        public const string IndexFile = "levels.index.json";

        private IReadOnlyList<string> _order = Array.Empty<string>();
        private IReadOnlyList<LevelDefinition> _levels = Array.Empty<LevelDefinition>();

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
        public void Populate(IReadOnlyList<string> order, IReadOnlyDictionary<string, string> levelTexts)
        {
            if (order == null) throw new ArgumentNullException(nameof(order));
            if (levelTexts == null) throw new ArgumentNullException(nameof(levelTexts));
            if (order.Count == 0) throw new LevelLoadException(IndexFile + " lists no levels");

            var problems = new List<string>();
            var levels = new List<LevelDefinition>(order.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in order)
            {
                if (!seen.Add(id)) { problems.Add($"{id}: listed twice in {IndexFile}"); continue; }
                if (!levelTexts.TryGetValue(id, out var text) || text == null) { problems.Add($"{id}: listed in the index but not found"); continue; }
                var parsed = LevelJson.Parse(text);
                if (!parsed.Ok) { problems.Add($"{id}: " + string.Join("; ", parsed.Errors)); continue; }
                var semantic = LevelValidator.Validate(parsed.Level);
                if (semantic.Count > 0) { problems.Add($"{id}: " + string.Join("; ", semantic)); continue; }
                if (parsed.Level.Id != id) { problems.Add($"{id}: file declares id '{parsed.Level.Id}'"); continue; }
                levels.Add(parsed.Level);
            }
            if (problems.Count > 0) throw new LevelLoadException(string.Join(" | ", problems));

            _order = new List<string>(order);
            _levels = levels;
            IsLoaded = true;
        }
    }
}
