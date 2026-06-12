using System;
using System.IO;
using System.Windows;
using AudioShare.Services;
using AudioShare.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AudioShare;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioShare", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(logDir, "audioshare-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("AudioShare starting");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Unhandled domain exception");
        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI exception");
            args.Handled = true;
        };

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IGroupService, GroupService>();
        services.AddSingleton<IDeviceMonitor, DeviceMonitor>();
        services.AddSingleton<IAudioEngine, WasapiAudioEngine>();
        services.AddSingleton<IBluetoothOptimizer, BluetoothOptimizer>();
        services.AddSingleton<MainViewModel>();
        Services = services.BuildServiceProvider();

        base.OnStartup(e);

        var vm = Services.GetRequiredService<MainViewModel>();
        vm.ApplyTheme();
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("AudioShare exiting");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
