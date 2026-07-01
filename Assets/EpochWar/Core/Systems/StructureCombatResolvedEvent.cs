using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that an attack (currently only an Area_Effect attack,
    /// Req 11) applied combat damage to a Structure and reduced its health accordingly.
    ///
    /// <para>
    /// The base spec's direct <see cref="CombatSystem.ResolveAttack"/> is Unit-vs-Unit only and never
    /// damages Structures, so before this expansion there was no combat event for a Structure taking
    /// damage. <see cref="CombatSystem.ResolveAreaAttack"/> (Area_Effect) is the first code path that
    /// damages Structures, so a dedicated event is emitted rather than overloading
    /// <see cref="CombatResolvedEvent"/> — whose <see cref="CombatResolvedEvent.DefenderUnitId"/> is a
    /// Unit id — with a Structure id, keeping the two target kinds unambiguous for consumers such as
    /// the veterancy XP hook (Req 12.1) and the Unity VFX layer (Req 5.6).
    /// </para>
    ///
    /// It carries the attacking Unit's id, the defending Structure's id, the damage actually applied,
    /// the Structure's before/after health (clamped so it is never negative), and whether the
    /// Structure was reduced to zero health. A separate <see cref="StructureRemovedEvent"/> is emitted
    /// when a destroyed Structure is removed from the Match (Req 4.5).
    /// </summary>
    public sealed class StructureCombatResolvedEvent : GameEvent
    {
        /// <summary>The id of the attacking Unit.</summary>
        public int AttackerUnitId { get; }

        /// <summary>The id of the defending Structure.</summary>
        public int DefenderStructureId { get; }

        /// <summary>The damage applied to the Structure (always at least 1 before clamping to health).</summary>
        public int Damage { get; }

        /// <summary>The Structure's health before the damage was applied.</summary>
        public int DefenderOldHealth { get; }

        /// <summary>The Structure's health after the damage was applied (never negative).</summary>
        public int DefenderNewHealth { get; }

        /// <summary>True when the Structure was reduced to zero health by this resolution.</summary>
        public bool DefenderDestroyed { get; }

        public StructureCombatResolvedEvent(
            int attackerUnitId,
            int defenderStructureId,
            int damage,
            int defenderOldHealth,
            int defenderNewHealth)
        {
            AttackerUnitId = attackerUnitId;
            DefenderStructureId = defenderStructureId;
            Damage = damage;
            DefenderOldHealth = defenderOldHealth;
            DefenderNewHealth = defenderNewHealth;
            DefenderDestroyed = defenderNewHealth <= 0;
        }

        public override string ToString()
            => $"StructureCombatResolved(attacker #{AttackerUnitId} -> structure #{DefenderStructureId}, "
               + $"dmg {Damage}, hp {DefenderOldHealth} -> {DefenderNewHealth}"
               + $"{(DefenderDestroyed ? ", destroyed" : string.Empty)})";
    }
}
