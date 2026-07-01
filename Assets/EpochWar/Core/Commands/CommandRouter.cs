using System;
using System.Collections.Generic;
using EpochWar.Core.State;

namespace EpochWar.Core.Commands
{
    /// <summary>
    /// The single authoritative entry point through which every command — from a human Player or an
    /// AI_Nation — is validated and applied (Req 8.2, 8.5).
    ///
    /// Systems register a per-command-type <see cref="ICommandHandler{T}"/> via
    /// <see cref="Register{T}"/>. <see cref="Dispatch"/> then performs router-level
    /// ownership/turn checks (the issuing Nation must exist and not be eliminated) before
    /// delegating to the handler that owns the command's concrete type. Because both AI and human
    /// commands traverse this identical path, an equivalent command from either source produces the
    /// same resulting state (Property 31).
    ///
    /// Rejection — whether from the router's own checks or from a handler — is always returned as a
    /// <see cref="CommandResult"/>, never thrown. On acceptance the handler has already mutated
    /// <see cref="MatchState"/>; the router additionally queues the produced <see cref="GameEvent"/>s
    /// in <see cref="PendingEvents"/> so the networking and UI layers can replicate/consume them.
    /// </summary>
    public sealed class CommandRouter
    {
        private readonly Dictionary<Type, Func<ICommand, MatchState, CommandResult>> _handlers
            = new Dictionary<Type, Func<ICommand, MatchState, CommandResult>>();

        private readonly Queue<GameEvent> _pendingEvents = new Queue<GameEvent>();

        /// <summary>
        /// Events produced by accepted commands and awaiting replication/consumption, in the order
        /// they were dispatched. Use <see cref="DrainEvents"/> to take and clear them.
        /// </summary>
        public IReadOnlyCollection<GameEvent> PendingEvents => _pendingEvents;

        /// <summary>
        /// Registers the <paramref name="handler"/> that owns the concrete command type
        /// <typeparamref name="T"/>. Registering a second handler for the same type replaces the
        /// first, keeping exactly one owning system per command type.
        /// </summary>
        public void Register<T>(ICommandHandler<T> handler) where T : ICommand
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            _handlers[typeof(T)] = (command, state) => handler.Handle((T)command, state);
        }

        /// <summary>Returns true if a handler is registered for <paramref name="commandType"/>.</summary>
        public bool HasHandler(Type commandType)
            => commandType != null && _handlers.ContainsKey(commandType);

        /// <summary>
        /// Validates and applies <paramref name="command"/> against <paramref name="state"/>.
        ///
        /// The router first enforces ownership: the issuing Nation must exist and must not be
        /// eliminated. It then routes to the registered handler for the command's runtime type. Any
        /// failure (unknown command type, unknown/eliminated nation, or a handler rejection) is
        /// returned as a rejected <see cref="CommandResult"/> and leaves state unchanged. On
        /// acceptance, the produced events are enqueued in <see cref="PendingEvents"/>.
        /// </summary>
        public CommandResult Dispatch(ICommand command, MatchState state)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            // Ownership/turn checks (Req 8.2): only a present, active Nation may issue commands.
            if (!state.Nations.TryGetValue(command.IssuingNationId, out var nation))
            {
                return CommandResult.Reject(
                    $"Issuing nation {command.IssuingNationId} does not exist in the match.");
            }

            if (nation.Eliminated)
            {
                return CommandResult.Reject(
                    $"Issuing nation {command.IssuingNationId} has been eliminated and cannot issue commands.");
            }

            // Per-system handler delegation: route to the owner of this concrete command type.
            if (!_handlers.TryGetValue(command.GetType(), out var handle))
            {
                return CommandResult.Reject(
                    $"No handler is registered for command type {command.GetType().Name}.");
            }

            var result = handle(command, state);

            if (result.Accepted)
            {
                foreach (var ev in result.Events)
                {
                    _pendingEvents.Enqueue(ev);
                }
            }

            return result;
        }

        /// <summary>
        /// Removes and returns all queued events in dispatch order, clearing
        /// <see cref="PendingEvents"/>. The simulation loop calls this each step to hand events to
        /// the replication/UI layers.
        /// </summary>
        public IReadOnlyList<GameEvent> DrainEvents()
        {
            if (_pendingEvents.Count == 0)
            {
                return Array.Empty<GameEvent>();
            }

            var drained = new GameEvent[_pendingEvents.Count];
            for (var i = 0; i < drained.Length; i++)
            {
                drained[i] = _pendingEvents.Dequeue();
            }

            return drained;
        }
    }
}
