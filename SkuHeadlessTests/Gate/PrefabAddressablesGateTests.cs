using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.Gate
{
    /// <summary>
    /// Every SKU prefab that the runtime loads by <c>AssetKey</c> actually has an Addressables entry at
    /// the address the key spells.
    ///
    /// <para><b>Why the link breaks without anything going red.</b> The two halves are produced by two
    /// different machines. The <c>AssetKey</c> comes from codegen, which runs from the manifests OUTSIDE
    /// <c>Assets/</c> and therefore off a plain file scan. The Addressables ENTRY comes from the
    /// framework's <c>PrefabAddressableRegistrar</c>, which only runs when Unity IMPORTS the prefab. A
    /// prefab authored as YAML outside the Editor gets the first and not the second: the key compiles,
    /// every engine-free and EditMode test stays green (they reach assets by PATH), and only a running
    /// game notices — <c>InvalidKeyException … No Location found for Key=…</c> at boot. Measured on a
    /// SKU: a HUD widget and two result dialogs shipped exactly that way. The expectations here are
    /// DERIVED, so a prefab added tomorrow is covered without anyone editing this file.</para>
    ///
    /// <para><b>The address rule is the registrar's, mirrored</b>
    /// (<c>PrefabAddressableRegistrar.AddressUnderPrefabs</c>): the path under the LAST <c>Prefabs/</c>
    /// folder, minus the <c>.prefab</c> extension. Game prefabs address bare; the reserved <c>PF/</c>
    /// prefix belongs to framework-package prefabs, which are not this SKU's to ship.</para>
    ///
    /// <para>A SKU that ships no prefab yet passes every case here with the reason stated — the gate
    /// starts biting at the first prefab. Emitted by the Setup Wizard's test kit; this file is the SKU's.</para>
    /// </summary>
    [TestFixture]
    public sealed class PrefabAddressablesGateTests
    {
        private const string PrefabsMarker = "/Prefabs/";
        private const string SkuRoot = "Assets/CapsChaos";

        private static readonly string[] GroupRelatives =
        {
            "Assets/AddressableAssetsData/AssetGroups/Local.asset",
            "Assets/AddressableAssetsData/AssetGroups/Remote.asset",
        };

        private const string GeneratedKeysRelative = "Assets/CapsChaos/Presentation/Gen/AssetKeys.gen.cs";

        /// <summary>Repo-relative path of every <c>.prefab</c> under the SKU, forward-slashed.</summary>
        private static IReadOnlyList<string> SkuPrefabs()
        {
            var root = RepoLayout.Path(SkuRoot.Split('/'));
            Assert.That(Directory.Exists(root), Is.True, root + ": not found.");

            var rootPrefix = RepoLayout.RepoRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return Directory
                .GetFiles(root, "*.prefab", SearchOption.AllDirectories)
                .Select(p => p.Substring(rootPrefix.Length).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>Mirror of <c>PrefabAddressableRegistrar.AddressUnderPrefabs</c>; null when the
        /// prefab is not under a <c>Prefabs/</c> folder and so is not addressable at all.</summary>
        private static string? AddressUnderPrefabs(string prefabRelative)
        {
            var norm = "/" + prefabRelative;
            var idx = norm.LastIndexOf(PrefabsMarker, StringComparison.Ordinal);
            if (idx < 0) return null;

            var rel = norm.Substring(idx + PrefabsMarker.Length);
            if (rel.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(0, rel.Length - ".prefab".Length);

            return rel.Length == 0 ? null : rel;
        }

        /// <summary>The GUID an asset's <c>.meta</c> declares.</summary>
        internal static string GuidOf(string assetRelative)
        {
            var metaPath = RepoLayout.Path((assetRelative + ".meta").Split('/'));

            Assert.That(File.Exists(metaPath), Is.True,
                metaPath + " is missing; without it Unity has no stable GUID for the asset and an " +
                "Addressables entry has nothing to point at.");

            var guid = Regex.Match(
                File.ReadAllText(metaPath), "^guid: *([0-9a-fA-F]{32}) *$", RegexOptions.Multiline);

            Assert.That(guid.Success, Is.True, metaPath + " declares no guid.");
            return guid.Groups[1].Value;
        }

        /// <summary>Every <c>(guid, address)</c> pair in the shipped groups. With
        /// <paramref name="requireAny"/>, an empty parse fails: either the groups are gone or their
        /// serialized shape changed and a gate reading them is reading nothing while reporting green.</summary>
        internal static IReadOnlyList<(string Guid, string Address)> Entries(bool requireAny)
        {
            var pairs = new List<(string, string)>();

            foreach (var relative in GroupRelatives)
            {
                var path = RepoLayout.Path(relative.Split('/'));
                if (!File.Exists(path)) continue;

                foreach (Match m in Regex.Matches(
                             File.ReadAllText(path),
                             "m_GUID: *([0-9a-fA-F]{32}) *\\r?\\n *m_Address: *(\\S+)"))
                {
                    pairs.Add((m.Groups[1].Value, m.Groups[2].Value));
                }
            }

            if (requireAny)
                Assert.That(pairs, Is.Not.Empty,
                    "no Addressables entries were parsed out of " + string.Join(" or ", GroupRelatives) +
                    "; either the groups are gone or their serialized shape changed.");

            return pairs;
        }

        [Test]
        public void Every_sku_prefab_lives_under_a_Prefabs_folder()
        {
            var stranded = SkuPrefabs().Where(p => AddressUnderPrefabs(p) == null).ToArray();

            Assert.That(stranded, Is.Empty,
                "these prefabs are not under a Prefabs/ folder: " + string.Join(", ", stranded) +
                ". Both machines that make a prefab loadable — codegen's AssetKey scan and the " +
                "framework's PrefabAddressableRegistrar — key off that folder name, so a prefab " +
                "outside one has no typed key and no address, and fails only at runtime.");
        }

        [Test]
        public void Every_sku_prefab_has_an_addressables_entry_at_its_scanned_address()
        {
            var addressable = SkuPrefabs().Where(p => AddressUnderPrefabs(p) != null).ToArray();
            if (addressable.Length == 0)
                Assert.Pass("this SKU ships no prefab under a Prefabs/ folder yet — nothing to link.");

            var byGuid = Entries(requireAny: true)
                .GroupBy(e => e.Guid, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Address, StringComparer.Ordinal);

            foreach (var prefab in addressable)
            {
                var expected = AddressUnderPrefabs(prefab)!;
                var guid = GuidOf(prefab);

                Assert.That(byGuid.ContainsKey(guid), Is.True,
                    prefab + " (GUID " + guid + ") has no Addressables entry, so the runtime's " +
                    "AssetKey \"" + expected + "\" resolves to nothing and the first load throws " +
                    "InvalidKeyException. Fix it by letting Unity import the prefab (the registrar runs " +
                    "on the codegen postprocessor's tick), never by hand-editing the group.");

                Assert.That(byGuid[guid], Is.EqualTo(expected),
                    prefab + " is addressable at '" + byGuid[guid] + "', but its path under " +
                    "Prefabs/ makes its address '" + expected + "' — which is the string codegen " +
                    "puts in AssetKeys.gen.cs. One of the two moved without the other.");
            }
        }

        [Test]
        public void Every_generated_AssetKey_resolves_to_an_addressables_entry()
        {
            var keysPath = RepoLayout.Path(GeneratedKeysRelative.Split('/'));
            var anyPrefab = SkuPrefabs().Any(p => AddressUnderPrefabs(p) != null);

            if (!File.Exists(keysPath))
            {
                Assert.That(anyPrefab, Is.False,
                    keysPath + ": not found, but the SKU ships prefabs. It is codegen output; run " +
                    "Framework/Codegen/Generate.");
                Assert.Pass("no AssetKeys.gen.cs and no prefab yet — nothing to resolve.");
            }

            var addresses = Regex
                .Matches(File.ReadAllText(keysPath), "new\\(\"([^\"]+)\"\\)")
                .Select(m => m.Groups[1].Value)
                .ToArray();
            if (addresses.Length == 0)
            {
                Assert.That(anyPrefab, Is.False,
                    keysPath + " declares no AssetKey values although the SKU ships prefabs; the " +
                    "generated shape changed and this gate is asserting nothing.");
                Assert.Pass("no generated AssetKey yet — nothing to resolve.");
            }

            var shipped = new HashSet<string>(Entries(requireAny: true).Select(e => e.Address), StringComparer.Ordinal);
            var unresolvable = addresses.Where(a => !shipped.Contains(a)).ToArray();

            Assert.That(unresolvable, Is.Empty,
                "these generated AssetKeys have no Addressables entry: " + string.Join(", ", unresolvable) +
                ". A typed key that resolves to nothing is green everywhere and InvalidKeyException at boot.");
        }

        [Test]
        public void No_two_addressables_entries_claim_one_address()
        {
            var duplicates = Entries(requireAny: false)
                .GroupBy(e => e.Address, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key + " x" + g.Count())
                .ToArray();

            Assert.That(duplicates, Is.Empty,
                "duplicate Addressables addresses: " + string.Join(", ", duplicates) +
                ". Which asset the runtime is handed is then a build-order accident.");
        }
    }
}
