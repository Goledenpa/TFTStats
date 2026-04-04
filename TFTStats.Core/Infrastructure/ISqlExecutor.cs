using System.Data.Common;

namespace TFTStats.Core.Infrastructure
{
    public interface ISqlExecutor
    {
        Task<int> ExecuteAsync(string query, Action<DbParameterCollection>? parameters = null);
        Task<List<T>> QueryAsync<T>(string query, Func<DbDataReader, T> mapper, Action<DbParameterCollection>? parameters = null);
        Task<T?> QueryScalarAsync<T>(string query, Action<DbParameterCollection>? parameters = null);
        Task<T?> QueryFirstOrDefaultAsync<T>(string query, Func<DbDataReader, T> mapper, Action<DbParameterCollection>? parameters = null);
        DbParameter CreateParameter(string name, object? value);
    }
}
