using System.Security.Cryptography;
using System.Diagnostics;

using var rsa = RSA.Create(2048);

var privateKey = rsa.ExportRSAPrivateKeyPem();
var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

Console.WriteLine("Generated new RS256 key pair. Setting secrets...");

RunSecret("Jwt:PrivateKey", privateKey);
RunSecret("Jwt:PublicKey", publicKey);

Console.WriteLine("Done. Both keys stored in Secret Manager.");
Console.WriteLine();
Console.WriteLine("PUBLIC KEY (safe to share):");
Console.WriteLine(publicKey);

static void RunSecret(string key, string value)
{
    var repoRoot = FindRepoRoot();

    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"user-secrets set \"{key}\" \"{value.Replace("\"", "\\\"")}\" --project src/ROTA.Api",
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };

    var process = Process.Start(psi)!;
    process.WaitForExit();

    if (process.ExitCode != 0)
        Console.WriteLine($"ERROR setting {key}: {process.StandardError.ReadToEnd()}");
    else
        Console.WriteLine($"Set {key}: OK");
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Could not find repo root containing ROTA.slnx. Run this tool from inside the ROTA project folder.");
}