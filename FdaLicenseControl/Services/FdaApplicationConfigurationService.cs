using System;
using System.IO;
using System.Text.Json;
using FdaLicenseControl.Models;

namespace FdaLicenseControl.Services
{
    public static class FdaApplicationConfigurationService
    {
        public static FdaApplicationConfiguration LoadFromFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new InvalidOperationException("Le fichier de configuration spécifié est introuvable.");

            try
            {
                var text = File.ReadAllText(filePath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var cfg = JsonSerializer.Deserialize<FdaApplicationConfiguration>(text, opts);
                if (cfg == null)
                    throw new InvalidOperationException("La version du fichier de configuration n'est pas prise en charge.");

                if (cfg.SchemaVersion != 1)
                    throw new InvalidOperationException("La version du fichier de configuration n'est pas prise en charge.");

                if (cfg.NinjaOne == null) cfg.NinjaOne = new NinjaOneSettings();
                if (cfg.ZyxelNebula == null) cfg.ZyxelNebula = new ZyxelNebulaSettings();

                // Trim and normalize
                cfg.NinjaOne.BaseUrl = (cfg.NinjaOne.BaseUrl ?? string.Empty).Trim();
                cfg.NinjaOne.ClientId = (cfg.NinjaOne.ClientId ?? string.Empty).Trim();
                cfg.NinjaOne.ClientSecret = (cfg.NinjaOne.ClientSecret ?? string.Empty).Trim();
                cfg.ZyxelNebula.BaseUrl = (cfg.ZyxelNebula.BaseUrl ?? string.Empty).Trim();
                cfg.ZyxelNebula.ApiKey = (cfg.ZyxelNebula.ApiKey ?? string.Empty).Trim();

                if (cfg.NinjaOne.BaseUrl.EndsWith('/'))
                    cfg.NinjaOne.BaseUrl = cfg.NinjaOne.BaseUrl.Substring(0, cfg.NinjaOne.BaseUrl.Length - 1);
                if (cfg.ZyxelNebula.BaseUrl.EndsWith('/'))
                    cfg.ZyxelNebula.BaseUrl = cfg.ZyxelNebula.BaseUrl.Substring(0, cfg.ZyxelNebula.BaseUrl.Length - 1);

                // Validate NinjaOne
                if (string.IsNullOrEmpty(cfg.NinjaOne.BaseUrl) || string.IsNullOrEmpty(cfg.NinjaOne.ClientId) || string.IsNullOrEmpty(cfg.NinjaOne.ClientSecret))
                    throw new InvalidOperationException("La configuration NinjaOne est incomplète.");
                if (!Uri.TryCreate(cfg.NinjaOne.BaseUrl, UriKind.Absolute, out var nuri) || nuri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("L’URL NinjaOne doit être une adresse HTTPS valide.");

                // Validate Zyxel
                if (string.IsNullOrEmpty(cfg.ZyxelNebula.BaseUrl) || string.IsNullOrEmpty(cfg.ZyxelNebula.ApiKey))
                    throw new InvalidOperationException("La configuration Zyxel Nebula est incomplète.");
                if (!Uri.TryCreate(cfg.ZyxelNebula.BaseUrl, UriKind.Absolute, out var zuri) || zuri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException("L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.");

                return cfg;
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Le fichier de configuration n'est pas un JSON valide.");
            }
        }

        public static void ExportToFile(string filePath, FdaApplicationConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            // Apply same normalization and validation as LoadFromFile
            configuration.NinjaOne ??= new NinjaOneSettings();
            configuration.ZyxelNebula ??= new ZyxelNebulaSettings();

            configuration.NinjaOne.BaseUrl = (configuration.NinjaOne.BaseUrl ?? string.Empty).Trim();
            configuration.NinjaOne.ClientId = (configuration.NinjaOne.ClientId ?? string.Empty).Trim();
            configuration.NinjaOne.ClientSecret = (configuration.NinjaOne.ClientSecret ?? string.Empty).Trim();
            configuration.ZyxelNebula.BaseUrl = (configuration.ZyxelNebula.BaseUrl ?? string.Empty).Trim();
            configuration.ZyxelNebula.ApiKey = (configuration.ZyxelNebula.ApiKey ?? string.Empty).Trim();

            if (configuration.NinjaOne.BaseUrl.EndsWith('/'))
                configuration.NinjaOne.BaseUrl = configuration.NinjaOne.BaseUrl.Substring(0, configuration.NinjaOne.BaseUrl.Length - 1);
            if (configuration.ZyxelNebula.BaseUrl.EndsWith('/'))
                configuration.ZyxelNebula.BaseUrl = configuration.ZyxelNebula.BaseUrl.Substring(0, configuration.ZyxelNebula.BaseUrl.Length - 1);

            if (string.IsNullOrEmpty(configuration.NinjaOne.BaseUrl) || string.IsNullOrEmpty(configuration.NinjaOne.ClientId) || string.IsNullOrEmpty(configuration.NinjaOne.ClientSecret))
                throw new InvalidOperationException("La configuration NinjaOne est incomplète.");
            if (!Uri.TryCreate(configuration.NinjaOne.BaseUrl, UriKind.Absolute, out var nuri) || nuri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("L’URL NinjaOne doit être une adresse HTTPS valide.");

            if (string.IsNullOrEmpty(configuration.ZyxelNebula.BaseUrl) || string.IsNullOrEmpty(configuration.ZyxelNebula.ApiKey))
                throw new InvalidOperationException("La configuration Zyxel Nebula est incomplète.");
            if (!Uri.TryCreate(configuration.ZyxelNebula.BaseUrl, UriKind.Absolute, out var zuri) || zuri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("L’URL de l’API Zyxel Nebula doit être une adresse HTTPS valide.");

            var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var tempPath = filePath + ".tmp";
            try
            {
                var json = JsonSerializer.Serialize(configuration, opts);
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
