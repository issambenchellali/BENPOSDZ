using Dapper;
using System.Text;
using System.Text.Json;

namespace BENPOSDZ.Services
{
    // محرك قوالب الطباعة المرئي: فاتورة / بون تسليم / وصل
    // القالب مجموعة أقسام (sections) تُظهر/تُخفى وتُعاد ترتيبها بصرياً دون أي كود HTML
    // تخطيط كل نوع يُحفظ كملف JSON، والطباعة تُبنى تلقائياً حسب ترتيب الأقسام
    public class PrintTemplateService
    {
        private readonly DatabaseService _dbService;
        private readonly string _templatesDir;

        public PrintTemplateService(DatabaseService dbService)
        {
            _dbService = dbService;
            string folderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BENPOSDZ", "Templates");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
            _templatesDir = folderPath;
        }

        public record PrintItem(string Name, decimal Qty, decimal Price, decimal Total);

        public class PrintData
        {
            public string DocType { get; set; } = "invoice"; // receipt | invoice | delivery
            public string Title { get; set; } = "فاتورة";
            public string InvoiceNumber { get; set; } = "";
            public string Date { get; set; } = "";
            public string Customer { get; set; } = "Au Comptoir";
            public string Cashier { get; set; } = "";
            public List<PrintItem> Items { get; set; } = new();
            public decimal Total { get; set; }
            public decimal Paid { get; set; }
            public decimal Debt { get; set; }
            public string StoreName { get; set; } = "";
            public string StorePhone { get; set; } = "";
            public string StoreAddress { get; set; } = "";
            public string StoreNIF { get; set; } = "";
            public string StoreRC { get; set; } = "";
            public string Header { get; set; } = "";
            public string Footer { get; set; } = "";
        }

        // قسم من القالب
        public class TemplateSection
        {
            public string Type { get; set; } = ""; // store | title | meta | items | totals | signatures | header | footer | note
            public bool Enabled { get; set; } = true;
            public string Text { get; set; } = ""; // نص الملاحظة المخصصة (note فقط)
        }

        // تخطيط القالب (ترتيب الأقسام)
        public class TemplateLayout
        {
            public string DocType { get; set; } = "invoice";
            public List<TemplateSection> Sections { get; set; } = new();
        }

        public TemplateLayout GetLayout(string docType)
        {
            string path = LayoutPath(docType);
            if (File.Exists(path))
            {
                try
                {
                    var layout = JsonSerializer.Deserialize<TemplateLayout>(File.ReadAllText(path));
                    if (layout != null && layout.Sections != null && layout.Sections.Count > 0) return layout;
                }
                catch { }
            }
            return DefaultLayout(docType);
        }

        public void SaveLayout(string docType, TemplateLayout layout)
        {
            layout.DocType = docType;
            File.WriteAllText(LayoutPath(docType), JsonSerializer.Serialize(layout));
            _dbService.LogEvent($"📄 تم حفظ قالب الطباعة ({docType}).");
        }

        public void ResetLayout(string docType)
        {
            string path = LayoutPath(docType);
            if (File.Exists(path)) File.Delete(path);
            _dbService.LogEvent($"♻️ تمت استعادة القالب الافتراضي ({docType}).");
        }

        private string LayoutPath(string docType) => Path.Combine(_templatesDir, "layout_" + docType + ".json");

        // التخطيط الافتراضي لكل نوع وثيقة
        private static TemplateLayout DefaultLayout(string docType)
        {
            var layout = new TemplateLayout { DocType = docType };
            layout.Sections.Add(new TemplateSection { Type = "store" });
            layout.Sections.Add(new TemplateSection { Type = "title" });
            layout.Sections.Add(new TemplateSection { Type = "meta" });
            layout.Sections.Add(new TemplateSection { Type = "header" });
            layout.Sections.Add(new TemplateSection { Type = "items" });
            layout.Sections.Add(new TemplateSection { Type = "totals" });
            if (docType == "delivery")
                layout.Sections.Add(new TemplateSection { Type = "signatures" });
            layout.Sections.Add(new TemplateSection { Type = "footer" });
            return layout;
        }

