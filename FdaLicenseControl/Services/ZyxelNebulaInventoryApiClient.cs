using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class ZyxelNebulaInventoryApiClient
    {
        private static readonly HttpClient HttpClient = new();

        private sealed class ZyxelApiHttpException : Exception
        {
            public HttpStatusCode StatusCode { get; }
            public string Url { get; }

            public ZyxelApiHttpException(string message, HttpStatusCode statusCode, string url) : base(message)
            {
                StatusCode = statusCode;
                Url = url;
            }
        }

        public async Task<ZyxelNebulaDownloadResult> DownloadAllAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!ZyxelNebulaSettingsService.IsConfigured())
                throw new InvalidOperationException("La configuration Zyxel Nebula est incomplète.");

            var baseUrl = ZyxelNebulaSettingsService.GetBaseUrl()?.Trim().TrimEnd('/') ?? string.Empty;
            var apiKey = ZyxelNebulaSettingsService.GetApiKey() ?? string.Empty;

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("La configuration Zyxel Nebula est incomplète.");

            var organizationsArray = await GetJsonArrayAsync($"{baseUrl}/v1/nebula/organizations", apiKey, cancellationToken);

            var organizations = new JsonArray();
            var organizationDetails = new JsonArray();
            var sites = new JsonArray();
            var devices = new JsonArray();
            var skippedOrganizations = new JsonArray();

            var index = 0;
            var total = organizationsArray.Count;

            foreach (var orgNode in organizationsArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                if (orgNode is not JsonObject orgObj)
                    throw new InvalidOperationException($"Organisation invalide à l'index {index}.");

                var orgId = orgObj["orgId"]?.GetValue<string>();
                var orgName = orgObj["name"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrEmpty(orgId))
                    throw new InvalidOperationException($"Organisation sans orgId à l'index {index}.");
                if (string.IsNullOrEmpty(orgName))
                    throw new InvalidOperationException($"Organisation sans nom à l'index {index}.");

                // keep original organization copy
                organizations.Add(orgObj.DeepClone());

                // check mode
                var mode = orgObj["mode"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrEmpty(mode))
                    mode = "UNKNOWN";

                progress?.Report($"Organisation {index}/{total} : {orgName}");

                if (!string.Equals(mode, "PRO", StringComparison.OrdinalIgnoreCase))
                {
                    var skip = new JsonObject
                    {
                        ["organizationId"] = orgId,
                        ["organizationName"] = orgName,
                        ["mode"] = mode,
                        ["httpStatus"] = null,
                        ["reason"] = $"Organisation non prise en charge par l’OpenAPI en mode {mode}."
                    };
                    skippedOrganizations.Add(skip);
                    continue;
                }

                // For PRO organizations, perform all calls but keep temporary containers until all succeed
                JsonObject? detailTemp = null;
                JsonArray sitesTemp = new JsonArray();
                JsonArray devicesTemp = new JsonArray();

                try
                {
                    // details
                    detailTemp = await GetJsonObjectAsync($"{baseUrl}/v1/nebula/organizations/{WebUtility.UrlEncode(orgId)}", apiKey, cancellationToken);
                    if (detailTemp is null)
                        throw new InvalidOperationException($"Échec de récupération des détails pour l'organisation {orgId}.");
                    detailTemp["organizationId"] = orgId;
                    detailTemp["organizationName"] = orgName;

                    // sites
                    var sitesArray = await GetJsonArrayAsync($"{baseUrl}/v1/nebula/organizations/{WebUtility.UrlEncode(orgId)}/sites", apiKey, cancellationToken);
                    foreach (var siteNode in sitesArray)
                    {
                        if (siteNode is not JsonObject siteObj)
                            continue;
                        var clone = siteObj.DeepClone();
                        clone["organizationId"] = orgId;
                        clone["organizationName"] = orgName;
                        sitesTemp.Add(clone);
                    }

                    // devices by site groups
                    var devicesArray = await GetJsonArrayAsync($"{baseUrl}/v1/nebula/organizations/{WebUtility.UrlEncode(orgId)}/sites/devices", apiKey, cancellationToken);
                    var deviceCountForOrg = 0;
                    foreach (var groupNode in devicesArray)
                    {
                        if (groupNode is not JsonObject groupObj)
                            continue;
                        var siteId = groupObj["siteId"]?.GetValue<string>() ?? string.Empty;
                        // find site name in sitesArray
                        string siteName = "Site inconnu (" + siteId + ")";
                        foreach (var s in (await Task.FromResult(sitesArray)))
                        {
                            if (s is JsonObject so && (so["siteId"]?.GetValue<string>() ?? string.Empty) == siteId)
                            {
                                siteName = so["name"]?.GetValue<string>() ?? siteName;
                                break;
                            }
                        }

                        var devs = groupObj["devices"] as JsonArray;
                        if (devs is null)
                            continue;

                        foreach (var devNode in devs)
                        {
                            if (devNode is not JsonObject devObj)
                                continue;
                            var clone = devObj.DeepClone();
                            clone["organizationId"] = orgId;
                            clone["organizationName"] = orgName;
                            clone["siteId"] = siteId;
                            clone["siteName"] = siteName;
                            devicesTemp.Add(clone);
                            deviceCountForOrg++;
                        }
                    }

                    // all succeeded for this organization; commit to global arrays
                    organizationDetails.Add(detailTemp.DeepClone());
                    foreach (var s in sitesTemp) sites.Add(s.DeepClone());
                    foreach (var d in devicesTemp) devices.Add(d.DeepClone());

                    progress?.Report($"{deviceCountForOrg} équipements Zyxel récupérés...");
                }
                catch (ZyxelApiHttpException zex) when (zex.StatusCode == HttpStatusCode.Forbidden)
                {
                    // skip this organization only
                    var skip = new JsonObject
                    {
                        ["organizationId"] = orgId,
                        ["organizationName"] = orgName,
                        ["mode"] = "PRO",
                        ["httpStatus"] = (int)HttpStatusCode.Forbidden,
                        ["reason"] = "Accès interdit par Zyxel Nebula pour cette organisation.",
                        ["failedRoute"] = GetPathFromUrl(zex.Url)
                    };
                    skippedOrganizations.Add(skip);
                    progress?.Report($"Organisation ignorée : {orgName} — accès interdit.");
                    continue;
                }
                catch (Exception)
                {
                    // propagate other errors (401, 429, 5xx, JSON parse, etc.)
                    throw;
                }
            }

            var retrievedAt = DateTimeOffset.UtcNow;

            var processedCount = organizationDetails.Count;
            var skippedCount = skippedOrganizations.Count;

            if (processedCount + skippedCount != organizations.Count)
                throw new InvalidOperationException("Le bilan des organisations Zyxel Nebula est incohérent.");

            var resultObj = new JsonObject
            {
                ["retrievedAtUtc"] = retrievedAt.ToString("o"),
                ["organizationCount"] = organizations.Count,
                ["processedOrganizationCount"] = processedCount,
                ["skippedOrganizationCount"] = skippedCount,
                ["siteCount"] = sites.Count,
                ["deviceCount"] = devices.Count,
                ["organizations"] = organizations,
                ["organizationDetails"] = organizationDetails,
                ["sites"] = sites,
                ["devices"] = devices,
                ["skippedOrganizations"] = skippedOrganizations
            };

            // prepare folder
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Data",
                "Zyxel",
                "History");
            Directory.CreateDirectory(folder);

            var fileName = $"zyxel-nebula-inventory-{retrievedAt:yyyyMMdd-HHmmss-fff}.json";
            var finalPath = Path.Combine(folder, fileName);
            var tmpPath = finalPath + ".tmp";

            var options = new JsonSerializerOptions { WriteIndented = true };
            try
            {
                await File.WriteAllTextAsync(tmpPath, resultObj.ToJsonString(options), cancellationToken);
                // move
                if (File.Exists(finalPath))
                    File.Delete(finalPath);
                File.Move(tmpPath, finalPath);
            }
            catch
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                throw;
            }

            return new ZyxelNebulaDownloadResult
            {
                RetrievedAtUtc = retrievedAt,
                OrganizationCount = organizations.Count,
                ProcessedOrganizationCount = processedCount,
                SkippedOrganizationCount = skippedCount,
                SiteCount = sites.Count,
                DeviceCount = devices.Count,
                FilePath = finalPath
            };
        }

        private async Task<JsonArray> GetJsonArrayAsync(string url, string apiKey, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-ZyxelNebula-API-Key", apiKey);
            req.Headers.Add("Accept", "application/json");
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = BuildHttpErrorMessage(url, resp.StatusCode, resp.ReasonPhrase, body, apiKey);
                if (resp.StatusCode == HttpStatusCode.Forbidden)
                    throw new ZyxelApiHttpException(msg, resp.StatusCode, url);
                // for other status codes, stop the extraction
                throw new InvalidOperationException(msg);
            }

            var doc = JsonNode.Parse(body);
            if (doc is JsonArray arr) return arr;
            throw new InvalidOperationException($"Échec de la récupération Zyxel Nebula pour {GetPathFromUrl(url)} : réponse invalide (array attendu).");
        }

        private async Task<JsonObject> GetJsonObjectAsync(string url, string apiKey, CancellationToken cancellationToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("X-ZyxelNebula-API-Key", apiKey);
            req.Headers.Add("Accept", "application/json");
            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                var msg = BuildHttpErrorMessage(url, resp.StatusCode, resp.ReasonPhrase, body, apiKey);
                if (resp.StatusCode == HttpStatusCode.Forbidden)
                    throw new ZyxelApiHttpException(msg, resp.StatusCode, url);
                throw new InvalidOperationException(msg);
            }

            var doc = JsonNode.Parse(body);
            if (doc is JsonObject obj) return obj;
            throw new InvalidOperationException($"Échec de la récupération Zyxel Nebula pour {GetPathFromUrl(url)} : réponse invalide (object attendu).");
        }

        private static string GetPathFromUrl(string url)
        {
            try
            {
                var u = new Uri(url);
                return u.PathAndQuery;
            }
            catch
            {
                return url;
            }
        }

        private static string BuildHttpErrorMessage(string url, HttpStatusCode code, string? reason, string body, string apiKey)
        {
            var path = GetPathFromUrl(url);
            var safeBody = body?.Replace(apiKey, "***") ?? string.Empty;
            // try extract message
            try
            {
                var node = JsonNode.Parse(body);
                var m = node?["message"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(m)) safeBody = m.Replace(apiKey, "***");
            }
            catch { }

            return $"Échec de la récupération Zyxel Nebula pour {path} : HTTP {(int)code} {reason}. {safeBody}";
        }
    }
}
