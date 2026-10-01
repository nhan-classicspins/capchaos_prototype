using System.Linq;
using VContainer;
using ClassicSpins.PrototypeFramework.Composition;
using Game.Presentation;
using Game.Views;

namespace Game.Composition
{
    /// <summary>
    /// The Gameplay screen's scope, on the Gameplay.unity scene (loaded ADDITIVELY over Master by the scene
    /// service). Wires the scene's WorldRoot and the entry screen. The <c>LevelCatalog</c> is NOT registered
    /// here: it is a Root singleton, filled at boot by <c>LevelConfigNode</c>, and this scope inherits it.
    /// </summary>
    public sealed class GameplayScreenScope : SceneLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Scaffold.Sync guarantees a root "WorldRoot" node in every screen scene (framework has no API for it)
            var worldRoot = gameObject.scene.GetRootGameObjects().First(g => g.name == "WorldRoot").transform;
            builder.RegisterInstance(new GameplaySceneRoot(worldRoot));

            // The HUD (Restart / Home) is authored in Gameplay.unity under an edit-time preview canvas; the
            // widget re-hosts it under the Ui layer on load (see GameplayHudWidget).
            var hud = gameObject.scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<GameplayHudView>(true))
                .FirstOrDefault(v => v != null)
                ?? throw new System.InvalidOperationException("Gameplay.unity has no GameplayHudView — add the GameplayHudWidget prefab instance.");
            builder.RegisterInstance(hud);
            builder.Register<GameplayHudWidget>(Lifetime.Scoped).AsSelf();

            // the Win / Lose popup: the dialog service resolves its controller from this (the active) scope
            builder.Register<ResultDialog>(Lifetime.Transient);
            builder.RegisterEntryScreen<GameplayScreen>();
        }
    }
}
