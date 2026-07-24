using System;
using System.Security.Cryptography;
using System.Text;

namespace FdaLicenseControl.Services
{
    public static class ZyxelNebulaOrganizationKeyFactory
    {
        public static string Create(string? organizationId, string? organizationName)
        {
            // Clean organizationId
            var cleanedId = organizationId?.Trim() ?? string.Empty;

            // If organizationId exists, return it directly
            if (!string.IsNullOrEmpty(cleanedId))
                return cleanedId;

            // Otherwise, generate a stable key from organizationName
            var cleanedName = organizationName?.Trim() ?? string.Empty;

            // Normalize the text
            var normalized = cleanedName.Normalize(NormalizationForm.FormKC);

            // Replace all sequences of whitespace with a single space
            var singleSpaced = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ");

            // Convert to upper invariant
            var upperName = singleSpaced.ToUpperInvariant();

            if (string.IsNullOrEmpty(upperName))
                throw new InvalidOperationException("Impossible de créer une clé stable pour une organisation Zyxel sans identifiant ni nom.");

            // Compute SHA-256 hash
            var nameBytes = Encoding.UTF8.GetBytes(upperName);
            var hash = SHA256.HashData(nameBytes);

            // Return generated key with prefix
            return "generated:" + Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
