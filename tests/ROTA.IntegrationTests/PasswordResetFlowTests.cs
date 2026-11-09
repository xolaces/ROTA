using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// Forgot password from end to end: the real endpoints, the real background sender and the real
// SmtpClient, talking SMTP over a socket to a small local server that hands the mail back to the test.
// The code the test redeems is the one that came out of the email, as a player's would.
//
// HOW TO VERIFY THIS TEST CATCHES THE BUG:
//   - Remove the CanSend check from AuthController.RequestPasswordReset: the email-off cases get a 202.
public class PasswordResetFlowTests : IAsyncLifetime
{
    private const string OldPassword = "Reset-Flow-1";
    private const string NewPassword = "Reset-Flow-2";
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(30);

    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program>? _factory;
    private FakeSmtpServer? _smtp;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_reset_flow_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        _smtp?.Dispose();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private async Task<HttpClient> StartApiAsync(bool enabled, string username, string password, int port = 2525)
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Seed:AdminPassword"]                  = "",
                        ["BetaGate:Enabled"]                    = "false",
                        ["Email:Enabled"]                       = enabled ? "true" : "false",
                        ["Email:Host"]                          = "127.0.0.1",
                        ["Email:Port"]                          = port.ToString(),
                        ["Email:UseStartTls"]                   = "false",
                        ["Email:Username"]                      = username,
                        ["Email:Password"]                      = password,
                        ["Email:FromAddress"]                   = "noreply@rota.test",
                    }));
            });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();
        return _factory.CreateClient();
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return Path.Combine(dir.FullName, "src", "ROTA.Api");
    }

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var terms = _factory!.Services.GetRequiredService<IConfiguration>().GetValue("Legal:CurrentTermsVersion", 1);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"reset_{suffix}@rota.test";
        var reg = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = "reset_" + suffix,
            Email = email,
            Password = OldPassword,
            AcceptedTermsVersion = terms,
        });
        reg.StatusCode.Should().Be(HttpStatusCode.Created, await reg.Content.ReadAsStringAsync());
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = password });

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, string email, string code)
        => client.PostAsJsonAsync("/api/auth/password-reset/confirm",
            new ResetPasswordRequest { Email = email, Code = code, NewPassword = NewPassword });

    [Fact]
    public async Task ForgotPassword_WorksFromTheEmailedCode_ToLoggingInWithTheNewPassword()
    {
        _smtp = new FakeSmtpServer();
        var client = await StartApiAsync(enabled: true, "smtp-user", "smtp-pass", _smtp.Port);
        var email = await RegisterAsync(client);

        var request = await client.PostAsJsonAsync("/api/auth/password-reset/request",
            new PasswordResetRequest { Email = email });
        request.StatusCode.Should().Be(HttpStatusCode.Accepted);

        var mail = await _smtp.NextMailAsync().WaitAsync(MailTimeout);
        mail.Recipients.Should().ContainSingle().Which.Should().Contain(email, "the code goes to the player");
        var code = mail.ResetCode();

        (await ConfirmAsync(client, email, "ZZZZ-ZZZZ")).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a wrong code changes nothing");

        (await ConfirmAsync(client, email, code)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await LoginAsync(client, email, OldPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, email, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ConfirmAsync(client, email, code)).StatusCode
            .Should().Be(HttpStatusCode.UnprocessableEntity, "a code works once");

        (await WaitForResetEmailStatusAsync(EmailSendStatus.Sent)).Should().Be(EmailSendStatus.Sent);
    }

    [Theory]
    [InlineData(false, "smtp-user", "smtp-pass")]   // switched off
    [InlineData(true, "", "")]                      // switched on with no login
    public async Task WhenEmailCannotSend_ResetSaysSo_AndIssuesNoCode(bool enabled, string username, string password)
    {
        var client = await StartApiAsync(enabled, username, password);
        var email = await RegisterAsync(client);

        var known = await client.PostAsJsonAsync("/api/auth/password-reset/request",
            new PasswordResetRequest { Email = email });
        var unknown = await client.PostAsJsonAsync("/api/auth/password-reset/request",
            new PasswordResetRequest { Email = "nobody@rota.test" });

        known.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await known.Content.ReadAsStringAsync()).Should().Contain("Password reset is down");
        unknown.StatusCode.Should().Be(known.StatusCode, "the answer must not reveal which emails exist");
        (await unknown.Content.ReadAsStringAsync()).Should().Be(await known.Content.ReadAsStringAsync());

        using var scope = _factory!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db.PasswordResetTokens.CountAsync()).Should().Be(0);
        (await db.OutboundEmails.CountAsync(e => e.Type == EmailType.PasswordReset)).Should().Be(0);
    }

    // The row is marked just after the SMTP conversation ends, so it can trail the mail by a moment.
    private async Task<EmailSendStatus?> WaitForResetEmailStatusAsync(EmailSendStatus wanted)
    {
        EmailSendStatus? status = null;
        for (var i = 0; i < 40 && status != wanted; i++)
        {
            await Task.Delay(250);
            using var scope = _factory!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
            status = await db.OutboundEmails.AsNoTracking()
                .Where(e => e.Type == EmailType.PasswordReset)
                .Select(e => (EmailSendStatus?)e.SendStatus)
                .SingleOrDefaultAsync();
        }
        return status;
    }

    private sealed record ReceivedMail(IReadOnlyList<string> Recipients, string Data)
    {
        public string ResetCode()
        {
            var split = Data.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            var headers = Data[..split];
            var body = Data[(split + 4)..];

            var decoded =
                headers.Contains("base64", StringComparison.OrdinalIgnoreCase)
                    ? Encoding.UTF8.GetString(Convert.FromBase64String(
                        Regex.Replace(body, @"\s", "")))
                : headers.Contains("quoted-printable", StringComparison.OrdinalIgnoreCase)
                    ? Regex.Replace(body.Replace("=\r\n", ""), "=([0-9A-Fa-f]{2})",
                        m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
                : body;

            var match = Regex.Match(decoded, "[2-9A-HJKMNP-TV-Z]{4}-[2-9A-HJKMNP-TV-Z]{4}");
            match.Success.Should().BeTrue("the email must carry an XXXX-XXXX code, got: " + decoded);
            return match.Value;
        }
    }

    // Just enough SMTP for SmtpClient to deliver one message at a time. It offers no AUTH or STARTTLS,
    // so SmtpClient sends without either.
    private sealed class FakeSmtpServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Channel<ReceivedMail> _mail = Channel.CreateUnbounded<ReceivedMail>();

        public FakeSmtpServer()
        {
            _listener.Start();
            _ = AcceptLoopAsync();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public Task<ReceivedMail> NextMailAsync() => _mail.Reader.ReadAsync(_stop.Token).AsTask();

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = ServeAsync(socket);
                }
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private async Task ServeAsync(TcpClient socket)
        {
            using (socket)
            await using (var stream = socket.GetStream())
            {
                using var reader = new StreamReader(stream, Encoding.Latin1);
                await using var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };

                var recipients = new List<string>();
                await writer.WriteLineAsync("220 fake.smtp ready");

                while (await reader.ReadLineAsync() is { } line)
                {
                    if (line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase))
                    {
                        recipients.Add(line);
                        await writer.WriteLineAsync("250 OK");
                    }
                    else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("354 go ahead");
                        var data = new StringBuilder();
                        while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            data.Append(dataLine).Append("\r\n");
                        await _mail.Writer.WriteAsync(new ReceivedMail(recipients.ToList(), data.ToString()));
                        await writer.WriteLineAsync("250 queued");
                    }
                    else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        await writer.WriteLineAsync("221 bye");
                        return;
                    }
                    else
                    {
                        await writer.WriteLineAsync("250 OK");
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }
    }
}
