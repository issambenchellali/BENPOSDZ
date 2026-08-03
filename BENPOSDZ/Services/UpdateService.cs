using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;

namespace BENPOSDZ.Services
{
    // موديل معلومات الإصدار كما تُنشر في version.json على GitHub Releases
    public class VersionInfo
    {
        public string version { get; set; } = "";
        public string notes { get; set; } = "";
        public string sha256 { get; set; } = "";
        public string zipUrl { get; set; } = "";
    }

    // ملف المهمة الذي يقرؤه BENPOSUpdater.exe
    public class UpdateJob
    {
        public string Version { get; set; } = "";
        public string AppExe { get; set; } = "";
        public string AppDir { get; set; } = "";
        public string StagingDir { get; set; } = "";
        public string BackupDir { get; set; } = "";
        public bool Relaunch { get; set; } = true;
        public string LogFile { get; set; } = "";
    }

    // خدمة التحديثات: فحص الإصدارات، تحميل الملف، التحقق، ثم تفويض التثبيت إلى BENPOSUpdater.exe
    public class UpdateService
    {
        public const string K_UpdateRepoUrl = "UpdateRepoUrl";
        public const string K_UpdateCheckOnStart = "UpdateCheckOnStart";
        public const string K_UpdateCheckPeriodic = "UpdateCheckPeriodic";
        public const string K_UpdateAutoDownload = "UpdateAutoDownload";
        public const string K_LastUpdateCheck = "LastUpdateCheck";

        private const string DefaultRepoUrl = "https://github.com/BENPOSDZ/BENPOSDZ";
        private readonly DatabaseService _db;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

        public VersionInfo? PendingUpdate { get; private set; }
        public bool IsBusy { get; private set; }

        public void DismissPendingUpdate() => PendingUpdate = null;

        public UpdateService(DatabaseService db)
        {
            _db = db;
        }

        // الإصدار الحالي المدمج في البرنامج (من csproj ApplicationDisplayVersion)
        public string CurrentVersionString
        {
            get
            {
                try { return Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString; }
                catch { return "1.0"; }
            }
        }

        private string? GetSetting(string key)
        {
            try
            {
                using var conn = _db.CreateLocalConnection();
                return conn.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = @K", new { K = key });
            }
            catch { return null; }
        }

        private void SetSetting(string key, string value)
        {
            using var conn = _db.CreateLocalConnection();
            int ra = conn.Execute("UPDATE AppSettings SET `Value` = @V WHERE `Key` = @K", new { V = value, K = key });
            if (ra == 0) conn.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES (@K, @V)", new { V = value, K = key });
        }

        public string RepoUrl
        {
            get
            {
                var v = GetSetting(K_UpdateRepoUrl);
                return string.IsNullOrWhiteSpace(v) ? DefaultRepoUrl : v.Trim();
            }
        }

        public bool CheckOnStart => GetSetting(K_UpdateCheckOnStart) == "true";
        public bool CheckPeriodic => GetSetting(K_UpdateCheckPeriodic) != "false";
        public bool AutoDownload => GetSetting(K_UpdateAutoDownload) == "true";

