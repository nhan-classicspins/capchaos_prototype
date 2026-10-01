using System;
using System.Linq;
using VContainer;
using ClassicSpins.PrototypeFramework.Composition;
using Game.Presentation;
using Game.Views;

namespace Game.Composition
{
    /// <summary>
    /// The Main screen's scope (&lt;X&gt;ScreenScope : SceneLifetimeScope). Sits on the
    /// Main.unity screen scene's single scope GameObject and registers MainScreen as the scope's
    /// entry screen. The parent (Root) and the typed MainParam are supplied by the scene service when
    /// the scene loads. It lives in Game.Composition — the only SKU tier that names a container
    /// type — while MainScreen lives in Game.Presentation. Scaffolded by Scaffold.Sync
    /// (generate-once).
    /// </summary>
    public sealed class MainScreenScope : SceneLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // The level-select panel is authored in Main.unity (a LevelSelectWidget prefab instance under an
            // edit-time preview canvas); hand the scene's instance to the widget, which re-hosts it on load.
            var levelSelect = gameObject.scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<LevelSelectView>(true))
                .FirstOrDefault(v => v != null)
                ?? throw new InvalidOperationException("Main.unity has no LevelSelectView — add the LevelSelectWidget prefab instance.");
            builder.RegisterInstance(levelSelect);

            builder.RegisterEntryScreen<MainScreen>();
            builder.Register<LevelSelectWidget>(Lifetime.Scoped).AsSelf();
        }
    }
}
