using System.Collections.Generic;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.State
{
    /// <summary>
    /// A player- or AI-controlled faction with its own economy, technology state, population,
    /// governance, and battalions (Req 1, 2, 5, 8).
    ///
    /// The Nation is the unit of ownership for everything in a Match: resources
    /// (<see cref="Resources"/>, Req 2.1), tech progression (<see cref="CurrentEra"/> /
    /// <see cref="CompletedTechIds"/>, persisted for the Match per Req 1.7), population
    /// (<see cref="Population"/> / <see cref="PopulationCapacity"/>, Req 5.1), active
    /// governance modifiers (Req 5.5), and named <see cref="Battalions"/> (Req 3.3).
    /// <see cref="IsAI"/> distinguishes AI_Nations, but AI and human commands flow through the
    /// identical authoritative pipeline (Req 8.5), so the rest of the simulation treats both
    /// uniformly. <see cref="Eliminated"/> is set by the Victory_System on Annihilation (Req 9.3).
    ///
    /// This is a plain data container; mutation rules live in the per-system handlers.
    /// </summary>
    public sealed class Nation
    {
        /// <summary>Stable per-Match identifier (also used as a command's issuing nation id).</summary>
        public int Id { get; }

        /// <summary>True when this Nation is controlled by AI rather than a human Player.</summary>
        public bool IsAI { get; set; }

        /// <summary>True once this Nation has been eliminated via the Annihilation path (Req 9.3).</summary>
        public bool Eliminated { get; set; }

        /// <summary>The Nation's current Era; starts at <see cref="Era.Prehistoric"/> (Req 1.7, 12.1).</summary>
        public Era CurrentEra { get; set; }

        /// <summary>The ids of every completed Technology, persisted for the Match (Req 1.7).</summary>
        public HashSet<string> CompletedTechIds { get; }

        /// <summary>Accumulated research progress (seconds/points) keyed by Technology id (Req 1.2).</summary>
        public Dictionary<string, float> ResearchProgress { get; }

        /// <summary>The independent stored quantity for each owned Resource type (Req 2.1).</summary>
        public Dictionary<ResourceType, ResourceStore> Resources { get; }

        /// <summary>Current population count (Req 5.1).</summary>
        public int Population { get; set; }

        /// <summary>Maximum supportable population (Req 5.1, 5.3).</summary>
        public int PopulationCapacity { get; set; }

        /// <summary>Currently active governance/civic options whose modifiers apply (Req 5.5).</summary>
        public List<GovernanceOption> ActiveGovernance { get; }

        /// <summary>The Nation's named Battalions keyed by Battalion id (Req 3.3).</summary>
        public Dictionary<int, Battalion> Battalions { get; }

        public Nation(int id, bool isAI = false, Era currentEra = Era.Prehistoric)
        {
            Id = id;
            IsAI = isAI;
            Eliminated = false;
            CurrentEra = currentEra;
            CompletedTechIds = new HashSet<string>();
            ResearchProgress = new Dictionary<string, float>();
            Resources = new Dictionary<ResourceType, ResourceStore>();
            Population = 0;
            PopulationCapacity = 0;
            ActiveGovernance = new List<GovernanceOption>();
            Battalions = new Dictionary<int, Battalion>();
        }

        public override string ToString()
            => $"Nation({Id}, {(IsAI ? "AI" : "Human")}, {CurrentEra}{(Eliminated ? ", eliminated" : string.Empty)})";
    }
}
