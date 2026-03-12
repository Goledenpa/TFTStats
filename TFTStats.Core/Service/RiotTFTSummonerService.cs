using System.Net.Http.Json;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Models;

namespace TFTStats.Core.Service
{
    public class RiotTFTSummonerService
    {
        private readonly RiotApiClient _apiClient;

        public RiotTFTSummonerService(RiotApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<RiotTFTSummoner?> GetSummonerByPuuidAsync(string region, string puuid)
        {
            var url = _apiClient.BuildUrl(region, $"tft/summoner/v1/summoners/by-puuid/{puuid}");
            var res = await _apiClient.Client.GetAsync(url);

            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<RiotTFTSummoner>();
            }

            return null;
        }
    }
}
