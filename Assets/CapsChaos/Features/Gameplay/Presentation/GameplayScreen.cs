using System;
using System.Collections.Generic;
using System.Threading;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Cysharp.Threading.Tasks;
using Game.Application;
using Game.Domain;
using Game.Gen;
using Game.Views;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Presentation
{
    /// <summary>The Gameplay scene's WorldRoot, handed over by the scope (the framework has no WorldRoot API).</summary>
    public sealed class GameplaySceneRoot
    {
        public Transform WorldRoot { get; }
        public GameplaySceneRoot(Transform worldRoot) => WorldRoot = worldRoot;
    }

    /// <summary>
    /// The Gameplay screen controller (GDD §5, §7). Decides WHAT happens: it owns the round
    /// (<see cref="CapChaosGame"/>), turns a lane tap into a rules call, and replays the returned facts on
    /// the <see cref="BoardView"/> in order (R9 — the Domain has already resolved; the board catches up).
    /// Loaded additively by the scene service as <c>Scenes/Gameplay</c> on top of Master.
    /// <para>Round end (GDD §5.4): after a short pause the Result popup — "GOOD JOB" + NEXT on a win (R14), "YOU CAN
    /// DO IT" + RESTART on a loss (R15). NEXT plays the next level, RESTART the same one, both in place (no scene
    /// reload); Back on the popup goes Home.</para>
    /// </summary>
    public sealed class GameplayScreen : ScreenBase
    {
        private readonly GameplayParam _param;
        private readonly LevelCatalog _catalog;
        private readonly IRenderLayerRegistry _layers;
        private readonly IAssetService _assets;
        private readonly ISceneService _scenes;
        private readonly GameplaySceneRoot _root;
        private readonly GameplayHudWidget _hud;
        private readonly IDialogService _dialogs;
        private readonly ILog _log;

        private readonly BoardPrefabs _prefabs = new BoardPrefabs();
        private readonly List<GameObject> _held = new List<GameObject>();
        /// <summary>A fact to replay, bound at enqueue time to the TRAY it concerns (rules slots get reused).</summary>
        private readonly struct Step
        {
            public readonly GameFact Fact; public readonly int Tray; public readonly int Cell;
            public Step(GameFact fact, int tray = -1, int cell = -1) { Fact = fact; Tray = tray; Cell = cell; }
        }
        private readonly Queue<Step> _replay = new Queue<Step>();

        private CancellationTokenSource _roundCts;
        private LevelDefinition _level;
        private CapChaosGame _game;
        private int _levelIndex;
        private GameObject _boardGo, _inputGo;
        private BoardView _board;
        private BoardInputView _input;
        private int[] _laneShown;          // how many trays of each lane have been put on the belt so far
        private int[] _trayInSlot;         // rules slot → id of the tray the RULES currently have there
        private readonly Dictionary<int, int> _cells = new Dictionary<int, int>();   // tray id → bottles assigned so far
        private int _nextTray;
        private readonly List<UniTask> _packs = new List<UniTask>();   // boxes still animating; the round end waits for all
        private bool _replaying;
        private bool _leaving;

        public GameplayScreen(GameplayParam param, LevelCatalog catalog, IRenderLayerRegistry layers,
            IAssetService assets, ISceneService scenes, GameplaySceneRoot root, GameplayHudWidget hud,
            IDialogService dialogs, ILog log = null)
        {
            _dialogs = dialogs;
            _hud = hud;
            _scenes = scenes;
            _param = param;
            _catalog = catalog;
            _layers = layers;
            _assets = assets;
            _root = root;
            _log = log ?? new NullLog();
        }

        public override async UniTask OnLoadAsync(CancellationToken ct)
        {
            _prefabs.Bottle = await Hold(AssetKeys.Bottle, ct);
            _prefabs.BottleHidden = await Hold(AssetKeys.BottleHidden, ct);
            _prefabs.Cap = await Hold(AssetKeys.Cap, ct);
            _prefabs.CapTray = await Hold(AssetKeys.CapTray, ct);
            _prefabs.Box = await Hold(AssetKeys.Box, ct);
            _prefabs.Slot = await Hold(AssetKeys.Slot, ct);
            _prefabs.Lane = await Hold(AssetKeys.Lane, ct);
            _prefabs.Floor = await Hold(AssetKeys.Floor, ct);
            _hud.Attach();
            _hud.RetryRequested += OnRetry;
            _hud.HomeRequested += GoHome;
            StartRound(_param.LevelIndex);
        }

        public override void OnEnter()
        {
            _hud.SetVisible(true);
            _hud.SetInteractable(true);
            _log.Info($"[GameplayScreen] entered — {_level?.Id} ({_catalog.Normalize(_levelIndex) + 1}/{_catalog.Count}).");
        }

        // OnPause/OnResume also fire when the APP loses/regains focus: the HUD stays visible and only stops
        // taking taps. It is a sibling under the Ui host, so OnEnter/OnExit show and hide it.
        public override void OnPause() => _hud.SetInteractable(false);
        public override void OnResume() => _hud.SetInteractable(true);

        public override void OnExit()
        {
            _hud.SetVisible(false);
            TeardownRound();
        }

        /// <summary>Back (Escape in the Editor, the system back on Android) does what Home does.</summary>
        public override void OnBackRequested() => GoHome();

        /// <summary>HUD Restart (GDD §5): replay the current level from the start, at once, no confirmation.</summary>
        private void OnRetry()
        {
            if (_leaving) return;
            _log.Info($"[GameplayScreen] restart {_level?.Id}.");
            StartRound(_levelIndex);
        }

        /// <summary>HUD Home / Back: return to the level list on Main.</summary>
        private void GoHome()
        {
            if (_leaving) return;
            _leaving = true;
            TeardownRound();
            _scenes.LoadAsync(SceneKeys.Main, new MainParam(ColdBoot: false), SceneTransition.Replace)
                .Forget(e => { _leaving = false; _log.Error("[GameplayScreen] could not return to Main: " + e.Message); });
        }

        public override UniTask OnUnloadAsync(CancellationToken ct)
        {
            TeardownRound();
            _hud.RetryRequested -= OnRetry;
            _hud.HomeRequested -= GoHome;
            _hud.Dispose();
            for (int i = _held.Count - 1; i >= 0; i--) _assets.Release(_held[i]);
            _held.Clear();
            return UniTask.CompletedTask;
        }

        // ── round ────────────────────────────────────────────────────────────────────────────
        private void StartRound(int index)
        {
            TeardownRound();
            _levelIndex = _catalog.Normalize(index);
            // already parsed and validated at boot (LevelConfigNode) — a failure here means boot never loaded them
            try { _level = _catalog.Get(_levelIndex); }
            catch (LevelLoadException e) { _log.Error("[GameplayScreen] " + e.Message); return; }

            _game = new CapChaosGame(_level);
            _roundCts = new CancellationTokenSource();
            var cam = _layers.GetCamera(RenderLayers.GamePlay);

            _boardGo = new GameObject("Board_" + _level.Id);
            _boardGo.transform.SetParent(_root.WorldRoot, false);
            _board = _boardGo.AddComponent<BoardView>();
            // NOT Stamp: Stamp zeroes every localPosition.z in the subtree (a 2D-rig contract) and would
            // flatten the 3D board (ADR-001 §5.5). Put the board on the GamePlay layer's culling layer only.
            int boardLayer = _layers.GetHost(RenderLayers.GamePlay).gameObject.layer;
            _board.Bind(_prefabs, go => SetLayer(go.transform, boardLayer));
            _board.PlaceInFrontOf(cam);
            _board.BuildTable(_level.Slots, _level.Lanes.Count);

            var stack = _game.Stack;
            _board.BuildStack(stack.Cols, stack.Depth, (float)_level.StackScale);
            for (int x = 0; x < stack.Cols; x++)
                for (int z = 0; z < stack.Depth; z++)
                    for (int h = 0; h < stack.Height(x, z); h++)
                    {
                        var b = stack.At(x, z, h);
                        _board.AddBottle(x, z, h, b.Color.ToTint(), b.Hidden);
                    }

            _laneShown = new int[_level.Lanes.Count];
            for (int j = 0; j < _level.Lanes.Count; j++)
                for (; _laneShown[j] < Math.Min(_level.Lanes[j].Count, DesignTokens.Board.VisibleTraysPerLane); _laneShown[j]++)
                    _board.AddLaneTray(j, _level.Lanes[j][_laneShown[j]].ToTint());
            _trayInSlot = new int[_level.Slots];
            _cells.Clear();
            _nextTray = 0;
            _packs.Clear();
            SetLayer(_boardGo.transform, boardLayer);

            _inputGo = new GameObject("BoardInput", typeof(RectTransform));
            _inputGo.transform.SetParent(_layers.GetHost(RenderLayers.GamePlay), false);
            _input = _inputGo.AddComponent<BoardInputView>();
            _input.Bind(cam, _board);
            _layers.Stamp(_inputGo, RenderLayers.GamePlay);
            _input.TrayTapped += OnTrayTapped;

            _log.Info($"[GameplayScreen] round {_level.Id}: {stack.Count} bottles, {_level.Lanes.Count} lanes, {_level.Slots} slots.");
        }

        private void TeardownRound()
        {
            if (_input != null) _input.TrayTapped -= OnTrayTapped;
            _roundCts?.Cancel();
            _roundCts?.Dispose();
            _roundCts = null;
            _replay.Clear();
            _replaying = false;
            if (_inputGo != null) Object.Destroy(_inputGo);
            if (_boardGo != null) Object.Destroy(_boardGo);
            _inputGo = _boardGo = null;
            _board = null; _input = null; _game = null;
        }

        // ── input → rules → replay ───────────────────────────────────────────────────────────
        /// <summary>
        /// R5: only the FRONT tray of a lane is released to a slot. A tray behind it answers "not this one" by
        /// shaking; so does the front tray when no slot is free (R8). The belt itself is not tappable.
        /// <para>"Free" is what the player SEES: the rules empty a slot the instant its tray is full, but on screen
        /// that tray is still collecting (bottles in flight, the box packing) until the box lifts off. A tap in that
        /// window is refused BEFORE it reaches the rules — nothing is placed and no bottle moves — so the board
        /// never shows a tray the player could not see room for. The rules' own free slots always include the
        /// on-screen ones, so a tap that passes this check is never refused for a full row by the rules.</para>
        /// </summary>
        private void OnTrayTapped(int lane, int index)
        {
            if (_game == null || _game.Status != GameStatus.Playing) return;
            if (index > 0) { _board.ShakeTray(lane, index).Forget(); return; }
            if (!_board.HasClearSlot) { _board.ShakeTray(lane, 0).Forget(); return; }

            var result = _game.Tap(lane);
            if (!result.Accepted)
            {
                if (result.Outcome == TapOutcome.RejectedNoFreeSlot) _board.ShakeTray(lane, 0).Forget();
                return;
            }

            foreach (var f in result.Facts)
            {
                switch (f)
                {
                    // Immediate feedback: the tapped tray leaves the belt NOW, not after earlier animations
                    // replay. Every other fact keeps its order in the replay queue.
                    case TrayPlaced t:
                        int id = ++_nextTray;
                        _trayInSlot[t.Slot] = id;
                        _cells[id] = 0;
                        _board.PlaceTray(id, t.Lane, t.Slot).Forget();
                        break;
                    case LaneAdvanced a:
                        var tail = CapColor.None;
                        if (_laneShown[a.Lane] < _level.Lanes[a.Lane].Count) tail = _level.Lanes[a.Lane][_laneShown[a.Lane]++];
                        _board.AdvanceLane(a.Lane, tail.ToTint()).Forget();
                        break;
                    case BottlePicked p:
                        int tray = _trayInSlot[p.Slot];
                        _replay.Enqueue(new Step(p, tray, _cells[tray]++));
                        break;
                    case TrayPacked k:
                        _replay.Enqueue(new Step(k, _trayInSlot[k.Slot]));
                        break;
                    case BottleCapped _:
                        break;                                     // part of the bottle's flight
                    default:
                        _replay.Enqueue(new Step(f));
                        break;
                }
            }
            if (!_replaying) ReplayAsync(_roundCts.Token).Forget();
        }

        private async UniTaskVoid ReplayAsync(CancellationToken ct)
        {
            _replaying = true;
            try
            {
                while (_replay.Count > 0 && !ct.IsCancellationRequested)
                    await Play(_replay.Dequeue(), ct);
            }
            catch (OperationCanceledException) { /* round torn down mid-animation */ }
            finally { if (!ct.IsCancellationRequested) _replaying = false; }
        }

        private async UniTask Play(Step step, CancellationToken ct)
        {
            switch (step.Fact)
            {
                case BottlePicked p:
                    await _board.FlyBottle(p.X, p.Z, step.Tray, step.Cell);
                    break;
                case StackDropped d:
                    await _board.DropPile(d.X, d.Z);
                    break;
                case BottleRevealed r:
                    await _board.Reveal(r.X, r.Z, r.Color.ToTint());
                    break;
                case TrayPacked k:
                    _packs.Add(_board.PackTray(step.Tray, k.Color.ToTint()).Preserve());
                    break;
                case LevelCompleted _:
                    foreach (var pack in _packs) await pack;
                    _log.Info($"[GameplayScreen] {_level.Id} cleared.");
                    await UniTask.Delay(TimeSpan.FromSeconds(DesignTokens.Motion.RoundEndPause), cancellationToken: ct);
                    await ShowResultAsync(won: true, ct);
                    break;
                case LevelFailed f:
                    _log.Info($"[GameplayScreen] {_level.Id} failed ({f.Reason}).");
                    await _board.Jam();
                    await UniTask.Delay(TimeSpan.FromSeconds(DesignTokens.Motion.RoundEndPause), cancellationToken: ct);
                    await ShowResultAsync(won: false, ct);
                    break;
            }
        }

        /// <summary>The Result popup, then what its button means: NEXT → the next level, RESTART → this one again.</summary>
        private async UniTask ShowResultAsync(bool won, CancellationToken ct)
        {
            var result = await _dialogs.ShowAsync<ResultDialog, Unit>(
                new ResultArgs(won, _catalog.Normalize(_levelIndex) + 1), default, ct);
            // A torn-down round (Home, Restart, scene change) aborts the popup — nothing more to do.
            if (ct.IsCancellationRequested || result.Reason == DialogCloseReason.Aborted || result.Reason == DialogCloseReason.CloseAll)
                return;
            if (result.Reason == DialogCloseReason.BackButton) { GoHome(); return; }
            _log.Info($"[GameplayScreen] {(won ? "next" : "restart")} after {_level?.Id}.");
            StartRound(won ? _levelIndex + 1 : _levelIndex);
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        private async UniTask<GameObject> Hold(AssetKey<GameObject> key, CancellationToken ct)
        {
            var go = await _assets.LoadAsync(key, ct);
            if (go == null) throw new InvalidOperationException($"[GameplayScreen] prefab '{key}' did not load");
            _held.Add(go);
            return go;
        }
    }
}
