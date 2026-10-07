using System.Collections.Generic;
using ClassicSpins.PrototypeFramework.Application;
using ClassicSpins.PrototypeFramework.Domain;
using Game.Application;
using Game.Gen;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.Player
{
    /// <summary>The player's saved data (PlayerProfile): boosters are wallet balances beside the coins, the level the
    /// player is on, and the debug Reset to a fresh install. There is no starting pack: a new player owns nothing.</summary>
    public sealed class PlayerProfileTests
    {
        /// <summary>The wallet as the shipped WalletService keeps it: balances in the saved WalletModel.</summary>
        private sealed class ModelWallet : WalletCore
        {
            private readonly IUserData _data;
            public ModelWallet(IUserData data) : base(new NullWalletChangedPublisher()) => _data = data;
            protected override IDictionary<string, long> Balances => _data.Get<WalletModel>().Balances;
            protected override void RequestSave() => _data.Save();
        }

        private static (PlayerProfile Profile, IWalletService Wallet, InMemoryUserData Data, PlayerProgressModel Progress) New(
            WalletModel wallet = null)
        {
            var progress = new PlayerProgressModel();
            var data = InMemoryUserData.Create(new IUserModel[] { wallet ?? new WalletModel(), progress });
            var w = new ModelWallet(data);
            return (new PlayerProfile(progress, w, data), w, data, progress);
        }

        [Test]
        public void Using_a_booster_spends_one_and_none_left_spends_nothing()
        {
            var (profile, _, _, _) = New();
            profile.GrantBoosters(ResourceKeys.BoosterShuffle, 1, GrantSource.Compensation);
            Assert.That(profile.TryUseBooster(ResourceKeys.BoosterShuffle), Is.True);
            Assert.That(profile.TryUseBooster(ResourceKeys.BoosterShuffle), Is.False);
            Assert.That(profile.BoosterCount(ResourceKeys.BoosterShuffle), Is.Zero);
        }

        [Test]
        public void Buying_boosters_with_coins_is_all_or_nothing()
        {
            var (profile, wallet, _, _) = New();
            wallet.Grant(ResourceKeys.Coins, 500, GrantSource.Reward);
            Assert.That(profile.TryBuyBooster(ResourceKeys.BoosterShuffle, 2, 200, ResourceKeys.Coins), Is.True);
            Assert.That((wallet.Balance(ResourceKeys.Coins), profile.BoosterCount(ResourceKeys.BoosterShuffle)), Is.EqualTo((100L, 2L)));
            Assert.That(profile.TryBuyBooster(ResourceKeys.BoosterShuffle, 1, 200, ResourceKeys.Coins), Is.False);
            Assert.That((wallet.Balance(ResourceKeys.Coins), profile.BoosterCount(ResourceKeys.BoosterShuffle)), Is.EqualTo((100L, 2L)),
                "too few coins: nothing changed");
        }

        [Test]
        public void Every_saved_model_key_is_unique()
        {
            Assert.DoesNotThrow(() => SaveKeyRegistry.Validate(new IUserModel[] { new WalletModel(), new PlayerProgressModel() }));
            Assert.That(new PlayerProgressModel().Key, Is.EqualTo("player"));
        }
    
        [Test]
        public void Winning_the_current_level_moves_the_player_on_and_an_older_one_never_takes_them_back()
        {
            var (profile, _, data, progress) = New();
            Assert.That(profile.CurrentLevel, Is.Zero, "a new player is on their first level");
            profile.CompleteLevel(0);
            Assert.That(profile.CurrentLevel, Is.EqualTo(1));
            profile.CompleteLevel(40);                                       // level 41 (past the catalog: it loops)
            Assert.That(profile.CurrentLevel, Is.EqualTo(41));
            int saves = data.SaveRequestCount;
            profile.CompleteLevel(3);                                        // replayed from the level list
            Assert.That((profile.CurrentLevel, progress.CurrentLevel), Is.EqualTo((41, 41)));
            Assert.That(data.SaveRequestCount, Is.EqualTo(saves), "nothing changed, nothing saved");
        }

        [Test]
        public void Setting_the_current_level_goes_both_ways_and_is_saved()
        {
            var (profile, _, data, progress) = New();
            profile.SetCurrentLevel(39);
            Assert.That((profile.CurrentLevel, progress.CurrentLevel), Is.EqualTo((39, 39)));
            profile.SetCurrentLevel(0);
            Assert.That(profile.CurrentLevel, Is.Zero, "Reset goes back");
            profile.SetCurrentLevel(-5);
            Assert.That(profile.CurrentLevel, Is.Zero);
            Assert.That(data.SaveRequestCount, Is.EqualTo(3));
        }

        [Test]
        public void Reset_is_a_fresh_install()
        {
            var (profile, wallet, _, _) = New();
            wallet.Grant(ResourceKeys.Coins, 1000, GrantSource.Reward);
            profile.GrantBoosters(ResourceKeys.BoosterHand, 10, GrantSource.Compensation);
            profile.GrantBoosters(ResourceKeys.BoosterShuffle, 2, GrantSource.Compensation);
            profile.CompleteLevel(12);
            profile.ResetToFreshInstall();
            Assert.That(wallet.Balance(ResourceKeys.Coins), Is.Zero);
            Assert.That(profile.BoosterCount(ResourceKeys.BoosterHand), Is.Zero);
            Assert.That(profile.BoosterCount(ResourceKeys.BoosterShuffle), Is.Zero);
            Assert.That(profile.CurrentLevel, Is.Zero);
        }
}
}