        // ملء بيانات المتجر والإعدادات من AppSettings
        public PrintData LoadStoreDefaults(PrintData data)
        {
            try
            {
                using var conn = _dbService.CreateLocalConnection();
                var settings = conn.Query<dynamic>("SELECT * FROM AppSettings").ToDictionary(x => (string)x.Key, x => (string)x.Value);
                string Get(string k) => settings.ContainsKey(k) ? settings[k] : "";
                data.StoreName = Get("StoreName");
                data.StorePhone = Get("StorePhone");
                data.StoreAddress = Get("StoreAddress");
                data.StoreNIF = Get("StoreNIF");
                data.StoreRC = Get("StoreRC");
                data.Header = Get("ReceiptHeader");
                data.Footer = Get("ReceiptFooter");
            }
            catch { }
            return data;
        }

        public string Render(string docType, PrintData data) => ApplyLayout(GetLayout(docType), docType, data);

        public string RenderFullDocument(string docType, PrintData data) => Render(docType, LoadStoreDefaults(data));

        // معاينة قالب مُحرَّر في الذاكرة دون الحاجة للحفظ (المعاينة الحية والطباعة التجريبية)
        public string RenderLayout(string docType, TemplateLayout layout, PrintData data) => ApplyLayout(layout, docType, data);

        private string ApplyLayout(TemplateLayout layout, string docType, PrintData data)
        {
            var sb = new StringBuilder();
            foreach (var section in layout.Sections)
            {
                if (!section.Enabled) continue;
                string html = BuildSectionHtml(section, docType, data);
                if (!string.IsNullOrWhiteSpace(html)) sb.AppendLine(html);
            }
            return Wrap(sb.ToString(), docType);
        }

        private string BuildSectionHtml(TemplateSection section, string docType, PrintData data)
        {
            switch (section.Type)
            {
                case "store":
                    return docType == "receipt" ? BuildStoreReceipt(data) : BuildStoreFull(data);
                case "title":
                    string title = !string.IsNullOrWhiteSpace(section.Text) ? section.Text : data.Title;
                    return $"<h3 class=\"doc-title\">{Escape(title)}</h3>";
                case "meta":
                    return BuildMeta(docType, data);
                case "items":
                    return BuildItemsTable(docType, data);
                case "totals":
                    return BuildTotals(docType, data);
                case "signatures":
                    return "<div class=\"doc-sig\"><span>توقيع البائع: ........................</span><span>توقيع الزبون: ........................</span></div>";
                case "header":
                    string headerText = !string.IsNullOrWhiteSpace(section.Text) ? section.Text : data.Header;
                    return string.IsNullOrWhiteSpace(headerText) ? "" : $"<p class=\"doc-header\">{headerText}</p>";
                case "footer":
                    string footerText = !string.IsNullOrWhiteSpace(section.Text) ? section.Text : data.Footer;
                    return string.IsNullOrWhiteSpace(footerText) ? "" : $"<p class=\"doc-footer\">{footerText}</p>";
                case "note":
                    return string.IsNullOrWhiteSpace(section.Text) ? "" : $"<p class=\"doc-note\">{Escape(section.Text)}</p>";
                default:
                    return "";
            }
        }

        private string BuildStoreReceipt(PrintData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"doc-store\">");
            sb.AppendLine($"<h2>{Escape(data.StoreName)}</h2>");
            if (!string.IsNullOrWhiteSpace(data.StoreAddress)) sb.AppendLine($"<p>{Escape(data.StoreAddress)}</p>");
            if (!string.IsNullOrWhiteSpace(data.StorePhone)) sb.AppendLine($"<p>الهاتف: {Escape(data.StorePhone)}</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private string BuildStoreFull(PrintData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"doc-store\">");
            sb.AppendLine($"<h2>{Escape(data.StoreName)}</h2>");
            if (!string.IsNullOrWhiteSpace(data.StoreAddress)) sb.AppendLine($"<p>{Escape(data.StoreAddress)}</p>");
            string line = "";
            if (!string.IsNullOrWhiteSpace(data.StorePhone)) line += "الهاتف: " + Escape(data.StorePhone);
            if (!string.IsNullOrWhiteSpace(data.StoreNIF)) line += (line.Length > 0 ? " | " : "") + "الرقم الجبائي: " + Escape(data.StoreNIF);
            if (!string.IsNullOrWhiteSpace(data.StoreRC)) line += (line.Length > 0 ? " | " : "") + "السجل التجاري: " + Escape(data.StoreRC);
            if (line.Length > 0) sb.AppendLine($"<p>{line}</p>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private string BuildMeta(string docType, PrintData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"doc-meta\"><span>رقم: " + Escape(data.InvoiceNumber) + "</span><span>التاريخ: " + Escape(data.Date) + "</span></div>");
            if (docType == "receipt")
                sb.AppendLine("<div class=\"doc-meta\"><span>الزبون: " + Escape(data.Customer) + "</span></div>");
            else
                sb.AppendLine("<div class=\"doc-meta\"><span>الزبون: " + Escape(data.Customer) + "</span><span>أمين الصندوق: " + Escape(data.Cashier) + "</span></div>");
            return sb.ToString();
        }

