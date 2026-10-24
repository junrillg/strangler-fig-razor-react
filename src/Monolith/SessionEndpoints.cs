using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Strangler.Common;

namespace Strangler.Monolith;

public static class SessionEndpoints
{
    public static void MapSession(this WebApplication app)
    {
        var api = app.MapGroup("/api").AddEndpointFilter<CsrfFilter>();
        api.MapGet("/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
            Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        api.MapGet("/session", (HttpContext context) => Results.Ok(new { name = context.User.Identity?.Name, role = context.User.FindFirstValue(ClaimTypes.Role) }))
            .RequireAuthorization();
        api.MapPost("/session", async (HttpContext context, LoginInput input, Accounts accounts) =>
        {
            var account = accounts.Authenticate(input.Username, input.Password);
            if (account is null)
            {
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            await context.SignInAsync(LabAuthentication.Scheme, LabAuthentication.Principal(account));
            return Results.Ok(new { name = account.Name, role = account.Role });
        }).RequireRateLimiting("login");
        api.MapDelete("/session", async (HttpContext context) =>
        {
            await context.SignOutAsync(LabAuthentication.Scheme);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}

public sealed class CsrfFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) &&
            !HttpMethods.IsOptions(http.Request.Method))
        {
            try
            {
                await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "invalid_antiforgery" });
            }
        }

        return await next(context);
    }
}
