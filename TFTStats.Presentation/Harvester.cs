using Microsoft.Extensions.Logging;
using TFTStats.Core.Entities.Harvester;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;

namespace TFTStats.Presentation
{
    public class Harvester
    {
        private readonly int _harvestCheckDelayMs;
        private readonly int _errorRetryDelayMs;
        private readonly bool _exitWhenIdle;

        private readonly RiotTFTMatchService _matchService;
        private readonly ITFTPatchRepository _patchRepo;
        private readonly IHarvesterRepository _harvestRepo;
        private readonly ILogger<Harvester> _logger;

        public Harvester(
            RiotTFTMatchService matchService,
            ITFTPatchRepository patchRepo,
            IHarvesterRepository harvestRepo,
            ILogger<Harvester> logger,
            int harvestCheckDelayMs = 10000,
            int errorRetryDelayMs = 5000,
            bool exitWhenIdle = false)
        {
            _matchService = matchService;
            _patchRepo = patchRepo;
            _harvestRepo = harvestRepo;
            _logger = logger;
            _harvestCheckDelayMs = harvestCheckDelayMs;
            _errorRetryDelayMs = errorRetryDelayMs;
            _exitWhenIdle = exitWhenIdle;
        }

        public async Task RunAsync(string cluster, int targetSet, CancellationToken ct)
        {
            _logger.LogInformation("[Harvester] Starting harvest loop on cluster: {cluster}", cluster);

            long setStartTime = 0;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (setStartTime == 0)
                    {
                        var activePatch = await _patchRepo.GetFirstPatch(targetSet);
                        setStartTime = ((DateTimeOffset)activePatch.StartDate).ToUnixTimeSeconds();
                    }
                    var playerInfo = await _harvestRepo.GetNextPlayerToHarvestAsync();

                    if (string.IsNullOrEmpty(playerInfo.Puuid))
                    {
                        if (_exitWhenIdle)
                        {
                            _logger.LogInformation("[Harvester] No pending players. Harvest phase complete.");
                            return;
                        }

                        _logger.LogInformation("[Harvester] No pending players. Waiting {delayMs}ms", _harvestCheckDelayMs);
                        await Task.Delay(_harvestCheckDelayMs, ct);
                        continue;
                    }

                    _logger.LogInformation("[Harvester] Harvesting player: {puuid}", playerInfo.Puuid);

                    await HarvestPlayerAsync(cluster, targetSet, setStartTime, playerInfo.Puuid, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    _logger.LogInformation("[Harvester] Shutdown requested. Stopping...");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError("[Harvester ERROR] {exMessage}", ex.Message);
                    try
                    {
                        await Task.Delay(_errorRetryDelayMs, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("[Harvester] Shutdown requested during retry delay. Stopping...");
                        break;
                    }
                }
            }
        }

        private async Task HarvestPlayerAsync(string cluster, int targetSet, long setStartTime, string puuid, CancellationToken ct)
        {
            var matchIds = await _matchService.GetSetMatchIdsAsync(cluster, puuid, setStartTime, ct);

            if (matchIds.Count == 0)
            {
                _logger.LogInformation("[Harvester] Player {puuid} has 0 matches for set {setNumber}", puuid, targetSet);
                await _harvestRepo.MarkPlayerAsHarvestedAsync(puuid);
                return;
            }

            _logger.LogInformation("[Harvester] Found {count} matches for {puuid}", matchIds.Count, puuid);

            var harvestedMatches = matchIds.Select(x => new MatchHarvestInfo(
                MatchId: x,
                GameCreation: 0,
                GameDateTime: null,
                SetNumber: targetSet,
                QueueId: null
            )).ToList();

            await _harvestRepo.UpsertMatchIdsAsync(puuid, harvestedMatches);
            await _harvestRepo.MarkPlayerAsHarvestedAsync(puuid);

            var pendingCount = await _harvestRepo.GetPendingMatchCountAsync();
            _logger.LogInformation("[Harvester] Player {puuid} finished. Pending matches in queue: {pendingCount} (+ {newMatches})", puuid, pendingCount, harvestedMatches.Count);
        }
    }
}
