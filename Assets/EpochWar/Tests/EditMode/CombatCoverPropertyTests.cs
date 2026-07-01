using System.Linq;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for the Cover_Bonus applied by <see cref="CombatSystem.ResolveAttack"/>
    /// (tasks 14.3/14.4), exercised for at least the design-mandated 100 generated iterations.
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 4/5/6</c>:
    /// <list type="bullet">
    ///   <item>Property 4 — Cover_Bonus tracks current position qualification (Req 10.1, 10.3, 10.4).</item>
    ///   <item>Property 5 — Structure-on-the-line grants a per-attack Cover_Bonus (Req 10.2).</item>
    ///   <item>Property 6 — Overlapping Cover bonuses take the greater, not the sum (Req 10.5).</item>
    /// </list>
    ///
    /// Every scenario keeps the attacker in the defender's front arc (defender faces +X, attacker is
    /// placed along +X) so no flanking bonus is applied and the only defense modifier under test is
    /// the cover bonus. With no governance adopted the resolved damage is exactly
    /// <c>ComputeDamage(attack, defense + coverBonus)</c>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class CombatCoverPropertyTests
    {
        private const int MinimumIterations = 100;

        // Tall enough that every generated elevation stays inside the volume (materials read as Soil).
        private static readonly Int3 Dims = new Int3(16, 32, 16);

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefOf(int attack, int defense, int maxHealth)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f, UnitRole.Soldier);

        private static (MatchState state, CombatSystem combat) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ);
            var terrain = new TerrainSystem();
            var combat = new CombatSystem(civ, units, terrain);
            // Soil everywhere: non-cover material, so terrain cover depends purely on elevation.
            var state = new MatchState(new TerrainVolume(Dims, CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            return (state, combat);
        }

        /// <summary>
        /// Resolves one front-arc attack and returns the applied damage. The defender sits at
        /// (0, defenderY, 0) facing +X; the attacker sits at (distance, attackerY, 0). An optional
        /// Structure is placed at the given X on the line of fire.
        /// </summary>
        private static int ResolveFrontAttack(
            int attack, int defense, int defenderY, int attackerY, int distance, int? structureX)
        {
            var (state, combat) = Build();

            var attacker = new UnitInstance(1, 1, UnitDefOf(attack, 0, 1000),
                WorldPosition.FromInts(distance, attackerY, 0));
            var defender = new UnitInstance(2, 2, UnitDefOf(0, defense, 1_000_000),
                WorldPosition.FromInts(0, defenderY, 0))
            {
                Facing = FacingDirection.FromDegrees(0), // faces +X, toward the attacker => Front
            };
            state.Units[1] = attacker;
            state.Units[2] = defender;

            if (structureX.HasValue)
            {
                var def = new StructureDef("s", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1,
                    StructureFunction.Defense);
                state.Structures[10] = new StructureInstance(10, 1, def, new CellCoord(structureX.Value, 0, 0));
            }

            var events = combat.ResolveAttack(state, 1, 2);
            return events.OfType<CombatResolvedEvent>().Single().Damage;
        }

        // ---- Property 4 -------------------------------------------------------------------------

        public sealed class ElevationScenario
        {
            public int Attack;
            public int Defense;
            public int DefenderY;
            public int AttackerY;
            public int Distance;

            public override string ToString()
                => $"ElevationScenario(atk={Attack}, def={Defense}, defY={DefenderY}, atkY={AttackerY}, d={Distance})";
        }

        private static Arbitrary<ElevationScenario> ElevationScenarios()
        {
            var gen = from attack in Gen.Choose(50, 400)
                      from defense in Gen.Choose(0, 20)
                      from defY in Gen.Choose(0, 12)
                      from atkY in Gen.Choose(0, 12)
                      from distance in Gen.Choose(1, 6)
                      select new ElevationScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          DefenderY = defY,
                          AttackerY = atkY,
                          Distance = distance,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 4: Cover_Bonus tracks current position qualification.
        ///
        /// The terrain/elevation Cover_Bonus is applied to the defender's defense if and only if the
        /// Terrain_System's cover-qualification query reports the defender's current cell as
        /// qualifying, and the bonus vanishes the moment the defender occupies a non-qualifying cell.
        ///
        /// **Validates: Requirements 10.1, 10.3, 10.4**
        /// </summary>
        [Test]
        [Category("Property 4")]
        public void Property4_CoverBonusTracksCurrentPositionQualification()
        {
            var terrainSystem = new TerrainSystem();
            var terrain = new TerrainVolume(Dims, CellMaterial.Soil);

            Prop.ForAll(ElevationScenarios(), s =>
            {
                var defenderCell = new CellCoord(0, s.DefenderY, 0);
                var attackerCell = new CellCoord(s.Distance, s.AttackerY, 0);

                bool qualifies = terrainSystem.GetCoverQualification(terrain, defenderCell, attackerCell);
                int expectedBonus = qualifies ? CombatSystem.TerrainCoverBonus : 0;

                int resolved = ResolveFrontAttack(s.Attack, s.Defense, s.DefenderY, s.AttackerY, s.Distance, null);
                int expected = CombatSystem.ComputeDamage(s.Attack, s.Defense + expectedBonus);
                if (resolved != expected)
                {
                    return false;
                }

                // Disappearance: dropping the defender to the attacker's elevation (non-qualifying,
                // Soil) in the same resolution removes the terrain cover bonus.
                int resolvedFlat = ResolveFrontAttack(s.Attack, s.Defense, s.AttackerY, s.AttackerY, s.Distance, null);
                return resolvedFlat == CombatSystem.ComputeDamage(s.Attack, s.Defense);
            }).Check(Config());
        }

        // ---- Property 5 -------------------------------------------------------------------------

        public sealed class StructureScenario
        {
            public int Attack;
            public int Defense;
            public int Distance;

            public override string ToString()
                => $"StructureScenario(atk={Attack}, def={Defense}, d={Distance})";
        }

        private static Arbitrary<StructureScenario> StructureScenarios()
        {
            var gen = from attack in Gen.Choose(50, 400)
                      from defense in Gen.Choose(0, 20)
                      from distance in Gen.Choose(2, 8)
                      select new StructureScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          Distance = distance,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 5: Structure-on-the-line grants a per-attack Cover_Bonus.
        ///
        /// When a Structure lies on the direct line between attacker and defender, the attack's damage
        /// reflects the structure Cover_Bonus against the defender's defense; with no such structure it
        /// reflects no cover.
        ///
        /// **Validates: Requirements 10.2**
        /// </summary>
        [Test]
        [Category("Property 5")]
        public void Property5_StructureOnLineGrantsCoverBonus()
        {
            Prop.ForAll(StructureScenarios(), s =>
            {
                // Flat terrain (equal elevation) => no terrain cover; isolate the structure bonus.
                int withStructure = ResolveFrontAttack(s.Attack, s.Defense, 0, 0, s.Distance, structureX: 1);
                int expectedWith = CombatSystem.ComputeDamage(s.Attack, s.Defense + CombatSystem.StructureCoverBonus);
                if (withStructure != expectedWith)
                {
                    return false;
                }

                // No structure on the line => base damage, no cover.
                int withoutStructure = ResolveFrontAttack(s.Attack, s.Defense, 0, 0, s.Distance, structureX: null);
                return withoutStructure == CombatSystem.ComputeDamage(s.Attack, s.Defense);
            }).Check(Config());
        }

        // ---- Property 6 -------------------------------------------------------------------------

        private static Arbitrary<StructureScenario> OverlapScenarios()
        {
            // distance >= 2 so a structure cell (X=1) sits strictly between attacker and defender.
            var gen = from attack in Gen.Choose(50, 400)
                      from defense in Gen.Choose(0, 20)
                      from distance in Gen.Choose(2, 8)
                      select new StructureScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          Distance = distance,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 6: Overlapping Cover bonuses take the greater, not the sum.
        ///
        /// When both a terrain/elevation Cover_Bonus and a structure Cover_Bonus apply to the same
        /// attack, the total cover applied equals the maximum of the two, never their sum (the two
        /// configured values differ, so the max and the sum produce distinct damage).
        ///
        /// **Validates: Requirements 10.5**
        /// </summary>
        [Test]
        [Category("Property 6")]
        public void Property6_OverlappingCoverTakesTheGreater()
        {
            Prop.ForAll(OverlapScenarios(), s =>
            {
                // Defender one level above the attacker (elevation cover qualifies) AND a structure on
                // the line of fire (structure cover qualifies).
                int resolved = ResolveFrontAttack(s.Attack, s.Defense, defenderY: 1, attackerY: 0, s.Distance, structureX: 1);

                int greater = System.Math.Max(CombatSystem.TerrainCoverBonus, CombatSystem.StructureCoverBonus);
                int sum = CombatSystem.TerrainCoverBonus + CombatSystem.StructureCoverBonus;

                int expectedMax = CombatSystem.ComputeDamage(s.Attack, s.Defense + greater);
                int asIfSummed = CombatSystem.ComputeDamage(s.Attack, s.Defense + sum);

                // Applies the greater bonus, and (because the two bonuses differ) never the sum.
                return resolved == expectedMax && resolved != asIfSummed;
            }).Check(Config());
        }
    }
}
