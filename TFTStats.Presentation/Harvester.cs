using Microsoft.Extensions.Logging;
using TFTStats.Core.Entities;
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
        private int _totalPlayers;

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

            List<TFTPatch> patches = [];
            _totalPlayers = await _harvestRepo.GetRemainingPlayerCountAsync();
            _logger.LogInformation("[Harvester] Initial remaining players: {totalPlayers}", _totalPlayers);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (patches.Count == 0)
                    {
                        patches = (await _patchRepo.GetPatchesBySetAsync(targetSet)).ToList();
                        if (patches.Count == 0)
                        {
                            _logger.LogError("[Harvester] No patches found for set {setNumber}. Aborting.", targetSet);
                            return;
                        }
                        _logger.LogInformation("[Harvester] Found {patchCount} patches for set {setNumber}", patches.Count, targetSet);
                        foreach (var patch in patches)
                        {
                            _logger.LogInformation("[Harvester] Patch: {patchName} ({startDate} - {endDate})",
                                patch.PatchName, patch.StartDate, patch.EndDate);
                        }
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

                    await HarvestPlayerAsync(cluster, targetSet, patches, playerInfo.Puuid, ct);
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

        private async Task HarvestPlayerAsync(string cluster, int targetSet, List<TFTPatch> patches, string puuid, CancellationToken ct)
        {
            var patchResults = new List<PatchHarvestResult>();
            int totalMatchIds = 0;

            foreach (var patch in patches)
            {
                if (ct.IsCancellationRequested) return;

                var patchStartTime = ((DateTimeOffset)patch.StartDate).ToUnixTimeSeconds();

                try
                {
                    var matchIds = await _matchService.GetSetMatchIdsAsync(cluster, puuid, patchStartTime, ct);

                    if (matchIds.Count == 0)
                    {
                        _logger.LogDebug("[Harvester] Patch {patchName}: 0 matches for {puuid}", patch.PatchName, puuid);
                        patchResults.Add(new PatchHarvestResult(patch.PatchName, targetSet, 0, 0));
                        continue;
                    }

                    totalMatchIds += matchIds.Count;

                    var harvestedMatches = matchIds.Select(x => new MatchHarvestInfo(
                        MatchId: x,
                        GameCreation: 0,
                        GameDateTime: null,
                        SetNumber: targetSet,
                        QueueId: null,
                        PatchId: patch.Id
                    )).ToList();

                    var pendingBefore = await _harvestRepo.GetPendingMatchCountAsync();
                    await _harvestRepo.UpsertMatchIdsAsync(puuid, harvestedMatches);
                    var pendingAfter = await _harvestRepo.GetPendingMatchCountAsync();
                    int newAdded = pendingAfter - pendingBefore;

                    patchResults.Add(new PatchHarvestResult(patch.PatchName, targetSet, matchIds.Count, newAdded));

                    _logger.LogDebug("[Harvester] Patch {patchName}: {found} match IDs found, {new} new added to staging",
                        patch.PatchName, matchIds.Count, newAdded);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("[Harvester] Failed to harvest patch {patchName} for {puuid}: {exMessage}",
                        patch.PatchName, puuid, ex.Message);
                    return; // Don't mark player as harvested, retry next cycle
                }
            }

            if (patchResults.Count > 0)
            {
                var summary = string.Join("\r\n", patchResults.Select(p =>
                    $"  {p.PatchName}: {p.MatchIdsFound} found, {p.NewMatchIdsAdded} new"));
                _logger.LogInformation("[Harvester] Player {puuid} harvest complete.\r\n" +
                    "Total match IDs collected: {totalIds}\r\n" +
                    "Per-patch breakdown:\r\n{summary}",
                    puuid, totalMatchIds, summary);
            }

            await _harvestRepo.MarkPlayerAsHarvestedAsync(puuid);

            var remaining = await _harvestRepo.GetRemainingPlayerCountAsync();
            var pendingCount = await _harvestRepo.GetPendingMatchCountAsync();
            double pct = _totalPlayers > 0 ? Math.Round((1.0 - (double)remaining / _totalPlayers) * 100, 5) : 0;
            _logger.LogInformation("[Harvester] Player {puuid} finalized.\r\n" +
                "Remaining: {remaining} ({pct}%) | Pending matches: {pendingCount}",
                puuid, remaining, pct, pendingCount);
        }
    }
}
