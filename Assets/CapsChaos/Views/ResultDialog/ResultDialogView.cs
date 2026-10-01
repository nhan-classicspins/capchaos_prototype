using UnityEngine;
using UnityEngine.UI;
using ClassicSpins.PrototypeFramework.Views;

namespace Game.Views
{
    /// <summary>Which of the two result looks the popup wears (art §3.3). The controller decides; the View maps it to tokens.</summary>
    public enum ResultLook
    {
        Win,
        Lose,
    }

    /// <summary>
    /// The Win / Lose popup (GDD §7, refs 09_win_popup / 10_lose_popup): ONE prefab, two themes — a 3-band panel
    /// (header · body with the title · band with the subtitle) over a big two-tone primary button that sinks when
    /// pressed. The dim and the input blocker behind it are the dialog service's (PF/DialogDim, PF/Blocker), never
    /// this prefab's. It never knows whether the round was won: <see cref="SetLook"/> and <see cref="SetTexts"/> are
    /// all it is told. Gallery-safe: any order, no services.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResultDialogView : DialogViewBase
    {
        [Header("Themed surfaces")]
        [SerializeField] private UiTint _header;
        [SerializeField] private UiTint _body;
        [SerializeField] private UiTint _band;
        [SerializeField] private UiTint _panelLip;
        [SerializeField] private UiTint _buttonTop;
        [SerializeField] private UiTint _buttonBottom;
        [SerializeField] private UiTint _buttonLip;

        [Header("Text")]
        [SerializeField] private TextProxy _title;
        [SerializeField] private TextProxy _subtitle;
        [SerializeField] private TextProxy _buttonLabel;

        [Header("Input")]
        [SerializeField] private Button _button;

        public void SetLook(ResultLook look)
        {
            bool win = look == ResultLook.Win;
            Paint(_header, win ? UiToken.WinHeader : UiToken.LoseHeader);
            Paint(_body, win ? UiToken.WinBody : UiToken.LoseBody);
            Paint(_band, win ? UiToken.WinBand : UiToken.LoseBand);
            Paint(_panelLip, win ? UiToken.WinButtonLip : UiToken.LoseButtonLip);
            Paint(_buttonTop, win ? UiToken.WinButtonTop : UiToken.LoseButtonTop);
            Paint(_buttonBottom, win ? UiToken.WinButtonBottom : UiToken.LoseButtonBottom);
            Paint(_buttonLip, win ? UiToken.WinButtonLip : UiToken.LoseButtonLip);
        }

        public void SetTexts(string title, string subtitle, string button)
        {
            if (_title != null) _title.SetText(title);
            if (_subtitle != null) _subtitle.SetText(subtitle);
            if (_buttonLabel != null) _buttonLabel.SetText(button);
        }

        /// <summary>Art §5: in = scale 0.8 → 1 (OutBack, 0.25 s) + fade; out is the same, reversed.</summary>
        public override ViewTransition CreateTransition()
            => ViewTransition.Combine(new FadeTransition(0.15f), new ScaleTransition(0.8f, 0.25f));

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(RaiseCloseRequested);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(RaiseCloseRequested);
        }

        private static void Paint(UiTint tint, UiToken token)
        {
            if (tint != null) tint.SetToken(token);
        }
    }
}
