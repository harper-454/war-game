using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Nation finished researching a Technology — its
    /// id has been added to the Nation's completed set and its in-progress entry cleared (Req 1.2,
    /// 1.7).
    ///
    /// The <see cref="TechSystem"/> emits this from <see cref="TechSystem.Tick"/> once a research's
    /// accumulated progress reaches its required duration. Completion expands the Nation's unlocked
    /// content (Req 4.6) and may newly satisfy an Era-advancement requirement (Req 1.4) or a
    /// Peace_Arch prerequisite (Req 10.1); consumers re-query the relevant predicates in response.
    /// </summary>
    public sealed class TechResearchCompletedEvent : GameEvent
    {
        /// <summary>The id of the Nation that completed the research.</summary>
        public int NationId { get; }

        /// <summary>The id of the now-completed Technology.</summary>
        public string TechnologyId { get; }

        public TechResearchCompletedEvent(int nationId, string technologyId)
        {
            NationId = nationId;
            TechnologyId = technologyId;
        }

        public override string ToString()
            => $"TechResearchCompleted(nation {NationId}, tech \"{TechnologyId}\")";
    }
}
