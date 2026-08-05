using Dapper;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Threading.Tasks;

namespace BENPOSDZ.Services
{
    // استيراد المنتجات والأنواع من قاعدة بيانات قديمة (ملف SQLite .db) إلى القاعدة الحالية
    // يعمل بالدمج: يُضيف المنتجات والأنواع الجديدة فقط، ولا يمسح أو يعدّل البيانات الموجودة
    public class ImportService
    {
        private readonly DatabaseService _db;

        public ImportService(DatabaseService db) => _db = db;

        public class ImportResult
        {
            public int Imported { get; set; }
            public int Skipped { get; set; }
            public int TypesImported { get; set; }
        }

        public async Task<ImportResult> ImportProductsFromDatabaseAsync(Stream fileStream)
        {
            var result = new ImportResult();
            string tmp = Path.Combine(Path.GetTempPath(), $"benpos_import_{Guid.NewGuid():N}.db");
            try
            {
                using (var fs = File.Create(tmp))
                    await fileStream.CopyToAsync(fs);

                using var old = new SqliteConnection($"Data Source={tmp}");
                old.Open();

                // الأنواع من القاعدة القديمة (مع تجاهل التكرار)
                // بعض القواعد القديمة لا تحتوي على جدول Product_Types — لا تفشل الاستيراد بسبب ذلك
                var oldTypes = new List<dynamic>();
                try
                {
                    bool hasTypes = await old.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Product_Types'") > 0;
                    if (hasTypes)
                        oldTypes = (await old.QueryAsync<dynamic>("SELECT * FROM Product_Types")).ToList();
                    else
                        _db.LogEvent("ℹ️ القاعدة القديمة لا تحتوي على جدول Product_Types — تم تخطي استيراد الأنواع.");
                }
                catch (Exception ex)
                {
                    _db.LogEvent($"⚠️ تعذر قراءة أنواع القاعدة القديمة: {ex.Message}");
                }

                using var current = _db.CreateConnection();
                string dateNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                foreach (var t in oldTypes)
                {
                    var td = (IDictionary<string, object?>)t;
                    if (!td.TryGetValue("Id", out var idObj) || idObj == null) continue;
                    string id = idObj.ToString()!;
                    string name = td.TryGetValue("Type_Name", out var nm) ? nm?.ToString() ?? "" : "";
                    int exists = await current.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Product_Types WHERE Id = @Id", new { Id = id });
                    if (exists == 0 && !string.IsNullOrWhiteSpace(name))
                    {
                        await current.ExecuteAsync(@"INSERT INTO Product_Types (Id, Type_Name, UpdatedAt, IsSynced, IsDeleted) VALUES (@Id, @Name, @Date, 0, 0)",
                            new { Id = id, Name = name, Date = dateNow });
                        result.TypesImported++;
                    }
                }

                var oldProducts = new List<dynamic>();
                try
                {
                    oldProducts = (await old.QueryAsync<dynamic>("SELECT * FROM Products")).ToList();
                }
                catch (Exception ex)
                {
                    _db.LogEvent($"⚠️ تعذر قراءة منتجات القاعدة القديمة: {ex.Message}");
                }

                foreach (var p in oldProducts)
                {
                    var d = (IDictionary<string, object?>)p;
                    if (!d.TryGetValue("Id", out var idObj) || idObj == null) continue;
                    string id = idObj.ToString()!;
                    string name = d.TryGetValue("Pro_Name", out var nm) ? nm?.ToString() ?? "" : "";
                    string barcode = d.TryGetValue("Pro_Barcode", out var bc) ? bc?.ToString() ?? "" : "";
                    string reference = d.TryGetValue("Pro_Ref", out var rf) ? rf?.ToString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // تجنب التكرار: معرّف موجود، أو باركود مكرر، أو مرجع مكرر في المنتجات غير المحذوفة
                    int dupId = await current.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Id = @Id", new { Id = id });
                    if (dupId > 0) { result.Skipped++; continue; }
                    if (!string.IsNullOrWhiteSpace(barcode))
                    {
                        int dupBc = await current.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Pro_Barcode = @Bc AND Pro_Barcode != '' AND IsDeleted = 0", new { Bc = barcode });
                        if (dupBc > 0) { result.Skipped++; continue; }
                    }
                    if (!string.IsNullOrWhiteSpace(reference))
                    {
                        int dupRef = await current.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Products WHERE Pro_Ref = @Ref AND Pro_Ref != '' AND IsDeleted = 0", new { Ref = reference });
                        if (dupRef > 0) { result.Skipped++; continue; }
                    }

                    string finalId = dupId > 0 ? Guid.NewGuid().ToString() : id;

                    await current.ExecuteAsync(@"INSERT INTO Products
                        (Id, Pro_Name, Pro_Ref, Pro_Mark, Pro_Propr, Pro_BuyPrice, Pro_SalePriceG, Pro_SalePrice_Min, Pro_SalePrice_Max,
                         Pro_Qty, Pro_QtyMin, Pro_Unit, Pro_Barcode, Pro_Image, Pro_date_exp, Pro_Type_ID, Pro_Qty_Inv, Pro_ImageUrl,
                         UpdatedAt, IsSynced, IsDeleted)
                        VALUES (@Id, @Pro_Name, @Pro_Ref, @Pro_Mark, @Pro_Propr, @Pro_BuyPrice, @Pro_SalePriceG, @Pro_SalePrice_Min, @Pro_SalePrice_Max,
                         @Pro_Qty, @Pro_QtyMin, @Pro_Unit, @Pro_Barcode, @Pro_Image, @Pro_date_exp, @Pro_Type_ID, @Pro_Qty_Inv, @Pro_ImageUrl,
                         @Date, 0, 0)",
                        new
                        {
                            Id = finalId,
                            Pro_Name = name,
                            Pro_Ref = reference,
                            Pro_Barcode = barcode,
                            Pro_Mark = GetStr(d, "Pro_Mark"),
                            Pro_Propr = GetStr(d, "Pro_Propr"),
                            Pro_BuyPrice = GetDec(d, "Pro_BuyPrice"),
                            Pro_SalePriceG = GetDec(d, "Pro_SalePriceG"),
                            Pro_SalePrice_Min = GetDec(d, "Pro_SalePrice_Min"),
                            Pro_SalePrice_Max = GetDec(d, "Pro_SalePrice_Max"),
                            Pro_Qty = GetDec(d, "Pro_Qty"),
                            Pro_QtyMin = GetDec(d, "Pro_QtyMin"),
                            Pro_Unit = GetStr(d, "Pro_Unit"),
                            Pro_Image = GetStr(d, "Pro_Image"),
                            Pro_date_exp = GetStr(d, "Pro_date_exp"),
                            Pro_Type_ID = GetStr(d, "Pro_Type_ID"),
                            Pro_Qty_Inv = GetDec(d, "Pro_Qty_Inv"),
                            Pro_ImageUrl = GetStr(d, "Pro_ImageUrl"),
                            Date = dateNow
                        });
                    result.Imported++;
                }

                _db.LogEvent($"📦 تم استيراد {result.Imported} منتجاً ({result.Skipped} مكرراً) و{result.TypesImported} نوعاً من قاعدة قديمة.");
                return result;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }

        private static string? GetStr(IDictionary<string, object?> d, string key)
            => d.TryGetValue(key, out var v) ? v?.ToString() : null;

        private static decimal GetDec(IDictionary<string, object?> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return 0;
            return decimal.TryParse(v.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dec) ? dec : 0;
        }
    }
}
