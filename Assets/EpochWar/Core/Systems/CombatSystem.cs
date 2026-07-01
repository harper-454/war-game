using System;
using System.Collections.Generic;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;

namespace EpochWar.Core.Systems
{
    /// <summary>
    /// The authoritative resolver of combat between opposing Units (Requirement 3.7).
    ///
    /// Combat is resolved by the simulation rather than by a player command: when two opposing Units
    /// engage, <see cref="ResolveAttack"/> reduces the defender's health by an amount derived from
    /// the attacker's attack value and the defender's defense value via <see cref="ComputeDamage"/>,
    /// applying the deploying Nations' governance attack/defense multipliers from the
    /// <see cref="CivSystem"/> first (Req 5.5). The resulting health is clamped so it is never
    /// negative, and a defender reduced to zero health is removed from the Match and from any
    /// Battalion through the <see cref="UnitSystem"/> (Req 3.5).
    ///
    /// The damage formula is designed so a greater attacker attack value never produces less damage
    /// (Property 16): it is non-decreasing in attack. The system is stateless — all data lives on the
    /// <see cref="MatchState"/> — and never throws to signal a gameplay outcome; callers inspect the
    /// returned events.
    /// </summary>
    public sealed class CombatSystem : ICommandHandler<IndirectFireCommand>
    {
        // ---- Flanking configuration (Req 9.1-9.3) ----------------------------------------------
        // Front grants no bonus; Side and Rear add to the attack's damage input, with the Rear bonus
        // configured to be >= the Side bonus (Req 9.2). These are added to the effective attack value
        // before ComputeDamage, so they raise damage exactly like extra attack (and remain subject to
        // the same max(1, ...) clamp and the Property 16 monotonicity guarantee).

        /// <summary>Total angular width, in degrees, of the defender's front cone (centered on facing).</summary>
        public const int FrontArcDegrees = 90;

        /// <summary>Angular width, in degrees, of the side band beyond the front cone on each side.</summary>
        public const int SideArcDegrees = 90;

        /// <summary>Flanking_Bonus added to the attack's damage input on a Side flank (Req 9.1).</summary>
        public const int SideFlankingBonus = 2;

        /// <summary>Flanking_Bonus added to the attack's damage input on a Rear flank; &gt;= <see cref="SideFlankingBonus"/> (Req 9.2).</summary>
        public const int RearFlankingBonus = 4;

        // ---- Cover configuration (Req 10.1-10.5) -----------------------------------------------
        // Cover adds to the defender's effective defense. The terrain/elevation bonus and the
        // structure-on-the-line bonus are deliberately different values so tests can prove that
        // overlapping bonuses take the greater rather than the sum (Property 6, Req 10.5).

        /// <summary>Cover_Bonus added to defense when the defender's current cell qualifies for terrain/elevation Cover (Req 10.1).</summary>
        public const int TerrainCoverBonus = 4;

        /// <summary>Cover_Bonus added to defense when a Structure lies on the line of fire (Req 10.2).</summary>
        public const int StructureCoverBonus = 6;

        private readonly CivSystem _civ;
        private readonly UnitSystem _units;
        private readonly TerrainSystem _terrain;
        private readonly BaseSystem _base;
        private readonly VisionSystem _vision;

        // In-flight Indirect_Fire projectiles awaiting resolution, in enqueue order (Req 15.5). Each
        // entry snapshots everything resolution needs — the impact location, the remaining flight
        // time, the Area_Effect radius, and the attacker's effective attack value as of launch — so
        // that resolution is fully independent of the issuing Nation's Spotting status and of the
        // attacker's state (or existence) after launch. Following the same pattern as UnitSystem's
        // internal build queue, this derived, tick-advanced list lives on the system, not on
        // MatchState (design "Where the state lives", Principle 5).
        private readonly List<PendingProjectile> _pendingProjectiles = new List<PendingProjectile>();

        /// <summary>
        /// Builds the combat resolver over the shared <paramref name="civSystem"/> and
        /// <paramref name="unitSystem"/>. The optional <paramref name="terrainSystem"/> supplies the
        /// cover-qualification query used for terrain/elevation Cover (Req 10.1, 10.4); when it is
        /// <c>null</c> the system falls back to calling <see cref="CoverClassifier"/> directly against
        /// <see cref="MatchState.Terrain"/>, so existing two-argument construction keeps working
        /// unchanged and terrain cover is still evaluated whenever terrain data is present.
        ///
        /// The optional <paramref name="baseSystem"/> supplies the authoritative Structure-removal
        /// path used when an Area_Effect attack (Req 11) reduces a Structure to zero health — it frees
        /// the Structure's footprint cells and releases its construction population exactly as any
        /// other removal does (Req 4.5). When it is <c>null</c> (e.g. legacy two/three-argument
        /// construction) the system falls back to removing the destroyed Structure directly from
        /// <see cref="MatchState.Structures"/> and emitting a <see cref="StructureRemovedEvent"/>, so
        /// existing construction keeps working unchanged.
        ///
        /// The optional <paramref name="visionSystem"/> supplies the Spotting query used to validate
        /// an <see cref="IndirectFireCommand"/> (Req 15.2, 15.4): the issuing Nation must currently
        /// have vision of the targeted location's cell. It is kept last so every existing call site
        /// (e.g. <c>new CombatSystem(civ, units, terrain)</c>) continues to compile unchanged; when it
        /// is <c>null</c> the handler treats every target as un-Spotted and rejects the command
        /// accordingly.
        /// </summary>
        public CombatSystem(
            CivSystem civSystem,
            UnitSystem unitSystem,
            TerrainSystem terrainSystem = null,
            BaseSystem baseSystem = null,
            VisionSystem visionSystem = null)
        {
            _civ = civSystem ?? throw new ArgumentNullException(nameof(civSystem));
            _units = unitSystem ?? throw new ArgumentNullException(nameof(unitSystem));
            _terrain = terrainSystem;
            _base = baseSystem;
            _vision = visionSystem;
        }

