using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class ZyxelNebulaInventoryReader
    {
        public ZyxelNebulaClientInventory[] Read(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            // Read arrays
            var organizationsElement = root.TryGetProperty("organizations", out var orgsEl) ? orgsEl : default;
            var devicesElement = root.TryGetProperty("devices", out var devsEl) ? devsEl : default;

            // Build organization reference dictionaries
            var orgKeyByOrgId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var orgNameByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Process organizations array
            if (organizationsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var org in organizationsElement.EnumerateArray())
                {
                    var orgId = ReadString(org, "orgId");
                    if (string.IsNullOrEmpty(orgId))
                        orgId = ReadString(org, "organizationId");

                    var orgName = ReadString(org, "name");
                    if (string.IsNullOrEmpty(orgName))
                        orgName = ReadString(org, "organizationName");

                    // Resolve organization key
                    var storedKey = ReadString(org, "organizationKey");
                    string organizationKey;
                    if (!string.IsNullOrEmpty(storedKey))
                        organizationKey = storedKey;
                    else
                        organizationKey = ZyxelNebulaOrganizationKeyFactory.Create(orgId, orgName);

                    // Store mappings
                    if (!string.IsNullOrEmpty(orgId))
                        orgKeyByOrgId[orgId] = organizationKey;
                    orgNameByKey[organizationKey] = orgName;
                }
            }

            // Aggregate devices by organization key
            var devicesByOrgKey = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
            var orgIdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (devicesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var device in devicesElement.EnumerateArray())
                {
                    var storedKey = ReadString(device, "organizationKey");
                    var orgId = ReadString(device, "organizationId");
                    if (string.IsNullOrEmpty(orgId))
                        orgId = ReadString(device, "orgId");
                    var orgName = ReadString(device, "organizationName");

                    // Resolve organization key
                    string organizationKey;
                    if (!string.IsNullOrEmpty(storedKey))
                        organizationKey = storedKey;
                    else if (!string.IsNullOrEmpty(orgId) && orgKeyByOrgId.TryGetValue(orgId, out var mappedKey))
                        organizationKey = mappedKey;
                    else
                        organizationKey = ZyxelNebulaOrganizationKeyFactory.Create(orgId, orgName);

                    // Store device
                    if (!devicesByOrgKey.TryGetValue(organizationKey, out var deviceList))
                    {
                        deviceList = new List<JsonElement>();
                        devicesByOrgKey[organizationKey] = deviceList;
                    }
                    deviceList.Add(device);

                    // Store orgId for this key
                    if (!string.IsNullOrEmpty(orgId) && !orgIdByKey.ContainsKey(organizationKey))
                        orgIdByKey[organizationKey] = orgId;

                    // Store name if not already present
                    if (!orgNameByKey.ContainsKey(organizationKey) && !string.IsNullOrEmpty(orgName))
                        orgNameByKey[organizationKey] = orgName;
                }
            }

            // Build clients
            var clients = new List<ZyxelNebulaClientInventory>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in devicesByOrgKey)
            {
                var organizationKey = kvp.Key;
                var devices = kvp.Value;

                // Check for duplicate keys
                if (!seenKeys.Add(organizationKey))
                    throw new InvalidOperationException($"La clé stable Zyxel est dupliquée entre plusieurs clients : {organizationKey}.");

                // Resolve organization ID
                var organizationId = orgIdByKey.TryGetValue(organizationKey, out var oid) ? oid : string.Empty;

                // Resolve organization name
                string clientName;
                if (orgNameByKey.TryGetValue(organizationKey, out var storedName))
                    clientName = storedName;
                else if (!string.IsNullOrEmpty(organizationId))
                    clientName = $"Organisation inconnue ({organizationId})";
                else
                    clientName = "Organisation Zyxel inconnue";

                // Aggregate sites
                var siteAggregates = new Dictionary<string, (string siteName, int deviceCount)>(StringComparer.OrdinalIgnoreCase);

                foreach (var device in devices)
                {
                    var siteId = ReadString(device, "siteId");
                    var siteName = ReadString(device, "siteName");

                    var siteKey = string.IsNullOrEmpty(siteId) ? "none" : siteId;

                    if (!siteAggregates.TryGetValue(siteKey, out var aggregate))
                        aggregate = (siteName: string.IsNullOrEmpty(siteName) ? "Sans site" : siteName, deviceCount: 0);

                    aggregate.deviceCount++;
                    siteAggregates[siteKey] = aggregate;
                }

                // Build sites
                var sites = siteAggregates
                    .Select(s => new ZyxelNebulaSiteInventory
                    {
                        SiteId = s.Key == "none" ? null : s.Key,
                        SiteName = s.Value.siteName,
                        DeviceCount = s.Value.deviceCount
                    })
                    .OrderBy(s => s.SiteName)
                    .ToArray();

                var totalDeviceCount = devices.Count;

                var client = new ZyxelNebulaClientInventory
                {
                    OrganizationId = organizationId,
                    OrganizationKey = organizationKey,
                    OrganizationName = clientName,
                    CanExpand = sites.Length > 1,
                    IsExpanded = false,
                    Sites = sites,
                    TotalDeviceCount = totalDeviceCount
                };

                clients.Add(client);
            }

            // Validate totals
            var totalLicenses = clients.Sum(c => c.TotalLicenseCount);
            var totalDevices = devicesByOrgKey.Sum(kvp => kvp.Value.Count);
            if (totalLicenses != totalDevices)
                throw new InvalidOperationException($"Le total des licences ({totalLicenses}) ne correspond pas au nombre de devices ({totalDevices}).");

            return clients.OrderBy(c => c.OrganizationName).ToArray();
        }

        private static string ReadString(JsonElement jsonObject, string propertyName)
        {
            if (jsonObject.ValueKind != JsonValueKind.Object)
                return string.Empty;

            if (!jsonObject.TryGetProperty(propertyName, out var value))
                return string.Empty;

            if (value.ValueKind != JsonValueKind.String)
                return string.Empty;

            return value.GetString()?.Trim() ?? string.Empty;
        }
    }
}
