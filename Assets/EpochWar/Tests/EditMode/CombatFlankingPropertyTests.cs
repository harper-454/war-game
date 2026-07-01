using System.Linq;
using EpochWar.Core.Math;
using EpochWar.Core.State;
using EpochWar.Core.State.Content;
using EpochWar.Core.Systems;
using FsCheck;
using NUnit.Framework;

namespace EpochWar.Tests.EditMode
{
    /// <summary>
    /// Property-based tests for the flanking bonus added to <see cref="CombatSystem.ResolveAttack"/>
    /// (tasks 14.1/14.2), exercised for at least the design-mandated 100 generated iterations.
    ///
    /// Covered properties, tagged <c>Feature: epoch-war-combat-visuals-expansion, Property 1/2</c>:
    /// <list type="bullet">
    ///   <item>Property 1 — Side-Flank grants a bonus at least matched by Rear-Flank (Req 9.1, 9.2).</item>
    ///   <item>Property 2 — Front-Flank applies no bonus (Req 9.3).</item>
    /// </list>
    ///
    /// The scenarios isolate flanking from cover: units sit on flat <see cref="CellMaterial.Soil"/>
    /// terrain at elevation zero with no Structures, so the cover bonus is always zero and the only
    /// modifier is the flanking bonus added to the attack's damage input. With no governance adopted
    /// the effective attack/defense equal the base values, so the resolved damage is exactly
    /// <c>ComputeDamage(attack + flankingBonus, defense)</c>.
    /// </summary>
    [TestFixture]
    [Category("Feature: epoch-war-combat-visuals-expansion")]
    public sealed class CombatFlankingPropertyTests
    {
        private const int MinimumIterations = 100;

        private static readonly Fixed FrontArc = Fixed.FromInt(CombatSystem.FrontArcDegrees);
        private static readonly Fixed SideArc = Fixed.FromInt(CombatSystem.SideArcDegrees);

        private static Configuration Config()
        {
            var config = Configuration.QuickThrowOnFailure;
            config.MaxNbOfTest = MinimumIterations;
            return config;
        }

        private static UnitDef UnitDefOf(int attack, int defense, int maxHealth)
            => new UnitDef("u", Era.Prehistoric, ResourceCost.Free, 0f, 0, maxHealth, attack, defense, 1f, UnitRole.Soldier);

        private static (MatchState state, CombatSystem combat) Build()
        {
            var res = new ResourceSystem();
            var civ = new CivSystem(res);
            var units = new UnitSystem(new InMemoryCatalog(), res, civ);
            var terrain = new TerrainSystem();
            var combat = new CombatSystem(civ, units, terrain);
            var state = new MatchState(new TerrainVolume(new Int3(8, 8, 8), CellMaterial.Soil));
            state.Nations[1] = new Nation(1);
            state.Nations[2] = new Nation(2);
            return (state, combat);
        }

        private static int ResolveDamage(
            int attack, int defense, int facingDegrees,
            int defX, int defZ, int atkX, int atkZ)
        {
            var (state, combat) = Build();
            var attacker = new UnitInstance(1, 1, UnitDefOf(attack, 0, 1000), WorldPosition.FromInts(atkX, 0, atkZ));
            var defender = new UnitInstance(2, 2, UnitDefOf(0, defense, 1_000_000), WorldPosition.FromInts(defX, 0, defZ))
            {
                Facing = FacingDirection.FromDegrees(facingDegrees),
            };
            state.Units[1] = attacker;
            state.Units[2] = defender;

            var events = combat.ResolveAttack(state, 1, 2);
            return events.OfType<CombatResolvedEvent>().Single().Damage;
        }

        /// <summary>
        /// A generated flanking geometry with combat stats chosen so the base damage is never clamped
        /// at the <c>max(1, ...)</c> floor (attack ≥ 20, defense ≤ 30 ⇒ attack − defense/2 ≥ 5),
        /// which lets Property 1 assert a <em>strict</em> increase on a side flank.
        /// </summary>
        public sealed class FlankScenario
        {
            public int Attack;
            public int Defense;
            public int FacingDegrees;
            public int DefenderX;
            public int DefenderZ;
            public int AttackerX;
            public int AttackerZ;

            public override string ToString()
                => $"FlankScenario(atk={Attack}, def={Defense}, face={FacingDegrees}, " +
                   $"def=({DefenderX},{DefenderZ}), atk=({AttackerX},{AttackerZ}))";
        }

