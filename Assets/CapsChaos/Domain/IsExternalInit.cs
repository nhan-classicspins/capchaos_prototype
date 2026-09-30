#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler shim enabling <c>init</c>-only setters (which positional <c>record</c>s like the
    /// scaffolded <c>MainParam</c> generate) on Unity's mono / netstandard2.1 profile.
    /// <c>IsExternalInit</c> ships natively in .NET 5+, so this internal copy is guarded out under
    /// <c>NET5_0_OR_GREATER</c>.
    ///
    /// <para><b>This copy covers <c>Game.Domain</c> and nothing else</b> — the type is
    /// <c>internal</c>, and the AD-1 spine gives this SKU eight assemblies. One copy per assembly that
    /// needs one. Seeded by the Setup Wizard (generate-once).</para>
    /// </summary>
    internal static class IsExternalInit { }
}
#endif
