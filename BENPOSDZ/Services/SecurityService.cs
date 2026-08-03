using System.Security.Cryptography;
using System.Text;

namespace BENPOSDZ.Services
{
    public class SecurityService
    {
        // معايير PBKDF2 (آمنة ومعيارية)
        private const int Pbkdf2Iterations = 100_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const string FormatPrefix = "PBKDF2$";

        // يُستخدم فقط للتحقق من كلمات المرور القديمة (قبل الترقية)
        private const string LegacySalt = "BENPOS_DZ_LEGACY_SALT";

        // تشفير كلمة المرور بتقنية PBKDF2 مع Salt عشوائي فريد لكل مستخدم
        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "";

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, HashSize);

            return $"{FormatPrefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        // التحقق من كلمة المرور (يدعم الصيغ الجديدة والقديمة تلقائياً)
        public bool VerifyPassword(string enteredPassword, string? storedHash)
        {
            if (string.IsNullOrEmpty(enteredPassword) || string.IsNullOrEmpty(storedHash)) return false;

            if (storedHash.StartsWith(FormatPrefix))
                return VerifyPbkdf2(enteredPassword, storedHash);

            // هاش قديم (SHA256 + Salt ثابت) — نتحقق منه للسماح بالترحيل
            return LegacyHash(enteredPassword) == storedHash;
        }

        // هل الهاش بالصيغة القديمة؟ (لكي نحدّثه عند أول تسجيل دخول ناجح)
        public bool IsLegacyHash(string? storedHash)
        {
            return !string.IsNullOrEmpty(storedHash) && !storedHash.StartsWith(FormatPrefix);
        }

        private static bool VerifyPbkdf2(string password, string storedHash)
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4) return false;
            if (!int.TryParse(parts[1], out int iterations) || iterations < 10_000) return false;

            try
            {
                byte[] salt = Convert.FromBase64String(parts[2]);
                byte[] expected = Convert.FromBase64String(parts[3]);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch { return false; }
        }

        // الخوارزمية القديمة (SHA256 + Salt ثابت) للتحقق من الحسابات القائمة فقط
        private static string LegacyHash(string password)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + LegacySalt));
            var builder = new StringBuilder();
            foreach (byte b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
