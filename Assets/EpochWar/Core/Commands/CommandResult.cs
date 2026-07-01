using System;
using System.Collections.Generic;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// The outcome of validating and applying an <see cref="ICommand"/> — either accepted with the
    /// <see cref="GameEvent"/>s it produced, or rejected with a human-readable reason (Req 8.2).
    ///
    /// Command rejection is a first-class, returned result and is <strong>never</strong> signalled
    /// by throwing an exception. Affordability (Req 1.6, 2.4), population (Req 5.4), placement
    /// (Req 4.2), and availability (Req 1.3) failures all return <see cref="Reject"/> and leave
    /// state untouched, surfacing <see cref="RejectReason"/> to the issuing client's UI. Successful
    /// commands return <see cref="Accept"/> with zero or more events for replication/UI.
    /// </summary>
    public readonly struct CommandResult
    {
        private static readonly IReadOnlyList<GameEvent> EmptyEvents = new GameEvent[0];

        /// <summary>True when the command was validated and applied; false when it was rejected.</summary>
        public bool Accepted { get; }

        /// <summary>
        /// The reason the command was rejected, or <c>null</c> when <see cref="Accepted"/> is true.
        /// </summary>
        public string RejectReason { get; }

        /// <summary>
        /// The events produced by an accepted command (empty when rejected, never <c>null</c>).
        /// </summary>
        public IReadOnlyList<GameEvent> Events { get; }

        private CommandResult(bool accepted, string rejectReason, IReadOnlyList<GameEvent> events)
        {
            Accepted = accepted;
            RejectReason = rejectReason;
            Events = events ?? EmptyEvents;
        }

        /// <summary>
        /// Creates a rejected result with the given <paramref name="reason"/>; the caller must not
        /// have mutated any state.
        /// </summary>
        public static CommandResult Reject(string reason)
        {
            if (string.IsNullOrEmpty(reason))
            {
                throw new ArgumentException("A rejection must provide a reason.", nameof(reason));
            }

            return new CommandResult(false, reason, EmptyEvents);
        }

        /// <summary>
        /// Creates an accepted result carrying the <paramref name="events"/> produced while applying
        /// the command. Passing no events yields an accepted result with an empty event list.
        /// </summary>
        public static CommandResult Accept(params GameEvent[] events)
        {
            if (events == null || events.Length == 0)
            {
                return new CommandResult(true, null, EmptyEvents);
            }

            return new CommandResult(true, null, events);
        }

        public override string ToString()
            => Accepted
                ? $"Accept({Events.Count} event{(Events.Count == 1 ? string.Empty : "s")})"
                : $"Reject(\"{RejectReason}\")";
    }
}
