using Dapper;
using System.IO.Compression;
using System.Text;
using ZXing;

namespace BENPOSDZ.Services
{
    // خيارات طباعة الباركود (تُحفظ في إعدادات التطبيق AppSettings)
    public class BarcodePrintOptions
    {
        public string Type { get; set; } = "Code128";   // Code128 | EAN-13 | QR Code
        public string Size { get; set; } = "Medium";     // Small | Medium | Large
        public bool ShowName { get; set; } = true;
        public bool ShowPrice { get; set; } = true;
        public bool ShowQuantity { get; set; } = false;
        public bool ShowHigherPrice { get; set; } = false;
        public int Copies { get; set; } = 1;
    }

    // بيانات منتج يُطبع باركوده (تمرر من نقاط الاستدعاء)
    public class BarcodePrintData
    {
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public decimal? Price { get; set; }
        public decimal? HigherPrice { get; set; }
        public decimal? Quantity { get; set; }
    }

    // توليد الباركود محلياً عبر ZXing (بدون أي خدمة خارجية) وترميزه كصورة PNG مضمّنة Base64
    public class BarcodeService
    {
        private readonly DatabaseService _db;

        public BarcodeService(DatabaseService db) => _db = db;

        public BarcodePrintOptions LoadOptions()
        {
            var opts = new BarcodePrintOptions();
            try
            {
                using var conn = _db.CreateLocalConnection();
                var rows = conn.Query("SELECT `Key`, `Value` FROM AppSettings WHERE `Key` IN ('BarcodeType','BarcodeSize','BarcodeShowName','BarcodeShowPrice','BarcodeShowQuantity','BarcodeShowHigherPrice','BarcodeCopies')");
                foreach (var r in rows)
                {
                    string key = (string)r.Key;
                    string val = (string)r.Value;
                    switch (key)
                    {
                        case "BarcodeType":
                            if (val == "EAN-13" || val == "QR Code" || val == "Code128") opts.Type = val;
                            break;
                        case "BarcodeSize":
                            if (val == "Small" || val == "Large") opts.Size = val;
                            break;
                        case "BarcodeShowName":
                            opts.ShowName = val != "false";
                            break;
                        case "BarcodeShowPrice":
                            opts.ShowPrice = val != "false";
                            break;
                        case "BarcodeShowQuantity":
                            opts.ShowQuantity = val == "true";
                            break;
                        case "BarcodeShowHigherPrice":
                            opts.ShowHigherPrice = val == "true";
                            break;
                        case "BarcodeCopies" when int.TryParse(val, out int c) && c >= 1 && c <= 5:
                            opts.Copies = c;
                            break;
                    }
                }
            }
            catch { }
            return opts;
        }

        // تحويل الإعداد إلى أبعاد البكسل الفعلية حسب النوع والحجم
        public static (int Width, int Height) GetSizePixels(string size, string type)
        {
            bool qr = type == "QR Code";
            int w = size switch { "Small" => qr ? 120 : 160, "Large" => qr ? 240 : 300, _ => qr ? 160 : 220 };
            int h = size switch { "Small" => qr ? 120 : 60, "Large" => qr ? 240 : 110, _ => qr ? 160 : 80 };
            return (w, h);
        }

        // توليد صورة الباركود محلياً (Code128 / EAN-13 / QR) وإرجاعها كـ Data URI (Base64 PNG)
        public string GenerateBarcodeDataUri(string code, string type, int width, int height)
        {
            BarcodeFormat format = type switch
            {
                "EAN-13" => BarcodeFormat.EAN_13,
                "QR Code" => BarcodeFormat.QR_CODE,
                _ => BarcodeFormat.CODE_128
            };
            // EAN-13 يحتاج 12 أو 13 رقماً فقط — وإلا نرجع لـ Code128
            if (format == BarcodeFormat.EAN_13 && !(code.All(char.IsDigit) && code.Length is 12 or 13))
                format = BarcodeFormat.CODE_128;

            try
            {
                return Encode(format, code, width, height);
            }
            catch
            {
                return Encode(BarcodeFormat.CODE_128, code, width, height);
            }
        }

        private static string Encode(BarcodeFormat format, string code, int width, int height)
        {
            var writer = new BarcodeWriterPixelData
            {
                Format = format,
                Options = new ZXing.Common.EncodingOptions
                {
                    Width = width,
                    Height = height,
                    Margin = format == BarcodeFormat.QR_CODE ? 1 : 4,
                    PureBarcode = true
                }
            };
            var pixelData = writer.Write(code);
            byte[] png = PngEncoder.EncodeRgb(pixelData.Pixels, pixelData.Width, pixelData.Height);
            return "data:image/png;base64," + Convert.ToBase64String(png);
        }

