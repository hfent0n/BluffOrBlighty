using System.Net;
using StackExchange.Redis;

namespace BluffOrBlighty.Api.Game;

/// <summary>
/// Reads the Redis connection setting. Accepts either form hosts show:
/// <list type="bullet">
/// <item>StackExchange.Redis: <c>host:6379,password=…,ssl=True</c></item>
/// <item>A URL: <c>rediss://default:PASSWORD@host:6379</c> (<c>rediss</c> = TLS), as Upstash shows by default.</item>
/// </list>
/// Anything that doesn't name a plausible host is rejected, so a mistake (say, a pasted command)
/// stops the app at start-up instead of failing every player's connection.
/// </summary>
public static class RedisSettings
{
    public static ConfigurationOptions Parse(string value)
    {
        value = value.Trim();
        var options = value.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
                      value.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase)
            ? FromUrl(value)
            : ConfigurationOptions.Parse(value);

        if (options.EndPoints.Count == 0)
            throw new ArgumentException("The Redis connection setting has no host in it.");

        foreach (var endPoint in options.EndPoints)
        {
            // The bad value isn't echoed: a malformed setting might have a password in it.
            if (endPoint is DnsEndPoint dns && !LooksLikeHost(dns.Host))
                throw new ArgumentException(
                    "The Redis connection setting doesn't contain a valid host name (it has spaces, '/' or " +
                    "other characters a host can't have; was a command pasted instead?). It should look like " +
                    "'host:6379,password=…,ssl=True' or 'rediss://default:PASSWORD@host:6379'.");
        }

        return options;
    }

    /// <summary>Where it connects, for logs and errors. Never includes the password.</summary>
    public static string Describe(ConfigurationOptions options) =>
        string.Join(", ", options.EndPoints.Select(e => e switch
        {
            DnsEndPoint d => $"{d.Host}:{d.Port}",
            _ => e.ToString(),
        })) + (options.Ssl ? " (TLS)" : "");

    private static ConfigurationOptions FromUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("The Redis URL isn't valid. Expected 'rediss://default:PASSWORD@host:6379'.");

        var options = new ConfigurationOptions
        {
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase),
        };
        options.EndPoints.Add(uri.Host, uri.Port > 0 ? uri.Port : 6379);
        if (options.Ssl) options.SslHost = uri.Host;

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);
            if (parts.Length == 2)
            {
                var user = Uri.UnescapeDataString(parts[0]);
                if (user.Length > 0 && user != "default") options.User = user;
                options.Password = Uri.UnescapeDataString(parts[1]);
            }
            else
            {
                options.Password = Uri.UnescapeDataString(parts[0]);
            }
        }

        return options;
    }

    private static bool LooksLikeHost(string host) =>
        host.Length > 0 && !host.Any(char.IsWhiteSpace) && !host.Contains('/') &&
        Uri.CheckHostName(host) != UriHostNameType.Unknown;
}
