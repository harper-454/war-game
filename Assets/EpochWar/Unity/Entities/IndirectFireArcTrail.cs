using UnityEngine;

namespace EpochWar.Unity.Entities
{
    /// <summary>
    /// Animates a single pooled Indirect_Fire projectile along a parabolic arc from the firing
    /// Artillery_Unit to its target over the attack's flight delay (task 24.5, Req 15.7).
    ///
    /// <para><b>Why a self-driving component.</b> The <see cref="VfxSystem"/> observes an
    /// <see cref="EpochWar.Core.Systems.IndirectFireLaunchedEvent"/> and rents a trail instance from the
    /// <see cref="EffectPool"/> for the exact flight-delay lifetime, then calls <see cref="Play"/>. This
    /// component then advances the arc every frame in its own <see cref="Update"/> — independent of the
    /// fixed simulation tick — so the projectile visibly travels for the whole flight regardless of tick
    /// cadence. Because it is purely presentational and keyed off a replicated Core event, the trail is
    /// visible to <em>all</em> Nations regardless of Spotting (Req 15.7); it never consults the
    /// Vision_System.</para>
    ///
    /// <para><b>Rendering.</b> When a <see cref="LineRenderer"/> is present on the instance (or a child),
    /// the traveled portion of the arc is drawn as a growing polyline behind the projectile head. A child
    /// transform named <c>"Head"</c> or <c>"Projectile"</c>, when present, is moved along the arc so a
    /// mesh/particle head reads as the shell in flight; otherwise the instance's own transform is moved.
    /// The component is safe to reuse across pool rents — <see cref="Play"/> fully resets its state.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IndirectFireArcTrail : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Number of polyline segments used to draw the arc when a LineRenderer is present.")]
        [Min(2)]
        private int _arcSegments = 24;

        private LineRenderer _line;
        private Transform _head;

        private Vector3 _from;
        private Vector3 _to;
        private float _duration;
        private float _arcHeight;
        private float _elapsed;
        private bool _playing;

        /// <summary>
        /// Starts (or restarts) the arc animation from <paramref name="from"/> to <paramref name="to"/>
        /// over <paramref name="durationSeconds"/> with an apex <paramref name="arcHeight"/> world-units
        /// above the straight-line path. A non-positive <paramref name="arcHeight"/> auto-derives a
        /// height proportional to the shot distance so a longer bombardment arcs higher. Idempotent per
        /// call — resets elapsed time so a pooled instance reused for a new shot starts cleanly.
        /// </summary>
        public void Play(Vector3 from, Vector3 to, float durationSeconds, float arcHeight)
        {
            ResolveRenderers();

            _from = from;
            _to = to;
            _duration = Mathf.Max(0.01f, durationSeconds);
            _arcHeight = arcHeight > 0f ? arcHeight : ComputeAutoArcHeight(from, to);
            _elapsed = 0f;
            _playing = true;

            Apply(0f);
        }

        private void Awake() => ResolveRenderers();

        private void Update()
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            Apply(t);

            if (t >= 1f)
            {
                _playing = false;
            }
        }

        private void ResolveRenderers()
        {
            if (_line == null)
            {
                _line = GetComponentInChildren<LineRenderer>(includeInactive: true);
            }

            if (_head == null)
            {
                _head = transform.Find("Head");
                if (_head == null)
                {
                    _head = transform.Find("Projectile");
                }
            }
        }

        /// <summary>The parabolic point at normalized flight fraction <paramref name="t"/> in [0, 1].</summary>
        private Vector3 PointAt(float t)
        {
            Vector3 linear = Vector3.Lerp(_from, _to, t);
            // 4*t*(1-t) peaks at 1.0 when t == 0.5, giving a symmetric arc that starts and ends on the line.
            float height = _arcHeight * 4f * t * (1f - t);
            linear.y += height;
            return linear;
        }

        private void Apply(float t)
        {
            Vector3 headPos = PointAt(t);
            if (_head != null)
            {
                _head.position = headPos;
            }
            else
            {
                transform.position = headPos;
            }

            if (_line == null)
            {
                return;
            }

            _line.useWorldSpace = true;

            int segments = Mathf.Max(2, _arcSegments);
            // Draw the traveled portion (0..t) so the trail grows as the shell flies. Always keep at
            // least two points so the LineRenderer is valid at t == 0.
            int pointCount = Mathf.Max(2, Mathf.CeilToInt(segments * Mathf.Max(t, 1f / segments)) + 1);
            _line.positionCount = pointCount;

            for (int i = 0; i < pointCount; i++)
            {
                float u = t * (i / (float)(pointCount - 1));
                _line.SetPosition(i, PointAt(u));
            }
        }

        private static float ComputeAutoArcHeight(Vector3 from, Vector3 to)
            => Mathf.Max(2f, Vector3.Distance(from, to) * 0.25f);
    }
}
