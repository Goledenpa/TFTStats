using Microsoft.Extensions.Logging;
using Moq;
using System.Data.Common;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories;
using Xunit;

namespace TFTStats.Tests.Core.Repositories
{
    public class MatchRepositoryTests
    {
        private readonly Mock<ISqlExecutor> _sqlExecutorMock;
        private readonly Mock<ILogger<MatchRepository>> _loggerMock;
        private readonly MatchRepository _repository;

        public MatchRepositoryTests()
        {
            _sqlExecutorMock = new Mock<ISqlExecutor>();
            _loggerMock = new Mock<ILogger<MatchRepository>>();
            // Note: constructor order is (ISqlExecutor, ILogger)
            _repository = new MatchRepository(_sqlExecutorMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task GetNextPendingMatchIdsAsync_UsesOrderByAndLimit()
        {
            // Arrange
            string? capturedSql = null;
            _sqlExecutorMock
                .Setup(x => x.QueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<DbDataReader, string>>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Func<DbDataReader, string> _, Action<DbParameterCollection> __) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(new List<string> { "m1", "m2" });

            // Act
            await _repository.GetNextPendingMatchIdsAsync(50);

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("staging_match_ids", capturedSql);
            Assert.Contains("crawled_at IS NULL", capturedSql);
            Assert.Contains("ORDER BY", capturedSql);
            Assert.Contains("game_datetime", capturedSql);
            Assert.Contains("LIMIT", capturedSql);
        }

        [Fact]
        public async Task MarkMatchesAsCrawledAsync_UsesAnyForBatchUpdate()
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

            var matchIds = new List<string> { "match-a", "match-b", "match-c" };

            // Act
            await _repository.MarkMatchesAsCrawledAsync(matchIds);

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("UPDATE staging_match_ids", capturedSql);
            Assert.Contains("crawled_at = CURRENT_TIMESTAMP", capturedSql);
            Assert.Contains("ANY(", capturedSql);
            Assert.Contains("@ids", capturedSql);
        }

        [Fact]
        public async Task MarkMatchesAsCrawledAsync_EmptyList_DoesNotExecuteSql()
        {
            // Arrange
            var matchIds = new List<string>();

            // Act
            await _repository.MarkMatchesAsCrawledAsync(matchIds);

            // Assert - ExecuteAsync should never be called for empty lists
            _sqlExecutorMock.Verify(
                x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<Action<DbParameterCollection>>()),
                Times.Never);
        }

        [Fact]
        public async Task FilterNewMatchIdsAsync_ReturnsOnlyIdsNotInDatabase()
        {
            // Arrange
            string? capturedSql = null;
            var allIds = new List<string> { "new-1", "new-2", "existing-1" };

            // DB returns only the existing ones
            _sqlExecutorMock
                .Setup(x => x.QueryAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<DbDataReader, string>>(),
                    It.IsAny<Action<DbParameterCollection>>()))
                .Callback((string sql, Func<DbDataReader, string> _, Action<DbParameterCollection> __) =>
                {
                    capturedSql = sql;
                })
                .ReturnsAsync(new List<string> { "existing-1" });

            // Act
            var result = await _repository.FilterNewMatchIdsAsync(allIds);

            // Assert
            Assert.NotNull(capturedSql);
            Assert.Contains("match_id", capturedSql);
            Assert.Contains("ANY(", capturedSql);
            Assert.Contains("@ids", capturedSql);

            // Should return only the IDs that are NOT in the database
            Assert.Equal(2, result.Count);
            Assert.Contains("new-1", result);
            Assert.Contains("new-2", result);
            Assert.DoesNotContain("existing-1", result);
        }

        [Fact]
        public async Task FilterNewMatchIdsAsync_EmptyInput_ReturnsEmpty()
        {
            // Arrange
            var emptyIds = new List<string>();

            // Act
            var result = await _repository.FilterNewMatchIdsAsync(emptyIds);

            // Assert - no SQL executed, empty result
            _sqlExecutorMock.Verify(
                x => x.QueryAsync(It.IsAny<string>(), It.IsAny<Func<DbDataReader, string>>(), It.IsAny<Action<DbParameterCollection>>()),
                Times.Never);
            Assert.Empty(result);
        }
    }
}
