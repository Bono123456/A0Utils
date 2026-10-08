using A0Utils.Wpf.Services;
using A0Utils.Wpf.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace A0Utils.Wpf
{
    public static class ServiceExtensions
    {
        public static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddHttpClient("yandexClient");

            services.AddMemoryCache();

            services.AddSingleton<MainViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<LicenseViewModel>();

            services.AddSingleton<DialogService>();  
            services.AddSingleton<FileOperationsService>();
            services.AddSingleton<YandexService>();
            services.AddSingleton<SettingsService>();
            services.AddSingleton<UpdateService>();

            return services.BuildServiceProvider();
        }
    }
}
