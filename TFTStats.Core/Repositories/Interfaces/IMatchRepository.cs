namespace TFTStats.Core.Repositories.Interfaces
{
    public interface IMatchRepository
    {
        Task<(string Puuid, DateTime? LastCrawledAt)> GetNextPlayerToCrawlAsync();
        Task<List<string>> FilterNewMatchIdsAsync(List<string> matchIds);
        Task MarkPlayerAsCrawledAsync(string puuid);
        Task<int> GetTotalPlayerCountAsync();
    }
}
