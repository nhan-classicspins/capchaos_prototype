using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace CapsChaos.SkuHeadlessTests.Gate
{
    /// <summary>
    /// The assembly SPINE, read off the shipped <c>.asmdef</c> files rather than inferred from what
    /// happens to compile: every SKU assembly references EXACTLY its authorised set, no assembly exists
    /// that the table does not name, and the engine-free and Editor-only tiers stay what they are.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this cannot be left to the compiler.</b> <c>Tools/pf/typecheck.sh</c> type-checks
    /// against the <c>.csproj</c> Unity regenerates FROM the asmdef, so adding an edge makes the
    /// type-check GREENER, not redder. A reference edge is an AD-1 spine decision, and the point of a
    /// routed decision is that drifting from it is LOUD.</para>
    /// <para><b>Editing the table IS the routing.</b> Emitted by the Setup Wizard's test kit with the
    /// reference sets the spine emit wrote (or, for a SKU that already had its asmdefs, the sets on disk
    /// at that moment). When a spec routes a new edge, the asmdef and its row here change in the same
    /// commit, with the reason beside the row. This file is the SKU's.</para>
    /// <para><b>Order-insensitive, duplicate-sensitive.</b> Unity ignores reference order, so the
    /// comparison is set-based; a repeated entry is still reported, because it is a merge gone wrong.</para>
    /// </remarks>
    [TestFixture]
    public sealed class AssemblyReferenceTests
    {
        public sealed record Row(string Asmdef, string[] References)
        {
            public override string ToString() => Asmdef;
        }

        /// <summary>The authorised table: every asmdef under <c>Assets/CapsChaos/</c> and its exact references.</summary>
        public static readonly Row[] Authorised =
        {
            new Row("Assets/CapsChaos/Domain/Game.Domain.asmdef", new string[]
            {
                "ClassicSpins.PrototypeFramework.Domain",
            }),
            new Row("Assets/CapsChaos/Application/Game.Application.asmdef", new string[]
            {
                "Game.Domain",
                "Game.Gen",
                "ClassicSpins.PrototypeFramework.Application",
                "ClassicSpins.PrototypeFramework.Domain",
            }),
            new Row("Assets/CapsChaos/Presentation/Game.Presentation.asmdef", new string[]
            {
                "Game.Application",
                "Game.Domain",
                "Game.Gen",
                "Game.Views",
                "ClassicSpins.PrototypeFramework.Presentation",
                "ClassicSpins.PrototypeFramework.Views",
                "ClassicSpins.PrototypeFramework.Application",
                "ClassicSpins.PrototypeFramework.Domain",
                "UniTask",
            }),
            new Row("Assets/CapsChaos/Infrastructure/Game.Infrastructure.asmdef", new string[]
            {
                "Game.Presentation",
                "Game.Application",
                "Game.Domain",
                "Game.Gen",
                "Game.Views",
                "ClassicSpins.PrototypeFramework.Infrastructure",
                "ClassicSpins.PrototypeFramework.Presentation",
                "ClassicSpins.PrototypeFramework.Views",
                "ClassicSpins.PrototypeFramework.Application",
                "ClassicSpins.PrototypeFramework.Domain",
                "UniTask",
                "Unity.Addressables",
                "Unity.ResourceManager",
            }),
            new Row("Assets/CapsChaos/Composition/Game.Composition.asmdef", new string[]
            {
                "Game.Infrastructure",
                "Game.Presentation",
                "Game.Application",
                "Game.Domain",
                "Game.Gen",
                "Game.Views",
                "ClassicSpins.PrototypeFramework.Composition",
                "ClassicSpins.PrototypeFramework.Infrastructure",
                "ClassicSpins.PrototypeFramework.Presentation",
                "ClassicSpins.PrototypeFramework.Views",
                "ClassicSpins.PrototypeFramework.Application",
                "ClassicSpins.PrototypeFramework.Domain",
                "UniTask",
                "VContainer",
            }),
            new Row("Assets/CapsChaos/Views/Game.Views.asmdef", new string[]
            {
                "ClassicSpins.PrototypeFramework.Views",
                "Unity.TextMeshPro",
                "LitMotion",
                "UniTask",
                // spline conveyors (ConveyorBeltView, ported from ConveyorKit) — approved by the product owner 2026-10-05
                "Unity.Splines",
                "Unity.Mathematics",
                "SplineMesh.Runtime",
            }),
            new Row("Assets/CapsChaos/Gen/Game.Gen.asmdef", new string[]
            {
                "ClassicSpins.PrototypeFramework.Domain",
            }),
            new Row("Assets/CapsChaos/Editor/Game.Editor.asmdef", new string[]
            {
            }),
            new Row("Assets/CapsChaos/Tests/Editor/Game.Tests.Editor.asmdef", new string[]
            {
                "Game.Editor",
                "Game.Domain",
                "Game.Application",
                "Game.Gen",
                "Game.Views",
                "Game.Presentation",
                "Game.Infrastructure",
                "Game.Composition",
                "ClassicSpins.PrototypeFramework.Domain",
                "ClassicSpins.PrototypeFramework.Application",
                "ClassicSpins.PrototypeFramework.Presentation",
                "ClassicSpins.PrototypeFramework.Composition",
                "Unity.TextMeshPro",
                "UnityEngine.TestRunner",
                "UnityEditor.TestRunner",
                "UniTask",
                "VContainer",
            }),
        };

        /// <summary>The tiers the engine-free gate globs — they must keep <c>noEngineReferences: true</c>.</summary>
        private static readonly string[] EngineFree = { "Game.Domain", "Game.Application", "Game.Gen" };

        /// <summary>The tiers that must never reach a player build.</summary>
        private static readonly string[] EditorOnly = { "Game.Editor", "Game.Tests.Editor" };

        private static JObject Read(string asmdef)
        {
            var path = RepoLayout.Path(asmdef.Split('/'));
            Assert.That(File.Exists(path), Is.True, asmdef + ": not found — the table names an asmdef the SKU does not have.");
            return JObject.Parse(File.ReadAllText(path));
        }

        private static IReadOnlyList<string> References(JObject asmdef)
            => asmdef["references"] is JArray array
                ? array.Select(t => t.Value<string>() ?? string.Empty).ToArray()
                : Array.Empty<string>();

        [TestCaseSource(nameof(Authorised))]
        public void Each_asmdef_references_exactly_its_authorised_set(Row row)
        {
            var references = References(Read(row.Asmdef));

            Assert.That(references.Distinct(StringComparer.Ordinal), Has.Exactly(references.Count).Items,
                row.Asmdef + " lists a duplicate reference: " + string.Join(", ", references));

            Assert.That(references, Is.EquivalentTo(row.References),
                row.Asmdef + " references [" + string.Join(", ", references) + "] but the table authorises ["
                + string.Join(", ", row.References) + "]. A new reference edge is an AD-1 spine decision, "
                + "not a patch — route it, then change the asmdef and this row together.");
        }

        [Test]
        public void No_asmdef_exists_that_the_table_does_not_name()
        {
            var root = RepoLayout.Path("Assets", "CapsChaos");
            Assert.That(Directory.Exists(root), Is.True, root + ": not found.");

            var prefix = RepoLayout.RepoRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var onDisk = Directory.GetFiles(root, "*.asmdef", SearchOption.AllDirectories)
                .Select(p => p.Substring(prefix.Length).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            var named = Authorised.Select(r => r.Asmdef).OrderBy(p => p, StringComparer.Ordinal).ToArray();

            Assert.That(onDisk, Is.EqualTo(named),
                "the SKU's asmdefs and the authorised table disagree. A new assembly beyond the spine is an "
                + "AD-1 decision, not a patch — route it, then add its row here.");
        }

        [Test]
        public void The_engine_free_tiers_declare_no_engine_references()
        {
            foreach (var row in Authorised.Where(r => EngineFree.Contains(Path.GetFileNameWithoutExtension(r.Asmdef))))
                Assert.That(Read(row.Asmdef)["noEngineReferences"]?.Value<bool>(), Is.True,
                    row.Asmdef + " must keep noEngineReferences: true — it is what lets the engine-free gate "
                    + "compile it without Unity, and what turns an engine type in a port into a compile error.");
        }

        [Test]
        public void The_editor_and_test_assemblies_stay_editor_only()
        {
            foreach (var row in Authorised.Where(r => EditorOnly.Contains(Path.GetFileNameWithoutExtension(r.Asmdef))))
                Assert.That(Read(row.Asmdef)["includePlatforms"]?.Select(t => t.Value<string>()).ToArray(),
                    Is.EqualTo(new[] { "Editor" }),
                    row.Asmdef + " is Editor-only; widening its platforms would ship tooling or tests into a player build.");
        }
    }
}
