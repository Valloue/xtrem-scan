using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using XtremScan.Services;
using XtremScan.ViewModels;

namespace XtremScan;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static Window MainWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppPaths.EnsureDirectories();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(AppPaths.Logs, "xtremscan-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<WiaScannerService>();
        services.AddSingleton<BrotherScanClient>();
        services.AddSingleton<ScannerAddressResolver>();
        services.AddSingleton<IScannerService, ScannerService>();
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<AboutViewModel>();
        Services = services.BuildServiceProvider();

        Log.Information("Xtrem Scan démarré");
        MainWindow = new MainWindow();
        MainWindow.Activate();
        Log.Debug("MainWindow activée");
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Log.Error(args.Exception, "Exception d'interface non gérée");
        args.Handled = true;
    }
}
