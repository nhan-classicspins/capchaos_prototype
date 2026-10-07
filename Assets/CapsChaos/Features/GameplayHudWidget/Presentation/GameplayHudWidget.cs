using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
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
        private GameFeel _feel;
        // per booster button: the instances of its prompt / banner prefabs (BoosterCatalog), under the HUD's overlay node
        private readonly Dictionary<int, BoosterPromptView> _prompts = new Dictionary<int, BoosterPromptView>();
        private readonly Dictionary<int, BoosterBannerView> _banners = new Dictionary<int, BoosterBannerView>();

        public event Action RetryRequested;
        public event Action HomeRequested;
        /// <summary>Booster button <c>index</c> (left to right) was tapped.</summary>
        public event Action<int> BoosterRequested;

        /// <summary>How many booster buttons the HUD has.</summary>
        public int BoosterSlots => _view != null ? _view.BoosterSlots : 0;

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
            _view.BoosterClicked += OnBooster;
            _view.SetVisible(false);                                            // the screen decides the reveal
        }

        public void SetVisible(bool visible) => _view?.SetVisible(visible);

        /// <summary>The level title reads <paramref name="text"/> (localized by the caller).</summary>
        public void SetLevel(string text) => _view?.SetLevel(text);

        /// <summary>The coin pill shows <paramref name="text"/> (the balance, localized by the caller).</summary>
        public void SetCoins(string text) => _view?.SetCoins(text);

        /// <summary>
        /// Booster button <paramref name="index"/> shows <paramref name="icon"/> and, at its corner, the red badge reading
        /// <paramref name="count"/> — or, when <paramref name="count"/> is null (the player owns none), the green "+".
        /// <paramref name="present"/> false hides the button (no booster for that slot).
        /// </summary>
        public void SetBooster(int index, bool present, Sprite icon, string count)
        {
            var button = _view != null ? _view.Booster(index) : null;
            if (button == null) return;
            button.SetVisible(present);
            if (!present) return;
            button.SetIcon(icon);
            if (count != null) button.ShowCount(count);
            else button.ShowAddMore();
        }

        /// <summary>The session's feel: how the booster prompt, banner and toast move.</summary>
        public void UseFeel(GameFeel feel)
        {
            _feel = feel;
            _view?.UseFeel(feel);
            foreach (var p in _prompts.Values) if (p != null) p.UseFeel(feel);
            foreach (var b in _banners.Values) if (b != null) b.UseFeel(feel);
        }

        /// <summary>
        /// Booster button <paramref name="index"/> plays with these prefabs (null = none): one instance of each goes under
        /// the HUD's overlay node, stamped onto the Ui layer and painted with the palette, hidden until it is asked for.
        /// Replaces what the button had before.
        /// </summary>
        public void SetBoosterOverlays(int index, BoosterPromptView prompt, BoosterBannerView banner)
        {
            if (_view == null) return;
            Replace(_prompts, index, prompt);
            Replace(_banners, index, banner);
        }

        private void Replace<T>(Dictionary<int, T> map, int index, T prefab) where T : Component
        {
            if (map.TryGetValue(index, out var old) && old != null) UnityEngine.Object.Destroy(old.gameObject);
            map.Remove(index);
            if (prefab == null) return;
            var instance = UnityEngine.Object.Instantiate(prefab, _view.BoosterOverlays, false);
            instance.gameObject.SetActive(false);
            _layers.Stamp(instance.gameObject, RenderLayers.Ui, sortingOrder: 20);   // instantiated after Attach's Stamp
            _palette.ApplyTo(instance.gameObject);
            switch (instance)
            {
                case BoosterPromptView p: p.UseFeel(_feel); break;
                case BoosterBannerView b: b.UseFeel(_feel); break;
            }
            map[index] = instance;
        }

        /// <summary>Booster button <paramref name="index"/> takes taps (true) or reads as off (false).</summary>
        public void SetBoosterInteractable(int index, bool interactable)
        {
            var button = _view != null ? _view.Booster(index) : null;
            if (button != null) button.SetInteractable(interactable);
        }

        /// <summary>Booster <paramref name="index"/> waits for its target: its prompt with <paramref name="icon"/>,
        /// <paramref name="title"/> and <paramref name="hint"/> (localized by the caller). Nothing when it has no prompt.</summary>
        public void ShowBoosterPrompt(int index, Sprite icon, string title, string hint)
        {
            if (_prompts.TryGetValue(index, out var p) && p != null) p.Show(icon, title, hint);
        }

        /// <summary>Every booster prompt goes.</summary>
        public void HideBoosterPrompts()
        {
            foreach (var p in _prompts.Values) if (p != null) p.Hide();
        }

        /// <summary>Booster <paramref name="index"/> acts: its banner's pass with <paramref name="icon"/>; done once it has
        /// gone — at once when it has no banner.</summary>
        public UniTask PlayBoosterBannerAsync(int index, Sprite icon, CancellationToken ct) =>
            _banners.TryGetValue(index, out var b) && b != null ? b.PlayAsync(icon, ct) : UniTask.CompletedTask;

        /// <summary>Every booster banner goes at once.</summary>
        public void StopBoosterBanners()
        {
            foreach (var b in _banners.Values) if (b != null) b.Stop();
        }

        /// <summary>A notice in the lower third (localized by the caller); it replaces the one on screen.</summary>
        public void ShowToast(string text)
        {
            if (_view != null && _view.Toast != null) _view.Toast.Show(text);
        }

        /// <summary>Paused (app lost focus, or covered): stay on screen, just stop taking taps.</summary>
        public void SetInteractable(bool interactable) => _view?.SetInteractable(interactable);

        private void OnRetry() => RetryRequested?.Invoke();
        private void OnHome() => HomeRequested?.Invoke();
        private void OnBooster(int index) => BoosterRequested?.Invoke(index);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RetryRequested = null;
            HomeRequested = null;
            BoosterRequested = null;
            if (_view != null)
            {
                _view.RetryClicked -= OnRetry;
                _view.HomeClicked -= OnHome;
                _view.BoosterClicked -= OnBooster;
            }
            _prompts.Clear();                                                   // children of the view: destroyed with it
            _banners.Clear();
            if (_viewGo != null) UnityEngine.Object.Destroy(_viewGo);
            _viewGo = null;
            _view = null;
        }
    }
}
