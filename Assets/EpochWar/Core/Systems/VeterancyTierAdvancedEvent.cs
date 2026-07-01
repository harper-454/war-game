using EpochWar.Core.Commands;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// A <see cref="GameEvent"/> announcing that a Unit advanced to a new Veterancy_Tier as a result
    /// of accumulating combat experience (Req 12.2, 12.6).
    ///
    /// The <see cref="UnitSystem"/> emits exactly one of these per tier crossed when its
    /// <see cref="UnitSystem.OnCombatResolved"/> veterancy hook grants experience to an attacking
    /// Unit — so a single large experience grant that crosses several tiers at once produces one
    /// event per crossed tier, each carrying the successive <see cref="NewTierIndex"/> (Property 13).
    /// The Unity UI_System consumes it to surface the Unit's new tier to its owning Player
    /// (Req 12.6 — the Core half of that requirement).
    /// </summary>
    public sealed class VeterancyTierAdvancedEvent : GameEvent
    {
        /// <summary>The id of the Nation that owns the advancing Unit.</summary>
        public int NationId { get; }

        /// <summary>The id of the Unit that advanced a Veterancy_Tier.</summary>
        public int UnitId { get; }

        /// <summary>
        /// The Unit's new 0-based <see cref="EpochWar.Core.State.UnitInstance.VeterancyTierIndex"/>
        /// after this advancement (the index into its <c>UnitDef.VeterancyCurve</c>).
        /// </summary>
        public int NewTierIndex { get; }

        public VeterancyTierAdvancedEvent(int nationId, int unitId, int newTierIndex)
        {
            NationId = nationId;
            UnitId = unitId;
            NewTierIndex = newTierIndex;
        }

        public override string ToString()
            => $"VeterancyTierAdvanced(nation {NationId}, unit #{UnitId} -> tier {NewTierIndex})";
    }
}
