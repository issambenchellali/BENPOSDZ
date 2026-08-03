namespace BENPOSDZ.Services
{
    public class ToastService
    {
        public string Title { get; private set; } = "";
        public string Message { get; private set; } = "";
        public bool IsVisible { get; private set; } = false;
        public string BackgroundColor { get; private set; } = "#28a745";

        public event Action? OnChange;

        public void ShowSuccess(string message, string? title = null) => Show(title ?? "✅ تم بنجاح", message, "#28a745", 3000);

        public void ShowSuccessShort(string message, string? title = null) => Show(title ?? "✅ تم بنجاح", message, "#28a745", 1800);

        public void ShowError(string message, string? title = null) => Show(title ?? "❌ خطأ", message, "#dc3545", 5000);

        public void ShowWarning(string message, string? title = null) => Show(title ?? "⚠️ تنبيه", message, "#ffc107", 4000);

        private void Show(string title, string message, string bg, int ms)
        {
            Title = title;
            Message = message;
            BackgroundColor = bg;
            IsVisible = true;
            OnChange?.Invoke();
            Task.Delay(ms).ContinueWith(_ => Hide());
        }

        private void Hide()
        {
            IsVisible = false;
            OnChange?.Invoke();
        }
    }
}