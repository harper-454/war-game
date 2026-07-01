using System.Linq;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="CombatSystem"/> (Requirement 3.7), validating the
    /// universal correctness property from design.md, exercised for at least the design-mandated
    /// minimum of 100 generated iterations (see design.md, "Testing Strategy").
    ///
    /// Covered property, tagged <c>Feature: epoch-war-game, Property N</c>:
    /// <list type="bullet">
    ///   <item>Property 16 — Combat damage formula and clamping (Req 3.7).</item>
    /// </list>
    ///
    /// Scenarios target the engine-free <c>EpochWar.Core</c> assembly with no Unity Play loop; with
    /// no governance adopted the effective attack/defense equal the base values, so the damage the
    /// system applies is exactly <see cref="CombatSystem.ComputeDamage"/>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class CombatSystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefOf(int attack, int defense, int maxHealth)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f, UnitRole.Soldier);

        private static (MatchState state, CombatSystem combat) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ);
            var combat = new CombatSystem(civ, units);
            var state = new MatchState(new TerrainVolume(new Int3(4, 4, 4), CellMaterial.Soil));
            return (state, combat);
        }

        /// <summary>
        /// A generated combat scenario: an attacker attack value, a defender defense value, a
        /// positive defender health, and a non-negative attack increment used to check monotonicity.
        /// </summary>
        public sealed class CombatScenario
        {
            public int Attack;
            public int Defense;
            public int Health;
            public int AttackIncrement;

            public override string ToString()
                => $"CombatScenario(atk={Attack}, def={Defense}, hp={Health}, +atk={AttackIncrement})";
        }

        private static Arbitrary<CombatScenario> CombatScenarios()
        {
            var gen = from attack in Gen.Choose(0, 500)
                      from defense in Gen.Choose(0, 500)
                      from health in Gen.Choose(1, 1000)
                      from increment in Gen.Choose(0, 500)
                      select new CombatScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          Health = health,
                          AttackIncrement = increment,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 16: Combat damage formula and clamping.
        ///
        /// For any attacking and defending Unit, combat reduces the defender's health by an amount
        /// derived from the attacker's attack and the defender's defense (<c>max(1, attack - defense/2)</c>),
        /// the resulting health is never negative, and greater attacker attack never produces less
        /// damage (the formula is non-decreasing in attack).
        ///
        /// **Validates: Requirements 3.7**
        /// </summary>
        [Test]
        [Category("Property 16")]
        public void Property16_CombatDamageFormulaAndClamping()
        {
            Prop.ForAll(CombatScenarios(), scenario =>
            {
                int expectedDamage = System.Math.Max(1, scenario.Attack - (scenario.Defense / 2));

                // The pure formula matches the design definition and is at least one.
                int damage = CombatSystem.ComputeDamage(scenario.Attack, scenario.Defense);
                if (damage != expectedDamage || damage < 1)
                {
                    return false;
                }

                // Non-decreasing in attack: a greater attack never yields less damage.
                int damageWithMoreAttack = CombatSystem.ComputeDamage(
                    scenario.Attack + scenario.AttackIncrement, scenario.Defense);
                if (damageWithMoreAttack < damage)
                {
                    return false;
                }

                // Applied to real units (no governance => effective == base), health is reduced by
                // exactly the computed damage and clamped so it is never negative (Req 3.7 / 3.5).
                var (state, combat) = Build();
                state.Nations[1] = new Nation(1);
                state.Nations[2] = new Nation(2);
                state.Units[1] = new UnitInstance(1, 1, UnitDefOf(scenario.Attack, 0, 100), WorldPosition.Zero);
                state.Units[2] = new UnitInstance(2, 2, UnitDefOf(0, scenario.Defense, scenario.Health), WorldPosition.Zero);

                var events = combat.ResolveAttack(state, 1, 2);
                var resolved = events.OfType<CombatResolvedEvent>().SingleOrDefault();
                if (resolved == null)
                {
                    return false;
                }

                int expectedNewHealth = System.Math.Max(0, scenario.Health - expectedDamage);

                if (resolved.Damage != expectedDamage
                    || resolved.DefenderOldHealth != scenario.Health
                    || resolved.DefenderNewHealth != expectedNewHealth
                    || resolved.DefenderNewHealth < 0)
                {
                    return false;
                }

                // A defender reduced to zero health is removed; otherwise it retains the clamped health.
                if (expectedNewHealth <= 0)
                {
                    return !state.Units.ContainsKey(2);
                }

                return state.Units.ContainsKey(2) && state.Units[2].Health == expectedNewHealth;
            }).Check(Config());
        }
    }
}
