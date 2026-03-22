namespace TFTStats.Core.Repositories.Interfaces
{
    public interface ISettingsProvider
    {
        Task<string> GetApiKeyAsync();
        Task<string> GetTargetPatchAsync();
        void InvalidateCache();
    }
}
