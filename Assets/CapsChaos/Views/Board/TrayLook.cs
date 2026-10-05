namespace Game.Views
{
    /// <summary>
    /// How one belt tray looks, in View primitives (GDD R17, R18, R21): its flavour, or the hidden "?" look, the lock
    /// count text when it is locked, and its container size. The controller decides them all; the board only draws them.
    /// </summary>
    public readonly struct TrayLook
    {
        public readonly TintFlavor Color;
        public readonly bool Hidden;
        /// <summary>The lock's count, already localized; null = not locked.</summary>
        public readonly string LockLabel;
        /// <summary>Container size 1 S · 2 M · 3 L · 4 XL (R21) — which container model is drawn.</summary>
        public readonly int Size;

        public TrayLook(TintFlavor color, bool hidden = false, string lockLabel = null, int size = 1)
        {
            Color = color; Hidden = hidden; LockLabel = lockLabel; Size = size;
        }

        public bool Locked => LockLabel != null;
    }
}
