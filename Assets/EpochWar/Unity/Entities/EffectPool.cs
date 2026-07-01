using System.Collections.Generic;
using UnityEngine;
using EpochWar.Unity.UI;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// Pooled particle/decal lifetime management for the Visuals pillar (task 4.1, Req 4.6, 5.8).
    ///
    /// <para><b>Why pooling.</b> Combat and terrain destruction spawn short-lived particle and decal
    /// effects continuously during a large-scale battle. Instantiating and destroying a fresh
    /// <see cref="GameObject"/> per effect churns the garbage collector and leaks transient objects
    /// into the scene. This pool instead rents an instance per prefab, keeps a per-prefab stack of
    /// idle instances, and <em>returns</em> spent instances to that stack rather than destroying them,
    /// so a steady stream of effects reuses a bounded set of objects.</para>
    ///
    /// <para><b>Automatic removal ceilings (Req 4.6, 5.8).</b> Every <see cref="Spawn(GameObject,Vector3,float)"/>
    /// call takes a per-effect lifetime in seconds; an <see cref="Update"/>-driven timer counts each live
    /// instance down and returns it to the pool the moment its lifetime elapses. Callers pass their
    /// category's removal ceiling — the <see cref="TerrainRenderer"/> passes <c>&lt;= 5s</c> for
    /// dust/debris (Req 4.6), and the <see cref="VfxSystem"/> passes <c>3s</c> for standard
    /// combat/destruction effects and <c>10s</c> for the Doomsday deployment effect (Req 5.8). The pool
    /// enforces the ceiling by returning the instance no later than the requested lifetime; there is no
    /// path by which a pooled instance outlives its ceiling.</para>
    ///
    /// <para><b>Particle-density scaling.</b> When requested (the default), spawned instances have their
    /// <see cref="ParticleSystem"/> emission scaled by the live
    /// <see cref="GraphicsSettingsController.ParticleDensity"/> setting. Baselines are captured per
    /// instance the first time it is rented, so repeated reuse never compounds the scaling.</para>
    ///
    /// <para>The pool is presentation-only and degrades gracefully: a null prefab is logged and ignored
    /// (returning <c>null</c>) rather than throwing, so an unwired effect reference never breaks a Match.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EffectPool : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Optional parent for idle (returned) pooled instances. Defaults to this transform.")]
        private Transform _poolRoot;

        [SerializeField]
        [Tooltip("When true, spawned particle systems are scaled by GraphicsSettingsController.ParticleDensity by default.")]
        private bool _scaleParticlesByDensity = true;

        // Idle (available) instances keyed by their source prefab.
        private readonly Dictionary<GameObject, Stack<GameObject>> _idle =
            new Dictionary<GameObject, Stack<GameObject>>();

        // Live instances counting down toward their removal ceiling.
        private readonly List<ActiveEffect> _active = new List<ActiveEffect>();

        // Captured emission baselines per instance so density scaling is idempotent across reuse.
        private readonly Dictionary<GameObject, List<ParticleBaseline>> _baselines =
            new Dictionary<GameObject, List<ParticleBaseline>>();

        /// <summary>Number of instances currently live (spawned and not yet returned). Diagnostic aid.</summary>
        public int ActiveCount => _active.Count;

        /// <summary>Total number of idle (returned, reusable) instances across every pooled prefab.</summary>
        public int IdleCount
        {
            get
            {
                int total = 0;
                foreach (var stack in _idle.Values)
                {
                    total += stack.Count;
                }

                return total;
            }
        }

        /// <summary>
        /// Spawns a pooled instance of <paramref name="prefab"/> at <paramref name="position"/> with an
        /// identity rotation, applying the default particle-density scaling, and schedules its automatic
        /// return after <paramref name="lifetimeSeconds"/>. Returns the live instance, or <c>null</c> when
        /// <paramref name="prefab"/> is unset.
        /// </summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, float lifetimeSeconds)
            => Spawn(prefab, position, Quaternion.identity, lifetimeSeconds, _scaleParticlesByDensity);

        /// <summary>
        /// Spawns a pooled instance of <paramref name="prefab"/> at the given
        /// <paramref name="position"/>/<paramref name="rotation"/> and schedules its automatic return to
        /// the pool after <paramref name="lifetimeSeconds"/> (the effect's removal ceiling, Req 4.6/5.8).
        /// When <paramref name="scaleByParticleDensity"/> is true the instance's particle emission is
        /// scaled by <see cref="GraphicsSettingsController.ParticleDensity"/>. Returns the live instance,
        /// or <c>null</c> when <paramref name="prefab"/> is unset (logged, never throws).
        /// </summary>
        public GameObject Spawn(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            float lifetimeSeconds,
            bool scaleByParticleDensity)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[EffectPool:{name}] Spawn called with a null prefab; skipping (graceful degradation).");
                return null;
            }

            GameObject instance = Rent(prefab);
            instance.transform.SetParent(_poolRoot != null ? _poolRoot : transform, worldPositionStays: false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);

            if (scaleByParticleDensity)
            {
                ApplyParticleDensity(instance, Mathf.Max(0f, GraphicsSettingsController.ParticleDensity));
            }

            RestartParticles(instance);

            _active.Add(new ActiveEffect(instance, prefab, Mathf.Max(0f, lifetimeSeconds)));
            return instance;
        }

        /// <summary>
        /// Returns a live instance to its idle pool immediately, before its lifetime elapses. Safe to
        /// call with a null or already-returned instance (no-op). Used when an effect's natural end is
        /// known ahead of its ceiling (e.g. a projectile trail that reaches its impact).
        /// </summary>
        public void Return(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Instance == instance)
                {
                    ReturnAt(i);
                    return;
                }
            }
        }

        private void Update()
        {
            if (_active.Count == 0)
            {
                return;
            }

            float dt = Time.deltaTime;

            // Iterate backwards so returning (removing) an entry does not skip the next one.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ActiveEffect effect = _active[i];

                // An instance destroyed out from under the pool (e.g. scene teardown) is dropped.
                if (effect.Instance == null)
                {
                    _active.RemoveAt(i);
                    continue;
                }

                effect.Remaining -= dt;
                if (effect.Remaining <= 0f)
                {
                    ReturnAt(i);
                }
                else
                {
                    _active[i] = effect;
                }
            }
        }

        // ------------------------------------------------------------------
        // Pool rent / return
        // ------------------------------------------------------------------

        private GameObject Rent(GameObject prefab)
        {
            if (_idle.TryGetValue(prefab, out var stack) && stack.Count > 0)
            {
                return stack.Pop();
            }

            GameObject instance = Instantiate(prefab, _poolRoot != null ? _poolRoot : transform);
            instance.name = $"{prefab.name}(Pooled)";
            CaptureParticleBaselines(instance);
            return instance;
        }

        private void ReturnAt(int activeIndex)
        {
            ActiveEffect effect = _active[activeIndex];
            _active.RemoveAt(activeIndex);

            GameObject instance = effect.Instance;
            if (instance != null)
            {
                StopParticles(instance);
                instance.SetActive(false);
                instance.transform.SetParent(_poolRoot != null ? _poolRoot : transform, worldPositionStays: false);

                if (!_idle.TryGetValue(effect.Prefab, out var stack))
                {
                    stack = new Stack<GameObject>();
                    _idle[effect.Prefab] = stack;
                }

                stack.Push(instance);
            }
        }

        // ------------------------------------------------------------------
        // Particle lifecycle + density scaling
        // ------------------------------------------------------------------

        private static void RestartParticles(GameObject instance)
        {
            var systems = instance.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            foreach (var ps in systems)
            {
                ps.Clear(withChildren: true);
                ps.Play(withChildren: true);
            }
        }

        private static void StopParticles(GameObject instance)
        {
            var systems = instance.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            foreach (var ps in systems)
            {
                ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void CaptureParticleBaselines(GameObject instance)
        {
            if (_baselines.ContainsKey(instance))
            {
                return;
            }

            var systems = instance.GetComponentsInChildren<ParticleSystem>(includeInactive: true);
            var baselines = new List<ParticleBaseline>(systems.Length);
            foreach (var ps in systems)
            {
                ParticleSystem.EmissionModule emission = ps.emission;

                ParticleSystem.Burst[] bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);

                float[] burstCounts = new float[bursts.Length];
                for (int b = 0; b < bursts.Length; b++)
                {
                    burstCounts[b] = bursts[b].count.constant;
                }

                baselines.Add(new ParticleBaseline
                {
                    System = ps,
                    RateOverTimeMultiplier = emission.rateOverTimeMultiplier,
                    RateOverDistanceMultiplier = emission.rateOverDistanceMultiplier,
                    BurstCounts = burstCounts,
                });
            }

            _baselines[instance] = baselines;
        }

        private void ApplyParticleDensity(GameObject instance, float density)
        {
            if (!_baselines.TryGetValue(instance, out var baselines))
            {
                CaptureParticleBaselines(instance);
                baselines = _baselines[instance];
            }

            foreach (ParticleBaseline baseline in baselines)
            {
                if (baseline.System == null)
                {
                    continue;
                }

                ParticleSystem.EmissionModule emission = baseline.System.emission;
                emission.rateOverTimeMultiplier = baseline.RateOverTimeMultiplier * density;
                emission.rateOverDistanceMultiplier = baseline.RateOverDistanceMultiplier * density;

                if (emission.burstCount > 0 && baseline.BurstCounts.Length == emission.burstCount)
                {
                    var bursts = new ParticleSystem.Burst[emission.burstCount];
                    emission.GetBursts(bursts);
                    for (int b = 0; b < bursts.Length; b++)
                    {
                        float scaled = baseline.BurstCounts[b] * density;
                        bursts[b].count = new ParticleSystem.MinMaxCurve(Mathf.Max(0f, scaled));
                    }

                    emission.SetBursts(bursts);
                }
            }
        }

        private struct ActiveEffect
        {
            public ActiveEffect(GameObject instance, GameObject prefab, float remaining)
            {
                Instance = instance;
                Prefab = prefab;
                Remaining = remaining;
            }

            public GameObject Instance { get; }
            public GameObject Prefab { get; }
            public float Remaining { get; set; }
        }

        private sealed class ParticleBaseline
        {
            public ParticleSystem System;
            public float RateOverTimeMultiplier;
            public float RateOverDistanceMultiplier;
            public float[] BurstCounts;
        }
    }
}
