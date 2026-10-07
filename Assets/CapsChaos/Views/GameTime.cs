using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Adapters;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// The gameplay's own clock (SKU owner, 2026-10-06): GAME seconds run <see cref="GameSpeed"/> times as fast as real
    /// ones. One per Gameplay scope; a frame pump (<c>GameTimePump</c>) advances it by the real frame time, and everything
    /// that moves on the board reads it instead of <c>UnityEngine.Time</c> — the oval belt's step cadence and drawing,
    /// the feeders, and every tween of the items and containers (scheduled on <see cref="Scheduler"/>, so a change of
    /// speed takes effect at once, even halfway through a flight). Engine time is never scaled: the UI and the
    /// framework's dialogs keep running in real time.
    /// </summary>
    public sealed class GameTime
    {
        private readonly ManualMotionDispatcher _motions = new ManualMotionDispatcher();
        private float _speed = 1f;

        /// <summary>
        /// The dispatcher keeps one runner per value type in a dictionary it walks on every <see cref="Advance"/>, and makes
        /// a runner the first time a tween of that type is scheduled. A tween that completes inside Advance resumes its
        /// awaiter right there; if that awaiter schedules the scope's FIRST tween of another type (a container's Vector3
        /// impact after its float flight), the dictionary changes mid-walk and Advance throws. So every type the game
        /// tweens on this clock gets its runner up front — add a type here when a game-time tween starts using it.
        /// </summary>
        public GameTime()
        {
            _motions.EnsureStorageCapacity<float, NoOptions, FloatMotionAdapter>(Prewarm);
            _motions.EnsureStorageCapacity<Vector2, NoOptions, Vector2MotionAdapter>(Prewarm);
            _motions.EnsureStorageCapacity<Vector3, NoOptions, Vector3MotionAdapter>(Prewarm);
            _motions.EnsureStorageCapacity<Quaternion, NoOptions, QuaternionMotionAdapter>(Prewarm);
            _motions.EnsureStorageCapacity<Color, NoOptions, ColorMotionAdapter>(Prewarm);
        }

        private const int Prewarm = 32;

        /// <summary>How many game seconds pass per real second: 1 = normal, 2 = twice as fast. Never negative.</summary>
        public float GameSpeed
        {
            get => _speed;
            set => _speed = Math.Max(0f, value);
        }

        /// <summary>Game seconds since this clock started.</summary>
        public double Now { get; private set; }

        /// <summary>Game seconds the last frame lasted (real frame time × <see cref="GameSpeed"/>).</summary>
        public float DeltaTime { get; private set; }

        /// <summary>Schedule a LitMotion tween here (<c>.WithScheduler(time.Scheduler)</c>) and it runs on game time.</summary>
        public IMotionScheduler Scheduler => _motions.Scheduler;

        /// <summary>One frame went by: <paramref name="realDeltaTime"/> real seconds. Moves the clock and its tweens on.</summary>
        public void Advance(float realDeltaTime)
        {
            DeltaTime = Math.Max(0f, realDeltaTime) * _speed;
            Now += DeltaTime;
            _motions.Update(DeltaTime);
        }

        /// <summary>Real seconds → game seconds at the current speed (for a fixed-tick dt).</summary>
        public float Scale(float realSeconds) => realSeconds * _speed;

        /// <summary>Wait <paramref name="seconds"/> of GAME time (shorter in real time while the game runs faster).</summary>
        public async UniTask Delay(float seconds, CancellationToken ct)
        {
            double until = Now + seconds;
            while (Now < until) await UniTask.Yield(PlayerLoopTiming.Update, ct);
        }
    }
}
