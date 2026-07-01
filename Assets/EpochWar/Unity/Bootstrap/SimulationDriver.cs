using System;
using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.Simulation;
using EpochWar.Core.State;

namespace EpochWar.Unity.Bootstrap
{
    /// <summary>
    /// The MonoBehaviour that drives the engine-free simulation from Unity's frame loop on the Host
    /// (design "Simulation Loop", task 15.1, Req 8.3).
    ///
    /// The simulation advances on a fixed deterministic step (default 20 Hz) while Unity's
    /// <see cref="Time.deltaTime"/> is variable, so this driver accumulates real time and steps the
    /// core a whole number of fixed ticks per frame — never a partial tick — so combat, construction,
    /// production, and population growth stay reproducible. Only the authoritative Host ticks the
    /// simulation (<see cref="HasAuthority"/>); non-host clients leave the state advancement to the
    /// Host and merely render the replicated state, so this component is inert on them.
    ///
    /// The driver deliberately knows nothing about how the Match was assembled: it is <em>bound</em>
    /// to a tick delegate and a state accessor (via <see cref="Bind(MatchBootstrapper,bool)"/> or
    /// <see cref="Bind(MatchSimulation,bool)"/>) by the match bootstrap wiring. Each fixed step it
    /// raises <see cref="Ticked"/> with the ordered <see cref="GameEvent"/>s the step produced so the
    /// presentation and (later) networking layers can mirror/replicate the change; it holds no
    /// gameplay rules itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SimulationDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Fixed simulation ticks per second. The core advances in whole steps of 1/this.")]
        [Min(1f)]
        private float _ticksPerSecond = 20f;

        [SerializeField]
        [Tooltip("Maximum fixed ticks to run in a single frame, so a long stall cannot spiral into a freeze.")]
        [Min(1)]
        private int _maxTicksPerFrame = 5;

        [SerializeField]
        [Tooltip("Whether this instance is the authoritative Host. Only the Host advances the simulation.")]
        private bool _hasAuthority = true;

        [SerializeField]
        [Tooltip("When false the driver is bound but paused (e.g. during a lobby or after the match ends).")]
        private bool _running = true;

        private Func<float, IReadOnlyList<GameEvent>> _tickFn;
        private Func<MatchState> _stateAccessor;
        private float _accumulator;

        /// <summary>
        /// Raised once per fixed simulation tick with the ordered events that tick produced. Views,
        /// the terrain renderer, and (later) the networking layer subscribe to mirror the change.
        /// </summary>
        public event Action<IReadOnlyList<GameEvent>> Ticked;

        /// <summary>The fixed simulation step in seconds (<c>1 / TicksPerSecond</c>).</summary>
        public float FixedDeltaTime => 1f / Mathf.Max(1f, _ticksPerSecond);

        /// <summary>Whether this instance is the authoritative Host that advances the simulation.</summary>
        public bool HasAuthority
        {
            get => _hasAuthority;
            set => _hasAuthority = value;
        }

        /// <summary>Whether the driver is currently advancing the simulation (bound, running, authoritative).</summary>
        public bool IsTicking => _running && _hasAuthority && _tickFn != null;

        /// <summary>The authoritative Match state, or <c>null</c> until the driver is bound.</summary>
        public MatchState State => _stateAccessor?.Invoke();

        /// <summary>True once a tick source has been bound via one of the <c>Bind</c> overloads.</summary>
        public bool IsBound => _tickFn != null;

        /// <summary>
        /// Binds the driver to a <see cref="MatchBootstrapper"/> so each fixed step also drives the
        /// registered AI controllers through the same authoritative command path (Req 8.5). This is
        /// the normal binding used by the match scene wiring.
        /// </summary>
        public void Bind(MatchBootstrapper bootstrapper, bool hasAuthority = true)
        {
            if (bootstrapper == null)
            {
                throw new ArgumentNullException(nameof(bootstrapper));
            }

            _tickFn = bootstrapper.Tick;
            _stateAccessor = () => bootstrapper.State;
            _hasAuthority = hasAuthority;
            _accumulator = 0f;
        }

        /// <summary>
        /// Binds the driver directly to a <see cref="MatchSimulation"/> (no AI controllers). Useful for
        /// scenes or tests that drive a bare simulation. Human/AI commands must be enqueued on the
        /// simulation directly.
        /// </summary>
        public void Bind(MatchSimulation simulation, bool hasAuthority = true)
        {
            if (simulation == null)
            {
                throw new ArgumentNullException(nameof(simulation));
            }

            _tickFn = simulation.Tick;
            _stateAccessor = () => simulation.State;
            _hasAuthority = hasAuthority;
            _accumulator = 0f;
        }

        /// <summary>Detaches any bound tick source; the driver becomes inert until re-bound.</summary>
        public void Unbind()
        {
            _tickFn = null;
            _stateAccessor = null;
            _accumulator = 0f;
        }

        /// <summary>Starts (or resumes) advancing the simulation.</summary>
        public void Play() => _running = true;

        /// <summary>Pauses advancement without unbinding; rendering of the current state continues.</summary>
        public void Pause() => _running = false;

        private void Update()
        {
            if (!IsTicking)
            {
                return;
            }

            float step = FixedDeltaTime;
            _accumulator += Time.deltaTime;

            int ticksThisFrame = 0;
            while (_accumulator >= step && ticksThisFrame < _maxTicksPerFrame)
            {
                _accumulator -= step;
                ticksThisFrame++;
                StepOnce(step);
            }

            // Drop any backlog beyond the catch-up budget so a hitch cannot snowball into a spiral of
            // death; the simulation simply proceeds from the current real time.
            if (_accumulator > step)
            {
                _accumulator = 0f;
            }
        }

        /// <summary>
        /// Advances the bound simulation by exactly one fixed step of <paramref name="fixedDt"/>
        /// seconds and raises <see cref="Ticked"/> with the produced events. Exposed for deterministic
        /// stepping from tests or a manual host loop. Does nothing when no tick source is bound.
        /// </summary>
        public IReadOnlyList<GameEvent> StepOnce(float fixedDt)
        {
            if (_tickFn == null)
            {
                return Array.Empty<GameEvent>();
            }

            IReadOnlyList<GameEvent> events = _tickFn(fixedDt) ?? Array.Empty<GameEvent>();

            var handler = Ticked;
            handler?.Invoke(events);

            return events;
        }
    }
}
