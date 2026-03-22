using Microsoft.Extensions.Logging;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Repositories
{
    public class DbSettingsProvider : ISettingsProvider
    {
        private readonly ILogger<DbSettingsProvider> _logger;
        private readonly SqlExecutor _sqlExecutor;
        
        private string? _cachedKey;
        private DateTime _expiryTime;
        private string? _cachedPatch;

        public DbSettingsProvider(SqlExecutor sqlExecutor, ILogger<DbSettingsProvider> logger)
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

            await RefreshApiKeyAsync();

            return _cachedKey ?? string.Empty;
        }

        public async Task<string> GetTargetPatchAsync()
        {
            const string query = "SELECT value FROM app_settings WHERE key = 'crawler_target_patch'";

            _cachedPatch = await _sqlExecutor.QueryScalarAsync<string>(query) ?? "16.1";

            return _cachedPatch;
        }

        public void InvalidateCache()
        {
            _expiryTime = DateTime.MinValue;
        }

        private async Task RefreshApiKeyAsync()
        {
            const string query = "SELECT value, last_updated_at FROM app_settings WHERE key = 'riot_api_key'";

            var res = await _sqlExecutor.QueryFirstOrDefaultAsync(query, r => new
            {
                Value = r.GetString(0),
                UpdatedAt = r.GetDateTime(1)
            });

            if (res is not null)
            {
                _cachedKey = res.Value;
                _expiryTime = res.UpdatedAt.AddHours(23.5);

                _logger.LogInformation("API Key Refresh from DB. Expires at {expireTime}", _expiryTime.ToLocalTime());
            }
        }
    }
}
