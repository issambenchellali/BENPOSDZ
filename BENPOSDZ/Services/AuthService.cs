using Dapper;
using System.IO;

namespace BENPOSDZ.Services
{
    public class AuthService
    {
        public UserModel? CurrentUser { get; private set; }
        public bool IsLoggedIn => CurrentUser != null;
        public bool IsAdmin => CurrentUser?.User_Type == 1;
        
        public bool IsLicensed { get; set; } = true;
        public bool IsTrialExpired { get; set; } = false;
        public bool DateTampered { get; set; } = false;

        public void Login(UserModel user) => CurrentUser = user;
        public void Logout() => CurrentUser = null;

        public void CheckLicense(DatabaseService dbService)
        {
            // 🔥 إجبار الاتصال المحلي (SQLite)
            using var connection = dbService.CreateLocalConnection();
            var settings = connection.Query<dynamic>("SELECT * FROM AppSettings").ToDictionary(x => (string)x.Key, x => (string)x.Value);

            string expDateStr = settings.ContainsKey("LicenseExpiryDate") ? settings["LicenseExpiryDate"] : "";
            if (!string.IsNullOrEmpty(expDateStr) && DateTime.TryParse(expDateStr, out var expDate) && expDate > DateTime.UtcNow)
            {
                IsLicensed = true;
                IsTrialExpired = false;
                return;
            }

            IsLicensed = false;
            string trialStartStr = settings.ContainsKey("TrialStartDate") ? settings["TrialStartDate"] : "";
            if (string.IsNullOrEmpty(trialStartStr))
            {
                trialStartStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                connection.Execute("DELETE FROM AppSettings WHERE `Key` = 'TrialStartDate'");
                connection.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES ('TrialStartDate', @Date)", new { Date = trialStartStr });
            }

            var trialStart = DateTime.Parse(trialStartStr);
            int daysLeft = 15 - (int)(DateTime.UtcNow - trialStart).TotalDays;
            if (daysLeft <= 0) IsTrialExpired = true;
        }

        // كشف إرجاع تاريخ النظام للوراء (تلاعب بالتاريخ)
        public void CheckDateTamper(DatabaseService dbService)
        {
            try
            {
                using var connection = dbService.CreateLocalConnection();
                var lastSeen = connection.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'LastSystemDate'");

                string todayStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
                if (DateTime.TryParse(lastSeen, out var lastDate) && DateTime.TryParse(todayStr, out var today) && today < lastDate)
                {
                    DateTampered = true;
                    dbService.LogEvent("🚨 تم اكتشاف إرجاع تاريخ النظام للوراء! تم قفل البرنامج.");
                    return;
                }

                // تحديث آخر تاريخ معروف للنظام
                connection.Execute("DELETE FROM AppSettings WHERE `Key` = 'LastSystemDate'");
                connection.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES ('LastSystemDate', @Date)", new { Date = todayStr });
            }
            catch { }
        }

        public string GetMachineId()
        {
            string raw;
#if ANDROID
            // بصمة مركّبة: ANDROID_ID + العلامة + الطراز (لا تتغير بإعادة التثبيت)
            string? androidId = Android.Provider.Settings.Secure.GetString(
                Android.App.Application.Context.ContentResolver,
                Android.Provider.Settings.Secure.AndroidId);
            raw = $"{(androidId ?? "?")}|{Android.OS.Build.Brand}|{Android.OS.Build.Model}";
#else
            raw = $"{Environment.ProcessorCount}-{Environment.MachineName}-{Environment.UserName}";
#endif
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                int val = BitConverter.ToInt32(bytes, 0);
                int val2 = BitConverter.ToInt32(bytes, 4);
                return $"{Math.Abs(val) % 10000:D4}-{Math.Abs(val2) % 10000:D4}";
            }
        }

        // سر التفعيل الافتراضي (يُحفظ في AppSettings حتى لا يُخزّن في الكود المصدري)
        private const string DefaultActivationSecret = "BENPOS_DZ_ACTIVATION_KEY";

        // قراءة سر التفعيل من الإعدادات المحلية (قابل للتغيير لكل تثبيت)
        public string GetActivationSecret(DatabaseService dbService)
        {
            try
            {
                using var connection = dbService.CreateLocalConnection();
                var secret = connection.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'ActivationSecret'");
                return string.IsNullOrEmpty(secret) ? DefaultActivationSecret : secret;
            }
            catch { return DefaultActivationSecret; }
        }

