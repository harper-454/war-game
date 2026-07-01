using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Unity.UI;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based test for the selection information panel view-model
    /// (<see cref="InfoPanelViewModel"/>), validating the universal correctness property from
    /// design.md:
    ///
    /// <para><b>Property 29 — Info panel content includes all entity attributes (Req 7.2).</b>
    /// <i>For any selected Unit, Battalion, or Structure, the information panel's view-model
    /// contains every detailed attribute of that entity.</i></para>
    ///
    /// The property is universally quantified over generated Units, Structures, and Battalions and
    /// exercised for at least the design-mandated minimum of 100 generated cases (see design.md,
    /// "Testing Strategy"). Every test is tagged <c>Feature: epoch-war-game</c> and
    /// <c>Property 29</c>, and carries the requirement it validates.
    ///
    /// <para>Because <see cref="InfoPanelViewModel"/> is an intentionally <c>UnityEngine</c>-free
    /// class (it lives in the <c>EpochWar.Unity</c> assembly but depends only on <c>EpochWar.Core</c>
    /// value types), its content can be verified directly with no Unity Play loop. Each case builds a
    /// randomized entity, produces the view-model via the matching <c>For*</c> factory, and asserts
    /// two things: <b>completeness</b> — every detailed attribute name in the entity's schema (Req 3.6
    /// for Units, Req 4 for Structures, Req 3.3 for Battalions) is present — and <b>fidelity</b> —
    /// each scalar attribute's formatted value equals the entity's actual value.</para>
    ///
    /// NOTE (asmdef): in a real Unity project this EditMode test requires the test asmdef
    /// (<c>EpochWar.Tests.EditMode</c>) to reference <c>EpochWar.Unity</c> in addition to
    /// <c>EpochWar.Core</c>, because it exercises <see cref="InfoPanelViewModel"/>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-game")]
    public sealed class InfoPanelViewModelPropertyTests
    {
        // Every property in this feature runs at least this many generated cases (>= 100 required).
        private const int MinimumIterations = 200;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static void Check(Property property)
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            property.Check(config);
        }

        // ---- formatting mirrors InfoPanelViewModel.Builder so value fidelity is exact ----
        private static string Fmt(int value) => value.ToString(Inv);
        private static string Fmt(bool value) => value ? "true" : "false";
        private static string Fmt(float value) => value.ToString("0.###", Inv);

        /// <summary>
        /// Asserts that <paramref name="vm"/> contains an attribute called <paramref name="name"/>
        /// (completeness) whose formatted value equals <paramref name="expected"/> (fidelity).
        /// </summary>
        private static bool HasAttributeWithValue(InfoPanelViewModel vm, string name, string expected)
            => vm.HasAttribute(name) && vm.TryGetValue(name, out var actual) && actual == expected;

        /// <summary>Asserts merely that <paramref name="name"/> is present (completeness only).</summary>
        private static bool HasAttribute(InfoPanelViewModel vm, string name) => vm.HasAttribute(name);

        // ==================================================================
        // Property 29 (Units): every detailed Unit attribute is present with the correct value.
        // ==================================================================

        /// <summary>
        /// For any selected Unit, the view-model exposes every instance attribute (id, owner, health,
        /// position, battalion membership, order) and every static <see cref="UnitDef"/> attribute
        /// (Era of origin, role, max health, attack, defense, move speed, population cost, build time,
        /// costs) required by Req 3.6 / 7.2.
        ///
        /// **Validates: Requirements 7.2**
        /// </summary>
        [Test]
        [Category("Property 29")]
        public void Property29_Unit_ViewModelContainsEveryAttribute()
        {
            var gen =
                from id in Gen.Choose(1, 100000)
                from owner in Gen.Choose(1, 8)
                from eraIdx in Gen.Choose(0, 8)
                from roleIdx in Gen.Choose(0, 4)
                from maxHealth in Gen.Choose(1, 500)
                from health in Gen.Choose(0, 500)
                from attack in Gen.Choose(0, 300)
                from defense in Gen.Choose(0, 300)
                from moveTenths in Gen.Choose(0, 200)
                from popCost in Gen.Choose(0, 50)
                from buildTenths in Gen.Choose(0, 600)
                from costMetal in Gen.Choose(0, 100)
                from hasBattalion in Gen.Choose(0, 1)
                from battalionId in Gen.Choose(1, 50)
                from px in Gen.Choose(0, 32)
                from py in Gen.Choose(0, 8)
                from pz in Gen.Choose(0, 32)
                select new
                {
                    id, owner, eraIdx, roleIdx, maxHealth, health, attack, defense,
                    moveTenths, popCost, buildTenths, costMetal, hasBattalion, battalionId, px, py, pz
                };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var era = (Era)g.eraIdx;
                var role = (UnitRole)g.roleIdx;
                float moveSpeed = g.moveTenths / 10f;
                float buildTime = g.buildTenths / 10f;
                var cost = g.costMetal > 0 ? ResourceCost.Single(ResourceType.Metal, g.costMetal) : ResourceCost.Free;

                var def = new UnitDef(
                    id: "u_test", era: era, cost: cost, buildTimeSeconds: buildTime,
                    populationCost: g.popCost, maxHealth: g.maxHealth, attack: g.attack,
                    defense: g.defense, moveSpeed: moveSpeed, role: role);

                var unit = new UnitInstance(g.id, g.owner, def, WorldPosition.FromInts(g.px, g.py, g.pz))
                {
                    Health = g.health,
                    BattalionId = g.hasBattalion == 1 ? (int?)g.battalionId : null,
                };

                var vm = InfoPanelViewModel.ForUnit(unit);

                if (vm.Kind != InfoEntityKind.Unit || vm.EntityId != $"Unit#{g.id}")
                {
                    return false;
                }

                // Instance attributes: present and faithful.
                bool instanceOk =
                    HasAttributeWithValue(vm, "Id", Fmt(g.id))
                    && HasAttributeWithValue(vm, "OwnerNationId", Fmt(g.owner))
                    && HasAttributeWithValue(vm, "Health", Fmt(g.health))
                    && HasAttributeWithValue(vm, "PositionX", Fmt((float)g.px))
                    && HasAttributeWithValue(vm, "PositionY", Fmt((float)g.py))
                    && HasAttributeWithValue(vm, "PositionZ", Fmt((float)g.pz))
                    && HasAttributeWithValue(vm, "BattalionId",
                        g.hasBattalion == 1 ? Fmt(g.battalionId) : "None")
                    && HasAttribute(vm, "Order");

                // Static def attributes: present and faithful (Req 3.6 explicitly requires health,
                // attack, defense, move speed and Era of origin).
                bool defOk =
                    HasAttributeWithValue(vm, "DefId", "u_test")
                    && HasAttributeWithValue(vm, "Era", era.ToString())
                    && HasAttributeWithValue(vm, "Role", role.ToString())
                    && HasAttributeWithValue(vm, "MaxHealth", Fmt(g.maxHealth))
                    && HasAttributeWithValue(vm, "Attack", Fmt(g.attack))
                    && HasAttributeWithValue(vm, "Defense", Fmt(g.defense))
                    && HasAttributeWithValue(vm, "MoveSpeed", Fmt(moveSpeed))
                    && HasAttributeWithValue(vm, "PopulationCost", Fmt(g.popCost))
                    && HasAttributeWithValue(vm, "BuildTimeSeconds", Fmt(buildTime))
                    && HasAttributeWithValue(vm, "Cost", cost.ToString())
                    && HasAttribute(vm, "LaunchCost");

                return instanceOk && defOk;
            }));
        }

        // ==================================================================
        // Property 29 (Structures): every detailed Structure attribute is present with the right value.
        // ==================================================================

        /// <summary>
        /// For any selected Structure, the view-model exposes every instance attribute (id, owner,
        /// health, origin, construction progress, operational flag) and every static
        /// <see cref="StructureDef"/> attribute (Era, function, max health, footprint, population cost,
        /// build time, cost, Peace_Arch flag) required by Req 4 / 7.2.
        ///
        /// **Validates: Requirements 7.2**
        /// </summary>
        [Test]
        [Category("Property 29")]
        public void Property29_Structure_ViewModelContainsEveryAttribute()
        {
            var gen =
                from id in Gen.Choose(1, 100000)
                from owner in Gen.Choose(1, 8)
                from eraIdx in Gen.Choose(0, 8)
                from funcIdx in Gen.Choose(0, 4)
                from maxHealth in Gen.Choose(1, 2000)
                from health in Gen.Choose(0, 2000)
                from width in Gen.Choose(1, 6)
                from length in Gen.Choose(1, 6)
                from popCost in Gen.Choose(0, 50)
                from buildTenths in Gen.Choose(0, 900)
                from progressTenths in Gen.Choose(0, 900)
                from operational in Gen.Choose(0, 1)
                from peaceArch in Gen.Choose(0, 1)
                from costStone in Gen.Choose(0, 100)
                from ox in Gen.Choose(0, 32)
                from oy in Gen.Choose(0, 8)
                from oz in Gen.Choose(0, 32)
                select new
                {
                    id, owner, eraIdx, funcIdx, maxHealth, health, width, length, popCost,
                    buildTenths, progressTenths, operational, peaceArch, costStone, ox, oy, oz
                };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                var era = (Era)g.eraIdx;
                var function = (StructureFunction)g.funcIdx;
                float buildTime = g.buildTenths / 10f;
                float progress = g.progressTenths / 10f;
                bool isPeaceArch = g.peaceArch == 1;
                var cost = g.costStone > 0 ? ResourceCost.Single(ResourceType.Stone, g.costStone) : ResourceCost.Free;

                var def = new StructureDef(
                    id: "s_test", era: era, cost: cost, buildTimeSeconds: buildTime,
                    populationCost: g.popCost, maxHealth: g.maxHealth, footprintWidth: g.width,
                    footprintLength: g.length, function: function, isPeaceArch: isPeaceArch);

                var structure = new StructureInstance(g.id, g.owner, def, new CellCoord(g.ox, g.oy, g.oz))
                {
                    Health = g.health,
                    ConstructionProgress = progress,
                    IsOperational = g.operational == 1,
                };

                var vm = InfoPanelViewModel.ForStructure(structure);

                if (vm.Kind != InfoEntityKind.Structure || vm.EntityId != $"Structure#{g.id}")
                {
                    return false;
                }

                bool instanceOk =
                    HasAttributeWithValue(vm, "Id", Fmt(g.id))
                    && HasAttributeWithValue(vm, "OwnerNationId", Fmt(g.owner))
                    && HasAttributeWithValue(vm, "Health", Fmt(g.health))
                    && HasAttributeWithValue(vm, "OriginX", Fmt(g.ox))
                    && HasAttributeWithValue(vm, "OriginY", Fmt(g.oy))
                    && HasAttributeWithValue(vm, "OriginZ", Fmt(g.oz))
                    && HasAttributeWithValue(vm, "ConstructionProgress", Fmt(progress))
                    && HasAttributeWithValue(vm, "IsOperational", Fmt(g.operational == 1));

                bool defOk =
                    HasAttributeWithValue(vm, "DefId", "s_test")
                    && HasAttributeWithValue(vm, "Era", era.ToString())
                    && HasAttributeWithValue(vm, "Function", function.ToString())
                    && HasAttributeWithValue(vm, "MaxHealth", Fmt(g.maxHealth))
                    && HasAttributeWithValue(vm, "FootprintWidth", Fmt(g.width))
                    && HasAttributeWithValue(vm, "FootprintLength", Fmt(g.length))
                    && HasAttributeWithValue(vm, "PopulationCost", Fmt(g.popCost))
                    && HasAttributeWithValue(vm, "BuildTimeSeconds", Fmt(buildTime))
                    && HasAttributeWithValue(vm, "Cost", cost.ToString())
                    && HasAttributeWithValue(vm, "IsPeaceArch", Fmt(isPeaceArch));

                return instanceOk && defOk;
            }));
        }

        // ==================================================================
        // Property 29 (Battalions): every detailed Battalion attribute is present with the right value.
        // ==================================================================

        /// <summary>
        /// For any selected Battalion, the view-model exposes every attribute of the group (id, name,
        /// member count, ordered member list) required by Req 3.3 / 7.2, and — when the Match state is
        /// supplied — additionally surfaces the living-member count and aggregate health of surviving
        /// members (Req 7.4). Members are reported in ascending id order.
        ///
        /// **Validates: Requirements 7.2**
        /// </summary>
        [Test]
        [Category("Property 29")]
        public void Property29_Battalion_ViewModelContainsEveryAttribute()
        {
            var gen =
                from id in Gen.Choose(1, 10000)
                from nameSeed in Gen.Choose(0, 9999)
                // A random-length array of member healths; its length is the member count and each
                // entry (which may be <= 0 for a dead unit) drives the state-backed living/health rollup.
                from healths in Gen.ArrayOf(Gen.Choose(-5, 200))
                select new { id, nameSeed, healths };

            Check(Prop.ForAll(Arb.From(gen), g =>
            {
                string name = $"Bn-{g.nameSeed}";
                int memberCount = g.healths.Length;

                // Member ids 1..memberCount; build a Match state whose units carry the generated health.
                var memberIds = Enumerable.Range(1, memberCount).ToList();
                var def = new UnitDef(
                    "member", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1, 1f, UnitRole.Soldier);

                var state = new MatchState();
                for (int i = 0; i < memberCount; i++)
                {
                    int unitId = memberIds[i];
                    state.Units[unitId] = new UnitInstance(unitId, 1, def, WorldPosition.Zero)
                    {
                        Health = g.healths[i],
                    };
                }

                var battalion = new Battalion(g.id, name, memberIds);

                // ---- variant A: no Match state supplied ----
                var vmNoState = InfoPanelViewModel.ForBattalion(battalion);
                string expectedMembers = memberCount == 0
                    ? "None"
                    : string.Join(", ", memberIds.OrderBy(x => x).Select(x => x.ToString(Inv)));

                bool baseOk =
                    vmNoState.Kind == InfoEntityKind.Battalion
                    && vmNoState.EntityId == $"Battalion#{g.id}"
                    && vmNoState.DisplayName == name
                    && HasAttributeWithValue(vmNoState, "Id", Fmt(g.id))
                    && HasAttributeWithValue(vmNoState, "Name", name)
                    && HasAttributeWithValue(vmNoState, "MemberCount", Fmt(memberCount))
                    && HasAttributeWithValue(vmNoState, "Members", expectedMembers);

                if (!baseOk)
                {
                    return false;
                }

                // ---- variant B: Match state supplied -> living/health rollup present and correct ----
                int expectedLiving = g.healths.Count(h => h > 0);
                int expectedTotalHealth = g.healths.Where(h => h > 0).Sum();

                var vmWithState = InfoPanelViewModel.ForBattalion(battalion, state);

                bool stateOk =
                    HasAttributeWithValue(vmWithState, "Id", Fmt(g.id))
                    && HasAttributeWithValue(vmWithState, "Name", name)
                    && HasAttributeWithValue(vmWithState, "MemberCount", Fmt(memberCount))
                    && HasAttributeWithValue(vmWithState, "Members", expectedMembers)
                    && HasAttributeWithValue(vmWithState, "LivingMemberCount", Fmt(expectedLiving))
                    && HasAttributeWithValue(vmWithState, "TotalHealth", Fmt(expectedTotalHealth));

                return stateOk;
            }));
        }
    }
}
