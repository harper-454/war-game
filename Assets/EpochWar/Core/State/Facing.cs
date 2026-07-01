using System;
using EpochWar.Core.Math;

namespace EpochWar.Core.State
{
    /// <summary>
    /// The four cardinal facing directions, matching <see cref="WorldPosition"/>'s fixed-point
    /// axes. Used by movement-oriented logic; the general-angle form used for flanking
    /// classification is <see cref="FacingDirection"/> below (Req 9.4).
    /// </summary>
    public enum Facing
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    /// <summary>
    /// A deterministic, general-purpose facing expressed as a fixed-point angle in degrees in
    /// the half-open range <c>[0, 360)</c> (Req 9.4).
    ///
    /// Flanking classification (Requirement 9) compares the angle between a defender's current
    /// facing and the direction from the defender to the attacker against configured front/side
    /// arc thresholds. Because that comparison must be bit-reproducible on the Host and in
    /// headless property tests, the angle is stored as <see cref="Fixed"/> (Q32.32) rather than
    /// a <c>float</c>, avoiding trigonometric drift (Design Principle 3).
    ///
    /// Immutable value type with structural equality.
    /// </summary>
    public readonly struct FacingDirection : IEquatable<FacingDirection>
    {
        /// <summary>The facing angle in degrees, deterministic fixed-point, canonically <c>[0, 360)</c>.</summary>
        public Fixed AngleDegrees { get; }

        public FacingDirection(Fixed angleDegrees)
        {
            AngleDegrees = angleDegrees;
        }

        /// <summary>A facing of zero degrees (the default).</summary>
        public static readonly FacingDirection Zero = new FacingDirection(Fixed.Zero);

        /// <summary>Builds a facing from an integer number of degrees.</summary>
        public static FacingDirection FromDegrees(int degrees) => new FacingDirection(Fixed.FromInt(degrees));

        public bool Equals(FacingDirection other) => AngleDegrees == other.AngleDegrees;

        public override bool Equals(object obj) => obj is FacingDirection other && Equals(other);

        public override int GetHashCode() => AngleDegrees.GetHashCode();

        public override string ToString() => $"Facing({AngleDegrees}deg)";
    }

    /// <summary>
    /// A relative facing classification of a defending Unit with respect to an attacking Unit's
    /// position at the moment of an attack (Req 9). Every possible angle maps to exactly one of
    /// these three values (Req 9.4). Side and Rear grant a Flanking_Bonus; Front grants none.
    /// </summary>
    public enum Flank
    {
        Front = 0,
        Side = 1,
        Rear = 2
    }
}
