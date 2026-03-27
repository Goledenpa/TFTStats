using Microsoft.Extensions.Logging;
using System.Net;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Infrastructure
{
    public class RiotRateLimitHandler : DelegatingHandler
    {
        private readonly ISettingsProvider _keyProvider;
        private readonly ILogger<RiotRateLimitHandler> _logger;

        public RiotRateLimitHandler(ISettingsProvider keyProvider, ILogger<RiotRateLimitHandler> logger)
        {
            _keyProvider = keyProvider;
            _logger = logger;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string currentKey = await _keyProvider.GetApiKeyAsync();

            request.Headers.Remove("X-Riot-Token");
            request.Headers.Add("X-Riot-Token", currentKey);

            var response = await base.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);

                _logger.LogWarning("Riot API Rate Limit hit. Resuming in {seconds}s [{time}]... ", retryAfter.TotalSeconds,
                    DateTime.UtcNow.AddSeconds(retryAfter.TotalSeconds).ToLocalTime().ToString("HH:mm:ss"));

                await ExecuteVisualCountdown(retryAfter, ct);

                _logger.LogInformation("Rate limit cooldown finished. Retry requesting... ");
            }

            if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.LogCritical("API Key has EXPIRED! Crawler is now PARKED.");
                _logger.LogInformation("Update the 'app_settings' table in Postgres with a new key to resume.");

                await WaitForNewKey(currentKey, ct);

                _logger.LogInformation("New API Key detected. Resuming crawler...");
            }

            return response;
        }

        private static async Task ExecuteVisualCountdown(TimeSpan delay, CancellationToken ct)
        {
            int totalSeconds = (int)Math.Ceiling(delay.TotalSeconds);

            Console.WriteLine();
            try
            {
                for (int i = totalSeconds; i > 0; i--)
                {
                    Console.Write($"\r[Rate Limit] Cooldown: {i}s remaining...              ");

                    await Task.Delay(1000, ct);
                }

                Console.WriteLine($"\r{"[Rate Limit] Resuming ingestion...",-60}");
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\r[Rate Limit] Cooldown aborted.                        ");
                throw;
            }
        }
        private async Task WaitForNewKey(string oldKey, CancellationToken ct)
        {
            string currentAttempKey = oldKey;

            while (true)
            {
                _keyProvider.InvalidateCache();
                string newKey = await _keyProvider.GetApiKeyAsync();

                if (newKey != oldKey && !string.IsNullOrWhiteSpace(newKey))
                {
                    currentAttempKey = newKey;
                    _logger.LogInformation("New API Key detected in DB. Entering progapation validation...");
                    break;
                }

                _logger.LogTrace("Still waiting for a new API Key in the database...");
                Console.Write($"\r[System] Parked: Waiting for new key... ({DateTime.Now:HH:mm:ss})  ");

                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }

            int attempt = 1;
            while (true)
            {
                _logger.LogInformation("Validating new Key, (Attempt {attempt})...", attempt);

                if (await IsKeyGloballyActive(currentAttempKey, ct))
                {
                    _logger.LogInformation("Validation successful!");
                    break;
                }

                _logger.LogWarning("Key returned 403 (Propagation is in progress). Waiting 30s...");

                for (int i = 30; i > 0; i--)
                {
                    Console.WriteLine($"\r[System] Riot Propagation: {i}s remaining...");
                    await Task.Delay(1000, ct);
                }
                attempt++;
            }
            Console.WriteLine();
        }

        private async Task<bool> IsKeyGloballyActive(string key, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://euw1.api.riotgames.com/tft/status/v1/platform-data");
                request.Headers.Add("X-Riot-Token", key);

                var res = await base.SendAsync(request, ct);

                return res.StatusCode != HttpStatusCode.Forbidden &&
                       res.StatusCode != HttpStatusCode.Unauthorized;
            }
            catch
            {
                return false;
            }
        }
    }
}