        // حفظ سر التفعيل الجديد من واجهة المالك — بعد التغيير تُصبح الأكواد القديمة غير صالحة
        public void SetActivationSecret(DatabaseService dbService, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret)) return;
            using var connection = dbService.CreateLocalConnection();
            connection.Execute("DELETE FROM AppSettings WHERE `Key` = 'ActivationSecret'");
            connection.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES ('ActivationSecret', @Secret)", new { Secret = secret.Trim() });
            dbService.LogEvent("🔑 تم تغيير سر التفعيل من واجهة المالك.");
        }

        public bool HasCustomActivationSecret(DatabaseService dbService)
        {
            try
            {
                using var connection = dbService.CreateLocalConnection();
                var secret = connection.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'ActivationSecret'");
                return !string.IsNullOrEmpty(secret);
            }
            catch { return false; }
        }

        public bool ValidateActivationCode(DatabaseService dbService, string machineId, string enteredCode)
        {
            string raw = machineId + GetActivationSecret(dbService);
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                int val = BitConverter.ToInt32(bytes, 0);
                return enteredCode == (Math.Abs(val) % 1000000).ToString("D6");
            }
        }

        // تسجيل الدخول بكود التفعيل (6 أرقام): يُفعّل البرنامج ويدخل بحساب المدير مباشرة
        public (bool Success, string? Error) TryLoginWithActivationCode(DatabaseService dbService, string enteredCode)
        {
            enteredCode = (enteredCode ?? "").Trim();
            if (enteredCode.Length != 6 || !enteredCode.All(char.IsDigit))
                return (false, "أدخل كود التفعيل المكوّن من 6 أرقام.");

            if (!ValidateActivationCode(dbService, GetMachineId(), enteredCode))
                return (false, "كود التفعيل غير صحيح. تأكد من الرقم الموجود في شاشة التفعيل (الإعدادات ▸ التفعيل).");

            using var connection = dbService.CreateConnection();
            var admin = connection.QueryFirstOrDefault<UserModel>("SELECT * FROM Users WHERE User_Name = 'admin' AND IsDeleted = 0");
            if (admin == null)
                return (false, "لم يتم العثور على حساب المدير في قاعدة البيانات.");

            Login(admin);
            IsLicensed = true;
            IsTrialExpired = false;
            dbService.LogEvent("🔑 تم تسجيل الدخول بكود التفعيل.");
            return (true, null);
        }

        public DateTime AddLicenseTime(DatabaseService dbService, int yearsToAdd)
        {
            // 🔥 إجبار الاتصال المحلي
            using var connection = dbService.CreateLocalConnection();
            var expStr = connection.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'LicenseExpiryDate'");
            DateTime currentExpiry = DateTime.TryParse(expStr, out var d) && d > DateTime.UtcNow ? d : DateTime.UtcNow;
            
            DateTime newExpiry = currentExpiry.AddYears(yearsToAdd);
            connection.Execute("DELETE FROM AppSettings WHERE `Key` = 'LicenseExpiryDate'");
            connection.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES ('LicenseExpiryDate', @Date)", new { Date = newExpiry.ToString("yyyy-MM-dd HH:mm:ss") });
            
            IsLicensed = true;
            IsTrialExpired = false;
            return newExpiry;
        }

        public string TryActivateUsb(DatabaseService dbService, string storeName)
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType == DriveType.Removable && drive.IsReady)
                {
                    string licFile = Path.Combine(drive.RootDirectory.FullName, "benpos.lic");
                    if (File.Exists(licFile))
                    {
                        string fileContent = File.ReadAllText(licFile);
                        string decrypted = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(fileContent));
                        var parts = decrypted.Split('|');
                        
                        if (parts.Length == 4)
                        {
                            string licStore = parts[0];
                            int maxDev = int.Parse(parts[1]);
                            int years = int.Parse(parts[3]);

                            if (licStore.ToLower() != storeName.ToLower())
                                return "خطأ: هذا المفتاح مخصص لمتجر آخر باسم مختلف!";

                            string machineId = GetMachineId();
                            string licPath = Path.Combine(drive.RootDirectory.FullName, "benpos_machines.lic");
                            
                            List<string> registeredMachines = new List<string>();
                            if (File.Exists(licPath)) registeredMachines = File.ReadAllLines(licPath).ToList();

                            if (!registeredMachines.Contains(machineId))
                            {
                                if (registeredMachines.Count >= maxDev)
                                    return "خطأ: لقد استنفدت الحد الأقصى لتفعيل الأجهزة (5 أجهزة).";
                                
                                registeredMachines.Add(machineId);
                                File.WriteAllLines(licPath, registeredMachines);
                            }

                            DateTime newExp = AddLicenseTime(dbService, years);
                            
                            // 🔥 إجبار الاتصال المحلي
                            using var connection = dbService.CreateLocalConnection();
                            connection.Execute("DELETE FROM AppSettings WHERE `Key` = 'StoreNameLocked'");
                            connection.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES ('StoreNameLocked', 'true')");
                            
                            return $"SUCCESS|{newExp:yyyy-MM-dd}";
                        }
                    }
                }
            }
            return "خطأ: لم يتم العثور على مفتاح التفعيل (benpos.lic) في أي فلاشة موصولة.";
        }

        public class UserModel
        {
            public string Id { get; set; } = "";
            public string User_Name { get; set; } = "";
            public string? User_Password { get; set; }
            public string User_FullName { get; set; } = "";
            public int User_Type { get; set; } 
        }
    }
}