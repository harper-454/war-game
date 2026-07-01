using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.UI
{
    /// <summary>
    /// The kind of entity an <see cref="InfoPanelViewModel"/> describes (Req 7.2).
    /// </summary>
    public enum InfoEntityKind
    {
        None = 0,
        Unit = 1,
        Battalion = 2,
        Structure = 3,
    }

    /// <summary>
    /// A single labelled attribute row shown in the information panel — a name/value pair such as
    /// <c>("Attack", "12")</c>. Values are pre-formatted strings so the presentation layer is a
    /// trivial renderer and the whole view-model can be verified without any UI.
    /// </summary>
    public readonly struct InfoAttribute : IEquatable<InfoAttribute>
    {
        /// <summary>The canonical attribute name (stable, used as a lookup key).</summary>
        public string Name { get; }

        /// <summary>The formatted display value.</summary>
        public string Value { get; }

        public InfoAttribute(string name, string value)
        {
            Name = name;
            Value = value ?? string.Empty;
        }

        public bool Equals(InfoAttribute other)
            => string.Equals(Name, other.Name, StringComparison.Ordinal)
               && string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is InfoAttribute other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (Name?.GetHashCode() ?? 0);
                hash = (hash * 31) + (Value?.GetHashCode() ?? 0);
                return hash;
            }
        }

        public override string ToString() => $"{Name} = {Value}";
    }

    /// <summary>
    /// A snapshot of one activatable <see cref="UnitAbilityDef"/> on the selected Unit, used to render
    /// a selectable ability control with its remaining cooldown on the information panel (Req 13.1,
    /// 13.4). Like the rest of the view-model this is a pure data snapshot with no <c>UnityEngine</c>
    /// dependency, so the controller renders it trivially and it can be verified without any UI.
    /// </summary>
    public readonly struct AbilityInfo : IEquatable<AbilityInfo>
    {
        /// <summary>The ability id, matching the Unit type's <c>UnitDef.AbilityDefs</c> (Req 13.1).</summary>
        public string AbilityId { get; }

        /// <summary>The category of effect the ability executes (for display/labelling).</summary>
        public AbilityEffectKind EffectKind { get; }

        /// <summary>The exact remaining cooldown in simulation seconds; 0 when ready.</summary>
        public float RemainingCooldownSeconds { get; }

        /// <summary>
        /// The remaining cooldown rounded UP to whole seconds for display (Req 13.4): a ceiling so a
        /// partially-elapsed second still shows as remaining. 0 when the ability is ready.
        /// </summary>
        public int RemainingWholeSeconds { get; }

        /// <summary>True when the ability's cooldown has fully elapsed (Req 13.4).</summary>
        public bool IsReady { get; }

        /// <summary>The Resource cost deducted on a successful activation (Req 13.2).</summary>
        public ResourceCost Cost { get; }

        /// <summary>A pre-formatted, locale-independent rendering of <see cref="Cost"/>.</summary>
        public string CostText { get; }

        public AbilityInfo(
            string abilityId,
            AbilityEffectKind effectKind,
            float remainingCooldownSeconds,
            int remainingWholeSeconds,
            bool isReady,
            ResourceCost cost,
            string costText)
        {
            AbilityId = abilityId ?? string.Empty;
            EffectKind = effectKind;
            RemainingCooldownSeconds = remainingCooldownSeconds;
            RemainingWholeSeconds = remainingWholeSeconds;
            IsReady = isReady;
            Cost = cost;
            CostText = costText ?? string.Empty;
        }

        public bool Equals(AbilityInfo other)
            => string.Equals(AbilityId, other.AbilityId, StringComparison.Ordinal)
               && EffectKind == other.EffectKind
               && RemainingWholeSeconds == other.RemainingWholeSeconds
               && IsReady == other.IsReady
               && Cost.Equals(other.Cost);

        public override bool Equals(object obj) => obj is AbilityInfo other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (AbilityId?.GetHashCode() ?? 0);
                hash = (hash * 31) + (int)EffectKind;
                hash = (hash * 31) + RemainingWholeSeconds;
                hash = (hash * 31) + (IsReady ? 1 : 0);
                return hash;
            }
        }

        public override string ToString()
            => $"{AbilityId} ({(IsReady ? "ready" : RemainingWholeSeconds + "s")})";
    }

    /// <summary>
    /// The engine-free, immutable view-model for the selection information panel (Req 7.2, 7.4).
    ///
    /// When a Player selects a <see cref="UnitInstance"/>, <see cref="Battalion"/>, or
    /// <see cref="StructureInstance"/>, the UI layer builds one of these snapshots and renders its
    /// <see cref="Attributes"/> — a complete, ordered list of every detailed attribute of the
    /// selected entity (Req 7.2, Property 29). Because the view-model is a plain data snapshot with
    /// no dependency on <c>UnityEngine</c>, its content can be verified directly by the optional
    /// property test (task 16.2) and it is cheap to rebuild whenever a change event fires so the
    /// panel refreshes within 1 second (Req 7.4).
    ///
    /// The model is a value snapshot: the controller discards the old instance and builds a fresh
    /// one via the <c>For*</c> factories on every change event rather than mutating in place, which
    /// keeps refresh logic trivial and the type safe to share.
    /// </summary>
    public sealed class InfoPanelViewModel
    {
        private readonly Dictionary<string, string> _index;

        /// <summary>The kind of entity described, or <see cref="InfoEntityKind.None"/> when nothing is selected.</summary>
        public InfoEntityKind Kind { get; }

        /// <summary>A stable identity string for the selected entity, e.g. <c>"Unit#3"</c>.</summary>
        public string EntityId { get; }

        /// <summary>A human-readable heading for the panel (the definition id or Battalion name).</summary>
        public string DisplayName { get; }

        /// <summary>Every detailed attribute of the selected entity, in a stable display order (Req 7.2).</summary>
        public IReadOnlyList<InfoAttribute> Attributes { get; }

        /// <summary>The selected Unit's id when <see cref="Kind"/> is <see cref="InfoEntityKind.Unit"/>; otherwise -1.</summary>
        public int UnitId { get; }

        /// <summary>The owning Nation id of the selected Unit; -1 when the selection is not a Unit.</summary>
        public int OwnerNationId { get; }

        /// <summary>
        /// The selected Unit's current Veterancy_Tier index (0-based into its Veterancy_Curve, 0 = base
        /// tier). 0 for non-Unit selections (Req 12.6).
        /// </summary>
        public int VeterancyTierIndex { get; }

        /// <summary>
        /// A display name for the selected Unit's current Veterancy_Tier (the tier's authored id, or a
        /// synthesized fallback), used to surface the Unit's tier to its owning Player (Req 12.6).
        /// </summary>
        public string VeterancyTierName { get; }

        /// <summary>
        /// The activatable abilities available on the selected Unit, each with its remaining cooldown,
        /// so the panel can render a selectable control per ability (Req 13.1, 13.4). Empty for non-Unit
        /// selections. Never null.
        /// </summary>
        public IReadOnlyList<AbilityInfo> Abilities { get; }

        private InfoPanelViewModel(
            InfoEntityKind kind,
            string entityId,
            string displayName,
            IReadOnlyList<InfoAttribute> attributes,
            int unitId = -1,
            int ownerNationId = -1,
            int veterancyTierIndex = 0,
            string veterancyTierName = null,
            IReadOnlyList<AbilityInfo> abilities = null)
        {
            Kind = kind;
            EntityId = entityId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Attributes = attributes ?? Array.Empty<InfoAttribute>();
            UnitId = unitId;
            OwnerNationId = ownerNationId;
            VeterancyTierIndex = veterancyTierIndex;
            VeterancyTierName = veterancyTierName ?? string.Empty;
            Abilities = abilities ?? Array.Empty<AbilityInfo>();

            _index = new Dictionary<string, string>(Attributes.Count, StringComparer.Ordinal);
            foreach (var attribute in Attributes)
            {
                _index[attribute.Name] = attribute.Value;
            }
        }

        /// <summary>The empty view-model shown when no entity is selected.</summary>
        public static readonly InfoPanelViewModel Empty =
            new InfoPanelViewModel(InfoEntityKind.None, string.Empty, "No selection", Array.Empty<InfoAttribute>());

        /// <summary>True when this view-model contains an attribute with the given canonical <paramref name="name"/>.</summary>
        public bool HasAttribute(string name) => name != null && _index.ContainsKey(name);

        /// <summary>Reads the formatted value of the attribute called <paramref name="name"/>, if present.</summary>
        public bool TryGetValue(string name, out string value)
        {
            if (name != null)
            {
                return _index.TryGetValue(name, out value);
            }

            value = null;
            return false;
        }

        /// <summary>The set of attribute names present, useful for completeness assertions (Property 29).</summary>
        public IReadOnlyCollection<string> AttributeNames => _index.Keys;

        // ------------------------------------------------------------------
        // Factories — build a complete snapshot for each entity kind (Req 7.2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Builds the complete view-model for a selected <see cref="UnitInstance"/>, exposing every
        /// mutable runtime attribute of the instance and every static attribute of its
        /// <see cref="UnitDef"/> (health, attack, defense, move speed, Era of origin, etc. — Req 3.6,
        /// 7.2). Never returns null; a null unit yields <see cref="Empty"/>.
        /// </summary>
        public static InfoPanelViewModel ForUnit(UnitInstance unit)
        {
            return ForUnit(unit, null);
        }

        /// <summary>
        /// Builds the complete view-model for a selected <see cref="UnitInstance"/> (see the parameterless
        /// overload), optionally substituting <paramref name="displayPosition"/> for the Unit's current
        /// position in the displayed coordinates. The Unity layer passes the fog-of-war display position
        /// (current while visible, Last_Known_Position while hidden-with-LKP) for enemy Units so the info
        /// panel never reveals a hidden enemy's live position (Req 14.5, 14.6); a null override uses the
        /// Unit's actual position.
        /// </summary>
        public static InfoPanelViewModel ForUnit(UnitInstance unit, WorldPosition? displayPosition)
        {
            if (unit == null)
            {
                return Empty;
            }

            var def = unit.Def;
            var b = new Builder(InfoEntityKind.Unit, $"Unit#{unit.Id}", def?.Id ?? "Unit");

            WorldPosition shownPosition = displayPosition ?? unit.Position;

            // Instance (mutable runtime) attributes.
            b.Add("Id", unit.Id);
            b.Add("OwnerNationId", unit.OwnerNationId);
            b.Add("Health", unit.Health);
            b.Add("PositionX", shownPosition.ToFloatX());
            b.Add("PositionY", shownPosition.ToFloatY());
            b.Add("PositionZ", shownPosition.ToFloatZ());
            b.Add("BattalionId", unit.BattalionId.HasValue ? unit.BattalionId.Value.ToString(CultureInfo.InvariantCulture) : "None");
            b.Add("Order", DescribeOrder(unit.CurrentOrder));

            // Veterancy (Req 12.6): surface the Unit's current tier and accumulated experience.
            string veterancyTierName = ResolveVeterancyTierName(def, unit.VeterancyTierIndex);
            b.Add("VeterancyTier", veterancyTierName);
            b.Add("VeterancyTierIndex", unit.VeterancyTierIndex);
            b.Add("VeterancyExperience", unit.VeterancyExperience);

            // Definition (static) attributes (Req 3.6).
            b.Add("DefId", def?.Id ?? "?");
            b.Add("Era", def != null ? def.Era.ToString() : "?");
            b.Add("Role", def != null ? def.Role.ToString() : "?");
            b.Add("MaxHealth", def?.MaxHealth ?? 0);
            b.Add("Attack", def?.Attack ?? 0);
            b.Add("Defense", def?.Defense ?? 0);
            b.Add("MoveSpeed", def?.MoveSpeed ?? 0f);
            b.Add("PopulationCost", def?.PopulationCost ?? 0);
            b.Add("BuildTimeSeconds", def?.BuildTimeSeconds ?? 0f);
            b.Add("Cost", def != null ? def.Cost.ToString() : "Free");
            b.Add("LaunchCost", def != null ? def.LaunchCost.ToString() : "Free");

            // Structured selection metadata for the interactive controls (abilities/veterancy).
            b.UnitId = unit.Id;
            b.OwnerNationId = unit.OwnerNationId;
            b.VeterancyTierIndex = unit.VeterancyTierIndex;
            b.VeterancyTierName = veterancyTierName;
            b.Abilities = BuildAbilities(unit, def);

            return b.Build();
        }

        /// <summary>
        /// Builds the complete view-model for a selected <see cref="StructureInstance"/>, exposing
        /// every runtime attribute of the instance and every static attribute of its
        /// <see cref="StructureDef"/> (health, construction progress, operational flag, footprint,
        /// function, etc. — Req 4, 7.2). Never returns null; a null structure yields <see cref="Empty"/>.
        /// </summary>
        public static InfoPanelViewModel ForStructure(StructureInstance structure)
        {
            return ForStructure(structure, null);
        }

        /// <summary>
        /// Builds the complete view-model for a selected <see cref="StructureInstance"/> (see the
        /// parameterless overload), optionally substituting <paramref name="displayPosition"/> for the
        /// Structure's origin in the displayed coordinates. The Unity layer passes the fog-of-war display
        /// position for enemy Structures so a hidden enemy Structure shows its Last_Known_Position rather
        /// than its live origin (Req 14.5, 14.6); a null override uses the Structure's actual origin.
        /// </summary>
        public static InfoPanelViewModel ForStructure(StructureInstance structure, WorldPosition? displayPosition)
        {
            if (structure == null)
            {
                return Empty;
            }

            var def = structure.Def;
            var b = new Builder(InfoEntityKind.Structure, $"Structure#{structure.Id}", def?.Id ?? "Structure");

            int originX = structure.Origin.X;
            int originY = structure.Origin.Y;
            int originZ = structure.Origin.Z;
            if (displayPosition.HasValue)
            {
                originX = displayPosition.Value.X.ToInt();
                originY = displayPosition.Value.Y.ToInt();
                originZ = displayPosition.Value.Z.ToInt();
            }

            // Instance (mutable runtime) attributes.
            b.Add("Id", structure.Id);
            b.Add("OwnerNationId", structure.OwnerNationId);
            b.Add("Health", structure.Health);
            b.Add("OriginX", originX);
            b.Add("OriginY", originY);
            b.Add("OriginZ", originZ);
            b.Add("ConstructionProgress", structure.ConstructionProgress);
            b.Add("IsOperational", structure.IsOperational);

            // Definition (static) attributes (Req 4).
            b.Add("DefId", def?.Id ?? "?");
            b.Add("Era", def != null ? def.Era.ToString() : "?");
            b.Add("Function", def != null ? def.Function.ToString() : "?");
            b.Add("MaxHealth", def?.MaxHealth ?? 0);
            b.Add("FootprintWidth", def?.FootprintWidth ?? 0);
            b.Add("FootprintLength", def?.FootprintLength ?? 0);
            b.Add("PopulationCost", def?.PopulationCost ?? 0);
            b.Add("BuildTimeSeconds", def?.BuildTimeSeconds ?? 0f);
            b.Add("Cost", def != null ? def.Cost.ToString() : "Free");
            b.Add("IsPeaceArch", def?.IsPeaceArch ?? false);

            return b.Build();
        }

        /// <summary>
        /// Builds the complete view-model for a selected <see cref="Battalion"/>, exposing every
        /// attribute of the group (id, name, membership) — Req 3.3, 7.2. When a <paramref name="state"/>
        /// is supplied the member ids are additionally resolved to a stable, ordered summary; passing
        /// null still yields a complete model built from the Battalion alone. A null battalion yields
        /// <see cref="Empty"/>.
        /// </summary>
        public static InfoPanelViewModel ForBattalion(Battalion battalion, MatchState state = null)
        {
            if (battalion == null)
            {
                return Empty;
            }

            var b = new Builder(InfoEntityKind.Battalion, $"Battalion#{battalion.Id}", battalion.Name ?? "Battalion");

            var memberIds = battalion.MemberUnitIds != null
                ? battalion.MemberUnitIds.OrderBy(id => id).ToList()
                : new List<int>();

            b.Add("Id", battalion.Id);
            b.Add("Name", battalion.Name ?? string.Empty);
            b.Add("MemberCount", memberIds.Count);
            b.Add("Members", memberIds.Count == 0
                ? "None"
                : string.Join(", ", memberIds.Select(id => id.ToString(CultureInfo.InvariantCulture))));

            // When the Match state is available, surface the surviving members' aggregate health so
            // the panel reflects the group's current strength (Req 7.4).
            if (state != null)
            {
                int totalHealth = 0;
                int living = 0;
                foreach (var id in memberIds)
                {
                    if (state.Units.TryGetValue(id, out var unit) && unit.Health > 0)
                    {
                        totalHealth += unit.Health;
                        living++;
                    }
                }

                b.Add("LivingMemberCount", living);
                b.Add("TotalHealth", totalHealth);
            }

            return b.Build();
        }

        private static string DescribeOrder(UnitOrder order)
        {
            return order.Kind == UnitOrder.OrderKind.Move
                ? $"Move -> {order.Destination} (waypoint {order.WaypointIndex}/{order.Path.Count})"
                : "Idle";
        }

        /// <summary>
        /// Resolves a display name for a Unit's current Veterancy_Tier (Req 12.6). The Unit's
        /// <see cref="UnitInstance.VeterancyTierIndex"/> indexes directly into its
        /// <c>UnitDef.VeterancyCurve</c> (matching the Core combat/veterancy lookup); a Unit type with
        /// no curve reports <c>"None"</c>, and an out-of-range index degrades to a synthesized label
        /// rather than throwing.
        /// </summary>
        private static string ResolveVeterancyTierName(UnitDef def, int tierIndex)
        {
            var curve = def?.VeterancyCurve;
            if (curve == null || curve.Count == 0)
            {
                return "None";
            }

            if (tierIndex >= 0 && tierIndex < curve.Count && curve[tierIndex] != null)
            {
                return curve[tierIndex].Id ?? $"Tier {tierIndex}";
            }

            return $"Tier {tierIndex}";
        }

        /// <summary>
        /// Builds the ability snapshots for a selected Unit (Req 13.1, 13.4): one <see cref="AbilityInfo"/>
        /// per <c>UnitDef.AbilityDef</c>, each carrying the current remaining cooldown read from the
        /// instance's <see cref="UnitInstance.AbilityRemainingCooldown"/> map (absent/non-positive =
        /// ready). Remaining seconds are rounded up for whole-second display. Returns an empty list when
        /// the Unit type defines no abilities.
        /// </summary>
        private static IReadOnlyList<AbilityInfo> BuildAbilities(UnitInstance unit, UnitDef def)
        {
            var defs = def?.AbilityDefs;
            if (defs == null || defs.Count == 0)
            {
                return Array.Empty<AbilityInfo>();
            }

            var abilities = new List<AbilityInfo>(defs.Count);
            foreach (var abilityDef in defs)
            {
                if (abilityDef == null)
                {
                    continue;
                }

                float remaining = 0f;
                if (unit.AbilityRemainingCooldown != null
                    && unit.AbilityRemainingCooldown.TryGetValue(abilityDef.Id, out Fixed cd)
                    && cd > Fixed.Zero)
                {
                    remaining = cd.ToFloat();
                }

                bool isReady = remaining <= 0f;
                int whole = isReady ? 0 : (int)System.Math.Ceiling(remaining);
                string costText = abilityDef.Cost.IsFree ? "Free" : abilityDef.Cost.ToString();

                abilities.Add(new AbilityInfo(
                    abilityDef.Id,
                    abilityDef.EffectKind,
                    remaining,
                    whole,
                    isReady,
                    abilityDef.Cost,
                    costText));
            }

            return abilities;
        }

        /// <summary>
        /// Accumulates ordered attributes and formats scalar values with the invariant culture so the
        /// produced strings are deterministic and locale-independent (important for the property test).
        /// </summary>
        private sealed class Builder
        {
            private readonly InfoEntityKind _kind;
            private readonly string _entityId;
            private readonly string _displayName;
            private readonly List<InfoAttribute> _attributes = new List<InfoAttribute>();

            public Builder(InfoEntityKind kind, string entityId, string displayName)
            {
                _kind = kind;
                _entityId = entityId;
                _displayName = displayName;
            }

            // Optional structured extras for a Unit selection (default to "not a Unit").
            public int UnitId = -1;
            public int OwnerNationId = -1;
            public int VeterancyTierIndex;
            public string VeterancyTierName = string.Empty;
            public IReadOnlyList<AbilityInfo> Abilities = Array.Empty<AbilityInfo>();

            public void Add(string name, string value) => _attributes.Add(new InfoAttribute(name, value));

            public void Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

            public void Add(string name, bool value) => Add(name, value ? "true" : "false");

            public void Add(string name, float value) => Add(name, value.ToString("0.###", CultureInfo.InvariantCulture));

            public InfoPanelViewModel Build()
                => new InfoPanelViewModel(
                    _kind,
                    _entityId,
                    _displayName,
                    _attributes,
                    UnitId,
                    OwnerNationId,
                    VeterancyTierIndex,
                    VeterancyTierName,
                    Abilities);
        }
    }
}
