using TFTStats.Core.Entities.Harvester;

namespace TFTStats.Core.Repositories.Interfaces
{
    public interface IHarvesterRepository
    {
        Task<PlayerHarvestInfo> GetNextPlayerToHarvestAsync();
        Task<int> UpsertMatchIdsAsync(string puuid, List<MatchHarvestInfo> matchIds);
        Task MarkPlayerAsHarvestedAsync(string puuid);
        Task<int> GetPendingMatchCountAsync();
        Task<int> GetPendingMatchCountCachedAsync();
        Task<int> GetRemainingPlayerCountAsync();
        Task IncrementPendingCounterAsync(int count);
        Task SyncPendingCounterAsync();
    }
}
