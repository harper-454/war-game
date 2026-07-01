using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// Renders the destructible <see cref="TerrainVolume"/> as chunked meshes and rebuilds only the
    /// chunks whose cells actually changed (task 15.2 + tasks 3/5, Req 3, Req 4, Req 6.1, 6.2).
    ///
    /// The volume is a dense cell field grouped into fixed-size cubic chunks of
    /// <see cref="TerrainVolume.ChunkSize"/> cells per axis. On first bind the renderer builds a mesh
    /// for every non-empty chunk; thereafter it listens to <see cref="SimulationDriver.Ticked"/> and,
    /// for each <see cref="TerrainModifiedEvent"/> that tick, maps the modified <see cref="CellCoord"/>s
    /// to their owning chunks and marks only those dirty. Dirty chunk meshes are rebuilt once in
    /// <see cref="LateUpdate"/>, so a weapon crater re-meshes a handful of chunks rather than the whole
    /// battlefield.
    ///
    /// <para><b>Material layering (task 3, Req 3).</b> Faces are grouped <em>by their cell's
    /// <see cref="CellMaterial"/></em> into one submesh per material, and the chunk's
    /// <see cref="MeshRenderer.sharedMaterials"/> array is assigned the <see cref="Material"/> mapped to
    /// each material via the serialized <see cref="_cellMaterials"/> table (a URP-lit diffuse + normal
    /// material per terrain type, Req 3.1, 3.2). Meshing therefore emits per-face UVs (for the diffuse
    /// texture) and recalculates tangents (so the normal map lights correctly under URP, Req 3.4). A
    /// terrain type with no material assigned falls back to <see cref="_fallbackCellMaterial"/> and the
    /// cell is still meshed — a missing assignment never skips a cell or fails the rebuild (Req 3.5).
    /// Because every dirty-chunk rebuild re-reads each cell's current material and re-assigns the
    /// submesh materials, a cell whose terrain type changed is re-materialised within the same chunk
    /// rebuild the terrain modification already triggers (Req 3.3) — no extra event or code path.</para>
    ///
    /// <para><b>Destruction VFX (task 5, Req 4).</b> The existing per-tick
    /// <see cref="TerrainModifiedEvent"/> handler additionally drives destruction effects through the
    /// <see cref="EffectPool"/>: a dust/debris burst at every modification (Req 4.1, capped at &lt;= 5s,
    /// Req 4.6); a crater decal when the effect <em>removed</em> cells and its destructive
    /// <see cref="TerrainEffect.Power"/> met or exceeded <see cref="_destructiveForceThreshold"/>
    /// (Req 4.2) and none otherwise (Req 4.3); a scorch-mark decal when cells were damaged but not
    /// removed (Req 4.4); and neither decal for an excavation-only modification with no correlated
    /// weapon/ability effect in the same tick's event batch (Req 4.5).</para>
    ///
    /// The renderer is pure presentation — it reads the volume and never mutates it.
    /// </summary>
    public sealed class TerrainRenderer : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Drives the simulation; the renderer re-meshes changed chunks after each tick.")]
        private SimulationDriver _driver;

        [Header("Cell materials (Req 3)")]
        [SerializeField]
        [Tooltip("URP-lit material (diffuse + normal) per terrain CellMaterial. Missing entries use the fallback.")]
        private CellMaterialEntry[] _cellMaterials = System.Array.Empty<CellMaterialEntry>();

        [SerializeField]
        [Tooltip("Fallback material rendered for any terrain type with no assigned Cell_Material (Req 3.5).")]
        private Material _fallbackCellMaterial;

        [SerializeField]
        [Tooltip("Legacy single material; used as the last-resort fallback when no Cell_Material/fallback is set.")]
        private Material _terrainMaterial;

        [Header("Destruction VFX (Req 4)")]
        [SerializeField]
        [Tooltip("Pool that owns dust/debris lifetime (Req 4.6). Optional; destruction VFX is skipped when unset.")]
        private EffectPool _effectPool;

        [SerializeField]
        [Tooltip("Dust-and-debris particle prefab spawned at every terrain modification (Req 4.1).")]
        private GameObject _dustDebrisEffect;

        [SerializeField]
        [Tooltip("Decal prefab placed at excavation sites when destructive force meets the threshold (Req 4.2).")]
        private GameObject _craterDecal;

        [SerializeField]
        [Tooltip("Decal prefab placed on cells that were damaged but not removed (Req 4.4).")]
        private GameObject _scorchDecal;

        [SerializeField]
        [Tooltip("Optional parent for spawned crater/scorch decals; defaults to this transform.")]
        private Transform _decalRoot;

        [SerializeField]
        [Tooltip("Minimum TerrainEffect.Power that removes cells for a crater decal to be rendered (Req 4.2, 4.3).")]
        private int _destructiveForceThreshold = 4;

        [SerializeField]
        [Tooltip("Lifetime, in seconds, of a dust/debris effect. Clamped to <= 5s per Req 4.6.")]
        [Range(0.1f, 5f)]
        private float _dustDebrisLifetimeSeconds = 5f;

        private TerrainVolume _volume;
        private Int3 _dims;
        private int _chunksX;
        private int _chunksY;
        private int _chunksZ;

        private readonly Dictionary<int, Chunk> _chunks = new Dictionary<int, Chunk>();
        private readonly HashSet<int> _dirtyChunks = new HashSet<int>();

        // CellMaterial -> render Material lookup, built from the serialized table on bind.
        private readonly Dictionary<CellMaterial, Material> _materialLookup =
            new Dictionary<CellMaterial, Material>();

        // Solid materials are grouped into submeshes in this stable, deterministic order.
        private static readonly CellMaterial[] SolidMaterialOrder =
        {
            CellMaterial.Soil,
            CellMaterial.Rock,
            CellMaterial.Sand,
            CellMaterial.Reinforced,
        };

        // Reused per-rebuild buffers, one per solid material, so a steady rebuild allocates little.
        private readonly Dictionary<CellMaterial, MaterialBuffer> _buffers =
            new Dictionary<CellMaterial, MaterialBuffer>();

        private void OnEnable()
        {
            if (_driver != null)
            {
                _driver.Ticked += OnTicked;
            }
        }

        private void OnDisable()
        {
            if (_driver != null)
            {
                _driver.Ticked -= OnTicked;
            }
        }

        /// <summary>
        /// Binds the renderer to a driver, captures its terrain volume, and marks every chunk dirty so
        /// the whole battlefield is meshed on the next <see cref="LateUpdate"/>. Call from the match
        /// scene wiring once the Match is assembled.
        /// </summary>
        public void Bind(SimulationDriver driver)
        {
            if (_driver != null)
            {
                _driver.Ticked -= OnTicked;
            }

            _driver = driver;

            if (_driver != null && isActiveAndEnabled)
            {
                _driver.Ticked += OnTicked;
            }

            BuildMaterialLookup();
            InitializeVolume();
        }

        private void BuildMaterialLookup()
        {
            _materialLookup.Clear();
            if (_cellMaterials == null)
            {
                return;
            }

            foreach (CellMaterialEntry entry in _cellMaterials)
            {
                if (entry.Material != null)
                {
                    // Later entries override earlier duplicates deterministically.
                    _materialLookup[entry.Terrain] = entry.Material;
                }
            }
        }

        /// <summary>
        /// Resolves the render <see cref="Material"/> for a terrain <paramref name="material"/>, falling
        /// back to the defined fallback (then the legacy single material) when no dedicated Cell_Material
        /// is assigned. Never throws; returning null still meshes the cell with an empty material slot so
        /// the rebuild always completes (Req 3.5).
        /// </summary>
        private Material ResolveMaterial(CellMaterial material)
        {
            if (_materialLookup.TryGetValue(material, out Material mat) && mat != null)
            {
                return mat;
            }

            if (_fallbackCellMaterial != null)
            {
                return _fallbackCellMaterial;
            }

            return _terrainMaterial;
        }

        private void InitializeVolume()
        {
            _volume = _driver != null && _driver.State != null ? _driver.State.Terrain : null;
            if (_volume == null)
            {
                return;
            }

            _dims = _volume.Dimensions;
            int size = TerrainVolume.ChunkSize;
            _chunksX = CeilDiv(_dims.X, size);
            _chunksY = CeilDiv(_dims.Y, size);
            _chunksZ = CeilDiv(_dims.Z, size);

            // Mark every chunk dirty for the initial full build.
            _dirtyChunks.Clear();
            for (int cz = 0; cz < _chunksZ; cz++)
            {
                for (int cy = 0; cy < _chunksY; cy++)
                {
                    for (int cx = 0; cx < _chunksX; cx++)
                    {
                        _dirtyChunks.Add(ChunkIndex(cx, cy, cz));
                    }
                }
            }
        }

        private void OnTicked(IReadOnlyList<GameEvent> events)
        {
            if (_volume == null)
            {
                InitializeVolume();
            }

            if (events == null)
            {
                return;
            }

            // Req 4.5: a terrain modification renders crater/scorch decals only when it is correlated
            // with a weapon or ability effect this tick. An excavation-only tick (a TerrainModifiedEvent
            // with no accompanying combat/ability event) still spawns dust/debris but suppresses decals.
            bool weaponEffectThisTick = HasCorrelatedWeaponEffect(events);

            int size = TerrainVolume.ChunkSize;
            foreach (var evt in events)
            {
                if (!(evt is TerrainModifiedEvent modified))
                {
                    continue;
                }

                foreach (var cell in modified.ModifiedCells)
                {
                    MarkCellDirty(cell, size);
                }

                SpawnDestructionEffects(modified, weaponEffectThisTick);
            }
        }

        /// <summary>
        /// True when the tick's event batch contains a weapon or ability effect that a
        /// <see cref="TerrainModifiedEvent"/> can be correlated with (Req 4.5). Terrain destruction
        /// caused by combat therefore renders decals, while pure excavation (with no such event) does not.
        /// </summary>
        private static bool HasCorrelatedWeaponEffect(IReadOnlyList<GameEvent> events)
        {
            foreach (var evt in events)
            {
                if (evt is CombatResolvedEvent
                    || evt is StructureCombatResolvedEvent
                    || evt is IndirectFireImpactEvent
                    || evt is AbilityActivatedEvent)
                {
                    return true;
                }
            }

            return false;
        }

        // ------------------------------------------------------------------
        // Destruction VFX (task 5, Req 4)
        // ------------------------------------------------------------------

        private void SpawnDestructionEffects(TerrainModifiedEvent modified, bool weaponEffect)
        {
            if (_volume == null || modified.ModifiedCells == null || modified.ModifiedCells.Count == 0)
            {
                return;
            }

            // Classify the modified cells against the post-effect volume: a cell that is no longer solid
            // was removed; a cell still solid was damaged but not removed.
            bool anyRemoved = false;
            bool anyDamaged = false;
            Vector3 removedSum = Vector3.zero;
            int removedCount = 0;
            CellCoord damagedTop = default;
            bool haveDamagedTop = false;
            Vector3 allSum = Vector3.zero;

            foreach (CellCoord cell in modified.ModifiedCells)
            {
                allSum += CellCenterLocal(cell);

                if (_volume.IsSolid(cell))
                {
                    anyDamaged = true;
                    if (!haveDamagedTop || cell.Y > damagedTop.Y)
                    {
                        damagedTop = cell;
                        haveDamagedTop = true;
                    }
                }
                else
                {
                    anyRemoved = true;
                    removedSum += CellCenterLocal(cell);
                    removedCount++;
                }
            }

            int cellCount = modified.ModifiedCells.Count;
            Vector3 centroidWorld = transform.TransformPoint(allSum / cellCount);

            // Req 4.1 / 4.6: dust and debris at every modification, dug or blasted, capped at <= 5s.
            if (_effectPool != null && _dustDebrisEffect != null)
            {
                float dustLifetime = Mathf.Clamp(_dustDebrisLifetimeSeconds, 0.1f, 5f);
                _effectPool.Spawn(_dustDebrisEffect, centroidWorld, dustLifetime);
            }

            // Req 4.5: decals only for weapon/ability-correlated destruction.
            if (!weaponEffect)
            {
                return;
            }

            // Req 4.2 / 4.3: crater decal only when cells were removed AND the destructive force met the
            // threshold; below the threshold (or with no removal) no crater is rendered.
            if (anyRemoved && _craterDecal != null && modified.Effect.Power >= _destructiveForceThreshold)
            {
                Vector3 craterLocal = removedCount > 0 ? removedSum / removedCount : allSum / cellCount;
                // Sit the crater on the top surface of the excavation.
                craterLocal.y += 0.5f;
                InstantiateDecal(_craterDecal, transform.TransformPoint(craterLocal));
            }

            // Req 4.4: scorch mark on cells damaged but not fully removed.
            if (anyDamaged && haveDamagedTop && _scorchDecal != null)
            {
                Vector3 scorchLocal = CellCenterLocal(damagedTop);
                scorchLocal.y += 0.5f; // lay it on the top face of the damaged cell
                InstantiateDecal(_scorchDecal, transform.TransformPoint(scorchLocal));
            }
        }

        /// <summary>
        /// Instantiates a persistent decal at <paramref name="worldPosition"/>, parented to the decal
        /// root. Decals are terrain scars with no required removal ceiling (Req 4 only bounds the
        /// dust/debris lifetime), so they are not pooled. A null prefab is ignored (graceful degradation).
        /// </summary>
        private void InstantiateDecal(GameObject prefab, Vector3 worldPosition)
        {
            if (prefab == null)
            {
                return;
            }

            Transform parent = _decalRoot != null ? _decalRoot : transform;
            Quaternion rotation = prefab.transform.rotation;
            Instantiate(prefab, worldPosition, rotation, parent);
        }

        /// <summary>The local-space center of a terrain cell (one cell == one unit).</summary>
        private static Vector3 CellCenterLocal(CellCoord c)
            => new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);

        // ------------------------------------------------------------------
        // Chunk dirtying + rebuild
        // ------------------------------------------------------------------

        /// <summary>Marks the chunk owning <paramref name="cell"/> dirty, plus any neighbor chunk it borders.</summary>
        private void MarkCellDirty(CellCoord cell, int size)
        {
            int cx = cell.X / size;
            int cy = cell.Y / size;
            int cz = cell.Z / size;
            MarkChunkDirty(cx, cy, cz);

            // A cell on a chunk boundary exposes/hides a face on the adjacent chunk too, so that chunk
            // must be re-meshed as well to stay seamless.
            if (cell.X % size == 0) MarkChunkDirty(cx - 1, cy, cz);
            if (cell.X % size == size - 1) MarkChunkDirty(cx + 1, cy, cz);
            if (cell.Y % size == 0) MarkChunkDirty(cx, cy - 1, cz);
            if (cell.Y % size == size - 1) MarkChunkDirty(cx, cy + 1, cz);
            if (cell.Z % size == 0) MarkChunkDirty(cx, cy, cz - 1);
            if (cell.Z % size == size - 1) MarkChunkDirty(cx, cy, cz + 1);
        }

        private void MarkChunkDirty(int cx, int cy, int cz)
        {
            if (cx < 0 || cy < 0 || cz < 0 || cx >= _chunksX || cy >= _chunksY || cz >= _chunksZ)
            {
                return;
            }

            _dirtyChunks.Add(ChunkIndex(cx, cy, cz));
        }

        private void LateUpdate()
        {
            if (_volume == null || _dirtyChunks.Count == 0)
            {
                return;
            }

            foreach (int chunkIndex in _dirtyChunks)
            {
                RebuildChunk(chunkIndex);
            }

            _dirtyChunks.Clear();
        }

        private void RebuildChunk(int chunkIndex)
        {
            DecodeChunkIndex(chunkIndex, out int cx, out int cy, out int cz);

            int size = TerrainVolume.ChunkSize;
            int baseX = cx * size;
            int baseY = cy * size;
            int baseZ = cz * size;

            ResetBuffers();

            int maxX = Mathf.Min(baseX + size, _dims.X);
            int maxY = Mathf.Min(baseY + size, _dims.Y);
            int maxZ = Mathf.Min(baseZ + size, _dims.Z);

            for (int y = baseY; y < maxY; y++)
            {
                for (int z = baseZ; z < maxZ; z++)
                {
                    for (int x = baseX; x < maxX; x++)
                    {
                        var coord = new CellCoord(x, y, z);
                        TerrainCell cell = _volume.Get(coord);
                        if (!cell.IsSolid)
                        {
                            continue;
                        }

                        MaterialBuffer buffer = GetBuffer(cell.Material);
                        Color32 tint = MaterialTint(cell.Material);
                        AddExposedFaces(coord, buffer, tint);
                    }
                }
            }

            Chunk chunk = GetOrCreateChunk(chunkIndex, cx, cy, cz);
            ApplyBuffersToChunk(chunk);
        }

        /// <summary>
        /// Combines the per-material buffers into a single multi-submesh mesh (one submesh per material
        /// present) and assigns the chunk's <see cref="MeshRenderer.sharedMaterials"/> to the resolved
        /// Cell_Materials in the same order (Req 3.1, 3.5). An empty chunk clears its mesh and materials.
        /// </summary>
        private void ApplyBuffersToChunk(Chunk chunk)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var submeshTriangles = new List<List<int>>();
            var materials = new List<Material>();

            foreach (CellMaterial material in SolidMaterialOrder)
            {
                if (!_buffers.TryGetValue(material, out MaterialBuffer buffer) || buffer.Triangles.Count == 0)
                {
                    continue;
                }

                int baseIndex = vertices.Count;
                vertices.AddRange(buffer.Vertices);
                uvs.AddRange(buffer.Uvs);
                colors.AddRange(buffer.Colors);

                var tris = new List<int>(buffer.Triangles.Count);
                foreach (int t in buffer.Triangles)
                {
                    tris.Add(t + baseIndex);
                }

                submeshTriangles.Add(tris);
                materials.Add(ResolveMaterial(material));
            }

            Mesh mesh = chunk.Mesh;
            mesh.Clear();

            if (vertices.Count == 0)
            {
                // Fully carved-out chunk: render nothing.
                mesh.subMeshCount = 0;
                chunk.MeshRenderer.sharedMaterials = System.Array.Empty<Material>();
                return;
            }

            // 32-bit indices so a dense chunk can exceed the 16-bit vertex limit.
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.subMeshCount = submeshTriangles.Count;
            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                mesh.SetTriangles(submeshTriangles[i], i);
            }

            mesh.RecalculateNormals();
            mesh.RecalculateTangents(); // required so Cell_Material normal maps light correctly (Req 3.4)
            mesh.RecalculateBounds();

            chunk.MeshRenderer.sharedMaterials = materials.ToArray();
        }

        private void ResetBuffers()
        {
            foreach (MaterialBuffer buffer in _buffers.Values)
            {
                buffer.Clear();
            }
        }

        private MaterialBuffer GetBuffer(CellMaterial material)
        {
            if (!_buffers.TryGetValue(material, out MaterialBuffer buffer))
            {
                buffer = new MaterialBuffer();
                _buffers[material] = buffer;
            }

            return buffer;
        }

        private Chunk GetOrCreateChunk(int chunkIndex, int cx, int cy, int cz)
        {
            if (_chunks.TryGetValue(chunkIndex, out var chunk))
            {
                return chunk;
            }

            var go = new GameObject($"TerrainChunk_{cx}_{cy}_{cz}");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var filter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = $"TerrainChunkMesh_{cx}_{cy}_{cz}" };
            filter.sharedMesh = mesh;

            chunk = new Chunk(go, mesh, meshRenderer);
            _chunks[chunkIndex] = chunk;
            return chunk;
        }

        // ---- Cubic face emission (only faces bordering non-solid cells) ----

        private void AddExposedFaces(CellCoord c, MaterialBuffer buffer, Color32 color)
        {
            float x = c.X;
            float y = c.Y;
            float z = c.Z;

            // +Y (top)
            if (!_volume.IsSolid(new CellCoord(c.X, c.Y + 1, c.Z)))
            {
                AddQuad(buffer, color,
                    new Vector3(x, y + 1, z), new Vector3(x, y + 1, z + 1),
                    new Vector3(x + 1, y + 1, z + 1), new Vector3(x + 1, y + 1, z));
            }

            // -Y (bottom)
            if (!_volume.IsSolid(new CellCoord(c.X, c.Y - 1, c.Z)))
            {
                AddQuad(buffer, color,
                    new Vector3(x, y, z + 1), new Vector3(x, y, z),
                    new Vector3(x + 1, y, z), new Vector3(x + 1, y, z + 1));
            }

            // +X
            if (!_volume.IsSolid(new CellCoord(c.X + 1, c.Y, c.Z)))
            {
                AddQuad(buffer, color,
                    new Vector3(x + 1, y, z), new Vector3(x + 1, y + 1, z),
                    new Vector3(x + 1, y + 1, z + 1), new Vector3(x + 1, y, z + 1));
            }

            // -X
            if (!_volume.IsSolid(new CellCoord(c.X - 1, c.Y, c.Z)))
            {
                AddQuad(buffer, color,
                    new Vector3(x, y, z + 1), new Vector3(x, y + 1, z + 1),
                    new Vector3(x, y + 1, z), new Vector3(x, y, z));
            }

            // +Z
            if (!_volume.IsSolid(new CellCoord(c.X, c.Y, c.Z + 1)))
            {
                AddQuad(buffer, color,
                    new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y + 1, z + 1),
                    new Vector3(x, y + 1, z + 1), new Vector3(x, y, z + 1));
            }

            // -Z
            if (!_volume.IsSolid(new CellCoord(c.X, c.Y, c.Z - 1)))
            {
                AddQuad(buffer, color,
                    new Vector3(x, y, z), new Vector3(x, y + 1, z),
                    new Vector3(x + 1, y + 1, z), new Vector3(x + 1, y, z));
            }
        }

        private static void AddQuad(
            MaterialBuffer buffer, Color32 color, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int start = buffer.Vertices.Count;
            buffer.Vertices.Add(a);
            buffer.Vertices.Add(b);
            buffer.Vertices.Add(c);
            buffer.Vertices.Add(d);

            // Planar quad UVs so each face samples the full diffuse/normal texture (Req 3.2).
            buffer.Uvs.Add(new Vector2(0f, 0f));
            buffer.Uvs.Add(new Vector2(0f, 1f));
            buffer.Uvs.Add(new Vector2(1f, 1f));
            buffer.Uvs.Add(new Vector2(1f, 0f));

            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);
            buffer.Colors.Add(color);

            buffer.Triangles.Add(start);
            buffer.Triangles.Add(start + 1);
            buffer.Triangles.Add(start + 2);
            buffer.Triangles.Add(start);
            buffer.Triangles.Add(start + 2);
            buffer.Triangles.Add(start + 3);
        }

        /// <summary>
        /// A per-material vertex-color tint applied on top of the assigned Cell_Material. It gives each
        /// terrain type a distinct look even when a material has no texture assigned yet (graceful
        /// degradation), and modulates the diffuse map otherwise.
        /// </summary>
        private static Color32 MaterialTint(CellMaterial material)
        {
            switch (material)
            {
                case CellMaterial.Soil: return new Color32(110, 78, 48, 255);
                case CellMaterial.Rock: return new Color32(120, 120, 128, 255);
                case CellMaterial.Sand: return new Color32(214, 197, 140, 255);
                case CellMaterial.Reinforced: return new Color32(70, 74, 88, 255);
                default: return new Color32(255, 255, 255, 255);
            }
        }

        // ---- Chunk indexing helpers ----

        private int ChunkIndex(int cx, int cy, int cz) => cx + (_chunksX * (cy + (_chunksY * cz)));

        private void DecodeChunkIndex(int index, out int cx, out int cy, out int cz)
        {
            cx = index % _chunksX;
            int rest = index / _chunksX;
            cy = rest % _chunksY;
            cz = rest / _chunksY;
        }

        private static int CeilDiv(int value, int divisor)
        {
            if (value <= 0)
            {
                return 0;
            }

            return (value + divisor - 1) / divisor;
        }

        /// <summary>Number of chunks currently instantiated (diagnostic/testing aid).</summary>
        public int ChunkCount => _chunks.Count;

        /// <summary>Number of chunks pending a mesh rebuild on the next LateUpdate.</summary>
        public int DirtyChunkCount => _dirtyChunks.Count;

        /// <summary>
        /// A serializable mapping from a terrain <see cref="CellMaterial"/> to the URP-lit render
        /// <see cref="Material"/> (diffuse + normal) used for its faces (Req 3.1). Authored on the
        /// <see cref="TerrainRenderer"/> component in the Editor.
        /// </summary>
        [System.Serializable]
        public struct CellMaterialEntry
        {
            [Tooltip("The terrain cell material this entry maps.")]
            public CellMaterial Terrain;

            [Tooltip("The URP-lit material (Base Map + Normal Map) rendered for that terrain material.")]
            public Material Material;
        }

        /// <summary>Per-material geometry accumulated while rebuilding one chunk into submeshes.</summary>
        private sealed class MaterialBuffer
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<Color32> Colors = new List<Color32>();
            public readonly List<int> Triangles = new List<int>();

            public void Clear()
            {
                Vertices.Clear();
                Uvs.Clear();
                Colors.Clear();
                Triangles.Clear();
            }
        }

        private sealed class Chunk
        {
            public Chunk(GameObject gameObject, Mesh mesh, MeshRenderer meshRenderer)
            {
                GameObject = gameObject;
                Mesh = mesh;
                MeshRenderer = meshRenderer;
            }

            public GameObject GameObject { get; }
            public Mesh Mesh { get; }
            public MeshRenderer MeshRenderer { get; }
        }
    }
}
