using System.Linq;
using VContainer;
using VContainer.Unity;
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

            // the floor is authored in Gameplay.unity (Floor prefab instance under WorldRoot); the board frames it
            var floor = gameObject.scene.GetRootGameObjects()
                .Select(g => g.GetComponentInChildren<BoardFloorView>(true))
                .FirstOrDefault(v => v != null)
                ?? throw new System.InvalidOperationException("Gameplay.unity has no BoardFloorView — add the Floor prefab instance under WorldRoot.");
            builder.RegisterInstance(floor);
            builder.Register<GameplayHudWidget>(Lifetime.Scoped).AsSelf();

            // the fixed tick (rule #15): the framework's gate + GameplayTickDriver, pumping the belt clock
            GameplayScopeInstaller.Install(builder);
            builder.Register<BeltClock>(Lifetime.Scoped).AsSelf().As<ClassicSpins.PrototypeFramework.Application.IGameStep>();
            // the gameplay's own clock (GameSpeed): advanced every frame, read by the belt and every board tween
            builder.Register<GameTime>(Lifetime.Scoped);
            builder.RegisterEntryPoint<GameTimePump>();

            // the Win / Lose popup: the dialog service resolves its controller from this (the active) scope
            builder.Register<ResultDialog>(Lifetime.Transient);
            // the two "one more slot" offers (R20)
            builder.Register<ParkingSlotDialog>(Lifetime.Transient);
            builder.Register<OutOfSlotDialog>(Lifetime.Transient);
            builder.RegisterEntryScreen<GameplayScreen>();
        }
    }
}
