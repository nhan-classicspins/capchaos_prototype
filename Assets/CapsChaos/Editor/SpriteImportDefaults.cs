using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>
    /// Closes the entry point of the SKU ruler <b>1 texture px == 1 canvas reference px == 1 world
    /// unit</b> by importing every sprite under <c>Assets/CapsChaos/</c> at
    /// <c>spritePixelsPerUnit = 1</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Emitted by the framework Setup Wizard (generate-once) — this file is yours now.</b> The
    /// template lives inside the pinned framework package and cannot be lost; THIS COPY CAN. If it goes
    /// missing, <c>Framework/Doctor</c> check 14 names it, and re-running the Setup Wizard (or the CLI's
    /// <c>emit --what spriteimport</c>) restores it verbatim. A re-emit never clobbers an existing file,
    /// so edits made here survive. To change the behaviour, EDIT THIS FILE — there is deliberately no
    /// opt-out flag.
    /// </para>
    ///
    /// <para><b>Why this exists.</b> Unity imports sprites at PPU 100, so a 100x100 px texture becomes
    /// a 1x1 unit sprite — a silent 100x error with no gate. This is the other half of the chain whose
    /// runtime half is <c>WorldSpaceCanvasScaler</c>, which pins
    /// <c>Canvas.referencePixelsPerUnit = 1</c>. uGUI computes
    /// <c>Image.pixelsPerUnit = sprite.pixelsPerUnit / canvas.referencePixelsPerUnit</c>; only 1/1 == 1
    /// makes <c>SetNativeSize()</c> on a 100x100 sprite produce a 100x100 rect. Sizing therefore comes
    /// from PPU, which leaves <c>localScale</c> free for animation.</para>
    ///
    /// <para><b>Why every decision is logged.</b> A silent no-op is a defect: an import that quietly
    /// declines to act is indistinguishable from a postprocessor that never ran. So every <i>in-scope
    /// sprite</i> emits exactly one line naming the asset path and the PPU actually in force. Severity
    /// splits by meaning — "already 1" is informational (the invariant holds, the line only proves the
    /// hook ran), while "PPU != 1" is a warning that spells the consequence out in units, because that
    /// deviation <i>is</i> the silent 100x error, and the reader must not have to do the division.</para>
    ///
    /// <para><b>The order constant is the override contract.</b> <see cref="GetPostprocessOrder"/>
    /// returns <see cref="PostprocessOrder"/>, deliberately ABOVE Unity's own
    /// <c>PresetManagerPostProcessor</c> (which sits at -1000; a LOWER order runs FIRST). Two
    /// consequences, both intended: a texture Preset does not beat this default, and any postprocessor
    /// left at the default order 0 does. That second one is the escape hatch — a subfolder needing a
    /// different PPU gets its own postprocessor at order 0, with no edit here and none in the
    /// framework.</para>
    ///
    /// <para><b>Naming.</b> The C# member is <see cref="TextureImporter.spritePixelsPerUnit"/>.
    /// <c>spritePixelsToUnits</c> is the <c>.meta</c> YAML key (a legacy C# alias also resolves — do
    /// not use it), which is why the two names differ between this file and a verification grep.</para>
    ///
    /// <para><b>Not overridden on purpose: <c>GetVersion()</c>.</b> Bumping it force-reimports every
    /// texture in the project, and the <c>importSettingsMissing</c> guard below makes re-application
    /// impossible anyway — pure churn with zero effect.</para>
    ///
    /// <para><b>Not covered: <c>.psd</c>.</b> A .psd is imported by PSDImporter, not by
    /// <see cref="TextureImporter"/>, so guard 2 returns early and .psd sprites still land at PPU 100.
    /// Covering them means handling a second importer type.</para>
    /// </remarks>
    internal sealed class SpriteImportDefaults : AssetPostprocessor
    {
        /// <summary>Console tag, so a project-wide reimport can be grepped for scope leaks.</summary>
        private const string Tag = "[SpriteImport]";

        /// <summary>The one folder this default owns. Widening it is an intent change, not a tweak.</summary>
        private const string ScopeRoot = "Assets/CapsChaos/";

        /// <summary>The invariant: one texture pixel is one world unit.</summary>
        private const float TargetPixelsPerUnit = 1f;

        /// <summary>A reference edge length, used only to state the override's consequence in units.</summary>
        private const float ReferenceEdgePixels = 100f;

        /// <summary>Above PresetManagerPostProcessor (-1000), below the default 0 — see the class
        /// remarks for what each side of that buys. Named rather than inlined because it is a contract:
        /// a SKU overrides this default by out-ordering it.</summary>
        private const int PostprocessOrder = -100;

        public override int GetPostprocessOrder() => PostprocessOrder;

        private void OnPreprocessTexture()
        {
            // GUARD 1 — folder scope. Ordinal, because this is a path, not prose; a culture-aware
            // compare on an asset path is a latent bug, not a style preference.
            //
            // This returns BEFORE any inspection or logging on purpose. OnPreprocessTexture fires for
            // every texture in the project — packages, TextMesh Pro, third-party art. A log placed
            // above this line would fire on every unrelated import and drown the signal it exists to
            // carry. "No log" here is the specified behaviour, not an oversight.
            if (!assetPath.StartsWith(ScopeRoot, StringComparison.Ordinal))
                return;

            if (assetImporter is not TextureImporter importer)
                return;

            // GUARD 2 — sprites only. A Default / NormalMap / Cursor texture has no meaningful PPU;
            // writing one would be noise, and logging one would be noise about noise. Silent return.
            //
            // With the editor in 2D behaviour mode a freshly added .png already arrives here typed
            // Sprite, which is why these two guards suffice on a first import. In a 3D-mode project the
            // asset arrives typed Default and falls to the audit branch below when it is later retyped.
            if (importer.textureType != TextureImporterType.Sprite)
                return;

            // GUARD 3 — first import only. DO NOT "simplify" this into "write whenever PPU != 1".
            //
            // The stronger rule would enforce the invariant forever, but it would also silently revert
            // a deliberate per-asset override on the very next re-import — an edit that vanishes with
            // no diff is worse than a wrong number that is visible. This guard leaves authorship with
            // the human; the warning below keeps any deviation audible. That pairing is the whole
            // reason the weaker guard is safe.
            //
            // importSettingsMissing is true exactly once per asset: on the import that has no .meta
            // yet. Every later re-import — including one caused by the user flipping the texture type
            // to Sprite after the fact — reads false and falls through to the audit branch.
            if (importer.importSettingsMissing)
            {
                importer.spritePixelsPerUnit = TargetPixelsPerUnit;
                Debug.Log(
                    $"{Tag} Applied spritePixelsPerUnit = {Format(TargetPixelsPerUnit)} on first import: " +
                    $"'{assetPath}'. 1 texture px == 1 world unit.");
                return;
            }

            // From here on we only ever REPORT. Two severities, because the two states mean different
            // things to the reader.
            float current = importer.spritePixelsPerUnit;

            if (Mathf.Approximately(current, TargetPixelsPerUnit))
            {
                Debug.Log(
                    $"{Tag} No-op (settings already authored): '{assetPath}' honours " +
                    $"spritePixelsPerUnit = {Format(current)}. Invariant holds.");
                return;
            }

            // An override is legitimate — a warning, never a failure. State the consequence in units
            // so the deviation reads as a fact about the asset rather than a number to interpret.
            //
            // current is guaranteed non-zero here: Unity clamps spritePixelsPerUnit to >= 0.001 on
            // import, and the Approximately branch above already caught the near-1 case. The guard
            // below costs nothing and keeps a hand-edited .meta from printing "Infinity" instead of a
            // diagnosis.
            string consequence = Mathf.Abs(current) < 0.0001f
                ? "an undefined size (its PPU is zero — the .meta is hand-edited and invalid)"
                : $"{Format(ReferenceEdgePixels / current)} instead of {Format(ReferenceEdgePixels)} world units";

            Debug.LogWarning(
                $"{Tag} No-op (per-asset override preserved): '{assetPath}' imports at " +
                $"spritePixelsPerUnit = {Format(current)}, not {Format(TargetPixelsPerUnit)}. " +
                $"A {Format(ReferenceEdgePixels)} px edge becomes {consequence} — the 1 px == 1 unit " +
                $"ruler does not hold for this asset. " +
                $"Fix it in the Inspector (Pixels Per Unit = 1). Do NOT delete the .meta to re-trigger " +
                $"the first-import default: that re-mints the asset's GUID and silently breaks every " +
                $"prefab, scene and Addressables entry referencing it. Assets imported before this " +
                $"postprocessor existed land here.");
        }

        /// <summary>
        /// Culture-invariant, no trailing zeros — so a log line reads "1" and "100", not "1.00" or a
        /// comma-decimal on a European editor locale.
        /// </summary>
        private static string Format(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
