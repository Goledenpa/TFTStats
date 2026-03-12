using System.Net;

namespace TFTStats.Core.Infrastructure
{
    public class RiotRateLimitHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);

            if(response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);

                Console.WriteLine($"[Rate Limit] Hit! Waiting {retryAfter.TotalSeconds} seconds...");
            }

            return response;
        }
    }
}
