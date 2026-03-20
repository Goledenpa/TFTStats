using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly;
using Polly.Extensions.Http;
using System.Data.Common;
using System.Net;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Infrastructure.Importers;
using TFTStats.Core.Infrastructure.Importers.Interfaces;
using TFTStats.Core.Repositories;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;
using TFTStats.Core.Service.Cache;

namespace TFTStats.Core.Base
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, string connectionString)
        {
            services.AddTransient<RiotRateLimitHandler>();
            services.AddSingleton<DbProviderFactory>(NpgsqlFactory.Instance);
            services.AddSingleton<UnitCacheService>();
            services.AddSingleton<ItemCacheService>();
            services.AddSingleton<TraitCacheService>();

            services.AddSingleton(sp =>
            {
                var factory = sp.GetRequiredService<DbProviderFactory>();
                return new SqlExecutor(connectionString, factory);
            });

            services.AddTransient<IApiKeyProvider, DbApiKeyProvider>();

            services.AddHttpClient<RiotApiClient>(c =>
            {
                c.Timeout = TimeSpan.FromMinutes(10);
            })
            .AddPolicyHandler(GetRetryPolicy())
            .AddHttpMessageHandler<RiotRateLimitHandler>();

            services.AddTransient<RiotAccountService>();
            services.AddTransient<RiotTFTSummonerService>();
            services.AddTransient<RiotTFTMatchService>();

            services.AddTransient<IRiotDataImporter>(sp =>
            {
                return ActivatorUtilities.CreateInstance<PostgreSqlBinaryImporter>(sp, connectionString);
            });

            services.AddTransient<IMatchRepository, MatchRepository>();
            services.AddTransient<ITFTPatchRepository, TFTPatchRepository>();

            return services;
        }

        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: _ => TimeSpan.Zero
            );
        }
    }
}
