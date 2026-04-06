using Microsoft.Extensions.Logging;
using Moq;
using TFTStats.Core.Entities;
using TFTStats.Core.Entities.Harvester;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;
using Xunit;
using Harvester = TFTStats.Presentation.Harvester;

namespace TFTStats.Tests.Presentation
{
    public class HarvesterTests
    {
        private readonly Mock<RiotTFTMatchService> _matchServiceMock;
        private readonly Mock<ITFTPatchRepository> _patchRepoMock;
        private readonly Mock<IHarvesterRepository> _harvestRepoMock;
        private readonly Mock<IMatchRepository> _matchRepoMock;
        private readonly Mock<ILogger<Harvester>> _loggerMock;

        private static readonly DateTime s_patchStart = new(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        private static readonly List<TFTPatch> s_defaultPatches = new()
        {
            new TFTPatch { StartDate = s_patchStart, SetNumber = 16, PatchName = "16.1" }
        };

        private void SetupDefaultPatches()
        {
            _patchRepoMock.Setup(x => x.GetPatchesBySetAsync(16))
                .ReturnsAsync(s_defaultPatches);
        }

        public HarvesterTests()
        {
            _matchServiceMock = new Mock<RiotTFTMatchService>(
                new RiotApiClient(new System.Net.Http.HttpClient()),
                Mock.Of<ILogger<RiotTFTMatchService>>()) { CallBase = false };
            _patchRepoMock = new Mock<ITFTPatchRepository>();
            _harvestRepoMock = new Mock<IHarvesterRepository>();
            _matchRepoMock = new Mock<IMatchRepository>();
            _loggerMock = new Mock<ILogger<Harvester>>();

            // Default mocks
            _harvestRepoMock.Setup(x => x.GetRemainingPlayerCountAsync())
                .ReturnsAsync(1000);
            _harvestRepoMock.Setup(x => x.GetPendingMatchCountAsync())
                .ReturnsAsync(0);
            _matchRepoMock.Setup(x => x.GetTotalPlayerCountAsync())
                .ReturnsAsync(1000);

            SetupDefaultPatches();
        }

        private Harvester CreateHarvester(int delayMs = 0, bool exitWhenIdle = false) => new(
            _matchServiceMock.Object,
            _patchRepoMock.Object,
            _harvestRepoMock.Object,
            _matchRepoMock.Object,
            _loggerMock.Object,
            harvestCheckDelayMs: delayMs,
            errorRetryDelayMs: delayMs,
            exitWhenIdle: exitWhenIdle);

        [Fact]
        public async Task RunAsync_HappyPath_GetsPlayer_UpsertsIds_MarksHarvested()
        {
            // Arrange
            var puuid = "player-abc-123";

            var callSequence = new Queue<PlayerHarvestInfo>(new[]
            {
                new PlayerHarvestInfo(puuid, null),
                new PlayerHarvestInfo("", null)
            });
            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(() => callSequence.Dequeue());

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", puuid, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "match-1", "match-2" });

            _harvestRepoMock.Setup(x => x.GetRemainingPlayerCountAsync())
                .ReturnsAsync(99);
            _harvestRepoMock.Setup(x => x.GetPendingMatchCountAsync())
                .ReturnsAsync(5);

            var cts = new CancellationTokenSource();
            _harvestRepoMock.Setup(x => x.MarkPlayerAsHarvestedAsync(puuid))
                .Callback(() => cts.Cancel());

            // Act
            await CreateHarvester().RunAsync("europe", 16, cts.Token);

            // Assert
            _harvestRepoMock.Verify(
                x => x.UpsertMatchIdsAsync(puuid, It.Is<List<MatchHarvestInfo>>(m =>
                    m.Count == 2 && m.All(x => x.SetNumber == 16))),
                Times.Once);
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync(puuid), Times.Once);
        }

        [Fact]
        public async Task RunAsync_ZeroMatches_SkipsUpsert_MarksHarvested()
        {
            // Arrange
            var callSequence = new Queue<PlayerHarvestInfo>(new[]
            {
                new PlayerHarvestInfo("p1", null),
                new PlayerHarvestInfo("", null)
            });
            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(() => callSequence.Dequeue());

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", "p1", It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string>());

            var cts = new CancellationTokenSource();
            _harvestRepoMock.Setup(x => x.MarkPlayerAsHarvestedAsync("p1"))
                .Callback(() => cts.Cancel());

            // Act
            await CreateHarvester().RunAsync("europe", 16, cts.Token);

            // Assert
            _harvestRepoMock.Verify(
                x => x.UpsertMatchIdsAsync(It.IsAny<string>(), It.IsAny<List<MatchHarvestInfo>>()),
                Times.Never);
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync("p1"), Times.Once);
        }

