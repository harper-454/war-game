namespace EpochWar.Core.State.Content
{
    /// <summary>
    /// The primary function a <see cref="StructureDef"/> performs once it is operational
    /// (Req 4.3). Construction disables the function until completion (Req 4.4).
    ///
    /// <see cref="Wonder"/> covers victory-objective structures; the Peace_Arch is a
    /// <see cref="Wonder"/> additionally flagged via <see cref="StructureDef.IsPeaceArch"/>
    /// (Req 10).
    /// </summary>
    public enum StructureFunction
    {
        ResourceExtractor = 0,
        Barracks = 1,
        ResearchLab = 2,
        Defense = 3,
        Wonder = 4
    }
}
