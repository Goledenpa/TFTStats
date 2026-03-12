using System.Net.Http.Json;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Models;

namespace TFTStats.Core.Service
{
    public class RiotAccountService
    {
        private RiotApiClient _apiClient;

        public RiotAccountService(RiotApiClient apiClient)
        {
            _apiClient = apiClient;
        }

        public async Task<RiotAccount?> GetAccountByRiotId(string region, string name, string tagLine)
        {
            var url = _apiClient.BuildUrl(region, $"riot/account/v1/accounts/by-riot-id/{name}/{tagLine}");
            var res = await _apiClient.Client.GetAsync(url);

            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<RiotAccount>();
            }

            return null;
        }
    }
}
