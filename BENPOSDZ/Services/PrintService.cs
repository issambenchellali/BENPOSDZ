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

        public PrintService(BarcodeService barcodeService) => _barcodeService = barcodeService;

        public async Task PrintHtmlAsync(IJSRuntime js, string html, bool isReceipt)
        {
#if ANDROID
            PrintOnAndroid(html);
            await Task.CompletedTask;
#else
            await js.InvokeVoidAsync("printHtml", html, isReceipt);
#endif
        }

        // طباعة باركود محلياً عبر ZXing (بدون أي خدمة خارجية) مع خيارات التخصيص من الإعدادات
        public async Task PrintBarcodeAsync(IJSRuntime js, string name, string barcode, decimal? price = null)
        {
            if (string.IsNullOrWhiteSpace(barcode)) return;
            var opts = _barcodeService.LoadOptions();
            string imageUri = _barcodeService.GenerateBarcodeDataUri(barcode, opts.Width, opts.Height);
            string html = _barcodeService.BuildPrintDocument(name, barcode, price, opts, imageUri);
#if ANDROID
            PrintOnAndroid(html);
            await Task.CompletedTask;
#else
            await js.InvokeVoidAsync("printHtml", html, false);
#endif
        }

        // المشاركة على أندرويد (شيت المشاركة) — على ويندوز تنزيل ملف HTML
        public async Task ShareHtmlAsync(IJSRuntime js, string html, string title)
        {
#if ANDROID
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null) return;
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

#if ANDROID
        private void PrintOnAndroid(string html)
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity == null) return;
            activity.RunOnUiThread(() =>
            {
                try
                {
                    var webView = new Android.Webkit.WebView(activity)
                    {
                        LayoutParameters = new Android.Views.ViewGroup.LayoutParams(1, 1)
                    };
                    webView.Settings.JavaScriptEnabled = true;
                    webView.Settings.DomStorageEnabled = true;
                    webView.SetWebViewClient(new PrintWebViewClient(activity));
                    webView.LoadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
                }
                catch { }
            });
        }

        private class PrintWebViewClient : Android.Webkit.WebViewClient
        {
            private readonly Android.App.Activity _activity;
            public PrintWebViewClient(Android.App.Activity activity) => _activity = activity;

            public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
            {
                base.OnPageFinished(view, url);
                try
                {
                    var printManager = (Android.Print.PrintManager)_activity.GetSystemService(Android.Content.Context.PrintService)!;
                    var adapter = view!.CreatePrintDocumentAdapter("BENPOSDZ");
                    printManager.Print("BENPOSDZ", adapter, new Android.Print.PrintAttributes.Builder().Build()!);
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
