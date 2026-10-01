namespace Game.Domain
{
    /// <summary>
    /// A past-tense fact the rules produced (rule #1: facts, never commands). A tap returns them IN THE
    /// ORDER THEY HAPPENED; the presentation replays that list as animation (GDD §5.2 R9) — the Domain
    /// has already moved on.
    /// </summary>
    public abstract class GameFact { }

    /// <summary>The front tray of <see cref="Lane"/> moved into <see cref="Slot"/>.</summary>
    public sealed class TrayPlaced : GameFact
    {
        public int Lane { get; } public int Slot { get; } public CapColor Color { get; }
        public TrayPlaced(int lane, int slot, CapColor color) { Lane = lane; Slot = slot; Color = color; }
    }

    /// <summary>A conveyor stepped one tray forward (R7); <see cref="Remaining"/> trays are left on it.</summary>
    public sealed class LaneAdvanced : GameFact
    {
        public int Lane { get; } public int Remaining { get; }
        public LaneAdvanced(int lane, int remaining) { Lane = lane; Remaining = remaining; }
    }

    /// <summary>The ground bottle at (X, Z) flew to <see cref="Slot"/>'s tray (R10).</summary>
    public sealed class BottlePicked : GameFact
    {
        public int X { get; } public int Z { get; } public int Slot { get; } public CapColor Color { get; }
        public BottlePicked(int x, int z, int slot, CapColor color) { X = x; Z = z; Slot = slot; Color = color; }
    }

    /// <summary>The pile at (X, Z) dropped one level; <see cref="Height"/> bottles remain there (R3).</summary>
    public sealed class StackDropped : GameFact
    {
        public int X { get; } public int Z { get; } public int Height { get; }
        public StackDropped(int x, int z, int height) { X = x; Z = z; Height = height; }
    }

    /// <summary>A hidden bottle reached the ground at (X, Z) and turned out to be <see cref="Color"/> (R4).</summary>
    public sealed class BottleRevealed : GameFact
    {
        public int X { get; } public int Z { get; } public CapColor Color { get; }
        public BottleRevealed(int x, int z, CapColor color) { X = x; Z = z; Color = color; }
    }

    /// <summary>A bottle was capped into <see cref="Slot"/>'s tray, which now holds <see cref="Filled"/>.</summary>
    public sealed class BottleCapped : GameFact
    {
        public int Slot { get; } public int Filled { get; }
        public BottleCapped(int slot, int filled) { Slot = slot; Filled = filled; }
    }

    /// <summary><see cref="Slot"/>'s tray was full, got boxed and shipped; the slot is free (R13).</summary>
    public sealed class TrayPacked : GameFact
    {
        public int Slot { get; } public CapColor Color { get; }
        public TrayPacked(int slot, CapColor color) { Slot = slot; Color = color; }
    }

    /// <summary>The stack is empty and the last box has shipped (R14).</summary>
    public sealed class LevelCompleted : GameFact { }

    public enum FailReason
    {
        /// <summary>R15: every slot holds a tray and no exposed bottle matches any of them.</summary>
        SlotsJammed,
        /// <summary>Defensive: bottles remain but nothing can ever move (an unbalanced level; V4 prevents it).</summary>
        NoMovesLeft,
    }

    public sealed class LevelFailed : GameFact
    {
        public FailReason Reason { get; }
        public LevelFailed(FailReason reason) { Reason = reason; }
    }
}
