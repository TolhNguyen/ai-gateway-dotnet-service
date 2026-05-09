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
    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:Postgres");

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    return dataSourceBuilder.Build();
});

builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
{
    var connectionString = builder.Configuration["Redis:ConnectionString"]
        ?? throw new InvalidOperationException("Missing Redis:ConnectionString");

    return ConnectionMultiplexer.Connect(connectionString);
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
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

app.UseMiddleware<AdminAuthMiddleware>();
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/dashboard/index.html"));

app.Run();
