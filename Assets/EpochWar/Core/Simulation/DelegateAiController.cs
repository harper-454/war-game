using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Simulation
{
    /// <summary>
    /// A minimal <see cref="IAiController"/> that delegates command production to a supplied function
    /// (task 13.1's optional AI stub, Req 8.5).
    ///
    /// It carries no AI logic of its own — it simply adapts a <c>(state, tick) =&gt; commands</c>
    /// callback (or a fixed "do nothing" behaviour) to the controller contract so tests and early
    /// wiring can route AI commands through the same authoritative path as human commands without a
    /// bespoke controller class. Real AI behaviour is layered on later; this keeps the unified command
    /// path exercisable today.
    /// </summary>
    public sealed class DelegateAiController : IAiController
    {
        private readonly Func<MatchState, long, IEnumerable<ICommand>> _produce;

        /// <summary>
        /// Creates a controller for <paramref name="nationId"/> that produces commands by invoking
        /// <paramref name="produce"/> each tick. When <paramref name="produce"/> is null the controller
        /// issues no commands (a passive AI_Nation).
        /// </summary>
        public DelegateAiController(int nationId, Func<MatchState, long, IEnumerable<ICommand>> produce = null)
        {
            NationId = nationId;
            _produce = produce;
        }

        /// <inheritdoc />
        public int NationId { get; }

        /// <inheritdoc />
        public IEnumerable<ICommand> ProduceCommands(MatchState state, long tickCount)
            => _produce?.Invoke(state, tickCount) ?? Array.Empty<ICommand>();
    }
}
