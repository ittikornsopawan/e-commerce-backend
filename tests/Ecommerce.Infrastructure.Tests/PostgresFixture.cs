using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;

namespace Ecommerce.Infrastructure.Tests;

/// <summary>
/// A throw-away PostgreSQL cluster (initdb + pg_ctl, as e-commerce/db/tests/run_db_tests.sh does) with every
/// migration of the Showcase applied, so tests run against the real schema. Needs initdb and pg_ctl on the PATH;
/// a missing tool fails the run, it is never skipped. ECOMMERCE_MIGRATIONS_DIR overrides the migrations folder.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private string _dir = "";

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ecommerce-pg-" + Guid.NewGuid().ToString("N"));
        try { await Start(); }
        catch { await DisposeAsync(); throw; }
    }

    private async Task Start()
    {
        var port = FreePort();
        Run("initdb", "-D", _dir, "-A", "trust", "-E", "UTF8", "--locale=C");
        Run("pg_ctl", "-D", _dir, "-o", $"-p {port} -c unix_socket_directories=", "-l", Path.Combine(_dir, "log"), "-w", "start");
        ConnectionString = $"Host=127.0.0.1;Port={port};Username={Environment.UserName};Database=postgres;Pooling=false";

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var file in Directory.GetFiles(MigrationsDir(), "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(file), connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    public Task DisposeAsync()
    {
        try { Run("pg_ctl", "-D", _dir, "-m", "immediate", "stop"); }
        catch (InvalidOperationException) { /* never started */ }
        finally { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        return Task.CompletedTask;
    }

    private static string MigrationsDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ECOMMERCE_MIGRATIONS_DIR");
        if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

        // The backend checkout is e-commerce/repository/e-commerce-backend, so the migrations are two levels up.
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "..", "..", "db", "migrations", "up");
            if (File.Exists(Path.Combine(dir.FullName, "Ecommerce.sln")) && Directory.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        throw new InvalidOperationException("Migrations not found; set ECOMMERCE_MIGRATIONS_DIR to e-commerce/db/migrations/up.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static void Run(string tool, params string[] args)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true };
        // macOS PostgreSQL starts multithreaded without a valid locale (same variables as run_db_tests.sh).
        info.Environment["LC_ALL"] = "en_US.UTF-8";
        info.Environment["LANG"] = "en_US.UTF-8";
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException($"{tool} did not start.");
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{tool} failed ({process.ExitCode}): {error}");
    }
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
