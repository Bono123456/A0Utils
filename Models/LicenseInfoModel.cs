using System;

namespace A0Utils.Wpf.Models
{
    public sealed class LicenseInfoModel
    {
        public string Content { get; set; }
        public DateTime A0LicenseExpAt { get; set; }
        public DateTime PIRLicenseExpAt { get; set; }
        public DateTime SubscriptionLicenseExpAt { get; set; }
    }
}
