using AiGateway.Api.Contracts;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AiMetricsRepository
{
    private readonly NpgsqlDataSource _ds;

    public AiMetricsRepository(NpgsqlDataSource ds) { _ds = ds; }

    public async Task UpsertHourlyAsync(
        DateTimeOffset bucketHour, long userId, string modelCode, string partnerCode, string accountKeyCode,
        long total, long success, long failed, long fallbackSuccess,
        long tokensIn, long tokensOut, long tokensTotal,
        long latencyTotalMs, long latencyCount,
        long errRateLimit, long errQuotaExceeded, long errTimeout, long errServerError,
        long errAuthError, long errPermissionError, long errBadResponse, long errUnknown,
        CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO ai_request_metrics_hourly (
            bucket_hour, user_id, model_code, partner_code, account_key_code,
            total_count, success_count, failed_count, fallback_success_count,
            input_tokens, output_tokens, total_tokens,
            latency_total_ms, latency_count,
            error_rate_limit, error_quota_exceeded, error_timeout, error_server_error,
            error_auth_error, error_permission_error, error_bad_response, error_unknown
        ) VALUES (
            @bucket, @userId, @model, @partner, @key,
            @total, @success, @failed, @fallback,
            @tIn, @tOut, @tTot,
            @latT, @latC,
            @eRate, @eQuota, @eTimeout, @eServer,
            @eAuth, @ePerm, @eBad, @eUnk
        )
        ON CONFLICT (bucket_hour, user_id, model_code, partner_code, account_key_code) DO UPDATE SET
            total_count             = ai_request_metrics_hourly.total_count             + EXCLUDED.total_count,
            success_count           = ai_request_metrics_hourly.success_count           + EXCLUDED.success_count,
            failed_count            = ai_request_metrics_hourly.failed_count            + EXCLUDED.failed_count,
            fallback_success_count  = ai_request_metrics_hourly.fallback_success_count  + EXCLUDED.fallback_success_count,
            input_tokens            = ai_request_metrics_hourly.input_tokens            + EXCLUDED.input_tokens,
            output_tokens           = ai_request_metrics_hourly.output_tokens           + EXCLUDED.output_tokens,
            total_tokens            = ai_request_metrics_hourly.total_tokens            + EXCLUDED.total_tokens,
            latency_total_ms        = ai_request_metrics_hourly.latency_total_ms        + EXCLUDED.latency_total_ms,
            latency_count           = ai_request_metrics_hourly.latency_count           + EXCLUDED.latency_count,
            error_rate_limit        = ai_request_metrics_hourly.error_rate_limit        + EXCLUDED.error_rate_limit,
            error_quota_exceeded    = ai_request_metrics_hourly.error_quota_exceeded    + EXCLUDED.error_quota_exceeded,
            error_timeout           = ai_request_metrics_hourly.error_timeout           + EXCLUDED.error_timeout,
            error_server_error      = ai_request_metrics_hourly.error_server_error      + EXCLUDED.error_server_error,
            error_auth_error        = ai_request_metrics_hourly.error_auth_error        + EXCLUDED.error_auth_error,
            error_permission_error  = ai_request_metrics_hourly.error_permission_error  + EXCLUDED.error_permission_error,
            error_bad_response      = ai_request_metrics_hourly.error_bad_response      + EXCLUDED.error_bad_response,
            error_unknown           = ai_request_metrics_hourly.error_unknown           + EXCLUDED.error_unknown,
            updated_at              = NOW();
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new {
            bucket = bucketHour, userId, model = modelCode, partner = partnerCode, key = accountKeyCode,
            total, success, failed, fallback = fallbackSuccess,
            tIn = tokensIn, tOut = tokensOut, tTot = tokensTotal,
            latT = latencyTotalMs, latC = latencyCount,
            eRate = errRateLimit, eQuota = errQuotaExceeded, eTimeout = errTimeout, eServer = errServerError,
            eAuth = errAuthError, ePerm = errPermissionError, eBad = errBadResponse, eUnk = errUnknown
        });
    }

    private sealed record OverviewRow(
        long Total, long Success, long Failed, long FallbackSuccess,
        long TokensIn, long TokensOut, long TokensTotal,
        long LatencyTotalMs, long LatencyCount);

    public async Task<DashboardOverviewDto> GetOverviewAsync(long userId, DateTimeOffset fromUtc, CancellationToken ct = default)
    {
        // Explicit ::bigint casts: SUM(bigint) returns numeric, which Npgsql binds as decimal.
        const string sql = """
        SELECT
            COALESCE(SUM(total_count), 0)::bigint            AS Total,
            COALESCE(SUM(success_count), 0)::bigint          AS Success,
            COALESCE(SUM(failed_count), 0)::bigint           AS Failed,
            COALESCE(SUM(fallback_success_count), 0)::bigint AS FallbackSuccess,
            COALESCE(SUM(input_tokens), 0)::bigint           AS TokensIn,
            COALESCE(SUM(output_tokens), 0)::bigint          AS TokensOut,
            COALESCE(SUM(total_tokens), 0)::bigint           AS TokensTotal,
            COALESCE(SUM(latency_total_ms), 0)::bigint       AS LatencyTotalMs,
            COALESCE(SUM(latency_count), 0)::bigint          AS LatencyCount
        FROM ai_request_metrics_hourly
        WHERE user_id = @u AND bucket_hour >= @f
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var row = await conn.QuerySingleAsync<OverviewRow>(sql, new { u = userId, f = fromUtc });

        return new DashboardOverviewDto
        {
            Total = row.Total,
            Success = row.Success,
            Failed = row.Failed,
            FallbackSuccess = row.FallbackSuccess,
            ErrorRate = row.Total > 0 ? Math.Round((decimal)row.Failed * 100 / row.Total, 2) : 0,
            AvgLatencyMs = row.LatencyCount > 0 ? Math.Round((decimal)row.LatencyTotalMs / row.LatencyCount, 2) : 0,
            TokensIn = row.TokensIn,
            TokensOut = row.TokensOut,
            TokensTotal = row.TokensTotal
        };
    }

    private sealed record GroupRow(string Code, long Total, long Success, long Failed, long LatencyTotalMs, long LatencyCount);

    public async Task<IReadOnlyList<DashboardGroupMetricDto>> GetGroupedAsync(
        long userId, string column, DateTimeOffset fromUtc, CancellationToken ct = default)
    {
        var col = column switch
        {
            "model" => "model_code",
            "partner" => "partner_code",
            "account_key" => "account_key_code",
            _ => "model_code"
        };

        var sql = $"""
        SELECT {col} AS Code,
               COALESCE(SUM(total_count), 0)::bigint       AS Total,
               COALESCE(SUM(success_count), 0)::bigint     AS Success,
               COALESCE(SUM(failed_count), 0)::bigint      AS Failed,
               COALESCE(SUM(latency_total_ms), 0)::bigint  AS LatencyTotalMs,
               COALESCE(SUM(latency_count), 0)::bigint     AS LatencyCount
        FROM ai_request_metrics_hourly
        WHERE user_id = @u AND bucket_hour >= @f
        GROUP BY {col}
        ORDER BY Total DESC
        """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<GroupRow>(sql, new { u = userId, f = fromUtc });

        return rows.Select(r => new DashboardGroupMetricDto
        {
            Code = r.Code,
            Total = r.Total,
            Success = r.Success,
            Failed = r.Failed,
            ErrorRate = r.Total > 0 ? Math.Round((decimal)r.Failed * 100 / r.Total, 2) : 0,
            AvgLatencyMs = r.LatencyCount > 0 ? Math.Round((decimal)r.LatencyTotalMs / r.LatencyCount, 2) : 0
        }).ToList();
    }
}
