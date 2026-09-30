using System.Linq;
using VContainer;
using ClassicSpins.PrototypeFramework.Composition;
using Game.Application;
using Game.Infrastructure;
using Game.Presentation;

namespace Game.Composition
{
    /// <summary>
    /// The Gameplay screen's scope, on the Gameplay.unity scene (loaded ADDITIVELY over Master by the scene
    /// service). Wires the level source adapter, the catalog, the scene's WorldRoot and the entry screen.
    /// </summary>
    public sealed class GameplayScreenScope : SceneLifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.Register<ILevelSource, ResourcesLevelSource>(Lifetime.Scoped);
            builder.Register<LevelCatalog>(Lifetime.Scoped);
            // Scaffold.Sync guarantees a root "WorldRoot" node in every screen scene (framework has no API for it)
            var worldRoot = gameObject.scene.GetRootGameObjects().First(g => g.name == "WorldRoot").transform;
            builder.RegisterInstance(new GameplaySceneRoot(worldRoot));
            builder.RegisterEntryScreen<GameplayScreen>();
        }
    }
}
