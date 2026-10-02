using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Gen;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>What a slot offer costs in coins and whether the player has them.</summary>
    public sealed record SlotOfferArgs(int Price, bool CanAfford) : DialogArgs;

    /// <summary>How the player answered a slot offer. Paying is the caller's job: the dialog only reports the choice.</summary>
    public enum SlotOfferChoice
    {
        /// <summary>✕ on Parking Slot, Restart on Out of Slot.</summary>
        Declined,
        WatchAd,
        PayCoins,
    }

    /// <summary>
    /// "Parking Slot" (GDD R20): the player tapped a locked slot. Offers it for a rewarded ad or <see cref="SlotOfferArgs.Price"/>
    /// coins; ✕ declines. A modal dialog — the service raises the dim and the input blocker.
    /// </summary>
    public sealed class ParkingSlotDialog : DialogBase<SlotOfferChoice>
    {
        private readonly ILocalizationService _loc;
        private readonly UiPaletteProvider _palette;
        private ParkingSlotDialogView _view;

        public ParkingSlotDialog(IAssetService assets, ILocalizationService loc, UiPaletteProvider palette) : base(assets)
        {
            _loc = loc;
            _palette = palette;
        }

        public override async UniTask OnCreateAsync(DialogArgs args, CancellationToken ct)
        {
            var offer = args as SlotOfferArgs ?? new SlotOfferArgs(0, false);
            _view = await LoadViewAsync<ParkingSlotDialogView>(AssetKeys.ParkingSlotDialog, ct);
            _palette.ApplyTo(_view.gameObject);
            _view.SetTexts(_loc.Get(LocKeys.ParkingSlotTitle), _loc.Get(LocKeys.ParkingSlotBody),
                _loc.Get(LocKeys.OfferFree), _loc.Get(LocKeys.OfferPrice, offer.Price));
            _view.SetPriceAffordable(offer.CanAfford);
            _view.FreeClicked += OnFree;
            _view.CoinsClicked += OnCoins;
            _view.CloseRequested += OnDecline;
        }

        private void OnFree() => Close(SlotOfferChoice.WatchAd);
        private void OnCoins() => Close(SlotOfferChoice.PayCoins);
        private void OnDecline() => Close(SlotOfferChoice.Declined);

        public override void OnDispose()
        {
            if (_view == null) return;
            _view.FreeClicked -= OnFree;
            _view.CoinsClicked -= OnCoins;
            _view.CloseRequested -= OnDecline;
        }
    }
}
