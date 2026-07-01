using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for a <see cref="StructureDef"/> (Req 4.1-4.6, 10.1-10.4).
    ///
    /// One asset describes one buildable Structure type. The design's <c>Vector2Int Footprint</c> is
    /// authored here as a Unity <see cref="Vector2Int"/> and split into the two integer extents the
    /// engine-free <see cref="StructureDef"/> expects (X = width along terrain X, Y = length along
    /// terrain Z). <see cref="_isPeaceArch"/> tags the single Peace victory wonder (Req 10);
    /// <see cref="ToCore"/> produces the engine-free def the Base_System consumes.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Structure", fileName = "Structure")]
    public sealed class StructureAsset : ScriptableObject
    {
        [Tooltip("Stable unique identifier; also the catalog key. Must be unique across all structures.")]
        [SerializeField] private string _id = string.Empty;

        [Tooltip("The era at which this structure type unlocks.")]
        [SerializeField] private Era _era = Era.Prehistoric;

        [Tooltip("Resource cost deducted when placement begins construction.")]
        [SerializeField] private ResourceCostAuthoring _cost = new ResourceCostAuthoring();

        [Tooltip("Simulation seconds required for construction to complete.")]
        [SerializeField] private float _buildTimeSeconds = 5f;

        [Tooltip("Population required to construct this structure; checked against availability.")]
        [SerializeField] private int _populationCost = 0;

        [Tooltip("Maximum (and starting) health for instances of this structure.")]
        [SerializeField] private int _maxHealth = 100;

        [Tooltip("Terrain cells occupied: X = width (terrain X), Y = length (terrain Z).")]
        [SerializeField] private Vector2Int _footprint = new Vector2Int(1, 1);

        [Tooltip("The function enabled once the structure is operational.")]
        [SerializeField] private StructureFunction _function = StructureFunction.ResourceExtractor;

        [Tooltip("True only for the Peace Arch wonder (Req 10).")]
        [SerializeField] private bool _isPeaceArch = false;

        [Tooltip("Visual_Detail_Tier OVERRIDE (Req 7.4). Leave at -1 (the default) to let the Entity_View_System "
            + "derive the tier from this structure's Era; set a positive value to force a specific tier. Any value "
            + "outside the Entity_View_System's valid range is treated as unset and falls back to the Era default (Req 7.5).")]
        [SerializeField] private int _visualDetailTier = UnsetVisualDetailTier;

        /// <summary>
        /// Sentinel authored value meaning "no override — use the Era-derived default" (Req 7.4, 7.5).
        /// Chosen negative so it can never collide with a valid positive <see cref="StructureDef.VisualDetailTier"/>
        /// and so the Entity_View_System can distinguish an authored override from an unset field.
        /// </summary>
        public const int UnsetVisualDetailTier = -1;

        /// <summary>Stable unique identifier (catalog key).</summary>
        public string Id => _id;

        /// <summary>The era at which this structure type unlocks.</summary>
        public Era Era => _era;

        /// <summary>The function this structure performs once operational.</summary>
        public StructureFunction Function => _function;

        /// <summary>True only for the Peace Arch wonder.</summary>
        public bool IsPeaceArch => _isPeaceArch;

        /// <summary>
        /// The authored Visual_Detail_Tier override, or <see cref="UnsetVisualDetailTier"/> (-1) when the
        /// content author left it unset so the Entity_View_System derives it from <see cref="Era"/> (Req 7.4).
        /// </summary>
        public int VisualDetailTier => _visualDetailTier;

        /// <summary>Converts this authored asset into its engine-free <see cref="StructureDef"/>.</summary>
        public StructureDef ToCore()
        {
            return new StructureDef(
                _id,
                _era,
                _cost != null ? _cost.ToCore() : ResourceCost.Free,
                _buildTimeSeconds,
                _populationCost,
                _maxHealth,
                _footprint.x,
                _footprint.y,
                _function,
                _isPeaceArch,
                visualDetailTier: _visualDetailTier);
        }
    }
}
