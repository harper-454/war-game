using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for <see cref="CombatSystem"/> (Requirement 3.7).
    ///
    /// These cover concrete, named scenarios for the damage formula and clamping, defender removal
    /// on zero health (3.5), governance attack/defense multipliers (5.5), and the no-op cases. They
    /// complement the universal FsCheck property added by the optional task 9.8.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class CombatSystemTests
    {
        private static UnitDef UnitDef(string id, int attack, int defense, int maxHealth, int ownerAttackNa = 0)
            => new UnitDef(id, Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f, UnitRole.Soldier);

        private static (MatchState state, CombatSystem combat, CivSystem civ) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ);
            var combat = new CombatSystem(civ, units);
            var state = new MatchState(new TerrainVolume(new Int3(4, 4, 4), CellMaterial.Soil));
            return (state, combat, civ);
        }

        [Test]
        public void ComputeDamage_UsesAttackMinusHalfDefense_FlooredAtOne()
        {
            Assert.That(CombatSystem.ComputeDamage(5, 4), Is.EqualTo(3), "5 - 4/2 = 3");
            Assert.That(CombatSystem.ComputeDamage(10, 6), Is.EqualTo(7), "10 - 6/2 = 7");
            Assert.That(CombatSystem.ComputeDamage(3, 100), Is.EqualTo(1), "clamped to a minimum of 1");
            Assert.That(CombatSystem.ComputeDamage(20, 0), Is.EqualTo(20), "no defense mitigation");
        }

        [Test]
        public void ComputeDamage_IsNonDecreasingInAttack()
        {
            const int defense = 8;
            int previous = CombatSystem.ComputeDamage(0, defense);
            for (int attack = 1; attack <= 200; attack++)
            {
                int current = CombatSystem.ComputeDamage(attack, defense);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous),
                    "greater attack must never yield less damage");
                previous = current;
            }
        }

        [Test]
        public void ResolveAttack_ReducesDefenderHealthByComputedDamage()
        {
            var (state, combat, _) = Build();
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            state.Units[1] = new UnitInstance(1, 1, UnitDef("atk", attack: 10, defense: 0, maxHealth: 30), WorldPosition.Zero);
            state.Units[2] = new UnitInstance(2, 2, UnitDef("def", attack: 0, defense: 4, maxHealth: 30), WorldPosition.Zero);

            var events = combat.ResolveAttack(state, 1, 2);

            // 10 - 4/2 = 8 damage.
            Assert.That(state.Units[2].Health, Is.EqualTo(22));
            var resolved = events.OfType<CombatResolvedEvent>().Single();
            Assert.That(resolved.Damage, Is.EqualTo(8));
            Assert.That(resolved.DefenderOldHealth, Is.EqualTo(30));
            Assert.That(resolved.DefenderNewHealth, Is.EqualTo(22));
            Assert.That(resolved.DefenderDestroyed, Is.False);
        }

        [Test]
        public void ResolveAttack_NeverDrivesHealthBelowZero_AndRemovesDeadDefender()
        {
            var (state, combat, _) = Build();
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            state.Units[1] = new UnitInstance(1, 1, UnitDef("atk", attack: 100, defense: 0, maxHealth: 30), WorldPosition.Zero);
            state.Units[2] = new UnitInstance(2, 2, UnitDef("def", attack: 0, defense: 0, maxHealth: 5), WorldPosition.Zero);

            var events = combat.ResolveAttack(state, 1, 2);

            Assert.That(state.Units.ContainsKey(2), Is.False, "dead defender removed from the match");
            var resolved = events.OfType<CombatResolvedEvent>().Single();
            Assert.That(resolved.DefenderNewHealth, Is.EqualTo(0), "health clamped at zero, never negative");
            Assert.That(resolved.DefenderDestroyed, Is.True);
            Assert.That(events.OfType<UnitEliminatedEvent>().Single().UnitId, Is.EqualTo(2));
        }

        [Test]
        public void ResolveAttack_AppliesGovernanceAttackAndDefenseMultipliers()
        {
            var (state, combat, civ) = Build();
            var attackerNation = new Nation(1);
            var defenderNation = new Nation(2);
            state.Nations[1] = attackerNation;
            state.Nations[2] = defenderNation;

            // Double the attacker's attack (10 -> 20) and leave defense at its base.
            civ.ApplyGovernance(attackerNation, new GovernanceOption("war", "War Economy", unitAttackMultiplier: 2f));

            state.Units[1] = new UnitInstance(1, 1, UnitDef("atk", attack: 10, defense: 0, maxHealth: 30), WorldPosition.Zero);
            state.Units[2] = new UnitInstance(2, 2, UnitDef("def", attack: 0, defense: 4, maxHealth: 50), WorldPosition.Zero);

            var events = combat.ResolveAttack(state, 1, 2);

            // Effective attack 20, defense 4 -> 20 - 2 = 18 damage.
            Assert.That(events.OfType<CombatResolvedEvent>().Single().Damage, Is.EqualTo(18));
            Assert.That(state.Units[2].Health, Is.EqualTo(32));
        }

        [Test]
        public void ResolveAttack_BetweenSameNationUnits_IsANoOp()
        {
            var (state, combat, _) = Build();
            state.Nations[1] = new Nation(1);
            state.Units[1] = new UnitInstance(1, 1, UnitDef("a", 10, 0, 30), WorldPosition.Zero);
            state.Units[2] = new UnitInstance(2, 1, UnitDef("b", 10, 0, 30), WorldPosition.Zero);

            var events = combat.ResolveAttack(state, 1, 2);

            Assert.That(events, Is.Empty);
            Assert.That(state.Units[2].Health, Is.EqualTo(30));
        }
    }
}
