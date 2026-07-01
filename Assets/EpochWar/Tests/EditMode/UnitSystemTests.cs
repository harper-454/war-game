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
    /// Example-based unit tests for <see cref="UnitSystem"/> (Requirement 3.1â€“3.5, 9.2, 11.2).
    ///
    /// These cover concrete, named scenarios for recruitment/build queues (3.1), movement orders
    /// over the nav grid (3.2), Battalion grouping/commanding/removal (3.3â€“3.5), Doomsday deployment
    /// (9.2), and the Colony_Ship colonization sequence (11.2). They complement the universal FsCheck
    /// properties added by the optional tasks 9.2/9.4/9.6/9.10.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class UnitSystemTests
    {
        private static TerrainVolume SolidVolume(int x, int y, int z)
            => new TerrainVolume(new Int3(x, y, z), CellMaterial.Soil);

        private static UnitDef SoldierDef(
            string id = "soldier",
            Era era = Era.Prehistoric,
            ResourceCost cost = default,
            float buildTime = 0f,
            int populationCost = 0,
            int maxHealth = 30,
            float moveSpeed = 1f,
            UnitRole role = UnitRole.Soldier,
            ResourceCost launchCost = default)
            => new UnitDef(id, era, cost, buildTime, populationCost, maxHealth, 5, 2, moveSpeed, role, launchCost);

        private static StructureDef BarracksDef()
            => new StructureDef("barracks", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1, StructureFunction.Barracks);

        private static (MatchState state, CommandRouter router, UnitSystem units, ResourceSystem res, CivSystem civ)
            Build(ICatalog catalog, TerrainVolume terrain = null, float colonizationDuration = 0f)
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(catalog, res, civ, colonizationDuration);
            var router = new CommandRouter();
            units.RegisterHandlers(router);
            var state = new MatchState(terrain ?? SolidVolume(8, 4, 8));
            return (state, router, units, res, civ);
        }

        private static StructureInstance PlaceOperationalBarracks(MatchState state, int nationId, CellCoord origin)
        {
            var structure = new StructureInstance(100, nationId, BarracksDef(), origin) { IsOperational = true };
            state.Structures[structure.Id] = structure;
            return structure;
        }

        // ------------------------------------------------------------------
        // Recruitment (Req 3.1)
        // ------------------------------------------------------------------

        [Test]
        public void Recruit_DeductsCostAndPopulation_ThenSpawnsExactlyOneUnitAfterBuildTime()
        {
            var cost = ResourceCost.Single(ResourceType.Food, 20f);
            var unitDef = SoldierDef(cost: cost, buildTime: 2f, populationCost: 3);
            var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { BarracksDef() });
            var (state, router, units, res, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 5 };
            res.Produce(nation, ResourceType.Food, 100f);
            state.Nations[1] = nation;
            PlaceOperationalBarracks(state, 1, new CellCoord(2, 3, 2));

            var result = router.Dispatch(new RecruitUnitCommand(1, 100, "soldier"), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(res.GetAmount(nation, ResourceType.Food), Is.EqualTo(80f), "resource cost deducted");
            Assert.That(nation.Population, Is.EqualTo(2), "population consumed");
            Assert.That(units.PendingBuildCount, Is.EqualTo(1));
            Assert.That(state.Units.Count, Is.EqualTo(0), "unit not produced before build time elapses");

            var tick1 = units.Tick(state, 1f);
            Assert.That(state.Units.Count, Is.EqualTo(0), "still building at t=1 of 2");
            Assert.That(tick1.OfType<UnitRecruitedEvent>().Any(), Is.False);

            var tick2 = units.Tick(state, 1f);
            Assert.That(state.Units.Count, Is.EqualTo(1), "exactly one unit produced after build time");
            var recruited = tick2.OfType<UnitRecruitedEvent>().Single();
            Assert.That(recruited.UnitDefId, Is.EqualTo("soldier"));
            Assert.That(recruited.StructureId, Is.EqualTo(100));
            Assert.That(recruited.SpawnCell, Is.EqualTo(new CellCoord(2, 3, 2)), "spawns at the issuing structure");
        }

        [Test]
        public void Recruit_UnaffordableCost_IsRejectedWithNoStateChange()
        {
            var cost = ResourceCost.Single(ResourceType.Food, 200f);
            var unitDef = SoldierDef(cost: cost, populationCost: 1);
            var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { BarracksDef() });
            var (state, router, units, res, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 5 };
            res.Produce(nation, ResourceType.Food, 50f);
            state.Nations[1] = nation;
            PlaceOperationalBarracks(state, 1, new CellCoord(2, 3, 2));

            var result = router.Dispatch(new RecruitUnitCommand(1, 100, "soldier"), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(res.GetAmount(nation, ResourceType.Food), Is.EqualTo(50f));
            Assert.That(nation.Population, Is.EqualTo(5));
            Assert.That(units.PendingBuildCount, Is.EqualTo(0));
        }

        [Test]
        public void Recruit_InsufficientPopulation_IsRejectedWithoutDeductingResources()
        {
            var cost = ResourceCost.Single(ResourceType.Food, 10f);
            var unitDef = SoldierDef(cost: cost, populationCost: 9);
            var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { BarracksDef() });
            var (state, router, units, res, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 5 };
            res.Produce(nation, ResourceType.Food, 100f);
            state.Nations[1] = nation;
            PlaceOperationalBarracks(state, 1, new CellCoord(2, 3, 2));

            var result = router.Dispatch(new RecruitUnitCommand(1, 100, "soldier"), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(res.GetAmount(nation, ResourceType.Food), Is.EqualTo(100f), "no resources deducted on population rejection");
            Assert.That(nation.Population, Is.EqualTo(5));
        }

        [Test]
        public void Recruit_AtUnderConstructionStructure_IsRejected()
        {
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { BarracksDef() });
            var (state, router, units, res, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 5 };
            res.Produce(nation, ResourceType.Food, 100f);
            state.Nations[1] = nation;
            var building = new StructureInstance(100, 1, BarracksDef(), new CellCoord(2, 3, 2)); // not operational
            state.Structures[building.Id] = building;

            var result = router.Dispatch(new RecruitUnitCommand(1, 100, "soldier"), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(units.PendingBuildCount, Is.EqualTo(0));
        }

        // ------------------------------------------------------------------
        // Movement (Req 3.2)
        // ------------------------------------------------------------------

        [Test]
        public void Move_ToReachableDestination_IssuesOrderAndAdvancesToDestination()
        {
            var unitDef = SoldierDef(moveSpeed: 4f);
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, units, _, _) = Build(catalog, SolidVolume(8, 4, 8));

            var nation = new Nation(1);
            state.Nations[1] = nation;
            // Surface of the solid volume is y=3; a ground unit stands at y=4.
            var unit = new UnitInstance(1, 1, unitDef, WorldPosition.FromInts(0, 4, 0));
            state.Units[unit.Id] = unit;

            var destination = new CellCoord(5, 3, 0);
            var result = router.Dispatch(new MoveCommand(1, new[] { 1 }, destination), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(unit.CurrentOrder.Kind, Is.EqualTo(UnitOrder.OrderKind.Move));
            Assert.That(result.Events.OfType<UnitMovedEvent>().Single().Arrived, Is.False);

            // Advance enough ticks for the unit to traverse the path.
            bool arrived = false;
            for (int i = 0; i < 50 && !arrived; i++)
            {
                var events = units.Tick(state, 1f);
                if (events.OfType<UnitMovedEvent>().Any(e => e.Arrived))
                {
                    arrived = true;
                }
            }

            Assert.That(arrived, Is.True, "unit reaches the reachable destination");
            Assert.That(unit.CurrentOrder.Kind, Is.EqualTo(UnitOrder.OrderKind.Idle));
            // Final standing cell is above the destination column's surface (y = 3 + 1).
            Assert.That(unit.Position.X.ToInt(), Is.EqualTo(5));
            Assert.That(unit.Position.Z.ToInt(), Is.EqualTo(0));
        }

        [Test]
        public void Move_WithNoTargetedUnits_IsRejected()
        {
            var catalog = new InMemoryCatalog();
            var (state, router, _, _, _) = Build(catalog);
            state.Nations[1] = new Nation(1);

            var result = router.Dispatch(new MoveCommand(1, new int[0], new CellCoord(1, 3, 1)), state);

            Assert.That(result.Accepted, Is.False);
        }

        [Test]
        public void Move_Battalion_AppliesOrderToEverySurvivingMember()
        {
            var unitDef = SoldierDef(moveSpeed: 2f);
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, _, _, _) = Build(catalog, SolidVolume(8, 4, 8));

            var nation = new Nation(1);
            state.Nations[1] = nation;
            var a = new UnitInstance(1, 1, unitDef, WorldPosition.FromInts(0, 4, 0));
            var b = new UnitInstance(2, 1, unitDef, WorldPosition.FromInts(1, 4, 0));
            state.Units[1] = a;
            state.Units[2] = b;
            var battalion = new Battalion(1, "Alpha", new[] { 1, 2 });
            nation.Battalions[1] = battalion;
            a.BattalionId = 1;
            b.BattalionId = 1;

            var result = router.Dispatch(new MoveCommand(1, 1, new CellCoord(4, 3, 0)), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(a.CurrentOrder.Kind, Is.EqualTo(UnitOrder.OrderKind.Move));
            Assert.That(b.CurrentOrder.Kind, Is.EqualTo(UnitOrder.OrderKind.Move));
        }

        // ------------------------------------------------------------------
        // Battalions (Req 3.3, 3.4, 3.5)
        // ------------------------------------------------------------------

        [Test]
        public void FormBattalion_WithTwoUnits_CreatesNamedBattalionAndAssignsMembers()
        {
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, _, _, _) = Build(catalog);

            var nation = new Nation(1);
            state.Nations[1] = nation;
            state.Units[1] = new UnitInstance(1, 1, unitDef, WorldPosition.Zero);
            state.Units[2] = new UnitInstance(2, 1, unitDef, WorldPosition.Zero);

            var result = router.Dispatch(new FormBattalionCommand(1, "Vanguard", new[] { 1, 2 }), state);

            Assert.That(result.Accepted, Is.True);
            var formed = result.Events.OfType<BattalionFormedEvent>().Single();
            Assert.That(formed.Name, Is.EqualTo("Vanguard"));
            Assert.That(nation.Battalions.Count, Is.EqualTo(1));
            var battalion = nation.Battalions[formed.BattalionId];
            Assert.That(battalion.MemberUnitIds, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(state.Units[1].BattalionId, Is.EqualTo(formed.BattalionId));
            Assert.That(state.Units[2].BattalionId, Is.EqualTo(formed.BattalionId));
        }

        [Test]
        public void FormBattalion_WithFewerThanTwoValidUnits_IsRejected()
        {
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, _, _, _) = Build(catalog);

            var nation = new Nation(1);
            state.Nations[1] = nation;
            state.Units[1] = new UnitInstance(1, 1, unitDef, WorldPosition.Zero);

            var result = router.Dispatch(new FormBattalionCommand(1, "Solo", new[] { 1, 999 }), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(nation.Battalions, Is.Empty);
        }

        [Test]
        public void Disband_RemovesBattalionAndClearsMembership()
        {
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, _, _, _) = Build(catalog);

            var nation = new Nation(1);
            state.Nations[1] = nation;
            var a = new UnitInstance(1, 1, unitDef, WorldPosition.Zero) { BattalionId = 1 };
            var b = new UnitInstance(2, 1, unitDef, WorldPosition.Zero) { BattalionId = 1 };
            state.Units[1] = a;
            state.Units[2] = b;
            nation.Battalions[1] = new Battalion(1, "Alpha", new[] { 1, 2 });

            var result = router.Dispatch(new DisbandBattalionCommand(1, 1), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(nation.Battalions, Is.Empty);
            Assert.That(a.BattalionId, Is.Null);
            Assert.That(b.BattalionId, Is.Null);
            Assert.That(state.Units.Count, Is.EqualTo(2), "disbanding keeps the units in the match");
        }

        [Test]
        public void RemoveUnit_ZeroHealth_RemovesFromMatchAndBattalion_ReleasesPopulation()
        {
            var unitDef = SoldierDef(populationCost: 2);
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, router, units, _, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 4 };
            state.Nations[1] = nation;
            var a = new UnitInstance(1, 1, unitDef, WorldPosition.Zero) { BattalionId = 5, Health = 0 };
            var b = new UnitInstance(2, 1, unitDef, WorldPosition.Zero) { BattalionId = 5 };
            state.Units[1] = a;
            state.Units[2] = b;
            nation.Battalions[5] = new Battalion(5, "Alpha", new[] { 1, 2 });

            var events = units.Tick(state, 1f); // sweep removes the zero-health unit

            Assert.That(state.Units.ContainsKey(1), Is.False, "zero-health unit removed from the match");
            Assert.That(nation.Battalions[5].MemberUnitIds, Has.No.Member(1), "removed from its battalion");
            Assert.That(nation.Battalions[5].MemberUnitIds, Does.Contain(2), "surviving member stays");
            Assert.That(nation.Population, Is.EqualTo(6), "population released back to the pool");
            Assert.That(events.OfType<UnitEliminatedEvent>().Single().UnitId, Is.EqualTo(1));
        }

        [Test]
        public void RemoveUnit_LastMember_DisbandsTheBattalion()
        {
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(units: new[] { unitDef });
            var (state, _, units, _, _) = Build(catalog);

            var nation = new Nation(1) { PopulationCapacity = 10, Population = 10 };
            state.Nations[1] = nation;
            var only = new UnitInstance(1, 1, unitDef, WorldPosition.Zero) { BattalionId = 9, Health = 0 };
            state.Units[1] = only;
            nation.Battalions[9] = new Battalion(9, "Lone", new[] { 1 });

            var events = units.Tick(state, 1f);

            Assert.That(nation.Battalions.ContainsKey(9), Is.False, "emptied battalion disbanded");
            Assert.That(events.OfType<BattalionDisbandedEvent>().Single().CausedByElimination, Is.True);
        }

        // ------------------------------------------------------------------
        // Doomsday deployment (Req 9.2)
        // ------------------------------------------------------------------

        [Test]
        public void DeployDoomsday_ExecutesEliminationEffect_RemovesTargetForcesAndMarksEliminated()
        {
            var doomsday = new TechnologyDef(
                "nuke", Era.Modern, ResourceCost.Free,
                category: TechCategory.DoomsdayWeapon,
                deploymentCost: ResourceCost.Single(ResourceType.Energy, 50f));
            var unitDef = SoldierDef();
            var catalog = new InMemoryCatalog(technologies: new[] { doomsday }, units: new[] { unitDef });
            var (state, router, _, res, _) = Build(catalog);

            var attacker = new Nation(1) { CurrentEra = Era.Modern };
            attacker.CompletedTechIds.Add("nuke");
            res.Produce(attacker, ResourceType.Energy, 100f);
            var target = new Nation(2) { CurrentEra = Era.Modern };
            state.Nations[1] = attacker;
            state.Nations[2] = target;
            state.Units[10] = new UnitInstance(10, 2, unitDef, WorldPosition.Zero);
            state.Structures[20] = new StructureInstance(20, 2, BarracksDef(), new CellCoord(1, 0, 1));

            var result = router.Dispatch(new DeployDoomsdayCommand(1, "nuke", 2), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(target.Eliminated, Is.True, "target marked eliminated");
            Assert.That(state.Units.ContainsKey(10), Is.False, "target units removed");
            Assert.That(state.Structures.ContainsKey(20), Is.False, "target structures removed");
            Assert.That(res.GetAmount(attacker, ResourceType.Energy), Is.EqualTo(50f), "deployment cost paid");
            Assert.That(result.Events.OfType<NationEliminatedEvent>().Single().EliminatedNationId, Is.EqualTo(2));
        }

        [Test]
        public void DeployDoomsday_WithoutCompletedResearch_IsRejected()
        {
            var doomsday = new TechnologyDef("nuke", Era.Modern, ResourceCost.Free, category: TechCategory.DoomsdayWeapon);
            var catalog = new InMemoryCatalog(technologies: new[] { doomsday });
            var (state, router, _, _, _) = Build(catalog);

            var attacker = new Nation(1) { CurrentEra = Era.Modern };
            var target = new Nation(2) { CurrentEra = Era.Modern };
            state.Nations[1] = attacker;
            state.Nations[2] = target;

            var result = router.Dispatch(new DeployDoomsdayCommand(1, "nuke", 2), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(target.Eliminated, Is.False);
        }

        // ------------------------------------------------------------------
        // Colony Ship colonization (Req 11.2)
        // ------------------------------------------------------------------

        [Test]
        public void LaunchColonyShip_BeginsColonizationSequence_AndCompletesAfterDuration()
        {
            var shipDef = SoldierDef(
                id: "colony", era: Era.Space, role: UnitRole.ColonyShip,
                launchCost: ResourceCost.Single(ResourceType.ExoticMatter, 10f));
            var catalog = new InMemoryCatalog(units: new[] { shipDef });
            var (state, router, units, res, _) = Build(catalog, colonizationDuration: 3f);

            var nation = new Nation(1) { CurrentEra = Era.Space };
            res.Produce(nation, ResourceType.ExoticMatter, 100f);
            state.Nations[1] = nation;
            state.Units[1] = new UnitInstance(1, 1, shipDef, WorldPosition.Zero);

            var result = router.Dispatch(new LaunchColonyShipCommand(1, 1), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Events.OfType<ColonizationEvent>().Single().Phase, Is.EqualTo(ColonizationPhase.Started));
            Assert.That(units.HasColonizationStarted(1), Is.True);
            Assert.That(res.GetAmount(nation, ResourceType.ExoticMatter), Is.EqualTo(90f), "launch cost paid");

            units.Tick(state, 2f);
            Assert.That(units.IsColonizationComplete(1), Is.False, "not complete at t=2 of 3");

            var events = units.Tick(state, 2f);
            Assert.That(units.IsColonizationComplete(1), Is.True, "colonization completes after its duration");
            Assert.That(events.OfType<ColonizationEvent>().Single().Phase, Is.EqualTo(ColonizationPhase.Completed));
        }

        [Test]
        public void LaunchColonyShip_BeforeSpaceEra_IsRejected()
        {
            var shipDef = SoldierDef(id: "colony", era: Era.Space, role: UnitRole.ColonyShip);
            var catalog = new InMemoryCatalog(units: new[] { shipDef });
            var (state, router, units, _, _) = Build(catalog);

            var nation = new Nation(1) { CurrentEra = Era.Modern };
            state.Nations[1] = nation;
            state.Units[1] = new UnitInstance(1, 1, shipDef, WorldPosition.Zero);

            var result = router.Dispatch(new LaunchColonyShipCommand(1, 1), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(units.HasColonizationStarted(1), Is.False);
        }
    }
}
