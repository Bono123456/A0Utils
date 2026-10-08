using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace A0Utils.Wpf.Models
{
    public sealed class SettingsModel
    {
        public string A0InstallationPath { get; set; }
        public string YandexUrl { get; set; }
        public string LicenseUrl { get; set; }
        public string SubscriptionUrl { get; set; }
        public string UpdatesUrl { get; set; }
        public string DownloadUpdatesPath { get; set; }
        public bool IsExtraSettingsEnabled { get; set; }
    }
}
