using System;
using System.Collections.Generic;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;

namespace Game.Application
{
    /// <summary>
    /// The SKU's door to the player's saved data. What the player OWNS — coins and boosters — are wallet balances
    /// (<see cref="IWalletService"/>: <c>Grant</c> / <c>TrySpend</c> / <c>TryExchange</c>, each attributed to a
    /// <see cref="GrantSource"/>), so this class never holds a count itself; it keeps the rest of the player's state in
    /// <see cref="PlayerProgressModel"/> and asks <see cref="IUserData"/> to save after each change. Root singleton.
    /// </summary>
    public sealed class PlayerProfile
    {
        private readonly PlayerProgressModel _progress;
        private readonly IWalletService _wallet;
        private readonly IUserData _userData;

        public PlayerProfile(PlayerProgressModel progress, IWalletService wallet, IUserData userData)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _userData = userData ?? throw new ArgumentNullException(nameof(userData));
        }

        /// <summary>The PLAY position of the level the player is on — what Main's Start button plays (level number
        /// <c>CurrentLevel + 1</c>).</summary>
        public int CurrentLevel => Math.Max(0, _progress.CurrentLevel);

        /// <summary>
        /// The player won the level at PLAY position <paramref name="play"/>: they are on the next one now — unless they
        /// were already further on (replaying an older level from the level list never takes them back).
        /// </summary>
        public void CompleteLevel(int play)
        {
            if (play + 1 <= CurrentLevel) return;
            _progress.CurrentLevel = play + 1;
            _userData.Save();
        }

        /// <summary>Put the player on the level at PLAY position <paramref name="play"/> — forwards or back (the debug
        /// Reset / Unlock All buttons on Main). Saved.</summary>
        public void SetCurrentLevel(int play)
        {
            _progress.CurrentLevel = Math.Max(0, play);
            _userData.Save();
        }

        /// <summary>
        /// Debug Reset: the player as on a fresh install — every wallet balance (coins and every booster) spent to 0
        /// through the wallet gate, and back on level 1. (There is no starting pack: a new player owns nothing.)
        /// </summary>
        public void ResetToFreshInstall()
        {
            var wallet = _userData.Get<WalletModel>();                         // read only: which resources to empty
            foreach (var id in new List<string>(wallet.Balances.Keys))
            {
                var resource = new ResourceKey(id);
                long balance = _wallet.Balance(resource);
                if (balance > 0) _wallet.TrySpend(resource, balance, GrantSource.Compensation);
            }
            _progress.CurrentLevel = 0;
            _userData.Save();
        }

        /// <summary>How many of <paramref name="booster"/> the player owns.</summary>
        public long BoosterCount(ResourceKey booster) => _wallet.Balance(booster);

        /// <summary>Give the player <paramref name="count"/> more of <paramref name="booster"/> (a reward, a gift, the test
        /// buttons on Main), attributed to <paramref name="source"/>.</summary>
        public void GrantBoosters(ResourceKey booster, long count, GrantSource source)
        {
            if (count > 0) _wallet.Grant(booster, count, source);
        }

        /// <summary>How many coins the player owns.</summary>
        public long Coins(ResourceKey coins) => _wallet.Balance(coins);

        /// <summary>Give the player <paramref name="amount"/> more coins (the debug button on Main), attributed to
        /// <paramref name="source"/>.</summary>
        public void GrantCoins(ResourceKey coins, long amount, GrantSource source)
        {
            if (amount > 0) _wallet.Grant(coins, amount, source);
        }

        /// <summary>Use one <paramref name="booster"/>. False — and nothing spent — when the player has none. Call it only
        /// once the rules have accepted the booster's effect, so a refused booster is never spent.</summary>
        public bool TryUseBooster(ResourceKey booster) => _wallet.TrySpend(booster, 1, GrantSource.Reward);

        /// <summary>Buy <paramref name="count"/> of <paramref name="booster"/> for <paramref name="coinPrice"/> coins each, in
        /// one atomic exchange: without enough coins nothing changes and it returns false.</summary>
        public bool TryBuyBooster(ResourceKey booster, int count, long coinPrice, ResourceKey coins) =>
            count > 0 && _wallet.TryExchange(coins, coinPrice * count, g => g.Grant(booster, count), GrantSource.Reward);
    }
}
