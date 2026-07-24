using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class Microsoft365LicenseHistoryService
    {
        public string HistoryDirectoryPath { get; }

        public Microsoft365LicenseHistoryService()
        {
            HistoryDirectoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Data",
                "Microsoft365",
                "History");
        }

        public async Task<IReadOnlyList<Microsoft365LicenseHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(HistoryDirectoryPath);

            var list = new List<Microsoft365LicenseHistoryItem>();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(HistoryDirectoryPath, "microsoft365-licenses-*.json");
            }
            catch
            {
                return list;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var stream = File.OpenRead(file);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!TryGetDateTimeOffset(root, "retrievedAtUtc", out var retrievedAtUtc))
                        continue;

                    var item = new Microsoft365LicenseHistoryItem
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        RetrievedAtUtc = retrievedAtUtc,
                        CustomerCount = GetInt32(root, "customerCount"),
                        ProcessedCustomerCount = GetInt32(root, "processedCustomerCount"),
                        SkippedCustomerCount = GetInt32(root, "skippedCustomerCount"),
                        SubscribedSkuCount = GetInt32(root, "subscribedSkuCount")
                    };

                    list.Add(item);
                }
                catch
                {
                    // ignore invalid files
                }
            }

            return list
                .OrderByDescending(x => x.RetrievedAtUtc)
                .ToList();
        }

        private static bool TryGetDateTimeOffset(JsonElement root, string propertyName, out DateTimeOffset value)
        {
            value = default;
            if (!root.TryGetProperty(propertyName, out var property))
                return false;

            if (property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString();
                if (!string.IsNullOrWhiteSpace(text) && DateTimeOffset.TryParse(text, out value))
                    return true;
            }

            return false;
        }

        private static int GetInt32(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var property))
                return 0;

            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
                return value;

            return 0;
        }
    }
}
