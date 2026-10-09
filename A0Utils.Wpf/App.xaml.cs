using A0Utils.Wpf.Services;
using A0Utils.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Windows;

namespace A0Utils.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var serviceProvider = ServiceExtensions.ConfigureServices();
            var settingsService = serviceProvider.GetRequiredService<SettingsService>();
            var settings = settingsService.GetSettings();
            var logLevel = settings.IsExtraSettingsEnabled ? new LoggingLevelSwitch(LogEventLevel.Debug) : new LoggingLevelSwitch(LevelAlias.Off);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(logLevel)
                .WriteTo.File("a0utils.log", rollOnFileSizeLimit: true, fileSizeLimitBytes: 1024 * 1024)
                .CreateLogger();

            var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
            var mainWindow = new MainWindow { DataContext = mainViewModel };
            mainWindow.Title = "Утилиты для А0";
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
