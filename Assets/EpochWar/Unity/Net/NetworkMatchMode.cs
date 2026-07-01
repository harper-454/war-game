namespace EpochWar.Unity.Net
{
    /// <summary>
    /// The two supported multiplayer Match configurations (Req 8.1).
    ///
    /// The mode tells the <see cref="MatchNetworkManager"/> how many human-controlled Nations to
    /// reserve for connecting clients versus how many AI_Nations the Host seeds and drives locally.
    /// It does <em>not</em> change the authoritative pipeline: in every mode the Host owns the
    /// simulation, human intents arrive as <c>ServerRpc</c>s, and AI commands are produced on the
    /// Host through the same <see cref="EpochWar.Core.Commands.CommandRouter"/> path (Req 8.2, 8.5).
    /// </summary>
    public enum NetworkMatchMode
    {
        /// <summary>
        /// Two human-controlled Nations playing against each other (Req 8.1). Both Nations are
        /// human-assignable; the Host is one of them and a single remote client is the other.
        /// </summary>
        CompetitiveTwoHuman = 0,

        /// <summary>
        /// One or more human-controlled Nations cooperating against one or more AI_Nations (Req 8.1).
        /// Human Nations are assigned to connecting clients (Host first) and the remaining AI_Nations
        /// are seeded and driven only on the Host.
        /// </summary>
        CooperativeVsAi = 1,
    }
}
