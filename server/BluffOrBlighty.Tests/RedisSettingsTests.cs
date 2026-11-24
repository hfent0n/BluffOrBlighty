using BluffOrBlighty.Api.Game;
using System.Net;

namespace BluffOrBlighty.Tests;

public class RedisSettingsTests
{
    [Fact]
    public void Reads_the_stackexchange_format()
    {
        var o = RedisSettings.Parse("eu1-cool-db.upstash.io:6379,password=s3cret,ssl=True,abortConnect=False");
        var ep = Assert.IsType<DnsEndPoint>(Assert.Single(o.EndPoints));
        Assert.Equal(("eu1-cool-db.upstash.io", 6379), (ep.Host, ep.Port));
        Assert.Equal("s3cret", o.Password);
        Assert.True(o.Ssl);
    }

    [Fact]
    public void Reads_the_url_format_upstash_shows()
    {
        var o = RedisSettings.Parse("rediss://default:p%40ss@eu1-cool-db.upstash.io:6379");
        var ep = Assert.IsType<DnsEndPoint>(Assert.Single(o.EndPoints));
        Assert.Equal(("eu1-cool-db.upstash.io", 6379), (ep.Host, ep.Port));
        Assert.Equal("p@ss", o.Password);
        Assert.Null(o.User); // "default" is the default user
        Assert.True(o.Ssl);
        Assert.Equal("eu1-cool-db.upstash.io", o.SslHost);
    }

    [Theory]
    [InlineData("redis://default:pw@legible-buck-1.upstash.io:6379")] // the slip that broke a deploy
    [InlineData("legible-buck-1.upstash.io:6379,password=pw")]
    public void Upstash_always_uses_tls(string value)
    {
        var o = RedisSettings.Parse(value);
        Assert.True(o.Ssl);
        Assert.Equal("legible-buck-1.upstash.io", o.SslHost);
    }

    [Fact]
    public void Plain_redis_urls_are_not_tls_and_default_the_port()
    {
        var o = RedisSettings.Parse("redis://localhost");
        Assert.False(o.Ssl);
        Assert.Equal(6379, Assert.IsType<DnsEndPoint>(Assert.Single(o.EndPoints)).Port);
    }

    [Theory]
    [InlineData("./scripts/redis-secret.sh blufforblighty")] // the command pasted at the prompt
    [InlineData("redis-cli --tls -u redis://default:SECRETVALUE@host:6379")]
    [InlineData("rediss://:SECRETVALUE@")]
    [InlineData("my host:6379,password=SECRETVALUE")]
    public void Rejects_things_that_are_not_a_connection_string_without_echoing_them(string value)
    {
        var e = Assert.ThrowsAny<ArgumentException>(() => RedisSettings.Parse(value));
        Assert.DoesNotContain("SECRETVALUE", e.Message);
        Assert.DoesNotContain("blufforblighty", e.Message);
    }

    [Theory]
    [InlineData("rediss://default:********@legible-buck-1.upstash.io:6379")] // copied while masked
    [InlineData("legible-buck-1.upstash.io:6379,password=********,ssl=True")]
    public void Rejects_a_masked_password(string value) =>
        Assert.Contains("masked", Assert.ThrowsAny<ArgumentException>(() => RedisSettings.Parse(value)).Message);

    [Fact]
    public void Description_never_includes_the_password()
    {
        var d = RedisSettings.Describe(RedisSettings.Parse("rediss://default:topsecret@db.example.io:6380"));
        Assert.Equal("db.example.io:6380 (TLS)", d);
    }
}
