using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Operations;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Export;
using AI.Document.Converter.Persistence.Jobs;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SEC-003 / SaaS §4: no real secret is committed. The local development
// connection string lives in .NET user secrets, which are stored in the user's
// own profile, outside the repository. appsettings.Development.json carries the
// shape only, with no password, so a developer can see what is expected without
// a credential ever entering source control.
//
//   dotnet user-secrets set "ConnectionStrings:Default" "<connection string>" \
//       --project src/AI.Document.Converter.Web
//
// Failing loudly here is deliberate: a silent fallback to some default
// connection is how a developer ends up unknowingly writing to the wrong
// database.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "No 'ConnectionStrings:Default' configured. For local development set it with "
        + "`dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" "
        + "--project src/AI.Document.Converter.Web`. See docs/saas/03-DEVELOPMENT-SETUP.md.");

builder.Services.AddDbContext<ConverterDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services
    .AddIdentity<ApplicationUser, ApplicationRole>(options =>
    {
        // SaaS §5.2: email verification is part of P0, so an unconfirmed
        // account must not be able to sign in.
        options.SignIn.RequireConfirmedEmail = true;
        options.User.RequireUniqueEmail = true;

        // Length over character classes. A 12-character minimum carries more
        // real strength than composition rules, which mostly push people
        // towards predictable substitutions ("Password1!"). Identity's
        // defaults require a digit, an uppercase and a lowercase; those are
        // turned off deliberately rather than left on by omission, so the
        // policy matches the reasoning.
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;

        options.Lockout.MaxFailedAccessAttempts = 10;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<ConverterDbContext>()
    .AddDefaultTokenProviders();

// SR-SEC-7. Two independent requirements, because either alone is
// insufficient: the role without MFA means a stolen password reaches
// cross-tenant data, and MFA without the role means every user does.
builder.Services.AddAuthorization(options =>
    options.AddPolicy(AI.Document.Converter.Web.Security.OperatorPolicy.Name, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole(AI.Document.Converter.Web.Security.OperatorPolicy.RoleName)
        .RequireClaim(
            AI.Document.Converter.Web.Security.OperatorPolicy.AuthenticationMethodClaim,
            AI.Document.Converter.Web.Security.OperatorPolicy.MultiFactorValue)));

builder.Services.ConfigureApplicationCookie(options =>
{
    // SaaS §4: same-origin HttpOnly cookie session. HttpOnly keeps the cookie
    // away from script, SameSite=Lax blocks it from cross-site POSTs (the CSRF
    // path that matters here) while still allowing ordinary inbound links, and
    // Always means it is never sent over plain HTTP.
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.Name = "adc.session";

    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/denied";
});

builder.Services.AddScoped<WorkspaceProvisioner>();
builder.Services.AddScoped<WorkspaceAccessService>();
// SR-BIL-1: no provider is approved for this seller, so checkout is
// disabled by the implementation rather than by a flag. Adopting a
// provider means changing this one line.
builder.Services.AddSingleton<IBillingProvider, NoBillingProvider>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<MeteringService>();
builder.Services.AddScoped<OperatorConsoleService>();
builder.Services.AddScoped<OperatorInspectionService>();
builder.Services.AddScoped<ConversionIntakeService>();

// The upload page shows these values and the worker's sweep enforces them, both
// from this one configuration section - so what the customer is promised and
// what actually happens cannot drift apart (SR-SEC-6).
builder.Services.Configure<RetentionPolicy>(builder.Configuration.GetSection("Retention"));
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<JobLifecycleService>();
builder.Services.AddScoped<ExportPackageBuilder>();
builder.Services.AddSingleton<UploadValidator>();

// Local adapter for development. Production swaps in a private object store;
// the interface exists so upload and download can be built and tested without
// one, and so it is proven against a real implementation rather than a mock.
builder.Services.Configure<LocalFileSystemObjectStorageOptions>(
    builder.Configuration.GetSection("ObjectStorage"));
builder.Services.AddSingleton<IObjectStorage, LocalFileSystemObjectStorage>();

// The dev capture adapter is registered ONLY in Development. In any other
// environment the application refuses to start until a real sender is wired up,
// rather than silently writing verification and password-reset mail to a folder
// where no customer will ever see it (SR-SEC-8).
if (builder.Environment.IsDevelopment())
{
    builder.Services.Configure<DevFileEmailSenderOptions>(
        builder.Configuration.GetSection("DevEmail"));
    builder.Services.AddSingleton<IEmailSender, DevFileEmailSender>();
}
else
{
    throw new InvalidOperationException(
        "No production IEmailSender is configured. Registration and password reset depend on "
        + "real email delivery; refusing to start rather than dropping mail on the floor.");
}

builder.Services.AddControllersWithViews(options =>
{
    // CSRF validation on by default for every non-GET action, rather than
    // relying on each controller to remember the attribute.
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});

var app = builder.Build();

// Operator role management, from the command line only.
//
// SR-SEC-7 makes operator status a privileged, MFA-gated thing, so granting it
// is deliberately NOT a web page: a UI that hands out administrative access is
// a privilege-escalation target, for something done a handful of times in a
// system's life. Running it requires shell access to the host and the
// database connection string, which is a reasonable bar for the action.
//
// It goes through UserManager rather than raw SQL so the account is verified to
// exist and the role tables stay consistent.
if (args.Length > 0 && OperatorRoleCommand.Handles(args[0]))
{
    return await OperatorRoleCommand.RunAsync(app.Services, args);
}


if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // FR-031 / SR-SEC-8: customers never see stack traces, Python paths, or
    // database detail.
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// SR-SEC-3, defence in depth. Converted customer documents are displayed as
// escaped text rather than rendered HTML, so nothing should be able to execute
// in the first place - but a CSP means a mistake in one view does not become a
// working script injection. No inline script, no third-party origins, and
// frame-ancestors 'none' so the app cannot be framed for clickjacking.
app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
        + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Liveness only. Deliberately does NOT touch the database or the engine: a
// health endpoint that fails when a dependency is briefly unavailable causes
// the orchestrator to kill an otherwise healthy process.
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

return 0;

// Exposed so the integration test host can reference this assembly.
public partial class Program;

// Top-level statements compile into an internal Program class, which
// WebApplicationFactory cannot reach. Declaring it public here is what lets the
// test suite boot the REAL pipeline - the same middleware order, the same
// authorization policies - rather than a hand-assembled imitation that could
// drift from it without anything noticing.
public partial class Program;

// Placed at the end of the file: it is a maintenance path, not part of the
// request pipeline.
internal static class OperatorRoleCommand
{
    private const string Grant = "grant-operator";
    private const string Revoke = "revoke-operator";
    private const string List = "list-operators";

    public static bool Handles(string argument) =>
        argument is Grant or Revoke or List;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        var role = AI.Document.Converter.Web.Security.OperatorPolicy.RoleName;

        if (!await roles.RoleExistsAsync(role))
        {
            await roles.CreateAsync(new ApplicationRole { Name = role });
        }

        if (args[0] == List)
        {
            var operators = await users.GetUsersInRoleAsync(role);

            foreach (var op in operators)
            {
                // Two-factor state is printed alongside, because an operator
                // without it cannot actually reach the console and that is
                // otherwise invisible until they try.
                Console.WriteLine(
                    $"{op.Email}	two-factor: {(op.TwoFactorEnabled ? "on" : "OFF - cannot use the console")}");
            }

            if (operators.Count == 0)
            {
                Console.WriteLine("No operators.");
            }

            return 0;
        }

        if (args.Length < 2)
        {
            Console.Error.WriteLine($"Usage: {args[0]} <email>");
            return 1;
        }

        var user = await users.FindByEmailAsync(args[1]);

        if (user is null)
        {
            Console.Error.WriteLine($"No account with email '{args[1]}'.");
            return 1;
        }

        if (args[0] == Grant)
        {
            var result = await users.AddToRoleAsync(user, role);

            if (!result.Succeeded && !await users.IsInRoleAsync(user, role))
            {
                Console.Error.WriteLine(string.Join("; ", result.Errors.Select(e => e.Description)));
                return 1;
            }

            Console.WriteLine($"Granted the {role} role to {user.Email}.");

            if (!user.TwoFactorEnabled)
            {
                // Not a failure - the grant is real - but the account cannot
                // use the console yet, and saying so here saves a confusing
                // "access denied" later.
                Console.WriteLine(
                    "NOTE: this account has no second factor, so the operator console will refuse it. "
                    + "Have them set one up at /security/two-factor, then sign out and back in.");
            }

            return 0;
        }

        await users.RemoveFromRoleAsync(user, role);
        Console.WriteLine($"Removed the {role} role from {user.Email}.");
        return 0;
    }
}
