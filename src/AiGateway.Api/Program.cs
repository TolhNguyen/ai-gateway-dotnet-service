using System.Security.Cryptography;
using System.Text.Json.Serialization;
using AiGateway.Api.Application.AI;
using AiGateway.Api.Application.Auth;
using AiGateway.Api.Application.Config;
using AiGateway.Api.Application.HealthCheck;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Http;
using AiGateway.Api.Infrastructure.Partners;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using AiGateway.Api.Workers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ─── Bootstrap secrets ───────────────────────────────────────────────────
//   Self-generates AES key and JWT signing key on first run and persists
//   them under PGDATA so they survive container restarts.
BootstrapSecrets(builder);

// ─── Options ────────────────────────────────────────────────────────────
builder.Services
    .AddOptions<AiGatewayOptions>()
    .Bind(builder.Configuration.GetSection("AiGateway"))
    .ValidateDataAnnotations().ValidateOnStart();

builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection("Jwt"))
    .ValidateDataAnnotations().ValidateOnStart();

builder.Services
    .AddOptions<OpenRouterOptions>()
    .Bind(builder.Configuration.GetSection("OpenRouter"));

// ─── Logging ────────────────────────────────────────────────────────────
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ";
});

// ─── Kestrel limits ────────────────────────────────────────────────────
var gwOpts = builder.Configuration.GetSection("AiGateway").Get<AiGatewayOptions>() ?? new();
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = gwOpts.MaxRequestBodyBytes;
});

// ─── Data sources ──────────────────────────────────────────────────────
var pgConn = builder.Configuration.GetConnectionString("Postgres")
             ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres");
var dsBuilder = new NpgsqlDataSourceBuilder(pgConn);
builder.Services.AddSingleton(_ => dsBuilder.Build());

var redisConn = builder.Configuration["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Missing Redis:ConnectionString");
var muxConfig = ConfigurationOptions.Parse(redisConn);
muxConfig.AbortOnConnectFail = false;
muxConfig.ConnectRetry = 5;
muxConfig.ConnectTimeout = 5000;
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(muxConfig));

// ─── Http clients ──────────────────────────────────────────────────────
builder.Services.AddHttpClient("gemini");
builder.Services.AddHttpClient("openai_compatible");
builder.Services.AddHttpClient("openrouter");

// ─── Security ─────────────────────────────────────────────────────────
builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<TokenHasher>();
builder.Services.AddSingleton<JwtService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ─── Repositories ─────────────────────────────────────────────────────
builder.Services.AddScoped<MigrationRunner>();
builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<AccountKeyRepository>();
builder.Services.AddScoped<AiConfigRepository>();
builder.Services.AddScoped<AiMetricsRepository>();
builder.Services.AddScoped<AiErrorRepository>();

// ─── Redis layer ──────────────────────────────────────────────────────
builder.Services.AddSingleton<RedisRateLimitStore>();
builder.Services.AddSingleton<RedisCooldownStore>();
builder.Services.AddSingleton<RedisMetricBuffer>();
builder.Services.AddSingleton<RedisConfigCache>();

// ─── Partner clients ──────────────────────────────────────────────────
builder.Services.AddSingleton<IAiPartnerClient, GeminiClient>();
builder.Services.AddSingleton<IAiPartnerClient, OpenAiCompatibleClient>();
builder.Services.AddSingleton<IAiPartnerClient, OpenRouterClient>();
builder.Services.AddSingleton<PartnerClientFactory>();

// ─── Application services ─────────────────────────────────────────────
builder.Services.AddSingleton<TokenEstimator>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AccountKeyService>();
builder.Services.AddScoped<AiConfigService>();
builder.Services.AddScoped<AiRouteSelector>();
builder.Services.AddScoped<ErrorRecordingService>();
builder.Services.AddScoped<AiGatewayService>();
builder.Services.AddScoped<ApiKeyHealthCheckService>();

