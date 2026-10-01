using System;
using UnityEngine;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Composition;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Application;
using Game.Infrastructure;
using ClassicSpins.PrototypeFramework.Infrastructure;
using ClassicSpins.PrototypeFramework.Presentation;
using Game.Gen;

namespace Game.Composition
{
    /// <summary>
    /// The CapsChaos SKU's concrete FrameworkSettings — the SINGLE registrant of the SKU-owned config the
    /// package cannot know: the real FirstSceneConfig target and the bundled ConfigDefaults. Emitted
    /// once by the Setup Wizard (generate-once) — this file is yours now; grow it with the SKU.
    /// </summary>
    [CreateAssetMenu(fileName = "CapsChaosFrameworkSettings", menuName = "CapsChaos/Framework Settings")]
    public sealed class CapsChaosFrameworkSettings : FrameworkSettings
    {
        protected override void InstallGameConfig(IContainerBuilder builder)
        {
            // The real FirstSceneConfig (the framework registered FirstSceneConfig.None; this wins by
            // later registration): boot lands on Main — the level list, so any level is one tap away while
            // levels are being tuned — loaded additively over Master like every screen. The Title screen
            // (GDD §7) will take this spot later.
            builder.RegisterInstance(new FirstSceneConfig(
                SceneKeys.Main,
                (sceneService, ct) => sceneService.LoadAsync(
                    SceneKeys.Main, new MainParam(ColdBoot: true), SceneTransition.Replace, null, ct)));

            // The SKU owns the single ConfigDefaults registration (the framework registers none —
            // zero registrants means every Get falls back to 0; two is a VContainer duplicate conflict).
            builder.RegisterInstance(ConfigDefaults.Empty);

            InstallBoot(builder);
        }

        /// <summary>
        /// The SKU's own boot graph additions: show the Loading scene, load every level, hold boot on a test
        /// node, then let the (unchanged) first-scene load put the first screen up and take the Loading scene
        /// back down.
        /// </summary>
        /// <remarks>
        /// Boot nodes are collected by the framework's <c>BootRunInstaller</c> from every registered
        /// <see cref="IBootNode"/>, and SKU installers run last, so these land in the graph with no
        /// framework-code edit. The order is expressed purely as capability edges (AD-4):
        /// <c>AssetReady → LoadingScene → TestWait → FirstScene</c> and, in parallel,
        /// <c>AssetReady → LevelConfig → FirstScene</c>. The decorator adds exactly TWO SKU edges in front
        /// of the framework's FirstScene node — <c>GameBootCaps.TestWaitDone</c> and
        /// <c>GameBootCaps.LevelsLoaded</c> — and nothing else; see the comment at the override for why the
        /// monetization caps are not there.
        /// </remarks>
        private static void InstallBoot(IContainerBuilder builder)
        {
            // AsSelf() keeps `Resolve<LoadingSceneHost>()` working (naming any contract with As<> would
            // otherwise drop the concrete one). As<IDisposable>() is the LAST-RESORT backstop only: it
            // fires when the Root scope is actually disposed, which for a session-long RootLifetimeScope
            // means play-mode exit — it catches a leaked handle, NOT an aborted boot. The abort case is
            // closed by the entry-point exception handler below.
            builder.Register<LoadingSceneHost>(Lifetime.Singleton).AsSelf().As<IDisposable>();
            builder.Register<LoadingSceneNode>(Lifetime.Singleton).As<IBootNode>();
            builder.Register<TestWaitNode>(Lifetime.Singleton).As<IBootNode>();

            // Levels load during the Loading stage, into a Root singleton every screen scope inherits.
            builder.Register<LevelCatalog>(Lifetime.Singleton);
            builder.Register<LevelConfigNode>(Lifetime.Singleton).As<IBootNode>();

            // The boot-abort teardown, for real. A Required boot node failing makes BootFlow throw
            // BootAbortedException; it propagates out of BootstrapEntryPoint.StartAsync into VContainer's
            // entry-point dispatcher, which does NOT dispose the container — and RootLifetimeScope lives
            // for the whole session, so IDisposable alone would not run until play-mode exit, when the
            // scene is unloading anyway. Without this hook an opaque, raycast-blocking overlay sits over
            // the aborted boot with no way down.
            //
            // What VContainer does instead: it hands the faulted IAsyncStartable task to the registered
            // entry-point exception handler (UniTask's Forget(handler) switches to the main thread before
            // invoking it, and the container is still alive), so this is the hook that fires AT abort
            // time. It needs no framework change — the framework registers no handler, and SKU installers
            // run last, so this registration wins by later-registration.
            //
            // Winning means SHADOWING VContainer's own Debug.LogException default. That is what the
            // re-log below is for: delete it and every entry-point exception in the SKU vanishes.
            IObjectResolver rootResolver = null;
            builder.RegisterBuildCallback(resolver => rootResolver = resolver);
            builder.RegisterEntryPointExceptionHandler(ex =>
            {
                Debug.LogException(ex); // MANDATORY: this handler replaces VContainer's default logger
                try
                {
                    // TryResolve, never Resolve: an abort before LoadingScene ran leaves nothing to hide,
                    // and a throw from inside this handler is only republished as an unobserved task
                    // exception — a swallowed teardown failure would be invisible.
                    if (rootResolver != null && rootResolver.TryResolve<LoadingSceneHost>(out var host))
                        host.HideAsync().Forget();
                }
                catch (Exception teardown)
                {
                    Debug.LogWarning("[Boot] Loading-scene teardown after an entry-point exception failed: "
                        + teardown.Message);
                }
            });

            // The AD-17 seam: swap the framework FirstScene node for the same node behind the
            // TestWaitDone + LevelsLoaded edges, with the Loading-scene teardown appended. The load itself is untouched —
            // it still runs the FirstSceneConfig delegate registered above.
            //
            // Deliberately ONLY SKU caps — never the framework's AdsReady / AnalyticsReady caps. The
            // framework's AdsInitNode documents that "FirstScene never waits" on AdsReady, and AdsInit
            // itself requires the ConsentResolved cap: an AdsReady edge here would make FirstScene a
            // TRANSITIVE dependent of ConsentResolved, so the mid-session ConsentRerunDriver's
            // RerunFromAsync(ConsentResolved) — whose rerun set is transitively closed — would hard-reload
            // Main under the player. It would also kill boot with a BootGraphException in any SKU that
            // drops the Ads/Analytics nodes. Add SKU caps here; never framework monetization caps.
            builder.Register(resolver => BootNodeOverride.Replace(
                    "FirstScene",
                    new GatedFirstSceneNode(
                        new FirstSceneNode(
                            resolver.Resolve<ISceneService>(),
                            resolver.Resolve<FirstSceneConfig>(),
                            resolver.TryResolve<ILog>(out var log) ? log : null),
                        resolver.Resolve<LoadingSceneHost>(),
                        GameBootCaps.TestWaitDone,
                        GameBootCaps.LevelsLoaded)),
                Lifetime.Singleton);
        }
    }
}
