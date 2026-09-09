using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
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
builder.Services.AddScoped<ConversionIntakeService>();
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

// Exposed so the integration test host can reference this assembly.
public partial class Program;
