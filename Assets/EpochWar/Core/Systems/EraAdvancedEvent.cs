using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Nation advanced from one Era to the next, and
    /// listing the content newly unlocked by the advance (Req 1.4, 1.5).
    ///
    /// The <see cref="TechSystem"/> emits this when an <see cref="AdvanceEraCommand"/> is accepted.
    /// The unlocked id/type lists carry exactly the Unit, Structure, and Resource definitions whose
    /// designated Era equals the new <see cref="ToEra"/> (the increment unlocked at this step); the
    /// cumulative unlocked set is always available via the <see cref="TechSystem"/> queries. The UI
    /// uses this to refresh availability of recruit/place controls (Req 7.5).
    /// </summary>
    public sealed class EraAdvancedEvent : GameEvent
    {
        /// <summary>The id of the Nation that advanced.</summary>
        public int NationId { get; }

        /// <summary>The Era the Nation advanced from.</summary>
        public Era FromEra { get; }

        /// <summary>The Era the Nation advanced to.</summary>
        public Era ToEra { get; }

        /// <summary>Ids of Unit types whose designated Era is <see cref="ToEra"/>.</summary>
        public IReadOnlyList<string> UnlockedUnitIds { get; }

        /// <summary>Ids of Structure types whose designated Era is <see cref="ToEra"/>.</summary>
        public IReadOnlyList<string> UnlockedStructureIds { get; }

        /// <summary>Resource types whose designated Era is <see cref="ToEra"/>.</summary>
        public IReadOnlyList<ResourceType> UnlockedResources { get; }

        public EraAdvancedEvent(
            int nationId,
            Era fromEra,
            Era toEra,
            IReadOnlyList<string> unlockedUnitIds,
            IReadOnlyList<string> unlockedStructureIds,
            IReadOnlyList<ResourceType> unlockedResources)
        {
            NationId = nationId;
            FromEra = fromEra;
            ToEra = toEra;
            UnlockedUnitIds = unlockedUnitIds ?? new List<string>();
            UnlockedStructureIds = unlockedStructureIds ?? new List<string>();
            UnlockedResources = unlockedResources ?? new List<ResourceType>();
        }

        public override string ToString()
            => $"EraAdvanced(nation {NationId}, {FromEra} -> {ToEra}, "
               + $"+{UnlockedUnitIds.Count}U/{UnlockedStructureIds.Count}S/{UnlockedResources.Count}R)";
    }
}
