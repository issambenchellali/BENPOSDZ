using Dapper;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using System.Data;
using System.Net;
using System.Net.Sockets;

namespace BENPOSDZ.Services
{
    public class DatabaseService
    {
        private readonly string _sqliteConnectionString;
        private readonly string _sqliteDbPath;
        private readonly string _logFilePath;

        public string DBMode { get; private set; } = "Local";
        public string MySQL_Host { get; private set; } = "";
        public string MySQL_User { get; private set; } = "";
        public string MySQL_Pass { get; private set; } = "";
        public bool IsServerOnline { get; private set; } = true;
		
        public string ConnectionStatus { get; private set; } = "🟢 متصل بقاعدة محلية (SQLite)";
        public string SystemLog { get; set; } = "النظام جاهز.\n";
        public string LogFilePath => _logFilePath;

        public DatabaseService()
        {
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BENPOSDZ");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            
            _sqliteDbPath = Path.Combine(folderPath, "StockDB.db");
            _sqliteConnectionString = $"Data Source={_sqliteDbPath}";
            _logFilePath = Path.Combine(folderPath, "System.log");

            InitializeSQLiteSettings();
            LoadConnectionSettings();
            InitializeSystem();
        }

        // سجل الأحداث المطوَّر: يكتب كل الأحداث في ملف System.log ويحتفظ بأحدث 300 سطر في الذاكرة
        public void LogEvent(string msg, string level = "INFO")
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {msg}";
            var lines = new List<string> { logEntry };
            if (!string.IsNullOrEmpty(SystemLog))
                lines.AddRange(SystemLog.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)));
            SystemLog = string.Join("\n", lines);
            if (lines.Count > 300)
                SystemLog = string.Join("\n", lines.Take(300));
            try { File.AppendAllText(_logFilePath, logEntry + Environment.NewLine); } catch { }
        }

        public void ClearLog()
        {
            SystemLog = "السجل نظيف.\n";
            try { File.WriteAllText(_logFilePath, ""); } catch { }
        }

        public void OpenLogFolder()
        {
            try
            {
                string folder = Path.GetDirectoryName(_logFilePath) ?? "";
                if (!string.IsNullOrEmpty(folder))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch { }
        }

        private void InitializeSQLiteSettings()
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            connection.Execute("CREATE TABLE IF NOT EXISTS AppSettings (`Key` TEXT PRIMARY KEY NOT NULL, `Value` TEXT);");
        }

        private void LoadConnectionSettings()
        {
            using var connection = new SqliteConnection(_sqliteConnectionString);
            connection.Open();
            var settings = connection.Query<dynamic>("SELECT * FROM AppSettings").ToDictionary(x => (string)x.Key, x => (string)x.Value);
            
            DBMode = settings.ContainsKey("DBMode") ? settings["DBMode"] : "Local";
            MySQL_Host = settings.ContainsKey("MySQL_Host") ? settings["MySQL_Host"] : "127.0.0.1";
            MySQL_User = settings.ContainsKey("MySQL_User") ? settings["MySQL_User"] : "root";
            MySQL_Pass = settings.ContainsKey("MySQL_Pass") ? SecretProtector.Decrypt(settings["MySQL_Pass"]) : "";
        }

        public void ReloadSettings()
        {
            LoadConnectionSettings();
            if (DBMode == "LAN")
            {
                LogEvent("تم التبديل لوضع الشبكة المحلية (LAN). جاري الاتصال بـ MySQL...");
                InitializeSystem();
                ConnectionStatus = IsServerOnline ? "🟢 متصل بالخادم (MySQL)" : "🔴 تعذر الاتصال بالخادم (MySQL)";
            }
            else
            {
                ConnectionStatus = "🟢 متصل بقاعدة محلية (SQLite)";
                LogEvent("تم التبديل للوضع المحلي (SQLite).");
            }
        }

        public SqliteConnection CreateLocalConnection()
        {
            var sqlite = new SqliteConnection(_sqliteConnectionString);
            sqlite.Open();
            return sqlite;
        }

        // اتصال مباشر بـ MySQL بغض النظر عن الوضع الحالي (يُستخدم في أداة نقل البيانات)
        public MySqlConnection CreateMySqlConnection()
        {
            var conn = new MySqlConnection($"Server={MySQL_Host};Database=benposdb;Uid={MySQL_User};Pwd={MySQL_Pass};AllowZeroDateTime=True;Connect Timeout=5;");
            conn.Open();
            return conn;
        }

        // يُستخدم من كل الصفحات: SQLite فقط في الوضع المحلي، MySQL فقط في وضع LAN — لا خليط أبداً
        public IDbConnection CreateConnection()
        {
            if (DBMode == "LAN")
            {
                try
                {
                    var conn = new MySqlConnection($"Server={MySQL_Host};Database=benposdb;Uid={MySQL_User};Pwd={MySQL_Pass};AllowZeroDateTime=True;Connect Timeout=3;");
                    conn.Open();
                    IsServerOnline = true;
                    ConnectionStatus = "🟢 متصل بالخادم (MySQL)";
                    return conn;
                }
                catch (Exception ex)
                {
                    IsServerOnline = false;
                    ConnectionStatus = "🔴 تعذر الاتصال بالخادم (MySQL)";
                    LogEvent($"❌ فشل اتصال MySQL: {ex.Message}");
                    throw;
                }
            }
            
            return CreateLocalConnection();
        }
		
        public async Task<int> CleanDatabaseAsync()
        {
            int cleanedCount = 0;
            try
            {
                using var connection = CreateConnection();

                // صياغة النص المدعومة: SQLite تستخدم CAST(... AS TEXT) و MySQL تستخدم CAST(... AS CHAR)
                string castType = DBMode == "LAN" ? "CHAR" : "TEXT";
                string C(string col) => $"CAST({col} AS {castType})";

                // 1. تنظيف Products
                cleanedCount += await connection.ExecuteAsync($@"
                    UPDATE Products SET 
                        Pro_BuyPrice = REPLACE({C("Pro_BuyPrice")}, ',', '.'),
                        Pro_SalePriceG = REPLACE({C("Pro_SalePriceG")}, ',', '.'),
                        Pro_SalePrice_Min = REPLACE({C("Pro_SalePrice_Min")}, ',', '.'),
                        Pro_SalePrice_Max = REPLACE({C("Pro_SalePrice_Max")}, ',', '.'),
                        Pro_Qty = REPLACE({C("Pro_Qty")}, ',', '.'),
                        Pro_QtyMin = REPLACE({C("Pro_QtyMin")}, ',', '.')
                    WHERE {C("Pro_BuyPrice")} LIKE '%,%' 
                       OR {C("Pro_SalePriceG")} LIKE '%,%' 
                       OR {C("Pro_SalePrice_Min")} LIKE '%,%' 
                       OR {C("Pro_SalePrice_Max")} LIKE '%,%' 
                       OR {C("Pro_Qty")} LIKE '%,%' 
                       OR {C("Pro_QtyMin")} LIKE '%,%';");

                // 2. تنظيف Orders
                cleanedCount += await connection.ExecuteAsync($@"
                    UPDATE Orders SET 
                        Price = REPLACE({C("Price")}, ',', '.'),
                        Paid = REPLACE({C("Paid")}, ',', '.'),
                        Unpaid = REPLACE({C("Unpaid")}, ',', '.')
                    WHERE {C("Price")} LIKE '%,%' 
                       OR {C("Paid")} LIKE '%,%' 
                       OR {C("Unpaid")} LIKE '%,%';");

                // 3. تنظيف Order_Details
                cleanedCount += await connection.ExecuteAsync($@"
                    UPDATE Order_Details SET 
                        Pro_Qty = REPLACE({C("Pro_Qty")}, ',', '.'),
                        Pro_Price = REPLACE({C("Pro_Price")}, ',', '.')
                    WHERE {C("Pro_Qty")} LIKE '%,%' 
                       OR {C("Pro_Price")} LIKE '%,%';");

                // 4. تنظيف Expenses
                cleanedCount += await connection.ExecuteAsync($@"
                    UPDATE Expenses SET Expn_Price = REPLACE({C("Expn_Price")}, ',', '.') 
                    WHERE {C("Expn_Price")} LIKE '%,%';");

                // 5. تنظيف Persons
                cleanedCount += await connection.ExecuteAsync($@"
                    UPDATE Persons SET Person_Debt = REPLACE({C("Person_Debt")}, ',', '.') 
                    WHERE {C("Person_Debt")} LIKE '%,%';");

                LogEvent($"🧹 تم تنظيف {cleanedCount} سجلاً من الأخطاء العشرية بنجاح.");
                return cleanedCount;
            }
            catch (Exception ex)
            {
                LogEvent($"❌ فشل التنظيف: {ex.Message}");
                return 0;
            }
        }
		
        // محو كل العمليات المالية: الفواتير، التفاصيل، المصاريف، الصندوق، والديون
        // تحتفظ بالمنتجات والأصناف والأشخاص والمستخدمين والإعدادات (يمكن اختيارياً تصفير المخزون)
        public async Task<int> WipeFinancialDataAsync(bool resetStock)
        {
            using var connection = CreateConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                int affected = 0;
                affected += await connection.ExecuteAsync("DELETE FROM Order_Details", transaction: transaction);
                affected += await connection.ExecuteAsync("DELETE FROM Orders", transaction: transaction);
                affected += await connection.ExecuteAsync("DELETE FROM Expenses", transaction: transaction);
                affected += await connection.ExecuteAsync("UPDATE Caisse SET Amount = 0", transaction: transaction);
                affected += await connection.ExecuteAsync("UPDATE Coffer SET Amount = 0", transaction: transaction);
                affected += await connection.ExecuteAsync("UPDATE Persons SET Person_Debt = 0", transaction: transaction);
                if (resetStock)
                    affected += await connection.ExecuteAsync("UPDATE Products SET Pro_Qty = 0, Pro_Qty_Inv = 0", transaction: transaction);

                transaction.Commit();
                LogEvent("🧨 تم محو كل العمليات المالية" + (resetStock ? " وتصفير المخزون." : "."));
                return affected;
            }
            catch
            {
                try { transaction.Rollback(); } catch { }
                throw;
            }
        }
		
        private void InitializeSystem()
        {
            try
            {
                if (DBMode == "LAN")
                {
                    // وضع LAN: كل شيء على MySQL فقط — لا SQLite للبيانات التجارية
                    using var serverConnection = CreateConnection();
                    CreateTables(serverConnection);
                    SeedDefaults(serverConnection);
                    LogEvent("✅ تمت تهيئة النظام على خادم MySQL (وضع LAN).");
                }
                else
                {
                    // وضع محلي: كل شيء على SQLite فقط
                    using var localConnection = CreateLocalConnection();
                    CreateTables(localConnection);
                    SeedDefaults(localConnection);
                    LogEvent("✅ تمت تهيئة النظام على SQLite (وضع محلي).");
                }
            }
            catch (Exception ex)
            {
                LogEvent("❌ فشل تهيئة النظام: " + ex.Message);
            }
        }

        private void CreateTables(IDbConnection connection)
        {
            connection.Execute(@"
                CREATE TABLE IF NOT EXISTS Users (Id VARCHAR(36) PRIMARY KEY, User_Name VARCHAR(50), User_Password VARCHAR(200), User_Type INT, User_FullName VARCHAR(100), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Product_Types (Id VARCHAR(36) PRIMARY KEY, Type_Name VARCHAR(100), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Products (Id VARCHAR(36) PRIMARY KEY, Pro_Ref VARCHAR(50), Pro_Name VARCHAR(100), Pro_Mark VARCHAR(50), Pro_Propr VARCHAR(50), Pro_BuyPrice DECIMAL(18,2), Pro_SalePriceG DECIMAL(18,2), Pro_SalePrice_Min DECIMAL(18,2), Pro_SalePrice_Max DECIMAL(18,2), Pro_Qty DECIMAL(18,2), Pro_QtyMin DECIMAL(18,2), Pro_Unit VARCHAR(20), Pro_Barcode VARCHAR(50), Pro_Image TEXT, Pro_date_exp VARCHAR(20), Pro_Type_ID VARCHAR(36), Pro_Qty_Inv DECIMAL(18,2), Is_Counted INT, Pro_ImageUrl TEXT, UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Persons (Id VARCHAR(36) PRIMARY KEY, Person_Name VARCHAR(100), Person_Type INT, Person_Adress VARCHAR(200), Person_Phone VARCHAR(50), Person_Notes VARCHAR(200), Person_NRC VARCHAR(50), Person_ART VARCHAR(50), Person_NIF VARCHAR(50), Person_NIS VARCHAR(50), Person_Debt DECIMAL(18,2), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Orders (Id VARCHAR(36) PRIMARY KEY, Order_Type INT, Order_Date VARCHAR(30), Person_ID VARCHAR(36), User_ID VARCHAR(36), Price DECIMAL(18,2), Paid DECIMAL(18,2), Unpaid DECIMAL(18,2), Parent_Order_Id VARCHAR(36), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Order_Details (Id VARCHAR(36) PRIMARY KEY, Order_ID VARCHAR(36), Pro_ID VARCHAR(36), Pro_Qty DECIMAL(18,2), Pro_Price DECIMAL(18,2), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
                CREATE TABLE IF NOT EXISTS Caisse (Id VARCHAR(36) PRIMARY KEY, Amount DECIMAL(18,2), UpdatedAt VARCHAR(30));
                CREATE TABLE IF NOT EXISTS Coffer (Id VARCHAR(36) PRIMARY KEY, Amount DECIMAL(18,2), UpdatedAt VARCHAR(30));
                CREATE TABLE IF NOT EXISTS Expenses (Id VARCHAR(36) PRIMARY KEY, Expn_Name VARCHAR(100), Expn_Price DECIMAL(18,2), Expn_Date VARCHAR(30), Expn_Notes VARCHAR(200), UpdatedAt VARCHAR(30), IsSynced INT DEFAULT 0, IsDeleted INT DEFAULT 0);
            ");

            // ترحيل القواعد القديمة: إضافة الأعمدة الجديدة إن لم تكن موجودة
            EnsureColumn(connection, "Orders", "Parent_Order_Id", "Parent_Order_Id VARCHAR(36)");
            EnsureColumn(connection, "Products", "Pro_ImageUrl", "Pro_ImageUrl TEXT");
        }

        private void EnsureColumn(IDbConnection connection, string table, string column, string definition)
        {
            try
            {
                connection.Execute($"ALTER TABLE {table} ADD COLUMN {definition}");
                LogEvent($"تمت إضافة العمود {column} إلى جدول {table}.");
            }
            catch { }
        }

        private void SeedDefaults(IDbConnection connection)
        {
            var secService = new SecurityService();
            string dateNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Users") == 0)
            {
                string adminPass = secService.HashPassword("123");
                string sellerPass = secService.HashPassword("123");
                connection.Execute("INSERT INTO Users (Id, User_Name, User_Password, User_Type, User_FullName, UpdatedAt) VALUES (@Id, 'admin', @Pass, 1, 'المدير العام', @Date)",
                    new { Id = Guid.NewGuid().ToString(), Pass = adminPass, Date = dateNow });
                connection.Execute("INSERT INTO Users (Id, User_Name, User_Password, User_Type, User_FullName, UpdatedAt) VALUES (@Id, '123', @Pass, 0, 'بائع', @Date)",
                    new { Id = Guid.NewGuid().ToString(), Pass = sellerPass, Date = dateNow });
                LogEvent("✅ تم إنشاء الحسابات الافتراضية: المدير admin/123 والبائع 123/123.");
            }

            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Caisse") == 0)
                connection.Execute("INSERT INTO Caisse (Id, Amount, UpdatedAt) VALUES (@Id, 0, @Date)", new { Id = Guid.NewGuid().ToString(), Date = dateNow });

            if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Coffer") == 0)
                connection.Execute("INSERT INTO Coffer (Id, Amount, UpdatedAt) VALUES (@Id, 0, @Date)", new { Id = Guid.NewGuid().ToString(), Date = dateNow });
        }

        // اكتشاف خوادم MySQL في الشبكة المحلية (منفذ 3306)
        public async Task<List<string>> DiscoverMySqlServersAsync(int timeoutMs = 700)
        {
            var found = new List<string>();
            var candidates = new List<string> { "localhost", "127.0.0.1", Environment.MachineName };

            // تحديد الشبكة الفرعية الخاصة (192.168.x / 10.x / 172.16-31.x) من عنوان الجهاز
            string? subnet = null;
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Environment.MachineName))
                {
                    if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = ip.ToString();
                    var octets = s.Split('.');
                    if (octets.Length != 4) continue;
                    bool isPrivate = (octets[0] == "192" && octets[1] == "168")
                                  || octets[0] == "10"
                                  || (octets[0] == "172" && int.TryParse(octets[1], out int b) && b >= 16 && b <= 31);
                    if (isPrivate) { subnet = s.Substring(0, s.LastIndexOf('.')) + "."; break; }
                }
            }
            catch { }

            if (subnet != null)
                for (int i = 1; i <= 254; i++) candidates.Add(subnet + i);

            var distinct = candidates.Distinct().ToList();
            LogEvent($"🔍 بدء اكتشاف خوادم MySQL عبر {distinct.Count} عنوان...");
            await Task.WhenAll(distinct.Select(host => Task.Run(async () =>
            {
                using var tcp = new TcpClient();
                try
                {
                    var connectTask = tcp.ConnectAsync(host, 3306);
                    var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                    if (done == connectTask && tcp.Connected)
                        lock (found) found.Add(host);
                }
                catch { }
            })));

            LogEvent($"🔎 تم العثور على {found.Count} خادم MySQL محتمل.");
            return found.OrderBy(x => x).ToList();
        }

        // فحص دقة الحسابات المالية: مطابقة تفاصيل الفواتير مع الإجماليات والمدفوعات والديون
        public async Task<FinancialAuditResult> AuditFinancialAccuracyAsync()
        {
            var result = new FinancialAuditResult();
            try
            {
                using var connection = CreateConnection();

                // 1. مجموع التفاصيل يجب أن يساوي سعر الفاتورة
                var mismatched = await connection.QueryAsync<dynamic>(@"
                    SELECT o.Id, o.Price AS OrderPrice, COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) AS DetailsTotal
                    FROM Orders o LEFT JOIN Order_Details od ON od.Order_ID = o.Id AND od.IsDeleted = 0
                    WHERE o.IsDeleted = 0
                    GROUP BY o.Id, o.Price
                    HAVING ABS(COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) - o.Price) > 0.01");
                foreach (var m in mismatched)
                {
                    result.Issues.Add($"⚠️ الفاتورة {((string)m.Id).Substring(0, 8)}... سعرها {(decimal)m.OrderPrice} لكن مجموع تفاصيلها {(decimal)m.DetailsTotal}");
                    result.TotalIssues++;
                }

                // 2. Paid + Unpaid يجب أن يساوي Price
                var debtMismatch = await connection.QueryAsync<dynamic>(@"
                    SELECT Id, Price, Paid, Unpaid FROM Orders
                    WHERE IsDeleted = 0 AND ABS((COALESCE(Paid, 0) + COALESCE(Unpaid, 0)) - Price) > 0.01");
                foreach (var m in debtMismatch)
                {
                    result.Issues.Add($"⚠️ الفاتورة {((string)m.Id).Substring(0, 8)}... المدفوع+الدين ({(decimal)m.Paid}+{(decimal)m.Unpaid}) لا يساوي السعر {(decimal)m.Price}");
                    result.TotalIssues++;
                }

                // 3. فواتير بدون تفاصيل
                int noDetails = await connection.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(*) FROM Orders o WHERE o.IsDeleted = 0
                    AND NOT EXISTS (SELECT 1 FROM Order_Details od WHERE od.Order_ID = o.Id AND od.IsDeleted = 0)");
                if (noDetails > 0)
                {
                    result.Issues.Add($"⚠️ توجد {noDetails} فاتورة بدون أي تفاصيل.");
                    result.TotalIssues += noDetails;
                }

                result.SalesTotal = await connection.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Price),0) FROM Orders WHERE Order_Type IN (0,1) AND IsDeleted = 0");
                result.PurchaseTotal = await connection.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Price),0) FROM Orders WHERE Order_Type = 2 AND IsDeleted = 0");
                result.InvoicesChecked = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE IsDeleted = 0");

                if (result.TotalIssues == 0)
                    result.Issues.Add("✅ كل الحسابات المالية دقيقة، لا توجد أي مشاكل.");
            }
            catch (Exception ex)
            {
                result.Issues.Add("❌ فشل التدقيق: " + ex.Message);
                result.TotalIssues++;
                LogEvent($"❌ فشل التدقيق المالي: {ex.Message}");
            }
            return result;
        }

        // نسخة احتياطية من قاعدة البيانات المحلية (عبر SQLite Backup API — يعمل حتى أثناء تشغيل القاعدة)
        public string BackupDatabase()
        {
            try
            {
                string backupsDir = Path.Combine(Path.GetDirectoryName(_sqliteDbPath)!, "Backups");
                Directory.CreateDirectory(backupsDir);
                string dest = Path.Combine(backupsDir, $"StockDB_{DateTime.Now:yyyyMMdd_HHmmss}.db");
                using (var source = new SqliteConnection(_sqliteConnectionString))
                {
                    source.Open();
                    using (var backup = new SqliteConnection($"Data Source={dest}"))
                    {
                        backup.Open();
                        source.BackupDatabase(backup);
                    }
                }
                LogEvent($"💾 تم إنشاء نسخة احتياطية: {dest}");
                return dest;
            }
            catch (Exception ex)
            {
                LogEvent($"❌ فشل النسخ الاحتياطي: {ex.Message}");
                return "";
            }
        }

        // استعادة أحدث نسخة احتياطية (عبر Backup API — آمن مع قاعدة مقفلة أو قيد الاستخدام)
        public string RestoreLatestBackup()
        {
            try
            {
                string backupsDir = Path.Combine(Path.GetDirectoryName(_sqliteDbPath)!, "Backups");
                if (!Directory.Exists(backupsDir)) return "";
                var latest = Directory.GetFiles(backupsDir, "StockDB_*.db").OrderByDescending(f => f).FirstOrDefault();
                if (latest == null) return "";
                using (var backup = new SqliteConnection($"Data Source={latest}"))
                {
                    backup.Open();
                    using (var live = new SqliteConnection(_sqliteConnectionString))
                    {
                        live.Open();
                        backup.BackupDatabase(live);
                    }
                }
                LogEvent($"♻️ تمت استعادة أحدث نسخة احتياطية: {Path.GetFileName(latest)}");
                return latest;
            }
            catch (Exception ex)
            {
                LogEvent($"❌ فشل الاستعادة: {ex.Message}");
                return "";
            }
        }
    }

    public class FinancialAuditResult
    {
        public int TotalIssues { get; set; }
        public List<string> Issues { get; set; } = new();
        public decimal SalesTotal { get; set; }
        public decimal PurchaseTotal { get; set; }
        public int InvoicesChecked { get; set; }
    }
}
