namespace TFTStats.Core.Repositories.Interfaces
{
    public interface IMatchRepository
    {
        Task<(string Puuid, DateTime? LastCrawledAt)> GetNextPlayerToCrawlAsync();
        Task<List<string>> FilterNewMatchIdsAsync(List<string> matchIds);
        Task MarkPlayerAsCrawledAsync(string puuid);
        Task<int> GetTotalPlayerCountAsync();
        
        // New batch methods for two-phase crawler
        Task<List<string>> GetNextPendingMatchIdsAsync(int count);
        Task MarkMatchesAsCrawledAsync(List<string> matchIds);
    }
}
