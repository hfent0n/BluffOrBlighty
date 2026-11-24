using BluffOrBlighty.Api.Data;
using Npgsql;

namespace BluffOrBlighty.Tests;

public class DatabaseSettingsTests
{
    [Fact]
    public void Converts_the_url_neon_shows()
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseSettings.ToNpgsql(
            "postgresql://neondb_owner:p%40ss@ep-curly-water-1.c-3.eu-west-2.aws.neon.tech/neondb?sslmode=require&channel_binding=require"));
        Assert.Equal("ep-curly-water-1.c-3.eu-west-2.aws.neon.tech", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("neondb", b.Database);
        Assert.Equal("neondb_owner", b.Username);
        Assert.Equal("p@ss", b.Password);
        Assert.Equal(SslMode.Require, b.SslMode);
        Assert.Equal(ChannelBinding.Require, b.ChannelBinding);
    }

    [Fact]
    public void Keeps_the_npgsql_form()
    {
        var b = new NpgsqlConnectionStringBuilder(DatabaseSettings.ToNpgsql(
            "Host=localhost;Database=bluff;Username=bluff;Password=pw"));
        Assert.Equal(("localhost", "bluff", "bluff"), (b.Host, b.Database, b.Username));
    }

    [Fact]
    public void Description_never_includes_the_password() =>
        Assert.Equal("db.example.com/app",
            DatabaseSettings.Describe(DatabaseSettings.ToNpgsql("postgres://u:topsecret@db.example.com:6543/app")));

    [Fact]
    public void Rejects_nonsense() =>
        Assert.ThrowsAny<ArgumentException>(() => DatabaseSettings.ToNpgsql("./scripts/db-secret.sh blufforblighty"));
}
