using System.Linq;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.UI;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based unit tests for <see cref="EndOfMatchSummary"/> (Requirement 12.4).
    ///
    /// When a Match ends, the presentation layer builds an <see cref="EndOfMatchSummary"/> and shows
    /// the winning Nation, the satisfied victory path, and an end-of-match summary to every connected
    /// Player. These tests verify that each factory (<c>FromOutcome</c>, <c>FromState</c>,
    /// <c>FromEvent</c>, <c>Create</c>) populates the winning <see cref="EndOfMatchSummary.WinningNationId"/>,
    /// the satisfied <see cref="EndOfMatchSummary.Path"/>, the <see cref="EndOfMatchSummary.CompletionTick"/>,
    /// the human-readable <see cref="EndOfMatchSummary.Lines"/>, and the local-perspective
    /// (Victory/Defeat) derivation.
    ///
    /// NOTE: <see cref="EndOfMatchSummary"/> lives in the <c>EpochWar.Unity</c> assembly (though it is
    /// intentionally UnityEngine-free). In a real Unity project the EditMode test asmdef
    /// (<c>EpochWar.Tests.EditMode.asmdef</c>) must therefore add <c>"EpochWar.Unity"</c> to its
    /// <c>references</c> array for this file to compile; the existing Core-only tests are unaffected by
    /// that addition.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class EndOfMatchSummaryTests
    {
        private static string ValueOf(EndOfMatchSummary summary, string name)
            => summary.Lines.Single(line => line.Name == name).Value;

        [Test]
        public void FromOutcome_PopulatesWinnerPathAndCompletionTick()
        {
            var outcome = new MatchOutcome(winningNationId: 3, path: VictoryPath.Peace, completionTick: 128);

            var summary = EndOfMatchSummary.FromOutcome(outcome);

            Assert.That(summary.HasOutcome, Is.True);
            Assert.That(summary.WinningNationId, Is.EqualTo(3));
            Assert.That(summary.Path, Is.EqualTo(VictoryPath.Peace));
            Assert.That(summary.CompletionTick, Is.EqualTo(128));
        }

        [Test]
        public void FromOutcome_PopulatesSummaryLinesForWinnerPathAndTick()
        {
            var outcome = new MatchOutcome(winningNationId: 3, path: VictoryPath.Peace, completionTick: 128);

            var summary = EndOfMatchSummary.FromOutcome(outcome);

            Assert.That(summary.Lines, Is.Not.Empty);
            Assert.That(ValueOf(summary, "Winner"), Is.EqualTo("Nation 3"));
            Assert.That(ValueOf(summary, "Victory Path"), Is.EqualTo("Peace"));
            Assert.That(ValueOf(summary, "Completed At Tick"), Is.EqualTo("128"));
            Assert.That(ValueOf(summary, "How"), Is.EqualTo("The Peace Arch was completed"));
        }

        [Test]
        public void FromOutcome_Null_YieldsPendingWithNoOutcome()
        {
            var summary = EndOfMatchSummary.FromOutcome(null);

            Assert.That(summary, Is.SameAs(EndOfMatchSummary.Pending));
            Assert.That(summary.HasOutcome, Is.False);
            Assert.That(summary.Lines, Is.Empty);
        }

        [Test]
        public void FromState_EndedMatch_PopulatesFromOutcome()
        {
            var state = new MatchState
            {
                Status = MatchStatus.Ended,
                Outcome = new MatchOutcome(winningNationId: 1, path: VictoryPath.Ascension, completionTick: 512),
            };

            var summary = EndOfMatchSummary.FromState(state);

            Assert.That(summary.HasOutcome, Is.True);
            Assert.That(summary.WinningNationId, Is.EqualTo(1));
            Assert.That(summary.Path, Is.EqualTo(VictoryPath.Ascension));
            Assert.That(summary.CompletionTick, Is.EqualTo(512));
        }

        [Test]
        public void FromState_InProgressOrNull_YieldsPending()
        {
            var inProgress = new MatchState { Status = MatchStatus.InProgress, Outcome = null };

            Assert.That(EndOfMatchSummary.FromState(inProgress).HasOutcome, Is.False);
            Assert.That(EndOfMatchSummary.FromState(null).HasOutcome, Is.False);
        }

        [Test]
        public void FromEvent_PopulatesWinnerPathAndCompletionTick()
        {
            var ended = new MatchEndedEvent(winningNationId: 2, path: VictoryPath.Annihilation, completionTick: 64);

            var summary = EndOfMatchSummary.FromEvent(ended);

            Assert.That(summary.HasOutcome, Is.True);
            Assert.That(summary.WinningNationId, Is.EqualTo(2));
            Assert.That(summary.Path, Is.EqualTo(VictoryPath.Annihilation));
            Assert.That(summary.CompletionTick, Is.EqualTo(64));
            Assert.That(ValueOf(summary, "How"), Is.EqualTo("All opposing Nations were eliminated"));
        }

        [Test]
        public void FromEvent_Null_YieldsPending()
        {
            Assert.That(EndOfMatchSummary.FromEvent(null).HasOutcome, Is.False);
        }

        [Test]
        public void Create_WithoutLocalNation_HasNoLocalPerspective()
        {
            var summary = EndOfMatchSummary.Create(
                winningNationId: 5, path: VictoryPath.Peace, completionTick: 10);

            Assert.That(summary.HasLocalPerspective, Is.False);
            Assert.That(summary.LocalNationWon, Is.False);
            Assert.That(summary.ResultForLocalPlayer, Is.EqualTo(string.Empty));
            Assert.That(summary.Lines.Any(line => line.Name == "Your Result"), Is.False);
        }

        [Test]
        public void Create_WhenLocalNationIsWinner_DerivesVictory()
        {
            var summary = EndOfMatchSummary.Create(
                winningNationId: 7, path: VictoryPath.Ascension, completionTick: 900, localNationId: 7);

            Assert.That(summary.HasLocalPerspective, Is.True);
            Assert.That(summary.LocalNationWon, Is.True);
            Assert.That(summary.ResultForLocalPlayer, Is.EqualTo("Victory"));
            Assert.That(ValueOf(summary, "Your Result"), Is.EqualTo("Victory"));
        }

        [Test]
        public void Create_WhenLocalNationIsNotWinner_DerivesDefeat()
        {
            var summary = EndOfMatchSummary.Create(
                winningNationId: 7, path: VictoryPath.Ascension, completionTick: 900, localNationId: 4);

            Assert.That(summary.HasLocalPerspective, Is.True);
            Assert.That(summary.LocalNationWon, Is.False);
            Assert.That(summary.ResultForLocalPlayer, Is.EqualTo("Defeat"));
            Assert.That(ValueOf(summary, "Your Result"), Is.EqualTo("Defeat"));
        }

        [Test]
        public void FromOutcome_ForwardsLocalPerspectiveToDerivation()
        {
            var outcome = new MatchOutcome(winningNationId: 9, path: VictoryPath.Peace, completionTick: 3);

            var winner = EndOfMatchSummary.FromOutcome(outcome, localNationId: 9);
            var loser = EndOfMatchSummary.FromOutcome(outcome, localNationId: 1);

            Assert.That(winner.ResultForLocalPlayer, Is.EqualTo("Victory"));
            Assert.That(loser.ResultForLocalPlayer, Is.EqualTo("Defeat"));
        }
    }
}
