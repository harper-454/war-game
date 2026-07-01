namespace EpochWar.Core.State
{
    /// <summary>
    /// The resolved result of a finished Match (Req 12.3, 12.4).
    ///
    /// <see cref="MatchState.Outcome"/> is <c>null</c> while the Match is in progress and is
    /// populated exactly once when a victory condition resolves. It captures the winning
    /// Nation, the <see cref="VictoryPath"/> that was satisfied, and the simulation
    /// <see cref="CompletionTick"/> at which the condition completed. The completion tick is
    /// what lets the Victory_System award the earliest victory when several resolve in the
    /// same step (Req 11.4).
    ///
    /// Immutable: an outcome, once decided, never changes.
    /// </summary>
    public sealed class MatchOutcome
    {
        /// <summary>The id of the <see cref="Nation"/> that won the Match.</summary>
        public int WinningNationId { get; }

        /// <summary>The victory path that was satisfied (Req 12.4).</summary>
        public VictoryPath Path { get; }

        /// <summary>The <see cref="MatchState.TickCount"/> at which the winning condition completed (Req 11.4).</summary>
        public long CompletionTick { get; }

        public MatchOutcome(int winningNationId, VictoryPath path, long completionTick)
        {
            WinningNationId = winningNationId;
            Path = path;
            CompletionTick = completionTick;
        }

        public override string ToString()
            => $"Outcome(Nation {WinningNationId} by {Path} @ tick {CompletionTick})";
    }
}