        private static Arbitrary<FlankScenario> FlankScenarios()
        {
            var gen = from attack in Gen.Choose(20, 400)
                      from defense in Gen.Choose(0, 30)
                      from facing in Gen.Choose(0, 359)
                      from dx in Gen.Choose(-40, 40)
                      from dz in Gen.Choose(-40, 40)
                      from ax in Gen.Choose(-40, 40)
                      from az in Gen.Choose(-40, 40)
                      select new FlankScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          FacingDegrees = facing,
                          DefenderX = dx,
                          DefenderZ = dz,
                          AttackerX = ax,
                          AttackerZ = az,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 1: Side-Flank grants a bonus at least matched by Rear-Flank.
        ///
        /// When the attacker's position classifies as the defender's side flank, the resolved damage
        /// is strictly greater than the same attack with no flanking bonus; and the rear-flank damage
        /// bonus is never less than the side-flank bonus (guaranteed by the configured
        /// <c>RearFlankingBonus &gt;= SideFlankingBonus</c> and the monotonicity of the damage formula
        /// in attack).
        ///
        /// **Validates: Requirements 9.1, 9.2**
        /// </summary>
        [Test]
        [Category("Property 1")]
        public void Property1_SideFlankBonus_AtMostMatchedByRear()
        {
            Prop.ForAll(FlankScenarios(), s =>
            {
                var facing = FacingDirection.FromDegrees(s.FacingDegrees);
                var defPos = WorldPosition.FromInts(s.DefenderX, 0, s.DefenderZ);
                var atkPos = WorldPosition.FromInts(s.AttackerX, 0, s.AttackerZ);

                Flank flank = FlankClassifier.Classify(facing, defPos, atkPos, FrontArc, SideArc);

                int baseDamage = CombatSystem.ComputeDamage(s.Attack, s.Defense);
                int sideDamage = CombatSystem.ComputeDamage(s.Attack + CombatSystem.SideFlankingBonus, s.Defense);
                int rearDamage = CombatSystem.ComputeDamage(s.Attack + CombatSystem.RearFlankingBonus, s.Defense);

                // Config + formula invariant: rear bonus is never smaller than side bonus, in raw
                // constant terms and in resulting damage terms.
                int sideBonus = CombatSystem.SideFlankingBonus;
                int rearBonus = CombatSystem.RearFlankingBonus;
                if (rearBonus < sideBonus)
                {
                    return false;
                }

                if (rearDamage < sideDamage)
                {
                    return false;
                }

                int resolved = ResolveDamage(
                    s.Attack, s.Defense, s.FacingDegrees, s.DefenderX, s.DefenderZ, s.AttackerX, s.AttackerZ);

                switch (flank)
                {
                    case Flank.Side:
                        // The side flank adds exactly the side bonus, strictly increasing damage.
                        return resolved == sideDamage && resolved > baseDamage;
                    case Flank.Rear:
                        return resolved == rearDamage && resolved >= sideDamage;
                    default: // Front — covered by Property 2.
                        return resolved == baseDamage;
                }
            }).Check(Config());
        }

        /// <summary>
        /// A generated front-flank geometry: the attacker is placed along the defender's facing with a
        /// small lateral jitter kept well inside the front half-arc, so the classification is
        /// guaranteed to be <see cref="Flank.Front"/> regardless of fixed-point rounding.
        /// </summary>
        public sealed class FrontScenario
        {
            public int Attack;
            public int Defense;
            public int FacingIndex; // 0..3 -> 0/90/180/270 degrees
            public int Distance;    // main offset along the facing
            public int Jitter;      // lateral offset (|jitter| <= distance/3)
            public int JitterSign;

            public override string ToString()
                => $"FrontScenario(atk={Attack}, def={Defense}, faceIdx={FacingIndex}, d={Distance}, j={Jitter * JitterSign})";
        }

        private static Arbitrary<FrontScenario> FrontScenarios()
        {
            var gen = from attack in Gen.Choose(20, 400)
                      from defense in Gen.Choose(0, 30)
                      from faceIdx in Gen.Choose(0, 3)
                      from distance in Gen.Choose(6, 40)
                      from jitterFraction in Gen.Choose(0, 100)
                      from signChoice in Gen.Choose(0, 1)
                      select new FrontScenario
                      {
                          Attack = attack,
                          Defense = defense,
                          FacingIndex = faceIdx,
                          Distance = distance,
                          // Keep |jitter| <= distance/3 so the bearing stays within ~18.4 degrees of
                          // the facing — comfortably inside the 45-degree front half-arc.
                          Jitter = (jitterFraction * (distance / 3)) / 100,
                          JitterSign = signChoice == 0 ? -1 : 1,
                      };

            return Arb.From(gen);
        }

        /// <summary>
        /// Property 2: Front-Flank applies no bonus.
        ///
        /// For geometry that classifies as the defender's front flank, the resolved damage equals the
        /// damage computed with no flanking bonus applied.
        ///
        /// **Validates: Requirements 9.3**
        /// </summary>
        [Test]
        [Category("Property 2")]
        public void Property2_FrontFlankAppliesNoBonus()
        {
            Prop.ForAll(FrontScenarios(), s =>
            {
                // Defender at the origin facing one of the four compass directions; attacker placed
                // ahead of the facing (main offset) with a small lateral jitter.
                int facingDegrees;
                int atkX;
                int atkZ;
                int j = s.Jitter * s.JitterSign;
                switch (s.FacingIndex)
                {
                    case 0: facingDegrees = 0; atkX = s.Distance; atkZ = j; break;   // +X
                    case 1: facingDegrees = 90; atkX = j; atkZ = s.Distance; break;  // +Z
                    case 2: facingDegrees = 180; atkX = -s.Distance; atkZ = j; break; // -X
                    default: facingDegrees = 270; atkX = j; atkZ = -s.Distance; break; // -Z
                }

                var facing = FacingDirection.FromDegrees(facingDegrees);
                var defPos = WorldPosition.Zero;
                var atkPos = WorldPosition.FromInts(atkX, 0, atkZ);

                // Guard: confirm the constructed geometry really is a front flank (it always should be
                // by construction); if a boundary case slipped through, the scenario is not applicable.
                Flank flank = FlankClassifier.Classify(facing, defPos, atkPos, FrontArc, SideArc);
                if (flank != Flank.Front)
                {
                    return true;
                }

                int baseDamage = CombatSystem.ComputeDamage(s.Attack, s.Defense);
                int resolved = ResolveDamage(s.Attack, s.Defense, facingDegrees, 0, 0, atkX, atkZ);

                return resolved == baseDamage;
            }).Check(Config());
        }
    }
}
