using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using ROTA.Api.Middleware;

namespace ROTA.UnitTests.Infrastructure;

// RF1 — the trusted-proxy setting can name the compose network, so a recreated Caddy on a new
// address keeps X-Forwarded-For honoured and the rate-limit buckets and audit IPs stay per player.
public class TrustedProxiesTests
{
    private static ForwardedHeadersOptions Fresh()
    {
        var o = new ForwardedHeadersOptions();
        o.KnownProxies.Clear();
        o.KnownIPNetworks.Clear();
        return o;
    }

    [Fact]
    public void ABareAddress_IsOneProxy()
    {
        var o = Fresh();
        TrustedProxies.Apply(o, new[] { "172.18.0.5" });

        o.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Parse("172.18.0.5"));
        o.KnownIPNetworks.Should().BeEmpty();
    }

    [Fact]
    public void ACidr_IsANetwork()
    {
        var o = Fresh();
        TrustedProxies.Apply(o, new[] { "172.18.0.0/16" });

        o.KnownProxies.Should().BeEmpty();
        var net = o.KnownIPNetworks.Should().ContainSingle().Which;
        net.Contains(IPAddress.Parse("172.18.0.5")).Should().BeTrue("the address Caddy had yesterday");
        net.Contains(IPAddress.Parse("172.18.0.9")).Should().BeTrue("the address it may have tomorrow");
        net.Contains(IPAddress.Parse("10.0.0.1")).Should().BeFalse("anything off the compose network is a stranger");
    }

    [Fact]
    public void BothForms_MayBeMixed_AndBlanksAreIgnored()
    {
        var o = Fresh();
        TrustedProxies.Apply(o, new[] { " 172.18.0.0/16 ", "", "127.0.0.1" });

        o.KnownIPNetworks.Should().HaveCount(1);
        o.KnownProxies.Should().ContainSingle().Which.Should().Be(IPAddress.Loopback);
    }

    [Fact]
    public void AMalformedEntry_Throws_SoTheBootGuardSeesIt()
    {
        var o = Fresh();
        var act = () => TrustedProxies.Apply(o, new[] { "caddy" });
        act.Should().Throw<FormatException>("a typo must stop the boot, not quietly trust nobody");
    }
}
