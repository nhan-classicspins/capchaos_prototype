using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Application;

namespace Game.Presentation
{
    /// <summary>
    /// The Main screen controller (the three-file screen pattern: &lt;X&gt;Screen : ScreenBase). A
    /// plain ctor-injected controller — never a MonoBehaviour. It lives in Game.Presentation and
    /// ctor-injects its MainParam from Game.Application. Scaffolded by Scaffold.Sync
    /// (generate-once-never-overwrite): this file is yours now — add screen logic freely.
    /// </summary>
    public sealed class MainScreen : ScreenBase
    {
        private readonly MainParam _param;
        private readonly ILog _log;

        public MainScreen(MainParam param, ILog log = null)
        {
            _param = param;
            _log = log ?? new NullLog();
        }

        public override void OnEnter() => _log.Info("[MainScreen] entered.");

        public override void OnExit() => _log.Info("[MainScreen] exited.");
    }
}