        public DateTime LastUpdateCheckUtc
        {
            get => DateTime.TryParse(GetSetting(K_LastUpdateCheck), out var d) ? d : DateTime.MinValue;
            set => SetSetting(K_LastUpdateCheck, value.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        // فحص وجود تحديث أحدث من الإصدار الحالي. يعيد true عند توفر تحديث.
        public async Task<bool> CheckForUpdateAsync()
        {
#if ANDROID
            // على أندرويد التحديث عبر المتجر — لا يوجد نظام تحديث مدمج
            return false;
#else
            if (IsBusy) return false;
            PendingUpdate = null;
            try
            {
                var url = $"{RepoUrl.TrimEnd('/')}/releases/latest/download/version.json";
                var json = await _http.GetStringAsync(url);
                var info = JsonSerializer.Deserialize<VersionInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (info == null || string.IsNullOrWhiteSpace(info.version)) return false;

                if (Version.TryParse(info.version, out var remote) && Version.TryParse(CurrentVersionString, out var current) && remote > current)
                {
                    PendingUpdate = info;
                    LastUpdateCheckUtc = DateTime.UtcNow;
                    return true;
                }
                LastUpdateCheckUtc = DateTime.UtcNow;
                return false;
            }
            catch
            {
                // فحص هادئ: فشل الشبكة لا يُزعج المستخدم
                return false;
            }
#endif
        }

        // تنزيل التحديث، التحقق من التجزئة، استخراجه، ثم إطلاق المحدّث وإغلاق التطبيق
        public async Task DownloadAndInstallAsync(VersionInfo info, IProgress<(long Done, long Total)>? progress, CancellationToken ct = default)
        {
#if ANDROID
            throw new PlatformNotSupportedException("التحديث المدمج غير مدعوم على أندرويد — استخدم المتجر.");
#else
            if (IsBusy) throw new InvalidOperationException("عملية تحديث قيد التنفيذ.");
            IsBusy = true;
            try
            {
                var updatesDir = UpdatesDir();
                Directory.CreateDirectory(updatesDir);
                var zipPath = Path.Combine(updatesDir, $"BENPOSDZ_{info.version}.zip");
                var stagingDir = Path.Combine(updatesDir, "staging", info.version);

                // 1) التحميل مع تقدم
                var zipUrl = string.IsNullOrWhiteSpace(info.zipUrl)
                    ? $"{RepoUrl.TrimEnd('/')}/releases/latest/download/BENPOSDZ_{info.version}.zip"
                    : info.zipUrl;
                await DownloadAsync(zipUrl, zipPath, progress, ct);

                // 2) التحقق من SHA-256 (يمنع التلاعب)
                if (!string.IsNullOrWhiteSpace(info.sha256))
                {
                    string actual = Sha256Hex(zipPath);
                    if (!string.Equals(actual, info.sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"فشل التحقق من سلامة الملف: {actual} != {info.sha256}");
                }

                // 3) الاستخراج إلى مجلد مؤقت
                if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, true);
                Directory.CreateDirectory(stagingDir);
                ZipFile.ExtractToDirectory(zipPath, stagingDir);

                var stagingExe = Path.Combine(stagingDir, "BENPOSDZ.exe");
                var stagingUpdater = Path.Combine(stagingDir, "BENPOSUpdater.exe");
                if (!File.Exists(stagingExe)) throw new FileNotFoundException("الملف الرئيسي غير موجود في حزمة التحديث.");
                if (!File.Exists(stagingUpdater)) throw new FileNotFoundException("أداة التحديث غير موجودة في حزمة التحديث.");

                // 4) نسخ المحدّث إلى مجلد دائم خارج مجلد البرنامج ثم إطلاقه
                var updaterCopy = Path.Combine(updatesDir, "BENPOSUpdater.exe");
                File.Copy(stagingUpdater, updaterCopy, overwrite: true);

                var currentVersion = string.IsNullOrWhiteSpace(CurrentVersionString) ? "old" : CurrentVersionString;
                var job = new UpdateJob
                {
                    Version = info.version,
                    AppExe = Path.Combine(AppContext.BaseDirectory, "BENPOSDZ.exe"),
                    AppDir = AppContext.BaseDirectory,
                    StagingDir = stagingDir,
                    BackupDir = Path.Combine(updatesDir, "backup", currentVersion),
                    Relaunch = true,
                    LogFile = Path.Combine(updatesDir, "updater.log")
                };
                var jobPath = Path.Combine(updatesDir, "job.json");
                await File.WriteAllTextAsync(jobPath, JsonSerializer.Serialize(job, new JsonSerializerOptions { WriteIndented = true }), ct);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updaterCopy, $"\"{jobPath}\"") { UseShellExecute = true });
            }
            finally
            {
                IsBusy = false;
            }
#endif
        }

        private async Task DownloadAsync(string url, string dest, IProgress<(long, long)>? progress, CancellationToken ct)
        {
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(dest);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                progress?.Report((done, total));
            }
        }

        public static string Sha256Hex(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        public static string UpdatesDir()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BENPOSDZ", "updates");
    }
}
