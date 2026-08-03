using Dapper;
using System.Data;

namespace BENPOSDZ.Services
{
    public interface ISettingsRepository
    {
        Task<Dictionary<string, string>> LoadAllAsync();
        Task<bool> SaveAllAsync(Dictionary<string, string> settings);
    }

    public class SettingsRepository : ISettingsRepository
    {
        private readonly DatabaseService _dbService;
        private readonly IApplicationStateService _appState;

        public SettingsRepository(DatabaseService dbService, IApplicationStateService appState)
        {
            _dbService = dbService;
            _appState = appState;
        }

        public async Task<Dictionary<string, string>> LoadAllAsync()
        {
            // 🔥 إجبار الاتصال المحلي
            using var connection = _dbService.CreateLocalConnection();
            var data = await connection.QueryAsync<dynamic>("SELECT * FROM AppSettings");
            var result = data.ToDictionary(x => (string)x.Key, x => (string)x.Value);
            if (result.ContainsKey("MySQL_Pass")) result["MySQL_Pass"] = SecretProtector.Decrypt(result["MySQL_Pass"]);
            if (result.ContainsKey("SupabaseKey")) result["SupabaseKey"] = SecretProtector.Decrypt(result["SupabaseKey"]);
            return result;
        }


        public async Task<bool> SaveAllAsync(Dictionary<string, string> settings)
        {
			using var connection = _dbService.CreateLocalConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                // تشفير الأسرار قبل الحفظ (لا نغيّر النسخة في الذاكرة المعروضة للواجهة)
                var toSave = new Dictionary<string, string>(settings);
                if (toSave.ContainsKey("MySQL_Pass")) toSave["MySQL_Pass"] = SecretProtector.Encrypt(toSave["MySQL_Pass"]);
                if (toSave.ContainsKey("SupabaseKey")) toSave["SupabaseKey"] = SecretProtector.Encrypt(toSave["SupabaseKey"]);

                foreach (var kvp in toSave)
                {
                    // 1. محاولة UPDATE (تعمل على SQLite و MySQL)
                    var rowsAffected = await connection.ExecuteAsync(
                        "UPDATE AppSettings SET `Value` = @Value WHERE `Key` = @Key", 
                        new { kvp.Key, kvp.Value }, transaction);

                    // 2. INSERT إذا لم يتم تحديث أي صف
                    if (rowsAffected == 0)
                    {
                        await connection.ExecuteAsync(
                            "INSERT INTO AppSettings (`Key`, `Value`) VALUES (@Key, @Value)", 
                            new { kvp.Key, kvp.Value }, transaction);
                    }
                }
                transaction.Commit();
                
                // 3. إعلام ApplicationState بتحديث الإعدادات
                _dbService.ReloadSettings();
                _appState.SetConnectionStatus(_dbService.ConnectionStatus);
                
                return true;
            }
            catch
            {
                transaction.Rollback();
                throw; // رمي الخطأ لتتلقاه الواجهة وتعرض إشعار Toast
            }
        }
    }
}