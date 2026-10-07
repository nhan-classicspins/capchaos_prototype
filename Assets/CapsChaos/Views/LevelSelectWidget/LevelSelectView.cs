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
        [Tooltip("The level list (title, Sync, grid, bottom bar) — hidden on the Home screen, shown by the debug button.")]
        [SerializeField] private GameObject _levelList;
        [Tooltip("The Home screen's Start button (the current level).")]
        [SerializeField] private LevelButtonView _start;
        [Tooltip("The debug button at the bottom-left that shows / hides the level list.")]
        [SerializeField] private LevelButtonView _debugToggle;
        [Tooltip("Debug: put the player back on level 1.")]
        [SerializeField] private LevelButtonView _reset;
        [Tooltip("Debug: put the player on the last level (every level unlocked).")]
        [SerializeField] private LevelButtonView _unlockAll;
        [Tooltip("The bottom bar's booster buttons (BottomBtns), left to right — one per booster of the catalog.")]
        [SerializeField] private LevelButtonView[] _boosterGrants = Array.Empty<LevelButtonView>();
        [Tooltip("The bottom bar's coin button (debug: + coins).")]
        [SerializeField] private LevelButtonView _coinGrant;

        /// <summary>The Sync Config pill was tapped.</summary>
        public event Action SyncClicked;
        /// <summary>The Home screen's Start button was tapped.</summary>
        public event Action StartClicked;
        /// <summary>The debug button (show / hide the level list) was tapped.</summary>
        public event Action DebugToggleClicked;
        /// <summary>The debug Reset button was tapped.</summary>
        public event Action ResetClicked;
        /// <summary>The debug Unlock All button was tapped.</summary>
        public event Action UnlockAllClicked;

        /// <summary>The Reset and Unlock All buttons read these (all localized).</summary>
        public void SetProgressButtons(string resetTitle, string resetLabel, string unlockTitle, string unlockLabel)
        {
            if (_reset != null) _reset.SetLabels(resetTitle, resetLabel);
            if (_unlockAll != null) _unlockAll.SetLabels(unlockTitle, unlockLabel);
        }

        /// <summary>The level list is shown (over the Home screen) or hidden.</summary>
        public void ShowLevelList(bool shown)
        {
            if (_levelList != null) _levelList.SetActive(shown);
            if (_start != null) _start.gameObject.SetActive(!shown);
        }

        /// <summary>The Start button reads <paramref name="title"/> over <paramref name="label"/> (both localized).</summary>
        public void SetStart(string title, string label)
        {
            if (_start != null) _start.SetLabels(title, label);
        }

        /// <summary>The debug button reads <paramref name="title"/> over <paramref name="label"/> (both localized).</summary>
        public void SetDebugToggle(string title, string label)
        {
            if (_debugToggle != null) _debugToggle.SetLabels(title, label);
        }
        /// <summary>Bottom-bar booster button <c>index</c> (left to right) was tapped.</summary>
        public event Action<int> BoosterGrantClicked;
        /// <summary>The bottom bar's coin button was tapped.</summary>
        public event Action CoinGrantClicked;

        /// <summary>The coin button reads <paramref name="title"/> over <paramref name="label"/> (both localized).</summary>
        public void SetCoinGrant(string title, string label)
        {
            if (_coinGrant != null) _coinGrant.SetLabels(title, label);
        }

        /// <summary>How many booster buttons the bottom bar has.</summary>
        public int BoosterGrantSlots => _boosterGrants.Length;

        private Action[] _grantHandlers = Array.Empty<Action>();

        /// <summary>Bottom-bar booster button <paramref name="index"/> reads <paramref name="title"/> over
        /// <paramref name="label"/> (both localized); <paramref name="present"/> false hides it.</summary>
        public void SetBoosterGrant(int index, bool present, string title, string label)
        {
            if (index < 0 || index >= _boosterGrants.Length || _boosterGrants[index] == null) return;
            _boosterGrants[index].gameObject.SetActive(present);
            if (present) _boosterGrants[index].SetLabels(title, label);
        }

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
            if (_start != null) _start.Clicked += OnStartClicked;
            if (_debugToggle != null) _debugToggle.Clicked += OnDebugToggleClicked;
            if (_reset != null) _reset.Clicked += OnResetClicked;
            if (_coinGrant != null) _coinGrant.Clicked += OnCoinGrantClicked;
            if (_unlockAll != null) _unlockAll.Clicked += OnUnlockAllClicked;
            _grantHandlers = new Action[_boosterGrants.Length];
            for (int i = 0; i < _boosterGrants.Length; i++)
            {
                int index = i;
                _grantHandlers[i] = () => BoosterGrantClicked?.Invoke(index);
                if (_boosterGrants[i] != null) _boosterGrants[i].Clicked += _grantHandlers[i];
            }
        }

        private void OnDestroy()
        {
            if (_sync != null) _sync.Clicked -= OnSyncClicked;
            if (_start != null) _start.Clicked -= OnStartClicked;
            if (_debugToggle != null) _debugToggle.Clicked -= OnDebugToggleClicked;
            if (_reset != null) _reset.Clicked -= OnResetClicked;
            if (_coinGrant != null) _coinGrant.Clicked -= OnCoinGrantClicked;
            if (_unlockAll != null) _unlockAll.Clicked -= OnUnlockAllClicked;
            for (int i = 0; i < _boosterGrants.Length && i < _grantHandlers.Length; i++)
                if (_boosterGrants[i] != null) _boosterGrants[i].Clicked -= _grantHandlers[i];
        }

        private void OnSyncClicked() => SyncClicked?.Invoke();
        private void OnStartClicked() => StartClicked?.Invoke();
        private void OnDebugToggleClicked() => DebugToggleClicked?.Invoke();
        private void OnResetClicked() => ResetClicked?.Invoke();
        private void OnCoinGrantClicked() => CoinGrantClicked?.Invoke();
        private void OnUnlockAllClicked() => UnlockAllClicked?.Invoke();
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
