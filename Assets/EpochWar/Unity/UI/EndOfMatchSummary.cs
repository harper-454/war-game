using System;
using System.Collections.Generic;
using System.Globalization;
using EpochWar.Core.State;
using EpochWar.Core.Systems;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The engine-free, immutable view-model for the end-of-match summary presented to every
    /// connected Player when a victory condition resolves (Req 12.3, 12.4).
    ///
    /// When the Match ends, the presentation layer builds one of these snapshots and renders its
    /// <see cref="Headline"/> and <see cref="Lines"/> — a complete description of how the Match was
    /// won: the winning <see cref="WinningNationId"/>, the satisfied <see cref="VictoryPath"/>, and
    /// the simulation <see cref="CompletionTick"/> at which the condition completed (the value the
    /// Victory_System uses for the earliest-completion tie-break, Req 11.4). It mirrors the pattern of
    /// <see cref="InfoPanelViewModel"/>: a plain data snapshot with no dependency on
    /// <c>UnityEngine</c>, so its content can be verified directly by the unit test (task 17.2) and it
    /// is cheap to build the moment the Match ends.
    ///
    /// <para>The same snapshot is presented identically to every connected Player. When the local
    /// Nation id is supplied (<see cref="LocalNationId"/>), the model additionally derives the local
    /// Player's perspective (<see cref="HasLocalPerspective"/> / <see cref="LocalNationWon"/> /
    /// <see cref="ResultForLocalPlayer"/>) so each client can headline "Victory!" or "Defeat" while
    /// still showing the same authoritative outcome. It never mutates state and carries no gameplay
    /// rules.</para>
    /// </summary>
    public sealed class EndOfMatchSummary
    {
        /// <summary>Sentinel <see cref="LocalNationId"/> meaning "no local perspective" (e.g. a spectator).</summary>
        public const int NoLocalNation = -1;

        /// <summary>True when the Match has ended and a resolved outcome is described (Req 12.3).</summary>
        public bool HasOutcome { get; }

        /// <summary>The id of the <see cref="Nation"/> that won the Match (Req 12.4).</summary>
        public int WinningNationId { get; }

        /// <summary>The satisfied <see cref="VictoryPath"/> (Req 12.4).</summary>
        public VictoryPath Path { get; }

        /// <summary>The simulation tick on which the winning condition completed (Req 11.4).</summary>
        public long CompletionTick { get; }

        /// <summary>The local Player's Nation id, or <see cref="NoLocalNation"/> when unknown/spectating.</summary>
        public int LocalNationId { get; }

        /// <summary>A human-readable headline for the summary banner.</summary>
        public string Headline { get; }

        /// <summary>Every summary detail, in a stable display order (winner, path, completion tick, ...).</summary>
        public IReadOnlyList<InfoAttribute> Lines { get; }

        private EndOfMatchSummary(
            bool hasOutcome,
            int winningNationId,
            VictoryPath path,
            long completionTick,
            int localNationId,
            string headline,
            IReadOnlyList<InfoAttribute> lines)
        {
            HasOutcome = hasOutcome;
            WinningNationId = winningNationId;
            Path = path;
            CompletionTick = completionTick;
            LocalNationId = localNationId;
            Headline = headline ?? string.Empty;
            Lines = lines ?? Array.Empty<InfoAttribute>();
        }

        /// <summary>The summary shown while the Match is still in progress (no outcome yet, Req 12.2).</summary>
        public static readonly EndOfMatchSummary Pending = new EndOfMatchSummary(
            hasOutcome: false,
            winningNationId: 0,
            path: VictoryPath.Annihilation,
            completionTick: 0,
            localNationId: NoLocalNation,
            headline: "Match in progress",
            lines: Array.Empty<InfoAttribute>());

        /// <summary>True when a local Nation perspective is available to headline win/loss.</summary>
        public bool HasLocalPerspective => LocalNationId != NoLocalNation;

        /// <summary>True when the local Nation is the winner (only meaningful when <see cref="HasLocalPerspective"/>).</summary>
        public bool LocalNationWon => HasOutcome && HasLocalPerspective && LocalNationId == WinningNationId;

        /// <summary>The local Player's result label: "Victory" / "Defeat" / "" (no local perspective).</summary>
        public string ResultForLocalPlayer
        {
            get
            {
                if (!HasOutcome || !HasLocalPerspective)
                {
                    return string.Empty;
                }

                return LocalNationWon ? "Victory" : "Defeat";
            }
        }

        // ------------------------------------------------------------------
        // Factories (Req 12.3, 12.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the summary from a resolved <see cref="MatchOutcome"/> (Req 12.3, 12.4). A null
        /// outcome yields <see cref="Pending"/>. Supply <paramref name="localNationId"/> to derive the
        /// local Player's win/loss perspective, or omit it for a neutral (spectator) summary.
        /// </summary>
        public static EndOfMatchSummary FromOutcome(MatchOutcome outcome, int localNationId = NoLocalNation)
        {
            if (outcome == null)
            {
                return Pending;
            }

            return Create(outcome.WinningNationId, outcome.Path, outcome.CompletionTick, localNationId);
        }

        /// <summary>
        /// Builds the summary from a <see cref="MatchState"/> (Req 12.3, 12.4). Returns
        /// <see cref="Pending"/> while the Match is in progress or has no populated outcome; otherwise
        /// builds from <see cref="MatchState.Outcome"/>. Supply <paramref name="localNationId"/> to
        /// derive the local Player's perspective.
        /// </summary>
        public static EndOfMatchSummary FromState(MatchState state, int localNationId = NoLocalNation)
        {
            if (state == null || state.Status != MatchStatus.Ended || state.Outcome == null)
            {
                return Pending;
            }

            return FromOutcome(state.Outcome, localNationId);
        }

        /// <summary>
        /// Builds the summary from a <see cref="MatchEndedEvent"/> (Req 12.3, 12.4) — the event a
        /// consumer that only observes the gameplay-event stream receives. Supply
        /// <paramref name="localNationId"/> for the local Player's perspective.
        /// </summary>
        public static EndOfMatchSummary FromEvent(MatchEndedEvent ended, int localNationId = NoLocalNation)
        {
            if (ended == null)
            {
                return Pending;
            }

            return Create(ended.WinningNationId, ended.Path, ended.CompletionTick, localNationId);
        }

        /// <summary>
        /// Builds a fully-populated summary from the resolved outcome fields (Req 12.3, 12.4). This
        /// primitive-argument factory is the one the networking layer uses on clients, which learn the
        /// outcome from the replicated lifecycle snapshot rather than from a Core object.
        /// </summary>
        public static EndOfMatchSummary Create(
            int winningNationId,
            VictoryPath path,
            long completionTick,
            int localNationId = NoLocalNation)
        {
            string pathLabel = DescribePath(path);
            string headline = BuildHeadline(winningNationId, path, localNationId);

            var lines = new List<InfoAttribute>
            {
                new InfoAttribute("Winner", $"Nation {winningNationId.ToString(CultureInfo.InvariantCulture)}"),
                new InfoAttribute("Victory Path", path.ToString()),
                new InfoAttribute("How", pathLabel),
                new InfoAttribute("Completed At Tick", completionTick.ToString(CultureInfo.InvariantCulture)),
            };

            if (localNationId != NoLocalNation)
            {
                lines.Add(new InfoAttribute(
                    "Your Result",
                    winningNationId == localNationId ? "Victory" : "Defeat"));
            }

            return new EndOfMatchSummary(
                hasOutcome: true,
                winningNationId: winningNationId,
                path: path,
                completionTick: completionTick,
                localNationId: localNationId,
                headline: headline,
                lines: lines);
        }

        /// <summary>A stable, human-readable description of how each victory path is won.</summary>
        public static string DescribePath(VictoryPath path)
        {
            switch (path)
            {
                case VictoryPath.Annihilation:
                    return "All opposing Nations were eliminated";
                case VictoryPath.Peace:
                    return "The Peace Arch was completed";
                case VictoryPath.Ascension:
                    return "Planetary colonization was completed";
                default:
                    return path.ToString();
            }
        }

        private static string BuildHeadline(int winningNationId, VictoryPath path, int localNationId)
        {
            if (localNationId != NoLocalNation)
            {
                return winningNationId == localNationId
                    ? $"Victory! You won by {path}."
                    : $"Defeat. Nation {winningNationId.ToString(CultureInfo.InvariantCulture)} won by {path}.";
            }

            return $"Nation {winningNationId.ToString(CultureInfo.InvariantCulture)} wins by {path}.";
        }

        public override string ToString()
            => HasOutcome
                ? $"EndOfMatchSummary(Nation {WinningNationId} by {Path} @ tick {CompletionTick})"
                : "EndOfMatchSummary(pending)";
    }
}
