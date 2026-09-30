using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace CapsChaos.SkuHeadlessTests
{
    /// <summary>
    /// The opt-in switch the <see cref="SlowAttribute"/> reads.
    ///
    /// <para><b>Why an opt-in at all.</b> The gate already excludes the slow set by filter
    /// (<c>TestCategory!=Slow</c> in <c>Tools/pf/gate.sh</c>), and that is enough — as long as every
    /// caller remembers the filter. One that did not cost a SKU ~35 minutes of wall clock: a bare
    /// <c>dotnet test &lt;project&gt;</c> pulled in a 10,000-level generator sweep, sat at 98% CPU
    /// across four invocations, and was finally killed by vstest's blame collector as a hung test
    /// host. The filter is a property of the COMMAND; this is a property of the TEST, so it holds for
    /// every command anyone can type — an agent's, a human's Run All in an IDE, a fresh CI step.</para>
    ///
    /// <para><b>And it is never silent.</b> An opted-out run reports the fixtures as Skipped with the
    /// reason below, so "the sweep did not run" is on the summary line rather than assumed.</para>
    /// </summary>
    public static class SlowGate
    {
        /// <summary>The environment variable that turns the slow set on.</summary>
        public const string OptInVariable = "PF_RUN_SLOW";

        /// <summary>Shown on every skipped slow test, so the way to run it travels with the skip.</summary>
        public const string SkipReason =
            "Slow: a sweep or soak that is deliberately outside the per-edit loop. "
            + "Run it with " + OptInVariable + "=1 (the rung-1 gate excludes it with "
            + "TestCategory!=Slow; a dedicated CI step is where it belongs).";

        /// <summary>True when this process was told to run the slow set.</summary>
        public static bool OptedIn => IsOptIn(Environment.GetEnvironmentVariable(OptInVariable));

        /// <summary>
        /// The parse, exposed so it can be proved without mutating the process environment.
        /// Accepts <c>1</c>, <c>true</c> and <c>yes</c> in any casing; everything else — including
        /// an unset variable and an empty one — is opted out.
        /// </summary>
        public static bool IsOptIn(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var trimmed = value.Trim();
            return trimmed.Equals("1", StringComparison.Ordinal)
                || trimmed.Equals("true", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Marks a sweep, soak or fuzz: it carries the <see cref="TestCategories.Slow"/> category AND
    /// ignores itself unless <see cref="SlowGate.OptedIn"/>.
    ///
    /// <para><b>One attribute, deliberately.</b> The category and the guard cannot be applied
    /// separately, so there is no way to write a slow test that the filter names but nothing stops
    /// when the filter is missing. <c>SlowOptInTests</c> asserts that no test reaches the category by
    /// any other route.</para>
    ///
    /// <para>It derives from <see cref="CategoryAttribute"/> rather than implementing
    /// <see cref="IApplyToTest"/> alone so that reflection over <c>CategoryAttribute</c> still sees
    /// it.</para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class SlowAttribute : CategoryAttribute, IApplyToTest
    {
        public SlowAttribute()
            : base(TestCategories.Slow)
        {
        }

        /// <summary>
        /// Re-implements the interface rather than overriding: NUnit 4's
        /// <c>CategoryAttribute.ApplyToTest</c> is not virtual, so the only seam is
        /// <see cref="IApplyToTest"/> itself — and a derived class that re-declares the interface
        /// wins interface dispatch. The category line is therefore applied here rather than
        /// delegated; <c>SlowOptInTests</c> proves both halves land.
        /// </summary>
        void IApplyToTest.ApplyToTest(Test test)
        {
            test.Properties.Add(PropertyNames.Category, Name);

            if (SlowGate.OptedIn)
            {
                return;
            }

            // Not Explicit: an Explicit test is selectable by an exact-name filter, which is how a
            // wide `dotnet test` reaches it again. Ignored is the state that says "not this run".
            test.RunState = RunState.Ignored;
            test.Properties.Set(PropertyNames.SkipReason, SlowGate.SkipReason);
        }
    }
}
