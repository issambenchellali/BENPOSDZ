using BENPOSDZ.Services;
using Dapper;

namespace BENPOSDZ.Services
{
    // محرك خلفي خفيف: يزامن فقط مع Supabase (السحابة) من قاعدة البيانات النشطة.
    // لا يوجد أي نسخ مزدوج بين SQLite وMySQL — النظام يعمل بوضع واحد فقط.
    public class BackgroundSyncService
    {
        private readonly IServiceProvider _serviceProvider;
        private CancellationTokenSource? _cts;

        public BackgroundSyncService(IServiceProvider serviceProvider) { _serviceProvider = serviceProvider; }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _ = Task.Run(() => ExecuteAsync(_cts.Token));
        }

        private async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                const int delaySeconds = 15;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbService = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                    var syncService = scope.ServiceProvider.GetRequiredService<CloudSyncService>();

                    // المزامنة السحابية (Supabase): تعمل إذا كانت مفعلة في الإعدادات
                    using var configConn = dbService.CreateLocalConnection();
                    var isEnabled = configConn.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'CloudSyncEnabled'");
                    if (isEnabled == "true")
                    {
                        await syncService.SyncAllAsync();
                        await syncService.PullFromSupabaseAsync();
                    }
                }
                catch (Exception) { }
                
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
        }
    }
}
