using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// The session's one <see cref="UiPalette"/>: a Root singleton that loads the addressable palette once (at boot,
    /// by <c>UiPaletteNode</c>), keeps the single Addressables hold for the whole session and releases it when the
    /// Root scope is disposed. Widget controllers call <see cref="ApplyTo"/> on the views they create — a View never
    /// looks a service up, the palette is pushed in.
    /// </summary>
    public sealed class UiPaletteProvider : IDisposable
    {
        private static readonly AssetKey<UiPalette> Key = new(UiPalette.Address);

        private readonly IAssetService _assets;
        private UiPalette _palette;
        private bool _disposed;

        public UiPaletteProvider(IAssetService assets) => _assets = assets;

        /// <summary>The cached palette, or null (not loaded / missing) — UiTint then paints the DesignTokens defaults.</summary>
        public UiPalette Palette => _palette;

        /// <summary>Load once; later calls are no-ops. A missing palette is not an error (defaults apply).</summary>
        public async UniTask LoadAsync(CancellationToken ct)
        {
            if (_palette != null || _disposed) return;
            var palette = await _assets.TryLoadAsync(Key, ct);
            if (_disposed) { if (palette != null) _assets.Release(palette); return; }
            _palette = palette;
        }

        /// <summary>Hand the cached palette to every UiTint and TextTint under <paramref name="root"/>.</summary>
        public void ApplyTo(GameObject root)
        {
            UiTint.ApplyPalette(root, _palette);
            TextTint.ApplyPalette(root, _palette);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_palette != null) { _assets.Release(_palette); _palette = null; }
        }
    }
}
