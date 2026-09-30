using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace CapsChaos.SkuHeadlessTests.Gate
{
    /// <summary>
    /// Proves the slow set stops itself, whatever command started the run.
    ///
    /// <para>The gate's filter is a claim about the COMMAND the gate runs. It says nothing about a
    /// command nobody wrote down: a bare <c>dotnet test &lt;project&gt;</c>, an IDE's Run All, a new CI
    /// step. <see cref="SlowAttribute"/> closes that gap in the TEST, and these are its proofs.</para>
    /// </summary>
    [TestFixture]
    public sealed class SlowOptInTests
    {
        [Test]
        public void Every_Slow_test_reaches_the_category_through_the_Slow_attribute()
        {
            var strays = new List<string>();

            foreach (var type in typeof(SlowOptInTests).Assembly.GetTypes())
            {
                foreach (var attribute in type.GetCustomAttributes<CategoryAttribute>(inherit: true))
                {
                    if (attribute.Name == TestCategories.Slow && attribute is not SlowAttribute)
                    {
                        strays.Add(type.Name);
                    }
                }

                foreach (var method in type.GetMethods(
                             BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    foreach (var attribute in method.GetCustomAttributes<CategoryAttribute>(inherit: true))
                    {
                        if (attribute.Name == TestCategories.Slow && attribute is not SlowAttribute)
                        {
                            strays.Add(type.Name + "." + method.Name);
                        }
                    }
                }
            }

            Assert.That(strays, Is.Empty,
                "these carry the Slow category without the guard that stops them — write [Slow], "
                + "never [Category(TestCategories.Slow)]: " + string.Join(", ", strays));
        }

        [Test]
        public void An_opted_out_run_ignores_the_test_and_says_how_to_run_it()
        {
            var previous = Environment.GetEnvironmentVariable(SlowGate.OptInVariable);
            try
            {
                Environment.SetEnvironmentVariable(SlowGate.OptInVariable, null);

                var probe = new TestSuite("probe");
                ((IApplyToTest)new SlowAttribute()).ApplyToTest(probe);

                Assert.Multiple(() =>
                {
                    Assert.That(probe.RunState, Is.EqualTo(RunState.Ignored));
                    Assert.That(probe.Properties.Get(PropertyNames.SkipReason),
                        Is.EqualTo(SlowGate.SkipReason));
                    Assert.That(probe.Properties[PropertyNames.Category], Does.Contain(TestCategories.Slow),
                        "an ignored slow test must still carry the category the gate filter names");
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable(SlowGate.OptInVariable, previous);
            }
        }

        [Test]
        public void An_opted_in_run_leaves_the_test_runnable()
        {
            var previous = Environment.GetEnvironmentVariable(SlowGate.OptInVariable);
            try
            {
                Environment.SetEnvironmentVariable(SlowGate.OptInVariable, "1");

                var probe = new TestSuite("probe");
                ((IApplyToTest)new SlowAttribute()).ApplyToTest(probe);

                Assert.That(probe.RunState, Is.EqualTo(RunState.Runnable));
            }
            finally
            {
                Environment.SetEnvironmentVariable(SlowGate.OptInVariable, previous);
            }
        }

        [TestCase("1", true)]
        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("yes", true)]
        [TestCase(" 1 ", true)]
        [TestCase("0", false)]
        [TestCase("false", false)]
        [TestCase("", false)]
        [TestCase("  ", false)]
        [TestCase(null, false)]
        public void The_opt_in_value_is_parsed_the_way_the_skip_reason_promises(string? value, bool expected)
        {
            Assert.That(SlowGate.IsOptIn(value), Is.EqualTo(expected));
        }

        [Test]
        public void The_skip_reason_names_the_variable_that_turns_the_set_on()
        {
            Assert.That(SlowGate.SkipReason, Does.Contain(SlowGate.OptInVariable),
                "the skip line is the only place a reader learns how to run these — it must name the flag");
        }

        [Test]
        public void The_category_is_the_word_the_gate_filter_and_pf_tests_key_on()
        {
            Assert.That(TestCategories.Slow, Is.EqualTo("Slow"),
                "Tools/pf/gate.sh filters `TestCategory!=Slow` and Tools/pf/pf-tests splits its tier on "
                + "[Category(\"Slow\")]; renaming the constant silently un-filters every slow test");
        }
    }
}
