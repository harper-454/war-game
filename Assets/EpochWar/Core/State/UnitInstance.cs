using System.Collections.Generic;
using EpochWar.Core.Math;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.State
{
    /// <summary>
    /// A live, recruited Unit in the Match (Req 3).
    ///
    /// An instance holds only its own mutable runtime state — <see cref="Health"/>,
    /// <see cref="Position"/>, optional <see cref="BattalionId"/> membership, and
    /// <see cref="CurrentOrder"/> — while sharing the immutable <see cref="UnitDef"/> for its
    /// static attributes (attack, defense, move speed, Era of origin, max health). Together the
    /// instance and its def expose the full per-Unit attribute schema required by Req 3.6 and
    /// feed the combat damage formula (Req 3.7). When <see cref="Health"/> reaches zero the
    /// Unit_System removes the instance from the Match and from any Battalion (Req 3.5).
    /// </summary>
    public sealed class UnitInstance
    {
        /// <summary>Stable per-Match identifier.</summary>
        public int Id { get; }

        /// <summary>The id of the owning <see cref="Nation"/>.</summary>
        public int OwnerNationId { get; }

        /// <summary>The shared immutable definition supplying static attributes (Req 3.6).</summary>
        public UnitDef Def { get; }

        /// <summary>Current health; clamped to never go below zero by combat resolution (Req 3.6, 3.7).</summary>
        public int Health { get; set; }

        /// <summary>Current continuous world position (Req 3.2).</summary>
        public WorldPosition Position { get; set; }

        /// <summary>The Battalion this Unit belongs to, or <c>null</c> if ungrouped (Req 3.3).</summary>
        public int? BattalionId { get; set; }

        /// <summary>The Unit's current standing order (Req 3.2); defaults to <see cref="UnitOrder.Idle"/>.</summary>
        public UnitOrder CurrentOrder { get; set; }

        /// <summary>
        /// The Unit's current facing direction as a deterministic fixed-point angle, used by
        /// flanking classification (Req 9.4). Defaults to <see cref="FacingDirection.Zero"/> and is
        /// updated when the Unit's movement order changes its direction of travel.
        /// </summary>
        public FacingDirection Facing { get; set; }

        /// <summary>
        /// Zero-based index into <see cref="UnitDef.VeterancyCurve"/> (0 = base/no tier). Advances as
        /// the Unit accumulates experience; capped at the highest defined tier (Req 12.1, 12.2, 12.4).
        /// </summary>
        public int VeterancyTierIndex { get; set; }

        /// <summary>
        /// Accumulated Veterancy experience; never decreases while the Unit exists (Req 12.1, 12.3).
        /// Discarded when the Unit is removed from the Match (Req 12.5).
        /// </summary>
        public int VeterancyExperience { get; set; }

        /// <summary>
        /// Remaining cooldown per ability id (keyed by <see cref="Content.UnitAbilityDef.Id"/>); an
        /// absent or non-positive entry means the ability is ready (Req 13.2, 13.4). Freshly
        /// constructed Units start with an empty map (all abilities ready).
        /// </summary>
        public Dictionary<string, Fixed> AbilityRemainingCooldown { get; }

        public UnitInstance(int id, int ownerNationId, UnitDef def, WorldPosition position)
        {
            Id = id;
            OwnerNationId = ownerNationId;
            Def = def;
            Health = def?.MaxHealth ?? 0;
            Position = position;
            BattalionId = null;
            CurrentOrder = UnitOrder.Idle;
            Facing = FacingDirection.Zero;
            VeterancyTierIndex = 0;
            VeterancyExperience = 0;
            AbilityRemainingCooldown = new Dictionary<string, Fixed>();
        }

        public override string ToString()
            => $"Unit#{Id}(owner {OwnerNationId}, {Def?.Id}, hp {Health})";
    }
}
