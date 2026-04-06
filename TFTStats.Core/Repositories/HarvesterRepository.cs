using Microsoft.Extensions.Logging;
using TFTStats.Core.Entities.Harvester;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Repositories
{
    public class HarvesterRepository : IHarvesterRepository
    {
        private readonly ILogger<HarvesterRepository> _logger;
        private readonly ISqlExecutor _sqlExecutor;

        public HarvesterRepository(ILogger<HarvesterRepository> logger, ISqlExecutor sqlExecutor)
        {
            _logger = logger;
            _sqlExecutor = sqlExecutor;
        }

        public async Task<PlayerHarvestInfo> GetNextPlayerToHarvestAsync()
        {
            const string query = @"
                SELECT puuid, last_harvested_at
                FROM player
                ORDER BY last_harvested_at ASC NULLS FIRST
                LIMIT 1";

            var result = await _sqlExecutor.QueryFirstOrDefaultAsync(
                query,
                r => new PlayerHarvestInfo(
                    r.GetString(0),
                    r.IsDBNull(1) ? null : r.GetDateTime(1)
                )
            );

            return result!;
        }

        public async Task<int> GetPendingMatchCountAsync()
        {
            const string query = "SELECT COUNT(*) FROM staging_match_ids WHERE crawled_at IS NULL";

            var res = await _sqlExecutor.QueryScalarAsync<object>(query);
            return res is null ? 0 : Convert.ToInt32(res);
        }

        public async Task<int> GetRemainingPlayerCountAsync()
        {
            const string query = "SELECT COUNT(*) FROM player WHERE last_harvested_at IS NULL";
            var res = await _sqlExecutor.QueryScalarAsync<object>(query);
            return res == null ? 0 : Convert.ToInt32(res);
        }

        public async Task MarkPlayerAsHarvestedAsync(string puuid)
        {
            const string query = "UPDATE player SET last_harvested_at = CURRENT_TIMESTAMP WHERE puuid = @puuid";

            await _sqlExecutor.ExecuteAsync(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("puuid", puuid));
            });
        }

        public async Task UpsertMatchIdsAsync(string puuid, List<MatchHarvestInfo> matchIds)
        {
            if (matchIds.Count == 0) return;

            const string query = @"
                INSERT INTO staging_match_ids(match_id, puuid, game_creation, game_datetime, set_number, queue_id, patch_id)
                SELECT
                    unnest(@matchIds::text[]),
                    @puuid,
                    unnest(@gameCreations::bigint[]),
                    unnest(@gameDatetimes::timestamptz[]),
                    unnest(@setNumbers::int[]),
                    unnest(@queueIds::int[]),
                    unnest(@patchIds::int[])
                ON CONFLICT (match_id, puuid) DO NOTHING";

            await _sqlExecutor.ExecuteAsync(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("matchIds", matchIds.Select(x => x.MatchId).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("puuid", puuid));
                p.Add(_sqlExecutor.CreateParameter("gameCreations", matchIds.Select(x => x.GameCreation).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("gameDatetimes", matchIds.Select(x => x.GameDateTime).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("setNumbers", matchIds.Select(x => x.SetNumber).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("queueIds", matchIds.Select(x => x.QueueId).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("patchIds", matchIds.Select(x => x.PatchId).ToArray()));
            });
        }
    }
}
