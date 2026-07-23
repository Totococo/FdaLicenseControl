using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FdaLicenseControl.Services
{
    public sealed class ZyxelNebulaApiService
    {
        private static readonly HttpClient HttpClient = new();

        public async Task<int> TestConnectionAsync(
            string baseUrl,
            string apiKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new InvalidOperationException("L’URL de l’API Zyxel Nebula est manquante.");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("La clé API Zyxel Nebula est manquante.");

            var uri = baseUrl.TrimEnd('/') + "/v1/nebula/organizations";
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Add("X-ZyxelNebula-API-Key", apiKey);
            req.Headers.Accept.Clear();
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await HttpClient.SendAsync(req, cancellationToken);
            var text = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (!resp.IsSuccessStatusCode)
            {
                var safe = text;
                if (!string.IsNullOrEmpty(apiKey) && safe.Contains(apiKey, StringComparison.Ordinal))
                    safe = safe.Replace(apiKey, "***", StringComparison.Ordinal);
                var status = (int)resp.StatusCode + " " + resp.StatusCode.ToString();
                string? message = null;
                try
                {
                    using var d = JsonDocument.Parse(text);
                    var root = d.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                        message = m.GetString();
                }
                catch { }

                var msg = $"Échec de la connexion à Zyxel Nebula : HTTP {status}.";
                if (!string.IsNullOrEmpty(message))
                    msg = $"Échec de la connexion à Zyxel Nebula : HTTP {status} - {message}.";
                throw new InvalidOperationException(msg);
            }

            try
            {
                using var d = JsonDocument.Parse(text);
                var root = d.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("La réponse Zyxel Nebula contenant les organisations n’est pas un tableau JSON.");
                return root.GetArrayLength();
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("La réponse Zyxel Nebula n’est pas un JSON valide.");
            }
        }
    }
}
