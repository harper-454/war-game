using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.State;

namespace EpochWar.Core.Simulation
{
    /// <summary>
    /// Produces the commands an AI_Nation wishes to issue on a given simulation tick (Req 8.5).
    ///
    /// An AI controller is the AI equivalent of a human client's input layer: instead of a Player
    /// clicking the UI, the controller inspects the authoritative <see cref="MatchState"/> and
    /// returns zero or more <see cref="ICommand"/>s for its Nation. Crucially it does <em>not</em>
    /// mutate state itself — the commands it returns are routed through the identical
    /// <see cref="CommandRouter"/> path a human command takes (via
    /// <see cref="MatchBootstrapper.Tick"/> → <see cref="MatchSimulation.EnqueueCommand"/>), so an
    /// AI command and an equivalent human command produce the same result (Req 8.5, Property 31).
    ///
    /// Controllers run only on the Host, are consulted once per tick before the systems advance, and
    /// must be deterministic given the same state so the simulation stays reproducible.
    /// </summary>
    public interface IAiController
    {
        /// <summary>The id of the AI-controlled <see cref="Nation"/> this controller issues commands for.</summary>
        int NationId { get; }

        /// <summary>
        /// Returns the commands this controller wishes to issue this tick given the current
        /// authoritative <paramref name="state"/> and simulation <paramref name="tickCount"/>. May
        /// return an empty sequence (do nothing this tick) but never <c>null</c>. Every returned
        /// command's <see cref="ICommand.IssuingNationId"/> should equal <see cref="NationId"/>; the
        /// router still enforces ownership regardless.
        /// </summary>
        IEnumerable<ICommand> ProduceCommands(MatchState state, long tickCount);
    }
}
