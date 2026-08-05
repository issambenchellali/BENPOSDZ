using Dapper;

namespace BENPOSDZ.Services
{
    // خدمة التدقيق المالي: تتحقق من صحة إجماليات الفواتير والمدفوعات والديون،
    // وتحسب إجمالي المبيعات والمشتريات، وتصحح الأخطاء التي تجدها.
    public class AuditService
    {
        private readonly DatabaseService _db;

        public AuditService(DatabaseService db) => _db = db;

        // 1. الفواتير التي لا يطابق مجموع تفاصيلها سعر الفاتورة
        public async Task<List<string>> FindInvoiceTotalMismatchesAsync()
        {
            using var connection = _db.CreateConnection();
            var rows = await connection.QueryAsync<dynamic>(@"
                SELECT o.Id, o.Price AS OrderPrice, COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) AS DetailsTotal
                FROM Orders o LEFT JOIN Order_Details od ON od.Order_ID = o.Id AND od.IsDeleted = 0
                WHERE o.IsDeleted = 0
                GROUP BY o.Id, o.Price
                HAVING ABS(COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) - o.Price) > 0.01");
            return rows.Select(m => $"الفاتورة {((string)m.Id).Substring(0, 8)}... سعرها {(decimal)m.OrderPrice} لكن مجموع تفاصيلها {(decimal)m.DetailsTotal}").ToList();
        }

        // 2. الفواتير التي لا يطابق (المدفوع + الدين) سعرها
        public async Task<List<string>> FindPaymentDebtIssuesAsync()
        {
            using var connection = _db.CreateConnection();
            var rows = await connection.QueryAsync<dynamic>(@"
                SELECT Id, Price, Paid, Unpaid FROM Orders
                WHERE IsDeleted = 0 AND ABS((COALESCE(Paid, 0) + COALESCE(Unpaid, 0)) - Price) > 0.01");
            return rows.Select(m => $"الفاتورة {((string)m.Id).Substring(0, 8)}... المدفوع+الدين ({(decimal)m.Paid}+{(decimal)m.Unpaid}) لا يساوي السعر {(decimal)m.Price}").ToList();
        }

        // 3. عدد الفواتير بدون أي تفاصيل
        public async Task<int> CountInvoicesWithoutDetailsAsync()
        {
            using var connection = _db.CreateConnection();
            return await connection.ExecuteScalarAsync<int>(@"
                SELECT COUNT(*) FROM Orders o WHERE o.IsDeleted = 0
                AND NOT EXISTS (SELECT 1 FROM Order_Details od WHERE od.Order_ID = o.Id AND od.IsDeleted = 0)");
        }

        // 4. إجمالي المبيعات والمشتريات وعدد الفواتير
        public async Task<(decimal Sales, decimal Purchases, int Invoices)> GetTotalsAsync()
        {
            using var connection = _db.CreateConnection();
            decimal sales = await connection.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Price),0) FROM Orders WHERE Order_Type IN (0,1) AND IsDeleted = 0");
            decimal purchases = await connection.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(Price),0) FROM Orders WHERE Order_Type = 2 AND IsDeleted = 0");
            int invoices = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM Orders WHERE IsDeleted = 0");
            return (sales, purchases, invoices);
        }

        // تشغيل الفحص الكامل وإرجاع التقرير
        public async Task<FinancialAuditResult> RunFullAuditAsync()
        {
            var result = new FinancialAuditResult();
            try
            {
                foreach (var issue in await FindInvoiceTotalMismatchesAsync())
                {
                    result.Issues.Add("⚠️ " + issue);
                    result.TotalIssues++;
                }

                foreach (var issue in await FindPaymentDebtIssuesAsync())
                {
                    result.Issues.Add("⚠️ " + issue);
                    result.TotalIssues++;
                }

                int noDetails = await CountInvoicesWithoutDetailsAsync();
                if (noDetails > 0)
                {
                    result.Issues.Add($"⚠️ توجد {noDetails} فاتورة بدون أي تفاصيل.");
                    result.TotalIssues += noDetails;
                }

                var totals = await GetTotalsAsync();
                result.SalesTotal = totals.Sales;
                result.PurchaseTotal = totals.Purchases;
                result.InvoicesChecked = totals.Invoices;

                if (result.TotalIssues == 0)
                    result.Issues.Add("✅ كل الحسابات المالية دقيقة، لا توجد أي مشاكل.");
            }
            catch (Exception ex)
            {
                result.Issues.Add("❌ فشل التدقيق: " + ex.Message);
                result.TotalIssues++;
                _db.LogEvent($"❌ فشل التدقيق المالي: {ex.Message}");
            }
            return result;
        }

        // تصحيح تلقائي: تحديث سعر الفاتورة ليطابق مجموع تفاصيلها
        public async Task<int> FixInvoiceTotalsAsync()
        {
            int fixedCount = 0;
            try
            {
                using var connection = _db.CreateConnection();
                using var transaction = connection.BeginTransaction();
                var rows = await connection.QueryAsync<dynamic>(@"
                    SELECT o.Id, COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) AS DetailsTotal
                    FROM Orders o LEFT JOIN Order_Details od ON od.Order_ID = o.Id AND od.IsDeleted = 0
                    WHERE o.IsDeleted = 0
                    GROUP BY o.Id, o.Price
                    HAVING ABS(COALESCE(SUM(od.Pro_Qty * od.Pro_Price), 0) - o.Price) > 0.01", transaction);
                foreach (var r in rows)
                {
                    await connection.ExecuteAsync("UPDATE Orders SET Price = @P WHERE Id = @Id",
                        new { P = (decimal)r.DetailsTotal, Id = (string)r.Id }, transaction);
                    fixedCount++;
                }
                transaction.Commit();
                if (fixedCount > 0)
                    _db.LogEvent($"🔧 تم تصحيح سعر {fixedCount} فاتورة لتطابق مجموع تفاصيلها.");
            }
            catch (Exception ex)
            {
                _db.LogEvent($"❌ فشل تصحيح إجماليات الفواتير: {ex.Message}");
            }
            return fixedCount;
        }
    }
}
