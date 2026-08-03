using System.Text;

namespace BENPOSDZ.Services
{
    // تشفير أسرار الإعدادات (SupabaseKey / MySQL_Pass) حسب المنصة:
    //  - Windows: DPAPI (مربوطة بحساب المستخدم)
    //  - Android: AndroidKeyStore (AES/GCM — مفتاح داخل مخزن النظام الآمن)
    // القيم القديمة غير المشفرة تمر كما هي (Decrypt يعيدها).
    public static class SecretProtector
    {
#if WINDOWS
        private const string Marker = "DPAPI1:";
#elif ANDROID
        private const string Marker = "AKS1:";
        private const string KeyAlias = "benposdz_secret_key";
#else
        private const string Marker = "B64:";
#endif

        public static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            try
            {
#if WINDOWS
                return WinEncrypt(plain);
#elif ANDROID
                return AndroidEncrypt(plain);
#else
                return Marker + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
#endif
            }
            catch { return plain; }
        }

        public static string Decrypt(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
#if ANDROID
            // قيم مشفرة على ويندوز لا يمكن فكها على أندرويد — تُعاد كما هي
            if (stored.StartsWith("DPAPI1:", StringComparison.Ordinal)) return stored;
#endif
            if (!stored.StartsWith(Marker, StringComparison.Ordinal)) return stored;
            try
            {
#if WINDOWS
                return WinDecrypt(stored);
#elif ANDROID
                return AndroidDecrypt(stored);
#else
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(Marker.Length)));
#endif
            }
            catch { return stored; }
        }

        public static bool IsEncrypted(string stored)
            => stored != null && (stored.StartsWith("DPAPI1:", StringComparison.Ordinal) || stored.StartsWith("AKS1:", StringComparison.Ordinal) || stored.StartsWith("B64:", StringComparison.Ordinal));

#if WINDOWS
        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static string WinEncrypt(string plain)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(plain);
            var input = ToBlob(bytes);
            if (!CryptProtectData(ref input, "BENPOSDZ", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output))
                return plain;
            try
            {
                return Marker + Convert.ToBase64String(FromBlob(output));
            }
            finally { LocalFree(output.pbData); }
        }

        private static string WinDecrypt(string stored)
        {
            byte[] enc = Convert.FromBase64String(stored.Substring(Marker.Length));
            var input = ToBlob(enc);
            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out var output))
                return stored;
            try
            {
                return Encoding.UTF8.GetString(FromBlob(output));
            }
            finally { LocalFree(output.pbData); }
        }

        private static DATA_BLOB ToBlob(byte[] data)
        {
            IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(data.Length);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = data.Length, pbData = ptr };
        }

        private static byte[] FromBlob(DATA_BLOB blob)
        {
            byte[] data = new byte[blob.cbData];
            System.Runtime.InteropServices.Marshal.Copy(blob.pbData, data, 0, blob.cbData);
            return data;
        }
#endif

#if ANDROID
        private static Javax.Crypto.Cipher GetCipher(bool encrypt)
        {
            var ks = Java.Security.KeyStore.GetInstance("AndroidKeyStore");
            ks.Load(null);
            var key = ks.GetKey(KeyAlias, null);
            if (key == null)
            {
                var kg = Javax.Crypto.KeyGenerator.GetInstance(Android.Security.Keystore.KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
                var spec = new Android.Security.Keystore.KeyGenParameterSpec.Builder(
                        KeyAlias,
                        Android.Security.Keystore.KeyStorePurpose.Encrypt | Android.Security.Keystore.KeyStorePurpose.Decrypt)
                    .SetBlockModes(Android.Security.Keystore.KeyProperties.BlockModeGcm)
                    .SetEncryptionPaddings(Android.Security.Keystore.KeyProperties.EncryptionPaddingNone)
                    .Build();
                kg.Init(spec);
                kg.GenerateKey();
                key = ks.GetKey(KeyAlias, null);
            }
            var cipher = Javax.Crypto.Cipher.GetInstance("AES/GCM/NoPadding");
            if (encrypt)
                cipher.Init(Javax.Crypto.CipherMode.EncryptMode, key);
            else
                cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key);
            return cipher;
        }

        private static string AndroidEncrypt(string plain)
        {
            var cipher = GetCipher(true);
            var iv = cipher.GetIV();
            var ct = cipher.DoFinal(Encoding.UTF8.GetBytes(plain));
            var all = new byte[iv.Length + ct.Length];
            Buffer.BlockCopy(iv, 0, all, 0, iv.Length);
            Buffer.BlockCopy(ct, 0, all, iv.Length, ct.Length);
            return Marker + Convert.ToBase64String(all);
        }

        private static string AndroidDecrypt(string stored)
        {
            var all = Convert.FromBase64String(stored.Substring(Marker.Length));
            const int ivLen = 12;
            var iv = new byte[ivLen];
            var ct = new byte[all.Length - ivLen];
            Buffer.BlockCopy(all, 0, iv, 0, ivLen);
            Buffer.BlockCopy(all, ivLen, ct, 0, ct.Length);

            var ks = Java.Security.KeyStore.GetInstance("AndroidKeyStore");
            ks.Load(null);
            var key = ks.GetKey(KeyAlias, null);
            var cipher = Javax.Crypto.Cipher.GetInstance("AES/GCM/NoPadding");
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, key, new Javax.Crypto.Spec.GCMParameterSpec(128, iv));
            return Encoding.UTF8.GetString(cipher.DoFinal(ct));
        }
#endif
    }
}
