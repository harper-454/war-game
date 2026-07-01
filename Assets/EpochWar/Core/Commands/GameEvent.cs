namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A fact about something that happened when a command was successfully applied to the
    /// Match state — for example "resource changed", "unit recruited", or "structure completed".
    ///
    /// Events are produced only by accepted commands (carried on <see cref="CommandResult.Events"/>)
    /// and are queued by the <see cref="CommandRouter"/> for replication to clients and for the UI
    /// to consume (Req 2.6, 7.4, 8.2). They describe state that has <em>already</em> changed; they
    /// never themselves mutate state. Concrete event types derive from this base class and add the
    /// specific payload the consuming system or view needs.
    /// </summary>
    public abstract class GameEvent
    {
    }
}
