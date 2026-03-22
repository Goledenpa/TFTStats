using TFTStats.Core.Entities;

namespace TFTStats.Core.Infrastructure.Importers.Interfaces
{
    public interface IRiotDataImporter
    {
        Task ImportMatchStreamAsync(IAsyncEnumerable<Match> matchStream, CancellationToken ct = default);
    }
}
