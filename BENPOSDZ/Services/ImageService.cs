using Microsoft.Maui.Storage;

namespace BENPOSDZ.Services
{
    public class ImageService
    {
        private readonly string _imagesFolder;

        public ImageService()
        {
            // مسار آمن لحفظ الصور يعمل على ويندوز وأندرويد
            _imagesFolder = Path.Combine(FileSystem.AppDataDirectory, "ProductImages");
            if (!Directory.Exists(_imagesFolder))
            {
                Directory.CreateDirectory(_imagesFolder);
            }
        }

        // حفظ الصورة (من Base64 إلى ملف) وإرجاع المسار
        public string SaveImageFromBase64(string base64String, string productId)
        {
            if (string.IsNullOrEmpty(base64String)) return "";

            try
            {
                // إزالة البادئة (data:image/jpeg;base64,)
                var base64Data = base64String.Contains(',') ? base64String.Split(',')[1] : base64String;
                byte[] imageBytes = Convert.FromBase64String(base64Data);
                
                string fileName = $"product_{productId}.jpg";
                string filePath = Path.Combine(_imagesFolder, fileName);
                
                File.WriteAllBytes(filePath, imageBytes);
                return filePath; // نعيد المسار الفعلي للملف
            }
            catch { return ""; }
        }

        // قراءة الصورة من الملف وإرجاعها كـ Base64 للعرض في الواجهة
        public string GetImageAsBase64(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) 
                return "https://via.placeholder.com/40"; // صورة افتراضية إذا لم يوجد الملف

            try
            {
                byte[] imageBytes = File.ReadAllBytes(filePath);
                return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
            }
            catch { return "https://via.placeholder.com/40"; }
        }
    }
}