using UnityEngine;
using EpochWar.Core.State;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// The presentation adapter for a single core <see cref="UnitInstance"/> (task 15.1, Req 8.3).
    ///
    /// Following the "authoritative simulation, dumb presentation" principle, this MonoBehaviour holds
    /// no gameplay state: it references the authoritative <see cref="UnitInstance"/> owned by the
    /// core and mirrors it onto the scene GameObject each time <see cref="SyncFromCore"/> is called
    /// (driven by the <see cref="EntityViewManager"/> after every simulation tick). It converts the
    /// core's engine-free <see cref="WorldPosition"/> (fixed-point) into a <see cref="Vector3"/> at
    /// the boundary and exposes health as a 0..1 fraction for health-bar/VFX consumers. It never
    /// writes back to the core.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitView : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional detail variants, ordered by ascending Visual_Detail_Tier (index 0 = tier 1). "
            + "When populated, the variant matching the assigned tier is activated and the rest deactivated.")]
        private GameObject[] _detailTierVariants = System.Array.Empty<GameObject>();

        /// <summary>The authoritative core unit this view mirrors; null until bound.</summary>
        public UnitInstance Model { get; private set; }

        /// <summary>The core unit id this view represents, or -1 when unbound.</summary>
        public int UnitId => Model?.Id ?? -1;

        /// <summary>
        /// The Visual_Detail_Tier the Entity_View_System resolved for this view (Req 7), or -1 until
        /// assigned. Presentation-only; a richer tier indicates a richer visual representation (Req 7.1).
        /// </summary>
        public int VisualDetailTier { get; private set; } = -1;

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

        /// <summary>Binds this view to a core unit and snaps it to the unit's current state.</summary>
        public void Bind(UnitInstance model)
        {
            Model = model;
            SyncFromCore();
        }

        /// <summary>
        /// Mirrors the bound core unit onto this GameObject: positions the transform at the unit's
        /// world position. Safe to call every frame or every tick; does nothing when unbound.
        /// </summary>
        public void SyncFromCore()
        {
            if (Model == null)
            {
                return;
            }

            transform.localPosition = ToVector3(Model.Position);
        }

        /// <summary>Converts an engine-free fixed-point <see cref="WorldPosition"/> to a Unity vector.</summary>
        public static Vector3 ToVector3(WorldPosition p)
            => new Vector3(p.ToFloatX(), p.ToFloatY(), p.ToFloatZ());

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

            // Variants are ordered by ascending tier (index 0 == tier 1). Clamp so any tier maps onto an
            // authored variant rather than leaving the view blank.
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
