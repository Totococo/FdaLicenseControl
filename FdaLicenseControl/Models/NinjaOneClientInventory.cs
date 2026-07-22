using System;
using System.Collections.Generic;
using System.Linq;

namespace FdaLicenseControl.Models
{
    public sealed class NinjaOneClientInventory
    {
        public int OrganizationId { get; init; }
        public string ClientName { get; init; } = string.Empty;
        public int TotalDeviceCount { get; init; }
        public bool IsExpanded { get; set; }
        public List<NinjaOneLocationInventory> Locations { get; init; } = new List<NinjaOneLocationInventory>();

        public bool CanExpand
        {
            get
            {
                // true only when client has at least two locations containing one or more machines
                return Locations.Count(l => l.DeviceCount > 0) >= 2;
            }
        }
    }
}
