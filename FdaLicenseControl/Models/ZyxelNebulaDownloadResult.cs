using System;

namespace FdaLicenseControl.Models
{
    public sealed class ZyxelNebulaDownloadResult
    {
        public DateTimeOffset RetrievedAtUtc { get; init; }

        public int OrganizationCount { get; init; }

        public int ProcessedOrganizationCount { get; init; }

        public int SkippedOrganizationCount { get; init; }

        public int SiteCount { get; init; }

        public int DeviceCount { get; init; }

        public string FilePath { get; init; } = string.Empty;
    }
}
