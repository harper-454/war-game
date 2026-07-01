using System.Collections.Generic;
using System.Linq;
using EpochWar.Core.Commands;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for the Unit abilities added in task 18
    /// (<c>UnitSystem.Handle(ActivateAbilityCommand)</c> and the per-tick cooldown decrement), each
    /// exercised for at least the design-mandated 100 generated iterations (design.md "Testing
    /// Strategy").
    ///
    /// Covered properties, tagged
    /// <c>Feature: epoch-war-combat-visuals-expansion, Property 14/15/16/17</c>:
    /// <list type="bullet">
    ///   <item>Property 14 — Available abilities exactly match the Unit type's defined list (Req 13.1).</item>
    ///   <item>Property 15 — Valid activation executes, deducts cost, and starts cooldown (Req 13.2).</item>
    ///   <item>Property 16 — Invalid activation is rejected without state change, with distinct
    ///     reasons (Req 13.3).</item>
    ///   <item>Property 17 — Remaining cooldown decreases monotonically to zero, never negative
    ///     (Req 13.4).</item>
    /// </list>
    ///
    /// NOTE: these are this expansion's own Property 14–17 (epoch-war-combat-visuals-expansion),
    /// distinct from the base spec's Properties 14–17. All scenarios target the engine-free
    /// <c>EpochWar.Core</c> assembly with no Unity Play loop.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class UnitAbilityPropertyTests
    {
        private const int MinimumIterations = 100;
        private const ResourceType CostType = ResourceType.Metal;

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefWithAbilities(List<UnitAbilityDef> abilities, int maxHealth = 100)
            => new UnitDef("hero", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, 5, 2, 1f,
                UnitRole.Soldier, abilityDefs: abilities);

        private static (MatchState state, UnitSystem units, ResourceSystem res, Nation nation) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ);
            var state = new MatchState(new TerrainVolume(new Int3(4, 4, 4), CellMaterial.Soil));
            var nation = new Nation(1);
            state.Nations[1] = nation;
            return (state, units, res, nation);
        }

        // ---- Property 14 ------------------------------------------------------------------------

        /// <summary>
        /// Property 14: Available abilities exactly match the Unit type's defined list.
        ///
        /// A Unit reports exactly the ability ids defined on its type — no more, no fewer: every
        /// defined ability is recognized and activatable (accepted when free and ready), and any id
        /// that is not on the type's list is rejected as an unknown ability.
        ///
        /// **Validates: Requirements 13.1**
        /// </summary>
        [Test]
        [Category("Property 14")]
        public void Property14_AvailableAbilitiesExactlyMatchDefinedList()
        {
            // Generate a distinct, non-empty set of ability ids as small integers rendered to strings.
            var gen = from ids in Gen.NonEmptyListOf(Gen.Choose(0, 12))
                      from bogus in Gen.Choose(100, 200)
                      select (ids.Distinct().ToList(), bogus);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (idNums, bogusNum) = tuple;
                var abilityIds = idNums.Select(n => $"a{n}").ToList();
                var abilities = abilityIds
                    .Select(id => new UnitAbilityDef(id, Fixed.FromInt(5), ResourceCost.Free, AbilityEffectKind.Buff))
                    .ToList();
                var def = UnitDefWithAbilities(abilities);

                var (state, units, _, _) = Build();

                // The instance's available ability set equals exactly the def's list.
                var unit = new UnitInstance(1, 1, def, WorldPosition.Zero);
                var reported = new HashSet<string>(unit.Def.AbilityDefs.Select(a => a.Id));
                if (!reported.SetEquals(abilityIds)) return false;

                // Each defined ability is recognized (free + ready => accepted).
                foreach (var id in abilityIds)
                {
                    state.Units[1] = new UnitInstance(1, 1, def, WorldPosition.Zero);
                    var ok = units.Handle(new ActivateAbilityCommand(1, 1, id), state);
                    if (!ok.Accepted) return false;
                }

                // An id not on the list is rejected as unknown, never activated.
                string bogusId = $"a{bogusNum}"; // outside the 0..12 range used above
                state.Units[1] = new UnitInstance(1, 1, def, WorldPosition.Zero);
                var rejected = units.Handle(new ActivateAbilityCommand(1, 1, bogusId), state);
                return !rejected.Accepted && !abilityIds.Contains(bogusId);
            }).Check(Config());
        }

        // ---- Property 15 ------------------------------------------------------------------------

        /// <summary>
        /// Property 15: Ability activation under valid preconditions executes, deducts cost, and starts
        /// cooldown.
        ///
        /// With the cooldown elapsed and the pool covering the cost, activating a Heal ability heals the
        /// Unit (effect executed), reduces the pool by exactly the cost, and sets the remaining cooldown
        /// to the ability's full defined duration.
        ///
        /// **Validates: Requirements 13.2**
        /// </summary>
        [Test]
        [Category("Property 15")]
        public void Property15_ValidActivationExecutesDeductsAndStartsCooldown()
        {
            var gen = from cost in Gen.Choose(0, 100)
                      from extra in Gen.Choose(0, 100)
                      from cooldown in Gen.Choose(1, 30)
                      from damage in Gen.Choose(1, 60)
                      select (cost, extra, cooldown, damage);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (cost, extra, cooldown, damage) = tuple;
                var ability = new UnitAbilityDef(
                    "heal", Fixed.FromInt(cooldown), ResourceCost.Single(CostType, cost), AbilityEffectKind.Heal);
                var def = UnitDefWithAbilities(new List<UnitAbilityDef> { ability }, maxHealth: 100);

                var (state, units, res, nation) = Build();
                res.Produce(nation, CostType, cost + extra);
                float poolBefore = res.GetAmount(nation, CostType);

                var unit = new UnitInstance(1, 1, def, WorldPosition.Zero) { Health = 100 - damage };
                state.Units[1] = unit;
                int healthBefore = unit.Health;

                var result = units.Handle(new ActivateAbilityCommand(1, 1, "heal"), state);
                if (!result.Accepted) return false;

                // Cost deducted exactly.
                if (res.GetAmount(nation, CostType) != poolBefore - cost) return false;

                // Cooldown started at the full defined duration.
                if (!unit.AbilityRemainingCooldown.TryGetValue("heal", out var remaining)
                    || remaining != Fixed.FromInt(cooldown))
                {
                    return false;
                }

                // Effect executed: the Unit healed by the default amount, clamped at MaxHealth.
                int expectedHealth = System.Math.Min(100, healthBefore + UnitSystem.DefaultAbilityHealAmount);
                if (unit.Health != expectedHealth) return false;

                // An AbilityActivatedEvent was emitted for this activation.
                return result.Events.OfType<AbilityActivatedEvent>()
                    .Any(e => e.UnitId == 1 && e.AbilityId == "heal");
            }).Check(Config());
        }

        // ---- Property 16 ------------------------------------------------------------------------

        /// <summary>
        /// Property 16: Ability activation under invalid preconditions is rejected without state change,
        /// with distinct reasons.
        ///
        /// When the cooldown is still active the activation is rejected with a <c>"cooldown-active"</c>
        /// reason; when the pool is insufficient (and the cooldown is ready) it is rejected with an
        /// <c>"insufficient-resources"</c> reason. In both cases the pool and the cooldown state are
        /// left exactly unchanged.
        ///
        /// **Validates: Requirements 13.3**
        /// </summary>
        [Test]
        [Category("Property 16")]
        public void Property16_InvalidActivationRejectedWithoutStateChange()
        {
            var gen = from cost in Gen.Choose(1, 100)
                      from shortfall in Gen.Choose(1, 50)
                      from cooldownRemaining in Gen.Choose(1, 30)
                      from cooldownDuration in Gen.Choose(1, 30)
                      select (cost, shortfall, cooldownRemaining, cooldownDuration);

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (cost, shortfall, cooldownRemaining, cooldownDuration) = tuple;
                var ability = new UnitAbilityDef(
                    "bolt", Fixed.FromInt(cooldownDuration), ResourceCost.Single(CostType, cost),
                    AbilityEffectKind.Bombard);
                var def = UnitDefWithAbilities(new List<UnitAbilityDef> { ability });

                // --- Case A: cooldown active, resources sufficient => "cooldown-active" ---
                {
                    var (state, units, res, nation) = Build();
                    res.Produce(nation, CostType, cost + 100); // affordable
                    float poolBefore = res.GetAmount(nation, CostType);

                    var unit = new UnitInstance(1, 1, def, WorldPosition.Zero);
                    unit.AbilityRemainingCooldown["bolt"] = Fixed.FromInt(cooldownRemaining);
                    state.Units[1] = unit;

                    var result = units.Handle(new ActivateAbilityCommand(1, 1, "bolt"), state);
                    if (result.Accepted) return false;
                    if (result.RejectReason == null || !result.RejectReason.Contains("cooldown-active")) return false;
                    // No state change.
                    if (res.GetAmount(nation, CostType) != poolBefore) return false;
                    if (unit.AbilityRemainingCooldown["bolt"] != Fixed.FromInt(cooldownRemaining)) return false;
                }

                // --- Case B: cooldown ready, resources insufficient => "insufficient-resources" ---
                {
                    var (state, units, res, nation) = Build();
                    int pool = cost - System.Math.Min(shortfall, cost); // strictly less than cost
                    if (pool >= cost) pool = cost - 1;
                    if (pool < 0) pool = 0;
                    res.Produce(nation, CostType, pool);
                    float poolBefore = res.GetAmount(nation, CostType);

                    var unit = new UnitInstance(1, 1, def, WorldPosition.Zero); // no cooldown entry = ready
                    state.Units[1] = unit;

                    var result = units.Handle(new ActivateAbilityCommand(1, 1, "bolt"), state);
                    if (result.Accepted) return false;
                    if (result.RejectReason == null || !result.RejectReason.Contains("insufficient-resources")) return false;
                    // No state change: pool untouched and cooldown still ready (absent).
                    if (res.GetAmount(nation, CostType) != poolBefore) return false;
                    if (unit.AbilityRemainingCooldown.ContainsKey("bolt")) return false;
                }

                return true;
            }).Check(Config());
        }

        // ---- Property 17 ------------------------------------------------------------------------

        /// <summary>
        /// Property 17: Remaining cooldown decreases monotonically to zero and never goes negative.
        ///
        /// Across a sequence of elapsed-time ticks, a Unit's remaining ability cooldown is non-increasing,
        /// is never negative, and reaches exactly zero once at least the full duration has elapsed.
        ///
        /// **Validates: Requirements 13.4**
        /// </summary>
        [Test]
        [Category("Property 17")]
        public void Property17_RemainingCooldownDecreasesMonotonicallyToZero()
        {
            var gen = from duration in Gen.Choose(1, 20)
                      from steps in Gen.NonEmptyListOf(Gen.Choose(1, 4))
                      select (duration, steps.ToList());

            Prop.ForAll(Arb.From(gen), tuple =>
            {
                var (duration, steps) = tuple;
                var ability = new UnitAbilityDef(
                    "dash", Fixed.FromInt(duration), ResourceCost.Free, AbilityEffectKind.Buff);
                var def = UnitDefWithAbilities(new List<UnitAbilityDef> { ability });

                var (state, units, _, _) = Build();
                var unit = new UnitInstance(1, 1, def, WorldPosition.Zero);
                unit.AbilityRemainingCooldown["dash"] = Fixed.FromInt(duration);
                state.Units[1] = unit;

                Fixed previous = unit.AbilityRemainingCooldown["dash"];
                int elapsed = 0;
                foreach (var dt in steps)
                {
                    units.Tick(state, dt);
                    elapsed += dt;

                    Fixed current = unit.AbilityRemainingCooldown["dash"];

                    // Never negative.
                    if (current < Fixed.Zero) return false;
                    // Non-increasing.
                    if (current > previous) return false;

                    // Once the full duration has elapsed, it must be exactly zero.
                    if (elapsed >= duration && current != Fixed.Zero) return false;

                    previous = current;
                }

                return true;
            }).Check(Config());
        }
    }
}
