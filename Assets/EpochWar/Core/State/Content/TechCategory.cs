namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// Classifies a <see cref="TechnologyDef"/> by the victory-path capability it relates to.
    ///
    /// The category lets the Tech_System (task 5) and Victory_System (task 12) apply the
    /// extra era/availability gating the special victory paths require without hard-coding
    /// technology ids:
    /// <list type="bullet">
    ///   <item><see cref="Normal"/> — an ordinary technology that simply unlocks content.</item>
    ///   <item><see cref="DoomsdayWeapon"/> — an Annihilation-path tech gated by Era (Req 9.1).</item>
    ///   <item><see cref="PeaceArchPrereq"/> — a prerequisite that feeds Peace_Arch availability (Req 10.1).</item>
    ///   <item><see cref="ColonyShip"/> — the Ascension-path tech gated by the Space Era (Req 11.1).</item>
    /// </list>
    /// </summary>
    public enum TechCategory
    {
        Normal = 0,
        DoomsdayWeapon = 1,
        PeaceArchPrereq = 2,
        ColonyShip = 3
    }
}
