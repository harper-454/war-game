using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// Renders the selection information panel for the currently selected Unit, Battalion, or
    /// Structure and refreshes it on change (Req 7.2, 7.4) using UI Toolkit.
    ///
    /// The controller keeps only a lightweight <em>selection reference</em> (kind + id); it does not
    /// cache entity data. After every authoritative <see cref="SimulationDriver.Ticked"/> event it
    /// re-resolves the selected entity from the current Match state and rebuilds an
    /// <see cref="InfoPanelViewModel"/> — the pure, engine-free snapshot that already contains every
    /// detailed attribute of the entity (Req 7.2, Property 29) — then repaints the panel. Because the
    /// driver ticks at a fixed rate, any change to a displayed attribute is reflected far inside the
    /// 1-second bound (Req 7.4), and a selected entity that leaves the Match (a killed Unit, a
    /// destroyed Structure, a disbanded Battalion) automatically clears the panel.
    ///
    /// UI is built entirely in code (no authored UXML needed): a title label plus a rows container
    /// that is repopulated from the view-model's attribute list each refresh.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class InfoPanelController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the info panel. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("Drives the simulation; the panel refreshes after each of its ticks.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Issues ability-activation commands through the authoritative pipeline (Req 13.2).")]
        private CommandControlsController _commandControls;

        [SerializeField]
        [Tooltip("Seconds between ability cooldown-display refreshes. Must be < 1s so the remaining "
            + "cooldown updates at least once per second (Req 13.4).")]
        [Range(0.05f, 0.9f)]
        private float _cooldownRefreshIntervalSeconds = 0.25f;

        private VisualElement _root;
        private Label _titleLabel;
        private VisualElement _rows;
        private VisualElement _abilities;
        private bool _built;
        private bool _subscribed;

        // The current selection, resolved against the state each refresh (never caches entity data).
        private InfoEntityKind _selectedKind = InfoEntityKind.None;
        private int _selectedId;
        private int _selectedNationId;

        // Ability-activation wiring (Req 13.1-13.4). The availability predicate binds each control's
        // enabled state; the command controller issues the ActivateAbilityCommand.
        private CommandAvailability _availability;
        private int _localNationId = -1;

        // Fog-of-war display resolver (Req 14.5, 14.6, 14.9): when a selected entity belongs to another
        // Nation, its shown position is resolved through the VisionSystem so the panel reflects the
        // Last_Known_Position (or ceases to display) rather than the live position.
        private VisionSystem _vision;

        // Persistent per-ability controls, reconciled by ability id so buttons/handlers are reused
        // across refreshes and only their cooldown/enabled state is updated.
        private readonly Dictionary<string, AbilityControl> _abilityControls =
            new Dictionary<string, AbilityControl>(StringComparer.Ordinal);
        private readonly List<string> _abilityRemovalScratch = new List<string>();

        // Wall-clock accumulator that guarantees the cooldown display refreshes at least once per second
        // even on peers whose driver does not tick (Req 13.4).
        private float _sinceCooldownRefresh;

        /// <summary>The most recently built view-model (never null; <see cref="InfoPanelViewModel.Empty"/> when nothing is selected).</summary>
        public InfoPanelViewModel ViewModel { get; private set; } = InfoPanelViewModel.Empty;

        /// <summary>Wires the panel to the driver whose tick drives refreshes and builds the surface.</summary>
        public void Bind(SimulationDriver driver)
        {
            UnsubscribeFromDriver();
            _driver = driver;
            EnsureBuilt();
            SubscribeToDriver();
            Refresh();
        }

        /// <summary>
        /// Wires the panel to the driver plus the ability-activation collaborators (Req 13.1-13.4): the
        /// <paramref name="commandControls"/> that issues an <see cref="EpochWar.Core.Commands.ActivateAbilityCommand"/>
        /// on the same authoritative path as every other command, the shared <paramref name="availability"/>
        /// predicate that binds each ability control's enabled state to "cooldown elapsed AND resources
        /// sufficient", and the <paramref name="localNationId"/> whose Units may have their abilities
        /// activated. Any argument may be null/-1 (the panel then simply renders read-only ability
        /// controls).
        /// </summary>
        public void Bind(
            SimulationDriver driver,
            CommandControlsController commandControls,
            CommandAvailability availability,
            int localNationId)
        {
            Bind(driver, commandControls, availability, localNationId, null);
        }

        /// <summary>
        /// Full binding overload that additionally supplies the <paramref name="visionSystem"/> so the
        /// panel resolves a selected enemy entity's shown position through the fog of war (Req 14.5,
        /// 14.6, 14.9). A null vision system disables that override (positions are always the live
        /// values, matching the pre-fog behaviour).
        /// </summary>
        public void Bind(
            SimulationDriver driver,
            CommandControlsController commandControls,
            CommandAvailability availability,
            int localNationId,
            VisionSystem visionSystem)
        {
            _commandControls = commandControls;
            _availability = availability;
            _localNationId = localNationId;
            _vision = visionSystem;
            Bind(driver);
        }

        // ------------------------------------------------------------------
        // Selection API (called by a selection/input system)
        // ------------------------------------------------------------------

        /// <summary>Selects a Unit by id so the panel shows its full attribute set (Req 7.2).</summary>
        public void SelectUnit(int unitId)
        {
            _selectedKind = InfoEntityKind.Unit;
            _selectedId = unitId;
            Refresh();
        }

        /// <summary>Selects a Structure by id so the panel shows its full attribute set (Req 7.2).</summary>
        public void SelectStructure(int structureId)
        {
            _selectedKind = InfoEntityKind.Structure;
            _selectedId = structureId;
            Refresh();
        }

        /// <summary>
        /// Selects a Battalion by id (Battalions are owned per-Nation, so the owning Nation id is
        /// required to resolve it) so the panel shows its full attribute set (Req 7.2).
        /// </summary>
        public void SelectBattalion(int nationId, int battalionId)
        {
            _selectedKind = InfoEntityKind.Battalion;
            _selectedNationId = nationId;
            _selectedId = battalionId;
            Refresh();
        }

        /// <summary>Clears the current selection, showing the empty panel.</summary>
        public void ClearSelection()
        {
            _selectedKind = InfoEntityKind.None;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            EnsureBuilt();
            SubscribeToDriver();
            Refresh();
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

        private void OnTicked(IReadOnlyList<GameEvent> events) => Refresh();

        /// <summary>
        /// Guarantees the ability cooldown display refreshes at least once per second (Req 13.4),
        /// independent of the simulation tick cadence — important for peers whose driver does not tick.
        /// Rebuilds the view-model and updates only the ability controls' cooldown text and enabled
        /// state (the full per-tick <see cref="Refresh"/> handles everything else). Cheap and idempotent.
        /// </summary>
        private void Update()
        {
            if (!_built || _selectedKind != InfoEntityKind.Unit)
            {
                return;
            }

            _sinceCooldownRefresh += Time.unscaledDeltaTime;
            if (_sinceCooldownRefresh < _cooldownRefreshIntervalSeconds)
            {
                return;
            }

            _sinceCooldownRefresh = 0f;

            ViewModel = BuildViewModel();
            ReconcileAbilityControls(ViewModel);
            UpdateAbilityDynamic(ViewModel);
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

            _root = new VisualElement { name = "info-panel-root" };
            _root.style.position = Position.Absolute;
            _root.style.right = 0;
            _root.style.bottom = 0;
            _root.style.minWidth = 240;
            _root.style.flexDirection = FlexDirection.Column;

            _titleLabel = new Label("No selection") { name = "info-panel-title" };
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _root.Add(_titleLabel);

            _rows = new VisualElement { name = "info-panel-rows" };
            _rows.style.flexDirection = FlexDirection.Column;
            _root.Add(_rows);

            _abilities = new VisualElement { name = "info-panel-abilities" };
            _abilities.style.flexDirection = FlexDirection.Column;
            _root.Add(_abilities);

            uiRoot.Add(_root);
            _built = true;
        }

        // ------------------------------------------------------------------
        // Refresh (Req 7.2, 7.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// Re-resolves the selected entity from the authoritative state, rebuilds the view-model, and
        /// repaints the panel. An entity that has left the Match reverts the panel to empty.
        /// </summary>
        public void Refresh()
        {
            EnsureBuilt();
            if (!_built)
            {
                return;
            }

            _sinceCooldownRefresh = 0f;
            ViewModel = BuildViewModel();
            Render(ViewModel);
        }

        private InfoPanelViewModel BuildViewModel()
        {
            var state = _driver != null ? _driver.State : null;
            if (state == null)
            {
                return InfoPanelViewModel.Empty;
            }

            switch (_selectedKind)
            {
                case InfoEntityKind.Unit:
                    if (state.Units.TryGetValue(_selectedId, out var unit))
                    {
                        if (TryResolveEnemyDisplayPosition(
                                unit.OwnerNationId,
                                VisionSystem.UnitKey(unit.Id),
                                unit.Position,
                                out WorldPosition? unitDisplay,
                                out bool unitSuppressed))
                        {
                            // Hidden with no Last_Known_Position: cease displaying it (Req 14.4, 14.9).
                            return unitSuppressed ? InfoPanelViewModel.Empty : InfoPanelViewModel.ForUnit(unit, unitDisplay);
                        }

                        return InfoPanelViewModel.ForUnit(unit);
                    }

                    return InfoPanelViewModel.Empty;

                case InfoEntityKind.Structure:
                    if (state.Structures.TryGetValue(_selectedId, out var structure))
                    {
                        if (TryResolveEnemyDisplayPosition(
                                structure.OwnerNationId,
                                VisionSystem.StructureKey(structure.Id),
                                WorldPosition.FromCell(structure.Origin),
                                out WorldPosition? structureDisplay,
                                out bool structureSuppressed))
                        {
                            return structureSuppressed
                                ? InfoPanelViewModel.Empty
                                : InfoPanelViewModel.ForStructure(structure, structureDisplay);
                        }

                        return InfoPanelViewModel.ForStructure(structure);
                    }

                    return InfoPanelViewModel.Empty;

                case InfoEntityKind.Battalion:
                    if (state.Nations.TryGetValue(_selectedNationId, out var nation)
                        && nation.Battalions.TryGetValue(_selectedId, out var battalion))
                    {
                        return InfoPanelViewModel.ForBattalion(battalion, state);
                    }

                    return InfoPanelViewModel.Empty;

                default:
                    return InfoPanelViewModel.Empty;
            }
        }

        /// <summary>
        /// Resolves the fog-of-war display position for a selected entity owned by
        /// <paramref name="ownerNationId"/> from the local Nation's perspective (Req 14.5, 14.6, 14.9).
        /// Returns false (no override) for the local Nation's own entities or when no vision context is
        /// wired — the caller then uses the live position. For an enemy entity it returns true and sets
        /// <paramref name="display"/> to the current position (visible) or Last_Known_Position
        /// (hidden-with-LKP); when the entity is hidden with no LKP it sets <paramref name="suppressed"/>
        /// so the caller ceases displaying it.
        /// </summary>
        private bool TryResolveEnemyDisplayPosition(
            int ownerNationId,
            int entityKey,
            WorldPosition currentPosition,
            out WorldPosition? display,
            out bool suppressed)
        {
            display = null;
            suppressed = false;

            if (_vision == null || _localNationId < 0 || ownerNationId == _localNationId)
            {
                return false;
            }

            WorldPosition? resolved = _vision.GetDisplayPosition(_localNationId, entityKey, currentPosition);
            if (resolved.HasValue)
            {
                display = resolved.Value;
            }
            else
            {
                suppressed = true;
            }

            return true;
        }

        private void Render(InfoPanelViewModel model)
        {
            _titleLabel.text = model.Kind == InfoEntityKind.None
                ? model.DisplayName
                : $"{model.DisplayName} ({model.EntityId})";

            _rows.Clear();
            foreach (var attribute in model.Attributes)
            {
                var row = new VisualElement { name = $"info-row-{attribute.Name}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.justifyContent = Justify.SpaceBetween;

                var nameLabel = new Label(attribute.Name);
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.marginRight = 8;

                var valueLabel = new Label(attribute.Value);

                row.Add(nameLabel);
                row.Add(valueLabel);
                _rows.Add(row);
            }

            // Selectable ability controls with live cooldown display (Req 13.1, 13.4).
            ReconcileAbilityControls(model);
            UpdateAbilityDynamic(model);
        }

        // ------------------------------------------------------------------
        // Ability controls (Req 13.1-13.4)
        // ------------------------------------------------------------------

        /// <summary>
        /// Reconciles the persistent per-ability controls to match <paramref name="model"/>'s ability
        /// list: creates a selectable control (button + status label) for each newly present ability,
        /// removes controls for abilities no longer present (or when the selection is not a Unit), and
        /// keeps existing controls so their handlers and focus survive a refresh (Req 13.1).
        /// </summary>
        private void ReconcileAbilityControls(InfoPanelViewModel model)
        {
            if (_abilities == null)
            {
                return;
            }

            // Add/keep controls for every ability currently present.
            foreach (var ability in model.Abilities)
            {
                if (string.IsNullOrEmpty(ability.AbilityId) || _abilityControls.ContainsKey(ability.AbilityId))
                {
                    continue;
                }

                _abilityControls[ability.AbilityId] = CreateAbilityControl(ability.AbilityId);
            }

            // Remove controls whose ability is no longer present in the current selection.
            _abilityRemovalScratch.Clear();
            foreach (var id in _abilityControls.Keys)
            {
                if (!ContainsAbility(model, id))
                {
                    _abilityRemovalScratch.Add(id);
                }
            }

            foreach (var id in _abilityRemovalScratch)
            {
                if (_abilityControls.TryGetValue(id, out var control))
                {
                    control.Row?.RemoveFromHierarchy();
                }

                _abilityControls.Remove(id);
            }
        }

        private static bool ContainsAbility(InfoPanelViewModel model, string abilityId)
        {
            foreach (var ability in model.Abilities)
            {
                if (string.Equals(ability.AbilityId, abilityId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private AbilityControl CreateAbilityControl(string abilityId)
        {
            string capturedId = abilityId;

            var row = new VisualElement { name = $"info-ability-{abilityId}" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.SpaceBetween;

            var button = new Button(() => OnAbilityClicked(capturedId))
            {
                name = $"ability-activate-{abilityId}",
                text = abilityId,
            };
            button.style.marginRight = 8;

            var status = new Label(string.Empty) { name = $"ability-status-{abilityId}" };

            row.Add(button);
            row.Add(status);
            _abilities.Add(row);

            return new AbilityControl(abilityId, row, button, status);
        }

        /// <summary>
        /// Updates every ability control's enabled state and cooldown/status text from the current
        /// view-model (Req 13.4). The enabled state is bound to the shared availability predicate —
        /// "cooldown fully elapsed AND resources sufficient" (Req 13.3) — falling back to the
        /// cooldown-only readiness in the snapshot when no availability helper is wired.
        /// </summary>
        private void UpdateAbilityDynamic(InfoPanelViewModel model)
        {
            if (_abilityControls.Count == 0)
            {
                return;
            }

            var state = _driver != null ? _driver.State : null;
            UnitInstance unit = null;
            Nation nation = null;
            if (state != null && model.UnitId >= 0)
            {
                state.Units.TryGetValue(model.UnitId, out unit);
                state.Nations.TryGetValue(model.OwnerNationId, out nation);
            }

            foreach (var ability in model.Abilities)
            {
                if (!_abilityControls.TryGetValue(ability.AbilityId, out var control))
                {
                    continue;
                }

                bool enabled = ComputeAbilityEnabled(ability, unit, nation);
                control.Button.SetEnabled(enabled);
                control.Status.text = BuildAbilityStatusText(ability, enabled);
            }
        }

        /// <summary>
        /// Whether an ability control is currently activatable: delegates to the shared
        /// <see cref="CommandAvailability.CanActivateAbility"/> predicate (cooldown elapsed AND resources
        /// sufficient) when the predicate, Unit, and Nation are resolvable; otherwise falls back to the
        /// snapshot's cooldown-only readiness (Req 13.3, 13.4).
        /// </summary>
        private bool ComputeAbilityEnabled(AbilityInfo ability, UnitInstance unit, Nation nation)
        {
            if (_availability != null && unit != null && nation != null)
            {
                return _availability.CanActivateAbility(nation, unit, ability.AbilityId);
            }

            return ability.IsReady;
        }

        /// <summary>
        /// Builds the observable status text for an ability control (Req 13.3, 13.4): the remaining
        /// cooldown in whole seconds while on cooldown; the affordability shortfall when off cooldown
        /// but disabled (insufficient resources); otherwise "Ready".
        /// </summary>
        private static string BuildAbilityStatusText(AbilityInfo ability, bool enabled)
        {
            if (!ability.IsReady)
            {
                return $"{ability.RemainingWholeSeconds}s";
            }

            if (!enabled)
            {
                return ability.Cost.IsFree ? "Unavailable" : $"Need {ability.CostText}";
            }

            return "Ready";
        }

        /// <summary>
        /// Issues an <see cref="EpochWar.Core.Commands.ActivateAbilityCommand"/> for the selected Unit
        /// and chosen ability through the command controller's authoritative pipeline (Req 13.2), then
        /// refreshes so any resulting cooldown/resource change is reflected immediately.
        /// </summary>
        private void OnAbilityClicked(string abilityId)
        {
            if (_selectedKind != InfoEntityKind.Unit || _commandControls == null || string.IsNullOrEmpty(abilityId))
            {
                return;
            }

            _commandControls.ActivateAbility(_selectedId, abilityId);
            Refresh();
        }

        /// <summary>A persistent selectable ability control: a button plus its cooldown/status label.</summary>
        private sealed class AbilityControl
        {
            public AbilityControl(string abilityId, VisualElement row, Button button, Label status)
            {
                AbilityId = abilityId;
                Row = row;
                Button = button;
                Status = status;
            }

            public string AbilityId { get; }

            public VisualElement Row { get; }

            public Button Button { get; }

            public Label Status { get; }
        }
    }
}
