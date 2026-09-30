using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Application;

namespace Game.Infrastructure
{
    /// <summary>
    /// A thin decorator over the framework's <c>FirstScene</c> boot node: it adds SKU capability edges in
    /// front of it and tears the Loading scene down the moment the first screen is in. Installed through
    /// the AD-17 boot-customization seam (<c>BootNodeOverride.Replace("FirstScene", …)</c>) — zero
    /// framework-code edits.
    /// </summary>
    /// <remarks>
    /// <para><b>What it does not change.</b> Every other leg of the node is delegated verbatim to the
    /// framework instance, including the actual load — the first screen is still Main, loaded by the
    /// SKU's own <c>FirstSceneConfig</c> delegate, exactly as before. The decorator only widens
    /// <see cref="Requires"/> and appends the Loading-scene teardown.</para>
    /// <para><b>Why a gate rather than a parallel hide node.</b> The framework's <c>FirstScene</c> is a
    /// terminal leaf that provides nothing, so nothing can be scheduled <i>after</i> it. Hiding the
    /// Loading scene from a node that merely waits on <c>TestWaitDone</c> would race the Main load
    /// instead of following it. Gating puts the whole sequence on real capability edges:
    /// <c>LoadingScene → TestWait → FirstScene(load Main, hide Loading)</c>.</para>
    /// <para>The teardown runs in a <c>finally</c>: a cancelled or failed first-screen load must not
    /// strand the Loading scene on top of the game. It is not the ONLY teardown path, on purpose — a
    /// <c>Required</c> boot node failing aborts the graph (<c>BootAbortedException</c>) before this node
    /// ever runs, and the entry-point exception handler the SKU's <c>InstallBoot</c> registers is what
    /// closes the scene in that case (<c>LoadingSceneHost</c> is <c>IDisposable</c> too, but the Root
    /// scope survives an aborted boot, so that one only fires at play-mode exit).</para>
    /// </remarks>
    public sealed class GatedFirstSceneNode : IBootNode
    {
        private readonly IBootNode _inner;
        private readonly LoadingSceneHost _loading;
        private readonly IReadOnlyList<BootCap> _requires;

        public GatedFirstSceneNode(IBootNode inner, LoadingSceneHost loading, params BootCap[] extraRequires)
        {
            _inner = inner;
            _loading = loading;

            var requires = new List<BootCap>(inner.Requires);
            if (extraRequires != null)
                foreach (var cap in extraRequires)
                    if (!requires.Contains(cap))
                        requires.Add(cap);
            _requires = requires;
        }

        // Same Id as the node it replaces: Reconcile keys on Id, and the report/timeline stay readable.
        public string Id => _inner.Id;
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _inner.Provides;
        public bool Required => _inner.Required;
        public TimeSpan SoftTimeout => _inner.SoftTimeout;
        public TimeSpan HardTimeout => _inner.HardTimeout;
        public Func<BootContext, bool> Predicate => _inner.Predicate;
        public string ExclusiveGroup => _inner.ExclusiveGroup;
        public float Weight => _inner.Weight;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            try
            {
                return await _inner.RunAsync(ctx, ct);
            }
            finally
            {
                await _loading.HideAsync();
            }
        }
    }
}
