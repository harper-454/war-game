namespace EpochWar.Core.State
{
    /// <summary>
    /// The three parallel victory paths available in every Match (Req 9, 10, 11).
    ///
    /// The satisfied path is recorded on the <see cref="MatchOutcome"/> when the Match ends so
    /// the end-of-match summary can report how victory was achieved (Req 12.4).
    /// </summary>
    public enum VictoryPath
    {
        /// <summary>Win by eliminating all opposing Nations (Req 9).</summary>
        Annihilation = 0,

        /// <summary>Win by completing the Peace_Arch wonder (Req 10).</summary>
        Peace = 1,

        /// <summary>Win by being first to complete planetary colonization (Req 11).</summary>
        Ascension = 2
    }
}
