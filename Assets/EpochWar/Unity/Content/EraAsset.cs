using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject authoring wrapper for an <see cref="EraDef"/> (Req 1.4).
    ///
    /// Content authors create one asset per <see cref="State.Era"/> stage and list the
    /// <see cref="TechnologyAsset"/>s that must be completed before a Nation may advance <em>into</em>
    /// that Era. <see cref="ToCore"/> flattens those asset references to their string ids and produces
    /// the engine-free <see cref="EraDef"/> the Tech_System consumes; the Prehistoric Era normally has
    /// no requirements.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Era", fileName = "Era")]
    public sealed class EraAsset : ScriptableObject
    {
        [Tooltip("The era stage this asset describes; also the catalog key.")]
        [SerializeField] private Era _era = Era.Prehistoric;

        [Tooltip("Human-readable name shown in the HUD.")]
        [SerializeField] private string _displayName = string.Empty;

        [Tooltip("Technologies that must be completed to advance INTO this era. Empty for Prehistoric.")]
        [SerializeField] private List<TechnologyAsset> _requiredTechs = new List<TechnologyAsset>();

        /// <summary>The era stage this asset describes.</summary>
        public Era Era => _era;

        /// <summary>Human-readable display name for UI.</summary>
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _era.ToString() : _displayName;

        /// <summary>Converts this authored asset into its engine-free <see cref="EraDef"/>.</summary>
        public EraDef ToCore()
        {
            IEnumerable<string> requiredIds = (_requiredTechs ?? new List<TechnologyAsset>())
                .Where(t => t != null)
                .Select(t => t.Id);

            return new EraDef(_era, DisplayName, requiredIds);
        }
    }
}
