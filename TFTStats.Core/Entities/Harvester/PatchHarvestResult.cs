namespace TFTStats.Core.Entities.Harvester
{
    public record PatchHarvestResult(
        string PatchName,
        int SetNumber,
        int MatchIdsFound,
        int NewMatchIdsAdded);
}
