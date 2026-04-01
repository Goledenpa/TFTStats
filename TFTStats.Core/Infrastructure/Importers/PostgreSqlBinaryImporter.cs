using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using TFTStats.Core.Entities;
using TFTStats.Core.Infrastructure.Importers.Interfaces;
using TFTStats.Core.Service.Cache;
using static NpgsqlTypes.NpgsqlDbType;

namespace TFTStats.Core.Infrastructure.Importers
{
    public class PostgreSqlBinaryImporter : IRiotDataImporter
    {
        private readonly string _connectionString;
        private readonly SqlExecutor _sqlExecutor;
        private readonly UnitCacheService _unitCache;
        private readonly ItemCacheService _itemCache;
        private readonly TraitCacheService _traitCache;
        private readonly ILogger<PostgreSqlBinaryImporter> _logger;

        public PostgreSqlBinaryImporter(string connectionString, SqlExecutor sqlExecutor, UnitCacheService cache, ItemCacheService itemCache, TraitCacheService traitCache, ILogger<PostgreSqlBinaryImporter> logger)
        {
            _connectionString = connectionString;
            _sqlExecutor = sqlExecutor;
            _unitCache = cache;
            _itemCache = itemCache;
            _traitCache = traitCache;
            _logger = logger;
        }

        public async Task ImportMatchStreamAsync(IAsyncEnumerable<Match> matchStream, CancellationToken ct = default)
        {
            using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            await InitializeCache(conn);

            var batch = new List<Match>();
            var enumerator = matchStream.GetAsyncEnumerator(ct);

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    var moveNextTask = enumerator.MoveNextAsync().AsTask();
                    var timeoutTask = Task.Delay(2000, ct);
                    var completedTask = await Task.WhenAny(moveNextTask, timeoutTask);

                    if (completedTask == moveNextTask)
                    {
                        if (!await moveNextTask) break;

                        batch.Add(enumerator.Current);

                        if (batch.Count >= 100)
                        {
                            await ExecuteBinaryCopyBatch(conn, batch, ct);
                            batch.Clear();
                        }
                    }
                    else
                    {
                        ct.ThrowIfCancellationRequested();

                        if (batch.Count > 0)
                        {
                            Console.WriteLine($"\n[System] Ingestion paused (API Cooldown). Flushing {batch.Count} matches to DB...");
                            await ExecuteBinaryCopyBatch(conn, batch, ct);
                            batch.Clear();
                        }

                        if (!await moveNextTask) break;
                        batch.Add(enumerator.Current);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[Importer] Ingestion cancelled by user. Discarding partial batch...");
                throw;
            }
            finally
            {
                if (batch.Count != 0 && !ct.IsCancellationRequested)
                {
                    await ExecuteBinaryCopyBatch(conn, batch, CancellationToken.None);
                }
                try
                {
                    await enumerator.DisposeAsync();
                }
                catch (NotSupportedException) { }
            }
        }

        private async Task InitializeCache(NpgsqlConnection conn)
        {
            await InitializeItemCache();
            await InitializeUnitCache();
            await InitializeTraitCache();
        }

        private async Task InitializeItemCache()
        {
            var dbItems = await _sqlExecutor.QueryAsync(
                "SELECT id, name FROM item_reference",
                 r => (
                     r.GetString(1),
                     r.GetInt32(0)
                 ));

            _itemCache.Initialize(dbItems);
        }

        private async Task InitializeUnitCache()
        {
            var dbUnits = await _sqlExecutor.QueryAsync(
                "SELECT id, character_id, tier, rarity FROM unit_reference",
                 r => new UnitReference
                 {
                     Id = r.GetInt32(0),
                     CharacterId = r.GetString(1),
                     Tier = r.GetInt32(2),
                     Rarity = r.GetInt32(3)
                 });

            _unitCache.Initialize(dbUnits);
        }

        private async Task InitializeTraitCache()
        {
            var dbTraits = await _sqlExecutor.QueryAsync(
                "SELECT id, name FROM trait_reference",
                 r => (
                     r.GetString(1),
                     r.GetInt32(0)
                 ));

            _traitCache.Initialize(dbTraits);
        }

