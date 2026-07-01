using System;

namespace EpochWar.Core.Math
{
    /// <summary>
    /// A deterministic Q32.32 fixed-point number.
    ///
    /// The simulation advances on a fixed tick and must produce bit-identical results
    /// on the Host and in headless property tests. Floating-point arithmetic can differ
    /// subtly across platforms/compilers; <see cref="Fixed"/> provides reproducible
    /// fractional arithmetic (e.g. construction/production accumulators, movement
    /// fractions) backed by a single 64-bit integer.
    ///
    /// The value is stored as <c>raw / 2^32</c>. All arithmetic is <c>unchecked</c> so
    /// overflow wraps identically everywhere.
    /// </summary>
    public readonly struct Fixed : IEquatable<Fixed>, IComparable<Fixed>
    {
        /// <summary>Number of fractional bits (Q32.32).</summary>
        public const int FractionalBits = 32;

        private const long One = 1L << FractionalBits;

        /// <summary>The underlying fixed-point representation (value * 2^32).</summary>
        public long Raw { get; }

        private Fixed(long raw)
        {
            Raw = raw;
        }

        /// <summary>The fixed-point value zero.</summary>
        public static readonly Fixed Zero = new Fixed(0L);

        /// <summary>The fixed-point value one.</summary>
        public static readonly Fixed OneValue = new Fixed(One);

        /// <summary>Constructs a <see cref="Fixed"/> from its raw 2^32-scaled representation.</summary>
        public static Fixed FromRaw(long raw) => new Fixed(raw);

        /// <summary>Constructs a <see cref="Fixed"/> from an integer.</summary>
        public static Fixed FromInt(int value)
        {
            unchecked
            {
                return new Fixed((long)value << FractionalBits);
            }
        }

        /// <summary>
        /// Constructs a <see cref="Fixed"/> from a <see cref="float"/>. Intended for
        /// authoring/conversion at boundaries only; runtime arithmetic should stay in
        /// fixed-point to remain deterministic.
        /// </summary>
        public static Fixed FromFloat(float value)
        {
            return new Fixed((long)System.Math.Round((double)value * One));
        }

        /// <summary>Truncates toward zero to the nearest integer.</summary>
        public int ToInt()
        {
            return (int)(Raw >> FractionalBits);
        }

        /// <summary>Converts to <see cref="float"/> (for rendering/presentation only).</summary>
        public float ToFloat()
        {
            return (float)((double)Raw / One);
        }

        public static Fixed operator +(Fixed a, Fixed b)
        {
            unchecked { return new Fixed(a.Raw + b.Raw); }
        }

        public static Fixed operator -(Fixed a, Fixed b)
        {
            unchecked { return new Fixed(a.Raw - b.Raw); }
        }

        public static Fixed operator -(Fixed a)
        {
            unchecked { return new Fixed(-a.Raw); }
        }

        public static Fixed operator *(Fixed a, Fixed b)
        {
            // result.raw = (a.raw * b.raw) >> 32, computed in full 128-bit precision so
            // no information is lost for in-range game values. Implemented manually
            // (no Math.BigMul/Int128 dependency) to stay portable across Unity runtimes.
            bool negative = (a.Raw < 0) ^ (b.Raw < 0);
            ulong ua = (ulong)(a.Raw < 0 ? -a.Raw : a.Raw);
            ulong ub = (ulong)(b.Raw < 0 ? -b.Raw : b.Raw);

            unchecked
            {
                ulong aLo = ua & 0xFFFFFFFFUL;
                ulong aHi = ua >> 32;
                ulong bLo = ub & 0xFFFFFFFFUL;
                ulong bHi = ub >> 32;

                ulong ll = aLo * bLo;
                ulong lh = aLo * bHi;
                ulong hl = aHi * bLo;
                ulong hh = aHi * bHi;

                // Combine partial products into a 128-bit value (high:low).
                ulong cross = (ll >> 32) + (lh & 0xFFFFFFFFUL) + (hl & 0xFFFFFFFFUL);
                ulong low = (ll & 0xFFFFFFFFUL) | (cross << 32);
                ulong high = hh + (lh >> 32) + (hl >> 32) + (cross >> 32);

                // Shift the 128-bit product right by FractionalBits (32).
                ulong shifted = (low >> FractionalBits) | (high << (64 - FractionalBits));
                long result = (long)shifted;
                return new Fixed(negative ? -result : result);
            }
        }

        public static Fixed operator /(Fixed a, Fixed b)
        {
            if (b.Raw == 0L)
            {
                throw new DivideByZeroException("Division by zero Fixed value.");
            }

            // Scale the numerator up by 2^32 before dividing to preserve fractional bits.
            // decimal is exact (base-10, fully specified) so the result is deterministic
            // across platforms, and it avoids the overflow of (a.Raw << 32).
            return new Fixed((long)(((decimal)a.Raw * One) / b.Raw));
        }

        /// <summary>Clamps this value into the inclusive range [min, max].</summary>
        public Fixed Clamp(Fixed min, Fixed max)
        {
            if (min.Raw > max.Raw)
            {
                throw new ArgumentException("min must be less than or equal to max.");
            }

            if (Raw < min.Raw) return min;
            if (Raw > max.Raw) return max;
            return this;
        }

        public static bool operator <(Fixed a, Fixed b) => a.Raw < b.Raw;
        public static bool operator >(Fixed a, Fixed b) => a.Raw > b.Raw;
        public static bool operator <=(Fixed a, Fixed b) => a.Raw <= b.Raw;
        public static bool operator >=(Fixed a, Fixed b) => a.Raw >= b.Raw;
        public static bool operator ==(Fixed a, Fixed b) => a.Raw == b.Raw;
        public static bool operator !=(Fixed a, Fixed b) => a.Raw != b.Raw;

        public int CompareTo(Fixed other) => Raw.CompareTo(other.Raw);

        public bool Equals(Fixed other) => Raw == other.Raw;

        public override bool Equals(object obj) => obj is Fixed other && Equals(other);

        public override int GetHashCode() => Raw.GetHashCode();

        public override string ToString() => ToFloat().ToString("0.######");
    }
}
