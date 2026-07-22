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
    public sealed class NinjaOneInventoryHistoryService
    {
        public string HistoryDirectoryPath { get; }

        public NinjaOneInventoryHistoryService()
        {
            HistoryDirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FdaLicenseControl", "Data", "History");
        }

        public async Task<IReadOnlyList<NinjaOneInventoryHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(HistoryDirectoryPath);

            var list = new List<NinjaOneInventoryHistoryItem>();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(HistoryDirectoryPath, "ninjaone-inventory-*.json");
            }
            catch
            {
                return list;
            }

            foreach (var f in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // skip temporary or non-json
                    if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var stream = File.OpenRead(f);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("retrievedAtUtc", out var retrievedEl))
                        continue;
                    if (!retrievedEl.TryGetDateTimeOffset(out var dto))
                        continue;

                    int deviceCount = 0;
                    if (root.TryGetProperty("deviceCount", out var devEl) && devEl.ValueKind == JsonValueKind.Number && devEl.TryGetInt32(out var dc))
                        deviceCount = dc;

                    int orgCount = 0;
                    if (root.TryGetProperty("organizationCount", out var orgEl) && orgEl.ValueKind == JsonValueKind.Number && orgEl.TryGetInt32(out var oc))
                        orgCount = oc;

                    int locCount = 0;
                    if (root.TryGetProperty("locationCount", out var locEl) && locEl.ValueKind == JsonValueKind.Number && locEl.TryGetInt32(out var lc))
                        locCount = lc;

                    var item = new NinjaOneInventoryHistoryItem
                    {
                        FilePath = f,
                        FileName = Path.GetFileName(f),
                        RetrievedAtUtc = dto,
                        DeviceCount = deviceCount,
                        OrganizationCount = orgCount,
                        LocationCount = locCount
                    };

                    list.Add(item);
                }
                catch
                {
                    // ignore invalid files
                }
            }

            var ordered = list.OrderByDescending(x => x.RetrievedAtUtc).ToList();
            return ordered;
        }
    }
}
