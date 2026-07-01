namespace EpochWar.Core.State
{
    /// <summary>
    /// The ordered set of technological eras a Nation advances through.
    ///
    /// The integer values are deliberately sequential starting at zero so the natural
    /// enum order *is* the progression order (Req 1.1). Comparisons such as
    /// <c>nation.CurrentEra &gt;= Era.Space</c> are therefore meaningful and are used to
    /// gate era-locked content (e.g. Colony Ship requires the Space era).
    ///
    /// Do not reorder or renumber these members: persisted match state and content
    /// definitions rely on the stable ordinal values.
    /// </summary>
    public enum Era
    {
        Prehistoric = 0,
        Ancient = 1,
        Classical = 2,
        Medieval = 3,
        Industrial = 4,
        Modern = 5,
        Information = 6,
        Futuristic = 7,
        Space = 8
    }
}