        /// <summary>
        /// Registers this system's command handlers with the single authoritative
        /// <paramref name="router"/> so Indirect_Fire intents from human Players and AI_Nations alike
        /// flow through the identical pipeline (Req 8.2, 8.5, 15.2). Called by <c>MatchSimulation</c>
        /// during wiring (task 22).
        /// </summary>
        public void RegisterHandlers(CommandRouter router)
        {
            if (router == null)
            {
                throw new ArgumentNullException(nameof(router));
            }

            router.Register<IndirectFireCommand>(this);
        }

        /// <summary>
        /// The core combat damage formula (design "UnitSystem &amp; CombatSystem"): each attack removes
        /// at least one point of health, otherwise the attacker's attack value reduced by half the
        /// defender's defense value (Req 3.7).
        ///
        /// The result is always at least <c>1</c> and is non-decreasing in <paramref name="attack"/>,
        /// so a greater attack value never yields less damage (Property 16). Integer division floors
        /// the defense mitigation.
        /// </summary>
        public static int ComputeDamage(int attack, int defense)
            => System.Math.Max(1, attack - (defense / 2));

        /// <summary>
        /// Resolves a single attack of <paramref name="attackerUnitId"/> against
        /// <paramref name="defenderUnitId"/> (Req 3.7). Applies the attacker Nation's governance
        /// attack multiplier and the defender Nation's governance defense multiplier (Req 5.5),
        /// computes damage via <see cref="ComputeDamage"/>, and reduces the defender's health clamped
        /// at zero. Emits a <see cref="CombatResolvedEvent"/>; if the defender is reduced to zero
        /// health it is removed via the <see cref="UnitSystem"/> and the removal events (including a
        /// <see cref="UnitEliminatedEvent"/>) are appended (Req 3.5).
        ///
        /// Returns no events when either Unit is absent, when they belong to the same Nation, or when
        /// the defender is already at zero health — none of which mutate state.
        /// </summary>
        public IReadOnlyList<GameEvent> ResolveAttack(MatchState state, int attackerUnitId, int defenderUnitId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.Units.TryGetValue(attackerUnitId, out var attacker)
                || !state.Units.TryGetValue(defenderUnitId, out var defender))
            {
                return Array.Empty<GameEvent>();
            }

            // Combat is only between opposing Nations (Req 3.7).
            if (attacker.OwnerNationId == defender.OwnerNationId)
            {
                return Array.Empty<GameEvent>();
            }

            if (defender.Health <= 0)
            {
                return Array.Empty<GameEvent>();
            }

            // The attack "position" a single-target attack flanks/covers from is the attacker's own
            // current position; Area_Effect attacks reuse the same per-target logic but supply the
            // impact point instead (see ResolveAreaAttack), which is exactly why the per-target damage
            // computation and application are factored into the shared helpers below (Property 8).
            int damage = ComputeUnitDamage(state, EffectiveAttack(state, attacker), defender, attacker.Position);

            var events = new List<GameEvent>();
            ApplyDamageToUnit(state, attackerUnitId, defender, damage, events);
            return events;
        }

        /// <summary>
        /// Resolves an Area_Effect attack (Requirement 11): applies <paramref name="attackerUnitId"/>'s
        /// attack, with the given Area_Effect <paramref name="radius"/>, to every
        /// <see cref="UnitInstance"/> and <see cref="StructureInstance"/> whose occupied space has a
        /// nearest point within <paramref name="radius"/> of <paramref name="impactPoint"/> — including
        /// entities belonging to the attacker's own Nation (friendly fire, Req 11.1).
        ///
        /// <para>
        /// Each target is resolved <em>independently</em> with the attack's full, unreduced damage —
        /// the damage pool is never divided among targets (Req 11.2). Every Unit target runs the exact
        /// same effective-attack/flanking/cover/damage/removal logic a single-target
        /// <see cref="ResolveAttack"/> would (via the shared <see cref="ComputeUnitDamage"/> /
        /// <see cref="ApplyDamageToUnit"/> helpers), so a lone Unit inside the radius takes precisely
        /// the damage it would as the sole target of an equivalent single-target attack — with the sole
        /// difference that the attack "position" used for flanking and cover is the
        /// <paramref name="impactPoint"/> rather than the attacker's position (Req 11.3, Property 8).
        /// </para>
        ///
        /// <para>
        /// Flanking is evaluated only for Unit targets and never for Structure targets, which have no
        /// facing (Req 11.3, Property 9). Structures carry no defense stat and no facing, so structure
        /// damage is <c>ComputeDamage(effectiveAttack, 0)</c> with no flanking and no cover applied —
        /// consistent with the base <see cref="ResolveAttack"/> never having damaged Structures.
        /// </para>
        ///
        /// <para>
        /// Targets are iterated in ascending id order so the emitted events and any resulting removals
        /// are deterministic. A destroyed Unit is removed through <see cref="UnitSystem.RemoveUnit"/>
        /// and a destroyed Structure through <see cref="BaseSystem.RemoveStructure"/> (or the direct
        /// fallback when no <see cref="BaseSystem"/> was supplied), matching the existing removal paths.
        /// Already-dead targets (zero health) are skipped. An absent attacker or non-positive radius
        /// yields no events and mutates nothing.
        /// </para>
        /// </summary>
        public IReadOnlyList<GameEvent> ResolveAreaAttack(
            MatchState state, int attackerUnitId, WorldPosition impactPoint, Fixed radius)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (!state.Units.TryGetValue(attackerUnitId, out var attacker))
            {
                return Array.Empty<GameEvent>();
            }

