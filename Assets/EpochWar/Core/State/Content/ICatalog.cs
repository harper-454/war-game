using System.Collections.Generic;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// The read-only lookup abstraction over all authored content definitions (Req 1.5, 3.6,
    /// 9.1, 10.1, 11.1).
    ///
    /// Systems in <see cref="EpochWar.Core"/> resolve definitions through this interface — by
    /// id and by Era — instead of holding Unity object references, which keeps the simulation
    /// engine-free and unit-testable. The Unity content layer (task 15.3) builds an
    /// implementation (see <see cref="InMemoryCatalog"/>) from authored ScriptableObjects, while
    /// tests construct one directly from POCOs.
    ///
    /// "By Era" lookups return the definitions whose <c>Era</c> tag equals the requested Era
    /// (the content that becomes newly available at that Era). Cumulative "available up to and
    /// including an Era" queries are provided as helpers for the unlock/availability rules
    /// (Req 1.5, 4.6).
    /// </summary>
    public interface ICatalog
    {
        // ---- Lookup by id (returns null when absent) ----

        TechnologyDef GetTechnology(string id);
        UnitDef GetUnit(string id);
        StructureDef GetStructure(string id);
        GovernanceOption GetGovernance(string id);

        /// <summary>Returns the resource definition for <paramref name="type"/>, or null if undefined.</summary>
        ResourceDef GetResource(ResourceType type);

        /// <summary>Returns the era definition for <paramref name="era"/>, or null if undefined.</summary>
        EraDef GetEra(Era era);

        // ---- Try-style lookup by id ----

        bool TryGetTechnology(string id, out TechnologyDef def);
        bool TryGetUnit(string id, out UnitDef def);
        bool TryGetStructure(string id, out StructureDef def);
        bool TryGetGovernance(string id, out GovernanceOption def);

        // ---- Full collections ----

        IReadOnlyCollection<TechnologyDef> Technologies { get; }
        IReadOnlyCollection<UnitDef> Units { get; }
        IReadOnlyCollection<StructureDef> Structures { get; }
        IReadOnlyCollection<ResourceDef> Resources { get; }
        IReadOnlyCollection<EraDef> Eras { get; }
        IReadOnlyCollection<GovernanceOption> Governances { get; }

        // ---- Lookup by Era (definitions tagged with exactly that Era) ----

        IReadOnlyList<TechnologyDef> TechnologiesAt(Era era);
        IReadOnlyList<UnitDef> UnitsAt(Era era);
        IReadOnlyList<StructureDef> StructuresAt(Era era);
        IReadOnlyList<ResourceDef> ResourcesAt(Era era);

        // ---- Cumulative availability (this Era and all earlier Eras) ----

        /// <summary>All Unit types whose Era is &lt;= <paramref name="era"/> (Req 1.5).</summary>
        IReadOnlyList<UnitDef> UnitsUpTo(Era era);

        /// <summary>All Structure types whose Era is &lt;= <paramref name="era"/> (Req 1.5, 4.6).</summary>
        IReadOnlyList<StructureDef> StructuresUpTo(Era era);

        /// <summary>All Resource types whose Era is &lt;= <paramref name="era"/> (Req 1.5).</summary>
        IReadOnlyList<ResourceDef> ResourcesUpTo(Era era);
    }
}
