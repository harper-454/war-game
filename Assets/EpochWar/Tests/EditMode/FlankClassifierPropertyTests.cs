using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="FlankClassifier"/> (Requirement 9.4), exercised for at
    /// least the design-mandated minimum of 100 generated iterations (design.md "Testing Strategy").
    ///
    /// Covered property, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 3</c>:
    /// <list type="bullet">
    ///   <item>
    ///     Property 3 — Flank classification is total and mutually exclusive: for any defender facing
    ///     and any attacker position, <see cref="FlankClassifier.Classify"/> returns exactly one of
    ///     Front/Side/Rear and is deterministic (identical inputs always classify identically).
    ///   </item>
    /// </list>
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class FlankClassifierPropertyTests
    {
        private const int MinimumIterations = 100;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        /// <summary>
        /// A generated flanking geometry: a defender facing angle, defender and attacker horizontal
        /// coordinates, and the two arc thresholds. Coordinates deliberately include the degenerate
        /// "attacker on top of the defender" case so totality is exercised there too.
        /// </summary>
        public sealed class FlankScenario
        {
            public int FacingDegrees;
            public int DefenderX;
            public int DefenderZ;
            public int AttackerX;
            public int AttackerZ;
            public int FrontArcDegrees;
            public int SideArcDegrees;

            public override string ToString()
                => $"FlankScenario(face={FacingDegrees}, def=({DefenderX},{DefenderZ}), " +
                   $"atk=({AttackerX},{AttackerZ}), front={FrontArcDegrees}, side={SideArcDegrees})";
        }

        private static Arbitrary<FlankScenario> FlankScenarios()
        {
            var gen = from facing in Gen.Choose(0, 359)
                      from dx in Gen.Choose(-50, 50)
                      from dz in Gen.Choose(-50, 50)
                      from ax in Gen.Choose(-50, 50)
                      from az in Gen.Choose(-50, 50)
                      from front in Gen.Choose(0, 180)
                      from side in Gen.Choose(0, 180)
                      select new FlankScenario
                      {
                          FacingDegrees = facing,
                          DefenderX = dx,
                          DefenderZ = dz,
                          AttackerX = ax,
                          AttackerZ = az,
                          FrontArcDegrees = front,
                          SideArcDegrees = side,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 3: Flank classification is total and mutually exclusive.
        ///
        /// For any defender facing direction and any attacker position, <see cref="FlankClassifier.Classify"/>
        /// returns exactly one of <see cref="Flank.Front"/>/<see cref="Flank.Side"/>/<see cref="Flank.Rear"/>
        /// (no input is unclassified or ambiguous), and the same inputs always classify to the same
        /// result.
        ///
        /// **Validates: Requirements 9.4**
        /// </summary>
        [Test]
        [Category("Property 3")]
        public void Property3_FlankClassificationIsTotalAndDeterministic()
        {
            Prop.ForAll(FlankScenarios(), scenario =>
            {
                var facing = FacingDirection.FromDegrees(scenario.FacingDegrees);
                var defenderPos = WorldPosition.FromInts(scenario.DefenderX, 0, scenario.DefenderZ);
                var attackerPos = WorldPosition.FromInts(scenario.AttackerX, 0, scenario.AttackerZ);
                Fixed frontArc = Fixed.FromInt(scenario.FrontArcDegrees);
                Fixed sideArc = Fixed.FromInt(scenario.SideArcDegrees);

                Flank result = FlankClassifier.Classify(facing, defenderPos, attackerPos, frontArc, sideArc);

                // Totality + mutual exclusivity: the result is exactly one of the three defined values.
                bool isOneOfThree =
                    result == Flank.Front || result == Flank.Side || result == Flank.Rear;
                if (!isOneOfThree)
                {
                    return false;
                }

                // Determinism: re-evaluating identical inputs yields the identical classification.
                Flank again = FlankClassifier.Classify(facing, defenderPos, attackerPos, frontArc, sideArc);
                return again == result;
            }).Check(Config());
        }
    }
}
