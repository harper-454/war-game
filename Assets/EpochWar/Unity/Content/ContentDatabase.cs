using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A ScriptableObject that assembles every authored content asset into a single engine-free
    /// <see cref="ICatalog"/> the simulation consumes (Req 1.5, 2.1, 3.6, 4.6).
    ///
    /// A designer drops all Era/Technology/Unit/Structure/Resource/Governance assets into the lists
    /// here, and <see cref="BuildCatalog"/> converts each to its Core POCO and constructs an
    /// <see cref="InMemoryCatalog"/>. Because the catalog is built from immutable POCOs, one instance
    /// can be shared safely across the Host simulation. Null entries are skipped so a partially filled
    /// database still builds; duplicate ids are rejected by <see cref="InMemoryCatalog"/> at build time.
    ///
    /// This is the single authored composition root for content; systems never see the ScriptableObjects.
    /// </summary>
    [CreateAssetMenu(menuName = "EpochWar/Content Database", fileName = "ContentDatabase")]
    public sealed class ContentDatabase : ScriptableObject
    {
        [Header("Era progression")]
        [SerializeField] private List<EraAsset> _eras = new List<EraAsset>();

        [Header("Catalog content")]
        [SerializeField] private List<TechnologyAsset> _technologies = new List<TechnologyAsset>();
        [SerializeField] private List<UnitAsset> _units = new List<UnitAsset>();
        [SerializeField] private List<StructureAsset> _structures = new List<StructureAsset>();
        [SerializeField] private List<ResourceAsset> _resources = new List<ResourceAsset>();
        [SerializeField] private List<GovernanceAsset> _governances = new List<GovernanceAsset>();

        /// <summary>
        /// Converts every authored asset to its engine-free POCO and assembles them into an immutable
        /// <see cref="InMemoryCatalog"/>. Null list entries are ignored; duplicate ids or resource/era
        /// keys cause <see cref="InMemoryCatalog"/> to throw so authoring errors surface immediately.
        /// </summary>
        public ICatalog BuildCatalog()
        {
            var eras = Convert(_eras, a => a.ToCore());
            var technologies = Convert(_technologies, a => a.ToCore());
            var units = Convert(_units, a => a.ToCore());
            var structures = Convert(_structures, a => a.ToCore());
            var resources = Convert(_resources, a => a.ToCore());
            var governances = Convert(_governances, a => a.ToCore());

            return new InMemoryCatalog(
                technologies,
                units,
                structures,
                resources,
                eras,
                governances);
        }

        private static List<TOut> Convert<TAsset, TOut>(List<TAsset> assets, System.Func<TAsset, TOut> convert)
            where TAsset : Object
        {
            var result = new List<TOut>();
            if (assets == null)
            {
                return result;
            }

            foreach (var asset in assets)
            {
                if (asset != null)
                {
                    result.Add(convert(asset));
                }
            }

            return result;
        }
    }
}
