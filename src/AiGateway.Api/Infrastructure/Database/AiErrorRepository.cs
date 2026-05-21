using AiGateway.Api.Contracts;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AiErrorRepository
{
    private readonly NpgsqlDataSource _ds;

    public AiErrorRepository(NpgsqlDataSource ds) { _ds = ds; }

    public async Task UpsertAggregateAsync(
        long userId, string modelCode, string partnerCode, string accountKeyCode,
        string errorType, string errorCode, int httpStatus, string? message,
        CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO ai_error_aggregates
            (user_id, model_code, partner_code, account_key_code,
             error_type, error_code, http_status, count,
             first_seen_at, last_seen_at, last_message)
        VALUES (@u, @m, @p, @k, @et, @ec, @hs, 1, NOW(), NOW(), @msg)
        ON CONFLICT (user_id, model_code, partner_code, account_key_code, error_type, error_code, http_status)
        DO UPDATE SET
            count        = ai_error_aggregates.count + 1,
            last_seen_at = NOW(),
            last_message = COALESCE(EXCLUDED.last_message, ai_error_aggregates.last_message),
            updated_at   = NOW()
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new {
            u = userId, m = modelCode, p = partnerCode, k = accountKeyCode,
            et = errorType, ec = errorCode ?? "none", hs = httpStatus,
            msg = message?.Length > 4000 ? message[..4000] : message
        });
    }

    public async Task InsertEventAsync(
        string? requestId, long userId, string modelCode, string partnerCode, string accountKeyCode,
        string errorType, string? errorCode, int? httpStatus, string? message, int? latencyMs,
        CancellationToken ct = default)
    {
        const string sql = """
        INSERT INTO ai_error_events
            (request_id, user_id, model_code, partner_code, account_key_code,
             error_type, error_code, http_status, message, latency_ms)
        VALUES (@r, @u, @m, @p, @k, @et, @ec, @hs, @msg, @ms)
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(sql, new {
            r = requestId, u = userId, m = modelCode, p = partnerCode, k = accountKeyCode,
            et = errorType, ec = errorCode, hs = httpStatus,
            msg = message?.Length > 4000 ? message[..4000] : message,
            ms = latencyMs
        });
    }

    public async Task<IReadOnlyList<DashboardErrorDto>> ListErrorsAsync(
        long userId, DateTimeOffset fromUtc, int limit, CancellationToken ct = default)
    {
        const string sql = """
        SELECT model_code AS ModelCode, partner_code AS PartnerCode, account_key_code AS AccountKeyCode,
               error_type AS ErrorType, error_code AS ErrorCode, http_status AS HttpStatus,
               count, first_seen_at AS FirstSeenAt, last_seen_at AS LastSeenAt, last_message AS LastMessage
        FROM ai_error_aggregates
        WHERE user_id = @u AND last_seen_at >= @f
        ORDER BY last_seen_at DESC
        LIMIT @lim
        """;
        await using var conn = await _ds.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<DashboardErrorDto>(sql, new { u = userId, f = fromUtc, lim = limit });
        return rows.ToList();
    }

    public async Task<int> DeleteOldEventsAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            "DELETE FROM ai_error_events WHERE created_at < @b", new { b = before });
    }
}
