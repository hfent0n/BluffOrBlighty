using System.Security.Claims;
using BluffOrBlighty.Api.Game;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace BluffOrBlighty.Api.Accounts;

public sealed record GoogleSignIn(string Credential);

public sealed record DevSignIn(string Name);

public sealed record Rename(string DisplayName);

/// <summary>
/// Sign-in and account endpoints. Sessions are an encrypted HttpOnly cookie (SameSite=Strict).
/// Requests with a body must be JSON, which a cross-site page can't send without CORS, so
/// together with SameSite they rule out cross-site request forgery.
/// </summary>
public static class AccountEndpoints
{
    public static string? CurrentUserId(this ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true ? user.FindFirstValue(ClaimTypes.NameIdentifier) : null;

    /// <summary>Endpoints when accounts are switched off: the client just hides sign-in.</summary>
    public static void MapAccountsDisabled(this WebApplication app) =>
        app.MapGet("/api/config", () => new { accounts = false, googleClientId = (string?)null });

    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapGet("/api/config", (IOptions<AccountOptions> o) =>
            new { accounts = true, googleClientId = o.Value.GoogleClientId });

        app.MapPost("/api/auth/google", async (GoogleSignIn body, IGoogleTokenValidator google,
            AccountService accounts, HttpContext http) =>
        {
            var identity = await google.ValidateAsync(body.Credential);
            if (identity is null) return Results.Unauthorized();
            var user = await accounts.SignInAsync(identity.Subject, identity.GivenName ?? identity.Name);
            await SignInCookie(http, user.Id);
            return Results.Ok(await accounts.GetMeAsync(user.Id));
        });

        app.MapPost("/api/auth/dev", async (DevSignIn body, IOptions<AccountOptions> o, IWebHostEnvironment env,
            AccountService accounts, HttpContext http) =>
        {
            if (!env.IsDevelopment() || !o.Value.AllowDevSignIn) return Results.NotFound();
            var user = await accounts.SignInAsync($"dev:{body.Name.Trim().ToLowerInvariant()}", body.Name);
            await SignInCookie(http, user.Id);
            return Results.Ok(await accounts.GetMeAsync(user.Id));
        });

        app.MapPost("/api/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var me = app.MapGroup("/api/me").RequireAuthorization();

        me.MapGet("", async (ClaimsPrincipal user, AccountService accounts, HttpContext http) =>
            await accounts.GetMeAsync(UserGuid(user)) is { } dto
                ? Results.Ok(dto)
                : await SignedOut(http)); // account deleted elsewhere

        me.MapPatch("", async (Rename body, ClaimsPrincipal user, AccountService accounts) =>
        {
            try
            {
                return await accounts.RenameAsync(UserGuid(user), body.DisplayName)
                    ? Results.Ok(await accounts.GetMeAsync(UserGuid(user)))
                    : Results.NotFound();
            }
            catch (GameException e)
            {
                return Results.BadRequest(new { error = e.Message });
            }
        });

        me.MapPost("/progress/import", async (ProgressImport body, ClaimsPrincipal user, AccountService accounts) =>
        {
            await accounts.ImportProgressAsync(UserGuid(user), body);
            return Results.Ok(await accounts.GetMeAsync(UserGuid(user)));
        });

        me.MapDelete("", async (ClaimsPrincipal user, AccountService accounts, HttpContext http) =>
        {
            await accounts.DeleteAsync(UserGuid(user));
            return await SignedOut(http);
        });
    }

    private static Guid UserGuid(ClaimsPrincipal user) => Guid.Parse(user.CurrentUserId()!);

    private static Task SignInCookie(HttpContext http, Guid userId) =>
        http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
                CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });

    private static async Task<IResult> SignedOut(HttpContext http)
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }
}
