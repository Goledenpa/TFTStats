using Microsoft.Extensions.Logging;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Repositories
{
    public class MatchRepository : IMatchRepository
    {
        private readonly ILogger<MatchRepository> _logger;
        private readonly SqlExecutor _sqlExecutor;

        public MatchRepository(SqlExecutor sqlExec, ILogger<MatchRepository> logger)
        {
            _sqlExecutor = sqlExec;
            _logger = logger;
        }

        public async Task<List<string>> FilterNewMatchIdsAsync(List<string> matchIds)
        {
            if (matchIds.Count == 0) return [];

            const string query = "SELECT match_id FROM match WHERE match_id = ANY(@ids::text[])";

            var existingInDb = await _sqlExecutor.QueryAsync(
                query,
                r => r.GetString(0),
                p =>
                {
                    p.Add(_sqlExecutor.CreateParameter("ids", matchIds.ToArray()));
                });


            return [.. matchIds.Except(existingInDb)];
        }

        public async Task<(string Puuid, DateTime? LastCrawledAt)> GetNextPlayerToCrawlAsync()
        {
            const string query = @"
                SELECT p.puuid, last_crawled_at
                FROM player p
                ORDER BY last_crawled_at ASC NULLS FIRST
                LIMIT 1";

            return await _sqlExecutor.QueryFirstOrDefaultAsync(
                query,
                reader => (
                    reader.GetString(0),
                    reader.IsDBNull(1) ? (DateTime?)null : reader.GetDateTime(1)
                )
            );
        }

        public async Task MarkPlayerAsCrawledAsync(string puuid)
        {
            const string query = "UPDATE player SET last_crawled_at = CURRENT_TIMESTAMP WHERE puuid = @puuid";

            await _sqlExecutor.ExecuteAsync(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("puuid", puuid));
            });
        }

        public async Task<int> GetTotalPlayerCountAsync()
        {
            const string query = "SELECT COUNT(*) FROM player";

            // We use object and Convert because some DBs return long for COUNT(*)
            var result = await _sqlExecutor.QueryScalarAsync<object>(query);
            return Convert.ToInt32(result);
        }
    }
}
