namespace FdaLicenseControl.Models
{
    public sealed class Microsoft365CustomerLicenseInventory
    {
        public string CustomerId { get; init; } = string.Empty;

        public string TenantId { get; init; } = string.Empty;

        public string CompanyName { get; init; } = string.Empty;

        public string Domain { get; init; } = string.Empty;

        public int BusinessBasicUsed { get; init; }

        public int BusinessBasicActive { get; init; }

        public int BusinessBasicAvailable { get; init; }

        public int BusinessStandardUsed { get; init; }

        public int BusinessStandardActive { get; init; }

        public int BusinessStandardAvailable { get; init; }

        public int ExchangeOnlinePlan1Used { get; init; }

        public int ExchangeOnlinePlan1Active { get; init; }

        public int ExchangeOnlinePlan1Available { get; init; }

        public int TeamsEssentialsUsed { get; init; }

        public int TeamsEssentialsActive { get; init; }

        public int TeamsEssentialsAvailable { get; init; }

        public int TotalUsed => BusinessBasicUsed + BusinessStandardUsed + ExchangeOnlinePlan1Used + TeamsEssentialsUsed;
    }
}
