using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace Strangler.Common;

public static class LabHosting
{
    public const string CspNonceItem = "lab.csp_nonce";
    public static async Task<int> ProbeAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await client.GetAsync(new Uri("http://127.0.0.1:8080/health/ready"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException) { return 1; }
        catch (OperationCanceledException) { return 1; }
    }

    public static void AddLabHosting(this WebApplicationBuilder builder, string serviceName)
    {
        var maxConnections = ReadBoundedLimit(builder.Configuration, "Hosting:MaxConcurrentConnections", 128, 1024);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false;
            options.Limits.MaxConcurrentConnections = maxConnections;
            options.Limits.MaxRequestBodySize = 16_384;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(10);
            options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(30);
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter((category, level) => category == "Lab.Requests" && level >= LogLevel.Information);
        builder.Logging.AddJsonConsole();
        var endpoint = builder.Configuration["Telemetry:Endpoint"];
        if (endpoint is not null)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address) ||
                address.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(address.UserInfo))
            {
                throw new InvalidOperationException("Telemetry endpoint must be an HTTP(S) URL without credentials.");
            }

            var username = builder.Configuration["Telemetry:Username"] ?? "logs@lab.example";
            if (username.Contains(':', StringComparison.Ordinal) || username.Any(char.IsControl))
            {
                throw new InvalidOperationException("Telemetry username is invalid.");
            }

            var password = Secrets.Read(builder.Configuration, "Telemetry:PasswordFile", "/run/secrets/openobserve_password");
            var authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
            builder.Logging.AddOpenTelemetry(options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = false;
                options.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService(serviceName));
                options.AddOtlpExporter((exporter, processor) =>
                {
                    exporter.Endpoint = address;
                    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                    exporter.Headers = $"Authorization={authorization}";
                    exporter.TimeoutMilliseconds = 2000;
                    processor.ExportProcessorType = OpenTelemetry.ExportProcessorType.Batch;
                    processor.BatchExportProcessorOptions.MaxQueueSize = 1024;
                    processor.BatchExportProcessorOptions.MaxExportBatchSize = 128;
                    processor.BatchExportProcessorOptions.ScheduledDelayMilliseconds = 1000;
                    processor.BatchExportProcessorOptions.ExporterTimeoutMilliseconds = 2000;
                });
            });
        }
    }

    public static int ReadBoundedLimit(IConfiguration configuration, string key, int defaultValue, int maximum)
    {
        var configured = configuration[key];
        if (configured is null) { return defaultValue; }
        if (!int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 1 || value > maximum)
        {
            throw new InvalidOperationException($"{key} must be an integer between 1 and {maximum}.");
        }
        return value;
    }

    public static void UseLabBoundary(this WebApplication app, string serviceName)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Lab.Requests");
        app.Use(async (context, next) =>
        {
            var correlation = Guid.TryParse(context.Request.Headers["X-Correlation-ID"], out var supplied)
                ? supplied : Guid.NewGuid();
            context.TraceIdentifier = correlation.ToString("D");
            context.Request.Headers["X-Correlation-ID"] = context.TraceIdentifier;
            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Correlation-ID"] = context.TraceIdentifier;
                context.Response.Headers.XContentTypeOptions = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                if (!context.Response.Headers.ContainsKey("Content-Security-Policy"))
                {
                    var inspection = context.Items[CspNonceItem] is string nonce
                        ? $"; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'unsafe-inline'; img-src 'self' data:"
                        : "";
                    context.Response.Headers.ContentSecurityPolicy = $"default-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'{inspection}";
                }
                var publicAsset = serviceName == "gateway" && context.Request.Path.StartsWithSegments("/assets") &&
                    context.GetEndpoint()?.Metadata.GetMetadata<StaticAssetDescriptor>() is not null &&
                    context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified;
                if (!publicAsset) { context.Response.Headers.CacheControl = "no-store"; }
                return Task.CompletedTask;
            });
            var watch = Stopwatch.StartNew();
            var category = "none";
            try
            {
                if (context.Request.ContentLength > 16_384)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                }
                else
                {
                    await next(context);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                category = "cancelled";
                context.Response.StatusCode = 499;
            }
            catch (NpgsqlException)
            {
                category = "storage_unavailable";
                if (context.Response.HasStarted)
                {
                    context.Abort();
                }
                else
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new { error = category }, context.RequestAborted);
                }
            }
            catch (BadHttpRequestException)
            {
                category = "invalid_request";
                if (context.Response.HasStarted)
                {
                    context.Abort();
                }
                else
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            }
            catch (Exception)
            {
                // Exceptions may carry submitted text or credentials; log only the safe category.
                category = "internal_error";
                if (context.Response.HasStarted)
                {
                    context.Abort();
                }
                else
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsJsonAsync(new { error = category }, context.RequestAborted);
                }
            }
            finally
            {
                if (category == "none" && context.Response.StatusCode >= 400) { category = "request_rejected"; }
                var route = context.Request.Path.StartsWithSegments("/api/requisitions") ? "requisitions"
                    : context.Request.Path.StartsWithSegments("/api/notes") ? "notes"
                    : context.Request.Path.StartsWithSegments("/health") ? "health" : "application";
                RequestLog.Completed(logger, serviceName, context.TraceIdentifier, route,
                    context.Response.StatusCode, category, watch.ElapsedMilliseconds);
            }
        });
    }
}

internal static partial class RequestLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "request_completed service={Service} correlation_id={CorrelationId} route={Route} status={Status} error_category={ErrorCategory} elapsed_ms={ElapsedMs}")]
    public static partial void Completed(ILogger logger, string service, string correlationId, string route, int status, string errorCategory, long elapsedMs);
}
