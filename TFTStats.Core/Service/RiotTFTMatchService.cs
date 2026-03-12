using System.Net.Http.Json;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Models;

namespace TFTStats.Core.Service
{
    public class RiotTFTMatchService
    {
        public RiotApiClient _apiClient;

        public RiotTFTMatchService(RiotApiClient riotApiClient)
        {
            _apiClient = riotApiClient;
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

        public async Task<RiotTFTMatch?> GetMatch(string region, string matchId)
        {

            var url = _apiClient.BuildUrl(region, $"tft/match/v1/matches/{matchId}");
            var res = await _apiClient.Client.GetAsync(url);

            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<RiotTFTMatch>();
            }

            return null;
        }
    }
}
