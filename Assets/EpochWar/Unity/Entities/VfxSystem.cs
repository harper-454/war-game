using System.Collections.Generic;
using UnityEngine;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.Systems;
using EpochWar.Unity.Bootstrap;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// The Unity-side VFX_System: it plays combat and destruction particle effects keyed off the
    /// <see cref="GameEvent"/>s that flow out of <see cref="MatchSimulation"/> each tick (task 6.1,
    /// Req 5).
    ///
    /// <para><b>Event path.</b> Like the other presentation systems, it subscribes to
    /// <see cref="SimulationDriver.Ticked"/> and consumes the ordered event batch each fixed step — it
    /// never polls Core internals. It reacts to <see cref="CombatResolvedEvent"/> (ranged muzzle flash +
    /// projectile trail + impact/explosion), <see cref="StructureCombatResolvedEvent"/> (impact/explosion
    /// on a struck Structure), <see cref="UnitEliminatedEvent"/> (unit death), <see cref="StructureRemovedEvent"/>
    /// (structure collapse), <see cref="IndirectFireImpactEvent"/> (arcing-fire explosion), and
    /// <see cref="NationEliminatedEvent"/> (the dedicated Doomsday_Weapon deployment effect).</para>
    ///
    /// <para><b>Explosiveness flag (Req 5.3, 5.4).</b> <see cref="CombatResolvedEvent"/> carries no
    /// explosiveness field and Core is frozen for this group, so explosiveness is <em>derived</em> from
    /// the attacking Unit's definition: an attack is explosive when the attacker's
    /// <see cref="UnitDef.AreaEffectRadius"/> is greater than zero or the attacker
    /// <see cref="UnitDef.IsArtillery"/>. Additionally, every <see cref="IndirectFireImpactEvent"/> is
    /// treated as explosive by definition (arcing/indirect fire). A direct hit from a non-area,
    /// non-artillery attacker renders the impact effect; anything area/artillery/indirect renders the
    /// explosion effect. This adds no field to any Core type.</para>
    ///
    /// <para><b>Removal ceilings (Req 5.8).</b> All standard combat/destruction effects are registered
    /// with the <see cref="EffectPool"/> at a 3-second ceiling; the Doomsday deployment effect is
    /// registered at up to a 10-second ceiling and lasts a configured 4–10 seconds (Req 5.7). Particle
    /// counts are scaled by <see cref="EpochWar.Unity.UI.GraphicsSettingsController.ParticleDensity"/>
    /// via the pool.</para>
    ///
    /// <para><b>Position resolution.</b> Death/collapse/Doomsday events report an <em>already-applied</em>
    /// removal, so the entity is gone from <see cref="MatchState"/> by the time the event is observed.
    /// The system therefore keeps a per-entity last-known-position cache, refreshed from live state at the
    /// end of each tick, so a removal event this tick resolves against the position captured last tick.
    /// Everything is null-guarded so an unwired effect prefab or pool logs and continues.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VfxSystem : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Drives the simulation; the system spawns VFX from each tick's event batch.")]
        private SimulationDriver _driver;

        [SerializeField]
        [Tooltip("Pool that owns effect lifetime and particle-density scaling (Req 5.8).")]
        private EffectPool _effectPool;

        [Header("Ranged attack effects (Req 5.1, 5.2)")]
        [SerializeField]
        [Tooltip("Muzzle-flash particle prefab spawned at the firing Unit's origin on a ranged attack.")]
        private GameObject _muzzleFlashEffect;

        [SerializeField]
        [Tooltip("Projectile-trail prefab animated along the attack's flight path. A LineRenderer, if present, is stretched to the impact.")]
        private GameObject _projectileTrailEffect;

        [Header("Impact effects (Req 5.3, 5.4)")]
        [SerializeField]
        [Tooltip("Impact particle prefab for a non-explosive attack.")]
        private GameObject _impactEffect;

        [SerializeField]
        [Tooltip("Explosion particle prefab for an explosive (area/artillery/indirect) attack.")]
        private GameObject _explosionEffect;

        [Header("Destruction effects (Req 5.5, 5.6)")]
        [SerializeField]
        [Tooltip("Unit death particle prefab spawned when a Unit's health reaches zero.")]
        private GameObject _unitDeathEffect;

        [SerializeField]
        [Tooltip("Structure destruction/collapse prefab spawned when a Structure's health reaches zero.")]
        private GameObject _structureCollapseEffect;

        [Header("Doomsday deployment effect (Req 5.7)")]
        [SerializeField]
        [Tooltip("Dedicated Doomsday_Weapon deployment prefab, visually distinct from impact/explosion effects.")]
        private GameObject _doomsdayEffect;

        [Header("Indirect fire trail (Req 15.7)")]
        [SerializeField]
        [Tooltip("Arcing projectile-trail prefab for an Indirect_Fire attack, visible to all Nations for the flight delay. "
            + "A LineRenderer and/or a child named \"Head\"/\"Projectile\", if present, are animated along the arc.")]
        private GameObject _indirectFireTrailEffect;

        [SerializeField]
        [Tooltip("Apex height (world units) of the indirect-fire arc above the straight line. <= 0 auto-derives from shot distance.")]
        private float _indirectFireArcHeight = 0f;

        [Header("Tuning")]
        [SerializeField]
        [Tooltip("Removal ceiling, in seconds, for all standard combat/destruction effects (Req 5.8).")]
        [Range(0.1f, 3f)]
        private float _standardEffectSeconds = 3f;

        [SerializeField]
        [Tooltip("Duration of the Doomsday deployment effect; clamped to 4–10s (Req 5.7, 5.8).")]
        [Range(4f, 10f)]
        private float _doomsdayEffectSeconds = 8f;

        [SerializeField]
        [Tooltip("World-distance beyond which a resolved attack is treated as ranged (muzzle flash + trail).")]
        private float _meleeRangeThreshold = 1.5f;

        // Last-known world positions of live entities, refreshed at the end of every tick, so a removal
        // event observed this tick can be positioned using last tick's snapshot.
        private readonly Dictionary<int, Vector3> _unitPositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, int> _unitNations = new Dictionary<int, int>();
        private readonly Dictionary<int, Vector3> _structurePositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, int> _structureNations = new Dictionary<int, int>();

        // Scratch id lists reused each refresh so the steady-state per-tick pass allocates nothing.
        private readonly List<int> _staleScratch = new List<int>();

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
        /// Binds the system to a driver at runtime (from the match scene wiring) and (re)subscribes to its
        /// tick event, seeding the position cache from the current state. Idempotent.
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

            RefreshPositionCache(_driver != null ? _driver.State : null);
        }

        private void OnTicked(IReadOnlyList<GameEvent> events)
        {
            MatchState state = _driver != null ? _driver.State : null;

            if (events != null)
            {
                foreach (GameEvent evt in events)
                {
                    HandleEvent(evt, state);
                }
            }

            // Refresh AFTER handling so this tick's removal events used last tick's snapshot.
            RefreshPositionCache(state);
        }

        // ------------------------------------------------------------------
        // Event handling
        // ------------------------------------------------------------------

        private void HandleEvent(GameEvent evt, MatchState state)
        {
            switch (evt)
            {
                case CombatResolvedEvent combat:
                    HandleCombatResolved(combat, state);
                    break;

                case StructureCombatResolvedEvent structureCombat:
                    HandleStructureCombat(structureCombat, state);
                    break;

                case UnitEliminatedEvent unitEliminated:
                    if (TryGetUnitPosition(state, unitEliminated.UnitId, out Vector3 deathPos))
                    {
                        SpawnStandard(_unitDeathEffect, deathPos);
                    }

                    break;

                case StructureRemovedEvent structureRemoved:
                    if (TryGetStructurePosition(state, structureRemoved.StructureId, out Vector3 collapsePos))
                    {
                        SpawnStandard(_structureCollapseEffect, collapsePos);
                    }

                    break;

                case IndirectFireImpactEvent impact:
                    // Indirect/arcing fire is explosive by definition (Req 5.4).
                    SpawnStandard(_explosionEffect, UnitView.ToVector3(impact.TargetLocation));
                    break;

                case IndirectFireLaunchedEvent launched:
                    // Arcing projectile trail, visible to all Nations for the whole flight delay (Req 15.7).
                    HandleIndirectFireLaunched(launched, state);
                    break;

                case NationEliminatedEvent doomsday:
                    HandleDoomsday(doomsday);
                    break;
            }
        }

        private void HandleCombatResolved(CombatResolvedEvent combat, MatchState state)
        {
            bool haveAttacker = TryGetUnitPosition(state, combat.AttackerUnitId, out Vector3 attackerPos);
            bool haveDefender = TryGetUnitPosition(state, combat.DefenderUnitId, out Vector3 defenderPos);

            // Impact site is the defender's position; fall back to the attacker's if the defender's is
            // unknown (e.g. removed and never cached).
            Vector3 impactPos = haveDefender ? defenderPos : attackerPos;

            bool explosive = IsExplosiveAttacker(state, combat.AttackerUnitId);

            // A ranged attack is one whose attacker and defender are separated beyond melee range
            // (Req 5.1, 5.2). Without an explicit ranged flag on Core events, distance is the cleanest
            // deterministic presentation-side heuristic.
            bool ranged = haveAttacker && haveDefender
                          && (defenderPos - attackerPos).sqrMagnitude
                             > _meleeRangeThreshold * _meleeRangeThreshold;

            if (ranged)
            {
                SpawnStandard(_muzzleFlashEffect, attackerPos);
                SpawnProjectileTrail(attackerPos, impactPos);
            }

            // Impact vs explosion on arrival (Req 5.3, 5.4).
            SpawnStandard(explosive ? _explosionEffect : _impactEffect, impactPos);
        }

        private void HandleStructureCombat(StructureCombatResolvedEvent combat, MatchState state)
        {
            if (!TryGetStructurePosition(state, combat.DefenderStructureId, out Vector3 pos))
            {
                return;
            }

            bool explosive = IsExplosiveAttacker(state, combat.AttackerUnitId);
            SpawnStandard(explosive ? _explosionEffect : _impactEffect, pos);
        }

        /// <summary>
        /// Spawns an arcing Indirect_Fire projectile trail from the firing Artillery_Unit to the target
        /// for the launched attack's flight delay (Req 15.7). The trail is rented from the pool for
        /// exactly the flight-delay lifetime (so it cleans up as the projectile lands, correlating with
        /// the subsequent <see cref="IndirectFireImpactEvent"/>) and animated by an
        /// <see cref="IndirectFireArcTrail"/>. Because this is keyed off the replicated launch event and
        /// never consults the Vision_System, the trail is visible to every Nation regardless of Spotting.
        /// The firing position falls back to the target when the Artillery_Unit's position is unknown
        /// (e.g. it was removed mid-flight), so the effect still plays rather than being dropped.
        /// </summary>
        private void HandleIndirectFireLaunched(IndirectFireLaunchedEvent launched, MatchState state)
        {
            if (_effectPool == null || _indirectFireTrailEffect == null)
            {
                return;
            }

            Vector3 target = UnitView.ToVector3(launched.TargetLocation);
            Vector3 from = TryGetUnitPosition(state, launched.ArtilleryUnitId, out Vector3 artilleryPos)
                ? artilleryPos
                : target;

            float flightSeconds = Mathf.Max(0.1f, launched.FlightDelay.ToFloat());

            Vector3 direction = target - from;
            Quaternion rotation = direction.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            GameObject trail = _effectPool.Spawn(
                _indirectFireTrailEffect, from, rotation, flightSeconds, scaleByParticleDensity: true);

            if (trail == null)
            {
                return;
            }

            var arc = trail.GetComponent<IndirectFireArcTrail>();
            if (arc == null)
            {
                arc = trail.AddComponent<IndirectFireArcTrail>();
            }

            arc.Play(from, target, flightSeconds, _indirectFireArcHeight);
        }

        private void HandleDoomsday(NationEliminatedEvent doomsday)
        {
            if (_effectPool == null || _doomsdayEffect == null)
            {
                return;
            }

            Vector3 target = ComputeNationCentroid(doomsday.EliminatedNationId, out bool haveTarget);
            if (!haveTarget)
            {
                // No cached forces for the eliminated Nation (all created+removed unseen); anchor at the
                // world origin so the deployment effect still plays rather than being silently dropped.
                target = Vector3.zero;
            }

            float seconds = Mathf.Clamp(_doomsdayEffectSeconds, 4f, 10f);
            _effectPool.Spawn(_doomsdayEffect, target, Quaternion.identity, seconds, scaleByParticleDensity: true);
        }

        // ------------------------------------------------------------------
        // Spawning helpers
        // ------------------------------------------------------------------

        private void SpawnStandard(GameObject prefab, Vector3 position)
        {
            if (_effectPool == null || prefab == null)
            {
                return;
            }

            float seconds = Mathf.Clamp(_standardEffectSeconds, 0.1f, 3f);
            _effectPool.Spawn(prefab, position, Quaternion.identity, seconds, scaleByParticleDensity: true);
        }

        private void SpawnProjectileTrail(Vector3 from, Vector3 to)
        {
            if (_effectPool == null || _projectileTrailEffect == null)
            {
                return;
            }

            Vector3 direction = to - from;
            Quaternion rotation = direction.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(direction.normalized)
                : Quaternion.identity;

            float seconds = Mathf.Clamp(_standardEffectSeconds, 0.1f, 3f);
            GameObject trail = _effectPool.Spawn(
                _projectileTrailEffect, from, rotation, seconds, scaleByParticleDensity: true);

            if (trail == null)
            {
                return;
            }

            // If the trail prefab draws with a LineRenderer, stretch it from origin to impact so the
            // trail spans the actual flight path (Req 5.2). Particle-only trails just play at the origin,
            // oriented toward the impact.
            var line = trail.GetComponentInChildren<LineRenderer>();
            if (line != null)
            {
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.SetPosition(0, from);
                line.SetPosition(1, to);
            }
        }

        // ------------------------------------------------------------------
        // Explosiveness + position resolution
        // ------------------------------------------------------------------

        /// <summary>
        /// Derives the explosiveness flag for an attack from the attacking Unit's definition: explosive
        /// when the attacker has an Area_Effect radius or is an Artillery_Unit (Req 5.3, 5.4). A removed
        /// attacker (unknown def) is treated as non-explosive.
        /// </summary>
        private static bool IsExplosiveAttacker(MatchState state, int attackerUnitId)
        {
            if (state != null
                && state.Units.TryGetValue(attackerUnitId, out UnitInstance attacker)
                && attacker.Def != null)
            {
                return attacker.Def.AreaEffectRadius > Fixed.Zero || attacker.Def.IsArtillery;
            }

            return false;
        }

        private bool TryGetUnitPosition(MatchState state, int unitId, out Vector3 position)
        {
            if (state != null && state.Units.TryGetValue(unitId, out UnitInstance unit))
            {
                position = UnitView.ToVector3(unit.Position);
                return true;
            }

            return _unitPositions.TryGetValue(unitId, out position);
        }

        private bool TryGetStructurePosition(MatchState state, int structureId, out Vector3 position)
        {
            if (state != null && state.Structures.TryGetValue(structureId, out StructureInstance structure))
            {
                position = StructureView.ToVector3(structure.Origin);
                return true;
            }

            return _structurePositions.TryGetValue(structureId, out position);
        }

        /// <summary>
        /// Averages the cached last-known positions of every Unit and Structure that belonged to
        /// <paramref name="nationId"/>, used to anchor the Doomsday deployment effect at the eliminated
        /// Nation's forces (Req 5.7). <paramref name="found"/> is false when nothing was cached for it.
        /// </summary>
        private Vector3 ComputeNationCentroid(int nationId, out bool found)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (KeyValuePair<int, int> pair in _unitNations)
            {
                if (pair.Value == nationId && _unitPositions.TryGetValue(pair.Key, out Vector3 p))
                {
                    sum += p;
                    count++;
                }
            }

            foreach (KeyValuePair<int, int> pair in _structureNations)
            {
                if (pair.Value == nationId && _structurePositions.TryGetValue(pair.Key, out Vector3 p))
                {
                    sum += p;
                    count++;
                }
            }

            found = count > 0;
            return found ? sum / count : Vector3.zero;
        }

        private void RefreshPositionCache(MatchState state)
        {
            if (state == null)
            {
                return;
            }

            // Units.
            foreach (KeyValuePair<int, UnitInstance> pair in state.Units)
            {
                _unitPositions[pair.Key] = UnitView.ToVector3(pair.Value.Position);
                _unitNations[pair.Key] = pair.Value.OwnerNationId;
            }

            _staleScratch.Clear();
            foreach (int id in _unitPositions.Keys)
            {
                if (!state.Units.ContainsKey(id))
                {
                    _staleScratch.Add(id);
                }
            }

            foreach (int id in _staleScratch)
            {
                _unitPositions.Remove(id);
                _unitNations.Remove(id);
            }

            // Structures.
            foreach (KeyValuePair<int, StructureInstance> pair in state.Structures)
            {
                _structurePositions[pair.Key] = StructureView.ToVector3(pair.Value.Origin);
                _structureNations[pair.Key] = pair.Value.OwnerNationId;
            }

            _staleScratch.Clear();
            foreach (int id in _structurePositions.Keys)
            {
                if (!state.Structures.ContainsKey(id))
                {
                    _staleScratch.Add(id);
                }
            }

            foreach (int id in _staleScratch)
            {
                _structurePositions.Remove(id);
                _structureNations.Remove(id);
            }
        }
    }
}
