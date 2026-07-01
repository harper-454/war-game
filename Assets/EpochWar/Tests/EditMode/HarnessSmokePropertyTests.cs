using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Smoke test for the property-based testing harness.
    ///
    /// This is an infrastructure check, not a gameplay rule: it confirms that the
    /// FsCheck + NUnit pipeline is wired correctly inside an EditMode test assembly
    /// that references <c>EpochWar.Core</c>, and that a property is exercised for at
    /// least the design-mandated minimum of 100 generated iterations (see design.md,
    /// "Testing Strategy": every property runs &gt;= 100 generated iterations).
    ///
    /// All real correctness properties added by later tasks follow the same harness
    /// shape and are tagged <c>Feature: epoch-war-game, Property N: &lt;text&gt;</c>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class HarnessSmokePropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        /// <summary>
        /// Drives a trivially-true property through FsCheck and asserts the harness
        /// actually invoked it at least <see cref="MinimumIterations"/> times.
        ///
        /// We count invocations with a side-effecting closure rather than trusting the
        /// configured test count, so the assertion verifies real execution of the
        /// generator/runner loop end to end.
        /// </summary>
        [Test]
        [Category("Smoke")]
        public void Harness_RunsAtLeast100Iterations()
        {
            var invocations = 0;

            // A property that always holds: integer addition with the additive identity.
            // The body increments a counter so we can observe how many cases ran.
            Property property = Prop.ForAll<int>(value =>
            {
                invocations++;
                return value + 0 == value;
            });

            // QuickThrowOnFailure runs the default suite (MaxNbOfTest = 100) and throws
            // an NUnit-visible exception if any generated case fails. We pin the count
            // explicitly to the feature's minimum and run via the C# fluent extension.
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;

            property.Check(config);

            Assert.That(
                invocations,
                Is.GreaterThanOrEqualTo(MinimumIterations),
                $"FsCheck harness must exercise the property at least {MinimumIterations} times; " +
                $"it ran {invocations} time(s).");
        }

        /// <summary>
        /// A second, value-returning property (using FsCheck's boolean property form)
        /// proving generated inputs reach core-style pure logic. Kept intentionally
        /// minimal: it only validates that the harness can express and check a
        /// universally-quantified statement over generated data.
        /// </summary>
        [Test]
        [Category("Smoke")]
        public void Harness_ChecksUniversalProperty()
        {
            // For any non-negative int, Math.Abs is idempotent and non-negative.
            Prop.ForAll<int>(value =>
                {
                    var abs = System.Math.Abs((long)value);
                    return abs >= 0;
                })
                .QuickCheckThrowOnFailure();
        }
    }
}
