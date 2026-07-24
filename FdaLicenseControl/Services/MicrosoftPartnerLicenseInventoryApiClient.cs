using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class MicrosoftPartnerLicenseInventoryApiClient
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public async Task<Microsoft365LicenseDownloadResult> DownloadAllAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!MicrosoftPartnerSettingsService.IsConfigured())
                throw new InvalidOperationException("La configuration Microsoft 365 est incomplète.");

            var partnerTenantId = MicrosoftPartnerSettingsService.GetPartnerTenantId().Trim();
            var clientId = MicrosoftPartnerSettingsService.GetClientId().Trim();
            var baseUrl = MicrosoftPartnerSettingsService.GetBaseUrl().Trim().TrimEnd('/');

            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "https://api.partnercenter.microsoft.com";

            var authService = new MicrosoftPartnerAuthenticationService();
            MicrosoftPartnerTokenContext tokenContext = await authService.AcquireTokenContextAsync(partnerTenantId, clientId, cancellationToken);

            var correlationId = Guid.NewGuid();
            var customersUri = new Uri($"{baseUrl}/v1/customers?size=200", UriKind.Absolute);

            var customerNodes = new List<JsonNode>();
            var customerObjects = new List<JsonObject>();
            var customersForLicenses = new List<CustomerDescriptor>();
            var skippedCustomers = new List<JsonObject>();
            int? firstPageCustomerCount = null;

            Uri? nextUri = customersUri;
            while (nextUri != null)
            {
                var response = await SendJsonRequestAsync(nextUri, tokenContext.AccessToken, correlationId, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw await CreateHttpExceptionAsync(response, cancellationToken);

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                var customersRoot = doc.RootElement;
                if (customersRoot.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("La réponse Partner Center n'est pas un objet JSON.");

                if (customersRoot.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemElement in itemsElement.EnumerateArray())
                    {
                        var cloned = JsonNode.Parse(itemElement.GetRawText())?.DeepClone();
                        if (cloned != null)
                            customerNodes.Add(cloned);

                        var customerObject = JsonNode.Parse(itemElement.GetRawText()) as JsonObject;
                        if (customerObject == null)
                            continue;

                        customerObjects.Add(customerObject);
                        var descriptor = BuildCustomerDescriptor(customerObject);
                        if (descriptor == null)
                        {
                            skippedCustomers.Add(BuildSkippedCustomerObject(customerObject, "Identifiant Partner Center absent.", null));
                            continue;
                        }

                        customersForLicenses.Add(descriptor);
                    }
                }

                if (firstPageCustomerCount == null)
                    firstPageCustomerCount = customersForLicenses.Count + skippedCustomers.Count;

                var nextLink = GetNextLink(customersRoot, customersUri);
                nextUri = nextLink;
            }

            var customerCount = customerObjects.Count;
            var retrievedAtUtc = DateTimeOffset.UtcNow;
            var processedCustomerLicenses = new List<JsonObject>();
            int subscribedSkuCount = 0;
            int processedCustomerCount = 0;
            int skippedCustomerCount = skippedCustomers.Count;

            foreach (var customer in customersForLicenses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Client {processedCustomerCount + skippedCustomerCount + 1}/{customerCount} : {customer.CompanyName}");

                var route = $"/v1/customers/{Uri.EscapeDataString(customer.CustomerId)}/subscribedskus";
                Uri requestUri = ResolveRequestUri(baseUrl, route, customersUri);

                using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenContext.AccessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Add("ValidateMfa", "true");
                request.Headers.Add("MS-RequestId", Guid.NewGuid().ToString());
                request.Headers.Add("MS-CorrelationId", correlationId.ToString());

                HttpResponseMessage response;
                try
                {
                    response = await HttpClient.SendAsync(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Échec de la récupération des licences Microsoft 365 : {ex.Message}", ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new InvalidOperationException("La session Microsoft n'est plus autorisée.");

                    if ((int)response.StatusCode == 429)
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta?.ToString() ?? response.Headers.RetryAfter?.Date?.ToString("O") ?? string.Empty;
                        var message = "La limite de requêtes Partner Center a été atteinte.";
                        if (!string.IsNullOrEmpty(retryAfter))
                            message += $" Retry-After: {retryAfter}.";
                        throw new InvalidOperationException(message);
                    }

                    if ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599)
                        throw new InvalidOperationException($"Échec de la récupération des licences Microsoft 365 : HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");

                    if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
                    {
                        skippedCustomers.Add(BuildSkippedCustomerObject(
                            customer.RawObject,
                            GetFailureReason(response),
                            route,
                            response.StatusCode));
                        skippedCustomerCount++;
                        progress?.Report($"{processedCustomerCount} client(s) Microsoft 365 traité(s)...");
                        continue;
                    }

                    throw await CreateHttpExceptionAsync(response, cancellationToken);
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                JsonObject? rootObject = JsonNode.Parse(content) as JsonObject;
                if (rootObject == null)
                    throw new InvalidOperationException("La réponse Partner Center n'est pas un objet JSON.");

                var skuItems = new JsonArray();
                if (rootObject["items"] is JsonArray itemsArray)
                {
                    foreach (var item in itemsArray)
                    {
                        var cloned = item?.DeepClone();
                        if (cloned != null)
                            skuItems.Add(cloned);
                    }
                }

                var customerLicense = new JsonObject
                {
                    ["customerId"] = customer.CustomerId,
                    ["tenantId"] = customer.TenantId,
                    ["companyName"] = customer.CompanyName,
                    ["domain"] = customer.Domain,
                    ["relationshipToPartner"] = customer.RelationshipToPartner,
                    ["subscribedSkuCount"] = skuItems.Count,
                    ["subscribedSkus"] = skuItems
                };

                processedCustomerLicenses.Add(customerLicense);
                processedCustomerCount++;
                subscribedSkuCount += skuItems.Count;
                progress?.Report($"{processedCustomerCount} client(s) Microsoft 365 traité(s)...");
            }

            if (processedCustomerCount + skippedCustomerCount != customerCount)
                throw new InvalidOperationException("Le bilan des clients Microsoft 365 est incohérent.");

            var result = new Microsoft365LicenseDownloadResult
            {
                RetrievedAtUtc = retrievedAtUtc,
                CustomerCount = customerCount,
                ProcessedCustomerCount = processedCustomerCount,
                SkippedCustomerCount = skippedCustomerCount,
                SubscribedSkuCount = subscribedSkuCount,
                AccountName = tokenContext.AccountName
            };

            var historyDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Data",
                "Microsoft365",
                "History");
            Directory.CreateDirectory(historyDirectory);

            var fileName = $"microsoft365-licenses-{retrievedAtUtc.UtcDateTime:yyyyMMdd-HHmmss-fff}.json";
            var filePath = Path.Combine(historyDirectory, fileName);
            var tempPath = filePath + ".tmp";

            var root = new JsonObject
            {
                ["retrievedAtUtc"] = retrievedAtUtc.ToString("O"),
                ["accountName"] = tokenContext.AccountName,
                ["customerCount"] = customerCount,
                ["processedCustomerCount"] = processedCustomerCount,
                ["skippedCustomerCount"] = skippedCustomerCount,
                ["subscribedSkuCount"] = subscribedSkuCount,
                ["customers"] = BuildCustomersArray(customerObjects),
                ["customerLicenses"] = BuildJsonArray(processedCustomerLicenses),
                ["skippedCustomers"] = BuildJsonArray(skippedCustomers)
            };

