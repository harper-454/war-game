namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of a single Veterancy_Tier within a Unit type's Veterancy_Curve
    /// (Requirement 12).
    ///
    /// A Unit advances to this tier once its accumulated experience reaches or exceeds
    /// <see cref="ExperienceThreshold"/>, at which point the tier's <see cref="AttackBonus"/> and
    /// <see cref="DefenseBonus"/> apply (Req 12.2). A <c>UnitDef.VeterancyCurve</c> is an
    /// ascending-by-threshold ordered list of these tiers.
    /// </summary>
    public sealed class VeterancyTierDef
    {
        /// <summary>Human-readable tier name, e.g. "Recruit", "Veteran", "Elite".</summary>
        public string Id { get; }

        /// <summary>Accumulated experience required to reach this tier.</summary>
        public int ExperienceThreshold { get; }

        /// <summary>Attack value added while this tier is active.</summary>
        public int AttackBonus { get; }

        /// <summary>Defense value added while this tier is active.</summary>
        public int DefenseBonus { get; }

        public VeterancyTierDef(string id, int experienceThreshold, int attackBonus, int defenseBonus)
        {
            Id = id;
            ExperienceThreshold = experienceThreshold;
            AttackBonus = attackBonus;
            DefenseBonus = defenseBonus;
        }

        public override string ToString()
            => $"VeterancyTier({Id}, threshold {ExperienceThreshold}, +{AttackBonus}atk/+{DefenseBonus}def)";
    }
}
