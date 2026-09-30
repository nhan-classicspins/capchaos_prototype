using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Application;

namespace Game.Infrastructure
{
    /// <summary>
    /// Puts the Loading scene on screen at the front of the boot graph, then emits
    /// <see cref="GameBootCaps.LoadingSceneShown"/> so the rest of the SKU's boot work can be ordered
    /// behind it.
    /// </summary>
    /// <remarks>
    /// <para><b><see cref="Requires"/> = { AssetReady }</b> — the scene is an addressable
    /// (<c>Scenes/Loading</c>), so Addressables must be initialized first (<c>AssetInitNode</c>). That is
    /// a handful of milliseconds into boot, and that sliver is <b>uncovered</b> — the Loading scene is
    /// itself an Addressables load, so it cannot run any earlier, and the framework raises no boot cover
    /// of its own. In a player build Unity's own built-in splash screen fills the gap <i>while</i>
    /// <c>Show Splash Screen</c> is enabled in Player Settings — turn it off and those first frames are
    /// the <c>GamePlay</c> camera's clear colour, which is also what the editor shows.</para>
    /// <para><b>Optional, not Required.</b> A loading screen is cosmetic: if the scene is missing or the
    /// load fails, the node reports <see cref="BootNodeResult.Degraded"/>, still emits its cap (so the
    /// nodes behind it are never jailed) and boot continues without a visible Loading screen.</para>
    /// </remarks>
    public sealed class LoadingSceneNode : IBootNode
    {
        private readonly IReadOnlyList<BootCap> _requires = new[] { BootCaps.AssetReady };
        private readonly IReadOnlyList<BootCap> _provides = new[] { GameBootCaps.LoadingSceneShown };

        private readonly LoadingSceneHost _host;

        public LoadingSceneNode(LoadingSceneHost host) => _host = host;

        public string Id => "LoadingScene";
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _provides;
        public bool Required => false; // cosmetic — a missing loading screen must never abort boot

        // A local additive load: no soft "continue" (a half-shown loading screen is worthless), but a
        // hard cap so a hung Addressables load cannot hold the whole graph.
        public TimeSpan SoftTimeout => TimeSpan.Zero;
        public TimeSpan HardTimeout => TimeSpan.FromSeconds(10);

        public Func<BootContext, bool> Predicate => null;
        public string ExclusiveGroup => "";
        public float Weight => 1f;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            bool shown = await _host.ShowAsync(ct);
            return shown ? BootNodeResult.Succeeded : BootNodeResult.Degraded;
        }
    }
}
