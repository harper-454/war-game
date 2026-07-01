using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;
using EpochWar.Unity.Net;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// Presents the end-of-match summary — the winning Nation, the satisfied victory path, and the
    /// completion tick — to every connected Player when the Match ends (Req 12.3, 12.4), using UI
    /// Toolkit.
    ///
    /// <para>The controller detects the Match's end from whichever authoritative source this peer has:
    /// <list type="bullet">
    ///   <item><b>Host.</b> The Host advances the authoritative simulation, so after each
    ///   <see cref="SimulationDriver.Ticked"/> its <see cref="SimulationDriver.State"/> carries the
    ///   populated <see cref="MatchState.Outcome"/> the moment the Victory_System resolves; the
    ///   controller also recognises the <see cref="MatchEndedEvent"/> in that tick's event stream.</item>
    ///   <item><b>Client.</b> A client never ticks the simulation; instead it learns the outcome from
    ///   the replicated lifecycle snapshot the Host publishes, surfaced by
    ///   <see cref="CommandRpcRouter.MatchClockChanged"/> (whose <see cref="NetMatchClock.HasOutcome"/>
    ///   flags the end and carries the winning Nation / path / completion tick).</item>
    /// </list>
    /// Because each peer runs its own <see cref="EndOfMatchController"/> bound to its own local source,
    /// the summary appears on every connected Player's screen (Req 12.3). It shows exactly once per
    /// Match: a guard prevents a re-show if further ticks/snapshots arrive after the end.</para>
    ///
    /// The panel is built entirely in code (no authored UXML needed): a full-screen dimmed overlay
    /// with a centered card showing a headline plus one row per <see cref="EndOfMatchSummary"/> line.
    /// It is pure presentation — it reads the resolved outcome and never mutates state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class EndOfMatchController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the summary overlay. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("Drives the simulation on the Host; the controller detects the end from its ticks/state.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Supplies the replicated lifecycle snapshot on clients (optional for host-only/offline).")]
        private CommandRpcRouter _router;

        [SerializeField]
        [Tooltip("The local Player's Nation id, used to headline Victory/Defeat. -1 means spectator.")]
        private int _localNationId = EndOfMatchSummary.NoLocalNation;

        private VisualElement _overlay;
        private Label _headlineLabel;
        private VisualElement _rows;
        private bool _built;
        private bool _subscribedDriver;
        private bool _subscribedRouter;
        private bool _shown;

        /// <summary>The most recently built summary (never null; <see cref="EndOfMatchSummary.Pending"/> until the Match ends).</summary>
        public EndOfMatchSummary Summary { get; private set; } = EndOfMatchSummary.Pending;

        /// <summary>True once the end-of-match summary has been presented for this Match.</summary>
        public bool IsShown => _shown;

        /// <summary>The Nation id whose win/loss perspective the headline reflects.</summary>
        public int LocalNationId
        {
            get => _localNationId;
            set => _localNationId = value;
        }

        /// <summary>
        /// Wires the controller to its data sources (used by the match scene bootstrap). Binds the
        /// Host's <paramref name="driver"/>, the optional <paramref name="router"/> that carries the
        /// replicated outcome to clients, and the local Nation id used to headline win/loss. Builds the
        /// (hidden) overlay and, if the Match has somehow already ended, presents it immediately.
        /// </summary>
        public void Bind(SimulationDriver driver, CommandRpcRouter router, int localNationId)
        {
            UnsubscribeAll();

            _driver = driver;
            _router = router;
            _localNationId = localNationId;

            EnsureBuilt();
            SubscribeAll();

            // Cover the (rare) case where the Match ended before we bound.
            TryPresentFromLocalState();
            TryPresentFromRouter();
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            EnsureBuilt();
            SubscribeAll();

            // If an outcome arrived while the document wasn't laid out yet, re-attempt now.
            if (!_shown && Summary.HasOutcome)
            {
                Present(Summary);
            }
        }

        private void OnDisable() => UnsubscribeAll();

        private void SubscribeAll()
        {
            if (_driver != null && !_subscribedDriver)
            {
                _driver.Ticked += OnTicked;
                _subscribedDriver = true;
            }

            if (_router != null && !_subscribedRouter)
            {
                _router.MatchClockChanged += OnMatchClockChanged;
                _subscribedRouter = true;
            }
        }

        private void UnsubscribeAll()
        {
            if (_driver != null && _subscribedDriver)
            {
                _driver.Ticked -= OnTicked;
            }

            _subscribedDriver = false;

            if (_router != null && _subscribedRouter)
            {
                _router.MatchClockChanged -= OnMatchClockChanged;
            }

            _subscribedRouter = false;
        }

        // ------------------------------------------------------------------
        // End detection (Req 12.3)
        // ------------------------------------------------------------------

        private void OnTicked(IReadOnlyList<GameEvent> events)
        {
            if (_shown)
            {
                return;
            }

            // Prefer the authoritative event this tick produced, if present; otherwise fall back to
            // the driver's current state outcome (both agree on the Host).
            if (events != null)
            {
                for (int i = 0; i < events.Count; i++)
                {
                    if (events[i] is MatchEndedEvent ended)
                    {
                        Present(EndOfMatchSummary.FromEvent(ended, _localNationId));
                        return;
                    }
                }
            }

            TryPresentFromLocalState();
        }

        private void OnMatchClockChanged(NetMatchClock clock)
        {
            if (_shown || !clock.HasOutcome)
            {
                return;
            }

            Present(EndOfMatchSummary.Create(
                clock.WinningNationId, clock.PathEnum, clock.CompletionTick, _localNationId));
        }

        private void TryPresentFromLocalState()
        {
            if (_shown)
            {
                return;
            }

            var state = _driver != null ? _driver.State : null;
            if (state != null && state.Status == MatchStatus.Ended && state.Outcome != null)
            {
                Present(EndOfMatchSummary.FromState(state, _localNationId));
            }
        }

        private void TryPresentFromRouter()
        {
            if (_shown || _router == null)
            {
                return;
            }

            NetMatchClock clock = _router.Clock;
            if (clock.HasOutcome)
            {
                Present(EndOfMatchSummary.Create(
                    clock.WinningNationId, clock.PathEnum, clock.CompletionTick, _localNationId));
            }
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <summary>
        /// Presents <paramref name="summary"/> in the overlay and, once it has actually been rendered,
        /// marks it shown so it is not re-presented. Public so scene wiring or tests can force a
        /// presentation.
        ///
        /// The <c>_shown</c> guard is latched <em>only</em> after a successful render: if the
        /// <see cref="UIDocument"/> has not laid out its root yet (so the overlay could not be built),
        /// the summary is retained and re-attempted on the next tick / clock change / <c>OnEnable</c>,
        /// so a transient not-ready document can never permanently suppress the end-of-match summary.
        /// </summary>
        public void Present(EndOfMatchSummary summary)
        {
            Summary = summary ?? EndOfMatchSummary.Pending;

            EnsureBuilt();
            Render(Summary);

            // Latch only when there is an outcome that we were actually able to display.
            _shown = _built && Summary.HasOutcome;
        }

        /// <summary>Hides the overlay and resets the shown guard (e.g. when starting a fresh Match).</summary>
        public void Reset()
        {
            _shown = false;
            Summary = EndOfMatchSummary.Pending;
            if (_overlay != null)
            {
                _overlay.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------
        // UI construction (code-built; no UXML required)
        // ------------------------------------------------------------------

        private void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            if (_document == null)
            {
                _document = GetComponent<UIDocument>();
            }

            var uiRoot = _document != null ? _document.rootVisualElement : null;
            if (uiRoot == null)
            {
                return;
            }

            _overlay = new VisualElement { name = "end-of-match-overlay" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.right = 0;
            _overlay.style.top = 0;
            _overlay.style.bottom = 0;
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            _overlay.style.display = DisplayStyle.None;

            var card = new VisualElement { name = "end-of-match-card" };
            card.style.flexDirection = FlexDirection.Column;
            card.style.minWidth = 320;
            card.style.paddingLeft = 24;
            card.style.paddingRight = 24;
            card.style.paddingTop = 16;
            card.style.paddingBottom = 16;
            card.style.backgroundColor = new Color(0.12f, 0.12f, 0.14f, 0.95f);

            _headlineLabel = new Label("Match ended") { name = "end-of-match-headline" };
            _headlineLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _headlineLabel.style.fontSize = 20;
            _headlineLabel.style.marginBottom = 12;
            _headlineLabel.style.whiteSpace = WhiteSpace.Normal;
            card.Add(_headlineLabel);

            _rows = new VisualElement { name = "end-of-match-rows" };
            _rows.style.flexDirection = FlexDirection.Column;
            card.Add(_rows);

            _overlay.Add(card);
            uiRoot.Add(_overlay);
            _built = true;
        }

        private void Render(EndOfMatchSummary summary)
        {
            if (!_built)
            {
                return;
            }

            if (!summary.HasOutcome)
            {
                _overlay.style.display = DisplayStyle.None;
                return;
            }

            _headlineLabel.text = summary.Headline;

            _rows.Clear();
            foreach (var line in summary.Lines)
            {
                var row = new VisualElement { name = $"end-of-match-row-{line.Name}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var nameLabel = new Label(line.Name);
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.marginRight = 16;

                var valueLabel = new Label(line.Value);

                row.Add(nameLabel);
                row.Add(valueLabel);
                _rows.Add(row);
            }

            _overlay.style.display = DisplayStyle.Flex;
        }
    }
}
