namespace Game.Domain
{
    /// <summary>
    /// A past-tense fact the rules produced (rule #1: facts, never commands). A tap returns them IN THE
    /// ORDER THEY HAPPENED — a tap or a belt step returns them; the presentation plays them as animation
    /// (GDD §5.2 R9) — the Domain has already moved on.
    /// </summary>
    public abstract class GameFact { }

    /// <summary>The front tray of <see cref="Lane"/> moved into <see cref="Slot"/>.</summary>
    public sealed class TrayPlaced : GameFact
    {
        public int Lane { get; } public int Slot { get; } public CapColor Color { get; }
        /// <summary>Items the tray takes before it is full (R21: its size × the level's trayCapacity).</summary>
        public int Capacity { get; }
        public TrayPlaced(int lane, int slot, CapColor color, int capacity) { Lane = lane; Slot = slot; Color = color; Capacity = capacity; }
    }

    /// <summary>A conveyor stepped one tray forward (R7); <see cref="Remaining"/> trays are left on it.</summary>
    public sealed class LaneAdvanced : GameFact
    {
        public int Lane { get; } public int Remaining { get; }
        public LaneAdvanced(int lane, int remaining) { Lane = lane; Remaining = remaining; }
    }

    /// <summary>Hidden tray <see cref="Tray"/> (authored index) of <see cref="Lane"/> reached the front and turned out
    /// to be <see cref="Color"/> (R17).</summary>
    public sealed class TrayRevealed : GameFact
    {
        public int Lane { get; } public int Tray { get; } public CapColor Color { get; }
        public TrayRevealed(int lane, int tray, CapColor color) { Lane = lane; Tray = tray; Color = color; }
    }

    /// <summary>The locked front tray of <see cref="Lane"/> counted a placement down; <see cref="Remaining"/> more
    /// to go, 0 = it is unlocked (R18).</summary>
    public sealed class TrayLockTicked : GameFact
    {
        public int Lane { get; } public int Tray { get; } public int Remaining { get; }
        public TrayLockTicked(int lane, int tray, int remaining) { Lane = lane; Tray = tray; Remaining = remaining; }
    }

    /// <summary>The bottle on belt row <see cref="Row"/>, track <see cref="Track"/> passed the pick zone and flew to
    /// <see cref="Slot"/>'s tray (R2, R10); its spot is empty now.</summary>
    public sealed class BottlePicked : GameFact
    {
        public int Row { get; } public int Track { get; } public int Slot { get; } public CapColor Color { get; }
        public BottlePicked(int row, int track, int slot, CapColor color) { Row = row; Track = track; Slot = slot; Color = color; }
    }

    /// <summary>The next bottle of <see cref="Feeder"/>'s track <see cref="Track"/> stepped onto the oval, into the empty
    /// spot of belt row <see cref="Row"/> passing its merge point (R4).</summary>
    public sealed class BottleFed : GameFact
    {
        public int Feeder { get; } public int Track { get; } public int Row { get; } public CapColor Color { get; }
        public BottleFed(int feeder, int track, int row, CapColor color) { Feeder = feeder; Track = track; Row = row; Color = color; }
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

    /// <summary>The left-most locked slot <see cref="Slot"/> was opened (R20).</summary>
    public sealed class SlotUnlocked : GameFact
    {
        public int Slot { get; }
        public SlotUnlocked(int slot) { Slot = slot; }
    }

    /// <summary>Locked slot <see cref="Slot"/> counted the trays that just flew to the slots down (R22); <see cref="Remaining"/>
    /// more to go, 0 = it is open and takes trays from the next tap on.</summary>
    public sealed class SlotLockTicked : GameFact
    {
        public int Slot { get; } public int Remaining { get; }
        public SlotLockTicked(int slot, int remaining) { Slot = slot; Remaining = remaining; }
    }

    /// <summary>The open slots ran out — nothing can move without another slot — while an extra slot can still be
    /// unlocked (R20). The round is not lost: it waits for an unlock or a restart.</summary>
    public sealed class SlotsRanOut : GameFact { }

    /// <summary>The belt and its feeders are empty and the last box has shipped (R14).</summary>
    public sealed class LevelCompleted : GameFact { }

    public enum FailReason
    {
        /// <summary>R15: every slot holds a tray, no bottle on the belt matches any of them and no feeder can move.</summary>
        SlotsJammed,
        /// <summary>A slot is free but no tap can ever be accepted again: every front tray is locked, waits for its
        /// link partner, or needs more free slots than there are — or (defensive) an unbalanced level ran dry.</summary>
        NoMovesLeft,
    }

    public sealed class LevelFailed : GameFact
    {
        public FailReason Reason { get; }
        public LevelFailed(FailReason reason) { Reason = reason; }
    }
}
