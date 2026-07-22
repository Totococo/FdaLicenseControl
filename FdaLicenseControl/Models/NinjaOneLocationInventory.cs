using System;

namespace FdaLicenseControl.Models
{
    public sealed class NinjaOneLocationInventory
    {
        public int? LocationId { get; init; }
        public string LocationName { get; init; } = string.Empty;
        public int DeviceCount { get; init; }
    }
}
