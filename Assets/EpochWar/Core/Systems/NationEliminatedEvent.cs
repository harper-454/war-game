using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Doomsday_Weapon's elimination effect fully
    /// resolved against a targeted Nation, removing its forces and marking it eliminated (Req 9.2,
    /// 9.3).
    ///
    /// The <see cref="UnitSystem"/> emits this when a <see cref="Commands.DeployDoomsdayCommand"/> is
    /// accepted and the elimination effect is executed. It carries the eliminated Nation, the Nation
    /// that deployed the weapon, and the weapon Technology id. The Victory_System (task 12) observes
    /// eliminated Nations to resolve the Annihilation victory when a sole survivor remains (Req 9.4).
    /// </summary>
    public sealed class NationEliminatedEvent : GameEvent
    {
        /// <summary>The id of the Nation that was eliminated.</summary>
        public int EliminatedNationId { get; }

        /// <summary>The id of the Nation that deployed the Doomsday_Weapon.</summary>
        public int ByNationId { get; }

        /// <summary>The id of the Doomsday_Weapon Technology that was deployed (Req 9.2).</summary>
        public string TechnologyId { get; }

        public NationEliminatedEvent(int eliminatedNationId, int byNationId, string technologyId)
        {
            EliminatedNationId = eliminatedNationId;
            ByNationId = byNationId;
            TechnologyId = technologyId;
        }

        public override string ToString()
            => $"NationEliminated(nation {EliminatedNationId} by {ByNationId} via \"{TechnologyId}\")";
    }
}
