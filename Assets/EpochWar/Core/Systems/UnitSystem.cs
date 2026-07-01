using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.Navigation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of Unit recruitment, movement, Battalion organisation, removal, and
    /// the Annihilation/Ascension special operations (Requirement 3, plus Req 9.2 and 11.2).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Recruitment (<see cref="RecruitUnitCommand"/>): validates the producing Structure,
    /// unlock, affordability and available population, deducts the Resource and population costs, and
    /// queues a build at the Structure; on build-time completion in <see cref="Tick"/> exactly one
    /// Unit spawns there and a <see cref="UnitRecruitedEvent"/> is emitted (Req 3.1).</item>
    /// <item>Movement (<see cref="MoveCommand"/>): computes a navigable path over the terrain's nav
    /// grid for each targeted Unit (or every surviving Battalion member) toward a reachable
    /// destination and issues a move order; <see cref="Tick"/> advances each Unit along its path and
    /// returns it to idle on arrival (Req 3.2, 3.4).</item>
    /// <item>Battalions (<see cref="FormBattalionCommand"/> / <see cref="DisbandBattalionCommand"/>):
    /// creates named Battalions that retain membership until disbanded or emptied by elimination, and
    /// applies Battalion-targeted commands to every surviving member (Req 3.3, 3.4).</item>
    /// <item>Removal: a Unit reduced to zero health is removed from the Match and from any Battalion,
    /// releasing its population (Req 3.5); an emptied Battalion is disbanded.</item>
    /// <item>Special operations: deploying a completed Doomsday_Weapon executes its elimination effect
    /// against a targeted Nation (Req 9.2), and launching a completed Colony_Ship begins the Nation's
    /// colonization sequence advanced in <see cref="Tick"/> (Req 11.2).</item>
    /// </list>
    ///
    /// Per-Match Unit/Battalion state lives on the <see cref="MatchState"/> and <see cref="Nation"/>;
    /// the system keeps only its internal build queue and colonization sequences, both purely derived
    /// from the fixed-step ticks so results are reproducible on the Host and in headless tests. It
    /// resolves content through the injected <see cref="ICatalog"/> and pays costs through the
    /// injected <see cref="ResourceSystem"/> / <see cref="CivSystem"/>. Following the pipeline
    /// contract, nothing here throws to signal a rejected command.
    /// </summary>
    public sealed class UnitSystem :
        ICommandHandler<RecruitUnitCommand>,
        ICommandHandler<MoveCommand>,
        ICommandHandler<FormBattalionCommand>,
        ICommandHandler<DisbandBattalionCommand>,
        ICommandHandler<DeployDoomsdayCommand>,
        ICommandHandler<LaunchColonyShipCommand>,
        ICommandHandler<ActivateAbilityCommand>
    {
        /// <summary>
        /// Default health restored by an <see cref="AbilityEffectKind.Heal"/> ability activation,
        /// clamped at the Unit's <see cref="UnitDef.MaxHealth"/> (Req 13.2).
        /// </summary>
        public const int DefaultAbilityHealAmount = 10;

        private readonly ICatalog _catalog;
        private readonly ResourceSystem _resources;
        private readonly CivSystem _civ;
        private readonly Pathfinder _pathfinder = new Pathfinder();
        private readonly int _navMaxStepHeight;
        private readonly float _colonizationDurationSeconds;
        private readonly int _experiencePerDamageDealt;
        private readonly int _experiencePerElimination;

        private readonly List<PendingBuild> _buildQueue = new List<PendingBuild>();
        private readonly Dictionary<int, Colonization> _colonizations = new Dictionary<int, Colonization>();

        // Combat events produced during the current tick and awaiting the veterancy XP hook. Fed by
        // the eventual MatchSimulation wiring (task 22) via RecordCombatEvents and drained by Tick
        // through OnCombatResolved; kept as a buffer so the hook stays deterministic and so combat
        // resolution (which lives in CombatSystem) and XP accounting (which lives here) remain
        // decoupled (Req 12.1).
        private readonly List<GameEvent> _pendingCombatEvents = new List<GameEvent>();

        private int _nextUnitId;

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="catalog">Resolves Unit/Technology definitions referenced by commands.</param>
        /// <param name="resourceSystem">Pays recruitment, deployment, and launch costs atomically.</param>
        /// <param name="civSystem">Reserves/releases population for recruited/removed Units.</param>
        /// <param name="colonizationDurationSeconds">
        /// Simulation seconds a launched Colony_Ship's colonization sequence takes to complete
        /// (Req 11.2). Clamped at zero; zero completes on the next tick.
        /// </param>
        /// <param name="navMaxStepHeight">
        /// Maximum surface step (in cells) a ground Unit may traverse between adjacent columns when a
        /// nav grid is derived for movement. Passed through to <see cref="NavGrid"/>.
        /// </param>
        /// <param name="experiencePerDamageDealt">
        /// Veterancy experience granted to an attacking Unit each time it deals damage to an opposing
        /// Unit or Structure (Req 12.1). Clamped at zero.
        /// </param>
        /// <param name="experiencePerElimination">
        /// Additional Veterancy experience granted to an attacking Unit when its attack reduces an
        /// opposing Unit or Structure to zero health (an elimination, Req 12.1). Clamped at zero.
        /// </param>
        public UnitSystem(
            ICatalog catalog,
            ResourceSystem resourceSystem,
            CivSystem civSystem,
            float colonizationDurationSeconds = 0f,
            int navMaxStepHeight = 1,
            int experiencePerDamageDealt = 1,
            int experiencePerElimination = 10)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resources = resourceSystem ?? throw new ArgumentNullException(nameof(resourceSystem));
            _civ = civSystem ?? throw new ArgumentNullException(nameof(civSystem));
            _colonizationDurationSeconds = colonizationDurationSeconds < 0f ? 0f : colonizationDurationSeconds;
            _navMaxStepHeight = navMaxStepHeight;
            _experiencePerDamageDealt = experiencePerDamageDealt < 0 ? 0 : experiencePerDamageDealt;
            _experiencePerElimination = experiencePerElimination < 0 ? 0 : experiencePerElimination;
        }

        /// <summary>
        /// Registers this system's command handlers with the single authoritative
        /// <paramref name="router"/> so recruit/move/battalion/doomsday/colony intents from human
        /// Players and AI_Nations alike flow through the identical pipeline (Req 8.2, 8.5).
        /// </summary>
        public void RegisterHandlers(CommandRouter router)
        {
            if (router == null)
            {
                throw new ArgumentNullException(nameof(router));
            }

            router.Register<RecruitUnitCommand>(this);
            router.Register<MoveCommand>(this);
            router.Register<FormBattalionCommand>(this);
            router.Register<DisbandBattalionCommand>(this);
            router.Register<DeployDoomsdayCommand>(this);
            router.Register<LaunchColonyShipCommand>(this);
            router.Register<ActivateAbilityCommand>(this);
        }

        // ==================================================================
        // Recruitment (Req 3.1)
        // ==================================================================

        /// <summary>
        /// Validates and applies a recruit command (Req 3.1, 5.4). On rejection nothing is mutated;
        /// on acceptance the Resource and population costs are deducted and a build is queued at the
        /// issuing Structure, producing the Unit once its build time elapses.
        /// </summary>
        public CommandResult Handle(RecruitUnitCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            if (!state.Structures.TryGetValue(command.StructureId, out var structure))
            {
                return CommandResult.Reject($"Structure {command.StructureId} does not exist.");
            }

            if (structure.OwnerNationId != nation.Id)
            {
                return CommandResult.Reject(
                    $"Structure {command.StructureId} is not owned by nation {nation.Id}.");
            }

            if (!structure.IsOperational)
            {
                // Under-construction structures have disabled functions (Req 4.4).
                return CommandResult.Reject(
                    $"Structure {command.StructureId} is still under construction and cannot recruit.");
            }

            if (!_catalog.TryGetUnit(command.UnitId, out var unitDef))
            {
                return CommandResult.Reject($"Unknown unit type \"{command.UnitId}\".");
            }

            if (!IsUnitUnlocked(nation, unitDef))
            {
                return CommandResult.Reject($"Unit type \"{command.UnitId}\" is not unlocked.");
            }

            // Validate both gates before mutating anything so a partially-affordable recruit changes
            // nothing (Req 3.1 / 5.4).
            if (!_resources.CanAfford(nation, unitDef.Cost))
            {
                return CommandResult.Reject($"Insufficient resources to recruit \"{command.UnitId}\".");
            }

            if (!_civ.HasAvailablePopulation(nation, unitDef.PopulationCost))
            {
                return CommandResult.Reject(
                    $"Insufficient population to recruit \"{command.UnitId}\".");
            }

            var events = new List<GameEvent>();

            // Both deductions are guaranteed to succeed after the pre-checks above.
            _resources.TryDeduct(nation, unitDef.Cost, out var costEvents);
            events.AddRange(costEvents);

            _civ.TryConsumePopulation(nation, unitDef.PopulationCost, out var popEvents);
            events.AddRange(popEvents);

            _buildQueue.Add(new PendingBuild
            {
                OwnerNationId = nation.Id,
                StructureId = structure.Id,
                Def = unitDef,
                Remaining = unitDef.BuildTimeSeconds < 0f ? 0f : unitDef.BuildTimeSeconds,
            });

            return CommandResult.Accept(events.ToArray());
        }

        // ==================================================================
        // Movement (Req 3.2, 3.4)
        // ==================================================================

        /// <summary>
        /// Validates and applies a move command (Req 3.2, 3.4). Computes a navigable path for every
        /// targeted Unit — or every surviving Battalion member — toward the destination and issues a
        /// move order to each Unit that can reach it. Rejected only when no targeted Unit can reach
        /// the destination, in which case no order is issued.
        /// </summary>
        public CommandResult Handle(MoveCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            // Resolve the target units, either from an explicit list or a Battalion's members (Req 3.4).
            List<int> targetUnitIds;
            if (command.BattalionId.HasValue)
            {
                if (!nation.Battalions.TryGetValue(command.BattalionId.Value, out var battalion))
                {
                    return CommandResult.Reject(
                        $"Battalion {command.BattalionId.Value} does not exist for nation {nation.Id}.");
                }

                targetUnitIds = new List<int>(battalion.MemberUnitIds);
            }
            else
            {
                targetUnitIds = new List<int>(command.UnitIds);
            }

            if (targetUnitIds.Count == 0)
            {
                return CommandResult.Reject("Move command targeted no units.");
            }

            targetUnitIds.Sort();

            // A nav grid derived from the current terrain surface; recomputed per command so it always
            // reflects the latest terrain modifications (Req 3.2, 6.3).
            var grid = new NavGrid(state.Terrain, _navMaxStepHeight);

            var events = new List<GameEvent>();
            bool anyMoved = false;

            foreach (var unitId in targetUnitIds)
            {
                if (!state.Units.TryGetValue(unitId, out var unit))
                {
                    continue;
                }

                // Only the issuing Nation's living units respond to its move command.
                if (unit.OwnerNationId != nation.Id || unit.Health <= 0)
                {
                    continue;
                }

                var start = ToCell(unit.Position);
                var path = _pathfinder.FindPath(grid, unit.Def.Role, start, command.Destination);
                if (!path.Found)
                {
                    continue;
                }

                anyMoved = true;

                if (path.Waypoints.Count <= 1)
                {
                    // Already standing at the destination column: nothing to travel.
                    unit.CurrentOrder = UnitOrder.Idle;
                    events.Add(new UnitMovedEvent(nation.Id, unit.Id, command.Destination, unit.Position, true));
                    continue;
                }

                unit.CurrentOrder = UnitOrder.Move(command.Destination, path.Waypoints, 1);
                events.Add(new UnitMovedEvent(nation.Id, unit.Id, command.Destination, unit.Position, false));
            }

            if (!anyMoved)
            {
                return CommandResult.Reject("No targeted unit can reach the destination.");
            }

            return CommandResult.Accept(events.ToArray());
        }

        // ==================================================================
        // Battalions (Req 3.3, 3.4)
        // ==================================================================

        /// <summary>
        /// Validates and applies a form-battalion command (Req 3.3). Requires at least two of the
        /// referenced Units to exist and be owned by the Nation; creates a named Battalion, assigns
        /// each member (removing it from any previous Battalion first), and emits a
        /// <see cref="BattalionFormedEvent"/>. Rejected with no state change when fewer than two valid
        /// Units are supplied.
        /// </summary>
        public CommandResult Handle(FormBattalionCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            // Collect the distinct, owned, living members in deterministic id order.
            var memberIds = new List<int>();
            var seen = new HashSet<int>();
            var sorted = new List<int>(command.UnitIds);
            sorted.Sort();
            foreach (var unitId in sorted)
            {
                if (!seen.Add(unitId))
                {
                    continue;
                }

                if (state.Units.TryGetValue(unitId, out var unit)
                    && unit.OwnerNationId == nation.Id
                    && unit.Health > 0)
                {
                    memberIds.Add(unitId);
                }
            }

            if (memberIds.Count < 2)
            {
                return CommandResult.Reject(
                    "A Battalion requires at least two of the Nation's living units.");
            }

            int battalionId = AllocateBattalionId(nation);
            var battalion = new Battalion(battalionId, command.Name, memberIds);
            nation.Battalions[battalionId] = battalion;

            foreach (var unitId in memberIds)
            {
                var unit = state.Units[unitId];

                // Detach from a previous Battalion so membership sets stay disjoint.
                if (unit.BattalionId.HasValue && unit.BattalionId.Value != battalionId
                    && nation.Battalions.TryGetValue(unit.BattalionId.Value, out var previous))
                {
                    previous.MemberUnitIds.Remove(unitId);
                }

                unit.BattalionId = battalionId;
            }

            return CommandResult.Accept(
                new BattalionFormedEvent(nation.Id, battalionId, command.Name, memberIds));
        }

        /// <summary>
        /// Validates and applies a disband-battalion command (Req 3.3). Removes the Battalion and
        /// clears the Battalion assignment on each surviving member (which otherwise remains in the
        /// Match), emitting a <see cref="BattalionDisbandedEvent"/>. An unknown Battalion is rejected.
        /// </summary>
        public CommandResult Handle(DisbandBattalionCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            if (!nation.Battalions.TryGetValue(command.BattalionId, out var battalion))
            {
                return CommandResult.Reject(
                    $"Battalion {command.BattalionId} does not exist for nation {nation.Id}.");
            }

            foreach (var unitId in battalion.MemberUnitIds)
            {
                if (state.Units.TryGetValue(unitId, out var unit) && unit.BattalionId == command.BattalionId)
                {
                    unit.BattalionId = null;
                }
            }

            nation.Battalions.Remove(command.BattalionId);

            return CommandResult.Accept(
                new BattalionDisbandedEvent(nation.Id, command.BattalionId, false));
        }

        // ==================================================================
        // Doomsday deployment (Req 9.2) and Colony Ship launch (Req 11.2)
        // ==================================================================

        /// <summary>
        /// Validates and applies a Doomsday_Weapon deployment (Req 9.2). Requires the weapon's
        /// research to be complete, a distinct not-yet-eliminated target Nation, and an affordable
        /// deployment cost; on acceptance pays the cost and executes the elimination effect — removing
        /// the target's Units and Structures and marking it eliminated — then emits a
        /// <see cref="NationEliminatedEvent"/> (Req 9.2, 9.3).
        /// </summary>
        public CommandResult Handle(DeployDoomsdayCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            if (command.TargetNationId == nation.Id)
            {
                return CommandResult.Reject("A Doomsday_Weapon cannot target the deploying Nation.");
            }

            if (!state.Nations.TryGetValue(command.TargetNationId, out var target))
            {
                return CommandResult.Reject($"Target nation {command.TargetNationId} does not exist.");
            }

            if (target.Eliminated)
            {
                return CommandResult.Reject($"Target nation {command.TargetNationId} is already eliminated.");
            }

            if (!_catalog.TryGetTechnology(command.TechnologyId, out var tech)
                || tech.Category != TechCategory.DoomsdayWeapon)
            {
                return CommandResult.Reject(
                    $"\"{command.TechnologyId}\" is not a Doomsday_Weapon technology.");
            }

            if (!nation.CompletedTechIds.Contains(tech.Id))
            {
                return CommandResult.Reject(
                    $"Doomsday_Weapon \"{tech.Id}\" has not been researched.");
            }

            // Pay the deployment cost atomically; on failure nothing is mutated (Req 9.2).
            if (!_resources.TryDeduct(nation, tech.DeploymentCost, out var costEvents))
            {
                return CommandResult.Reject($"Insufficient resources to deploy \"{tech.Id}\".");
            }

            var events = new List<GameEvent>(costEvents);

            // Execute the elimination effect: remove the target's forces and mark it eliminated
            // (Req 9.2, 9.3). Iterate snapshots so the collections are not modified while enumerated.
            var targetUnitIds = new List<int>();
            foreach (var unit in state.Units.Values)
            {
                if (unit.OwnerNationId == target.Id)
                {
                    targetUnitIds.Add(unit.Id);
                }
            }

            targetUnitIds.Sort();
            foreach (var unitId in targetUnitIds)
            {
                state.Units.Remove(unitId);
            }

            var targetStructureIds = new List<int>();
            foreach (var structure in state.Structures.Values)
            {
                if (structure.OwnerNationId == target.Id)
                {
                    targetStructureIds.Add(structure.Id);
                }
            }

            targetStructureIds.Sort();
            foreach (var structureId in targetStructureIds)
            {
                state.Structures.Remove(structureId);
            }

            target.Battalions.Clear();
            target.Eliminated = true;

            events.Add(new NationEliminatedEvent(target.Id, nation.Id, tech.Id));

            return CommandResult.Accept(events.ToArray());
        }

        /// <summary>
        /// Validates and applies a Colony_Ship launch (Req 11.2). Requires the referenced Unit to be a
        /// Colony_Ship owned by the Nation, the Nation to have reached the Space Era (Req 11.1), no
        /// colonization sequence already in progress, and an affordable launch cost; on acceptance
        /// pays the launch cost and begins the Nation's colonization sequence, emitting a
        /// <see cref="ColonizationEvent"/> with phase <see cref="ColonizationPhase.Started"/>.
        /// </summary>
        public CommandResult Handle(LaunchColonyShipCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            if (!state.Units.TryGetValue(command.ColonyShipUnitId, out var ship))
            {
                return CommandResult.Reject($"Colony_Ship unit {command.ColonyShipUnitId} does not exist.");
            }

            if (ship.OwnerNationId != nation.Id)
            {
                return CommandResult.Reject(
                    $"Colony_Ship unit {command.ColonyShipUnitId} is not owned by nation {nation.Id}.");
            }

            if (ship.Def == null || ship.Def.Role != UnitRole.ColonyShip)
            {
                return CommandResult.Reject(
                    $"Unit {command.ColonyShipUnitId} is not a Colony_Ship.");
            }

            if (nation.CurrentEra < Era.Space)
            {
                return CommandResult.Reject(
                    $"Nation {nation.Id} has not reached the Space Era and cannot launch a Colony_Ship.");
            }

            if (_colonizations.TryGetValue(nation.Id, out var existing) && !existing.Complete)
            {
                return CommandResult.Reject(
                    $"Nation {nation.Id} already has a colonization sequence in progress.");
            }

            if (!_resources.TryDeduct(nation, ship.Def.LaunchCost, out var costEvents))
            {
                return CommandResult.Reject(
                    $"Insufficient resources to launch Colony_Ship \"{ship.Def.Id}\".");
            }

            var events = new List<GameEvent>(costEvents);

            _colonizations[nation.Id] = new Colonization
            {
                NationId = nation.Id,
                ColonyShipUnitId = ship.Id,
                Progress = 0f,
                Duration = _colonizationDurationSeconds,
                Complete = false,
            };

            events.Add(new ColonizationEvent(
                nation.Id, ColonizationPhase.Started, 0f, _colonizationDurationSeconds));

            return CommandResult.Accept(events.ToArray());
        }

        // ==================================================================
        // Unit abilities (Req 13.1, 13.2, 13.3)
        // ==================================================================

        /// <summary>
        /// Validates and applies a <see cref="ActivateAbilityCommand"/> (Req 13.2, 13.3).
        ///
        /// Looks up the target Unit and the <see cref="UnitAbilityDef"/> on its type matching
        /// <see cref="ActivateAbilityCommand.AbilityId"/>. The activation is accepted only when the
        /// ability's remaining cooldown is non-positive (fully elapsed) <em>and</em> the owning Nation
        /// can afford the ability's <see cref="UnitAbilityDef.Cost"/>; on acceptance it executes the
        /// ability's <see cref="AbilityEffectKind"/> effect, deducts the cost through the shared
        /// <see cref="ResourceSystem"/>, starts the cooldown by setting
        /// <see cref="UnitInstance.AbilityRemainingCooldown"/> for the ability to its full
        /// <see cref="UnitAbilityDef.CooldownSeconds"/>, and emits an <see cref="AbilityActivatedEvent"/>.
        ///
        /// On failure it returns a <see cref="CommandResult.Reject"/> whose reason begins with the
        /// distinguishing token <c>"cooldown-active"</c> or <c>"insufficient-resources"</c> (Req 13.3),
        /// and leaves the Unit's cooldown state and the Nation's resource pool exactly unchanged. An
        /// unknown Unit, foreign Unit, or unknown ability id is likewise rejected with no mutation.
        /// </summary>
        public CommandResult Handle(ActivateAbilityCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            var nation = state.Nations[command.IssuingNationId];

            if (!state.Units.TryGetValue(command.UnitId, out var unit))
            {
                return CommandResult.Reject($"Unit {command.UnitId} does not exist.");
            }

            if (unit.OwnerNationId != nation.Id)
            {
                return CommandResult.Reject(
                    $"Unit {command.UnitId} is not owned by nation {nation.Id}.");
            }

            var ability = FindAbility(unit, command.AbilityId);
            if (ability == null)
            {
                return CommandResult.Reject(
                    $"Unit {command.UnitId} (\"{unit.Def?.Id}\") has no ability \"{command.AbilityId}\".");
            }

            // Precondition 1: the cooldown must have fully elapsed. An absent entry means ready.
            if (unit.AbilityRemainingCooldown.TryGetValue(command.AbilityId, out var remaining)
                && remaining > Fixed.Zero)
            {
                return CommandResult.Reject(
                    $"cooldown-active: ability \"{command.AbilityId}\" on unit {command.UnitId} "
                    + $"has {remaining} remaining.");
            }

            // Precondition 2: the owning Nation must be able to afford the ability's cost. Checked
            // before any mutation so a rejected activation changes nothing (Req 13.3).
            if (!_resources.CanAfford(nation, ability.Cost))
            {
                return CommandResult.Reject(
                    $"insufficient-resources: nation {nation.Id} cannot afford ability "
                    + $"\"{command.AbilityId}\".");
            }

            var events = new List<GameEvent>();

            // Deduct the cost (guaranteed to succeed after the CanAfford pre-check).
            _resources.TryDeduct(nation, ability.Cost, out var costEvents);
            events.AddRange(costEvents);

            // Execute the ability's concrete effect (Req 13.2).
            ExecuteAbilityEffect(unit, ability, command, events);

            // Start the cooldown at its full defined duration (Req 13.2, feeds Req 13.4 decrement).
            unit.AbilityRemainingCooldown[command.AbilityId] = ability.CooldownSeconds;

            events.Add(new AbilityActivatedEvent(
                nation.Id, unit.Id, ability.Id, ability.EffectKind, command.TargetPosition));

            return CommandResult.Accept(events.ToArray());
        }

        /// <summary>
        /// Returns the <see cref="UnitAbilityDef"/> with id <paramref name="abilityId"/> defined on
        /// <paramref name="unit"/>'s type, or <c>null</c> when the Unit type defines no such ability
        /// (Req 13.1).
        /// </summary>
        private static UnitAbilityDef FindAbility(UnitInstance unit, string abilityId)
        {
            var abilities = unit.Def?.AbilityDefs;
            if (abilities == null || abilityId == null)
            {
                return null;
            }

            foreach (var ability in abilities)
            {
                if (ability != null && ability.Id == abilityId)
                {
                    return ability;
                }
            }

            return null;
        }

        /// <summary>
        /// Executes the concrete effect of an activated ability (Req 13.2). Kept intentionally minimal
        /// but real: <see cref="AbilityEffectKind.Heal"/> restores <see cref="DefaultAbilityHealAmount"/>
        /// health to the acting Unit, clamped at its <see cref="UnitDef.MaxHealth"/>;
        /// <see cref="AbilityEffectKind.Buff"/>, <see cref="AbilityEffectKind.Cloak"/>, and
        /// <see cref="AbilityEffectKind.Bombard"/> currently apply no additional Core state change
        /// beyond the cost deduction, cooldown start, and the emitted <see cref="AbilityActivatedEvent"/>
        /// (their presentational/targeted resolution is handled by the Unity/combat layers); the event
        /// carries the effect kind and any target position so those layers can react.
        /// </summary>
        private static void ExecuteAbilityEffect(
            UnitInstance unit, UnitAbilityDef ability, ActivateAbilityCommand command, List<GameEvent> events)
        {
            switch (ability.EffectKind)
            {
                case AbilityEffectKind.Heal:
                    int maxHealth = unit.Def?.MaxHealth ?? unit.Health;
                    int healed = unit.Health + DefaultAbilityHealAmount;
                    unit.Health = healed > maxHealth ? maxHealth : healed;
                    break;

                case AbilityEffectKind.Buff:
                case AbilityEffectKind.Cloak:
                case AbilityEffectKind.Bombard:
                default:
                    // No direct Core-state mutation for now beyond cost/cooldown/event; documented above.
                    break;
            }
        }

        // ==================================================================
        // Simulation tick
        // ==================================================================

        /// <summary>
        /// Advances the system by <paramref name="deltaSeconds"/>: progresses build queues (spawning
        /// completed Units), advances Units along their movement orders, progresses colonization
        /// sequences, and sweeps any zero-health Units from the Match (Req 3.1, 3.2, 3.5, 11.2). A
        /// non-positive delta still runs the dead-unit sweep but performs no time-based progress.
        /// Returns the ordered events produced.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state, float deltaSeconds)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var events = new List<GameEvent>();

            if (deltaSeconds > 0f)
            {
                AdvanceBuildQueue(state, deltaSeconds, events);
                AdvanceMovement(state, deltaSeconds, events);
                AdvanceColonization(deltaSeconds, events);
                AdvanceAbilityCooldowns(state, deltaSeconds);
            }

            // Drain this tick's combat events through the veterancy XP hook (Req 12.1, 12.2, 12.6),
            // following the same drain-then-process pattern the other advance steps use. This runs
            // regardless of the delta because experience accrual is event-driven, not time-based.
            DrainCombatResolvedForVeterancy(state, events);

            SweepEliminatedUnits(state, events);

            return events;
        }

        private void AdvanceBuildQueue(MatchState state, float deltaSeconds, List<GameEvent> events)
        {
            if (_buildQueue.Count == 0)
            {
                return;
            }

            // Iterate a snapshot; keep incomplete builds and spawn the completed ones in queue order.
            var remaining = new List<PendingBuild>(_buildQueue.Count);
            foreach (var build in _buildQueue)
            {
                build.Remaining -= deltaSeconds;
                if (build.Remaining > 0f)
                {
                    remaining.Add(build);
                    continue;
                }

                SpawnBuiltUnit(state, build, events);
            }

            _buildQueue.Clear();
            _buildQueue.AddRange(remaining);
        }

        private void SpawnBuiltUnit(MatchState state, PendingBuild build, List<GameEvent> events)
        {
            // The producing Structure may have been destroyed while the unit was building; spawn at
            // its last known origin if present, otherwise skip production (the cost was already paid).
            CellCoord spawnCell;
            if (state.Structures.TryGetValue(build.StructureId, out var structure))
            {
                spawnCell = structure.Origin;
            }
            else
            {
                return;
            }

            int unitId = AllocateUnitId(state);
            var unit = new UnitInstance(unitId, build.OwnerNationId, build.Def, WorldPosition.FromCell(spawnCell));
            state.Units[unitId] = unit;

            events.Add(new UnitRecruitedEvent(
                build.OwnerNationId, unitId, build.Def.Id, build.StructureId, spawnCell));
        }

        private void AdvanceMovement(MatchState state, float deltaSeconds, List<GameEvent> events)
        {
            var dt = Fixed.FromFloat(deltaSeconds);

            foreach (var unitId in SortedUnitIds(state))
            {
                if (!state.Units.TryGetValue(unitId, out var unit))
                {
                    continue;
                }

                var order = unit.CurrentOrder;
                if (order.Kind != UnitOrder.OrderKind.Move || order.Path.Count == 0)
                {
                    continue;
                }

                float moveSpeed = unit.Def?.MoveSpeed ?? 0f;
                if (moveSpeed <= 0f)
                {
                    continue;
                }

                Fixed budget = Fixed.FromFloat(moveSpeed) * dt;
                if (budget <= Fixed.Zero)
                {
                    continue;
                }

                var path = order.Path;
                int index = order.WaypointIndex;
                var position = unit.Position;

                while (index < path.Count && budget > Fixed.Zero)
                {
                    var target = path[index];
                    Fixed remaining = ManhattanDistance(position, target);

                    if (remaining <= Fixed.Zero)
                    {
                        // Coincident waypoint (e.g. the start cell); step past it without spending budget.
                        index++;
                        continue;
                    }

                    if (remaining <= budget)
                    {
                        position = target;
                        budget = budget - remaining;
                        index++;
                    }
                    else
                    {
                        Fixed fraction = budget / remaining;
                        position = Lerp(position, target, fraction);
                        budget = Fixed.Zero;
                    }
                }

                unit.Position = position;

                if (index >= path.Count)
                {
                    unit.CurrentOrder = UnitOrder.Idle;
                    events.Add(new UnitMovedEvent(unit.OwnerNationId, unit.Id, order.Destination, position, true));
                }
                else
                {
                    unit.CurrentOrder = order.WithWaypointIndex(index);
                }
            }
        }

        private void AdvanceColonization(float deltaSeconds, List<GameEvent> events)
        {
            if (_colonizations.Count == 0)
            {
                return;
            }

            var nationIds = new List<int>(_colonizations.Keys);
            nationIds.Sort();

            foreach (var nationId in nationIds)
            {
                var colonization = _colonizations[nationId];
                if (colonization.Complete)
                {
                    continue;
                }

                colonization.Progress += deltaSeconds;
                if (colonization.Progress >= colonization.Duration)
                {
                    colonization.Progress = colonization.Duration;
                    colonization.Complete = true;
                    events.Add(new ColonizationEvent(
                        nationId, ColonizationPhase.Completed, colonization.Progress, colonization.Duration));
                }
            }
        }

        /// <summary>
        /// Decrements every non-zero ability cooldown on every Unit by <paramref name="deltaSeconds"/>,
        /// clamping at zero so a remaining cooldown never goes negative (Req 13.4). A ready ability
        /// (absent or non-positive entry) is left untouched. Units are iterated in ascending id order
        /// for deterministic, reproducible results.
        /// </summary>
        private void AdvanceAbilityCooldowns(MatchState state, float deltaSeconds)
        {
            Fixed dt = Fixed.FromFloat(deltaSeconds);
            if (dt <= Fixed.Zero)
            {
                return;
            }

            foreach (var unitId in SortedUnitIds(state))
            {
                if (!state.Units.TryGetValue(unitId, out var unit)
                    || unit.AbilityRemainingCooldown.Count == 0)
                {
                    continue;
                }

                // Snapshot the keys so the map can be updated while iterating.
                var abilityIds = new List<string>(unit.AbilityRemainingCooldown.Keys);
                abilityIds.Sort(System.StringComparer.Ordinal);

                foreach (var abilityId in abilityIds)
                {
                    Fixed remaining = unit.AbilityRemainingCooldown[abilityId];
                    if (remaining <= Fixed.Zero)
                    {
                        continue;
                    }

                    Fixed next = remaining - dt;
                    if (next < Fixed.Zero)
                    {
                        next = Fixed.Zero;
                    }

                    unit.AbilityRemainingCooldown[abilityId] = next;
                }
            }
        }

        // ==================================================================
        // Veterancy XP hook (Req 12.1, 12.2, 12.4, 12.6)
        // ==================================================================

        /// <summary>
        /// Records combat events produced during the current tick for the veterancy XP hook to process
        /// on the next <see cref="Tick"/> (or the current one, if called before it drains). This is the
        /// intended wiring point for <c>MatchSimulation</c> (task 22): once <see cref="CombatSystem"/>'s
        /// per-tick resolution is threaded into the loop, its emitted <see cref="CombatResolvedEvent"/>s
        /// and <see cref="StructureCombatResolvedEvent"/>s are handed here so the attacking Units gain
        /// experience. Non-combat events are ignored. Passing <c>null</c> records nothing.
        /// </summary>
        public void RecordCombatEvents(IEnumerable<GameEvent> combatEvents)
        {
            if (combatEvents == null)
            {
                return;
            }

            foreach (var evt in combatEvents)
            {
                if (evt is CombatResolvedEvent || evt is StructureCombatResolvedEvent)
                {
                    _pendingCombatEvents.Add(evt);
                }
            }
        }

        /// <summary>
        /// Drains the buffered combat events recorded via <see cref="RecordCombatEvents"/> through the
        /// veterancy hook and appends any produced <see cref="VeterancyTierAdvancedEvent"/>s to
        /// <paramref name="events"/>. Called by <see cref="Tick"/> each step.
        /// </summary>
        private void DrainCombatResolvedForVeterancy(MatchState state, List<GameEvent> events)
        {
            if (_pendingCombatEvents.Count == 0)
            {
                return;
            }

            var batch = new List<GameEvent>(_pendingCombatEvents);
            _pendingCombatEvents.Clear();
            events.AddRange(OnCombatResolved(state, batch));
        }

        /// <summary>
        /// The veterancy experience hook (Req 12.1, 12.2, 12.4, 12.6). For each
        /// <see cref="CombatResolvedEvent"/> / <see cref="StructureCombatResolvedEvent"/> in
        /// <paramref name="combatEvents"/> (any other event type is ignored), grants the configured
        /// experience to the still-present attacking Unit: <c>experiencePerDamageDealt</c> for dealing
        /// damage plus, when the event reduced its target to zero health, an additional
        /// <c>experiencePerElimination</c> for the elimination (Req 12.1). After each grant it advances
        /// the attacker's <see cref="UnitInstance.VeterancyTierIndex"/> across <em>every</em> tier its
        /// new accumulated experience crosses, capped at the highest defined tier in the Unit type's
        /// <c>VeterancyCurve</c> (Req 12.2, 12.4), emitting exactly one
        /// <see cref="VeterancyTierAdvancedEvent"/> per tier crossed (Req 12.6).
        ///
        /// <para>
        /// This method is public and side-effect-scoped to the Units named in the events, so it is
        /// directly unit- and property-testable in isolation now; the eventual per-tick wiring that
        /// feeds it <see cref="CombatSystem"/>'s tick events lives in <c>MatchSimulation</c> (task 22),
        /// which will call <see cref="RecordCombatEvents"/> so <see cref="Tick"/> drains it here.
        /// </para>
        /// </summary>
        public IReadOnlyList<GameEvent> OnCombatResolved(MatchState state, IReadOnlyList<GameEvent> combatEvents)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (combatEvents == null || combatEvents.Count == 0)
            {
                return Array.Empty<GameEvent>();
            }

            var produced = new List<GameEvent>();

            foreach (var evt in combatEvents)
            {
                int attackerId;
                bool destroyed;

                if (evt is CombatResolvedEvent cre)
                {
                    attackerId = cre.AttackerUnitId;
                    destroyed = cre.DefenderDestroyed;
                }
                else if (evt is StructureCombatResolvedEvent scre)
                {
                    attackerId = scre.AttackerUnitId;
                    destroyed = scre.DefenderDestroyed;
                }
                else
                {
                    continue;
                }

                // The attacker may already be gone (e.g. removed earlier this tick); if so there is no
                // Veterancy track to credit.
                if (!state.Units.TryGetValue(attackerId, out var attacker))
                {
                    continue;
                }

                int xp = _experiencePerDamageDealt + (destroyed ? _experiencePerElimination : 0);
                GrantExperience(attacker, xp, produced);
            }

            return produced;
        }

        /// <summary>
        /// Adds <paramref name="xp"/> to <paramref name="unit"/>'s accumulated
        /// <see cref="UnitInstance.VeterancyExperience"/> and advances its
        /// <see cref="UnitInstance.VeterancyTierIndex"/> to the highest tier its new total reaches,
        /// emitting one <see cref="VeterancyTierAdvancedEvent"/> per tier crossed (Req 12.2, 12.6).
        /// A non-positive grant or a Unit type with no <c>VeterancyCurve</c> advances no tier.
        /// </summary>
        private static void GrantExperience(UnitInstance unit, int xp, List<GameEvent> events)
        {
            if (xp > 0)
            {
                unit.VeterancyExperience += xp;
            }

            var curve = unit.Def?.VeterancyCurve;
            if (curve == null || curve.Count == 0)
            {
                return;
            }

            int newTier = ComputeTierIndex(curve, unit.VeterancyExperience);
            while (unit.VeterancyTierIndex < newTier)
            {
                unit.VeterancyTierIndex++;
                events.Add(new VeterancyTierAdvancedEvent(
                    unit.OwnerNationId, unit.Id, unit.VeterancyTierIndex));
            }
        }

        /// <summary>
        /// The Veterancy_Tier index a Unit with accumulated experience <paramref name="experience"/>
        /// occupies: the highest 0-based index into the ascending-by-threshold
        /// <paramref name="curve"/> whose <see cref="VeterancyTierDef.ExperienceThreshold"/> does not
        /// exceed <paramref name="experience"/>, capped at the highest defined tier index (Req 12.2,
        /// 12.4). Pure function of <c>(curve, experience)</c> (Property 10). The base tier (index 0) is
        /// authored with threshold 0 and, by convention, no stat bonus ("0 = base/no tier"), so a Unit
        /// with no experience sits at the base tier with no bonus; an empty curve is tier 0.
        /// </summary>
        internal static int ComputeTierIndex(List<VeterancyTierDef> curve, int experience)
        {
            int tier = 0;
            for (int i = 0; i < curve.Count; i++)
            {
                if (curve[i] != null && curve[i].ExperienceThreshold <= experience)
                {
                    tier = i;
                }
                else
                {
                    // Ascending thresholds: once one is out of reach, so are all later ones.
                    break;
                }
            }

            return tier;
        }

        private void SweepEliminatedUnits(MatchState state, List<GameEvent> events)
        {
            List<int> dead = null;
            foreach (var unit in state.Units.Values)
            {
                if (unit.Health <= 0)
                {
                    (dead ??= new List<int>()).Add(unit.Id);
                }
            }

            if (dead == null)
            {
                return;
            }

            dead.Sort();
            foreach (var unitId in dead)
            {
                events.AddRange(RemoveUnit(state, unitId));
            }
        }

        // ==================================================================
        // Removal (Req 3.5)
        // ==================================================================

        /// <summary>
        /// Removes the Unit <paramref name="unitId"/> from the Match and from any Battalion of which
        /// it is a member, releasing its population back to the owning Nation, and disbands a Battalion
        /// left with no members (Req 3.3, 3.5). Returns the events produced (a
        /// <see cref="UnitEliminatedEvent"/>, any population-change event, and a
        /// <see cref="BattalionDisbandedEvent"/> when a Battalion is emptied). An unknown unit id
        /// yields no events.
        ///
        /// <para>
        /// The Unit's Veterancy state (<see cref="UnitInstance.VeterancyTierIndex"/> and
        /// <see cref="UnitInstance.VeterancyExperience"/>) and ability cooldowns live on the
        /// <see cref="UnitInstance"/> itself, so removing the instance from <see cref="MatchState.Units"/>
        /// here discards them with it (Req 12.5, Req 12.3). The <see cref="UnitSystem"/> keeps no
        /// separate per-Unit veterancy or cooldown store that could outlive the instance, so no extra
        /// teardown is required for a removed Unit's veterancy to be gone.
        /// </para>
        /// </summary>
        public IReadOnlyList<GameEvent> RemoveUnit(MatchState state, int unitId, bool releasePopulation = true)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.Units.TryGetValue(unitId, out var unit))
            {
                return Array.Empty<GameEvent>();
            }

            state.Units.Remove(unitId);

            var events = new List<GameEvent>();

            if (state.Nations.TryGetValue(unit.OwnerNationId, out var nation))
            {
                // Remove from its Battalion and disband the Battalion if it is now empty (Req 3.3, 3.5).
                if (unit.BattalionId.HasValue
                    && nation.Battalions.TryGetValue(unit.BattalionId.Value, out var battalion))
                {
                    battalion.MemberUnitIds.Remove(unitId);
                    if (battalion.MemberUnitIds.Count == 0)
                    {
                        nation.Battalions.Remove(battalion.Id);
                        events.Add(new BattalionDisbandedEvent(nation.Id, battalion.Id, true));
                    }
                }

                if (releasePopulation && unit.Def != null)
                {
                    events.AddRange(_civ.ReleasePopulation(nation, unit.Def.PopulationCost));
                }
            }

            events.Add(new UnitEliminatedEvent(unit.OwnerNationId, unitId, unit.Def?.Id));

            return events;
        }

        // ==================================================================
        // Colonization queries (for the Victory_System, task 12)
        // ==================================================================

        /// <summary>
        /// True when <paramref name="nationId"/>'s Colony_Ship colonization sequence has completed
        /// (Req 11.3). Pure query — never mutates state.
        /// </summary>
        public bool IsColonizationComplete(int nationId)
            => _colonizations.TryGetValue(nationId, out var colonization) && colonization.Complete;

        /// <summary>
        /// True when <paramref name="nationId"/> has begun a colonization sequence, whether or not it
        /// has completed (Req 11.2).
        /// </summary>
        public bool HasColonizationStarted(int nationId) => _colonizations.ContainsKey(nationId);

        /// <summary>The number of Units currently queued for production across all Structures.</summary>
        public int PendingBuildCount => _buildQueue.Count;

        // ==================================================================
        // Helpers
        // ==================================================================

        private bool IsUnitUnlocked(Nation nation, UnitDef def)
        {
            if (def.Era <= nation.CurrentEra)
            {
                return true;
            }

            foreach (var techId in nation.CompletedTechIds)
            {
                if (_catalog.TryGetTechnology(techId, out var tech))
                {
                    foreach (var unlockedId in tech.UnlocksUnits)
                    {
                        if (unlockedId == def.Id)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Allocates a Match-unique, monotonically increasing Unit id, never reusing an id currently
        /// present in <paramref name="state"/>, so spawned Units never collide with seeded ones or
        /// with each other across ticks.
        /// </summary>
        private int AllocateUnitId(MatchState state)
        {
            int max = 0;
            foreach (var id in state.Units.Keys)
            {
                if (id > max)
                {
                    max = id;
                }
            }

            if (_nextUnitId <= max)
            {
                _nextUnitId = max + 1;
            }

            return _nextUnitId++;
        }

        /// <summary>Allocates a Battalion id unique within <paramref name="nation"/>.</summary>
        private static int AllocateBattalionId(Nation nation)
        {
            int max = 0;
            foreach (var id in nation.Battalions.Keys)
            {
                if (id > max)
                {
                    max = id;
                }
            }

            return max + 1;
        }

        /// <summary>Returns the Match's unit ids sorted ascending for deterministic iteration.</summary>
        private static List<int> SortedUnitIds(MatchState state)
        {
            var ids = new List<int>(state.Units.Keys);
            ids.Sort();
            return ids;
        }

        /// <summary>
        /// Converts a continuous <see cref="WorldPosition"/> to the terrain cell containing it,
        /// truncating each component toward zero (deterministic, no floating-point).
        /// </summary>
        private static CellCoord ToCell(WorldPosition position)
            => new CellCoord(position.X.ToInt(), position.Y.ToInt(), position.Z.ToInt());

        /// <summary>
        /// The L1 (taxicab) distance between two positions in fixed-point. Movement measures distance
        /// this way so segment lengths — and therefore arrival — are exact and reproducible without
        /// any square-root/floating-point arithmetic.
        /// </summary>
        private static Fixed ManhattanDistance(WorldPosition a, WorldPosition b)
            => Abs(b.X - a.X) + Abs(b.Y - a.Y) + Abs(b.Z - a.Z);

        private static Fixed Abs(Fixed value) => value < Fixed.Zero ? -value : value;

        /// <summary>
        /// Linearly interpolates each component from <paramref name="from"/> toward
        /// <paramref name="to"/> by <paramref name="fraction"/> (expected in [0, 1]) in fixed-point.
        /// </summary>
        private static WorldPosition Lerp(WorldPosition from, WorldPosition to, Fixed fraction)
        {
            Fixed x = from.X + ((to.X - from.X) * fraction);
            Fixed y = from.Y + ((to.Y - from.Y) * fraction);
            Fixed z = from.Z + ((to.Z - from.Z) * fraction);
            return new WorldPosition(x, y, z);
        }

        // ------------------------------------------------------------------
        // Internal per-Match progress state (derived, reproducible from ticks)
        // ------------------------------------------------------------------

        private sealed class PendingBuild
        {
            public int OwnerNationId;
            public int StructureId;
            public UnitDef Def;
            public float Remaining;
        }

        private sealed class Colonization
        {
            public int NationId;
            public int ColonyShipUnitId;
            public float Progress;
            public float Duration;
            public bool Complete;
        }
    }
}
