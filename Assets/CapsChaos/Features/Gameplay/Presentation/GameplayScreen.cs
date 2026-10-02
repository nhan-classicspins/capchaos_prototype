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
        private readonly BeltClock _clock;
        private readonly IGameplayGateControl _gate;
        private readonly IWalletService _wallet;
        private readonly IAdsService _ads;
        private readonly IGameConfig _config;
        private readonly IUserData _userData;
        private readonly IWorldViewport _viewport;
        private readonly ILog _log;

        private readonly BoardPrefabs _prefabs = new BoardPrefabs();
        private readonly List<GameObject> _held = new List<GameObject>();

        private CancellationTokenSource _roundCts;
        private LevelDefinition _level;
        private CapChaosGame _game;
        private int _levelIndex;
        private GameObject _boardGo, _inputGo;
        private BoardView _board;
        private BoardInputView _input;
        private int[] _laneShown;          // how many trays of each lane have been put on the belt so far
        private int[] _laneTaken;          // how many trays have left each lane (== the rules' lane head, kept in step with the belt)
        private int[] _trayInSlot;         // rules slot → id of the tray the RULES currently have there
        private readonly Dictionary<int, int> _cells = new Dictionary<int, int>();   // tray id → bottles assigned so far
        private int _nextTray;
        private readonly List<UniTask> _packs = new List<UniTask>();   // boxes still animating; the round end waits for all
        private float _beltTime;           // seconds since the belt's last row step
        private bool _ending;              // the rules finished the round; the result is on its way
        private bool _offering;            // a slot offer (R20) is on screen or paying
        private bool _leaving;

        public GameplayScreen(GameplayParam param, LevelCatalog catalog, IRenderLayerRegistry layers,
            IAssetService assets, ISceneService scenes, GameplaySceneRoot root, GameplayHudWidget hud,
            IDialogService dialogs, ILocalizationService loc, UiPaletteProvider palette, BeltClock clock,
            IGameplayGateControl gate, IWalletService wallet, IAdsService ads, IGameConfig config, IUserData userData,
            IWorldViewport viewport, ILog log = null)
        {
            _viewport = viewport;
            _wallet = wallet;
            _ads = ads;
            _config = config;
            _userData = userData;
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
            _prefabs.Bottle = await Hold(AssetKeys.Bottle, ct);
            _prefabs.Cap = await Hold(AssetKeys.Cap, ct);
            _prefabs.CapTray = await Hold(AssetKeys.CapTray, ct);
            _prefabs.Box = await Hold(AssetKeys.Box, ct);
            _prefabs.Slot = await Hold(AssetKeys.Slot, ct);
            _prefabs.Lane = await Hold(AssetKeys.Lane, ct);
            _prefabs.Floor = await Hold(AssetKeys.Floor, ct);
            _prefabs.TrayLock = await Hold(AssetKeys.TrayLock, ct);
            _prefabs.TrayLink = await Hold(AssetKeys.TrayLink, ct);
            _hud.Attach();
            _hud.RetryRequested += OnRetry;
            _hud.HomeRequested += GoHome;
            _clock.Ticked += OnTick;
            GrantStarterCoins();
            RefreshCoins();
            StartRound(_param.LevelIndex);
        }

        public override void OnEnter()
        {
            _hud.SetVisible(true);
            _hud.SetInteractable(true);
            _log.Info($"[GameplayScreen] entered — {_level?.Id} ({_catalog.Normalize(_levelIndex) + 1}/{_catalog.Count}).");
        }

        // OnPause/OnResume also fire when the APP loses/regains focus: the HUD stays visible and only stops
        // taking taps, and the belt stops with it (the gate halts the fixed tick — no time jump on resume).
        // The HUD is a sibling under the Ui host, so OnEnter/OnExit show and hide it.
        public override void OnPause() { _hud.SetInteractable(false); _gate.SetAppFocused(false); }
        public override void OnResume() { _hud.SetInteractable(true); _gate.SetAppFocused(true); }

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
            _clock.Ticked -= OnTick;
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
            _board.PlaceInFrontOf(cam, _viewport.SafeRect.height * 0.5f);
            _board.BuildTable(_level.Slots, _level.ExtraSlots, _level.Lanes.Count);

            var belt = _game.Belt;
            var (sx, sz, sr) = _level.Shape.Corners(belt.Rows, belt.PickRows);
            _board.BuildLoop(belt.Rows, belt.Width, belt.PickRows, Floats(sx), Floats(sz), Floats(sr));
            for (int row = 0; row < belt.Rows; row++)
                for (int k = 0; k < belt.Width; k++)
                    if (belt.At(row, k) != CapColor.None) _board.AddBeltBottle(row, k, belt.At(row, k).ToTint());
            for (int f = 0; f < belt.FeederCount; f++)
            {
                var tracks = new List<IReadOnlyList<TintFlavor>>();
                for (int k = 0; k < belt.Width; k++)
                {
                    var queue = new List<TintFlavor>();
                    for (int d = 0; d < belt.FeederRemaining(f, k); d++) queue.Add(belt.FeederAt(f, k, d).ToTint());
                    tracks.Add(queue);
                }
                _board.AddFeeder(belt.MergeAt(f), tracks);
            }
            _board.FinishLoop();
            _beltTime = 0f;
            _ending = false;
            _board.SetBeltPhase(belt.Offset, DesignTokens.Motion.BeltRowsPerSecond);

            _laneShown = new int[_level.Lanes.Count];
            _laneTaken = new int[_level.Lanes.Count];
            for (int j = 0; j < _level.Lanes.Count; j++)
                for (; _laneShown[j] < Math.Min(_level.Lanes[j].Count, DesignTokens.Board.VisibleTraysPerLane); _laneShown[j]++)
                    _board.AddLaneTray(j, LookOf(j, _laneShown[j]));
            for (int j = 0; j < _level.Lanes.Count; j++)
                for (int t = 0; t < _laneShown[j]; t++) LinkIfShown(j, t);
            _trayInSlot = new int[_game.SlotCount];
            _offering = false;
            _cells.Clear();
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

            _log.Info($"[GameplayScreen] round {_level.Id}: {belt.Count} bottles on a {belt.Rows}×{belt.Width} belt, " +
                      $"{belt.FeederRemainingTotal} in {belt.FeederCount} feeder(s), {_level.Lanes.Count} lanes, {_level.Slots} slots.");
        }

        private void TeardownRound()
        {
            if (_input != null) { _input.TrayTapped -= OnTrayTapped; _input.LockedSlotTapped -= OnLockedSlotTapped; }
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
        /// shaking; so does the front tray when no slot is free (R8), when it is locked (R18), or when it is linked and
        /// its partner is not at the front yet (R19 — the pair shakes together). A belt carrying a tray linked to another
        /// lane is HELD with an empty front until the linked belt can step too (R19); its trays shake. A linked pair is released by a tap on
        /// either of its trays once both are ready. The belt itself is not tappable.
        /// <para>"Free" is what the player SEES: the rules empty a slot the instant its tray is full, but on screen
        /// that tray is still collecting (bottles in flight, the box packing) until the box lifts off. A tap in that
        /// window is refused BEFORE it reaches the rules — nothing is placed and no bottle moves — so the board
        /// never shows a tray the player could not see room for. The rules' own free slots always include the
        /// on-screen ones, so a tap that passes this check is never refused for a full row by the rules.</para>
        /// </summary>
        private void OnTrayTapped(int lane, int index)
        {
            if (_game == null || _game.Status != GameStatus.Playing) return;
            int tray = _laneTaken[lane] + index;
            bool linked = _game.TryPartner(lane, tray, out var partner);

            // the tray must stand at the front of its belt — or be the back tray of a same-lane pair whose front tray
            // does. A held belt (R19) has an empty front: nothing on it is at the front.
            bool atFront = _game.IsAtFront(lane, tray)
                || (linked && partner.Lane == lane && partner.Index == tray - 1 && _game.IsAtFront(lane, tray - 1));
            if (!atFront) { Shake(lane, tray, linked, partner); return; }
            if (_board.ClearSlotCount < (linked ? 2 : 1)) { Shake(lane, tray, linked, partner); return; }

            var result = _game.Tap(lane);
            if (!result.Accepted)
            {
                if (result.Outcome is TapOutcome.RejectedNoFreeSlot or TapOutcome.RejectedLocked or TapOutcome.RejectedLinkNotReady
                    or TapOutcome.RejectedBeltHeld)
                    Shake(lane, tray, linked, partner);
                return;
            }

            // Immediate feedback: the released trays leave the belt NOW, and the belt / reveal / lock beats play at once
            // too; the bottles the trays take from the pick zone fly as they are listed.
            var advanced = new Dictionary<int, (int steps, List<TrayLook> tails)>();
            foreach (var f in result.Facts)
            {
                switch (f)
                {
                    case TrayPlaced t:
                        int id = ++_nextTray;
                        _trayInSlot[t.Slot] = id;
                        _cells[id] = 0;
                        _laneTaken[t.Lane]++;
                        _board.PlaceTray(id, t.Lane, t.Slot).Forget();
                        break;
                    case LaneAdvanced a:
                        var step = advanced.TryGetValue(a.Lane, out var st) ? st : (0, new List<TrayLook>());
                        if (_laneShown[a.Lane] < _level.Lanes[a.Lane].Count) step.Item2.Add(LookOf(a.Lane, _laneShown[a.Lane]++));
                        advanced[a.Lane] = (step.Item1 + 1, step.Item2);
                        break;
                    case TrayRevealed _:
                    case TrayLockTicked _:
                        break;                                     // below, once the belt has moved
                }
            }
            foreach (var kv in advanced)
            {
                int shownBefore = _laneShown[kv.Key] - kv.Value.tails.Count;
                _board.AdvanceLane(kv.Key, kv.Value.steps, kv.Value.tails).Forget();
                for (int t = shownBefore; t < _laneShown[kv.Key]; t++) LinkIfShown(kv.Key, t);
            }
            foreach (var f in result.Facts)
            {
                switch (f)
                {
                    case TrayRevealed r:
                        _board.RevealLaneTray(r.Lane, r.Tray - _laneTaken[r.Lane], r.Color.ToTint()).Forget();
                        break;
                    case TrayLockTicked k when k.Remaining > 0:
                        _board.SetTrayLock(k.Lane, k.Tray - _laneTaken[k.Lane], LockLabel(k.Remaining)).Forget();
                        break;
                    case TrayLockTicked k:
                        _board.UnlockTray(k.Lane, k.Tray - _laneTaken[k.Lane]).Forget();
                        break;
                }
            }
            PlayBelt(result.Facts);
        }

        // ── the belt ─────────────────────────────────────────────────────────────────────────
        /// <summary>The fixed gameplay tick (gate open): the oval moves one row every 1 / BeltRowsPerSecond seconds; the
        /// board draws it in between.</summary>
        private void OnTick(float dt)
        {
            if (_board != null) _board.PlaceInFrontOf(_layers.GetCamera(RenderLayers.GamePlay), _viewport.SafeRect.height * 0.5f);   // the view may resize
            if (_game == null || _board == null || _ending || _game.Status != GameStatus.Playing) return;
            float interval = 1f / DesignTokens.Motion.BeltRowsPerSecond;
            _beltTime += dt;
            while (_beltTime >= interval && _game.Status == GameStatus.Playing)
            {
                _beltTime -= interval;
                PlayBelt(_game.Step());
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
                        _board.FlyBottle(p.Row, p.Track, tray, _cells[tray]++, flights++ * DesignTokens.Motion.PickStagger);
                        break;
                    case BottleFed d:
                        _board.FeedBottle(d.Feeder, d.Track, d.Row);
                        break;
                    case TrayPacked k:
                        _packs.Add(_board.PackTray(_trayInSlot[k.Slot], k.Color.ToTint()).Preserve());
                        break;
                    case SlotsRanOut _:
                        OfferSlotAsync(rescue: true, _roundCts.Token).Forget();
                        break;
                    case LevelCompleted _:
                        _wallet.Grant(ResourceKeys.Coins, _config.Get(GameConfigKeys.EconomyWinReward), GrantSource.Reward);
                        RefreshCoins();
                        _ending = true;
                        EndRoundAsync(f, _roundCts.Token).Forget();
                        break;
                    case LevelFailed _:
                        _ending = true;
                        EndRoundAsync(f, _roundCts.Token).Forget();
                        break;
                }
            }
        }

        /// <summary>"Not this one": the tray shakes — with its partner, when it is linked and the partner is on the belt.</summary>
        private void Shake(int lane, int tray, bool linked, TrayRef partner)
        {
            _board.ShakeTray(lane, tray - _laneTaken[lane]).Forget();
            if (linked && OnBelt(partner.Lane, partner.Index)) _board.ShakeTray(partner.Lane, partner.Index - _laneTaken[partner.Lane]).Forget();
        }

        /// <summary>How authored tray <paramref name="tray"/> of <paramref name="lane"/> looks right now (R17, R18).</summary>
        private TrayLook LookOf(int lane, int tray)
        {
            int locked = _game.IsAtFront(lane, tray) ? _game.LockLeft(lane) : _game.LockTurns(lane, tray);
            bool hidden = _game.IsTrayHidden(lane, tray);
            return new TrayLook(hidden ? TintFlavor.None : _game.TrayColor(lane, tray).ToTint(), hidden,
                locked > 0 ? LockLabel(locked) : null);
        }

        private string LockLabel(int turns) => _loc.Get(LocKeys.GameplayLockTurns, turns);

        private bool OnBelt(int lane, int tray) => tray >= _laneTaken[lane] && tray < _laneShown[lane];

        /// <summary>A tray just came onto the belt: tie it to its partner if that one is on the belt too (R19).</summary>
        private void LinkIfShown(int lane, int tray)
        {
            if (!_game.TryPartner(lane, tray, out var p) || !OnBelt(p.Lane, p.Index) || !OnBelt(lane, tray)) return;
            _board.LinkTrays(lane, tray - _laneTaken[lane], p.Lane, p.Index - _laneTaken[p.Lane]);
        }

        // ── extra slots (R20) ───────────────────────────────────────────────────────────────
        /// <summary>A locked slot was tapped: offer it (Parking Slot). Ignored while another offer is up or the round is over.</summary>
        private void OnLockedSlotTapped(int slot)
        {
            if (_game == null || _game.Status != GameStatus.Playing || _ending || _offering || _game.SlotsRanOut) return;
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
            }
        }

        /// <summary>The first time the game runs, the wallet has never held coins: give the starting balance once.</summary>
        private void GrantStarterCoins()
        {
            var wallet = _userData.Get<WalletModel>();
            if (wallet != null && wallet.Balances.ContainsKey(ResourceKeys.Coins.Value)) return;
            _wallet.Grant(ResourceKeys.Coins, _config.Get(GameConfigKeys.EconomyStartCoins), GrantSource.Reward);
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
                new ResultArgs(won, _catalog.Normalize(_levelIndex) + 1), default, ct);
            // A torn-down round (Home, Restart, scene change) aborts the popup — nothing more to do.
            if (ct.IsCancellationRequested || result.Reason == DialogCloseReason.Aborted || result.Reason == DialogCloseReason.CloseAll)
                return;
            if (result.Reason == DialogCloseReason.BackButton) { GoHome(); return; }
            _log.Info($"[GameplayScreen] {(won ? "next" : "restart")} after {_level?.Id}.");
            StartRound(won ? _levelIndex + 1 : _levelIndex);
        }

        private static float[] Floats(double[] v)
        {
            var f = new float[v.Length];
            for (int i = 0; i < v.Length; i++) f[i] = (float)v[i];
            return f;
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
