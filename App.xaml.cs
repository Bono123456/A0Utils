using A0Utils.Wpf.Services;
using A0Utils.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Windows;

namespace A0Utils.Wpf
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IServiceProvider _serviceProvider;

        public App()
        {
            _serviceProvider = ServiceExtensions.ConfigureServices();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settingsService = _serviceProvider.GetService<SettingsService>();
            var settings = settingsService.GetSettings();
            var logLevel = settings.IsExtraSettingsEnabled ? new LoggingLevelSwitch(LogEventLevel.Debug) : new LoggingLevelSwitch(LevelAlias.Off);

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.ControlledBy(logLevel)
                .WriteTo.File("a0utils.log", rollOnFileSizeLimit: true, fileSizeLimitBytes: 1024 * 1024)
                .CreateLogger();

            // Режим копирования лицензий с правами администратора (запускается из самой программы)
            if (e.Args.Length == 3 && e.Args[0] == FileOperationsService.CopyLicensesArgument)
            {
                var fileOperationsService = _serviceProvider.GetService<FileOperationsService>();
                Shutdown(fileOperationsService.RunElevatedCopyJob(e.Args[1], e.Args[2]));
                return;
            }

            var mainViewModel = _serviceProvider.GetService<MainViewModel>();
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
