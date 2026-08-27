using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ROTA.IntegrationTests;

/// <summary>
/// One RS256 keypair for the whole test assembly, published as ENVIRONMENT VARIABLES before any test
/// host is built.
///
/// WHY ENVIRONMENT VARIABLES, and not the <c>AddInMemoryCollection</c> every fixture here already
/// uses: <c>Program.cs</c> reads <c>Jwt:PublicKey</c> eagerly, while the builder is being
/// constructed, and bakes it into <c>JwtBearerOptions.IssuerSigningKey</c>. Under the minimal hosting
/// model, <c>WebApplicationFactory</c>'s <c>ConfigureAppConfiguration</c> callbacks run AFTER that
/// point, so the in-memory values never reach the validation key. Environment variables are in the
/// default source set from the very first read, and outrank user secrets and appsettings.
///
/// The symptom this fixes is deeply unhelpful: login returns 200 with a well-formed token, the
/// resolved <c>IConfiguration</c> in DI shows exactly the keys the test set, and every authenticated
/// call still returns 401 <c>"The signature key was not found"</c> -- because the token was SIGNED
/// with the test's key (read lazily, per request) and VALIDATED against a different one (read
/// eagerly, at startup).
///
/// The module initializer matters as much as the mechanism. Set from inside a fixture's
/// InitializeAsync, this would be a race: whichever test class happened to build its host first would
/// win, and only sometimes. Running at assembly load puts the keys in place before any host exists.
///
/// SECONDARY BENEFIT: the suite no longer depends on the developer's user secrets. Before this, the
/// in-memory Jwt values were silently ignored and the API actually started on whatever keypair
/// <c>tools/SetSecrets</c> had left in user secrets -- so the tests only ran at all on a machine
/// where that had been done.
/// </summary>
internal static class TestJwtKeys
{
    public static string PublicPem { get; private set; } = string.Empty;
    public static string PrivatePem { get; private set; } = string.Empty;

    public const string Issuer   = "rota-test";
    public const string Audience = "rota-test";

    [ModuleInitializer]
    internal static void Publish()
    {
        using var rsa = RSA.Create(2048);
        PublicPem  = rsa.ExportSubjectPublicKeyInfoPem();
        PrivatePem = rsa.ExportRSAPrivateKeyPem();

        // Double underscore is the configuration provider's separator for a nested key.
        Environment.SetEnvironmentVariable("Jwt__PublicKey",  PublicPem);
        Environment.SetEnvironmentVariable("Jwt__PrivateKey", PrivatePem);
        Environment.SetEnvironmentVariable("Jwt__Issuer",     Issuer);
        Environment.SetEnvironmentVariable("Jwt__Audience",   Audience);
    }
}
