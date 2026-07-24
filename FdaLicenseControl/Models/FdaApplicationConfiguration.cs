using System;

namespace FdaLicenseControl.Models
{
    public sealed class FdaApplicationConfiguration
    {
        public int SchemaVersion { get; set; } = 1;
        public NinjaOneSettings NinjaOne { get; set; } = new NinjaOneSettings();
        public ZyxelNebulaSettings ZyxelNebula { get; set; } = new ZyxelNebulaSettings();
        public MicrosoftPartnerSettings MicrosoftPartner { get; set; } = new MicrosoftPartnerSettings();
    }
}
