using System;
using System.Linq;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for the <see cref="Era"/> progression order (Req 1.1) and the
    /// per-Unit attribute schema exposed by <see cref="UnitDef"/> / <see cref="UnitInstance"/>
    /// (Req 3.6).
    ///
    /// Req 1.1 requires a fixed, ordered set of eras running from Prehistoric to Space; because
    /// era-locked content is gated with ordinal comparisons (e.g. <c>era &gt;= Era.Space</c>) the
    /// enum's declared order must match the intended progression exactly. Req 3.6 requires every
    /// Unit to expose health, attack, defense, move speed, and its Era of origin — these tests
    /// assert those members are present and carried faithfully from the shared definition onto a
    /// live instance.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class EraSchemaTests
    {
        [Test]
        public void Era_ProgressionOrder_IsPrehistoricThroughSpace()
        {
            var expected = new[]
            {
                Era.Prehistoric,
                Era.Ancient,
                Era.Classical,
                Era.Medieval,
                Era.Industrial,
                Era.Modern,
                Era.Information,
                Era.Futuristic,
                Era.Space
            };

            // Ordering by the underlying ordinal value must reproduce the design progression.
            var actual = Enum.GetValues(typeof(Era)).Cast<Era>().OrderBy(e => (int)e).ToArray();

            Assert.That(actual, Is.EqualTo(expected), "Era enum order must run Prehistoric..Space (Req 1.1)");
        }

        [Test]
        public void Era_IsAContiguousZeroBasedSequence()
        {
            var values = Enum.GetValues(typeof(Era)).Cast<Era>().Select(e => (int)e).OrderBy(v => v).ToArray();

            Assert.That(values.First(), Is.EqualTo(0), "the progression must start at zero (Prehistoric)");
            for (int i = 0; i < values.Length; i++)
            {
                Assert.That(values[i], Is.EqualTo(i), "ordinal values must be contiguous so comparisons reflect progression");
            }
        }

        [Test]
        public void Era_FirstAndLast_AreOrderedForGating()
        {
            Assert.That((int)Era.Prehistoric, Is.LessThan((int)Era.Space),
                "the earliest era must compare less than the latest so era gates work (Req 1.1)");
            Assert.That(Enum.GetValues(typeof(Era)).Cast<Era>().Min(), Is.EqualTo(Era.Prehistoric));
            Assert.That(Enum.GetValues(typeof(Era)).Cast<Era>().Max(), Is.EqualTo(Era.Space));
        }

        [Test]
        public void UnitDef_ExposesHealthAttackDefenseMoveSpeedAndEraOfOrigin()
        {
            var def = new UnitDef(
                id: "legionary",
                era: Era.Classical,
                cost: ResourceCost.Free,
                buildTimeSeconds: 4f,
                populationCost: 1,
                maxHealth: 42,
                attack: 12,
                defense: 7,
                moveSpeed: 2.5f,
                role: UnitRole.Soldier);

            // Req 3.6: the definition must carry the full attribute schema.
            Assert.That(def.MaxHealth, Is.EqualTo(42), "health");
            Assert.That(def.Attack, Is.EqualTo(12), "attack");
            Assert.That(def.Defense, Is.EqualTo(7), "defense");
            Assert.That(def.MoveSpeed, Is.EqualTo(2.5f), "move speed");
            Assert.That(def.Era, Is.EqualTo(Era.Classical), "Era of origin");
        }

        [Test]
        public void UnitInstance_CarriesTheDefinitionAttributesAndStartsAtMaxHealth()
        {
            var def = new UnitDef(
                id: "rifleman",
                era: Era.Industrial,
                cost: ResourceCost.Free,
                buildTimeSeconds: 3f,
                populationCost: 1,
                maxHealth: 30,
                attack: 15,
                defense: 5,
                moveSpeed: 3f,
                role: UnitRole.Soldier);

            var instance = new UnitInstance(id: 7, ownerNationId: 1, def: def, position: WorldPosition.Zero);

            // A live Unit exposes its mutable health and reaches its static attributes via its Def (Req 3.6).
            Assert.That(instance.Health, Is.EqualTo(def.MaxHealth), "instances start at full health");
            Assert.That(instance.Def.Attack, Is.EqualTo(15), "attack via def");
            Assert.That(instance.Def.Defense, Is.EqualTo(5), "defense via def");
            Assert.That(instance.Def.MoveSpeed, Is.EqualTo(3f), "move speed via def");
            Assert.That(instance.Def.Era, Is.EqualTo(Era.Industrial), "Era of origin via def");
        }

        [Test]
        public void UnitInstance_HealthIsMutableForCombat_ButDefEraOfOriginIsStable()
        {
            var def = new UnitDef("knight", Era.Medieval, ResourceCost.Free, 5f, 1, 50, 18, 12, 2f, UnitRole.Soldier);
            var instance = new UnitInstance(1, 1, def, WorldPosition.Zero);

            instance.Health -= 20;

            Assert.That(instance.Health, Is.EqualTo(30), "health is per-instance mutable runtime state (Req 3.6)");
            Assert.That(instance.Def.Era, Is.EqualTo(Era.Medieval), "Era of origin comes from the shared immutable def");
        }
    }
}
