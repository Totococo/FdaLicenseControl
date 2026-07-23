using System;

namespace FdaLicenseControl.Models
{
    public sealed class ZyxelNebulaInventoryHistoryItem
    {
        public string FilePath { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public DateTimeOffset RetrievedAtUtc { get; init; }

        public int OrganizationCount { get; init; }

        public int ProcessedOrganizationCount { get; init; }

        public int SkippedOrganizationCount { get; init; }

        public int SiteCount { get; init; }

        public int DeviceCount { get; init; }

        public string DisplayText
        {
            get
            {
                var date = RetrievedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                var licencesText = DeviceCount == 1 ? "1 licence" : $"{DeviceCount} licences";
                return $"{date} — {licencesText}";
            }
        }

        public override string ToString() => DisplayText;
    }
}
