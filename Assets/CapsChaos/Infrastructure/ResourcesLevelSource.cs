using Game.Application;
using UnityEngine;

namespace Game.Infrastructure
{
    /// <summary>
    /// <see cref="ILevelSource"/> over the bundled level JSON in
    /// <c>Assets/CapsChaos/Content/Resources/Levels/</c> (the framework's own bundled-JSON precedent:
    /// Resources/Localization, Resources/Config). Addressables would need the Addressables editor API in
    /// Game.Editor, whose reference list is pinned empty. Bytes only — parsing is Domain.
    /// </summary>
    public sealed class ResourcesLevelSource : ILevelSource
    {
        private const string Folder = "Levels/";

        public string ReadIndex() => Read("levels.index");

        public string ReadLevel(string levelId) => Read(levelId);

        private static string Read(string name)
        {
            var asset = Resources.Load<TextAsset>(Folder + name);
            if (asset == null) return null;
            string text = asset.text;
            Resources.UnloadAsset(asset);
            return text;
        }
    }
}
