using Microsoft.Extensions.Logging;
using TFTStats.Core.Infrastructure.Importers.Interfaces;
using TFTStats.Core.Mappings;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;

namespace TFTStats.Presentation
{
    public class Crawler
    {
        private readonly RiotTFTMatchService _matchService;
        private readonly IMatchRepository _matchRepo;
        private readonly IRiotDataImporter _importer;
        private readonly ILogger<Crawler> _logger;
        private readonly ITFTPatchRepository _patchRepository;

        public Crawler(RiotTFTMatchService matchService, IMatchRepository matchRepo, IRiotDataImporter importer, ILogger<Crawler> logger, ITFTPatchRepository tftPatchRepository)
        {
            _matchService = matchService;
            _matchRepo = matchRepo;
            _importer = importer;
            _logger = logger;
            _patchRepository = tftPatchRepository;
        }

        public async Task RunAsync(string cluster, int targetSet, CancellationToken ct)
        {
            DateTime longTime = (await _patchRepository.GetLastPatch(targetSet)).StartDate;
            DateTime startSetTime = (await _patchRepository.GetFirstPatch(targetSet)).StartDate;
            long longTimeStart = ((DateTimeOffset)longTime).ToUnixTimeSeconds();
            long longStartSetTime = ((DateTimeOffset)startSetTime).ToUnixTimeSeconds();
            long currentQueryStartTime;

            _logger.LogInformation("[Crawler] Starting crawl on cluster: {cluster}", cluster);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (puuid, lastCrawledAt) = await _matchRepo.GetNextPlayerToCrawlAsync();

                    if (string.IsNullOrEmpty(puuid))
                    {
                        _logger.LogInformation("[Crawler] No peding players in DB. Waiting 10 seconds...");
                        await Task.Delay(10000, ct);
                        continue;
                    }

                    if (lastCrawledAt.HasValue)
                    {
                        currentQueryStartTime = new DateTimeOffset(lastCrawledAt.Value).ToUnixTimeSeconds() - 3600;
                    }
                    else
                    {
                        currentQueryStartTime = longTimeStart;
                    }

                    var startTimeAsOffset = DateTimeOffset.FromUnixTimeSeconds(currentQueryStartTime);
                    var localStart = startTimeAsOffset.ToLocalTime();
                    var timeSpan = DateTimeOffset.UtcNow - startTimeAsOffset;

                    string searchRangeInfo;

                    if(currentQueryStartTime == longStartSetTime)
                    {
                        searchRangeInfo = $"Start of Set ({localStart:MMM dd, yyyy HH:mm})";
                    }
                    else
                    {
                        string timeAgo = timeSpan.TotalDays >= 1
                            ? $"{(int)timeSpan.TotalDays} days"
                            : $"{(int)timeSpan.TotalHours} hours";

                        searchRangeInfo = $"{localStart:MMM dd yyyy, yyyy HH:mm} ({timeAgo} ago)";
                    }

                    _logger.LogInformation("[Crawler] Target Found: {puuid}", puuid);
                    _logger.LogInformation("[Crawler] Searching from: {searchRangeInfo}", searchRangeInfo);

                    var allIds = await _matchService.GetSetMatchIdsAsync(cluster, puuid, currentQueryStartTime, ct);

                    var uniqueIds = allIds.Distinct().ToList();

                    if(uniqueIds.Count != allIds.Count)
                    {
                        _logger.LogInformation("[System] Duplicated ID in Riot GetMatchs");
                    }

                    var newIds = await _matchRepo.FilterNewMatchIdsAsync(uniqueIds);

                    if (newIds.Count > 0)
                    {
                        // Calculation for clarity
                        int existingCount = uniqueIds.Count - newIds.Count;

                        _logger.LogInformation("[Crawler] Found {newIdsCount} new matches ({existingCount} already in DB, {uniqueIdsCount} total for this set).", newIds.Count, existingCount, uniqueIds.Count);
                        _logger.LogInformation("[Crawler] Starting Ingestion...");

                        var dtoStream = _matchService.GetMatchStreamAsync(cluster, newIds, ct);
                        var entityStream = dtoStream
                            .Where(x => x.Info.TftSetNumber == targetSet)
                            .Select(x => x.ToEntity())
                            .Where(x => x.Participants.Count != 0);

                        await _importer.ImportMatchStremAsync(entityStream, ct);
                    }
                    else
                    {
                        _logger.LogInformation("[Crawler] 0 new matches found. (Player is up to date with {uniqueIdsCount} matches).", uniqueIds.Count);
                    }

                    await _matchRepo.MarkPlayerAsCrawledAsync(puuid);

                    int totalPlayers = await _matchRepo.GetTotalPlayerCountAsync();
                    _logger.LogInformation("[Crawler] Target {puuid} finished. Total Players Known: {totalPlayers}", puuid, totalPlayers);
                }
                catch (OperationCanceledException ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        _logger.LogInformation("[Crawler] Shutdown requested by user. Stopping...");
                        break;
                    }
                    else
                    {
                        _logger.LogError("[Crawler ERROR] A task timed out (Internal Cancellation): {exMessage}", ex.Message);
                        await Task.Delay(5000, ct);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Crawler ERROR] {exMessage}", ex.Message);

                    await Task.Delay(5000, ct);
                }
            }
        }
    }
}
