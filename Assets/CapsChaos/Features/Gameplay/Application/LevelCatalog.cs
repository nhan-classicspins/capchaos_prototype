using System;
using System.Collections.Generic;
using Game.Domain;

namespace Game.Application
{
    /// <summary>
    /// Port: raw level text by id, plus the play-order index. The adapter (Infrastructure) decides where
    /// the bytes live; everything after the bytes — parsing and validation — is engine-free Domain code.
    /// </summary>
    public interface ILevelSource
    {
        /// <summary>The text of <c>levels.index.json</c>.</summary>
        string ReadIndex();
        /// <summary>The text of <c>&lt;levelId&gt;.json</c>, or null when there is no such level.</summary>
        string ReadLevel(string levelId);
    }

    public sealed class LevelLoadException : Exception
    {
        public LevelLoadException(string message) : base(message) { }
    }

    /// <summary>
    /// The play order and the loaded levels (GDD §6). A level that fails V1–V5 is REFUSED at load with every
    /// problem named — the runtime never plays a malformed level. V6 (solvability) is CI's job
    /// (ContentLevelsTests / LevelTool validate), not a per-load cost.
    /// </summary>
    public sealed class LevelCatalog
    {
        private readonly ILevelSource _source;
        private IReadOnlyList<string> _order;

        public LevelCatalog(ILevelSource source) => _source = source ?? throw new ArgumentNullException(nameof(source));

        public IReadOnlyList<string> Order => _order ??= ReadOrder();

        public int Count => Order.Count;

        /// <summary>Index wraps past the end, so "next" after the last level replays from the first.</summary>
        public int Normalize(int index) => Count == 0 ? 0 : ((index % Count) + Count) % Count;

        public LevelDefinition Load(int index)
        {
            if (Count == 0) throw new LevelLoadException("levels.index.json lists no levels");
            string id = Order[Normalize(index)];
            string text = _source.ReadLevel(id) ?? throw new LevelLoadException($"{id}: listed in the index but not found");
            var parsed = LevelJson.Parse(text);
            if (!parsed.Ok) throw new LevelLoadException($"{id}: " + string.Join("; ", parsed.Errors));
            var problems = LevelValidator.Validate(parsed.Level);
            if (problems.Count > 0) throw new LevelLoadException($"{id}: " + string.Join("; ", problems));
            if (parsed.Level.Id != id) throw new LevelLoadException($"{id}: file declares id '{parsed.Level.Id}'");
            return parsed.Level;
        }

        private IReadOnlyList<string> ReadOrder()
        {
            var root = JsonReader.Parse(_source.ReadIndex() ?? throw new LevelLoadException("levels.index.json not found"));
            if (!root.TryGet("order", out var order) || order.Kind != JsonKind.Array)
                throw new LevelLoadException("levels.index.json: 'order' array missing");
            var ids = new List<string>();
            foreach (var item in order.Items)
            {
                if (item.Kind != JsonKind.String) throw new LevelLoadException("levels.index.json: 'order' entries must be strings");
                ids.Add(item.String);
            }
            return ids;
        }
    }
}
