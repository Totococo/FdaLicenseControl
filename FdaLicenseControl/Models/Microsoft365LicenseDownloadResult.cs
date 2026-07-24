using System;

namespace FdaLicenseControl.Models
{
    public sealed class Microsoft365LicenseDownloadResult
    {
        public DateTimeOffset RetrievedAtUtc { get; init; }

        public int CustomerCount { get; init; }

        public int ProcessedCustomerCount { get; init; }

        public int SkippedCustomerCount { get; init; }

        public int SubscribedSkuCount { get; init; }

        public string AccountName { get; init; } = string.Empty;

        public string FilePath { get; init; } = string.Empty;
    }
}
