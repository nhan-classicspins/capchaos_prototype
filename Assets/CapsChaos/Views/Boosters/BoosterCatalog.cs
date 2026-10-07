using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// Every booster the game has, ONE asset for the session (addressable <see cref="Address"/>,
    /// <c>Content/Configs/BoosterCatalog.asset</c>): per booster its id, the texts the player reads and its icon. What
    /// the player OWNS is not here — that is a wallet balance whose key is the booster's <see cref="BoosterDefinition.Id"/>
    /// (<c>ResourceKeys</c> in <c>game.resources.json</c>). The texts are localization KEYS (rows of <c>loc.csv</c>), never
    /// the words themselves: the controller resolves them through the localization service (rule #4).
    /// <c>SkuHeadlessTests/Gate/BoosterCatalogGateTests</c> keeps every id a real resource and every key a real row.
    /// </summary>
    [CreateAssetMenu(menuName = "CapsChaos/Booster Catalog", fileName = "BoosterCatalog")]
    public sealed class BoosterCatalog : ScriptableObject
    {
        public const string Address = "BoosterCatalog";

        [SerializeField] private List<BoosterDefinition> _boosters = new List<BoosterDefinition>();

        /// <summary>Every booster, in the order they are shown.</summary>
        public IReadOnlyList<BoosterDefinition> Boosters => _boosters;

        /// <summary>The booster with id <paramref name="id"/> (a ResourceKey value, e.g. <c>booster_shuffle</c>).</summary>
        public bool TryGet(string id, out BoosterDefinition booster)
        {
            foreach (var b in _boosters)
                if (b != null && string.Equals(b.Id, id, StringComparison.Ordinal)) { booster = b; return true; }
            booster = null;
            return false;
        }
    }

    /// <summary>One booster as the player sees it.</summary>
    [Serializable]
    public sealed class BoosterDefinition
    {
        [Tooltip("The wallet balance this booster is counted in — a ResourceKey value from game.resources.json " +
                 "(e.g. booster_shuffle). It is also the save key of the player's count: never rename one that shipped.")]
        [SerializeField] private string _id;
        [Tooltip("Localization key (loc.csv) of the booster's name, e.g. booster.shuffle.name.")]
        [SerializeField] private string _nameKey;
        [Tooltip("Localization key of what it does — on its button / the shop.")]
        [SerializeField] private string _descriptionKey;
        [Tooltip("Localization key of the text shown when the player unlocks it.")]
        [SerializeField] private string _unlockedDescriptionKey;
        [Tooltip("Its icon (button, shop, unlock popup).")]
        [SerializeField] private Sprite _icon;

        public string Id => _id;
        public string NameKey => _nameKey;
        public string DescriptionKey => _descriptionKey;
        public string UnlockedDescriptionKey => _unlockedDescriptionKey;
        public Sprite Icon => _icon;
    }
}
