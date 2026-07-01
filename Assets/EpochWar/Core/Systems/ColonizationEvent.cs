using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The phase of a Nation's Colony_Ship colonization sequence a <see cref="ColonizationEvent"/>
    /// reports (Req 11.2, 11.3).
    /// </summary>
    public enum ColonizationPhase
    {
        /// <summary>The colonization sequence has just begun after a Colony_Ship launch (Req 11.2).</summary>
        Started = 0,

        /// <summary>The colonization sequence has advanced but not yet completed.</summary>
        Progress = 1,

        /// <summary>The colonization sequence has completed; the Victory_System resolves Ascension (Req 11.3).</summary>
        Completed = 2,
    }

    /// <summary>
    /// A <see cref="GameEvent"/> announcing progress of a Nation's Colony_Ship colonization sequence
    /// (Req 11.2, 11.3).
    ///
    /// The <see cref="UnitSystem"/> emits a <see cref="ColonizationPhase.Started"/> event when a
    /// <see cref="Commands.LaunchColonyShipCommand"/> is accepted and the sequence begins, and a
    /// <see cref="ColonizationPhase.Completed"/> event from <see cref="UnitSystem.Tick"/> when the
    /// sequence finishes. It carries the owning Nation and the accumulated/required duration so the
    /// UI can present a colonization progress indicator (Req 7) and the Victory_System (task 12) can
    /// resolve the Ascension victory on completion.
    /// </summary>
    public sealed class ColonizationEvent : GameEvent
    {
        /// <summary>The id of the Nation whose colonization sequence changed.</summary>
        public int NationId { get; }

        /// <summary>The phase of the colonization sequence being reported.</summary>
        public ColonizationPhase Phase { get; }

        /// <summary>Simulation seconds accumulated toward completion.</summary>
        public float ProgressSeconds { get; }

        /// <summary>Total simulation seconds the sequence requires.</summary>
        public float DurationSeconds { get; }

        public ColonizationEvent(int nationId, ColonizationPhase phase, float progressSeconds, float durationSeconds)
        {
            NationId = nationId;
            Phase = phase;
            ProgressSeconds = progressSeconds;
            DurationSeconds = durationSeconds;
        }

        public override string ToString()
            => $"Colonization(nation {NationId}, {Phase}, {ProgressSeconds:0.##}/{DurationSeconds:0.##}s)";
    }
}
