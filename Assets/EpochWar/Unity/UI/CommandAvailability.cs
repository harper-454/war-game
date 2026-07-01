using System;
using System.Collections.Generic;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The single, engine-free source of truth for whether a UI command control is currently
    /// available (Req 7.5, Property 30).
    ///
    /// A command button (recruit, place Structure, initiate research, form Battalion) must be
    /// enabled <em>if and only if</em> its corresponding action would currently be accepted by the
    /// authoritative command pipeline. Rather than duplicate that logic inside a MonoBehaviour — which
    /// could drift from the systems' handlers and cannot be unit-tested — the availability checks live
    /// here as pure queries that delegate to the very same side-effect-free predicates the
    /// <see cref="ResourceSystem"/>, <see cref="TechSystem"/>, <see cref="CivSystem"/>,
    /// <see cref="UnitSystem"/>, and <see cref="BaseSystem"/> expose. The UI binds each control's
    /// <c>SetEnabled(...)</c> to the matching method here, and the optional property test (task 16.4)
    /// verifies these predicates against the handlers over generated Nation states (Property 30).
    ///
    /// The checks are deliberately <em>location/target independent</em> where a placement or move
    /// target is a per-click argument: they answer "is this <em>kind</em> of action available to the
    /// Nation right now?" (unlocked + affordable + enough population), matching what a persistent
    /// control's enabled state represents. Terrain occupancy/validity for a specific cell is resolved
    /// when the command is actually issued (Req 4.2), never gating the button itself.
    /// </summary>
    public sealed class CommandAvailability
    {
        private readonly ICatalog _catalog;
        private readonly ResourceSystem _resources;
        private readonly TechSystem _tech;
        private readonly CivSystem _civ;
        private readonly BaseSystem _baseSystem;

        public CommandAvailability(
            ICatalog catalog,
            ResourceSystem resourceSystem,
            TechSystem techSystem,
            CivSystem civSystem,
            BaseSystem baseSystem)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resources = resourceSystem ?? throw new ArgumentNullException(nameof(resourceSystem));
            _tech = techSystem ?? throw new ArgumentNullException(nameof(techSystem));
            _civ = civSystem ?? throw new ArgumentNullException(nameof(civSystem));
            _baseSystem = baseSystem ?? throw new ArgumentNullException(nameof(baseSystem));
        }

        /// <summary>
        /// True when the Nation could recruit <paramref name="unitId"/> at Structure
        /// <paramref name="structureId"/> right now: the Structure exists, is owned by the Nation and
        /// operational (Req 4.4); the Unit type is unlocked (Req 3.1); and the Nation can afford the
        /// Resource cost (Req 2.3) and the population cost (Req 5.4). Mirrors
        /// <see cref="UnitSystem.Handle(EpochWar.Core.Commands.RecruitUnitCommand, MatchState)"/>'s
        /// location-independent acceptance conditions. Pure query.
        /// </summary>
        public bool CanRecruit(MatchState state, Nation nation, int structureId, string unitId)
        {
            if (state == null || nation == null || string.IsNullOrEmpty(unitId))
            {
                return false;
            }

            if (nation.Eliminated)
            {
                return false;
            }

            if (!state.Structures.TryGetValue(structureId, out var structure))
            {
                return false;
            }

            if (structure.OwnerNationId != nation.Id || !structure.IsOperational)
            {
                return false;
            }

            if (!_catalog.TryGetUnit(unitId, out var def))
            {
                return false;
            }

            if (!IsUnitUnlocked(nation, def))
            {
                return false;
            }

            return _resources.CanAfford(nation, def.Cost)
                   && _civ.HasAvailablePopulation(nation, def.PopulationCost);
        }

        /// <summary>
        /// True when the Nation could place Structure type <paramref name="structureId"/> right now:
        /// the type exists and is placeable (unlocked, or — for the Peace_Arch — its prerequisite techs
        /// are complete, Req 4.6/10.1); and the Nation can afford the Resource (Req 2.3) and population
        /// (Req 5.4) costs. Per-cell terrain validity is checked at issue time (Req 4.2), not here.
        /// Mirrors <see cref="BaseSystem.Handle(EpochWar.Core.Commands.PlaceStructureCommand, MatchState)"/>'s
        /// location-independent acceptance conditions. Pure query.
        /// </summary>
        public bool CanPlaceStructure(Nation nation, string structureId)
        {
            if (nation == null || string.IsNullOrEmpty(structureId))
            {
                return false;
            }

            if (nation.Eliminated)
            {
                return false;
            }

            if (!_catalog.TryGetStructure(structureId, out var def))
            {
                return false;
            }

            if (!_baseSystem.IsStructurePlaceable(nation, def.Id))
            {
                return false;
            }

            return _resources.CanAfford(nation, def.Cost)
                   && _civ.HasAvailablePopulation(nation, def.PopulationCost);
        }

        /// <summary>
        /// True when the Nation could begin researching <paramref name="technologyId"/> right now: the
        /// Technology is available (exists, not already completed, prerequisites complete and its Era
        /// reached — Req 1.3), is not already in progress, and its Research cost is affordable
        /// (Req 1.2/1.6). Mirrors
        /// <see cref="TechSystem.Handle(EpochWar.Core.Commands.StartResearchCommand, MatchState)"/>'s
        /// acceptance conditions. Pure query.
        /// </summary>
        public bool CanResearch(Nation nation, string technologyId)
        {
            if (nation == null || string.IsNullOrEmpty(technologyId))
            {
                return false;
            }

            if (nation.Eliminated)
            {
                return false;
            }

            if (!_catalog.TryGetTechnology(technologyId, out var tech))
            {
                return false;
            }

            // Already researched or currently in progress: the action is not available.
            if (nation.CompletedTechIds.Contains(tech.Id) || nation.ResearchProgress.ContainsKey(tech.Id))
            {
                return false;
            }

            if (!_tech.IsTechAvailable(nation, tech.Id))
            {
                return false;
            }

            return _resources.CanAfford(nation, tech.ResearchCost);
        }

        /// <summary>
        /// True when the Nation could form a Battalion from <paramref name="unitIds"/> right now: at
        /// least two distinct, living Units owned by the Nation are referenced (Req 3.3). Mirrors
        /// <see cref="UnitSystem.Handle(EpochWar.Core.Commands.FormBattalionCommand, MatchState)"/>'s
        /// acceptance condition. Pure query.
        /// </summary>
        public bool CanFormBattalion(MatchState state, Nation nation, IReadOnlyList<int> unitIds)
        {
            if (state == null || nation == null || unitIds == null || unitIds.Count < 2)
            {
                return false;
            }

            if (nation.Eliminated)
            {
                return false;
            }

            var seen = new HashSet<int>();
            int valid = 0;
            foreach (var id in unitIds)
            {
                if (!seen.Add(id))
                {
                    continue;
                }

                if (state.Units.TryGetValue(id, out var unit)
                    && unit.OwnerNationId == nation.Id
                    && unit.Health > 0)
                {
                    valid++;
                    if (valid >= 2)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// True when the Nation could activate the ability <paramref name="abilityId"/> on
        /// <paramref name="unit"/> right now: the Unit exists and is owned by the (non-eliminated)
        /// Nation, its type defines that ability (Req 13.1), the ability's cooldown has fully elapsed
        /// (Req 13.4), and the Nation can afford the ability's Resource cost (Req 13.2). Mirrors the
        /// <see cref="UnitSystem"/>'s <see cref="EpochWar.Core.Commands.ActivateAbilityCommand"/>
        /// acceptance conditions so the UI control's enabled state matches the authoritative handler
        /// exactly (Req 13.3, Property 30). Pure query — never mutates state.
        /// </summary>
        public bool CanActivateAbility(Nation nation, UnitInstance unit, string abilityId)
        {
            if (nation == null || unit == null || string.IsNullOrEmpty(abilityId))
            {
                return false;
            }

            if (nation.Eliminated)
            {
                return false;
            }

            if (unit.OwnerNationId != nation.Id || unit.Def == null)
            {
                return false;
            }

            UnitAbilityDef ability = FindAbility(unit.Def, abilityId);
            if (ability == null)
            {
                return false;
            }

            // Both preconditions must hold: cooldown fully elapsed AND resources sufficient (Req 13.3).
            return IsAbilityOffCooldown(unit, abilityId)
                   && _resources.CanAfford(nation, ability.Cost);
        }

        /// <summary>
        /// True when <paramref name="unit"/>'s ability <paramref name="abilityId"/> is off cooldown —
        /// i.e. its <see cref="UnitInstance.AbilityRemainingCooldown"/> has no entry, or a non-positive
        /// one, for that ability (Req 13.4). A static, side-effect-free helper so the cooldown half of
        /// the predicate can be example-unit-tested in isolation.
        /// </summary>
        public static bool IsAbilityOffCooldown(UnitInstance unit, string abilityId)
        {
            if (unit == null || string.IsNullOrEmpty(abilityId))
            {
                return false;
            }

            if (unit.AbilityRemainingCooldown != null
                && unit.AbilityRemainingCooldown.TryGetValue(abilityId, out Fixed remaining))
            {
                return remaining <= Fixed.Zero;
            }

            // No recorded cooldown entry means the ability has never been used (or has fully reset): ready.
            return true;
        }

        /// <summary>Finds the <see cref="UnitAbilityDef"/> with id <paramref name="abilityId"/> on <paramref name="def"/>, or null.</summary>
        private static UnitAbilityDef FindAbility(UnitDef def, string abilityId)
        {
            List<UnitAbilityDef> abilities = def?.AbilityDefs;
            if (abilities == null)
            {
                return null;
            }

            for (int i = 0; i < abilities.Count; i++)
            {
                UnitAbilityDef ability = abilities[i];
                if (ability != null && string.Equals(ability.Id, abilityId, StringComparison.Ordinal))
                {
                    return ability;
                }
            }

            return null;
        }

        /// <summary>
        /// Mirror of the <see cref="UnitSystem"/>'s private unlock rule: a Unit type is unlocked when
        /// its Era is at or below the Nation's current Era, or a completed Technology unlocks it
        /// explicitly (Req 1.5, 3.1).
        /// </summary>
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
    }
}
