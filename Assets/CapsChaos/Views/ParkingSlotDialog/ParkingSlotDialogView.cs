using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// "Parking Slot" (GDD R20): the player tapped a locked slot — open it now for FREE (an ad) or for coins; the red
    /// ✕ declines. The card shows the slot icon with a green plus. Layout and behaviour: <see cref="SlotOfferView"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ParkingSlotDialogView : SlotOfferView { }
}
