using System;
using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative owner of every Nation's technology progression (Requirement 1, plus the
    /// Era/availability gating the special victory paths require: Req 9.1, 10.1, 11.1).
    ///
    /// Responsibilities:
    /// <list type="bullet">
    /// <item>Validates and applies research selection via the <see cref="StartResearchCommand"/>
    /// handler: the Technology must exist, be available (all prerequisites completed and its Era
    /// reached, Req 1.3) and not already completed or in progress, and the Nation must afford its
    /// Research cost. On success the cost is deducted exactly once and progress begins accumulating
    /// (Req 1.2, Property 1); an unaffordable or unavailable request is rejected with no state
    /// change (Req 1.6, Property 5).</item>
    /// <item>Accumulates in-progress research each <see cref="Tick"/> and, once a research reaches
    /// its required duration, records the Technology in the Nation's completed set (Req 1.7).</item>
    /// <item>Computes Era-advancement availability — enabled exactly when the completed set contains
    /// every Technology the next Era requires (Req 1.4, Property 3) — and, via the
    /// <see cref="AdvanceEraCommand"/> handler / <see cref="AdvanceEra"/>, advances the Nation and
    /// unlocks every Unit, Structure, and Resource type designated for the new Era (Req 1.5,
    /// Property 4).</item>
    /// <item>Gates the special techs/wonders: a Doomsday_Weapon is researchable only once its Era is
    /// reached (Req 9.1, Property 32); the Peace_Arch is available only once its prerequisite techs
    /// are complete (Req 10.1, Property 36); the Colony_Ship is available only in the Space Era
    /// (Req 11.1, Property 40).</item>
    /// </list>
    ///
    /// The system holds no per-Match mutable state of its own — all tech state lives on the
    /// <see cref="Nation"/> (<see cref="Nation.CurrentEra"/>, <see cref="Nation.CompletedTechIds"/>,
    /// <see cref="Nation.ResearchProgress"/>), persisted for the Match per Req 1.7 — so a single
    /// instance serves the whole Match. It resolves authored content through the injected
    /// <see cref="ICatalog"/> and pays Research costs through the injected
    /// <see cref="ResourceSystem"/>, reusing its atomic affordability/deduction contract. Following
    /// the pipeline contract, nothing here throws to signal a rejected command.
    /// </summary>
    public sealed class TechSystem :
        ICommandHandler<StartResearchCommand>,
        ICommandHandler<AdvanceEraCommand>
    {
        private readonly ICatalog _catalog;
        private readonly ResourceSystem _resources;

        public TechSystem(ICatalog catalog, ResourceSystem resourceSystem)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _resources = resourceSystem ?? throw new ArgumentNullException(nameof(resourceSystem));
        }

        /// <summary>
        /// Registers this system's command handlers with the single authoritative
        /// <paramref name="router"/> so research and Era-advancement intents from human Players and
        /// AI_Nations alike flow through the identical pipeline (Req 8.2, 8.5).
        /// </summary>
        public void RegisterHandlers(CommandRouter router)
        {
            if (router == null)
            {
                throw new ArgumentNullException(nameof(router));
            }

            router.Register<StartResearchCommand>(this);
            router.Register<AdvanceEraCommand>(this);
        }

        // ------------------------------------------------------------------
        // Command handling
        // ------------------------------------------------------------------

        /// <summary>
        /// Validates and applies a research selection (Req 1.2, 1.3, 1.6). Rejection leaves all tech
        /// and resource state untouched (Property 5); acceptance deducts the Research cost exactly
        /// once and starts non-decreasing progress (Property 1), emitting the resource-change events
        /// plus a <see cref="ResearchStartedEvent"/>.
        /// </summary>
        public CommandResult Handle(StartResearchCommand command, MatchState state)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            // The router has already confirmed the issuing nation exists and is not eliminated.
            var nation = state.Nations[command.IssuingNationId];

            if (!_catalog.TryGetTechnology(command.TechnologyId, out var tech))
            {
                return CommandResult.Reject($"Unknown technology \"{command.TechnologyId}\".");
            }

            if (nation.CompletedTechIds.Contains(tech.Id))
            {
                return CommandResult.Reject($"Technology \"{tech.Id}\" is already researched.");
            }

            if (nation.ResearchProgress.ContainsKey(tech.Id))
            {
                return CommandResult.Reject($"Technology \"{tech.Id}\" is already being researched.");
            }

            // Availability: prerequisites complete and the gating Era reached (Req 1.3, 9.1, 11.1).
            if (!IsTechAvailable(nation, tech))
            {
                return CommandResult.Reject(
                    $"Technology \"{tech.Id}\" is not available: prerequisites or Era requirement unmet.");
            }

            // Affordability + atomic deduction (Req 1.2 / 1.6). On failure nothing is mutated.
            if (!_resources.TryDeduct(nation, tech.ResearchCost, out var costEvents))
            {
                return CommandResult.Reject(
                    $"Insufficient resources to research \"{tech.Id}\".");
            }

            // Begin accumulating progress (Property 1).
            nation.ResearchProgress[tech.Id] = 0f;

            var events = new List<GameEvent>(costEvents)
            {
                new ResearchStartedEvent(nation.Id, tech.Id, ResearchDurationSeconds(tech)),
            };

            return CommandResult.Accept(events.ToArray());
        }

        /// <summary>
        /// Validates and applies an Era advancement (Req 1.4, 1.5). The action is enabled only when
        /// the completed Technology set satisfies the next Era's requirements (Property 3); on
        /// acceptance the Nation advances one stage and the newly designated content is unlocked
        /// (Property 4), emitting an <see cref="EraAdvancedEvent"/>.
        /// </summary>
        public CommandResult Handle(AdvanceEraCommand command, MatchState state)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            var nation = state.Nations[command.IssuingNationId];

            if (!CanAdvanceEra(nation))
            {
                if (nation.CurrentEra >= Era.Space)
                {
                    return CommandResult.Reject(
                        $"Nation {nation.Id} is already at the final Era ({Era.Space}).");
                }

                return CommandResult.Reject(
                    $"Nation {nation.Id} has not completed every Technology required for {NextEra(nation.CurrentEra)}.");
            }

            var events = AdvanceEra(nation);
            return CommandResult.Accept(events.ToArray());
        }

        // ------------------------------------------------------------------
        // Simulation tick: accumulate progress and complete research
        // ------------------------------------------------------------------

        /// <summary>
        /// Advances every Nation's in-progress research by <paramref name="deltaSeconds"/> and
        /// completes any research whose accumulated progress reaches its required duration,
        /// recording the Technology in the Nation's completed set (Req 1.2, 1.7) and emitting a
        /// <see cref="TechResearchCompletedEvent"/>. Progress is monotonically non-decreasing
        /// (Property 1). A non-positive delta is a no-op.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state, float deltaSeconds)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (deltaSeconds <= 0f)
            {
                return Array.Empty<GameEvent>();
            }

            var events = new List<GameEvent>();

            foreach (var nation in state.Nations.Values)
            {
                if (nation.ResearchProgress.Count == 0)
                {
                    continue;
                }

                // Snapshot ids so completed entries can be removed without mutating during enumeration.
                var inProgress = nation.ResearchProgress.Keys.ToList();
                foreach (var techId in inProgress)
                {
                    float progress = nation.ResearchProgress[techId] + deltaSeconds;

                    float duration = _catalog.TryGetTechnology(techId, out var tech)
                        ? ResearchDurationSeconds(tech)
                        : 0f;

                    if (progress >= duration)
                    {
                        nation.ResearchProgress.Remove(techId);
                        nation.CompletedTechIds.Add(techId);
                        events.Add(new TechResearchCompletedEvent(nation.Id, techId));
                    }
                    else
                    {
                        nation.ResearchProgress[techId] = progress;
                    }
                }
            }

            return events;
        }

        // ------------------------------------------------------------------
        // Availability queries
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true when <paramref name="technologyId"/> is currently available for selection by
        /// <paramref name="nation"/>: the Technology exists, has not been completed, all of its
        /// prerequisite Technologies are in the completed set (Req 1.3, Property 2), and its
        /// designated Era has been reached (Req 9.1/11.1). Pure query — never mutates state.
        /// </summary>
        public bool IsTechAvailable(Nation nation, string technologyId)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return _catalog.TryGetTechnology(technologyId, out var tech) && IsTechAvailable(nation, tech);
        }

        private bool IsTechAvailable(Nation nation, TechnologyDef tech)
        {
            if (nation.CompletedTechIds.Contains(tech.Id))
            {
                return false;
            }

            // Era gate: a Technology — including Era-gated Doomsday (Req 9.1) and Colony Ship
            // (Req 11.1) techs — is only selectable once the Nation has reached its Era.
            if (nation.CurrentEra < tech.Era)
            {
                return false;
            }

            return ArePrerequisitesMet(nation, tech);
        }

        /// <summary>
        /// Returns true when every prerequisite Technology of <paramref name="technologyId"/> is in
        /// <paramref name="nation"/>'s completed set, independent of any Era gate (Req 1.3,
        /// Property 2). An unknown Technology returns false.
        /// </summary>
        public bool ArePrerequisitesMet(Nation nation, string technologyId)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return _catalog.TryGetTechnology(technologyId, out var tech) && ArePrerequisitesMet(nation, tech);
        }

        private static bool ArePrerequisitesMet(Nation nation, TechnologyDef tech)
        {
            foreach (var prereqId in tech.Prerequisites)
            {
                if (!nation.CompletedTechIds.Contains(prereqId))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Era advancement
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the Era immediately following <paramref name="era"/>, or <paramref name="era"/>
        /// itself when it is already the final (<see cref="Era.Space"/>) Era.
        /// </summary>
        public static Era NextEra(Era era) => era >= Era.Space ? Era.Space : era + 1;

        /// <summary>
        /// Returns true when <paramref name="nation"/> may advance to the next Era — i.e. it is not
        /// already at the final Era and its completed Technology set contains every Technology the
        /// next Era requires (Req 1.4, Property 3). When the next Era has no authored
        /// <see cref="EraDef"/> (or no requirements), advancement is permitted. Pure query.
        /// </summary>
        public bool CanAdvanceEra(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (nation.CurrentEra >= Era.Space)
            {
                return false;
            }

            var next = NextEra(nation.CurrentEra);
            var eraDef = _catalog.GetEra(next);
            if (eraDef == null)
            {
                return true;
            }

            foreach (var requiredTechId in eraDef.RequiredTechIds)
            {
                if (!nation.CompletedTechIds.Contains(requiredTechId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Advances <paramref name="nation"/> to the next Era and unlocks every Unit, Structure, and
        /// Resource type designated for it (Req 1.5, Property 4), creating per-Nation stores for any
        /// newly unlocked Resource types at their default capacity. Callers must first confirm
        /// <see cref="CanAdvanceEra"/>; if the Nation is already at the final Era this is a no-op and
        /// returns no events. Returns the <see cref="EraAdvancedEvent"/> plus any resource-store
        /// change events produced while provisioning newly unlocked resources.
        /// </summary>
        public IReadOnlyList<GameEvent> AdvanceEra(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            if (nation.CurrentEra >= Era.Space)
            {
                return Array.Empty<GameEvent>();
            }

            var from = nation.CurrentEra;
            var to = NextEra(from);
            nation.CurrentEra = to;

            var unlockedUnitIds = _catalog.UnitsAt(to).Select(u => u.Id).ToList();
            var unlockedStructureIds = _catalog.StructuresAt(to).Select(s => s.Id).ToList();
            var unlockedResources = _catalog.ResourcesAt(to).Select(r => r.Type).ToList();

            var events = new List<GameEvent>();

            // Provision a store for each newly unlocked resource that the Nation does not yet hold,
            // honouring its authored default capacity (Req 1.5 / 2.5).
            foreach (var resourceDef in _catalog.ResourcesAt(to))
            {
                if (!nation.Resources.ContainsKey(resourceDef.Type))
                {
                    events.AddRange(_resources.SetCapacity(nation, resourceDef.Type, resourceDef.DefaultCapacity));
                }
            }

            events.Add(new EraAdvancedEvent(
                nation.Id, from, to, unlockedUnitIds, unlockedStructureIds, unlockedResources));

            return events;
        }

        // ------------------------------------------------------------------
        // Unlocked-content queries (cumulative Era unlocks + completed-tech unlocks)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns the ids of every Unit type currently unlocked for <paramref name="nation"/>: all
        /// types whose Era is at or below the Nation's current Era, plus any unlocked by a completed
        /// Technology (Req 1.5, 4.6).
        /// </summary>
        public IReadOnlyCollection<string> GetUnlockedUnitIds(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            var ids = new HashSet<string>(_catalog.UnitsUpTo(nation.CurrentEra).Select(u => u.Id));
            AddCompletedTechUnlocks(nation, ids, tech => tech.UnlocksUnits);
            return ids;
        }

        /// <summary>
        /// Returns the ids of every Structure type currently unlocked for <paramref name="nation"/>:
        /// all types whose Era is at or below the Nation's current Era, plus any unlocked by a
        /// completed Technology. This is exactly the placeable set referenced by Req 4.6 /
        /// Property 22.
        /// </summary>
        public IReadOnlyCollection<string> GetUnlockedStructureIds(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            var ids = new HashSet<string>(_catalog.StructuresUpTo(nation.CurrentEra).Select(s => s.Id));
            AddCompletedTechUnlocks(nation, ids, tech => tech.UnlocksStructures);
            return ids;
        }

        /// <summary>
        /// Returns every Resource type currently unlocked for <paramref name="nation"/>: all types
        /// whose Era is at or below the Nation's current Era, plus any unlocked by a completed
        /// Technology (Req 1.5).
        /// </summary>
        public IReadOnlyCollection<ResourceType> GetUnlockedResources(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            var types = new HashSet<ResourceType>(_catalog.ResourcesUpTo(nation.CurrentEra).Select(r => r.Type));
            foreach (var techId in nation.CompletedTechIds)
            {
                if (_catalog.TryGetTechnology(techId, out var tech))
                {
                    foreach (var resource in tech.UnlocksResources)
                    {
                        types.Add(resource);
                    }
                }
            }

            return types;
        }

        /// <summary>True when the given Unit type is unlocked for <paramref name="nation"/>.</summary>
        public bool IsUnitUnlocked(Nation nation, string unitId)
            => GetUnlockedUnitIds(nation).Contains(unitId);

        /// <summary>True when the given Structure type is unlocked for <paramref name="nation"/>.</summary>
        public bool IsStructureUnlocked(Nation nation, string structureId)
            => GetUnlockedStructureIds(nation).Contains(structureId);

        private void AddCompletedTechUnlocks(
            Nation nation, HashSet<string> ids, Func<TechnologyDef, IReadOnlyList<string>> selector)
        {
            foreach (var techId in nation.CompletedTechIds)
            {
                if (_catalog.TryGetTechnology(techId, out var tech))
                {
                    foreach (var id in selector(tech))
                    {
                        ids.Add(id);
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Special victory-path gating (Req 9.1, 10.1, 11.1)
        // ------------------------------------------------------------------

        /// <summary>
        /// Returns true when the Doomsday_Weapon Technology <paramref name="technologyId"/> is
        /// available for research by <paramref name="nation"/>: it must be a
        /// <see cref="TechCategory.DoomsdayWeapon"/> tech and the Nation must have reached its
        /// designated Era, with its prerequisites complete and not already researched (Req 9.1,
        /// Property 32).
        /// </summary>
        public bool IsDoomsdayAvailable(Nation nation, string technologyId)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return _catalog.TryGetTechnology(technologyId, out var tech)
                   && tech.Category == TechCategory.DoomsdayWeapon
                   && IsTechAvailable(nation, tech);
        }

        /// <summary>
        /// Returns true when the Colony_Ship is available to <paramref name="nation"/> — i.e. the
        /// Nation has reached the <see cref="Era.Space"/> Era (Req 11.1, Property 40).
        /// </summary>
        public bool IsColonyShipAvailable(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            return nation.CurrentEra >= Era.Space;
        }

        /// <summary>
        /// Returns true when the Peace_Arch is available for placement by <paramref name="nation"/> —
        /// i.e. the Nation has completed every <see cref="TechCategory.PeaceArchPrereq"/> Technology
        /// the catalog defines (Req 10.1, Property 36). When no such prerequisite techs are defined
        /// the wonder is unrestricted.
        /// </summary>
        public bool IsPeaceArchAvailable(Nation nation)
        {
            if (nation == null)
            {
                throw new ArgumentNullException(nameof(nation));
            }

            foreach (var tech in _catalog.Technologies)
            {
                if (tech.Category == TechCategory.PeaceArchPrereq
                    && !nation.CompletedTechIds.Contains(tech.Id))
                {
                    return false;
                }
            }

            return true;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// The simulation seconds of accumulated progress a Technology requires before completing.
        /// Derived from the Research component of its cost (a more expensive Technology takes
        /// proportionally longer); a Technology with no Research cost completes on the next tick.
        /// </summary>
        private static float ResearchDurationSeconds(TechnologyDef tech)
        {
            float amount = tech.ResearchCost.AmountOf(ResourceType.Research);
            return amount > 0f ? amount : 0f;
        }
    }
}
