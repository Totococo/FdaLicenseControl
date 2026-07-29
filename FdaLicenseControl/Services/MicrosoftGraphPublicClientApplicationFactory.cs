using System;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Desktop;

namespace FdaLicenseControl.Services
{
    internal static class MicrosoftGraphPublicClientApplicationFactory
    {
        internal static IPublicClientApplication Create(
            string clientId,
            string authority)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("Le clientId Graph ne peut pas être vide.", nameof(clientId));

            if (string.IsNullOrWhiteSpace(authority))
                throw new ArgumentException("L’autorité Graph ne peut pas être vide.", nameof(authority));

            return PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(authority)
                .WithRedirectUri("https://login.microsoftonline.com/common/oauth2/nativeclient")
                .WithWindowsEmbeddedBrowserSupport()
                .Build();
        }
    }
}
