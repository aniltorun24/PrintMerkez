using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PrintMerkez.Web.Services;

namespace PrintMerkez.Web.Pages;

[AllowAnonymous]
public class LoginModel(LocalAdminCredentials credentials) : PageModel
{
    [BindProperty, Required(ErrorMessage = "Kullanıcı adını girin."), StringLength(100)]
    public string Username { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Şifreyi girin."), StringLength(256), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => User.Identity?.IsAuthenticated == true
        ? LocalRedirect(SafeReturnUrl()) : Page();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        if (!credentials.Verify(Username, Password))
        {
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            Password = "";
            return Page();
        }
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, credentials.Username),
            new Claim(ClaimTypes.Role, "Admin")
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                AllowRefresh = false
            });
        return LocalRedirect(SafeReturnUrl());
    }

    private string SafeReturnUrl() => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : Url.Content("~/");
}
