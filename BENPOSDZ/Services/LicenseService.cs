using Dapper;

namespace BENPOSDZ.Services
{
    // نظام الترخيص: ثلاث نسخ (Mono / Multi / Full) + أكواد التفعيل والترقية والدخول اليومي.
    // الخوارزمية خاصة: لا تُنسخ ولا تُوثّق خارج هذا الكود.
    public class LicenseService
    {
        public const string EditionMono = "Mono";
        public const string EditionMulti = "Multi";
        public const string EditionFull = "Full";

        public const string K_Edition = "Edition";

        public static string[] AllEditions => new[] { EditionMono, EditionMulti, EditionFull };

        // رموز البادئات: M للـ Mono، P للـ Multi، F للـ Full
        public static string EditionPrefix(string edition) => edition switch
        {
            EditionMono => "M",
            EditionMulti => "P",
            EditionFull => "F",
            _ => "F"
        };

        public static string EditionFromPrefix(char prefix) => prefix switch
        {
            'M' or 'm' => EditionMono,
            'P' or 'p' => EditionMulti,
            _ => EditionFull
        };

        public static string EditionLabel(string edition) => edition switch
        {
            EditionMono => "النسخة الأساسية (Mono-poste)",
            EditionMulti => "النسخة المتعددة (Multi-poste)",
            EditionFull => "النسخة الكاملة (Full)",
            _ => "النسخة الكاملة (Full)"
        };

        private const string DailyCodeSalt = "BENPOS_DZ_DAILY_2024_DEV_KEY";
        private const string DefaultActivationSecret = "BENPOS_DZ_2024_SECRET_KEY";

        private readonly DatabaseService _db;

        public LicenseService(DatabaseService db) => _db = db;

        // قراءة سر التفعيل من الإعدادات المحلية (قابل للتغيير لكل تثبيت)
        public string GetSecret()
        {
            try
            {
                using var conn = _db.CreateLocalConnection();
                var s = conn.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = 'ActivationSecret'");
                return string.IsNullOrEmpty(s) ? DefaultActivationSecret : s;
            }
            catch { return DefaultActivationSecret; }
        }

        // النسخة الحالية (تُخزَّن في AppSettings)
        public string GetEdition()
        {
            try
            {
                using var conn = _db.CreateLocalConnection();
                var e = conn.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = @K", new { K = K_Edition });
                return !string.IsNullOrEmpty(e) && AllEditions.Contains(e) ? e : EditionFull;
            }
            catch { return EditionFull; }
        }

        public void SetEdition(string edition)
        {
            if (!AllEditions.Contains(edition)) edition = EditionFull;
            using var conn = _db.CreateLocalConnection();
            int ra = conn.Execute("UPDATE AppSettings SET `Value` = @V WHERE `Key` = @K", new { V = edition, K = K_Edition });
            if (ra == 0) conn.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES (@K, @V)", new { V = edition, K = K_Edition });
            _db.LogEvent($"🔑 تم تغيير نوع النسخة إلى: {EditionLabel(edition)}.");
        }

        // كود الدخول اليومي (6 أرقام): مرتبط بالتاريخ فقط، نفس الكود على كل الأجهزة
        public static string ComputeDailyCode()
        {
            string raw = DateTime.Now.ToString("yyyy-MM-dd") + DailyCodeSalt;
            return Sha6(raw);
        }

        // كود التفعيل للإصدارات: machineId + secret + النسخة + الساعة (yyyyMMddHH) → 7 خانات (حرف + 6 أرقام)
        public static string ComputeEditionCode(string machineId, string secret, string edition, DateTime stamp)
        {
            string raw = machineId + secret + edition + stamp.ToString("yyyyMMddHH");
            return EditionPrefix(edition) + Sha6(raw);
        }

        // الكود القديم (بدون نسخة) — يبقى مقبولاً للتوافق ويُعتبر نسخة كاملة
        public static string ComputeLegacyCode(string machineId, string secret, DateTime stamp)
        {
            string raw = machineId + secret + stamp.ToString("yyyyMMddHH");
            return Sha6(raw);
        }

        // التحقق من صحة كود التفعيل (مع بادئة النسخة أو القديم) مع تفاوت ساعة واحدة
        public (bool Valid, string Edition) ValidateActivationCode(string machineId, string enteredCode)
        {
            enteredCode = (enteredCode ?? "").Trim();

            string edition;
            string digits;
            if (enteredCode.Length == 7 && "MPFmpf".Contains(enteredCode[0]))
            {
                edition = EditionFromPrefix(enteredCode[0]);
                digits = enteredCode.Substring(1);
            }
            else if (enteredCode.Length == 6 && enteredCode.All(char.IsDigit))
            {
                edition = EditionFull;
                digits = enteredCode;
            }
            else
            {
                return (false, EditionFull);
            }

            if (digits.Length != 6 || !digits.All(char.IsDigit))
                return (false, edition);

            string secret = GetSecret();
            var nowUtc = DateTime.UtcNow;
            var nowLocal = DateTime.Now;
            string[] stamps =
            {
                nowUtc.AddHours(-1).ToString("yyyyMMddHH"), nowUtc.ToString("yyyyMMddHH"), nowUtc.AddHours(1).ToString("yyyyMMddHH"),
                nowLocal.AddHours(-1).ToString("yyyyMMddHH"), nowLocal.ToString("yyyyMMddHH"), nowLocal.AddHours(1).ToString("yyyyMMddHH")
            };

            bool matches = stamps.Any(s =>
                Sha6(machineId + secret + edition + s) == digits ||
                Sha6(machineId + secret + s) == digits);

            return (matches, edition);
        }

        private static string Sha6(string raw)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
            int val = System.BitConverter.ToInt32(bytes, 0);
            return (Math.Abs(val) % 1000000).ToString("D6");
        }
    }
}
