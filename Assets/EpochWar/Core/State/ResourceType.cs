namespace EpochWar.Core.State
{
    /// <summary>
    /// The economic resource types tracked per Nation (Req 2.1).
    ///
    /// Each Nation maintains an independent stored quantity for every value here.
    /// <see cref="Research"/> is the resource consumed by the Tech_System, and
    /// <see cref="ExoticMatter"/> is a late/Space-era resource feeding Ascension and
    /// other end-game capabilities. Values are stable ordinals so they can be used as
    /// dictionary keys and serialized across the match.
    /// </summary>
    public enum ResourceType
    {
        Food = 0,
        Wood = 1,
        Stone = 2,
        Metal = 3,
        Energy = 4,
        Research = 5,
        ExoticMatter = 6
    }
}
