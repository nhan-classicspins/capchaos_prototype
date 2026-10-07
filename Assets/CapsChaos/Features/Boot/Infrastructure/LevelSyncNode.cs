using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Application;
using Game.Presentation;

namespace Game.Infrastructure
{
    /// <summary>
    /// Resyncs the level config from the sheet every time the game starts (<see cref="ILevelConfigSync"/>), right
    /// after the bundled levels load, so the first screen already lists the synced levels. Emits
    /// <see cref="GameBootCaps.LevelsSynced"/>, which the first-scene node waits on.
    /// </summary>
    /// <remarks>
    /// <para><b>Three-tier law.</b> A boot node because it is awaited (network) and needs external ordering (after
    /// LevelsLoaded, before the first scene).</para>
    /// <para><b>Race rule.</b> <see cref="Requires"/> = { AssetReady, LevelsLoaded }: the settings asset loads through
    /// <c>IAssetService</c>, and the sync writes into the loaded <c>LevelCatalog</c>.</para>
    /// <para><b>Optional.</b> Offline, no sheet or a bad cell only means the bundled levels are played: it always
    /// emits the cap (Degraded when nothing was synced), and the hard timeout keeps a hung network from holding
    /// the first screen.</para>
    /// </remarks>
    public sealed class LevelSyncNode : IBootNode
    {
        private readonly IReadOnlyList<BootCap> _requires = new[] { BootCaps.AssetReady, GameBootCaps.LevelsLoaded };
        private readonly IReadOnlyList<BootCap> _provides = new[] { GameBootCaps.LevelsSynced };
        private readonly ILevelConfigSync _sync;

        public LevelSyncNode(ILevelConfigSync sync) => _sync = sync;

        public string Id => "LevelSync";
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _provides;
        public bool Required => false;

        public TimeSpan SoftTimeout => TimeSpan.Zero;
        public TimeSpan HardTimeout => TimeSpan.FromSeconds(12);

        public Func<BootContext, bool> Predicate => null;
        public string ExclusiveGroup => "";
        public float Weight => 0.5f;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            try
            {
                var report = await _sync.SyncAsync(ct);
                return report.Ran && report.Problems.Count == 0 ? BootNodeResult.Succeeded : BootNodeResult.Degraded;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning("[LevelSync] boot sync failed, the bundled levels are played: " + ex.Message);
                return BootNodeResult.Degraded;
            }
        }
    }
}
