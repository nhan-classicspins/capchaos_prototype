using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;
using Game.Gen;

namespace Game.Presentation
{
    /// <summary>
    /// The Main screen (Home): one Start button reading the player's current level — their saved progress
    /// (<see cref="PlayerProfile.CurrentLevel"/>) — that plays it. A debug button at the bottom-left shows / hides the level
    /// list over it: every level in play order (one tap to play it), the Sync Config pill (resyncs the level config from
    /// the sheet, <see cref="ILevelConfigSync"/>; boot already did once) and the +10 booster buttons. It is the boot
    /// landing screen, and Gameplay's back button returns here.
    /// </summary>
    public sealed class MainScreen : ScreenBase
    {
        private readonly MainParam _param;
        private readonly LevelSelectWidget _levels;
        private readonly ISceneService _scenes;
        private readonly ILoadingCover _cover;
        private readonly ILocalizationService _loc;
        private readonly ILevelConfigSync _sync;
        private readonly PlayerProfile _profile;
        private readonly LevelCatalog _catalog;
        private readonly ILog _log;
        private bool _leaving;
        private bool _listShown;                                              // the debug level list is up (Home screen otherwise)
        private CancellationTokenSource _syncCts;

        public MainScreen(MainParam param, LevelSelectWidget levels, ISceneService scenes, ILoadingCover cover,
            ILocalizationService loc, ILevelConfigSync sync, PlayerProfile profile, LevelCatalog catalog, ILog log = null)
        {
            _catalog = catalog;
            _profile = profile;
            _sync = sync;
            _cover = cover;
            _loc = loc;
            _param = param;
            _levels = levels;
            _scenes = scenes;
            _log = log ?? new NullLog();
        }

        public override async UniTask OnLoadAsync(CancellationToken ct)
        {
            await _levels.CreateAsync(ct);
            _levels.LevelChosen += OnLevelChosen;
            _levels.SyncRequested += OnSyncRequested;
            _levels.BoosterGrantRequested += OnBoosterGrantRequested;
            _levels.StartRequested += OnStartRequested;
            _levels.DebugToggleRequested += OnDebugToggle;
            _levels.ResetRequested += OnResetProgress;
            _levels.CoinGrantRequested += OnCoinGrantRequested;
            _levels.UnlockAllRequested += OnUnlockAll;
            RefreshBoosterGrants();
            _syncCts = new CancellationTokenSource();
        }

        public override void OnEnter()
        {
            _leaving = false;
            _levels.SetVisible(true);
            _levels.SetStartLevel(_profile.CurrentLevel + 1);
            _levels.ShowLevelList(_listShown);
            _levels.SetInteractable(true);
            _log.Info($"[MainScreen] entered (cold boot: {_param.ColdBoot}).");
        }

        // OnPause/OnResume also fire when the APP loses/regains focus (clicking outside the Game view, a
        // phone call): pausing must never hide the list — it only stops taking taps. Hiding is OnExit's job.
        public override void OnPause() => _levels.SetInteractable(false);
        public override void OnResume() => _levels.SetInteractable(true);
        public override void OnExit() => _levels.SetVisible(false);

        public override UniTask OnUnloadAsync(CancellationToken ct)
        {
            _levels.LevelChosen -= OnLevelChosen;
            _levels.SyncRequested -= OnSyncRequested;
            _levels.BoosterGrantRequested -= OnBoosterGrantRequested;
            _levels.StartRequested -= OnStartRequested;
            _levels.DebugToggleRequested -= OnDebugToggle;
            _levels.ResetRequested -= OnResetProgress;
            _levels.CoinGrantRequested -= OnCoinGrantRequested;
            _levels.UnlockAllRequested -= OnUnlockAll;
            _syncCts?.Cancel();
            _syncCts?.Dispose();
            _syncCts = null;
            _levels.Dispose();
            return UniTask.CompletedTask;
        }

        private void OnLevelChosen(int index)
        {
            if (_leaving) return;              // one navigation per visit — a double tap must not load twice
            _leaving = true;
            _log.Info($"[MainScreen] play level index {index}.");
            PlayAsync(index).Forget();
        }

        /// <summary>Home screen Start: play the player's current level (their saved progress).</summary>
        private void OnStartRequested() => OnLevelChosen(_profile.CurrentLevel);

        /// <summary>The debug button: show or hide the level list over the Home screen.</summary>
        private void OnDebugToggle()
        {
            _listShown = !_listShown;
            _levels.ShowLevelList(_listShown);
        }

