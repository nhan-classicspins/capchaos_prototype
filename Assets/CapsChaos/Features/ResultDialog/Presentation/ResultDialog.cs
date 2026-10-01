using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Gen;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>What the round ended with, and which level it was (1-based, for the subtitle).</summary>
    public sealed record ResultArgs(bool Won, int LevelNumber) : DialogArgs;

    /// <summary>
    /// The Win / Lose popup (GDD §5.4, §7): "GOOD JOB" + NEXT after R14, "YOU CAN DO IT" + RESTART after R15 — one
    /// prefab, themed by <see cref="ResultArgs.Won"/>. A modal dialog: the service raises its dim and input blocker.
    /// It only reports that the player pressed the button (<c>Unit</c>); what NEXT / RESTART do is the screen's call,
    /// and every other close reason (Back, Aborted, CloseAll) is handled at the call site.
    /// </summary>
    public sealed class ResultDialog : DialogBase<Unit>
    {
        private readonly ILocalizationService _loc;
        private readonly UiPaletteProvider _palette;
        private ResultDialogView _view;

        public ResultDialog(IAssetService assets, ILocalizationService loc, UiPaletteProvider palette) : base(assets)
        {
            _loc = loc;
            _palette = palette;
        }

        public override async UniTask OnCreateAsync(DialogArgs args, CancellationToken ct)
        {
            var result = args as ResultArgs ?? new ResultArgs(Won: true, LevelNumber: 1);
            _view = await LoadViewAsync<ResultDialogView>(AssetKeys.ResultDialog, ct);
            _palette.ApplyTo(_view.gameObject);
            _view.SetLook(result.Won ? ResultLook.Win : ResultLook.Lose);
            if (result.Won)
                _view.SetTexts(_loc.Get(LocKeys.WinTitle), _loc.Get(LocKeys.WinSubtitle, result.LevelNumber), _loc.Get(LocKeys.WinNext));
            else
                _view.SetTexts(_loc.Get(LocKeys.LoseTitle), _loc.Get(LocKeys.LoseSubtitle), _loc.Get(LocKeys.LoseRestart));
            _view.CloseRequested += OnCloseRequested;
        }

        private void OnCloseRequested() => Close(Unit.Default);

        public override void OnDispose()
        {
            if (_view != null) _view.CloseRequested -= OnCloseRequested;
        }
    }
}
