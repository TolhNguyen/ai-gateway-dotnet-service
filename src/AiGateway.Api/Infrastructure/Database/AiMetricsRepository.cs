using AiGateway.Api.Contracts;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed record MetricSnapshot
{
    public required DateTimeOffset BucketHour { get; init; }
    public required string ClientCode { get; init; }
    public required string ModelCode { get; init; }
    public required string PartnerCode { get; init; }
    public required string AccountCode { get; init; }
    public long Total { get; init; }
    public long Success { get; init; }
    public long Failed { get; init; }
    public long FallbackSuccess { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens { get; init; }
    public long LatencyTotalMs { get; init; }
    public long LatencyCount { get; init; }
    public long ErrorRateLimit { get; init; }
    public long ErrorQuotaExceeded { get; init; }
    public long ErrorTimeout { get; init; }
    public long ErrorServerError { get; init; }
    public long ErrorAuthError { get; init; }
    public long ErrorPermissionError { get; init; }
    public long ErrorBadResponse { get; init; }
    public long ErrorUnknown { get; init; }
}

public sealed class AiMetricsRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public AiMetricsRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task UpsertHourlyAsync(MetricSnapshot metric)
    {
        const string sql = """
        INSERT INTO ai_request_metrics_hourly (
            bucket_hour, client_code, model_code, partner_code, account_code,
            total_count, success_count, failed_count, fallback_success_count,
            input_tokens, output_tokens, total_tokens,
            latency_total_ms, latency_count,
            error_rate_limit, error_quota_exceeded, error_timeout, error_server_error,
            error_auth_error, error_permission_error, error_bad_response, error_unknown,
            updated_at)
        VALUES (
            @BucketHour, @ClientCode, @ModelCode, @PartnerCode, @AccountCode,
            @Total, @Success, @Failed, @FallbackSuccess,
            @InputTokens, @OutputTokens, @TotalTokens,
            @LatencyTotalMs, @LatencyCount,
            @ErrorRateLimit, @ErrorQuotaExceeded, @ErrorTimeout, @ErrorServerError,
            @ErrorAuthError, @ErrorPermissionError, @ErrorBadResponse, @ErrorUnknown,
            NOW())
        ON CONFLICT (bucket_hour, client_code, model_code, partner_code, account_code)
        DO UPDATE SET
            total_count = EXCLUDED.total_count,
            success_count = EXCLUDED.success_count,
            failed_count = EXCLUDED.failed_count,
            fallback_success_count = EXCLUDED.fallback_success_count,
            input_tokens = EXCLUDED.input_tokens,
            output_tokens = EXCLUDED.output_tokens,
            total_tokens = EXCLUDED.total_tokens,
            latency_total_ms = EXCLUDED.latency_total_ms,
            latency_count = EXCLUDED.latency_count,
            error_rate_limit = EXCLUDED.error_rate_limit,
            error_quota_exceeded = EXCLUDED.error_quota_exceeded,
            error_timeout = EXCLUDED.error_timeout,
            error_server_error = EXCLUDED.error_server_error,
            error_auth_error = EXCLUDED.error_auth_error,
            error_permission_error = EXCLUDED.error_permission_error,
            error_bad_response = EXCLUDED.error_bad_response,
            error_unknown = EXCLUDED.error_unknown,
            updated_at = NOW();
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(sql, metric);
    }

    public async Task<DashboardOverviewDto> GetOverviewAsync(DateTimeOffset from, DateTimeOffset to)
    {
        const string sql = """
        SELECT
            COALESCE(SUM(total_count), 0) AS total,
            COALESCE(SUM(success_count), 0) AS success,
            COALESCE(SUM(failed_count), 0) AS failed,
            COALESCE(SUM(fallback_success_count), 0) AS fallback_success,
            COALESCE(SUM(input_tokens), 0) AS tokens_in,
            COALESCE(SUM(output_tokens), 0) AS tokens_out,
            COALESCE(SUM(total_tokens), 0) AS tokens_total,
            COALESCE(SUM(latency_total_ms), 0) AS latency_total_ms,
            COALESCE(SUM(latency_count), 0) AS latency_count
        FROM ai_request_metrics_hourly
        WHERE bucket_hour >= @From AND bucket_hour < @To;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var row = await connection.QuerySingleAsync<OverviewRow>(sql, new { From = from, To = to });
        return new DashboardOverviewDto
        {
            Total = row.Total,
            Success = row.Success,
            Failed = row.Failed,
            FallbackSuccess = row.FallbackSuccess,
            ErrorRate = row.Total == 0 ? 0 : Math.Round(row.Failed * 100m / row.Total, 2),
            AvgLatencyMs = row.LatencyCount == 0 ? 0 : Math.Round(row.LatencyTotalMs * 1m / row.LatencyCount, 2),
            TokensIn = row.TokensIn,
            TokensOut = row.TokensOut,
            TokensTotal = row.TokensTotal
        };
    }

    public async Task<IReadOnlyList<DashboardGroupMetricDto>> GetGroupedAsync(
        string groupBy,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var column = groupBy switch
        {
            "client" => "client_code",
            "model" => "model_code",
            "partner" => "partner_code",
            "account" => "account_code",
            _ => throw new ArgumentOutOfRangeException(nameof(groupBy), groupBy, "Invalid group")
        };

        var sql = $"""
        SELECT
            {column} AS code,
            COALESCE(SUM(total_count), 0) AS total,
            COALESCE(SUM(success_count), 0) AS success,
            COALESCE(SUM(failed_count), 0) AS failed,
            COALESCE(SUM(latency_total_ms), 0) AS latency_total_ms,
            COALESCE(SUM(latency_count), 0) AS latency_count
        FROM ai_request_metrics_hourly
        WHERE bucket_hour >= @From AND bucket_hour < @To
        GROUP BY {column}
        ORDER BY total DESC;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<GroupRow>(sql, new { From = from, To = to });

        return rows.Select(row => new DashboardGroupMetricDto
        {
            Code = row.Code,
            Total = row.Total,
            Success = row.Success,
            Failed = row.Failed,
            ErrorRate = row.Total == 0 ? 0 : Math.Round(row.Failed * 100m / row.Total, 2),
            AvgLatencyMs = row.LatencyCount == 0 ? 0 : Math.Round(row.LatencyTotalMs * 1m / row.LatencyCount, 2)
        }).ToList();
    }

    private sealed record OverviewRow(
        long Total,
        long Success,
        long Failed,
        long FallbackSuccess,
        long TokensIn,
        long TokensOut,
        long TokensTotal,
        long LatencyTotalMs,
        long LatencyCount);

    private sealed record GroupRow(
        string Code,
        long Total,
        long Success,
        long Failed,
        long LatencyTotalMs,
        long LatencyCount);
}
