using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// Pure, static classification of a defending Unit's <see cref="Flank"/> relative to an
    /// attacking Unit's position at the moment an attack is resolved (Requirement 9.4).
    ///
    /// <para>
    /// <b>Determinism (Design Principle 3).</b> Combat resolution must be bit-reproducible on the
    /// Host and in headless property tests, so every value here is <see cref="Fixed"/> (Q32.32)
    /// integer/fixed-point arithmetic — there is no <c>float</c>/<c>double</c> at runtime and no
    /// call into any platform trigonometric function (which would drift across platforms).
    /// </para>
    ///
    /// <para>
    /// <b>Why an angle bearing rather than a pure dot/cross test.</b> A dot/cross-product test
    /// classifies front-vs-rear and left-vs-right from two <em>vectors</em>. Here, however, the
    /// defender's orientation is supplied as an <em>angle</em> (<see cref="FacingDirection.AngleDegrees"/>),
    /// not a vector, and the arc thresholds are supplied in <em>degrees</em> (matching Req 9.4's
    /// "front-arc angle threshold and side-arc angle threshold"). Rather than convert the facing
    /// angle back into a vector (which needs sine/cosine) we convert the single defender→attacker
    /// direction into a bearing angle with a deterministic fixed-point <see cref="Atan2Degrees"/>
    /// and then classify purely by comparing fixed-point degree values. The decision itself is thus
    /// trig-free (only comparisons), fully deterministic, and — because it is a three-way
    /// <c>if/else-if/else</c> over the folded angle — <b>total and mutually exclusive</b>: every
    /// possible geometry maps to exactly one of Front/Side/Rear (Property 3).
    /// </para>
    ///
    /// <para>
    /// <b>Angle convention.</b> Angles are measured in the horizontal (X/Z) plane — Y is up and is
    /// ignored for flanking — as degrees increasing from the +X axis toward the +Z axis, matching
    /// <see cref="FacingDirection"/>. The bands over the folded angle θ ∈ [0, 180] (the absolute
    /// difference between the defender's facing and the defender→attacker bearing) are:
    /// <list type="bullet">
    ///   <item>Front: θ ≤ <c>frontArcDegrees / 2</c></item>
    ///   <item>Side:  <c>frontArcDegrees / 2</c> &lt; θ ≤ <c>frontArcDegrees / 2 + sideArcDegrees</c></item>
    ///   <item>Rear:  θ &gt; <c>frontArcDegrees / 2 + sideArcDegrees</c></item>
    /// </list>
    /// The front cone is centered on the facing (hence the half-width), the side band extends
    /// <c>sideArcDegrees</c> beyond it on each side, and everything nearer the opposite of the
    /// facing is Rear — the informal "within a rear arc of the opposite of facing" region.
    /// </para>
    /// </summary>
    public static class FlankClassifier
    {
        // atan approximation constant 9/32 = 0.28125, stored exactly in Q32.32.
        // atan(z) ≈ z / (1 + (9/32) * z^2) radians for z in [0, 1] (max error < 0.3°).
        private static readonly Fixed AtanCoefficient = Fixed.FromRaw((9L << Fixed.FractionalBits) / 32);

        // Degrees per radian = 180 / π ≈ 57.29577951308232, stored in Q32.32.
        // Raw = round(57.29577951308232 * 2^32).
        private static readonly Fixed DegreesPerRadian = Fixed.FromRaw(246083499208L);

        private static readonly Fixed Deg45 = Fixed.FromInt(45);
        private static readonly Fixed Deg90 = Fixed.FromInt(90);
        private static readonly Fixed Deg180 = Fixed.FromInt(180);
        private static readonly Fixed Deg360 = Fixed.FromInt(360);
        private static readonly Fixed Two = Fixed.FromInt(2);

        /// <summary>
        /// Classifies the <see cref="Flank"/> of a defender (at <paramref name="defenderPos"/>, facing
        /// <paramref name="defenderFacing"/>) against an attacker at <paramref name="attackerPos"/>,
        /// using the configured front/side arc thresholds (Req 9.4).
        ///
        /// The result is deterministic (identical inputs always yield the identical result) and total:
        /// every geometry — including the degenerate case where the attacker occupies the defender's
        /// exact position — maps to exactly one of <see cref="Flank.Front"/>, <see cref="Flank.Side"/>,
        /// or <see cref="Flank.Rear"/> (Property 3).
        /// </summary>
        /// <param name="defenderFacing">The defender's current facing angle (degrees, [0, 360)).</param>
        /// <param name="defenderPos">The defender's current world position.</param>
        /// <param name="attackerPos">The attacker's current world position.</param>
        /// <param name="frontArcDegrees">
        /// Total angular width of the front cone centered on the facing; the front half-width is half
        /// this value.
        /// </param>
        /// <param name="sideArcDegrees">
        /// Angular width of the side band extending beyond the front cone on each side; beyond it is Rear.
        /// </param>
        public static Flank Classify(
            FacingDirection defenderFacing,
            WorldPosition defenderPos,
            WorldPosition attackerPos,
            Fixed frontArcDegrees,
            Fixed sideArcDegrees)
        {
            // Direction from the defender to the attacker, in the horizontal (X/Z) plane.
            Fixed dx = attackerPos.X - defenderPos.X;
            Fixed dz = attackerPos.Z - defenderPos.Z;

            // Bearing of that direction, then the absolute angular difference from the facing,
            // folded into [0, 180]. Atan2Degrees handles the degenerate (0,0) direction by
            // returning 0, keeping the function total.
            Fixed bearing = Atan2Degrees(dz, dx);
            Fixed folded = FoldedDifference(bearing, defenderFacing.AngleDegrees);

            // Three-way partition of [0, 180] guarantees totality and mutual exclusivity for any
            // threshold values (comparisons only — no trig).
            Fixed frontHalf = frontArcDegrees / Two;
            if (folded <= frontHalf)
            {
                return Flank.Front;
            }

            Fixed sideOuter = frontHalf + sideArcDegrees;
            if (folded <= sideOuter)
            {
                return Flank.Side;
            }

            return Flank.Rear;
        }

        /// <summary>
        /// The absolute angular difference between two degree values, normalized into [0, 180].
        /// Deterministic fixed-point modular arithmetic (no float).
        /// </summary>
        private static Fixed FoldedDifference(Fixed a, Fixed b)
        {
            Fixed diff = Mod360(a - b); // [0, 360)
            if (diff > Deg180)
            {
                diff = Deg360 - diff; // fold the reflex angle back into [0, 180]
            }

            return diff;
        }

        /// <summary>Reduces a fixed-point degree value into the half-open range [0, 360).</summary>
        private static Fixed Mod360(Fixed value)
        {
            long period = Deg360.Raw;
            long r = value.Raw % period;
            if (r < 0)
            {
                r += period;
            }

            return Fixed.FromRaw(r);
        }

        /// <summary>
        /// A deterministic fixed-point atan2 returning degrees in [0, 360), measured from +X toward
        /// +Z. Implemented with a rational atan approximation over the first octant plus exact
        /// octant/quadrant reconstruction from the component signs, so it is total (defined for every
        /// input, including <c>(0, 0)</c> which returns 0) and reproducible on every platform.
        /// </summary>
        /// <param name="y">The component along the +Z (toward-90°) axis.</param>
        /// <param name="x">The component along the +X (0°) axis.</param>
        private static Fixed Atan2Degrees(Fixed y, Fixed x)
        {
            bool xZero = x.Raw == 0;
            bool yZero = y.Raw == 0;
            if (xZero && yZero)
            {
                return Fixed.Zero; // degenerate direction — defined so classification stays total
            }

            Fixed ax = Abs(x);
            Fixed ay = Abs(y);

            // First-quadrant angle in [0, 90] using the smaller/larger ratio for accuracy and to
            // avoid dividing by a zero component.
            Fixed quadrantAngle;
            if (ax >= ay)
            {
                Fixed z = ay / ax;               // [0, 1]
                quadrantAngle = AtanUnitDegrees(z);        // [0, 45]
            }
            else
            {
                Fixed z = ax / ay;               // [0, 1)
                quadrantAngle = Deg90 - AtanUnitDegrees(z); // (45, 90]
            }

            // Reconstruct the full-circle bearing from the signs of x and z(=y here).
            bool xNeg = x.Raw < 0;
            bool yNeg = y.Raw < 0;

            Fixed result;
            if (!xNeg && !yNeg)
            {
                result = quadrantAngle;              // Q1: [0, 90]
            }
            else if (xNeg && !yNeg)
            {
                result = Deg180 - quadrantAngle;     // Q2: [90, 180]
            }
            else if (xNeg && yNeg)
            {
                result = Deg180 + quadrantAngle;     // Q3: [180, 270]
            }
            else
            {
                result = Deg360 - quadrantAngle;     // Q4: [270, 360)
            }

            return Mod360(result);
        }

        /// <summary>
        /// Approximates <c>atan(z)</c> in degrees for z in [0, 1] via the rational form
        /// <c>z / (1 + (9/32) z^2)</c> radians converted to degrees. Monotonic increasing over the
        /// domain, so it preserves ordering; maximum error is under ~0.3°.
        /// </summary>
        private static Fixed AtanUnitDegrees(Fixed z)
        {
            Fixed z2 = z * z;
            Fixed denominator = Fixed.OneValue + (AtanCoefficient * z2);
            Fixed radians = z / denominator;
            return radians * DegreesPerRadian;
        }

        /// <summary>Absolute value of a fixed-point number (deterministic sign flip on the raw value).</summary>
        private static Fixed Abs(Fixed value)
            => value.Raw < 0 ? Fixed.FromRaw(-value.Raw) : value;
    }
}
