using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using TMPro;
using UnityEngine;
using M = Game.Views.DesignTokens.Motion;

namespace Game.Views
{
    /// <summary>
    /// The lock on a locked tray (GDD R18): a padlock with the number of placements still to wait. Root of the
    /// <c>TrayLock</c> prefab, which the art may replace wholesale — every reference is optional, so a prefab without a
    /// count or a shackle still works. Humble: it shows the text it is given and plays its two beats.
    /// </summary>
    public sealed class TrayLockView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _count;
        [Tooltip("Lifts open on unlock (optional).")]
        [SerializeField] private Transform _shackle;

        private GameTime _time;
        /// <summary>Tweens run on the gameplay clock once the board hands it one (<see cref="UseTime"/>), else on engine time.</summary>
        private IMotionScheduler Sched => _time != null ? _time.Scheduler : MotionScheduler.Update;

        /// <summary>Animate on <paramref name="time"/> (GameSpeed) — the board calls it when it creates this view.</summary>
        public void UseTime(GameTime time) => _time = time;

        public void SetCount(string text)
        {
            if (_count != null) _count.SetText(text);
        }

        /// <summary>The count changed: show the new one with a small pop.</summary>
        public async UniTask PlayTickAsync(string text, CancellationToken ct)
        {
            SetCount(text);
            var t = _count != null ? _count.transform : transform;
            var rest = t.localScale;
            await LMotion.Create(1.35f, 1f, M.LockTick).WithScheduler(Sched).WithEase(Ease.OutBack)
                .Bind(k => { if (t != null) t.localScale = rest * k; }).AddTo(gameObject).ToUniTask(ct);
        }

        /// <summary>Unlocked: the shackle springs open, the lock swells and vanishes, then destroys itself.</summary>
        public async UniTask PlayUnlockAsync(CancellationToken ct)
        {
            if (_count != null) _count.gameObject.SetActive(false);
            var root = transform;
            var rest = root.localScale;
            Vector3 shackleFrom = _shackle != null ? _shackle.localPosition : Vector3.zero;
            await LMotion.Create(0f, 1f, M.Unlock).WithScheduler(Sched).WithEase(Ease.Linear).Bind(k =>
            {
                if (root == null) return;
                if (_shackle != null) _shackle.localPosition = shackleFrom + Vector3.up * (0.12f * Mathf.Clamp01(k * 2.5f));
                // swell to 1.2 over the first 40 %, then shrink to nothing
                float s = k < 0.4f ? Mathf.Lerp(1f, 1.2f, k / 0.4f) : Mathf.Lerp(1.2f, 0f, (k - 0.4f) / 0.6f);
                root.localScale = rest * s;
            }).AddTo(gameObject).ToUniTask(ct);
            if (this != null) Destroy(gameObject);
        }
    }
}
