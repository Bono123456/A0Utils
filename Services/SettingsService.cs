using A0Utils.Wpf.Models;
using System;
using System.IO;

namespace A0Utils.Wpf.Services
{
    public sealed class SettingsService
    {
        public void UpdateDownloadPath(string path)
        {
            var settings = GetSettings();
            settings.DownloadUpdatesPath = path;
            Save(settings);
        }

        public void SaveWithoutDownloadPath(SettingsModel settings)
        {
            var currentSettings = GetSettings();
            settings.DownloadUpdatesPath = currentSettings.DownloadUpdatesPath;
            Save(settings);
        }

        public SettingsModel GetSettings()
        {
            var settings = new SettingsModel
            {
                A0InstallationPath = Properties.Settings.Default.A0InstallationPath,
                DownloadUpdatesPath = Properties.Settings.Default.DownloadUpdatesPath,
                LicenseUrl = Properties.Settings.Default.LicenseUrl,
                SubscriptionUrl = Properties.Settings.Default.SubscriptionUrl,
                UpdatesUrl = Properties.Settings.Default.UpdatesUrl,
                YandexUrl = Properties.Settings.Default.YandexUrl,
                IsExtraSettingsEnabled = Properties.Settings.Default.IsExtraSettingsEnabled
            };

            if (string.IsNullOrEmpty(settings.DownloadUpdatesPath))
            {
                settings.DownloadUpdatesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "A0Updates");
                Directory.CreateDirectory(settings.DownloadUpdatesPath);
            }

            return settings;
        }

        private void Save(SettingsModel settings)
        {
            Properties.Settings.Default.A0InstallationPath = settings.A0InstallationPath;
            Properties.Settings.Default.LicenseUrl = settings.LicenseUrl;
            Properties.Settings.Default.SubscriptionUrl = settings.SubscriptionUrl;
            Properties.Settings.Default.UpdatesUrl = settings.UpdatesUrl;
            Properties.Settings.Default.YandexUrl = settings.YandexUrl;
            Properties.Settings.Default.DownloadUpdatesPath = settings.DownloadUpdatesPath;
            Properties.Settings.Default.IsExtraSettingsEnabled = settings.IsExtraSettingsEnabled;

            Properties.Settings.Default.Save();
        }
    }
}
