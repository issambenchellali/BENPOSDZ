using Microsoft.Extensions.Logging;
using BENPOSDZ.Services;
using Microsoft.Maui.LifecycleEvents;

namespace BENPOSDZ;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddSingleton<DatabaseService>();
		builder.Services.AddSingleton<AuthService>();
		builder.Services.AddSingleton<CloudSyncService>();
		builder.Services.AddSingleton<BackgroundSyncService>();
		builder.Services.AddSingleton<DataTransferService>();
		builder.Services.AddSingleton<UpdateService>();
#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif
#if WINDOWS
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(windows => windows.OnWindowCreated(window =>
            {
                window.ExtendsContentIntoTitleBar = false;
                var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                var id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(handle);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(id);
                if (appWindow != null)
                {
                    var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
                    presenter?.Maximize(); // فتح البرنامج على كامل الشاشة
                }
            }));
        });
#endif
		// تسجيل الطبقات المعمارية الجديدة
		builder.Services.AddSingleton<IApplicationStateService, ApplicationStateService>();
		builder.Services.AddSingleton<ISettingsRepository, SettingsRepository>();
		builder.Services.AddSingleton<ToastService>();
		builder.Services.AddSingleton<ImageService>();
		builder.Services.AddSingleton<SecurityService>();
		builder.Services.AddSingleton<PrintTemplateService>();
		builder.Services.AddSingleton<BarcodeService>();
		builder.Services.AddSingleton<PrintService>();
		builder.Services.AddSingleton<NetworkScanner>();
        var app = builder.Build();
        
        // تشغيل الخدمة الخلفية يدوياً بعد بناء التطبيق
        var bgService = app.Services.GetRequiredService<BackgroundSyncService>();
        bgService.Start();
        
        return app;
	}
}
