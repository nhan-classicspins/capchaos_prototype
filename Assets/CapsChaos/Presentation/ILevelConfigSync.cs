using System.Threading;
using Cysharp.Threading.Tasks;
using Game.Application;

namespace Game.Presentation
{
    /// <summary>
    /// Resyncs the level config from its live source (the level config sheet) into the <c>LevelCatalog</c>.
    /// Runs once as the game starts (after the bundled levels load) and again whenever the player asks (Main's
    /// Sync Config button). Never throws for a bad network or a bad level: the report says what happened, and every
    /// level it could not take keeps what it had. The next round played picks the new levels up.
    /// </summary>
    public interface ILevelConfigSync
    {
        UniTask<LevelSyncReport> SyncAsync(CancellationToken ct);
    }
}
