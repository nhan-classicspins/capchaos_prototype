using System;
using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// The level-select panel: a titled, vertically scrolling grid of level tiles over the ground colour, and the Sync
    /// Config pill at the right of the title bar (a <see cref="LevelButtonView"/>: same look as the tiles).
    /// Dumb by design — the controller hands it the title text and parents the pooled tiles under
    /// <see cref="Grid"/>; the grid's cell size, gap and padding come from <see cref="DesignTokens.Ui"/>.
    /// Gallery-safe: every member works in any order without the game running.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelSelectView : WidgetViewBase
    {
        [SerializeField] private TextProxy _title;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private GridLayoutGroup _grid;
        [SerializeField] private LevelButtonView _sync;

        /// <summary>The Sync Config pill was tapped.</summary>
        public event Action SyncClicked;

        /// <summary>Where the controller parents the tiles.</summary>
        public RectTransform Grid => (RectTransform)_grid.transform;

        public void SetTitle(string text)
        {
            if (_title != null) _title.SetText(text);
        }

        /// <summary>The Sync Config pill reads <paramref name="title"/> over <paramref name="status"/> (both localized)
        /// and takes taps or not.</summary>
        public void SetSync(string title, string status, bool interactable)
        {
            if (_sync == null) return;
            _sync.SetLabels(title, status);
            _sync.SetInteractable(interactable);
        }

        public void ScrollToTop()
        {
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private void Awake()
        {
            ApplyTokens();
            if (_sync != null) _sync.Clicked += OnSyncClicked;
        }

        private void OnDestroy()
        {
            if (_sync != null) _sync.Clicked -= OnSyncClicked;
        }

        private void OnSyncClicked() => SyncClicked?.Invoke();
        private void OnValidate() => ApplyTokens();

        private void ApplyTokens()
        {
            if (_grid == null) return;
            var u = DesignTokens.Ui.Unit;
            _grid.cellSize = new Vector2(DesignTokens.Ui.TileSize, DesignTokens.Ui.TileSize);
            _grid.spacing = new Vector2(DesignTokens.Ui.Gap, DesignTokens.Ui.Gap);
            int pad = Mathf.RoundToInt(DesignTokens.Ui.EdgePad);
            _grid.padding = new RectOffset(pad, pad, Mathf.RoundToInt(2 * u), pad);
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = DesignTokens.Ui.TileColumns;
            _grid.childAlignment = TextAnchor.UpperCenter;
        }
    }
}
