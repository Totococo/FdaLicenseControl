using System;
using System.Collections.Generic;
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
using Microsoft.Identity.Client;

namespace FdaLicenseControl.Services
{
    public sealed class MicrosoftGraphCustomerUsersService
    {
        private static readonly HttpClient s_httpClient = new HttpClient();
        private static readonly string[] s_scopes = ["https://graph.microsoft.com/User.Read.All"];
        private const string PreferredPartnerAccountUsername = "thomas@fda31.fr";
        private IPublicClientApplication? _application;
        private IAccount? _account;
        private IntPtr _parentWindowHandle;

        public async Task<IAccount> InitializeAccountAsync(
            string partnerTenantId,
            string clientId,
            IntPtr parentWindowHandle,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParse(partnerTenantId, out var partnerTenantGuid) || partnerTenantGuid == Guid.Empty)
                throw new InvalidOperationException("L’identifiant du tenant Microsoft Graph n’est pas valide.");

            if (!Guid.TryParse(clientId, out var clientGuid) || clientGuid == Guid.Empty)
                throw new InvalidOperationException("L’identifiant du tenant Microsoft Graph n’est pas valide.");

            _application = MicrosoftGraphPublicClientApplicationFactory.Create(
                clientId,
                "https://login.microsoftonline.com/organizations");
            _parentWindowHandle = parentWindowHandle;

            IReadOnlyList<IAccount> accounts = (await _application
                .GetAccountsAsync()
                .ConfigureAwait(false))
                .ToList();

            var existingAccount = SelectPreferredAccount(accounts);
            AuthenticationResult? authenticationResult = null;
            authenticationResult = await AcquireAuthenticationResultAsync(
                partnerTenantId,
                existingAccount,
                cancellationToken).ConfigureAwait(false);

            if (authenticationResult.Account == null || string.IsNullOrWhiteSpace(authenticationResult.Account.Username))
                throw new InvalidOperationException("L’authentification Microsoft Graph n’a retourné aucun compte.");

            _account = authenticationResult.Account;
            return _account;
        }

        public async Task<MicrosoftGraphCustomerUsersResult> DownloadUsersAsync(
            string customerTenantId,
            IAccount account,
            CancellationToken cancellationToken = default)
        {
            if (_application == null || _account == null)
                throw new InvalidOperationException("Le compte Microsoft Graph n’a pas été initialisé.");

            if (!Guid.TryParse(customerTenantId, out var customerTenantGuid) || customerTenantGuid == Guid.Empty)
                throw new InvalidOperationException("L’identifiant du tenant Microsoft Graph n’est pas valide.");

            try
            {
                var app = _application;
                var authenticatedAccount = account;
                var authResult = await app
                    .AcquireTokenSilent(s_scopes, authenticatedAccount)
                    .WithTenantId(customerTenantId)
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);

                var users = new JsonArray();
                var pageUrl = "https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,officeLocation,assignedLicenses,accountEnabled&$top=999";
                while (!string.IsNullOrWhiteSpace(pageUrl))
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, pageUrl);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    using var response = await s_httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        return await CreateHttpFailureResultAsync(customerTenantId, response, cancellationToken).ConfigureAwait(false);

                    await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                    using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                        return CreateErrorResult(customerTenantId, string.Empty, "Microsoft Graph a retourné une réponse inattendue.");

