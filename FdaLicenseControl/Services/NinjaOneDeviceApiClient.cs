using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class NinjaOneDeviceApiClient
    {
        private static readonly HttpClient HttpClient = new();

        public async Task<NinjaOneDeviceDownloadResult> DownloadAllDevicesAsync(
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!NinjaOneSettingsService.IsConfigured())
                throw new InvalidOperationException("La configuration NinjaOne est incomplète.");

            var baseUrl = NinjaOneSettingsService.GetBaseUrl()?.Trim() ?? string.Empty;
            var clientId = NinjaOneSettingsService.GetClientId()?.Trim() ?? string.Empty;
            var clientSecret = NinjaOneSettingsService.GetClientSecret()?.Trim() ?? string.Empty;

            baseUrl = baseUrl.TrimEnd('/');

            var accessToken = await GetAccessTokenAsync(baseUrl, clientId, clientSecret, cancellationToken);

            var allDevices = new JsonArray();
            string? after = null;
            int previousCount = -1;
            int pageSize = 1000;
            int loopGuard = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                loopGuard++;
                if (loopGuard > 10000)
                    throw new InvalidOperationException("La pagination NinjaOne ne progresse plus.");

                var uri = baseUrl + $"/v2/devices-detailed?pageSize={pageSize}";
                if (!string.IsNullOrEmpty(after))
                    uri += "&after=" + Uri.EscapeDataString(after);

                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                using var resp = await HttpClient.SendAsync(req, cancellationToken);
                var text = await resp.Content.ReadAsStringAsync(cancellationToken);

                if (!resp.IsSuccessStatusCode)
                {
                    // sanitize
                    if (!string.IsNullOrEmpty(clientSecret) && text.Contains(clientSecret, StringComparison.Ordinal))
                        text = text.Replace(clientSecret, "***", StringComparison.Ordinal);

                    var status = (int)resp.StatusCode + " " + resp.StatusCode.ToString();
                    string? error = null;
                    try
                    {
                        using var d = JsonDocument.Parse(text);
                        var root = d.RootElement;
                        if (root.TryGetProperty("error", out var e))
                            error = e.GetString();
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"Échec de la récupération des appareils NinjaOne : HTTP {status} - {error}.");

                    throw new InvalidOperationException($"Échec de la récupération des appareils NinjaOne : HTTP {status}.");
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("Réponse invalide reçue de NinjaOne.");
                }

                if (node is not JsonArray arr)
                    throw new InvalidOperationException("La réponse NinjaOne contenant les appareils n’est pas un tableau JSON.");

                if (arr.Count == 0)
                {
                    progress?.Report(allDevices.Count);
                    break;
                }

                // Validate items and add deep clones
                foreach (var item in arr)
                {
                    if (item is not JsonObject obj)
                        throw new InvalidOperationException("Un élément de la réponse n'est pas un objet JSON.");
                    allDevices.Add(obj.DeepClone());
                }

                progress?.Report(allDevices.Count);

                if (arr.Count < pageSize)
                    break;

                // get id of last element
                var last = arr[^1];
                if (last is not JsonObject lastObj)
                    throw new InvalidOperationException("Dernier élément de la page inattendu.");

                string? lastIdStr = null;
                if (lastObj.TryGetPropertyValue("id", out var idNode) && idNode != null)
                {
                    lastIdStr = idNode.ToString();
                }

                if (string.IsNullOrEmpty(lastIdStr) || !long.TryParse(lastIdStr, out var lastId))
                    throw new InvalidOperationException("Le dernier élément de la page ne contient pas d'identifiant numérique 'id'.");

                if (!string.IsNullOrEmpty(after) && long.TryParse(after, out var prevAfter) && lastId <= prevAfter)
                    throw new InvalidOperationException("La pagination NinjaOne ne progresse plus.");

                after = lastId.ToString();
            }

            // After devices, retrieve organizations and locations
            var organizations = new JsonArray();
            var locations = new JsonArray();

            // Get organizations
            var orgsUri = baseUrl + "/v2/organizations";
            using (var orgReq = new HttpRequestMessage(HttpMethod.Get, orgsUri))
            {
                orgReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                orgReq.Headers.Accept.Clear();
                orgReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                using var orgResp = await HttpClient.SendAsync(orgReq, cancellationToken);
                var orgText = await orgResp.Content.ReadAsStringAsync(cancellationToken);

                if (!orgResp.IsSuccessStatusCode)
                {
                    if (!string.IsNullOrEmpty(clientSecret) && orgText.Contains(clientSecret, StringComparison.Ordinal))
                        orgText = orgText.Replace(clientSecret, "***", StringComparison.Ordinal);

                    var status = (int)orgResp.StatusCode + " " + orgResp.StatusCode.ToString();
                    string? error = null;
                    try
                    {
                        using var d = JsonDocument.Parse(orgText);
                        var root = d.RootElement;
                        if (root.TryGetProperty("error", out var e))
                            error = e.GetString();
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"Échec de la récupération des organisations NinjaOne : GET {orgsUri} - HTTP {status} - {error}.");

                    throw new InvalidOperationException($"Échec de la récupération des organisations NinjaOne : GET {orgsUri} - HTTP {status}.");
                }

                JsonNode? orgNode;
                try
                {
                    orgNode = JsonNode.Parse(orgText);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException("Réponse invalide reçue de NinjaOne pour les organisations.");
                }

                if (orgNode is not JsonArray orgArr)
                    throw new InvalidOperationException("La réponse NinjaOne contenant les organisations n’est pas un tableau JSON.");

                foreach (var item in orgArr)
                {
                    if (item is not JsonObject obj)
                        throw new InvalidOperationException("Un élément de la réponse des organisations n'est pas un objet JSON.");

                    // Validate id and name
                    if (!obj.TryGetPropertyValue("id", out var idNode) || idNode == null || !long.TryParse(idNode.ToString(), out var orgId))
                        throw new InvalidOperationException("Une organisation ne contient pas d'identifiant numérique 'id'.");
                    if (!obj.TryGetPropertyValue("name", out var nameNode) || nameNode == null || string.IsNullOrEmpty(nameNode.ToString()))
                        throw new InvalidOperationException($"L'organisation {orgId} ne contient pas de propriété texte 'name'.");

                    organizations.Add(obj.DeepClone());
                }
            }

            // For each organization get locations
            foreach (var org in organizations)
            {
                if (org is not JsonObject orgObj)
                    throw new InvalidOperationException("Organisation inattendue lors du traitement des emplacements.");

                if (!orgObj.TryGetPropertyValue("id", out var idNode) || idNode == null || !long.TryParse(idNode.ToString(), out var orgId))
                    throw new InvalidOperationException("Une organisation ne contient pas d'identifiant numérique 'id'.");

                var orgName = orgObj.TryGetPropertyValue("name", out var nameNode) && nameNode != null ? nameNode.ToString() ?? string.Empty : string.Empty;

                var locUri = baseUrl + $"/v2/organization/{orgId}/locations";
                using var locReq = new HttpRequestMessage(HttpMethod.Get, locUri);
                locReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
                locReq.Headers.Accept.Clear();
                locReq.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                using var locResp = await HttpClient.SendAsync(locReq, cancellationToken);
                var locText = await locResp.Content.ReadAsStringAsync(cancellationToken);

                if (!locResp.IsSuccessStatusCode)
                {
                    if (!string.IsNullOrEmpty(clientSecret) && locText.Contains(clientSecret, StringComparison.Ordinal))
                        locText = locText.Replace(clientSecret, "***", StringComparison.Ordinal);

                    var status = (int)locResp.StatusCode + " " + locResp.StatusCode.ToString();
                    string? error = null;
                    try
                    {
                        using var d = JsonDocument.Parse(locText);
                        var root = d.RootElement;
                        if (root.TryGetProperty("error", out var e))
                            error = e.GetString();
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(error))
                        throw new InvalidOperationException($"Échec de la récupération des emplacements pour l'organisation '{orgName}' ({orgId}) : GET {locUri} - HTTP {status} - {error}.");

                    throw new InvalidOperationException($"Échec de la récupération des emplacements pour l'organisation '{orgName}' ({orgId}) : GET {locUri} - HTTP {status}.");
                }

                JsonNode? locNode;
                try
                {
                    locNode = JsonNode.Parse(locText);
                }
                catch (JsonException)
                {
                    throw new InvalidOperationException($"La réponse des emplacements pour l'organisation '{orgName}' ({orgId}) n'est pas un JSON valide.");
                }

                if (locNode is not JsonArray locArr)
                    throw new InvalidOperationException($"La réponse NinjaOne contenant les emplacements pour l'organisation '{orgName}' ({orgId}) n’est pas un tableau JSON.");

                foreach (var item in locArr)
                {
                    if (item is not JsonObject locObj)
                        throw new InvalidOperationException($"Un élément des emplacements pour l'organisation '{orgName}' ({orgId}) n'est pas un objet JSON.");

                    if (!locObj.TryGetPropertyValue("id", out var lid) || lid == null || !long.TryParse(lid.ToString(), out var locId))
                        throw new InvalidOperationException($"Un emplacement pour l'organisation '{orgName}' ({orgId}) ne contient pas d'identifiant numérique 'id'.");
                    if (!locObj.TryGetPropertyValue("name", out var lname) || lname == null || string.IsNullOrEmpty(lname.ToString()))
                        throw new InvalidOperationException($"Un emplacement pour l'organisation '{orgName}' ({orgId}) ne contient pas de propriété texte 'name'.");

                    var clone = locObj.DeepClone() as JsonObject ?? throw new InvalidOperationException("Erreur lors de la copie d'un emplacement.");
                    // add or replace organizationId numeric
                    clone["organizationId"] = orgId;
                    locations.Add(clone);
                }
            }

            // Prepare output with organizations and locations included
            var utcNow = DateTimeOffset.UtcNow;

            var rootObj = new JsonObject
            {
                ["retrievedAtUtc"] = utcNow,
                ["deviceCount"] = allDevices.Count,
                ["organizationCount"] = organizations.Count,
                ["locationCount"] = locations.Count,
                ["organizations"] = organizations,
                ["locations"] = locations,
                ["devices"] = allDevices
            };

            var historyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FdaLicenseControl", "Data", "History");
            Directory.CreateDirectory(historyFolder);

            var fileName = $"ninjaone-inventory-{utcNow:yyyyMMdd-HHmmss-fff}.json";
            var finalPath = Path.Combine(historyFolder, fileName);
            var tempPath = finalPath + ".tmp";

            var options = new JsonSerializerOptions { WriteIndented = true };
            try
            {
                var json = rootObj.ToJsonString(options);
                await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken);
                // Move temporary to final. Do not delete existing historical files.
                File.Move(tempPath, finalPath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }

            return new NinjaOneDeviceDownloadResult
            {
                DeviceCount = allDevices.Count,
                RetrievedAtUtc = utcNow,
                FilePath = finalPath
            };
        }

        private static async Task<string> GetAccessTokenAsync(
            string baseUrl,
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken)
        {
            var uri = baseUrl + "/ws/oauth/token";
            var content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string,string>("grant_type", "client_credentials"),
                new KeyValuePair<string,string>("client_id", clientId),
                new KeyValuePair<string,string>("client_secret", clientSecret),
                new KeyValuePair<string,string>("scope", "monitoring")
            });

            using var resp = await HttpClient.PostAsync(uri, content, cancellationToken);
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                if (!string.IsNullOrEmpty(clientSecret) && text.Contains(clientSecret, StringComparison.Ordinal))
                    text = text.Replace(clientSecret, "***", StringComparison.Ordinal);

                var status = (int)resp.StatusCode + " " + resp.StatusCode.ToString();
                string? error = null;
                string? errorDescription = null;
                try
                {
                    using var d = JsonDocument.Parse(text);
                    var root = d.RootElement;
                    if (root.TryGetProperty("error", out var e))
                        error = e.GetString();
                    if (root.TryGetProperty("error_description", out var ed))
                        errorDescription = ed.GetString();
                }
                catch { }

                if (!string.IsNullOrEmpty(error))
                {
                    var msg = $"Échec de l’authentification NinjaOne : HTTP {status} - {error}.";
                    if (!string.IsNullOrEmpty(errorDescription))
                        msg = $"Échec de l’authentification NinjaOne : HTTP {status} - {error} - {errorDescription}.";
                    throw new InvalidOperationException(msg);
                }

                throw new InvalidOperationException($"Échec de l’authentification NinjaOne : HTTP {status}.");
            }

            try
            {
                using var d = JsonDocument.Parse(text);
                var root = d.RootElement;
                if (!root.TryGetProperty("access_token", out var t) || string.IsNullOrEmpty(t.GetString()))
                    throw new InvalidOperationException("L’authentification NinjaOne n’a retourné aucun jeton d’accès.");

                return t.GetString()!;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Réponse invalide reçue de NinjaOne.");
            }
        }
    }
}
