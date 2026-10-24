using System.Threading.RateLimiting;
using Strangler.Common;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;

if (args is ["--healthcheck"])
{
    return await LabHosting.ProbeAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddLabHosting("gateway");
builder.Configuration["AllowedHosts"] ??= "localhost;app.localhost;127.0.0.1;[::1]";
var target = builder.Configuration["Monolith:Address"] ?? "http://monolith:8080";
if (!Uri.TryCreate(target, UriKind.Absolute, out var upstream) ||
    upstream.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(upstream.UserInfo) ||
    upstream.AbsolutePath != "/" || !string.IsNullOrEmpty(upstream.Query))
{
    throw new InvalidOperationException("Monolith address must be an HTTP(S) origin without credentials.");
}

var maxRequests = LabHosting.ReadBoundedLimit(builder.Configuration, "Gateway:MaxConcurrentRequests", 64, 256);
var maxUpstreamConnections = LabHosting.ReadBoundedLimit(builder.Configuration, "Gateway:MaxConnectionsPerServer", 32, 128);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("requests", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = maxRequests,
            QueueLimit = 0,
        }));
});
builder.Services.AddHttpClient("readiness", client => client.Timeout = TimeSpan.FromSeconds(2));
builder.Services.AddReverseProxy().LoadFromMemory([
    new RouteConfig { RouteId = "monolith", ClusterId = "monolith", Match = new RouteMatch { Path = "{**path}" } },
], [
    new ClusterConfig
    {
        ClusterId = "monolith",
        Destinations = new Dictionary<string, DestinationConfig>(StringComparer.Ordinal)
        {
            ["primary"] = new() { Address = upstream.AbsoluteUri },
        },
        HttpClient = new HttpClientConfig { MaxConnectionsPerServer = maxUpstreamConnections },
        HttpRequest = new ForwarderRequestConfig { ActivityTimeout = TimeSpan.FromSeconds(15) },
    },
]);
var app = builder.Build();
app.UseLabBoundary("gateway");
app.UseRateLimiter();
app.MapStaticAssets();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (IHttpClientFactory clients, CancellationToken cancellationToken) =>
{
    try
    {
        using var response = await clients.CreateClient("readiness").GetAsync(new Uri(upstream, "health/ready"), cancellationToken);
        return response.IsSuccessStatusCode ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503);
    }
    catch (HttpRequestException) { return Results.StatusCode(503); }
    catch (OperationCanceledException) { return Results.StatusCode(503); }
});
app.MapReverseProxy();
await app.RunAsync();
return 0;

public partial class Program;
