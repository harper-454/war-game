using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for a <see cref="UnitDef"/> (Req 3.1, 3.6, 3.7, 11.1).
    ///
    /// One asset describes one recruitable Unit type and its full attribute set — recruit
    /// <see cref="_cost"/>/build time, population cost, health, attack, defense, move speed, Era of
    /// origin, and functional <see cref="UnitRole"/>. <see cref="_launchCost"/> is only meaningful for
    /// the <see cref="UnitRole.ColonyShip"/> role (Req 11.2). <see cref="ToCore"/> produces the
    /// engine-free <see cref="UnitDef"/> the Unit_/Combat_ systems consume.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Unit", fileName = "Unit")]
    public sealed class UnitAsset : ScriptableObject
    {
        [Tooltip("Stable unique identifier; also the catalog key. Must be unique across all units.")]
        [SerializeField] private string _id = string.Empty;

        [Tooltip("The era of origin for this unit type.")]
        [SerializeField] private Era _era = Era.Prehistoric;

        [Tooltip("Resource cost deducted when recruitment is issued.")]
        [SerializeField] private ResourceCostAuthoring _cost = new ResourceCostAuthoring();

        [Tooltip("Simulation seconds that must elapse before the unit is produced.")]
        [SerializeField] private float _buildTimeSeconds = 1f;

        [Tooltip("Population required to field this unit; checked against availability.")]
        [SerializeField] private int _populationCost = 1;

        [Tooltip("Maximum (and starting) health for instances of this unit.")]
        [SerializeField] private int _maxHealth = 10;

        [Tooltip("Attack value used by the combat damage formula.")]
        [SerializeField] private int _attack = 1;

        [Tooltip("Defense value used by the combat damage formula.")]
        [SerializeField] private int _defense = 0;

        [Tooltip("Movement speed in cells per second over the navigation grid.")]
        [SerializeField] private float _moveSpeed = 1f;

        [Tooltip("Functional role; influences pathfinding and special victory behavior.")]
        [SerializeField] private UnitRole _role = UnitRole.Worker;

        [Tooltip("Resource cost paid to LAUNCH a completed Colony Ship (Req 11.2). Ignored for other roles.")]
        [SerializeField] private ResourceCostAuthoring _launchCost = new ResourceCostAuthoring();

        [Tooltip("Visual_Detail_Tier OVERRIDE (Req 7.4). Leave at -1 (the default) to let the Entity_View_System "
            + "derive the tier from this unit's Era; set a positive value to force a specific tier. Any value "
            + "outside the Entity_View_System's valid range is treated as unset and falls back to the Era default (Req 7.5).")]
        [SerializeField] private int _visualDetailTier = UnsetVisualDetailTier;

        /// <summary>
        /// Sentinel authored value meaning "no override — use the Era-derived default" (Req 7.4, 7.5).
        /// Chosen negative so it can never collide with a valid positive <see cref="UnitDef.VisualDetailTier"/>
        /// and so the Entity_View_System can distinguish an authored override from an unset field.
        /// </summary>
        public const int UnsetVisualDetailTier = -1;

        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id => _id;

        /// <summary>The era of origin for this unit type.</summary>
        public Era Era => _era;

        /// <summary>The functional role of this unit type.</summary>
        public UnitRole Role => _role;

        /// <summary>
        /// The authored Visual_Detail_Tier override, or <see cref="UnsetVisualDetailTier"/> (-1) when the
        /// content author left it unset so the Entity_View_System derives it from <see cref="Era"/> (Req 7.4).
        /// </summary>
        public int VisualDetailTier => _visualDetailTier;

        /// <summary>Converts this authored asset into its engine-free <see cref="UnitDef"/>.</summary>
        public UnitDef ToCore()
        {
            return new UnitDef(
                _id,
                _era,
                _cost != null ? _cost.ToCore() : ResourceCost.Free,
                _buildTimeSeconds,
                _populationCost,
                _maxHealth,
                _attack,
                _defense,
                _moveSpeed,
                _role,
                _launchCost != null ? _launchCost.ToCore() : ResourceCost.Free,
                visualDetailTier: _visualDetailTier);
        }
    }
}
