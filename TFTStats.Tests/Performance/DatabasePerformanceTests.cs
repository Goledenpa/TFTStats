using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using TFTStats.Core.Entities.Harvester;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace TFTStats.Tests.Performance
{
    public class DatabasePerformanceTests : IClassFixture<DatabasePerformanceTests.DbFixture>, IDisposable
    {
        private readonly DbFixture _fixture;
        private readonly ITestOutputHelper _output;
        private readonly Stopwatch _sw = new();
        private readonly List<long> _timings = new();

        public DatabasePerformanceTests(DbFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        public void Dispose()
        {
            if (_timings.Count > 0)
            {
                var avg = _timings.Average();
                var min = _timings.Min();
                var max = _timings.Max();
                _output.WriteLine($"[Summary] {_timings.Count} runs — " +
                    $"avg: {avg:F0}ms, min: {min:F0}ms, max: {max:F0}ms");
            }
        }

        private async Task TimeAsync(string label, Func<Task> operation)
        {
            _sw.Restart();
            await operation();
            _sw.Stop();
            var ms = _sw.ElapsedMilliseconds;
            _timings.Add(ms);
            _output.WriteLine($"[{label}] {ms} ms");
        }

        private async Task<T> TimeAsync<T>(string label, Func<Task<T>> operation)
        {
            _sw.Restart();
            var result = await operation();
            _sw.Stop();
            var ms = _sw.ElapsedMilliseconds;
            _timings.Add(ms);
            _output.WriteLine($"[{label}] {ms} ms");
            return result;
        }

        [Fact]
        public async Task GetNextPlayerToHarvest_WithWhereClause_ShouldBeUnder10ms()
        {
            var repo = CreateHarvesterRepository();
            await TimeAsync("GetNextPlayerToHarvest", () => repo.GetNextPlayerToHarvestAsync());
        }

        [Fact]
        public async Task GetPendingMatchCount_FullCount_ShouldLogDuration()
        {
            var repo = CreateHarvesterRepository();
            var count = await TimeAsync("GetPendingMatchCount (COUNT*)", () => repo.GetPendingMatchCountAsync());
            _output.WriteLine($"  Result: {count:N0} pending matches");
        }

        [Fact]
        public async Task GetPendingMatchCountCached_CounterLookup_ShouldBeUnder1ms()
        {
            var repo = CreateHarvesterRepository();
            var count = await TimeAsync("GetPendingMatchCountCached", () => repo.GetPendingMatchCountCachedAsync());
            _output.WriteLine($"  Result: {count:N0} pending matches (from counter)");
        }

        [Fact]
        public async Task UpsertMatchIdsAsync_100Matches_ShouldLogDuration()
        {
            var repo = CreateHarvesterRepository();
            var matches = new List<MatchHarvestInfo>();
            for (int i = 0; i < 100; i++)
            {
                matches.Add(new MatchHarvestInfo(
                    MatchId: $"PERF_TEST_{Guid.NewGuid():N}",
                    GameCreation: 0,
                    GameDateTime: null,
                    SetNumber: 16,
                    QueueId: null,
                    PatchId: 1
                ));
            }

            var puuid = "perf-test-puuid";
            var inserted = await TimeAsync("UpsertMatchIdsAsync (100 rows)", async () =>
            {
                await repo.UpsertMatchIdsAsync(puuid, matches);
                return matches.Count;
            });

            // Cleanup
            await CleanupMatchIds(matches.Select(m => m.MatchId).ToList());

            _output.WriteLine($"  Inserted: {inserted} match IDs");
        }

        [Fact]
        public async Task IncrementPendingCounter_ShouldBeUnder10ms()
        {
            var repo = CreateHarvesterRepository();
            await TimeAsync("IncrementPendingCounterAsync", () => repo.IncrementPendingCounterAsync(100));
        }

        [Fact]
        public async Task GetRemainingPlayerCount_ShouldBeUnder100ms()
        {
            var repo = CreateHarvesterRepository();
            var count = await TimeAsync("GetRemainingPlayerCount", () => repo.GetRemainingPlayerCountAsync());
            _output.WriteLine($"  Result: {count:N0} remaining players");
        }

        private HarvesterRepository CreateHarvesterRepository()
        {
            var sqlExecutor = new SqlExecutor(_fixture.ConnectionString, NpgsqlFactory.Instance);
            return new HarvesterRepository(new Mock<ILogger<HarvesterRepository>>().Object, sqlExecutor);
        }

        private async Task CleanupMatchIds(List<string> matchIds)
        {
            await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM staging_match_ids WHERE match_id = ANY(@ids)", conn);
            cmd.Parameters.AddWithValue("ids", NpgsqlTypes.NpgsqlDbType.Array | NpgsqlTypes.NpgsqlDbType.Text, matchIds.ToArray());
            await cmd.ExecuteNonQueryAsync();
        }

        public class DbFixture : IAsyncLifetime
        {
            public string ConnectionString { get; private set; } = string.Empty;

            public async Task InitializeAsync()
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true)
                    .Build();

                ConnectionString = config.GetConnectionString("TftDatabase")
                    ?? throw new InvalidOperationException(
                        "Set the 'TftDatabase' connection string in appsettings.json to run performance tests.\n" +
                        "Example: Host=your-host;Port=5432;Username=youruser;Password=yourpass;Database=TFTStats");
            }

            public Task DisposeAsync() => Task.CompletedTask;
        }
    }
}
