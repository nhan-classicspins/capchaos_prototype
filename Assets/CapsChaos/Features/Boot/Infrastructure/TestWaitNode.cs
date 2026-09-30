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
    /// A deliberately artificial boot node: it does nothing but hold boot for
    /// <see cref="WaitFor"/> so the Loading scene is on screen long enough to be seen and the boot
    /// ordering is observable. Delete it (and its <see cref="GameBootCaps.TestWaitDone"/> edge on the
    /// first-scene node) when real boot work replaces it.
    /// </summary>
    /// <remarks>
    /// <b><see cref="Requires"/> = { LoadingSceneShown }</b> so the wait starts only once there is
    /// something to look at, and it publishes <see cref="GameBootCaps.TestWaitDone"/> — the cap the
    /// first-scene node is gated on, which is what makes "Loading for 2s, then Main" deterministic
    /// rather than a race against however long the rest of boot happens to take.
    /// </remarks>
    public sealed class TestWaitNode : IBootNode
    {
        /// <summary>How long the node holds boot.</summary>
        public static readonly TimeSpan WaitFor = TimeSpan.FromSeconds(2);

        private readonly IReadOnlyList<BootCap> _requires = new[] { GameBootCaps.LoadingSceneShown };
        private readonly IReadOnlyList<BootCap> _provides = new[] { GameBootCaps.TestWaitDone };

        private readonly ILog _log;

        public TestWaitNode(ILog log = null) => _log = log ?? new NullLog();

        public string Id => "TestWait";
        public IReadOnlyList<BootCap> Requires => _requires;
        public IReadOnlyList<BootCap> Provides => _provides;
        public bool Required => false; // a test stub must never be able to abort boot

        // Zero timeouts: the wait IS the work, so a soft timeout would just cut it short.
        public TimeSpan SoftTimeout => TimeSpan.Zero;
        public TimeSpan HardTimeout => TimeSpan.Zero;

        public Func<BootContext, bool> Predicate => null;
        public string ExclusiveGroup => "";

        // Heavier than the 1f local nodes: it owns most of boot's wall-clock, so the Framework/Boot
        // Graph preview draws it at a length that matches what the player actually waits for.
        public float Weight => 2f;

        public async UniTask<BootNodeResult> RunAsync(BootContext ctx, CancellationToken ct)
        {
            _log.Info("[TestWait] holding boot for " + WaitFor.TotalSeconds + "s …");
            await UniTask.Delay(WaitFor, DelayType.UnscaledDeltaTime, cancellationToken: ct);
            _log.Info("[TestWait] done.");
            return BootNodeResult.Succeeded;
        }
    }
}
