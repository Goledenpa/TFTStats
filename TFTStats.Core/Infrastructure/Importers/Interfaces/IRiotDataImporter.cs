using TFTStats.Core.Entities;

namespace TFTStats.Core.Infrastructure.Importers.Interfaces
{
    public interface IRiotDataImporter
    {
        Task ImportMatchStremAsync(IAsyncEnumerable<Match> matchStream, CancellationToken ct = default);
    }
}
