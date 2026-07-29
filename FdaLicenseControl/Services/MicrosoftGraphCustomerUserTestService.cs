using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FdaLicenseControl.Services
{
    public sealed class MicrosoftGraphCustomerUserTestResult
    {
        public DateTimeOffset RetrievedAtUtc { get; init; }

        public string TenantId { get; init; } = string.Empty;

        public string AccountName { get; init; } = string.Empty;

        public int UserCount { get; init; }

        public int LicensedUserCount { get; init; }

        public int UserWithOfficeCount { get; init; }

        public int OfficeCount { get; init; }

        public int AssignedLicenseCount { get; init; }

        public string FilePath { get; init; } = string.Empty;
    }

    public sealed class MicrosoftGraphCustomerUserTestService
    {
        private static readonly HttpClient s_httpClient = new HttpClient();
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public async Task<MicrosoftGraphCustomerUserTestResult> TestAsync(
            string customerTenantId,
            IntPtr parentWindowHandle,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(customerTenantId, out var tenantIdGuid) || tenantIdGuid == Guid.Empty)
                throw new InvalidOperationException("L’identifiant du tenant Microsoft Graph n’est pas valide.");

            if (!MicrosoftPartnerSettingsService.IsConfigured())
                throw new InvalidOperationException("La configuration Microsoft 365 n’est pas disponible.");

            var clientId = MicrosoftPartnerSettingsService.GetClientId().Trim();
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("La configuration Microsoft 365 n’est pas disponible.");

            var app = MicrosoftGraphPublicClientApplicationFactory.Create(
                clientId,
                "https://login.microsoftonline.com/organizations");

            const string scope = "https://graph.microsoft.com/User.Read.All";
            AuthenticationResult authResult = await AcquireInteractiveAsync(app, customerTenantId, scope, parentWindowHandle, null, cancellationToken).ConfigureAwait(false);

            if (authResult.Account == null || string.IsNullOrWhiteSpace(authResult.Account.Username))
                throw new InvalidOperationException("L’authentification Microsoft Graph n’a retourné aucun compte.");

            var accountName = authResult.Account.Username;
            progress?.Report($"Connexion à Microsoft Graph pour le tenant {customerTenantId}...");

            var users = new List<JsonElement>();
            var officeGroups = new Dictionary<string, OfficeSummaryAccumulator>(StringComparer.OrdinalIgnoreCase);

            var pageUrl = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,officeLocation,assignedLicenses,accountEnabled&$top=999";
            while (!string.IsNullOrWhiteSpace(pageUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"Lecture Microsoft Graph en cours... {users.Count} utilisateurs récupérés.");

                using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                using var response = await s_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    throw await CreateGraphExceptionAsync(response, cancellationToken).ConfigureAwait(false);

                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Microsoft Graph a retourné une réponse inattendue.");

                if (root.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var userElement in valueElement.EnumerateArray())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        users.Add(userElement.Clone());
                        UpdateOfficeAggregation(officeGroups, userElement);
                    }
                }

                pageUrl = GetNextLink(root);
            }

            var userCount = users.Count;
            var licensedUserCount = users.Count(user => HasAssignedLicenses(user));
            var userWithOfficeCount = users.Count(user => HasOfficeLocation(user));
            var assignedLicenseCount = users.Sum(GetAssignedLicenseCount);
            var officeSummary = officeGroups
                .Select(group => new MicrosoftGraphOfficeSummary
                {
                    OfficeLocation = group.Value.DisplayName,
                    UserCount = group.Value.UserCount,
                    LicensedUserCount = group.Value.LicensedUserCount,
                    AssignedLicenseCount = group.Value.AssignedLicenseCount
                })
                .OrderBy(item => item.OfficeLocation, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var officeCount = officeSummary.Count;

            var outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Data",
                "Microsoft365",
                "GraphTests");
            Directory.CreateDirectory(outputFolder);

            var fileName = $"microsoft365-graph-users-association-du-may-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.json";
            var finalFilePath = Path.Combine(outputFolder, fileName);
            var tempFilePath = finalFilePath + ".tmp";

            var diagnosticDocument = new MicrosoftGraphDiagnosticDocument
            {
                RetrievedAtUtc = DateTimeOffset.UtcNow,
                TenantId = customerTenantId,
                AccountName = accountName,
                UserCount = userCount,
                LicensedUserCount = licensedUserCount,
                UserWithOfficeCount = userWithOfficeCount,
                OfficeCount = officeCount,
                AssignedLicenseCount = assignedLicenseCount,
                OfficeSummary = officeSummary,
                Users = users
            };

            try
            {
                await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await JsonSerializer.SerializeAsync(stream, diagnosticDocument, s_jsonOptions, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(tempFilePath, finalFilePath, true);
            }
            catch
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);

                throw;
            }

            progress?.Report($"Microsoft Graph terminé : {userCount} utilisateurs, {licensedUserCount} utilisateurs licenciés, {officeCount} bureaux.");

            return new MicrosoftGraphCustomerUserTestResult
            {
                RetrievedAtUtc = DateTimeOffset.UtcNow,
                TenantId = customerTenantId,
                AccountName = accountName,
                UserCount = userCount,
                LicensedUserCount = licensedUserCount,
                UserWithOfficeCount = userWithOfficeCount,
                OfficeCount = officeCount,
                AssignedLicenseCount = assignedLicenseCount,
                FilePath = finalFilePath
            };
        }

        private static async Task<AuthenticationResult> AcquireInteractiveAsync(IPublicClientApplication app, string customerTenantId, string scope, IntPtr parentWindowHandle, IAccount? account, CancellationToken cancellationToken)
        {
            try
            {
                var interactive = app
                    .AcquireTokenInteractive(new[] { scope })
                    .WithTenantId(customerTenantId)
                    .WithPrompt(Prompt.SelectAccount)
                    .WithParentActivityOrWindow(parentWindowHandle)
                    .WithUseEmbeddedWebView(true);

                if (account != null)
                    interactive = interactive.WithAccount(account);

                return await interactive.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw new InvalidOperationException("L’authentification Microsoft a été annulée.");
            }
            catch (MsalException ex) when (IsInteractiveCancellation(ex))
            {
                throw new InvalidOperationException("L’authentification Microsoft a été annulée.");
            }
        }

        private static bool IsInteractiveCancellation(MsalException ex)
        {
            return string.Equals(ex.ErrorCode, "authentication_canceled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "user_cancelled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ex.ErrorCode, "cancelled", StringComparison.OrdinalIgnoreCase);
        }

        private static string? GetNextLink(JsonElement root)
        {
            if (!root.TryGetProperty("@odata.nextLink", out var nextLinkElement) || nextLinkElement.ValueKind != JsonValueKind.String)
                return null;

            var nextLink = nextLinkElement.GetString();
            if (string.IsNullOrWhiteSpace(nextLink))
                return null;

            if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var nextUri)
                || nextUri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(nextUri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Microsoft Graph a retourné une adresse de pagination non autorisée.");
            }

            return nextLink;
        }

        private static void UpdateOfficeAggregation(Dictionary<string, OfficeSummaryAccumulator> officeGroups, JsonElement userElement)
        {
            var officeLocation = ReadString(userElement, "officeLocation");
            var key = NormalizeOfficeLocation(officeLocation);
            if (!officeGroups.TryGetValue(key, out var summary))
            {
                summary = new OfficeSummaryAccumulator
                {
                    DisplayName = string.IsNullOrWhiteSpace(officeLocation) ? "Sans bureau" : officeLocation.Trim()
                };
                officeGroups[key] = summary;
            }
            else if (summary.IsBlankGroup && string.IsNullOrWhiteSpace(summary.DisplayName))
            {
                summary.DisplayName = "Sans bureau";
            }
            else if (!summary.IsBlankGroup && !string.IsNullOrWhiteSpace(officeLocation) && string.IsNullOrWhiteSpace(summary.DisplayName))
            {
                summary.DisplayName = officeLocation.Trim();
            }

            summary.UserCount++;
            if (HasAssignedLicenses(userElement))
                summary.LicensedUserCount++;

            summary.AssignedLicenseCount += GetAssignedLicenseCount(userElement);
        }

        private static string NormalizeOfficeLocation(string? officeLocation)
        {
            if (string.IsNullOrWhiteSpace(officeLocation))
                return string.Empty;

            return officeLocation.Trim();
        }

        private static bool HasOfficeLocation(JsonElement userElement)
        {
            var officeLocation = ReadString(userElement, "officeLocation");
            return !string.IsNullOrWhiteSpace(officeLocation);
        }

        private static bool HasAssignedLicenses(JsonElement userElement)
        {
            return userElement.TryGetProperty("assignedLicenses", out var licensesElement)
                && licensesElement.ValueKind == JsonValueKind.Array
                && licensesElement.GetArrayLength() > 0;
        }

        private static int GetAssignedLicenseCount(JsonElement userElement)
        {
            if (!userElement.TryGetProperty("assignedLicenses", out var licensesElement) || licensesElement.ValueKind != JsonValueKind.Array)
                return 0;

            return licensesElement.GetArrayLength();
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return string.Empty;

            return propertyValue.ValueKind == JsonValueKind.String ? propertyValue.GetString() ?? string.Empty : string.Empty;
        }

        private static async Task<InvalidOperationException> CreateGraphExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var details = await ReadGraphErrorDetailsAsync(response, cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            string message;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                message = "La session Microsoft Graph n’est plus autorisée.";
            }
            else if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                message = "Microsoft Graph refuse l’accès au tenant Association du May. Vérifiez le consentement User.Read.All et les rôles GDAP du compte connecté.";
            }
            else if (response.StatusCode == (HttpStatusCode)429)
            {
                message = "La limite de requêtes Microsoft Graph a été atteinte.";
                if (response.Headers.RetryAfter != null)
                    message += $" Retry-After: {response.Headers.RetryAfter}";
            }
            else if (statusCode >= 500 && statusCode <= 599)
            {
                message = $"Microsoft Graph rencontre une erreur temporaire : HTTP {statusCode}.";
            }
            else
            {
                var reasonPhrase = response.ReasonPhrase ?? string.Empty;
                message = $"Échec Microsoft Graph : HTTP {statusCode} {reasonPhrase}.";
            }

            if (!string.IsNullOrWhiteSpace(details))
                message += Environment.NewLine + details;

            return new InvalidOperationException(message);
        }

        private static async Task<string> ReadGraphErrorDetailsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    var code = ReadString(errorElement, "code");
                    var message = ReadString(errorElement, "message");
                    if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
                    {
                        var parts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(code))
                            parts.Add($"Code Graph : {code}");
                        if (!string.IsNullOrWhiteSpace(message))
                            parts.Add($"Message Graph : {message}");
                        return string.Join(Environment.NewLine, parts);
                    }
                }
            }
            catch
            {
                // ignore diagnostic parsing errors
            }

            return string.Empty;
        }

        private sealed class OfficeSummaryAccumulator
        {
            public string DisplayName { get; set; } = string.Empty;

            public int UserCount { get; set; }

            public int LicensedUserCount { get; set; }

            public int AssignedLicenseCount { get; set; }

            public bool IsBlankGroup => string.Equals(DisplayName, "Sans bureau", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class MicrosoftGraphOfficeSummary
        {
            public string OfficeLocation { get; init; } = string.Empty;

            public int UserCount { get; init; }

            public int LicensedUserCount { get; init; }

            public int AssignedLicenseCount { get; init; }
        }

        private sealed class MicrosoftGraphDiagnosticDocument
        {
            public DateTimeOffset RetrievedAtUtc { get; init; }

            public string TenantId { get; init; } = string.Empty;

            public string AccountName { get; init; } = string.Empty;

            public int UserCount { get; init; }

            public int LicensedUserCount { get; init; }

            public int UserWithOfficeCount { get; init; }

            public int OfficeCount { get; init; }

            public int AssignedLicenseCount { get; init; }

            public List<MicrosoftGraphOfficeSummary> OfficeSummary { get; init; } = new List<MicrosoftGraphOfficeSummary>();

            public List<JsonElement> Users { get; init; } = new List<JsonElement>();
        }
    }
}
