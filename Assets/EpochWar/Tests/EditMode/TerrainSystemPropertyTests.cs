using System;
using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based test for <see cref="TerrainSystem"/> support consequences (Requirement 6.4),
    /// covering the universal property from design.md ("Correctness Properties"):
    ///
    /// <list type="bullet">
    /// <item>Property 28 — Loss of support applies the defined consequence (Req 6.4).</item>
    /// </list>
    ///
    /// The property is exercised for at least <see cref="MinimumIterations"/> generated cases,
    /// matching the harness conventions in <see cref="ResourceSystemPropertyTests"/> (>= 100 generated
    /// iterations, tagged <c>Feature: epoch-war-game, Property 28</c>).
    ///
    /// Each case builds a fully solid volume, places a single entity (Structure or ground Unit) with a
    /// 1x1 footprint resting on solid terrain above the world floor, then queues an effect that carves
    /// away exactly the cell supporting it. After a <see cref="TerrainSystem.Tick"/> the configured
    /// <see cref="SupportLossConsequence"/> (randomised between Destroy and Damage) must have been
    /// applied: the entity's health, its removal-when-lethal, and the emitted
    /// <see cref="SupportLossEvent"/> must all match the consequence exactly.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class TerrainSystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        private static void CheckAtLeast100<T>(Arbitrary<T> arb, Func<T, bool> body)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            Prop.ForAll(arb, body).Check(config);
        }

        private static StructureDef WallDef(int maxHealth)
            => new StructureDef(
                "wall", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth,
                footprintWidth: 1, footprintLength: 1, StructureFunction.Defense);

        private static UnitDef SoldierDef(int maxHealth)
            => new UnitDef(
                "soldier", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth,
                attack: 5, defense: 2, moveSpeed: 1f, UnitRole.Soldier);

        /// <summary>
        /// The health an entity should have after the consequence, mirroring the requirement
        /// (Destroy => 0; Damage => current health reduced by the configured amount, clamped at zero).
        /// </summary>
        private static int ExpectedHealthAfter(SupportLossConsequence consequence, int health, int damage)
        {
            if (consequence == SupportLossConsequence.Damage)
            {
                int reduced = health - damage;
                return reduced < 0 ? 0 : reduced;
            }

            return 0;
        }

        /// <summary>
        /// Property 28: Loss of support applies the defined consequence.
        /// For any Structure or Unit whose supporting Terrain_Cell is removed, the defined consequence
        /// is applied to that Structure or Unit.
        ///
        /// **Validates: Requirements 6.4**
        /// </summary>
        [Test]
        [Category("Property 28: Loss of support applies the defined consequence")]
        public void Property28_LossOfSupportAppliesDefinedConsequence()
        {
            var gen =
                from dimX in Gen.Choose(2, 5)
                from dimY in Gen.Choose(2, 5)
                from dimZ in Gen.Choose(2, 5)
                // Entity position: resting above the world floor (Y >= 1) so it has a support cell.
                from ex in Gen.Choose(0, dimX - 1)
                from ey in Gen.Choose(1, dimY - 1)
                from ez in Gen.Choose(0, dimZ - 1)
                from health in Gen.Choose(1, 500)
                // false => Structure, true => ground Unit.
                from isUnit in Gen.Elements(false, true)
                // false => Destroy, true => Damage.
                from useDamage in Gen.Elements(false, true)
                from damage in Gen.Choose(0, 700)
                select (dimX, dimY, dimZ, ex, ey, ez, health, isUnit, useDamage, damage);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (dimX, dimY, dimZ, ex, ey, ez, health, isUnit, useDamage, damage) = input;

                var consequence = useDamage ? SupportLossConsequence.Damage : SupportLossConsequence.Destroy;
                int expectedHealth = ExpectedHealthAfter(consequence, health, damage);
                bool expectedDestroyed = expectedHealth <= 0;

                // A fully solid volume: the entity and its support cell both start solid.
                var state = new MatchState(new TerrainVolume(new Int3(dimX, dimY, dimZ), CellMaterial.Rock));
                const int entityId = 42;
                const int ownerNationId = 1;

                if (isUnit)
                {
                    var nation = new Nation(ownerNationId);
                    state.Nations[nation.Id] = nation;
                    state.Units[entityId] = new UnitInstance(
                        entityId, ownerNationId, SoldierDef(health), WorldPosition.FromInts(ex, ey, ez))
                    {
                        Health = health,
                    };
                }
                else
                {
                    state.Structures[entityId] = new StructureInstance(
                        entityId, ownerNationId, WallDef(health), new CellCoord(ex, ey, ez))
                    {
                        Health = health,
                    };
                }

                var sys = new TerrainSystem(navGrid: null, consequence: consequence, supportLossDamage: damage);
                // Carve exactly the single support cell directly below the entity.
                sys.QueueEffect(new TerrainEffect(new CellCoord(ex, ey - 1, ez), radius: 0, depth: 1, power: 100));

                IReadOnlyList<GameEvent> events = sys.Tick(state);

                // Exactly one support-loss event must be produced for the affected entity.
                var losses = events.OfType<SupportLossEvent>().ToList();
                if (losses.Count != 1)
                {
                    return false;
                }

                SupportLossEvent loss = losses[0];
                SupportedEntityKind expectedKind =
                    isUnit ? SupportedEntityKind.Unit : SupportedEntityKind.Structure;

                if (loss.EntityKind != expectedKind
                    || loss.EntityId != entityId
                    || loss.OwnerNationId != ownerNationId
                    || loss.OldHealth != health
                    || loss.NewHealth != expectedHealth
                    || loss.Destroyed != expectedDestroyed)
                {
                    return false;
                }

                // The consequence must be reflected in the live state: a lethal consequence removes
                // the entity; a survivable one leaves it present at exactly the reduced health.
                if (isUnit)
                {
                    bool present = state.Units.TryGetValue(entityId, out UnitInstance unit);
                    if (expectedDestroyed)
                    {
                        return !present;
                    }

                    return present && unit.Health == expectedHealth;
                }
                else
                {
                    bool present = state.Structures.TryGetValue(entityId, out StructureInstance structure);
                    if (expectedDestroyed)
                    {
                        return !present;
                    }

                    return present && structure.Health == expectedHealth;
                }
            });
        }
    }
}
