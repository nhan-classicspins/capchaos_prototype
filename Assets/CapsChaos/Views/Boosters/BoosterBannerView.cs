using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Views
{
    /// <summary>
    /// A booster acting (ref 2026-10-07): the screen dims, the <c>bg-active-booster</c> ribbon unrolls across it, the
    /// booster's icon runs along the ribbon from the left, slows to a stop at the centre, holds, then speeds off the
    /// right edge, and all of it goes. While it plays it swallows every
    /// tap. Humble: <see cref="PlayAsync"/> plays the pass it is asked for; what the booster does is the controller's
    /// call. Gallery-safe.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class BoosterBannerView : MonoBehaviour
    {
        [SerializeField] private Image _dim;
        [SerializeField] private RectTransform _ribbon;
        [Tooltip("Runs along the ribbon; its Image shows the booster's icon.")]
        [SerializeField] private RectTransform _runner;
        [SerializeField] private Image _icon;

        private CanvasGroup _group;
        private CancellationTokenSource _cts;
        private GameFeel _feel;

        private GameFeel Feel => _feel != null ? _feel : GameFeel.Defaults;

        /// <summary>The session's feel (timings, eases, distances); without it the defaults apply.</summary>
        public void UseFeel(GameFeel feel) => _feel = feel;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            if (_dim != null) { _dim.color = DesignTokens.Booster.BannerDim; _dim.raycastTarget = true; }
        }

        /// <summary>Play one pass with <paramref name="icon"/>; done once the banner has gone.</summary>
        public async UniTask PlayAsync(Sprite icon, CancellationToken ct)
        {
            if (_group == null) Awake();
            Stop();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct, destroyCancellationToken);
            var token = _cts.Token;
            if (_icon != null) { _icon.sprite = icon; _icon.enabled = icon != null; }
            gameObject.SetActive(true);
            _group.blocksRaycasts = true;
            _group.alpha = 0f;
            var feel = Feel;
            float slope = Mathf.Tan(feel.RibbonAngle * Mathf.Deg2Rad);
            Place(-feel.BannerEnterX, slope);
            try
            {
                await LMotion.Create(0f, 1f, feel.BannerIn).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).WithEase(feel.BannerInEase)
                    .Bind(k =>
                    {
                        _group.alpha = Mathf.Clamp01(k);
                        if (_ribbon != null) _ribbon.localScale = new Vector3(Mathf.Max(0f, k), 1f, 1f);
                    }).AddTo(gameObject).ToUniTask(token);
                await LMotion.Create(-feel.BannerEnterX, 0f, feel.BannerArrive).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithEase(feel.BannerArriveEase).Bind(x => Place(x, slope)).AddTo(gameObject).ToUniTask(token);
                await UniTask.Delay(TimeSpan.FromSeconds(feel.BannerHold), Cysharp.Threading.Tasks.DelayType.UnscaledDeltaTime, cancellationToken: token);
                await LMotion.Create(0f, feel.BannerExitX, feel.BannerLeave).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .WithEase(feel.BannerLeaveEase).Bind(x => Place(x, slope)).AddTo(gameObject).ToUniTask(token);
                await LMotion.Create(1f, 0f, feel.BannerOut).WithScheduler(MotionScheduler.UpdateIgnoreTimeScale).WithEase(Ease.InQuad)
                    .Bind(a => _group.alpha = a).AddTo(gameObject).ToUniTask(token);
            }
            finally
            {
                if (this != null && !token.IsCancellationRequested) Hide();
            }
        }

        /// <summary>Gone at once (the round was torn down mid-pass).</summary>
        public void Stop()
        {
            if (_cts != null) { _cts.Cancel(); _cts.Dispose(); _cts = null; }
            Hide();
        }

        private void Hide()
        {
            if (_group != null) { _group.alpha = 0f; _group.blocksRaycasts = false; }
            gameObject.SetActive(false);
        }

        private void Place(float x, float slope)
        {
            if (_runner != null) _runner.anchoredPosition = new Vector2(x, x * slope);
        }
    }
}
