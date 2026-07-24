using System;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace FdaLicenseControl.Services
{
    internal sealed class MicrosoftPartnerTokenContext
    {
        public string AccessToken { get; init; } = string.Empty;

        public string AccountName { get; init; } = string.Empty;
    }

    public sealed class MicrosoftPartnerConnectionTestResult
    {
        public string AccountName { get; init; } = string.Empty;

        public int CustomerCount { get; init; }

        public bool? IsMfaCompliant { get; init; }
    }

    public sealed class MicrosoftPartnerAuthenticationService
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private const string CustomerScope = "https://api.partnercenter.microsoft.com/user_impersonation";

        internal async Task<MicrosoftPartnerTokenContext> AcquireTokenContextAsync(
            string partnerTenantId,
            string clientId,
            CancellationToken cancellationToken = default)
        {
            var authResult = await AcquireTokenAsync(partnerTenantId, clientId, cancellationToken);
            return new MicrosoftPartnerTokenContext
            {
                AccessToken = authResult.AccessToken,
                AccountName = authResult.Account?.Username ?? string.Empty
            };
        }

        public async Task<MicrosoftPartnerConnectionTestResult> TestConnectionAsync(
            string partnerTenantId,
            string clientId,
            string baseUrl,
            CancellationToken cancellationToken = default)
        {
            var tenantId = partnerTenantId?.Trim() ?? string.Empty;
            var appClientId = clientId?.Trim() ?? string.Empty;
            var url = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(appClientId))
                throw new InvalidOperationException("La configuration Microsoft 365 est incomplète.");

            if (!Guid.TryParse(tenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
                throw new InvalidOperationException("L'identifiant du tenant partenaire Microsoft n'est pas valide.");

            if (!Guid.TryParse(appClientId, out var clientGuid) || clientGuid == Guid.Empty)
                throw new InvalidOperationException("L'identifiant de l'application Microsoft n'est pas valide.");

            if (string.IsNullOrEmpty(url))
                url = "https://api.partnercenter.microsoft.com";

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                throw new InvalidOperationException("L'URL Partner Center doit être une URL HTTPS absolue.");

            var tokenContext = await AcquireTokenContextAsync(tenantId, appClientId, cancellationToken);
            var accessToken = tokenContext.AccessToken;
            var accountName = tokenContext.AccountName;
            var correlationId = Guid.NewGuid();

            var customersUri = new Uri($"{url}/v1/customers?size=200", UriKind.Absolute);
            var customerCount = 0;
            var firstPageItemCount = 0;
            int? totalCount = null;
            bool? isMfaCompliant = null;
            Uri? nextUri = customersUri;

            while (nextUri != null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, nextUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
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
                    throw new InvalidOperationException($"Échec de la connexion Microsoft Partner Center : {ex.Message}", ex);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = $"Échec de la connexion Microsoft Partner Center : HTTP {(int)response.StatusCode} {response.ReasonPhrase}.";
                    try
                    {
                        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                        if (!string.IsNullOrEmpty(errorContent))
                        {
                            using var doc = JsonDocument.Parse(errorContent);
                            if (doc.RootElement.TryGetProperty("error_description", out var errorDesc))
                                errorMessage += $" {errorDesc.GetString()}";
                            else if (doc.RootElement.TryGetProperty("message", out var msg))
                                errorMessage += $" {msg.GetString()}";
                        }
                    }
                    catch
                    {
                        // Ignore JSON parsing errors
                    }

                    throw new InvalidOperationException(errorMessage);
                }

                if (response.Headers.TryGetValues("isMfaCompliant", out var mfaValues))
                {
                    var mfaValue = mfaValues.FirstOrDefault();
                    if (string.Equals(mfaValue, "true", StringComparison.OrdinalIgnoreCase))
                        isMfaCompliant = true;
                    else if (string.Equals(mfaValue, "false", StringComparison.OrdinalIgnoreCase))
                        isMfaCompliant = false;
                }

                if (isMfaCompliant == false)
                    throw new InvalidOperationException("La connexion Partner Center a réussi, mais l'appel n'est pas reconnu comme conforme au MFA.");

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var pageItemCount = 0;
                Uri? discoveredNextUri = null;

                try
                {
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;

                    if (root.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException("Échec de la connexion Microsoft Partner Center : la réponse Partner Center n'est pas un objet JSON.");

                    if (root.TryGetProperty("totalCount", out var totalCountElement) &&
                        totalCountElement.ValueKind == JsonValueKind.Number &&
                        totalCountElement.TryGetInt32(out var parsedTotalCount) &&
                        parsedTotalCount >= 0)
                    {
                        totalCount = parsedTotalCount;
                    }

                    if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        pageItemCount = itemsElement.GetArrayLength();
                    }

                    if (totalCount == null)
                    {
                        customerCount += pageItemCount;

                        if (root.TryGetProperty("links", out var linksElement) &&
                            linksElement.ValueKind == JsonValueKind.Object &&
                            linksElement.TryGetProperty("next", out var nextElement) &&
                            nextElement.ValueKind == JsonValueKind.Object &&
                            nextElement.TryGetProperty("uri", out var uriElement) &&
                            uriElement.ValueKind == JsonValueKind.String)
                        {
                            var nextUriValue = uriElement.GetString();
                            if (!string.IsNullOrWhiteSpace(nextUriValue))
                                discoveredNextUri = ResolveNextUri(nextUriValue, customersUri);
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                    pageItemCount = 0;
                }

                if (totalCount != null)
                {
                    customerCount = totalCount.Value;
                    firstPageItemCount = pageItemCount;
#if DEBUG
                    Debug.WriteLine($"Partner Center customers: totalCount={totalCount}, firstPageItems={firstPageItemCount}, reportedCount={customerCount}");
#endif
                    break;
                }

                if (nextUri == customersUri)
                {
                    firstPageItemCount = pageItemCount;
                }

                nextUri = discoveredNextUri;
            }

            if (totalCount == null)
            {
#if DEBUG
                Debug.WriteLine($"Partner Center customers: totalCount={totalCount}, firstPageItems={firstPageItemCount}, reportedCount={customerCount}");
#endif
            }

            return new MicrosoftPartnerConnectionTestResult
            {
                AccountName = accountName,
                CustomerCount = customerCount,
                IsMfaCompliant = isMfaCompliant
            };
        }

        private static Uri ResolveNextUri(string nextUriValue, Uri baseUri)
        {
            if (Uri.TryCreate(nextUriValue, UriKind.Relative, out var relativeUri))
                return new Uri(baseUri, relativeUri);

            if (!Uri.TryCreate(nextUriValue, UriKind.Absolute, out var absoluteUri))
                throw new InvalidOperationException("Échec de la connexion Microsoft Partner Center : l'URI de continuation est invalide.");

            if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(absoluteUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Échec de la connexion Microsoft Partner Center : l'URI de continuation est invalide.");
            }

            return absoluteUri;
        }

        private static async Task<AuthenticationResult> AcquireTokenAsync(
            string partnerTenantId,
            string clientId,
            CancellationToken cancellationToken)
        {
            var tenantId = partnerTenantId?.Trim() ?? string.Empty;
            var appClientId = clientId?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(appClientId))
                throw new InvalidOperationException("La configuration Microsoft 365 est incomplète.");

            if (!Guid.TryParse(tenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
                throw new InvalidOperationException("L'identifiant du tenant partenaire Microsoft n'est pas valide.");

            if (!Guid.TryParse(appClientId, out var clientGuid) || clientGuid == Guid.Empty)
                throw new InvalidOperationException("L'identifiant de l'application Microsoft n'est pas valide.");

            var app = PublicClientApplicationBuilder
                .Create(appClientId)
                .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
                .WithRedirectUri("http://localhost")
                .Build();

            var scopes = new[] { CustomerScope };

            try
            {
                var accounts = await app.GetAccountsAsync();
                var firstAccount = accounts.FirstOrDefault();

                if (firstAccount != null)
                {
                    try
                    {
                        return await app.AcquireTokenSilent(scopes, firstAccount)
                            .ExecuteAsync(cancellationToken);
                    }
                    catch (MsalUiRequiredException)
                    {
                        return await app.AcquireTokenInteractive(scopes)
                            .WithSystemWebViewOptions(new SystemWebViewOptions())
                            .ExecuteAsync(cancellationToken);
                    }
                }

                return await app.AcquireTokenInteractive(scopes)
                    .WithSystemWebViewOptions(new SystemWebViewOptions())
                    .ExecuteAsync(cancellationToken);
            }
            catch (MsalException ex)
            {
                throw new InvalidOperationException($"Échec de l'authentification Microsoft : {ex.Message}", ex);
            }
        }
    }
}
