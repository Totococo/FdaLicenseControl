using System.Text.Json.Nodes;

namespace FdaLicenseControl.Models
{
    public sealed class MicrosoftGraphCustomerUsersResult
    {
        public string TenantId { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public string ErrorCode { get; init; } = string.Empty;

        public string ErrorMessage { get; init; } = string.Empty;

        public int UserCount { get; init; }

        public int LicensedUserCount { get; init; }

        public int UserWithOfficeCount { get; init; }

        public int OfficeCount { get; init; }

        public int AssignedLicenseCount { get; init; }

        public JsonArray Users { get; init; } = new();
    }
}
