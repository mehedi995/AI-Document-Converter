using AI.Document.Converter.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AI.Document.Converter.Web.Tests.TestSupport;

// Boots the REAL application pipeline against a throwaway database.
//
// This exists because the things it tests cannot be tested any other way. The
// operator authorization policy, the two-factor login step and the anti-forgery
// and cookie configuration all live in Program.cs, and a service-level test
// never executes a line of it. Until this harness existed, the only proof that
// the policy refused a password-only operator was me running the app and trying
// it - which proves nothing about tomorrow's Program.cs.
//
// Deliberately NOT a rebuilt pipeline. Assembling an imitation host in the test
// project would let the two drift apart, and the drift would be invisible
// precisely in the security wiring that matters most.
public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public WebAppFactory(string connectionString) => _connectionString = connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, because Program.cs refuses to start otherwise: the
        // dev-only email sender throws outside Development rather than
        // silently discarding confirmation mail. That guard is correct, so the
        // test host meets it rather than weakening it.
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            // Point the app at the fixture's database. Everything else -
            // Identity, the policies, the middleware order - is left exactly
            // as the application configures it, because that is the thing
            // under test.
            services.RemoveAll<DbContextOptions<ConverterDbContext>>();
            services.RemoveAll<ConverterDbContext>();

            services.AddDbContext<ConverterDbContext>(options => options.UseNpgsql(_connectionString));
        });
    }

    // Cookies are the session, so a client that discards them cannot test
    // authorization at all. Redirects are NOT followed: the interesting result
    // is usually the 302 itself and where it points - to the login page, or to
    // access-denied - and following it would turn every refusal into an
    // indistinguishable 200.
    public HttpClient CreateSessionClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost")
        });
}
