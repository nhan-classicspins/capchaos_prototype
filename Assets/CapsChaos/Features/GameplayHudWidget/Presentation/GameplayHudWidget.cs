using System;
using UnityEngine;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// The Gameplay HUD controller: relays Restart and Home to the screen as <see cref="RetryRequested"/> /
    /// <see cref="HomeRequested"/>. A plain IDisposable, screen-owned (<c>Lifetime.Scoped</c>).
    /// </summary>
    /// <remarks>
    /// Authored in Gameplay.unity like the Main level list: a GameplayHudWidget prefab instance under an edit-time
    /// preview canvas (world-space, camera-less <c>UI</c> layer). <see cref="Attach"/> moves it under the <c>Ui</c>
    /// render-layer host (rule #16), <c>Stamp</c>s it and deletes the preview canvas; from then on it belongs to
    /// Master, so <see cref="Dispose"/> destroys it. A sibling of other screens' views under the host, so the screen
    /// plumbs <see cref="SetVisible"/>.
    /// </remarks>
    public sealed class GameplayHudWidget : IDisposable
    {
        private readonly IRenderLayerRegistry _layers;
        private readonly UiPaletteProvider _palette;
        private GameplayHudView _view;
        private GameObject _viewGo;
        private bool _attached, _disposed;

        public event Action RetryRequested;
        public event Action HomeRequested;

        public GameplayHudWidget(GameplayHudView sceneView, IRenderLayerRegistry layers, UiPaletteProvider palette)
        {
            _palette = palette;
            _view = sceneView ?? throw new ArgumentNullException(nameof(sceneView));
            _viewGo = sceneView.gameObject;
            _layers = layers;
        }

        public void Attach()
        {
            if (_disposed || _attached) return;
            _attached = true;
            var preview = _viewGo.transform.parent;
            _viewGo.transform.SetParent(_layers.GetHost(RenderLayers.Ui), false);
            _layers.Stamp(_viewGo, RenderLayers.Ui, sortingOrder: 20);          // above the board's own Ui furniture
            _palette.ApplyTo(_viewGo);
            if (preview != null && preview.childCount == 0 && preview.GetComponent<Canvas>() != null)
                UnityEngine.Object.Destroy(preview.gameObject);
            _view.RetryClicked += OnRetry;
            _view.HomeClicked += OnHome;
            _view.SetVisible(false);                                            // the screen decides the reveal
        }

        public void SetVisible(bool visible) => _view?.SetVisible(visible);

        /// <summary>Paused (app lost focus, or covered): stay on screen, just stop taking taps.</summary>
        public void SetInteractable(bool interactable) => _view?.SetInteractable(interactable);

        private void OnRetry() => RetryRequested?.Invoke();
        private void OnHome() => HomeRequested?.Invoke();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RetryRequested = null;
            HomeRequested = null;
            if (_view != null)
            {
                _view.RetryClicked -= OnRetry;
                _view.HomeClicked -= OnHome;
            }
            if (_viewGo != null) UnityEngine.Object.Destroy(_viewGo);
            _viewGo = null;
            _view = null;
        }
    }
}
