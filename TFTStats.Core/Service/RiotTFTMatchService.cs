using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Models;

namespace TFTStats.Core.Service
{
    public class RiotTFTMatchService
    {
        private readonly ILogger<RiotTFTMatchService> _logger;
        private readonly RiotApiClient _apiClient;

        public RiotTFTMatchService(RiotApiClient riotApiClient, ILogger<RiotTFTMatchService> logger)
        {
            _apiClient = riotApiClient;
            _logger = logger;
        }

        public async Task<List<string>> GetMatches(string region, string puuid, int start = 0, int count = 20)
        {
            var url = _apiClient.BuildUrl(region, $"tft/match/v1/matches/by-puuid/{puuid}/ids?start={start}&count={count}");
            var res = await _apiClient.Client.GetAsync(url);

            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<List<string>>() ?? [];
            }

            return [];
        }

        public async Task<RiotTFTMatch?> GetMatch(string region, string matchId, CancellationToken ct = default)
        {

            var url = _apiClient.BuildUrl(region, $"tft/match/v1/matches/{matchId}");

            var res = await _apiClient.Client.GetAsync(url, ct);

            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Match {matchId} returned 404 (Not Found). Skipping.", matchId);
                return null;
            }

            res.EnsureSuccessStatusCode();

            return await res.Content.ReadFromJsonAsync<RiotTFTMatch>(ct);
        }

        public async IAsyncEnumerable<RiotTFTMatch> GetMatchStreamAsync(string region, List<string> matchIds, [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach(var matchId in matchIds)
            {
                if (ct.IsCancellationRequested) break;

                RiotTFTMatch? match = null;

                try
                {
                    match = await GetMatch(region, matchId, ct);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Skipping match {matchId}: {exMessage}", matchId, ex.Message);
                }

                if(match is not null)
                {
                    yield return match;
                }
            }
        }
        public async Task<List<string>> GetSetMatchIdsAsync(string cluster, string puuid, long setStartTime, CancellationToken ct = default)
        {
            var allIds = new List<string>();
            int startOffset = 0;
            int pageSize = 100;

            while (true)
            {
                var url = _apiClient.BuildUrl(cluster,
                    $"tft/match/v1/matches/by-puuid/{puuid}/ids?startTime={setStartTime}&start={startOffset}&count={pageSize}");

                var ids = await _apiClient.Client.GetFromJsonAsync<List<string>>(url, ct);

                if (ids == null || ids.Count == 0) break;

                allIds.AddRange(ids);

                if (ids.Count < pageSize) break; // We reached the end

                startOffset += pageSize;
            }
            return allIds.Distinct().ToList();
        }
    }
}
