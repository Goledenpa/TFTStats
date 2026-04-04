namespace TFTStats.Core.Entities.Harvester
{
    public record PlayerHarvestInfo(
        string Puuid,
        DateTime? LastHarvestedAt
    );
}
