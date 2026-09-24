using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Xunit;

namespace VMS.Tests.Infrastructure;

/// <summary>
/// Boots the real API against a throwaway LocalDB database. The database is created, migrated and
/// seeded by the application's own start-up code, exactly as in production, and dropped afterwards.
/// Requires SQL Server LocalDB, the same as running the API locally.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "superadmin@test.local";
    public const string AdminPassword = "Test@Admin2026!";
    public const string Secret = "test-only-secret-0123456789-abcdefghijklmnopq";

    public string DatabaseName { get; } = "VMSTest_" + Guid.NewGuid().ToString("N")[..12];

    public string ConnectionString =>
        $"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    /// <summary>Where uploaded files go during the run. Created empty and deleted afterwards.</summary>
    public string FileRoot { get; } = Path.Combine(Path.GetTempPath(), "vms-test-files-" + Guid.NewGuid().ToString("N")[..12]);

    public ApiFactory()
    {
        Environment.SetEnvironmentVariable("FileStorage__RootPath", FileRoot);
        Environment.SetEnvironmentVariable("FileStorage__EncryptionKey", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()));
        // Program reads configuration while it builds, before the test host's own settings apply, so
        // environment variables are the reliable way in. They only affect this test process.
        Environment.SetEnvironmentVariable("Data__mainOrg", ConnectionString);
        Environment.SetEnvironmentVariable("AppSettings__Secret", Secret);
        Environment.SetEnvironmentVariable("Seed__SuperAdmin__Email", AdminEmail);
        Environment.SetEnvironmentVariable("Seed__SuperAdmin__Password", AdminPassword);
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitPerMinute", "1000");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Development");

    /// <summary>Runs a query against the test database, for assertions the API cannot make.</summary>
    public T Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        var result = command.ExecuteScalar();
        if (result is null or DBNull) return default!;
        if (result is T direct) return direct;
        return (T)Convert.ChangeType(result, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    /// <summary>
    /// Inserts a tenant directly, without an admin user, for tests that need one of their own (a token for an
    /// unknown tenant is refused). The API is started first so the database exists.
    /// </summary>
    public Guid CreateTenant(string? timeZone = null)
    {
        CreateClient();
        var id = Guid.NewGuid();
        Execute(
            "INSERT INTO tenancy.Tenants (Id, TenantCode, TenantName, IsActive, TimeZone, CreatedBy, CreatedDate) VALUES (@i, @c, 'Test tenant', 1, @z, 0, SYSUTCDATETIME())",
            ("@i", id), ("@c", "T" + id.ToString("N")[..12]), ("@z", (object?)timeZone ?? DBNull.Value));
        return id;
    }

    /// <summary>Runs a query and returns each row as column name → value (null for SQL NULL).</summary>
    public IReadOnlyList<Dictionary<string, object?>> Query(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        using var reader = command.ExecuteReader();

        var rows = new List<Dictionary<string, object?>>();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Runs a statement that returns nothing (used to set up or break things on purpose).</summary>
    public void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        try { if (Directory.Exists(FileRoot)) Directory.Delete(FileRoot, recursive: true); }
        catch { /* best effort: a leftover temp folder is harmless */ }
        try
        {
            await using var connection = new SqlConnection(
                "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"IF DB_ID('{DatabaseName}') IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END";
            await command.ExecuteNonQueryAsync();
        }
        catch
        {
            // Best effort: a leftover scratch database is harmless.
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
