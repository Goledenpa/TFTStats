using TFTStats.Core.Entities.Harvester;

namespace TFTStats.Core.Repositories.Interfaces
{
    public interface IHarvesterRepository
    {
        Task<PlayerHarvestInfo> GetNextPlayerToHarvestAsync();
        Task UpsertMatchIdsAsync(string puuid, List<MatchHarvestInfo> matchIds);
        Task MarkPlayerAsHarvestedAsync(string puuid);
        Task<int> GetPendingMatchCountAsync();
    }
}
