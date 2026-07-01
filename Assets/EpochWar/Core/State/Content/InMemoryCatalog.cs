using System;
using System.Collections.Generic;
using System.Linq;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// A straightforward in-memory <see cref="ICatalog"/> built from collections of POCO
    /// definitions.
    ///
    /// All indexes (by id, by Era) are computed once at construction, so every lookup is O(1)
    /// or a precomputed list. The catalog is immutable after construction: it holds only the
    /// authored, read-only definitions and never mutable match state, which makes it safe to
    /// share across the Host simulation and EditMode tests.
    ///
    /// Tests build a catalog directly from POCOs; the Unity content layer (task 15.3) builds
    /// one by converting authored ScriptableObjects into these POCOs.
    /// </summary>
    public sealed class InMemoryCatalog : ICatalog
    {
        private readonly Dictionary<string, TechnologyDef> _techsById;
        private readonly Dictionary<string, UnitDef> _unitsById;
        private readonly Dictionary<string, StructureDef> _structuresById;
        private readonly Dictionary<string, GovernanceOption> _governancesById;
        private readonly Dictionary<ResourceType, ResourceDef> _resourcesByType;
        private readonly Dictionary<Era, EraDef> _erasByEra;

        private readonly Dictionary<Era, List<TechnologyDef>> _techsByEra;
        private readonly Dictionary<Era, List<UnitDef>> _unitsByEra;
        private readonly Dictionary<Era, List<StructureDef>> _structuresByEra;
        private readonly Dictionary<Era, List<ResourceDef>> _resourcesByEra;

        public InMemoryCatalog(
            IEnumerable<TechnologyDef> technologies = null,
            IEnumerable<UnitDef> units = null,
            IEnumerable<StructureDef> structures = null,
            IEnumerable<ResourceDef> resources = null,
            IEnumerable<EraDef> eras = null,
            IEnumerable<GovernanceOption> governances = null)
        {
            var techList = (technologies ?? Enumerable.Empty<TechnologyDef>()).ToList();
            var unitList = (units ?? Enumerable.Empty<UnitDef>()).ToList();
            var structureList = (structures ?? Enumerable.Empty<StructureDef>()).ToList();
            var resourceList = (resources ?? Enumerable.Empty<ResourceDef>()).ToList();
            var eraList = (eras ?? Enumerable.Empty<EraDef>()).ToList();
            var governanceList = (governances ?? Enumerable.Empty<GovernanceOption>()).ToList();

            _techsById = BuildIdIndex(techList, d => d.Id, nameof(TechnologyDef));
            _unitsById = BuildIdIndex(unitList, d => d.Id, nameof(UnitDef));
            _structuresById = BuildIdIndex(structureList, d => d.Id, nameof(StructureDef));
            _governancesById = BuildIdIndex(governanceList, d => d.Id, nameof(GovernanceOption));

            _resourcesByType = new Dictionary<ResourceType, ResourceDef>();
            foreach (var def in resourceList)
            {
                if (_resourcesByType.ContainsKey(def.Type))
                {
                    throw new ArgumentException($"Duplicate ResourceDef for type '{def.Type}'.");
                }

                _resourcesByType[def.Type] = def;
            }

            _erasByEra = new Dictionary<Era, EraDef>();
            foreach (var def in eraList)
            {
                if (_erasByEra.ContainsKey(def.Era))
                {
                    throw new ArgumentException($"Duplicate EraDef for era '{def.Era}'.");
                }

                _erasByEra[def.Era] = def;
            }

            _techsByEra = GroupByEra(techList, d => d.Era);
            _unitsByEra = GroupByEra(unitList, d => d.Era);
            _structuresByEra = GroupByEra(structureList, d => d.Era);
            _resourcesByEra = GroupByEra(resourceList, d => d.Era);

            Technologies = techList;
            Units = unitList;
            Structures = structureList;
            Resources = resourceList;
            Eras = eraList;
            Governances = governanceList;
        }

        public IReadOnlyCollection<TechnologyDef> Technologies { get; }
        public IReadOnlyCollection<UnitDef> Units { get; }
        public IReadOnlyCollection<StructureDef> Structures { get; }
        public IReadOnlyCollection<ResourceDef> Resources { get; }
        public IReadOnlyCollection<EraDef> Eras { get; }
        public IReadOnlyCollection<GovernanceOption> Governances { get; }

        public TechnologyDef GetTechnology(string id) => Lookup(_techsById, id);
        public UnitDef GetUnit(string id) => Lookup(_unitsById, id);
        public StructureDef GetStructure(string id) => Lookup(_structuresById, id);
        public GovernanceOption GetGovernance(string id) => Lookup(_governancesById, id);

        public ResourceDef GetResource(ResourceType type)
            => _resourcesByType.TryGetValue(type, out var def) ? def : null;

        public EraDef GetEra(Era era)
            => _erasByEra.TryGetValue(era, out var def) ? def : null;

        public bool TryGetTechnology(string id, out TechnologyDef def) => TryLookup(_techsById, id, out def);
        public bool TryGetUnit(string id, out UnitDef def) => TryLookup(_unitsById, id, out def);
        public bool TryGetStructure(string id, out StructureDef def) => TryLookup(_structuresById, id, out def);
        public bool TryGetGovernance(string id, out GovernanceOption def) => TryLookup(_governancesById, id, out def);

        public IReadOnlyList<TechnologyDef> TechnologiesAt(Era era) => AtEra(_techsByEra, era);
        public IReadOnlyList<UnitDef> UnitsAt(Era era) => AtEra(_unitsByEra, era);
        public IReadOnlyList<StructureDef> StructuresAt(Era era) => AtEra(_structuresByEra, era);
        public IReadOnlyList<ResourceDef> ResourcesAt(Era era) => AtEra(_resourcesByEra, era);

        public IReadOnlyList<UnitDef> UnitsUpTo(Era era) => UpTo(_unitsByEra, era);
        public IReadOnlyList<StructureDef> StructuresUpTo(Era era) => UpTo(_structuresByEra, era);
        public IReadOnlyList<ResourceDef> ResourcesUpTo(Era era) => UpTo(_resourcesByEra, era);

        // ---- helpers ----

        private static Dictionary<string, T> BuildIdIndex<T>(
            IEnumerable<T> items, Func<T, string> idSelector, string typeName)
        {
            var map = new Dictionary<string, T>();
            foreach (var item in items)
            {
                string id = idSelector(item);
                if (string.IsNullOrEmpty(id))
                {
                    throw new ArgumentException($"{typeName} has a null or empty Id.");
                }

                if (map.ContainsKey(id))
                {
                    throw new ArgumentException($"Duplicate {typeName} Id '{id}'.");
                }

                map[id] = item;
            }

            return map;
        }

        private static Dictionary<Era, List<T>> GroupByEra<T>(IEnumerable<T> items, Func<T, Era> eraSelector)
        {
            var map = new Dictionary<Era, List<T>>();
            foreach (var item in items)
            {
                Era era = eraSelector(item);
                if (!map.TryGetValue(era, out var list))
                {
                    list = new List<T>();
                    map[era] = list;
                }

                list.Add(item);
            }

            return map;
        }

        private static T Lookup<T>(Dictionary<string, T> map, string id) where T : class
        {
            if (id != null && map.TryGetValue(id, out var def))
            {
                return def;
            }

            return null;
        }

        private static bool TryLookup<T>(Dictionary<string, T> map, string id, out T def)
        {
            if (id != null)
            {
                return map.TryGetValue(id, out def);
            }

            def = default;
            return false;
        }

        private static IReadOnlyList<T> AtEra<T>(Dictionary<Era, List<T>> byEra, Era era)
            => byEra.TryGetValue(era, out var list) ? list : (IReadOnlyList<T>)Array.Empty<T>();

        private static IReadOnlyList<T> UpTo<T>(Dictionary<Era, List<T>> byEra, Era era)
        {
            var result = new List<T>();
            foreach (var kvp in byEra)
            {
                if (kvp.Key <= era)
                {
                    result.AddRange(kvp.Value);
                }
            }

            return result;
        }
    }
}
