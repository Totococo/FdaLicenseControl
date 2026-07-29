using System;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace FdaLicenseControl.Services
{
    internal static class MicrosoftPublicClientApplicationFactory
    {
        internal static IPublicClientApplication Create(
            string clientId,
            string authority,
            IntPtr parentWindowHandle)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("L’ID de l’application Microsoft est absent.");

            if (string.IsNullOrWhiteSpace(authority))
                throw new InvalidOperationException("L’autorité Microsoft est absente.");

            if (parentWindowHandle == IntPtr.Zero)
                throw new InvalidOperationException("La fenêtre parente d’authentification Microsoft n’est pas disponible.");

            BrokerOptions brokerOptions = new(BrokerOptions.OperatingSystems.Windows)
            {
                Title = "FDA License Control"
            };

            return PublicClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(authority)
                .WithDefaultRedirectUri()
                .WithParentActivityOrWindow(() => parentWindowHandle)
                .WithBroker(brokerOptions)
                .Build();
        }
    }
}
