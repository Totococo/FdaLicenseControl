using System;
using System.IO;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public static class NinjaOneSettingsService
    {
        public const string BaseUrlVariableName = "NINJA_BASE_URL";
        public const string ClientIdVariableName = "NINJA_CLIENT_ID";
        public const string ClientSecretVariableName = "NINJA_CLIENT_SECRET";
        private static readonly string ConfigFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FdaLicenseControl", "Config");
        private static readonly string ConfigFilePath = Path.Combine(ConfigFolder, "ninjaone-settings.json");
        private static bool _migrationAttempted = false;

        public static string GetBaseUrl()
        {
            EnsureConfigLoaded();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var text = File.ReadAllText(ConfigFilePath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var s = JsonSerializer.Deserialize<NinjaOneSettings>(text, opts);
                    if (s != null && !string.IsNullOrWhiteSpace(s.BaseUrl))
                    {
                        return s.BaseUrl;
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier de configuration NinjaOne est invalide.", ex);
            }

            return "https://eu.ninjarmm.com";
        }

        public static string GetClientId()
        {
            EnsureConfigLoaded();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var text = File.ReadAllText(ConfigFilePath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var s = JsonSerializer.Deserialize<NinjaOneSettings>(text, opts);
                    if (s != null && !string.IsNullOrEmpty(s.ClientId))
                        return s.ClientId;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier de configuration NinjaOne est invalide.", ex);
            }
            return string.Empty;
        }

        public static string GetClientSecret()
        {
            EnsureConfigLoaded();
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    var text = File.ReadAllText(ConfigFilePath);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var s = JsonSerializer.Deserialize<NinjaOneSettings>(text, opts);
                    if (s != null && !string.IsNullOrEmpty(s.ClientSecret))
                        return s.ClientSecret;
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier de configuration NinjaOne est invalide.", ex);
            }
            return string.Empty;
        }

        public static void Save(
            string baseUrl,
            string clientId,
            string clientSecret)
        {
            baseUrl = (baseUrl ?? string.Empty).Trim();
            clientId = (clientId ?? string.Empty).Trim();
            clientSecret = (clientSecret ?? string.Empty).Trim();

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            // Ensure folder
            Directory.CreateDirectory(ConfigFolder);

            var settings = new NinjaOneSettings
            {
                BaseUrl = baseUrl,
                ClientId = clientId,
                ClientSecret = clientSecret
            };

            var opts = new JsonSerializerOptions { WriteIndented = true };
            var tempPath = ConfigFilePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(settings, opts);
                File.WriteAllText(tempPath, json);
                if (File.Exists(ConfigFilePath))
                    File.Delete(ConfigFilePath);
                File.Move(tempPath, ConfigFilePath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }

        public static bool IsConfigured()
        {
            try
            {
                var b = GetBaseUrl();
                var id = GetClientId();
                var secret = GetClientSecret();
                return !string.IsNullOrWhiteSpace(b) && !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret);
            }
            catch
            {
                return false;
            }
        }

        private static string? GetEnv(string name)
        {
            var v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (string.IsNullOrEmpty(v))
                v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            return v;
        }

        private static void EnsureConfigLoaded()
        {
            if (_migrationAttempted)
                return;

            _migrationAttempted = true;
            try
            {
                if (File.Exists(ConfigFilePath))
                    return;

                // Read env vars once
                string? baseUrl = GetEnv(BaseUrlVariableName);
                string? clientId = GetEnv(ClientIdVariableName);
                string? clientSecret = GetEnv(ClientSecretVariableName);

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    // try process then user already handled in GetEnv
                    return;
                }

                baseUrl = baseUrl!.Trim();
                clientId = clientId!.Trim();
                clientSecret = clientSecret!.Trim();
                if (baseUrl.EndsWith("/"))
                    baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

                // create config from env vars
                Directory.CreateDirectory(ConfigFolder);
                var settings = new NinjaOneSettings { BaseUrl = baseUrl, ClientId = clientId, ClientSecret = clientSecret };
                var opts = new JsonSerializerOptions { WriteIndented = true };
                var tempPath = ConfigFilePath + ".tmp";
                try
                {
                    var json = JsonSerializer.Serialize(settings, opts);
                    File.WriteAllText(tempPath, json);
                    if (File.Exists(ConfigFilePath))
                        File.Delete(ConfigFilePath);
                    File.Move(tempPath, ConfigFilePath);
                }
                catch
                {
                    try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                }
            }
            catch
            {
                // ignore migration errors
            }
        }

        public static NinjaOneSettings LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new InvalidOperationException("Le fichier de configuration spécifié est introuvable.");

            try
            {
                var text = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<NinjaOneSettings>(text, opts);
                if (s == null)
                    throw new InvalidOperationException("Le fichier de configuration est invalide.");
                if (s.SchemaVersion != 1)
                    throw new InvalidOperationException("Version de schéma de configuration non supportée.");

                s.BaseUrl = (s.BaseUrl ?? string.Empty).Trim();
                s.ClientId = (s.ClientId ?? string.Empty).Trim();
                s.ClientSecret = (s.ClientSecret ?? string.Empty).Trim();

                if (string.IsNullOrEmpty(s.BaseUrl) || string.IsNullOrEmpty(s.ClientId) || string.IsNullOrEmpty(s.ClientSecret))
                    throw new InvalidOperationException("Le fichier de configuration ne contient pas tous les paramètres requis.");

                if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("L’URL NinjaOne dans le fichier de configuration doit être une URL HTTPS valide.");

                return s;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Le fichier de configuration NinjaOne est invalide.");
            }
        }

        public static void ExportToFile(string filePath, string baseUrl, string clientId, string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                throw new InvalidOperationException("Les paramètres fournis sont incomplets.");

            baseUrl = baseUrl.Trim();
            clientId = clientId.Trim();
            clientSecret = clientSecret.Trim();
            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("L’URL NinjaOne doit être une adresse HTTPS valide.");

            var settings = new NinjaOneSettings { BaseUrl = baseUrl, ClientId = clientId, ClientSecret = clientSecret };
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var tempPath = filePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(settings, opts);
                File.WriteAllText(tempPath, json);
                if (File.Exists(filePath))
                    File.Delete(filePath);
                File.Move(tempPath, filePath);
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                throw;
            }
        }
    }
}
