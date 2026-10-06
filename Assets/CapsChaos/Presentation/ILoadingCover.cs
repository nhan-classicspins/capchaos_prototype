using System.Threading;
using Cysharp.Threading.Tasks;

namespace Game.Presentation
{
    /// <summary>
    /// The full-screen loading cover (the SKU's Loading scene): raised while a level is being built — the Gameplay
    /// scene load, its assets and the board — and taken down once the round is on screen. The same cover boot shows,
    /// so the game has one loading look. A port, so screens never reach Infrastructure; the Root
    /// <c>LoadingSceneHost</c> implements it.
    /// </summary>
    public interface ILoadingCover
    {
        /// <summary>Show the cover reading <paramref name="label"/> (already localized). Completes once it is on
        /// screen; showing it again while it is up only changes the label.</summary>
        UniTask ShowAsync(string label, CancellationToken ct);

        /// <summary>Fade the cover out and take it down. Idempotent; never throws.</summary>
        UniTask HideAsync();
    }
}
