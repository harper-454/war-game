using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.Navigation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for <see cref="TerrainSystem"/> (Requirement 6.2, 6.4).
    ///
    /// These cover concrete, named scenarios for queued-effect application (6.2), navigation
    /// recomputation after a batch (6.3), and the configured support-loss consequence applied to
    /// unsupported Structures/Units (6.4). They complement the universal FsCheck property added by
    /// task 8.2.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class TerrainSystemTests
    {
        private static TerrainVolume SolidVolume(int x, int y, int z, CellMaterial material = CellMaterial.Soil)
            => new TerrainVolume(new Int3(x, y, z), material);

        private static StructureDef StructureDef(int width, int length, int maxHealth)
            => new StructureDef(
                "wall", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth,
                width, length, StructureFunction.Defense);

        private static UnitDef GroundUnitDef(int maxHealth)
            => new UnitDef(
                "soldier", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth,
                5, 2, 1f, UnitRole.Soldier);

        private static UnitDef FlyingUnitDef(int maxHealth)
            => new UnitDef(
                "flyer", Era.Modern, ResourceCost.Free, 0f, 0, maxHealth,
                5, 2, 2f, UnitRole.Aircraft);

        private static TerrainModifiedEvent[] Modified(IReadOnlyList<GameEvent> events)
            => events.OfType<TerrainModifiedEvent>().ToArray();

        private static SupportLossEvent[] SupportLosses(IReadOnlyList<GameEvent> events)
            => events.OfType<SupportLossEvent>().ToArray();

        [Test]
        public void Tick_WithNoQueuedEffects_ReturnsNoEventsAndDoesNotMutateTerrain()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            var sys = new TerrainSystem();

            var events = sys.Tick(state);

            Assert.That(events, Is.Empty);
            Assert.That(state.Terrain.IsSolid(new CellCoord(1, 1, 1)), Is.True);
        }

        [Test]
        public void Tick_AppliesQueuedEffect_AndEmitsTerrainModifiedEvent()
        {
            var state = new MatchState(SolidVolume(4, 4, 4, CellMaterial.Sand));
            var sys = new TerrainSystem();
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 3, 1), radius: 0, depth: 1, power: 5));

            Assert.That(sys.PendingEffectCount, Is.EqualTo(1));
            var events = sys.Tick(state);

            Assert.That(sys.PendingEffectCount, Is.EqualTo(0));
            Assert.That(state.Terrain.IsSolid(new CellCoord(1, 3, 1)), Is.False, "targeted cell should be carved out");
            var modified = Modified(events);
            Assert.That(modified.Length, Is.EqualTo(1));
            Assert.That(modified[0].ModifiedCells, Does.Contain(new CellCoord(1, 3, 1)));
        }

        [Test]
        public void Tick_EffectHittingOnlyEmptyCells_EmitsNothing()
        {
            var state = new MatchState(SolidVolume(4, 4, 4)); // top row (y=3) is solid soil
            var sys = new TerrainSystem();
            // Target well outside the volume so no cell changes.
            sys.QueueEffect(new TerrainEffect(new CellCoord(50, 50, 50), radius: 1, depth: 1, power: 10));

            var events = sys.Tick(state);

            Assert.That(events, Is.Empty);
        }

        [Test]
        public void Tick_RemovingSupportUnderStructure_DestroysItByDefault()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            // Structure sits at y=2 with a 1x1 footprint; its support cell is (1,1,1).
            var structure = new StructureInstance(10, ownerNationId: 1, StructureDef(1, 1, maxHealth: 100), new CellCoord(1, 2, 1));
            state.Structures[structure.Id] = structure;

            var sys = new TerrainSystem(); // default: Destroy
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 1, 1), radius: 0, depth: 1, power: 10));

            var events = sys.Tick(state);

            Assert.That(state.Structures.ContainsKey(10), Is.False, "destroyed structure is removed");
            var losses = SupportLosses(events);
            Assert.That(losses.Length, Is.EqualTo(1));
            Assert.That(losses[0].EntityKind, Is.EqualTo(SupportedEntityKind.Structure));
            Assert.That(losses[0].EntityId, Is.EqualTo(10));
            Assert.That(losses[0].Destroyed, Is.True);
            Assert.That(losses[0].NewHealth, Is.EqualTo(0));
        }

        [Test]
        public void Tick_DamageConsequence_ReducesHealthWithoutDestroyingWhenSurvivable()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            var structure = new StructureInstance(11, ownerNationId: 1, StructureDef(1, 1, maxHealth: 100), new CellCoord(1, 2, 1));
            state.Structures[structure.Id] = structure;

            var sys = new TerrainSystem(consequence: SupportLossConsequence.Damage, supportLossDamage: 40);
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 1, 1), radius: 0, depth: 1, power: 10));

            var events = sys.Tick(state);

            Assert.That(state.Structures.ContainsKey(11), Is.True, "structure survives non-lethal damage");
            Assert.That(state.Structures[11].Health, Is.EqualTo(60));
            var loss = SupportLosses(events).Single();
            Assert.That(loss.Destroyed, Is.False);
            Assert.That(loss.OldHealth, Is.EqualTo(100));
            Assert.That(loss.NewHealth, Is.EqualTo(60));
        }

        [Test]
        public void Tick_StructureRestingOnFloor_IsNotAffected()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            // Origin at y=0 rests on the world floor and is always supported.
            var structure = new StructureInstance(12, ownerNationId: 1, StructureDef(1, 1, 50), new CellCoord(1, 0, 1));
            state.Structures[structure.Id] = structure;

            var sys = new TerrainSystem();
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 3, 1), radius: 2, depth: 3, power: 10));
            var events = sys.Tick(state);

            Assert.That(state.Structures.ContainsKey(12), Is.True);
            Assert.That(SupportLosses(events), Is.Empty);
        }

        [Test]
        public void Tick_RemovingSupportUnderGroundUnit_AppliesConsequenceAndRemovesFromBattalion()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            var nation = new Nation(1);
            var battalion = new Battalion(7, "Alpha", new[] { 20 });
            nation.Battalions[battalion.Id] = battalion;
            state.Nations[nation.Id] = nation;

            // Unit stands at cell (1,2,1); its support cell is (1,1,1).
            var unit = new UnitInstance(20, ownerNationId: 1, GroundUnitDef(30), WorldPosition.FromInts(1, 2, 1))
            {
                BattalionId = 7,
            };
            state.Units[unit.Id] = unit;

            var sys = new TerrainSystem();
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 1, 1), radius: 0, depth: 1, power: 10));
            var events = sys.Tick(state);

            Assert.That(state.Units.ContainsKey(20), Is.False, "destroyed unit is removed from the Match");
            Assert.That(battalion.MemberUnitIds, Has.No.Member(20), "removed unit leaves its battalion");
            var loss = SupportLosses(events).Single();
            Assert.That(loss.EntityKind, Is.EqualTo(SupportedEntityKind.Unit));
            Assert.That(loss.EntityId, Is.EqualTo(20));
        }

        [Test]
        public void Tick_FlyingUnit_IsExemptFromSupportChecks()
        {
            var state = new MatchState(SolidVolume(4, 4, 4));
            var unit = new UnitInstance(21, ownerNationId: 1, FlyingUnitDef(30), WorldPosition.FromInts(1, 2, 1));
            state.Units[unit.Id] = unit;

            var sys = new TerrainSystem();
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 1, 1), radius: 0, depth: 1, power: 10));
            var events = sys.Tick(state);

            Assert.That(state.Units.ContainsKey(21), Is.True, "flying unit is unaffected by lost ground support");
            Assert.That(SupportLosses(events), Is.Empty);
        }

        [Test]
        public void Tick_RecomputesNavGridForChangedCells()
        {
            var volume = SolidVolume(3, 3, 3);
            var navGrid = new NavGrid(volume);
            // Before: every column's surface is the top solid cell (y = 2).
            Assert.That(navGrid.SurfaceHeight(1, 1), Is.EqualTo(2));

            var state = new MatchState(volume);
            var sys = new TerrainSystem(navGrid);
            // Carve the whole (1,1) column from the top down.
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 2, 1), radius: 0, depth: 3, power: 10));

            sys.Tick(state);

            Assert.That(navGrid.SurfaceHeight(1, 1), Is.EqualTo(NavGrid.NoSurface),
                "column emptied by the effect should no longer be walkable");
        }

        [Test]
        public void Tick_UnaffectedEntities_AreLeftUntouched()
        {
            var state = new MatchState(SolidVolume(6, 4, 6));
            var near = new StructureInstance(30, 1, StructureDef(1, 1, 50), new CellCoord(1, 2, 1));
            var far = new StructureInstance(31, 1, StructureDef(1, 1, 50), new CellCoord(4, 2, 4));
            state.Structures[near.Id] = near;
            state.Structures[far.Id] = far;

            var sys = new TerrainSystem();
            sys.QueueEffect(new TerrainEffect(new CellCoord(1, 1, 1), radius: 0, depth: 1, power: 10));
            var events = sys.Tick(state);

            Assert.That(state.Structures.ContainsKey(30), Is.False, "structure above the carved cell loses support");
            Assert.That(state.Structures.ContainsKey(31), Is.True, "distant structure keeps its support");
            Assert.That(SupportLosses(events).Single().EntityId, Is.EqualTo(30));
        }
    }
}
