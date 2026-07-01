using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for the <see cref="CommandRouter"/> (Req 8.2).
    ///
    /// The router is the single authoritative entry point for every command. These tests verify the
    /// two halves of Req 8.2: an <em>accepted</em> command is applied by its handler (state mutates)
    /// and the produced events are queued on the router for replication/UI; a <em>rejected</em>
    /// command — whether rejected by the router's own ownership/turn checks or by a handler — leaves
    /// the state untouched and queues no events. Rejection is always a returned
    /// <see cref="CommandResult"/>, never an exception.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class CommandRouterTests
    {
        // A minimal event so accepted commands have something to queue.
        private sealed class CounterChangedEvent : GameEvent
        {
            public int NationId { get; }
            public int NewValue { get; }

            public CounterChangedEvent(int nationId, int newValue)
            {
                NationId = nationId;
                NewValue = newValue;
            }
        }

        // A test command: bump the issuing nation's population by a delta. A non-positive delta is
        // treated as invalid so a handler can demonstrate rejection-without-mutation.
        private sealed class BumpPopulationCommand : ICommand
        {
            public int IssuingNationId { get; }
            public int Delta { get; }

            public BumpPopulationCommand(int issuingNationId, int delta)
            {
                IssuingNationId = issuingNationId;
                Delta = delta;
            }
        }

        // A handler that mutates state on accept and touches nothing on reject.
        private sealed class BumpPopulationHandler : ICommandHandler<BumpPopulationCommand>
        {
            public CommandResult Handle(BumpPopulationCommand command, MatchState state)
            {
                if (command.Delta <= 0)
                {
                    // Rejection is a returned result; state MUST be left untouched.
                    return CommandResult.Reject("Delta must be positive.");
                }

                var nation = state.Nations[command.IssuingNationId];
                nation.Population += command.Delta;
                return CommandResult.Accept(new CounterChangedEvent(nation.Id, nation.Population));
            }
        }

        private static (CommandRouter router, MatchState state) Build()
        {
            var router = new CommandRouter();
            router.Register(new BumpPopulationHandler());
            var state = new MatchState();
            state.Nations[1] = new Nation(1);
            return (router, state);
        }

        [Test]
        public void Dispatch_AcceptedCommand_MutatesStateAndQueuesEvents()
        {
            var (router, state) = Build();

            var result = router.Dispatch(new BumpPopulationCommand(1, 5), state);

            Assert.That(result.Accepted, Is.True);
            Assert.That(state.Nations[1].Population, Is.EqualTo(5), "accepted command must mutate state");

            // The produced event is queued on the router for replication/UI.
            Assert.That(router.PendingEvents.Count, Is.EqualTo(1));
            var queued = router.PendingEvents.OfType<CounterChangedEvent>().Single();
            Assert.That(queued.NationId, Is.EqualTo(1));
            Assert.That(queued.NewValue, Is.EqualTo(5));

            // The returned result also carries the same events.
            Assert.That(result.Events.OfType<CounterChangedEvent>().Single().NewValue, Is.EqualTo(5));
        }

        [Test]
        public void Dispatch_MultipleAcceptedCommands_QueuesEventsInOrder()
        {
            var (router, state) = Build();

            router.Dispatch(new BumpPopulationCommand(1, 2), state);
            router.Dispatch(new BumpPopulationCommand(1, 3), state);

            Assert.That(state.Nations[1].Population, Is.EqualTo(5));
            var values = router.PendingEvents.OfType<CounterChangedEvent>().Select(e => e.NewValue).ToArray();
            Assert.That(values, Is.EqualTo(new[] { 2, 5 }), "events queue in dispatch order");
        }

        [Test]
        public void Dispatch_HandlerRejection_LeavesStateUntouchedAndQueuesNoEvents()
        {
            var (router, state) = Build();

            var result = router.Dispatch(new BumpPopulationCommand(1, 0), state);

            Assert.That(result.Accepted, Is.False, "rejection is a returned result, not an exception");
            Assert.That(result.RejectReason, Is.EqualTo("Delta must be positive."));
            Assert.That(state.Nations[1].Population, Is.EqualTo(0), "rejected command must not mutate state");
            Assert.That(router.PendingEvents, Is.Empty, "rejected command queues no events");
        }

        [Test]
        public void Dispatch_UnknownIssuingNation_IsRejectedWithoutStateChange()
        {
            var (router, state) = Build();

            var result = router.Dispatch(new BumpPopulationCommand(99, 5), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(state.Nations[1].Population, Is.EqualTo(0));
            Assert.That(router.PendingEvents, Is.Empty);
        }

        [Test]
        public void Dispatch_EliminatedNation_IsRejectedWithoutStateChange()
        {
            var (router, state) = Build();
            state.Nations[1].Eliminated = true;

            var result = router.Dispatch(new BumpPopulationCommand(1, 5), state);

            Assert.That(result.Accepted, Is.False);
            Assert.That(state.Nations[1].Population, Is.EqualTo(0), "an eliminated nation cannot mutate state");
            Assert.That(router.PendingEvents, Is.Empty);
        }

        [Test]
        public void Dispatch_UnregisteredCommandType_IsRejectedWithoutStateChange()
        {
            // A router with no handler registered at all.
            var router = new CommandRouter();
            var state = new MatchState();
            state.Nations[1] = new Nation(1);

            var result = router.Dispatch(new BumpPopulationCommand(1, 5), state);

            Assert.That(result.Accepted, Is.False, "no registered handler => rejected result, not a throw");
            Assert.That(state.Nations[1].Population, Is.EqualTo(0));
            Assert.That(router.PendingEvents, Is.Empty);
        }

        [Test]
        public void DrainEvents_ReturnsQueuedEventsAndClearsThem()
        {
            var (router, state) = Build();
            router.Dispatch(new BumpPopulationCommand(1, 4), state);

            var drained = router.DrainEvents();

            Assert.That(drained.OfType<CounterChangedEvent>().Single().NewValue, Is.EqualTo(4));
            Assert.That(router.PendingEvents, Is.Empty, "draining clears the pending queue");
        }
    }
}
