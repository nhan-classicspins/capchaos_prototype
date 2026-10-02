using System;
using ClassicSpins.PrototypeFramework.Application;

namespace Game.Presentation
{
    /// <summary>
    /// The Gameplay scope's one <see cref="IGameStep"/>: <c>GameplayTickDriver</c> pumps it on the fixed tick while the
    /// gameplay gate is open (rule #15), and it hands the scaled dt to whoever drives the round — the
    /// <see cref="GameplayScreen"/>, which steps the oval belt on its own cadence.
    /// </summary>
    public sealed class BeltClock : IGameStep
    {
        public event Action<float> Ticked;

        public void Step(float dt) => Ticked?.Invoke(dt);
    }
}
