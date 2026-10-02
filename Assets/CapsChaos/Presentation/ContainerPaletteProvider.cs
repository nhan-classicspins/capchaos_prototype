using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// The session's one <see cref="ContainerPalette"/> (the containers' materials): a Root singleton that loads the
    /// addressable palette the first time a round needs it, keeps the single Addressables hold for the whole session and
    /// releases it when the Root scope is disposed. Every container on every board shares it — none keeps its own list.
    /// </summary>
    public sealed class ContainerPaletteProvider : IDisposable
    {
        private static readonly AssetKey<ContainerPalette> Key = new(ContainerPalette.Address);
        private readonly IAssetService _assets;
        private ContainerPalette _palette;
        private bool _disposed;

        public ContainerPaletteProvider(IAssetService assets) => _assets = assets;

        /// <summary>The cached palette, or null (not loaded / missing) — containers then keep their authored material.</summary>
        public ContainerPalette Palette => _palette;

        /// <summary>Load once; later calls are no-ops. A missing palette is not an error (the containers stay as authored).</summary>
        public async UniTask<ContainerPalette> LoadAsync(CancellationToken ct)
        {
            if (_palette != null || _disposed) return _palette;
            var palette = await _assets.TryLoadAsync(Key, ct);
            if (_disposed) { if (palette != null) _assets.Release(palette); return null; }
            if (_palette != null) { if (palette != null) _assets.Release(palette); return _palette; }   // a racing load won
            _palette = palette;
            return _palette;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_palette != null) { _assets.Release(_palette); _palette = null; }
        }
    }
}