#if DEBUG
            Debug.WriteLine($"Microsoft 365 customers: totalCount={customerCount}, firstPageItems={firstPageCustomerCount ?? 0}, reportedCount={customerCount}");
#endif

            if (root["customerLicenses"] is JsonArray licenseArray && root["skippedCustomers"] is JsonArray skippedArray)
            {
                if (licenseArray.Count + skippedArray.Count != customerCount)
                    throw new InvalidOperationException("Le bilan des clients Microsoft 365 est incohérent.");
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(tempPath, root.ToJsonString(options), cancellationToken);

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(tempPath, filePath);
            }
            catch
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }

                throw;
            }

            return new Microsoft365LicenseDownloadResult
            {
                RetrievedAtUtc = retrievedAtUtc,
                CustomerCount = customerCount,
                ProcessedCustomerCount = processedCustomerCount,
                SkippedCustomerCount = skippedCustomerCount,
                SubscribedSkuCount = subscribedSkuCount,
                AccountName = tokenContext.AccountName,
                FilePath = filePath
            };
        }

        private static CustomerDescriptor? BuildCustomerDescriptor(JsonObject customerObject)
        {
            var customerId = GetString(customerObject, "id");
            if (string.IsNullOrWhiteSpace(customerId))
                return null;

            var companyProfile = customerObject["companyProfile"] as JsonObject;
            var tenantId = GetString(companyProfile, "tenantId");
            var companyName = GetString(companyProfile, "companyName");
            var domain = GetString(companyProfile, "domain");
            var relationshipToPartner = GetString(customerObject, "relationshipToPartner");

            if (string.IsNullOrWhiteSpace(companyName))
                companyName = string.IsNullOrWhiteSpace(domain) ? $"Client Microsoft inconnu ({customerId})" : domain;

            return new CustomerDescriptor(customerId, tenantId, companyName, domain, relationshipToPartner, customerObject.DeepClone() as JsonObject ?? new JsonObject());
        }

        private static JsonObject BuildSkippedCustomerObject(JsonObject customerObject, string reason, string? failedRoute, HttpStatusCode? httpStatus = null)
        {
            var companyProfile = customerObject["companyProfile"] as JsonObject;
            var customerId = GetString(customerObject, "id");
            var tenantId = GetString(companyProfile, "tenantId");
            var companyName = GetString(companyProfile, "companyName");
            var domain = GetString(companyProfile, "domain");

            if (string.IsNullOrWhiteSpace(companyName))
                companyName = string.IsNullOrWhiteSpace(domain) ? $"Client Microsoft inconnu ({customerId})" : domain;

            var skipped = new JsonObject
            {
                ["customerId"] = customerId,
                ["tenantId"] = tenantId,
                ["companyName"] = companyName,
                ["domain"] = domain,
                ["reason"] = reason
            };

            if (!string.IsNullOrWhiteSpace(failedRoute))
                skipped["failedRoute"] = failedRoute;

            if (httpStatus.HasValue)
                skipped["httpStatus"] = (int)httpStatus.Value;

            return skipped;
        }

        private static string GetFailureReason(HttpResponseMessage response)
        {
            var reason = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
            try
            {
                return reason;
            }
            catch
            {
                return reason;
            }
        }

        private static async Task<HttpResponseMessage> SendJsonRequestAsync(Uri requestUri, string accessToken, Guid correlationId, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("ValidateMfa", "true");
            request.Headers.Add("MS-RequestId", Guid.NewGuid().ToString());
            request.Headers.Add("MS-CorrelationId", correlationId.ToString());
            return await HttpClient.SendAsync(request, cancellationToken);
        }

        private static Uri? GetNextLink(JsonElement root, Uri baseUri)
        {
            if (!root.TryGetProperty("links", out var linksElement) ||
                linksElement.ValueKind != JsonValueKind.Object ||
                !linksElement.TryGetProperty("next", out var nextElement) ||
                nextElement.ValueKind != JsonValueKind.Object ||
                !nextElement.TryGetProperty("uri", out var uriElement) ||
                uriElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var nextUriValue = uriElement.GetString();
            if (string.IsNullOrWhiteSpace(nextUriValue))
                return null;

            if (Uri.TryCreate(nextUriValue, UriKind.Relative, out var relativeUri))
                return new Uri(baseUri, relativeUri);

            if (!Uri.TryCreate(nextUriValue, UriKind.Absolute, out var absoluteUri))
                throw new InvalidOperationException("L’API Partner Center a retourné une adresse de pagination non autorisée.");

            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(absoluteUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("L’API Partner Center a retourné une adresse de pagination non autorisée.");

            return absoluteUri;
        }

        private static Uri ResolveRequestUri(string baseUrl, string route, Uri baseUri)
        {
            var requestUri = new Uri($"{baseUrl}{route}", UriKind.Absolute);
            if (!string.Equals(requestUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(requestUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("L’API Partner Center a retourné une adresse de pagination non autorisée.");
            }

            return requestUri;
        }

        private static async Task<InvalidOperationException> CreateHttpExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var message = $"Échec de la récupération des licences Microsoft 365 : HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
            try
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("message", out var messageEl))
                        message += $" {messageEl.GetString()}";
                }
            }
            catch
            {
            }

            return new InvalidOperationException(message);
        }

        private static string GetString(JsonObject? obj, string propertyName)
        {
            if (obj == null)
                return string.Empty;

            if (!obj.TryGetPropertyValue(propertyName, out var node) || node == null)
                return string.Empty;

            return node.GetValue<string>();
        }

        private static JsonArray BuildCustomersArray(IEnumerable<JsonObject> customerObjects)
        {
            var array = new JsonArray();
            foreach (var customerObject in customerObjects)
            {
                array.Add(customerObject.DeepClone());
            }

            return array;
        }

        private static JsonArray BuildJsonArray(IEnumerable<JsonObject> items)
        {
            var array = new JsonArray();
            foreach (var item in items)
            {
                array.Add(item.DeepClone());
            }

            return array;
        }

        private sealed class CustomerDescriptor
        {
            public CustomerDescriptor(string customerId, string tenantId, string companyName, string domain, string relationshipToPartner, JsonObject rawObject)
            {
                CustomerId = customerId;
                TenantId = tenantId;
                CompanyName = companyName;
                Domain = domain;
                RelationshipToPartner = relationshipToPartner;
                RawObject = rawObject;
            }

            public string CustomerId { get; }

            public string TenantId { get; }

            public string CompanyName { get; }

            public string Domain { get; }

            public string RelationshipToPartner { get; }

            public JsonObject RawObject { get; }
        }
    }
}
