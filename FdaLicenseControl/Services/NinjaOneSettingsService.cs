using System;

namespace FdaLicenseControl.Services
{
    public static class NinjaOneSettingsService
    {
        public const string BaseUrlVariableName = "NINJA_BASE_URL";
        public const string ClientIdVariableName = "NINJA_CLIENT_ID";
        public const string ClientSecretVariableName = "NINJA_CLIENT_SECRET";

        public static string GetBaseUrl()
        {
            var v = GetEnv(BaseUrlVariableName);
            if (string.IsNullOrWhiteSpace(v))
                return "https://eu.ninjarmm.com";
            return v!;
        }

        public static string GetClientId()
        {
            var v = GetEnv(ClientIdVariableName);
            return string.IsNullOrEmpty(v) ? string.Empty : v!;
        }

        public static string GetClientSecret()
        {
            var v = GetEnv(ClientSecretVariableName);
            return string.IsNullOrEmpty(v) ? string.Empty : v!;
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

            Environment.SetEnvironmentVariable(BaseUrlVariableName, baseUrl, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(ClientIdVariableName, clientId, EnvironmentVariableTarget.User);
            Environment.SetEnvironmentVariable(ClientSecretVariableName, clientSecret, EnvironmentVariableTarget.User);

            Environment.SetEnvironmentVariable(BaseUrlVariableName, baseUrl, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(ClientIdVariableName, clientId, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(ClientSecretVariableName, clientSecret, EnvironmentVariableTarget.Process);
        }

        public static bool IsConfigured()
        {
            var baseUrl = GetEnv(BaseUrlVariableName);
            var clientId = GetEnv(ClientIdVariableName);
            var clientSecret = GetEnv(ClientSecretVariableName);

            return !string.IsNullOrWhiteSpace(baseUrl)
                && !string.IsNullOrWhiteSpace(clientId)
                && !string.IsNullOrWhiteSpace(clientSecret);
        }

        private static string? GetEnv(string name)
        {
            var v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process);
            if (string.IsNullOrEmpty(v))
                v = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            return v;
        }
    }
}
