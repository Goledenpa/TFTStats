using TFTStats.Core.Entities;
using TFTStats.Core.Infrastructure;
using TFTStats.Core.Repositories.Interfaces;

namespace TFTStats.Core.Repositories
{
    public class TFTPatchRepository : ITFTPatchRepository
    {
        private readonly SqlExecutor _sqlExecutor;

        public TFTPatchRepository(SqlExecutor sqlExecutor)
        {
            _sqlExecutor = sqlExecutor;
        }

        public Task<TFTPatch> GetFirstPatch(int set)
        {
            const string query = "SELECT id, set_number, patch_name, start_date, end_date FROM tft_patch WHERE set_number = @set ORDER BY ID ASC LIMIT 1";

            return _sqlExecutor.QueryFirstOrDefaultAsync(query, r =>
            {
                return new TFTPatch
                {
                    Id = r.GetInt32(0),
                    SetNumber = r.GetInt32(1),
                    PatchName = r.GetString(2),
                    StartDate = r.GetDateTime(3),
                    EndDate = r.GetDateTime(4),
                };
            },
            p =>
            {
                p.Add(_sqlExecutor.CreateParameter("set", set));
            })!;
        }

        public Task<TFTPatch> GetLastPatch(int set)
        {
            const string query = "SELECT id, set_number, patch_name, start_date, end_date FROM tft_patch WHERE end_date IS NOT NULL ORDER BY ID DESC LIMIT 1";

            return _sqlExecutor.QueryFirstOrDefaultAsync(query, r =>
            {
                return new TFTPatch
                {
                    Id = r.GetInt32(0),
                    SetNumber = r.GetInt32(1),
                    PatchName = r.GetString(2),
                    StartDate = r.GetDateTime(3),
                    EndDate = r.GetDateTime(4),
                };
            })!;
        }

        public Task<TFTPatch?> GetPatch(string name)
        {
            const string query = "SELECT id, set_number, patch_name, start_date, end_date FROM tft_patch WHERE patch_name = @patchName";

            return _sqlExecutor.QueryFirstOrDefaultAsync(query, r =>
            {
                return new TFTPatch
                {
                    Id = r.GetInt32(0),
                    SetNumber = r.GetInt32(1),
                    PatchName = r.GetString(2),
                    StartDate = r.GetDateTime(3),
                    EndDate = r.GetDateTime(4),
                };
            },
            p =>
            {
                p.Add(_sqlExecutor.CreateParameter("patchName", name));
            });
        }

        public async Task<IEnumerable<TFTPatch>> GetPatchesBySetAsync(int setNumber)
        {
            const string query = "SELECT id, set_number, patch_name, start_date, end_date FROM tft_patch WHERE set_number = @setNumber ORDER BY start_date ASC";

            return await _sqlExecutor.QueryAsync(query, r =>
            {
                return new TFTPatch
                {
                    Id = r.GetInt32(0),
                    SetNumber = r.GetInt32(1),
                    PatchName = r.GetString(2),
                    StartDate = r.GetDateTime(3),
                    EndDate = r.IsDBNull(4) ? null : r.GetDateTime(4),
                };
            },
            p =>
            {
                p.Add(_sqlExecutor.CreateParameter("setNumber", setNumber));
            });
        }
    }
}
