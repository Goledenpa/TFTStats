using Microsoft.Extensions.Logging;
using Moq;
using System.Data.Common;
using TFTStats.Core.Entities.Harvester;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories;
using Xunit;

namespace TFTStats.Tests.Core.Repositories
{
    public class HarvesterRepositoryTests
    {
        private readonly Mock<ISqlExecutor> _sqlExecutorMock;
        private readonly Mock<ILogger<HarvesterRepository>> _loggerMock;
        private readonly HarvesterRepository _repository;

        public HarvesterRepositoryTests()
        {
            _sqlExecutorMock = new Mock<ISqlExecutor>();
            _loggerMock = new Mock<ILogger<HarvesterRepository>>();
            _repository = new HarvesterRepository(_loggerMock.Object, _sqlExecutorMock.Object);
        }

        [Fact]
        public async Task GetNextPlayerToHarvestAsync_UsesCorrectOrderByAndLimit()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.QueryFirstOrDefaultAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<DbDataReader, PlayerHarvestInfo>>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Func<DbDataReader, PlayerHarvestInfo> _, Action<DbParameterCollection> __) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(new PlayerHarvestInfo("p1", null));

            // Act
            await _repository.GetNextPlayerToHarvestAsync();

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("ORDER BY", capturedSql);
            Assert.Contains("last_harvested_at", capturedSql);
            Assert.Contains("NULLS FIRST", capturedSql);
            Assert.Contains("LIMIT 1", capturedSql);
        }

        [Fact]
        public async Task UpsertMatchIdsAsync_UsesUnnestAndOnConflictDoNothing()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.QueryScalarAsync<long>(
                    It.IsAny<string>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Action<DbParameterCollection> _) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(2L);

            var matches = new List<MatchHarvestInfo>
            {
                new("match-1", 0, null, 16, null, 1),
                new("match-2", 0, null, 16, null, 1)
            };

            // Act
            var result = await _repository.UpsertMatchIdsAsync("p1", matches);

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("INSERT INTO staging_match_ids", capturedSql);
            Assert.Contains("patch_id", capturedSql);
            Assert.Contains("unnest", capturedSql);
            Assert.Contains("ON CONFLICT", capturedSql);
            Assert.Contains("DO NOTHING", capturedSql);
            Assert.Contains("RETURNING", capturedSql);
            Assert.Equal(2, result);
        }

        [Fact]
        public async Task UpsertMatchIdsAsync_EmptyList_DoesNotExecuteSql()
        {
            // Arrange
            var matches = new List<MatchHarvestInfo>();

            // Act
            var result = await _repository.UpsertMatchIdsAsync("p1", matches);

            // Assert - no SQL executed for empty lists
            _sqlExecutorMock.Verify(
                x => x.QueryScalarAsync<long>(It.IsAny<string>(), It.IsAny<Action<DbParameterCollection>>()),
                Times.Never);
            Assert.Equal(0, result);
        }

        [Fact]
        public async Task MarkPlayerAsHarvestedAsync_UsesUpdateWithTimestamp()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Action<DbParameterCollection> _) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(1);

            // Act
            await _repository.MarkPlayerAsHarvestedAsync("test-puuid");

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("UPDATE player", capturedSql);
            Assert.Contains("last_harvested_at", capturedSql);
            Assert.Contains("CURRENT_TIMESTAMP", capturedSql);
            Assert.Contains("@puuid", capturedSql);
        }

        [Fact]
        public async Task GetPendingMatchCountAsync_UsesCountWhereCrawledAtIsNull()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.QueryScalarAsync<object>(
                    It.IsAny<string>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Action<DbParameterCollection> _) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(42L);

            // Act
            var result = await _repository.GetPendingMatchCountAsync();

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("COUNT", capturedSql);
            Assert.Contains("staging_match_ids", capturedSql);
            Assert.Contains("crawled_at IS NULL", capturedSql);
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task GetRemainingPlayerCountAsync_ReturnsCorrectCount()
        {
            // Arrange
            _sqlExecutorMock
                .Setup(x => x.QueryScalarAsync<object>(It.IsAny<string>(), It.IsAny<Action<DbParameterCollection>>()))
                .ReturnsAsync(42L);

            // Act
            var result = await _repository.GetRemainingPlayerCountAsync();

            // Assert
            Assert.Equal(42, result);
        }

        [Fact]
        public async Task GetRemainingPlayerCountAsync_UsesCorrectSqlQuery()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.QueryScalarAsync<object>(It.IsAny<string>(), It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Action<DbParameterCollection> _) => capturedSql = sql)
                .ReturnsAsync(0L);

            // Act
            await _repository.GetRemainingPlayerCountAsync();

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Equal("SELECT COUNT(*) FROM player WHERE last_harvested_at IS NULL", capturedSql);
        }
    }
}
