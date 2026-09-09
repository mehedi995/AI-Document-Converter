using AI.Document.Converter.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace AI.Document.Converter.Web.Tests;

// Tenancy is enforced by queries, and a query only behaves the way you think it
// does once a real database has translated it. These tests therefore run
// against real PostgreSQL rather than the in-memory provider, which does not
// exercise SQL translation, constraints, or the UTC timestamptz mapping at all.
//
// Each run gets its own throwaway database so tests cannot see each other's
// rows or leave anything behind.
public sealed class PostgresFixture : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;

    public string TestConnectionString { get; private set; } = string.Empty;

    public string DatabaseName { get; } = $"adc_test_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        // Reuses the Web project's user-secret store (same UserSecretsId), so
        // `dotnet test` works on any machine that can already run the app,
        // without a second credential to configure or a password in the repo.
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<PostgresFixture>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var baseConnectionString =
            configuration.GetConnectionString("Default")
            ?? Environment.GetEnvironmentVariable("ADC_TEST_CONNECTION");

        // Fail loudly rather than skipping. A silently skipped cross-tenant
        // test is worse than no test: the suite still reports green while the
        // isolation guarantee is unverified.
        if (string.IsNullOrWhiteSpace(baseConnectionString))
        {
            throw new InvalidOperationException(
                "No database connection configured for tests. Set it with "
                + "`dotnet user-secrets set \"ConnectionStrings:Default\" \"<connection string>\" "
                + "--project src/AI.Document.Converter.Web`, or set ADC_TEST_CONNECTION. "
                + "See docs/saas/03-DEVELOPMENT-SETUP.md.");
        }

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        builder.Database = DatabaseName;
        TestConnectionString = builder.ConnectionString;

        await ExecuteNonQueryAsync(_adminConnectionString, $"""CREATE DATABASE "{DatabaseName}";""");

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public ConverterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ConverterDbContext>()
            .UseNpgsql(TestConnectionString)
            .Options;

        return new ConverterDbContext(options);
    }

    public async Task DisposeAsync()
    {
        // Npgsql pools connections, and PostgreSQL refuses to drop a database
        // that still has any open. Clearing the pool first is what makes
        // cleanup reliable instead of intermittently failing.
        NpgsqlConnection.ClearAllPools();

        await ExecuteNonQueryAsync(
            _adminConnectionString, $"""DROP DATABASE IF EXISTS "{DatabaseName}" WITH (FORCE);""");
    }

    private static async Task ExecuteNonQueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(nameof(PostgresCollection))]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
