using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Strangler.Common;

public sealed record LabAccount(Guid Id, string Name, string Role, string PasswordHash);
public sealed record LoginInput(string? Username, string? Password);

public sealed class Accounts
{
    private readonly PasswordHasher<string> hasher = new();
    private readonly LabAccount[] accounts;

    public Accounts(IConfiguration configuration)
    {
        accounts = [
            new(new Guid("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"), "alex", "requester",
                hasher.HashPassword("alex", Secrets.Read(configuration, "Auth:AlexPasswordFile", "/run/secrets/alex_password"))),
            new(new Guid("bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"), "sam", "approver",
                hasher.HashPassword("sam", Secrets.Read(configuration, "Auth:SamPasswordFile", "/run/secrets/sam_password")))
        ];
    }

    public LabAccount? Authenticate(string? username, string? password)
    {
        if (username is null || password is null || username.Length > 32 || password.Length > 512)
        {
            return null;
        }

        var account = accounts.FirstOrDefault(a => string.Equals(a.Name, username, StringComparison.Ordinal));
        var candidate = account ?? accounts[0];
        var result = hasher.VerifyHashedPassword(candidate.Name, candidate.PasswordHash, password);
        return account is not null && result != PasswordVerificationResult.Failed ? account : null;
    }
}

public static class LabAuthentication
{
    public const string Scheme = "StranglerCookie";

    public static IServiceCollection AddLabAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var insecure = configuration.GetValue<bool>("Auth:AllowInsecureHttp");
        var keyPath = configuration["Auth:KeyRingPath"] ?? "/app/keyring";
        if (!Path.IsPathFullyQualified(keyPath))
        {
            throw new InvalidOperationException("Auth keyring path must be absolute.");
        }

        Directory.CreateDirectory(keyPath);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyPath)).SetApplicationName("StranglerLab");
        services.AddAuthentication(Scheme).AddCookie(Scheme, options =>
        {
            options.Cookie.Name = "strangler.session";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = insecure ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.LoginPath = "/Login";
            options.Events = new CookieAuthenticationEvents
            {
                OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }
                    else
                    {
                        context.Response.Redirect(context.Options.LoginPath.ToString());
                    }

                    return Task.CompletedTask;
                },
                OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                },
            };
        });
        services.AddAuthorization();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "strangler.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = insecure ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        services.AddSingleton<Accounts>();
        return services;
    }

    public static ClaimsPrincipal Principal(LabAccount account) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
        new Claim(ClaimTypes.Name, account.Name),
        new Claim(ClaimTypes.Role, account.Role),
    ], Scheme));

    public static Guid Owner(this ClaimsPrincipal principal) => Guid.Parse(
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("An authenticated owner is required."));
}
