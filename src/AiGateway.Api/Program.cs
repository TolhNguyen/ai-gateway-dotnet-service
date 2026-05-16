using System.Text.Json;
using System.Text.Json.Serialization;
using AiGateway.Api.Application;
using AiGateway.Api.Infrastructure.Database;
using AiGateway.Api.Infrastructure.Partners;
using AiGateway.Api.Infrastructure.Redis;
using AiGateway.Api.Infrastructure.Security;
using AiGateway.Api.Options;
using AiGateway.Api.Workers;
using Dapper;
using Npgsql;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

DefaultTypeMap.MatchNamesWithUnderscores = true;

builder.Services
    .AddOptions<AiGatewayOptions>()
    .Bind(builder.Configuration.GetSection("AiGateway"))
    .Validate(options => !string.IsNullOrWhiteSpace(options.EncryptionKeyBase64), "AiGateway:EncryptionKeyBase64 is required")
    .Validate(options =>
    {
        try
        {
            return Convert.FromBase64String(options.EncryptionKeyBase64).Length == 32;
        }
        catch
        {
            return false;
        }
    }, "AiGateway:EncryptionKeyBase64 must decode to 32 bytes")
    .ValidateOnStart();

builder.Services
    .AddOptions<AdminAuthOptions>()
    .Bind(builder.Configuration.GetSection("AdminAuth"))
    .Validate(options =>
    {
        if (!options.Enabled) return true;
        if (options.AllowInDevelopmentWithoutKey) return true;
        return !string.IsNullOrWhiteSpace(options.ApiKeyHash);
    }, "AdminAuth:ApiKeyHash is required when AdminAuth is enabled")
    .ValidateOnStart();

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.WriteIndented = false;
    });

builder.Services.AddSingleton(_ =>
{
    var connectionString =
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING")
        ?? builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing PostgreSQL connection string");

    if (builder.Environment.IsProduction() &&
        (connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
         connectionString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            "Production is using local PostgreSQL. Set ConnectionStrings__Postgres in Render Environment.");
    }

    var pg = new NpgsqlConnectionStringBuilder(connectionString);
    Console.WriteLine($"Postgres config loaded. Host={pg.Host}, Database={pg.Database}, Username={pg.Username}");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    return dataSourceBuilder.Build();
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var redisConnectionString =
        builder.Configuration["Redis:ConnectionString"]
        ?? Environment.GetEnvironmentVariable("Redis__ConnectionString")
        ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING")
        ?? "localhost:6379";

    if (redisConnectionString.StartsWith("redis://", StringComparison.OrdinalIgnoreCase) ||
        redisConnectionString.StartsWith("rediss://", StringComparison.OrdinalIgnoreCase))
    {
        var uri = new Uri(redisConnectionString);

        var options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.Port } },
            Ssl = uri.Scheme.Equals("rediss", StringComparison.OrdinalIgnoreCase),
            AbortOnConnectFail = false
        };

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var parts = uri.UserInfo.Split(':', 2);

            if (parts.Length == 2)
            {
                options.User = Uri.UnescapeDataString(parts[0]);
                options.Password = Uri.UnescapeDataString(parts[1]);
            }
            else
            {
                options.Password = Uri.UnescapeDataString(parts[0]);
            }
        }

        return ConnectionMultiplexer.Connect(options);
    }

    var parsed = ConfigurationOptions.Parse(redisConnectionString);
    parsed.AbortOnConnectFail = false;

    return ConnectionMultiplexer.Connect(parsed);
});

builder.Services.AddHttpClient("ai-partners", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AiGateway/1.0");
});

builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
builder.Services.AddSingleton<ApiKeyHasher>();

builder.Services.AddScoped<AiConfigRepository>();
builder.Services.AddScoped<AiMetricsRepository>();
builder.Services.AddScoped<AiErrorRepository>();

builder.Services.AddScoped<RedisConfigCache>();
builder.Services.AddScoped<RedisRateLimitStore>();
builder.Services.AddScoped<RedisCooldownStore>();
builder.Services.AddScoped<RedisMetricBuffer>();

builder.Services.AddScoped<AiConfigService>();
builder.Services.AddScoped<ClientAuthService>();
builder.Services.AddScoped<AiRouteSelector>();
builder.Services.AddScoped<TokenEstimator>();
builder.Services.AddScoped<ErrorRecordingService>();
builder.Services.AddScoped<AiGatewayService>();
builder.Services.AddScoped<DataSeeder>();

builder.Services.AddScoped<IAiPartnerClient, OpenAiCompatibleClient>();
builder.Services.AddScoped<IAiPartnerClient, GeminiClient>();
builder.Services.AddScoped<IAiPartnerClient, OpenRouterClient>();
builder.Services.AddScoped<PartnerClientFactory>();

builder.Services.AddHostedService<MetricFlushWorker>();
builder.Services.AddHostedService<CleanupWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await RunMigrationsAsync(scope.ServiceProvider);
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.UseMiddleware<AdminAuthMiddleware>();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/dashboard/index.html"));

app.Run();
static async Task RunMigrationsAsync(IServiceProvider services)
{
    var logger = services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("MigrationRunner");

    var dataSource = services.GetRequiredService<NpgsqlDataSource>();

    var migrationDir = FindMigrationDirectory();

    if (migrationDir is null)
    {
        throw new InvalidOperationException("Migration directory not found. Expected a 'migrations' folder in the app directory.");
    }

    var files = Directory
        .GetFiles(migrationDir, "*.sql")
        .OrderBy(x => x)
        .ToArray();

    if (files.Length == 0)
    {
        throw new InvalidOperationException($"No SQL migration files found in: {migrationDir}");
    }

    await using var connection = await dataSource.OpenConnectionAsync();

    foreach (var file in files)
    {
        var sql = await File.ReadAllTextAsync(file);

        if (string.IsNullOrWhiteSpace(sql))
        {
            continue;
        }

        logger.LogInformation("Applying migration: {File}", Path.GetFileName(file));

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();

        logger.LogInformation("Applied migration: {File}", Path.GetFileName(file));
    }
}

static string? FindMigrationDirectory()
{
    var candidates = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "migrations"),
        Path.Combine(Directory.GetCurrentDirectory(), "migrations"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "migrations"),
        Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "migrations")
    };

    return candidates.FirstOrDefault(Directory.Exists);
}