        private async Task ExecuteBinaryCopyBatch(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            await HandleNewUnits(conn, batch);
            await HandleNewItems(conn, batch);
            await HandleNewTraits(conn, batch);

            using var transaction = await conn.BeginTransactionAsync(ct);

            try
            {
                // 1. Get unique players
                await UpsertPlayersFromBatch(conn, batch, ct);

                // 2. match Table
                await CopyMatches(conn, batch, ct);

                // 3. participant Table
                await CopyParticipants(conn, batch, ct);

                // 4. participant_unit table
                await CopyUnits(conn, batch, ct);

                // 5. participant_trait table
                await CopyTraits(conn, batch, ct);

                await transaction.CommitAsync(ct);

                _logger.LogInformation("Batch Success: {Count} matches ingested into all tables.", batch.Count);

                var memoryMb = GC.GetTotalMemory(false) / 1_000_000;
                _logger.LogInformation("[Memory] Managed head: {memoryMb} MB", memoryMb);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                _logger.LogError(ex, "Transaction Failed: Batch of {Count} matches rolled back.", batch.Count);
                throw;
            }
        }

        private async Task HandleNewTraits(NpgsqlConnection conn, List<Match> batch)
        {
            var newTraits = batch.SelectMany(x => x.Participants)
                                .SelectMany(x => x.Traits)
                                .Where(x => _traitCache.GetId(x.TraitName) is null)
                                .Select(x => x.TraitName)
                                .Distinct().ToList();

            if (newTraits.Count == 0) return;

            foreach (var trait in newTraits)
            {
                const string query = @"
                    INSERT INTO trait_reference(name) 
                    VALUES(@name) 
                    ON CONFLICT(name) DO UPDATE
                    SET name = EXCLUDED.name
                    RETURNING id
                ";

                var id = await _sqlExecutor.QueryScalarAsync<int?>(query,
                    p =>
                    {
                        p.Add(_sqlExecutor.CreateParameter("name", trait));
                    }
                );

                if (id.HasValue)
                {
                    _traitCache.Add(trait, id.Value);
                }
            }
        }

        private async Task HandleNewItems(NpgsqlConnection conn, List<Match> batch)
        {
            var newItems = batch.SelectMany(x => x.Participants)
                                .SelectMany(x => x.Units)
                                .SelectMany(x => x.Items)
                                .Where(x => _itemCache.GetId(x) is null)
                                .Distinct().ToList();

            if (newItems.Count == 0) return;

            foreach (var name in newItems)
            {
                const string query = @"
                    INSERT INTO item_reference(name) 
                    VALUES(@name) 
                    ON CONFLICT(name) DO UPDATE
                    SET name = EXCLUDED.name
                    RETURNING id
                ";

                var id = await _sqlExecutor.QueryScalarAsync<int?>(query,
                    p =>
                    {
                        p.Add(_sqlExecutor.CreateParameter("name", name));
                    }
                );

                if (id.HasValue)
                {
                    _itemCache.Add(name, id.Value);
                }
            }
        }

        private async Task HandleNewUnits(NpgsqlConnection conn, List<Match> batch)
        {
            var newUnits = batch.SelectMany(x => x.Participants)
                                .SelectMany(x => x.Units)
                                .Where(u => _unitCache.GetId(u.CharacterId, u.Tier, u.Rarity) is null)
                                .GroupBy(u => new { u.CharacterId, u.Tier, u.Rarity })
                                .Select(x => x.First())
                                .ToList();

            if (newUnits.Count == 0) return;

            foreach (var u in newUnits)
            {
                const string query = @"
                    INSERT INTO unit_reference(character_id, tier, rarity)
                    VALUES (@characterId, @tier, @rarity)
                    ON CONFLICT (character_id, tier, rarity) DO UPDATE
                    SET character_id = EXCLUDED.character_id
                    RETURNING id
                ";

                var id = await _sqlExecutor.QueryScalarAsync<int?>(query,
                    p =>
                    {
                        p.Add(_sqlExecutor.CreateParameter("characterId", u.CharacterId));
                        p.Add(_sqlExecutor.CreateParameter("tier", u.Tier));
                        p.Add(_sqlExecutor.CreateParameter("rarity", u.Rarity));
                    }
                );

                if (id.HasValue)
                {
                    _unitCache.Add(u.CharacterId, u.Tier, u.Rarity, id.Value);
                }
            }
        }

