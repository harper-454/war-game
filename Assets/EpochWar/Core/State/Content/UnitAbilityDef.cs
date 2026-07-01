using EpochWar.Core.Math;

namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// An engine-free definition of an activatable Unit_Ability available to a Unit type
    /// (Requirement 13).
    ///
    /// A Unit type exposes zero or more of these via <c>UnitDef.AbilityDefs</c>. When a Player
    /// activates an ability whose cooldown has fully elapsed and whose <see cref="Cost"/> the
    /// owning Nation can afford, the Unit_System executes the ability's <see cref="EffectKind"/>
    /// effect, deducts the cost, and starts a cooldown of <see cref="CooldownSeconds"/>
    /// (Req 13.2). Fixed-point cooldown seconds keep cooldown accounting deterministic under the
    /// fixed simulation tick.
    /// </summary>
    public sealed class UnitAbilityDef
    {
        /// <summary>Stable unique identifier for this ability within its Unit type.</summary>
        public string Id { get; }

        /// <summary>Cooldown duration in simulation seconds (deterministic fixed-point).</summary>
        public Fixed CooldownSeconds { get; }

        /// <summary>Resource cost deducted on a successful activation (reuses <see cref="ResourceCost"/>).</summary>
        public ResourceCost Cost { get; }

        /// <summary>The category of effect executed on activation, resolved by the Unit_System.</summary>
        public AbilityEffectKind EffectKind { get; }

        public UnitAbilityDef(string id, Fixed cooldownSeconds, ResourceCost cost, AbilityEffectKind effectKind)
        {
            Id = id;
            CooldownSeconds = cooldownSeconds;
            Cost = cost;
            EffectKind = effectKind;
        }

        public override string ToString()
            => $"Ability({Id}, cd {CooldownSeconds}s, {EffectKind})";
    }
}
