using Microsoft.Extensions.Logging;
using Moq;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Infrastructure.Importers.Interfaces;
using TFTStats.Core.Models;
using TFTStats.Core.Repositories.Interfaces;
using TFTStats.Core.Service;
using Xunit;
using Crawler = TFTStats.Presentation.Crawler;
using CoreMatch = TFTStats.Core.Entities.Match;

namespace TFTStats.Tests.Presentation
{
    public class CrawlerTests
    {
        private readonly Mock<RiotTFTMatchService> _matchServiceMock;
        private readonly Mock<IMatchRepository> _matchRepoMock;
        private readonly Mock<IHarvesterRepository> _harvesterRepoMock;
        private readonly Mock<IRiotDataImporter> _importerMock;
        private readonly Mock<ILogger<Crawler>> _loggerMock;

        public CrawlerTests()
        {
            _matchServiceMock = new Mock<RiotTFTMatchService>(
                new RiotApiClient(new System.Net.Http.HttpClient()),
                Mock.Of<ILogger<RiotTFTMatchService>>()) { CallBase = false };
            _matchRepoMock = new Mock<IMatchRepository>();
            _harvesterRepoMock = new Mock<IHarvesterRepository>();
            _importerMock = new Mock<IRiotDataImporter>();
            _loggerMock = new Mock<ILogger<Crawler>>();

            // Default: FilterNewMatchIdsAsync returns all IDs (none are duplicates)
            _matchRepoMock.Setup(x => x.FilterNewMatchIdsAsync(It.IsAny<List<string>>()))
                .ReturnsAsync((List<string> ids) => ids);

            // Default: GetPendingMatchCountCachedAsync returns 0
            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync())
                .ReturnsAsync(0);
        }

        private Crawler CreateCrawler(int delayMs = 0) => new(
            _matchServiceMock.Object,
            _matchRepoMock.Object,
            _harvesterRepoMock.Object,
            _importerMock.Object,
            _loggerMock.Object,
            crawlerCheckDelayMs: delayMs,
            errorRetryDelayMs: delayMs);

        private static RiotTFTMatch CreateMatch(string matchId, int participantCount)
        {
            var participants = new List<RiotTFTParticipant>();
            for (int i = 0; i < participantCount; i++)
            {
                participants.Add(new RiotTFTParticipant(
                    Placement: i + 1,
                    Level: 7,
                    GoldLeft: 10,
                    LastRound: 10,
                    TimeEliminated: 600,
                    PlayersEliminated: 0,
                    TotalDamageToPlayers: 100,
                    Puuid: $"puuid-{i}",
                    Companion: new RiotCompanion("companion-1", 0, "species-1"),
                    Traits: new List<RiotTFTTrait>(),
                    Units: new List<RiotTFTUnit>(),
                    RiotGameName: $"Player{i}",
                    RiotTagline: "EUW"
                ));
            }

            return new RiotTFTMatch(
                Metadata: new RiotTFTMatchMetadata(
                    DataVersion: "1",
                    MatchId: matchId,
                    Participants: new List<string> { "p1" }),
                Info: new RiotTFTMatchInfo(
                    GameCreation: 1000,
                    GameDatetime: 1700000000000,
                    GameLength: 1200,
                    GameVersion: "1.0",
                    Participants: participants,
                    TftSetNumber: 16,
                    QueueId: 1100)
            );
        }

