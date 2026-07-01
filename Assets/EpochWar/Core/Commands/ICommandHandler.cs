using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// Validates and applies a single concrete command type <typeparamref name="T"/> on behalf of
    /// the system that owns it (Tech, Resource, Unit, Base, Civ, Terrain, Victory).
    ///
    /// Each system registers its handlers with the <see cref="CommandRouter"/>, which delegates a
    /// dispatched command to the matching handler after its ownership checks pass. A handler
    /// performs all command-specific validation (affordability, availability, population,
    /// placement legality, ...) and, on success, mutates <see cref="MatchState"/> and returns
    /// <see cref="CommandResult.Accept"/> with the produced events. On any failure it returns
    /// <see cref="CommandResult.Reject"/> and leaves state unchanged — it never throws to indicate
    /// rejection (Req 8.2).
    /// </summary>
    /// <typeparam name="T">The concrete command type this handler is responsible for.</typeparam>
    public interface ICommandHandler<in T> where T : ICommand
    {
        /// <summary>
        /// Validates <paramref name="command"/> against <paramref name="state"/> and, if valid,
        /// applies it. Returns an accepted result (with any events) or a rejected result with a
        /// reason; in the rejected case <paramref name="state"/> must be left untouched.
        /// </summary>
        CommandResult Handle(T command, MatchState state);
    }
}