        /// <summary>Debug Reset: the player as on a fresh install — no coins, no boosters, level 1.</summary>
        private void OnResetProgress()
        {
            if (_leaving) return;
            _profile.ResetToFreshInstall();
            _levels.SetStartLevel(_profile.CurrentLevel + 1);
            RefreshBoosterGrants();
            _log.Info("[MainScreen] reset to a fresh install: level 1, empty wallet.");
        }

        /// <summary>Debug Unlock All: the player is on the last level of the catalog — every level reached.</summary>
        private void OnUnlockAll() => SetProgress(Math.Max(0, _catalog.Count - 1));

        private void SetProgress(int play)
        {
            if (_leaving) return;
            _profile.SetCurrentLevel(play);
            _levels.SetStartLevel(_profile.CurrentLevel + 1);
            _log.Info($"[MainScreen] progress set: the player is on level {_profile.CurrentLevel + 1}.");
        }

        /// <summary>How many boosters one tap of a bottom-bar button gives.</summary>
        private const int BoosterGrantAmount = 10;

        /// <summary>A bottom-bar button: +<see cref="BoosterGrantAmount"/> of that booster, straight into the wallet.</summary>
        private void OnBoosterGrantRequested(string boosterId)
        {
            if (_leaving) return;
            _profile.GrantBoosters(new ResourceKey(boosterId), BoosterGrantAmount, GrantSource.Compensation);
            _log.Info($"[MainScreen] +{BoosterGrantAmount} {boosterId} → {_profile.BoosterCount(new ResourceKey(boosterId))}.");
            RefreshBoosterGrants();
        }

        private void RefreshBoosterGrants()
        {
            _levels.SetBoosterGrants(_loc.Get(LocKeys.LevelSelectBoosterGrant, BoosterGrantAmount), id => _profile.BoosterCount(new ResourceKey(id)));
            _levels.SetCoinGrant(CoinGrantAmount, _profile.Coins(ResourceKeys.Coins));
        }

        /// <summary>How many coins one tap of the bottom-bar coin button gives.</summary>
        private const int CoinGrantAmount = 5000;

        /// <summary>The bottom-bar coin button: +<see cref="CoinGrantAmount"/> coins, straight into the wallet.</summary>
        private void OnCoinGrantRequested()
        {
            if (_leaving) return;
            _profile.GrantCoins(ResourceKeys.Coins, CoinGrantAmount, GrantSource.Compensation);
            _log.Info($"[MainScreen] +{CoinGrantAmount} coins → {_profile.Coins(ResourceKeys.Coins)}.");
            RefreshBoosterGrants();
        }

        private void OnSyncRequested()
        {
            if (_leaving || _syncCts == null) return;
            SyncAsync(_syncCts.Token).Forget();
        }

        /// <summary>Resync the level config: the pill reads "Syncing…" and takes no taps meanwhile, then says how many
        /// levels changed (or that it failed) for <see cref="SyncResultSeconds"/> before it reads "Config" again.</summary>
        private async UniTaskVoid SyncAsync(CancellationToken ct)
        {
            _levels.SetSyncStatus(_loc.Get(LocKeys.LevelSelectSyncBusy), busy: true);
            string result;
            try
            {
                var report = await _sync.SyncAsync(ct);
                _levels.Refresh();
                result = report.Ran ? _loc.Get(LocKeys.LevelSelectSyncDone, report.Changed.Count + report.ChangedConveyors.Count) : _loc.Get(LocKeys.LevelSelectSyncFailed);
                _log.Info($"[MainScreen] level config sync: {report}.");
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e)
            {
                result = _loc.Get(LocKeys.LevelSelectSyncFailed);
                _log.Error("[MainScreen] level config sync failed: " + e.Message);
            }
            _levels.SetSyncStatus(result, busy: false);
            if (await UniTask.Delay(TimeSpan.FromSeconds(SyncResultSeconds), cancellationToken: ct).SuppressCancellationThrow()) return;
            _levels.SetSyncStatus(null, busy: false);
        }

        private const float SyncResultSeconds = 2f;

        /// <summary>The loading cover goes up first, then the Gameplay screen loads under it (its assets and the board
        /// take a couple of seconds); Gameplay takes the cover down once the round is on screen.</summary>
        private async UniTaskVoid PlayAsync(int index)
        {
            try
            {
                await _cover.ShowAsync(_loc.Get(LocKeys.LoadingTitle), default);
                await _scenes.LoadAsync(SceneKeys.Gameplay, new GameplayParam(LevelIndex: index), SceneTransition.Replace);
            }
            catch (Exception e)
            {
                _leaving = false;
                _cover.HideAsync().Forget();
                _log.Error("[MainScreen] could not open Gameplay: " + e.Message);
            }
        }
    }
}
