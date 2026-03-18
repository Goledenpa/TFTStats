using Microsoft.Extensions.Logging;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Repositories
{
    public class DbApiKeyProvider : IApiKeyProvider
    {
        private readonly ILogger<DbApiKeyProvider> _logger;
        private readonly SqlExecutor _sqlExecutor;
        private string? _cachedKey;
        private DateTime _expiryTime;

        public DbApiKeyProvider(SqlExecutor sqlExecutor, ILogger<DbApiKeyProvider> logger)
        {
            _sqlExecutor = sqlExecutor;
            _logger = logger;
        }

        public async Task<string> GetApiKeyAsync()
        {
            if (_cachedKey is not null && DateTime.UtcNow < _expiryTime)
            {
                return _cachedKey;
            }

            await RefreshFromDbAsync();

            return _cachedKey ?? string.Empty;
        }

        public void InvalidateCache()
        {
            _expiryTime = DateTime.MinValue;
        }

        private async Task RefreshFromDbAsync()
        {
            const string query = "SELECT value, last_updated_at FROM app_settings WHERE key = 'riot_api_key'";

            var res = await _sqlExecutor.QueryFirstOrDefaultAsync(query, r => new
            {
                Key = r.GetString(0),
                UpdatedAt = r.GetDateTime(1)
            });

            if (res is not null)
            {
                _cachedKey = res.Key;
                _expiryTime = res.UpdatedAt.AddHours(23.5);

                _logger.LogInformation("API Key Refresh from DB. Expires at {expireTime}", _expiryTime.ToLocalTime());
            }
        }
    }
}
