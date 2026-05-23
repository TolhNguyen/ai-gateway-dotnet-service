using AiGateway.Api.Domain;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AccountKeyRepository
{
    private readonly NpgsqlDataSource _ds;

    public AccountKeyRepository(NpgsqlDataSource ds) { _ds = ds; }

    private const string Select = """
        SELECT k.id, k.user_id AS UserId, k.partner_id AS PartnerId, p.code AS PartnerCode,
               k.code, k.name, k.status,
               k.api_key_enc AS ApiKeyEnc, k.api_key_fingerprint AS ApiKeyFingerprint,
               k.rpm_limit AS RpmLimit, k.rpd_limit AS RpdLimit,
               k.tpm_limit AS TpmLimit, k.tpd_limit AS TpdLimit,
               k.weight, k.priority,
               k.default_model_code AS DefaultModelCode,
               k.last_health_check_at AS LastHealthCheckAt,
               k.last_health_status   AS LastHealthStatus,
               k.last_health_error    AS LastHealthError,
               k.last_health_latency_ms AS LastHealthLatencyMs
        FROM user_account_keys k
        JOIN ai_partners p ON p.id = k.partner_id
        """;

    public async Task<IReadOnlyList<UserAccountKey>> ListForUserAsync(long userId, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<UserAccountKey>(
            $"{Select} WHERE k.user_id = @u ORDER BY p.code, k.code",
            new { u = userId });
        return rows.ToList();
    }

    public async Task<UserAccountKey?> FindByIdAsync(long userId, long id, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<UserAccountKey>(
            $"{Select} WHERE k.user_id = @u AND k.id = @id",
            new { u = userId, id });
    }

    public async Task<UserAccountKey> CreateAsync(
        long userId, long partnerId, string code, string? name,
        string apiKeyEnc, string fingerprint,
        int? rpm, int? rpd, int? tpm, int? tpd, int weight, int priority,
        string? defaultModelCode,
        CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO user_account_keys
            (user_id, partner_id, code, name, api_key_enc, api_key_fingerprint,
             rpm_limit, rpd_limit, tpm_limit, tpd_limit, weight, priority, default_model_code)
        VALUES (@u, @p, @code, @name, @enc, @fp, @rpm, @rpd, @tpm, @tpd, @w, @pri, @dm)
        RETURNING id
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var newId = await conn.ExecuteScalarAsync<long>(sql, new
        {
            u = userId, p = partnerId, code, name, enc = apiKeyEnc, fp = fingerprint,
            rpm, rpd, tpm, tpd, w = weight, pri = priority, dm = defaultModelCode
        });

        var entity = await FindByIdAsync(userId, newId, ct);
        return entity!;
    }

    public async Task<int> UpdateAsync(
        long userId, long id,
        string? apiKeyEnc, string? fingerprint, string? name, string? status,
        int? rpm, int? rpd, int? tpm, int? tpd, int? weight, int? priority,
        string? defaultModelCode, bool updateDefaultModel,
        CancellationToken ct = default)
    {
        const string sql = """
        UPDATE user_account_keys SET
            api_key_enc         = COALESCE(@enc, api_key_enc),
            api_key_fingerprint = COALESCE(@fp,  api_key_fingerprint),
            name                = COALESCE(@name, name),
            status              = COALESCE(@status, status),
            rpm_limit           = COALESCE(@rpm, rpm_limit),
            rpd_limit           = COALESCE(@rpd, rpd_limit),
            tpm_limit           = COALESCE(@tpm, tpm_limit),
            tpd_limit           = COALESCE(@tpd, tpd_limit),
            weight              = COALESCE(@w,   weight),
            priority            = COALESCE(@pri, priority),
            default_model_code  = CASE WHEN @udm THEN @dm ELSE default_model_code END,
            updated_at          = NOW()
        WHERE user_id = @u AND id = @id
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(sql, new
        {
            id, u = userId,
            enc = apiKeyEnc, fp = fingerprint, name, status,
            rpm, rpd, tpm, tpd, w = weight, pri = priority,
            dm = defaultModelCode, udm = updateDefaultModel
        });
    }

    public async Task<int> DeleteAsync(long userId, long id, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            "DELETE FROM user_account_keys WHERE id = @id AND user_id = @u",
            new { id, u = userId });
    }

    public async Task UpdateHealthAsync(
        long id, string status, string? errorMessage, int? latencyMs, CancellationToken ct = default)
    {
        const string sql = """
        UPDATE user_account_keys SET
            last_health_check_at   = NOW(),
            last_health_status     = @s,
            last_health_error      = @e,
            last_health_latency_ms = @ms,
            updated_at             = NOW()
        WHERE id = @id
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new { id, s = status, e = errorMessage, ms = latencyMs });
    }

    /// <summary>Returns active keys across all users for the health-check worker.</summary>
    public async Task<IReadOnlyList<UserAccountKey>> ListAllActiveAsync(CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<UserAccountKey>(
            $"{Select} WHERE k.status = 'active' AND p.status = 'active' ORDER BY k.id");
        return rows.ToList();
    }
}
