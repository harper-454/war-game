using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for <see cref="CombatSystem.ResolveAreaAttack"/> (task 16.1), exercised
    /// for at least the design-mandated 100 generated iterations (design.md "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 7/8/9</c>:
    /// <list type="bullet">
    ///   <item>Property 7 — Area_Effect selects exactly the targets within radius, including
    ///     own-Nation entities (Req 11.1).</item>
    ///   <item>Property 8 — Area_Effect damage is full and independent per target (Req 11.2).</item>
    ///   <item>Property 9 — Area_Effect Flanking applies to Units and never to Structures (Req 11.3).</item>
    /// </list>
    ///
    /// Positions and radii are generated as integers so the expected radius-membership set is computed
    /// with exact integer/long arithmetic that matches the system's deterministic fixed-point
    /// comparison of squared distance to squared radius. With no governance adopted the effective
    /// attack/defense equal the base values.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class AreaEffectPropertyTests
    {
        private const int MinimumIterations = 100;

        // Large, flat Soil volume: non-cover material everywhere, so cover depends purely on
        // elevation. Big enough that every generated in-map position/elevation stays in bounds.
        private static readonly Int3 Dims = new Int3(64, 32, 64);

        // The attacker is parked far outside every generated radius so it is never itself a target,
        // keeping Property 7's expected target set unambiguous.
        private const int AttackerId = 9000;
        private static readonly WorldPosition AttackerHome = WorldPosition.FromInts(1000, 0, 1000);

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefOf(int attack, int defense, int maxHealth)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f, UnitRole.Soldier);

        private static StructureDef StructureDefOf(int maxHealth, int width, int length)
            => new StructureDef("s", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, width, length,
                StructureFunction.Defense);

        private static (MatchState state, CombatSystem combat) Build(int attackerAttack)
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var catalog = new InMemoryCatalog();
            var units = new UnitSystem(catalog, res, civ);
            var terrain = new TerrainSystem();
            var tech = new TechSystem(catalog, res);
            var baseSystem = new BaseSystem(catalog, res, civ, tech);
            var combat = new CombatSystem(civ, units, terrain, baseSystem);
            var state = new MatchState(new TerrainVolume(Dims, CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            state.Nations[3] = new Nation(3);

            // The attacker belongs to Nation 1; own-Nation entities must still be damaged (Req 11.1).
            state.Units[AttackerId] = new UnitInstance(AttackerId, 1, UnitDefOf(attackerAttack, 0, 1000), AttackerHome);
            return (state, combat);
        }

        private static long DistanceSquared(int ax, int az, int bx, int bz)
        {
            long dx = ax - bx;
            long dz = az - bz;
            return (dx * dx) + (dz * dz);
        }

        private static int ClampInt(int value, int min, int max)
            => value < min ? min : (value > max ? max : value);

        /// <summary>
        /// Replicates <c>CombatSystem.IsOnHorizontalSegmentExclusive</c>: true when point
        /// <c>(px,pz)</c> lies strictly on the open X/Z segment between <c>(ax,az)</c> and
        /// <c>(bx,bz)</c> (endpoints excluded). Used to reproduce the structure-on-line-of-fire Cover
        /// rule (Req 10.2) that <see cref="CombatSystem.ResolveAreaAttack"/> honors per Unit target.
        /// </summary>
        private static bool OnSegmentExclusive(int ax, int az, int bx, int bz, int px, int pz)
        {
            if ((px == ax && pz == az) || (px == bx && pz == bz))
            {
                return false;
            }

            long abx = (long)bx - ax;
            long abz = (long)bz - az;
            long apx = (long)px - ax;
            long apz = (long)pz - az;

            if ((abx * apz) - (abz * apx) != 0)
            {
                return false; // not collinear
            }

            int minX = System.Math.Min(ax, bx);
            int maxX = System.Math.Max(ax, bx);
            int minZ = System.Math.Min(az, bz);
            int maxZ = System.Math.Max(az, bz);
            return px >= minX && px <= maxX && pz >= minZ && pz <= maxZ;
        }

        /// <summary>
        /// The Cover_Bonus the system applies to a Unit target of an Area_Effect attack, computed
        /// independently of the system: the greater of the terrain/elevation bonus (the Unit stands at
        /// least one level above the impact elevation on non-cover Soil) and the structure-on-line
        /// bonus (a Structure origin lies strictly between the impact cell and the Unit cell), never
        /// their sum (Req 10.1, 10.2, 10.5).
        /// </summary>
        private static int ExpectedUnitCover(EntitySpec unit, AreaScenario s)
        {
            int terrainBonus = (unit.Y - s.ImpactY) >= CoverClassifier.DefaultElevationMargin
                ? CombatSystem.TerrainCoverBonus
                : 0;

            bool structureOnLine = s.Entities.Any(e =>
                e.IsStructure && OnSegmentExclusive(s.ImpactX, s.ImpactZ, unit.X, unit.Z, e.X, e.Z));
            int structureBonus = structureOnLine ? CombatSystem.StructureCoverBonus : 0;

            return System.Math.Max(terrainBonus, structureBonus);
        }

        // ---- Entity specifications -------------------------------------------------------------

        public sealed class EntitySpec
        {
            public bool IsStructure;
            public int X;
            public int Z;
            public int Y;          // elevation (units only; structures sit at origin cell)
            public int Nation;     // 1 = attacker's own Nation (friendly fire), 2/3 = others
            public int Defense;    // units only
            public int FacingDegrees; // units only
            public int Width;      // structures only
            public int Length;     // structures only

            public override string ToString()
                => IsStructure
                    ? $"Struct(({X},{Z}) {Width}x{Length}, n{Nation})"
                    : $"Unit(({X},{Y},{Z}) def={Defense} face={FacingDegrees} n{Nation})";
        }

        private static Gen<EntitySpec> EntityGen()
            => from structFlag in Gen.Choose(0, 2) // 0 => Structure (1/3), 1/2 => Unit (2/3)
               from x in Gen.Choose(0, 40)
               from z in Gen.Choose(0, 40)
               from y in Gen.Choose(0, 8)
               from nation in Gen.Choose(1, 3)
               from defense in Gen.Choose(0, 30)
               from facing in Gen.Choose(0, 359)
               from w in Gen.Choose(1, 3)
               from l in Gen.Choose(1, 3)
               select new EntitySpec
               {
                   IsStructure = structFlag == 0,
                   X = x,
                   Z = z,
                   Y = y,
                   Nation = nation,
                   Defense = defense,
                   FacingDegrees = facing,
                   Width = w,
                   Length = l,
               };

        public sealed class AreaScenario
        {
            public int Attack;
            public int ImpactX;
            public int ImpactZ;
            public int ImpactY;
            public int Radius;
            public List<EntitySpec> Entities;

            public override string ToString()
                => $"Area(atk={Attack}, impact=({ImpactX},{ImpactY},{ImpactZ}), r={Radius}, n={Entities.Count})";
        }

        private static Arbitrary<AreaScenario> AreaScenarios()
        {
            var gen = from attack in Gen.Choose(20, 300)
                      from ix in Gen.Choose(0, 40)
                      from iz in Gen.Choose(0, 40)
                      from iy in Gen.Choose(0, 8)
                      from radius in Gen.Choose(1, 20)
                      from entities in Gen.ListOf(EntityGen())
                      select new AreaScenario
                      {
                          Attack = attack,
                          ImpactX = ix,
                          ImpactZ = iz,
                          ImpactY = iy,
                          Radius = radius,
                          Entities = entities.ToList(),
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Populates <paramref name="state"/> with the scenario's entities, assigning disjoint id
        /// ranges (units 1.., structures 500..) and returning the id assigned to each spec. Units are
        /// given very high health so none are removed mid-resolution, keeping the emitted event set a
        /// faithful record of exactly which targets were damaged.
        /// </summary>
        private static (Dictionary<int, EntitySpec> unitSpecs, Dictionary<int, EntitySpec> structSpecs) Populate(
            MatchState state, AreaScenario s)
        {
            var unitSpecs = new Dictionary<int, EntitySpec>();
            var structSpecs = new Dictionary<int, EntitySpec>();
            int nextUnitId = 1;
            int nextStructId = 500;

            foreach (var e in s.Entities)
            {
                if (e.IsStructure)
                {
                    int id = nextStructId++;
                    state.Structures[id] = new StructureInstance(
                        id, e.Nation, StructureDefOf(1_000_000, e.Width, e.Length), new CellCoord(e.X, 0, e.Z));
                    structSpecs[id] = e;
                }
                else
                {
                    int id = nextUnitId++;
                    state.Units[id] = new UnitInstance(
                        id, e.Nation, UnitDefOf(0, e.Defense, 1_000_000), WorldPosition.FromInts(e.X, e.Y, e.Z))
                    {
                        Facing = FacingDirection.FromDegrees(e.FacingDegrees),
                    };
                    unitSpecs[id] = e;
                }
            }

            return (unitSpecs, structSpecs);
        }

        // ---- Property 7 -------------------------------------------------------------------------

        /// <summary>
        /// Property 7: Area_Effect selects exactly the targets within radius, including own-Nation
        /// entities.
        ///
        /// The set of Units and Structures that receive Area_Effect damage equals exactly the set whose
        /// occupied space's nearest point lies within the radius of the impact point, with no exclusion
        /// based on owning Nation (Units occupy a point; a Structure's nearest point is the point of its
        /// footprint AABB closest to the impact point).
        ///
        /// **Validates: Requirements 11.1**
        /// </summary>
        [Test]
        [Category("Property 7")]
        public void Property7_SelectsExactlyTargetsWithinRadius_IncludingOwnNation()
        {
            Prop.ForAll(AreaScenarios(), s =>
            {
                var (state, combat) = Build(s.Attack);
                var (unitSpecs, structSpecs) = Populate(state, s);

                long r2 = (long)s.Radius * s.Radius;

                var expectedUnitIds = new HashSet<int>();
                foreach (var kv in unitSpecs)
                {
                    if (DistanceSquared(kv.Value.X, kv.Value.Z, s.ImpactX, s.ImpactZ) <= r2)
                    {
                        expectedUnitIds.Add(kv.Key);
                    }
                }

                var expectedStructIds = new HashSet<int>();
                foreach (var kv in structSpecs)
                {
                    var e = kv.Value;
                    int nearestX = ClampInt(s.ImpactX, e.X, e.X + e.Width - 1);
                    int nearestZ = ClampInt(s.ImpactZ, e.Z, e.Z + e.Length - 1);
                    if (DistanceSquared(nearestX, nearestZ, s.ImpactX, s.ImpactZ) <= r2)
                    {
                        expectedStructIds.Add(kv.Key);
                    }
                }

                var events = combat.ResolveAreaAttack(
                    state, AttackerId, WorldPosition.FromInts(s.ImpactX, s.ImpactY, s.ImpactZ), Fixed.FromInt(s.Radius));

                var actualUnitIds = new HashSet<int>(
                    events.OfType<CombatResolvedEvent>().Select(e => e.DefenderUnitId));
                var actualStructIds = new HashSet<int>(
                    events.OfType<StructureCombatResolvedEvent>().Select(e => e.DefenderStructureId));

                return actualUnitIds.SetEquals(expectedUnitIds)
                    && actualStructIds.SetEquals(expectedStructIds);
            }).Check(Config());
        }

        // ---- Property 8 -------------------------------------------------------------------------

        /// <summary>
        /// Resolves a lone single-target attack from an attacker positioned <em>at the impact point</em>
        /// against a single Unit copy, returning the damage. This is the "equivalent single-target
        /// attack" the Area_Effect per-target damage must match (Req 11.2): same effective attack, same
        /// flank source (the impact point), same terrain/elevation cover, and the same Structures on
        /// the field (so any structure-on-line-of-fire Cover the Unit earns is reproduced exactly).
        /// </summary>
        private static int LoneUnitDamage(int attack, EntitySpec e, AreaScenario s)
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var catalog = new InMemoryCatalog();
            var units = new UnitSystem(catalog, res, civ);
            var terrain = new TerrainSystem();
            var combat = new CombatSystem(civ, units, terrain);
            var state = new MatchState(new TerrainVolume(Dims, CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);

            state.Units[1] = new UnitInstance(1, 1, UnitDefOf(attack, 0, 1000),
                WorldPosition.FromInts(s.ImpactX, s.ImpactY, s.ImpactZ));
            state.Units[2] = new UnitInstance(2, 2, UnitDefOf(0, e.Defense, 1_000_000),
                WorldPosition.FromInts(e.X, e.Y, e.Z))
            {
                Facing = FacingDirection.FromDegrees(e.FacingDegrees),
            };

            // Reproduce every Structure on the field so structure-on-line cover matches the AoE scene.
            int sid = 700;
            foreach (var spec in s.Entities.Where(x => x.IsStructure))
            {
                state.Structures[sid++] = new StructureInstance(
                    sid, spec.Nation, StructureDefOf(1_000_000, spec.Width, spec.Length),
                    new CellCoord(spec.X, 0, spec.Z));
            }

            var events = combat.ResolveAttack(state, 1, 2);
            return events.OfType<CombatResolvedEvent>().Single().Damage;
        }

        /// <summary>
        /// Property 8: Area_Effect damage is full and independent per target.
        ///
        /// Each Unit target's applied damage equals the damage it would take as the lone target of an
        /// equivalent single-target attack (honoring its own defense and cover), and each Structure
        /// target takes the attacker's full effective attack against zero defense — proving the damage
        /// is never divided or reduced to distribute a fixed pool among the affected targets.
        ///
        /// **Validates: Requirements 11.2**
        /// </summary>
        [Test]
        [Category("Property 8")]
        public void Property8_DamageIsFullAndIndependentPerTarget()
        {
            Prop.ForAll(AreaScenarios(), s =>
            {
                var (state, combat) = Build(s.Attack);
                var (unitSpecs, structSpecs) = Populate(state, s);

                var events = combat.ResolveAreaAttack(
                    state, AttackerId, WorldPosition.FromInts(s.ImpactX, s.ImpactY, s.ImpactZ), Fixed.FromInt(s.Radius));

                // Every damaged Unit took exactly the damage a lone single-target attack (flanked and
                // covered from the impact point) would have dealt it — regardless of the other targets.
                foreach (var evt in events.OfType<CombatResolvedEvent>())
                {
                    if (!unitSpecs.TryGetValue(evt.DefenderUnitId, out var spec))
                    {
                        return false; // an unexpected id was damaged
                    }

                    int lone = LoneUnitDamage(s.Attack, spec, s);
                    if (evt.Damage != lone)
                    {
                        return false;
                    }
                }

                // Every damaged Structure took the attacker's full effective attack vs zero defense.
                int expectedStructDamage = CombatSystem.ComputeDamage(s.Attack, 0);
                foreach (var evt in events.OfType<StructureCombatResolvedEvent>())
                {
                    if (!structSpecs.ContainsKey(evt.DefenderStructureId) || evt.Damage != expectedStructDamage)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ---- Property 9 -------------------------------------------------------------------------

        /// <summary>
        /// Property 9: Area_Effect Flanking applies to Units and never to Structures.
        ///
        /// Each affected Unit's damage reflects its Flank classification computed against the impact
        /// point (front adds no bonus, side/rear add the configured bonuses), while every affected
        /// Structure's damage is exactly the base attacker-attack-vs-zero-defense value — never
        /// modified by any Flanking_Bonus.
        ///
        /// **Validates: Requirements 11.3**
        /// </summary>
        [Test]
        [Category("Property 9")]
        public void Property9_FlankingAppliesToUnitsNeverStructures()
        {
            var frontArc = Fixed.FromInt(CombatSystem.FrontArcDegrees);
            var sideArc = Fixed.FromInt(CombatSystem.SideArcDegrees);

            Prop.ForAll(AreaScenarios(), s =>
            {
                var (state, combat) = Build(s.Attack);
                var (unitSpecs, structSpecs) = Populate(state, s);

                var impact = WorldPosition.FromInts(s.ImpactX, s.ImpactY, s.ImpactZ);
                var events = combat.ResolveAreaAttack(state, AttackerId, impact, Fixed.FromInt(s.Radius));

                int structDamage = CombatSystem.ComputeDamage(s.Attack, 0);

                foreach (var evt in events.OfType<CombatResolvedEvent>())
                {
                    var spec = unitSpecs[evt.DefenderUnitId];
                    // Flank is computed from the impact point, and terrain cover uses the impact
                    // elevation as the comparison elevation (attacker "cell" = impact cell).
                    Flank flank = FlankClassifier.Classify(
                        FacingDirection.FromDegrees(spec.FacingDegrees),
                        WorldPosition.FromInts(spec.X, spec.Y, spec.Z),
                        impact,
                        frontArc,
                        sideArc);

                    int flankBonus = flank == Flank.Side
                        ? CombatSystem.SideFlankingBonus
                        : (flank == Flank.Rear ? CombatSystem.RearFlankingBonus : 0);

                    int coverBonus = ExpectedUnitCover(spec, s);

                    int expected = CombatSystem.ComputeDamage(s.Attack + flankBonus, spec.Defense + coverBonus);
                    if (evt.Damage != expected)
                    {
                        return false;
                    }
                }

                // Structures are never flank-adjusted: their damage is always the base value.
                foreach (var evt in events.OfType<StructureCombatResolvedEvent>())
                {
                    if (evt.Damage != structDamage)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }
    }
}
