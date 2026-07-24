using System;
using System.IO;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public static class MicrosoftPartnerSettingsService
    {
        private static string GetConfigFilePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FdaLicenseControl",
                "Config",
                "microsoft-partner-settings.json");
        }

        public static string GetPartnerTenantId()
        {
            try
            {
                var settings = Load();
                return settings.PartnerTenantId;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetClientId()
        {
            try
            {
                var settings = Load();
                return settings.ClientId;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetBaseUrl()
        {
            try
            {
                var settings = Load();
                return settings.BaseUrl;
            }
            catch
            {
                return "https://api.partnercenter.microsoft.com";
            }
        }

        public static bool IsConfigured()
        {
            try
            {
                var settings = Load();
                var tenantId = settings.PartnerTenantId?.Trim() ?? string.Empty;
                var clientId = settings.ClientId?.Trim() ?? string.Empty;

                if (string.IsNullOrEmpty(tenantId) && string.IsNullOrEmpty(clientId))
                    return false;

                if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId))
                    throw new InvalidOperationException("La configuration Microsoft 365 est incomplète.");

                if (!Guid.TryParse(tenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
                    throw new InvalidOperationException("L'identifiant du tenant partenaire Microsoft n'est pas valide.");

                if (!Guid.TryParse(clientId, out var clientGuid) || clientGuid == Guid.Empty)
                    throw new InvalidOperationException("L'identifiant de l'application Microsoft n'est pas valide.");

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Save(string partnerTenantId, string clientId, string baseUrl)
        {
            var tenantId = partnerTenantId?.Trim() ?? string.Empty;
            var appClientId = clientId?.Trim() ?? string.Empty;
            var url = baseUrl?.Trim().TrimEnd('/') ?? string.Empty;

            if (string.IsNullOrEmpty(url))
                url = "https://api.partnercenter.microsoft.com";

            if (!string.IsNullOrEmpty(tenantId) || !string.IsNullOrEmpty(appClientId))
            {
                if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(appClientId))
                    throw new InvalidOperationException("La configuration Microsoft 365 est incomplète.");

                if (!Guid.TryParse(tenantId, out var tenantGuid) || tenantGuid == Guid.Empty)
                    throw new InvalidOperationException("L'identifiant du tenant partenaire Microsoft n'est pas valide.");

                if (!Guid.TryParse(appClientId, out var clientGuid) || clientGuid == Guid.Empty)
                    throw new InvalidOperationException("L'identifiant de l'application Microsoft n'est pas valide.");
            }

            if (!string.IsNullOrEmpty(url))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                    throw new InvalidOperationException("L'URL Partner Center doit être une URL HTTPS absolue.");
            }

            var settings = new MicrosoftPartnerSettings
            {
                SchemaVersion = 1,
                PartnerTenantId = tenantId,
                ClientId = appClientId,
                BaseUrl = url
            };

            var filePath = GetConfigFilePath();
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var tmpPath = filePath + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(tmpPath, json);

            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(tmpPath, filePath);
        }

        public static MicrosoftPartnerSettings Load()
        {
            var filePath = GetConfigFilePath();
            if (!File.Exists(filePath))
            {
                return new MicrosoftPartnerSettings
                {
                    SchemaVersion = 1,
                    PartnerTenantId = string.Empty,
                    ClientId = string.Empty,
                    BaseUrl = "https://api.partnercenter.microsoft.com"
                };
            }

            return LoadFromFile(filePath);
        }

        public static MicrosoftPartnerSettings LoadFromFile(string filePath)
        {
            var json = File.ReadAllText(filePath);
            var settings = JsonSerializer.Deserialize<MicrosoftPartnerSettings>(json);
            if (settings == null)
            {
                return new MicrosoftPartnerSettings
                {
                    SchemaVersion = 1,
                    PartnerTenantId = string.Empty,
                    ClientId = string.Empty,
                    BaseUrl = "https://api.partnercenter.microsoft.com"
                };
            }

            if (string.IsNullOrEmpty(settings.BaseUrl))
                settings.BaseUrl = "https://api.partnercenter.microsoft.com";

            return settings;
        }

        public static void ExportToFile(string filePath, MicrosoftPartnerSettings settings)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(filePath, json);
        }
    }
}
