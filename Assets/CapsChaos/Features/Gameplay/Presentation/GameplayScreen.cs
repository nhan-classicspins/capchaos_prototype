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
    /// (<see cref="CapChaosGame"/>), turns a lane tap into a rules call, steps the oval belt on the fixed gameplay tick
    /// (<see cref="BeltClock"/>, one row every 1 / <c>BeltRowsPerSecond</c> s) and plays the facts each returns on the
    /// <see cref="BoardView"/> at once (R9 — the Domain has already resolved; the board catches up).
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
        private readonly ILocalizationService _loc;
        private readonly UiPaletteProvider _palette;
        private readonly ContainerPaletteProvider _containerPalette;
        private readonly BeltClock _clock;
        private readonly IGameplayGateControl _gate;
        private readonly IWalletService _wallet;
        private readonly IAdsService _ads;
        private readonly IGameConfig _config;
        private readonly PlayerProfile _profile;
        private BoosterCatalog _boosters;                                     // held from load to unload
        private readonly IWorldViewport _viewport;
        private readonly ILog _log;
        private readonly GameTime _time;   // the gameplay clock: GameSpeed scales the belt and every board animation
        private readonly ILoadingCover _cover;   // raised by Main over the load; down once the round is on screen
        private bool _coverUp = true;            // the loading cover is over the board: the feeders' run-in waits for it

        private readonly BoardPrefabs _prefabs = new BoardPrefabs();
        private readonly List<GameObject> _held = new List<GameObject>();

        private CancellationTokenSource _roundCts;
        private LevelDefinition _level;
        private CapChaosGame _game;
        private int _levelIndex;
        private GameObject _boardGo, _inputGo;
        private Quaternion? _camRotation;                                    // the GamePlay camera's rig rotation, while the board has it pitched
        private readonly BoardFloorView _floor;                              // authored in Gameplay.unity; the board frames it
        private BoardView _board;
        private BoardInputView _input;
        private int[] _laneShown;          // how many trays of each lane have been put on the belt so far
        private int[] _trayInSlot;         // rules slot → id of the tray the RULES currently have there
        private readonly Dictionary<int, int> _cells = new Dictionary<int, int>();   // tray id → bottles assigned so far
        private readonly Dictionary<int, int> _capacity = new Dictionary<int, int>();   // tray id → items it takes (R21)
        private int _nextTray;
        private readonly List<UniTask> _packs = new List<UniTask>();   // boxes still animating; the round end waits for all
        private float _beltTime;           // seconds since the belt's last row step
        private GameFeel _feel;           // the board's tunable feel (addressable GameFeel), or the defaults
        private bool _feelHeld;            // _feel is an Addressables hold to release on unload
        private bool _holding;             // the player holds an empty spot of the board: the game runs at HoldSpeed
        private bool _ending;              // the rules finished the round; the result is on its way
        private bool _offering;            // a slot offer (R20) is on screen or paying
        private bool _leaving;
        private BoosterPhase _boosterPhase;  // Booster Hand: waiting for the player to pick a box, or playing it
        private int _activeBooster = -1;     // HUD index of the booster in play
        private CancellationTokenSource _boosterCts;

        private enum BoosterPhase { None, Choosing, Acting }

        public GameplayScreen(GameplayParam param, LevelCatalog catalog, IRenderLayerRegistry layers,
            IAssetService assets, ISceneService scenes, GameplaySceneRoot root, GameplayHudWidget hud,
            IDialogService dialogs, ILocalizationService loc, UiPaletteProvider palette, BeltClock clock,
            IGameplayGateControl gate, IWalletService wallet, IAdsService ads, IGameConfig config, PlayerProfile profile,
            IWorldViewport viewport, ContainerPaletteProvider containerPalette, BoardFloorView floor, GameTime time, ILoadingCover cover, ILog log = null)
        {
            _time = time;
            _cover = cover;
            _floor = floor;
            _containerPalette = containerPalette;
            _viewport = viewport;
            _wallet = wallet;
            _ads = ads;
            _config = config;
            _profile = profile;
            _clock = clock;
            _gate = gate;
            _loc = loc;
            _palette = palette;
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
            // the item of each colour, in CapColor order (R O B G P Y C N) — matched by the colour of its texture
            _prefabs.Items = new[]
            {
                await Hold(AssetKeys.Items.Items_01, ct),   // Red     #E8314D
                await Hold(AssetKeys.Items.Items_06, ct),   // Orange  #F97610
                await Hold(AssetKeys.Items.Items_02, ct),   // Blue    #2977F7
                await Hold(AssetKeys.Items.Items_03, ct),   // Green   #64E917
                await Hold(AssetKeys.Items.Items_07, ct),   // Purple  #9141D8
                await Hold(AssetKeys.Items.Items_04, ct),   // Yellow  #FBC40F
                await Hold(AssetKeys.Items.Items_08, ct),   // Cyan    #1AD1ED
                await Hold(AssetKeys.Items.Items_05, ct),   // "Brown" (code N) is drawn pink: #FE79C0
            };
            _prefabs.Containers = new[]                                     // by size 1 S … 4 XL (R21)
            {
                await Hold(AssetKeys.Containers.Container_S, ct), await Hold(AssetKeys.Containers.Container_M, ct),
                await Hold(AssetKeys.Containers.Container_L, ct), await Hold(AssetKeys.Containers.Container_XL, ct),
            };
            _prefabs.ContainerPalette = await _containerPalette.LoadAsync(ct);
            _feel = await _assets.TryLoadAsync(new AssetKey<GameFeel>(GameFeel.Address), ct);
            _feelHeld = _feel != null;
            if (_feel == null)
            {
                _log?.Warn($"[Gameplay] no addressable '{GameFeel.Address}' — the board uses its default feel");
                _feel = GameFeel.CreateDefault();
            }
            _prefabs.Feel = _feel;
            _boosters = await _assets.TryLoadAsync(new AssetKey<BoosterCatalog>(BoosterCatalog.Address), ct);
            if (_boosters == null) _log?.Warn($"[Gameplay] no addressable '{BoosterCatalog.Address}' — no booster buttons");
            if (_prefabs.ContainerPalette == null)
                _log?.Warn($"[Gameplay] no addressable '{ContainerPalette.Address}' — containers keep their authored material");
            _prefabs.Slot = await Hold(AssetKeys.Slot, ct);
            _prefabs.Lane = await Hold(AssetKeys.Lane, ct);
            _prefabs.TrayLock = await Hold(AssetKeys.TrayLock, ct);
            _prefabs.TrayLink = await Hold(AssetKeys.TrayLink, ct);
            _prefabs.ConveyorBelt = await Hold(AssetKeys.ConveyorBelt, ct);
            _hud.Attach();
            _hud.UseFeel(_feel);
            BindBoosterOverlays();
            _hud.RetryRequested += OnRetry;
            _hud.HomeRequested += GoHome;
            _hud.BoosterRequested += OnBoosterRequested;
            _clock.Ticked += OnTick;
            RefreshCoins();
            RefreshBoosters();
            StartRound(_param.LevelIndex);
        }

        public override void OnEnter()
        {
            HideCoverThenRunInAsync().Forget();                               // the board is built: the loading cover goes
            _hud.SetVisible(true);
            _hud.SetInteractable(true);
            _log.Info($"[GameplayScreen] entered — level {_levelIndex + 1}: {_level?.Id} (of {_catalog.Count}).");
        }

        // OnPause/OnResume also fire when the APP loses/regains focus: the HUD stays visible and only stops
        // taking taps, and the belt stops with it (the gate halts the fixed tick — no time jump on resume).
        // The HUD is a sibling under the Ui host, so OnEnter/OnExit show and hide it.
        public override void OnPause() { _hud.SetInteractable(false); _gate.SetAppFocused(false); OnEmptyHold(false); }
        public override void OnResume() { _hud.SetInteractable(true); _gate.SetAppFocused(true); }

        public override void OnExit()
        {
            _hud.SetVisible(false);
            TeardownRound();
            RestoreCamera();
        }

        /// <summary>The board pitched the shared GamePlay camera down to look at the XZ plane (ADR-001 §8): give the rig
        /// its own rotation back, so whatever renders on that layer next finds the camera as it was.</summary>
        private void RestoreCamera()
        {
            if (_camRotation == null) return;
            var cam = _layers.GetCamera(RenderLayers.GamePlay);
            if (cam != null) cam.transform.rotation = _camRotation.Value;
            _camRotation = null;
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
            RestoreCamera();
            _scenes.LoadAsync(SceneKeys.Main, new MainParam(ColdBoot: false), SceneTransition.Replace)
                .Forget(e => { _leaving = false; _log.Error("[GameplayScreen] could not return to Main: " + e.Message); });
        }

        public override UniTask OnUnloadAsync(CancellationToken ct)
        {
            TeardownRound();
            RestoreCamera();
            _clock.Ticked -= OnTick;
            _hud.RetryRequested -= OnRetry;
            _hud.HomeRequested -= GoHome;
            _hud.BoosterRequested -= OnBoosterRequested;
            _hud.Dispose();
            for (int i = _held.Count - 1; i >= 0; i--) _assets.Release(_held[i]);
            _held.Clear();
            if (_feelHeld) _assets.Release(_feel);
            _feelHeld = false;
            if (_boosters != null) _assets.Release(_boosters);
            _boosters = null;
            return UniTask.CompletedTask;
        }

        // ── round ────────────────────────────────────────────────────────────────────────────
        private void StartRound(int index)
        {
            TeardownRound();
            // _levelIndex is the PLAY position — it keeps counting past the last level (the HUD shows it + 1); the catalog
            // decides which level that is, looping from levels.loopFrom once every level has been played
            _levelIndex = Math.Max(0, index);
            _hud.SetLevel(_loc.Get(LocKeys.HudLevel, _levelIndex + 1));
            // already parsed and validated at boot (LevelConfigNode) — a failure here means boot never loaded them
            try { _level = _catalog.Get(_catalog.IndexForPlay(_levelIndex, _config.Get(GameConfigKeys.LevelsLoopFrom))); }
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
            _board.Bind(_prefabs, go => SetLayer(go.transform, boardLayer), _time);
            SetLayer(_floor.transform, boardLayer);
            _board.UseFloor(_floor);
            _camRotation ??= cam.transform.rotation;                        // the rig's own pose, put back on exit
            _board.Frame(cam, _viewport.SafeRect.height * 0.5f);
            _board.BuildTable(_level.Slots, _level.ExtraSlots, _level.Lanes.Count);
            for (int s = 0; s < _game.SlotCount; s++)                                         // R22
                if (_game.SlotLockLeft(s) > 0) _board.LockSlotTurns(s, LockLabel(_game.SlotLockLeft(s)));

            var belt = _game.Belt;
            var conveyor = _level.Conveyor;
            _board.BuildLoop(belt.Rows, belt.Width, belt.PickRows, (float)conveyor.Scale, Knots(conveyor.Loop));
            _board.SetFeedReach(LoopBelt.FeedReach);
            for (int row = 0; row < belt.Rows; row++)
                for (int k = 0; k < belt.Width; k++)
                    if (belt.At(row, k) != CapColor.None) _board.AddBeltBottle(row, k, belt.At(row, k).ToTint());
            for (int f = 0; f < belt.FeederCount; f++)
            {
                var feeder = _level.Loop.Feeders[f];
                var tracks = new List<IReadOnlyList<TintFlavor>>();
                var masked = new List<IReadOnlyList<bool>>();                 // R23: hidden queue rows draw grey until they join
                for (int k = 0; k < belt.Width; k++)
                {
                    var queue = new List<TintFlavor>();
                    var hide = new List<bool>();
                    for (int d = 0; d < belt.FeederRemaining(f, k); d++)
                    {
                        queue.Add(belt.FeederAt(f, k, d).ToTint());
                        hide.Add(feeder.IsHiddenRow(belt.FeederRowsJoined(f) + d));
                    }
                    tracks.Add(queue);
                    masked.Add(hide);
                }
                _board.AddFeeder(belt.MergeAt(f), Knots(conveyor.Feeders[f].Nodes), tracks, masked);
            }
            _board.FinishLoop();
            for (int f = 0; f < belt.FeederCount; f++)                                        // R24: padlocks over locked queue rows
                foreach (var kv in _level.Loop.Feeders[f].LockedRows)
                {
                    if (kv.Key < belt.FeederRowsJoined(f)) continue;
                    int turns = belt.FeederRowLockLeft(f, kv.Key);
                    if (turns > 0) _board.LockFeederRow(f, kv.Key, LockLabel(turns));
                }
            _beltTime = 0f;
            _ending = false;
            _board.SetBeltPhase(belt.Offset, DesignTokens.Motion.BeltRowsPerSecond);

            _laneShown = new int[_level.Lanes.Count];
            for (int j = 0; j < _level.Lanes.Count; j++)
                for (; _laneShown[j] < _level.Lanes[j].Count && OnVisibleBelt(j, _laneShown[j]); _laneShown[j]++)
                    _board.AddLaneTray(j, LookOf(j, _laneShown[j]), _game.TrayPosition(j, _laneShown[j]));
            for (int j = 0; j < _level.Lanes.Count; j++)
                for (int t = 0; t < _laneShown[j]; t++) LinkIfShown(j, t);
            _trayInSlot = new int[_game.SlotCount];
            _offering = false;
            _cells.Clear();
            _capacity.Clear();
            _nextTray = 0;
            _packs.Clear();
            SetLayer(_boardGo.transform, boardLayer);
            _palette?.ApplyTo(_boardGo);                                     // the lock counts are TextTint text

            _inputGo = new GameObject("BoardInput", typeof(RectTransform));
            _inputGo.transform.SetParent(_layers.GetHost(RenderLayers.GamePlay), false);
            _input = _inputGo.AddComponent<BoardInputView>();
            _input.Bind(cam, _board);
            _layers.Stamp(_inputGo, RenderLayers.GamePlay);
            _input.TrayTapped += OnTrayTapped;
            _input.LockedSlotTapped += OnLockedSlotTapped;
            _input.EmptyHoldChanged += OnEmptyHold;

            if (!_coverUp) _board.StartFeederRunIn();                         // no cover (Restart): the queues run in at once
            _log.Info($"[GameplayScreen] round {_level.Id}: {belt.Count} bottles on conveyor {_level.Conveyor.Id ?? "(built in code)"} ({belt.Rows}×{belt.Width}), " +
                      $"{belt.FeederRemainingTotal} in {belt.FeederCount} feeder(s), {_level.Lanes.Count} lanes, {_level.Slots} slots.");
        }

        private void TeardownRound()
        {
            if (_input != null)
            {
                _input.TrayTapped -= OnTrayTapped; _input.LockedSlotTapped -= OnLockedSlotTapped; _input.EmptyHoldChanged -= OnEmptyHold;
            }
            _holding = false;
            ApplySpeed();
            EndBooster();
            _roundCts?.Cancel();
            _roundCts?.Dispose();
            _roundCts = null;
            _ending = false;
            if (_inputGo != null) Object.Destroy(_inputGo);
            if (_boardGo != null) Object.Destroy(_boardGo);
            _inputGo = _boardGo = null;
            _board = null; _input = null; _game = null;
        }

        // ── input → rules → replay ───────────────────────────────────────────────────────────
        /// <summary>
        /// R5: only the FRONT tray of a lane is released to a slot. A tray behind it answers "not this one" by
        /// shaking; so does the front tray when no slot is free (R8), when it is locked (R18), or when it is still linked
        /// (R19 — its partner has not reached the front of its lane yet; the pair shakes together). Lanes move on their
        /// own; once both linked trays stand at the front the rope lets go and each is released by its own tap. The belt
        /// itself is not tappable.
        /// <para>"Free" is what the player SEES: the rules empty a slot the instant its tray is full, but on screen
        /// that tray is still collecting (bottles in flight, the box packing) until the box lifts off. A tap in that
        /// window is refused BEFORE it reaches the rules — nothing is placed and no bottle moves — so the board
        /// never shows a tray the player could not see room for. The rules' own free slots always include the
        /// on-screen ones, so a tap that passes this check is never refused for a full row by the rules.</para>
        /// </summary>
        private void OnTrayTapped(int lane, int index)
        {
            if (_game == null || _game.Status != GameStatus.Playing) return;
            if (_boosterPhase == BoosterPhase.Acting) return;
            int tray = _game.TrayAtPosition(lane, index);
            if (tray < 0) return;
            if (_boosterPhase == BoosterPhase.Choosing) { OnBoosterTarget(lane, tray); return; }
            bool linked = _game.TryPartner(lane, tray, out var partner);

            if (!_game.IsAtFront(lane, tray)) { Shake(lane, tray, linked, partner); return; }
            if (_board.ClearSlotCount < 1) { Shake(lane, tray, linked, partner); return; }

            var result = _game.Tap(lane);
            if (!result.Accepted)
            {
                if (result.Outcome is TapOutcome.RejectedNoFreeSlot or TapOutcome.RejectedLocked or TapOutcome.RejectedLinkNotReady)
                    Shake(lane, tray, linked, partner);
                return;
            }
            PlayRelease(result.Facts).Forget();
        }

        /// <summary>
        /// The facts of a tap (or of Booster Hand): the released tray leaves the belt NOW, and the belt / reveal / lock
        /// beats play at once too; the bottles the trays take from the pick zone fly as they are listed. Returns the
        /// tray's flight to its slot (done once it has landed).
        /// </summary>
        private UniTask PlayRelease(IReadOnlyList<GameFact> facts)
        {
            TrayPlaced placed = null;                             // one per release; Hand may reveal / untie it first
            foreach (var f in facts) if (f is TrayPlaced p) { placed = p; break; }
            bool IsPlaced(int lane, int tray) => placed != null && placed.Lane == lane && placed.Tray == tray;
            var flight = UniTask.CompletedTask;
            var moved = new HashSet<int>();                       // lanes a tray left
            foreach (var f in facts)
            {
                switch (f)
                {
                    case TrayRevealed r when IsPlaced(r.Lane, r.Tray):                  // Hand: it shows its colour as it lifts
                        _board.RevealLaneTray(r.Lane, placed.Position, r.Color.ToTint()).Forget();
                        break;
                    case TrayLinkBroken b when IsPlaced(b.A.Lane, b.A.Index) || IsPlaced(b.B.Lane, b.B.Index):
                        _board.UnlinkTrays(b.A.Lane, BoardIndex(b.A, placed), b.B.Lane, BoardIndex(b.B, placed));
                        break;
                    case TrayPlaced t:
                        int id = ++_nextTray;
                        _trayInSlot[t.Slot] = id;
                        _cells[id] = 0;
                        _capacity[id] = t.Capacity;
                        moved.Add(t.Lane);
                        flight = _board.PlaceTray(id, t.Lane, t.Slot, t.Position).Preserve();
                        _board.SetTrayCount(id, CountLabel(t.Capacity));      // R21: how many items it still misses
                        break;
                    case LaneAdvanced a:
                        moved.Add(a.Lane);
                        break;
                    case TrayRevealed _:
                    case TrayLockTicked _:
                    case TrayLinkBroken _:
                        break;                                     // below, once the belt has moved
                    case SlotLockTicked _:
                    case FeederRowLockTicked _:
                        break;                                     // below, once every tray of this tap has claimed its slot
                }
            }
            foreach (int movedLane in moved) SyncLane(movedLane);
            foreach (var f in facts)
            {
                switch (f)
                {
                    case TrayRevealed r when IsPlaced(r.Lane, r.Tray):
                    case TrayLinkBroken b when IsPlaced(b.A.Lane, b.A.Index) || IsPlaced(b.B.Lane, b.B.Index):
                        break;                                                          // played above, before it flew
                    case TrayLinkBroken b:                                              // R19: both at the front
                        _board.UnlinkTrays(b.A.Lane, _game.TrayPosition(b.A.Lane, b.A.Index), b.B.Lane, _game.TrayPosition(b.B.Lane, b.B.Index));
                        break;
                    case TrayRevealed r:
                        _board.RevealLaneTray(r.Lane, _game.TrayPosition(r.Lane, r.Tray), r.Color.ToTint()).Forget();
                        break;
                    case TrayLockTicked k when k.Remaining > 0:
                        _board.SetTrayLock(k.Lane, _game.TrayPosition(k.Lane, k.Tray), LockLabel(k.Remaining)).Forget();
                        break;
                    case TrayLockTicked k:
                        _board.UnlockTray(k.Lane, _game.TrayPosition(k.Lane, k.Tray)).Forget();
                        break;
                    case SlotLockTicked k when k.Remaining > 0:                         // R22
                        _board.SetSlotTurns(k.Slot, LockLabel(k.Remaining)).Forget();
                        break;
                    case SlotLockTicked k:
                        _board.UnlockSlotTurns(k.Slot).Forget();
                        break;
                    case FeederRowLockTicked k when k.Remaining > 0:                    // R24
                        _board.SetFeederRowLock(k.Feeder, k.Row, LockLabel(k.Remaining)).Forget();
                        break;
                    case FeederRowLockTicked k:
                        _board.UnlockFeederRow(k.Feeder, k.Row).Forget();
                        break;
                }
            }
            PlayBelt(facts);
            return flight;
        }

        /// <summary>Where tray <paramref name="t"/> stands on its belt as the board has it BEFORE <paramref name="placed"/>
        /// leaves: the placed tray at the spot it left, any other where the rules have it (a link joins two lanes, so the
        /// placement never moved it).</summary>
        private int BoardIndex(TrayRef t, TrayPlaced placed) =>
            t.Lane == placed.Lane && t.Index == placed.Tray ? placed.Position : _game.TrayPosition(t.Lane, t.Index);

        /// <summary>Holding an empty spot of the board sets <see cref="GameTime.GameSpeed"/> to the feel's HoldSpeed (2) —
        /// the belt's steps and every board animation run on that clock; letting go sets it back to 1 (SKU owner,
        /// 2026-10-06). Only while a round is being played: an offer, the round end, a pause or leaving the screen puts
        /// it back to 1. The engine's own time scale is never touched — the UI keeps real time.</summary>
        private void OnEmptyHold(bool held)
        {
            _holding = held;
            ApplySpeed();
        }

        private void ApplySpeed()
        {
            bool fast = _holding && _game != null && _game.Status == GameStatus.Playing && !_offering && !_ending && _feel != null;
            _time.GameSpeed = fast ? _feel.HoldSpeed : 1f;
        }

        // ── the belt ─────────────────────────────────────────────────────────────────────────
        /// <summary>The fixed gameplay tick (gate open): the oval moves one row every 1 / BeltRowsPerSecond GAME seconds (the
        /// real dt × GameTime.GameSpeed); the
        /// board draws it in between.</summary>
        private void OnTick(float dt)
        {
            if (_board != null) _board.Frame(_layers.GetCamera(RenderLayers.GamePlay), _viewport.SafeRect.height * 0.5f);   // the view may resize
            if (_game == null || _board == null || _ending || _game.Status != GameStatus.Playing) return;
            float interval = 1f / DesignTokens.Motion.BeltRowsPerSecond;
            _beltTime += _time.Scale(dt);                                    // game seconds: GameSpeed 2 steps the belt twice as often
            while (_beltTime >= interval && _game.Status == GameStatus.Playing)
            {
                _beltTime -= interval;
                PlayBelt(_game.Step(feed: !_board.FeedersRunningIn));         // the loop always turns; queues join once they are in
            }
            _board.SetBeltPhase(_game.Belt.Offset + _beltTime / interval,
                _game.Status == GameStatus.Playing ? DesignTokens.Motion.BeltRowsPerSecond : 0f);
        }

        /// <summary>
        /// The belt facts of a tap or a step, at once: picked bottles fly to their tray (one after another, a stagger
        /// apart), fed bottles step onto the oval, a full tray packs once its own flights land, and the round end
        /// waits for every box before the result.
        /// </summary>
        private void PlayBelt(IReadOnlyList<GameFact> facts)
        {
            int flights = 0;
            foreach (var f in facts)
            {
                switch (f)
                {
                    case BottlePicked p:
                        int tray = _trayInSlot[p.Slot];
                        int item = _cells[tray]++;                    // the order it lands in: the board fills its anchors 4 at a time
                        int missing = (_capacity.TryGetValue(tray, out var cap) ? cap : 0) - _cells[tray];
                        _board.FlyBottle(p.Row, p.Track, tray, item, flights++ * DesignTokens.Motion.PickStagger,
                            missing > 0 ? CountLabel(missing) : null);
                        break;
                    case BottleFed d:
                        _board.FeedBottle(d.Feeder, d.Track, d.Row);
                        break;
                    case TrayPacked k:
                        _packs.Add(_board.PackTray(_trayInSlot[k.Slot]).Preserve());
                        break;
                    case SlotsRanOut _:
                        OfferSlotAsync(rescue: true, _roundCts.Token).Forget();
                        break;
                    case LevelCompleted _:
                        _profile.CompleteLevel(_levelIndex);                     // the player is on the next level now
                        _wallet.Grant(ResourceKeys.Coins, _config.Get(GameConfigKeys.EconomyWinReward), GrantSource.Reward);
                        RefreshCoins();
                        _ending = true;
                        ApplySpeed();
                        if (_boosterPhase == BoosterPhase.Choosing) EndBooster();     // nothing left to pick for
                        EndRoundAsync(f, _roundCts.Token).Forget();
                        break;
                    case LevelFailed _:
                        _ending = true;
                        ApplySpeed();
                        if (_boosterPhase == BoosterPhase.Choosing) EndBooster();     // nothing left to pick for
                        EndRoundAsync(f, _roundCts.Token).Forget();
                        break;
                }
            }
        }

        /// <summary>"Not this one": the tray shakes — with its partner, when it is linked and the partner is on the belt.</summary>
        private void Shake(int lane, int tray, bool linked, TrayRef partner)
        {
            _board.ShakeTray(lane, _game.TrayPosition(lane, tray)).Forget();
            if (linked && OnBelt(partner.Lane, partner.Index)) _board.ShakeTray(partner.Lane, _game.TrayPosition(partner.Lane, partner.Index)).Forget();
        }

        /// <summary>How authored tray <paramref name="tray"/> of <paramref name="lane"/> looks right now (R17, R18, R21: the
        /// items it takes, shown on the belt too).</summary>
        private TrayLook LookOf(int lane, int tray)
        {
            int locked = _game.IsAtFront(lane, tray) ? _game.LockLeft(lane) : _game.LockTurns(lane, tray);
            bool hidden = _game.IsTrayHidden(lane, tray);
            return new TrayLook(hidden ? TintFlavor.None : _game.TrayColor(lane, tray).ToTint(), hidden,
                locked > 0 ? LockLabel(locked) : null, (int)_game.TraySizeOf(lane, tray), CountLabel(_game.TrayCapacity(lane, tray)));
        }

        private string LockLabel(int turns) => _loc.Get(LocKeys.GameplayLockTurns, turns);
        private string CountLabel(int missing) => _loc.Get(LocKeys.GameplayContainerMissing, missing);

        private bool OnBelt(int lane, int tray) => tray < _laneShown[lane] && _game.TrayPosition(lane, tray) >= 0;

        /// <summary>Does tray <paramref name="tray"/> of <paramref name="lane"/> stand on the drawn stretch of its belt?</summary>
        private bool OnVisibleBelt(int lane, int tray)
        {
            int position = _game.TrayPosition(lane, tray);
            return position >= 0 && position < DesignTokens.Board.VisibleTraysPerLane;
        }

        /// <summary>
        /// Lay <paramref name="lane"/> out the way the rules have it now (R7): every tray at its belt position, and the trays that have come into view slide in at the back (ropes drawn for any pair now fully shown).
        /// </summary>
        private void SyncLane(int lane)
        {
            int shownBefore = _laneShown[lane];
            var tails = new List<TrayLook>();
            for (; _laneShown[lane] < _level.Lanes[lane].Count && OnVisibleBelt(lane, _laneShown[lane]); _laneShown[lane]++)
                tails.Add(LookOf(lane, _laneShown[lane]));
            var positions = new List<int>(_laneShown[lane] - _game.LaneHead(lane));
            for (int t = _game.LaneHead(lane); t < _laneShown[lane]; t++)
            {
                int position = _game.TrayPosition(lane, t);
                if (position >= 0) positions.Add(position);                 // a tray Booster Hand took is gone
            }
            _board.LayoutLane(lane, positions, tails).Forget();
            for (int t = shownBefore; t < _laneShown[lane]; t++) LinkIfShown(lane, t);
        }

        /// <summary>A tray just came onto the belt: tie it to its partner if that one is on the belt too (R19).</summary>
        private void LinkIfShown(int lane, int tray)
        {
            if (!_game.TryPartner(lane, tray, out var p) || !OnBelt(p.Lane, p.Index) || !OnBelt(lane, tray)) return;
            _board.LinkTrays(lane, _game.TrayPosition(lane, tray), p.Lane, _game.TrayPosition(p.Lane, p.Index));
        }

        // ── extra slots (R20) ───────────────────────────────────────────────────────────────
        /// <summary>A locked slot was tapped: offer it (Parking Slot). Ignored while another offer is up or the round is over.</summary>
        private void OnLockedSlotTapped(int slot)
        {
            if (_game == null || _game.Status != GameStatus.Playing || _ending || _offering || _game.SlotsRanOut) return;
            if (_boosterPhase != BoosterPhase.None) return;
            OfferSlotAsync(rescue: false, _roundCts.Token).Forget();
        }

        /// <summary>
        /// Offer one more slot for a rewarded ad or coins: Parking Slot when the player asked (✕ = no thanks), Out of Slot
        /// when the open slots ran out (Restart = play the level again). The belt waits while the popup is up. Paying is
        /// done here, not in the dialog: an ad that does not reward, or coins that are not there, opens nothing — and a
        /// rescue that was not paid for is offered again.
        /// </summary>
        private async UniTaskVoid OfferSlotAsync(bool rescue, CancellationToken ct)
        {
            if (_offering) return;
            _offering = true;
            ApplySpeed();
            _gate.SetDialogCovering(true);
            try
            {
                while (!ct.IsCancellationRequested && _game != null && _game.Status == GameStatus.Playing && _game.LockedSlotCount > 0)
                {
                    int price = _config.Get(rescue ? GameConfigKeys.SlotRescuePrice : GameConfigKeys.SlotUnlockPrice);
                    var args = new SlotOfferArgs(price, _wallet.CanAfford(ResourceKeys.Coins, price));
                    var result = rescue
                        ? await _dialogs.ShowAsync<OutOfSlotDialog, SlotOfferChoice>(args, default, ct)
                        : await _dialogs.ShowAsync<ParkingSlotDialog, SlotOfferChoice>(args, default, ct);
                    if (ct.IsCancellationRequested || result.Reason == DialogCloseReason.Aborted || result.Reason == DialogCloseReason.CloseAll)
                        return;
                    var choice = result.Reason == DialogCloseReason.BackButton ? SlotOfferChoice.Declined : result.Value;
                    if (choice == SlotOfferChoice.Declined)
                    {
                        if (rescue) { _log.Info($"[GameplayScreen] out of slots on {_level.Id}: restart."); StartRound(_levelIndex); }
                        return;
                    }
                    bool paid = choice == SlotOfferChoice.WatchAd
                        ? await _ads.ShowAsync(rescue ? AdPlacements.SlotRescueRewarded : AdPlacements.SlotUnlockRewarded, ct) == AdResult.Rewarded
                        : _wallet.TrySpend(ResourceKeys.Coins, price, GrantSource.Reward);
                    RefreshCoins();
                    if (paid)
                    {
                        _log.Info($"[GameplayScreen] slot unlocked by {(choice == SlotOfferChoice.WatchAd ? "ad" : $"{price} coins")}.");
                        var facts = _game.UnlockSlot();
                        foreach (var f in facts) if (f is SlotUnlocked u) _board.UnlockSlot(u.Slot).Forget();
                        PlayBelt(facts);
                        return;
                    }
                    if (!rescue) return;                                    // a rescue stays on offer until it is taken or declined
                }
            }
            catch (OperationCanceledException) { /* round torn down */ }
            finally
            {
                _gate.SetDialogCovering(false);
                _offering = false;
                ApplySpeed();
            }
        }

        // ── boosters ─────────────────────────────────────────────────────────────────────────
        /// <summary>One HUD button per booster of the catalog, in its order: its icon and, at the corner, how many the
        /// player owns — or the "+" when none. A button with no booster behind it is hidden.</summary>
        private void RefreshBoosters()
        {
            for (int i = 0; i < _hud.BoosterSlots; i++)
            {
                var booster = BoosterAt(i);
                if (booster == null) { _hud.SetBooster(i, false, null, null); continue; }
                long owned = _profile.BoosterCount(new ResourceKey(booster.Id));
                _hud.SetBooster(i, true, booster.Icon, owned > 0 ? _loc.Get(LocKeys.HudBoosterCount, owned) : null);
            }
        }

        /// <summary>Each booster button gets its booster's prompt and banner (BoosterCatalog), once per screen.</summary>
        private void BindBoosterOverlays()
        {
            for (int i = 0; i < _hud.BoosterSlots; i++)
            {
                var booster = BoosterAt(i);
                if (booster != null && booster.WaitsForTarget && booster.Prompt == null)
                    _log.Warn($"[GameplayScreen] booster '{booster.Id}' waits for a target but has no prompt prefab — nothing tells the player what to pick.");
                _hud.SetBoosterOverlays(i, booster?.WaitsForTarget == true ? booster.Prompt : null, booster?.Banner);
            }
        }

        private BoosterDefinition BoosterAt(int index) =>
            _boosters != null && index >= 0 && index < _boosters.Boosters.Count ? _boosters.Boosters[index] : null;

        /// <summary>
        /// A booster button was tapped. With none owned the shop would open (not built yet). A booster that cannot do
        /// anything right now says why in a toast and spends nothing (Hand: no box left in the queues, no slot free).
        /// One that <see cref="BoosterDefinition.WaitsForTarget"/> shows its prompt and waits for the pick (the other
        /// boosters off; tapping it again cancels, spending nothing); any other acts at once.
        /// </summary>
        private void OnBoosterRequested(int index)
        {
            var booster = BoosterAt(index);
            if (booster == null || _game == null || _ending) return;
            if (_boosterPhase == BoosterPhase.Acting) return;
            if (_boosterPhase == BoosterPhase.Choosing)
            {
                if (index == _activeBooster) { _log.Info($"[GameplayScreen] booster '{booster.Id}' cancelled."); EndBooster(); }
                return;
            }
            var resource = new ResourceKey(booster.Id);
            long owned = _profile.BoosterCount(resource);
            if (owned <= 0) { _log.Info($"[GameplayScreen] booster '{booster.Id}' tapped with none owned — the shop is not built yet."); return; }
            if (!CanUse(resource, out var why))
            {
                if (why.HasValue) _hud.ShowToast(_loc.Get(why.Value));
                return;
            }

            _activeBooster = index;
            _boosterCts = CancellationTokenSource.CreateLinkedTokenSource(_roundCts.Token);
            if (!booster.WaitsForTarget)
            {
                ActAsync(index, default, _boosterCts.Token).Forget();
                return;
            }
            _boosterPhase = BoosterPhase.Choosing;
            for (int i = 0; i < _hud.BoosterSlots; i++) _hud.SetBoosterInteractable(i, i == index);
            _hud.ShowBoosterPrompt(index, booster.Icon, Text(booster.PromptTitleKey), Text(booster.PromptHintKey));
            _log.Info($"[GameplayScreen] booster '{booster.Id}': pick a target.");
        }

        private string Text(string key) => string.IsNullOrEmpty(key) ? string.Empty : _loc.Get(new LocKey(key));

        /// <summary>Can booster <paramref name="booster"/> do anything right now? When not, <paramref name="why"/> is the
        /// toast to show (null = say nothing: its effect is not built).</summary>
        private bool CanUse(ResourceKey booster, out LocKey? why)
        {
            why = null;
            if (booster == ResourceKeys.BoosterHand)
            {
                if (!AnyBoxInQueues()) { why = LocKeys.ToastNoBoxLeft; return false; }
                if (!SlotFreeOnScreen()) { why = LocKeys.ToastNoSlots; return false; }
                return true;
            }
            _log.Info($"[GameplayScreen] booster '{booster.Value}' — its effect is not built yet.");
            return false;
        }

        private bool AnyBoxInQueues()
        {
            for (int j = 0; j < _game.LaneCount; j++) if (_game.LaneRemaining(j) > 0) return true;
            return false;
        }

        private bool SlotFreeOnScreen() => _game.HasFreeSlot && _board.ClearSlotCount > 0;

        /// <summary>The player picked a target for the booster waiting for one: belt tray <paramref name="tray"/> of
        /// <paramref name="lane"/>.</summary>
        private void OnBoosterTarget(int lane, int tray)
        {
            var booster = BoosterAt(_activeBooster);
            if (booster == null) { EndBooster(); return; }
            if (new ResourceKey(booster.Id) == ResourceKeys.BoosterHand && !SlotFreeOnScreen())
            {
                _hud.ShowToast(_loc.Get(LocKeys.ToastNoSlots));
                return;
            }
            ActAsync(_activeBooster, new TrayRef(lane, tray), _boosterCts.Token).Forget();
        }

        /// <summary>
        /// A booster acts: every booster button off, its banner plays its pass and goes (none = straight on), then its
        /// effect plays on the board; if the rules took it, it is spent. Once the effect is done the buttons come back.
        /// The board takes no tap meanwhile (the banner covers it, and the phase refuses it).
        /// <paramref name="target"/> is the pick of a booster that waits for one; default otherwise.
        /// </summary>
        private async UniTaskVoid ActAsync(int index, TrayRef target, CancellationToken ct)
        {
            var booster = BoosterAt(index);
            var resource = new ResourceKey(booster.Id);
            _boosterPhase = BoosterPhase.Acting;
            for (int i = 0; i < _hud.BoosterSlots; i++) _hud.SetBoosterInteractable(i, false);
            _hud.HideBoosterPrompts();
            try
            {
                await _hud.PlayBoosterBannerAsync(index, booster.Icon, ct);
                var effect = Apply(resource, target);
                if (effect == null) return;                                    // refused: nothing spent
                _profile.TryUseBooster(resource);
                RefreshBoosters();
                await effect.Value.AttachExternalCancellation(ct);
            }
            catch (OperationCanceledException) { /* round torn down */ }
            finally
            {
                if (!ct.IsCancellationRequested) EndBooster();
            }
        }

        /// <summary>The booster's effect on the rules, played on the board; null when the rules refused it (nothing to
        /// spend). The task is done once the board has caught up.</summary>
        private UniTask? Apply(ResourceKey booster, TrayRef target)
        {
            if (booster == ResourceKeys.BoosterHand)
            {
                var result = _game.TakeTray(target.Lane, target.Index);
                if (!result.Accepted)
                {
                    _log.Info($"[GameplayScreen] Hand refused L{target.Lane}#{target.Index}: {result.Outcome} — nothing spent.");
                    if (result.Outcome == TapOutcome.RejectedNoFreeSlot) _hud.ShowToast(_loc.Get(LocKeys.ToastNoSlots));
                    return null;
                }
                _log.Info($"[GameplayScreen] Hand took L{target.Lane}#{target.Index} out of its queue.");
                return PlayRelease(result.Facts);
            }
            _log.Info($"[GameplayScreen] booster '{booster.Value}' has no effect yet — nothing spent.");
            return null;
        }

        /// <summary>No booster in play: the prompt goes, every booster button is back on.</summary>
        private void EndBooster()
        {
            if (_boosterPhase == BoosterPhase.None && _activeBooster < 0) return;
            _boosterPhase = BoosterPhase.None;
            _activeBooster = -1;
            _boosterCts?.Cancel();
            _boosterCts?.Dispose();
            _boosterCts = null;
            _hud.HideBoosterPrompts();
            _hud.StopBoosterBanners();
            for (int i = 0; i < _hud.BoosterSlots; i++) _hud.SetBoosterInteractable(i, true);
        }

        private void RefreshCoins() => _hud.SetCoins(_loc.Get(LocKeys.HudCoins, _wallet.Balance(ResourceKeys.Coins)));

        private async UniTaskVoid EndRoundAsync(GameFact end, CancellationToken ct)
        {
            try
            {
                if (end is LevelFailed failed)
                {
                    _log.Info($"[GameplayScreen] {_level.Id} failed ({failed.Reason}).");
                    await _board.Jam();
                }
                else
                {
                    foreach (var pack in _packs) await pack;
                    _log.Info($"[GameplayScreen] {_level.Id} cleared.");
                }
                await UniTask.Delay(TimeSpan.FromSeconds(DesignTokens.Motion.RoundEndPause), cancellationToken: ct);
                await ShowResultAsync(won: end is LevelCompleted, ct);
            }
            catch (OperationCanceledException) { /* round torn down mid-animation */ }
        }

        /// <summary>The Result popup, then what its button means: NEXT → the next level, RESTART → this one again.</summary>
        private async UniTask ShowResultAsync(bool won, CancellationToken ct)
        {
            var result = await _dialogs.ShowAsync<ResultDialog, Unit>(
                new ResultArgs(won, _levelIndex + 1), default, ct);
            // A torn-down round (Home, Restart, scene change) aborts the popup — nothing more to do.
            if (ct.IsCancellationRequested || result.Reason == DialogCloseReason.Aborted || result.Reason == DialogCloseReason.CloseAll)
                return;
            if (result.Reason == DialogCloseReason.BackButton) { GoHome(); return; }
            _log.Info($"[GameplayScreen] {(won ? "next" : "restart")} after {_level?.Id}.");
            if (won) NextLevelAsync().Forget();
            else StartRound(_levelIndex);
        }

        /// <summary>The cover fades away, then the feeders' queues run in from the far end of their belts.</summary>
        private async UniTaskVoid HideCoverThenRunInAsync()
        {
            await _cover.HideAsync();
            _coverUp = false;
            if (_board != null) _board.StartFeederRunIn();
        }

        /// <summary>NEXT builds a whole new board in place (no scene load) — a noticeable hitch: do it under the loading
        /// cover. The round's own token dies in StartRound's teardown, so this runs on none.</summary>
        private async UniTaskVoid NextLevelAsync()
        {
            _coverUp = true;
            await _cover.ShowAsync(_loc.Get(LocKeys.LoadingTitle), default);
            if (_leaving) { HideCoverThenRunInAsync().Forget(); return; }
            StartRound(_levelIndex + 1);
            await UniTask.NextFrame();                                        // the first frame of the new board is drawn
            HideCoverThenRunInAsync().Forget();
        }

        /// <summary>A conveyor spline as the board draws it (the Domain knot → the View's).</summary>
        private static BeltNode[] Knots(IReadOnlyList<ConveyorNode> nodes)
        {
            var knots = new BeltNode[nodes.Count];
            for (int i = 0; i < knots.Length; i++)
                knots[i] = new BeltNode((float)nodes[i].X, (float)nodes[i].Z, (float)nodes[i].YRotation, nodes[i].Linear);
            return knots;
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
