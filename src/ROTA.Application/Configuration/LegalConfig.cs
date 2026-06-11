namespace ROTA.Application.Configuration;

/// <summary>
/// T68 — terms/privacy acceptance. Bump <see cref="CurrentTermsVersion"/> (appsettings or env
/// Legal__CurrentTermsVersion) whenever the legal text changes materially; every account whose
/// accepted version is older is then flagged for re-acceptance at login.
/// </summary>
public sealed class LegalConfig
{
    public int CurrentTermsVersion { get; set; } = 1;
}