// ─── Authentication: JWT + PAT side-by-side, dispatched by token prefix ─
const string PolicyScheme = "JwtOrPat";

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = PolicyScheme;
        o.DefaultChallengeScheme = PolicyScheme;
    })
    .AddPolicyScheme(PolicyScheme, "JWT or PAT", options =>
    {
        options.ForwardDefaultSelector = ctx =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(auth) &&
                auth.StartsWith("Bearer aigw_", StringComparison.OrdinalIgnoreCase))
            {
                return PatAuthenticationHandler.SchemeName;
            }
            return JwtBearerDefaults.AuthenticationScheme;
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, jwt =>
    {
        // Configured via PostConfigure once JwtService is built so we don't need a 2-pass DI.
    })
    .AddScheme<PatAuthenticationOptions, PatAuthenticationHandler>(PatAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<JwtBearerOptions>>(sp =>
    new ConfigureJwtBearer(sp.GetRequiredService<JwtService>()));

builder.Services.AddAuthorization();

// ─── MVC + JSON ───────────────────────────────────────────────────────
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// ─── CORS (open by default — restrict via reverse proxy) ──────────────
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// ─── Hosted services ──────────────────────────────────────────────────
builder.Services.AddHostedService<MetricFlushWorker>();
builder.Services.AddHostedService<CleanupWorker>();
builder.Services.AddHostedService<HealthCheckWorker>();

var app = builder.Build();

// ─── Run migrations + bootstrap admin BEFORE serving requests ─────────
await using (var scope = app.Services.CreateAsyncScope())
{
    var runner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();
    var migrationsDir = Path.Combine(AppContext.BaseDirectory, "migrations");
    await runner.RunAsync(migrationsDir);

    await EnsureBootstrapAdminAsync(scope.ServiceProvider);
}

// ─── Middleware pipeline ──────────────────────────────────────────────
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseCors();

app.UseMiddleware<CorrelationIdMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// =============================================================
// Helpers
// =============================================================

static void BootstrapSecrets(WebApplicationBuilder builder)
{
    // Persisted under /var/lib/postgresql/data so they survive container restarts
    // (single mounted volume already exists for the bundled Postgres).
    var pgDataDir = Environment.GetEnvironmentVariable("PGDATA")
                    ?? Path.Combine("/var/lib/postgresql", "data");

    var aesPath = Path.Combine(pgDataDir, ".aigateway.aeskey");
    var jwtPath = Path.Combine(pgDataDir, ".aigateway.jwtkey");

    var aesKey = builder.Configuration["AiGateway:EncryptionKeyBase64"];
    if (string.IsNullOrWhiteSpace(aesKey))
    {
        aesKey = ReadOrCreateKey(aesPath, 32);
        builder.Configuration["AiGateway:EncryptionKeyBase64"] = aesKey;
    }

    var jwtKey = builder.Configuration["Jwt:SecretBase64"];
    if (string.IsNullOrWhiteSpace(jwtKey))
    {
        jwtKey = ReadOrCreateKey(jwtPath, 32);
        builder.Configuration["Jwt:SecretBase64"] = jwtKey;
    }
}

static string ReadOrCreateKey(string path, int byteLen)
{
    try
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (!string.IsNullOrEmpty(existing)) return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var raw = RandomNumberGenerator.GetBytes(byteLen);
        var b64 = Convert.ToBase64String(raw);

        File.WriteAllText(path, b64);
        try
        {
            File.SetUnixFileMode(path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch { /* not on unix or permission issue — non-fatal */ }
        return b64;
    }
    catch (UnauthorizedAccessException)
    {
        // Fallback: ephemeral key. Issues a warning on first auth attempt.
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteLen));
    }
}

static async Task EnsureBootstrapAdminAsync(IServiceProvider sp)
{
    var users = sp.GetRequiredService<UserRepository>();
    var hasher = sp.GetRequiredService<IPasswordHasher>();
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiGatewayOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<Program>>();

    var count = await users.CountAsync();
    if (count > 0)
    {
        logger.LogInformation("Users already exist — skipping bootstrap admin");
        return;
    }

    if (string.IsNullOrWhiteSpace(opts.BootstrapAdminEmail) ||
        string.IsNullOrWhiteSpace(opts.BootstrapAdminPassword))
    {
        logger.LogWarning("BootstrapAdminEmail/Password not set — no initial admin will be created.");
        return;
    }

    var hash = hasher.Hash(opts.BootstrapAdminPassword);
    var admin = await users.CreateAsync(opts.BootstrapAdminEmail, hash, role: "admin", displayName: "Administrator");
    logger.LogWarning(
        "Created bootstrap admin account: {Email} (id={Id}). CHANGE THE PASSWORD NOW.",
        admin.Email, admin.Id);
}

// =============================================================
// Late JWT config binder
// =============================================================
internal sealed class ConfigureJwtBearer : Microsoft.Extensions.Options.IPostConfigureOptions<JwtBearerOptions>
{
    private readonly JwtService _jwt;
    public ConfigureJwtBearer(JwtService jwt) { _jwt = jwt; }

    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        options.TokenValidationParameters = _jwt.ValidationParameters;
        options.RequireHttpsMetadata = false;  // single-container deployment behind a reverse proxy.
        options.MapInboundClaims = false;
    }
}

public partial class Program { }
