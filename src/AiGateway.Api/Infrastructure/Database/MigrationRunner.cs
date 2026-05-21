using System.Security.Cryptography;
using System.Text;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class MigrationRunner
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(NpgsqlDataSource ds, ILogger<MigrationRunner> log)
    {
        _dataSource = ds;
        _logger = log;
    }

    public async Task RunAsync(string migrationsDir, CancellationToken ct = default)
    {
        if (!Directory.Exists(migrationsDir))
        {
            _logger.LogWarning("Migration directory not found: {Dir}", migrationsDir);
            return;
        }

        // Retry connecting to Postgres while supervisord brings it up (~ first 60s).
        await WaitForPostgresAsync(ct);

        await EnsureMigrationsTableAsync(ct);

        var applied = await GetAppliedAsync(ct);
        var files = Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f).ToArray();

        foreach (var file in files)
        {
            var version = Path.GetFileNameWithoutExtension(file);
            var sql = await File.ReadAllTextAsync(file, ct);
            if (string.IsNullOrWhiteSpace(sql)) continue;

            var checksum = Sha256Hex(sql);

            if (applied.TryGetValue(version, out var existingChecksum))
            {
                if (existingChecksum != checksum)
                {
                    _logger.LogWarning(
                        "Migration {Version} checksum mismatch (applied {Old}, file {New}). " +
                        "Edit history changed; not re-applying.",
                        version, existingChecksum, checksum);
                }
                else
                {
                    _logger.LogDebug("Migration already applied: {Version}", version);
                }
                continue;
            }

            _logger.LogInformation("Applying migration {Version}", version);

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            try
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 120;
                    await cmd.ExecuteNonQueryAsync(ct);
                }

                await conn.ExecuteAsync(
                    "INSERT INTO __schema_migrations (version, name, checksum) VALUES (@v, @n, @c)",
                    new { v = version, n = Path.GetFileName(file), c = checksum },
                    transaction: tx);

                await tx.CommitAsync(ct);
                _logger.LogInformation("Migration applied: {Version}", version);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                _logger.LogError(ex, "Migration failed: {Version}", version);
                throw;
            }
        }
    }

    private async Task WaitForPostgresAsync(CancellationToken ct)
    {
        const int maxAttempts = 60;
        for (var i = 1; i <= maxAttempts; i++)
        {
            try
            {
                await using var conn = await _dataSource.OpenConnectionAsync(ct);
                await conn.ExecuteScalarAsync<int>("SELECT 1");
                return;
            }
            catch (Exception ex) when (i < maxAttempts)
            {
                _logger.LogDebug("Postgres not ready ({Attempt}/{Max}): {Msg}", i, maxAttempts, ex.Message);
                await Task.Delay(1000, ct);
            }
        }
        throw new InvalidOperationException("Postgres not reachable after waiting.");
    }

    private async Task EnsureMigrationsTableAsync(CancellationToken ct)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS __schema_migrations (
            version    TEXT PRIMARY KEY,
            name       TEXT NOT NULL,
            checksum   TEXT NOT NULL,
            applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        """;
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(sql);
    }

    private async Task<Dictionary<string, string>> GetAppliedAsync(CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(string version, string checksum)>(
            "SELECT version, checksum FROM __schema_migrations");
        return rows.ToDictionary(r => r.version, r => r.checksum);
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s.Replace("\r\n", "\n")));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
