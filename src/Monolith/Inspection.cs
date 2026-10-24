using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;
using Strangler.Common;

namespace Strangler.Monolith;

public static class Inspection
{
    public static void MapInspection(this WebApplication app)
    {
        if (!app.Environment.IsEnvironment("Local")) { return; }
        app.MapOpenApi().RequireAuthorization();
        var reference = app.MapScalarApiReference("/reference", (options, context) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            context.Items[LabHosting.CspNonceItem] = nonce;
            options.WithTitle("Requisition API").WithNonce(nonce).DisableDefaultFonts().DisableAgent();
            options.Telemetry = false;
            options.ProxyUrl = "";
            options.PersistentAuthentication = false;
        }).RequireAuthorization();
        reference.Finally(endpoint =>
        {
            // Scalar marks packaged assets anonymous; protect the complete inspection surface.
            foreach (var anonymous in endpoint.Metadata.OfType<IAllowAnonymous>().ToArray())
            {
                endpoint.Metadata.Remove(anonymous);
            }
        });
    }
}
