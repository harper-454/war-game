using System.Collections.Generic;

namespace EpochWar.Core.State
{
    /// <summary>
    /// The material a single <see cref="TerrainCell"/> is made of (Req 6.1).
    ///
    /// <see cref="Empty"/> means the cell has been dug out or destroyed and is no longer
    /// solid. The remaining materials are ordered loosely by hardness (Sand softest,
    /// Reinforced hardest); see <see cref="TerrainVolume.DefaultIntegrity"/> for the
    /// integrity each starts with.
    /// </summary>
    public enum CellMaterial : byte
    {
        Empty = 0,
        Soil,
        Rock,
        Sand,
        Reinforced,
    }

    /// <summary>
    /// A single addressable unit of terrain — the value stored at every
    /// <see cref="CellCoord"/> in a <see cref="TerrainVolume"/> (Req 6.1).
    ///
    /// <see cref="Material"/> determines solidity (<see cref="CellMaterial.Empty"/> is
    /// non-solid / carved out) while <see cref="Integrity"/> is the remaining hardness used
    /// for partial damage: a <see cref="TerrainEffect"/> removes integrity, and when it drops
    /// to zero the cell becomes <see cref="CellMaterial.Empty"/> (Req 6.2). A value type so the
    /// volume can store cells in a dense flat array with no per-cell allocation.
    /// </summary>
    public struct TerrainCell
    {
        /// <summary>The cell's material; <see cref="CellMaterial.Empty"/> means dug out / destroyed.</summary>
        public CellMaterial Material;

        /// <summary>Remaining hardness for partial damage; reaching zero empties the cell.</summary>
        public byte Integrity;

        public TerrainCell(CellMaterial material, byte integrity)
        {
            Material = material;
            Integrity = integrity;
        }

        /// <summary>A canonical empty (non-solid) cell.</summary>
        public static readonly TerrainCell Empty = new TerrainCell(CellMaterial.Empty, 0);

        /// <summary>True when the cell is anything other than <see cref="CellMaterial.Empty"/>.</summary>
        public bool IsSolid => Material != CellMaterial.Empty;
    }

    /// <summary>
    /// A weapon or excavation effect applied to the terrain volume (Req 6.2).
    ///
    /// The effect carves a region centered on <see cref="Center"/>: cells within
    /// <see cref="Radius"/> horizontally (X/Z) and reaching <see cref="Depth"/> cells downward
    /// (decreasing Y) have <see cref="Power"/> subtracted from their integrity. When a cell's
    /// integrity is fully removed it becomes <see cref="CellMaterial.Empty"/>. Regions are
    /// clamped to the volume bounds by <see cref="TerrainVolume.ApplyEffect"/>.
    /// </summary>
    public struct TerrainEffect
    {
        /// <summary>The cell at the top center of the affected region.</summary>
        public CellCoord Center;

        /// <summary>The horizontal (X/Z) radius of the affected area, in cells.</summary>
        public int Radius;

        /// <summary>How many cells downward (decreasing Y) the effect carves, starting at <see cref="Center"/>.</summary>
        public int Depth;

        /// <summary>Integrity removed from each affected cell; values at or above a cell's integrity empty it.</summary>
        public int Power;

        public TerrainEffect(CellCoord center, int radius, int depth, int power)
        {
            Center = center;
            Radius = radius;
            Depth = depth;
            Power = power;
        }
    }

    /// <summary>
    /// The 3D grid of addressable <see cref="TerrainCell"/>s that makes up the battlefield
    /// (Req 6.1), and the destructible/diggable model the <c>TerrainSystem</c> mutates.
    ///
    /// Cells are stored in a dense flat array using X-fastest ordering (see
    /// <see cref="CellCoord.ToFlatIndex"/>), grouped conceptually into fixed-size chunks
    /// (<see cref="ChunkSize"/>) so the presentation layer can rebuild only the chunk meshes
    /// whose cells changed. Lookups are O(1) and out-of-range access is safe: it returns
    /// <see cref="TerrainCell.Empty"/> / non-solid rather than throwing (design "Terrain edge
    /// safety").
    ///
    /// This type extends the earlier placeholder (task 2.2) in place: it keeps the same
    /// <see cref="Dimensions"/> (<see cref="Int3"/>) surface and parameterless constructor so
    /// <see cref="MatchState"/> still compiles, while adding the full cell store and the
    /// <see cref="Get"/>/<see cref="IsSolid"/>/<see cref="ApplyEffect"/>/<see cref="IsSupported"/>
    /// operations from the design.
    /// </summary>
    public sealed class TerrainVolume
    {
        /// <summary>Edge length, in cells, of a meshing/replication chunk along each axis.</summary>
        public const int ChunkSize = 16;

        private readonly TerrainCell[] _cells;

        /// <summary>The volume dimensions in cells: X, Y (up), Z.</summary>
        public Int3 Dimensions { get; }

        /// <summary>
        /// Creates a volume of the given <paramref name="dimensions"/> filled uniformly with
        /// <paramref name="fillMaterial"/>. Each solid cell starts at the material's
        /// <see cref="DefaultIntegrity"/>; <see cref="CellMaterial.Empty"/> cells start at zero.
        /// Non-positive dimensions yield an empty (zero-cell) volume.
        /// </summary>
        public TerrainVolume(Int3 dimensions, CellMaterial fillMaterial = CellMaterial.Empty)
        {
            Dimensions = dimensions;

            long count = dimensions.X > 0 && dimensions.Y > 0 && dimensions.Z > 0
                ? dimensions.Volume
                : 0;

            _cells = new TerrainCell[count];

            if (fillMaterial != CellMaterial.Empty && count > 0)
            {
                byte integrity = DefaultIntegrity(fillMaterial);
                var cell = new TerrainCell(fillMaterial, integrity);
                for (int i = 0; i < _cells.Length; i++)
                {
                    _cells[i] = cell;
                }
            }
        }

