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
                WHERE last_harvested_at IS NULL
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

        public async Task<int> GetPendingMatchCountCachedAsync()
        {
            const string query = "SELECT value FROM app_settings WHERE key = 'staging_pending_count'";
            var res = await _sqlExecutor.QueryScalarAsync<string>(query);
            return res is not null ? int.Parse(res) : 0;
        }

        public async Task<int> GetRemainingPlayerCountAsync()
        {
            const string query = "SELECT COUNT(*) FROM player WHERE last_harvested_at IS NULL";
            var res = await _sqlExecutor.QueryScalarAsync<object>(query);
            return res == null ? 0 : Convert.ToInt32(res);
        }

        public async Task IncrementPendingCounterAsync(int count)
        {
            const string query = "UPDATE app_settings SET value = (value::bigint + @count)::text WHERE key = 'staging_pending_count'";
            await _sqlExecutor.ExecuteAsync(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("count", count));
            });
        }

        public async Task SyncPendingCounterAsync()
        {
            const string countQuery = "SELECT COUNT(*) FROM staging_match_ids WHERE crawled_at IS NULL";
            const string getQuery = "SELECT value FROM app_settings WHERE key = 'staging_pending_count'";
            const string updateQuery = "UPDATE app_settings SET value = @count WHERE key = 'staging_pending_count'";

            var oldValue = await _sqlExecutor.QueryScalarAsync<string>(getQuery);
            var actualCount = await _sqlExecutor.QueryScalarAsync<long>(countQuery);

            await _sqlExecutor.ExecuteAsync(updateQuery, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("count", actualCount.ToString()));
            });

            if (oldValue != null && long.TryParse(oldValue, out var oldCount))
            {
                var drift = actualCount - oldCount;
                if (drift > 0)
                {
                    _logger.LogWarning("[HarvesterRepository] Pending counter drifted: was {oldCount}, actual {actualCount} (+{drift})", oldCount, actualCount, drift);
                }
                else if (drift < 0)
                {
                    _logger.LogWarning("[HarvesterRepository] Pending counter was OVER-counted: was {oldCount}, actual {actualCount} ({drift})", oldCount, actualCount, drift);
                }
                else
                {
                    _logger.LogInformation("[HarvesterRepository] Pending counter synced: {actualCount} (no drift)", actualCount);
                }
            }
            else
            {
                _logger.LogInformation("[HarvesterRepository] Pending counter initialized: {actualCount}", actualCount);
            }
        }

        public async Task MarkPlayerAsHarvestedAsync(string puuid)
        {
            const string query = "UPDATE player SET last_harvested_at = CURRENT_TIMESTAMP WHERE puuid = @puuid";

            await _sqlExecutor.ExecuteAsync(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("puuid", puuid));
            });
        }

        public async Task<int> UpsertMatchIdsAsync(string puuid, List<MatchHarvestInfo> matchIds)
        {
            if (matchIds.Count == 0) return 0;

            const string query = @"
                WITH new_matches AS (
                    INSERT INTO staging_match_ids(match_id, puuid, game_creation, game_datetime, set_number, queue_id, patch_id ,created_at)
                    SELECT
                        unnest(@matchIds::text[]),
                        @puuid,
                        unnest(@gameCreations::bigint[]),
                        unnest(@gameDatetimes::timestamptz[]),
                        unnest(@setNumbers::int[]),
                        unnest(@queueIds::int[]),
                        unnest(@patchIds::int[]),
                        @created_at
                    ON CONFLICT (match_id, puuid) DO NOTHING
                    RETURNING match_id
                )
                SELECT COUNT(*) FROM new_matches";

            var res = await _sqlExecutor.QueryScalarAsync<long>(query, p =>
            {
                p.Add(_sqlExecutor.CreateParameter("matchIds", matchIds.Select(x => x.MatchId).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("puuid", puuid));
                p.Add(_sqlExecutor.CreateParameter("gameCreations", matchIds.Select(x => x.GameCreation).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("gameDatetimes", matchIds.Select(x => x.GameDateTime).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("setNumbers", matchIds.Select(x => x.SetNumber).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("queueIds", matchIds.Select(x => x.QueueId).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("patchIds", matchIds.Select(x => x.PatchId).ToArray()));
                p.Add(_sqlExecutor.CreateParameter("created_at", DateTime.UtcNow));
            });

            return (int)res;
        }
    }
}
