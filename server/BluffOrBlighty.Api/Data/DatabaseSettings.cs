using Npgsql;

namespace BluffOrBlighty.Api.Data;

/// <summary>
/// Reads the database connection setting. Accepts either form:
/// <list type="bullet">
/// <item>Npgsql: <c>Host=…;Database=…;Username=…;Password=…;SSL Mode=Require</c></item>
/// <item>A URL: <c>postgresql://user:password@host/db?sslmode=require</c>, as Neon and most hosts show by default.</item>
/// </list>
/// Npgsql itself only understands the first, so URLs are converted.
/// </summary>
public static class DatabaseSettings
{
    public static string ToNpgsql(string value)
    {
        value = value.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return new NpgsqlConnectionStringBuilder(value).ConnectionString; // validates the format

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("The database URL isn't valid. Expected postgresql://user:password@host/database.");

        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
        };
        if (uri.UserInfo.Split(':', 2) is [var user, var password])
        {
            b.Username = Uri.UnescapeDataString(user);
            b.Password = Uri.UnescapeDataString(password);
        }
        else if (uri.UserInfo.Length > 0)
        {
            b.Username = Uri.UnescapeDataString(uri.UserInfo);
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var val = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (key)
            {
                case "sslmode":
                    b.SslMode = val.ToLowerInvariant() switch
                    {
                        "disable" => SslMode.Disable,
                        "allow" => SslMode.Allow,
                        "prefer" => SslMode.Prefer,
                        "verify-ca" => SslMode.VerifyCA,
                        "verify-full" => SslMode.VerifyFull,
                        _ => SslMode.Require,
                    };
                    break;
                case "channel_binding":
                    b.ChannelBinding = val.Equals("require", StringComparison.OrdinalIgnoreCase)
                        ? ChannelBinding.Require
                        : val.Equals("disable", StringComparison.OrdinalIgnoreCase) ? ChannelBinding.Disable : ChannelBinding.Prefer;
                    break;
                // Other URL options (e.g. connect_timeout) aren't needed here; ignore them.
            }
        }

        return b.ConnectionString;
    }

    /// <summary>Where it connects, for logs and errors. Never includes the password.</summary>
    public static string Describe(string npgsql)
    {
        var b = new NpgsqlConnectionStringBuilder(npgsql);
        return $"{b.Host}/{b.Database}";
    }
}
