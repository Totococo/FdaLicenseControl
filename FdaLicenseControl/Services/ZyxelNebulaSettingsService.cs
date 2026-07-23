using System;
using System.IO;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public static class ZyxelNebulaSettingsService
    {
        private static readonly string ConfigFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FdaLicenseControl", "Config");
        private static readonly string ConfigFilePath = Path.Combine(ConfigFolder, "zyxel-nebula-settings.json");
        private const string DefaultBaseUrl = "https://api.nebula.zyxel.com";

        public static string GetBaseUrl()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return DefaultBaseUrl;

                var text = File.ReadAllText(ConfigFilePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<ZyxelNebulaSettings>(text, opts);
                if (s == null)
                    return DefaultBaseUrl;
                if (string.IsNullOrWhiteSpace(s.BaseUrl))
                    return DefaultBaseUrl;
                return s.BaseUrl.Trim();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier de configuration Zyxel Nebula est invalide.", ex);
            }
        }

        public static string GetApiKey()
        {
            try
            {
                if (!File.Exists(ConfigFilePath))
                    return string.Empty;

                var text = File.ReadAllText(ConfigFilePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<ZyxelNebulaSettings>(text, opts);
                if (s == null)
                    return string.Empty;
                return s.ApiKey ?? string.Empty;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Le fichier de configuration Zyxel Nebula est invalide.", ex);
            }
        }

        public static bool IsConfigured()
        {
            try
            {
                var url = GetBaseUrl();
                var key = GetApiKey();
                if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
                    return false;
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Save(string baseUrl, string apiKey)
        {
            baseUrl = (baseUrl ?? string.Empty).Trim();
            apiKey = (apiKey ?? string.Empty).Trim();

            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("La clé API Zyxel Nebula est obligatoire.");

            Directory.CreateDirectory(ConfigFolder);
            var settings = new ZyxelNebulaSettings { BaseUrl = baseUrl, ApiKey = apiKey };
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

        public static ZyxelNebulaSettings LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new InvalidOperationException("Le fichier de configuration spécifié est introuvable.");

            try
            {
                var text = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var s = JsonSerializer.Deserialize<ZyxelNebulaSettings>(text, opts);
                if (s == null)
                    throw new InvalidOperationException("Le fichier de configuration Zyxel Nebula est invalide.");
                if (s.SchemaVersion != 1)
                    throw new InvalidOperationException("Version de schéma de configuration non supportée.");

                s.BaseUrl = (s.BaseUrl ?? string.Empty).Trim();
                s.ApiKey = (s.ApiKey ?? string.Empty).Trim();

                if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.");
                if (string.IsNullOrEmpty(s.ApiKey))
                    throw new InvalidOperationException("La clé API Zyxel Nebula est obligatoire.");

                return s;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Le fichier de configuration Zyxel Nebula est invalide.");
            }
        }

        public static void ExportToFile(string filePath, string baseUrl, string apiKey)
        {
            baseUrl = (baseUrl ?? string.Empty).Trim();
            apiKey = (apiKey ?? string.Empty).Trim();
            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.Substring(0, baseUrl.Length - 1);

            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.");
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("La clé API Zyxel Nebula est obligatoire.");

            var settings = new ZyxelNebulaSettings { BaseUrl = baseUrl, ApiKey = apiKey };
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
