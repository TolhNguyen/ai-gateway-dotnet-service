using AiGateway.Api.Domain;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class UserRepository
{
    private readonly NpgsqlDataSource _ds;

    public UserRepository(NpgsqlDataSource ds) { _ds = ds; }

    // ──────────────── Users ────────────────

    public async Task<long?> CountAsync(CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM users");
    }

    public async Task<User> CreateAsync(string email, string passwordHash, string role, string? displayName, CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO users (email, password_hash, role, display_name)
        VALUES (@email, @hash, @role, @display)
        RETURNING id, email, password_hash AS PasswordHash, role, status, display_name AS DisplayName, created_at AS CreatedAt, updated_at AS UpdatedAt;
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<User>(sql, new
        {
            email = email.Trim().ToLowerInvariant(),
            hash = passwordHash,
            role,
            display = displayName
        });
    }

    public async Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, email, password_hash AS PasswordHash, role, status, display_name AS DisplayName,
               created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM users WHERE email = @email
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<User>(sql, new { email = email.Trim().ToLowerInvariant() });
    }

    public async Task<User?> FindByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, email, password_hash AS PasswordHash, role, status, display_name AS DisplayName,
               created_at AS CreatedAt, updated_at AS UpdatedAt
        FROM users WHERE id = @id
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<User>(sql, new { id });
    }

    // ──────────────── PATs ────────────────

    public async Task<PersonalAccessToken> CreatePatAsync(
        long userId, string name, string tokenHash, string tokenPrefix, DateTimeOffset? expiresAt, CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO user_personal_access_tokens (user_id, name, token_hash, token_prefix, expires_at)
        VALUES (@u, @n, @h, @p, @e)
        RETURNING id, user_id AS UserId, name, token_hash AS TokenHash, token_prefix AS TokenPrefix,
                  last_used_at AS LastUsedAt, expires_at AS ExpiresAt, created_at AS CreatedAt;
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<PersonalAccessToken>(sql,
            new { u = userId, n = name, h = tokenHash, p = tokenPrefix, e = expiresAt });
    }

    public async Task<PersonalAccessToken?> FindActivePatByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, user_id AS UserId, name, token_hash AS TokenHash, token_prefix AS TokenPrefix,
               last_used_at AS LastUsedAt, expires_at AS ExpiresAt, created_at AS CreatedAt
        FROM user_personal_access_tokens
        WHERE token_hash = @h
          AND (expires_at IS NULL OR expires_at > NOW())
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<PersonalAccessToken>(sql, new { h = tokenHash });
    }

    public async Task<IReadOnlyList<PersonalAccessToken>> ListPatsForUserAsync(long userId, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, user_id AS UserId, name, token_hash AS TokenHash, token_prefix AS TokenPrefix,
               last_used_at AS LastUsedAt, expires_at AS ExpiresAt, created_at AS CreatedAt
        FROM user_personal_access_tokens
        WHERE user_id = @u
        ORDER BY created_at DESC
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PersonalAccessToken>(sql, new { u = userId });
        return rows.ToList();
    }

    public async Task<int> RevokePatAsync(long userId, long patId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM user_personal_access_tokens WHERE id = @id AND user_id = @u";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(sql, new { id = patId, u = userId });
    }

    public async Task TouchPatLastUsedAsync(long patId)
    {
        // Best-effort; swallow exceptions to avoid blocking auth.
        try
        {
            const string sql = "UPDATE user_personal_access_tokens SET last_used_at = NOW() WHERE id = @id";
            await using var conn = await _ds.OpenConnectionAsync();
            await conn.ExecuteAsync(sql, new { id = patId });
        }
        catch { /* intentionally swallowed */ }
    }
}