        private string BuildItemsTable(string docType, PrintData data)
        {
            bool isDelivery = docType == "delivery";
            var sb = new StringBuilder();
            sb.AppendLine("<table class=\"items-table\"><thead><tr><th>السلعة</th><th>الكمية</th>");
            sb.AppendLine(isDelivery ? "<th>التوقيع</th>" : "<th>السعر</th><th>المجموع</th>");
            sb.AppendLine("</tr></thead><tbody>");

            foreach (var item in data.Items)
            {
                sb.AppendLine($"<tr><td>{Escape(item.Name)}</td><td>{item.Qty:0.##}</td>");
                if (isDelivery) sb.AppendLine("<td></td>");
                else sb.AppendLine($"<td>{item.Price:0.00}</td><td>{item.Total:0.00}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");
            return sb.ToString();
        }

        private string BuildTotals(string docType, PrintData data)
        {
            if (docType == "delivery") return "";
            var sb = new StringBuilder();
            sb.AppendLine("<div class=\"doc-totals\">");
            sb.AppendLine($"<div>المجموع: {data.Total:0.00} د.ج</div>");
            sb.AppendLine($"<div>المدفوع: {data.Paid:0.00} د.ج</div>");
            if (data.Debt > 0)
                sb.AppendLine($"<div style=\"color:red;\">الدين: {data.Debt:0.00} د.ج</div>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        // تغليف الوثيقة بالتنسيق الاحترافي
        private string Wrap(string body, string docType)
        {
            bool isReceipt = docType == "receipt";
            string width = isReceipt ? "80mm" : "210mm";
            string padding = isReceipt ? "5mm 3mm" : "20mm";
            return $@"<!DOCTYPE html>
<html dir=""rtl"" lang=""ar"">
<head>
<meta charset=""utf-8""/>
<title>طباعة</title>
<style>
body {{ font-family: 'Segoe UI', Arial, sans-serif; width: {width}; padding: {padding}; margin: 0 auto; direction: rtl; color: #000; }}
.doc-store {{ text-align: center; margin-bottom: 12px; }}
.doc-store h2 {{ margin: 0; font-size: {(isReceipt ? "16px" : "24px")}; }}
.doc-store p {{ margin: 2px 0; }}
.doc-title {{ text-align: center; margin: 10px 0 8px; }}
.doc-meta {{ display: flex; justify-content: space-between; flex-wrap: wrap; margin: 4px 0; font-size: 14px; }}
.doc-header {{ text-align: center; margin: 8px 0; font-weight: bold; }}
.doc-note {{ text-align: center; color: #555; margin: 8px 0; }}
.doc-footer {{ text-align: center; margin-top: 12px; font-weight: bold; }}
table.items-table {{ width: 100%; border-collapse: collapse; margin: 10px 0; }}
table.items-table th, table.items-table td {{ border: 1px solid #000; padding: 6px; text-align: right; }}
table.items-table th {{ background: #f2f2f2; }}
.doc-totals {{ margin-top: 8px; }}
.doc-totals div {{ text-align: left; font-weight: bold; margin: 2px 0; }}
.doc-sig {{ display: flex; justify-content: space-between; margin-top: 28px; }}
@media print {{ body {{ width: {(isReceipt ? "80mm" : "auto")}; padding: {(isReceipt ? "3mm" : "10mm")}; }} }}
</style>
</head>
<body>{body}</body>
</html>";
        }

        private static string Escape(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    }
}
