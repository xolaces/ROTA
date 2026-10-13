using FluentAssertions;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Configuration;
using ROTA.Application.Validators;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Validators;

// bcrypt reads only the first 72 bytes of a password, so register and reset refuse anything longer.
public class PasswordLengthValidatorTests
{
    private static RegisterRequestValidator Register()
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BetaGate:Enabled"] = "false",
        }).Build());

    private static string Ascii(int n) => "Aa1" + new string('x', n - 3);

    [Fact]
    public void Register_accepts_exactly_72_bytes()
        => Register().TestValidate(new RegisterRequest { Password = Ascii(72) })
                     .ShouldNotHaveValidationErrorFor(x => x.Password);

    [Fact]
    public void Register_refuses_73_bytes()
        => Register().TestValidate(new RegisterRequest { Password = Ascii(73) })
                     .ShouldHaveValidationErrorFor(x => x.Password)
                     .WithErrorMessage(PasswordRules.TooLongMessage);

    [Fact]
    public void Register_counts_bytes_not_characters()
    {
        // 40 characters, but each é is two bytes: 3 + 37×2 = 77 bytes.
        var pw = "Aa1" + new string('é', 37);
        pw.Length.Should().BeLessThan(72);
        Register().TestValidate(new RegisterRequest { Password = pw })
                  .ShouldHaveValidationErrorFor(x => x.Password);
    }

    [Fact]
    public void Reset_refuses_73_bytes()
        => new ResetPasswordRequestValidator()
               .TestValidate(new ResetPasswordRequest { NewPassword = Ascii(73) })
               .ShouldHaveValidationErrorFor(x => x.NewPassword);
}
