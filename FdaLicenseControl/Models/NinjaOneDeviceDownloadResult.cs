using System;

namespace FdaLicenseControl.Models
{
    public sealed class NinjaOneDeviceDownloadResult
    {
        public int DeviceCount { get; init; }
        public DateTimeOffset RetrievedAtUtc { get; init; }
        public string FilePath { get; init; } = string.Empty;
    }
}
