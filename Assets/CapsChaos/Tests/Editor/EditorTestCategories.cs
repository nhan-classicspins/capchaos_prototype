namespace Game.Tests.Editor
{
    /// <summary>
    /// The NUnit category names this EditMode tree uses.
    ///
    /// <para><see cref="Slow"/> marks a class that renders or captures: a capture or floor fixture
    /// whose <c>OneTimeSetUp</c> draws a surface and reads its pixels back. Measured on a SKU,
    /// synchronous and unfocused: nine such classes took ~260 s of a 277 s suite, the other 38
    /// classes ~18 s. <c>Tools/pf/pf-tests --all</c> skips this tier; <c>--auto</c> includes it only
    /// when the working tree touches a path that draws.</para>
    ///
    /// <para>Constants, not string literals: a typo'd <c>[Category("Slwo")]</c> would quietly put a
    /// capture fixture back into the fast tier. A typo'd constant does not compile.</para>
    ///
    /// <para>Emitted by the Setup Wizard's test kit. This file is the SKU's.</para>
    /// </summary>
    public static class EditorTestCategories
    {
        /// <summary>Renders or captures; skipped by <c>pf-tests --all</c>.</summary>
        public const string Slow = "Slow";
    }
}
