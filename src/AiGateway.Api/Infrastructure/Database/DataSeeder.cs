using AiGateway.Api.Infrastructure.Security;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

/// <summary>
/// Chạy tự động khi app khởi động.
/// Kiểm tra từng record, chỉ insert nếu chưa tồn tại (idempotent).
///
/// API key đọc từ environment variable theo pattern:
///   AI_ACCOUNT_KEY__<account_code_uppercase>
///
/// Ví dụ:
///   AI_ACCOUNT_KEY__GEMINI_ACC_01=AIzaSy...
///   AI_ACCOUNT_KEY__GROQ_ACC_01=gsk_...
///
/// Nếu env var chưa set → account bị skip, log warning.
/// Nếu account đã tồn tại trong DB → không ghi đè.
/// </summary>
public sealed class DataSeeder
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ISecretProtector _secretProtector;
    private readonly ILogger<DataSeeder> _logger;

    public DataSeeder(
        NpgsqlDataSource dataSource,
        ISecretProtector secretProtector,
        ILogger<DataSeeder> logger)
    {
        _dataSource = dataSource;
        _secretProtector = secretProtector;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("DataSeeder: starting...");

        await SeedModelsAsync();
        await SeedPartnersAsync();
        await SeedAccountsAsync();
        await SeedRoutesAsync();

        _logger.LogInformation("DataSeeder: done.");
    }

    // ─────────────────────────────────────────────────────────
    // MODELS
    // ─────────────────────────────────────────────────────────

    private async Task SeedModelsAsync()
    {
        var models = new[]
        {
            new
            {
                Code = "text-fast",
                Name = "Fast Text Model",
                Status = "active",
                DefaultTemperature = 0.7m,
                DefaultMaxTokens = 1000,
                Strategy = "balanced",
                FallbackEnabled = true,
                MaxRetry = 3
            },
            new
            {
                Code = "text-pro",
                Name = "Pro Text Model",
                Status = "active",
                DefaultTemperature = 0.7m,
                DefaultMaxTokens = 2000,
                Strategy = "balanced",
                FallbackEnabled = true,
                MaxRetry = 2
            }
        };

        const string sql = """
            INSERT INTO ai_models (code, name, status, default_temperature, default_max_tokens, strategy, fallback_enabled, max_retry)
            VALUES (@Code, @Name, @Status, @DefaultTemperature, @DefaultMaxTokens, @Strategy, @FallbackEnabled, @MaxRetry)
            ON CONFLICT (code) DO NOTHING;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        foreach (var model in models)
        {
            var inserted = await conn.ExecuteAsync(sql, model);
            if (inserted > 0)
                _logger.LogInformation("DataSeeder: inserted model '{Code}'", model.Code);
        }
    }

    // ─────────────────────────────────────────────────────────
    // PARTNERS
    // ─────────────────────────────────────────────────────────

    private async Task SeedPartnersAsync()
    {
        var partners = new[]
        {
            new { Code = "gemini",      Name = "Google Gemini",   AdapterCode = "gemini",            BaseUrl = "https://generativelanguage.googleapis.com", Weight = 100, Priority = 1, QualityScore = 90 },
            new { Code = "groq",        Name = "Groq",            AdapterCode = "openai_compatible", BaseUrl = "https://api.groq.com/openai",               Weight = 90,  Priority = 2, QualityScore = 80 },
            new { Code = "mistral",     Name = "Mistral AI",      AdapterCode = "openai_compatible", BaseUrl = "https://api.mistral.ai",                    Weight = 80,  Priority = 3, QualityScore = 82 },
            new { Code = "openrouter",  Name = "OpenRouter",      AdapterCode = "openrouter",        BaseUrl = "https://openrouter.ai/api",                 Weight = 70,  Priority = 4, QualityScore = 75 },
            new { Code = "fireworks",   Name = "Fireworks AI",    AdapterCode = "openai_compatible", BaseUrl = "https://api.fireworks.ai/inference",        Weight = 75,  Priority = 5, QualityScore = 78 },
            new { Code = "huggingface", Name = "Hugging Face",    AdapterCode = "openai_compatible", BaseUrl = "https://router.huggingface.co",             Weight = 60,  Priority = 6, QualityScore = 72 },
        };

        const string sql = """
            INSERT INTO ai_partners (code, name, status, adapter_code, base_url, weight, priority, quality_score)
            VALUES (@Code, @Name, 'active', @AdapterCode, @BaseUrl, @Weight, @Priority, @QualityScore)
            ON CONFLICT (code) DO NOTHING;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        foreach (var partner in partners)
        {
            var inserted = await conn.ExecuteAsync(sql, partner);
            if (inserted > 0)
                _logger.LogInformation("DataSeeder: inserted partner '{Code}'", partner.Code);
        }
    }

    // ─────────────────────────────────────────────────────────
    // ACCOUNTS
    // Đọc API key từ env var: AI_ACCOUNT_KEY__<CODE_UPPERCASE>
    // Nếu env var chưa set → skip, không tạo account.
    // Nếu account đã có trong DB → không ghi đè (để tránh mất key đang dùng).
    // ─────────────────────────────────────────────────────────

    private async Task SeedAccountsAsync()
    {
        var accounts = new[]
        {
            new AccountSeed
            {
                PartnerCode = "gemini",
                Code        = "gemini_acc_01",
                Name        = "Gemini Free Account",
                RpmLimit    = 15,
                RpdLimit    = 1500,
                TpmLimit    = 1_000_000,
                TpdLimit    = null,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "gemini",
                Code        = "gemini_acc_02",
                Name        = "Gemini Free Account",
                RpmLimit    = 15,
                RpdLimit    = 1500,
                TpmLimit    = 1_000_000,
                TpdLimit    = null,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "groq",
                Code        = "groq_acc_01",
                Name        = "Groq Free Account",
                RpmLimit    = 30,
                RpdLimit    = 14_400,
                TpmLimit    = 6_000,
                TpdLimit    = 500_000,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "mistral",
                Code        = "mistral_acc_01",
                Name        = "Mistral Experiment Account",
                RpmLimit    = 5,
                RpdLimit    = null,
                TpmLimit    = null,
                TpdLimit    = null,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "openrouter",
                Code        = "openrouter_acc_01",
                Name        = "OpenRouter Free Account",
                RpmLimit    = 20,
                RpdLimit    = 50,
                TpmLimit    = null,
                TpdLimit    = null,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "fireworks",
                Code        = "fireworks_acc_01",
                Name        = "Fireworks Credit Account",
                RpmLimit    = 10,
                RpdLimit    = null,
                TpmLimit    = null,
                TpdLimit    = null,
                Weight      = 100
            },
            new AccountSeed
            {
                PartnerCode = "huggingface",
                Code        = "hf_acc_01",
                Name        = "HuggingFace Free Account",
                RpmLimit    = null,
                RpdLimit    = null,
                TpmLimit    = null,
                TpdLimit    = null,
                Weight      = 100
            },
        };

        // Kiểm tra account đã tồn tại hay chưa để không ghi đè key đang dùng.
        const string existsSql = "SELECT COUNT(1) FROM ai_accounts WHERE code = @Code;";

        const string insertSql = """
            INSERT INTO ai_accounts (
                partner_id, code, name, status, api_key_enc,
                rpm_limit, rpd_limit, tpm_limit, tpd_limit, weight, priority)
            SELECT
                p.id, @Code, @Name, 'active', @ApiKeyEnc,
                @RpmLimit, @RpdLimit, @TpmLimit, @TpdLimit, @Weight, 100
            FROM ai_partners p
            WHERE p.code = @PartnerCode;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();

        foreach (var account in accounts)
        {
            // Bỏ qua nếu account đã có trong DB
            var exists = await conn.ExecuteScalarAsync<int>(existsSql, new { account.Code });
            if (exists > 0)
            {
                _logger.LogDebug("DataSeeder: account '{Code}' already exists, skipping.", account.Code);
                continue;
            }

            // Đọc API key từ env var
            var envKey = $"AI_ACCOUNT_KEY__{account.Code.ToUpperInvariant().Replace('-', '_')}";
            var rawApiKey = Environment.GetEnvironmentVariable(envKey);

            if (string.IsNullOrWhiteSpace(rawApiKey))
            {
                _logger.LogWarning(
                    "DataSeeder: env var '{EnvKey}' not set — skipping account '{Code}'. " +
                    "Set the env var and restart to create this account.",
                    envKey, account.Code);
                continue;
            }

            string? encryptedKey;
            try
            {
                encryptedKey = _secretProtector.Protect(rawApiKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataSeeder: failed to encrypt key for account '{Code}', skipping.", account.Code);
                continue;
            }

            var inserted = await conn.ExecuteAsync(insertSql, new
            {
                account.PartnerCode,
                account.Code,
                account.Name,
                ApiKeyEnc = encryptedKey,
                account.RpmLimit,
                account.RpdLimit,
                account.TpmLimit,
                account.TpdLimit,
                account.Weight
            });

            if (inserted > 0)
                _logger.LogInformation("DataSeeder: inserted account '{Code}' for partner '{PartnerCode}'.",
                    account.Code, account.PartnerCode);
            else
                _logger.LogWarning("DataSeeder: insert account '{Code}' affected 0 rows — partner '{PartnerCode}' may not exist yet.",
                    account.Code, account.PartnerCode);
        }
    }

    // ─────────────────────────────────────────────────────────
    // ROUTES
    // ─────────────────────────────────────────────────────────

    private async Task SeedRoutesAsync()
    {
        var routes = new[]
        {
            // ── text-fast ──────────────────────────────────────────────────────────
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "gemini",      RouteCode = "flash",       ProviderModel = "gemini-2.5-flash",                                   TimeoutMs = 30_000, Weight = 100, Priority = 1 },
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "groq",        RouteCode = "llama-fast",  ProviderModel = "llama-3.1-8b-instant",                               TimeoutMs = 20_000, Weight = 80,  Priority = 2 },
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "mistral",     RouteCode = "small",       ProviderModel = "mistral-small-latest",                               TimeoutMs = 30_000, Weight = 70,  Priority = 3 },
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "openrouter",  RouteCode = "llama-free",  ProviderModel = "meta-llama/llama-3.2-3b-instruct:free",              TimeoutMs = 30_000, Weight = 60,  Priority = 4 },
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "fireworks",   RouteCode = "llama-8b",    ProviderModel = "accounts/fireworks/models/llama-v3p1-8b-instruct",   TimeoutMs = 25_000, Weight = 65,  Priority = 5 },
            new RouteSeed { ModelCode = "text-fast", PartnerCode = "huggingface", RouteCode = "mistral-7b",  ProviderModel = "mistralai/Mistral-7B-Instruct-v0.3",                 TimeoutMs = 60_000, Weight = 50,  Priority = 6 },

            // ── text-pro ───────────────────────────────────────────────────────────
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "gemini",      RouteCode = "pro",         ProviderModel = "gemini-2.5-pro",                                     TimeoutMs = 60_000,  Weight = 100, Priority = 1 },
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "groq",        RouteCode = "llama-70b",   ProviderModel = "llama-3.3-70b-versatile",                            TimeoutMs = 30_000,  Weight = 80,  Priority = 2 },
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "mistral",     RouteCode = "large",       ProviderModel = "mistral-large-latest",                               TimeoutMs = 45_000,  Weight = 70,  Priority = 3 },
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "openrouter",  RouteCode = "gemma-free",  ProviderModel = "google/gemma-3-27b-it:free",                         TimeoutMs = 45_000,  Weight = 60,  Priority = 4 },
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "fireworks",   RouteCode = "llama-70b",   ProviderModel = "accounts/fireworks/models/llama-v3p3-70b-instruct",  TimeoutMs = 40_000,  Weight = 65,  Priority = 5 },
            new RouteSeed { ModelCode = "text-pro",  PartnerCode = "huggingface", RouteCode = "llama-70b",   ProviderModel = "meta-llama/Llama-3.3-70B-Instruct",                  TimeoutMs = 120_000, Weight = 50,  Priority = 6 },
        };

        const string sql = """
            INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
            SELECT m.id, p.id, @RouteCode, 'active', @ProviderModel, @TimeoutMs, @Weight, @Priority
            FROM ai_models m
            CROSS JOIN ai_partners p
            WHERE m.code = @ModelCode AND p.code = @PartnerCode
            ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;
            """;

        await using var conn = await _dataSource.OpenConnectionAsync();
        foreach (var route in routes)
        {
            var inserted = await conn.ExecuteAsync(sql, route);
            if (inserted > 0)
                _logger.LogInformation("DataSeeder: inserted route '{ModelCode}' -> '{PartnerCode}' [{RouteCode}]",
                    route.ModelCode, route.PartnerCode, route.RouteCode);
        }
    }

    // ─────────────────────────────────────────────────────────
    // Internal seed record types
    // ─────────────────────────────────────────────────────────

    private sealed record AccountSeed
    {
        public required string PartnerCode { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public int? RpmLimit { get; init; }
        public int? RpdLimit { get; init; }
        public int? TpmLimit { get; init; }
        public int? TpdLimit { get; init; }
        public int Weight { get; init; } = 100;
    }

    private sealed record RouteSeed
    {
        public required string ModelCode { get; init; }
        public required string PartnerCode { get; init; }
        public required string RouteCode { get; init; }
        public required string ProviderModel { get; init; }
        public int TimeoutMs { get; init; }
        public int Weight { get; init; }
        public int Priority { get; init; }
    }
}
