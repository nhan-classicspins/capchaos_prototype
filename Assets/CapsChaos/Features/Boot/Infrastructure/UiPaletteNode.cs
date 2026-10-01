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
    /// Loads the UI palette into the Root <see cref="UiPaletteProvider"/> during the Loading stage, so the first
    /// screen already paints with it. Emits <see cref="GameBootCaps.UiPaletteReady"/>, which the first-scene node is
    /// gated on.
    /// </summary>
    /// <remarks>
    /// <para><b>Three-tier law.</b> Awaited (an Addressables load) and externally ordered (before the first screen).</para>
    /// <para><b>Race rule.</b> <see cref="Requires"/> = { AssetReady } asserts the IAssetService the provider loads
    /// through; the provider itself is a plain tier-1 object.</para>
    /// <para><b>Optional.</b> A missing or broken palette only costs the custom colours — the UI falls back to the
    /// DesignTokens defaults — so a failure logs a warning and reports Degraded (which still emits the cap, so the
    /// first screen is never stranded).</para>
    /// </remarks>
    public sealed class UiPaletteNode : IBootNode
    {
        private readonly IReadOnlyList<BootCap> _requires = new[] { BootCaps.AssetReady };
        private readonly IReadOnlyList<BootCap> _provides = new[] { GameBootCaps.UiPaletteReady };
        private readonly UiPaletteProvider _provider;

        public UiPaletteNode(UiPaletteProvider provider) => _provider = provider;

        public string Id => "UiPalette";
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _provides;
        public bool Required => false;

        public TimeSpan SoftTimeout => TimeSpan.Zero;
        public TimeSpan HardTimeout => TimeSpan.FromSeconds(10);

        public Func<BootContext, bool> Predicate => null;
        public string ExclusiveGroup => "";
        public float Weight => 0.5f;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            try
            {
                await _provider.LoadAsync(ct);
                if (_provider.Palette != null) return BootNodeResult.Succeeded;
                Debug.LogWarning("[UiPalette] no addressable 'UiPalette' — the UI uses the DesignTokens defaults.");
                return BootNodeResult.Degraded;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Debug.LogWarning("[UiPalette] load failed, the UI uses the DesignTokens defaults: " + ex.Message);
                return BootNodeResult.Degraded;
            }
        }
    }
}
