using Game.Views;
using UnityEngine;
using VContainer.Unity;

namespace Game.Composition
{
    /// <summary>
    /// Advances the Gameplay scope's <see cref="GameTime"/> by the real frame time, once per frame. Not gated: tweens
    /// already in flight finish while a dialog covers the board, as they did on engine time; the belt's own step is
    /// gated by the framework's fixed tick (BeltClock).
    /// </summary>
    public sealed class GameTimePump : ITickable
    {
        private readonly GameTime _time;

        public GameTimePump(GameTime time) => _time = time;

        public void Tick() => _time.Advance(Time.deltaTime);
    }
}
