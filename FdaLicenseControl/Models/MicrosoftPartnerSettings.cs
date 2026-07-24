namespace FdaLicenseControl.Models
{
    public sealed class MicrosoftPartnerSettings
    {
        public int SchemaVersion { get; set; } = 1;

        public string PartnerTenantId { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://api.partnercenter.microsoft.com";
    }
}
