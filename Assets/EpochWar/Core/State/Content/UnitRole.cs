namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// The functional role of a <see cref="UnitDef"/>, used by movement, combat, and the
    /// special victory paths.
    ///
    /// Notably, <see cref="Aircraft"/> and <see cref="ColonyShip"/> ignore the ground
    /// navigation grid when pathfinding (design: "flying units ignore ground nav"), and
    /// <see cref="ColonyShip"/> additionally drives the Ascension colonization sequence
    /// (Req 11.2).
    /// </summary>
    public enum UnitRole
    {
        Worker = 0,
        Soldier = 1,
        Vehicle = 2,
        Aircraft = 3,
        ColonyShip = 4
    }
}
