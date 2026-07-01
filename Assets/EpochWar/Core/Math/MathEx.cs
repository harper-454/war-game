using System;

namespace EpochWar.Core.Math
{
    /// <summary>
    /// Platform-independent math helpers used across the simulation core.
    ///
    /// These exist so the core never reaches for <c>UnityEngine.Mathf</c> (which is
    /// not referenceable from <see cref="EpochWar.Core"/>) and so clamping behaviour
    /// is identical on every platform. Clamping at zero/capacity underpins several
    /// invariants asserted by the property tests (health never negative, resource
    /// never above capacity, etc.).
    /// </summary>
    public static class MathEx
    {
        /// <summary>Clamps <paramref name="value"/> to the inclusive range [min, max].</summary>
        public static int Clamp(int value, int min, int max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must be less than or equal to max.");
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Clamps <paramref name="value"/> to the inclusive range [min, max].</summary>
        public static long Clamp(long value, long min, long max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must be less than or equal to max.");
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Clamps <paramref name="value"/> to the inclusive range [min, max].</summary>
        public static float Clamp(float value, float min, float max)
        {
            if (min > max)
            {
                throw new ArgumentException("min must be less than or equal to max.");
            }

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>Clamps <paramref name="value"/> into [0, 1].</summary>
        public static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        /// <summary>Returns <paramref name="value"/> floored at zero (never negative).</summary>
        public static int ClampNonNegative(int value)
        {
            return value < 0 ? 0 : value;
        }

        /// <summary>Returns <paramref name="value"/> floored at zero (never negative).</summary>
        public static float ClampNonNegative(float value)
        {
            return value < 0f ? 0f : value;
        }

        /// <summary>Returns the larger of two integers.</summary>
        public static int Max(int a, int b) => a > b ? a : b;

        /// <summary>Returns the smaller of two integers.</summary>
        public static int Min(int a, int b) => a < b ? a : b;
    }
}
