namespace TFTStats.Core.Repositories.Interfaces
{
    public interface IApiKeyProvider
    {
        Task<string> GetApiKeyAsync();
        void InvalidateCache();
    }
}
