using AiGateway.Api.Domain;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AiConfigRepository
{
    private readonly NpgsqlDataSource _ds;

    public AiConfigRepository(NpgsqlDataSource ds) { _ds = ds; }

    // ─────── Partners ───────

    public async Task<IReadOnlyList<AiPartner>> ListPartnersAsync(CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, code, name, status, adapter_code AS AdapterCode, base_url AS BaseUrl,
               health_check_model AS HealthCheckModel, weight, priority, quality_score AS QualityScore
        FROM ai_partners ORDER BY priority, code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<AiPartner>(sql)).ToList();
    }

    public async Task<AiPartner?> GetPartnerByCodeAsync(string code, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, code, name, status, adapter_code AS AdapterCode, base_url AS BaseUrl,
               health_check_model AS HealthCheckModel, weight, priority, quality_score AS QualityScore
        FROM ai_partners WHERE code = @code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiPartner>(sql, new { code });
    }

    public async Task<AiPartner> UpsertPartnerAsync(AiPartner p, CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO ai_partners
            (code, name, status, adapter_code, base_url, health_check_model, weight, priority, quality_score)
        VALUES
            (@Code, @Name, @Status, @AdapterCode, @BaseUrl, @HealthCheckModel, @Weight, @Priority, @QualityScore)
        ON CONFLICT (code) DO UPDATE SET
            name               = EXCLUDED.name,
            status             = EXCLUDED.status,
            adapter_code       = EXCLUDED.adapter_code,
            base_url           = EXCLUDED.base_url,
            health_check_model = EXCLUDED.health_check_model,
            weight             = EXCLUDED.weight,
            priority           = EXCLUDED.priority,
            quality_score      = EXCLUDED.quality_score,
            updated_at         = NOW()
        RETURNING id, code, name, status, adapter_code AS AdapterCode, base_url AS BaseUrl,
                  health_check_model AS HealthCheckModel, weight, priority, quality_score AS QualityScore
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AiPartner>(sql, p);
    }

    public async Task<int> UpdatePartnerStatusAsync(string code, string status, CancellationToken ct = default)
    {
        const string sql = "UPDATE ai_partners SET status = @s, updated_at = NOW() WHERE code = @c";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(sql, new { c = code, s = status });
    }

    // ─────── Models ───────

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, code, name, status,
               default_temperature AS DefaultTemperature,
               default_max_tokens  AS DefaultMaxTokens,
               strategy,
               fallback_enabled    AS FallbackEnabled,
               max_retry           AS MaxRetry
        FROM ai_models ORDER BY code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<AiModel>(sql)).ToList();
    }

    public async Task<AiModel?> GetModelByCodeAsync(string code, CancellationToken ct = default)
    {
        const string sql = """
        SELECT id, code, name, status,
               default_temperature AS DefaultTemperature,
               default_max_tokens  AS DefaultMaxTokens,
               strategy,
               fallback_enabled    AS FallbackEnabled,
               max_retry           AS MaxRetry
        FROM ai_models WHERE code = @code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<AiModel>(sql, new { code });
    }

    public async Task<AiModel> UpsertModelAsync(AiModel m, CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO ai_models
            (code, name, status, default_temperature, default_max_tokens, strategy, fallback_enabled, max_retry)
        VALUES
            (@Code, @Name, @Status, @DefaultTemperature, @DefaultMaxTokens, @Strategy, @FallbackEnabled, @MaxRetry)
        ON CONFLICT (code) DO UPDATE SET
            name                = EXCLUDED.name,
            status              = EXCLUDED.status,
            default_temperature = EXCLUDED.default_temperature,
            default_max_tokens  = EXCLUDED.default_max_tokens,
            strategy            = EXCLUDED.strategy,
            fallback_enabled    = EXCLUDED.fallback_enabled,
            max_retry           = EXCLUDED.max_retry,
            updated_at          = NOW()
        RETURNING id, code, name, status, default_temperature AS DefaultTemperature,
                  default_max_tokens AS DefaultMaxTokens, strategy,
                  fallback_enabled AS FallbackEnabled, max_retry AS MaxRetry
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AiModel>(sql, m);
    }

    public async Task<int> UpdateModelStatusAsync(string code, string status, CancellationToken ct = default)
    {
        const string sql = "UPDATE ai_models SET status = @s, updated_at = NOW() WHERE code = @c";
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(sql, new { c = code, s = status });
    }

    // ─────── Routes ───────

    public async Task<IReadOnlyList<AiModelRoute>> ListRoutesForModelAsync(string modelCode, CancellationToken ct = default)
    {
        const string sql = """
        SELECT r.id, r.model_id AS ModelId, r.partner_id AS PartnerId,
               m.code AS ModelCode, p.code AS PartnerCode,
               r.route_code AS RouteCode, r.status, r.provider_model AS ProviderModel,
               r.timeout_ms AS TimeoutMs, r.weight, r.priority
        FROM ai_model_routes r
        JOIN ai_models   m ON m.id = r.model_id
        JOIN ai_partners p ON p.id = r.partner_id
        WHERE m.code = @code
        ORDER BY r.priority, p.code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return (await conn.QueryAsync<AiModelRoute>(sql, new { code = modelCode })).ToList();
    }

    public async Task<AiModelRoute> UpsertRouteAsync(
        string modelCode, string partnerCode, string routeCode, string providerModel,
        string status, int timeoutMs, int weight, int priority,
        CancellationToken ct = default)
    {
        const string sql = """
        WITH m AS (SELECT id FROM ai_models   WHERE code = @mc),
             p AS (SELECT id FROM ai_partners WHERE code = @pc)
        INSERT INTO ai_model_routes
            (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
        SELECT m.id, p.id, @rc, @status, @pm, @to, @w, @pri FROM m, p
        ON CONFLICT (model_id, partner_id, route_code) DO UPDATE SET
            status         = EXCLUDED.status,
            provider_model = EXCLUDED.provider_model,
            timeout_ms     = EXCLUDED.timeout_ms,
            weight         = EXCLUDED.weight,
            priority       = EXCLUDED.priority,
            updated_at     = NOW()
        RETURNING id, model_id AS ModelId, partner_id AS PartnerId,
                  @mc AS ModelCode, @pc AS PartnerCode,
                  route_code AS RouteCode, status, provider_model AS ProviderModel,
                  timeout_ms AS TimeoutMs, weight, priority
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<AiModelRoute>(sql, new {
            mc = modelCode, pc = partnerCode, rc = routeCode, status,
            pm = providerModel, to = timeoutMs, w = weight, pri = priority
        });
    }

    public async Task<int> DeleteRouteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync("DELETE FROM ai_model_routes WHERE id = @id", new { id });
    }

    // ─────── Per-user route candidates ───────

    public async Task<IReadOnlyList<(AiModelRoute Route, AiPartner Partner, UserAccountKey Key)>>
        GetUserRouteCandidatesAsync(long userId, string modelCode, CancellationToken ct = default)
    {
        const string sql = """
        SELECT
            -- Route
            r.id, r.model_id AS ModelId, r.partner_id AS PartnerId,
            m.code AS ModelCode, p.code AS PartnerCode,
            r.route_code AS RouteCode, r.status, r.provider_model AS ProviderModel,
            r.timeout_ms AS TimeoutMs, r.weight, r.priority,
            -- Partner
            p.id, p.code, p.name, p.status, p.adapter_code AS AdapterCode,
            p.base_url AS BaseUrl, p.health_check_model AS HealthCheckModel,
            p.weight, p.priority, p.quality_score AS QualityScore,
            -- Key
            k.id, k.user_id AS UserId, k.partner_id AS PartnerId, p.code AS PartnerCode,
            k.code, k.name, k.status,
            k.api_key_enc AS ApiKeyEnc, k.api_key_fingerprint AS ApiKeyFingerprint,
            k.rpm_limit AS RpmLimit, k.rpd_limit AS RpdLimit,
            k.tpm_limit AS TpmLimit, k.tpd_limit AS TpdLimit,
            k.weight, k.priority,
            k.last_health_check_at AS LastHealthCheckAt,
            k.last_health_status   AS LastHealthStatus,
            k.last_health_error    AS LastHealthError,
            k.last_health_latency_ms AS LastHealthLatencyMs
        FROM ai_model_routes r
        JOIN ai_models   m ON m.id = r.model_id
        JOIN ai_partners p ON p.id = r.partner_id
        JOIN user_account_keys k ON k.partner_id = r.partner_id AND k.user_id = @u
        WHERE m.code = @mc
          AND r.status = 'active' AND m.status = 'active'
          AND p.status = 'active' AND k.status = 'active'
        ORDER BY r.priority, p.priority, k.priority
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AiModelRoute, AiPartner, UserAccountKey,
            (AiModelRoute, AiPartner, UserAccountKey)>(
            sql,
            (route, partner, key) => (route, partner, key),
            new { u = userId, mc = modelCode },
            splitOn: "id,id");

        return rows.ToList();
    }

    // ─────── Available models for a user ───────

    public async Task<IReadOnlyList<AvailableModelRow>> GetAvailableModelsForUserAsync(
        long userId, CancellationToken ct = default)
    {
        const string sql = """
        SELECT DISTINCT
            m.code          AS ModelCode,
            m.name          AS ModelName,
            m.status        AS ModelStatus,
            m.default_temperature AS DefaultTemperature,
            m.default_max_tokens  AS DefaultMaxTokens,
            p.code          AS PartnerCode,
            p.name          AS PartnerName
        FROM ai_models m
        JOIN ai_model_routes r ON r.model_id = m.id
        JOIN ai_partners p ON p.id = r.partner_id
        JOIN user_account_keys k ON k.partner_id = p.id AND k.user_id = @u
        WHERE m.status = 'active'
          AND r.status = 'active'
          AND p.status = 'active'
          AND k.status = 'active'
        ORDER BY m.code, p.code
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<AvailableModelRow>(sql, new { u = userId });
        return rows.ToList();
    }
}

/// <summary>Flat row returned by GetAvailableModelsForUserAsync.</summary>
public sealed record AvailableModelRow
{
    public string ModelCode { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public string ModelStatus { get; init; } = string.Empty;
    public decimal DefaultTemperature { get; init; }
    public int DefaultMaxTokens { get; init; }
    public string PartnerCode { get; init; } = string.Empty;
    public string PartnerName { get; init; } = string.Empty;
}
