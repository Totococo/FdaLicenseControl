using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FdaLicenseControl.Services
{
    public sealed class NinjaOneAuthenticationService
    {
        private static readonly HttpClient HttpClient = new();

        public async Task TestConnectionAsync(
            string baseUrl,
            string clientId,
            string clientSecret,
            CancellationToken cancellationToken = default)
        {
            baseUrl = (baseUrl ?? string.Empty).Trim();
            clientId = (clientId ?? string.Empty).Trim();
            clientSecret = (clientSecret ?? string.Empty).Trim();

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
                throw new InvalidOperationException("Les paramètres d'authentification doivent être renseignés.");

            var requestUri = baseUrl + "/ws/oauth/token";

            var content = new FormUrlEncodedContent(new[] {
                new KeyValuePair<string,string>("grant_type", "client_credentials"),
                new KeyValuePair<string,string>("client_id", clientId),
                new KeyValuePair<string,string>("client_secret", clientSecret),
                new KeyValuePair<string,string>("scope", "monitoring")
            });

            using var response = await HttpClient.PostAsync(requestUri, content, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Try to extract error fields from JSON
                string? error = null;
                string? errorDescription = null;
                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var e))
                        error = e.GetString();
                    if (root.TryGetProperty("error_description", out var d))
                        errorDescription = d.GetString();
                }
                catch { /* ignore parse errors */ }

                var status = (int)response.StatusCode + " " + response.StatusCode.ToString();

                // Sanitize any accidental exposure of the client secret
                if (!string.IsNullOrEmpty(clientSecret) && responseText.Contains(clientSecret, StringComparison.Ordinal))
                    responseText = responseText.Replace(clientSecret, "***", StringComparison.Ordinal);

                if (!string.IsNullOrEmpty(error))
                {
                    var msg = $"Échec de l’authentification NinjaOne : HTTP {status} - {error}.";
                    if (!string.IsNullOrEmpty(errorDescription))
                        msg = $"Échec de l’authentification NinjaOne : HTTP {status} - {error} - {errorDescription}.";
                    throw new InvalidOperationException(msg);
                }

                throw new InvalidOperationException($"Échec de l’authentification NinjaOne : HTTP {status}.");
            }

            // Success - parse JSON and check access_token
            try
            {
                using var doc = JsonDocument.Parse(responseText);
                var root = doc.RootElement;
                if (!root.TryGetProperty("access_token", out var tokenElement))
                    throw new InvalidOperationException("L’authentification NinjaOne a réussi, mais aucun jeton d’accès n’a été retourné.");

                var token = tokenElement.GetString();
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException("L’authentification NinjaOne a réussi, mais aucun jeton d’accès n’a été retourné.");
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Réponse invalide reçue de NinjaOne.");
            }
        }
    }
}
