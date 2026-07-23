using System;

namespace FdaLicenseControl.Models
{
    public sealed class NinjaOneSettings
    {
        public int SchemaVersion { get; set; } = 1;
        public string BaseUrl { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }
}
