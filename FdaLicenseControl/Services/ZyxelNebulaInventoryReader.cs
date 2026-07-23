using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class ZyxelNebulaInventoryReader
    {
        public ZyxelNebulaClientInventory[] Read(string filePath)
        {
            var text = System.IO.File.ReadAllText(filePath);
            var root = JsonNode.Parse(text) as JsonObject ?? throw new InvalidOperationException("Fichier d'inventaire Zyxel invalide.");

            var orgsNode = root["organizations"] as JsonArray ?? new JsonArray();
            var detailsNode = root["organizationDetails"] as JsonArray ?? new JsonArray();
            var sitesNode = root["sites"] as JsonArray ?? new JsonArray();
            var devicesNode = root["devices"] as JsonArray ?? new JsonArray();

            var orgMap = new Dictionary<string, List<ZyxelNebulaSiteInventory>>();
            var orgNames = new Dictionary<string, string>();

            foreach (var s in sitesNode)
            {
                if (s is not JsonObject so) continue;
                var orgId = so["organizationId"]?.GetValue<string>() ?? string.Empty;
                var siteId = so["siteId"]?.GetValue<string>() ?? string.Empty;
                var siteName = so["name"]?.GetValue<string>() ?? string.Empty;
                var deviceCount = so["deviceCount"]?.GetValue<int?>() ?? 0;
                var site = new ZyxelNebulaSiteInventory { SiteId = siteId, SiteName = siteName, DeviceCount = deviceCount };
                if (!orgMap.TryGetValue(orgId, out var list)) { list = new List<ZyxelNebulaSiteInventory>(); orgMap[orgId] = list; }
                list.Add(site);
            }

            foreach (var d in detailsNode)
            {
                if (d is not JsonObject dob) continue;
                var orgId = dob["organizationId"]?.GetValue<string>() ?? string.Empty;
                var orgName = dob["organizationName"]?.GetValue<string>() ?? string.Empty;
                if (!orgNames.ContainsKey(orgId)) orgNames[orgId] = orgName;
            }

            var clients = new List<ZyxelNebulaClientInventory>();
            foreach (var o in orgsNode)
            {
                if (o is not JsonObject oo) continue;
                var orgId = oo["orgId"]?.GetValue<string>() ?? string.Empty;
                var orgName = oo["name"]?.GetValue<string>() ?? string.Empty;
                var sites = orgMap.TryGetValue(orgId, out var list) ? list : new List<ZyxelNebulaSiteInventory>();
                var totalDeviceCount = sites.Sum(x => x.DeviceCount);
                // Validate totals: sum of site licenses == organization totalDeviceCount from details if available
                int reportedTotal = 0;
                var det = detailsNode.FirstOrDefault(x => x is JsonObject jd && (jd["organizationId"]?.GetValue<string>() ?? string.Empty) == orgId) as JsonObject;
                if (det != null)
                    reportedTotal = det["deviceCount"]?.GetValue<int?>() ?? 0;

                if (reportedTotal != 0 && reportedTotal != totalDeviceCount)
                    throw new InvalidOperationException($"Le total des licences Zyxel est incohérent pour l'organisation {orgName}.");

                var client = new ZyxelNebulaClientInventory
                {
                    OrganizationId = int.TryParse(orgId, out var iid) ? iid : 0,
                    OrganizationName = orgName,
                    CanExpand = sites.Count > 0,
                    IsExpanded = false,
                    Sites = sites.ToArray(),
                    TotalDeviceCount = totalDeviceCount
                };
                clients.Add(client);
            }

            return clients.ToArray();
        }
    }
}
