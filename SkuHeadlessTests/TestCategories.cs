namespace CapsChaos.SkuHeadlessTests
{
    /// <summary>
    /// The NUnit category names this test tree uses.
    ///
    /// <para>They are constants rather than string literals for one reason: the rung-1 gate runs
    /// <c>--filter "TestCategory!=Slow"</c>, so a typo'd <c>[Category("Slwo")]</c> would silently
    /// create an UNFILTERED slow test — green, and quietly costing the gate its budget. A typo'd
    /// <c>TestCategories.Slwo</c> does not compile.</para>
    /// </summary>
    public static class TestCategories
    {
        /// <summary>
        /// A test the default gate filter excludes: a sweep, a soak, or anything else that is not
        /// allowed to sit inside the rung-1 time bar. Mark it <c>[Slow]</c> (SlowTestAttribute.cs),
        /// never <c>[Category(TestCategories.Slow)]</c>.
        /// </summary>
        public const string Slow = "Slow";
    }
}
