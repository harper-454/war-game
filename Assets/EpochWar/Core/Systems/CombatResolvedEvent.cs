using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that combat between two opposing Units was resolved and
    /// the defender's health reduced accordingly (Req 3.7).
    ///
    /// The <see cref="CombatSystem"/> emits this whenever it applies the damage formula to a
    /// defending Unit. It carries the attacker and defender ids, the damage actually applied, the
    /// defender's before/after health (clamped so it is never negative), and whether the defender was
    /// reduced to zero health. A separate <see cref="UnitEliminatedEvent"/> is emitted when a
    /// destroyed defender is removed from the Match (Req 3.5).
    /// </summary>
    public sealed class CombatResolvedEvent : GameEvent
    {
        /// <summary>The id of the attacking Unit.</summary>
        public int AttackerUnitId { get; }

        /// <summary>The id of the defending Unit.</summary>
        public int DefenderUnitId { get; }

        /// <summary>The damage applied to the defender (always at least 1 before clamping to health).</summary>
        public int Damage { get; }

        /// <summary>The defender's health before the damage was applied.</summary>
        public int DefenderOldHealth { get; }

        /// <summary>The defender's health after the damage was applied (never negative).</summary>
        public int DefenderNewHealth { get; }

        /// <summary>True when the defender was reduced to zero health by this resolution.</summary>
        public bool DefenderDestroyed { get; }

        public CombatResolvedEvent(
            int attackerUnitId,
            int defenderUnitId,
            int damage,
            int defenderOldHealth,
            int defenderNewHealth)
        {
            AttackerUnitId = attackerUnitId;
            DefenderUnitId = defenderUnitId;
            Damage = damage;
            DefenderOldHealth = defenderOldHealth;
            DefenderNewHealth = defenderNewHealth;
            DefenderDestroyed = defenderNewHealth <= 0;
        }

        public override string ToString()
            => $"CombatResolved(attacker #{AttackerUnitId} -> defender #{DefenderUnitId}, "
               + $"dmg {Damage}, hp {DefenderOldHealth} -> {DefenderNewHealth}"
               + $"{(DefenderDestroyed ? ", destroyed" : string.Empty)})";
    }
}
