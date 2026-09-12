using System.Net;
using Microsoft.AspNetCore.Builder;   // ForwardedHeadersOptions lives here, not in HttpOverrides

namespace ROTA.Api.Middleware;

/// <summary>
/// Turns the <c>ForwardedHeaders:TrustedProxies</c> entries into the two lists
/// <see cref="ForwardedHeadersOptions"/> keeps. A bare address is one proxy; an entry with a
/// <c>/</c> is a network, so the compose subnet (<c>172.18.0.0/16</c>) can be trusted as a whole and
/// the setting does not go stale when Docker hands a recreated Caddy a different address — which
/// left the list non-empty, the boot guard satisfied, and X-Forwarded-For silently ignored.
/// </summary>
public static class TrustedProxies
{
    public static void Apply(ForwardedHeadersOptions options, IEnumerable<string> entries)
    {
        foreach (var raw in entries)
        {
            var entry = raw.Trim();
            if (entry.Length == 0) continue;
            if (entry.Contains('/'))
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(entry));
            else
                options.KnownProxies.Add(IPAddress.Parse(entry));
        }
    }
}
