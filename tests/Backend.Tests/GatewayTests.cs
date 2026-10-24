extern alias GatewayApp;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Strangler.BackendTests;

public sealed class GatewayTests
{
    [Fact]
    public async Task PublicAssetsBeatTheProxyRouteAndUnknownHostsAreRejected()
    {
        await using var factory = new WebApplicationFactory<GatewayApp::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Local");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Monolith:Address"] = "http://127.0.0.1:1",
            }));
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://app.localhost"),
            AllowAutoRedirect = false,
        });
        using var css = await client.GetAsync("/assets/site.css", TestContext.Current.CancellationToken);
        css.EnsureSuccessStatusCode();
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--ground: #f3f5f8", await css.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.NotNull(css.Headers.ETag);
        Assert.False(css.Headers.CacheControl?.NoStore);
        using var revalidation = new HttpRequestMessage(HttpMethod.Get, "/assets/site.css");
        revalidation.Headers.IfNoneMatch.Add(css.Headers.ETag!);
        using var unchanged = await client.SendAsync(revalidation, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotModified, unchanged.StatusCode);
        Assert.False(unchanged.Headers.CacheControl?.NoStore);
        using var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        Assert.True(health.Headers.CacheControl?.NoStore);
        using var missing = await client.GetAsync("/assets/not-a-registered-asset.css", TestContext.Current.CancellationToken);
        Assert.True(missing.Headers.CacheControl?.NoStore);
        using var font = await client.GetAsync("/assets/SourceSans3VF-Upright.ttf.woff2", TestContext.Current.CancellationToken);
        font.EnsureSuccessStatusCode();
        Assert.Equal("font/woff2", font.Content.Headers.ContentType?.MediaType);
        using var malicious = new HttpRequestMessage(HttpMethod.Get, "/assets/site.css");
        malicious.Headers.Host = "attacker.example";
        using var denied = await client.SendAsync(malicious, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
    }

    [Theory]
    [InlineData("Gateway:MaxConcurrentRequests", "0")]
    [InlineData("Gateway:MaxConcurrentRequests", "257")]
    [InlineData("Gateway:MaxConnectionsPerServer", "129")]
    [InlineData("Gateway:MaxConnectionsPerServer", "unbounded")]
    [InlineData("Hosting:MaxConcurrentConnections", "-1")]
    [InlineData("Hosting:MaxConcurrentConnections", "1025")]
    public async Task InvalidCapacityConfigurationStopsStartup(string key, string value)
    {
        await using var factory = GatewayFactory(new Dictionary<string, string?> { [key] = value });
        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(key, error.Message);
    }

    [Fact]
    public async Task AdmissionRejectsExcessInFlightRequestsAndReleasesCapacityAfterCompletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var forwarded = 0;
        var upstreamBuilder = WebApplication.CreateBuilder();
        upstreamBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = upstreamBuilder.Build();
        upstream.MapGet("/held", async (HttpContext context) =>
        {
            Interlocked.Increment(ref forwarded);
            entered.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.Headers.CacheControl = "public, max-age=600";
            return Results.Ok();
        });
        await upstream.StartAsync(cancellationToken);
        await using var factory = GatewayFactory(new Dictionary<string, string?>
        {
            ["Monolith:Address"] = upstream.Urls.Single(),
            ["Gateway:MaxConcurrentRequests"] = "1",
            ["Gateway:MaxConnectionsPerServer"] = "1",
            ["Hosting:MaxConcurrentConnections"] = "8",
        });
        using var client = factory.CreateClient();
        var firstRequest = client.GetAsync("/held", cancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            using var rejected = await client.GetAsync("/held", cancellationToken);
            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.True(rejected.Headers.CacheControl?.NoStore);
            Assert.Equal(1, Volatile.Read(ref forwarded));
        }
        finally
        {
            release.TrySetResult();
        }
        using var first = await firstRequest;
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True(first.Headers.CacheControl?.NoStore);
        using var admitted = await client.GetAsync("/held", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, admitted.StatusCode);
        Assert.Equal(2, Volatile.Read(ref forwarded));
        var cluster = Assert.Single(factory.Services.GetRequiredService<IProxyConfigProvider>().GetConfig().Clusters);
        Assert.Equal(1, cluster.HttpClient?.MaxConnectionsPerServer);
        Assert.Equal(8, factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits.MaxConcurrentConnections);
    }

    private static WebApplicationFactory<GatewayApp::Program> GatewayFactory(Dictionary<string, string?> settings) =>
        new WebApplicationFactory<GatewayApp::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Local");
            foreach (var (key, value) in settings) { builder.UseSetting(key, value); }
        });
}
