using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that the Match has ended because a victory condition
    /// resolved — the single signal the networking and presentation layers consume to present the
    /// outcome and end-of-match summary to every connected Player (Req 9.4, 10.3, 11.3, 12.3, 12.4).
    ///
    /// The <see cref="VictorySystem"/> emits exactly one of these, from the tick on which it first
    /// observes a satisfied victory condition, immediately after it sets
    /// <see cref="MatchState.Status"/> to <see cref="MatchStatus.Ended"/> and populates
    /// <see cref="MatchState.Outcome"/>. It mirrors the resolved <see cref="MatchOutcome"/> — the
    /// winning Nation, the satisfied <see cref="VictoryPath"/>, and the completion
    /// <see cref="CompletionTick"/> used for the earliest-completion tie-break (Req 11.4) — so a
    /// consumer that only observes the event stream (rather than polling state) still has the full
    /// summary payload.
    /// </summary>
    public sealed class MatchEndedEvent : GameEvent
    {
        /// <summary>The id of the <see cref="Nation"/> that won the Match (Req 12.4).</summary>
        public int WinningNationId { get; }

        /// <summary>The victory path that was satisfied (Req 12.4).</summary>
        public VictoryPath Path { get; }

        /// <summary>The <see cref="MatchState.TickCount"/> at which the winning condition completed (Req 11.4).</summary>
        public long CompletionTick { get; }

        public MatchEndedEvent(int winningNationId, VictoryPath path, long completionTick)
        {
            WinningNationId = winningNationId;
            Path = path;
            CompletionTick = completionTick;
        }

        public override string ToString()
            => $"MatchEnded(Nation {WinningNationId} wins by {Path} @ tick {CompletionTick})";
    }
}
