using System;

namespace EpochWar.Core.Math
{
    /// <summary>
    /// A seeded, fully deterministic pseudo-random number generator.
    ///
    /// Determinism is a hard requirement of the simulation core (see design.md,
    /// "Deterministic RNG"): the Host and the EditMode property tests must be able
    /// to reproduce the exact same stream of values from the same seed, independent
    /// of platform, so that combat resolution and any other randomized outcome are
    /// reproducible and timestampable for victory tie-breaks.
    ///
    /// Implemented with the xorshift64* algorithm. All arithmetic is performed in an
    /// <c>unchecked</c> context so overflow wraps identically on every platform. The
    /// generator carries no <c>UnityEngine</c> dependency so it can run inside
    /// <see cref="EpochWar.Core"/> under headless EditMode tests.
    /// </summary>
    public sealed class DeterministicRandom
    {
        // Multiplier from Vigna's xorshift64* (2014). Chosen for good statistical quality.
        private const ulong Multiplier = 0x2545F4914F6CDD1DUL;

        private ulong _state;

        /// <summary>The seed this generator was created/reset with.</summary>
        public ulong Seed { get; private set; }

        /// <summary>Creates a generator seeded with the supplied 64-bit seed.</summary>
        public DeterministicRandom(ulong seed)
        {
            Reset(seed);
        }

        /// <summary>Creates a generator seeded with a signed 32-bit seed.</summary>
        public DeterministicRandom(int seed)
            : this(unchecked((ulong)seed))
        {
        }

        /// <summary>
        /// The raw internal state. Exposing it allows the simulation to serialize and
        /// later restore the generator at an exact point in its stream, preserving
        /// determinism across save/load boundaries.
        /// </summary>
        public ulong State
        {
            get => _state;
            set => _state = value == 0UL ? Multiplier : value;
        }

        /// <summary>Resets the generator back to the start of the stream for a seed.</summary>
        public void Reset(ulong seed)
        {
            Seed = seed;
            // xorshift64* must never operate on a zero state; substitute a fixed
            // non-zero constant so a zero seed still yields a valid stream.
            _state = seed == 0UL ? Multiplier : seed;
        }

        /// <summary>Returns the next 64-bit unsigned value and advances the stream.</summary>
        public ulong NextULong()
        {
            unchecked
            {
                ulong x = _state;
                x ^= x >> 12;
                x ^= x << 25;
                x ^= x >> 27;
                _state = x;
                return x * Multiplier;
            }
        }

        /// <summary>Returns the next 32-bit unsigned value.</summary>
        public uint NextUInt()
        {
            // Use the high bits, which have the best distribution for this generator.
            return (uint)(NextULong() >> 32);
        }

        /// <summary>
        /// Returns a non-negative <see cref="int"/> in the range [0, <see cref="int.MaxValue"/>].
        /// </summary>
        public int NextInt()
        {
            return (int)(NextUInt() & 0x7FFFFFFFu);
        }

        /// <summary>
        /// Returns an <see cref="int"/> in the half-open range [minInclusive, maxExclusive).
        /// Uses rejection sampling to remain unbiased across the requested span.
        /// </summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive > maxExclusive)
            {
                throw new ArgumentException(
                    "minInclusive must be less than or equal to maxExclusive.");
            }

            if (minInclusive == maxExclusive)
            {
                return minInclusive;
            }

            uint range = (uint)((long)maxExclusive - minInclusive);

            // Unbiased bounded sampling: reject the small tail that would skew results.
            uint limit = uint.MaxValue - (uint.MaxValue % range);
            uint value;
            do
            {
                value = NextUInt();
            }
            while (value >= limit);

            return minInclusive + (int)(value % range);
        }

        /// <summary>Returns a <see cref="double"/> in the half-open range [0.0, 1.0).</summary>
        public double NextDouble()
        {
            // 53 bits of mantissa precision.
            return (NextULong() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Returns a <see cref="float"/> in the half-open range [0.0, 1.0).</summary>
        public float NextFloat()
        {
            // 24 bits of mantissa precision.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>Returns a random boolean with equal probability.</summary>
        public bool NextBool()
        {
            return (NextULong() & 1UL) == 1UL;
        }
    }
}
