using TFTStats.Core.Entities;

namespace TFTStats.Core.Infrastructure.Importers.Interfaces
{
    public interface IRiotDataImporter
    {
        /// <summary>
        /// Imports matches from the stream into the database within a transaction.
        /// Returns the list of match IDs that were successfully imported.
        /// </summary>
        Task<List<string>> ImportMatchStreamAsync(IAsyncEnumerable<Match> matchStream, CancellationToken ct = default);
    }
}
