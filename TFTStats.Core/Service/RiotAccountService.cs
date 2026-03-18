using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Models;

namespace TFTStats.Core.Service
{
    public class RiotAccountService
    {
        private readonly RiotApiClient _apiClient;
        private readonly ILogger<RiotAccountService> _logger;

        public RiotAccountService(RiotApiClient apiClient, ILogger<RiotAccountService> logger)
        {
            _apiClient = apiClient;
            _logger = logger;
        }

        public async Task<RiotTFTAccount?> GetAccountByRiotId(string region, string name, string tagline)
        {
            var url = _apiClient.BuildUrl(region, $"riot/account/v1/accounts/by-riot-id/{name}/{tagline}");
            var res = await _apiClient.Client.GetAsync(url);

            if (res.IsSuccessStatusCode)
            {
                return await res.Content.ReadFromJsonAsync<RiotTFTAccount>();
            }

            return null;
        }
    }
}
