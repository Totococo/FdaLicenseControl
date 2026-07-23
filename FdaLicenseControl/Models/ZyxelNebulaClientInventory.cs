using System;
using System.Collections.Generic;

namespace FdaLicenseControl.Models
{
    public sealed class ZyxelNebulaClientInventory
    {
        public int OrganizationId { get; init; }
        public string OrganizationName { get; init; } = string.Empty;
        public bool CanExpand { get; init; }
        public bool IsExpanded { get; set; }
        public IReadOnlyList<ZyxelNebulaSiteInventory> Sites { get; init; } = Array.Empty<ZyxelNebulaSiteInventory>();
        public int TotalDeviceCount { get; init; }

        public int TotalLicenseCount => TotalDeviceCount;
    }
}
