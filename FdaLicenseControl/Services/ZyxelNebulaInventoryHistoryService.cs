using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class ZyxelNebulaInventoryHistoryService
    {
        public string HistoryDirectoryPath { get; }

        public ZyxelNebulaInventoryHistoryService()
        {
            HistoryDirectoryPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Data",
                "Zyxel",
                "History");
        }

        public async Task<IReadOnlyList<ZyxelNebulaInventoryHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(HistoryDirectoryPath);

            var files = Directory.EnumerateFiles(HistoryDirectoryPath, "zyxel-nebula-inventory-*.json")
                .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var list = new List<ZyxelNebulaInventoryHistoryItem>();
            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var text = await File.ReadAllTextAsync(f, cancellationToken);
                    var root = JsonNode.Parse(text) as JsonObject;
                    if (root == null)
                        continue;

                    var retrieved = root["retrievedAtUtc"]?.GetValue<string>();
                    if (!DateTimeOffset.TryParse(retrieved, out var dt))
                        continue;

                    var orgCount = root["organizationCount"]?.GetValue<int?>() ?? 0;
                    var processed = root["processedOrganizationCount"]?.GetValue<int?>() ?? 0;
                    var skipped = root["skippedOrganizationCount"]?.GetValue<int?>() ?? 0;
                    var siteCount = root["siteCount"]?.GetValue<int?>() ?? 0;
                    var deviceCount = root["deviceCount"]?.GetValue<int?>() ?? 0;

                    var item = new ZyxelNebulaInventoryHistoryItem
                    {
                        FilePath = f,
                        FileName = Path.GetFileName(f),
                        RetrievedAtUtc = dt,
                        OrganizationCount = orgCount,
                        ProcessedOrganizationCount = processed,
                        SkippedOrganizationCount = skipped,
                        SiteCount = siteCount,
                        DeviceCount = deviceCount
                    };
                    list.Add(item);
                }
                catch
                {
                    // ignore invalid files
                }
            }

            var ordered = list.OrderByDescending(x => x.RetrievedAtUtc).ToArray();
            return ordered;
        }
    }
}
