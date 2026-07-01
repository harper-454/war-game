using System.Collections.Generic;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Unity.Content
{
    /// <summary>
    /// A hand-authored, engine-free starter catalog covering all nine <see cref="Era"/> stages with a
    /// small, balanced-enough set of Technologies, Units, Structures, Resources, and Governance
    /// options — enough real content to play a full Match end to end without any hand-authored
    /// <c>.asset</c> files (Req 1.5, 2.1, 3.6, 4.6).
    ///
    /// This is the code counterpart of a <see cref="ContentDatabase"/>: it builds the same
    /// <see cref="InMemoryCatalog"/> the ScriptableObject path produces, but purely from
    /// <c>EpochWar.Core</c> POCOs so it can be used from tests, headless hosts, and quick-start
    /// scenes alike. It deliberately depends on nothing in <c>UnityEngine</c>.
    ///
    /// The content wires up every special victory path so all three are reachable:
    /// <list type="bullet">
    ///   <item>a Doomsday_Weapon technology (<see cref="DoomsdayTechId"/>, Futuristic era) with a
    ///   deployment cost, for the Annihilation path (Req 9.1, 9.2);</item>
    ///   <item>a Peace_Arch wonder (<see cref="PeaceArchStructureId"/>) gated by a
    ///   <see cref="TechCategory.PeaceArchPrereq"/> technology, for the Peace path (Req 10.1);</item>
    ///   <item>a Colony_Ship unit (<see cref="ColonyShipUnitId"/>, Space era) with a launch cost, for
    ///   the Ascension path (Req 11.1, 11.2).</item>
    /// </list>
    /// Ids are exposed as constants so bootstrap/UI code and tests can reference the special content
    /// without magic strings.
    /// </summary>
    public static class ContentSeed
    {
        // ---- Well-known ids for the special victory-path content ----
        public const string DoomsdayTechId = "tech_doomsday";
        public const string PeaceArchPrereqTechId = "tech_peace_doctrine";
        public const string ColonyShipTechId = "tech_colonization";
        public const string PeaceArchStructureId = "struct_peace_arch";
        public const string ColonyShipUnitId = "unit_colony_ship";

        /// <summary>
        /// The <see cref="CellMaterial"/> every seeded battlefield cell starts as (Req 3.1). The scene
        /// bootstrap (<c>MatchSceneController</c>) fills the <see cref="TerrainVolume"/> with this value,
        /// and it is the terrain type the <c>TerrainRenderer</c> renders with its assigned Cell_Material.
        /// </summary>
        public const CellMaterial DefaultTerrainFill = CellMaterial.Soil;

        /// <summary>
        /// The terrain type whose Cell_Material the <c>TerrainRenderer</c> substitutes for any solid
        /// terrain type left without an assigned material (Req 3.5). Kept as the standard ground material
        /// so an unauthored terrain type is never rendered as a missing/magenta surface.
        /// </summary>
        public const CellMaterial FallbackTerrainType = CellMaterial.Soil;

        /// <summary>
        /// Builds the standard starter <see cref="ICatalog"/>. The returned catalog is immutable and
        /// safe to share across the Host simulation and tests.
        /// </summary>
        public static ICatalog BuildStandardCatalog()
        {
            return new InMemoryCatalog(
                BuildTechnologies(),
                BuildUnits(),
                BuildStructures(),
                BuildResources(),
                BuildEras(),
                BuildGovernances());
        }

        // ==================================================================
        // Terrain palette (Req 3.1) — the Cell_Material set the standard Match seeds
        // ==================================================================

        /// <summary>
        /// The ordered set of solid terrain <see cref="CellMaterial"/> types the standard Match uses,
        /// i.e. the "authored Cell_Material references per seeded terrain type" (Req 3.1). It is engine-free
        /// (a Core enum, no <c>UnityEngine.Material</c>) and deliberately mirrors the
        /// <c>TerrainRenderer</c>'s solid-material submesh order and its serialized Cell_Material table so
        /// authoring the two stays in lock-step: every terrain type listed here must have a matching
        /// URP-lit material entry on the <c>TerrainRenderer</c> component (see <c>VFX_SETUP.md</c>), and any
        /// omitted entry renders with the <see cref="FallbackTerrainType"/> material without failing the
        /// chunk rebuild (Req 3.5). <see cref="CellMaterial.Empty"/> is excluded because a carved-out cell
        /// is not rendered.
        /// </summary>
        public static IReadOnlyList<CellMaterial> BuildStandardTerrainPalette() => new[]
        {
            CellMaterial.Soil,
            CellMaterial.Rock,
            CellMaterial.Sand,
            CellMaterial.Reinforced,
        };

        // ==================================================================
        // Resources (Req 2.1, 1.5)
        // ==================================================================

        private static List<ResourceDef> BuildResources() => new List<ResourceDef>
        {
            new ResourceDef(ResourceType.Food, "Food", Era.Prehistoric, 500f),
            new ResourceDef(ResourceType.Wood, "Wood", Era.Prehistoric, 500f),
            new ResourceDef(ResourceType.Stone, "Stone", Era.Ancient, 500f),
            new ResourceDef(ResourceType.Metal, "Metal", Era.Classical, 500f),
            new ResourceDef(ResourceType.Energy, "Energy", Era.Industrial, 1000f),
            // Research is uncapped (capacity <= 0) so long-term tech investment is never wasted.
            new ResourceDef(ResourceType.Research, "Research", Era.Prehistoric, 0f),
            new ResourceDef(ResourceType.ExoticMatter, "Exotic Matter", Era.Space, 200f),
        };

        // ==================================================================
        // Eras (Req 1.1, 1.4) — RequiredTechIds gate advancement INTO each era
        // ==================================================================

        private static List<EraDef> BuildEras() => new List<EraDef>
        {
            new EraDef(Era.Prehistoric, "Prehistoric Era"),
            new EraDef(Era.Ancient, "Ancient Era", new[] { "tech_agriculture" }),
            new EraDef(Era.Classical, "Classical Era", new[] { "tech_bronze_working" }),
            new EraDef(Era.Medieval, "Medieval Era", new[] { "tech_masonry" }),
            new EraDef(Era.Industrial, "Industrial Era", new[] { "tech_gunpowder" }),
            new EraDef(Era.Modern, "Modern Era", new[] { "tech_steam_power" }),
            new EraDef(Era.Information, "Information Era", new[] { "tech_electronics" }),
            new EraDef(Era.Futuristic, "Futuristic Era", new[] { "tech_computing" }),
            new EraDef(Era.Space, "Space Era", new[] { "tech_fusion" }),
        };

        // ==================================================================
        // Technologies (Req 1) — gateway techs plus the three special-path techs
        // ==================================================================

        private static List<TechnologyDef> BuildTechnologies() => new List<TechnologyDef>
        {
            // --- Gateway techs: each is researchable in one era and required to enter the next. ---
            Tech("tech_agriculture", Era.Prehistoric, 20f,
                unlocksStructures: new[] { "struct_granary" }),
            Tech("tech_bronze_working", Era.Ancient, 30f,
                prerequisites: new[] { "tech_agriculture" },
                unlocksUnits: new[] { "unit_spearman" }),
            Tech("tech_masonry", Era.Classical, 40f,
                prerequisites: new[] { "tech_bronze_working" },
                unlocksStructures: new[] { "struct_library" }),
            Tech("tech_gunpowder", Era.Medieval, 60f,
                prerequisites: new[] { "tech_masonry" },
                unlocksUnits: new[] { "unit_rifleman" }),
            Tech("tech_steam_power", Era.Industrial, 90f,
                prerequisites: new[] { "tech_gunpowder" },
                unlocksStructures: new[] { "struct_factory" }),
            Tech("tech_electronics", Era.Modern, 120f,
                prerequisites: new[] { "tech_steam_power" },
                unlocksUnits: new[] { "unit_drone" }),
            Tech("tech_computing", Era.Information, 160f,
                prerequisites: new[] { "tech_electronics" },
                unlocksStructures: new[] { "struct_datacenter" }),
            Tech("tech_fusion", Era.Futuristic, 200f,
                prerequisites: new[] { "tech_computing" },
                unlocksStructures: new[] { "struct_fusion_reactor" }),

            // --- Peace path: completing this makes the Peace_Arch placeable (Req 10.1). ---
            Tech(PeaceArchPrereqTechId, Era.Modern, 150f,
                prerequisites: new[] { "tech_electronics" },
                unlocksStructures: new[] { PeaceArchStructureId },
                category: TechCategory.PeaceArchPrereq),

            // --- Annihilation path: an Era-gated Doomsday weapon with a separate deployment cost. ---
            Tech(DoomsdayTechId, Era.Futuristic, 250f,
                prerequisites: new[] { "tech_fusion" },
                category: TechCategory.DoomsdayWeapon,
                deploymentCost: ResourceCost.Of(
                    (ResourceType.ExoticMatter, 50f), (ResourceType.Energy, 100f))),

            // --- Ascension path: unlocks the Colony_Ship, only researchable in the Space era. ---
            Tech(ColonyShipTechId, Era.Space, 300f,
                prerequisites: new[] { "tech_fusion" },
                unlocksUnits: new[] { ColonyShipUnitId },
                category: TechCategory.ColonyShip),
        };

        // ==================================================================
        // Units (Req 3.1, 3.6, 3.7) — a representative unit or two per era
        // ==================================================================
        //
        // Each entry authors an explicit Visual_Detail_Tier (Req 7.4) via the constructor's
        // visualDetailTier argument. The seeded values follow the same non-decreasing-by-Era ordering
        // the Entity_View_System's Era-derived default uses (tier == Era ordinal + 1, so Prehistoric = 1
        // through Space = 9), so same-Era units share a tier and later-Era units are never lower (Req 7.1,
        // 7.2, 7.3). They are authored here as overrides so the content path (asset -> UnitDef) is exercised
        // end to end rather than relying on the fallback.

        private static List<UnitDef> BuildUnits() => new List<UnitDef>
        {
            // Prehistoric
            new UnitDef("unit_gatherer", Era.Prehistoric,
                ResourceCost.Of((ResourceType.Food, 20f)), 3f, 1, 8, 1, 0, 2f, UnitRole.Worker,
                visualDetailTier: 1),
            new UnitDef("unit_warrior", Era.Prehistoric,
                ResourceCost.Of((ResourceType.Food, 30f), (ResourceType.Wood, 10f)),
                4f, 1, 20, 5, 2, 2f, UnitRole.Soldier,
                visualDetailTier: 1),

            // Ancient
            new UnitDef("unit_spearman", Era.Ancient,
                ResourceCost.Of((ResourceType.Food, 30f), (ResourceType.Wood, 20f)),
                5f, 1, 28, 7, 4, 2f, UnitRole.Soldier,
                visualDetailTier: 2),

            // Classical
            new UnitDef("unit_legionary", Era.Classical,
                ResourceCost.Of((ResourceType.Food, 40f), (ResourceType.Metal, 20f)),
                6f, 1, 40, 10, 6, 2f, UnitRole.Soldier,
                visualDetailTier: 3),

            // Medieval
            new UnitDef("unit_knight", Era.Medieval,
                ResourceCost.Of((ResourceType.Food, 50f), (ResourceType.Metal, 40f)),
                7f, 2, 60, 16, 10, 3f, UnitRole.Vehicle,
                visualDetailTier: 4),

            // Industrial
            new UnitDef("unit_rifleman", Era.Industrial,
                ResourceCost.Of((ResourceType.Metal, 40f), (ResourceType.Energy, 10f)),
                6f, 1, 55, 22, 8, 3f, UnitRole.Soldier,
                visualDetailTier: 5),

            // Modern
            new UnitDef("unit_tank", Era.Modern,
                ResourceCost.Of((ResourceType.Metal, 80f), (ResourceType.Energy, 40f)),
                9f, 2, 120, 40, 25, 4f, UnitRole.Vehicle,
                visualDetailTier: 6),

            // Information
            new UnitDef("unit_drone", Era.Information,
                ResourceCost.Of((ResourceType.Metal, 60f), (ResourceType.Energy, 60f)),
                7f, 1, 60, 35, 10, 6f, UnitRole.Aircraft,
                visualDetailTier: 7),

            // Futuristic
            new UnitDef("unit_mech", Era.Futuristic,
                ResourceCost.Of((ResourceType.Metal, 120f), (ResourceType.Energy, 100f)),
                12f, 3, 200, 70, 45, 4f, UnitRole.Vehicle,
                visualDetailTier: 8),

            // Space
            new UnitDef("unit_star_trooper", Era.Space,
                ResourceCost.Of((ResourceType.Metal, 90f), (ResourceType.Energy, 80f)),
                8f, 1, 90, 55, 30, 5f, UnitRole.Soldier,
                visualDetailTier: 9),

            // Space — Colony Ship: the Ascension vehicle. Recruited with a heavy resource cost, then
            // LAUNCHED for an Exotic Matter cost to begin the colonization sequence (Req 11.2).
            new UnitDef(ColonyShipUnitId, Era.Space,
                ResourceCost.Of((ResourceType.Metal, 300f), (ResourceType.Energy, 300f), (ResourceType.ExoticMatter, 100f)),
                30f, 5, 300, 0, 40, 2f, UnitRole.ColonyShip,
                launchCost: ResourceCost.Of((ResourceType.ExoticMatter, 100f)),
                visualDetailTier: 9),
        };

        // ==================================================================
        // Structures (Req 4) — a representative structure per era + the Peace Arch
        // ==================================================================
        //
        // As with units, each entry authors an explicit Visual_Detail_Tier (Req 7.4) that follows the
        // Entity_View_System's non-decreasing-by-Era ordering (tier == Era ordinal + 1), so same-Era
        // structures share a tier and later-Era structures are never lower (Req 7.1, 7.2, 7.3).

        private static List<StructureDef> BuildStructures() => new List<StructureDef>
        {
            // Prehistoric
            new StructureDef("struct_gathering_camp", Era.Prehistoric,
                ResourceCost.Of((ResourceType.Wood, 30f)), 5f, 0, 80, 1, 1,
                StructureFunction.ResourceExtractor,
                visualDetailTier: 1),
            new StructureDef("struct_war_camp", Era.Prehistoric,
                ResourceCost.Of((ResourceType.Wood, 50f), (ResourceType.Food, 20f)), 8f, 0, 120, 2, 2,
                StructureFunction.Barracks,
                visualDetailTier: 1),

            // Ancient
            new StructureDef("struct_granary", Era.Ancient,
                ResourceCost.Of((ResourceType.Wood, 60f), (ResourceType.Stone, 20f)), 8f, 0, 150, 2, 2,
                StructureFunction.ResourceExtractor,
                visualDetailTier: 2),

            // Classical
            new StructureDef("struct_library", Era.Classical,
                ResourceCost.Of((ResourceType.Stone, 60f), (ResourceType.Wood, 40f)), 10f, 0, 140, 2, 2,
                StructureFunction.ResearchLab,
                visualDetailTier: 3),

            // Medieval
            new StructureDef("struct_castle", Era.Medieval,
                ResourceCost.Of((ResourceType.Stone, 150f), (ResourceType.Metal, 60f)), 18f, 0, 400, 3, 3,
                StructureFunction.Defense,
                visualDetailTier: 4),

            // Industrial
            new StructureDef("struct_factory", Era.Industrial,
                ResourceCost.Of((ResourceType.Metal, 120f), (ResourceType.Stone, 60f)), 15f, 0, 300, 3, 3,
                StructureFunction.ResourceExtractor,
                visualDetailTier: 5),

            // Modern
            new StructureDef("struct_research_lab", Era.Modern,
                ResourceCost.Of((ResourceType.Metal, 100f), (ResourceType.Energy, 60f)), 14f, 0, 220, 2, 2,
                StructureFunction.ResearchLab,
                visualDetailTier: 6),

            // Information
            new StructureDef("struct_datacenter", Era.Information,
                ResourceCost.Of((ResourceType.Metal, 140f), (ResourceType.Energy, 120f)), 16f, 0, 260, 3, 3,
                StructureFunction.ResearchLab,
                visualDetailTier: 7),

            // Futuristic
            new StructureDef("struct_fusion_reactor", Era.Futuristic,
                ResourceCost.Of((ResourceType.Metal, 200f), (ResourceType.Energy, 100f)), 20f, 0, 350, 3, 3,
                StructureFunction.ResourceExtractor,
                visualDetailTier: 8),

            // Space
            new StructureDef("struct_spaceport", Era.Space,
                ResourceCost.Of((ResourceType.Metal, 250f), (ResourceType.Energy, 200f)), 22f, 0, 400, 4, 4,
                StructureFunction.Barracks,
                visualDetailTier: 9),

            // Peace Arch wonder — availability is gated by the PeaceArchPrereq tech, not by era unlock
            // alone (Req 10.1). Tagged Modern so it sits alongside its prerequisite tech.
            new StructureDef(PeaceArchStructureId, Era.Modern,
                ResourceCost.Of((ResourceType.Stone, 400f), (ResourceType.Metal, 300f), (ResourceType.Energy, 200f)),
                60f, 0, 500, 3, 3,
                StructureFunction.Wonder,
                isPeaceArch: true,
                visualDetailTier: 6),
        };

        // ==================================================================
        // Governance options (Req 5.5)
        // ==================================================================

        private static List<GovernanceOption> BuildGovernances() => new List<GovernanceOption>
        {
            new GovernanceOption("gov_tribalism", "Tribalism",
                new Dictionary<ResourceType, float> { { ResourceType.Food, 1.2f } }),
            new GovernanceOption("gov_monarchy", "Monarchy",
                unitAttackMultiplier: 1.15f),
            new GovernanceOption("gov_democracy", "Democracy",
                new Dictionary<ResourceType, float> { { ResourceType.Research, 1.25f } }),
            new GovernanceOption("gov_militarism", "Militarism",
                new Dictionary<ResourceType, float> { { ResourceType.Food, 0.9f } },
                unitAttackMultiplier: 1.2f,
                unitDefenseMultiplier: 1.1f),
        };

        // ==================================================================
        // Local builder helpers
        // ==================================================================

        private static TechnologyDef Tech(
            string id,
            Era era,
            float researchCost,
            IEnumerable<string> prerequisites = null,
            IEnumerable<string> unlocksUnits = null,
            IEnumerable<string> unlocksStructures = null,
            IEnumerable<ResourceType> unlocksResources = null,
            TechCategory category = TechCategory.Normal,
            ResourceCost deploymentCost = default)
        {
            return new TechnologyDef(
                id,
                era,
                researchCost > 0f ? ResourceCost.Single(ResourceType.Research, researchCost) : ResourceCost.Free,
                prerequisites,
                unlocksUnits,
                unlocksStructures,
                unlocksResources,
                category,
                deploymentCost);
        }
    }
}
