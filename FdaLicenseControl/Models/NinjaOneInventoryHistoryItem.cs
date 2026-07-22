using System;

namespace FdaLicenseControl.Models
{
    public sealed class NinjaOneInventoryHistoryItem
    {
        public string FilePath { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public DateTimeOffset RetrievedAtUtc { get; init; }
        public int DeviceCount { get; init; }
        public int OrganizationCount { get; init; }
        public int LocationCount { get; init; }

        public string DisplayText
        {
            get
            {
                var local = RetrievedAtUtc.ToLocalTime();
                var date = local.ToString("dd/MM/yyyy HH:mm");
                var devices = DeviceCount;
                return $"{date} — {devices} postes";
            }
        }

        public override string ToString() => DisplayText;
    }
}
