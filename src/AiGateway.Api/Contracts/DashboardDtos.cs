namespace AiGateway.Api.Contracts;

public sealed record DashboardOverviewDto
{
    public long Total { get; init; }
    public long Success { get; init; }
    public long Failed { get; init; }
    public long FallbackSuccess { get; init; }
    public decimal ErrorRate { get; init; }
    public decimal AvgLatencyMs { get; init; }
    public long TokensIn { get; init; }
    public long TokensOut { get; init; }
    public long TokensTotal { get; init; }
}

public sealed record DashboardGroupMetricDto
{
    public required string Code { get; init; }
    public long Total { get; init; }
    public long Success { get; init; }
    public long Failed { get; init; }
    public decimal ErrorRate { get; init; }
    public decimal AvgLatencyMs { get; init; }
}

public sealed record DashboardErrorDto
{
    public required string ModelCode { get; init; }
    public required string PartnerCode { get; init; }
    public required string AccountKeyCode { get; init; }
    public required string ErrorType { get; init; }
    public string? ErrorCode { get; init; }
    public int? HttpStatus { get; init; }
    public long Count { get; init; }
    public DateTimeOffset? FirstSeenAt { get; init; }
    public DateTimeOffset? LastSeenAt { get; init; }
    public string? LastMessage { get; init; }
}
