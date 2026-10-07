using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;
using Game.Gen;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// The level-select panel on Main: one tile per level in the <see cref="LevelCatalog"/> (already loaded at
    /// boot), in play order. Tapping a tile raises <see cref="LevelChosen"/> with that level's index; the
    /// screen decides what that means. A plain IDisposable, screen-owned (<c>Lifetime.Scoped</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Authored in Main.unity, hosted at runtime.</b> The panel is a LevelSelectWidget prefab instance
    /// placed in the Main scene under an edit-time preview canvas (world-space, on the camera-less <c>UI</c>
    /// layer — visible in the Scene view, never drawn in play). <see cref="CreateAsync"/> moves it under the
    /// <c>Ui</c> render-layer host (rule #16: uGUI renders under a host canvas, never a screen-scene canvas),
    /// <c>Stamp</c>s it and deletes the preview canvas. Once moved it belongs to Master, so the Main scene
    /// unload no longer takes it: <see cref="Dispose"/> destroys it. It is a sibling of other screens' views
    /// under the host, so the screen plumbs <see cref="SetVisible"/>.</para>
    /// </remarks>
    /// <remarks>
    /// The tiles are N interchangeable copies whose count comes from data — one prefab, N instances of
    /// <see cref="LevelButtonView"/>, no per-tile controller (add-widget §5). They are NOT pooled yet: the
    /// framework's <c>PoolInstaller</c> registers <c>PoolContext</c> without an <c>IAssetService</c>-backed
    /// prefab loader, so <c>ForPrefabAsync</c> throws at runtime. Until that is fixed in the framework, the
    /// widget holds the tile prefab itself (one LoadAsync, one Release) and the tiles live and die with the
    /// panel — they are built once per visit, so a pool would buy nothing here anyway.
    /// </remarks>
    public sealed class LevelSelectWidget : IDisposable
    {
        private readonly IAssetService _assets;
        private readonly IRenderLayerRegistry _layers;
        private readonly ILocalizationService _loc;
        private readonly LevelCatalog _catalog;
        private readonly UiPaletteProvider _palette;
        private readonly ILog _log;

        private readonly List<(LevelButtonView View, Action Handler)> _tiles = new List<(LevelButtonView, Action)>();
        private GameObject _tilePrefab;
        private GameObject _viewGo;
        private LevelSelectView _view;
        private bool _disposed;

        /// <summary>A tile was tapped: the index of its level in the catalog's play order.</summary>
        public event Action<int> LevelChosen;

        /// <summary>The Sync Config pill was tapped; the screen runs the sync.</summary>
        public event Action SyncRequested;

        public LevelSelectWidget(LevelSelectView sceneView, IAssetService assets, IRenderLayerRegistry layers,
            ILocalizationService loc, LevelCatalog catalog, UiPaletteProvider palette, ILog log = null)
        {
            _palette = palette;
            _view = sceneView ?? throw new ArgumentNullException(nameof(sceneView));
            _viewGo = sceneView.gameObject;
            _assets = assets;
            _layers = layers;
            _loc = loc;
            _catalog = catalog;
            _log = log ?? new NullLog();
        }

        public async UniTask CreateAsync(CancellationToken ct)
        {
            if (_disposed) return;
            try
            {
                MoveUnderHost();
                _view.SetVisible(false);                                   // the screen decides the reveal
                _view.SetTitle(_loc.Get(LocKeys.LevelSelectTitle));

                _tilePrefab = await _assets.LoadAsync(AssetKeys.LevelButton, ct);
                if (_disposed) { ReleasePrefab(); return; }                // Dispose already ran — give the hold back

                for (int i = 0; i < _catalog.Count; i++) AddTile(i);
                _view.SyncClicked += OnSyncClicked;
                SetSyncStatus(null, busy: false);
                _view.ScrollToTop();
                _log.Info($"[LevelSelectWidget] {_catalog.Count} levels listed.");
            }
            catch { Dispose(); throw; }
        }

        public void SetVisible(bool visible) => _view?.SetVisible(visible);

        /// <summary>The Sync Config pill: <paramref name="status"/> under its title (null = the idle "Config"); busy =
        /// a sync is running, so it takes no taps.</summary>
        public void SetSyncStatus(string status, bool busy)
        {
            if (_view == null) return;
            _view.SetSync(_loc.Get(LocKeys.LevelSelectSync), status ?? _loc.Get(LocKeys.LevelSelectSyncIdle), !busy);
        }

        /// <summary>Re-read every tile's labels from the catalog (a sync may have changed a level's difficulty).</summary>
        public void Refresh()
        {
            if (_view == null || _tilePrefab == null) return;
            for (int i = 0; i < _tiles.Count && i < _catalog.Count; i++)
                _tiles[i].View.SetLabels(_loc.Get(LocKeys.LevelSelectNumber, i + 1), _loc.Get(DifficultyKey(_catalog.Get(i).Difficulty)));
        }

        private void OnSyncClicked() => SyncRequested?.Invoke();

        /// <summary>Paused (app lost focus, or covered): stay on screen, just stop taking taps.</summary>
        public void SetInteractable(bool interactable) => _view?.SetInteractable(interactable);

        /// <summary>Scene-authored panel → the Ui host; the now-empty edit-time preview canvas goes away.</summary>
        private void MoveUnderHost()
        {
            var preview = _viewGo.transform.parent;
            _viewGo.transform.SetParent(_layers.GetHost(RenderLayers.Ui), false);
            _layers.Stamp(_viewGo, RenderLayers.Ui, sortingOrder: 10);
            _palette.ApplyTo(_viewGo);
            if (preview != null && preview.childCount == 0 && preview.GetComponent<Canvas>() != null)
                UnityEngine.Object.Destroy(preview.gameObject);
        }

        private void AddTile(int index)
        {
            var level = _catalog.Get(index);
            var tile = UnityEngine.Object.Instantiate(_tilePrefab, _view.Grid, false).GetComponent<LevelButtonView>()
                       ?? throw new InvalidOperationException("[LevelSelectWidget] LevelButton prefab carries no LevelButtonView.");
            _layers.Stamp(tile.gameObject, RenderLayers.Ui, sortingOrder: 11);   // instantiated after the panel's Stamp
            _palette.ApplyTo(tile.gameObject);                                  // …and after the panel got its palette
            tile.SetLabels(_loc.Get(LocKeys.LevelSelectNumber, index + 1), _loc.Get(DifficultyKey(level.Difficulty)));
            Action handler = () => LevelChosen?.Invoke(index);
            tile.Clicked += handler;
            _tiles.Add((tile, handler));
        }

        /// <summary>Which label a level's authored difficulty reads as — the controller's call, never the View's.</summary>
        private static LocKey DifficultyKey(string difficulty) => difficulty switch
        {
            "tutorial" => LocKeys.LevelSelectDifficultyTutorial,
            "easy"     => LocKeys.LevelSelectDifficultyEasy,
            "medium"   => LocKeys.LevelSelectDifficultyMedium,
            "hard"     => LocKeys.LevelSelectDifficultyHard,
            "breather" => LocKeys.LevelSelectDifficultyBreather,
            _          => LocKeys.LevelSelectDifficultyUnknown,
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            LevelChosen = null;
            SyncRequested = null;
            if (_view != null) _view.SyncClicked -= OnSyncClicked;
            foreach (var (tile, handler) in _tiles)
                if (tile != null) tile.Clicked -= handler;
            _tiles.Clear();
            if (_viewGo != null) UnityEngine.Object.Destroy(_viewGo);      // the tiles are children: they go with it
            _viewGo = null;
            _view = null;
            ReleasePrefab();
        }

        private void ReleasePrefab()
        {
            // ref-count: one Release per LoadAsync, on every path
            if (_tilePrefab != null) { _assets.Release(_tilePrefab); _tilePrefab = null; }
        }
    }
}
