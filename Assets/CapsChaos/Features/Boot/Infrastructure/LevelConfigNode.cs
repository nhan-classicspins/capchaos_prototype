using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;
using Game.Domain;

namespace Game.Infrastructure
{
    /// <summary>
    /// Loads EVERY level during the Loading stage: <c>levels.index.json</c> first, then each level it lists, then
    /// every shared conveyor file those levels name (<c>Conveyors/&lt;id&gt;.json</c>), all through Addressables, and
    /// hands the text to <see cref="LevelCatalog.Populate"/> (parse + V1–V9).
    /// Emits <see cref="GameBootCaps.LevelsLoaded"/>, which the first-scene node is gated on — so Gameplay
    /// never waits on, or fails at, a level load.
    /// </summary>
    /// <remarks>
    /// <para><b>Addressing.</b> <c>Assets/CapsChaos/Content/LevelConfig/</c> is ONE Addressables folder
    /// entry with the address <see cref="Folder"/>, so each file inside is addressable as
    /// <c>LevelConfig/&lt;file&gt;.json</c> (a conveyor <c>LevelConfig/Conveyors/&lt;id&gt;.json</c> — the folder
    /// entry takes its subfolders too) — a level the LevelTool writes tomorrow is covered without touching the group. The level ids come from data (the index), so the keys are built here rather than
    /// generated.</para>
    /// <para><b>Three-tier law.</b> A boot node because it is awaited (I/O), can fail meaningfully (a
    /// broken level), and needs external ordering (before the first scene). App-wide, not a feature scope:
    /// the catalog is a Root singleton every screen reads.</para>
    /// <para><b>Race rule.</b> <see cref="Requires"/> = { AssetReady } asserts <see cref="IAssetService"/>;
    /// <see cref="LevelCatalog"/> is a plain tier-1 object with no readiness of its own. No <c>ILog</c>:
    /// nothing here asserts LoggingReady, so it reports through <c>Debug</c> like the framework's
    /// <c>AssetInitNode</c>.</para>
    /// <para><b>Required.</b> Without levels there is no game, so a failure aborts boot with every broken
    /// level named in the log.</para>
    /// </remarks>
    public sealed class LevelConfigNode : IBootNode
    {
        /// <summary>The Addressables address of the LevelConfig folder entry.</summary>
        public const string Folder = "LevelConfig";

        private readonly IReadOnlyList<BootCap> _requires = new[] { BootCaps.AssetReady };
        private readonly IReadOnlyList<BootCap> _provides = new[] { GameBootCaps.LevelsLoaded };

        private readonly IAssetService _assets;
        private readonly LevelCatalog _catalog;

        public LevelConfigNode(IAssetService assets, LevelCatalog catalog)
        {
            _assets = assets;
            _catalog = catalog;
        }

        public string Id => "LevelConfig";
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _provides;
        public bool Required => true;

        // Local Addressables I/O, same budget as the framework's AssetInit.
        public TimeSpan SoftTimeout => TimeSpan.FromSeconds(5);
        public TimeSpan HardTimeout => TimeSpan.FromSeconds(30);

        public Func<BootContext, bool> Predicate => null;
        public string ExclusiveGroup => "";
        public float Weight => 1f;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            try
            {
                string index = await ReadAsync(LevelCatalog.IndexFile, ct);
                if (index == null)
                    throw new LevelLoadException($"{Folder}/{LevelCatalog.IndexFile} has no Addressables location — is Content/LevelConfig an addressable folder with address '{Folder}'?");
                var order = LevelCatalog.ParseOrder(index);

                var reads = new UniTask<string>[order.Count];
                for (int i = 0; i < order.Count; i++) reads[i] = ReadAsync(order[i] + ".json", ct);
                var texts = await UniTask.WhenAll(reads);

                var byId = new Dictionary<string, string>(order.Count, StringComparer.Ordinal);
                for (int i = 0; i < order.Count; i++) if (texts[i] != null) byId[order[i]] = texts[i];

                // only the conveyors some level names — a layout nobody plays is never loaded
                var conveyorIds = new List<string>();
                foreach (var text in byId.Values)
                {
                    string id = LevelJson.ConveyorIdOf(text);
                    if (id != null && ConveyorDefinition.IsConveyorId(id) && !conveyorIds.Contains(id)) conveyorIds.Add(id);
                }
                var conveyorReads = new UniTask<string>[conveyorIds.Count];
                for (int i = 0; i < conveyorIds.Count; i++) conveyorReads[i] = ReadAsync(ConveyorJson.FileOf(conveyorIds[i]), ct);
                var conveyorTexts = await UniTask.WhenAll(conveyorReads);
                var conveyors = new Dictionary<string, string>(conveyorIds.Count, StringComparer.Ordinal);
                for (int i = 0; i < conveyorIds.Count; i++) if (conveyorTexts[i] != null) conveyors[conveyorIds[i]] = conveyorTexts[i];

                _catalog.Populate(order, byId, conveyors);

                Debug.Log($"[LevelConfig] {_catalog.Count} levels on {conveyors.Count} conveyors loaded from {Folder}/.");
                return BootNodeResult.Succeeded;
            }
            catch (OperationCanceledException)
            {
                throw;   // hard timeout — the scheduler classifies it
            }
            catch (Exception ex)
            {
                Debug.LogError("[LevelConfig] level load failed: " + ex.Message);
                return BootNodeResult.Failed;
            }
        }

        /// <summary>The text of <c>LevelConfig/&lt;file&gt;</c>, or null when no such address exists. The
        /// TextAsset is released straight away — the catalog keeps the parsed levels, not the asset.</summary>
        private async UniTask<string> ReadAsync(string file, CancellationToken ct)
        {
            var asset = await _assets.TryLoadAsync(new AssetKey<TextAsset>(Folder + "/" + file), ct);
            if (asset == null) return null;
            string text = asset.text;
            _assets.Release(asset);
            return text;
        }
    }
}
