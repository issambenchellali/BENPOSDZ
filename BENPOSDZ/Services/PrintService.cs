using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BENPOSDZ.Services
{
    // خدمة الطباعة متعددة المنصات:
    //  - Windows: window.print() الحالية عبر iframe
    //  - Android: حوار طباعة النظام (PrintManager) — يشمل الطباعة الورقية وحفظ PDF
    public class PrintService
    {
        private readonly BarcodeService _barcodeService;
        private readonly DatabaseService _dbService;

        public PrintService(BarcodeService barcodeService, DatabaseService dbService)
        {
            _barcodeService = barcodeService;
            _dbService = dbService;
        }

        public async Task PrintHtmlAsync(IJSRuntime js, string html, bool isReceipt)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                _dbService.LogEvent("🖨️ محاولة طباعة بدون محتوى HTML — تم تجاهلها.", "WARN");
                return;
            }
            try
            {
#if ANDROID
                PrintOnAndroid(html);
                await Task.CompletedTask;
#else
                await js.InvokeVoidAsync("printHtml", html, isReceipt);
#endif
            }
            catch (Exception ex)
            {
                _dbService.LogEvent($"❌ فشل طباعة المستند: {ex.Message}", "ERROR");
            }
        }

        // طباعة باركود محلياً عبر ZXing (بدون أي خدمة خارجية) مع خيارات التخصيص من الإعدادات
        public async Task PrintBarcodeAsync(IJSRuntime js, BarcodePrintData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Code))
            {
                _dbService.LogEvent("🖨️ محاولة طباعة باركود بدون بيانات — تم تجاهلها.", "WARN");
                return;
            }
            try
            {
                var opts = _barcodeService.LoadOptions();
                var (w, h) = BarcodeService.GetSizePixels(opts.Size, opts.Type);
                string imageUri = _barcodeService.GenerateBarcodeDataUri(data.Code, opts.Type, w, h);
                string html = _barcodeService.BuildPrintDocument(data.Name, data.Code, data.Price, data.HigherPrice, data.Quantity, opts, imageUri);
#if ANDROID
                PrintOnAndroid(html);
                await Task.CompletedTask;
#else
                await js.InvokeVoidAsync("printHtml", html, false);
#endif
            }
            catch (Exception ex)
            {
                _dbService.LogEvent($"❌ فشل طباعة الباركود: {ex.Message}", "ERROR");
            }
        }

        // المشاركة على أندرويد (شيت المشاركة) — على ويندوز تنزيل ملف HTML
        public async Task ShareHtmlAsync(IJSRuntime js, string html, string title)
        {
            if (string.IsNullOrWhiteSpace(html))
            {
                _dbService.LogEvent("📤 محاولة مشاركة بدون محتوى — تم تجاهلها.", "WARN");
                return;
            }
            try
            {
#if ANDROID
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity == null)
                {
                    _dbService.LogEvent("📤 لا يوجد نشاط Android نشط — تعذرت المشاركة.", "WARN");
                    return;
                }
                activity.RunOnUiThread(() =>
                {
                    var intent = new Android.Content.Intent(Android.Content.Intent.ActionSend);
                    intent.SetType("text/plain");
                    intent.PutExtra(Android.Content.Intent.ExtraSubject, title);
                    intent.PutExtra(Android.Content.Intent.ExtraText, StripHtml(html));
                    activity.StartActivity(Android.Content.Intent.CreateChooser(intent, title));
                });
                await Task.CompletedTask;
#else
                await js.InvokeVoidAsync("downloadDoc", html, title.Replace(' ', '_') + ".html");
#endif
            }
            catch (Exception ex)
            {
                _dbService.LogEvent($"❌ فشل المشاركة: {ex.Message}", "ERROR");
            }
        }

#if ANDROID
        private void PrintOnAndroid(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null)
            {
                _dbService.LogEvent("🖨️ لا يوجد نشاط Android نشط — تعذرت الطباعة.", "WARN");
                return;
            }
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var webView = new Android.Webkit.WebView(activity)
                    {
                        LayoutParameters = new Android.Views.ViewGroup.LayoutParams(
                            Android.Views.ViewGroup.LayoutParams.MatchParent,
                            Android.Views.ViewGroup.LayoutParams.MatchParent)
                    };
                    webView.Settings.JavaScriptEnabled = true;
                    webView.Settings.DomStorageEnabled = true;
                    webView.Settings.BuiltInZoomControls = false;
                    // إخفاء العرض حتى لا يظهر وميض فوق الشاشة أثناء التحضير
                    webView.Visibility = Android.Views.ViewStates.Invisible;
                    webView.SetWebViewClient(new PrintWebViewClient(activity, _dbService));
                    // ربط الـ WebView بنافذة النشاط حتى يكتمل الـ Layout وتنجح الطباعة على كل الأجهزة
                    (activity.Window?.DecorView as Android.Views.ViewGroup)?.AddView(webView);
                    webView.LoadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
                }
                catch (Exception ex)
                {
                    _dbService.LogEvent($"❌ فشل تهيئة الطباعة على Android: {ex.Message}", "ERROR");
                }
            });
        }

        private class PrintWebViewClient : Android.Webkit.WebViewClient
        {
            private readonly Android.App.Activity _activity;
            private readonly DatabaseService _dbService;
            private bool _printed;

            public PrintWebViewClient(Android.App.Activity activity, DatabaseService dbService)
            {
                _activity = activity;
                _dbService = dbService;
            }

            public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
            {
                base.OnPageFinished(view, url);
                if (_printed || view == null) return;
                _printed = true;
                try
                {
                    var printManager = _activity.GetSystemService(Android.Content.Context.PrintService) as Android.Print.PrintManager;
                    if (printManager == null)
                    {
                        _dbService.LogEvent("🖨️ خدمة الطباعة غير متوفرة على هذا الجهاز.", "WARN");
                        RemoveWebView(view);
                        return;
                    }
                    var adapter = view.CreatePrintDocumentAdapter("BENPOSDZ");
                    printManager.Print("BENPOSDZ", adapter, new Android.Print.PrintAttributes.Builder().Build());
                    _dbService.LogEvent("🖨️ تم إرسال المستند إلى حوار الطباعة (Android).");
                }
                catch (Exception ex)
                {
                    _dbService.LogEvent($"❌ فشل إرسال المستند للطباعة على Android: {ex.Message}", "ERROR");
                    RemoveWebView(view);
                }
            }

            public override void OnReceivedError(Android.Webkit.WebView? view, Android.Webkit.IWebResourceRequest? request, Android.Webkit.WebResourceError? error)
            {
                base.OnReceivedError(view, request, error);
                _dbService.LogEvent($"❌ خطأ في تحميل مستند الطباعة: {error?.ErrorCode} {error?.Description}", "ERROR");
            }

            // تنظيف العرض بعد فترة كافية حتى لا يتعارض مع مهمة الطباعة الجارية
            private void RemoveWebView(Android.Webkit.WebView view)
            {
                try
                {
                    var mainLooper = Android.OS.Looper.MainLooper;
                    if (mainLooper == null) return;
                    var handler = new Android.OS.Handler(mainLooper);
                    handler.PostDelayed(() =>
                    {
                        try
                        {
                            var parent = view.Parent as Android.Views.ViewGroup;
                            parent?.RemoveView(view);
                            view.Dispose();
                        }
                        catch { }
                    }, 10000);
                }
                catch { }
            }
        }
#endif

        private static string StripHtml(string html)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
            return System.Net.WebUtility.HtmlDecode(text);
        }
    }
}
