namespace Game.Views
{
    /// <summary>
    /// How one belt tray looks, in View primitives (GDD R17, R18, R21): its flavour, or the hidden "?" look, the lock
    /// count text when it is locked, its container size and its item count. The controller decides them all; the board only draws them.
    /// </summary>
    public readonly struct TrayLook
    {
        public readonly TintFlavor Color;
        public readonly bool Hidden;
        /// <summary>The lock's count, already localized; null = not locked.</summary>
        public readonly string LockLabel;
        /// <summary>Container size 1 S · 2 M · 3 L · 4 XL (R21) — which container model is drawn.</summary>
        public readonly int Size;
        /// <summary>How many items the container takes, already localized — shown on it on the belt too; null = none.</summary>
        public readonly string CountLabel;

        public TrayLook(TintFlavor color, bool hidden = false, string lockLabel = null, int size = 1, string countLabel = null)
        {
            Color = color; Hidden = hidden; LockLabel = lockLabel; Size = size; CountLabel = countLabel;
        }

        public bool Locked => LockLabel != null;
    }
}
