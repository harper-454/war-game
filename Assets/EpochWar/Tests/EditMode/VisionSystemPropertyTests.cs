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
    /// Property-based tests for <see cref="VisionSystem"/> (task 20), exercised for at least the
    /// design-mandated 100 generated iterations (design.md "Testing Strategy").
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 18-23</c>:
    /// <list type="bullet">
    ///   <item>Property 18 — Visible-cell set equals the union of owned entities' sight radii (Req 14.1).</item>
    ///   <item>Property 19 — Hidden/visible classification is exactly membership in the visible set (Req 14.2).</item>
    ///   <item>Property 20 — Last_Known_Position captures the exact transition-moment position (Req 14.3).</item>
    ///   <item>Property 21 — Displayed position resolves to exactly one of three cases (Req 14.4, 14.5, 14.6).</item>
    ///   <item>Property 22 — Recompute triggers are consistent with a full recomputation (Req 14.7, 14.8).</item>
    ///   <item>Property 23 — Last_Known_Position is discarded on removal-while-hidden (Req 14.9).</item>
    /// </list>
    ///
    /// Positions and Sight_Radius values are generated as integers, so the expected visible-cell set
    /// and membership can be computed with exact integer/long arithmetic that matches the system's
    /// deterministic fixed-point squared-distance-vs-squared-radius test on a per-elevation plane.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class VisionSystemPropertyTests
    {
        private const int MinimumIterations = 100;

        // Compact volume for the union/classification properties; big volume for the transition and
        // display properties so an entity can move well out of a sight radius yet stay in-bounds.
        private static readonly Int3 SmallDims = new Int3(16, 6, 16);
        private static readonly Int3 BigDims = new Int3(40, 4, 40);

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefWithSight(int sightRadius)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, 10, 1, 0, 1f, UnitRole.Soldier,
                sightRadius: Fixed.FromInt(sightRadius));

        private static StructureDef StructDefWithSight(int sightRadius)
            => new StructureDef("s", Era.Prehistoric, ResourceCost.Free, 0f, 0, 10, 1, 1,
                StructureFunction.Defense, sightRadius: Fixed.FromInt(sightRadius));

        private static MatchState NewState(Int3 dims, params int[] nationIds)
        {
            var state = new MatchState(new TerrainVolume(dims, CellMaterial.Soil));
            foreach (int id in nationIds)
            {
                state.Nations[id] = new Nation(id);
            }

            return state;
        }

        private static long DistanceSquared(int ax, int az, int bx, int bz)
        {
            long dx = ax - bx;
            long dz = az - bz;
            return (dx * dx) + (dz * dz);
        }

        // ---- Sight-source specification (owned Units/Structures granting vision) ---------------

        public sealed class SightSpec
        {
            public bool IsStructure;
            public int X;
            public int Y;
            public int Z;
            public int Radius;

            public override string ToString()
                => $"{(IsStructure ? "Struct" : "Unit")}(({X},{Y},{Z}) r={Radius})";
        }

        private static Gen<SightSpec> SightSpecGen()
            => from isStruct in Gen.Choose(0, 1)
               from x in Gen.Choose(0, SmallDims.X - 1)
               from y in Gen.Choose(0, SmallDims.Y - 1)
               from z in Gen.Choose(0, SmallDims.Z - 1)
               from r in Gen.Choose(0, 6)
               select new SightSpec { IsStructure = isStruct == 0, X = x, Y = y, Z = z, Radius = r };

        /// <summary>Adds the sight sources to <paramref name="state"/> under Nation <paramref name="ownerId"/>.</summary>
        private static void PopulateOwned(MatchState state, int ownerId, IEnumerable<SightSpec> specs)
        {
            int uid = 1;
            int sid = 1;
            foreach (var s in specs)
            {
                if (s.IsStructure)
                {
                    state.Structures[sid] = new StructureInstance(
                        sid, ownerId, StructDefWithSight(s.Radius), new CellCoord(s.X, s.Y, s.Z));
                    sid++;
                }
                else
                {
                    state.Units[uid] = new UnitInstance(
                        uid, ownerId, UnitDefWithSight(s.Radius), WorldPosition.FromInts(s.X, s.Y, s.Z));
                    uid++;
                }
            }
        }

        /// <summary>
        /// Independent oracle for the visible-cell set: a cell is visible iff some owned sight source
        /// shares the cell's elevation and the cell lies within that source's (planar) Sight_Radius.
        /// </summary>
        private static HashSet<CellCoord> ExpectedVisibleCells(IEnumerable<SightSpec> specs, Int3 dims)
        {
            var expected = new HashSet<CellCoord>();
            var list = specs.ToList();
            for (int x = 0; x < dims.X; x++)
            {
                for (int y = 0; y < dims.Y; y++)
                {
                    for (int z = 0; z < dims.Z; z++)
                    {
                        foreach (var s in list)
                        {
                            long r2 = (long)s.Radius * s.Radius;
                            if (s.Y == y && DistanceSquared(s.X, s.Z, x, z) <= r2)
                            {
                                expected.Add(new CellCoord(x, y, z));
                                break;
                            }
                        }
                    }
                }
            }

            return expected;
        }

        // ---- Property 18 ------------------------------------------------------------------------

        /// <summary>
        /// Property 18: Visible-cell set equals the union of owned entities' sight radii.
        ///
        /// For any Nation and set of owned Units/Structures at generated positions with generated
        /// Sight_Radius values, the computed visible-cell set equals exactly the union of all
        /// Terrain_Cells within each entity's Sight_Radius of that entity's position.
        ///
        /// **Validates: Requirements 14.1**
        /// </summary>
        [Test]
        [Category("Property 18")]
        public void Property18_VisibleCellsEqualUnionOfSightRadii()
        {
            Prop.ForAll(Arb.From(Gen.ListOf(SightSpecGen())), specsSeq =>
            {
                var specs = specsSeq.ToList();
                var state = NewState(SmallDims, 1);
                PopulateOwned(state, 1, specs);

                var vision = new VisionSystem();
                vision.Tick(state);

                var actual = vision.GetVisionState(1).VisibleCells;
                var expected = ExpectedVisibleCells(specs, SmallDims);
                return actual.SetEquals(expected);
            }).Check(Config());
        }

        // ---- Property 19 ------------------------------------------------------------------------

        public sealed class ClassificationScenario
        {
            public List<SightSpec> Owned;
            public List<SightSpec> Enemies; // reused SightSpec shape; Radius unused for enemies

            public override string ToString() => $"Owned={Owned.Count}, Enemies={Enemies.Count}";
        }

        private static Arbitrary<ClassificationScenario> ClassificationScenarios()
        {
            var gen = from owned in Gen.ListOf(SightSpecGen())
                      from enemies in Gen.ListOf(SightSpecGen())
                      select new ClassificationScenario { Owned = owned.ToList(), Enemies = enemies.ToList() };
            return Arb.From(gen);
        }

        /// <summary>
        /// Property 19: Hidden/visible classification is exactly membership in the visible-cell set.
        ///
        /// For every enemy Unit/Structure, the recorded visibility bit equals whether that entity's
        /// occupying cell is inside the Nation's visible-cell set — and the equivalence still holds
        /// after the enemies move and the set is recomputed (newly hidden / newly visible / unchanged).
        ///
        /// **Validates: Requirements 14.2**
        /// </summary>
        [Test]
        [Category("Property 19")]
        public void Property19_ClassificationIsExactlyVisibleMembership()
        {
            Prop.ForAll(ClassificationScenarios(), s =>
            {
                var state = NewState(SmallDims, 1, 2);
                PopulateOwned(state, 1, s.Owned);

                // Enemies belong to Nation 2. Track each enemy's key and current cell for assertions.
                var enemyKeys = new List<(int key, System.Func<CellCoord> cell)>();
                int uid = 1;
                int sid = 1;
                foreach (var e in s.Enemies)
                {
                    if (e.IsStructure)
                    {
                        int id = sid++;
                        state.Structures[id] = new StructureInstance(
                            id, 2, StructDefWithSight(0), new CellCoord(e.X, e.Y, e.Z));
                        enemyKeys.Add((VisionSystem.StructureKey(id), () => state.Structures[id].Origin));
                    }
                    else
                    {
                        int id = uid++;
                        state.Units[id] = new UnitInstance(
                            id, 2, UnitDefWithSight(0), WorldPosition.FromInts(e.X, e.Y, e.Z));
                        enemyKeys.Add((VisionSystem.UnitKey(id), () =>
                        {
                            var p = state.Units[id].Position;
                            return new CellCoord(p.X.ToInt(), p.Y.ToInt(), p.Z.ToInt());
                        }));
                    }
                }

                var vision = new VisionSystem();

                bool ClassificationMatches()
                {
                    var vs = vision.GetVisionState(1);
                    foreach (var (key, cellFn) in enemyKeys)
                    {
                        bool expected = vs.VisibleCells.Contains(cellFn());
                        if (!vs.EnemyVisibility.TryGetValue(key, out bool actual) || actual != expected)
                        {
                            return false;
                        }
                    }

                    return true;
                }

                vision.Tick(state);
                if (!ClassificationMatches())
                {
                    return false;
                }

                // Move every enemy Unit by a deterministic offset and recompute (Req 14.8): the
                // membership equivalence must still hold for newly hidden / newly visible / unchanged.
                foreach (var u in state.Units.Values.Where(u => u.OwnerNationId == 2).ToList())
                {
                    int nx = (u.Position.X.ToInt() + 3) % SmallDims.X;
                    u.Position = WorldPosition.FromInts(nx, u.Position.Y.ToInt(), u.Position.Z.ToInt());
                }

                vision.Tick(state);
                return ClassificationMatches();
            }).Check(Config());
        }

        // ---- Property 20 ------------------------------------------------------------------------

        public sealed class TransitionScenario
        {
            public bool EnemyIsStructure;
            public int OwnerRadius;
            public int VisibleDx;   // enemy visible offset from owner along +X (<= radius)
            public int HiddenBX;    // enemy hidden position B
            public int HiddenBZ;
            public int HiddenCX;    // enemy hidden position C (later, while still hidden)
            public int HiddenCZ;

            public override string ToString()
                => $"{(EnemyIsStructure ? "Struct" : "Unit")}(r={OwnerRadius}, vdx={VisibleDx}, B=({HiddenBX},{HiddenBZ}), C=({HiddenCX},{HiddenCZ}))";
        }

        // Owner sits at this cell in BigDims; the hidden band is far enough that any generated hidden
        // position is guaranteed outside the owner's (<=6) sight radius.
        private static readonly CellCoord OwnerCell = new CellCoord(5, 0, 5);

        private static Arbitrary<TransitionScenario> TransitionScenarios()
        {
            var gen = from isStruct in Gen.Choose(0, 1)
                      from r in Gen.Choose(2, 6)
                      from vdx in Gen.Choose(0, 2)      // <= r, so the visible cell is within sight
                      from bx in Gen.Choose(0, 5)
                      from bz in Gen.Choose(0, 5)
                      from cx in Gen.Choose(0, 5)
                      from cz in Gen.Choose(0, 5)
                      select new TransitionScenario
                      {
                          EnemyIsStructure = isStruct == 0,
                          OwnerRadius = r,
                          VisibleDx = System.Math.Min(vdx, r),
                          HiddenBX = 30 + bx,
                          HiddenBZ = 30 + bz,
                          HiddenCX = 30 + cx,
                          HiddenCZ = 30 + cz,
                      };
            return Arb.From(gen);
        }

        /// <summary>
        /// Places or moves the single generated enemy entity (id 1 in whichever collection) to
        /// <paramref name="cell"/> and returns its vision-map key.
        /// </summary>
        private static int PlaceEnemy(MatchState state, bool isStructure, CellCoord cell, bool create)
        {
            if (isStructure)
            {
                if (create)
                {
                    state.Structures[1] = new StructureInstance(1, 2, StructDefWithSight(0), cell);
                }
                else
                {
                    state.Structures[1].Origin = cell;
                }

                return VisionSystem.StructureKey(1);
            }

            if (create)
            {
                state.Units[1] = new UnitInstance(1, 2, UnitDefWithSight(0), WorldPosition.FromCell(cell));
            }
            else
            {
                state.Units[1].Position = WorldPosition.FromCell(cell);
            }

            return VisionSystem.UnitKey(1);
        }

        /// <summary>
        /// Property 20: Last_Known_Position captures the exact transition-moment position.
        ///
        /// An enemy visible at tick 1 that moves out of vision at tick 2 has its Last_Known_Position
        /// recorded as its tick-2 position — and that record is not overwritten by a later position
        /// while it remains hidden (tick 3), proving it is the exact transition-moment position.
        ///
        /// **Validates: Requirements 14.3**
        /// </summary>
        [Test]
        [Category("Property 20")]
        public void Property20_LastKnownPositionIsTheTransitionMomentPosition()
        {
            Prop.ForAll(TransitionScenarios(), s =>
            {
                var state = NewState(BigDims, 1, 2);
                state.Units[100] = new UnitInstance(
                    100, 1, UnitDefWithSight(s.OwnerRadius), WorldPosition.FromCell(OwnerCell));

                var vision = new VisionSystem();

                // Tick 1: enemy inside the owner's sight radius => visible.
                var visibleCell = new CellCoord(OwnerCell.X + s.VisibleDx, 0, OwnerCell.Z);
                int key = PlaceEnemy(state, s.EnemyIsStructure, visibleCell, create: true);
                vision.Tick(state);
                var vs = vision.GetVisionState(1);
                if (!vs.EnemyVisibility[key])
                {
                    return false; // scenario precondition: must start visible
                }

                // Tick 2: move far away (out of sight) => hidden; LKP == tick-2 position B.
                var bCell = new CellCoord(s.HiddenBX, 0, s.HiddenBZ);
                PlaceEnemy(state, s.EnemyIsStructure, bCell, create: false);
                vision.Tick(state);
                if (vs.EnemyVisibility[key])
                {
                    return false; // must now be hidden
                }

                var expectedLkp = WorldPosition.FromCell(bCell);
                if (!vs.LastKnownPosition.TryGetValue(key, out var lkp) || lkp != expectedLkp)
                {
                    return false;
                }

                // Tick 3: move again while STILL hidden => LKP must remain the tick-2 transition value.
                var cCell = new CellCoord(s.HiddenCX, 0, s.HiddenCZ);
                PlaceEnemy(state, s.EnemyIsStructure, cCell, create: false);
                vision.Tick(state);
                return vs.EnemyVisibility[key] == false
                    && vs.LastKnownPosition.TryGetValue(key, out var lkp3)
                    && lkp3 == expectedLkp;
            }).Check(Config());
        }

        // ---- Property 21 ------------------------------------------------------------------------

        /// <summary>
        /// Property 21: Displayed position resolves to exactly one of three cases.
        ///
        /// Visible → the entity's current position; hidden with a recorded Last_Known_Position → that
        /// Last_Known_Position (never the current position); hidden with none ever recorded → no
        /// displayable position.
        ///
        /// **Validates: Requirements 14.4, 14.5, 14.6**
        /// </summary>
        [Test]
        [Category("Property 21")]
        public void Property21_DisplayPositionResolvesToExactlyOneOfThreeCases()
        {
            Prop.ForAll(TransitionScenarios(), s =>
            {
                // Case C — hidden and never seen: no Last_Known_Position => null.
                {
                    var state = NewState(BigDims, 1, 2);
                    state.Units[100] = new UnitInstance(
                        100, 1, UnitDefWithSight(s.OwnerRadius), WorldPosition.FromCell(OwnerCell));
                    var hidden = new CellCoord(s.HiddenBX, 0, s.HiddenBZ);
                    var vision = new VisionSystem();
                    int key = PlaceEnemy(state, s.EnemyIsStructure, hidden, create: true);
                    vision.Tick(state);
                    if (vision.GetVisionState(1).EnemyVisibility[key])
                    {
                        return true; // precondition slipped (shouldn't happen); skip
                    }

                    var display = vision.GetDisplayPosition(1, key, WorldPosition.FromCell(hidden));
                    if (display != null)
                    {
                        return false;
                    }
                }

                // Case A — visible: returns the supplied current position.
                {
                    var state = NewState(BigDims, 1, 2);
                    state.Units[100] = new UnitInstance(
                        100, 1, UnitDefWithSight(s.OwnerRadius), WorldPosition.FromCell(OwnerCell));
                    var visibleCell = new CellCoord(OwnerCell.X + s.VisibleDx, 0, OwnerCell.Z);
                    var vision = new VisionSystem();
                    int key = PlaceEnemy(state, s.EnemyIsStructure, visibleCell, create: true);
                    vision.Tick(state);
                    if (!vision.GetVisionState(1).EnemyVisibility[key])
                    {
                        return true; // precondition slipped; skip
                    }

                    var current = WorldPosition.FromCell(visibleCell);
                    if (vision.GetDisplayPosition(1, key, current) != current)
                    {
                        return false;
                    }
                }

                // Case B — hidden with a recorded LKP: returns the LKP, not the current position.
                {
                    var state = NewState(BigDims, 1, 2);
                    state.Units[100] = new UnitInstance(
                        100, 1, UnitDefWithSight(s.OwnerRadius), WorldPosition.FromCell(OwnerCell));
                    var vision = new VisionSystem();
                    var visibleCell = new CellCoord(OwnerCell.X + s.VisibleDx, 0, OwnerCell.Z);
                    int key = PlaceEnemy(state, s.EnemyIsStructure, visibleCell, create: true);
                    vision.Tick(state);
                    if (!vision.GetVisionState(1).EnemyVisibility[key])
                    {
                        return true; // precondition slipped; skip
                    }

                    var bCell = new CellCoord(s.HiddenBX, 0, s.HiddenBZ);
                    PlaceEnemy(state, s.EnemyIsStructure, bCell, create: false);
                    vision.Tick(state);

                    var lkp = WorldPosition.FromCell(bCell);
                    // Pass a DIFFERENT "current" position to prove the LKP (not current) is returned.
                    var laterCurrent = WorldPosition.FromCell(new CellCoord(s.HiddenCX, 0, s.HiddenCZ));
                    var display = vision.GetDisplayPosition(1, key, laterCurrent);
                    if (display == null || display.Value != lkp)
                    {
                        return false;
                    }
                }

                return true;
            }).Check(Config());
        }

        // ---- Property 22 ------------------------------------------------------------------------

        /// <summary>
        /// Property 22: Recompute triggers are consistent with a full recomputation.
        ///
        /// After an owned-entity move/create/remove and the resulting recompute, the incrementally
        /// maintained visible-cell set and every enemy's classification are identical to those a fresh,
        /// from-scratch computation produces over the post-change entity set.
        ///
        /// **Validates: Requirements 14.7, 14.8**
        /// </summary>
        [Test]
        [Category("Property 22")]
        public void Property22_IncrementalRecomputeMatchesFromScratch()
        {
            Prop.ForAll(ClassificationScenarios(), s =>
            {
                var state = NewState(SmallDims, 1, 2);
                PopulateOwned(state, 1, s.Owned);

                int uid = 1000;
                int sid = 1000;
                foreach (var e in s.Enemies)
                {
                    if (e.IsStructure)
                    {
                        state.Structures[sid] = new StructureInstance(
                            sid, 2, StructDefWithSight(0), new CellCoord(e.X, e.Y, e.Z));
                        sid++;
                    }
                    else
                    {
                        state.Units[uid] = new UnitInstance(
                            uid, 2, UnitDefWithSight(0), WorldPosition.FromInts(e.X, e.Y, e.Z));
                        uid++;
                    }
                }

                var vision = new VisionSystem();
                vision.Tick(state);

                // Apply a mutation (move / create / remove an owned sight source), then recompute.
                var ownedUnit = state.Units.Values.FirstOrDefault(u => u.OwnerNationId == 1);
                if (ownedUnit != null)
                {
                    int nx = (ownedUnit.Position.X.ToInt() + 4) % SmallDims.X;
                    ownedUnit.Position = WorldPosition.FromInts(nx, ownedUnit.Position.Y.ToInt(), ownedUnit.Position.Z.ToInt());
                }
                else
                {
                    // No owned unit to move — create one so the "create" trigger is exercised too.
                    state.Units[1] = new UnitInstance(1, 1, UnitDefWithSight(3), WorldPosition.FromInts(8, 0, 8));
                }

                vision.Tick(state);

                // A fresh system computing once over the post-change state is the from-scratch oracle.
                var fresh = new VisionSystem();
                fresh.Tick(state);

                foreach (int nationId in state.Nations.Keys)
                {
                    var a = vision.GetVisionState(nationId);
                    var b = fresh.GetVisionState(nationId);
                    if (!a.VisibleCells.SetEquals(b.VisibleCells))
                    {
                        return false;
                    }

                    if (a.EnemyVisibility.Count != b.EnemyVisibility.Count)
                    {
                        return false;
                    }

                    foreach (var kv in b.EnemyVisibility)
                    {
                        if (!a.EnemyVisibility.TryGetValue(kv.Key, out bool av) || av != kv.Value)
                        {
                            return false;
                        }
                    }
                }

                return true;
            }).Check(Config());
        }

        // ---- Property 23 ------------------------------------------------------------------------

        /// <summary>
        /// Property 23: Last_Known_Position is discarded on removal-while-hidden.
        ///
        /// An enemy hidden from a Nation with a recorded Last_Known_Position that is then permanently
        /// removed from the Match has its Last_Known_Position discarded — it is absent for that
        /// entity's key afterward, and the display query no longer yields a position.
        ///
        /// **Validates: Requirements 14.9**
        /// </summary>
        [Test]
        [Category("Property 23")]
        public void Property23_LastKnownPositionDiscardedOnRemovalWhileHidden()
        {
            Prop.ForAll(TransitionScenarios(), s =>
            {
                var state = NewState(BigDims, 1, 2);
                state.Units[100] = new UnitInstance(
                    100, 1, UnitDefWithSight(s.OwnerRadius), WorldPosition.FromCell(OwnerCell));

                var vision = new VisionSystem();

                // Visible, then hidden with a recorded LKP.
                var visibleCell = new CellCoord(OwnerCell.X + s.VisibleDx, 0, OwnerCell.Z);
                int key = PlaceEnemy(state, s.EnemyIsStructure, visibleCell, create: true);
                vision.Tick(state);
                if (!vision.GetVisionState(1).EnemyVisibility[key])
                {
                    return true; // precondition slipped; skip
                }

                var bCell = new CellCoord(s.HiddenBX, 0, s.HiddenBZ);
                PlaceEnemy(state, s.EnemyIsStructure, bCell, create: false);
                vision.Tick(state);
                var vs = vision.GetVisionState(1);
                if (!vs.LastKnownPosition.ContainsKey(key))
                {
                    return false; // must have a recorded LKP before removal
                }

                // Permanently remove the (hidden) enemy from the Match, then recompute.
                if (s.EnemyIsStructure)
                {
                    state.Structures.Remove(1);
                }
                else
                {
                    state.Units.Remove(1);
                }

                vision.Tick(state);

                // The stale LKP (and classification) must be discarded, and display yields nothing.
                return !vs.LastKnownPosition.ContainsKey(key)
                    && !vs.EnemyVisibility.ContainsKey(key)
                    && vision.GetDisplayPosition(1, key, WorldPosition.FromCell(bCell)) == null;
            }).Check(Config());
        }
    }
}
