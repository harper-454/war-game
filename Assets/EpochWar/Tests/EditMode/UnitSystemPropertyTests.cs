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
    /// Property-based tests for <see cref="UnitSystem"/> (Requirement 3.1–3.5, 9.2, 11.2), validating
    /// the universal correctness properties from design.md, each exercised for at least the
    /// design-mandated minimum of 100 generated iterations (see design.md, "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-game, Property N</c>:
    /// <list type="bullet">
    ///   <item>Property 11 — Recruitment deducts cost and produces the unit after build time (Req 3.1).</item>
    ///   <item>Property 12 — Movement reaches a reachable destination (Req 3.2).</item>
    ///   <item>Property 13 — Battalion membership is stable until disband or elimination (Req 3.3).</item>
    ///   <item>Property 14 — Battalion commands reach every surviving member (Req 3.4).</item>
    ///   <item>Property 15 — Zero-health units are fully removed (Req 3.5).</item>
    ///   <item>Property 33 — Deploying a doomsday weapon executes its elimination effect (Req 9.2).</item>
    ///   <item>Property 41 — Completing a Colony_Ship begins the colonization sequence (Req 11.2).</item>
    /// </list>
    ///
    /// All scenarios target the engine-free <c>EpochWar.Core</c> assembly with no Unity Play loop and
    /// build small in-memory catalogs plus a <see cref="MatchState"/> with a fully solid terrain
    /// volume so a nav grid exists wherever movement/recruitment needs it.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class UnitSystemPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        // ------------------------------------------------------------------
        // Shared builders
        // ------------------------------------------------------------------

        private static TerrainVolume SolidVolume(int x, int y, int z)
            => new TerrainVolume(new Int3(x, y, z), CellMaterial.Soil);

        private static UnitDef UnitDefOf(
            string id = "soldier",
            Era era = Era.Prehistoric,
            ResourceCost cost = default,
            float buildTime = 0f,
            int populationCost = 0,
            int maxHealth = 30,
            int attack = 5,
            int defense = 2,
            float moveSpeed = 1f,
            UnitRole role = UnitRole.Soldier,
            ResourceCost launchCost = default)
            => new UnitDef(id, era, cost, buildTime, populationCost, maxHealth, attack, defense, moveSpeed, role, launchCost);

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

        // ==================================================================
        // Property 11: Recruitment deducts cost and produces the unit after
        // build time (Req 3.1)
        // ==================================================================

        /// <summary>
        /// A generated recruitment scenario: an affordable single-resource cost, a build time, a
        /// small population cost, and ample stored food/population so the recruit is always accepted.
        /// </summary>
        public sealed class RecruitScenario
        {
            public int CostFood;
            public int StartFood;
            public int PopulationCost;
            public float BuildTime;

            public override string ToString()
                => $"RecruitScenario(cost={CostFood}, food={StartFood}, pop={PopulationCost}, build={BuildTime}s)";
        }

        private static Arbitrary<RecruitScenario> RecruitScenarios()
        {
            var gen = from cost in Gen.Choose(0, 200)
                      from extra in Gen.Choose(0, 200)
                      from popCost in Gen.Choose(0, 5)
                      from buildTenths in Gen.Choose(0, 50)
                      select new RecruitScenario
                      {
                          CostFood = cost,
                          StartFood = cost + extra,   // always affordable
                          PopulationCost = popCost,
                          BuildTime = buildTenths / 10f, // 0.0 .. 5.0 seconds
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 11: Recruitment deducts cost and produces the unit after build time.
        ///
        /// For any unlocked Unit type whose cost the Nation can afford, issuing a recruit command
        /// deducts the Resource cost and, after the Unit's build time elapses in simulation, produces
        /// exactly one Unit of that type at the issuing Structure.
        ///
        /// **Validates: Requirements 3.1**
        /// </summary>
        [Test]
        [Category("Property 11")]
        public void Property11_RecruitmentDeductsCostAndProducesUnitAfterBuildTime()
        {
            Prop.ForAll(RecruitScenarios(), scenario =>
            {
                var cost = ResourceCost.Single(ResourceType.Food, scenario.CostFood);
                var unitDef = UnitDefOf(cost: cost, buildTime: scenario.BuildTime, populationCost: scenario.PopulationCost);
                var catalog = new InMemoryCatalog(units: new[] { unitDef }, structures: new[] { BarracksDef() });
                var (state, router, units, res, _) = Build(catalog);

                var nation = new Nation(1) { PopulationCapacity = 100, Population = 100 };
                res.Produce(nation, ResourceType.Food, scenario.StartFood);
                state.Nations[1] = nation;
                var origin = new CellCoord(2, 3, 2);
                state.Structures[100] = new StructureInstance(100, 1, BarracksDef(), origin) { IsOperational = true };

                float foodBefore = res.GetAmount(nation, ResourceType.Food);
                int popBefore = nation.Population;

                var result = router.Dispatch(new RecruitUnitCommand(1, 100, "soldier"), state);
                if (!result.Accepted)
                {
                    return false;
                }

                // Cost and population are deducted at accept time (Req 3.1 / 5.4).
                if (res.GetAmount(nation, ResourceType.Food) != foodBefore - scenario.CostFood)
                {
                    return false;
                }

                if (nation.Population != popBefore - scenario.PopulationCost)
                {
                    return false;
                }

                // Queued, not yet produced.
                if (units.PendingBuildCount != 1 || state.Units.Count != 0)
                {
                    return false;
                }

                // Before the build time elapses, no unit is produced.
                if (scenario.BuildTime > 0f)
                {
                    units.Tick(state, scenario.BuildTime * 0.5f);
                    if (state.Units.Count != 0 || units.PendingBuildCount != 1)
                    {
                        return false;
                    }
                }

                // After the build time elapses, exactly one unit spawns at the issuing structure.
                float finishDelta = scenario.BuildTime > 0f ? scenario.BuildTime : 1f;
                var events = units.Tick(state, finishDelta);

                if (state.Units.Count != 1)
                {
                    return false;
                }

                var spawned = state.Units.Values.Single();
                if (spawned.OwnerNationId != 1 || spawned.Def.Id != "soldier")
                {
                    return false;
                }

                var recruited = events.OfType<UnitRecruitedEvent>().SingleOrDefault();
                return recruited != null
                       && recruited.StructureId == 100
                       && recruited.SpawnCell == origin;
            }).Check(Config());
        }

        // ==================================================================
        // Property 12: Movement reaches a reachable destination (Req 3.2)
        // ==================================================================

        public sealed class MoveScenario
        {
            public int DimX;
            public int DimZ;
            public int StartX;
            public int StartZ;
            public int DestX;
            public int DestZ;

            public override string ToString()
                => $"MoveScenario(dim=({DimX},{DimZ}), start=({StartX},{StartZ}), dest=({DestX},{DestZ}))";
        }

        private static Arbitrary<MoveScenario> MoveScenarios()
        {
            var gen = from dimX in Gen.Choose(1, 6)
                      from dimZ in Gen.Choose(1, 6)
                      from sx in Gen.Choose(0, dimX - 1)
                      from sz in Gen.Choose(0, dimZ - 1)
                      from dx in Gen.Choose(0, dimX - 1)
                      from dz in Gen.Choose(0, dimZ - 1)
                      select new MoveScenario
                      {
                          DimX = dimX,
                          DimZ = dimZ,
                          StartX = sx,
                          StartZ = sz,
                          DestX = dx,
                          DestZ = dz,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 12: Movement reaches a reachable destination.
        ///
        /// For any Unit and any destination reachable over the current navigable terrain, executing
        /// the movement command produces a navigable path whose final node is the destination — here
        /// verified end-to-end: on a fully solid, flat volume every column is reachable, and the Unit
        /// advances to the destination column over subsequent ticks.
        ///
        /// **Validates: Requirements 3.2**
        /// </summary>
        [Test]
        [Category("Property 12")]
        public void Property12_MovementReachesAReachableDestination()
        {
            Prop.ForAll(MoveScenarios(), scenario =>
            {
                const int dimY = 2; // solid volume; surface at y=1, ground units stand at y=2.
                var unitDef = UnitDefOf(moveSpeed: 2f);
                var catalog = new InMemoryCatalog(units: new[] { unitDef });
                var (state, router, units, _, _) = Build(catalog, SolidVolume(scenario.DimX, dimY, scenario.DimZ));

                state.Nations[1] = new Nation(1);
                var unit = new UnitInstance(1, 1, unitDef, WorldPosition.FromInts(scenario.StartX, dimY, scenario.StartZ));
                state.Units[1] = unit;

                var destination = new CellCoord(scenario.DestX, dimY - 1, scenario.DestZ);
                var result = router.Dispatch(new MoveCommand(1, new[] { 1 }, destination), state);
                if (!result.Accepted)
                {
                    return false;
                }

                int manhattan = System.Math.Abs(scenario.StartX - scenario.DestX)
                                + System.Math.Abs(scenario.StartZ - scenario.DestZ);

                // Advance until the move order completes (arrival returns the unit to Idle).
                int cap = manhattan + 5;
                for (int i = 0; i < cap && unit.CurrentOrder.Kind == UnitOrder.OrderKind.Move; i++)
                {
                    units.Tick(state, 1f);
                }

                // The unit ends standing on the destination column.
                return unit.CurrentOrder.Kind == UnitOrder.OrderKind.Idle
                       && unit.Position.X.ToInt() == scenario.DestX
                       && unit.Position.Z.ToInt() == scenario.DestZ;
            }).Check(Config());
        }

        // ==================================================================
        // Property 13: Battalion membership is stable until disband or
        // elimination (Req 3.3)
        // ==================================================================

        private static Arbitrary<(int n, int k)> BattalionScenarios()
        {
            var gen = from n in Gen.Choose(2, 6)
                      from k in Gen.Choose(0, n)
                      select (n, k);
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 13: Battalion membership is stable until disband or elimination.
        ///
        /// For any set of two or more Units grouped into a Battalion, the Battalion's membership
        /// equals that set across neutral simulation ticks, and only changes when the Player disbands
        /// it (membership cleared) or members are eliminated (membership shrinks to the survivors).
        ///
        /// **Validates: Requirements 3.3**
        /// </summary>
        [Test]
        [Category("Property 13")]
        public void Property13_BattalionMembershipIsStableUntilDisbandOrElimination()
        {
            Prop.ForAll(BattalionScenarios(), tuple =>
            {
                var (n, k) = tuple;
                var unitDef = UnitDefOf(populationCost: 0);
                var catalog = new InMemoryCatalog(units: new[] { unitDef });
                var (state, router, units, _, _) = Build(catalog);

                var nation = new Nation(1) { PopulationCapacity = 100, Population = 100 };
                state.Nations[1] = nation;
                var ids = new List<int>();
                for (int i = 1; i <= n; i++)
                {
                    state.Units[i] = new UnitInstance(i, 1, unitDef, WorldPosition.Zero);
                    ids.Add(i);
                }

                var formed = router.Dispatch(new FormBattalionCommand(1, "Alpha", ids), state);
                if (!formed.Accepted)
                {
                    return false;
                }

                int bid = formed.Events.OfType<BattalionFormedEvent>().Single().BattalionId;
                if (!nation.Battalions[bid].MemberUnitIds.SetEquals(ids))
                {
                    return false;
                }

                // Membership is stable across neutral ticks (no eliminations, no disband).
                for (int t = 0; t < 3; t++)
                {
                    units.Tick(state, 1f);
                    if (!nation.Battalions.ContainsKey(bid)
                        || !nation.Battalions[bid].MemberUnitIds.SetEquals(ids))
                    {
                        return false;
                    }
                }

                if (k == 0)
                {
                    // Disband clears membership but keeps the units in the Match (Req 3.3).
                    var disbanded = router.Dispatch(new DisbandBattalionCommand(1, bid), state);
                    if (!disbanded.Accepted || nation.Battalions.ContainsKey(bid))
                    {
                        return false;
                    }

                    foreach (var id in ids)
                    {
                        if (!state.Units.ContainsKey(id) || state.Units[id].BattalionId != null)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                // Eliminate the first k members; membership shrinks to exactly the survivors.
                for (int i = 1; i <= k; i++)
                {
                    state.Units[i].Health = 0;
                }

                units.Tick(state, 1f);

                var survivors = new HashSet<int>(Enumerable.Range(k + 1, n - k));

                foreach (var id in Enumerable.Range(1, k))
                {
                    if (state.Units.ContainsKey(id))
                    {
                        return false; // eliminated unit still present
                    }
                }

                if (survivors.Count == 0)
                {
                    // An emptied battalion is disbanded.
                    return !nation.Battalions.ContainsKey(bid);
                }

                return nation.Battalions.ContainsKey(bid)
                       && nation.Battalions[bid].MemberUnitIds.SetEquals(survivors);
            }).Check(Config());
        }

        // ==================================================================
        // Property 14: Battalion commands reach every surviving member (Req 3.4)
        // ==================================================================

        private static Arbitrary<(int n, int k)> SurvivingBattalionScenarios()
        {
            var gen = from n in Gen.Choose(2, 6)
                      from k in Gen.Choose(0, n - 1) // always leave at least one survivor
                      select (n, k);
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 14: Battalion commands reach every surviving member.
        ///
        /// For any Battalion and any command issued to it, every surviving member Unit receives that
        /// command — here a Battalion move order, which every living member picks up as a Move order
        /// toward the (distinct) destination column.
        ///
        /// **Validates: Requirements 3.4**
        /// </summary>
        [Test]
        [Category("Property 14")]
        public void Property14_BattalionCommandsReachEverySurvivingMember()
        {
            Prop.ForAll(SurvivingBattalionScenarios(), tuple =>
            {
                var (n, k) = tuple;
                const int dimY = 2;
                var unitDef = UnitDefOf(moveSpeed: 1f);
                var catalog = new InMemoryCatalog(units: new[] { unitDef });
                // Columns x = 0..n-1 at z = 0 for the members; z = 1 provides a distinct destination.
                var (state, router, units, _, _) = Build(catalog, SolidVolume(n, dimY, 2));

                var nation = new Nation(1) { PopulationCapacity = 100, Population = 100 };
                state.Nations[1] = nation;
                var ids = new List<int>();
                for (int i = 1; i <= n; i++)
                {
                    state.Units[i] = new UnitInstance(i, 1, unitDef, WorldPosition.FromInts(i - 1, dimY, 0));
                    ids.Add(i);
                }

                var formed = router.Dispatch(new FormBattalionCommand(1, "Alpha", ids), state);
                if (!formed.Accepted)
                {
                    return false;
                }

                int bid = formed.Events.OfType<BattalionFormedEvent>().Single().BattalionId;

                // Eliminate the first k members, leaving survivors {k+1..n}.
                for (int i = 1; i <= k; i++)
                {
                    state.Units[i].Health = 0;
                }

                units.Tick(state, 1f);

                // Column (0,1) differs from every member column (i-1, 0), so a Move order is issued.
                var destination = new CellCoord(0, dimY - 1, 1);
                var result = router.Dispatch(new MoveCommand(1, bid, destination), state);
                if (!result.Accepted)
                {
                    return false;
                }

                var survivors = Enumerable.Range(k + 1, n - k).ToList();

                // The battalion's membership is exactly the survivors, and each received the order.
                if (!nation.Battalions[bid].MemberUnitIds.SetEquals(survivors))
                {
                    return false;
                }

                foreach (var id in survivors)
                {
                    if (!state.Units.ContainsKey(id)
                        || state.Units[id].CurrentOrder.Kind != UnitOrder.OrderKind.Move)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ==================================================================
        // Property 15: Zero-health units are fully removed (Req 3.5)
        // ==================================================================

        /// <summary>
        /// Property 15: Zero-health units are fully removed.
        ///
        /// For any Unit whose health reaches zero, the Unit is absent from the Match's unit set and
        /// absent from the membership of every Battalion.
        ///
        /// **Validates: Requirements 3.5**
        /// </summary>
        [Test]
        [Category("Property 15")]
        public void Property15_ZeroHealthUnitsAreFullyRemoved()
        {
            Prop.ForAll(BattalionScenarios(), tuple =>
            {
                var (n, k) = tuple;
                var unitDef = UnitDefOf(populationCost: 0);
                var catalog = new InMemoryCatalog(units: new[] { unitDef });
                var (state, router, units, _, _) = Build(catalog);

                var nation = new Nation(1) { PopulationCapacity = 100, Population = 100 };
                state.Nations[1] = nation;
                var ids = new List<int>();
                for (int i = 1; i <= n; i++)
                {
                    state.Units[i] = new UnitInstance(i, 1, unitDef, WorldPosition.Zero);
                    ids.Add(i);
                }

                var formed = router.Dispatch(new FormBattalionCommand(1, "Alpha", ids), state);
                if (!formed.Accepted)
                {
                    return false;
                }

                // Drive the first k members to zero health.
                for (int i = 1; i <= k; i++)
                {
                    state.Units[i].Health = 0;
                }

                units.Tick(state, 1f);

                // Every zeroed unit is gone from the match AND from every battalion's membership.
                foreach (var id in Enumerable.Range(1, k))
                {
                    if (state.Units.ContainsKey(id))
                    {
                        return false;
                    }

                    foreach (var battalion in nation.Battalions.Values)
                    {
                        if (battalion.MemberUnitIds.Contains(id))
                        {
                            return false;
                        }
                    }
                }

                // Every surviving unit remains present.
                foreach (var id in Enumerable.Range(k + 1, n - k))
                {
                    if (!state.Units.ContainsKey(id))
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ==================================================================
        // Property 33: Deploying a doomsday weapon executes its elimination
        // effect (Req 9.2)
        // ==================================================================

        public sealed class DoomsdayScenario
        {
            public int AttackerUnits;
            public int TargetUnits;
            public int TargetStructures;
            public int DeploymentCost;
            public int StartEnergy;

            public override string ToString()
                => $"DoomsdayScenario(atkUnits={AttackerUnits}, tgtUnits={TargetUnits}, "
                   + $"tgtStructs={TargetStructures}, cost={DeploymentCost}, energy={StartEnergy})";
        }

        private static Arbitrary<DoomsdayScenario> DoomsdayScenarios()
        {
            var gen = from attackerUnits in Gen.Choose(0, 4)
                      from targetUnits in Gen.Choose(0, 5)
                      from targetStructures in Gen.Choose(0, 5)
                      from cost in Gen.Choose(0, 100)
                      from extra in Gen.Choose(0, 100)
                      select new DoomsdayScenario
                      {
                          AttackerUnits = attackerUnits,
                          TargetUnits = targetUnits,
                          TargetStructures = targetStructures,
                          DeploymentCost = cost,
                          StartEnergy = cost + extra,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 33: Deploying a doomsday weapon executes its elimination effect.
        ///
        /// For any targeted opposing Nation, completing a Doomsday_Weapon and paying its deployment
        /// cost executes the weapon's defined elimination effect against that target: the target's
        /// Units and Structures are removed, the target is marked eliminated, and the deployment cost
        /// is paid — while the deploying Nation's own forces are untouched.
        ///
        /// **Validates: Requirements 9.2**
        /// </summary>
        [Test]
        [Category("Property 33")]
        public void Property33_DeployingDoomsdayWeaponExecutesEliminationEffect()
        {
            Prop.ForAll(DoomsdayScenarios(), scenario =>
            {
                var doomsday = new TechnologyDef(
                    "nuke", Era.Modern, ResourceCost.Free,
                    category: TechCategory.DoomsdayWeapon,
                    deploymentCost: ResourceCost.Single(ResourceType.Energy, scenario.DeploymentCost));
                var unitDef = UnitDefOf(id: "grunt");
                var catalog = new InMemoryCatalog(
                    technologies: new[] { doomsday },
                    units: new[] { unitDef },
                    structures: new[] { BarracksDef() });
                var (state, router, _, res, _) = Build(catalog);

                var attacker = new Nation(1) { CurrentEra = Era.Modern };
                attacker.CompletedTechIds.Add("nuke");
                res.Produce(attacker, ResourceType.Energy, scenario.StartEnergy);
                var target = new Nation(2) { CurrentEra = Era.Modern };
                state.Nations[1] = attacker;
                state.Nations[2] = target;

                // Deploying Nation's own forces (must survive).
                for (int i = 0; i < scenario.AttackerUnits; i++)
                {
                    int id = 1 + i;
                    state.Units[id] = new UnitInstance(id, 1, unitDef, WorldPosition.Zero);
                }

                // Target forces (must all be eliminated).
                for (int i = 0; i < scenario.TargetUnits; i++)
                {
                    int id = 100 + i;
                    state.Units[id] = new UnitInstance(id, 2, unitDef, WorldPosition.Zero);
                }

                for (int i = 0; i < scenario.TargetStructures; i++)
                {
                    int id = 200 + i;
                    state.Structures[id] = new StructureInstance(id, 2, BarracksDef(), new CellCoord(i, 0, 0));
                }

                float energyBefore = res.GetAmount(attacker, ResourceType.Energy);

                var result = router.Dispatch(new DeployDoomsdayCommand(1, "nuke", 2), state);
                if (!result.Accepted)
                {
                    return false;
                }

                if (!target.Eliminated)
                {
                    return false;
                }

                // No target-owned unit or structure remains.
                if (state.Units.Values.Any(u => u.OwnerNationId == 2)
                    || state.Structures.Values.Any(s => s.OwnerNationId == 2))
                {
                    return false;
                }

                // The deploying Nation's forces are untouched.
                if (state.Units.Values.Count(u => u.OwnerNationId == 1) != scenario.AttackerUnits)
                {
                    return false;
                }

                // Deployment cost paid exactly.
                if (res.GetAmount(attacker, ResourceType.Energy) != energyBefore - scenario.DeploymentCost)
                {
                    return false;
                }

                return result.Events.OfType<NationEliminatedEvent>().Any(e => e.EliminatedNationId == 2);
            }).Check(Config());
        }

        // ==================================================================
        // Property 41: Completing a Colony_Ship begins the colonization
        // sequence (Req 11.2)
        // ==================================================================

        private static Arbitrary<(int cost, int start, float duration)> ColonyScenarios()
        {
            var gen = from cost in Gen.Choose(0, 100)
                      from extra in Gen.Choose(0, 100)
                      from durationTenths in Gen.Choose(0, 50)
                      select (cost, cost + extra, durationTenths / 10f);
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 41: Completing a Colony_Ship begins the colonization sequence.
        ///
        /// For any Nation that completes a Colony_Ship and pays its launch cost, the defined
        /// colonization sequence begins: the launch cost is deducted, the sequence is marked started,
        /// and a <see cref="ColonizationPhase.Started"/> event is emitted.
        ///
        /// **Validates: Requirements 11.2**
        /// </summary>
        [Test]
        [Category("Property 41")]
        public void Property41_CompletingColonyShipBeginsColonizationSequence()
        {
            Prop.ForAll(ColonyScenarios(), tuple =>
            {
                var (cost, start, duration) = tuple;
                var shipDef = UnitDefOf(
                    id: "colony", era: Era.Space, role: UnitRole.ColonyShip,
                    launchCost: ResourceCost.Single(ResourceType.ExoticMatter, cost));
                var catalog = new InMemoryCatalog(units: new[] { shipDef });
                var (state, router, units, res, _) = Build(catalog, colonizationDuration: duration);

                var nation = new Nation(1) { CurrentEra = Era.Space };
                res.Produce(nation, ResourceType.ExoticMatter, start);
                state.Nations[1] = nation;
                state.Units[1] = new UnitInstance(1, 1, shipDef, WorldPosition.Zero);

                float before = res.GetAmount(nation, ResourceType.ExoticMatter);

                var result = router.Dispatch(new LaunchColonyShipCommand(1, 1), state);
                if (!result.Accepted)
                {
                    return false;
                }

                if (!units.HasColonizationStarted(1))
                {
                    return false;
                }

                if (res.GetAmount(nation, ResourceType.ExoticMatter) != before - cost)
                {
                    return false;
                }

                return result.Events.OfType<ColonizationEvent>()
                    .Any(e => e.Phase == ColonizationPhase.Started && e.NationId == 1);
            }).Check(Config());
        }
    }
}