        // بناء مستند الطباعة الكامل (ملصقات الباركود)
        public string BuildPrintDocument(string name, string code, decimal? price, decimal? higherPrice, decimal? quantity, BarcodePrintOptions opts, string imageUri)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html dir=\"rtl\" lang=\"ar\"><head><meta charset=\"utf-8\"/><title>باركود</title><style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 0 auto; padding: 10mm; direction: rtl; text-align: center; }");
            sb.AppendLine(".barcode-label { display: inline-block; text-align: center; border: 1px dashed #999; border-radius: 6px; padding: 10px 14px; margin: 6px; page-break-inside: avoid; vertical-align: top; }");
            sb.AppendLine(".barcode-name { font-size: 14px; font-weight: bold; margin-bottom: 4px; max-width: 240px; }");
            sb.AppendLine(".barcode-img { max-width: 100%; height: auto; display: block; margin: 0 auto; }");
            sb.AppendLine(".barcode-code { font-size: 13px; letter-spacing: 1px; margin-top: 3px; direction: ltr; word-break: break-all; }");
            sb.AppendLine(".barcode-price { font-size: 15px; font-weight: bold; color: #000; margin-top: 3px; }");
            sb.AppendLine(".barcode-higher { font-size: 13px; color: #333; margin-top: 2px; }");
            sb.AppendLine(".barcode-qty { font-size: 13px; color: #333; margin-top: 2px; }");
            sb.AppendLine("</style></head><body>");

            string nameBlock = opts.ShowName && !string.IsNullOrWhiteSpace(name)
                ? $"<div class=\"barcode-name\">{System.Net.WebUtility.HtmlEncode(name)}</div>"
                : "";
            string priceBlock = opts.ShowPrice && price.HasValue
                ? $"<div class=\"barcode-price\">السعر: {price.Value:0.00} د.ج</div>"
                : "";
            string higherBlock = opts.ShowHigherPrice && higherPrice.HasValue
                ? $"<div class=\"barcode-higher\">سعر التجزئة: {higherPrice.Value:0.00} د.ج</div>"
                : "";
            string qtyBlock = opts.ShowQuantity && quantity.HasValue
                ? $"<div class=\"barcode-qty\">الكمية: {quantity.Value:0.##}</div>"
                : "";

            for (int i = 0; i < opts.Copies; i++)
            {
                sb.AppendLine("<div class=\"barcode-label\">");
                sb.AppendLine(nameBlock);
                sb.AppendLine($"<img class=\"barcode-img\" src=\"{imageUri}\" alt=\"barcode\" />");
                sb.AppendLine($"<div class=\"barcode-code\">{System.Net.WebUtility.HtmlEncode(code)}</div>");
                sb.AppendLine(priceBlock);
                sb.AppendLine(higherBlock);
                sb.AppendLine(qtyBlock);
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }
    }

    // مشفّر PNG مستقل (RGB) — بدون الاعتماد على System.Drawing أو SkiaSharp
    internal static class PngEncoder
    {
        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static uint Crc32(byte[] data, int offset, int length)
        {
            uint c = 0xFFFFFFFF;
            for (int i = 0; i < length; i++)
                c = CrcTable[(c ^ data[offset + i]) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFF;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static void WriteUint32BE(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)v);
        }

        private static void WriteChunk(Stream s, string type, byte[] data)
        {
            WriteUint32BE(s, (uint)data.Length);
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            s.Write(typeBytes, 0, 4);
            s.Write(data, 0, data.Length);

            var crcInput = new byte[4 + data.Length];
            typeBytes.CopyTo(crcInput, 0);
            data.CopyTo(crcInput, 4);
            WriteUint32BE(s, Crc32(crcInput, 0, crcInput.Length));
        }

        private static byte[] ZlibCompress(byte[] raw)
        {
            using var deflate = new MemoryStream();
            using (var ds = new DeflateStream(deflate, CompressionLevel.Optimal, true))
                ds.Write(raw, 0, raw.Length);

            byte[] deflated = deflate.ToArray();
            var zlib = new MemoryStream();
            zlib.WriteByte(0x78);
            zlib.WriteByte(0x9C);
            zlib.Write(deflated, 0, deflated.Length);
            uint adler = Adler32(raw);
            WriteUint32BE(zlib, adler);
            return zlib.ToArray();
        }

        // rgb: 3 بايت لكل بكسل (R,G,B) — color type 2
        public static byte[] EncodeRgb(byte[] rgb, int width, int height)
        {
            using var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

            var ihdrData = new byte[13];
            ihdrData[0] = (byte)(width >> 24);
            ihdrData[1] = (byte)(width >> 16);
            ihdrData[2] = (byte)(width >> 8);
            ihdrData[3] = (byte)width;
            ihdrData[4] = (byte)(height >> 24);
            ihdrData[5] = (byte)(height >> 16);
            ihdrData[6] = (byte)(height >> 8);
            ihdrData[7] = (byte)height;
            ihdrData[8] = 8;   // bit depth
            ihdrData[9] = 2;   // color type: truecolor RGB
            ihdrData[10] = 0;  // compression
            ihdrData[11] = 0;  // filter
            ihdrData[12] = 0;  // interlace
            WriteChunk(ms, "IHDR", ihdrData);

            int stride = width * 3;
            var raw = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int rowStart = y * (stride + 1);
                raw[rowStart] = 0; // filter: none
                Buffer.BlockCopy(rgb, y * stride, raw, rowStart + 1, stride);
            }
            WriteChunk(ms, "IDAT", ZlibCompress(raw));
            WriteChunk(ms, "IEND", Array.Empty<byte>());
            return ms.ToArray();
        }
    }
}
