using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;
using Microsoft.OpenApi;
using Strangler.Monolith;
using Strangler.Common;

if (args is ["--healthcheck"])
{
    return await LabHosting.ProbeAsync();
}

var builder = WebApplication.CreateBuilder(args);
builder.AddLabHosting("monolith");
builder.Services.AddLabDatabase(builder.Configuration);
builder.Services.AddLabAuthentication(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.MaxDepth = 8;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Servers = [new OpenApiServer { Url = "/" }];
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        if (context.Description.HttpMethod is "POST" or "PUT" or "DELETE")
        {
            operation.Parameters ??= [];
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "X-CSRF-TOKEN",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Copy the token from GET /api/antiforgery after signing in. Requests use the same-origin browser session cookie.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String },
            });
        }

        return Task.CompletedTask;
    });
});
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("requests", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 32,
            QueueLimit = 0,
        }));
    options.AddFixedWindowLimiter("login", limiter =>
    {
        limiter.PermitLimit = 12;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
var app = builder.Build();
_ = app.Services.GetRequiredService<Accounts>();
app.UseLabBoundary("monolith");
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (NpgsqlDataSource database, CancellationToken cancellationToken) =>
{
    await using var command = database.CreateCommand("SELECT 1 FROM monolith.requisitions LIMIT 0; SELECT 1 FROM monolith.notes LIMIT 0; SELECT 1 FROM monolith.requisition_audit LIMIT 0");
    await command.ExecuteScalarAsync(cancellationToken);
    return Results.Ok(new { status = "ready" });
});
app.MapSession();
app.MapFeatures();
app.MapInspection();
app.MapRazorPages();
await app.RunAsync();
return 0;

public partial class Program;
