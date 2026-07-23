using System;

namespace FdaLicenseControl.Models
{
    public sealed class ZyxelNebulaSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
