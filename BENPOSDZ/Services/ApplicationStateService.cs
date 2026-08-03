namespace BENPOSDZ.Services
{
    // 1. واجهة الخدمة (Loose Coupling)
    public interface IApplicationStateService
    {
        string ConnectionStatus { get; }
        event Action? OnConnectionStateChanged;
        void SetConnectionStatus(string status);

        // خصائص كاش أنواع المنتجات
        List<ProductTypeModel> CachedProductTypes { get; }
        event Action? OnProductTypesChanged;
        void RefreshProductTypes(List<ProductTypeModel> types);
    }

    // 2. التنفيذ (Thread-Safe Event Invocation)
    public class ApplicationStateService : IApplicationStateService
    {
        private string _connectionStatus = "🟢 متصل بقاعدة محلية (SQLite)";
        private readonly object _lock = new();

        public string ConnectionStatus => _connectionStatus;
        public event Action? OnConnectionStateChanged;

        public void SetConnectionStatus(string status)
        {
            lock (_lock)
            {
                if (_connectionStatus == status) return;
                _connectionStatus = status;
            }
            OnConnectionStateChanged?.Invoke();
        }

        // --- كاش أنواع المنتجات ---
        private List<ProductTypeModel> _cachedProductTypes = new();
        public List<ProductTypeModel> CachedProductTypes => _cachedProductTypes;
        public event Action? OnProductTypesChanged;

        public void RefreshProductTypes(List<ProductTypeModel> types)
        {
            _cachedProductTypes = types;
            OnProductTypesChanged?.Invoke();
        }
    }

    // 3. موديل نوع المنتج (ليكون متاحاً للواجهات الأخرى)
    public class ProductTypeModel
    {
        public string Id { get; set; } = "";
        public string Type_Name { get; set; } = "";
    }
}