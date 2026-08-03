using Dapper;
using System.Data;

namespace BENPOSDZ.Services
{
    // أداة نقل كل البيانات التجارية بين SQLite المحلي و MySQL (LAN) والعكس
    // تُستخدم في الإعدادات ▸ نقل البيانات، وتعمل بغض النظر عن الوضع الحالي
    public class DataTransferService
    {
        private readonly DatabaseService _dbService;

        private static readonly string[] Tables =
        {
            "Products", "Persons", "Orders", "Order_Details", "Expenses", "Product_Types", "Caisse"
        };

        public DataTransferService(DatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task<int> TransferSqliteToMySqlAsync()
        {
            using var src = _dbService.CreateLocalConnection();
            using var dst = _dbService.CreateMySqlConnection();
            return await TransferAsync(src, dst);
        }

        public async Task<int> TransferMySqlToSqliteAsync()
        {
            using var src = _dbService.CreateMySqlConnection();
            using var dst = _dbService.CreateLocalConnection();
            return await TransferAsync(src, dst);
        }

        private async Task<int> TransferAsync(IDbConnection src, IDbConnection dst)
        {
            int total = 0;
            using var transaction = dst.BeginTransaction();
            try
            {
                foreach (var table in Tables)
                {
                    try
                    {
                        // مسح الجدول الوجهة أولاً لضمان التطابق الكامل
                        await dst.ExecuteAsync($"DELETE FROM `{table}`", transaction: transaction);
                    }
                    catch { }

                    var rows = (await src.QueryAsync<dynamic>($"SELECT * FROM `{table}`")).ToList();
                    foreach (var row in rows)
                    {
                        var dict = (IDictionary<string, object?>)row;
                        if (dict.Count == 0) continue;

                        var keys = dict.Keys.ToList();
                        string cols = string.Join(", ", keys.Select(k => $"`{k}`"));
                        string pars = string.Join(", ", keys.Select((_, i) => $"@p{i}"));
                        var parameters = new DynamicParameters();
                        for (int i = 0; i < keys.Count; i++) parameters.Add($"p{i}", dict[keys[i]]);

                        await dst.ExecuteAsync($"INSERT INTO `{table}` ({cols}) VALUES ({pars})", parameters, transaction);
                        total++;
                    }
                }

                transaction.Commit();
                _dbService.LogEvent($"✅ تم نقل {total} سجل بنجاح بين قاعدتي البيانات.");
                return total;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }
}
