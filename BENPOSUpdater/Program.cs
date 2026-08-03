using System.Diagnostics;
using System.Text.Json;

// BENPOSUpdater: يستبدل ملفات BENPOSDZ.exe بعد إغلاق التطبيق، مع نسخة احتياطية وتراجع عند الفشل.
// الاستخدام: BENPOSUpdater.exe "path\to\job.json" [-elevated]

if (args.Length < 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("لا يوجد ملف مهمة (job.json).");
    return 1;
}

var jobPath = args[0];
UpdateJob? job = null;
try { job = JsonSerializer.Deserialize<UpdateJob>(await File.ReadAllTextAsync(jobPath)); }
catch (Exception ex) { Console.Error.WriteLine("فشل قراءة المهمة: " + ex.Message); return 2; }

if (job == null) { Console.Error.WriteLine("مهمة غير صالحة."); return 3; }

var logger = new Logger(job.LogFile);
bool elevated = args.Contains("-elevated", StringComparer.OrdinalIgnoreCase);

logger.Log($"BENPOSUpdater بدأ. الإصدار الجديد: {job.Version}، رفع الصلاحيات: {elevated}");

try
{
    if (string.IsNullOrWhiteSpace(job.AppDir) || !Directory.Exists(job.AppDir))
    {
        logger.Log("فشل: مجلد التطبيق غير موجود: " + job.AppDir);
        return 4;
    }

    // 1) انتظار إغلاق التطبيق
    WaitForAppExit(job.AppExe, logger);

    // 2) إن لزم، طلب رفع الصلاحيات عند فشل الكتابة في المجلد
    if (!elevated && !CanWrite(job.AppDir))
    {
        logger.Log("الوصول مرفوض — إعادة تشغيل بصلاحيات إدارية...");
        var psi = new ProcessStartInfo
        {
            FileName = Process.GetCurrentProcess().MainModule?.FileName,
            Arguments = $"\"{jobPath}\" -elevated",
            UseShellExecute = true,
            Verb = "runas"
        };
        Process.Start(psi);
        return 0;
    }

    // 3) نسخة احتياطية
    Directory.CreateDirectory(job.BackupDir);
    CopyDirectory(job.AppDir, job.BackupDir, logger);
    logger.Log("تم إنشاء نسخة احتياطية في: " + job.BackupDir);

    // 4) التثبيت: مسح المجلد الحالي ثم النسخ من المجلد المؤقت
    if (!Directory.Exists(job.StagingDir))
    {
        logger.Log("فشل: مجلد التحديث غير موجود: " + job.StagingDir);
        return 5;
    }
    ClearDirectory(job.AppDir, logger);
    CopyDirectory(job.StagingDir, job.AppDir, logger);
    logger.Log("تم تثبيت الإصدار " + job.Version);

    // 5) تنظيف المجلد المؤقت
    try { Directory.Delete(job.StagingDir, true); } catch { }

    // 6) إعادة تشغيل التطبيق
    if (job.Relaunch && File.Exists(job.AppExe))
    {
        Process.Start(new ProcessStartInfo(job.AppExe) { WorkingDirectory = job.AppDir, UseShellExecute = true });
        logger.Log("تمت إعادة تشغيل التطبيق.");
    }

    logger.Log("اكتمل التحديث بنجاح ✓");
    return 0;
}
catch (Exception ex)
{
    logger.Log("خطأ أثناء التحديث: " + ex);
    // تراجع تلقائي من النسخة الاحتياطية
    try
    {
        if (Directory.Exists(job.BackupDir))
        {
            ClearDirectory(job.AppDir, logger);
            CopyDirectory(job.BackupDir, job.AppDir, logger);
            logger.Log("تم التراجع إلى الإصدار السابق.");
        }
    }
    catch (Exception rb) { logger.Log("فشل التراجع: " + rb); }
    return 6;
}

static void WaitForAppExit(string appExe, Logger logger)
{
    string processName = Path.GetFileNameWithoutExtension(appExe);
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed.TotalSeconds < 180)
    {
        if (Process.GetProcessesByName(processName).Length == 0)
        {
            logger.Log("التطبيق تم إغلاقه بنجاح.");
            return;
        }
        Thread.Sleep(1000);
    }
    logger.Log("مهلة انتظار إغلاق التطبيق انتهت (180 ثانية) — المتابعة على مسؤولية.");
}

static bool CanWrite(string dir)
{
    try
    {
        var probe = Path.Combine(dir, ".upd_probe");
        File.WriteAllText(probe, "x");
        File.Delete(probe);
        return true;
    }
    catch { return false; }
}

static void ClearDirectory(string dir, Logger logger)
{
    foreach (var d in Directory.GetDirectories(dir))
        Directory.Delete(d, true);
    foreach (var f in Directory.GetFiles(dir))
        File.Delete(f);
}

static void CopyDirectory(string src, string dst, Logger logger)
{
    Directory.CreateDirectory(dst);
    foreach (var file in Directory.GetFiles(src))
    {
        var target = Path.Combine(dst, Path.GetFileName(file));
        if (string.Equals(Path.GetFileName(file), "BENPOSUpdater.exe", StringComparison.OrdinalIgnoreCase))
            continue;
        File.Copy(file, target, true);
    }
    foreach (var dir in Directory.GetDirectories(src))
        CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)), logger);
}

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

public class Logger
{
    private readonly string _path;
    private readonly object _lock = new();
    public Logger(string path) => _path = path;
    public void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
            lock (_lock)
                File.AppendAllText(_path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
        }
        catch { }
    }
}
