namespace TFTStats.Core.Infrastructure
{
    public class RiotApiClient
    {
        public HttpClient Client { get; }

        public RiotApiClient(HttpClient client)
        {
            Client = client;
        }

        public string BuildUrl(string routingValue, string path)
        {
            return $"https://{routingValue}.api.riotgames.com/{path}";
        }
    }
}
