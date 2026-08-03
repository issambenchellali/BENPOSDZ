using Dapper;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace BENPOSDZ.Services
{
    public class CloudSyncService
    {
        private readonly DatabaseService _dbService;
        private readonly HttpClient _httpClient;
        private string _supabaseUrl = "";
        private string _supabaseKey = "";

        public CloudSyncService(DatabaseService dbService)
        {
            _dbService = dbService;
            _httpClient = new HttpClient();
        }

        // قراءة إعدادات Supabase من قاعدة البيانات المحلية (وليس من الكود)
        private void LoadCloudSettings()
        {
            string url = "";
            string key = "";
            try
            {
                using var connection = _dbService.CreateLocalConnection();
                var settings = connection.Query<dynamic>("SELECT * FROM AppSettings").ToDictionary(x => (string)x.Key, x => (string)x.Value);
                url = settings.ContainsKey("SupabaseURL") ? settings["SupabaseURL"] : "";
                key = settings.ContainsKey("SupabaseKey") ? SecretProtector.Decrypt(settings["SupabaseKey"]) : "";
            }
            catch { }

            _supabaseUrl = url.TrimEnd('/');
            _supabaseKey = key;

            if (string.IsNullOrEmpty(_supabaseKey))
            {
                _httpClient.DefaultRequestHeaders.Remove("apikey");
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                return;
            }

            _httpClient.DefaultRequestHeaders.Remove("apikey");
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseKey}");
        }
        public bool IsConfigured
        {
            get
            {
                LoadCloudSettings();
                return !string.IsNullOrEmpty(_supabaseUrl) && !string.IsNullOrEmpty(_supabaseKey);
            }
        }

        // المزامنة الكاملة: رفع كل الجداول التجارية إلى Supabase (بدون Users حتى لا تُكشف كلمات المرور)
        public async Task SyncAllAsync()
        {
            if (!IsConfigured) return;

            var tables = new (string Local, string Remote)[]
            {
                ("Products", "Products"),
                ("Persons", "Persons"),
                ("Orders", "Orders"),
                ("Order_Details", "Order_Details"),
                ("Expenses", "Expenses"),
                ("Product_Types", "Product_Types")
            };

            Exception? lastError = null;
            foreach (var (local, remote) in tables)
            {
                try
                {
                    await PushTableAsync(_httpClient, _supabaseUrl, local, remote);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _dbService.LogEvent($"❌ فشل رفع {local} إلى Supabase: {ex.Message}");
                }
            }

            if (lastError != null) throw lastError;
        }

        private async Task PushTableAsync(HttpClient httpClient, string url, string localTable, string remoteTable)
        {
            // بيانات الأعمال تُقرأ من القاعدة النشطة (SQLite أو MySQL)،
            // لكن مؤشر LastCloudPushTime يُخزَّن دائماً في AppSettings المحلية (SQLite)
            using var connection = _dbService.CreateConnection();

            // نرفع كل السجلات غير الموسومة (IsSynced = 0) — مزامنة كاملة ودقيقة،
            // حتى القديمة منها التي لم تُرفع في جلسات سابقة
            string selectSql = $"SELECT * FROM {localTable} WHERE IsSynced = 0 ORDER BY UpdatedAt ASC LIMIT 500";
            var unsyncedRows = (await connection.QueryAsync<dynamic>(selectSql)).ToList();
            
            if (!unsyncedRows.Any()) return;

            // 1. جمع كل المفاتيح الممكنة من كل السجلات لضمان التوحيد
            var allKeys = new HashSet<string>();
            var rawRows = new List<Dictionary<string, object?>>();

            foreach (var row in unsyncedRows)
            {
                var dict = (IDictionary<string, object?>)row;
                var sanitized = DataHelper.SanitizeForApi(dict);
                
                var cleanDict = new Dictionary<string, object?>();
                foreach (var kvp in sanitized)
                {
                    string lowerKey = kvp.Key.ToLower();
                    allKeys.Add(lowerKey);
                    
                    if (lowerKey == "issynced" || lowerKey == "isdeleted" || lowerKey == "is_counted")
                    {
                        cleanDict[lowerKey] = kvp.Value != null ? Convert.ToInt32(kvp.Value) : 0;
                    }
                    else 
                    {
                        cleanDict[lowerKey] = kvp.Value;
                    }
                }
                rawRows.Add(cleanDict);
            }

            // 2. إجبار كل سجل على احتواء كل المفاتيح (ملء المفاتيح المفقودة بـ null)
            var jsonRows = new List<Dictionary<string, object?>>();
            foreach (var row in rawRows)
            {
                var uniformRow = new Dictionary<string, object?>();
                foreach (var key in allKeys)
                {
                    uniformRow[key] = row.ContainsKey(key) ? row[key] : null;
                }
                jsonRows.Add(uniformRow);
            }

            // 3. الإرسال إلى Supabase مع معالجة تلقائية للأخطاء:
            //    - PGRST205: الجدول غير موجود بالاسم الحالي → إعادة المحاولة بالاسم الصغير (Supabase)
            //    - PGRST204: عمود غير موجود في المخطط البعيد → حذف هذا العمود من البيانات وإعادة المحاولة
            //      (الأعمدة المضافة محلياً حديثاً مثل parent_order_id قد لا تكون في Supabase بعد)
            // لا تُرفع صور المنتجات إلى Supabase (تُحفظ محلياً فقط) — نستبعد أعمدة الصور من الحمولة
            if (localTable == "Products")
            {
                foreach (var row in jsonRows)
                {
                    row.Remove("pro_image");
                    row.Remove("pro_imageurl");
                }
            }

            string effectiveTable = remoteTable;
            var currentRows = jsonRows;
            HttpResponseMessage? finalResponse = null;

            for (int attempt = 0; attempt < 15; attempt++)
            {
                string payload = JsonSerializer.Serialize(currentRows);
                var body = new StringContent(payload, Encoding.UTF8, "application/json");
                var sendRequest = new HttpRequestMessage(HttpMethod.Post, $"{url}/rest/v1/{effectiveTable}?on_conflict=id")
                { Content = body };
                sendRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

                finalResponse = await httpClient.SendAsync(sendRequest);
                if (finalResponse.IsSuccessStatusCode) break;

                string errText = await finalResponse.Content.ReadAsStringAsync();

                if (attempt == 0 && errText.Contains("PGRST205") && effectiveTable != effectiveTable.ToLowerInvariant())
                {
                    effectiveTable = effectiveTable.ToLowerInvariant();
                    continue;
                }

                string? missingColumn = ExtractMissingColumn(errText);
                if (missingColumn != null)
                {
                    _dbService.LogEvent($"⚠️ العمود '{missingColumn}' غير موجود في Supabase ({remoteTable}) — تم تجاوزه في هذا الرفع.");
                    currentRows = currentRows.Select(r =>
                    {
                        var d = new Dictionary<string, object?>(r);
                        d.Remove(missingColumn);
                        return d;
                    }).ToList();
                    continue;
                }

                throw new Exception($"Sync Error {remoteTable}: {errText}");
            }

            if (finalResponse == null || !finalResponse.IsSuccessStatusCode)
                throw new Exception($"Sync Error {remoteTable}: استجابة غير متوقعة من الخادم.");

            // 4. تحديث وقت آخر رفع حتى لا تُعاد نفس السجلات مرة أخرى (في القاعدة المحلية دائماً)
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            WriteLocalMarker("LastCloudPushTime", nowStr);

            // 5. تمييز السجلات المرفوعة بـ IsSynced = 1 حتى يختفي عداد "بانتظار المزامنة"
            foreach (var row in unsyncedRows)
            {
                var raw = (IDictionary<string, object?>)row;
                var id = raw.TryGetValue("Id", out var idVal) ? idVal?.ToString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                try
                {
                    await connection.ExecuteAsync($"UPDATE {localTable} SET IsSynced = 1 WHERE Id = @Id", new { Id = id });
                }
                catch { }
            }

            _dbService.LogEvent($"✅ تم رفع {unsyncedRows.Count} سجل إلى Supabase ({remoteTable}).");
        }

        // تفريغ كل الجداول البعيدة في Supabase (مسح كامل) — يُستخدم من أداة نقل البيانات
        public async Task ClearSupabaseAsync()
        {
            if (!IsConfigured) return;

            var tables = new[] { "products", "persons", "orders", "order_details", "expenses", "product_types" };
            foreach (var table in tables)
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_supabaseUrl}/rest/v1/{table}?id=not.is.null");
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    string err = await response.Content.ReadAsStringAsync();
                    throw new Exception($"فشل تفريغ الجدول {table}: {err}");
                }
            }
            _dbService.LogEvent("🗑️ تم تفريغ كل جداول Supabase بنجاح.");
        }

        // استخراج اسم العمود الناقص من رسالة خطأ PostgREST (PGRST204)
        // مثال: Could not find the 'pro_imageurl' column of 'products' in the schema cache
        private static string? ExtractMissingColumn(string errorText)
        {
            const string marker = "Could not find the '";
            int start = errorText.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return null;
            start += marker.Length;
            int end = errorText.IndexOf("'", start, StringComparison.Ordinal);
            if (end < 0) return null;
            string column = errorText.Substring(start, end - start);
            return string.IsNullOrWhiteSpace(column) ? null : column;
        }

        // سحب كل الجداول البعيدة من Supabase ودمجها في القاعدة المحلية (آخر تعديل يفوز)
        public async Task<int> PullFromSupabaseAsync()
        {
            if (!IsConfigured) return 0;

            var tables = new (string Local, string Remote)[]
            {
                ("Product_Types", "product_types"),
                ("Products", "products"),
                ("Persons", "persons"),
                ("Orders", "orders"),
                ("Order_Details", "order_details"),
                ("Expenses", "expenses")
            };

            // سحب تدريجي: نسحب فقط الصفوف التي حُدّثت بعد بداية الدورة السابقة.
            // العلامة المائية = بداية هذه الدورة (وليس نهايتها) حتى لا تُفقد أي
            // تحديث يقع أثناء تنفيذ السحب — سيلتقطه السحب التالي تلقائياً.
            DateTime cycleStart = DateTime.UtcNow;
            DateTime? since = null;
            string marker = ReadLocalMarker("LastCloudPullTime");
            if (DateTime.TryParse(marker, out var m))
                since = DateTime.SpecifyKind(m, DateTimeKind.Utc);

            int total = 0;
            using var connection = _dbService.CreateConnection();
            foreach (var (local, remote) in tables)
            {
                try
                {
                    total += await MergeTableAsync(connection, local, remote, since);
                }
                catch (Exception ex)
                {
                    _dbService.LogEvent($"⚠️ فشل سحب {local} من Supabase: {ex.Message}");
                }
            }

            WriteLocalMarker("LastCloudPullTime", cycleStart.ToString("yyyy-MM-dd HH:mm:ss"));
            if (total > 0)
                _dbService.LogEvent($"⬇️ تم سحب {total} سجل من Supabase بنجاح.");
            return total;
        }

        private async Task<int> MergeTableAsync(IDbConnection connection, string localTable, string remoteTable, DateTime? sinceUtc)
        {
            var localColumns = GetLocalColumns(connection, localTable);
            if (localColumns.Count == 0) return 0;

            var remoteRows = await FetchRemoteRowsAsync(remoteTable, sinceUtc);
            if (remoteRows.Count == 0) return 0;

            int merged = 0;
            foreach (var row in remoteRows)
            {
                var rawId = row.ContainsKey("id") ? row["id"] : null;
                if (rawId == null) continue;
                string id = rawId.ToString() ?? "";

                dynamic? existingRow = await connection.QueryFirstOrDefaultAsync($"SELECT * FROM {localTable} WHERE Id = @Id", new { Id = id });
                IDictionary<string, object>? existing = existingRow as IDictionary<string, object>;
                bool exists = existing != null;

                string remoteUpdated = NormalizeDate(row.ContainsKey("updatedat") ? row["updatedat"] : null);
                string localUpdated = "";
                if (existing != null)
                {
                    foreach (var k in existing.Keys)
                    {
                        if (string.Equals(k, "UpdatedAt", StringComparison.OrdinalIgnoreCase))
                        {
                            localUpdated = NormalizeDate(existing[k]);
                            break;
                        }
                    }
                }

                bool remoteNewer = !exists || string.Compare(remoteUpdated, localUpdated, StringComparison.Ordinal) > 0;

                if (exists && !remoteNewer)
                {
                    await connection.ExecuteAsync($"UPDATE {localTable} SET IsSynced = 1 WHERE Id = @Id", new { Id = id });
                    continue;
                }

                var assignments = new Dictionary<string, object?>();
                foreach (var col in localColumns)
                {
                    string lower = col.ToLowerInvariant();
                    if (lower == "id" || !row.ContainsKey(lower)) continue;
                    object? val = ConvertRemoteValue(row[lower]);
                    if (lower == "updatedat") val = NormalizeDate(val);
                    if (lower == "issynced" || lower == "isdeleted") val = val == null ? 0 : Convert.ToInt32(val);
                    assignments[col] = val;
                }
                if (assignments.Count == 0) continue;

                // السجلات المدمجة أصبحت متزامنة — نعلّمها IsSynced = 1 لتجنّب إعادة رفعها
                string? isSyncedCol = localColumns.FirstOrDefault(c => string.Equals(c, "IsSynced", StringComparison.OrdinalIgnoreCase));
                if (isSyncedCol != null) assignments[isSyncedCol] = 1;

                if (!exists)
                {
                    assignments["Id"] = id;
                    var cols = assignments.Keys.ToList();
                    var sql = $"INSERT INTO {localTable} ({string.Join(",", cols)}) VALUES ({string.Join(",", cols.Select(c => "@" + c))})";
                    await connection.ExecuteAsync(sql, assignments);
                }
                else
                {
                    var setParts = assignments.Keys.Select(c => $"{c} = @{c}").ToList();
                    var ps = new DynamicParameters(assignments);
                    ps.Add("Id", id);
                    var sql = $"UPDATE {localTable} SET {string.Join(",", setParts)} WHERE Id = @Id";
                    await connection.ExecuteAsync(sql, ps);
                }
                merged++;
            }

            if (merged > 0)
                _dbService.LogEvent($"⬇️ سحب {remoteTable}: {merged} سجل مدمج.");
            return merged;
        }

        private async Task<List<Dictionary<string, object?>>> FetchRemoteRowsAsync(string remoteTable, DateTime? sinceUtc)
        {
            // Supabase/PostgREST يرجّع 1000 صف كحد أقصى لكل طلب — نستخدم الترقيم لجلب كل الصفوف
            const int pageSize = 1000;
            var rows = new List<Dictionary<string, object?>>();
            int offset = 0;

            // سحب تدريجي: updatedat=gt.<الزمن> يعيد فقط ما تغيّر بعد العلامة المائية
            string filter = sinceUtc.HasValue ? $"&updatedat=gt.{sinceUtc.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")}" : "";

            while (true)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{_supabaseUrl}/rest/v1/{remoteTable}?select=*&limit={pageSize}&offset={offset}{filter}");
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    if (!string.IsNullOrEmpty(filter))
                    {
                        // جدول سحابي قديم لا يحتوي عمود updatedat (أو خطأ عابر) —
                        // نتراجع عن الفلترة ونعيد الجلب الكامل من الصفر حتى لا تُفقد صفوف.
                        filter = "";
                        offset = 0;
                        rows.Clear();
                        continue;
                    }
                    string err = await response.Content.ReadAsStringAsync();
                    throw new Exception($"فشل السحب من {remoteTable}: {err}");
                }
                string json = await response.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(json)) break;

                var page = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json) ?? new();
                foreach (var raw in page)
                {
                    var row = new Dictionary<string, object?>();
                    foreach (var kvp in raw)
                    {
                        row[kvp.Key] = ConvertRemoteValue(kvp.Value);
                    }
                    rows.Add(row);
                }

                if (page.Count < pageSize) break;
                offset += pageSize;
            }
            return rows;
        }

        private static object? ConvertRemoteValue(object? el)
        {
            if (el is not JsonElement j) return el;
            switch (j.ValueKind)
            {
                case JsonValueKind.String: return j.GetString();
                case JsonValueKind.Number: return j.TryGetDecimal(out var d) ? d : (object)j.GetDouble();
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;
                default: return j.ToString();
            }
        }

        private static string NormalizeDate(object? val)
        {
            if (val == null) return "";
            string s = val.ToString() ?? "";
            if (DateTime.TryParse(s, out var dt)) return dt.ToString("yyyy-MM-dd HH:mm:ss");
            return s;
        }

        private static List<string> GetLocalColumns(IDbConnection connection, string table)
        {
            var cols = new List<string>();
            try
            {
                using var cmd = connection.CreateCommand();
                if (connection is SqliteConnection)
                {
                    cmd.CommandText = "SELECT name FROM pragma_table_info(@t)";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@t";
                    p.Value = table;
                    cmd.Parameters.Add(p);
                }
                else
                {
                    cmd.CommandText = "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND TABLE_SCHEMA = DATABASE()";
                    var p = cmd.CreateParameter();
                    p.ParameterName = "@t";
                    p.Value = table;
                    cmd.Parameters.Add(p);
                }
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    cols.Add(reader.GetString(0));
                }
            }
            catch { }
            return cols;
        }

        // قراءة علامة مائية محلية (مثال: وقت آخر سحب) من AppSettings المحلية (SQLite)
        private string ReadLocalMarker(string key)
        {
            try
            {
                using var localConn = _dbService.CreateLocalConnection();
                var v = localConn.QueryFirstOrDefault<string>("SELECT `Value` FROM AppSettings WHERE `Key` = @K", new { K = key });
                return v ?? "";
            }
            catch { return ""; }
        }

        // تسجيل وقت آخر رفع في AppSettings المحلية (SQLite) — معلومات فقط
        private void WriteLocalMarker(string key, string value)
        {
            try
            {
                using var localConn = _dbService.CreateLocalConnection();
                var affected = localConn.Execute("UPDATE AppSettings SET `Value` = @V WHERE `Key` = @K", new { V = value, K = key });
                if (affected == 0)
                    localConn.Execute("INSERT INTO AppSettings (`Key`, `Value`) VALUES (@K, @V)", new { V = value, K = key });
            }
            catch { }
        }
    }
}
