namespace Game.Application
{
    /// <summary>
    /// The typed per-screen param for GameplayScreen: which level to play, as a position in
    /// <c>levels.index.json</c>'s order (GDD §6.1). Injected into the screen scope before Configure by the
    /// scene service. Engine-free — it is inside the headless gate.
    /// </summary>
    public sealed record GameplayParam(int LevelIndex);
}