                    if (root.TryGetProperty("value", out var valueElement) && valueElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var userElement in valueElement.EnumerateArray())
                            users.Add(userElement.Clone());
                    }

                    pageUrl = GetNextLink(root);
                }

                var userCount = users.Count;
                var licensedUserCount = users.Count(user => user is JsonObject u && HasAssignedLicenses(u));
                var userWithOfficeCount = users.Count(user => user is JsonObject u && HasOfficeLocation(u));
                var assignedLicenseCount = users.Sum(user => user is JsonObject u ? GetAssignedLicenseCount(u) : 0);
                var officeCount = CountOffices(users);

                return new MicrosoftGraphCustomerUsersResult
                {
                    TenantId = customerTenantId,
                    Status = "Success",
                    UserCount = userCount,
                    LicensedUserCount = licensedUserCount,
                    UserWithOfficeCount = userWithOfficeCount,
                    OfficeCount = officeCount,
                    AssignedLicenseCount = assignedLicenseCount,
                    Users = users
                };
            }
            catch (MsalUiRequiredException ex)
            {
                if (IsConsentRequired(ex.ErrorCode))
                {
                    return new MicrosoftGraphCustomerUsersResult
                    {
                        TenantId = customerTenantId,
                        Status = "ConsentRequired",
                        ErrorCode = ex.ErrorCode,
                        ErrorMessage = "Le consentement Microsoft Graph est absent ou une interaction est requise pour ce tenant."
                    };
                }

                return CreateErrorResult(
                    customerTenantId,
                    ex.ErrorCode,
                    "Microsoft Graph n’a pas pu obtenir silencieusement le jeton pour ce tenant.");
            }
            catch (InvalidOperationException ex)
            {
                return CreateErrorResult(customerTenantId, string.Empty, ex.Message);
            }
        }

        private static IAccount? SelectPreferredAccount(IReadOnlyList<IAccount> accounts)
        {
            var preferredAccount = accounts.FirstOrDefault(account =>
                !string.IsNullOrWhiteSpace(account.Username) &&
                string.Equals(account.Username, PreferredPartnerAccountUsername, StringComparison.OrdinalIgnoreCase));

            if (preferredAccount != null)
                return preferredAccount;

            return accounts.FirstOrDefault(account => !string.IsNullOrWhiteSpace(account.Username));
        }

        private async Task<AuthenticationResult> AcquireAuthenticationResultAsync(
            string partnerTenantId,
            IAccount? existingAccount,
            CancellationToken cancellationToken)
        {
            AuthenticationResult? authenticationResult = null;

            if (existingAccount != null)
            {
                try
                {
                    var app = _application ?? throw new InvalidOperationException("Le compte Microsoft Graph n’a pas été initialisé.");
                    authenticationResult = await app
                        .AcquireTokenSilent(s_scopes, existingAccount)
                        .WithTenantId(partnerTenantId)
                        .ExecuteAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (MsalUiRequiredException)
                {
                    authenticationResult = await AcquireInteractiveGraphAsync(partnerTenantId, existingAccount, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                authenticationResult = await AcquireInteractiveGraphAsync(partnerTenantId, null, cancellationToken).ConfigureAwait(false);
            }

            if (authenticationResult == null)
                throw new InvalidOperationException("L’authentification Microsoft Graph n’a retourné aucun compte.");

            return authenticationResult;
        }

        private async Task<AuthenticationResult> AcquireInteractiveGraphAsync(
            string partnerTenantId,
            IAccount? account,
            CancellationToken cancellationToken)
        {
            try
            {
                var app = _application ?? throw new InvalidOperationException("Le compte Microsoft Graph n’a pas été initialisé.");
                var interactive = app
                    .AcquireTokenInteractive(s_scopes)
                    .WithTenantId(partnerTenantId)
                    .WithPrompt(Prompt.SelectAccount)
                    .WithParentActivityOrWindow(_parentWindowHandle)
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

        private static bool IsConsentRequired(string? errorCode)
        {
            return string.Equals(errorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "AADSTS65001", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "consent_required", StringComparison.OrdinalIgnoreCase)
                || string.Equals(errorCode, "interaction_required", StringComparison.OrdinalIgnoreCase);
        }

        private static MicrosoftGraphCustomerUsersResult CreateErrorResult(string customerTenantId, string errorCode, string errorMessage)
        {
            return new MicrosoftGraphCustomerUsersResult
            {
                TenantId = customerTenantId,
                Status = "Error",
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Users = new JsonArray()
            };
        }

        private static async Task<MicrosoftGraphCustomerUsersResult> CreateHttpFailureResultAsync(string customerTenantId, HttpResponseMessage response, CancellationToken cancellationToken)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return new MicrosoftGraphCustomerUsersResult
                {
                    TenantId = customerTenantId,
                    Status = "Unauthorized",
                    ErrorMessage = "Microsoft Graph refuse l’accès à ce tenant. Vérifiez le consentement de l’application et les rôles GDAP."
                };
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return new MicrosoftGraphCustomerUsersResult
                {
                    TenantId = customerTenantId,
                    Status = "Forbidden",
                    ErrorMessage = "Microsoft Graph refuse l’accès à ce tenant. Vérifiez le consentement de l’application et les rôles GDAP."
                };
            }

            if ((int)response.StatusCode == 429 || ((int)response.StatusCode >= 500 && (int)response.StatusCode <= 599))
                throw new InvalidOperationException($"Microsoft Graph rencontre une erreur temporaire : HTTP {(int)response.StatusCode}.");

            var (errorCode, errorMessage) = await ReadGraphErrorAsync(response, cancellationToken).ConfigureAwait(false);
            var message = string.IsNullOrWhiteSpace(errorMessage)
                ? $"Échec Microsoft Graph : HTTP {(int)response.StatusCode} {response.ReasonPhrase}."
                : errorMessage;

            return new MicrosoftGraphCustomerUsersResult
            {
                TenantId = customerTenantId,
                Status = "Error",
                ErrorCode = errorCode,
                ErrorMessage = message
            };
        }

        private static async Task<(string ErrorCode, string ErrorMessage)> ReadGraphErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                if (root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.Object)
                {
                    var errorCode = ReadString(errorElement, "code");
                    var errorMessage = ReadString(errorElement, "message");
                    return (errorCode, errorMessage);
                }
            }
            catch
            {
            }

            return (string.Empty, string.Empty);
        }

        private static string? GetNextLink(JsonElement root)
        {
            if (!root.TryGetProperty("@odata.nextLink", out var nextLinkElement) || nextLinkElement.ValueKind != JsonValueKind.String)
                return null;

            var nextLink = nextLinkElement.GetString();
            if (string.IsNullOrWhiteSpace(nextLink))
                return null;

            if (!Uri.TryCreate(nextLink, UriKind.Absolute, out var nextUri))
                throw new InvalidOperationException("Microsoft Graph a retourné une adresse de pagination non autorisée.");

            if (!string.Equals(nextUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !string.Equals(nextUri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Microsoft Graph a retourné une adresse de pagination non autorisée.");

            return nextLink;
        }

        private static bool HasOfficeLocation(JsonObject user)
        {
            return !string.IsNullOrWhiteSpace(ReadString(user, "officeLocation"));
        }

        private static bool HasAssignedLicenses(JsonObject user)
        {
            return user["assignedLicenses"] is JsonArray licensesElement && licensesElement.Count > 0;
        }

        private static int GetAssignedLicenseCount(JsonObject user)
        {
            if (user["assignedLicenses"] is not JsonArray licensesElement)
                return 0;

            return licensesElement.Count;
        }

        private static int CountOffices(JsonArray users)
        {
            var offices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var hasBlankOffice = false;
            foreach (var user in users)
            {
                if (user is not JsonObject userObject)
                    continue;

                var officeLocation = ReadString(userObject, "officeLocation");
                if (string.IsNullOrWhiteSpace(officeLocation))
                {
                    hasBlankOffice = true;
                    continue;
                }

                var trimmed = officeLocation.Trim();
                if (!offices.ContainsKey(trimmed))
                    offices[trimmed] = trimmed;
            }

            return offices.Count + (hasBlankOffice ? 1 : 0);
        }

        private static string ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var propertyValue) || propertyValue.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return string.Empty;

            return propertyValue.ValueKind == JsonValueKind.String ? propertyValue.GetString() ?? string.Empty : string.Empty;
        }

        private static string ReadString(JsonObject element, string propertyName)
        {
            if (!element.TryGetPropertyValue(propertyName, out var node) || node == null)
                return string.Empty;

            return node.GetValue<string>();
        }
    }
}
