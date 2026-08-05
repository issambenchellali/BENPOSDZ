using Camera.MAUI;
using Camera.MAUI.ZXing;
using Camera.MAUI.ZXingHelper;

namespace BENPOSDZ;

// شاشة مسح الباركود المستمر بالكاميرا (تعرض فوق صفحة Blazor كنافذة وطنية)
// تُستخدم على أندرويد فقط: كل باركود يُقرأ يُضاف فوراً للسلة عبر onCode،
// وتبقى الكاميرا مفتوحة لمسح المنتج التالي حتى يغلقها المستخدم.
public sealed class BarcodeScannerPage : ContentPage
{
    private readonly CameraView _cameraView;
    private readonly Label _statusLabel;
    private readonly Action<string>? _onCode;
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private bool _started;
    private bool _disposed;
    private string? _lastCode;
    private DateTime _lastCodeAt;

    public BarcodeScannerPage(Action<string>? onCode)
    {
        _onCode = onCode;

        BackgroundColor = Colors.Black;
        Padding = 0;

        _cameraView = new CameraView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            MirroredImage = true,
            WidthRequest = 1,
            HeightRequest = 1
        };
        _cameraView.CamerasLoaded += OnCamerasLoaded;
        _cameraView.BarcodeDetected += OnBarcodeDetected;
        _cameraView.BarCodeDecoder = new ZXingBarcodeDecoder();
        _cameraView.BarCodeOptions = new BarcodeDecodeOptions
        {
            AutoRotate = true,
            TryHarder = true,
            TryInverted = true,
            ReadMultipleCodes = false,
            PossibleFormats = new System.Collections.Generic.List<Camera.MAUI.BarcodeFormat>
            {
                Camera.MAUI.BarcodeFormat.QR_CODE,
                Camera.MAUI.BarcodeFormat.All_1D
            }
        };
        _cameraView.BarCodeDetectionFrameRate = 8;
        _cameraView.BarCodeDetectionMaxThreads = 3;
        _cameraView.ControlBarcodeResultDuplicate = true;
        _cameraView.BarCodeDetectionEnabled = true;

        // إطار المسح (مستطيل أحمر في المنتصف)
        var frame = new Border
        {
            WidthRequest = 260,
            HeightRequest = 140,
            Stroke = Colors.Red,
            StrokeThickness = 4,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Opacity = 0.9
        };

        _statusLabel = new Label
        {
            Text = "وجّه الكاميرا نحو الباركود...",
            TextColor = Colors.White,
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(12, 8)
        };

        var closeButton = new Button
        {
            Text = "✖ إغلاق",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#B3000000"),
            FontSize = 17,
            HeightRequest = 52,
            Padding = new Thickness(18, 0),
            CornerRadius = 10
        };
        closeButton.Clicked += async (_, _) => await CloseAsync();

        var torchButton = new Button
        {
            Text = "🔦",
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#B3000000"),
            FontSize = 17,
            HeightRequest = 52,
            WidthRequest = 64,
            CornerRadius = 10
        };
        torchButton.Clicked += OnTorchClicked;

        var topBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(10, 6),
            BackgroundColor = Color.FromArgb("#99000000")
        };
        topBar.Add(new Label
        {
            Text = "المسح المستمر — أضف الباركود أمام الكاميرا",
            TextColor = Colors.White,
            FontSize = 15,
            VerticalTextAlignment = TextAlignment.Center
        }, 0, 0);
        topBar.Add(torchButton, 1, 0);

        var bottomBar = new Grid
        {
            Padding = new Thickness(10, 8),
            BackgroundColor = Color.FromArgb("#99000000")
        };
        bottomBar.Add(closeButton);

        var overlay = new Grid();
        overlay.Add(frame);
        overlay.Add(new Label
        {
            Text = "لإيقاف المسح اضغط «إغلاق»",
            TextColor = Color.FromArgb("#CCFFFFFF"),
            FontSize = 13,
            VerticalOptions = LayoutOptions.End,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var rootGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        rootGrid.Add(topBar, 0, 0);
        rootGrid.Add(_cameraView, 0, 1);
        rootGrid.Add(overlay, 0, 1);
        rootGrid.Add(_statusLabel, 0, 2);
        rootGrid.Add(bottomBar, 0, 3);
        Content = rootGrid;
    }

    // فتح شاشة المسح وانتظار إغلاقها (يُستدعى من صفحة البيع)
    public static async Task ScanAsync(Action<string> onCode)
    {
        var page = new BarcodeScannerPage(onCode);
        var root = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (root?.Navigation == null) return;
        await root.Navigation.PushModalAsync(page);
        await page._closed.Task;
    }

    private void OnTorchClicked(object? sender, EventArgs e)
    {
        try
        {
            _cameraView.TorchEnabled = !_cameraView.TorchEnabled;
        }
        catch { }
    }

    private async void OnCamerasLoaded(object? sender, EventArgs e)
    {
        try
        {
            if (_started) return;
            var cam = _cameraView.Cameras?.FirstOrDefault();
            if (cam == null) return;
            _cameraView.Camera = cam;
            await StartCameraSafeAsync();
        }
        catch { }
    }

    private async Task StartCameraSafeAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        try
        {
            await _cameraView.StartCameraAsync();
        }
        catch
        {
            _started = false;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (_cameraView.Cameras?.Count > 0 && !_started)
            {
                _cameraView.Camera = _cameraView.Cameras.FirstOrDefault();
                await StartCameraSafeAsync();
            }
        }
        catch { }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _ = StopAsync();
    }

    private async Task StopAsync()
    {
        _disposed = true;
        try { await _cameraView.StopCameraAsync(); } catch { }
        _closed.TrySetResult(true);
    }

    private void OnBarcodeDetected(object? sender, BarcodeEventArgs args)
    {
        if (_disposed || args?.Result == null) return;

        // التقاط الكود في خيط الواجهة
        string? found = null;
        foreach (var r in args.Result)
        {
            if (r == null || string.IsNullOrWhiteSpace(r.Text)) continue;
            found = r.Text;
            break;
        }
        if (found == null) return;

        // منع القراءة المزدوجة السريعة لنفس الكود
        var now = DateTime.UtcNow;
        if (found == _lastCode && (now - _lastCodeAt) < TimeSpan.FromMilliseconds(400)) return;
        _lastCode = found;
        _lastCodeAt = now;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_disposed) return;
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(120)); } catch { }
            PlayBeep();
            _statusLabel.Text = "✅ تمت القراءة: " + found;
            _onCode?.Invoke(found);
        });
    }

    private static void PlayBeep()
    {
#if ANDROID
        try
        {
            using var tone = new Android.Media.ToneGenerator(Android.Media.Stream.Notification, 85);
            tone.StartTone(Android.Media.Tone.PropBeep2, 120);
        }
        catch { }
#endif
    }

    private async Task CloseAsync()
    {
        if (_disposed) return;
        await StopAsync();
        try { await Navigation.PopModalAsync(); } catch { }
    }
}
