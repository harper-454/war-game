using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for the Artillery / Indirect_Fire behavior added in task 21
    /// (<see cref="CombatSystem.Handle(IndirectFireCommand, MatchState)"/> and
    /// <see cref="CombatSystem.Tick(MatchState, Fixed)"/>), each exercised for at least the
    /// design-mandated 100 generated iterations (design.md "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 24/25/26</c>:
    /// <list type="bullet">
    ///   <item>Property 24 — Indirect_Fire acceptance is exactly range-within-bounds AND Spotting-present;
    ///     every rejection carries a reason distinguishing out-of-range from no-Spotting; a rejected
    ///     command produces no state change (Req 15.1-15.4).</item>
    ///   <item>Property 25 — Indirect_Fire damage resolves after the flight delay regardless of
    ///     intervening Spotting loss (Req 15.5).</item>
    ///   <item>Property 26 — Resolving Indirect_Fire with a defined Area_Effect radius applies
    ///     Area_Effect rules at impact, consistent with Properties 7/8/9 (Req 15.6).</item>
    /// </list>
    ///
    /// Positions, ranges, and flight delays are generated as integers so range membership and impact
    /// timing can be checked with exact integer arithmetic matching the system's deterministic
    /// fixed-point squared-distance/flight-time comparisons. With no governance or veterancy the
    /// attacker's effective attack equals its base attack.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class IndirectFirePropertyTests
    {
        private const int MinimumIterations = 100;

        // Comfortably larger than any generated coordinate so every generated position stays in-bounds.
        private static readonly Int3 Dims = new Int3(48, 8, 48);

        private const int AttackerId = 9000;
        private const int SpotterId = 9001;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef ArtilleryDef(
            int attack, int directRange, int indirectRange, int flightDelay, int aoeRadius)
            => new UnitDef(
                "arty", Era.Prehistoric, ResourceCost.Free, 0f, 0, 1000, attack, 0, 1f, UnitRole.Soldier,
                isArtillery: true,
                indirectFireRange: Fixed.FromInt(indirectRange),
                directFireRange: Fixed.FromInt(directRange),
                indirectFireFlightDelay: Fixed.FromInt(flightDelay),
                areaEffectRadius: Fixed.FromInt(aoeRadius));

        private static UnitDef PlainUnitDef(int attack, int defense, int maxHealth)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f,
                UnitRole.Soldier);

        private static StructureDef PlainStructureDef(int maxHealth, int width, int length)
            => new StructureDef("s", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, width, length,
                StructureFunction.Defense);

        private static (MatchState state, CombatSystem combat, VisionSystem vision) NewScene(bool withVision)
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var catalog = new InMemoryCatalog();
            var units = new UnitSystem(catalog, res, civ);
            var terrain = new TerrainSystem();
            var tech = new TechSystem(catalog, res);
            var baseSystem = new BaseSystem(catalog, res, civ, tech);
            VisionSystem vision = withVision ? new VisionSystem() : null;
            var combat = new CombatSystem(civ, units, terrain, baseSystem, vision);
            var state = new MatchState(new TerrainVolume(Dims, CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            state.Nations[3] = new Nation(3);
            return (state, combat, vision);
        }

        // ==================================================================
        // Property 24
        // ==================================================================

        public sealed class AcceptanceScenario
        {
            public int Attack;
            public int DirectRange;
            public int IndirectRange;
            public int TargetX;   // artillery sits at (0,0,0); target is at (TargetX, 0, 0)
            public bool Spotting;

            public override string ToString()
                => $"Acceptance(atk={Attack}, direct={DirectRange}, indirect={IndirectRange}, tx={TargetX}, spot={Spotting})";
        }

        private static Arbitrary<AcceptanceScenario> AcceptanceScenarios()
        {
            var gen = from attack in Gen.Choose(1, 50)
                      from directRange in Gen.Choose(0, 10)
                      from gap in Gen.Choose(0, 20)   // indirect >= direct (a valid range band) most of the time
                      from tx in Gen.Choose(0, 30)
                      from spot in Gen.Choose(0, 1)
                      select new AcceptanceScenario
                      {
                          Attack = attack,
                          DirectRange = directRange,
                          IndirectRange = directRange + gap,
                          TargetX = tx,
                          Spotting = spot == 1,
                      };
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 24: Indirect_Fire acceptance is exactly range-within-bounds AND Spotting-present.
        ///
        /// An Indirect_Fire command is accepted if and only if the target is beyond the Artillery_Unit's
        /// direct-fire range and within its maximum Indirect_Fire range AND the issuing Nation currently
        /// has Spotting on the target; every rejection carries an observable reason distinguishing an
        /// out-of-range rejection from a no-Spotting rejection, and a rejected command produces no state
        /// change (no projectile is enqueued).
        ///
        /// **Validates: Requirements 15.1, 15.2, 15.3, 15.4**
        /// </summary>
        [Test]
        [Category("Property 24")]
        public void Property24_AcceptanceIsExactlyRangeWithinBoundsAndSpotting()
        {
            Prop.ForAll(AcceptanceScenarios(), s =>
            {
                var (state, combat, vision) = NewScene(withVision: true);

                // Artillery at the origin (sight radius 0, so it never Spots the far target itself).
                state.Units[AttackerId] = new UnitInstance(
                    AttackerId, 1, ArtilleryDef(s.Attack, s.DirectRange, s.IndirectRange, 1, 0),
                    WorldPosition.FromInts(0, 0, 0));

                var target = WorldPosition.FromInts(s.TargetX, 0, 0);

                // Spotting is granted exactly when the scenario asks for it, by placing an owned
                // sight source on the target cell (sight radius 0 => sees precisely its own cell).
                if (s.Spotting)
                {
                    state.Units[SpotterId] = new UnitInstance(
                        SpotterId, 1, PlainUnitDef(0, 0, 10), WorldPosition.FromInts(s.TargetX, 0, 0));
                }

                vision.Tick(state);

                // Independent oracle for range membership (integer form of the squared comparison,
                // valid because all coordinates/ranges are non-negative and the artillery is at X=0).
                bool inRange = s.TargetX > s.DirectRange && s.TargetX <= s.IndirectRange;
                bool expectedAccept = inRange && s.Spotting;

                var result = combat.Handle(new IndirectFireCommand(1, AttackerId, target), state);

                if (result.Accepted != expectedAccept)
                {
                    return false;
                }

                if (expectedAccept)
                {
                    // Acceptance emits a launch event and truly enqueues a projectile: ticking past the
                    // flight delay resolves it (an impact is produced).
                    if (!result.Events.OfType<IndirectFireLaunchedEvent>().Any())
                    {
                        return false;
                    }

                    var ticked = combat.Tick(state, Fixed.FromInt(1000));
                    return ticked.OfType<IndirectFireImpactEvent>().Any();
                }

                // Rejection: the reason distinguishes out-of-range from no-Spotting (range checked
                // first, so an out-of-range target reports out-of-range regardless of Spotting).
                string reason = result.RejectReason ?? string.Empty;
                if (!inRange)
                {
                    if (!reason.Contains("out-of-range")) return false;
                }
                else
                {
                    if (!reason.Contains("no-spotting")) return false;
                }

                // No state change on rejection: nothing was enqueued, so a large tick resolves nothing.
                var afterReject = combat.Tick(state, Fixed.FromInt(1000));
                return !afterReject.OfType<IndirectFireImpactEvent>().Any();
            }).Check(Config());
        }

        // ==================================================================
        // Property 25
        // ==================================================================

        public sealed class FlightScenario
        {
            public int Attack;
            public int Defense;
            public int TargetX;
            public int FlightDelay;

            public override string ToString()
                => $"Flight(atk={Attack}, def={Defense}, tx={TargetX}, delay={FlightDelay})";
        }

        private static Arbitrary<FlightScenario> FlightScenarios()
        {
            var gen = from attack in Gen.Choose(2, 50)
                      from defense in Gen.Choose(0, 20)
                      from tx in Gen.Choose(6, 25)     // strictly beyond direct(2), within indirect(40)
                      from delay in Gen.Choose(1, 8)
                      select new FlightScenario { Attack = attack, Defense = defense, TargetX = tx, FlightDelay = delay };
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 25: Indirect_Fire damage resolves after the flight delay regardless of intervening
        /// Spotting loss.
        ///
        /// An accepted Indirect_Fire command whose issuing Nation then <em>loses</em> Spotting on the
        /// target still applies its damage at exactly the tick corresponding to (acceptance + flight
        /// delay): the target takes no damage on any earlier tick and is damaged on exactly that tick,
        /// unaffected by the lost Spotting.
        ///
        /// **Validates: Requirements 15.5**
        /// </summary>
        [Test]
        [Category("Property 25")]
        public void Property25_DamageResolvesAfterFlightDelayRegardlessOfSpottingLoss()
        {
            Prop.ForAll(FlightScenarios(), s =>
            {
                var (state, combat, vision) = NewScene(withVision: true);

                // Artillery at origin; direct=2, indirect=40, so the target band [6,25] is valid.
                state.Units[AttackerId] = new UnitInstance(
                    AttackerId, 1, ArtilleryDef(s.Attack, 2, 40, s.FlightDelay, 0),
                    WorldPosition.FromInts(0, 0, 0));

                // Owned spotter at the target cell grants Spotting at acceptance time.
                state.Units[SpotterId] = new UnitInstance(
                    SpotterId, 1, PlainUnitDef(0, 0, 10), WorldPosition.FromInts(s.TargetX, 0, 0));

                // Enemy (Nation 2) sitting on the target cell; high health so it is never removed.
                const int EnemyId = 1;
                const int EnemyStartHealth = 1000;
                state.Units[EnemyId] = new UnitInstance(
                    EnemyId, 2, PlainUnitDef(0, s.Defense, EnemyStartHealth), WorldPosition.FromInts(s.TargetX, 0, 0));

                var target = WorldPosition.FromInts(s.TargetX, 0, 0);

                vision.Tick(state);
                var accept = combat.Handle(new IndirectFireCommand(1, AttackerId, target), state);
                if (!accept.Accepted)
                {
                    return false; // scenario precondition: the command must be accepted
                }

                // Intervening Spotting loss: remove the spotter and recompute vision so Nation 1 no
                // longer Spots the target cell during the flight window. Resolution must ignore this.
                state.Units.Remove(SpotterId);
                vision.Tick(state);

                for (int t = 1; t <= s.FlightDelay; t++)
                {
                    var ticked = combat.Tick(state, Fixed.FromInt(1));
                    bool impact = ticked.OfType<IndirectFireImpactEvent>().Any();

                    if (t < s.FlightDelay)
                    {
                        // Before the delay elapses: no impact and no damage yet.
                        if (impact) return false;
                        if (state.Units[EnemyId].Health != EnemyStartHealth) return false;
                    }
                    else
                    {
                        // Exactly at (acceptance + flight delay): the impact resolves and damage lands.
                        if (!impact) return false;
                        if (state.Units[EnemyId].Health >= EnemyStartHealth) return false;
                        if (!ticked.OfType<CombatResolvedEvent>().Any(e => e.DefenderUnitId == EnemyId)) return false;
                    }
                }

                // The projectile is consumed: no further impact on subsequent ticks.
                var afterResolution = combat.Tick(state, Fixed.FromInt(1));
                return !afterResolution.OfType<IndirectFireImpactEvent>().Any();
            }).Check(Config());
        }

        // ==================================================================
        // Property 26
        // ==================================================================

        // Impact point for the Area_Effect resolution; distance from the origin artillery is
        // sqrt(20^2 + 20^2) ~= 28.28, i.e. beyond direct(5) and within indirect(40).
        private static readonly WorldPosition Impact = WorldPosition.FromInts(20, 0, 20);

        public sealed class AoeEntity
        {
            public bool IsStructure;
            public int X;
            public int Z;
            public int Y;
            public int Nation;
            public int Defense;
            public int Facing;
            public int Width;
            public int Length;

            public override string ToString()
                => IsStructure ? $"S({X},{Z} {Width}x{Length} n{Nation})" : $"U({X},{Y},{Z} d{Defense} n{Nation})";
        }

        private static Gen<AoeEntity> AoeEntityGen()
            => from structFlag in Gen.Choose(0, 2) // 0 => Structure (1/3), else Unit (2/3)
               from x in Gen.Choose(10, 30)
               from z in Gen.Choose(10, 30)
               from y in Gen.Choose(0, 5)
               from nation in Gen.Choose(1, 3)
               from defense in Gen.Choose(0, 20)
               from facing in Gen.Choose(0, 359)
               from w in Gen.Choose(1, 3)
               from l in Gen.Choose(1, 3)
               select new AoeEntity
               {
                   IsStructure = structFlag == 0,
                   X = x, Z = z, Y = y, Nation = nation, Defense = defense, Facing = facing, Width = w, Length = l,
               };

        public sealed class AoeScenario
        {
            public int Attack;
            public int Radius;
            public List<AoeEntity> Entities;

            public override string ToString() => $"Aoe(atk={Attack}, r={Radius}, n={Entities.Count})";
        }

        private static Arbitrary<AoeScenario> AoeScenarios()
        {
            var gen = from attack in Gen.Choose(20, 200)
                      from radius in Gen.Choose(2, 15)
                      from entities in Gen.ListOf(AoeEntityGen())
                      select new AoeScenario { Attack = attack, Radius = radius, Entities = entities.ToList() };
            return Arb.From(gen);
        }

        /// <summary>
        /// Builds a scene with the origin artillery, an owned spotter on the impact cell, and the
        /// scenario's target entities (deterministic disjoint id ranges). All entities are given very
        /// high health so none is removed mid-resolution, keeping the damage-event set stable.
        /// </summary>
        private static (MatchState state, CombatSystem combat, VisionSystem vision) BuildAoeScene(
            AoeScenario s, bool withVision)
        {
            var (state, combat, vision) = NewScene(withVision);

            state.Units[AttackerId] = new UnitInstance(
                AttackerId, 1, ArtilleryDef(s.Attack, 5, 40, 1, s.Radius), WorldPosition.FromInts(0, 0, 0));
            state.Units[SpotterId] = new UnitInstance(
                SpotterId, 1, PlainUnitDef(0, 0, 1_000_000), WorldPosition.FromInts(20, 0, 20));

            int nextUnitId = 1;
            int nextStructId = 500;
            foreach (var e in s.Entities)
            {
                if (e.IsStructure)
                {
                    int id = nextStructId++;
                    state.Structures[id] = new StructureInstance(
                        id, e.Nation, PlainStructureDef(1_000_000, e.Width, e.Length), new CellCoord(e.X, 0, e.Z));
                }
                else
                {
                    int id = nextUnitId++;
                    state.Units[id] = new UnitInstance(
                        id, e.Nation, PlainUnitDef(0, e.Defense, 1_000_000), WorldPosition.FromInts(e.X, e.Y, e.Z))
                    {
                        Facing = FacingDirection.FromDegrees(e.Facing),
                    };
                }
            }

            return (state, combat, vision);
        }

        private static (Dictionary<int, int> units, Dictionary<int, int> structures) DamageMap(
            IEnumerable<GameEvent> events)
        {
            var units = new Dictionary<int, int>();
            var structures = new Dictionary<int, int>();
            foreach (var evt in events)
            {
                if (evt is CombatResolvedEvent cre)
                {
                    units[cre.DefenderUnitId] = cre.Damage;
                }
                else if (evt is StructureCombatResolvedEvent scre)
                {
                    structures[scre.DefenderStructureId] = scre.Damage;
                }
            }

            return (units, structures);
        }

        private static bool MapsEqual(Dictionary<int, int> a, Dictionary<int, int> b)
        {
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out int v) || v != kv.Value) return false;
            }

            return true;
        }

        /// <summary>
        /// Property 26: Resolving Indirect_Fire with a defined Area_Effect radius applies Area_Effect
        /// rules at impact.
        ///
        /// When an accepted Indirect_Fire attack whose Artillery_Unit defines an Area_Effect radius
        /// resolves after its flight delay, the set of damaged targets and the per-target damage are
        /// identical to those produced by an equivalent direct <see cref="CombatSystem.ResolveAreaAttack"/>
        /// at the impact point — so the same Area_Effect selection, independence, and flanking rules
        /// established by Properties 7/8/9 hold at the Indirect_Fire impact.
        ///
        /// **Validates: Requirements 15.6**
        /// </summary>
        [Test]
        [Category("Property 26")]
        public void Property26_AoeResolutionMatchesDirectAreaAttack()
        {
            Prop.ForAll(AoeScenarios(), s =>
            {
                // Scene A: the full Indirect_Fire flow (accept, then resolve on the flight-delay tick).
                var (stateA, combatA, visionA) = BuildAoeScene(s, withVision: true);
                visionA.Tick(stateA);
                var accept = combatA.Handle(new IndirectFireCommand(1, AttackerId, Impact), stateA);
                if (!accept.Accepted)
                {
                    return false; // precondition: in range + Spotted, so it must be accepted
                }

                var resolveEvents = combatA.Tick(stateA, Fixed.FromInt(1)); // flight delay is 1
                var aMap = DamageMap(resolveEvents);

                // The impact event must be present and precede the damage events for that impact.
                if (!resolveEvents.OfType<IndirectFireImpactEvent>().Any())
                {
                    return false;
                }

                // Scene B: an identical scene resolved directly via ResolveAreaAttack (the Property 7/8/9
                // oracle). With no governance/veterancy the snapshot attack equals the live attack, so
                // the two resolutions must be identical.
                var (stateB, combatB, _) = BuildAoeScene(s, withVision: false);
                var directEvents = combatB.ResolveAreaAttack(
                    stateB, AttackerId, Impact, Fixed.FromInt(s.Radius));
                var bMap = DamageMap(directEvents);

                return MapsEqual(aMap.units, bMap.units) && MapsEqual(aMap.structures, bMap.structures);
            }).Check(Config());
        }
    }
}
