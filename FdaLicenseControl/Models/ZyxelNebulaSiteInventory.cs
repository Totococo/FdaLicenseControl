using System;
namespace FdaLicenseControl.Models
{
    public sealed class ZyxelNebulaSiteInventory
    {
        public string SiteId { get; init; } = string.Empty;
        public string SiteName { get; init; } = string.Empty;
        public int DeviceCount { get; init; }

        public int LicenseCount => DeviceCount;
    }
}
