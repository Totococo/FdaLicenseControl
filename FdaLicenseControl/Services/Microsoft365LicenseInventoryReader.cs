using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public sealed class Microsoft365LicenseInventoryReader
    {
        public async Task<IReadOnlyList<Microsoft365CustomerLicenseInventory>> ReadAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new InvalidOperationException("Le fichier historique Microsoft 365 ne contient pas de tableau customerLicenses valide.");

            await using var stream = File.OpenRead(filePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Le fichier historique Microsoft 365 ne contient pas de tableau customerLicenses valide.");

            if (!root.TryGetProperty("customerLicenses", out var customerLicensesElement) || customerLicensesElement.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Le fichier historique Microsoft 365 ne contient pas de tableau customerLicenses valide.");

            var list = new List<Microsoft365CustomerLicenseInventory>();
            foreach (var entry in customerLicensesElement.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;

                var customerId = ReadString(entry, "customerId");
                var tenantId = ReadString(entry, "tenantId");
                var companyName = ReadString(entry, "companyName");
                var domain = ReadString(entry, "domain");

                if (string.IsNullOrWhiteSpace(companyName))
                    companyName = string.IsNullOrWhiteSpace(domain) ? $"Client Microsoft inconnu ({customerId})" : domain;

                var inventory = BuildInventory(entry, customerId, tenantId, companyName, domain);
                list.Add(inventory);
            }

            var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"), true);
            var ordered = list.OrderBy(x => x.CompanyName, comparer).ToList();

            var totalBasic = ordered.Sum(x => x.BusinessBasicUsed);
            var totalStandard = ordered.Sum(x => x.BusinessStandardUsed);
            var totalGlobal = ordered.Sum(x => x.TotalUsed);
            if (ordered.Count != 28 || totalBasic != 71 || totalStandard != 171 || totalGlobal != 242)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"Microsoft 365 inventory totals: clients={ordered.Count}, basic={totalBasic}, standard={totalStandard}, total={totalGlobal}");
#endif
            }

            return ordered;
        }

        private static Microsoft365CustomerLicenseInventory BuildInventory(JsonElement entryElement, string customerId, string tenantId, string companyName, string domain)
        {
            var businessBasicUsed = 0;
            var businessBasicActive = 0;
            var businessBasicAvailable = 0;
            var businessStandardUsed = 0;
            var businessStandardActive = 0;
            var businessStandardAvailable = 0;
            var exchangeOnlinePlan1Used = 0;
            var exchangeOnlinePlan1Active = 0;
            var exchangeOnlinePlan1Available = 0;
            var teamsEssentialsUsed = 0;
            var teamsEssentialsActive = 0;
            var teamsEssentialsAvailable = 0;

            if (entryElement.TryGetProperty("subscribedSkus", out var subscribedSkusElement) && subscribedSkusElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var skuElement in subscribedSkusElement.EnumerateArray())
                {
                    if (skuElement.ValueKind != JsonValueKind.Object)
                        continue;

                    if (!skuElement.TryGetProperty("productSku", out JsonElement productSkuElement)
                        || productSkuElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var skuPartNumber = ReadString(productSkuElement, "skuPartNumber");
                    var consumedUnits = ReadNonNegativeInt(skuElement, "consumedUnits", companyName);
                    var activeUnits = ReadNonNegativeInt(skuElement, "activeUnits", companyName);
                    var availableUnits = ReadNonNegativeInt(skuElement, "availableUnits", companyName);

#if DEBUG
                    System.Diagnostics.Debug.WriteLine(
                        $"Microsoft SKU: client={companyName}, " +
                        $"skuPartNumber={skuPartNumber}, " +
                        $"consumedUnits={consumedUnits}");
#endif

                    if (string.Equals(skuPartNumber, "O365_BUSINESS_ESSENTIALS", StringComparison.OrdinalIgnoreCase))
                    {
                        businessBasicUsed += consumedUnits;
                        businessBasicActive += activeUnits;
                        businessBasicAvailable += availableUnits;
                    }
                    else if (string.Equals(skuPartNumber, "O365_BUSINESS_PREMIUM", StringComparison.OrdinalIgnoreCase))
                    {
                        businessStandardUsed += consumedUnits;
                        businessStandardActive += activeUnits;
                        businessStandardAvailable += availableUnits;
                    }
                    else if (string.Equals(skuPartNumber, "EXCHANGESTANDARD", StringComparison.OrdinalIgnoreCase))
                    {
                        exchangeOnlinePlan1Used += consumedUnits;
                        exchangeOnlinePlan1Active += activeUnits;
                        exchangeOnlinePlan1Available += availableUnits;
                    }
                    else if (string.Equals(skuPartNumber, "TEAMS_ESSENTIALS_AAD", StringComparison.OrdinalIgnoreCase))
                    {
                        teamsEssentialsUsed += consumedUnits;
                        teamsEssentialsActive += activeUnits;
                        teamsEssentialsAvailable += availableUnits;
                    }
                }
            }

            return new Microsoft365CustomerLicenseInventory
            {
                CustomerId = customerId,
                TenantId = tenantId,
                CompanyName = companyName,
                Domain = domain,
                BusinessBasicUsed = businessBasicUsed,
                BusinessBasicActive = businessBasicActive,
                BusinessBasicAvailable = businessBasicAvailable,
                BusinessStandardUsed = businessStandardUsed,
                BusinessStandardActive = businessStandardActive,
                BusinessStandardAvailable = businessStandardAvailable,
                ExchangeOnlinePlan1Used = exchangeOnlinePlan1Used,
                ExchangeOnlinePlan1Active = exchangeOnlinePlan1Active,
                ExchangeOnlinePlan1Available = exchangeOnlinePlan1Available,
                TeamsEssentialsUsed = teamsEssentialsUsed,
                TeamsEssentialsActive = teamsEssentialsActive,
                TeamsEssentialsAvailable = teamsEssentialsAvailable
            };
        }

        private static int ReadNonNegativeInt(JsonElement element, string propertyName, string companyName)
        {
            if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return 0;

            if (propertyValue.ValueKind != JsonValueKind.Number)
                return 0;

            if (propertyValue.TryGetInt32(out var intValue))
            {
                if (intValue < 0)
                    throw new InvalidOperationException($"Une quantité de licences Microsoft 365 est négative pour le client {companyName}.");

                return intValue;
            }

            if (propertyValue.TryGetInt64(out var longValue))
            {
                if (longValue < 0)
                    throw new InvalidOperationException($"Une quantité de licences Microsoft 365 est négative pour le client {companyName}.");

                return checked((int)longValue);
            }

            var decimalValue = propertyValue.GetDecimal();
            if (decimalValue < 0)
                throw new InvalidOperationException($"Une quantité de licences Microsoft 365 est négative pour le client {companyName}.");

            return checked((int)decimalValue);
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return string.Empty;

            return propertyValue.ValueKind == JsonValueKind.String ? propertyValue.GetString() ?? string.Empty : string.Empty;
        }
    }
}
