namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// The category of effect produced when a <see cref="UnitAbilityDef"/> is activated
    /// (Requirement 13). The Unit_System's ability handler resolves each kind to its concrete
    /// effect when an activation passes its cooldown and resource-cost preconditions (Req 13.2).
    /// </summary>
    public enum AbilityEffectKind
    {
        /// <summary>Restores health to the activating Unit or a target.</summary>
        Heal = 0,

        /// <summary>Applies a temporary stat buff.</summary>
        Buff = 1,

        /// <summary>Delivers a bombardment/attack effect at a target location.</summary>
        Bombard = 2,

        /// <summary>Hides the activating Unit from enemy vision.</summary>
        Cloak = 3
    }
}
