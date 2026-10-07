using ClassicSpins.PrototypeFramework.Domain;

namespace Game.Application
{
    /// <summary>
    /// The player's own saved progress — everything about the player that is NOT something they own. What they own
    /// (coins, boosters) is a balance in the framework's wallet (<c>IWalletService</c>, saved as <c>"wallet"</c>); this
    /// model sits beside it in the same save file (<c>FileUserData</c>, <c>persistentDataPath/save.json</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>Stable key.</b> <see cref="Key"/> is <c>"player"</c> and never changes — renaming it orphans every
    /// player's saved progress. Adding a property needs no save-schema bump: an older save simply lacks it and reads
    /// the default.</para>
    /// <para><b>One writer.</b> Only <see cref="PlayerProfile"/> changes it (and asks for the save); screens read
    /// through the profile. Registered <c>.As&lt;IUserModel&gt;()</c> — without that it would silently never save.</para>
    /// </remarks>
    public sealed class PlayerProgressModel : IUserModel
    {
        public string Key => "player";

        /// <summary>The PLAY position of the level the player is on (0 = their first level; it keeps counting past the
        /// last level — the catalog loops, the number does not). The level they see is <c>CurrentLevel + 1</c>.</summary>
        public int CurrentLevel { get; set; }
    }
}
