using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// Binds the persistent HUD control surface to the local Nation's economy, Era, and population
    /// (Req 7.1, 7.4) using UI Toolkit.
    ///
    /// The controller owns a <see cref="UIDocument"/> and builds its control surface entirely in C#
    /// (no hand-authored UXML is required): a resource strip with one readout per
    /// <see cref="ResourceType"/>, an Era readout, and a population readout. It subscribes to the
    /// authoritative <see cref="SimulationDriver.Ticked"/> event and re-reads the local
    /// <see cref="Nation"/> from the Match state after every fixed tick, so a change to any displayed
    /// value is reflected essentially immediately — far inside the 1-second bound the requirement sets
    /// (Req 2.6, 7.4). The driver ticks at a fixed rate (20 Hz by default) that guarantees at least one
    /// refresh well within a second.
    ///
    /// The HUD is a pure presentation reader: it never mutates state, and it renders whatever the
    /// authoritative state currently holds, so it behaves identically on the Host and on a client that
    /// is displaying replicated state. Resource display names are resolved through the optional
    /// <see cref="ICatalog"/> when one is bound, falling back to the enum name otherwise.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudController : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The UIDocument whose rootVisualElement hosts the HUD. Defaults to the sibling UIDocument.")]
        private UIDocument _document;

        [SerializeField]
        [Tooltip("Drives the simulation; the HUD refreshes after each of its ticks.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("The id of the Nation this local client controls; its resources/era/population are shown.")]
        private int _localNationId;

        private ICatalog _catalog;

        // Built lazily from code so the HUD works with no authored .uxml/.uss.
        private VisualElement _root;
        private readonly Dictionary<ResourceType, Label> _resourceValueLabels =
            new Dictionary<ResourceType, Label>();
        private Label _eraValueLabel;
        private Label _populationValueLabel;
        private bool _built;
        private bool _subscribed;

        /// <summary>The id of the Nation whose state this HUD displays.</summary>
        public int LocalNationId
        {
            get => _localNationId;
            set
            {
                _localNationId = value;
                Refresh();
            }
        }

        /// <summary>
        /// Wires the HUD to its data sources at runtime (used by the match scene bootstrap). Binds the
        /// driver whose tick drives refreshes, the local Nation id, and an optional catalog for
        /// resource display names, then builds and populates the surface immediately.
        /// </summary>
        public void Bind(SimulationDriver driver, int localNationId, ICatalog catalog = null)
        {
            UnsubscribeFromDriver();

            _driver = driver;
            _localNationId = localNationId;
            _catalog = catalog;

            EnsureBuilt();
            SubscribeToDriver();
            Refresh();
        }

        /// <summary>Supplies (or replaces) the catalog used to resolve resource display names.</summary>
        public void SetCatalog(ICatalog catalog)
        {
            _catalog = catalog;
            Refresh();
        }

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
                // The UIDocument has not been laid out yet (e.g. called before OnEnable completes);
                // building is retried on the next Refresh/OnEnable.
                return;
            }

            _root = new VisualElement { name = "hud-root" };
            _root.style.flexDirection = FlexDirection.Column;
            _root.style.position = Position.Absolute;
            _root.style.top = 0;
            _root.style.left = 0;
            _root.style.right = 0;
            _root.pickingMode = PickingMode.Ignore;

            // --- Resource strip: one readout per resource type (Req 7.1) ---
            var resourceStrip = new VisualElement { name = "hud-resources" };
            resourceStrip.style.flexDirection = FlexDirection.Row;
            resourceStrip.style.flexWrap = Wrap.Wrap;
            _root.Add(resourceStrip);

            _resourceValueLabels.Clear();
            foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
            {
                var cell = new VisualElement { name = $"hud-resource-{type}" };
                cell.style.flexDirection = FlexDirection.Row;
                cell.style.marginRight = 12;

                var nameLabel = new Label(ResourceDisplayName(type) + ":") { name = $"hud-resource-name-{type}" };
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                nameLabel.style.marginRight = 4;

                var valueLabel = new Label("0") { name = $"hud-resource-value-{type}" };

                cell.Add(nameLabel);
                cell.Add(valueLabel);
                resourceStrip.Add(cell);

                _resourceValueLabels[type] = valueLabel;
            }

            // --- Era + population readouts (Req 7.1) ---
            var statusStrip = new VisualElement { name = "hud-status" };
            statusStrip.style.flexDirection = FlexDirection.Row;

            _eraValueLabel = AddLabelledReadout(statusStrip, "hud-era", "Era", "-");
            _populationValueLabel = AddLabelledReadout(statusStrip, "hud-population", "Population", "0 / 0");

            _root.Add(statusStrip);

            uiRoot.Add(_root);
            _built = true;
        }

        private static Label AddLabelledReadout(VisualElement parent, string id, string caption, string initialValue)
        {
            var cell = new VisualElement { name = id };
            cell.style.flexDirection = FlexDirection.Row;
            cell.style.marginRight = 16;

            var nameLabel = new Label(caption + ":") { name = $"{id}-name" };
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginRight = 4;

            var valueLabel = new Label(initialValue) { name = $"{id}-value" };

            cell.Add(nameLabel);
            cell.Add(valueLabel);
            parent.Add(cell);

            return valueLabel;
        }

        // ------------------------------------------------------------------
        // Refresh (Req 7.1, 7.4, 2.6)
        // ------------------------------------------------------------------

        /// <summary>
        /// Re-reads the local Nation from the authoritative state and repaints every readout. Safe to
        /// call at any time; a no-op until the surface is built and a bound Nation is available.
        /// </summary>
        public void Refresh()
        {
            EnsureBuilt();
            if (!_built)
            {
                return;
            }

            var state = _driver != null ? _driver.State : null;
            Nation nation = null;
            state?.Nations.TryGetValue(_localNationId, out nation);

            // Resource readouts.
            foreach (var pair in _resourceValueLabels)
            {
                pair.Value.text = FormatResource(nation, pair.Key);
            }

            // Era readout.
            _eraValueLabel.text = nation != null ? nation.CurrentEra.ToString() : "-";

            // Population readout (current / capacity).
            _populationValueLabel.text = nation != null
                ? string.Format(
                    CultureInfo.InvariantCulture, "{0} / {1}", nation.Population, nation.PopulationCapacity)
                : "0 / 0";
        }

        private static string FormatResource(Nation nation, ResourceType type)
        {
            if (nation == null || !nation.Resources.TryGetValue(type, out var store))
            {
                return "0";
            }

            string amount = store.Amount.ToString("0.#", CultureInfo.InvariantCulture);
            return store.IsUncapped
                ? amount
                : string.Format(
                    CultureInfo.InvariantCulture, "{0} / {1}", amount, store.Capacity.ToString("0.#", CultureInfo.InvariantCulture));
        }

        private string ResourceDisplayName(ResourceType type)
        {
            if (_catalog != null)
            {
                var def = _catalog.GetResource(type);
                if (def != null && !string.IsNullOrEmpty(def.DisplayName))
                {
                    return def.DisplayName;
                }
            }

            return type.ToString();
        }
    }
}
