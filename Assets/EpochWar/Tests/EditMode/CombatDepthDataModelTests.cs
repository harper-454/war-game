using System.Collections.Generic;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Example-based schema/default tests for the Combat Depth data-model additions (task 11).
    ///
    /// These assert that a freshly constructed <see cref="UnitInstance"/> starts with the correct
    /// Veterancy defaults and an empty (all-ready) ability-cooldown map (Req 12.1, 12.3, 13.1),
    /// and that <see cref="UnitDef"/> and <see cref="StructureDef"/> expose every new field added
    /// for flanking/vision/veterancy/abilities/artillery with sensible defaults (Req 9.4, 12.x,
    /// 13.1, 14.1, 15.x) so existing construction sites keep compiling.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class CombatDepthDataModelTests
    {
        [Test]
        public void UnitInstance_FreshlyConstructed_HasZeroVeterancyAndEmptyCooldowns()
        {
            var def = new UnitDef("soldier", Era.Prehistoric, ResourceCost.Free, 0f, 0, 20, 5, 2, 1f, UnitRole.Soldier);

            var unit = new UnitInstance(1, 1, def, WorldPosition.Zero);

            Assert.That(unit.VeterancyTierIndex, Is.EqualTo(0), "a new Unit starts at the base tier (Req 12.1)");
            Assert.That(unit.VeterancyExperience, Is.EqualTo(0), "a new Unit starts with no experience (Req 12.1)");
            Assert.That(unit.AbilityRemainingCooldown, Is.Not.Null, "the cooldown map is always initialized (Req 13.1)");
            Assert.That(unit.AbilityRemainingCooldown, Is.Empty, "a new Unit has every ability ready (Req 13.2, 13.4)");
            Assert.That(unit.Facing, Is.EqualTo(FacingDirection.Zero), "a new Unit faces the default direction (Req 9.4)");
        }

        [Test]
        public void UnitInstance_VeterancyAndCooldownState_AreMutable()
        {
            var def = new UnitDef("soldier", Era.Prehistoric, ResourceCost.Free, 0f, 0, 20, 5, 2, 1f, UnitRole.Soldier);
            var unit = new UnitInstance(2, 1, def, WorldPosition.Zero);

            unit.VeterancyExperience += 50;
            unit.VeterancyTierIndex = 1;
            unit.AbilityRemainingCooldown["ability_barrage"] = Fixed.FromInt(8);
            unit.Facing = FacingDirection.FromDegrees(90);

            Assert.That(unit.VeterancyExperience, Is.EqualTo(50));
            Assert.That(unit.VeterancyTierIndex, Is.EqualTo(1));
            Assert.That(unit.AbilityRemainingCooldown["ability_barrage"], Is.EqualTo(Fixed.FromInt(8)));
            Assert.That(unit.Facing.AngleDegrees, Is.EqualTo(Fixed.FromInt(90)));
        }

        [Test]
        public void UnitDef_ExposesAllCombatDepthFields_WithDefaults()
        {
            // Constructed via the pre-existing positional signature: new fields must default.
            var def = new UnitDef("basic", Era.Prehistoric, ResourceCost.Free, 0f, 0, 20, 5, 2, 1f, UnitRole.Soldier);

            Assert.That(def.SightRadius, Is.EqualTo(Fixed.Zero), "SightRadius default (Req 14.1)");
            Assert.That(def.AbilityDefs, Is.Not.Null.And.Empty, "AbilityDefs default (Req 13.1)");
            Assert.That(def.VeterancyCurve, Is.Not.Null.And.Empty, "VeterancyCurve default (Req 12.2)");
            Assert.That(def.IsArtillery, Is.False, "IsArtillery default (Req 15.1)");
            Assert.That(def.IndirectFireRange, Is.EqualTo(Fixed.Zero), "IndirectFireRange default (Req 15.2)");
            Assert.That(def.DirectFireRange, Is.EqualTo(Fixed.Zero), "DirectFireRange default (Req 15.1)");
            Assert.That(def.IndirectFireFlightDelay, Is.EqualTo(Fixed.Zero), "IndirectFireFlightDelay default (Req 15.5)");
            Assert.That(def.AreaEffectRadius, Is.EqualTo(Fixed.Zero), "AreaEffectRadius default (Req 11)");
            Assert.That(def.VisualDetailTier, Is.EqualTo(0), "VisualDetailTier default (Req 7)");
        }

        [Test]
        public void UnitDef_ExposesAllCombatDepthFields_WhenAuthored()
        {
            var abilities = new List<UnitAbilityDef>
            {
                new UnitAbilityDef("heal", Fixed.FromInt(10), ResourceCost.Free, AbilityEffectKind.Heal)
            };
            var curve = new List<VeterancyTierDef>
            {
                new VeterancyTierDef("Recruit", 0, 0, 0),
                new VeterancyTierDef("Veteran", 100, 2, 1)
            };

            var def = new UnitDef(
                "howitzer", Era.Modern, ResourceCost.Free, 0f, 0, 40, 20, 5, 1f, UnitRole.Vehicle,
                launchCost: default,
                sightRadius: Fixed.FromInt(6),
                abilityDefs: abilities,
                veterancyCurve: curve,
                isArtillery: true,
                indirectFireRange: Fixed.FromInt(30),
                directFireRange: Fixed.FromInt(4),
                indirectFireFlightDelay: Fixed.FromInt(3),
                areaEffectRadius: Fixed.FromInt(2),
                visualDetailTier: 3);

            Assert.That(def.SightRadius, Is.EqualTo(Fixed.FromInt(6)));
            Assert.That(def.AbilityDefs, Is.SameAs(abilities));
            Assert.That(def.VeterancyCurve, Is.SameAs(curve));
            Assert.That(def.IsArtillery, Is.True);
            Assert.That(def.IndirectFireRange, Is.EqualTo(Fixed.FromInt(30)));
            Assert.That(def.DirectFireRange, Is.EqualTo(Fixed.FromInt(4)));
            Assert.That(def.IndirectFireFlightDelay, Is.EqualTo(Fixed.FromInt(3)));
            Assert.That(def.AreaEffectRadius, Is.EqualTo(Fixed.FromInt(2)));
            Assert.That(def.VisualDetailTier, Is.EqualTo(3));
        }

        [Test]
        public void StructureDef_ExposesSightRadiusAndVisualDetailTier_WithDefaults()
        {
            var def = new StructureDef(
                "wall", Era.Prehistoric, ResourceCost.Free, 0f, 0, 100, 1, 1, StructureFunction.Defense);

            Assert.That(def.SightRadius, Is.EqualTo(Fixed.Zero), "SightRadius default (Req 14.1)");
            Assert.That(def.VisualDetailTier, Is.EqualTo(0), "VisualDetailTier default (Req 7)");
        }

        [Test]
        public void StructureDef_ExposesSightRadiusAndVisualDetailTier_WhenAuthored()
        {
            var def = new StructureDef(
                "watchtower", Era.Medieval, ResourceCost.Free, 0f, 0, 100, 1, 1, StructureFunction.Defense,
                isPeaceArch: false,
                sightRadius: Fixed.FromInt(9),
                visualDetailTier: 2);

            Assert.That(def.SightRadius, Is.EqualTo(Fixed.FromInt(9)));
            Assert.That(def.VisualDetailTier, Is.EqualTo(2));
        }

        [Test]
        public void VeterancyTierDef_And_UnitAbilityDef_CarryTheirFields()
        {
            var tier = new VeterancyTierDef("Elite", 250, 4, 3);
            Assert.That(tier.Id, Is.EqualTo("Elite"));
            Assert.That(tier.ExperienceThreshold, Is.EqualTo(250));
            Assert.That(tier.AttackBonus, Is.EqualTo(4));
            Assert.That(tier.DefenseBonus, Is.EqualTo(3));

            var ability = new UnitAbilityDef("bombard", Fixed.FromInt(12), ResourceCost.Free, AbilityEffectKind.Bombard);
            Assert.That(ability.Id, Is.EqualTo("bombard"));
            Assert.That(ability.CooldownSeconds, Is.EqualTo(Fixed.FromInt(12)));
            Assert.That(ability.Cost, Is.EqualTo(ResourceCost.Free));
            Assert.That(ability.EffectKind, Is.EqualTo(AbilityEffectKind.Bombard));
        }

        [Test]
        public void Flank_And_Facing_ClassificationTypesAreDefined()
        {
            Assert.That(new[] { Flank.Front, Flank.Side, Flank.Rear }, Has.Length.EqualTo(3));
            var facing = FacingDirection.FromDegrees(270);
            Assert.That(facing.AngleDegrees, Is.EqualTo(Fixed.FromInt(270)));
        }
    }
}
