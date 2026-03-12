using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using System.Net;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Service;

namespace TFTStats.Core.Base
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddCoreServices(this IServiceCollection services, string apiKey, string connectionString)
        {
            services.AddTransient<RiotRateLimitHandler>();

            services.AddHttpClient<RiotApiClient>(c =>
            {
                c.DefaultRequestHeaders.Add("X-Riot-Token", apiKey);
            })
            .AddHttpMessageHandler<RiotRateLimitHandler>()
            .AddPolicyHandler(GetRetryPolicy());

            services.AddDbContext<TFTStatsDbContext>(opt =>
                opt.UseNpgsql(connectionString)
            );    

            services.AddTransient<RiotAccountService>();
            services.AddTransient<RiotTFTSummonerService>();
            services.AddTransient<RiotTFTMatchService>();

            return services;
        }


        private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: (retryCount, response, context) =>
                    {
                        // If Riot sends a Retry-After header, use it. Otherwise, wait 10s.
                        return response.Result?.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                    },
                    onRetryAsync: async (response, timespan, retryCount, context) =>
                    {
                        // This logic runs right before the wait starts
                        await Task.CompletedTask;
                    }
            );
        }
    }
}
