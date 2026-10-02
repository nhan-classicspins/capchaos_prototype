using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Gen;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// "Out of Slot" (GDD R20): the open slots ran out while an extra slot can still be unlocked. Offers it for a
    /// rewarded ad or <see cref="SlotOfferArgs.Price"/> coins; Restart declines (the caller restarts the level).
    /// A modal dialog — the service raises the dim and the input blocker.
    /// </summary>
    public sealed class OutOfSlotDialog : DialogBase<SlotOfferChoice>
    {
        private readonly ILocalizationService _loc;
        private readonly UiPaletteProvider _palette;
        private OutOfSlotDialogView _view;

        public OutOfSlotDialog(IAssetService assets, ILocalizationService loc, UiPaletteProvider palette) : base(assets)
        {
            _loc = loc;
            _palette = palette;
        }

        public override async UniTask OnCreateAsync(DialogArgs args, CancellationToken ct)
        {
            var offer = args as SlotOfferArgs ?? new SlotOfferArgs(0, false);
            _view = await LoadViewAsync<OutOfSlotDialogView>(AssetKeys.OutOfSlotDialog, ct);
            _palette.ApplyTo(_view.gameObject);
            _view.SetTexts(_loc.Get(LocKeys.OutOfSlotTitle), _loc.Get(LocKeys.OutOfSlotBody),
                _loc.Get(LocKeys.OfferFree), _loc.Get(LocKeys.OfferPrice, offer.Price));
            _view.SetRestartLabel(_loc.Get(LocKeys.OutOfSlotRestart));
            _view.SetPriceAffordable(offer.CanAfford);
            _view.FreeClicked += OnFree;
            _view.CoinsClicked += OnCoins;
            _view.CloseRequested += OnRestart;
        }

        private void OnFree() => Close(SlotOfferChoice.WatchAd);
        private void OnCoins() => Close(SlotOfferChoice.PayCoins);
        private void OnRestart() => Close(SlotOfferChoice.Declined);

        public override void OnDispose()
        {
            if (_view == null) return;
            _view.FreeClicked -= OnFree;
            _view.CoinsClicked -= OnCoins;
            _view.CloseRequested -= OnRestart;
        }
    }
}
