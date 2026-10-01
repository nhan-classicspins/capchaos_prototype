using System;
using Game.Domain;
using Game.Views;

namespace Game.Presentation
{
    /// <summary>
    /// The one bridge from the rules' <see cref="CapColor"/> to the View's <see cref="TintFlavor"/>
    /// (Game.Views may not reference Game.Domain). Spelled out member by member, so reordering either enum
    /// can never silently repaint a level.
    /// </summary>
    internal static class CapColorTint
    {
        public static TintFlavor ToTint(this CapColor color) => color switch
        {
            CapColor.None   => TintFlavor.None,
            CapColor.Red    => TintFlavor.Red,
            CapColor.Orange => TintFlavor.Orange,
            CapColor.Blue   => TintFlavor.Blue,
            CapColor.Green  => TintFlavor.Green,
            CapColor.Purple => TintFlavor.Purple,
            CapColor.Yellow => TintFlavor.Yellow,
            CapColor.Cyan   => TintFlavor.Cyan,
            CapColor.Brown  => TintFlavor.Brown,
            _ => throw new ArgumentOutOfRangeException(nameof(color), color, "no TintFlavor for this CapColor"),
        };
    }
}