        [Fact]
        public async Task RunAsync_ErrorRecovers_ThenContinues()
        {
            // Arrange
            var callSequence = new Queue<PlayerHarvestInfo>(new[]
            {
                new PlayerHarvestInfo("p1", null),
                new PlayerHarvestInfo("", null)
            });

            // First call throws, subsequent calls succeed
            _harvestRepoMock.SetupSequence(x => x.GetNextPlayerToHarvestAsync())
                .ThrowsAsync(new Exception("DB connection lost"))
                .ReturnsAsync(() => callSequence.Dequeue())
                .ReturnsAsync(() => callSequence.Dequeue());

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", "p1", It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "m1" });

            var cts = new CancellationTokenSource();
            _harvestRepoMock.Setup(x => x.MarkPlayerAsHarvestedAsync("p1"))
                .Callback(() => cts.Cancel());

            // Act
            await CreateHarvester(delayMs: 1).RunAsync("europe", 16, cts.Token);

            // Assert - despite the error, the player was eventually harvested
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync("p1"), Times.Once);
        }

        [Fact]
        public async Task RunAsync_NoPendingPlayers_WaitsAndChecksAgain()
        {
            // Arrange
            var callSequence = new Queue<PlayerHarvestInfo>(new[]
            {
                new PlayerHarvestInfo("", null),
                new PlayerHarvestInfo("p1", null),
                new PlayerHarvestInfo("", null)
            });
            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(() => callSequence.Dequeue());

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", "p1", It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "m1" });

            var cts = new CancellationTokenSource();
            _harvestRepoMock.Setup(x => x.MarkPlayerAsHarvestedAsync("p1"))
                .Callback(() => cts.Cancel());

            // Act
            await CreateHarvester().RunAsync("europe", 16, cts.Token);

            // Assert - must have checked at least twice (once empty, once with player)
            _harvestRepoMock.Verify(x => x.GetNextPlayerToHarvestAsync(), Times.AtLeast(2));
        }

        [Fact]
        public async Task RunAsync_Cancellation_ExitsGracefully()
        {
            // Arrange
            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act - should return immediately since already cancelled
            await CreateHarvester().RunAsync("europe", 16, cts.Token);

            // Assert - no harvesting should have occurred
            _harvestRepoMock.Verify(
                x => x.MarkPlayerAsHarvestedAsync(It.IsAny<string>()),
                Times.Never);
            _harvestRepoMock.Verify(
                x => x.UpsertMatchIdsAsync(It.IsAny<string>(), It.IsAny<List<MatchHarvestInfo>>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_GetPatchesThrows_ErrorIsCaughtAndRetried()
        {
            // Arrange - GetPatchesBySetAsync throws once, then succeeds
            _patchRepoMock.SetupSequence(x => x.GetPatchesBySetAsync(16))
                .ThrowsAsync(new Exception("Patch API down"))
                .ReturnsAsync(s_defaultPatches);

            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(200);

            // Act - should not crash
            await CreateHarvester(delayMs: 1).RunAsync("europe", 16, cts.Token);

            // Assert - recovered and continued
            _patchRepoMock.Verify(x => x.GetPatchesBySetAsync(16), Times.AtLeast(2));
        }

        [Fact]
        public async Task RunAsync_UpsertThrows_PlayerNotMarkedHarvested()
        {
            // Arrange
            var puuid = "p1";

            _harvestRepoMock.SetupSequence(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo(puuid, null))
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", puuid, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "m1" });

            // Upsert throws, so MarkPlayerAsHarvestedAsync should NOT be called
            _harvestRepoMock.Setup(x => x.UpsertMatchIdsAsync(It.IsAny<string>(), It.IsAny<List<MatchHarvestInfo>>()))
                .ThrowsAsync(new Exception("Insert failed"));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(200);

            // Act
            await CreateHarvester(delayMs: 1).RunAsync("europe", 16, cts.Token);

            // Assert - player NOT marked harvested because upsert failed
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync(puuid), Times.Never);
        }

        [Fact]
        public async Task RunAsync_GetSetMatchIdsThrows_PlayerNotMarkedHarvested()
        {
            // Arrange

            _harvestRepoMock.SetupSequence(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo("p1", null))
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            // API call throws
            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", "p1", It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Riot API timeout"));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(200);

            // Act
            await CreateHarvester(delayMs: 1).RunAsync("europe", 16, cts.Token);

            // Assert - player NOT marked harvested because API failed
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync("p1"), Times.Never);
            _harvestRepoMock.Verify(x => x.UpsertMatchIdsAsync(It.IsAny<string>(), It.IsAny<List<MatchHarvestInfo>>()), Times.Never);
        }

        [Fact]
        public async Task RunAsync_ExitWhenIdle_ExitsImmediatelyWhenNoPlayers()
        {
            // Arrange
            _harvestRepoMock.Setup(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            // Act - should return immediately without cancellation
            await CreateHarvester(exitWhenIdle: true).RunAsync("europe", 16, CancellationToken.None);

            // Assert - no harvesting occurred
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync(It.IsAny<string>()), Times.Never);
            _harvestRepoMock.Verify(x => x.GetNextPlayerToHarvestAsync(), Times.Once);
        }

        [Fact]
        public async Task RunAsync_ExitWhenIdle_ProcessesPlayersThenExits()
        {
            // Arrange
            var puuid = "p1";

            _harvestRepoMock.SetupSequence(x => x.GetNextPlayerToHarvestAsync())
                .ReturnsAsync(new PlayerHarvestInfo(puuid, null))
                .ReturnsAsync(new PlayerHarvestInfo("", null));

            _matchServiceMock.Setup(x => x.GetSetMatchIdsAsync("europe", puuid, It.IsAny<long>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<string> { "m1" });

            // Act - should exit automatically after processing all players
            await CreateHarvester(exitWhenIdle: true).RunAsync("europe", 16, CancellationToken.None);

            // Assert - player was harvested, then exited
            _harvestRepoMock.Verify(x => x.MarkPlayerAsHarvestedAsync(puuid), Times.Once);
            _harvestRepoMock.Verify(x => x.GetNextPlayerToHarvestAsync(), Times.Exactly(2));
        }
    }
}