        [Fact]
        public async Task RunAsync_HappyPath_FetchesDetails_Imports_MarksCrawled()
        {
            // Arrange
            var matchIds = new List<string> { "match-1", "match-2" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-1", 2));
            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-2", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-2", 1));

            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync()).ReturnsAsync(10);

            var cts = new CancellationTokenSource();
            _matchRepoMock.Setup(x => x.MarkMatchesAsCrawledAsync(matchIds))
                .Callback(() => cts.Cancel());

            // Act
            await CreateCrawler().RunAsync("europe", cts.Token);

            // Assert
            _importerMock.Verify(
                x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Once);
        }

        [Fact]
        public async Task RunAsync_MatchReturnsNull_SkipsImport_MarksCrawled()
        {
            // Arrange
            var matchIds = new List<string> { "match-missing" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            // 404 returns null
            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-missing", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RiotTFTMatch?)null);

            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync()).ReturnsAsync(0);

            var cts = new CancellationTokenSource();
            _matchRepoMock.Setup(x => x.MarkMatchesAsCrawledAsync(matchIds))
                .Callback(() => cts.Cancel());

            // Act
            await CreateCrawler().RunAsync("europe", cts.Token);

            // Assert - no import since match was null
            _importerMock.Verify(
                x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Once);
        }

        [Fact]
        public async Task RunAsync_MatchHasZeroParticipants_SkipsImport_MarksCrawled()
        {
            // Arrange
            var matchIds = new List<string> { "match-empty" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-empty", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-empty", 0));

            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync()).ReturnsAsync(0);

            var cts = new CancellationTokenSource();
            _matchRepoMock.Setup(x => x.MarkMatchesAsCrawledAsync(matchIds))
                .Callback(() => cts.Cancel());

            // Act
            await CreateCrawler().RunAsync("europe", cts.Token);

            // Assert - match has 0 participants, so it's skipped for import
            _importerMock.Verify(
                x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()),
                Times.Never);
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Once);
        }

        [Fact]
        public async Task RunAsync_ErrorRecovers_ThenContinues()
        {
            // Arrange
            var matchIds = new List<string> { "match-ok" };

            // First call throws, subsequent calls return data
            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ThrowsAsync(new Exception("Network timeout"))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-ok", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-ok", 1));

            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync()).ReturnsAsync(5);

            var cts = new CancellationTokenSource();
            _matchRepoMock.Setup(x => x.MarkMatchesAsCrawledAsync(matchIds))
                .Callback(() => cts.Cancel());

            // Act
            await CreateCrawler(delayMs: 1).RunAsync("europe", cts.Token);

            // Assert - despite initial error, processing eventually succeeded
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Once);
        }

        [Fact]
        public async Task RunAsync_Cancellation_ExitsGracefully()
        {
            // Arrange
            _matchRepoMock.Setup(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(new List<string>());

            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act - should return immediately since already cancelled
            await CreateCrawler().RunAsync("europe", cts.Token);

            // Assert - no processing should have occurred
            _matchRepoMock.Verify(
                x => x.MarkMatchesAsCrawledAsync(It.IsAny<List<string>>()),
                Times.Never);
            _importerMock.Verify(
                x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Fact]
        public async Task RunAsync_ImportThrows_MatchesNotMarkedCrawled()
        {
            // Arrange
            var matchIds = new List<string> { "match-1" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-1", 2));

            // Importer throws
            _importerMock.Setup(x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new Exception("Import failed"));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(200);

            // Act
            await CreateCrawler(delayMs: 1).RunAsync("europe", cts.Token);

            // Assert - matches NOT marked crawled because import failed
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Never);
        }

        [Fact]
        public async Task RunAsync_PartialBatch_SomeNullSomeValid_ImportsValidOnes()
        {
            // Arrange
            var matchIds = new List<string> { "match-null", "match-ok", "match-empty" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-null", It.IsAny<CancellationToken>()))
                .ReturnsAsync((RiotTFTMatch?)null);
            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-ok", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-ok", 2));
            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-empty", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-empty", 0));

            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync()).ReturnsAsync(3);

            var cts = new CancellationTokenSource();
            _matchRepoMock.Setup(x => x.MarkMatchesAsCrawledAsync(matchIds))
                .Callback(() => cts.Cancel());

            // Act
            await CreateCrawler().RunAsync("europe", cts.Token);

            // Assert - only the valid match was imported
            _importerMock.Verify(
                x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()),
                Times.Once);
            // All 3 are still marked crawled (null/empty ones are skipped for import but marked)
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Once);
        }

        [Fact]
        public async Task RunAsync_MarkCrawledThrows_ErrorIsCaughtAndRetried()
        {
            // Arrange
            var matchIds = new List<string> { "match-1" };

            _matchRepoMock.SetupSequence(x => x.GetNextPendingMatchIdsAsync(50))
                .ReturnsAsync(matchIds)
                .ReturnsAsync(matchIds)
                .ReturnsAsync(new List<string>());

            _matchServiceMock.Setup(x => x.GetMatch("europe", "match-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync(CreateMatch("match-1", 1));

            _importerMock.Setup(x => x.ImportMatchStreamAsync(It.IsAny<IAsyncEnumerable<CoreMatch>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // First MarkMatchesAsCrawledAsync throws, second succeeds
            _matchRepoMock.SetupSequence(x => x.MarkMatchesAsCrawledAsync(It.IsAny<List<string>>()))
                .ThrowsAsync(new Exception("Update failed"))
                .Returns(Task.CompletedTask);

            var cts = new CancellationTokenSource();
            _harvesterRepoMock.Setup(x => x.GetPendingMatchCountCachedAsync())
                .Callback(() => cts.CancelAfter(50));

            // Act
            await CreateCrawler(delayMs: 1).RunAsync("europe", cts.Token);

            // Assert - retried after error, eventually succeeded
            _matchRepoMock.Verify(x => x.MarkMatchesAsCrawledAsync(matchIds), Times.Exactly(2));
        }
    }
}
