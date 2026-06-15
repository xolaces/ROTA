namespace ROTA.Application.Configuration;

/// <summary>
/// Normalizes an RS256 key PEM read from configuration. A multi-line PEM can't be stored in a Docker
/// <c>.env</c> file (no multi-line values), so a deployment may supply it single-line with literal
/// <c>\n</c> escapes. This restores real newlines so <c>RSA.ImportFromPem</c> accepts it; a PEM that
/// already has real newlines passes through unchanged. See docs/BETA_DEPLOY.md.
/// </summary>
public static class PemKey
{
    public static string Normalize(string? pem)
    {
        if (string.IsNullOrWhiteSpace(pem))
            throw new InvalidOperationException("A JWT key PEM is not configured.");
        return pem.Replace("\\r\\n", "\n").Replace("\\n", "\n").Trim();
    }
}
