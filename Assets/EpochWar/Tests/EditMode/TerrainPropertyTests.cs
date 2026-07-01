using System;
using System.Collections.Generic;
using EpochWar.Core.Navigation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for the terrain core — <see cref="TerrainVolume"/>/<see cref="TerrainEffect"/>
    /// and <see cref="NavGrid"/>/<see cref="Pathfinder"/> — covering two universal properties from
    /// design.md ("Correctness Properties"):
    ///
    /// <list type="bullet">
    /// <item>Property 26 — Terrain effects modify exactly the targeted region (Req 6.2).</item>
    /// <item>Property 27 — Cell removal keeps pathfinding consistent (Req 6.3).</item>
    /// </list>
    ///
    /// Every property is exercised for at least <see cref="MinimumIterations"/> generated cases,
    /// matching the harness conventions in <see cref="HarnessSmokePropertyTests"/> and
    /// <see cref="ResourceSystemPropertyTests"/> (>= 100 generated iterations per property, tagged
    /// <c>Feature: epoch-war-game, Property N</c>).
    ///
    /// Volumes are kept small and are filled with a single solid material so the modified region can
    /// be characterised exactly. Property 26 verifies region membership with a per-cell predicate that
    /// is formulated independently of <see cref="TerrainVolume.ApplyEffect"/>'s triple-loop
    /// implementation, so the test genuinely pins down the geometry rather than restating the code.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class TerrainPropertyTests
    {
        // The minimum number of generated cases every property in this feature must run.
        private const int MinimumIterations = 100;

        // The solid materials a generated volume may be filled with (Empty is excluded so every
        // in-region cell starts solid and is therefore altered/removed by an effect).
        private static readonly CellMaterial[] SolidMaterials =
        {
            CellMaterial.Sand,
            CellMaterial.Soil,
            CellMaterial.Rock,
            CellMaterial.Reinforced,
        };

        private static void CheckAtLeast100<T>(Arbitrary<T> arb, Func<T, bool> body)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            Prop.ForAll(arb, body).Check(config);
        }

        /// <summary>
        /// Independent (implementation-agnostic) test of whether a cell lies inside the region a
        /// <see cref="TerrainEffect"/> carves: it must be within <see cref="TerrainEffect.Depth"/>
        /// cells downward of the center and within the horizontal disc of the effect's radius.
        /// </summary>
        private static bool InEffectRegion(TerrainEffect effect, CellCoord cell)
        {
            int down = effect.Center.Y - cell.Y; // downward distance from the center layer
            if (down < 0 || down >= effect.Depth)
            {
                return false;
            }

            int dx = cell.X - effect.Center.X;
            int dz = cell.Z - effect.Center.Z;
            return (dx * dx) + (dz * dz) <= effect.Radius * effect.Radius;
        }

        /// <summary>
        /// Property 26: Terrain effects modify exactly the targeted region.
        /// For any terrain effect applied at any position with a defined area and depth, exactly the
        /// Terrain_Cells within the computed region are altered or removed, and all cells outside the
        /// region are unchanged.
        ///
        /// **Validates: Requirements 6.2**
        /// </summary>
        [Test]
        [Category("Property 26: Terrain effects modify exactly the targeted region")]
        public void Property26_EffectModifiesExactlyTheTargetedRegion()
        {
            var gen =
                from dimX in Gen.Choose(1, 6)
                from dimY in Gen.Choose(1, 6)
                from dimZ in Gen.Choose(1, 6)
                from materialIdx in Gen.Choose(0, SolidMaterials.Length - 1)
                // Center may sit anywhere from just outside to inside the volume so bounds clamping
                // (cells outside the volume are never touched) is exercised too.
                from cx in Gen.Choose(-1, dimX)
                from cy in Gen.Choose(-1, dimY)
                from cz in Gen.Choose(-1, dimZ)
                from radius in Gen.Choose(0, 3)
                from depth in Gen.Choose(1, 4)
                from power in Gen.Choose(1, 10)
                select (dimX, dimY, dimZ, materialIdx, cx, cy, cz, radius, depth, power);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (dimX, dimY, dimZ, materialIdx, cx, cy, cz, radius, depth, power) = input;

                CellMaterial material = SolidMaterials[materialIdx];
                byte startIntegrity = TerrainVolume.DefaultIntegrity(material);
                var dims = new Int3(dimX, dimY, dimZ);
                var volume = new TerrainVolume(dims, material);
                var effect = new TerrainEffect(new CellCoord(cx, cy, cz), radius, depth, power);

                IReadOnlyList<CellCoord> modified = volume.ApplyEffect(effect);

                // The returned list must be duplicate-free and correspond exactly to the in-bounds
                // region cells (every in-bounds region cell started solid, so all are altered).
                var modifiedSet = new HashSet<CellCoord>(modified);
                if (modifiedSet.Count != modified.Count)
                {
                    return false; // no cell should be reported twice
                }

                for (int z = 0; z < dimZ; z++)
                {
                    for (int y = 0; y < dimY; y++)
                    {
                        for (int x = 0; x < dimX; x++)
                        {
                            var cell = new CellCoord(x, y, z);
                            bool inRegion = InEffectRegion(effect, cell);

                            if (inRegion != modifiedSet.Contains(cell))
                            {
                                return false; // altered set must equal the region exactly
                            }

                            TerrainCell after = volume.Get(cell);
                            if (inRegion)
                            {
                                // Inside the region the solid cell is either emptied (power fully
                                // removes its integrity) or has exactly `power` integrity removed.
                                if (power >= startIntegrity)
                                {
                                    if (after.Material != CellMaterial.Empty || after.Integrity != 0)
                                    {
                                        return false;
                                    }
                                }
                                else if (after.Material != material
                                         || after.Integrity != (byte)(startIntegrity - power))
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                // Outside the region the cell is completely untouched.
                                if (after.Material != material || after.Integrity != startIntegrity)
                                {
                                    return false;
                                }
                            }
                        }
                    }
                }

                return true;
            });
        }

        /// <summary>
        /// Property 27: Cell removal keeps pathfinding consistent.
        /// For any removed Terrain_Cell, the navigation graph used for pathfinding reflects the
        /// modified terrain (newly opened cells become traversable and removed support becomes
        /// non-traversable).
        ///
        /// The property is checked two ways against the same modification: (1) the incrementally
        /// <see cref="NavGrid.Recompute"/>d grid matches, column-for-column, a grid built fresh from
        /// the post-modification volume — so the graph reflects the new terrain — and (2) a
        /// <see cref="Pathfinder"/> query over the recomputed grid returns the same route as the same
        /// query over the freshly rebuilt grid.
        ///
        /// **Validates: Requirements 6.3**
        /// </summary>
        [Test]
        [Category("Property 27: Cell removal keeps pathfinding consistent")]
        public void Property27_CellRemovalKeepsPathfindingConsistent()
        {
            var gen =
                from dimX in Gen.Choose(2, 6)
                from dimY in Gen.Choose(2, 5)
                from dimZ in Gen.Choose(2, 6)
                from materialIdx in Gen.Choose(0, SolidMaterials.Length - 1)
                // The excavation site (kept inside the volume so it removes real cells).
                from cx in Gen.Choose(0, dimX - 1)
                from cz in Gen.Choose(0, dimZ - 1)
                from radius in Gen.Choose(0, 2)
                from depth in Gen.Choose(1, dimY)
                // Start/destination columns for the pathfinding cross-check.
                from sx in Gen.Choose(0, dimX - 1)
                from sz in Gen.Choose(0, dimZ - 1)
                from dx in Gen.Choose(0, dimX - 1)
                from dz in Gen.Choose(0, dimZ - 1)
                select (dimX, dimY, dimZ, materialIdx, cx, cz, radius, depth, sx, sz, dx, dz);

            CheckAtLeast100(gen.ToArbitrary(), input =>
            {
                var (dimX, dimY, dimZ, materialIdx, cx, cz, radius, depth, sx, sz, dx, dz) = input;

                CellMaterial material = SolidMaterials[materialIdx];
                var dims = new Int3(dimX, dimY, dimZ);
                var volume = new TerrainVolume(dims, material);

                // Grid derived from the original terrain, then incrementally recomputed for the
                // cells the effect removes (Req 6.3).
                var incremental = new NavGrid(volume);

                // Carve from the top of the (cx, cz) column downward so support is genuinely removed.
                var effect = new TerrainEffect(new CellCoord(cx, dimY - 1, cz), radius, depth, power: 100);
                IReadOnlyList<CellCoord> changed = volume.ApplyEffect(effect);
                incremental.Recompute(volume, changed);

                // Ground truth: a grid built fresh from the already-modified volume.
                var rebuilt = new NavGrid(volume);

                // (1) The incremental grid must reflect the modified terrain column-for-column.
                for (int z = 0; z < dimZ; z++)
                {
                    for (int x = 0; x < dimX; x++)
                    {
                        if (incremental.SurfaceHeight(x, z) != rebuilt.SurfaceHeight(x, z))
                        {
                            return false;
                        }

                        if (incremental.IsWalkable(x, z) != rebuilt.IsWalkable(x, z))
                        {
                            return false;
                        }
                    }
                }

                // (2) Pathfinding over the recomputed graph agrees with pathfinding over the rebuilt
                // graph, so routes follow the modified terrain.
                var pathfinder = new Pathfinder();
                var start = new CellCoord(sx, 0, sz);
                var destination = new CellCoord(dx, 0, dz);

                NavPath viaIncremental = pathfinder.FindPath(incremental, UnitRole.Soldier, start, destination);
                NavPath viaRebuilt = pathfinder.FindPath(rebuilt, UnitRole.Soldier, start, destination);

                if (viaIncremental.Found != viaRebuilt.Found)
                {
                    return false;
                }

                if (viaIncremental.Found)
                {
                    if (viaIncremental.Cells.Count != viaRebuilt.Cells.Count)
                    {
                        return false;
                    }

                    for (int i = 0; i < viaIncremental.Cells.Count; i++)
                    {
                        if (viaIncremental.Cells[i] != viaRebuilt.Cells[i])
                        {
                            return false;
                        }
                    }
                }

                return true;
            });
        }
    }
}
