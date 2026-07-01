using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Unit_Ability was successfully activated on a Unit
    /// (Req 13.2): its cooldown had fully elapsed, the owning Nation could afford its cost, the
    /// ability's effect was executed, the cost was deducted, and its cooldown was started.
    ///
    /// The <see cref="UnitSystem"/>'s <see cref="ActivateAbilityCommand"/> handler emits this on an
    /// accepted activation. It carries the owning Nation, the acting Unit, the activated ability id,
    /// the <see cref="AbilityEffectKind"/> that was executed, and the optional target position — so
    /// the Unity UI_System/VFX_System can present the activation (Req 13, Req 5) without re-deriving
    /// which ability fired.
    /// </summary>
    public sealed class AbilityActivatedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owns the activating Unit.</summary>
        public int NationId { get; }

        /// <summary>The id of the Unit whose ability was activated.</summary>
        public int UnitId { get; }

        /// <summary>The id of the activated ability (matches the Unit type's <c>UnitDef.AbilityDefs</c>).</summary>
        public string AbilityId { get; }

        /// <summary>The category of effect executed by the activation.</summary>
        public AbilityEffectKind EffectKind { get; }

        /// <summary>The activation's target position, or <c>null</c> for self/no-target abilities.</summary>
        public WorldPosition? TargetPosition { get; }

        public AbilityActivatedEvent(
            int nationId,
            int unitId,
            string abilityId,
            AbilityEffectKind effectKind,
            WorldPosition? targetPosition = null)
        {
            NationId = nationId;
            UnitId = unitId;
            AbilityId = abilityId;
            EffectKind = effectKind;
            TargetPosition = targetPosition;
        }

        public override string ToString()
            => $"AbilityActivated(nation {NationId}, unit #{UnitId}, ability \"{AbilityId}\", {EffectKind})";
    }
}
