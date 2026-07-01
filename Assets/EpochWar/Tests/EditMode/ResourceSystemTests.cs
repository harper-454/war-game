using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for <see cref="ResourceSystem"/> (Requirement 2).
    ///
    /// These cover concrete, named scenarios for resource independence (2.1), production capping
    /// and overflow discard (2.2/2.5), atomic affordable deduction (2.3), rejection of unaffordable
    /// costs with no mutation (2.4), and the resource-changed events the UI consumes (2.6). They
    /// complement the universal FsCheck properties added by task 4.2.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class ResourceSystemTests
    {
        private static ResourceChangedEvent[] Changes(IReadOnlyList<GameEvent> events)
            => events.OfType<ResourceChangedEvent>().ToArray();

        [Test]
        public void Produce_IntoUncappedStore_AddsFullAmountAndEmitsEvent()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(1);

            var events = sys.Produce(nation, ResourceType.Wood, 50f);

            Assert.That(sys.GetAmount(nation, ResourceType.Wood), Is.EqualTo(50f));
            var changes = Changes(events);
            Assert.That(changes.Length, Is.EqualTo(1));
            Assert.That(changes[0].NationId, Is.EqualTo(1));
            Assert.That(changes[0].ResourceType, Is.EqualTo(ResourceType.Wood));
            Assert.That(changes[0].OldAmount, Is.EqualTo(0f));
            Assert.That(changes[0].NewAmount, Is.EqualTo(50f));
            Assert.That(changes[0].Delta, Is.EqualTo(50f));
        }

        [Test]
        public void Produce_BeyondCapacity_CapsAndDiscardsOverflow()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(1);
            sys.SetCapacity(nation, ResourceType.Food, 100f);

            sys.Produce(nation, ResourceType.Food, 80f);
            var events = sys.Produce(nation, ResourceType.Food, 50f); // 130 -> capped 100

            Assert.That(sys.GetAmount(nation, ResourceType.Food), Is.EqualTo(100f));
            var change = Changes(events).Single();
            Assert.That(change.NewAmount, Is.EqualTo(100f));
            Assert.That(change.Delta, Is.EqualTo(20f));
        }

        [Test]
        public void Produce_IntoFullStore_DoesNothingAndEmitsNoEvent()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(1);
            sys.SetCapacity(nation, ResourceType.Food, 100f);
            sys.Produce(nation, ResourceType.Food, 100f);

            var events = sys.Produce(nation, ResourceType.Food, 25f);

            Assert.That(sys.GetAmount(nation, ResourceType.Food), Is.EqualTo(100f));
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void Produce_OnOneType_LeavesOtherTypesUnchanged()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(1);

            sys.Produce(nation, ResourceType.Wood, 30f);

            Assert.That(sys.GetAmount(nation, ResourceType.Wood), Is.EqualTo(30f));
            Assert.That(sys.GetAmount(nation, ResourceType.Stone), Is.EqualTo(0f));
            Assert.That(sys.GetAmount(nation, ResourceType.Metal), Is.EqualTo(0f));
        }

        [Test]
        public void TryDeduct_AffordableCost_DeductsExactlyAndEmitsPerTypeEvents()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(2);
            sys.Produce(nation, ResourceType.Wood, 100f);
            sys.Produce(nation, ResourceType.Stone, 40f);
            var cost = ResourceCost.Of((ResourceType.Wood, 30f), (ResourceType.Stone, 40f));

            Assert.That(sys.CanAfford(nation, cost), Is.True);
            var accepted = sys.TryDeduct(nation, cost, out var events);

            Assert.That(accepted, Is.True);
            Assert.That(sys.GetAmount(nation, ResourceType.Wood), Is.EqualTo(70f));
            Assert.That(sys.GetAmount(nation, ResourceType.Stone), Is.EqualTo(0f));
            Assert.That(Changes(events).Length, Is.EqualTo(2));
        }

        [Test]
        public void TryDeduct_UnaffordableCost_RejectsWithNoMutationOrEvents()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(3);
            sys.Produce(nation, ResourceType.Wood, 100f);
            sys.Produce(nation, ResourceType.Metal, 5f);
            var cost = ResourceCost.Of((ResourceType.Wood, 50f), (ResourceType.Metal, 20f));

            Assert.That(sys.CanAfford(nation, cost), Is.False);
            var accepted = sys.TryDeduct(nation, cost, out var events);

            Assert.That(accepted, Is.False);
            Assert.That(sys.GetAmount(nation, ResourceType.Wood), Is.EqualTo(100f), "affordable component must not be touched");
            Assert.That(sys.GetAmount(nation, ResourceType.Metal), Is.EqualTo(5f));
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void TryDeduct_FreeCost_SucceedsWithNoEvents()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(1);

            var accepted = sys.TryDeduct(nation, ResourceCost.Free, out var events);

            Assert.That(accepted, Is.True);
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void SetCapacity_BelowCurrentAmount_TrimsAndEmitsEvent()
        {
            var sys = new ResourceSystem();
            var nation = new Nation(4);
            sys.Produce(nation, ResourceType.Energy, 90f);

            var events = sys.SetCapacity(nation, ResourceType.Energy, 60f);

            Assert.That(sys.GetAmount(nation, ResourceType.Energy), Is.EqualTo(60f));
            Assert.That(Changes(events).Single().NewAmount, Is.EqualTo(60f));
        }
    }
}
