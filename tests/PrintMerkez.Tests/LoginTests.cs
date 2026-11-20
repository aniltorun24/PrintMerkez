using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using PrintMerkez.Core;

namespace PrintMerkez.Tests;

public sealed class LoginTests
{
    private static async Task<Dictionary<string, string>> LoginForm(HttpClient client, string password = "12345", string username = "admin")
    {
        var html = await client.GetStringAsync("/Login");
        return new()
        {
            ["Username"] = username, ["Password"] = password,
            ["__RequestVerificationToken"] = Token(html)
        };
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html,
        "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    [Theory]
    [InlineData("/")][InlineData("/Printers")][InlineData("/Users")][InlineData("/Audit")]
    [InlineData("/Notifications")][InlineData("/Managers")][InlineData("/Tariffs")]
    [InlineData("/Departments")][InlineData("/Locations")]
    [InlineData("/Jobs?handler=Export")]
    public async Task AnonymousCannotReadManagementPagesOrExport(string path)
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Login?ReturnUrl=", response.Headers.Location!.ToString());
        Assert.DoesNotContain("<table", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/live/events")][InlineData("/Jobs?handler=Live")][InlineData("/?handler=Live")]
    public async Task AnonymousLiveRequestsReturnUnauthorized(string path)
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("1234", "admin")][InlineData("12345", "other")]
    public async Task InvalidCredentialsDoNotCreateSession(string password, string username)
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(await LoginForm(client, password, username)));
        Assert.Contains("Kullanıcı adı veya şifre hatalı.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Jobs")).StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("PrintMerkez.Admin=")));
    }

    [Fact]
    public async Task LoginUsesProtectedCookieReturnsToLocalPageAndLogoutProtectsPagesAgain()
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var form = await LoginForm(client);
        form["ReturnUrl"] = "/Jobs?Source=WindowsPrintService";
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(form["ReturnUrl"], response.Headers.Location!.ToString());
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("PrintMerkez.Admin="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.DoesNotContain("expires=", cookie.ToLowerInvariant());
        var page = await client.GetAsync("/Jobs");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Jobs?handler=Live")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        var logout = await client.PostAsync("/Logout", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["__RequestVerificationToken"] = Token(await page.Content.ReadAsStringAsync()) }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/Login", logout.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/Jobs?handler=Live")).StatusCode);
    }

    [Theory]
    [InlineData("https://example.com")][InlineData("//example.com")]
    public async Task LoginDoesNotRedirectToExternalSites(string destination)
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var form = await LoginForm(client); form["ReturnUrl"] = destination;
        var response = await client.PostAsync("/Login", new FormUrlEncodedContent(form));
        Assert.Equal("/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task LoginRequiresAntiforgeryAndThrottlesRepeatedAttempts()
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Login", new FormUrlEncodedContent(
            new Dictionary<string,string> { ["Username"] = "admin", ["Password"] = "12345" }))).StatusCode);
        var form = await LoginForm(client, "wrong");
        for (var i = 0; i < 9; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/login/", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/Login", new FormUrlEncodedContent(form))).StatusCode);
    }

    [Fact]
    public async Task CollectorApiStillUsesAgentKeyWithoutBrowserLogin()
    {
        using var app = new AppFactory(false);
        using var client = app.CreateClient(new() { AllowAutoRedirect = false });
        var job = new PrintEventRequest { EventId = "login-collector-test", Server = "TEST", PrinterId = 1,
            Account = "TEST", OccurredAt = DateTimeOffset.UtcNow, Source = EventSource.WindowsPrintService };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/print-events", job)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Agent-Key", AppFactory.Key);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/print-events", job)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }
}
