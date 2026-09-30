using ClassicSpins.PrototypeFramework.Domain;

namespace Game.Application
{
    /// <summary>
    /// The SKU's own boot capability keys. The framework's <c>BootCaps</c> set is pinned (AR-04) and the
    /// framework never invents a cap, but the <b>SKU owns its own boot edges</b> — these are the caps the
    /// SKU's boot nodes publish so the scheduler orders them capability-to-capability, never by node name.
    /// </summary>
    public static class GameBootCaps
    {
        /// <summary>The Loading scene is additively loaded and its TMP label is on screen.</summary>
        public static readonly BootCap LoadingSceneShown = new("LoadingSceneShown");

        /// <summary>The artificial test delay has elapsed (see <c>TestWaitNode</c>).</summary>
        public static readonly BootCap TestWaitDone = new("TestWaitDone");
    }
}
