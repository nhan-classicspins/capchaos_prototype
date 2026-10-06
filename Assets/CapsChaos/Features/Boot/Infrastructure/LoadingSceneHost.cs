using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using ClassicSpins.PrototypeFramework.Application;
using Game.Presentation;
using Game.Views;

namespace Game.Infrastructure
{
    /// <summary>
    /// Owns the boot-time Loading scene: one additive Addressables load at the front of the boot graph
    /// and one unload once the first screen is in. A plain ctor-injected service (never a MonoBehaviour)
    /// so the two boot nodes that drive it stay dumb.
    /// </summary>
    /// <remarks>
    /// The scene is addressed <c>Scenes/Loading</c> — the same <c>Scenes/</c> prefix the framework's
    /// <c>SceneService</c> uses for screens — but it is deliberately <b>not</b> a screen: it has no
    /// <c>LifetimeScope</c>, no <c>IScreen</c> and no param, so it never enters the scene back-stack and
    /// is unreachable by the back router. Its canvas is Screen-Space-Overlay, so it composites after
    /// every camera and covers the whole render rig (up to and including the Overlay layer at depth 30)
    /// with no camera of its own. It is the SKU's own boot cover — the framework ships none.
    /// <para>Cosmetic by contract: a failed load is warned and swallowed — boot must never abort because
    /// a loading screen is missing.</para>
    /// <para><b>Three teardown paths, on purpose.</b> (1) The happy one: <c>GatedFirstSceneNode</c>'s
    /// <c>finally</c>, once the first screen is in. (2) The <b>aborted boot</b>: a <c>Required</c> boot
    /// node failing makes the scheduler throw <c>BootAbortedException</c>, so <c>FirstScene</c> never runs
    /// at all — VContainer routes that fault to the entry-point exception handler the SKU's
    /// <c>InstallBoot</c> registers, and that handler calls <see cref="HideAsync"/>. Without it an opaque,
    /// raycast-blocking overlay would be stranded over the game with no way down. (3) <see
    /// cref="IDisposable"/>, registered via <c>.AsSelf().As&lt;IDisposable&gt;()</c>, as the last-resort
    /// backstop for a leaked handle. Path 3 is <b>not</b> the abort fix: the Root scope is only disposed
    /// at play-mode exit, by which point the scene is unloading anyway.</para>
    /// </remarks>
    /// <remarks>Also the in-game <see cref="ILoadingCover"/> (2026-10-06): the screens raise the same scene over a level
    /// load, label set by them, and it fades out (<see cref="LoadingScreenView"/>) before it unloads — at boot too.</remarks>
    public sealed class LoadingSceneHost : IDisposable, ILoadingCover
    {
        /// <summary>The addressable address of the Loading scene.</summary>
        public const string Address = "Scenes/Loading";

        private readonly ILog _log;
        private SceneInstance? _scene;
        private LoadingScreenView _view;

        public LoadingSceneHost(ILog log = null) => _log = log ?? new NullLog();

        /// <summary>True while the Loading scene is loaded.</summary>
        public bool IsShown => _scene.HasValue;

        /// <summary>Additively load the Loading scene. Returns false when it could not be shown (warned,
        /// never thrown) so the caller can report Degraded instead of failing boot.</summary>
        public async UniTask<bool> ShowAsync(CancellationToken ct)
        {
            if (_scene.HasValue) return true;

            // The handle is held in a LOCAL the catch blocks can see: AttachExternalCancellation abandons
            // the AWAIT, it does not cancel the underlying Addressables load. On the node's hard timeout
            // the load therefore completes anyway and ACTIVATES the scene while `_scene` stays null — so
            // HideAsync would no-op and the overlay would be stranded with no handle left to take it down.
            // Releasing on every non-success exit is what closes that strand. It is NOT an immediate
            // unload: an in-flight operation sits at ref count 2 (one from construction, one Start() takes
            // "until the operation completes"), so Release takes 2 → 1 and the scene actually comes down
            // when the load finishes and drops the last reference. Deferred — but no longer stranded.
            var handle = default(AsyncOperationHandle<SceneInstance>);
            try
            {
                handle = Addressables.LoadSceneAsync(Address, LoadSceneMode.Additive);
                _scene = await handle.Task.AsUniTask().AttachExternalCancellation(ct);
                return true;
            }
            catch (OperationCanceledException)
            {
                ReleaseQuietly(ref handle);
                throw; // boot cancellation / hard timeout — let the scheduler classify it
            }
            catch (Exception ex)
            {
                // Same leak on a plain failure: LoadSceneAsync may have handed back a valid handle before
                // faulting (or the await failed after activation), so release before swallowing.
                ReleaseQuietly(ref handle);
                _log.Warn("[LoadingScene] could not show '" + Address + "': " + ex.Message);
                return false;
            }
        }

        /// <summary>ILoadingCover: the Loading scene up (loaded if it is not), reading <paramref name="label"/>, and
        /// one frame rendered — so the work the caller starts next runs under a cover already on screen.</summary>
        async UniTask ILoadingCover.ShowAsync(string label, CancellationToken ct)
        {
            if (!await ShowAsync(ct)) return;                                // cosmetic: no cover, the load goes on
            _view ??= FindView();
            if (_view != null)
            {
                _view.ShowNow();
                _view.SetLabel(label);
            }
            await UniTask.NextFrame(ct);
        }

        private LoadingScreenView FindView()
        {
            if (!_scene.HasValue) return null;
            foreach (var root in _scene.Value.Scene.GetRootGameObjects())
            {
                var view = root.GetComponentInChildren<LoadingScreenView>(true);
                if (view != null) return view;
            }
            return null;
        }

        /// <summary>Fade the Loading scene out, then unload it. Idempotent, and never throws — a stuck loading screen
        /// is worse than a logged unload warning.</summary>
        public async UniTask HideAsync()
        {
            if (!_scene.HasValue) return;
            var scene = _scene.Value;
            var view = _view ?? FindView();
            _scene = null;
            _view = null;
            try
            {
                if (view != null) await view.FadeOutAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _log.Warn("[LoadingScene] fade issue: " + ex.Message);
            }
            try
            {
                await Addressables.UnloadSceneAsync(scene).Task.AsUniTask();
            }
            catch (Exception ex)
            {
                _log.Warn("[LoadingScene] unload issue: " + ex.Message);
            }
        }

        /// <summary>Last-resort scope-teardown backstop (teardown path 3 in the remarks): close the scene
        /// if it somehow outlived both the <c>FirstScene</c> <c>finally</c> and the boot-abort handler.
        /// The Root scope is disposed at play-mode exit, so this is a handle/refcount hygiene net, NOT the
        /// aborted-boot fix. Dispose is synchronous and the unload is not, so the UniTask is
        /// fire-and-forget via <c>Forget()</c> — the codebase idiom for exactly this (HideAsync already
        /// swallows every failure, so nothing can go unobserved).</summary>
        public void Dispose()
        {
            if (!_scene.HasValue) return;
            HideAsync().Forget();
        }

        // Release a possibly-invalid handle without ever throwing out of a catch block.
        private void ReleaseQuietly(ref AsyncOperationHandle<SceneInstance> handle)
        {
            if (!handle.IsValid()) return;
            try
            {
                Addressables.Release(handle);
            }
            catch (Exception ex)
            {
                _log.Warn("[LoadingScene] release issue: " + ex.Message);
            }
            finally
            {
                handle = default;
            }
        }
    }
}