        /// <summary>Creates an empty (zero-dimension) terrain volume placeholder.</summary>
        public TerrainVolume()
            : this(Int3.Zero)
        {
        }

        /// <summary>The starting integrity for a freshly created cell of the given material.</summary>
        public static byte DefaultIntegrity(CellMaterial material)
        {
            switch (material)
            {
                case CellMaterial.Sand: return 1;
                case CellMaterial.Soil: return 2;
                case CellMaterial.Rock: return 4;
                case CellMaterial.Reinforced: return 8;
                default: return 0; // Empty
            }
        }

        /// <summary>
        /// Returns the cell at <paramref name="c"/>, or <see cref="TerrainCell.Empty"/> when the
        /// coordinate lies outside the volume (edge safety — never throws).
        /// </summary>
        public TerrainCell Get(CellCoord c)
        {
            if (!c.IsInside(Dimensions))
            {
                return TerrainCell.Empty;
            }

            return _cells[c.ToFlatIndex(Dimensions)];
        }

        /// <summary>
        /// True when the cell at <paramref name="c"/> exists and is not
        /// <see cref="CellMaterial.Empty"/>. Out-of-range coordinates are non-solid.
        /// </summary>
        public bool IsSolid(CellCoord c)
        {
            return Get(c).IsSolid;
        }

        /// <summary>
        /// Applies <paramref name="effect"/> to the volume, carving a region of
        /// <see cref="TerrainEffect.Radius"/> (horizontal) and <see cref="TerrainEffect.Depth"/>
        /// (downward) centered on <see cref="TerrainEffect.Center"/> and removing
        /// <see cref="TerrainEffect.Power"/> integrity from each solid cell it reaches (Req 6.2).
        /// Cells whose integrity is fully removed become <see cref="CellMaterial.Empty"/>.
        ///
        /// The region is clamped to the volume bounds, so out-of-range cells are skipped rather
        /// than throwing. Returns the coordinates of every cell that actually changed (integrity
        /// reduced and/or material emptied); cells already <see cref="CellMaterial.Empty"/> or a
        /// non-positive <see cref="TerrainEffect.Power"/> produce no modifications.
        /// </summary>
        public IReadOnlyList<CellCoord> ApplyEffect(TerrainEffect effect)
        {
            var modified = new List<CellCoord>();

            if (effect.Power <= 0 || effect.Radius < 0 || effect.Depth <= 0 || _cells.Length == 0)
            {
                return modified;
            }

            int radiusSquared = effect.Radius * effect.Radius;

            for (int dy = 0; dy < effect.Depth; dy++)
            {
                int y = effect.Center.Y - dy;
                if (y < 0 || y >= Dimensions.Y)
                {
                    continue;
                }

                for (int dz = -effect.Radius; dz <= effect.Radius; dz++)
                {
                    int z = effect.Center.Z + dz;
                    if (z < 0 || z >= Dimensions.Z)
                    {
                        continue;
                    }

                    for (int dx = -effect.Radius; dx <= effect.Radius; dx++)
                    {
                        // Restrict the horizontal area to a disc so the effect carves a rounded crater.
                        if ((dx * dx) + (dz * dz) > radiusSquared)
                        {
                            continue;
                        }

                        int x = effect.Center.X + dx;
                        if (x < 0 || x >= Dimensions.X)
                        {
                            continue;
                        }

                        var coord = new CellCoord(x, y, z);
                        int index = coord.ToFlatIndex(Dimensions);
                        TerrainCell cell = _cells[index];

                        if (!cell.IsSolid)
                        {
                            continue; // already carved out — nothing to modify
                        }

                        if (effect.Power >= cell.Integrity)
                        {
                            _cells[index] = TerrainCell.Empty;
                        }
                        else
                        {
                            cell.Integrity = (byte)(cell.Integrity - effect.Power);
                            _cells[index] = cell;
                        }

                        modified.Add(coord);
                    }
                }
            }

            return modified;
        }

        /// <summary>
        /// Returns true when a structure/unit occupying the rectangular footprint of size
        /// <paramref name="footprint"/> (its X and Z components, in cells) anchored at
        /// <paramref name="footprintOrigin"/> still has supporting terrain beneath it (Req 6.4).
        ///
        /// Support means every cell directly below the footprint (at <c>Origin.Y - 1</c>) is
        /// solid. A footprint resting on the world floor (<c>Origin.Y == 0</c>) is always
        /// supported. A non-positive footprint extent is treated as unsupported. The
        /// <c>Y</c> component of <paramref name="footprint"/> is ignored (footprints are 2D).
        /// </summary>
        public bool IsSupported(CellCoord footprintOrigin, Int3 footprint)
        {
            int width = footprint.X;
            int depth = footprint.Z;

            if (width <= 0 || depth <= 0)
            {
                return false;
            }

            // Anything sitting on the bottom of the volume rests on the world floor.
            if (footprintOrigin.Y <= 0)
            {
                return true;
            }

            int belowY = footprintOrigin.Y - 1;

            for (int dz = 0; dz < depth; dz++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    var below = new CellCoord(footprintOrigin.X + dx, belowY, footprintOrigin.Z + dz);
                    if (!IsSolid(below))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
