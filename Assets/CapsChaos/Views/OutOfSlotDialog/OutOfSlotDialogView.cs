using UnityEngine;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>
    /// "Out of Slot" (GDD R20): the open slots ran out and nothing can move — open one more for FREE (an ad) or for
    /// coins, or Restart the level (the underlined link, which is this popup's close). The card shows the slot bar:
    /// four trays and the locked "+" slots. Layout and behaviour: <see cref="SlotOfferView"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OutOfSlotDialogView : SlotOfferView
    {
        [SerializeField] private TextProxy _restartLabel;

        public void SetRestartLabel(string label)
        {
            if (_restartLabel != null) _restartLabel.SetText(label);
        }
    }
}
