namespace EpochWar.Core.State
{
    /// <summary>
    /// The lifecycle status of a <see cref="MatchState"/> (Req 12.2, 12.3).
    ///
    /// A Match remains <see cref="InProgress"/> while the Victory_System finds no satisfied
    /// victory condition, and transitions to <see cref="Ended"/> exactly once a condition is
    /// satisfied — at which point <see cref="MatchState.Outcome"/> is populated.
    /// </summary>
    public enum MatchStatus
    {
        /// <summary>The Match is being played and no victory condition has been satisfied yet.</summary>
        InProgress = 0,

        /// <summary>A victory condition has resolved; the Match is over and an outcome exists.</summary>
        Ended = 1
    }
}
