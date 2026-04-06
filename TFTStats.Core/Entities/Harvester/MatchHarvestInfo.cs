namespace TFTStats.Core.Entities.Harvester
{
    public record MatchHarvestInfo(
        string MatchId,
        long GameCreation,
        DateTime? GameDateTime,
        int? SetNumber,
        int? QueueId,
        int? PatchId);
} 