        private async Task CopyTraits(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            var allTraits = batch.SelectMany(m => m.Participants).SelectMany(p => p.Traits).ToList();
            int totalRows = allTraits.Count;
            int current = 0;

            using var writer = await conn.BeginBinaryImportAsync(
                                "COPY participant_trait (participant_id, trait_ref_id, num_units, tier_current, tier_total) FROM STDIN (FORMAT BINARY)", ct);
            foreach (var m in batch)
            {
                foreach (var p in m.Participants)
                {
                    foreach (var t in p.Traits)
                    {
                        int? traitId = _traitCache.GetId(t.TraitName);

                        if (traitId.HasValue)
                        {
                            await writer.StartRowAsync(ct);
                            await writer.WriteAsync(p.Id, Varchar, ct);
                            await writer.WriteAsync(traitId, Integer, ct);
                            await writer.WriteAsync((short)t.NumUnits, Smallint, ct);
                            await writer.WriteAsync((short)t.TierCurrent, Smallint, ct);
                            await writer.WriteAsync((short)t.TierTotal, Smallint, ct);

                            current++;
                            ReportProgress("Traits", current, totalRows, ConsoleColor.Magenta);
                        }
                        else
                        {
                            _logger.LogWarning("Trait {traitName} not found in cache!", t.TraitName);
                        }
                    }
                }
            }
            await writer.CompleteAsync(ct);
        }

        private async Task CopyUnits(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            var allUnits = batch.SelectMany(m => m.Participants).SelectMany(p => p.Units).ToList();
            int totalRows = allUnits.Count;
            int current = 0;

            using var writer = await conn.BeginBinaryImportAsync(
                                "COPY participant_unit (participant_id, unit_ref_id, item_ids) FROM STDIN (FORMAT BINARY)", ct);

            foreach (var m in batch)
            {
                foreach (var p in m.Participants)
                {
                    foreach (var u in p.Units)
                    {
                        int? unitId = _unitCache.GetId(u.CharacterId, u.Tier, u.Rarity);

                        int[] itemIds = u.Items
                            .Select(x => _itemCache.GetId(x))
                            .Where(x => x.HasValue)
                            .Select(x => x!.Value)
                            .ToArray();

                        if (unitId.HasValue)
                        {
                            await writer.StartRowAsync(ct);
                            await writer.WriteAsync(p.Id, Varchar, ct);
                            await writer.WriteAsync(unitId.Value, Integer, ct);
                            await writer.WriteAsync(itemIds, NpgsqlDbType.Array | Integer, ct);

                            current++;
                            ReportProgress("Units", current, totalRows, ConsoleColor.Green);
                        }
                        else
                        {
                            _logger.LogWarning("Unit {characterId} {tier}* not found in cache!", u.CharacterId, u.Tier);
                        }
                    }
                }
            }
            await writer.CompleteAsync(ct);
        }