            if (radius <= Fixed.Zero)
            {
                return Array.Empty<GameEvent>();
            }

            var events = new List<GameEvent>();

            // Compute the attacker's effective attack once (it does not vary per target) and delegate
            // to the shared area-damage application used by both this on-demand resolver and the
            // Indirect_Fire tick resolution (which supplies a launch-time snapshot instead).
            ApplyAreaDamage(state, attackerUnitId, EffectiveAttack(state, attacker), impactPoint, radius, events);

            return events;
        }

        /// <summary>
        /// Applies Area_Effect damage with the given already-computed <paramref name="effectiveAttack"/>
        /// to every live Unit and Structure whose occupied space has a nearest point within
        /// <paramref name="radius"/> of <paramref name="impactPoint"/> (Req 11.1-11.3), appending the
        /// produced events to <paramref name="events"/>. Shared by the on-demand
        /// <see cref="ResolveAreaAttack"/> (which computes the effective attack from the live attacker)
        /// and by the Indirect_Fire <see cref="Tick"/> resolution (which supplies the value snapshotted
        /// at launch so resolution is independent of the attacker's later state or existence). The
        /// <paramref name="attackerUnitId"/> is used only to attribute the emitted events; the attacker
        /// instance itself is never dereferenced here.
        /// </summary>
        private void ApplyAreaDamage(
            MatchState state, int attackerUnitId, int effectiveAttack, WorldPosition impactPoint, Fixed radius,
            List<GameEvent> events)
        {
            // Snapshot the candidate ids up front (ascending, for determinism) so that removals during
            // resolution never invalidate the collection being iterated. Only entities that are within
            // the radius and still alive are actually damaged.
            var unitIds = new List<int>(state.Units.Keys);
            unitIds.Sort();
            foreach (int unitId in unitIds)
            {
                if (!state.Units.TryGetValue(unitId, out var target))
                {
                    continue; // removed earlier in this resolution
                }

                if (target.Health <= 0)
                {
                    continue;
                }

                if (!WithinRadius(UnitNearestPointDistanceSquared(target, impactPoint), radius))
                {
                    continue;
                }

                // Full, unreduced, independent damage against this Unit, flanked/covered from the
                // impact point exactly as a lone single-target attack would be (Req 11.2, 11.3).
                int damage = ComputeUnitDamage(state, effectiveAttack, target, impactPoint);
                ApplyDamageToUnit(state, attackerUnitId, target, damage, events);
            }

            var structureIds = new List<int>(state.Structures.Keys);
            structureIds.Sort();
            foreach (int structureId in structureIds)
            {
                if (!state.Structures.TryGetValue(structureId, out var target))
                {
                    continue;
                }

                if (target.Health <= 0)
                {
                    continue;
                }

                if (!WithinRadius(StructureNearestPointDistanceSquared(target, impactPoint), radius))
                {
                    continue;
                }

                int damage = ComputeStructureDamage(effectiveAttack);
                ApplyDamageToStructure(state, attackerUnitId, target, damage, events);
            }
        }

        // ==================================================================
        // Indirect_Fire command handling and in-flight projectile ticking (Req 15)
        // ==================================================================

        /// <summary>
        /// Validates and applies an <see cref="IndirectFireCommand"/> (Req 15.1-15.5). The command is
        /// accepted <em>if and only if</em> both of the following hold (Property 24):
        /// <list type="number">
        ///   <item>the target location is beyond the Artillery_Unit's direct-fire range and within its
        ///     maximum Indirect_Fire range — i.e. <c>DirectFireRange &lt; distance &lt;= IndirectFireRange</c>
        ///     in the planar (X/Z) plane (Req 15.1, 15.2); and</item>
        ///   <item>the issuing Nation currently has Spotting on the target location — its cell is in the
        ///     Nation's Vision_System visible-cell set (Req 15.2).</item>
        /// </list>
        ///
        /// The range gate is checked first, so a rejection reason begins with the distinguishing token
        /// <c>"out-of-range"</c> (Req 15.4) when the target is out of bounds, and <c>"no-spotting"</c>
        /// (Req 15.3) when the target is in range but un-Spotted — every rejection therefore carries an
        /// observable reason distinguishing the two (Property 24). A rejected command mutates nothing:
        /// no projectile is enqueued.
        ///
        /// On acceptance a pending in-flight projectile is enqueued, snapshotting the target location,
        /// the Unit's flight delay, its Area_Effect radius, and its effective attack value as of launch,
        /// so the eventual resolution in <see cref="Tick"/> is independent of any later Spotting change
        /// or attacker state change (Req 15.5); an <see cref="IndirectFireLaunchedEvent"/> is returned.
        ///
        /// A missing Unit, a Unit not owned by the issuing Nation, or a non-Artillery_Unit is likewise
        /// rejected with no mutation, with reasons distinct from the range/Spotting tokens.
        /// </summary>
        public CommandResult Handle(IndirectFireCommand command, MatchState state)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Defensive: the CommandRouter guarantees a present, active issuing Nation, but this
            // handler is also unit-testable directly, so guard rather than index-throw.
            if (!state.Nations.TryGetValue(command.IssuingNationId, out var nation))
            {
                return CommandResult.Reject($"Issuing nation {command.IssuingNationId} does not exist.");
            }

            if (!state.Units.TryGetValue(command.ArtilleryUnitId, out var unit))
            {
                return CommandResult.Reject($"Artillery unit {command.ArtilleryUnitId} does not exist.");
            }

            if (unit.OwnerNationId != nation.Id)
            {
                return CommandResult.Reject(
                    $"Artillery unit {command.ArtilleryUnitId} is not owned by nation {nation.Id}.");
            }

            if (unit.Def == null || !unit.Def.IsArtillery)
            {
                return CommandResult.Reject(
                    $"not-artillery: unit {command.ArtilleryUnitId} (\"{unit.Def?.Id}\") is not an Artillery_Unit.");
            }

            // (1) Range gate (Req 15.1, 15.2, 15.4). Compare squared planar distance against squared
            // ranges to avoid a square root, matching the WithinRadius convention. Accept only when the
            // target is strictly beyond direct-fire range and at or within maximum Indirect_Fire range.
            Fixed dx = command.TargetLocation.X - unit.Position.X;
            Fixed dz = command.TargetLocation.Z - unit.Position.Z;
            Fixed distanceSquared = (dx * dx) + (dz * dz);

            Fixed directRange = unit.Def.DirectFireRange;
            Fixed indirectRange = unit.Def.IndirectFireRange;
            bool beyondDirect = distanceSquared > directRange * directRange;
            bool withinIndirect = distanceSquared <= indirectRange * indirectRange;
            if (!(beyondDirect && withinIndirect))
            {
                return CommandResult.Reject(
                    $"out-of-range: target {command.TargetLocation} is not beyond direct-fire range "
                    + $"and within maximum Indirect_Fire range of artillery {command.ArtilleryUnitId}.");
            }

            // (2) Spotting gate (Req 15.2, 15.3): the issuing Nation must currently have vision of the
            // target location's cell. A null Vision_System means no Spotting is available at all.
            CellCoord targetCell = ToCell(command.TargetLocation);
            bool spotting = _vision != null
                && _vision.GetVisionState(nation.Id).VisibleCells.Contains(targetCell);
            if (!spotting)
            {
                return CommandResult.Reject(
                    $"no-spotting: nation {nation.Id} does not currently have Spotting on target "
                    + $"{command.TargetLocation}.");
            }

            // Accepted: enqueue an in-flight projectile snapshotting everything resolution needs so it
            // is independent of later Spotting loss or attacker state changes (Req 15.5).
            _pendingProjectiles.Add(new PendingProjectile
            {
                AttackerUnitId = command.ArtilleryUnitId,
                TargetLocation = command.TargetLocation,
                RemainingFlightTime = unit.Def.IndirectFireFlightDelay,
                AreaEffectRadius = unit.Def.AreaEffectRadius,
                EffectiveAttackSnapshot = EffectiveAttack(state, unit),
            });

            return CommandResult.Accept(new IndirectFireLaunchedEvent(
                nation.Id,
                command.ArtilleryUnitId,
                command.TargetLocation,
                unit.Def.IndirectFireFlightDelay,
                unit.Def.AreaEffectRadius));
        }

        /// <summary>
        /// Advances every in-flight Indirect_Fire projectile by <paramref name="dt"/> and resolves each
        /// one whose remaining flight time reaches zero, at its stored target location (Req 15.5, 15.6).
        ///
        /// Resolution is deliberately <em>independent</em> of the issuing Nation's current Spotting
        /// status — Spotting is never re-checked here, only at launch — and independent of the
        /// attacker's current state or existence, because each entry carries the effective attack value
        /// snapshotted at launch (Req 15.5). A resolving projectile with a positive stored Area_Effect
        /// radius applies the shared Area_Effect rules via <see cref="ApplyAreaDamage"/> (identical to
        /// <see cref="ResolveAreaAttack"/>, satisfying Property 26); one with a non-positive radius
        /// applies a single-point hit at the target cell via <see cref="ApplyDirectPointDamage"/>
        /// (Req 15.5 for a non-Area_Effect artillery attack).
        ///
        /// A non-positive <paramref name="dt"/> advances no flight time and resolves nothing (guard).
        /// Projectiles are advanced and resolved in enqueue order for deterministic results; each
        /// resolution emits an <see cref="IndirectFireImpactEvent"/> immediately before its damage
        /// events. Resolved (and any already-expired) entries are removed from the pending list.
        /// </summary>
        public IReadOnlyList<GameEvent> Tick(MatchState state, Fixed dt)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (dt <= Fixed.Zero || _pendingProjectiles.Count == 0)
            {
                return Array.Empty<GameEvent>();
            }

            var events = new List<GameEvent>();

            // Advance in enqueue order; keep the still-in-flight entries and resolve the arrived ones.
            var stillInFlight = new List<PendingProjectile>(_pendingProjectiles.Count);
            foreach (var projectile in _pendingProjectiles)
            {
                projectile.RemainingFlightTime = projectile.RemainingFlightTime - dt;
                if (projectile.RemainingFlightTime > Fixed.Zero)
                {
                    stillInFlight.Add(projectile);
                    continue;
                }

                // Arrived: announce the impact, then apply damage at the target location.
                events.Add(new IndirectFireImpactEvent(
                    projectile.AttackerUnitId, projectile.TargetLocation, projectile.AreaEffectRadius));

                if (projectile.AreaEffectRadius > Fixed.Zero)
                {
                    ApplyAreaDamage(
                        state,
                        projectile.AttackerUnitId,
                        projectile.EffectiveAttackSnapshot,
                        projectile.TargetLocation,
                        projectile.AreaEffectRadius,
                        events);
                }
                else
                {
                    ApplyDirectPointDamage(
                        state,
                        projectile.AttackerUnitId,
                        projectile.EffectiveAttackSnapshot,
                        projectile.TargetLocation,
                        events);
                }
            }

            _pendingProjectiles.Clear();
            _pendingProjectiles.AddRange(stillInFlight);

            return events;
        }

        /// <summary>
        /// Resolves a single-point (non-Area_Effect) Indirect_Fire hit at <paramref name="targetLocation"/>
        /// with the launch-time <paramref name="effectiveAttack"/> snapshot: it damages every live Unit
        /// occupying the target cell and every live Structure whose footprint contains the target cell
        /// (Req 15.5). Since an Indirect_Fire attack has no single named defender, it resolves against
        /// whatever entities occupy the target cell, applying the exact same per-target damage logic a
        /// single-target attack would — flanking/cover for Units are evaluated from the target location
        /// as the attack origin, and Structures take the flat effective-attack-vs-zero-defense damage —
        /// via the same <see cref="ApplyDamageToUnit"/>/<see cref="ApplyDamageToStructure"/> paths used
        /// everywhere else. Own-Nation entities on the cell are included, mirroring Area_Effect
        /// friendly-fire semantics (Req 11.1). Entities are processed in ascending id order for
        /// deterministic results.
        /// </summary>
        private void ApplyDirectPointDamage(
            MatchState state, int attackerUnitId, int effectiveAttack, WorldPosition targetLocation,
            List<GameEvent> events)
        {
            CellCoord targetCell = ToCell(targetLocation);

            var unitIds = new List<int>(state.Units.Keys);
            unitIds.Sort();
            foreach (int unitId in unitIds)
            {
                if (!state.Units.TryGetValue(unitId, out var target) || target.Health <= 0)
                {
                    continue;
                }

                if (ToCell(target.Position) != targetCell)
                {
                    continue;
                }

                int damage = ComputeUnitDamage(state, effectiveAttack, target, targetLocation);
                ApplyDamageToUnit(state, attackerUnitId, target, damage, events);
            }

            var structureIds = new List<int>(state.Structures.Keys);
            structureIds.Sort();
            foreach (int structureId in structureIds)
            {
                if (!state.Structures.TryGetValue(structureId, out var target) || target.Health <= 0)
                {
                    continue;
                }

                if (!StructureFootprintContains(target, targetCell))
                {
                    continue;
                }

                int damage = ComputeStructureDamage(effectiveAttack);
                ApplyDamageToStructure(state, attackerUnitId, target, damage, events);
            }
        }

        /// <summary>
        /// True when <paramref name="cell"/> lies within the axis-aligned X/Z footprint rectangle of
        /// <paramref name="structure"/> anchored at its <see cref="StructureInstance.Origin"/> (the
        /// same footprint model <see cref="StructureNearestPointDistanceSquared"/> uses).
        /// </summary>
        private static bool StructureFootprintContains(StructureInstance structure, CellCoord cell)
        {
            int width = System.Math.Max(1, structure.Def?.FootprintWidth ?? 1);
            int length = System.Math.Max(1, structure.Def?.FootprintLength ?? 1);
            return cell.X >= structure.Origin.X && cell.X <= structure.Origin.X + width - 1
                && cell.Z >= structure.Origin.Z && cell.Z <= structure.Origin.Z + length - 1;
        }

        /// <summary>
        /// Computes the damage a single attack with the given already-computed
        /// <paramref name="effectiveAttack"/> deals to Unit <paramref name="defender"/>, treating
        /// <paramref name="sourcePosition"/> as the attack's origin for both flanking classification
        /// (Req 9.1-9.3) and cover evaluation (Req 10.1, 10.2, 10.5). Shared by
        /// <see cref="ResolveAttack"/> (source = attacker position) and the Area_Effect path
        /// (source = impact point) so per-target behavior is identical. The attacker's effective attack
        /// is passed in rather than recomputed so an Indirect_Fire resolution can supply the value
        /// snapshotted at launch (independent of the attacker's later state).
        /// </summary>
        private int ComputeUnitDamage(
            MatchState state, int effectiveAttack, UnitInstance defender, WorldPosition sourcePosition)
        {
            int effectiveDefense = EffectiveDefense(state, defender);

            // (a) Flanking (Req 9.1-9.3): classify the defender's flank relative to the attack's origin
            // and add the configured Flanking_Bonus to the attack's damage input. Front adds nothing,
            // so a front, no-cover attack resolves to exactly the base formula.
            Flank flank = FlankClassifier.Classify(
                defender.Facing,
                defender.Position,
                sourcePosition,
                Fixed.FromInt(FrontArcDegrees),
                Fixed.FromInt(SideArcDegrees));
            effectiveAttack += FlankingBonusFor(flank);

            // (b) Cover (Req 10.1, 10.2, 10.5): apply the GREATER of the terrain/elevation Cover_Bonus
            // (recomputed from the defender's current cell, so it naturally tracks the current
            // position) and the structure-on-the-line-of-fire Cover_Bonus to the defense — never both.
            effectiveDefense += CoverBonus(state, sourcePosition, defender);

            return ComputeDamage(effectiveAttack, effectiveDefense);
        }

        /// <summary>
        /// Computes the damage a single attack with the given <paramref name="effectiveAttack"/> deals
        /// to a Structure. Structures have no defense stat and no facing, so no defense mitigation, no
        /// cover, and no flanking are applied — the damage is the attacker's effective attack reduced
        /// by a defense of zero (Req 11.2, 11.3).
        /// </summary>
        private static int ComputeStructureDamage(int effectiveAttack)
            => ComputeDamage(effectiveAttack, 0);

        /// <summary>
        /// Applies <paramref name="damage"/> to Unit <paramref name="defender"/>, clamping its health
        /// at zero, appending a <see cref="CombatResolvedEvent"/>, and removing the Unit through
        /// <see cref="UnitSystem.RemoveUnit"/> (appending its removal events) when it reaches zero
        /// health (Req 3.5). Shared by single-target and Area_Effect resolution.
        /// </summary>
        private void ApplyDamageToUnit(
            MatchState state, int attackerUnitId, UnitInstance defender, int damage, List<GameEvent> events)
        {
            int oldHealth = defender.Health;
            int newHealth = MathEx.ClampNonNegative(oldHealth - damage);
            defender.Health = newHealth;

            events.Add(new CombatResolvedEvent(attackerUnitId, defender.Id, damage, oldHealth, newHealth));

            // Zero-health units are fully removed from the Match and any Battalion (Req 3.5).
            if (newHealth <= 0)
            {
                events.AddRange(_units.RemoveUnit(state, defender.Id));
            }
        }

        /// <summary>
        /// Applies <paramref name="damage"/> to Structure <paramref name="defender"/>, clamping its
        /// health at zero, appending a <see cref="StructureCombatResolvedEvent"/>, and removing the
        /// Structure when it reaches zero health (Req 4.5). Removal goes through
        /// <see cref="BaseSystem.RemoveStructure"/> when a <see cref="BaseSystem"/> was supplied (so
        /// footprint cells and construction population are released), otherwise it falls back to a
        /// direct removal plus a <see cref="StructureRemovedEvent"/>.
        /// </summary>
        private void ApplyDamageToStructure(
            MatchState state, int attackerUnitId, StructureInstance defender, int damage, List<GameEvent> events)
        {
            int oldHealth = defender.Health;
            int newHealth = MathEx.ClampNonNegative(oldHealth - damage);
            defender.Health = newHealth;

            events.Add(new StructureCombatResolvedEvent(
                attackerUnitId, defender.Id, damage, oldHealth, newHealth));

            if (newHealth <= 0)
            {
                if (_base != null)
                {
                    events.AddRange(_base.RemoveStructure(state, defender.Id));
                }
                else if (state.Structures.Remove(defender.Id))
                {
                    bool wasIncompletePeaceArch = (defender.Def?.IsPeaceArch ?? false) && !defender.IsOperational;
                    events.Add(new StructureRemovedEvent(
                        defender.OwnerNationId, defender.Id, defender.Def?.Id, wasIncompletePeaceArch));
                }
            }
        }

        /// <summary>
        /// The squared planar (X/Z) distance from a Unit's occupied point to
        /// <paramref name="impactPoint"/>. A Unit occupies a single point, so its nearest point is that
        /// point itself. Squared distance is compared against squared radius to avoid a square root and
        /// keep the check deterministic fixed-point arithmetic (Req 11.1).
        /// </summary>
        private static Fixed UnitNearestPointDistanceSquared(UnitInstance unit, WorldPosition impactPoint)
        {
            Fixed dx = unit.Position.X - impactPoint.X;
            Fixed dz = unit.Position.Z - impactPoint.Z;
            return (dx * dx) + (dz * dz);
        }

        /// <summary>
        /// The squared planar (X/Z) distance from the nearest point of a Structure's footprint to
        /// <paramref name="impactPoint"/>. A Structure occupies a
        /// <see cref="StructureDef.FootprintWidth"/> x <see cref="StructureDef.FootprintLength"/>
        /// axis-aligned rectangle of cells anchored at its <see cref="StructureInstance.Origin"/>; the
        /// nearest point of that rectangle to the impact point is found by clamping the impact point's
        /// X/Z into the footprint's bounds (the standard point-to-AABB nearest-point computation). A
        /// single-cell footprint therefore reduces to the origin cell (Req 11.1).
        /// </summary>
        private static Fixed StructureNearestPointDistanceSquared(
            StructureInstance structure, WorldPosition impactPoint)
        {
            int width = System.Math.Max(1, structure.Def?.FootprintWidth ?? 1);
            int length = System.Math.Max(1, structure.Def?.FootprintLength ?? 1);

            Fixed minX = Fixed.FromInt(structure.Origin.X);
            Fixed maxX = Fixed.FromInt(structure.Origin.X + width - 1);
            Fixed minZ = Fixed.FromInt(structure.Origin.Z);
            Fixed maxZ = Fixed.FromInt(structure.Origin.Z + length - 1);

            Fixed nearestX = impactPoint.X.Clamp(minX, maxX);
            Fixed nearestZ = impactPoint.Z.Clamp(minZ, maxZ);

            Fixed dx = nearestX - impactPoint.X;
            Fixed dz = nearestZ - impactPoint.Z;
            return (dx * dx) + (dz * dz);
        }

        /// <summary>
        /// True when a target whose nearest-point squared distance to the impact point is
        /// <paramref name="distanceSquared"/> lies within (inclusive of) an Area_Effect
        /// <paramref name="radius"/>. Compares squared values so no square root is needed (Req 11.1).
        /// </summary>
        private static bool WithinRadius(Fixed distanceSquared, Fixed radius)
            => distanceSquared <= radius * radius;

        /// <summary>
        /// The attacker's effective attack value after applying its Nation's governance attack
        /// multiplier (Req 5.5), rounded to the nearest integer (deterministic half-away-from-zero
        /// rounding) and floored at zero, then plus the attacker's current Veterancy_Tier attack bonus
        /// (Req 12.2). A Unit with no <c>VeterancyCurve</c> or sitting at the base tier (index 0,
        /// authored with no bonus) adds nothing, so no-veterancy combat is byte-identical to before
        /// this expansion — keeping the base combat property (Property 16) and the flanking/cover/AoE
        /// property tests unchanged.
        /// </summary>
        private int EffectiveAttack(MatchState state, UnitInstance attacker)
        {
            int baseAttack = attacker.Def?.Attack ?? 0;
            int veterancy = VeterancyAttackBonus(attacker);
            if (!state.Nations.TryGetValue(attacker.OwnerNationId, out var nation))
            {
                return MathEx.ClampNonNegative(baseAttack) + veterancy;
            }

            float multiplier = _civ.GetUnitAttackMultiplier(nation);
            return RoundToNonNegativeInt(baseAttack * multiplier) + veterancy;
        }

        /// <summary>
        /// The defender's effective defense value after applying its Nation's governance defense
        /// multiplier (Req 5.5), rounded to the nearest integer (deterministic half-away-from-zero
        /// rounding) and floored at zero, then plus the defender's current Veterancy_Tier defense bonus
        /// (Req 12.2). A Unit with no <c>VeterancyCurve</c> or at the base tier adds nothing (see
        /// <see cref="EffectiveAttack"/>).
        /// </summary>
        private int EffectiveDefense(MatchState state, UnitInstance defender)
        {
            int baseDefense = defender.Def?.Defense ?? 0;
            int veterancy = VeterancyDefenseBonus(defender);
            if (!state.Nations.TryGetValue(defender.OwnerNationId, out var nation))
            {
                return MathEx.ClampNonNegative(baseDefense) + veterancy;
            }

            float multiplier = _civ.GetUnitDefenseMultiplier(nation);
            return RoundToNonNegativeInt(baseDefense * multiplier) + veterancy;
        }

        /// <summary>
        /// The attack bonus granted by <paramref name="unit"/>'s current Veterancy_Tier (Req 12.2):
        /// the <see cref="State.Content.VeterancyTierDef.AttackBonus"/> of the tier its
        /// <see cref="UnitInstance.VeterancyTierIndex"/> points at, or zero when the Unit type defines
        /// no <c>VeterancyCurve</c> or the index is out of range. The bonus is read from the Unit's
        /// current tier so it is always consistent with the tier the veterancy hook has advanced it to.
        /// </summary>
        private static int VeterancyAttackBonus(UnitInstance unit)
        {
            var curve = unit.Def?.VeterancyCurve;
            if (curve == null || curve.Count == 0)
            {
                return 0;
            }

            int index = unit.VeterancyTierIndex;
            if (index < 0 || index >= curve.Count || curve[index] == null)
            {
                return 0;
            }

            return curve[index].AttackBonus;
        }

        /// <summary>
        /// The defense bonus granted by <paramref name="unit"/>'s current Veterancy_Tier (Req 12.2);
        /// see <see cref="VeterancyAttackBonus"/> for the lookup rules.
        /// </summary>
        private static int VeterancyDefenseBonus(UnitInstance unit)
        {
            var curve = unit.Def?.VeterancyCurve;
            if (curve == null || curve.Count == 0)
            {
                return 0;
            }

            int index = unit.VeterancyTierIndex;
            if (index < 0 || index >= curve.Count || curve[index] == null)
            {
                return 0;
            }

            return curve[index].DefenseBonus;
        }

        /// <summary>
        /// The Flanking_Bonus added to the attack's damage input for the given <paramref name="flank"/>:
        /// zero for <see cref="Flank.Front"/> (Req 9.3), <see cref="SideFlankingBonus"/> for
        /// <see cref="Flank.Side"/> (Req 9.1), and <see cref="RearFlankingBonus"/> for
        /// <see cref="Flank.Rear"/> (Req 9.2, configured &gt;= the side bonus).
        /// </summary>
        private static int FlankingBonusFor(Flank flank)
        {
            switch (flank)
            {
                case Flank.Side: return SideFlankingBonus;
                case Flank.Rear: return RearFlankingBonus;
                default: return 0; // Front
            }
        }

        /// <summary>
        /// The Cover_Bonus applied to the defender's effective defense for this attack: the greater of
        /// the terrain/elevation Cover_Bonus (when the defender's current cell qualifies) and the
        /// structure-on-the-line Cover_Bonus (when a Structure lies on the direct line between the
        /// attack's <paramref name="sourcePosition"/> and the defender), never their sum (Req 10.1,
        /// 10.2, 10.5). Returns zero when neither applies. The <paramref name="sourcePosition"/> is the
        /// attacker's position for a single-target attack and the impact point for an Area_Effect one.
        /// </summary>
        private int CoverBonus(MatchState state, WorldPosition sourcePosition, UnitInstance defender)
        {
            CellCoord defenderCell = ToCell(defender.Position);
            CellCoord attackerCell = ToCell(sourcePosition);

            int terrainBonus = QualifiesForTerrainCover(state, defenderCell, attackerCell)
                ? TerrainCoverBonus
                : 0;

            int structureBonus = StructureOnLineOfFire(state, attackerCell, defenderCell)
                ? StructureCoverBonus
                : 0;

            return System.Math.Max(terrainBonus, structureBonus);
        }

        /// <summary>
        /// Whether the defender's current cell qualifies for terrain/elevation Cover relative to the
        /// attacker's cell (Req 10.1, 10.4). Prefers the <see cref="TerrainSystem"/> query when one was
        /// supplied, otherwise evaluates <see cref="CoverClassifier"/> directly against
        /// <see cref="MatchState.Terrain"/> so the behavior is identical either way.
        /// </summary>
        private bool QualifiesForTerrainCover(MatchState state, CellCoord defenderCell, CellCoord attackerCell)
        {
            if (_terrain != null)
            {
                return _terrain.GetCoverQualification(state.Terrain, defenderCell, attackerCell);
            }

            if (state.Terrain == null)
            {
                return false;
            }

            CellMaterial material = state.Terrain.Get(defenderCell).Material;
            return CoverClassifier.IsCoverQualifying(material, defenderCell.Y, attackerCell.Y);
        }

        /// <summary>
        /// True when any placed Structure's footprint origin cell lies strictly on the direct line
        /// between the attacker and defender cells in the horizontal (X/Z) plane (Req 10.2). Uses an
        /// exact integer collinearity test plus a bounding-box between-check; the two endpoints
        /// themselves (the combatants' own cells) never count as intervening cover.
        /// </summary>
        private static bool StructureOnLineOfFire(MatchState state, CellCoord attackerCell, CellCoord defenderCell)
        {
            foreach (StructureInstance structure in state.Structures.Values)
            {
                if (IsOnHorizontalSegmentExclusive(attackerCell, defenderCell, structure.Origin))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Deterministic integer test for whether <paramref name="p"/> lies on the open line segment
        /// between <paramref name="a"/> and <paramref name="b"/> in the X/Z plane (endpoints excluded).
        /// Collinearity is the exact zero cross-product of the direction and offset vectors; the
        /// bounding-box comparison confirms the point is between the endpoints rather than on the
        /// extended line.
        /// </summary>
        private static bool IsOnHorizontalSegmentExclusive(CellCoord a, CellCoord b, CellCoord p)
        {
            // Exclude the endpoints (the attacker's / defender's own cells).
            if ((p.X == a.X && p.Z == a.Z) || (p.X == b.X && p.Z == b.Z))
            {
                return false;
            }

            long abx = (long)b.X - a.X;
            long abz = (long)b.Z - a.Z;
            long apx = (long)p.X - a.X;
            long apz = (long)p.Z - a.Z;

            // Collinear iff the 2D cross product is zero.
            if ((abx * apz) - (abz * apx) != 0)
            {
                return false;
            }

            // Between iff inside the axis-aligned bounding box of the segment.
            int minX = System.Math.Min(a.X, b.X);
            int maxX = System.Math.Max(a.X, b.X);
            int minZ = System.Math.Min(a.Z, b.Z);
            int maxZ = System.Math.Max(a.Z, b.Z);

            return p.X >= minX && p.X <= maxX && p.Z >= minZ && p.Z <= maxZ;
        }

        /// <summary>
        /// Converts a continuous <see cref="WorldPosition"/> to the terrain cell that contains it,
        /// truncating each component toward zero (deterministic, no floating-point).
        /// </summary>
        private static CellCoord ToCell(WorldPosition position)
            => new CellCoord(position.X.ToInt(), position.Y.ToInt(), position.Z.ToInt());

        private static int RoundToNonNegativeInt(float value)
        {
            if (value <= 0f)
            {
                return 0;
            }

            return (int)System.Math.Round(value, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// An in-flight Indirect_Fire projectile awaiting resolution (Req 15.5). Every field is a
        /// launch-time snapshot so that resolution in <see cref="Tick"/> is fully independent of the
        /// issuing Nation's later Spotting status and of the firing Artillery_Unit's later state or
        /// existence — the projectile is already in flight and must land regardless.
        /// </summary>
        private sealed class PendingProjectile
        {
            /// <summary>The firing Artillery_Unit's id, retained only to attribute the emitted events.</summary>
            public int AttackerUnitId;

            /// <summary>The world-space location the projectile resolves at.</summary>
            public WorldPosition TargetLocation;

            /// <summary>Remaining flight time; the projectile resolves once this reaches zero.</summary>
            public Fixed RemainingFlightTime;

            /// <summary>The Area_Effect radius applied at resolution (0 = single-point).</summary>
            public Fixed AreaEffectRadius;

            /// <summary>The attacker's effective attack value as of launch, used for the applied damage.</summary>
            public int EffectiveAttackSnapshot;
        }
    }
}
