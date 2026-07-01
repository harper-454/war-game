using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.Navigation;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The consequence applied to a Structure or Unit that loses its supporting terrain (Req 6.4).
    /// </summary>
    public enum SupportLossConsequence
    {
        /// <summary>Reduce the entity straight to zero health and remove it from the Match.</summary>
        Destroy = 0,

        /// <summary>Subtract a configured amount of damage from the entity's health (clamped at zero).</summary>
        Damage = 1,
    }

    /// <summary>
    /// The authoritative owner of destructible/diggable terrain changes within the simulation loop
    /// (Requirement 6, design "TerrainSystem").
    ///
    /// Responsibilities, executed once per <see cref="Tick"/> after the frame's commands are applied
    /// and in the fixed system order (Resource, Civ, Base, Unit, <em>Terrain</em>, Victory):
    /// <list type="bullet">
    /// <item>Drains the queue of pending <see cref="TerrainEffect"/>s — populated by weapon/excavation
    /// commands via <see cref="QueueEffect"/> — and applies each to the Match's
    /// <see cref="TerrainVolume"/> through <see cref="TerrainVolume.ApplyEffect"/>, carving exactly the
    /// targeted region and collecting the cells that actually changed (Req 6.2). One
    /// <see cref="TerrainModifiedEvent"/> is emitted per effect that changed cells so the change can be
    /// replicated (Req 6.5).</item>
    /// <item>After the batch, recomputes only the navigation nodes above the changed columns of the
    /// supplied <see cref="NavGrid"/> so pathfinding reflects the modified terrain (Req 6.3). When no
    /// grid is supplied this step is skipped.</item>
    /// <item>Runs support checks against every Structure/Unit whose supporting cells were among the
    /// changed set and applies the configured <see cref="SupportLossConsequence"/> to those that are
    /// now unsupported (Req 6.4, Property 28), emitting a <see cref="SupportLossEvent"/> for each and
    /// removing any entity reduced to zero health (Req 3.5, 4.5).</item>
    /// </list>
    ///
    /// Support is evaluated only for entities whose support cells were actually removed this batch, so
    /// an already-floating entity is not repeatedly punished by unrelated effects and the consequence
    /// maps precisely to the "IF ... loses its supporting terrain due to terrain modification" trigger
    /// (Req 6.4). Flying units (<see cref="UnitRole.Aircraft"/>, <see cref="UnitRole.ColonyShip"/>)
    /// are exempt from ground-support checks. Following the project's pipeline contract the system
    /// never throws to signal gameplay outcomes; it returns the produced events for replication/UI.
    /// </summary>
    public sealed class TerrainSystem
    {
        private readonly Queue<TerrainEffect> _pending = new Queue<TerrainEffect>();
        private readonly NavGrid _navGrid;
        private readonly SupportLossConsequence _consequence;
        private readonly int _supportLossDamage;

        /// <summary>
        /// Creates the system.
        /// </summary>
        /// <param name="navGrid">
        /// The ground navigation grid derived from the Match terrain. When supplied it is recomputed
        /// for the changed cells after each batch (Req 6.3); pass <c>null</c> to skip nav recomputation
        /// (e.g. in tests that only exercise support consequences).
        /// </param>
        /// <param name="consequence">
        /// The consequence applied to entities that lose support (Req 6.4). Defaults to
        /// <see cref="SupportLossConsequence.Destroy"/>.
        /// </param>
        /// <param name="supportLossDamage">
        /// Health removed from an unsupported entity when <paramref name="consequence"/> is
        /// <see cref="SupportLossConsequence.Damage"/>; clamped at zero and ignored for
        /// <see cref="SupportLossConsequence.Destroy"/>. An entity driven to zero health by damage is
        /// removed like a destroyed one.
        /// </param>
        public TerrainSystem(
            NavGrid navGrid = null,
            SupportLossConsequence consequence = SupportLossConsequence.Destroy,
            int supportLossDamage = 0)
        {
            _navGrid = navGrid;
            _consequence = consequence;
            _supportLossDamage = supportLossDamage < 0 ? 0 : supportLossDamage;
        }

        /// <summary>The number of effects currently queued for the next <see cref="Tick"/>.</summary>
        public int PendingEffectCount => _pending.Count;

        /// <summary>
        /// Reports whether a defending Unit standing on <paramref name="defenderCell"/> qualifies for
        /// Cover against an attacker on <paramref name="attackerCell"/> (Req 10.4). This is a thin,
        /// stateless query: it reads the defender cell's material from <paramref name="terrain"/> and
        /// delegates the decision to <see cref="CoverClassifier.IsCoverQualifying"/>, using the
        /// attacker cell's elevation (its <c>Y</c> coordinate) as the comparison elevation for the
        /// high-ground rule.
        ///
        /// <para>
        /// Cover qualification is fully derived from the existing <see cref="CellMaterial"/> and cell
        /// coordinate data, so no new mutable state is added to <see cref="TerrainVolume"/>; an
        /// out-of-range defender cell reads as <see cref="CellMaterial.Empty"/> (non-qualifying by
        /// material). A <c>null</c> <paramref name="terrain"/> reports non-qualifying rather than
        /// throwing, consistent with the pipeline's never-throw-for-gameplay contract.
        /// </para>
        ///
        /// <para>
        /// The design describes this member as <c>GetCoverQualification(CellCoord defenderCell,
        /// CellCoord attackerCell)</c>; because <see cref="TerrainSystem"/> is stateless and does not
        /// own the <see cref="TerrainVolume"/> (it lives on <see cref="MatchState.Terrain"/> and is
        /// supplied per tick), the volume is passed explicitly so the query stays a pure,
        /// deterministic function of the terrain the caller already holds.
        /// </para>
        /// </summary>
        public bool GetCoverQualification(TerrainVolume terrain, CellCoord defenderCell, CellCoord attackerCell)
        {
            if (terrain == null)
            {
                return false;
            }

            CellMaterial material = terrain.Get(defenderCell).Material;
            return CoverClassifier.IsCoverQualifying(material, defenderCell.Y, attackerCell.Y);
        }

        /// <summary>
        /// Enqueues <paramref name="effect"/> to be applied to the terrain on the next
        /// <see cref="Tick"/>. Weapon and excavation command handlers call this so all terrain
        /// mutation is deferred to the ordered, authoritative tick rather than applied inline
        /// (Req 6.2). Queuing never mutates terrain itself.
        /// </summary>
        public void QueueEffect(TerrainEffect effect)
        {
            _pending.Enqueue(effect);
        }

        /// <summary>
        /// Applies every queued <see cref="TerrainEffect"/> to <paramref name="state"/>'s terrain,
        /// recomputes the affected navigation nodes, and applies the configured consequence to any
        /// Structure/Unit that lost its supporting terrain (Req 6.2, 6.3, 6.4).
        ///
        /// Returns the ordered list of <see cref="GameEvent"/>s produced: one
        /// <see cref="TerrainModifiedEvent"/> per effect that changed cells, followed by one
        /// <see cref="SupportLossEvent"/> per entity that lost support. With an empty queue — or when
        /// the queued effects changed no cells — no support checks run and the returned list contains
        /// only any terrain-modified events (empty when nothing changed).
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            if (_pending.Count == 0)
            {
                return Array.Empty<GameEvent>();
            }

            var events = new List<GameEvent>();
            var changedCells = new List<CellCoord>();

            // Apply the whole queued batch, collecting every cell that actually changed (Req 6.2).
            while (_pending.Count > 0)
            {
                TerrainEffect effect = _pending.Dequeue();
                IReadOnlyList<CellCoord> modified = state.Terrain.ApplyEffect(effect);
                if (modified.Count > 0)
                {
                    changedCells.AddRange(modified);
                    events.Add(new TerrainModifiedEvent(effect, modified));
                }
            }

            if (changedCells.Count == 0)
            {
                // Effects targeted already-empty/out-of-range cells: nothing to re-navigate or unsupport.
                return events;
            }

            // Recompute only the nav nodes above the touched columns (Req 6.3).
            _navGrid?.Recompute(state.Terrain, changedCells);

            // Support checks run once after the batch, scoped to the cells that changed (Req 6.4).
            var changedSet = new HashSet<CellCoord>(changedCells);
            ApplySupportConsequences(state, changedSet, events);

            return events;
        }

        /// <summary>
        /// Finds every Structure and Unit whose support cells were removed this batch and is now
        /// unsupported, then applies the configured consequence to each and removes any reduced to
        /// zero health. Entities are collected before mutation so the Match collections are not
        /// modified while being enumerated.
        /// </summary>
        private void ApplySupportConsequences(MatchState state, HashSet<CellCoord> changedSet, List<GameEvent> events)
        {
            List<StructureInstance> unsupportedStructures = null;
            foreach (StructureInstance structure in state.Structures.Values)
            {
                if (StructureLostSupport(structure, changedSet, state.Terrain))
                {
                    (unsupportedStructures ??= new List<StructureInstance>()).Add(structure);
                }
            }

            List<UnitInstance> unsupportedUnits = null;
            foreach (UnitInstance unit in state.Units.Values)
            {
                if (UnitLostSupport(unit, changedSet, state.Terrain))
                {
                    (unsupportedUnits ??= new List<UnitInstance>()).Add(unit);
                }
            }

            if (unsupportedStructures != null)
            {
                foreach (StructureInstance structure in unsupportedStructures)
                {
                    int oldHealth = structure.Health;
                    int newHealth = ConsequenceHealth(oldHealth);
                    structure.Health = newHealth;

                    bool destroyed = newHealth <= 0;
                    if (destroyed)
                    {
                        state.Structures.Remove(structure.Id);
                    }

                    events.Add(new SupportLossEvent(
                        SupportedEntityKind.Structure, structure.Id, structure.OwnerNationId,
                        oldHealth, newHealth, destroyed));
                }
            }

            if (unsupportedUnits != null)
            {
                foreach (UnitInstance unit in unsupportedUnits)
                {
                    int oldHealth = unit.Health;
                    int newHealth = ConsequenceHealth(oldHealth);
                    unit.Health = newHealth;

                    bool destroyed = newHealth <= 0;
                    if (destroyed)
                    {
                        state.Units.Remove(unit.Id);
                        RemoveFromBattalion(state, unit);
                    }

                    events.Add(new SupportLossEvent(
                        SupportedEntityKind.Unit, unit.Id, unit.OwnerNationId,
                        oldHealth, newHealth, destroyed));
                }
            }
        }

        /// <summary>
        /// The health an entity should have after the configured consequence: zero for
        /// <see cref="SupportLossConsequence.Destroy"/>, or the current health reduced by the
        /// configured damage (never below zero) for <see cref="SupportLossConsequence.Damage"/>.
        /// </summary>
        private int ConsequenceHealth(int currentHealth)
        {
            if (_consequence == SupportLossConsequence.Damage)
            {
                return MathEx.ClampNonNegative(currentHealth - _supportLossDamage);
            }

            return 0;
        }

        /// <summary>
        /// True when <paramref name="structure"/> had at least one of its footprint's support cells
        /// removed this batch and is no longer supported by the terrain beneath it (Req 6.4). A
        /// structure resting on the world floor (<c>Origin.Y == 0</c>) or with no footprint cell in the
        /// changed set is never treated as having lost support here.
        /// </summary>
        private static bool StructureLostSupport(
            StructureInstance structure,
            HashSet<CellCoord> changedSet,
            TerrainVolume terrain)
        {
            StructureDef def = structure.Def;
            if (def == null)
            {
                return false;
            }

            int width = def.FootprintWidth;
            int length = def.FootprintLength;
            if (width <= 0 || length <= 0)
            {
                return false;
            }

            CellCoord origin = structure.Origin;
            if (origin.Y <= 0)
            {
                // Rests on the world floor — never loses support from a cell removal.
                return false;
            }

            int belowY = origin.Y - 1;
            bool touched = false;
            for (int dz = 0; dz < length && !touched; dz++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    var below = new CellCoord(origin.X + dx, belowY, origin.Z + dz);
                    if (changedSet.Contains(below))
                    {
                        touched = true;
                        break;
                    }
                }
            }

            if (!touched)
            {
                return false;
            }

            return !terrain.IsSupported(origin, new Int3(width, 0, length));
        }

        /// <summary>
        /// True when the ground <paramref name="unit"/> was standing on a cell that was removed this
        /// batch and no longer has solid terrain directly beneath it (Req 6.4). Flying units and units
        /// resting on the world floor are exempt.
        /// </summary>
        private static bool UnitLostSupport(UnitInstance unit, HashSet<CellCoord> changedSet, TerrainVolume terrain)
        {
            if (unit.Def != null && Pathfinder.IsFlying(unit.Def.Role))
            {
                return false;
            }

            CellCoord cell = ToCell(unit.Position);
            if (cell.Y <= 0)
            {
                return false;
            }

            var below = new CellCoord(cell.X, cell.Y - 1, cell.Z);
            if (!changedSet.Contains(below))
            {
                return false;
            }

            return !terrain.IsSolid(below);
        }

        /// <summary>Removes <paramref name="unit"/> from its owning Nation's Battalion, if any (Req 3.5).</summary>
        private static void RemoveFromBattalion(MatchState state, UnitInstance unit)
        {
            if (unit.BattalionId == null)
            {
                return;
            }

            if (state.Nations.TryGetValue(unit.OwnerNationId, out Nation nation)
                && nation.Battalions.TryGetValue(unit.BattalionId.Value, out Battalion battalion))
            {
                battalion.MemberUnitIds.Remove(unit.Id);
            }
        }

        /// <summary>
        /// Converts a continuous <see cref="WorldPosition"/> to the terrain cell that contains it,
        /// truncating each component toward zero (deterministic, no floating-point).
        /// </summary>
        private static CellCoord ToCell(WorldPosition position)
            => new CellCoord(position.X.ToInt(), position.Y.ToInt(), position.Z.ToInt());
    }
}
