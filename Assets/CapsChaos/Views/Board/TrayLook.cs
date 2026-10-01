namespace Game.Views
{
    /// <summary>
    /// How one belt tray looks, in View primitives (GDD R17–R18): its flavour, or the hidden "?" look, plus the lock
    /// count text when it is locked. The controller decides all three; the board only draws them.
    /// </summary>
    public readonly struct TrayLook
    {
        public readonly TintFlavor Color;
        public readonly bool Hidden;
        /// <summary>The lock's count, already localized; null = not locked.</summary>
        public readonly string LockLabel;

        public TrayLook(TintFlavor color, bool hidden = false, string lockLabel = null)
        {
            Color = color; Hidden = hidden; LockLabel = lockLabel;
        }

        public bool Locked => LockLabel != null;
    }
}
