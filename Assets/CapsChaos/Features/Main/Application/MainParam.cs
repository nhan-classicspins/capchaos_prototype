namespace Game.Application
{
    /// <summary>
    /// The typed per-screen param for MainScreen (the three-file pattern: &lt;X&gt;Param immutable
    /// record). Injected into the screen scope <b>before</b> Configure by the scene service, so
    /// MainScreen ctor-injects it type-safely. Immutable positional <c>record</c> (C# 9 — no
    /// <c>record struct</c> on the SKU; the Game.Application IsExternalInit shim covers <c>init</c>
    /// setters). Engine-free by construction — that is what puts it inside the headless gate.
    /// Scaffolded by Scaffold.Sync (generate-once) — replace the placeholder field with the real params.
    /// </summary>
    public sealed record MainParam(bool ColdBoot);
}
