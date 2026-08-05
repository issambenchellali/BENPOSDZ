using CommunityToolkit.Maui.Storage;
using Microsoft.Maui.Storage;
using System.IO;
using System.Threading.Tasks;

namespace BENPOSDZ.Services
{
    // تصدير نسخة احتياطية إلى موقع يختاره المستخدم (فلاشة USB / مجلد) واستعادة من ملف
    // يستخدم CommunityToolkit.Maui.Storage (FileSaver) على أندرويد وويندوز + FilePicker المدمج للاستعادة
    public class FileDialogService
    {
        private readonly DatabaseService _db;

        public FileDialogService(DatabaseService db) => _db = db;

        // آخر خطأ حدث أثناء التصدير/الاستعادة (يعرضه الواجهة بدل رسالة عامة)
        public string LastError { get; private set; } = "";

        // تصدير نسخة احتياطية: يفتح حوار الحفظ لاختيار الوجهة (USB، مجلد، ...)
        public async Task<string> ExportBackupAsync()
        {
            LastError = "";
            try
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string tempPath = Path.Combine(Path.GetTempPath(), $"benpos_export_{stamp}.db");
                string backupPath = _db.BackupDatabaseTo(tempPath);
                if (string.IsNullOrEmpty(backupPath))
                {
                    LastError = "فشل إنشاء النسخة الاحتياطية من قاعدة البيانات (راجع السجل للتفاصيل).";
                    return "";
                }
                byte[] bytes = await File.ReadAllBytesAsync(tempPath);
                try { File.Delete(tempPath); } catch { }

                using var stream = new MemoryStream(bytes);
                var result = await FileSaver.Default.SaveAsync($"StockDB_{stamp}.db", stream, CancellationToken.None);
                if (result.IsSuccessful)
                {
                    _db.LogEvent($"💾 تم تصدير النسخة الاحتياطية إلى: {result.FilePath}");
                    return result.FilePath;
                }
                LastError = result.Exception?.Message ?? "ألغى المستخدم اختيار موقع الحفظ.";
                _db.LogEvent($"❌ فشل تصدير النسخة الاحتياطية: {LastError}");
                return "";
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _db.LogEvent($"❌ فشل تصدير النسخة الاحتياطية: {ex.Message}");
                return "";
            }
        }

        // استعادة قاعدة البيانات من ملف نسخة احتياطية يختاره المستخدم
        public async Task<string> RestoreBackupAsync()
        {
            LastError = "";
            try
            {
                var pickResult = await FilePicker.Default.PickAsync(new PickOptions
                {
                    PickerTitle = "اختر ملف النسخة الاحتياطية (StockDB_*.db)",
                    FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                    {
                        { DevicePlatform.WinUI, new[] { ".db", ".sqlite", "*" } },
                        { DevicePlatform.Android, new[] { "application/octet-stream", "application/x-sqlite3", "*/*" } },
                    })
                });
                if (pickResult == null)
                {
                    LastError = "ألغى المستخدم اختيار الملف.";
                    return "";
                }

                // نسخ الملف المختار إلى ملف مؤقت ثم الاستعادة عبر SQLite Backup API
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string tempPath = Path.Combine(Path.GetTempPath(), $"benpos_restore_{stamp}.db");
                using (var src = await pickResult.OpenReadAsync())
                using (var dst = File.Create(tempPath))
                    await src.CopyToAsync(dst);

                string restored = _db.RestoreDatabaseFromFile(tempPath);
                try { File.Delete(tempPath); } catch { }
                if (string.IsNullOrEmpty(restored))
                    LastError = "فشل استعادة قاعدة البيانات من الملف (راجع السجل للتفاصيل).";
                return restored;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _db.LogEvent($"❌ فشل الاستعادة من الملف: {ex.Message}");
                return "";
            }
        }
    }
}
