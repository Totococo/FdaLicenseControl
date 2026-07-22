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
    public sealed class NinjaOneInventoryReader
    {
        public async Task<IReadOnlyList<NinjaOneClientInventory>> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Le fichier d'inventaire n'existe pas.", filePath);

            string text = await File.ReadAllTextAsync(filePath, cancellationToken);

            JsonNode? rootNode;
            try
            {
                rootNode = JsonNode.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier d'inventaire n'est pas un JSON valide.", ex);
            }

            if (rootNode is not JsonObject root)
                throw new InvalidOperationException("Le fichier d'inventaire JSON n'a pas un objet racine.");

            if (!root.TryGetPropertyValue("organizations", out var orgsNode) || orgsNode is not JsonArray orgsArr)
                throw new InvalidOperationException("Le fichier d'inventaire ne contient pas de tableau 'organizations'.");
            if (!root.TryGetPropertyValue("locations", out var locsNode) || locsNode is not JsonArray locsArr)
                throw new InvalidOperationException("Le fichier d'inventaire ne contient pas de tableau 'locations'.");
            if (!root.TryGetPropertyValue("devices", out var devsNode) || devsNode is not JsonArray devsArr)
                throw new InvalidOperationException("Le fichier d'inventaire ne contient pas de tableau 'devices'.");

            // Build organization dictionary id -> name
            var orgDict = new Dictionary<long, string>();
            foreach (var item in orgsArr)
            {
                if (item is not JsonObject obj)
                    continue;
                if (!obj.TryGetPropertyValue("id", out var idNode) || idNode == null || !long.TryParse(idNode.ToString(), out var id))
                    continue;
                var name = obj.TryGetPropertyValue("name", out var nameNode) && nameNode != null ? nameNode.ToString() ?? string.Empty : string.Empty;
                orgDict[id] = name;
            }

            // Build location dictionary keyed by orgId + locId
            var locDict = new Dictionary<(long orgId, long locId), string>();
            foreach (var item in locsArr)
            {
                if (item is not JsonObject obj)
                    continue;
                if (!obj.TryGetPropertyValue("id", out var idNode) || idNode == null || !long.TryParse(idNode.ToString(), out var id))
                    continue;
                if (!obj.TryGetPropertyValue("organizationId", out var orgIdNode) || orgIdNode == null || !long.TryParse(orgIdNode.ToString(), out var orgId))
                    continue;
                var name = obj.TryGetPropertyValue("name", out var nameNode) && nameNode != null ? nameNode.ToString() ?? string.Empty : string.Empty;
                locDict[(orgId, id)] = name;
            }

            // Counters
            var orgCounts = new Dictionary<long, int>();
            var locCounts = new Dictionary<(long orgId, long? locId), int>();

            foreach (var d in devsArr)
            {
                if (d is not JsonObject dobj)
                    continue;

                // organizationId mandatory
                if (!dobj.TryGetPropertyValue("organizationId", out var orgNode) || orgNode == null || !long.TryParse(orgNode.ToString(), out var orgId))
                {
                    throw new InvalidOperationException("Un device ne contient pas d'organizationId valide.");
                }

                long? locIdNullable = null;
                if (dobj.TryGetPropertyValue("locationId", out var locNode) && locNode != null && long.TryParse(locNode.ToString(), out var locIdParsed))
                    locIdNullable = locIdParsed;

                // increment org count
                orgCounts.TryGetValue(orgId, out var oc);
                orgCounts[orgId] = oc + 1;

                // increment loc count (use null for no location)
                var locKey = (orgId, locIdNullable);
                locCounts.TryGetValue(locKey, out var lc);
                locCounts[locKey] = lc + 1;
            }

            // Build client inventories for orgs that have at least one machine
            var clients = new List<NinjaOneClientInventory>();
            foreach (var kv in orgCounts)
            {
                var orgId = kv.Key;
                var total = kv.Value;
                var clientName = orgDict.TryGetValue(orgId, out var name) && !string.IsNullOrEmpty(name) ? name : $"Organisation inconnue ({orgId})";

                var client = new NinjaOneClientInventory
                {
                    OrganizationId = (int)orgId,
                    ClientName = clientName,
                    TotalDeviceCount = total
                };

                // collect locations with counts >0 for this org
                var locs = new List<NinjaOneLocationInventory>();
                foreach (var lkv in locCounts)
                {
                    var (oId, lIdNullable) = lkv.Key;
                    if (oId != orgId)
                        continue;
                    var count = lkv.Value;
                    if (count <= 0)
                        continue;

                    int? locIdOut = null;
                    string locName;
                    if (!lIdNullable.HasValue)
                    {
                        locName = "Sans emplacement";
                    }
                    else
                    {
                        locIdOut = (int)lIdNullable.Value;
                        if (!locDict.TryGetValue((orgId, lIdNullable.Value), out var lname) || string.IsNullOrEmpty(lname))
                            locName = "Sans emplacement";
                        else
                            locName = lname;
                    }

                    locs.Add(new NinjaOneLocationInventory { LocationId = locIdOut, LocationName = locName, DeviceCount = count });
                }

                // merge entries where LocationName is same (e.g., multiple unspecified)
                var merged = locs.GroupBy(l => (l.LocationId, l.LocationName)).Select(g => new NinjaOneLocationInventory
                {
                    LocationId = g.Key.LocationId,
                    LocationName = g.Key.LocationName,
                    DeviceCount = g.Sum(x => x.DeviceCount)
                }).ToList();

                // Only keep locations with at least one machine
                merged = merged.Where(x => x.DeviceCount > 0).OrderBy(x => x.LocationName, StringComparer.OrdinalIgnoreCase).ToList();

                client.Locations.AddRange(merged);

                // verify sum
                var sumLoc = client.Locations.Sum(x => x.DeviceCount);
                if (sumLoc != client.TotalDeviceCount)
                {
                    throw new InvalidOperationException($"Incohérence pour l'organisation {clientName} ({orgId}) : total {client.TotalDeviceCount} != somme emplacements {sumLoc}.");
                }

                clients.Add(client);
            }

            // sort clients by ClientName
            var sorted = clients.OrderBy(c => c.ClientName, StringComparer.OrdinalIgnoreCase).ToList();

            return sorted;
        }
    }
}
