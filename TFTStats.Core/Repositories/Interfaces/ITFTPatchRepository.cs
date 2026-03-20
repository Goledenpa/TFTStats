using TFTStats.Core.Entities;

namespace TFTStats.Core.Repositories.Interfaces
{
    public interface ITFTPatchRepository
    {
        Task<TFTPatch> GetFirstPatch(int set);
        Task<TFTPatch> GetLastPatch(int set);
    }
}
