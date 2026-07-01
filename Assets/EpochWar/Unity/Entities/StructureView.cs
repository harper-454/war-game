using UnityEngine;
using EpochWar.Core.State;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// The presentation adapter for a single core <see cref="StructureInstance"/> (task 15.1, Req 8.3).
    ///
    /// Like <see cref="UnitView"/>, this MonoBehaviour is a "dumb" mirror of authoritative core state:
    /// it references the <see cref="StructureInstance"/> owned by the core and, on
    /// <see cref="SyncFromCore"/> (driven by the <see cref="EntityViewManager"/> after every tick),
    /// positions the GameObject at the structure's terrain <see cref="StructureInstance.Origin"/> cell
    /// and surfaces derived display values — construction progress and operational state — so visuals
    /// (scaffolding vs. finished, disabled-function tint) can react without touching the core. The
    /// structure's footprint origin cell maps directly to a world position (one cell == one world unit).
    /// It never writes back to the core.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StructureView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional object shown only while the structure is under construction (scaffolding).")]
        private GameObject _underConstructionVisual;

        [SerializeField]
        [Tooltip("Optional object shown only once the structure is operational.")]
        private GameObject _operationalVisual;

        [SerializeField]
        [Tooltip("Optional detail variants, ordered by ascending Visual_Detail_Tier (index 0 = tier 1). "
            + "When populated, the variant matching the assigned tier is activated and the rest deactivated.")]
        private GameObject[] _detailTierVariants = System.Array.Empty<GameObject>();

        /// <summary>The authoritative core structure this view mirrors; null until bound.</summary>
        public StructureInstance Model { get; private set; }

        /// <summary>The core structure id this view represents, or -1 when unbound.</summary>
        public int StructureId => Model?.Id ?? -1;

        /// <summary>
        /// The Visual_Detail_Tier the Entity_View_System resolved for this view (Req 7), or -1 until
        /// assigned. Presentation-only; a richer tier indicates a richer visual representation (Req 7.1).
        /// </summary>
        public int VisualDetailTier { get; private set; } = -1;

        /// <summary>True once the bound structure has finished construction (Req 4.3).</summary>
        public bool IsOperational => Model?.IsOperational ?? false;

        /// <summary>Construction progress as a fraction in [0, 1]; 1 once operational.</summary>
        public float ConstructionFraction
        {
            get
            {
                if (Model?.Def == null)
                {
                    return 0f;
                }

                if (Model.IsOperational)
                {
                    return 1f;
                }

                float buildTime = Model.Def.BuildTimeSeconds;
                if (buildTime <= 0f)
                {
                    return 1f;
                }

                return Mathf.Clamp01(Model.ConstructionProgress / buildTime);
            }
        }

        /// <summary>Current health as a fraction of max health in [0, 1]; 0 when unbound.</summary>
        public float HealthFraction
        {
            get
            {
                if (Model?.Def == null || Model.Def.MaxHealth <= 0)
                {
                    return 0f;
                }

                return Mathf.Clamp01((float)Model.Health / Model.Def.MaxHealth);
            }
        }

        /// <summary>Binds this view to a core structure and snaps it to the structure's current state.</summary>
        public void Bind(StructureInstance model)
        {
            Model = model;
            SyncFromCore();
        }

        /// <summary>
        /// Mirrors the bound core structure onto this GameObject: positions it at its origin cell and
        /// toggles the construction/operational visuals to match its build state. Safe to call every
        /// tick; does nothing when unbound.
        /// </summary>
        public void SyncFromCore()
        {
            if (Model == null)
            {
                return;
            }

            transform.localPosition = ToVector3(Model.Origin);

            bool operational = Model.IsOperational;
            if (_underConstructionVisual != null && _underConstructionVisual.activeSelf == operational)
            {
                _underConstructionVisual.SetActive(!operational);
            }

            if (_operationalVisual != null && _operationalVisual.activeSelf != operational)
            {
                _operationalVisual.SetActive(operational);
            }
        }

        /// <summary>Converts a terrain <see cref="CellCoord"/> to a world position (one cell per unit).</summary>
        public static Vector3 ToVector3(CellCoord c) => new Vector3(c.X, c.Y, c.Z);

        /// <summary>
        /// Records the Visual_Detail_Tier the Entity_View_System assigned to this view (Req 7.1) and, when
        /// tier-specific detail variants are authored, activates the one matching <paramref name="tier"/>
        /// (clamped to the authored range) and deactivates the rest. Safe to call repeatedly; a missing or
        /// empty variant list simply records the tier without changing any child (graceful degradation).
        /// </summary>
        public void SetVisualDetailTier(int tier)
        {
            VisualDetailTier = tier;

            if (_detailTierVariants == null || _detailTierVariants.Length == 0)
            {
                return;
            }

            int index = Mathf.Clamp(tier - 1, 0, _detailTierVariants.Length - 1);
            for (int i = 0; i < _detailTierVariants.Length; i++)
            {
                GameObject variant = _detailTierVariants[i];
                if (variant != null && variant.activeSelf != (i == index))
                {
                    variant.SetActive(i == index);
                }
            }
        }
    }
}
