using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.Gate
{
    /// <summary>
    /// The level content is loadable at boot: <c>Content/LevelConfig/</c> is ONE Addressables folder entry
    /// at the address <c>LevelConfigNode.Folder</c> reads (<c>"LevelConfig"</c>, so a level is
    /// <c>LevelConfig/level_0001.json</c>). Without the entry every engine-free test still passes — they read
    /// levels by path — and only the running game notices, with the Required LevelConfig node aborting boot.
    /// The node lives in engine-bound Infrastructure, so the address is mirrored here; change both together.
    /// <para><b>Its own group, on purpose.</b> The entry lives in a SKU-owned <c>Levels</c> group, NOT in
    /// <c>Local</c>/<c>Remote</c>: the framework's <c>PrefabAddressableRegistrar.SyncAll</c> runs on every codegen
    /// tick and removes every entry in those two groups that is neither a prefab nor a scene — a folder entry
    /// placed there silently disappears (it did, twice). This gate therefore reads EVERY group file.</para>
    /// </summary>
    [TestFixture]
    public sealed class LevelConfigAddressablesGateTests
    {
        private const string FolderRelative = "Assets/CapsChaos/Content/LevelConfig";
        private const string ExpectedAddress = "LevelConfig";   // == Game.Infrastructure.LevelConfigNode.Folder
        private const string ExpectedGroup = "Levels";
        private static readonly string[] RegistrarPrunedGroups = { "Local", "Remote" };

        /// <summary>(group, guid, address) for every entry in every Addressables group file.</summary>
        private static IReadOnlyList<(string Group, string Guid, string Address)> AllEntries()
        {
            var dir = RepoLayout.Path("Assets", "AddressableAssetsData", "AssetGroups");
            var list = new List<(string, string, string)>();
            foreach (var file in Directory.GetFiles(dir, "*.asset"))
            {
                var text = File.ReadAllText(file);
                var group = Regex.Match(text, @"^\s*m_GroupName: *(\S+)", RegexOptions.Multiline).Groups[1].Value;
                foreach (Match m in Regex.Matches(text, "m_GUID: *([0-9a-fA-F]{32}) *\\r?\\n *m_Address: *(\\S+)"))
                    list.Add((group, m.Groups[1].Value, m.Groups[2].Value));
            }
            Assert.That(list, Is.Not.Empty, "no Addressables entries parsed under " + dir);
            return list;
        }

        [Test]
        public void The_LevelConfig_folder_is_one_addressables_entry_at_its_runtime_address()
        {
            var guid = PrefabAddressablesGateTests.GuidOf(FolderRelative);
            var mine = AllEntries().Where(e => string.Equals(e.Guid, guid, StringComparison.Ordinal)).ToArray();

            Assert.That(mine, Is.Not.Empty,
                FolderRelative + " (GUID " + guid + ") is not in an Addressables group, so LevelConfigNode " +
                "finds no 'LevelConfig/levels.index.json' and boot aborts. Mark the folder Addressable in the " +
                "Editor (address '" + ExpectedAddress + "'), never by hand-editing the group YAML.");
            Assert.That(mine[0].Address, Is.EqualTo(ExpectedAddress),
                "the LevelConfig folder entry's address must be what LevelConfigNode.Folder spells");
            Assert.That(RegistrarPrunedGroups, Does.Not.Contain(mine[0].Group),
                "the LevelConfig entry is in '" + mine[0].Group + "', which PrefabAddressableRegistrar prunes of every " +
                "non-prefab, non-scene entry on each codegen tick — keep it in the '" + ExpectedGroup + "' group.");
        }

        /// <summary>The Root UiPaletteProvider loads the palette by this address; mirrored from UiPalette.Address.</summary>
        [Test]
        public void The_UI_palette_is_addressable_outside_the_registrar_pruned_groups()
        {
            const string paletteRelative = "Assets/CapsChaos/Content/UI/UiPalette.asset";
            var guid = PrefabAddressablesGateTests.GuidOf(paletteRelative);
            var mine = AllEntries().Where(e => string.Equals(e.Guid, guid, StringComparison.Ordinal)).ToArray();
            Assert.That(mine, Is.Not.Empty,
                paletteRelative + " is not addressable, so UiPaletteProvider finds nothing and the UI falls back to the " +
                "DesignTokens defaults — custom colours silently stop applying.");
            Assert.That(mine[0].Address, Is.EqualTo("UiPalette"));
            Assert.That(RegistrarPrunedGroups, Does.Not.Contain(mine[0].Group),
                "the palette entry is in '" + mine[0].Group + "', which PrefabAddressableRegistrar prunes of non-prefab entries.");
        }

        [Test]
        public void No_prefab_references_the_palette_directly()
        {
            // the palette is pushed in at runtime; a serialized reference would copy it into every bundle
            var guid = PrefabAddressablesGateTests.GuidOf("Assets/CapsChaos/Content/UI/UiPalette.asset");
            var offenders = Directory.GetFiles(RepoLayout.Path("Assets", "CapsChaos"), "*.prefab", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(RepoLayout.Path("Assets", "CapsChaos"), "*.unity", SearchOption.AllDirectories))
                .Where(f => File.ReadAllText(f).Contains(guid))
                .ToArray();
            Assert.That(offenders, Is.Empty, "these reference UiPalette.asset directly: " + string.Join(", ", offenders));
        }

        [Test]
        public void No_level_file_has_its_own_entry_beside_the_folder_entry()
        {
            // a per-file entry would give the same asset a second address and hide a folder-entry regression
            var levelAddresses = AllEntries()
                .Where(e => e.Address.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || e.Address.StartsWith("level_", StringComparison.Ordinal))
                .Select(e => e.Address)
                .ToArray();
            Assert.That(levelAddresses, Is.Empty, "levels are addressed through the LevelConfig folder entry only");
        }
    }
}
