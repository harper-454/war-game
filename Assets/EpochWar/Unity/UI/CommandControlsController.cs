using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Unity.Bootstrap;
using EpochWar.Unity.Net;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The persistent command control bar: recruit / place-Structure / initiate-research /
    /// form-Battalion buttons whose enabled state is bound to the corresponding core availability
    /// predicate and whose clicks submit the matching command through the one authoritative pipeline
    /// (Req 7.5, 8.5).
    ///
    /// Each control is enabled <em>if and only if</em> its action is currently available, evaluated
    /// through the shared <see cref="CommandAvailability"/> helper (Req 7.5, Property 30). The bar
    /// re-evaluates every control after each authoritative <see cref="SimulationDriver.Ticked"/>, so a
    /// control disables the moment its action stops being available (a resource dips below the cost, a
    /// producing Structure is destroyed, a tech starts researching) and re-enables when it becomes
    /// available again — all within the fixed tick interval, well under a second.
    ///
    /// The controls act on a small, externally-supplied <em>selection context</em> (which Structure
    /// recruits, which Unit/Structure/Tech is targeted, which Units to group). A selection/input
    /// system sets those fields; on click the bar builds the concrete engine-free
    /// <see cref="ICommand"/> and hands it to <see cref="CommandRpcRouter.SubmitLocalCommand"/>, which
    /// routes it identically whether this client is the Host or a remote client (Req 8.2, 8.5). The
    /// bar never mutates state directly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class CommandControlsController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the control bar. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("Drives the simulation; controls re-evaluate availability after each tick.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Routes issued commands to the authoritative pipeline (Host or via ServerRpc).")]
        private CommandRpcRouter _router;

        [SerializeField]
        [Tooltip("The id of the Nation this local client issues commands for.")]
        private int _localNationId;

        private CommandAvailability _availability;

        // --- Selection context (set by an input/selection system before clicking) ---

        /// <summary>The Structure at which a recruit is issued and where the Unit spawns (Req 3.1).</summary>
        public int RecruitStructureId { get; set; }

        /// <summary>The Unit-definition id to recruit.</summary>
        public string RecruitUnitId { get; set; }

        /// <summary>The Structure-definition id to place (Req 4.1).</summary>
        public string PlaceStructureId { get; set; }

        /// <summary>The footprint-origin cell at which to place the Structure (Req 4.1, 4.2).</summary>
        public CellCoord PlaceOrigin { get; set; }

        /// <summary>The Technology-definition id to research (Req 1.2).</summary>
        public string ResearchTechId { get; set; }

        /// <summary>The display name for a newly formed Battalion (Req 3.3).</summary>
        public string BattalionName { get; set; } = "Battalion";

        /// <summary>The Unit ids to group into a Battalion (Req 3.3).</summary>
        public IReadOnlyList<int> FormBattalionUnitIds { get; set; } = System.Array.Empty<int>();

        // --- Built controls ---
        private Button _recruitButton;
        private Button _placeButton;
        private Button _researchButton;
        private Button _formBattalionButton;
        private bool _built;
        private bool _subscribed;

        /// <summary>
        /// Wires the control bar to its data sources and builds the surface. <paramref name="availability"/>
        /// is the shared predicate helper the buttons bind to; <paramref name="router"/> submits the
        /// issued commands.
        /// </summary>
        public void Bind(
            SimulationDriver driver,
            CommandRpcRouter router,
            CommandAvailability availability,
            int localNationId)
        {
            UnsubscribeFromDriver();

            _driver = driver;
            _router = router;
            _availability = availability;
            _localNationId = localNationId;

            EnsureBuilt();
            SubscribeToDriver();
            RefreshAvailability();
        }

        private void OnEnable()
        {
            EnsureBuilt();
            SubscribeToDriver();
            RefreshAvailability();
        }

        private void OnDisable() => UnsubscribeFromDriver();

        private void SubscribeToDriver()
        {
            if (_driver != null && !_subscribed)
            {
                _driver.Ticked += OnTicked;
                _subscribed = true;
            }
        }

        private void UnsubscribeFromDriver()
        {
            if (_driver != null && _subscribed)
            {
                _driver.Ticked -= OnTicked;
            }

            _subscribed = false;
        }

        private void OnTicked(IReadOnlyList<GameEvent> events) => RefreshAvailability();

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

            var bar = new VisualElement { name = "command-bar" };
            bar.style.position = Position.Absolute;
            bar.style.left = 0;
            bar.style.bottom = 0;
            bar.style.flexDirection = FlexDirection.Row;

            _recruitButton = new Button(OnRecruitClicked) { name = "cmd-recruit", text = "Recruit" };
            _placeButton = new Button(OnPlaceClicked) { name = "cmd-place", text = "Place Structure" };
            _researchButton = new Button(OnResearchClicked) { name = "cmd-research", text = "Research" };
            _formBattalionButton = new Button(OnFormBattalionClicked) { name = "cmd-form-battalion", text = "Form Battalion" };

            bar.Add(_recruitButton);
            bar.Add(_placeButton);
            bar.Add(_researchButton);
            bar.Add(_formBattalionButton);

            uiRoot.Add(bar);
            _built = true;
        }

        // ------------------------------------------------------------------
        // Availability binding (Req 7.5, Property 30)
        // ------------------------------------------------------------------

        /// <summary>
        /// Re-evaluates each control's enabled state so it is enabled exactly when its action is
        /// currently available for the local Nation (Req 7.5). A missing binding disables every
        /// control.
        /// </summary>
        public void RefreshAvailability()
        {
            EnsureBuilt();
            if (!_built)
            {
                return;
            }

            var state = _driver != null ? _driver.State : null;
            Nation nation = null;
            state?.Nations.TryGetValue(_localNationId, out nation);

            if (state == null || nation == null || _availability == null)
            {
                SetEnabled(false, false, false, false);
                return;
            }

            _recruitButton.SetEnabled(_availability.CanRecruit(state, nation, RecruitStructureId, RecruitUnitId));
            _placeButton.SetEnabled(_availability.CanPlaceStructure(nation, PlaceStructureId));
            _researchButton.SetEnabled(_availability.CanResearch(nation, ResearchTechId));
            _formBattalionButton.SetEnabled(_availability.CanFormBattalion(state, nation, FormBattalionUnitIds));
        }

        private void SetEnabled(bool recruit, bool place, bool research, bool formBattalion)
        {
            _recruitButton?.SetEnabled(recruit);
            _placeButton?.SetEnabled(place);
            _researchButton?.SetEnabled(research);
            _formBattalionButton?.SetEnabled(formBattalion);
        }

        // ------------------------------------------------------------------
        // Click handlers — build the command and submit it (Req 8.2, 8.5)
        // ------------------------------------------------------------------

        private void OnRecruitClicked()
        {
            if (string.IsNullOrEmpty(RecruitUnitId))
            {
                return;
            }

            Submit(new RecruitUnitCommand(_localNationId, RecruitStructureId, RecruitUnitId));
        }

        private void OnPlaceClicked()
        {
            if (string.IsNullOrEmpty(PlaceStructureId))
            {
                return;
            }

            Submit(new PlaceStructureCommand(_localNationId, PlaceStructureId, PlaceOrigin));
        }

        private void OnResearchClicked()
        {
            if (string.IsNullOrEmpty(ResearchTechId))
            {
                return;
            }

            Submit(new StartResearchCommand(_localNationId, ResearchTechId));
        }

        private void OnFormBattalionClicked()
        {
            if (FormBattalionUnitIds == null || FormBattalionUnitIds.Count < 2)
            {
                return;
            }

            Submit(new FormBattalionCommand(_localNationId, BattalionName, FormBattalionUnitIds));
        }

        /// <summary>
        /// Issues an <see cref="ActivateAbilityCommand"/> for <paramref name="unitId"/>'s ability
        /// <paramref name="abilityId"/> (with an optional <paramref name="targetPosition"/> for targeted
        /// abilities) through the same authoritative command pipeline every other control uses (Req
        /// 13.2). Called by the information panel's selectable ability controls; the Core handler
        /// enforces ownership, cooldown, and cost, so a rejected activation simply leaves state
        /// unchanged. No-ops on a missing ability id.
        /// </summary>
        public void ActivateAbility(int unitId, string abilityId, WorldPosition? targetPosition = null)
        {
            if (string.IsNullOrEmpty(abilityId))
            {
                return;
            }

            Submit(new ActivateAbilityCommand(_localNationId, unitId, abilityId, targetPosition));
        }

        private void Submit(ICommand command)
        {
            if (_router == null)
            {
                return;
            }

            _router.SubmitLocalCommand(command);

            // Reflect any resulting availability change on the next tick immediately for responsiveness.
            RefreshAvailability();
        }
    }
}
