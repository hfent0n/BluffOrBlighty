using Google.Apis.Auth;
using Microsoft.Extensions.Options;

namespace BluffOrBlighty.Api.Accounts;

public sealed class AccountOptions
{
    /// <summary>OAuth client id from Google Cloud ("…apps.googleusercontent.com"). Public, not a secret.</summary>
    public string? GoogleClientId { get; set; }

    /// <summary>
    /// Development only: allows <c>POST /api/auth/dev</c> to sign in as any name, for local testing
    /// without Google. Ignored outside the Development environment.
    /// </summary>
    public bool AllowDevSignIn { get; set; }
}

/// <summary>Who a Google sign-in belongs to.</summary>
public sealed record GoogleIdentity(string Subject, string? GivenName, string? Name);

public interface IGoogleTokenValidator
{
    /// <summary>Checks a Google ID token (signature, issuer, expiry, audience). Null if it isn't valid.</summary>
    Task<GoogleIdentity?> ValidateAsync(string idToken);
}

public sealed class GoogleTokenValidator(IOptions<AccountOptions> options, ILogger<GoogleTokenValidator> logger)
    : IGoogleTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.GoogleClientId)) return null;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, new GoogleJsonWebSignature.ValidationSettings
            {
                // Only tokens issued to this app are accepted.
                Audience = [options.Value.GoogleClientId],
            });
            return new GoogleIdentity(payload.Subject, payload.GivenName, payload.Name);
        }
        catch (InvalidJwtException e)
        {
            logger.LogWarning("Rejected Google sign-in: {Reason}", e.Message);
            return null;
        }
    }
}
