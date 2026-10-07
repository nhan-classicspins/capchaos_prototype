using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;
using Game.Gen;

namespace Game.Presentation
{
    /// <summary>
    /// The Main screen: for now, the level list — every level in play order, one tap to play it — and the Sync Config
    /// pill, which resyncs the level config from the sheet (<see cref="ILevelConfigSync"/>; boot already did once) and
    /// relabels the tiles. It is the
    /// boot landing screen, and Gameplay's back button returns here, so any level is two taps away while the
    /// levels are being tuned. The Title screen (GDD §7) will take the landing spot later.
    /// </summary>
    public sealed class MainScreen : ScreenBase
    {
        private readonly MainParam _param;
        private readonly LevelSelectWidget _levels;
        private readonly ISceneService _scenes;
        private readonly ILoadingCover _cover;
        private readonly ILocalizationService _loc;
        private readonly ILevelConfigSync _sync;
        private readonly ILog _log;
        private bool _leaving;
        private CancellationTokenSource _syncCts;

        public MainScreen(MainParam param, LevelSelectWidget levels, ISceneService scenes, ILoadingCover cover,
            ILocalizationService loc, ILevelConfigSync sync, ILog log = null)
        {
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
            _syncCts = new CancellationTokenSource();
        }

        public override void OnEnter()
        {
            _leaving = false;
            _levels.SetVisible(true);
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
