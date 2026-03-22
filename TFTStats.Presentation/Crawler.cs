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
        private const int crawlerCheckDelayMs = 10000;
        private const int errorRetryDelayMs = 5000;
        private const int hourInSeconds = 3600;

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
            var setTimeRange = await GetSetTimeRangeAsync(targetSet);
            _logger.LogInformation("[Crawler] Starting crawl on cluster: {cluster}", cluster);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var (puuid, lastCrawledAt) = await _matchRepo.GetNextPlayerToCrawlAsync();

                    if (string.IsNullOrEmpty(puuid))
                    {
                        _logger.LogInformation("[Crawler] No peding players in DB. Waiting 10 seconds...");
                        await Task.Delay(crawlerCheckDelayMs, ct);
                        continue;
                    }

                    await CrawlPlayerAsync(cluster, targetSet, puuid, lastCrawledAt, setTimeRange, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("[Crawler] Shutdown requested by user. Stopping...");
                    break;
                }
                catch (OperationCanceledException ex)
                {
                    _logger.LogError("[Crawler ERROR] A task timed out (Internal Cancellation): {exMessage}", ex.Message);
                    await Task.Delay(errorRetryDelayMs, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Crawler ERROR] {exMessage}", ex.Message);
                    await Task.Delay(errorRetryDelayMs, ct);
                }
            }
        }

        private async Task<SetTimeRange> GetSetTimeRangeAsync(int targetSet)
        {
            var lastPatch = await _patchRepository.GetLastPatch(targetSet);
            //var firstPatch = await _patchRepository.GetFirstPatch(targetSet);
            
            return new SetTimeRange(lastPatch);
        }

        private async Task CrawlPlayerAsync(string cluster, int targetSet, string puuid, DateTime? lastCrawledAt, SetTimeRange setTimeRange, CancellationToken ct)
        {
            long queryStartTime = lastCrawledAt.HasValue
                ? new DateTimeOffset(lastCrawledAt.Value).ToUnixTimeSeconds() - hourInSeconds
                : setTimeRange.PatchStartTime;

            _logger.LogInformation("[Crawler] Target Found: {puuid}", puuid);

            LogSearchRange(queryStartTime, setTimeRange.PatchEndTime);

            var allIds = await _matchService.GetSetMatchIdsAsync(cluster, puuid, queryStartTime, ct);
            var uniqueIds = allIds.Distinct().ToList();

            if (uniqueIds.Count != allIds.Count)
            {
                _logger.LogInformation("[System] Duplicated ID in Riot GetMatchs");
            }

            var newIds = await _matchRepo.FilterNewMatchIdsAsync(uniqueIds);

            if (newIds.Count > 0)
            {
                await ImportNewMatchesAsync(cluster, targetSet, newIds, uniqueIds, ct);
            }
            else
            {
                _logger.LogInformation("[Crawler] 0 new matches found. (Player is up to date with {uniqueIdsCount} matches).", uniqueIds.Count);
            }

            await _matchRepo.MarkPlayerAsCrawledAsync(puuid);

            int totalPlayers = await _matchRepo.GetTotalPlayerCountAsync();
            _logger.LogInformation("[Crawler] Target {puuid} finished. Total Players Known: {totalPlayers}", puuid, totalPlayers);
        }

        private void LogSearchRange(long queryStartTime, long setEndTime)
        {
            var startTimeAsOffset = DateTimeOffset.FromUnixTimeSeconds(queryStartTime);
            var localStart = startTimeAsOffset.ToLocalTime();
            var timeSpan = DateTimeOffset.UtcNow - startTimeAsOffset;

            string searchRangeInfo = queryStartTime == setEndTime
                ? $"Start of Set ({localStart:MMM dd, yyyy HH:mm})"
                : $"{localStart:MMM dd, yyyy HH:mm} ({GetTimeAgoString(timeSpan)} ago)";

            _logger.LogInformation("[Crawler] Searching from: {searchRangeInfo}", searchRangeInfo);
        }

        private string GetTimeAgoString(TimeSpan timeSpan)
        {
            return timeSpan.TotalDays >= 1
                ? $"{(int)timeSpan.TotalDays} days"
                : $"{(int)timeSpan.TotalHours} hours";
        }

        private async Task ImportNewMatchesAsync(string cluster, int targetSet, List<string> newIds, List<string> uniqueIds, CancellationToken ct)
        {
            int existingCount = uniqueIds.Count - newIds.Count;

            _logger.LogInformation("[Crawler] Found {newIdsCount} new matches ({existingCount} already in DB, {uniqueIdsCount} total for this patch).", newIds.Count, existingCount, uniqueIds.Count);
            _logger.LogInformation("[Crawler] Starting Ingestion...");

            var dtoStream = _matchService.GetMatchStreamAsync(cluster, newIds, ct);
            var entityStream = dtoStream
                .Where(x => x.Info.TftSetNumber == targetSet)
                .Select(x => x.ToEntity())
                .Where(x => x.Participants.Count != 0);

            await _importer.ImportMatchStremAsync(entityStream, ct);
        }
    }
}