using System;
using System.Globalization;

namespace FdaLicenseControl.Models
{
    public sealed class Microsoft365LicenseHistoryItem
    {
        public string FilePath { get; init; } = string.Empty;

        public string FileName { get; init; } = string.Empty;

        public DateTimeOffset RetrievedAtUtc { get; init; }

        public int CustomerCount { get; init; }

        public int ProcessedCustomerCount { get; init; }

        public int SkippedCustomerCount { get; init; }

        public int SubscribedSkuCount { get; init; }

        public string DisplayText
        {
            get
            {
                var local = RetrievedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
                var clientText = CustomerCount == 1 ? "1 client" : $"{CustomerCount} clients";
                return $"{local} — {clientText}";
            }
        }

        public override string ToString()
        {
            return DisplayText;
        }
    }
}
