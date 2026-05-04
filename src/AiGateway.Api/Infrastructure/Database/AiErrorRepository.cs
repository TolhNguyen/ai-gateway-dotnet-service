using AiGateway.Api.Contracts;
using Dapper;
using Npgsql;

namespace AiGateway.Api.Infrastructure.Database;

public sealed class AiErrorRepository
{
    private readonly NpgsqlDataSource _dataSource;

    public AiErrorRepository(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task RecordErrorAsync(
        string? requestId,
        string clientCode,
        string modelCode,
        string partnerCode,
        string accountCode,
        string errorType,
        string? errorCode,
        int? httpStatus,
        string? message,
        int latencyMs,
        object? metadata,
        bool writeEvent,
        CancellationToken cancellationToken)
    {
        const string aggregateSql = """
        INSERT INTO ai_error_aggregates (
            client_code, model_code, partner_code, account_code,
            error_type, error_code, http_status,
            count, first_seen_at, last_seen_at, last_message,
            updated_at)
        VALUES (
            @ClientCode, @ModelCode, @PartnerCode, @AccountCode,
            @ErrorType, @ErrorCode, @HttpStatus,
            1, NOW(), NOW(), @Message,
            NOW())
        ON CONFLICT (client_code, model_code, partner_code, account_code, error_type, error_code, http_status)
        DO UPDATE SET
            count = ai_error_aggregates.count + 1,
            last_seen_at = NOW(),
            last_message = EXCLUDED.last_message,
            updated_at = NOW();
        """;

        const string eventSql = """
        INSERT INTO ai_error_events (
            request_id, client_code, model_code, partner_code, account_code,
            error_type, error_code, http_status, message, latency_ms, metadata)
        VALUES (
            @RequestId, @ClientCode, @ModelCode, @PartnerCode, @AccountCode,
            @ErrorType, @ErrorCode, @HttpStatus, @Message, @LatencyMs, CAST(@MetadataJson AS jsonb));
        """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var tx = await connection.BeginTransactionAsync(cancellationToken);

        var args = new
        {
            RequestId = requestId,
            ClientCode = clientCode,
            ModelCode = modelCode,
            PartnerCode = partnerCode,
            AccountCode = accountCode,
            ErrorType = errorType,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "none" : errorCode,
            HttpStatus = httpStatus ?? 0,
            Message = Trim(message, 1000),
            LatencyMs = latencyMs,
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata ?? new { })
        };

        await connection.ExecuteAsync(aggregateSql, args, tx);

        if (writeEvent)
        {
            await connection.ExecuteAsync(eventSql, args, tx);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DashboardErrorDto>> GetErrorsAsync(int limit)
    {
        const string sql = """
        SELECT client_code, model_code, partner_code, account_code,
               error_type, error_code, http_status, count,
               first_seen_at, last_seen_at, last_message
        FROM ai_error_aggregates
        ORDER BY last_seen_at DESC NULLS LAST
        LIMIT @Limit;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        var rows = await connection.QueryAsync<DashboardErrorDto>(sql, new { Limit = Math.Clamp(limit, 1, 500) });
        return rows.ToList();
    }

    public async Task<DashboardErrorDto?> GetLastErrorForAccountAsync(string accountCode)
    {
        const string sql = """
        SELECT client_code, model_code, partner_code, account_code,
               error_type, error_code, http_status, count,
               first_seen_at, last_seen_at, last_message
        FROM ai_error_aggregates
        WHERE account_code = @AccountCode
        ORDER BY last_seen_at DESC NULLS LAST
        LIMIT 1;
        """;

        await using var connection = await _dataSource.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<DashboardErrorDto>(sql, new { AccountCode = accountCode });
    }

    public async Task CleanupErrorEventsAsync(int retentionDays, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM ai_error_events WHERE created_at < NOW() - (@Days * INTERVAL '1 day');";
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { Days = retentionDays });
    }

    private static string Trim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