        private async Task CopyParticipants(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            int totalRows = batch.SelectMany(m => m.Participants).Count();
            int current = 0;

            using var writer = await conn.BeginBinaryImportAsync(
                                @"COPY participant(id, match_id, puuid, placement, level, gold_left, last_round,
                    time_eliminated, players_eliminated, total_damage_to_players, companion_species, companion_skin_id)
                    FROM STDIN (FORMAT BINARY)", ct);
            foreach (var match in batch)
            {
                foreach (var p in match.Participants)
                {
                    await writer.StartRowAsync(ct);
                    await writer.WriteAsync(p.Id, Varchar, ct);
                    await writer.WriteAsync(p.MatchId, Varchar, ct);
                    await writer.WriteAsync(p.Puuid, Varchar, ct);
                    await writer.WriteAsync(p.Placement, Integer, ct);
                    await writer.WriteAsync(p.Level, Integer, ct);
                    await writer.WriteAsync(p.GoldLeft, Integer, ct);
                    await writer.WriteAsync(p.LastRound, Integer, ct);
                    await writer.WriteAsync(p.TimeEliminated, NpgsqlDbType.Double, ct);
                    await writer.WriteAsync(p.PlayersEliminated, Integer, ct);
                    await writer.WriteAsync(p.TotalDamageToPlayers, Integer, ct);
                    if (string.IsNullOrEmpty(p.CompanionSpecies))
                        await writer.WriteNullAsync(ct);
                    else
                        await writer.WriteAsync(p.CompanionSpecies, Varchar, ct);
                    await writer.WriteAsync(p.CompanionSkinId, Integer, ct);

                    current++;
                    ReportProgress("Participants", current, totalRows, ConsoleColor.Yellow);
                }
            }

            await writer.CompleteAsync(ct);
        }

        private async Task CopyMatches(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            int count = 0;

            using var writer = await conn.BeginBinaryImportAsync(
                                "COPY match(match_id, queue_id, game_datetime, game_length, game_creation, game_version, set_number) FROM STDIN (FORMAT BINARY)", ct);

            foreach (var m in batch)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(m.MatchId, Varchar, ct);
                await writer.WriteAsync(m.QueueId, Integer, ct);
                await writer.WriteAsync(m.GameDateTime, TimestampTz, ct);
                await writer.WriteAsync(m.GameLength, NpgsqlDbType.Double, ct);
                await writer.WriteAsync(m.GameCreation, Bigint, ct);
                await writer.WriteAsync(m.GameVersion, Varchar, ct);
                await writer.WriteAsync(m.SetNumber, Integer, ct);
                count++;
                ReportProgress("Matches", count, batch.Count, ConsoleColor.Cyan);
            }
            await writer.CompleteAsync(ct);
        }

        private static async Task UpsertPlayersFromBatch(NpgsqlConnection conn, List<Match> batch, CancellationToken ct)
        {
            Dictionary<string, (string Name, string Tag)> uniquePlayers = [];

            foreach (var match in batch)
            {
                foreach (var p in match.Participants)
                {
                    uniquePlayers[p.Puuid] = (p.RiotGameName, p.RiotTagLine);
                }
            }

            // 2. Truncate staging

            using (var cmd = new NpgsqlCommand("TRUNCATE staging_player", conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }


            // 3 Binary copy into staging
            // We stream all the players from the matchs to the staging table.
            using (var writer = await conn.BeginBinaryImportAsync(
                "COPY staging_player(puuid, game_name, tagline) FROM STDIN (FORMAT BINARY)", ct))
            {
                foreach (var kvp in uniquePlayers)
                {
                    await writer.StartRowAsync(ct);
                    await writer.WriteAsync(kvp.Key, Varchar, ct);
                    await writer.WriteAsync(kvp.Value.Name, Varchar, ct);
                    await writer.WriteAsync(kvp.Value.Tag, Varchar, ct);
                }

                await writer.CompleteAsync(ct);
            }

            // 4. Internal upsert

            var upsertSQL = @"
                INSERT INTO player(puuid, game_name, tagline)
                SELECT puuid, game_name, tagline FROM staging_player
                ON CONFLICT (puuid) DO UPDATE
                SET game_name = EXCLUDED.game_name,
                    tagline = EXCLUDED.tagline";

            using (var cmd = new NpgsqlCommand(upsertSQL, conn))
            {
                await cmd.ExecuteNonQueryAsync(ct);
            }
        }

        private void ReportProgress(string table, int current, int total, ConsoleColor color)
        {
            if (current % 50 == 0 || current == total)
            {
                double percent = total > 0 ? (current / (double)total) * 100 : 0;

                Console.ForegroundColor = color;

                Console.Write($"\r[Ingestion] {table,-12} | {current,5} / {total,-5} | {percent,3:F0}% completed...      ");
                Console.ResetColor();

                if (current == total)
                {
                    Console.WriteLine(" [OK]");
                }
            }
        }
    }
}
