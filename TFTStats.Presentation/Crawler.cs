using Microsoft.Extensions.Logging;
using TFTStats.Core.Entities;
using TFTStats.Core.Infrastructure.Importers.Interfaces;
using TFTStats.Core.Mappings;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;

namespace TFTStats.Presentation
{
    public class Crawler
    {
        private readonly int _crawlerCheckDelayMs;
        private readonly int _errorRetryDelayMs;
        private const int batchSize = 50;

        private readonly RiotTFTMatchService _matchService;
        private readonly IMatchRepository _matchRepo;
        private readonly IHarvesterRepository _harvesterRepo;
        private readonly IRiotDataImporter _importer;
        private readonly ILogger<Crawler> _logger;

        public Crawler(
            RiotTFTMatchService matchService,
            IMatchRepository matchRepo,
            IHarvesterRepository harvesterRepo,
            IRiotDataImporter importer,
            ILogger<Crawler> logger,
            int crawlerCheckDelayMs = 10000,
            int errorRetryDelayMs = 5000)
        {
            _matchService = matchService;
            _matchRepo = matchRepo;
            _harvesterRepo = harvesterRepo;
            _importer = importer;
            _logger = logger;
            _crawlerCheckDelayMs = crawlerCheckDelayMs;
            _errorRetryDelayMs = errorRetryDelayMs;
        }

        public async Task RunAsync(string cluster, CancellationToken ct)
        {
            _logger.LogInformation("[Crawler] Starting batch ingestion loop on cluster: {cluster}", cluster);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var matchIds = await _matchRepo.GetNextPendingMatchIdsAsync(batchSize);

                    if (matchIds.Count == 0)
                    {
                        _logger.LogInformation("[Crawler] No pending matches in queue. Waiting {delayMs}ms...", _crawlerCheckDelayMs);
                        await Task.Delay(_crawlerCheckDelayMs, ct);
                        continue;
                    }

                    _logger.LogInformation("[Crawler] Fetching details for {count} matches...", matchIds.Count);

                    var newMatchIds = await _matchRepo.FilterNewMatchIdsAsync(matchIds);

                    var matches = new List<Match>();
                    foreach (var id in newMatchIds)
                    {
                        var dto = await _matchService.GetMatch(cluster, id, ct);
                        if (dto is not null)
                        {
                            var entity = dto.ToEntity();
                            if (entity.Participants.Count > 0)
                            {
                                matches.Add(entity);
                            }
                        }
                    }

                    if (matches.Count > 0)
                    {
                        await _importer.ImportMatchStreamAsync(ToAsyncEnumerable(matches), ct);
                    }

                    // Mark ALL pending IDs as crawled (including duplicates that were filtered)
                    await _matchRepo.MarkMatchesAsCrawledAsync(matchIds);

                    var pendingCount = await _harvesterRepo.GetPendingMatchCountAsync();
                    _logger.LogInformation("[Crawler] Batch ingested ({count} matches). Pending matches: {pendingCount}", matches.Count, pendingCount);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("[Crawler] Shutdown requested. Stopping...");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Crawler ERROR] {exMessage}", ex.Message);
                    try
                    {
                        await Task.Delay(_errorRetryDelayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("[Crawler] Shutdown requested during retry delay. Stopping...");
                        break;
                    }
                }
            }
        }

        private static async IAsyncEnumerable<Match> ToAsyncEnumerable(IEnumerable<Match> matches)
        {
            foreach (var m in matches)
            {
                yield return m;
            }
        }
    }
}
