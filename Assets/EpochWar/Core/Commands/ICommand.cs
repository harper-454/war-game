namespace EpochWar.Core.Commands
{
    /// <summary>
    /// A request to change Match state, issued by either a human Player or an AI_Nation.
    ///
    /// Commands are the only way mutable Match state is changed: human clients send a serialized
    /// <see cref="ICommand"/> to the Host and AI_Nations produce the same instances on the Host,
    /// and both are validated and applied through the identical authoritative
    /// <see cref="CommandRouter"/> pipeline (Req 8.2, 8.5). Concrete command types (recruit,
    /// place structure, initiate research, form battalion, ...) are owned by the system that
    /// validates and applies them via an <see cref="ICommandHandler{T}"/>.
    ///
    /// A command carries no behaviour itself — it is a plain, serializable intent. Whether it is
    /// accepted is decided by its handler and reported as a <see cref="CommandResult"/>; it is
    /// never signalled by throwing.
    /// </summary>
    public interface ICommand
    {
        /// <summary>
        /// The id of the <see cref="EpochWar.Core.State.Nation"/> issuing this command. The
        /// <see cref="CommandRouter"/> uses it to enforce ownership before any system handler runs.
        /// </summary>
        int IssuingNationId { get; }
    }
}
