using System.Text.Json;
using AiGateway.Api.Contracts;
using AiGateway.Api.Domain;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AiConfigRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AiConfigRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertClientAsync(UpsertClientRequest request, string? apiKeyHash)
    {
        const string sql = """
        INSERT INTO ai_clients (code, name, status, api_key_hash, rpm_limit, rpd_limit, allowed_models, config, updated_at)
        VALUES (@Code, @Name, @Status, @ApiKeyHash, @RpmLimit, @RpdLimit, CAST(@AllowedModelsJson AS jsonb), CAST(@ConfigJson AS jsonb), NOW())
        ON CONFLICT (code) DO UPDATE SET
            name = EXCLUDED.name,
            status = EXCLUDED.status,
            api_key_hash = COALESCE(EXCLUDED.api_key_hash, ai_clients.api_key_hash),
            rpm_limit = EXCLUDED.rpm_limit,
            rpd_limit = EXCLUDED.rpd_limit,
            allowed_models = EXCLUDED.allowed_models,
            config = EXCLUDED.config,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new
        {
            request.Code,
            request.Name,
            request.Status,
            ApiKeyHash = apiKeyHash,
            request.RpmLimit,
            request.RpdLimit,
            AllowedModelsJson = JsonSerializer.Serialize(request.AllowedModels, JsonOptions),
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOptions)
        });
    }

    public async Task<IReadOnlyList<AiClientConfig>> GetClientsAsync()
    {
        const string sql = """
        SELECT code, name, status, api_key_hash, rpm_limit, rpd_limit,
               allowed_models::text AS allowed_models_json,
               config::text AS config_json
        FROM ai_clients
        ORDER BY code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<ClientRow>(sql);
        return rows.Select(MapClient).ToList();
    }

    public async Task<AiClientConfig?> GetClientByCodeAsync(string code)
    {
        const string sql = """
        SELECT code, name, status, api_key_hash, rpm_limit, rpd_limit,
               allowed_models::text AS allowed_models_json,
               config::text AS config_json
        FROM ai_clients
        WHERE code = @Code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<ClientRow>(sql, new { Code = code });
        return row is null ? null : MapClient(row);
    }

    public async Task UpdateClientStatusAsync(string code, string status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE ai_clients SET status = @Status, updated_at = NOW() WHERE code = @Code",
            new { Code = code, Status = status });
    }

    public async Task UpsertModelAsync(UpsertModelRequest request)
    {
        const string sql = """
        INSERT INTO ai_models (
            code, name, status, default_temperature, default_max_tokens,
            strategy, fallback_enabled, max_retry, config, updated_at)
        VALUES (
            @Code, @Name, @Status, @DefaultTemperature, @DefaultMaxTokens,
            @Strategy, @FallbackEnabled, @MaxRetry, CAST(@ConfigJson AS jsonb), NOW())
        ON CONFLICT (code) DO UPDATE SET
            name = EXCLUDED.name,
            status = EXCLUDED.status,
            default_temperature = EXCLUDED.default_temperature,
            default_max_tokens = EXCLUDED.default_max_tokens,
            strategy = EXCLUDED.strategy,
            fallback_enabled = EXCLUDED.fallback_enabled,
            max_retry = EXCLUDED.max_retry,
            config = EXCLUDED.config,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new
        {
            request.Code,
            request.Name,
            request.Status,
            request.DefaultTemperature,
            request.DefaultMaxTokens,
            request.Strategy,
            request.FallbackEnabled,
            request.MaxRetry,
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOptions)
        });
    }

    public async Task<IReadOnlyList<AiModelConfig>> GetModelsAsync()
    {
        const string sql = """
        SELECT code, name, status, default_temperature, default_max_tokens,
               strategy, fallback_enabled, max_retry, config::text AS config_json
        FROM ai_models
        ORDER BY code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<ModelRow>(sql);
        return rows.Select(MapModel).ToList();
    }

    public async Task<AiModelConfig?> GetModelByCodeAsync(string code)
    {
        const string sql = """
        SELECT code, name, status, default_temperature, default_max_tokens,
               strategy, fallback_enabled, max_retry, config::text AS config_json
        FROM ai_models
        WHERE code = @Code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<ModelRow>(sql, new { Code = code });
        return row is null ? null : MapModel(row);
    }

    public async Task UpdateModelStatusAsync(string code, string status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE ai_models SET status = @Status, updated_at = NOW() WHERE code = @Code",
            new { Code = code, Status = status });
    }

    public async Task UpsertPartnerAsync(UpsertPartnerRequest request)
    {
        const string sql = """
        INSERT INTO ai_partners (
            code, name, status, adapter_code, base_url,
            weight, priority, quality_score, config, updated_at)
        VALUES (
            @Code, @Name, @Status, @AdapterCode, @BaseUrl,
            @Weight, @Priority, @QualityScore, CAST(@ConfigJson AS jsonb), NOW())
        ON CONFLICT (code) DO UPDATE SET
            name = EXCLUDED.name,
            status = EXCLUDED.status,
            adapter_code = EXCLUDED.adapter_code,
            base_url = EXCLUDED.base_url,
            weight = EXCLUDED.weight,
            priority = EXCLUDED.priority,
            quality_score = EXCLUDED.quality_score,
            config = EXCLUDED.config,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, new
        {
            request.Code,
            request.Name,
            request.Status,
            request.AdapterCode,
            request.BaseUrl,
            request.Weight,
            request.Priority,
            request.QualityScore,
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOptions)
        });
    }

    public async Task<IReadOnlyList<AiPartnerConfig>> GetPartnersAsync()
    {
        const string sql = """
        SELECT code, name, status, adapter_code, base_url,
               weight, priority, quality_score, config::text AS config_json
        FROM ai_partners
        ORDER BY code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<PartnerRow>(sql);
        return rows.Select(MapPartner).ToList();
    }

    public async Task<AiPartnerConfig?> GetPartnerByCodeAsync(string code)
    {
        const string sql = """
        SELECT code, name, status, adapter_code, base_url,
               weight, priority, quality_score, config::text AS config_json
        FROM ai_partners
        WHERE code = @Code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<PartnerRow>(sql, new { Code = code });
        return row is null ? null : MapPartner(row);
    }

    public async Task UpdatePartnerStatusAsync(string code, string status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE ai_partners SET status = @Status, updated_at = NOW() WHERE code = @Code",
            new { Code = code, Status = status });
    }

    public async Task UpsertAccountAsync(string partnerCode, UpsertAccountRequest request, string? apiKeyEnc)
    {
        const string sql = """
        INSERT INTO ai_accounts (
            partner_id, code, name, status, account_ref, api_key_enc, api_key_ref,
            rpm_limit, rpd_limit, tpm_limit, tpd_limit,
            weight, priority, config, updated_at)
        SELECT
            p.id, @Code, @Name, @Status, @AccountRef, @ApiKeyEnc, @ApiKeyRef,
            @RpmLimit, @RpdLimit, @TpmLimit, @TpdLimit,
            @Weight, @Priority, CAST(@ConfigJson AS jsonb), NOW()
        FROM ai_partners p
        WHERE p.code = @PartnerCode
        ON CONFLICT (code) DO UPDATE SET
            name = EXCLUDED.name,
            status = EXCLUDED.status,
            account_ref = EXCLUDED.account_ref,
            api_key_enc = COALESCE(EXCLUDED.api_key_enc, ai_accounts.api_key_enc),
            api_key_ref = EXCLUDED.api_key_ref,
            rpm_limit = EXCLUDED.rpm_limit,
            rpd_limit = EXCLUDED.rpd_limit,
            tpm_limit = EXCLUDED.tpm_limit,
            tpd_limit = EXCLUDED.tpd_limit,
            weight = EXCLUDED.weight,
            priority = EXCLUDED.priority,
            config = EXCLUDED.config,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var affected = await connection.ExecuteAsync(sql, new
        {
            PartnerCode = partnerCode,
            request.Code,
            request.Name,
            request.Status,
            request.AccountRef,
            ApiKeyEnc = apiKeyEnc,
            request.ApiKeyRef,
            request.RpmLimit,
            request.RpdLimit,
            request.TpmLimit,
            request.TpdLimit,
            request.Weight,
            request.Priority,
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOptions)
        });

        if (affected == 0)
        {
            throw new InvalidOperationException($"Partner not found: {partnerCode}");
        }
    }

    public async Task<IReadOnlyList<AiAccountConfig>> GetAccountsByPartnerAsync(string partnerCode)
    {
        const string sql = """
        SELECT a.code, p.code AS partner_code, a.name, a.status, a.account_ref,
               a.api_key_enc, a.api_key_ref,
               a.rpm_limit, a.rpd_limit, a.tpm_limit, a.tpd_limit,
               a.weight, a.priority, a.config::text AS config_json
        FROM ai_accounts a
        JOIN ai_partners p ON p.id = a.partner_id
        WHERE p.code = @PartnerCode
        ORDER BY a.priority ASC, a.code ASC;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<AccountRow>(sql, new { PartnerCode = partnerCode });
        return rows.Select(MapAccount).ToList();
    }

    public async Task<AiAccountConfig?> GetAccountByCodeAsync(string code)
    {
        const string sql = """
        SELECT a.code, p.code AS partner_code, a.name, a.status, a.account_ref,
               a.api_key_enc, a.api_key_ref,
               a.rpm_limit, a.rpd_limit, a.tpm_limit, a.tpd_limit,
               a.weight, a.priority, a.config::text AS config_json
        FROM ai_accounts a
        JOIN ai_partners p ON p.id = a.partner_id
        WHERE a.code = @Code;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleOrDefaultAsync<AccountRow>(sql, new { Code = code });
        return row is null ? null : MapAccount(row);
    }

    public async Task UpdateAccountStatusAsync(string code, string status)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "UPDATE ai_accounts SET status = @Status, updated_at = NOW() WHERE code = @Code",
            new { Code = code, Status = status });
    }

    public async Task UpsertModelRouteAsync(string modelCode, UpsertModelRouteRequest request)
    {
        const string sql = """
        INSERT INTO ai_model_routes (
            model_id, partner_id, route_code, status, provider_model,
            timeout_ms, weight, priority, config, updated_at)
        SELECT
            m.id, p.id, @RouteCode, @Status, @ProviderModel,
            @TimeoutMs, @Weight, @Priority, CAST(@ConfigJson AS jsonb), NOW()
        FROM ai_models m
        CROSS JOIN ai_partners p
        WHERE m.code = @ModelCode AND p.code = @PartnerCode
        ON CONFLICT (model_id, partner_id, route_code) DO UPDATE SET
            status = EXCLUDED.status,
            provider_model = EXCLUDED.provider_model,
            timeout_ms = EXCLUDED.timeout_ms,
            weight = EXCLUDED.weight,
            priority = EXCLUDED.priority,
            config = EXCLUDED.config,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var affected = await connection.ExecuteAsync(sql, new
        {
            ModelCode = modelCode,
            request.PartnerCode,
            RouteCode = string.IsNullOrWhiteSpace(request.RouteCode) ? "default" : request.RouteCode,
            request.Status,
            request.ProviderModel,
            request.TimeoutMs,
            request.Weight,
            request.Priority,
            ConfigJson = JsonSerializer.Serialize(request.Config, JsonOptions)
        });

        if (affected == 0)
        {
            throw new InvalidOperationException($"Model or partner not found: {modelCode}/{request.PartnerCode}");
        }
    }

    public async Task<IReadOnlyList<AiModelRouteConfig>> GetRoutesByModelAsync(string modelCode)
    {
        const string sql = """
        SELECT m.code AS model_code, p.code AS partner_code, r.route_code, r.status, r.provider_model,
               r.timeout_ms, r.weight, r.priority, r.config::text AS config_json
        FROM ai_model_routes r
        JOIN ai_models m ON m.id = r.model_id
        JOIN ai_partners p ON p.id = r.partner_id
        WHERE m.code = @ModelCode
        ORDER BY r.priority ASC, p.code ASC;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<RouteRow>(sql, new { ModelCode = modelCode });
        return rows.Select(MapRoute).ToList();
    }

    public async Task<IReadOnlyList<AiRouteCandidate>> GetRouteCandidatesAsync(AiModelConfig model)
    {
        const string sql = """
        SELECT
            m.code AS model_code, m.name AS model_name, m.status AS model_status,
            m.default_temperature, m.default_max_tokens, m.strategy, m.fallback_enabled, m.max_retry,
            m.config::text AS model_config_json,

            p.code AS partner_code, p.name AS partner_name, p.status AS partner_status,
            p.adapter_code, p.base_url, p.weight AS partner_weight, p.priority AS partner_priority,
            p.quality_score, p.config::text AS partner_config_json,

            a.code AS account_code, a.name AS account_name, a.status AS account_status,
            a.account_ref, a.api_key_enc, a.api_key_ref,
            a.rpm_limit, a.rpd_limit, a.tpm_limit, a.tpd_limit,
            a.weight AS account_weight, a.priority AS account_priority,
            a.config::text AS account_config_json,

            r.route_code, r.status AS route_status, r.provider_model, r.timeout_ms,
            r.weight AS route_weight, r.priority AS route_priority,
            r.config::text AS route_config_json
        FROM ai_model_routes r
        JOIN ai_models m ON m.id = r.model_id
        JOIN ai_partners p ON p.id = r.partner_id
        JOIN ai_accounts a ON a.partner_id = p.id
        WHERE m.code = @ModelCode
          AND m.status = 'active'
          AND r.status = 'active'
          AND p.status = 'active'
          AND a.status = 'active'
        ORDER BY r.priority ASC, p.priority ASC, a.priority ASC;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<RouteCandidateRow>(sql, new { ModelCode = model.Code });
        return rows.Select(MapCandidate).ToList();
    }

    private static AiClientConfig MapClient(ClientRow row) => new()
    {
        Code = row.Code,
        Name = row.Name,
        Status = row.Status,
        ApiKeyHash = row.ApiKeyHash,
        RpmLimit = row.RpmLimit,
        RpdLimit = row.RpdLimit,
        AllowedModels = Deserialize<List<string>>(row.AllowedModelsJson) ?? [],
        Config = Deserialize<Dictionary<string, object?>>(row.ConfigJson) ?? new()
    };

    private static AiModelConfig MapModel(ModelRow row) => new()
    {
        Code = row.Code,
        Name = row.Name,
        Status = row.Status,
        DefaultTemperature = row.DefaultTemperature,
        DefaultMaxTokens = row.DefaultMaxTokens,
        Strategy = row.Strategy,
        FallbackEnabled = row.FallbackEnabled,
        MaxRetry = row.MaxRetry,
        Config = Deserialize<Dictionary<string, object?>>(row.ConfigJson) ?? new()
    };

    private static AiPartnerConfig MapPartner(PartnerRow row) => new()
    {
        Code = row.Code,
        Name = row.Name,
        Status = row.Status,
        AdapterCode = row.AdapterCode,
        BaseUrl = row.BaseUrl,
        Weight = row.Weight,
        Priority = row.Priority,
        QualityScore = row.QualityScore,
        Config = Deserialize<Dictionary<string, object?>>(row.ConfigJson) ?? new()
    };

    private static AiAccountConfig MapAccount(AccountRow row) => new()
    {
        Code = row.Code,
        PartnerCode = row.PartnerCode,
        Name = row.Name,
        Status = row.Status,
        AccountRef = row.AccountRef,
        ApiKeyEnc = row.ApiKeyEnc,
        ApiKeyRef = row.ApiKeyRef,
        RpmLimit = row.RpmLimit,
        RpdLimit = row.RpdLimit,
        TpmLimit = row.TpmLimit,
        TpdLimit = row.TpdLimit,
        Weight = row.Weight,
        Priority = row.Priority,
        Config = Deserialize<Dictionary<string, object?>>(row.ConfigJson) ?? new()
    };

    private static AiModelRouteConfig MapRoute(RouteRow row) => new()
    {
        ModelCode = row.ModelCode,
        PartnerCode = row.PartnerCode,
        RouteCode = row.RouteCode,
        Status = row.Status,
        ProviderModel = row.ProviderModel,
        TimeoutMs = row.TimeoutMs,
        Weight = row.Weight,
        Priority = row.Priority,
        Config = Deserialize<Dictionary<string, object?>>(row.ConfigJson) ?? new()
    };

    private static AiRouteCandidate MapCandidate(RouteCandidateRow row) => new()
    {
        Model = new AiModelConfig
        {
            Code = row.ModelCode,
            Name = row.ModelName,
            Status = row.ModelStatus,
            DefaultTemperature = row.DefaultTemperature,
            DefaultMaxTokens = row.DefaultMaxTokens,
            Strategy = row.Strategy,
            FallbackEnabled = row.FallbackEnabled,
            MaxRetry = row.MaxRetry,
            Config = Deserialize<Dictionary<string, object?>>(row.ModelConfigJson) ?? new()
        },
        Partner = new AiPartnerConfig
        {
            Code = row.PartnerCode,
            Name = row.PartnerName,
            Status = row.PartnerStatus,
            AdapterCode = row.AdapterCode,
            BaseUrl = row.BaseUrl,
            Weight = row.PartnerWeight,
            Priority = row.PartnerPriority,
            QualityScore = row.QualityScore,
            Config = Deserialize<Dictionary<string, object?>>(row.PartnerConfigJson) ?? new()
        },
        Account = new AiAccountConfig
        {
            Code = row.AccountCode,
            PartnerCode = row.PartnerCode,
            Name = row.AccountName,
            Status = row.AccountStatus,
            AccountRef = row.AccountRef,
            ApiKeyEnc = row.ApiKeyEnc,
            ApiKeyRef = row.ApiKeyRef,
            RpmLimit = row.RpmLimit,
            RpdLimit = row.RpdLimit,
            TpmLimit = row.TpmLimit,
            TpdLimit = row.TpdLimit,
            Weight = row.AccountWeight,
            Priority = row.AccountPriority,
            Config = Deserialize<Dictionary<string, object?>>(row.AccountConfigJson) ?? new()
        },
        Route = new AiModelRouteConfig
        {
            ModelCode = row.ModelCode,
            PartnerCode = row.PartnerCode,
            RouteCode = row.RouteCode,
            Status = row.RouteStatus,
            ProviderModel = row.ProviderModel,
            TimeoutMs = row.TimeoutMs,
            Weight = row.RouteWeight,
            Priority = row.RoutePriority,
            Config = Deserialize<Dictionary<string, object?>>(row.RouteConfigJson) ?? new()
        }
    };

    private static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private sealed record ClientRow(
        string Code,
        string Name,
        string Status,
        string? ApiKeyHash,
        int? RpmLimit,
        int? RpdLimit,
        string AllowedModelsJson,
        string ConfigJson);

    private sealed record ModelRow(
        string Code,
        string Name,
        string Status,
        decimal DefaultTemperature,
        int DefaultMaxTokens,
        string Strategy,
        bool FallbackEnabled,
        int MaxRetry,
        string ConfigJson);

    private sealed record PartnerRow(
        string Code,
        string Name,
        string Status,
        string AdapterCode,
        string BaseUrl,
        int Weight,
        int Priority,
        int QualityScore,
        string ConfigJson);

    private sealed record AccountRow(
        string Code,
        string PartnerCode,
        string? Name,
        string Status,
        string? AccountRef,
        string? ApiKeyEnc,
        string? ApiKeyRef,
        int? RpmLimit,
        int? RpdLimit,
        int? TpmLimit,
        int? TpdLimit,
        int Weight,
        int Priority,
        string ConfigJson);

    private sealed record RouteRow(
        string ModelCode,
        string PartnerCode,
        string RouteCode,
        string Status,
        string ProviderModel,
        int TimeoutMs,
        int Weight,
        int Priority,
        string ConfigJson);

    private sealed record RouteCandidateRow(
        string ModelCode,
        string ModelName,
        string ModelStatus,
        decimal DefaultTemperature,
        int DefaultMaxTokens,
        string Strategy,
        bool FallbackEnabled,
        int MaxRetry,
        string ModelConfigJson,
        string PartnerCode,
        string PartnerName,
        string PartnerStatus,
        string AdapterCode,
        string BaseUrl,
        int PartnerWeight,
        int PartnerPriority,
        int QualityScore,
        string PartnerConfigJson,
        string AccountCode,
        string? AccountName,
        string AccountStatus,
        string? AccountRef,
        string? ApiKeyEnc,
        string? ApiKeyRef,
        int? RpmLimit,
        int? RpdLimit,
        int? TpmLimit,
        int? TpdLimit,
        int AccountWeight,
        int AccountPriority,
        string AccountConfigJson,
        string RouteCode,
        string RouteStatus,
        string ProviderModel,
        int TimeoutMs,
        int RouteWeight,
        int RoutePriority,
        string RouteConfigJson);
}
