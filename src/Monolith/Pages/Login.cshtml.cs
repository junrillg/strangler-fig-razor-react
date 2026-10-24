using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Strangler.Common;

namespace Strangler.Monolith.Pages;

[EnableRateLimiting("login")]
public sealed class LoginModel(Accounts accounts) : PageModel
{
    [BindProperty, Required, StringLength(32)]
    public string Username { get; set; } = "";

    [BindProperty, Required, StringLength(512), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true ? LocalRedirect("/") : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        var account = ModelState.IsValid ? accounts.Authenticate(Username, Password) : null;
        Password = "";
        ModelState.Remove(nameof(Password));
        if (account is null)
        {
            ErrorMessage = "Check your username and password.";
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Page();
        }

        await HttpContext.SignInAsync(LabAuthentication.Scheme, LabAuthentication.Principal(account));
        return LocalRedirect("/");
    }
}
