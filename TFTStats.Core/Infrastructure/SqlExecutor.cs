using System.Data;
using System.Data.Common;

namespace TFTStats.Core.Infrastructure
{
    public class SqlExecutor : ISqlExecutor
    {
        private readonly string _connectionString;
        private readonly DbProviderFactory _factory;

        public SqlExecutor(string connectionString, DbProviderFactory factory)
        {
            _connectionString = connectionString;
            _factory = factory;
        }

        private async Task<DbConnection> CreateConnectionAsync()
        {
            var conn = _factory.CreateConnection() ?? throw new InvalidOperationException("Database provider failed to create a connection.");

            conn.ConnectionString = _connectionString;
            await conn.OpenAsync();
            return conn;
        }

        public async Task<int> ExecuteAsync(string query, Action<DbParameterCollection>? parameters = null)
        {
            using var conn = await CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            parameters?.Invoke(cmd.Parameters);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<List<T>> QueryAsync<T>(string query, Func<DbDataReader, T> mapper, Action<DbParameterCollection>? paramters = null)
        {
            using var conn = await CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            paramters?.Invoke(cmd.Parameters);

            var res = new List<T>();
            
            using var reader = await cmd.ExecuteReaderAsync();
            while(await reader.ReadAsync())
            {
                res.Add(mapper(reader));
            }

            return res;
        }

        public async Task<T?> QueryScalarAsync<T>(string query, Action<DbParameterCollection>? parameters = null)
        {
            using var conn = await CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            parameters?.Invoke(cmd.Parameters);

            var result = await cmd.ExecuteScalarAsync();
            if (result == null || result == DBNull.Value) return default;
            return (T)result;
        }

        public async Task<T?> QueryFirstOrDefaultAsync<T>(string query, Func<DbDataReader, T> mapper, Action<DbParameterCollection>? parameters = null)
        {
            using var conn = await CreateConnectionAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
            parameters?.Invoke(cmd.Parameters);

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow);
            if (await reader.ReadAsync())
            {
                return mapper(reader);
            }
            return default;
        }

        public DbParameter CreateParameter(string name, object? value)
        {
            var param = _factory.CreateParameter() ?? throw new InvalidOperationException("Provider failed to create parameter.");

            param.ParameterName = name;
            param.Value = value ?? DBNull.Value;
            return param;
        }

    }
}
